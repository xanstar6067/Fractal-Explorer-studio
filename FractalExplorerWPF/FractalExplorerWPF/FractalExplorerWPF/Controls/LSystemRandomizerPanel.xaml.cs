using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Views;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Controls;

public partial class LSystemRandomizerPanel : UserControl
{
    private bool _isSpatial;
    public bool IsSpatial
    {
        get => _isSpatial;
        set
        {
            _isSpatial = value;
            if (SpatialSettings != null) SpatialSettings.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    public Func<LSystemDefinition>? CapturePlanar { get; set; }
    public Func<LSystem3DSettings>? CaptureSpatial { get; set; }
    public event EventHandler<LSystemRandomizationResult>? Generated;
    public event EventHandler? UndoRequested;
    private CancellationTokenSource? _generation;
    private Window? _window;
    public LSystemRandomizerPanel()
    {
        InitializeComponent();
        Unloaded += (_, _) => CancelWork();
    }
    public Window OpenWindow(Window owner)
    {
        if (_window != null) { _window.Activate(); return _window; }
        SpatialSettings.Visibility = IsSpatial ? Visibility.Visible : Visibility.Collapsed;
        _window = IsSpatial ? new LSystem3DRandomizerWindow(this) : new LSystemRandomizerWindow(this);
        _window.Owner = owner;
        var window = _window;
        window.Closed += (_, _) => { CancelWork(); window.Content = null; _window = null; };
        _window.Show();
        return _window;
    }
    private void Family_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CurveSettings == null || BranchSettings == null) return;
        bool curve = FamilyBox.SelectedIndex == (int)LSystemShapeFamily.Curves;
        CurveSettings.Visibility = curve ? Visibility.Visible : Visibility.Collapsed;
        BranchSettings.Visibility = curve ? Visibility.Collapsed : Visibility.Visible;
    }
    private static double ReadNumber(TextBox box) =>
        double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value : throw new InvalidOperationException("Введите число в диапазонах углов.");
    public void SetUndoAvailable(bool available) => UndoButton.IsEnabled = available;
    public void CancelWork() => _generation?.Cancel();
    private void New_OnClick(object sender, RoutedEventArgs e) => _ = GenerateAsync(false);
    private void Nearby_OnClick(object sender, RoutedEventArgs e) => _ = GenerateAsync(true);
    private void Cancel_OnClick(object sender, RoutedEventArgs e) => CancelWork();
    private void Undo_OnClick(object sender, RoutedEventArgs e) { CancelWork(); UndoRequested?.Invoke(this, EventArgs.Empty); }

    private async Task GenerateAsync(bool nearby)
    {
        _generation?.Cancel();
        using var cts = new CancellationTokenSource();
        _generation = cts;
        try
        {
            if (FreshSeedBox.IsChecked == true) SeedBox.Text = Random.Shared.Next(int.MaxValue).ToString(CultureInfo.InvariantCulture);
            if (!int.TryParse(SeedBox.Text, out int seed) || seed < 0)
                throw new InvalidOperationException("Случайное число: целое от 0 до 2 147 483 647.");
            var options = new LSystemRandomizationSettings
            {
                Family = (LSystemShapeFamily)Math.Max(0, FamilyBox.SelectedIndex),
                Branching = (int)BranchSlider.Value, Detail = (int)DetailSlider.Value,
                Variation = VariationSlider.Value / 100, Symmetric = RegularBox.IsChecked == true, Seed = seed,
                CurveKind = (LSystemCurveKind)Math.Max(0, CurveBox.SelectedIndex),
                RuleComplexity = (int)ComplexitySlider.Value, StemLength = (int)StemSlider.Value,
                AngleMinimum = ReadNumber(AngleMinBox), AngleMaximum = ReadNumber(AngleMaxBox),
                PitchMinimum = ReadNumber(PitchMinBox), PitchMaximum = ReadNumber(PitchMaxBox),
                RollMinimum = ReadNumber(RollMinBox), RollMaximum = ReadNumber(RollMaxBox),
                Radius = RadiusSlider.Value, BranchTaper = TaperSlider.Value / 100, StepDecay = StepSlider.Value / 100
            };
            options.Validate();
            var planar = IsSpatial ? null : CapturePlanar?.Invoke()?.Clone() ?? new LSystemDefinition();
            var spatial = IsSpatial ? CaptureSpatial?.Invoke() ?? new LSystem3DSettings() : null;
            NewButton.IsEnabled = NearbyButton.IsEnabled = false; CancelButton.IsEnabled = true;
            ResultText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SecondaryTextBrush");
            ResultText.Text = "Собираем правила и проверяем форму…";
            var result = await Task.Run(() => IsSpatial
                ? LSystemRandomizer.Create3D(options, spatial!, nearby, cts.Token)
                : LSystemRandomizer.Create2D(options, planar!, nearby, cts.Token), cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            Generated?.Invoke(this, result);
            ResultText.Text = $"{result.Description}\n{result.Segments:N0} сегментов. Можно менять правила вручную.";
        }
        catch (OperationCanceledException) { ResultText.Text = "Генерация отменена. Предыдущая форма сохранена."; }
        catch (Exception ex)
        { ResultText.Text = ex.Message; ResultText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.DangerBrush"); }
        finally
        {
            if (ReferenceEquals(_generation, cts))
            { _generation = null; NewButton.IsEnabled = NearbyButton.IsEnabled = true; CancelButton.IsEnabled = false; }
        }
    }
}
