using System.Runtime.InteropServices;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Single-owner 3D DirectCompute engine; complete dispatched steps commit atomically.</summary>
public sealed class GrayScott3DGpuEngine : IGrayScott3DEngine
{
    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private ID3D11Buffer _constants = null!, _staging = null!;
    private ID3D11Query _completion = null!;
    private readonly Dictionary<string, ID3D11ComputeShader> _shaders = [];
    private FieldBuffer _current = null!, _next = null!;
    private readonly float[] _parameters = new float[12];
    private readonly int _size;
    private long _step;
    private bool _disposed;
    public string DeviceName { get; private set; } = "ГП · Direct3D 11";

    public GrayScott3DGpuEngine(GrayScott3DSettings settings)
    {
        settings.Validate(); _size = settings.Size;
        try
        {
            CreateDevice();
            foreach (var entry in GrayScott3DComputeShader.CacheEntries)
                _shaders[entry.EntryPoint] = _device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(entry,
                    e => Compiler.Compile(e.Source, e.EntryPoint, "GrayScott3D", e.Profile)).Span);
            _constants = _device.CreateBuffer(new BufferDescription { ByteWidth = 48, Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.Write });
            _current = new(_device, _size); _next = new(_device, _size);
            _staging = _device.CreateBuffer(new BufferDescription { ByteWidth = (uint)(_size * _size * _size * 8),
                Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
            _completion = _device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
            _context.UpdateSubresource(settings.Field?.Values ?? GrayScott3DSimulation.InitialField(settings), _current.Buffer);
            _step = settings.Field?.Step ?? 0;
            _parameters[0] = (float)settings.DiffusionU; _parameters[1] = (float)settings.DiffusionV;
            _parameters[2] = (float)settings.Feed; _parameters[3] = (float)settings.Kill; _parameters[4] = _size;
        }
        catch { Dispose(); throw; }
    }

    private void CreateDevice()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
        {
            if (adapter is null) continue;
            using (adapter)
            {
                if ((adapter.Description1.Flags & AdapterFlags.Software) != 0) continue;
                if (!D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None,
                    [FeatureLevel.Level_11_0], out ID3D11Device? device, out ID3D11DeviceContext? context).Success) continue;
                _device = device!; _context = context!; DeviceName = adapter.Description1.Description.Trim(); return;
            }
        }
        throw new InvalidOperationException("Вычисления на ГП недоступны; используется ЦП.");
    }

    private void Dispatch(string entry, bool injection = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var mapped = _context.Map(_constants, MapMode.WriteDiscard);
        try { Marshal.Copy(_parameters, 0, mapped.DataPointer, 12); } finally { _context.Unmap(_constants, 0); }
        _context.CSSetShader(_shaders[entry]); _context.CSSetConstantBuffer(0, _constants);
        _context.CSSetShaderResource(0, injection ? null : _current.View);
        _context.CSSetUnorderedAccessView(0, injection ? _current.WriteView : _next.WriteView);
        uint groups = (uint)((_size + 3) / 4); _context.Dispatch(groups, groups, groups);
        _context.CSSetShaderResource(0, null); _context.CSSetUnorderedAccessView(0, null);
    }

    public void Advance(int steps, CancellationToken token)
    {
        if (steps is < 0 or > 2000) throw new ArgumentOutOfRangeException(nameof(steps));
        for (int i = 0; i < steps; i++)
        {
            token.ThrowIfCancellationRequested(); Dispatch("Evolve");
            (_current, _next) = (_next, _current); _step++;
            // Bound the command queue and driver work even on the largest supported field.
            if ((i & 31) == 31) Synchronize(token);
        }
        if (steps > 0) Synchronize(token);
    }

    private void Synchronize(CancellationToken token)
    {
        _context.End(_completion); _context.Flush(); var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var result = _context.GetData(_completion, IntPtr.Zero, 0, AsyncGetDataFlags.DoNotFlush); result.CheckError();
            if (result.Code == 0) return;
            if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("ГП не завершил шаг Gray–Scott 3D.");
            Thread.Sleep(1);
        }
    }

    public void Inject(double x, double y, double z, double radius)
    {
        GrayScott3DSimulation.ValidateBrush(x, y, z, radius);
        _parameters[8] = (float)x; _parameters[9] = (float)y; _parameters[10] = (float)z; _parameters[11] = (float)radius;
        Dispatch("Inject", true);
    }

    public GrayScott3DField Snapshot()
    {
        _context.CopyResource(_staging, _current.Buffer); var mapped = _context.Map(_staging, MapMode.Read);
        float[] values = new float[_size * _size * _size * 2];
        try { Marshal.Copy(mapped.DataPointer, values, 0, values.Length); } finally { _context.Unmap(_staging, 0); }
        return new(_size, _step, values, true);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _context?.ClearState(); _current?.Dispose(); _next?.Dispose(); _staging?.Dispose();
        foreach (var shader in _shaders.Values) shader.Dispose();
        _completion?.Dispose(); _constants?.Dispose(); _context?.Dispose(); _device?.Dispose();
    }

    private sealed class FieldBuffer : IDisposable
    {
        public ID3D11Buffer Buffer { get; }
        public ID3D11ShaderResourceView View { get; } = null!;
        public ID3D11UnorderedAccessView WriteView { get; } = null!;
        public FieldBuffer(ID3D11Device device, int side)
        {
            Buffer = device.CreateBuffer(new BufferDescription { ByteWidth = (uint)(side * side * side * 8),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = 8 });
            try { View = device.CreateShaderResourceView(Buffer); WriteView = device.CreateUnorderedAccessView(Buffer); }
            catch { View?.Dispose(); Buffer.Dispose(); throw; }
        }
        public void Dispose() { WriteView.Dispose(); View.Dispose(); Buffer.Dispose(); }
    }
}
