using System.Runtime.InteropServices;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Single-owner DirectCompute engine. Scratch work never overwrites the committed field.</summary>
public sealed class TuringGpuEngine : ITuringEngine
{
    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private ID3D11Buffer _constants = null!;
    private ID3D11Query _completion = null!;
    private readonly Dictionary<string, ID3D11ComputeShader> _shaders = [];
    private readonly List<GpuBuffer> _buffers = [];
    private GpuBuffer _field = null!, _next = null!, _work = null!, _temporary = null!, _activator = null!, _inhibitor = null!, _blur = null!, _best = null!, _stats0 = null!, _stats1 = null!, _palette = null!;
    private GpuBuffer? _pixels;
    private readonly float[] _parameters = new float[48];
    private readonly string _blurShader;
    private bool _disposed;
    private long _step;
    private readonly TuringReactionModel _reactionModel;
    public int Size { get; }
    public TuringBackend Backend => TuringBackend.Gpu;
    public string DeviceName { get; private set; } = "Direct3D 11";

    public TuringGpuEngine(TuringState state, bool softwareForVerification = false)
    {
        state.Validate(); Size = state.GridSize; _reactionModel = state.Reaction.Model;
        _blurShader = Size <= 256 ? "Blur256" : Size <= 512 ? "Blur512" : Size <= 1024 ? "Blur1024" : "Blur";
        try
        {
            CreateDevice(softwareForVerification);
            _completion = _device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
            foreach (ShaderCacheEntry entry in TuringComputeShader.CacheEntries)
                _shaders[entry.EntryPoint] = _device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(entry,
                    e => Compiler.Compile(e.Source, e.EntryPoint, "Turing", e.Profile)).Span);
            _constants = _device.CreateBuffer(new BufferDescription { ByteWidth = 192, Usage = ResourceUsage.Dynamic, BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.Write });
            int count = Size * Size;
            _field = Allocate(count); _next = Allocate(count); _work = Allocate(count); _temporary = Allocate(count);
            _activator = Allocate(count); _inhibitor = Allocate(count); _blur = Allocate(count); _best = Allocate(count);
            _stats0 = Allocate((count + 255) / 256); _stats1 = Allocate((count + 255) / 256); _palette = Allocate(1024);
            var initial = new TuringSimulation(state).Snapshot(); _step = initial.StepCount;
            float[] values = new float[count * 4];
            for (int i = 0; i < count; i++) { values[i * 4] = initial.Field[i]; values[i * 4 + 1] = initial.Scales[i]; if (state.Reaction.IsClassical) { values[i * 4 + 2] = initial.U[i]; values[i * 4 + 3] = initial.V[i]; } }
            _context.UpdateSubresource(values, _field.Buffer);
        }
        catch { Dispose(); throw; }
    }

    private void CreateDevice(bool software)
    {
        if (software)
        {
            D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.None, [FeatureLevel.Level_11_0], out _device, out _context).CheckError();
            DeviceName = "WARP (проверка)"; return;
        }
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var adapters = new List<IDXGIAdapter1>();
        try
        {
            for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++) if (adapter is not null) adapters.Add(adapter);
            foreach (var adapter in adapters.OrderByDescending(a => a.Description1.DedicatedVideoMemory))
            {
                if ((adapter.Description1.Flags & AdapterFlags.Software) != 0) continue;
                if (!D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, [FeatureLevel.Level_11_0], out ID3D11Device? device, out ID3D11DeviceContext? context).Success) continue;
                _device = device!; _context = context!; DeviceName = adapter.Description1.Description.Trim(); return;
            }
        }
        finally { foreach (var adapter in adapters) adapter.Dispose(); }
        throw new InvalidOperationException("Нужна видеокарта с Direct3D 11 и актуальным драйвером.");
    }

    private GpuBuffer Allocate(int count, int stride = 16)
    {
        var buffer = new GpuBuffer(_device, count, stride); _buffers.Add(buffer); return buffer;
    }
    private void Parameters(TuringState state)
    {
        Array.Clear(_parameters); _parameters[0] = Size; _parameters[2] = (int)state.Boundary;
        _parameters[6] = state.Symmetry; _parameters[7] = state.Mirror ? 1 : 0;
        var r = state.Reaction; var equilibrium = r.Equilibrium;
        _parameters[32] = (int)r.Model; _parameters[33] = (float)r.A; _parameters[34] = (float)r.B;
        _parameters[38] = (float)equilibrium.U; _parameters[39] = (float)equilibrium.V;
    }
    private void Dispatch(string shader, GpuBuffer? a, GpuBuffer? b, GpuBuffer? c, GpuBuffer? output, int x, int y = 1, GpuBuffer? pixels = null)
    {
        var mapped = _context.Map(_constants, MapMode.WriteDiscard);
        try { Marshal.Copy(_parameters, 0, mapped.DataPointer, _parameters.Length); } finally { _context.Unmap(_constants, 0); }
        _context.CSSetConstantBuffer(0, _constants); _context.CSSetShader(_shaders[shader]);
        _context.CSSetShaderResource(0, a?.View); _context.CSSetShaderResource(1, b?.View); _context.CSSetShaderResource(2, c?.View);
        _context.CSSetShaderResource(3, shader == "Render" ? _palette.View : null);
        _context.CSSetUnorderedAccessView(0, output?.WriteView); _context.CSSetUnorderedAccessView(1, pixels?.WriteView);
        _context.Dispatch((uint)x, (uint)y, 1);
        // Explicit unbinding avoids SRV/UAV hazards on the next pass, including in-place operations.
        for (uint i = 0; i < 4; i++) _context.CSSetShaderResource(i, null);
        _context.CSSetUnorderedAccessView(0, null); _context.CSSetUnorderedAccessView(1, null);
    }

    private void Smooth(GpuBuffer source, GpuBuffer target, int radius, CancellationToken token)
    {
        foreach (int width in TuringSimulation.GaussianBoxWidths(radius * .75))
        {
            token.ThrowIfCancellationRequested(); _parameters[1] = width / 2; _parameters[5] = 0;
            Dispatch(_blurShader, source, null, null, _temporary, Size);
            _parameters[5] = 1; Dispatch(_blurShader, _temporary, null, null, target, Size);
            source = target;
        }
    }

    public void Advance(int steps, TuringState state, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); state.Validate();
        if (steps is < 0 or > 2000 || state.GridSize != Size) throw new ArgumentOutOfRangeException(nameof(steps));
        if (state.Reaction.Model != _reactionModel) throw new ArgumentException("Смена модели требует нового поля.");
        if (state.Reaction.IsClassical) { AdvanceReaction(steps, state, token); return; }
        int count = Size * Size, groups = (count + 255) / 256;
        for (int step = 0; step < steps; step++)
        {
            token.ThrowIfCancellationRequested(); Parameters(state); bool first = true;
            for (int i = 0; i < state.Layers.Count; i++)
            {
                var scale = state.Layers[i]; if (!scale.Enabled) continue;
                int radius = Math.Clamp((int)Math.Round(scale.Radius * state.DetailSize * Size / 256), 1, Size - 1);
                int inhibitor = Math.Clamp((int)Math.Round(radius * state.InhibitorRatio), radius + 1, Size);
                Smooth(_field, _activator, radius, token); Smooth(_field, _inhibitor, inhibitor, token);
                Dispatch("Difference", _activator, _inhibitor, null, _work, groups);
                Smooth(_work, _blur, radius, token);
                _parameters[4] = first ? 1 : 0; _parameters[6] = i; _parameters[8] = (float)(scale.Amount * state.EvolutionRate);
                Dispatch("Choose", _blur, _activator, _inhibitor, _best, groups); first = false;
            }
            Dispatch("Compose", _field, _best, null, _work, groups);
            if (state.Symmetry == 1 && !state.Mirror)
            {
                // Identity symmetry needs neither resampling nor a GPU copy.
                // Both buffers are scratch; the committed field stays untouched.
                (_work, _next) = (_next, _work);
            }
            else
            {
                _parameters[6] = state.Symmetry; _parameters[2] = (int)TuringBoundary.Reflect;
                Dispatch("Symmetry", _work, null, null, _next, (Size + 15) / 16, (Size + 15) / 16);
            }
            _parameters[3] = count; _parameters[4] = 0;
            Dispatch("Reduce", _next, null, null, _stats0, groups);
            int remaining = groups; GpuBuffer range = _stats0, other = _stats1;
            while (remaining > 1)
            {
                _parameters[3] = remaining; _parameters[4] = 1;
                remaining = (remaining + 255) / 256;
                Dispatch("Reduce", range, null, null, other, remaining); (range, other) = (other, range);
            }
            Dispatch("Normalize", range, null, null, _next, groups);
            token.ThrowIfCancellationRequested(); (_field, _next) = (_next, _field); _step++;
        }
        if (steps > 0) Synchronize(token);
    }

    private void AdvanceReaction(int steps, TuringState state, CancellationToken token)
    {
        var integration = state.Reaction.Integration(Size, 256, state.DetailSize, state.EvolutionRate, 2);
        for (int step = 0; step < steps; step++)
        {
            Parameters(state); _parameters[35] = (float)integration.Dt;
            _parameters[36] = (float)(state.Reaction.DiffusionU * integration.DiffusionScale);
            _parameters[37] = (float)(state.Reaction.DiffusionV * integration.DiffusionScale);
            GpuBuffer source = _field, target = _work;
            for (int sub = 0; sub < integration.Count; sub++)
            {
                token.ThrowIfCancellationRequested();
                Dispatch("ReactionStep", source, null, null, target, (Size + 15) / 16, (Size + 15) / 16);
                source = target; target = ReferenceEquals(target, _work) ? _next : _work;
            }
            if (state.Symmetry != 1 || state.Mirror)
            {
                _parameters[2] = (int)TuringBoundary.Reflect;
                Dispatch("ReactionSymmetry", source, null, null, target, (Size + 15) / 16, (Size + 15) / 16);
                source = target;
            }
            token.ThrowIfCancellationRequested();
            if (ReferenceEquals(source, _work)) (_field, _work) = (_work, _field);
            else (_field, _next) = (_next, _field);
            _step++;
        }
        if (steps > 0) Synchronize(token);
    }

    private void Synchronize(CancellationToken token)
    {
        // Keep preparation progress and stopping tied to completed work, rather than queued dispatches.
        _context.End(_completion); _context.Flush();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            // Offscreen compute has no Present to keep the driver queue moving.
            // Let polling submit pending work, including short preparation batches.
            var result = _context.GetData(_completion, IntPtr.Zero, 0, AsyncGetDataFlags.None);
            result.CheckError();
            if (result.Code == 0) return;
            if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("Видеокарта не завершила расчёт. Попробуйте уменьшить сетку или выбрать ЦП.");
            Thread.Sleep(1);
        }
    }

    public void Paint(double x, double y, double radius, double strength, TuringBrush brush, TuringState state)
    {
        Parameters(state); _parameters[9] = (float)strength; _parameters[10] = (float)radius; _parameters[11] = (int)brush;
        _parameters[12] = (float)x; _parameters[13] = (float)y; _parameters[14] = BitConverter.Int32BitsToSingle(state.RandomSeed); _parameters[15] = BitConverter.Int32BitsToSingle(unchecked((int)_step));
        Dispatch("Paint", null, null, null, _field, (Size + 15) / 16, (Size + 15) / 16);
    }

    public TuringCheckpoint Snapshot()
    {
        float[] values = _field.ReadFloats(_context);
        var cp = new TuringCheckpoint { Size = Size, StepCount = _step, Field = new float[Size * Size], Scales = new byte[Size * Size], Model = _reactionModel, U = _reactionModel == TuringReactionModel.McCabe ? [] : new float[Size * Size], V = _reactionModel == TuringReactionModel.McCabe ? [] : new float[Size * Size] };
        for (int i = 0; i < cp.Field.Length; i++) { cp.Field[i] = values[i * 4]; cp.Scales[i] = (byte)values[i * 4 + 1];
            if (cp.U.Length > 0) { cp.U[i] = values[i * 4 + 2]; cp.V[i] = values[i * 4 + 3];
                if (!float.IsFinite(cp.U[i] + cp.V[i]) || cp.U[i] > 1000 || cp.V[i] > 1000)
                    throw new InvalidOperationException("Реакция расходится. Измените A/B или диффузию и начните заново."); } }
        return cp;
    }

    public byte[] RenderFrame(TuringState state, int width, int height, CancellationToken token, double displayAspect = 0)
    {
        if (width < 1 || height < 1 || (long)width * height > 100_000_000) throw new ArgumentOutOfRangeException(nameof(width));
        token.ThrowIfCancellationRequested();
        int count = checked(width * height);
        if (_pixels is null || _pixels.Count != count)
        {
            if (_pixels is not null) { _buffers.Remove(_pixels); _pixels.Dispose(); }
            _pixels = Allocate(count, 4);
        }
        float[] palette = new float[4096];
        for (int i = 0; i < 1024; i++)
        {
            var color = TuringRenderer.PaletteColor(state.Palette.Colors, i / 1023d);
            palette[i * 4] = color.R; palette[i * 4 + 1] = color.G; palette[i * 4 + 2] = color.B;
        }
        _context.UpdateSubresource(palette, _palette.Buffer);
        Parameters(state); _parameters[16] = width; _parameters[17] = height; _parameters[18] = (int)state.Coloring; _parameters[19] = state.ReversePalette ? 1 : 0;
        _parameters[20] = (float)state.Contrast; _parameters[21] = (float)state.Relief; _parameters[22] = (float)state.Zoom;
        _parameters[24] = (float)state.PanX; _parameters[25] = (float)state.PanY;
        _parameters[26] = (float)displayAspect;
        Dispatch("Render", _field, null, null, null, (width + 15) / 16, (height + 15) / 16, _pixels);
        byte[] pixels = _pixels.ReadBytes(_context); token.ThrowIfCancellationRequested(); return pixels;
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _context?.ClearState(); foreach (var buffer in _buffers) buffer.Dispose(); _buffers.Clear();
        foreach (var shader in _shaders.Values) shader.Dispose(); _shaders.Clear();
        _completion?.Dispose(); _constants?.Dispose(); _context?.Dispose(); _device?.Dispose();
    }

    private sealed class GpuBuffer : IDisposable
    {
        private readonly ID3D11Device _device;
        private ID3D11Buffer? _staging;
        public ID3D11Buffer Buffer { get; }
        public ID3D11ShaderResourceView View { get; }
        public ID3D11UnorderedAccessView WriteView { get; }
        public int Count { get; }
        private readonly int _stride;
        public GpuBuffer(ID3D11Device device, int count, int stride)
        {
            _device = device; Count = count; _stride = stride;
            Buffer = device.CreateBuffer(new BufferDescription { ByteWidth = checked((uint)(count * stride)), Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess, MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = (uint)stride });
            try { View = device.CreateShaderResourceView(Buffer); WriteView = device.CreateUnorderedAccessView(Buffer); }
            catch { View?.Dispose(); Buffer.Dispose(); throw; }
        }
        private MappedSubresource Read(ID3D11DeviceContext context)
        {
            _staging ??= _device.CreateBuffer(new BufferDescription { ByteWidth = checked((uint)(Count * _stride)), Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
            context.CopyResource(_staging, Buffer); return context.Map(_staging, MapMode.Read);
        }
        public float[] ReadFloats(ID3D11DeviceContext context)
        {
            var mapped = Read(context); var result = new float[Count * _stride / 4];
            try { Marshal.Copy(mapped.DataPointer, result, 0, result.Length); } finally { context.Unmap(_staging!, 0); } return result;
        }
        public byte[] ReadBytes(ID3D11DeviceContext context)
        {
            var mapped = Read(context); var result = new byte[Count * _stride];
            try { Marshal.Copy(mapped.DataPointer, result, 0, result.Length); } finally { context.Unmap(_staging!, 0); } return result;
        }
        public void Dispose() { _staging?.Dispose(); WriteView.Dispose(); View.Dispose(); Buffer.Dispose(); }
    }
}
