using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Цвета и интерполяция, общие для всех лабораторий.</summary>
internal static class LaboratoryHelpers
{
    /// <summary>Циклическая палитра лаборатории: основной → акцентный → второй цвет.</summary>
    public static Color Palette(MathematicalLaboratoryState state, double amount)
    {
        amount -= Math.Floor(amount);
        return amount < 0.5
            ? Mix(state.PrimaryColor, state.AccentColor, amount * 2)
            : Mix(state.AccentColor, state.SecondaryColor, amount * 2 - 1);
    }

    public static Color Mix(Color from, Color to, double amount) => RasterDrawing.Lerp(from, to, amount);

    public static LaboratoryPoint Lerp(LaboratoryPoint from, LaboratoryPoint to, double amount) =>
        new(from.X + (to.X - from.X) * amount, from.Y + (to.Y - from.Y) * amount);
}
