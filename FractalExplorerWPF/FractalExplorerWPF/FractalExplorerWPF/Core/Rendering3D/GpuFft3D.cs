using System.Runtime.InteropServices;
using FractalExplorerWPF.Infrastructure;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Two complex channels in float4, radix-2 FFT along X/Y/Z. A whole line is transformed
/// in group memory, with a barrier after every butterfly stage. Forward sign −, inverse +,
/// inverse normalization 1/N per axis. Caller holds the device gate and owns the two buffers.
/// </summary>
internal sealed class GpuFft3D : IDisposable
{
    private readonly Direct3DDeviceHost _host;
    private readonly ID3D11ComputeShader _shader;
    private readonly ID3D11ComputeShader _tiledShader;
    private readonly bool? _useTiled;
    private readonly ID3D11Buffer _constants;
    private readonly int[] _parameters = new int[4];
    public static ShaderCacheEntry CacheEntry { get; } = new("fft3d-lines", Source, "Transform", "cs_5_0");
    public static ShaderCacheEntry TiledCacheEntry { get; } = new("fft3d-tiled", Source, "TransformTiled", "cs_5_0");

    public GpuFft3D(Direct3DDeviceHost host, bool? useTiled = null)
    {
        _host = host; _useTiled = useTiled;
        _shader = host.Device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(CacheEntry,
            e => Compiler.Compile(e.Source, e.EntryPoint, "Fft3D", e.Profile)).Span);
        try
        {
            _tiledShader = host.Device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(TiledCacheEntry,
                e => Compiler.Compile(e.Source, e.EntryPoint, "Fft3D", e.Profile)).Span);
            _constants = host.Device.CreateBuffer(new BufferDescription { ByteWidth = 16, Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.Write });
        }
        catch { _tiledShader?.Dispose(); _shader.Dispose(); throw; }
    }

    public void Transform(int size, bool inverse, ref GpuComplexBuffer current, ref GpuComplexBuffer scratch)
    {
        var context = _host.Context;
        _parameters[0] = size; _parameters[1] = System.Numerics.BitOperations.Log2((uint)size);
        _parameters[3] = inverse ? 1 : 0;
        bool tiled = _useTiled ?? size >= 64;
        for (int axis = 0; axis < 3; axis++)
        {
            _parameters[2] = axis;
            var map = context.Map(_constants, MapMode.WriteDiscard);
            try { Marshal.Copy(_parameters, 0, map.DataPointer, 4); }
            finally { context.Unmap(_constants, 0); }
            context.CSSetShader(tiled ? _tiledShader : _shader); context.CSSetConstantBuffer(0, _constants);
            context.CSSetShaderResource(0, current.View); context.CSSetUnorderedAccessView(0, scratch.WriteView);
            context.Dispatch((uint)(tiled ? size/4 : size), (uint)size, 1);
            context.CSSetShaderResource(0, null!); context.CSSetUnorderedAccessView(0, null!);
            (current, scratch) = (scratch, current);
        }
    }

    public void Dispose() { _constants.Dispose(); _tiledShader.Dispose(); _shader.Dispose(); }

    private static string Source => ShaderSource.Load("GpuFft3D.hlsl");
}

internal sealed class GpuComplexBuffer : IDisposable
{
    public ID3D11Buffer Buffer { get; }
    public ID3D11ShaderResourceView View { get; }
    public ID3D11UnorderedAccessView WriteView { get; }
    public GpuComplexBuffer(ID3D11Device device, int size)
    {
        Buffer = device.CreateBuffer(new BufferDescription { ByteWidth = (uint)(size * size * size * 16),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
            MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = 16 });
        try { View = device.CreateShaderResourceView(Buffer); WriteView = device.CreateUnorderedAccessView(Buffer); }
        catch { View?.Dispose(); Buffer.Dispose(); throw; }
    }
    public void Dispose() { WriteView.Dispose(); View.Dispose(); Buffer.Dispose(); }
}
