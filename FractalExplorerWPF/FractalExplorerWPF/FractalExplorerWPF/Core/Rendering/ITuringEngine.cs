using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public interface ITuringEngine : IDisposable
{
    TuringBackend Backend { get; }
    string DeviceName { get; }
    void Advance(int steps, TuringState state, CancellationToken token);
    void Paint(double x, double y, double radius, double strength, TuringBrush brush, TuringState state);
    TuringCheckpoint Snapshot();
    byte[] RenderFrame(TuringState state, int width, int height, CancellationToken token, double displayAspect = 0);
}

public sealed class TuringCpuEngine(TuringState state) : ITuringEngine
{
    private readonly TuringSimulation _simulation = new(state);
    public TuringBackend Backend => TuringBackend.Cpu;
    public string DeviceName => "ЦП";
    public void Advance(int steps, TuringState state, CancellationToken token) => _simulation.Advance(steps, state, token);
    public void Paint(double x, double y, double radius, double strength, TuringBrush brush, TuringState state) => _simulation.Paint(x, y, radius, strength, brush, state);
    public TuringCheckpoint Snapshot() => _simulation.Snapshot();
    public byte[] RenderFrame(TuringState state, int width, int height, CancellationToken token, double displayAspect = 0) => TuringRenderer.RenderFrame(_simulation.Snapshot(), state, width, height, token, displayAspect);
    public void Dispose() { }
}

internal static class TuringEngineFactory
{
    internal static Func<TuringState, ITuringEngine>? GpuFactoryOverrideForTests { get; set; }
    public static ITuringEngine Create(TuringState state, out string? fallback)
    {
        fallback = null;
        if (state.Backend == TuringBackend.Gpu)
        {
            try { return GpuFactoryOverrideForTests?.Invoke(state) ?? new TuringGpuEngine(state); }
            catch (Exception ex) { fallback = $"ГП недоступен. Продолжаем на ЦП: {ex.Message}"; }
        }
        return new TuringCpuEngine(state);
    }
}
