using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public interface IGrayScottEngine : IDisposable
{
    int Size { get; }
    GrayScottBackend Backend { get; }
    string DeviceName { get; }
    void Advance(int steps, CancellationToken token);
    void Inject(double x, double y, int radius);
    GrayScottSnapshot Snapshot();
    byte[] RenderFrame(GrayScottState state, int width, int height, CancellationToken token, double displayAspect = 0);
}

public sealed class GrayScottCpuEngine(GrayScottState state) : IGrayScottEngine
{
    private readonly GrayScottSimulation _simulation = new(state);
    public int Size => _simulation.Size;
    public GrayScottBackend Backend => GrayScottBackend.Cpu;
    public string DeviceName => "ЦП";
    public void Advance(int steps, CancellationToken token) => _simulation.Advance(steps, token);
    public void Inject(double x, double y, int radius) => _simulation.Inject(x, y, radius);
    public GrayScottSnapshot Snapshot() => _simulation.Snapshot();
    public byte[] RenderFrame(GrayScottState state, int width, int height, CancellationToken token, double displayAspect = 0) => GrayScottRenderer.RenderFrame(_simulation.CurrentView(), state, width, height, token, displayAspect);
    public void Dispose() { }
}

internal static class GrayScottEngineFactory
{
    internal static Func<GrayScottState, IGrayScottEngine>? GpuFactoryOverrideForTests { get; set; }
    public static IGrayScottEngine Create(GrayScottState state, out string? fallback)
    {
        fallback = null;
        if (state.Backend == GrayScottBackend.Gpu)
        {
            try { return GpuFactoryOverrideForTests?.Invoke(state) ?? new GrayScottGpuEngine(state); }
            catch (Exception ex) { fallback = $"ГП недоступен. Продолжаем на ЦП: {ex.Message}"; }
        }
        return new GrayScottCpuEngine(state);
    }
}
