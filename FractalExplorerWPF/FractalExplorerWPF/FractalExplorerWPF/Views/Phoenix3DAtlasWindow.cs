using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class Phoenix3DAtlasWindow : Fractal3DParameterAtlasWindow
{
    public decimal[] SelectedConstants => SelectedComponents;

    public Phoenix3DAtlasWindow(Fractal3DState source) : base(source)
    {
        if (source.Kind != Fractal3DKind.Phoenix) throw new ArgumentException("Нужен режим Phoenix 3D.");
        Initialize3DAtlas("Атлас C₁ / C₂ · Феникс 3D",
            ["C₁ · вещественная", "C₁ · i", "C₁ · j", "C₂ · вещественная", "C₂ · i", "C₂ · j"],
            Phoenix3DParameters.FromState(source).AtlasPoint,
            "Выберите две из шести компонент C₁/C₂ для осей; остальные фиксированы. Камера, степени и оформление одинаковы во всех ячейках. Пустые формы возможны. Применение меняет только константы.",
            xAxis: 0, yAxis: 3);
    }

    protected override void ApplyComponents(Fractal3DState state, decimal[] point) =>
        Phoenix3DParameters.FromState(state).WithAtlasPoint(point).ApplyTo(state);
}
