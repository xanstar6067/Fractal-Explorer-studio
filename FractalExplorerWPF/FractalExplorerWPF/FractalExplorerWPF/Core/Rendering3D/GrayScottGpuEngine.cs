using System.Runtime.InteropServices;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Single-owner DirectCompute reaction–diffusion engine with a committed ping-pong field.</summary>
public sealed class GrayScottGpuEngine : IGrayScottEngine
{
    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private ID3D11Buffer _constants = null!;
    private ID3D11Query _completion = null!;
    private readonly Dictionary<string, ID3D11ComputeShader> _shaders = [];
    private readonly List<GpuBuffer> _buffers = [];
    private GpuBuffer _field = null!, _next = null!, _palette = null!;
    private GpuBuffer? _pixels;
    private readonly float[] _parameters = new float[32];
    private bool _disposed;
    private long _step;
    public int Size { get; }
    public GrayScottBackend Backend => GrayScottBackend.Gpu;
    public string DeviceName { get; private set; } = "Direct3D 11";

    public GrayScottGpuEngine(GrayScottState state)
    {
        state.Validate(); Size = state.GridSize;
        try
        {
            CreateDevice();
            _completion = _device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
            foreach (ShaderCacheEntry entry in GrayScottComputeShader.CacheEntries)
                _shaders[entry.EntryPoint] = _device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(entry,
                    e => Compiler.Compile(e.Source, e.EntryPoint, "GrayScott", e.Profile)).Span);
            _constants = _device.CreateBuffer(new BufferDescription { ByteWidth = 128, Usage = ResourceUsage.Dynamic, BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.Write });
            _field = Allocate(Size * Size, 8); _next = Allocate(Size * Size, 8); _palette = Allocate(1024, 4);
            GrayScottSnapshot initial = new GrayScottSimulation(state).CurrentView(); _step = initial.StepCount;
            float[] values = new float[Size * Size * 2];
            for (int i = 0; i < initial.U.Length; i++) { values[i * 2] = initial.U[i]; values[i * 2 + 1] = initial.V[i]; }
            _context.UpdateSubresource(values, _field.Buffer);
            _parameters[0] = Size; _parameters[1] = (float)state.DiffusionU; _parameters[2] = (float)state.DiffusionV;
            _parameters[3] = (float)state.Feed; _parameters[4] = (float)state.Kill; _parameters[5] = (float)state.DeltaTime;
        }
        catch { Dispose(); throw; }
    }

    private void CreateDevice()
    {
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

    private GpuBuffer Allocate(int count, int stride)
    {
        var buffer = new GpuBuffer(_device, count, stride); _buffers.Add(buffer); return buffer;
    }

    private void Dispatch(string shader, GpuBuffer? input, GpuBuffer? output, int width, int height, GpuBuffer? pixels = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var mapped = _context.Map(_constants, MapMode.WriteDiscard);
        try { Marshal.Copy(_parameters, 0, mapped.DataPointer, 32); } finally { _context.Unmap(_constants, 0); }
        _context.CSSetConstantBuffer(0, _constants); _context.CSSetShader(_shaders[shader]);
        _context.CSSetShaderResource(0, input?.View);
        _context.CSSetShaderResource(1, shader == "Render" ? _palette.View : null);
        _context.CSSetUnorderedAccessView(0, output?.WriteView); _context.CSSetUnorderedAccessView(1, pixels?.WriteView);
        _context.Dispatch((uint)((width + 15) / 16), (uint)((height + 15) / 16), 1);
        _context.CSSetShaderResource(0, null); _context.CSSetShaderResource(1, null);
        _context.CSSetUnorderedAccessView(0, null); _context.CSSetUnorderedAccessView(1, null);
    }

    public void Advance(int steps, CancellationToken token)
    {
        if (steps is < 0 or > 2000) throw new ArgumentOutOfRangeException(nameof(steps));
        for (int step = 0; step < steps; step++)
        {
            token.ThrowIfCancellationRequested(); Dispatch("Evolve", _field, _next, Size, Size);
            // Once dispatched, this complete step is ordered before every subsequent read or pass.
            (_field, _next) = (_next, _field); _step++;
        }
        if (steps > 0) Synchronize(token);
    }

    private void Synchronize(CancellationToken token)
    {
        _context.End(_completion); _context.Flush();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var result = _context.GetData(_completion, IntPtr.Zero, 0, AsyncGetDataFlags.DoNotFlush); result.CheckError();
            if (result.Code == 0) return;
            if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("Видеокарта не завершила расчёт. Уменьшите сетку или выберите ЦП.");
            Thread.Sleep(1);
        }
    }

    public void Inject(double x, double y, int radius)
    {
        _parameters[6] = Math.Clamp(radius, 1, Size / 3);
        _parameters[8] = Math.Clamp((int)Math.Round(x * (Size - 1)), 0, Size - 1);
        _parameters[9] = Math.Clamp((int)Math.Round(y * (Size - 1)), 0, Size - 1);
        Dispatch("Inject", null, _field, Size, Size);
    }

    public GrayScottSnapshot Snapshot()
    {
        float[] values = _field.ReadFloats(_context); var u = new float[Size * Size]; var v = new float[u.Length];
        for (int i = 0; i < u.Length; i++) { u[i] = values[i * 2]; v[i] = values[i * 2 + 1]; }
        return new(Size, u, v, _step);
    }

    public byte[] RenderFrame(GrayScottState state, int width, int height, CancellationToken token, double displayAspect = 0)
    {
        if (width < 1 || height < 1 || (long)width * height > 100_000_000) throw new ArgumentOutOfRangeException(nameof(width));
        token.ThrowIfCancellationRequested(); int count = checked(width * height);
        if (_pixels is null || _pixels.Count != count)
        {
            if (_pixels is not null) { _buffers.Remove(_pixels); _pixels.Dispose(); }
            _pixels = Allocate(count, 4);
        }
        _context.UpdateSubresource(GrayScottRenderer.BuildPalette(state.Palette), _palette.Buffer);
        _parameters[12] = width; _parameters[13] = height; _parameters[14] = (int)state.FieldMode; _parameters[15] = state.ReversePalette ? 1 : 0;
        _parameters[16] = (float)state.RangeMinimum; _parameters[17] = (float)state.RangeMaximum; _parameters[18] = (float)displayAspect;
        Dispatch("Render", _field, null, width, height, _pixels);
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
