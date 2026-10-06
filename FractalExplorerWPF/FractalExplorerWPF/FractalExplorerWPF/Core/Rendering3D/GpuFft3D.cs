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

    private const string Source = """
        cbuffer Fft : register(b0) { uint N; uint LogN; uint Axis; uint Inverse; };
        StructuredBuffer<float4> Input : register(t0);
        RWStructuredBuffer<float4> Output : register(u0);
        groupshared float4 Line[128];
        uint Address(uint lane, uint2 coords)
        {
            uint3 p = Axis == 0 ? uint3(lane,coords.x,coords.y) :
                Axis == 1 ? uint3(coords.x,lane,coords.y) : uint3(coords.x,coords.y,lane);
            return (p.z * N + p.y) * N + p.x;
        }
        float2 Multiply(float2 a, float2 b) { return float2(a.x*b.x-a.y*b.y, a.x*b.y+a.y*b.x); }
        [numthreads(128,1,1)]
        void Transform(uint lane : SV_GroupIndex, uint3 group : SV_GroupID)
        {
            if (lane < N) Line[reversebits(lane) >> (32 - LogN)] = Input[Address(lane,group.xy)];
            GroupMemoryBarrierWithGroupSync();
            for (uint span = 2; span <= N; span <<= 1)
            {
                if (lane < N/2)
                {
                    uint halfSpan = span/2, j = lane % halfSpan;
                    uint a = (lane/halfSpan)*span + j, b = a + halfSpan;
                    float angle = (Inverse != 0 ? 1.0 : -1.0) * 6.283185307179586 * j/span;
                    float sn, cs; sincos(angle,sn,cs); float2 w = float2(cs,sn);
                    float4 u = Line[a], v = Line[b];
                    float4 t = float4(Multiply(v.xy,w),Multiply(v.zw,w));
                    Line[a] = u+t; Line[b] = u-t;
                }
                GroupMemoryBarrierWithGroupSync();
            }
            if (lane < N) Output[Address(lane,group.xy)] = Line[lane] * (Inverse != 0 ? 1.0/N : 1.0);
        }
        // Four contiguous rows per group. Each pass cycles XYZ -> YZX, so the next
        // pass also reads contiguous X rows. Three passes restore the original layout.
        // Reassign lanes on write: each consecutive four threads write adjacent cells.
        groupshared float4 Tile[512];
        [numthreads(128,4,1)]
        void TransformTiled(uint3 thread : SV_GroupThreadID, uint index : SV_GroupIndex, uint3 group : SV_GroupID)
        {
            uint lane = thread.x, row = thread.y, baseRow = row*N;
            if (lane < N) Tile[baseRow+(reversebits(lane) >> (32-LogN))] = Input[(group.y*N+group.x*4+row)*N+lane];
            GroupMemoryBarrierWithGroupSync();
            for (uint span = 2; span <= N; span <<= 1)
            {
                if (lane < N/2)
                {
                    uint halfSpan = span/2, j = lane % halfSpan;
                    uint a = baseRow+(lane/halfSpan)*span+j, b = a+halfSpan;
                    float angle = (Inverse != 0 ? 1.0 : -1.0)*6.283185307179586*j/span;
                    float sn,cs; sincos(angle,sn,cs); float2 w = float2(cs,sn);
                    float4 u = Tile[a], v = Tile[b], t = float4(Multiply(v.xy,w),Multiply(v.zw,w));
                    Tile[a] = u+t; Tile[b] = u-t;
                }
                GroupMemoryBarrierWithGroupSync();
            }
            if (index < N*4)
            {
                uint outLane = index/4, outRow = index%4;
                Output[(outLane*N+group.y)*N+group.x*4+outRow] = Tile[outRow*N+outLane]*(Inverse != 0 ? 1.0/N : 1.0);
            }
        }
        """;
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
