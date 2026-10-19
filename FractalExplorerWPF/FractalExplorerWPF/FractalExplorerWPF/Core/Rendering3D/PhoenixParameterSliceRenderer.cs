using System.Numerics;
using System.Runtime.InteropServices;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

internal sealed record PhoenixSliceFrame(byte[] Pixels, float[] Depths, int Width, int Height,
    Fractal3DPose Pose);

/// <summary>GPU occupancy section followed by surface tracing; camera and marker reuse the field.</summary>
internal sealed class PhoenixParameterSliceRenderer : IDisposable
{
    internal const int Resolution = 192;
    internal const double FieldOfView = 45;
    internal static IEnumerable<ShaderCacheEntry> CacheEntries => new[] { "Build", "Render" }
        .Select(entry => new ShaderCacheEntry("phoenix-parameter-" + entry.ToLowerInvariant(),
            ShaderSource.Load("PhoenixParameterSlice.hlsl"), entry, "cs_5_0"));

    private readonly Direct3DDeviceHost _host;
    private readonly int _resolution;
    private ID3D11ComputeShader? _build, _render;
    private ID3D11Buffer? _constants, _pixels, _staging;
    private ID3D11Texture3D? _field;
    private ID3D11Texture3D? _fence;
    private ID3D11UnorderedAccessView? _fieldWrite, _pixelsWrite;
    private ID3D11ShaderResourceView? _fieldRead;
    private ID3D11SamplerState? _sampler;
    private float[]? _fieldKey;
    private int _pixelCount;
    private bool _disposed;
    internal int FieldBuildCount { get; private set; }

    internal PhoenixParameterSliceRenderer(Direct3DDeviceHost host, int resolution = Resolution)
    {
        if (resolution is < 16 or > 256 || resolution % 8 != 0) throw new ArgumentOutOfRangeException(nameof(resolution));
        _host = host.AddRef(); _resolution = resolution;
    }

    internal Task<PhoenixSliceFrame> RenderAsync(PhoenixState state, bool c1, Fractal3DPose pose,
        Vector3 marker, bool shadows, int width, int height, IProgress<int>? progress, CancellationToken token) =>
        Task.Run(() => Render(state, c1, pose, marker, shadows, width, height, progress, token), token);

    private PhoenixSliceFrame Render(PhoenixState state, bool c1, Fractal3DPose pose,
        Vector3 marker, bool shadows, int width, int height, IProgress<int>? progress, CancellationToken token)
    {
        // Only immutable snapshots enter this worker. Both views share the window's device gate.
        float[] values = new float[44];
        values[0] = _resolution; values[2] = c1 ? 0 : 1; values[3] = (int)state.Variant;
        values[4] = state.PrimaryPower; values[5] = state.SecondaryPower;
        values[6] = Math.Clamp(state.Iterations, 1, 1000);
        values[7] = (float)((double)state.Threshold * (double)state.Threshold);
        // Only the fourth, fixed coordinate changes the geometry of a section.
        values[9] = c1 ? 0 : (float)state.C1Imaginary;
        values[11] = c1 ? (float)state.C2Imaginary : 0;
        values[12] = (float)state.InitialZReal; values[13] = (float)state.InitialZImaginary;
        values[14] = (float)state.InitialPreviousReal; values[15] = (float)state.InitialPreviousImaginary;
        if (state.SecondaryPower > 0 && values.AsSpan(12, 4).IndexOfAnyExcept(0f) < 0) values[12] = 1;
        float[] key = values[..16];
        Put(values, 16, pose.Position); values[19] = (float)(1 / Math.Tan(FieldOfView * Math.PI / 360));
        Put(values, 20, pose.Forward); Put(values, 24, pose.Right); Put(values, 28, pose.Up);
        values[32] = width; values[33] = height;
        Put(values, 36, marker); values[39] = 0.055f; values[40] = shadows ? 1 : 0;
        byte[] pixels = new byte[checked(width * height * 4)];
        float[] depths = new float[checked(width * height)];

        _host.Gate.Wait(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureResources();
            ID3D11DeviceContext context = _host.Context;
            if (_fieldKey is null || !key.AsSpan().SequenceEqual(_fieldKey))
            {
                _fieldKey = null; // A cancelled build must never publish a partial field.
                for (int z = 0; z < _resolution; z += 4)
                {
                    token.ThrowIfCancellationRequested();
                    values[1] = z;
                    WriteConstants(values);
                    context.CSSetShader(_build!);
                    context.CSSetUnorderedAccessView(0, _fieldWrite!);
                    context.Dispatch((uint)(_resolution / 8), (uint)(_resolution / 8), 4);
                    context.CSSetUnorderedAccessView(0, null);
                    // Readback fences each small dispatch, keeping cancellation and TDR bounded.
                    Fence();
                    progress?.Report((z + 4) * 80 / _resolution);
                }
                _fieldKey = key;
                FieldBuildCount++;
            }
            EnsurePixels(width * Math.Min(height, 32));
            for (int row = 0; row < height; row += 32)
            {
                token.ThrowIfCancellationRequested();
                int rows = Math.Min(32, height - row);
                values[34] = row; values[35] = rows;
                WriteConstants(values);
                context.CSSetShader(_render!);
                context.CSSetShaderResource(0, _fieldRead!);
                context.CSSetSampler(0, _sampler!);
                context.CSSetUnorderedAccessView(1, _pixelsWrite!);
                context.Dispatch((uint)((width + 7) / 8), (uint)((rows + 7) / 8), 1);
                context.CSSetShaderResource(0, null);
                context.CSSetUnorderedAccessView(1, null);
                context.CopyResource(_staging!, _pixels!);
                MappedSubresource mapped = context.Map(_staging!, MapMode.Read);
                try
                {
                    int[] packed = new int[width * rows * 2];
                    Marshal.Copy(mapped.DataPointer, packed, 0, packed.Length);
                    for (int i = 0; i < width * rows; i++)
                    {
                        int pixel = packed[2 * i];
                        int offset = (row * width + i) * 4;
                        pixels[offset] = (byte)pixel; pixels[offset + 1] = (byte)(pixel >> 8);
                        pixels[offset + 2] = (byte)(pixel >> 16); pixels[offset + 3] = 255;
                        depths[row * width + i] = BitConverter.Int32BitsToSingle(packed[2 * i + 1]);
                    }
                }
                finally { context.Unmap(_staging!, 0); }
                progress?.Report(80 + (row + rows) * 20 / height);
            }
            token.ThrowIfCancellationRequested();
            return new(pixels, depths, width, height, pose);
        }
        finally { _host.Gate.Release(); }
    }

