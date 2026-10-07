using System.Runtime.InteropServices;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>GPU orbit metrics, with the existing CPU palette and lighting pipeline.</summary>
public sealed class MandelbrotPreviewRenderer : IDisposable
{
    public const double MaximumZoom = 1_000_000;
    public const int MaximumIterations = 4096;
    private readonly object _gate = new();
    private readonly Direct3DDeviceHost _host = new();
    private ID3D11ComputeShader? _shader;
    private ID3D11Buffer? _constants, _output, _staging, _coordinates;
    private ID3D11ShaderResourceView? _coordinateView;
    private int _coordinateCapacity;
    private ID3D11UnorderedAccessView? _view;
    private int _capacity;
    private volatile bool _failed, _disposed;
    public bool IsAvailable => !_failed && !_disposed;
    public string? FailureReason { get; private set; }

    public static bool Supports(MandelbrotState state) =>
        state.Variant is MandelbrotVariant.Julia or MandelbrotVariant.JuliaBurningShip
            or MandelbrotVariant.Mandelbrot or MandelbrotVariant.BurningShip &&
        state.Zoom >= 0.01 && state.Zoom <= MaximumZoom &&
        state.Iterations is > 0 and <= MaximumIterations &&
        state.CenterXExact is null && state.CenterYExact is null;

    public bool CanRender(MandelbrotState state) => IsAvailable && Supports(state);

