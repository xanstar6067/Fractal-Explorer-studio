using System.Diagnostics;
using System.Runtime.InteropServices;
using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private ID3D11ComputeShader? _newtonCompute;
    private ID3D11PixelShader? _newtonPixel;
    private ID3D11Buffer? _newtonConstants;
    private ID3D11ShaderResourceView? _newtonView;
    private Newton3DSettings? _newtonSource;
    internal int NewtonFieldBuilds { get; private set; }
    internal bool NeedsNewtonField(Newton3DSettings settings) => _newtonSource?.SameField(settings) != true;

    private ID3D11ShaderResourceView NewtonVolumeView(Fractal3DState state, CancellationToken token,
        IProgress<int>? progress = null)
    {
        var settings = state.Newton;
        settings.Validate();
        token.ThrowIfCancellationRequested();
        if (!NeedsNewtonField(settings)) return _newtonView!;
        _newtonCompute ??= _device!.CreateComputeShader(Compile(Newton3DComputeShader.CacheEntry).Span);
        _newtonConstants ??= _device!.CreateBuffer(new BufferDescription {
            ByteWidth = 48, Usage = ResourceUsage.Dynamic, BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write });
        uint side = (uint)settings.Size;
        using var texture = _device!.CreateTexture3D(new Texture3DDescription {
            Width = side, Height = side, Depth = side, MipLevels = 1, Format = Format.R32G32B32A32_Float,
            Usage = ResourceUsage.Default, BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource });
        using var output = _device.CreateUnorderedAccessView(texture);
        using var fence = _device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
        float[] parameters = [side, settings.Degree, settings.MaxIterations, 0,
            (float)settings.Extent, (float)settings.SliceW, (float)settings.Relaxation, settings.BoundaryIterations,
            (int)settings.Region, settings.PlusRoot, settings.MinusRoot, 0];
        var context = _context!;
        context.PSSetShaderResource(0, null!);
        try
        {
            const uint batchDepth = 4;
            for (uint plane = 0; plane < side; plane += batchDepth)
            {
                token.ThrowIfCancellationRequested();
                parameters[3] = plane;
                var mapped = context.Map(_newtonConstants, MapMode.WriteDiscard);
                try { Marshal.Copy(parameters, 0, mapped.DataPointer, parameters.Length); }
                finally { context.Unmap(_newtonConstants, 0); }
                context.CSSetShader(_newtonCompute);
                context.CSSetConstantBuffer(0, _newtonConstants);
                context.CSSetUnorderedAccessView(0, output);
                context.Dispatch((side + 7) / 8, (side + 7) / 8, batchDepth);
                context.CSSetUnorderedAccessView(0, null!);
                context.End(fence);
                context.Flush();
                var watch = Stopwatch.StartNew();
                while (true)
                {
                    // No Present drives this offscreen queue. Old drivers can hold each small
                    // batch for two seconds if polling forbids flushing (as in GrayScottGpuEngine).
                    var result = context.GetData(fence, IntPtr.Zero, 0, AsyncGetDataFlags.None);
                    result.CheckError();
                    if (result.Code == 0) break;
                    token.ThrowIfCancellationRequested();
                    if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("ГП не завершил расчёт бассейнов Ньютона.");
                    Thread.Sleep(1);
                }
                progress?.Report((int)(Math.Min(plane + batchDepth, side) * 60 / side));
            }
        }
        finally { context.CSSetUnorderedAccessView(0, null!); }
        token.ThrowIfCancellationRequested();
        var view = _device.CreateShaderResourceView(texture);
        _newtonView?.Dispose();
        _newtonView = view;
        _newtonSource = settings with { };
        NewtonFieldBuilds++;
        return view;
    }

    internal float[] ReadNewtonField(Fractal3DState state, CancellationToken token)
    {
        Gate.Wait(token);
        try
        {
            EnsureDevice();
            var view = NewtonVolumeView(state, token);
            uint side = (uint)state.Newton.Size;
            using var resource = view.Resource;
            using var texture = resource.QueryInterface<ID3D11Texture3D>();
            using var staging = _device!.CreateTexture3D(new Texture3DDescription {
                Width = side, Height = side, Depth = side, MipLevels = 1, Format = Format.R32G32B32A32_Float,
                Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
            _context!.CopyResource(staging, texture);
            var mapped = _context.Map(staging, 0, MapMode.Read);
            var values = new float[side * side * side * 4];
            try
            {
                for (int z = 0; z < side; z++) for (int y = 0; y < side; y++)
                    Marshal.Copy(mapped.DataPointer + checked((int)(z * mapped.DepthPitch + y * mapped.RowPitch)),
                        values, (int)((z * side + y) * side * 4), (int)side * 4);
            }
            finally { _context.Unmap(staging, 0); }
            return values;
        }
        finally { Gate.Release(); }
    }

    private void DisposeNewton()
    {
        _newtonView?.Dispose(); _newtonConstants?.Dispose();
        _newtonCompute?.Dispose(); _newtonPixel?.Dispose();
        _newtonSource = null;
    }
}