    private static void Put(float[] values, int index, Vector3 v)
    { values[index] = v.X; values[index + 1] = v.Y; values[index + 2] = v.Z; }

    private void EnsureResources()
    {
        ID3D11Device device = _host.Device;
        if (_build is not null) return;
        try
        {
            foreach (ShaderCacheEntry entry in CacheEntries)
            {
                var shader = device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(entry,
                    e => Compiler.Compile(e.Source, e.EntryPoint, "PhoenixParameterSlice", e.Profile)).Span);
                if (entry.EntryPoint == "Build") _build = shader; else _render = shader;
            }
            _constants = device.CreateBuffer(new BufferDescription { ByteWidth = 44 * 4,
                Usage = ResourceUsage.Dynamic, BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.Write });
            _field = device.CreateTexture3D(new Texture3DDescription { Width = (uint)_resolution, Height = (uint)_resolution,
                Depth = (uint)_resolution, MipLevels = 1, Format = Format.R32_Float,
                Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess });
            _fieldRead = device.CreateShaderResourceView(_field);
            _fieldWrite = device.CreateUnorderedAccessView(_field);
            _sampler = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear,
                TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp));
        }
        catch { DisposeResources(); throw; }
    }

    private void WriteConstants(float[] values)
    {
        ID3D11DeviceContext context = _host.Context;
        var mapped = context.Map(_constants!, MapMode.WriteDiscard);
        try { Marshal.Copy(values, 0, mapped.DataPointer, values.Length); }
        finally { context.Unmap(_constants!, 0); }
        context.CSSetConstantBuffer(0, _constants!);
    }

    private void Fence()
    {
        // A 4-byte copy from the volume waits for its preceding build dispatch.
        _fence ??= _host.Device.CreateTexture3D(new Texture3DDescription { Width = 1, Height = 1,
            Depth = 1, MipLevels = 1, Format = Format.R32_Float, Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
        _host.Context.CopySubresourceRegion(_fence, 0, 0, 0, 0, _field!, 0,
            new Vortice.Mathematics.Box(0, 0, 0, 1, 1, 1));
        _host.Context.Map(_fence, 0, MapMode.Read);
        _host.Context.Unmap(_fence, 0);
    }

    /// <summary>Read the section for CPU/GPU agreement diagnostics; never needed by navigation.</summary>
    internal async Task<float[]> ReadFieldAsync(CancellationToken token)
    {
        await _host.Gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_fieldKey is null) throw new InvalidOperationException("Срез ещё не построен.");
            using var staging = _host.Device.CreateTexture3D(new Texture3DDescription { Width = (uint)_resolution,
                Height = (uint)_resolution, Depth = (uint)_resolution, MipLevels = 1, Format = Format.R32_Float,
                Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
            _host.Context.CopyResource(staging, _field!);
            var mapped = _host.Context.Map(staging, 0, MapMode.Read);
            try
            {
                float[] result = new float[_resolution * _resolution * _resolution];
                for (int z = 0; z < _resolution; z++)
                    for (int y = 0; y < _resolution; y++)
                        Marshal.Copy(mapped.DataPointer + checked((int)(z * mapped.DepthPitch + y * mapped.RowPitch)),
                            result, (z * _resolution + y) * _resolution, _resolution);
                return result;
            }
            finally { _host.Context.Unmap(staging, 0); }
        }
        finally { _host.Gate.Release(); }
    }

    private void EnsurePixels(int count)
    {
        if (_pixelCount == count) return;
        _pixelsWrite?.Dispose(); _pixels?.Dispose(); _staging?.Dispose();
        _pixelCount = 0;
        _pixels = _host.Device.CreateBuffer(new BufferDescription { ByteWidth = checked((uint)(count * 8)),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.UnorderedAccess,
            MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = 8 });
        _pixelsWrite = _host.Device.CreateUnorderedAccessView(_pixels);
        _staging = _host.Device.CreateBuffer(new BufferDescription { ByteWidth = checked((uint)(count * 8)),
            Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
        _pixelCount = count;
    }

    public void Dispose()
    {
        _host.Gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            DisposeResources();
        }
        finally { _host.Gate.Release(); }
        _host.Release();
    }

    private void DisposeResources()
    {
        _build?.Dispose(); _render?.Dispose(); _constants?.Dispose(); _sampler?.Dispose();
        _fieldWrite?.Dispose(); _fieldRead?.Dispose(); _field?.Dispose(); _fence?.Dispose();
        _pixelsWrite?.Dispose(); _pixels?.Dispose(); _staging?.Dispose();
        _build = _render = null; _constants = _pixels = _staging = null;
        _field = _fence = null; _fieldWrite = _pixelsWrite = null;
        _fieldRead = null; _sampler = null; _fieldKey = null; _pixelCount = 0;
    }
}
