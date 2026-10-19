using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class GrayScottWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Real(this, DiffusionUBox, "Диффузия U", double.Epsilon, 1);
        NumericInput.Real(this, DiffusionVBox, "Диффузия V", double.Epsilon, 1);
        NumericInput.Real(this, FeedBox, "Подача F", 0, .2);
        NumericInput.Real(this, KillBox, "Убыль K", 0, .2);
        NumericInput.Real(this, DeltaTimeBox, "Шаг времени", .05, 1.5);
        NumericInput.Real(this, RangeMinimumBox, "Минимум окраски", -double.MaxValue / 4, double.MaxValue / 4);
        NumericInput.Real(this, RangeMaximumBox, "Максимум окраски", -double.MaxValue / 4, double.MaxValue / 4);
        NumericInput.Integer(this, GridSizeBox, "Размер сетки", 32, GrayScottState.MaxGridSize);
        NumericInput.Integer(this, StepsPerFrameBox, "Шаги на кадр", 1, 64);
        NumericInput.Integer(this, RandomSeedBox, "Seed", int.MinValue, int.MaxValue);
        NumericInput.Integer(this, SeedCountBox, "Число затравок", 1, 500);
        NumericInput.Integer(this, SeedRadiusBox, "Радиус затравки", 1, 128);
        NumericInput.Integer(this, BrushRadiusBox, "Радиус кисти", 1, 128);
        NumericInput.Integer(this, FrameWidthBox, "Ширина кадра", 32, 8192);
        NumericInput.Integer(this, FrameHeightBox, "Высота кадра", 32, 8192);
        NumericInput.Pair(this, FrameWidthBox, FrameHeightBox, (w, h) => (long)int.Parse(w) * int.Parse(h) <= 16777216, "Не более 16 млн пикселей в кадре.");
        NumericInput.OrderedPair(this, RangeMinimumBox, RangeMaximumBox);
    }
}