    // Each renderer belongs to one view. Calls and disposal are serialized; no resource can
    // be freed while a worker is submitting a frame. The CPU fallback never retries a bad device.
    public bool TryRender(MandelbrotState state, byte[] pixels, int width, int height,
        CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (!CanRender(state)) return false;
            try
            {
                var device = _host.Device;
                var context = _host.Context;
                if (_host.AdapterName.Contains("WARP", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Аппаратный Direct3D 11 недоступен.");
                _shader ??= device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(
                    MandelbrotPreviewShader.CacheEntry, e => Compiler.Compile(e.Source, e.EntryPoint, "JuliaPreview", e.Profile)).Span);
                _constants ??= device.CreateBuffer(new BufferDescription { ByteWidth = 96,
                    Usage = ResourceUsage.Dynamic, BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.Write });
                bool relief = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
                int sampleWidth = checked(width + (relief ? 2 : 0));
                int sampleHeight = checked(height + (relief ? 2 : 0));
                int bandHeight = Math.Min(32, sampleHeight);
                int count = checked(sampleWidth * bandHeight);
                if (_capacity < count)
                {
                    _view?.Dispose(); _output?.Dispose(); _staging?.Dispose();
                    _view = null; _output = null; _staging = null; _capacity = 0;
                    _output = device.CreateBuffer(new BufferDescription { ByteWidth = checked((uint)(count * 20)),
                        Usage = ResourceUsage.Default, BindFlags = BindFlags.UnorderedAccess,
                        MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = 20 });
                    _view = device.CreateUnorderedAccessView(_output);
                    _staging = device.CreateBuffer(new BufferDescription { ByteWidth = checked((uint)(count * 20)),
                        Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
                    _capacity = count;
                }
                double viewWidth = 3 / state.Zoom.ToDouble();
                double step = viewWidth / width;
                var parameters = new Parameters
                {
                    OriginX = (double)state.CenterX - viewWidth / 2 - (relief ? step : 0),
                    OriginY = (double)state.CenterY + viewWidth * height / width / 2 + (relief ? step : 0),
                    StepX = step, StepY = step,
                    ConstantX = (double)state.JuliaCReal, ConstantY = (double)state.JuliaCImaginary,
                    ThresholdSquared = Math.Max(relief ? 4 : 0, (double)(state.Threshold * state.Threshold)),
                    StripeFrequency = state.StripeFrequency, Width = (uint)sampleWidth, Height = (uint)sampleHeight,
                    Limit = (uint)state.Iterations,
                    IsJulia = state.Variant is MandelbrotVariant.Julia or MandelbrotVariant.JuliaBurningShip ? 1u : 0u,
                    IsShip = state.Variant is MandelbrotVariant.BurningShip or MandelbrotVariant.JuliaBurningShip ? 1u : 0u,
                    Mode = (uint)state.ColoringMode
                };
                // Compute only the 1D axes on CPU using the existing decimal grid. Converting
                // after decimal addition matches CPU pixel positions even at exact axes, where
                // one double ULP can flip a reflected orbit to a different branch.
                decimal decimalWidth = 3m / (decimal)state.Zoom.ToDouble();
                decimal decimalHeight = decimalWidth * height / width;
                var axes = new double[sampleWidth + sampleHeight];
                for (int x = 0; x < sampleWidth; x++)
                    axes[x] = (double)(state.CenterX + ((decimal)(x - (relief ? 1 : 0)) / width - 0.5m) * decimalWidth);
                for (int y = 0; y < sampleHeight; y++)
                    axes[sampleWidth + y] = (double)(state.CenterY + (0.5m - (decimal)(y - (relief ? 1 : 0)) / height) * decimalHeight);
                if (_coordinateCapacity < axes.Length)
                {
                    _coordinateView?.Dispose(); _coordinates?.Dispose();
                    _coordinateView = null; _coordinates = null; _coordinateCapacity = 0;
                    _coordinates = device.CreateBuffer(new BufferDescription { ByteWidth = checked((uint)(axes.Length * 8)),
                        Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource,
                        MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = 8 });
                    _coordinateView = device.CreateShaderResourceView(_coordinates);
                    _coordinateCapacity = axes.Length;
                }
                // UpdateSubresource must cover the full allocated buffer when the view shrinks.
                if (axes.Length < _coordinateCapacity) Array.Resize(ref axes, _coordinateCapacity);
                context.UpdateSubresource(axes, _coordinates!);
                var metrics = new float[checked(sampleWidth * sampleHeight * 5)];
                for (int y = 0; y < sampleHeight; y += bandHeight)
                {
                    token.ThrowIfCancellationRequested();
                    parameters.FirstRow = (uint)y;
                    parameters.RowCount = (uint)Math.Min(bandHeight, sampleHeight - y);
                    var mapped = context.Map(_constants, MapMode.WriteDiscard);
                    try { Marshal.StructureToPtr(parameters, mapped.DataPointer, false); }
                    finally { context.Unmap(_constants, 0); }
                    context.CSSetConstantBuffer(0, _constants); context.CSSetShader(_shader);
                    context.CSSetShaderResource(0, _coordinateView);
                    context.CSSetUnorderedAccessView(0, _view);
                    try { context.Dispatch((uint)((sampleWidth + 7) / 8), (parameters.RowCount + 7) / 8, 1); }
                    finally { context.CSSetUnorderedAccessView(0, null); context.CSSetShaderResource(0, null); }
                    context.CopyResource(_staging!, _output!);
                    mapped = context.Map(_staging!, MapMode.Read);
                    try { Marshal.Copy(mapped.DataPointer, metrics, y * sampleWidth * 5, (int)parameters.RowCount * sampleWidth * 5); }
                    finally { context.Unmap(_staging!, 0); }
                }
                token.ThrowIfCancellationRequested();
                MandelbrotFamilyRenderer.ColorGpuMetrics(state, metrics, pixels, width, height, token);
                token.ThrowIfCancellationRequested();
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _failed = true;
                FailureReason = ex.Message;
                CrashLogger.Log("MandelbrotPreviewRenderer", ex);
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _view?.Dispose(); _output?.Dispose(); _staging?.Dispose();
            _coordinateView?.Dispose(); _coordinates?.Dispose();
            _constants?.Dispose(); _shader?.Dispose(); _host.Release();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters
    {
        public double OriginX, OriginY, StepX, StepY, ConstantX, ConstantY, ThresholdSquared, StripeFrequency;
        public uint Width, Height, FirstRow, RowCount, Limit, IsJulia, IsShip, Mode;
    }
}
