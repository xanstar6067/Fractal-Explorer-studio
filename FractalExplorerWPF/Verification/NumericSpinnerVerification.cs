using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Theming;

internal static partial class Program
{
    private static void VerifyNumericSpinners()
    {
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/FractalExplorerWPF;component/Theming/ThemeStyles.xaml", UriKind.Relative)
        });
        ThemeManager.Initialize(Application.Current);
        var box = new TextBox { Width = 190, Style = (Style)Application.Current.FindResource(typeof(TextBox)) };
        var host = new Window { Content = box, Width = 240, Height = 100, ShowInTaskbar = false };
        NumericSpinner.SetIsEnabled(box, true);
        box.ApplyTemplate();
        box.Measure(new Size(190, 40));
        box.Arrange(new Rect(0, 0, 190, 40));
        var up = (RepeatButton)box.Template.FindName("PART_Increase", box);
        var down = (RepeatButton)box.Template.FindName("PART_Decrease", box);
        var spinner = (Grid)box.Template.FindName("Spinner", box);
        if (spinner.Visibility != Visibility.Visible) throw new Exception("Numeric arrows are hidden.");

        void Click(string input, string expected, bool integer = false, bool decrease = false)
        {
            NumericSpinner.SetIsInteger(box, integer);
            box.Text = input;
            (decrease ? down : up).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (box.Text != expected) throw new Exception($"Spinner: {input} -> {box.Text}, expected {expected}.");
        }

        foreach (string culture in new[] { "ru-RU", "en-US" })
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Click("500", "501", true);
                Click("0", "-1", true, true);
                Click("0.75", "0.85");
                Click("0,75", "0,65", decrease: true);
                Click("0.001", "0.0011");
                Click("1e1000", "1.1e1000");
                Click("1e-1000", "0.9e-1000", decrease: true);
                Click("-0.123456789012345678901234567890123456789", "-0.023456789012345678901234567890123456789");
                Click("0.1", "0", decrease: true);
                Click("z^2+1", "z^2+1");
                Click("", "");
                Click("1e", "1e");
                Click("NaN", "NaN");
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
        NumericSpinner.SetIntegerStep(box, 2);
        Click("501", "503", true);
        NumericSpinner.SetIntegerStep(box, 1);
        NumericSpinner.SetIsInteger(box, false);
        box.Text = "1";
        box.IsReadOnly = true;
        up.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (box.Text != "1" || spinner.Visibility != Visibility.Collapsed) throw new Exception("Read-only spinner is active.");
        box.IsReadOnly = false;
        box.IsEnabled = false;
        up.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (box.Text != "1") throw new Exception("Disabled spinner changed text.");
        box.IsEnabled = true;

        var source = new SpinnerBindingSource { Value = "2" };
        box.SetBinding(TextBox.TextProperty, new Binding(nameof(SpinnerBindingSource.Value)) { Source = source, Mode = BindingMode.TwoWay });
        up.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (source.Value != "2.1" || box.GetBindingExpression(TextBox.TextProperty) is null)
            throw new Exception("Spinner broke or failed to commit its binding.");

        Color? firstColor = null;
        bool changed = false;
        foreach (var theme in ThemeManager.GetAllThemes())
        {
            ThemeManager.SetTheme(theme.Id);
            box.UpdateLayout();
            Color actual = ((SolidColorBrush)up.Background).Color;
            Color expected = ((SolidColorBrush)Application.Current.FindResource("Theme.ControlBackgroundAltBrush")).Color;
            if (actual != expected) throw new Exception($"Spinner does not follow theme {theme.Id}.");
            if (firstColor.HasValue && actual != firstColor) changed = true;
            firstColor ??= actual;
        }
        if (!changed) throw new Exception("Theme switch did not change spinner colors.");
        NumericSpinner.SetIsEnabled(box, false);
        if (spinner.Visibility != Visibility.Collapsed) throw new Exception("Plain text field has arrows.");
        host.Close();
        Console.WriteLine("PASS (numeric): clicks, integers, fractions, cultures, exact digits, 1e1000, binding, disabled/read-only and all themes.");
    }

    public sealed class SpinnerBindingSource
    {
        public string Value { get; set; } = "";
    }
}
