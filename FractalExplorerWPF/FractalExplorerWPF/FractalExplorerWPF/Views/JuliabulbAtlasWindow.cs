using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Атлас динамических форм Жюлиабульба: три компоненты C и сферическая степень.</summary>
public sealed class JuliabulbAtlasWindow : Fractal3DParameterAtlasWindow
{
    private readonly double[] _original;
    public decimal[] SelectedParameters => SelectedComponents;

    public JuliabulbAtlasWindow(Fractal3DState source) : base(source)
    {
        if (source.Kind != Fractal3DKind.Juliabulb) throw new ArgumentException("Нужен режим Жюлиабульба.");
        _original = [source.JuliaCX, source.JuliaCY, source.JuliaCZ, source.Power];
        for (int i = 0; i < _original.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (!double.IsFinite(_original[i]) || _original[i] < (double)bounds.Minimum || _original[i] > (double)bounds.Maximum)
                throw new ArgumentException("Компоненты C должны быть от −8 до 8, степень — от −32 до 32.");
        }
        Initialize3DAtlas("Атлас параметров · Жюлиабульб", ["C · x", "C · y", "C · z", "Степень n"],
            _original.Select(value => (decimal)value).ToArray(),
            "Выберите две компоненты C или степень для осей; остальные значения фиксированы. " +
            "Превью используют текущие камеру и оформление. Применение меняет только C и степень. Некоторые варианты могут быть пустыми.");
    }

    protected override (decimal Minimum, decimal Maximum) ComponentBounds(int index) => index == 3 ? (-32m, 32m) : (-8m, 8m);
    protected override (decimal Minimum, decimal Maximum) AxisBounds(int index) => ComponentBounds(index);
    protected override decimal InitialRangeWidth(int index) => index == 3 ? 2m : .4m;
    protected override ParameterAtlasRange OverviewRange => new(
        XAxisIndex == 3 ? 2m : -2m, XAxisIndex == 3 ? 16m : 2m,
        YAxisIndex == 3 ? 2m : -2m, YAxisIndex == 3 ? 16m : 2m);

    public void ApplySelectionTo(Fractal3DState state) => ApplyComponents(state, SelectedComponents);

    protected override void ApplyComponents(Fractal3DState state, decimal[] point)
    {
        if (state.Kind != Fractal3DKind.Juliabulb || point.Length != 4)
            throw new ArgumentException("Нужны три компоненты C и степень Жюлиабульба.");
        var values = new double[4];
        for (int i = 0; i < values.Length; i++)
        {
            var bounds = ComponentBounds(i);
            if (point[i] < bounds.Minimum || point[i] > bounds.Maximum)
                throw new ArgumentException("Параметр вне допустимого диапазона.");
            // Не терять точность исходного double при возврате неизменённого decimal из оболочки.
            values[i] = point[i] == (decimal)_original[i] ? _original[i] : (double)point[i];
        }
        state.JuliaCX = values[0]; state.JuliaCY = values[1]; state.JuliaCZ = values[2]; state.Power = values[3];
    }
}
