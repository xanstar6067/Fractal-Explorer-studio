using System.Windows.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class QuaternionJuliaAtlasWindow : ParameterAtlasWindow
{
    private readonly Fractal3DState _source;
    private readonly Fractal3DRenderer _renderer = new();
    public decimal[] SelectedConstant => SelectedComponents;
    protected override int PreviewIterationCount => Math.Clamp(_source.Iterations, 1, 32);
    protected override decimal ComponentMinimum => -8;
    protected override decimal ComponentMaximum => 8;
    protected override decimal RangeMinimum => -8;
    protected override decimal RangeMaximum => 8;

    public QuaternionJuliaAtlasWindow(Fractal3DState source)
    {
        if (source.Kind != Fractal3DKind.QuaternionJulia) throw new ArgumentException("Нужен режим кватернионного Жюлиа.");
        _source = source.Clone();
        InitializeAtlas("Атлас константы · Кватернионное Жюлиа", ["C · x (вещественная)", "C · y (i)", "C · z (j)", "C · w (k)"],
            [(decimal)source.JuliaCX, (decimal)source.JuliaCY, (decimal)source.JuliaCZ, (decimal)source.JuliaCW],
            "Выберите две компоненты C для осей; две другие фиксированы. Все варианты используют текущие срез, его наклон и камеру. Применение меняет только C.");
        ((ComboBox)FindName("GridSizeBox")).SelectedIndex = 0;
    }

    protected override Func<decimal[], CancellationToken, Task<byte[]>> CreateAsyncRenderSnapshot()
    {
        var source = _source.Clone();
        source.Iterations = PreviewIterationCount;
        source.MaxSteps = Math.Min(source.MaxSteps, 160);
        source.Ssaa = 1; source.SoftShadows = false; source.AmbientOcclusion = false;
        return async (point, token) =>
        {
            var state = source.Clone();
            SetConstant(state, point);
            var frame = await _renderer.RenderPixelsAsync(state, 144, 108, null, null, token);
            token.ThrowIfCancellationRequested();
            return frame.Buffer;
        };
    }

    internal static void SetConstant(Fractal3DState state, decimal[] point)
    {
        if (point.Length != 4 || point.Any(c => c < -8 || c > 8)) throw new ArgumentException("Четыре компоненты C должны быть от −8 до 8.");
        state.JuliaCX = (double)point[0]; state.JuliaCY = (double)point[1];
        state.JuliaCZ = (double)point[2]; state.JuliaCW = (double)point[3];
    }
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · ГП, по одному варианту · до {iterations} итераций";
    protected override void ReleaseRenderResources() => _renderer.Dispose();
}
