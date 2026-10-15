using System.Diagnostics;
using System.Runtime.InteropServices;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Core.Rendering;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private ID3D11ComputeShader? _newtonCompute;
    private string? _newtonComputeKey;
    private ID3D11PixelShader? _newtonPixel;
    private ID3D11Buffer? _newtonConstants;
    private ID3D11ShaderResourceView? _newtonView;
    private Newton3DSettings? _newtonSource;
    private bool _newtonExpressionProbe;
    internal int NewtonFieldBuilds { get; private set; }
    internal bool NeedsNewtonField(Newton3DSettings settings) => _newtonExpressionProbe || _newtonSource?.SameField(settings) != true;

    private ID3D11ShaderResourceView NewtonVolumeView(Fractal3DState state, CancellationToken token,
        IProgress<int>? progress = null, bool expressionProbe = false)
    {
        var settings = state.Newton;
        settings.Validate();
        if (!expressionProbe) settings = state.Newton = Newton3DFormula.Prepare(settings, token);
        token.ThrowIfCancellationRequested();
        if (_newtonExpressionProbe == expressionProbe && _newtonSource?.SameField(settings) == true) return _newtonView!;
        var entry = Newton3DComputeShader.Entry(settings, expressionProbe);
        if (_newtonComputeKey != entry.Key)
        {
            var shader = _device!.CreateComputeShader(Compile(entry).Span);
            _newtonCompute?.Dispose(); _newtonCompute = shader; _newtonComputeKey = entry.Key;
        }
        _newtonConstants ??= _device!.CreateBuffer(new BufferDescription {
            ByteWidth = 560, Usage = ResourceUsage.Dynamic, BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write });
        uint side = (uint)settings.Size;
        using var texture = _device!.CreateTexture3D(new Texture3DDescription {
            Width = side, Height = side, Depth = side, MipLevels = 1, Format = Format.R32G32B32A32_Float,
            Usage = ResourceUsage.Default, BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource });
        using var output = _device.CreateUnorderedAccessView(texture);
        using var fence = _device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
        var parameters = new float[140];
        float[] header = [side, settings.RootCount, settings.MaxIterations, 0,
            (float)settings.Extent, (float)settings.SliceW, (float)settings.Relaxation, settings.BoundaryIterations,
            (int)settings.Region, settings.PlusRoot, settings.MinusRoot, 0];
        header.CopyTo(parameters, 0);
        if (settings.Formula is not null && settings.Roots is not null)
            for (int i = 0; i < settings.Roots.Values.Count; i++)
            {
                parameters[12 + i * 4] = (float)settings.Roots.Values[i].Real;
                parameters[13 + i * 4] = (float)settings.Roots.Values[i].Imaginary;
            }
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
        _newtonExpressionProbe = expressionProbe;
        NewtonFieldBuilds++;
        return view;
    }

    internal float[] ReadNewtonField(Fractal3DState state, CancellationToken token, bool expressionProbe = false)
    {
        Gate.Wait(token);
        try
        {
            EnsureDevice();
            var view = NewtonVolumeView(state, token, expressionProbe: expressionProbe);
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
