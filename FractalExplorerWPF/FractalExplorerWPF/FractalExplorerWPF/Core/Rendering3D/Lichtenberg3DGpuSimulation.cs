using System.Runtime.InteropServices;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Lattice dielectric breakdown: solve Δφ = 0, choose a frontier bond with probability φ^η.
/// Red/black SOR runs on the window's GPU. Only the categorical draw and topology use the CPU.
/// The caller holds DeviceHost.Gate. Cancellation restores the last committed potential before retry.
/// </summary>
internal sealed class Lichtenberg3DGpuSimulation : IDisposable
{
    private readonly Direct3DDeviceHost _host;
    private ID3D11ComputeShader _shader = null!;
    private ID3D11Buffer _potential = null!, _maskBuffer = null!, _constants = null!, _staging = null!;
    private ID3D11ShaderResourceView _maskView = null!;
    private ID3D11UnorderedAccessView _potentialWrite = null!;
    private readonly uint[] _mask;
    private readonly List<int> _cells;
    private float[] _values;
    private bool _dirty = true;
    public Lichtenberg3DSettings Settings { get; }
    public int Count => _cells.Count - 1;
    public bool BoundaryReached { get; private set; }

    public Lichtenberg3DGpuSimulation(Direct3DDeviceHost host, Lichtenberg3DSettings settings)
    {
        settings.Validate(); _host = host; Settings = settings with { Field = null };
        int n = settings.Size;
        _mask = InitialMask(settings);
        _values = settings.Field?.Potential.ToArray() ?? InitialPotential(settings, _mask);
        _cells = settings.Field?.GrowthOrder.ToArray().ToList() ?? [SeedCell(settings)];
        if (_cells[0] != SeedCell(settings) || _cells.Any(i => _mask[i] != 0))
            throw new InvalidOperationException("Поле не соответствует электродам.");
        foreach (int cell in _cells) { _mask[cell] = 1; _values[cell] = 0; }
        for (int i = 0; i < _mask.Length; i++)
            if (_mask[i] == 2 && _values[i] != 1 || (_mask[i] is 1 or 3) && _values[i] != 0)
                throw new InvalidOperationException("Нарушен потенциал электрода.");
        BoundaryReached = _cells.Any(TouchesElectrode);
        try
        {
            var device = host.Device;
            var entry = Lichtenberg3DComputeShader.CacheEntry;
            _shader = device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(entry,
                e => Compiler.Compile(e.Source, e.EntryPoint, "Lichtenberg3D", e.Profile)).Span);
            _potential = device.CreateBuffer(new BufferDescription { ByteWidth = (uint)(_mask.Length * 4),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.UnorderedAccess,
                MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = 4 });
            _potentialWrite = device.CreateUnorderedAccessView(_potential);
            _maskBuffer = device.CreateBuffer(new BufferDescription { ByteWidth = (uint)(_mask.Length * 4),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource,
                MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = 4 });
            _maskView = device.CreateShaderResourceView(_maskBuffer);
            _constants = device.CreateBuffer(new BufferDescription { ByteWidth = 16, Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ConstantBuffer });
            _staging = device.CreateBuffer(new BufferDescription { ByteWidth = (uint)(_mask.Length * 4),
                Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
        }
        catch { Dispose(); throw; }
    }

    internal static int SeedCell(Lichtenberg3DSettings settings)
    {
        int n = settings.Size, y = settings.Electrodes == LichtenbergElectrodes.Plane ? n - 5 : n / 2;
        return (n / 2 * n + y) * n + n / 2;
    }

    internal static uint[] InitialMask(Lichtenberg3DSettings settings)
    {
        int n = settings.Size; var mask = new uint[n * n * n];
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            int index = (z * n + y) * n + x;
            if (settings.Electrodes == LichtenbergElectrodes.Radial)
            {
                double r2 = (x - n / 2) * (x - n / 2) + (y - n / 2) * (y - n / 2) + (z - n / 2) * (z - n / 2);
                if (r2 >= (n / 2 - 3) * (n / 2 - 3)) mask[index] = 2;
            }
            else if (y == 0) mask[index] = 2;
            else if (y == n - 1) mask[index] = 3;
        }
        return mask;
    }

    private static float[] InitialPotential(Lichtenberg3DSettings settings, uint[] mask)
    {
        int n = settings.Size; var values = new float[mask.Length];
        for (int i = 0; i < values.Length; i++)
            values[i] = settings.Electrodes == LichtenbergElectrodes.Radial ? 1 : 1f - (i / n % n) / (n - 1f);
        return values;
    }

    public bool HasPrefix(Lichtenberg3DField? field) => field is null ||
        field.Size == Settings.Size && field.Cells.Length <= _cells.Count && field.Cells.SequenceEqual(_cells.Take(field.Cells.Length));

    public Lichtenberg3DField Snapshot() => new(Settings.Size, _cells.ToArray(), _values);

