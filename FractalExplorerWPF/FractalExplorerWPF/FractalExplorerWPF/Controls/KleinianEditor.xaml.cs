using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

public partial class KleinianEditor : UserControl
{
    private bool _loading = true;
    public event EventHandler? SettingsChanged;
    public event RoutedEventHandler? AtlasRequested;

    private void Atlas_OnClick(object sender, RoutedEventArgs e) => AtlasRequested?.Invoke(this, e);

    public KleinianEditor() { InitializeComponent(); _loading = false; }

    public KleinianSettings Capture()
    {
        NumericInput.Prepare(this);
        var settings = new KleinianSettings
        {
            Configuration = (KleinianConfiguration)ConfigurationBox.SelectedIndex,
            RadiusRatio = ReadDouble(RadiusBox, "Относительный радиус сфер", .65, 1),
            MinRadius = ReadDouble(MinRadiusBox, "Минимальный радиус деталей", .001, .05),
            Thickness = ReadDouble(ThicknessBox, "Утолщение множества", 0, .04)
        };
        settings.Validate();
        return settings;
    }

    public void Load(KleinianSettings settings)
    {
        settings.Validate();
        _loading = true;
        try
        {
            ConfigurationBox.SelectedIndex = (int)settings.Configuration;
            Set(RadiusSlider, RadiusBox, settings.RadiusRatio);
            Set(MinRadiusSlider, MinRadiusBox, settings.MinRadius);
            Set(ThicknessSlider, ThicknessBox, settings.Thickness);
            GeometryStatus.Text = "";
        }
        finally { _loading = false; }
        InitializeNumericInput();
    }

    private static void Set(Slider slider, TextBox box, double value)
    {
        slider.Value = value;
        box.SetCurrentValue(TextBox.TextProperty, value.ToString("R", CultureInfo.InvariantCulture));
    }

    public void OnFrameDisplayed(int balls, bool limited) => GeometryStatus.Text =
        $"Сфер приближения: {balls:N0}." + (limited
            ? " Достигнут предел детализации: часть ветвей осталась крупнее. Увеличьте минимальный радиус деталей."
            : "");

    private void Changed(object sender, EventArgs e)
    {
        if (_loading) return;
        GeometryStatus.Text = "";
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
