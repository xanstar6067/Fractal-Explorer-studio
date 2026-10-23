using System.Windows.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Последовательные GPU-превью параметров с камерой и оформлением исходного 3D-вида.</summary>
public abstract class Fractal3DParameterAtlasWindow : ParameterAtlasWindow
{
    private readonly Fractal3DState _source;
    private readonly Fractal3DRenderer _renderer = new();
    private const int PreviewWidth = 144, PreviewHeight = 108;
    // Допуск попадания луча — доля пикселя, поэтому в мелкой миниатюре он в разы шире,
    // чем в окне: вокруг «пыли» без внутренности миниатюра рисовала толстую ложную
    // оболочку, которой в окне нет. Детализация пересчитывается на типичную высоту холста.
    private const double WindowCanvasHeight = 720;
    internal static double PreviewDetail(double detail) => detail * PreviewHeight / WindowCanvasHeight;
    protected override int PreviewIterationCount => Math.Clamp(_source.Iterations, 1, 32);
    protected override decimal ComponentMinimum => -8;
    protected override decimal ComponentMaximum => 8;
    protected override decimal RangeMinimum => -8;
    protected override decimal RangeMaximum => 8;

    protected Fractal3DParameterAtlasWindow(Fractal3DState source) => _source = source.Clone();

    protected void Initialize3DAtlas(string title, string[] names, decimal[] point, string hint, int xAxis = 0, int yAxis = 1)
    {
        InitializeAtlas(title, names, point, hint, xAxis, yAxis);
        ((ComboBox)FindName("GridSizeBox")).SelectedIndex = 0;
    }

    protected abstract void ApplyComponents(Fractal3DState state, decimal[] point);

    public virtual void ApplySelectionTo(Fractal3DState state) => ApplyComponents(state, SelectedComponents);

    protected override Func<decimal[], CancellationToken, Task<byte[]>> CreateAsyncRenderSnapshot()
    {
        var source = _source.Clone();
        source.Iterations = PreviewIterationCount;
        source.MaxSteps = Math.Min(source.MaxSteps, 160);
        source.Detail = PreviewDetail(source.Detail);
        source.Ssaa = 1; source.SoftShadows = false; source.AmbientOcclusion = false;
        return async (point, token) =>
        {
            var state = source.Clone();
            ApplyComponents(state, point);
            var frame = await _renderer.RenderPixelsAsync(state, PreviewWidth, PreviewHeight, null, null, token);
            token.ThrowIfCancellationRequested();
            return frame.Buffer;
        };
    }

    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · ГП, по одному варианту · до {iterations} итераций";
    protected override void ReleaseRenderResources() => _renderer.Dispose();
}
