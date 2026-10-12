using System.Globalization;
using System.Windows.Controls;

namespace FractalExplorerWPF.Controls;

/// <summary>Числовые поля панелей параметров: ввод в любой культуре, ошибка с допустимым диапазоном.</summary>
internal static class PanelFields
{
    public static bool TryReadDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    public static double ReadDouble(TextBox box, string name, double minimum, double maximum)
    {
        if (!TryReadDouble(box.Text, out double value) || !double.IsFinite(value) ||
            value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"Параметр «{name}» должен быть числом от {minimum:G8} до {maximum:G8}.");
        }
        return value;
    }

    public static int ReadInt(TextBox box, string name, int minimum, int maximum)
    {
        if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
            value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"Параметр «{name}» должен быть целым числом от {minimum} до {maximum}.");
        }
        return value;
    }

    public static string Format(double value) => value.ToString("G15", CultureInfo.InvariantCulture);
}
