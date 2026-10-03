using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class DynamicSystemWindow
{
    private ComboBox? _popcornPresetsBox, _popcornModeBox, _popcornPaletteBox;
    private readonly Dictionary<string, TextBox> _popcornBoxes = [];
    private readonly Dictionary<string, StackPanel> _popcornFieldPanels = [];

    private void BuildPopcornPanel()
    {
        Heading.Text = "Popcorn Пиковера";
        var formula = new TextBlock
        {
            Text = "x′ = x − h·sin(y + tan(k·y))\ny′ = y − h·sin(x + tan(k·x))",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        };
        formula.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SecondaryTextBrush");
        ParameterPanel.Children.Add(formula);
        ParameterPanel.Children.Add(new TextBlock { Text = "Готовые формы" });
        _popcornPresetsBox = new ComboBox { ItemsSource = PopcornPresets.All, DisplayMemberPath = "Name" };
        _popcornPresetsBox.SelectionChanged += (_, _) =>
        {
            if (_syncing || _popcornPresetsBox.SelectedIndex < 0) return;
            _cts?.Cancel();
            PopcornPresets.Apply(_state, _popcornPresetsBox.SelectedIndex);
            SyncControls(); UpdatePopcornPresentation(); UpdateSwatches(); UpdatePreviewTransform(); Schedule();
        };
        ParameterPanel.Children.Add(_popcornPresetsBox);
        ParameterPanel.Children.Add(new TextBlock { Text = "Способ построения" });
        _popcornModeBox = new ComboBox
        {
            ItemsSource = new ChoiceOption[]
            {
                new("Орбиты сетки", nameof(PopcornPlotMode.GridOrbits)),
                new("Кружево Гильберта", nameof(PopcornPlotMode.HilbertCurve))
            }, DisplayMemberPath = nameof(ChoiceOption.Display)
        };
        _popcornModeBox.SelectionChanged += (_, _) =>
        {
            if (_syncing || _popcornModeBox.SelectedItem is not ChoiceOption option) return;
            _cts?.Cancel();
            _state.Popcorn.PlotMode = Enum.Parse<PopcornPlotMode>(option.Value);
            ClearPopcornPreset(); UpdatePopcornPresentation(); Schedule();
        };
        ParameterPanel.Children.Add(_popcornModeBox);
        AddPopcornField("Шаг h · 0,0001–0,5", nameof(PopcornSettings.H));
        AddPopcornField("Частота k · 0,1–12", nameof(PopcornSettings.K));
        AddPopcornField("Итерации каждой точки · 1–1000", nameof(PopcornSettings.OrbitIterations));
        AddPopcornField("Сторона сетки · 8–512", nameof(PopcornSettings.GridSize));
        AddPopcornField("Порядок Гильберта · 2–9", nameof(PopcornSettings.HilbertOrder));
        AddPopcornField("Размер области затравок", nameof(PopcornSettings.SeedSpan));
        AddPopcornField("Размер кадра", nameof(PopcornSettings.Span));
        var explanation = new TextBlock
        {
            Text = "Сетка накапливает все шаги орбит. Гильберт соединяет точки кривой после заданного числа шагов. Затравки остаются на месте при перемещении и масштабировании кадра.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10)
        };
        explanation.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SecondaryTextBrush");
        ParameterPanel.Children.Add(explanation);
        ParameterPanel.Children.Add(new TextBlock { Text = "Палитра плотности" });
        _popcornPaletteBox = new ComboBox { DisplayMemberPath = nameof(ChoiceOption.Display) };
        _popcornPaletteBox.SelectionChanged += (_, _) =>
        {
            if (_syncing || _popcornPaletteBox.SelectedItem is not ChoiceOption option) return;
            _cts?.Cancel();
            _state.PaletteName = option.Value;
            UpdatePopcornPresentation(); Schedule();
        };
        ParameterPanel.Children.Add(_popcornPaletteBox);
    }

    private void AddPopcornField(string label, string key)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        var box = new TextBox { Tag = key };
        NumericSpinner.SetIsEnabled(box, true);
        NumericSpinner.SetIsInteger(box, typeof(PopcornSettings).GetProperty(key)!.PropertyType == typeof(int));
        box.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            _cts?.Cancel(); ClearPopcornPreset(); Schedule();
        };
        panel.Children.Add(box);
        _popcornBoxes[key] = box;
        _popcornFieldPanels[key] = panel;
        ParameterPanel.Children.Add(panel);
    }

    private void ClearPopcornPreset()
    {
        _state.PointOfInterestId = null;
        bool syncing = _syncing;
        _syncing = true;
        _popcornPresetsBox!.SelectedIndex = -1;
        _syncing = syncing;
    }

    private void ReadPopcornSettings()
    {
        var settings = _state.Popcorn.Clone();
        foreach ((string key, TextBox box) in _popcornBoxes)
        {
            if (_popcornFieldPanels[key].Visibility == Visibility.Collapsed) continue;
            PropertyInfo property = typeof(PopcornSettings).GetProperty(key)!;
            if (property.PropertyType == typeof(int))
            {
                if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    throw new InvalidOperationException("Число итераций, размер сетки и порядок кривой должны быть целыми.");
                property.SetValue(settings, value);
            }
            else
            {
                if (!TryDouble(box.Text, out double value) || !double.IsFinite(value))
                    throw new InvalidOperationException("Параметры Popcorn должны быть конечными числами.");
                property.SetValue(settings, value);
            }
        }
        settings.Validate();
        if (_state.DensityGamma is < .05 or > 8)
            throw new InvalidOperationException("Гамма плотности должна быть от 0,05 до 8.");
        _state.Popcorn = settings;
    }

    private void SyncPopcornControls()
    {
        if (_popcornModeBox is null) return;
        foreach ((string key, TextBox box) in _popcornBoxes)
        {
            object? value = typeof(PopcornSettings).GetProperty(key)!.GetValue(_state.Popcorn);
            box.Text = value is double number ? number.ToString("R", CultureInfo.InvariantCulture) : Format(value);
        }
        _popcornModeBox.SelectedItem = _popcornModeBox.Items.OfType<ChoiceOption>()
            .First(p => p.Value == _state.Popcorn.PlotMode.ToString());
        _popcornPresetsBox!.SelectedItem = PopcornPresets.All.FirstOrDefault(p => "popcorn_" + p.Id == _state.PointOfInterestId);
        _popcornPaletteBox!.ItemsSource = new[] { new ChoiceOption("Один цвет", "") }
            .Concat(_palettes.Select(p => new ChoiceOption(p.Name, p.Name))).ToArray();
        _popcornPaletteBox.SelectedItem = _popcornPaletteBox.Items.OfType<ChoiceOption>()
            .FirstOrDefault(p => p.Value == _state.PaletteName);
        UpdatePopcornPresentation();
    }

    private void UpdatePopcornPresentation()
    {
        if (_popcornModeBox is null) return;
        bool curve = _state.Popcorn.PlotMode == PopcornPlotMode.HilbertCurve;
        _popcornFieldPanels[nameof(PopcornSettings.GridSize)].Visibility = curve ? Visibility.Collapsed : Visibility.Visible;
        _popcornFieldPanels[nameof(PopcornSettings.HilbertOrder)].Visibility = curve ? Visibility.Visible : Visibility.Collapsed;
        FractalColorPanel.Visibility = ActivePalette is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
