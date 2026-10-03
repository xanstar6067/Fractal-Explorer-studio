using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class DynamicSystemWindow
{
    private StackPanel? _iconPanel;
    private ComboBox? _iconPresetsBox, _iconPaletteBox, _iconDegreeBox;
    private CheckBox? _iconMirrorBox;
    private TextBlock? _iconStatus;
    private Button? _iconSearchButton, _iconUndoButton, _iconVariationButton;
    private readonly Dictionary<string, TextBox> _iconBoxes = [];
    private CancellationTokenSource? _iconSearchCts;
    private int _iconSearchVersion;
    private DynamicSystemState? _iconUndo;

    private bool IsSymmetricIcon => _kind == DynamicSystemKind.Attractors2D &&
        Attractor2DRenderer.ParseKind(_state.Attractor2DMode) == Attractor2DKind.SymmetricIcon;

    private void BuildIconPanel()
    {
        _iconPanel = new StackPanel { Visibility = Visibility.Collapsed };
        _iconPanel.Children.Add(new TextBlock { Text = "Готовые орнаменты" });
        _iconPresetsBox = new ComboBox { ItemsSource = SymmetricIconPresets.All, DisplayMemberPath = "Name" };
        _iconPresetsBox.SelectionChanged += (_, _) =>
        {
            if (_syncing || _iconPresetsBox.SelectedIndex < 0) return;
            CancelIconSearch();
            RememberIconUndo();
            SymmetricIconPresets.Apply(_state, _iconPresetsBox.SelectedIndex);
            FinishIconChange("Готовая форма загружена");
        };
        _iconPanel.Children.Add(_iconPresetsBox);
        _iconPanel.Children.Add(new TextBlock { Text = "Число лучей" });
        _iconDegreeBox = new ComboBox { ItemsSource = Enumerable.Range(3, 14) };
        _iconDegreeBox.SelectionChanged += (_, _) =>
        {
            if (_syncing || _iconDegreeBox.SelectedItem is not int degree) return;
            CancelIconSearch();
            _state.SymmetricIcon.Degree = degree;
            _state.PointOfInterestId = null;
            ClearIconPresetSelection();
            Schedule();
        };
        _iconPanel.Children.Add(_iconDegreeBox);
        _iconMirrorBox = new CheckBox { Content = "Зеркальная симметрия", Margin = new Thickness(0, 2, 0, 10) };
        _iconMirrorBox.Checked += MirrorChanged;
        _iconMirrorBox.Unchecked += MirrorChanged;
        _iconPanel.Children.Add(_iconMirrorBox);
        _iconPanel.Children.Add(new TextBlock { Text = "Палитра плотности" });
        _iconPaletteBox = new ComboBox { DisplayMemberPath = nameof(ChoiceOption.Display) };
        _iconPaletteBox.SelectionChanged += (_, _) =>
        {
            if (_syncing || _iconPaletteBox.SelectedItem is not ChoiceOption option) return;
            _state.PaletteName = option.Value;
            UpdateAttractorPresentation();
            Schedule();
        };
        _iconPanel.Children.Add(_iconPaletteBox);
        AddIconField(_iconPanel, "Поворот орнамента, °", nameof(SymmetricIconSettings.Rotation));
        var buttons = new StackPanel();
        _iconSearchButton = new Button { Content = "Найти новый орнамент" };
        _iconSearchButton.Click += (_, _) => StartIconSearch(false);
        _iconVariationButton = new Button { Content = "Вариация текущей формы" };
        _iconVariationButton.Click += (_, _) => StartIconSearch(true);
        _iconUndoButton = new Button { Content = "Вернуть предыдущую форму", IsEnabled = false };
        _iconUndoButton.Click += (_, _) =>
        {
            if (_iconUndo is null) return;
            CancelIconSearch();
            DynamicSystemState previous = _iconUndo;
            _iconUndo = null;
            LoadState(previous);
            _iconUndoButton.IsEnabled = false;
            if (_iconStatus is not null) _iconStatus.Text = "Предыдущая форма восстановлена";
        };
        buttons.Children.Add(_iconSearchButton);
        buttons.Children.Add(_iconVariationButton);
        buttons.Children.Add(_iconUndoButton);
        _iconPanel.Children.Add(buttons);
        _iconStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        _iconStatus.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SecondaryTextBrush");
        _iconPanel.Children.Add(_iconStatus);
        var advanced = new StackPanel();
        foreach (var field in new[] { ("λ · линейная часть", "Lambda"), ("α · радиальная часть", "Alpha"),
            ("β · угловой узор", "Beta"), ("γ · лепестки", "Gamma"), ("ω · закручивание", "Omega"),
            ("Размер кадра", "Span"), ("Следующее число генератора", "Seed") })
            AddIconField(advanced, field.Item1, field.Item2);
        var fit = new Button { Content = "Подобрать кадр по орбите" };
        fit.Click += (_, _) =>
        {
            try
            {
                DynamicSystemState snapshot = CaptureState("fit");
                SymmetricIconMap.Analysis view = SymmetricIconMap.Analyze(snapshot.SymmetricIcon,
                    CancellationToken.None, snapshot.X0, snapshot.Y0)
                    ?? throw new InvalidOperationException("Не удалось оценить орбиту. Измените параметры или выберите готовую форму.");
                _state.SymmetricIcon.Span = Math.Max(.05, view.Radius * 2.25);
                _state.CenterX = 0; _state.CenterY = 0; _state.Zoom = 1;
                FinishIconChange($"Кадр подобран · λ₁ ≈ {view.Lyapunov:F3}");
            }
            catch (InvalidOperationException ex) { _iconStatus.Text = ex.Message; }
        };
        advanced.Children.Add(fit);
        _iconPanel.Children.Add(new Expander { Header = "Точная настройка", Content = advanced });
        ParameterPanel.Children.Add(_iconPanel);
    }

    private void AddIconField(Panel parent, string label, string key)
    {
        parent.Children.Add(new TextBlock { Text = label });
        var box = new TextBox { Tag = key };
        NumericSpinner.SetIsEnabled(box, true);
        NumericSpinner.SetIsInteger(box, key == nameof(SymmetricIconSettings.Seed));
        box.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            CancelIconSearch();
            _state.PointOfInterestId = null;
            ClearIconPresetSelection();
            Schedule();
        };
        parent.Children.Add(box);
        _iconBoxes[key] = box;
    }

    private void MirrorChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || _iconMirrorBox is null) return;
        CancelIconSearch();
        _state.SymmetricIcon.Mirror = _iconMirrorBox.IsChecked == true;
        _iconBoxes[nameof(SymmetricIconSettings.Omega)].IsEnabled = !_state.SymmetricIcon.Mirror;
        _state.PointOfInterestId = null;
        ClearIconPresetSelection();
        Schedule();
    }

    private void ReadIconSettings()
    {
        SymmetricIconSettings settings = _state.SymmetricIcon.Clone();
        foreach ((string key, TextBox box) in _iconBoxes)
        {
            PropertyInfo property = typeof(SymmetricIconSettings).GetProperty(key)!;
            if (property.PropertyType == typeof(int))
            {
                if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
                    throw new InvalidOperationException("Число генератора должно быть целым и неотрицательным.");
                property.SetValue(settings, value);
            }
            else
            {
                if (!TryDouble(box.Text, out double value) || !double.IsFinite(value))
                    throw new InvalidOperationException($"Некорректный параметр орнамента: {key}");
                property.SetValue(settings, value);
            }
        }
        settings.Validate();
        _state.SymmetricIcon = settings;
    }

    private void SyncIconControls()
    {
        if (_iconPanel is null) return;
        foreach ((string key, TextBox box) in _iconBoxes)
        {
            object? value = typeof(SymmetricIconSettings).GetProperty(key)!.GetValue(_state.SymmetricIcon);
            box.Text = value is double number ? number.ToString("R", CultureInfo.InvariantCulture) : Format(value);
        }
        _iconDegreeBox!.SelectedItem = _state.SymmetricIcon.Degree;
        _iconMirrorBox!.IsChecked = _state.SymmetricIcon.Mirror;
        _iconBoxes[nameof(SymmetricIconSettings.Omega)].IsEnabled = !_state.SymmetricIcon.Mirror;
        _iconPaletteBox!.ItemsSource = new[] { new ChoiceOption("Один цвет", "") }
            .Concat(_palettes.Select(p => new ChoiceOption(p.Name, p.Name))).ToArray();
        _iconPaletteBox.SelectedItem = _iconPaletteBox.Items.OfType<ChoiceOption>()
            .FirstOrDefault(p => p.Value == _state.PaletteName);
        _iconPresetsBox!.SelectedItem = SymmetricIconPresets.All.FirstOrDefault(p =>
            p.Settings.Degree == _state.SymmetricIcon.Degree && p.Settings.Mirror == _state.SymmetricIcon.Mirror &&
            p.Settings.Lambda == _state.SymmetricIcon.Lambda && p.Settings.Alpha == _state.SymmetricIcon.Alpha &&
            p.Settings.Beta == _state.SymmetricIcon.Beta && p.Settings.Gamma == _state.SymmetricIcon.Gamma &&
            p.Settings.Omega == _state.SymmetricIcon.Omega);
    }

    private void ClearIconPresetSelection()
    {
        if (_iconPresetsBox is null) return;
        bool previous = _syncing;
        _syncing = true;
        _iconPresetsBox.SelectedIndex = -1;
        _syncing = previous;
    }

    private void RememberIconUndo()
    {
        try { _iconUndo = CaptureState("undo"); }
        catch (InvalidOperationException) { _iconUndo = _state.Clone(); }
        if (_iconUndoButton is not null) _iconUndoButton.IsEnabled = true;
    }

    private void FinishIconChange(string message)
    {
        _state.PointOfInterestId = null;
        SyncControls(); UpdateAttractorPresentation(); UpdateSwatches(); UpdatePreviewTransform();
        if (_iconStatus is not null) _iconStatus.Text = message;
        Schedule();
    }

    private async void StartIconSearch(bool variation)
    {
        if (_iconSearchCts is not null) { CancelIconSearch(); return; }
        DynamicSystemState snapshot;
        try { snapshot = CaptureState("search"); }
        catch (InvalidOperationException ex) { _iconStatus!.Text = ex.Message; return; }
        int seed = snapshot.SymmetricIcon.Seed;
        SymmetricIconSettings basis = snapshot.SymmetricIcon.Clone();
        if (!variation)
        {
            SymmetricIconPreset[] candidates = SymmetricIconPresets.All.Where(p => p.Settings.Degree == basis.Degree).ToArray();
            if (candidates.Length > 0)
            {
                SymmetricIconSettings preset = candidates[new Random(seed).Next(candidates.Length)].Settings.Clone();
                preset.Mirror = basis.Mirror;
                preset.Rotation = basis.Rotation;
                basis = preset;
            }
        }
        using var cts = new CancellationTokenSource();
        _iconSearchCts = cts;
        int version = ++_iconSearchVersion;
        _iconSearchButton!.Content = "Остановить поиск";
        _iconVariationButton!.IsEnabled = false;
        _iconStatus!.Text = "Ищу ограниченный хаотический орнамент…";
        try
        {
            var progress = new Progress<int>(p =>
            {
                if (version == _iconSearchVersion) _iconStatus.Text = $"Поиск: {p}% попыток";
            });
            SymmetricIconMap.SearchResult result = await Task.Run(() => SymmetricIconMap.Search(basis, seed, cts.Token, progress));
            if (version != _iconSearchVersion || cts.IsCancellationRequested) return;
            _iconUndo = snapshot;
            _iconUndoButton!.IsEnabled = true;
            _state.SymmetricIcon = result.Settings;
            _state.SymmetricIcon.Seed = seed == int.MaxValue ? 0 : seed + 1;
            _state.X0 = .01; _state.Y0 = .01;
            _state.CenterX = 0; _state.CenterY = 0; _state.Zoom = 1;
            FinishIconChange($"Найдена форма за {result.Attempts} попыток · λ₁ ≈ {result.Analysis.Lyapunov:F3} · число {seed}");
        }
        catch (OperationCanceledException) { if (version == _iconSearchVersion) _iconStatus.Text = "Поиск остановлен"; }
        catch (InvalidOperationException ex) { if (version == _iconSearchVersion) _iconStatus.Text = ex.Message; }
        finally
        {
            if (ReferenceEquals(_iconSearchCts, cts)) _iconSearchCts = null;
            _iconSearchButton.Content = "Найти новый орнамент";
            _iconVariationButton.IsEnabled = true;
        }
    }

    private void CancelIconSearch()
    {
        ++_iconSearchVersion;
        _iconSearchCts?.Cancel();
        if (_iconSearchCts is not null && _iconStatus is not null) _iconStatus.Text = "Поиск остановлен";
    }

    private static DynamicSystemState CreateIconPoint(int index)
    {
        DynamicSystemState state = DynamicSystemState.CreateDefault(DynamicSystemKind.Attractors2D);
        SymmetricIconPresets.Apply(state, index);
        state.SaveName = $"Symmetric Icons — {SymmetricIconPresets.All[index].Name}";
        state.PointOfInterestId = $"symmetric_icon_{index}";
        state.Timestamp = DateTime.MinValue;
        return state;
    }
}
