using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Controls;

public partial class Buddhabrot4DEditor : UserControl
{
    private bool _loading = true;
    public event EventHandler? SettingsChanged;
    public Buddhabrot4DEditor() { InitializeComponent(); Load(new()); }

    public Buddhabrot4DSettings Capture()
    {
        var result = new Buddhabrot4DSettings
        {
            SampleCount = Integer(SamplesBox), Seed = Integer(SeedBox),
            MinIterations = Integer(MinIterationsBox), MaxIterations = Integer(MaxIterationsBox),
            RedLimit = Integer(RedBox), GreenLimit = Integer(GreenBox), BlueLimit = Integer(BlueBox),
            Projection = (Buddhabrot4DProjection)Math.Max(0, ProjectionBox.SelectedIndex),
            ZrZi = Number(ZrZiBox), ZrCr = Number(ZrCrBox), ZrCi = Number(ZrCiBox),
            ZiCr = Number(ZiCrBox), ZiCi = Number(ZiCiBox), CrCi = Number(CrCiBox),
            Exposure = ExposureSlider.Value, Gamma = GammaSlider.Value,
            Density = DensitySlider.Value, Saturation = SaturationSlider.Value
        };
        result.Validate(); return result;
    }

    public void Load(Buddhabrot4DSettings settings)
    {
        _loading = true;
        SamplesBox.Text = settings.SampleCount.ToString(CultureInfo.InvariantCulture);
        SeedBox.Text = settings.Seed.ToString(CultureInfo.InvariantCulture);
        MinIterationsBox.Text = settings.MinIterations.ToString(CultureInfo.InvariantCulture);
        MaxIterationsBox.Text = settings.MaxIterations.ToString(CultureInfo.InvariantCulture);
        RedBox.Text = settings.RedLimit.ToString(CultureInfo.InvariantCulture);
        GreenBox.Text = settings.GreenLimit.ToString(CultureInfo.InvariantCulture);
        BlueBox.Text = settings.BlueLimit.ToString(CultureInfo.InvariantCulture);
        ProjectionBox.SelectedIndex = (int)settings.Projection;
        ZrZiSlider.Value = settings.ZrZi; ZrCrSlider.Value = settings.ZrCr; ZrCiSlider.Value = settings.ZrCi;
        ZiCrSlider.Value = settings.ZiCr; ZiCiSlider.Value = settings.ZiCi; CrCiSlider.Value = settings.CrCi;
        ExposureSlider.Maximum = 20;
        ExposureSlider.Value = settings.Exposure; GammaSlider.Value = settings.Gamma;
        DensitySlider.Value = settings.Density; SaturationSlider.Value = settings.Saturation;
        _loading = false;
    }

    public void SetCloudStyle(bool cloud) => DensitySlider.IsEnabled = cloud;
    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Reset_OnClick(object sender, RoutedEventArgs e)
    {
        // Reset is available even while another input is temporarily incomplete.
        _loading = true;
        ZrZiSlider.Value = ZrCrSlider.Value = ZrCiSlider.Value = 0;
        ZiCrSlider.Value = ZiCiSlider.Value = CrCiSlider.Value = 0;
        _loading = false;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private static int Integer(TextBox box) =>
        int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value : throw new ArgumentException("Введите целое число в настройках орбит.");
    private static double Number(TextBox box) =>
        double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value : throw new ArgumentException("Введите угол 4D-поворота числом.");
}
