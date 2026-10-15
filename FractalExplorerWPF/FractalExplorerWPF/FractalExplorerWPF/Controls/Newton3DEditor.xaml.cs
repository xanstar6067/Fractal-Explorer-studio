using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

public partial class Newton3DEditor : UserControl
{
    private bool _loading = true;
    public event EventHandler? SettingsChanged;

    public Newton3DEditor()
    {
        InitializeComponent();
        DegreeBox.ItemsSource = Enumerable.Range(3, 6).ToArray();
        SizeBox.ItemsSource = new[] { 32, 64, 96, 128, 192 };
        Load(new());
    }

    public Newton3DSettings Capture()
    {
        var settings = new Newton3DSettings {
            Degree = DegreeBox.SelectedItem is int degree ? degree : 3,
            Size = SizeBox.SelectedItem is int size ? size : 128,
            MaxIterations = ReadInt(IterationsBox, "Итерации Ньютона", 16, 128),
            Extent = ReadDouble(ExtentBox, "Полуразмер области", .2, 4),
            SliceW = ReadDouble(SliceWBox, "Координата w", -2, 2),
            Relaxation = ReadDouble(RelaxationBox, "Релаксация", .2, 1.8),
            BoundaryIterations = ReadInt(BoundaryBox, "Порог границы", 2, 127),
            Region = (Newton3DRegion)Math.Max(0, RegionBox.SelectedIndex),
            PlusRoot = Math.Max(0, PlusRootBox.SelectedIndex),
            MinusRoot = Math.Max(0, MinusRootBox.SelectedIndex),
            CutAxis = Math.Max(0, CutBox.SelectedIndex), CutPosition = CutSlider.Value,
            SliceOnly = SliceBox.IsChecked == true
        };
        settings.Validate();
        return settings;
    }

    public void Load(Newton3DSettings settings)
    {
        settings.Validate();
        _loading = true;
        try
        {
            DegreeBox.SelectedItem = settings.Degree;
            SizeBox.SelectedItem = settings.Size;
            UpdateRoots(settings.PlusRoot, settings.MinusRoot);
            IterationsBox.Text = settings.MaxIterations.ToString(CultureInfo.InvariantCulture);
            ExtentBox.Text = Format(settings.Extent);
            SliceWBox.Text = Format(settings.SliceW);
            RelaxationBox.Text = Format(settings.Relaxation);
            BoundaryBox.Text = settings.BoundaryIterations.ToString(CultureInfo.InvariantCulture);
            RegionBox.SelectedIndex = (int)settings.Region;
            CutBox.SelectedIndex = settings.CutAxis;
            CutSlider.Value = settings.CutPosition;
            SliceBox.IsChecked = settings.SliceOnly;
            UpdateView();
        }
        finally { _loading = false; }
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private void UpdateRoots(int plus, int minus)
    {
        int degree = DegreeBox.SelectedItem is int n ? n : 3;
        var roots = Enumerable.Range(1, degree).Select(i => $"{i} · {360.0*(i-1)/degree:G4}°").ToArray();
        PlusRootBox.ItemsSource = roots;
        MinusRootBox.ItemsSource = roots;
        PlusRootBox.SelectedIndex = Math.Clamp(plus, 0, degree - 1);
        MinusRootBox.SelectedIndex = Math.Clamp(minus, 0, degree - 1);
    }

    private void UpdateView()
    {
        BoundaryPanel.Visibility = RegionBox.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        RootsPanel.Visibility = RegionBox.SelectedIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        MinusRootPanel.Visibility = RegionBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (SliceBox.IsChecked == true && CutBox.SelectedIndex <= 0) CutBox.SelectedIndex = 3;
        CutSlider.IsEnabled = CutBox.SelectedIndex > 0;
        if (CutBox.SelectedIndex <= 0) CutLabel.Text = "";
        else if (double.TryParse(ExtentBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double extent))
            CutLabel.Text = $"{(char)('X' + CutBox.SelectedIndex - 1)} = {CutSlider.Value * extent:G5}";
    }

    private void Changed(object sender, EventArgs e)
    {
        if (_loading) return;
        _loading = true;
        try
        {
            if (ReferenceEquals(sender, DegreeBox)) UpdateRoots(PlusRootBox.SelectedIndex, MinusRootBox.SelectedIndex);
            UpdateView();
        }
        finally { _loading = false; }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
