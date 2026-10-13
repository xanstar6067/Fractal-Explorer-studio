using System.Globalization;
using System.Windows.Controls;

namespace FractalExplorerWPF.Controls;

/// <summary>
/// Список числа потоков рендера в панели двумерного окна: 1…N по числу логических процессоров
/// и «Auto» (выбран по умолчанию).
/// </summary>
public static class RenderThreadsBox
{
    public const string Auto = "Auto";

    public static void Fill(ComboBox box)
    {
        for (int count = 1; count <= Environment.ProcessorCount; count++) box.Items.Add(count);
        box.Items.Add(Auto);
        box.SelectedItem = Auto;
    }

    public static bool IsAuto(ComboBox box) => box.SelectedItem?.ToString() == Auto;

    /// <summary>Выбранное число потоков; «Auto» — все логические процессоры.</summary>
    public static int Read(ComboBox box) => IsAuto(box)
        ? Environment.ProcessorCount
        : Math.Max(1, Convert.ToInt32(box.SelectedItem, CultureInfo.InvariantCulture));
}
