using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class DynamicSystemWindow
{
    private ComboBox? _hopalongPresetsBox, _hopalongPaletteBox;
    private Button? _hopalongSearchButton, _hopalongVariationButton, _hopalongBackButton, _hopalongFitButton;
    private readonly Dictionary<string, TextBox> _hopalongBoxes = [];
    private CancellationTokenSource? _hopalongSearchCts;
    private int _hopalongRevision;
    private DynamicSystemState? _previousHopalong;
    private double _renderedHopalongSpan = 1;

    private void BuildHopalongPanel()
    {
        Heading.Text = "Hopalong Мартина";
        var formula = new TextBlock
        {
            Text = "x′ = y − sign(x)·√|b·x − c|\ny′ = a − x",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        };
        formula.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SecondaryTextBrush");
        ParameterPanel.Children.Add(formula);
        ParameterPanel.Children.Add(new TextBlock { Text = "Готовые формы" });
        _hopalongPresetsBox = new ComboBox { ItemsSource = HopalongPresets.All, DisplayMemberPath = "Name" };
        _hopalongPresetsBox.SelectionChanged += (_, _) =>
        {
            if (_syncing || _hopalongPresetsBox.SelectedIndex < 0) return;
            CancelHopalongSearch(); _cts?.Cancel(); RememberHopalong();
            HopalongPresets.Apply(_state, _hopalongPresetsBox.SelectedIndex);
            RefreshHopalong();
        };
        ParameterPanel.Children.Add(_hopalongPresetsBox);

        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new()); buttons.ColumnDefinitions.Add(new());
        _hopalongSearchButton = new Button { Content = "Новая форма", Margin = new Thickness(0, 4, 3, 4), Padding = new Thickness(3) };
        _hopalongVariationButton = new Button { Content = "Вариация", Margin = new Thickness(3, 4, 0, 4), Padding = new Thickness(3) };
        _hopalongSearchButton.Click += async (_, _) => await FindHopalongAsync(false);
        _hopalongVariationButton.Click += async (_, _) => await FindHopalongAsync(true);
        buttons.Children.Add(_hopalongSearchButton); buttons.Children.Add(_hopalongVariationButton);
        Grid.SetColumn(_hopalongVariationButton, 1); ParameterPanel.Children.Add(buttons);
        _hopalongBackButton = new Button { Content = "Вернуть предыдущий вид", IsEnabled = false };
        _hopalongBackButton.Click += (_, _) => RestorePreviousHopalong();
        ParameterPanel.Children.Add(_hopalongBackButton);

        AddHopalongField("Параметр a", nameof(HopalongSettings.A));
        AddHopalongField("Параметр b", nameof(HopalongSettings.B));
        AddHopalongField("Параметр c", nameof(HopalongSettings.C));
        AddHopalongField("Начальная точка x₀", nameof(HopalongSettings.StartX));
        AddHopalongField("Начальная точка y₀", nameof(HopalongSettings.StartY));
        AddHopalongField("Поворот · градусы", nameof(HopalongSettings.Rotation));
        AddHopalongField("Размер кадра", nameof(HopalongSettings.Span));
        _hopalongFitButton = new Button { Content = "Подобрать кадр по орбите" };
        _hopalongFitButton.Click += async (_, _) => await FitHopalongAsync();
        ParameterPanel.Children.Add(_hopalongFitButton);
        var explanation = new TextBlock
        {
            Text = "Начальная точка определяет рисунок. Цвет показывает плотность одной непрерывной орбиты. Подбор кадра охватывает её основную часть; редкие дальние точки могут остаться за краем.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10)
        };
        explanation.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SecondaryTextBrush");
        ParameterPanel.Children.Add(explanation);
        ParameterPanel.Children.Add(new TextBlock { Text = "Палитра плотности" });
        _hopalongPaletteBox = new ComboBox { DisplayMemberPath = nameof(ChoiceOption.Display) };
        _hopalongPaletteBox.SelectionChanged += (_, _) =>
        {
            if (_syncing || _hopalongPaletteBox.SelectedItem is not ChoiceOption option) return;
            CancelHopalongSearch(); _cts?.Cancel(); _state.PaletteName = option.Value;
            UpdateHopalongPresentation(); Schedule();
        };
        ParameterPanel.Children.Add(_hopalongPaletteBox);
    }

    private void AddHopalongField(string label, string key)
    {
        ParameterPanel.Children.Add(new TextBlock { Text = label });
        var box = new TextBox { Tag = key };
        NumericSpinner.SetIsEnabled(box, true);
        box.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            CancelHopalongSearch(); _cts?.Cancel(); _state.PointOfInterestId = null;
            bool wasSyncing = _syncing; _syncing = true;
            _hopalongPresetsBox!.SelectedIndex = -1; _syncing = wasSyncing;
            Schedule();
        };
        _hopalongBoxes[key] = box; ParameterPanel.Children.Add(box);
    }

    private void ReadHopalongSettings()
    {
        var settings = _state.Hopalong.Clone();
        foreach ((string key, TextBox box) in _hopalongBoxes)
        {
            if (!TryDouble(box.Text, out double value) || !double.IsFinite(value))
                throw new InvalidOperationException("Параметры Hopalong должны быть конечными числами.");
            typeof(HopalongSettings).GetProperty(key)!.SetValue(settings, value);
        }
        settings.Validate();
        if (_state.Iterations is < 1 or > 100_000_000 || _state.DensityGamma is < .05 or > 8)
            throw new InvalidOperationException("Число точек: 1–100 млн; гамма плотности: 0,05–8.");
        _state.Hopalong = settings;
    }

    private void SyncHopalongControls()
    {
        if (_hopalongPresetsBox is null) return;
        foreach ((string key, TextBox box) in _hopalongBoxes)
            box.Text = ((double)typeof(HopalongSettings).GetProperty(key)!.GetValue(_state.Hopalong)!).ToString("R", CultureInfo.InvariantCulture);
        _hopalongPresetsBox.SelectedItem = HopalongPresets.All.FirstOrDefault(p => "hopalong_" + p.Id == _state.PointOfInterestId);
        _hopalongPaletteBox!.ItemsSource = new[] { new ChoiceOption("Один цвет", "") }
            .Concat(_palettes.Select(p => new ChoiceOption(p.Name, p.Name))).ToArray();
        _hopalongPaletteBox.SelectedItem = _hopalongPaletteBox.Items.OfType<ChoiceOption>()
            .FirstOrDefault(p => p.Value == _state.PaletteName);
        UpdateHopalongPresentation();
    }

    private void UpdateHopalongPresentation()
    {
        if (_hopalongPaletteBox is null) return;
        FractalColorPanel.Visibility = ActivePalette is null ? Visibility.Visible : Visibility.Collapsed;
        _hopalongBackButton!.IsEnabled = _previousHopalong is not null;
    }

    private void RefreshHopalong()
    {
        SyncControls(); UpdateSwatches(); UpdatePreviewTransform(); Schedule();
    }

    private void RememberHopalong()
    {
        try { _previousHopalong = CaptureState("previous"); }
        catch (InvalidOperationException) { _previousHopalong = _state.Clone(); }
    }

    private void RestorePreviousHopalong()
    {
        if (_previousHopalong is null) return;
        DynamicSystemState previous = _previousHopalong;
        RememberHopalong(); LoadState(previous);
    }

    private void CancelHopalongSearch()
    {
        _hopalongRevision++;
        if (_hopalongSearchCts is not null) { _hopalongSearchCts.Cancel(); StatusText.Text = "Подбор остановлен"; }
    }

    private async Task FindHopalongAsync(bool variation) => await AnalyzeHopalongAsync(variation, false);
    private async Task FitHopalongAsync() => await AnalyzeHopalongAsync(false, true);

    private async Task AnalyzeHopalongAsync(bool variation, bool fit)
    {
        if (_hopalongSearchCts is not null) { CancelHopalongSearch(); return; }
        DynamicSystemState before;
        try { before = CaptureState("before"); }
        catch (InvalidOperationException exception) { StatusText.Text = exception.Message; return; }
        _timer.Stop(); _cts?.Cancel();
        using var cts = new CancellationTokenSource();
        _hopalongSearchCts = cts;
        int revision = ++_hopalongRevision;
        _hopalongSearchButton!.Content = "Остановить";
        _hopalongVariationButton!.IsEnabled = _hopalongFitButton!.IsEnabled = false;
        StatusText.Text = fit ? "Подбираю кадр по орбите…" : "Подбираю новую форму…";
        try
        {
            HopalongMap.SearchResult result = await Task.Run(() => fit
                ? new HopalongMap.SearchResult(before.Hopalong.Clone(), HopalongMap.Analyze(before.Hopalong, cts.Token), 0)
                : HopalongMap.Search(Random.Shared.Next(), variation ? before.Hopalong : null, cts.Token));
            if (cts.IsCancellationRequested || revision != _hopalongRevision) return;
            _previousHopalong = before;
            _state.Hopalong = result.Settings;
            _state.Hopalong.Span = result.View.Span;
            _state.CenterX = result.View.CenterX; _state.CenterY = result.View.CenterY; _state.Zoom = 1;
            if (!fit) _state.PointOfInterestId = null;
            RefreshHopalong();
            StatusText.Text = fit ? "Кадр подобран по основной части орбиты." : $"Новая форма найдена за {result.Attempts} попыток.";
        }
        catch (OperationCanceledException) { if (revision == _hopalongRevision) StatusText.Text = "Подбор остановлен"; }
        catch (InvalidOperationException exception) { if (revision == _hopalongRevision) StatusText.Text = exception.Message; }
        finally
        {
            _hopalongSearchCts = null;
            _hopalongSearchButton.Content = "Новая форма";
            _hopalongVariationButton.IsEnabled = _hopalongFitButton.IsEnabled = true;
        }
    }
}
