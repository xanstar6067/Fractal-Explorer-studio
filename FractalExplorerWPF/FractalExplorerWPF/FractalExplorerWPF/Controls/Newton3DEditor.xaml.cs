using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

public partial class Newton3DEditor : UserControl
{
    private bool _loading = true;
    private string? _appliedFormula;
    private Newton3DRootCatalog? _appliedRoots;
    private double _appliedRadius = 8;
    private CancellationTokenSource? _formulaCts;
    public event EventHandler? SettingsChanged;

    public Newton3DEditor()
    {
        InitializeComponent();
        DegreeBox.ItemsSource = Enumerable.Range(3, 6).ToArray();
        SizeBox.ItemsSource = new[] { 32, 64, 96, 128, 192 };
        FormulaExamplesBox.ItemsSource = Newton3DFormulas.Examples;
        Unloaded += (_, _) => _formulaCts?.Cancel();
        Load(new());
    }

    public Newton3DSettings Capture()
    {
        NumericInput.Prepare(this);
        var settings = new Newton3DSettings {
            Degree = DegreeBox.SelectedItem is int degree ? degree : 3,
            Formula = _appliedFormula, Roots = _appliedRoots, RootSearchRadius = _appliedRadius,
            Size = SizeBox.SelectedItem is int size ? size : 128,
            MaxIterations = ReadInt(IterationsBox, "Итерации Ньютона", 16, 128),
            Extent = ReadDouble(ExtentBox, "Полуразмер области", .2, 4),
            SliceW = ReadDouble(SliceWBox, "Координата w", -2, 2),
            Relaxation = ReadDouble(RelaxationBox, "Релаксация", .2, 1.8),
            BoundaryIterations = ReadInt(BoundaryBox, "Порог границы", 2, 127),
            Region = (Newton3DRegion)Math.Max(0, RegionBox.SelectedIndex),
            PlusRoot = Math.Max(0, PlusRootBox.SelectedIndex), MinusRoot = Math.Max(0, MinusRootBox.SelectedIndex),
            CutAxis = Math.Max(0, CutBox.SelectedIndex), CutPosition = CutSlider.Value,
            SliceOnly = SliceBox.IsChecked == true
        };
        settings.Validate();
        return settings;
    }

    public void Load(Newton3DSettings settings)
    {
        settings = Newton3DFormula.Prepare(settings);
        _formulaCts?.Cancel(); _formulaCts = null;
        _loading = true;
        try
        {
            _appliedFormula = settings.Formula; _appliedRoots = settings.Roots; _appliedRadius = settings.RootSearchRadius;
            DegreeBox.SelectedItem = settings.Degree; SizeBox.SelectedItem = settings.Size;
            FormulaExamplesBox.SelectedItem = Newton3DFormulas.Examples.FirstOrDefault(example => example.Formula == settings.Expression);
            FormulaBox.Text = settings.Expression; RootRadiusBox.Text = Format(_appliedRadius);
            UpdateRoots(settings.PlusRoot, settings.MinusRoot);
            IterationsBox.Text = settings.MaxIterations.ToString(CultureInfo.InvariantCulture);
            ExtentBox.Text = Format(settings.Extent); SliceWBox.Text = Format(settings.SliceW);
            RelaxationBox.Text = Format(settings.Relaxation);
            BoundaryBox.Text = settings.BoundaryIterations.ToString(CultureInfo.InvariantCulture);
            RegionBox.SelectedIndex = (int)settings.Region;
            CutBox.SelectedIndex = settings.CutAxis; CutSlider.Value = settings.CutPosition;
            SliceBox.IsChecked = settings.SliceOnly;
            ApplyFormulaButton.IsEnabled = RandomFormulaButton.IsEnabled = true;
            CancelFormulaButton.Visibility = Visibility.Collapsed;
            AppliedMessage(); UpdateView();
        }
        finally { _loading = false; }
        InitializeNumericInput();
    }

