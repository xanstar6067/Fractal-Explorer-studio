using System.Globalization;
using System.Windows.Controls;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

public partial class AntoineEditor : UserControl
{
    private bool _loading = true;
    public event EventHandler? SettingsChanged;

    public AntoineEditor()
    {
        InitializeComponent();
        LinksBox.ItemsSource = Enumerable.Range(12, 13).Select(n => n * 2).ToArray();
        LinksBox.SelectedIndex = 0;
        _loading = false;
    }

    public AntoineSettings Capture()
    {
        NumericInput.Prepare(this);
        var settings = new AntoineSettings
        {
            Links = LinksBox.SelectedItem is int links ? links : 24,
            ScaleFactor = ReadDouble(ScaleBox, "Масштаб звеньев", 1.35, 1.5),
            Thickness = ReadDouble(ThicknessBox, "Толщина звеньев", 0, 1),
            Tilt = ReadDouble(TiltBox, "Наклон звеньев", 60, 90)
        };
        settings.Validate();
        return settings;
    }

    public void Load(AntoineSettings settings)
    {
        settings.Validate();
        _loading = true;
        try
        {
            LinksBox.SelectedItem = settings.Links;
            Set(ScaleSlider, ScaleBox, settings.ScaleFactor);
            Set(ThicknessSlider, ThicknessBox, settings.Thickness);
            Set(TiltSlider, TiltBox, settings.Tilt);
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

    public void OnFrameDisplayed(int rings) => GeometryStatus.Text = $"Колец в поколении: {rings:N0}.";

    private void Changed(object sender, EventArgs e)
    {
        if (_loading) return;
        GeometryStatus.Text = "";
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
