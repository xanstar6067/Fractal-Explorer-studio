using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Масштаб складывания и глубина тетраэдра Серпинского.</summary>
public sealed class SierpinskiAtlasWindow : Fractal3DParameterAtlasWindow
{
    private readonly double _originalScale;
    private readonly Fractal3DField _iterations;

    public SierpinskiAtlasWindow(Fractal3DState source) : base(source)
    {
        if (source.Kind != Fractal3DKind.SierpinskiTetrahedron)
            throw new ArgumentException("Нужен тетраэдр Серпинского.");
        _originalScale = source.SierpinskiScale;
        _iterations = Fractal3DCatalog.Mode(source.Kind).IterationsField;
        if (!double.IsFinite(_originalScale) || _originalScale < 1.05 || _originalScale > 8 ||
            source.Iterations < _iterations.Minimum || source.Iterations > _iterations.Maximum)
            throw new ArgumentException("Параметры тетраэдра вне допустимого диапазона.");
        Initialize3DAtlas("Атлас параметров · Тетраэдр Серпинского", ["Масштаб складывания", "Итерации"],
            [(decimal)_originalScale, source.Iterations],
            "По горизонтали — масштаб складывания, по вертикали — целое число итераций. " +
            "Камера и оформление — из текущего вида. Применение меняет только эти два параметра. " +
            "При больших масштабах форма может распадаться на мелкие детали; некоторые варианты выходят за кадр.");
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => index == 0
        ? (1.05m, 8m) : ((decimal)_iterations.Minimum, (decimal)_iterations.Maximum);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override bool IsIntegerComponent(int index) => index == 1;
    protected override decimal InitialRangeWidth(int index) => index == 0 ? .4m : 4m;
    protected override ParameterAtlasRange OverviewRange => new(1.05m, 8m, (decimal)_iterations.Minimum, (decimal)_iterations.Maximum);
    protected override string PreviewStatus(int completed, int total, int iterations) =>
        $"{completed} / {total} · ГП, по одному варианту · итерации по оси Y";

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.SierpinskiTetrahedron || point.Length != 2)
            throw new ArgumentException("Нужны масштаб и итерации тетраэдра Серпинского.");
        for (int i = 0; i < point.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (point[i] < bounds.Minimum || point[i] > bounds.Maximum ||
                IsIntegerComponent(i) && point[i] != decimal.Truncate(point[i]))
                throw new ArgumentException("Параметр вне диапазона или требует целого значения.");
        }
        state.SierpinskiScale = point[0] == (decimal)_originalScale ? _originalScale : (double)point[0];
        state.Iterations = (int)point[1];
    }
}