    internal async Task<bool> ApplyFormulaAsync()
    {
        NumericInput.CommitAll(this);
        _formulaCts?.Cancel();
        var cts = new CancellationTokenSource(); _formulaCts = cts;
        var token = cts.Token;
        string text = FormulaBox.Text.Trim(), radiusText = RootRadiusBox.Text;
        ApplyFormulaButton.IsEnabled = RandomFormulaButton.IsEnabled = false;
        CancelFormulaButton.Visibility = Visibility.Visible;
        FormulaStatus.Text = "Проверка формулы, поиск корней и подготовка программы…";
        try
        {
            var candidate = Capture() with { Formula = text, Roots = null,
                RootSearchRadius = ReadDouble(RootRadiusBox, "Радиус поиска корней", 1, 64) };
            var prepared = await Task.Run(() => Newton3DComputeShader.Prepare(candidate, token), token);
            if (token.IsCancellationRequested || !ReferenceEquals(_formulaCts, cts) ||
                text != FormulaBox.Text.Trim() || radiusText != RootRadiusBox.Text) return false;
            _loading = true;
            try
            {
                _appliedFormula = prepared.Formula; _appliedRoots = prepared.Roots; _appliedRadius = prepared.RootSearchRadius;
                FormulaExamplesBox.SelectedItem = Newton3DFormulas.Examples.FirstOrDefault(example => example.Formula == text);
                UpdateRoots(prepared.PlusRoot, prepared.MinusRoot);
                AppliedMessage();
            }
            finally { _loading = false; }
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (ReferenceEquals(_formulaCts, cts) && !token.IsCancellationRequested)
                FormulaStatus.Text = $"{exception.Message} Прежняя формула сохранена.";
            return false;
        }
        finally
        {
            if (ReferenceEquals(_formulaCts, cts))
            {
                _formulaCts = null;
                ApplyFormulaButton.IsEnabled = RandomFormulaButton.IsEnabled = true;
                CancelFormulaButton.Visibility = Visibility.Collapsed;
            }
            cts.Dispose();
        }
    }

    private async void ApplyFormulaClicked(object sender, RoutedEventArgs e) => await ApplyFormulaAsync();
    private void CancelFormulaClicked(object sender, RoutedEventArgs e)
    {
        _formulaCts?.Cancel();
        FormulaStatus.Text = "Применение отменено. Прежняя формула сохранена.";
    }
    private async void RandomFormulaClicked(object sender, RoutedEventArgs e)
    {
        FormulaExamplesBox.SelectedIndex = -1;
        FormulaBox.Text = Newton3DFormulas.RandomFormula(Random.Shared);
        await ApplyFormulaAsync();
    }
    private async void ExampleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FormulaExamplesBox.SelectedItem is not Newton3DFormulaExample example) return;
        FormulaBox.Text = example.Formula;
        await ApplyFormulaAsync();
    }
    private void FormulaDraftChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _formulaCts?.Cancel();
        FormulaStatus.Text = "Изменения не применены. Нажмите «Применить формулу».";
    }
    private void AppliedMessage() => FormulaStatus.Text =
        $"Применено: {_appliedFormula ?? $"z^{DegreeBox.SelectedItem}-1"}. Корней: {_appliedRoots?.Values.Count ?? (int)(DegreeBox.SelectedItem ?? 3)}." +
        (_appliedFormula is null ? "" : " Для неполиномиальных функций корни вне радиуса поиска не окрашиваются.");

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private void UpdateRoots(int plus, int minus)
    {
        int degree = DegreeBox.SelectedItem is int n ? n : 3;
        var roots = _appliedRoots?.Values.Select((r, i) => $"{i + 1} · {r.Real:G4}{r.Imaginary:+0.####;-0.####;+0}i").ToArray() ??
            Enumerable.Range(1, degree).Select(i => $"{i} · {360.0 * (i - 1) / degree:G4}°").ToArray();
        PlusRootBox.ItemsSource = roots; MinusRootBox.ItemsSource = roots;
        PlusRootBox.SelectedIndex = Math.Clamp(plus, 0, roots.Length - 1);
        MinusRootBox.SelectedIndex = Math.Clamp(minus, 0, roots.Length - 1);
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
            if (ReferenceEquals(sender, DegreeBox))
            {
                _formulaCts?.Cancel();
                _appliedFormula = null; _appliedRoots = null;
                FormulaBox.Text = $"z^{DegreeBox.SelectedItem}-1";
                FormulaExamplesBox.SelectedIndex = -1;
                UpdateRoots(PlusRootBox.SelectedIndex, MinusRootBox.SelectedIndex);
                AppliedMessage();
            }
            UpdateView();
        }
        finally { _loading = false; }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