    public void GrowTo(int count, CancellationToken token)
    {
        while (Count < count && !BoundaryReached)
        {
            token.ThrowIfCancellationRequested();
            float[] solved = Solve(token);
            int[] frontier = Frontier();
            if (frontier.Length == 0) { BoundaryReached = true; break; }
            double[] weights = BondWeights(frontier, solved, Settings.Eta, Settings.Size, _mask);
            double total = weights.Sum();
            if (!(total > 0)) throw new InvalidOperationException("На границе канала нет электрического поля.");
            // One stream per complete segment: frame batching and cancellation cannot change the draw.
            var random = new Random(unchecked(Settings.Seed * 73856093 ^ (Count + 1) * 19349663));
            double draw = random.NextDouble() * total;
            int selected = frontier.Length - 1;
            for (int i = 0; i < weights.Length; i++) { draw -= weights[i]; if (draw < 0) { selected = i; break; } }
            token.ThrowIfCancellationRequested();
            int cell = frontier[selected];
            solved[cell] = 0;
            _mask[cell] = 1; _cells.Add(cell); _values = solved; _dirty = true;
            BoundaryReached = TouchesElectrode(cell);
        }
    }

    private int[] Frontier()
    {
        int n = Settings.Size; var result = new HashSet<int>();
        foreach (int cell in _cells)
            foreach (int i in Lichtenberg3DField.Neighbors(cell, n))
            {
                int x = i % n, y = i / n % n, z = i / (n * n);
                if (x > 0 && y > 0 && z > 0 && x < n - 1 && y < n - 1 && z < n - 1 && _mask[i] == 0) result.Add(i);
            }
        return result.Order().ToArray();
    }

    internal static double[] BondWeights(int[] frontier, float[] potential, double eta, int n, uint[] mask)
    {
        // Every conducting neighbor is a separate possible bond in the original DBM.
        double maximum = frontier.Max(i => (double)potential[i]);
        return frontier.Select(i => Lichtenberg3DField.Neighbors(i, n).Count(j => mask[j] == 1) *
            (eta == 0 ? 1 : Math.Pow(potential[i] / Math.Max(maximum, 1e-30), eta))).ToArray();
    }

    private bool TouchesElectrode(int cell) => Lichtenberg3DField.Neighbors(cell, Settings.Size).Any(i => _mask[i] == 2);

    /// <summary>Diagnostic entry used by the independent Laplace verification.</summary>
    internal float[] Solve(CancellationToken token)
    {
        var context = _host.Context;
        if (_dirty)
        {
            context.UpdateSubresource(_values, _potential); context.UpdateSubresource(_mask, _maskBuffer);
            _dirty = false;
        }
        try
        {
            int n = Settings.Size;
            context.CSSetShader(_shader); context.CSSetConstantBuffer(0, _constants);
            context.CSSetShaderResource(0, _maskView); context.CSSetUnorderedAccessView(0, _potentialWrite);
            for (int iteration = 0; iteration < 1024; iteration += 32)
            {
                token.ThrowIfCancellationRequested();
                for (int sweep = 0; sweep < 32; sweep++) for (uint color = 0; color < 2; color++)
                {
                    uint[] parameters = [(uint)n, color, BitConverter.SingleToUInt32Bits(1.65f), 0];
                    context.UpdateSubresource(parameters, _constants);
                    context.Dispatch((uint)((n + 3) / 4), (uint)((n + 3) / 4), (uint)((n + 3) / 4));
                }
                context.CSSetUnorderedAccessView(0, null!);
                context.CopyResource(_staging, _potential);
                var mapped = context.Map(_staging, MapMode.Read);
                float[] values = new float[_mask.Length];
                try { Marshal.Copy(mapped.DataPointer, values, 0, values.Length); }
                finally { context.Unmap(_staging, 0); }
                token.ThrowIfCancellationRequested();
                if (Residual(values, _mask, n) <= 1e-4) return values;
                context.CSSetUnorderedAccessView(0, _potentialWrite);
            }
            throw new InvalidOperationException("Поле не сошлось. Начните заново на меньшей сетке.");
        }
        finally
        {
            context.CSSetShaderResource(0, null!); context.CSSetUnorderedAccessView(0, null!);
            // The CPU checkpoint remains authoritative until a whole bond is committed.
            _dirty = true;
        }
    }

    internal static double Residual(float[] v, uint[] mask, int n)
    {
        double maximum = 0;
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            int i = (z * n + y) * n + x;
            if (mask[i] != 0) continue;
            double sum = v[x == 0 ? i : i - 1] + (double)v[x == n - 1 ? i : i + 1] +
                v[y == 0 ? i : i - n] + (double)v[y == n - 1 ? i : i + n] +
                v[z == 0 ? i : i - n * n] + (double)v[z == n - 1 ? i : i + n * n];
            maximum = Math.Max(maximum, Math.Abs(v[i] - sum / 6));
        }
        return maximum;
    }

    public void Dispose()
    {
        _maskView?.Dispose(); _potentialWrite?.Dispose(); _maskBuffer?.Dispose(); _potential?.Dispose();
        _constants?.Dispose(); _staging?.Dispose(); _shader?.Dispose();
    }
}
