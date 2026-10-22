using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public sealed class QuaternionJuliaAtlasWindow : Fractal3DParameterAtlasWindow
{
    public decimal[] SelectedConstant => SelectedComponents;

    public QuaternionJuliaAtlasWindow(Fractal3DState source) : base(source)
    {
        if (source.Kind != Fractal3DKind.QuaternionJulia) throw new ArgumentException("Нужен режим кватернионного Жюлиа.");
        Initialize3DAtlas("Атлас константы · Кватернионное Жюлиа", ["C · x (вещественная)", "C · y (i)", "C · z (j)", "C · w (k)"],
            [(decimal)source.JuliaCX, (decimal)source.JuliaCY, (decimal)source.JuliaCZ, (decimal)source.JuliaCW],
            "Выберите две компоненты C для осей; две другие фиксированы. Все варианты используют текущие срез, его наклон и камеру. Применение меняет только C.");
    }

    protected override void ApplyComponents(Fractal3DState state, decimal[] point) => SetConstant(state, point);

    internal static void SetConstant(Fractal3DState state, decimal[] point)
    {
        if (point.Length != 4 || point.Any(c => c < -8 || c > 8)) throw new ArgumentException("Четыре компоненты C должны быть от −8 до 8.");
        state.JuliaCX = (double)point[0]; state.JuliaCY = (double)point[1];
        state.JuliaCZ = (double)point[2]; state.JuliaCW = (double)point[3];
    }
}
