using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.ColorPicking;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Views;

public partial class NewtonPaletteWindow : Window
{
    private readonly NewtonPaletteManager _manager;
    private readonly IReadOnlyList<Complex> _roots;
    private readonly IReadOnlyList<string>? _labels;
    private readonly ColorSelectionService _colorSelectionService = ColorSelectionService.Default;
    private readonly List<Color> _editingColors = [];
    private NewtonColorPalette? _selected;
    private Color _backgroundColor;

    public event EventHandler? PaletteApplied;

    public NewtonPaletteWindow(NewtonPaletteManager manager, IReadOnlyList<Complex> roots)
        : this(manager, roots, null, null, showGradientOption: true)
    {
    }

    /// <summary>
    /// Палитра для произвольного списка бассейнов: окно бассейнов Мюллера, Лагерра, секущих и
    /// отображений передаёт свои подписи строк и заголовок вместо «Корень k». Яркостью там
    /// управляет режим раскраски окна, поэтому флажок градиента можно скрыть.
    /// </summary>
    public NewtonPaletteWindow(NewtonPaletteManager manager, IReadOnlyList<Complex> targets,
        IReadOnlyList<string>? targetLabels, string? heading, bool showGradientOption)
    {
        InitializeComponent();
        _manager = manager;
        _roots = targets;
        _labels = targetLabels;
        RootCountText.Text = heading ?? $"Найдено корней в формуле: {_roots.Count}";
        if (!showGradientOption) GradientBox.Visibility = Visibility.Collapsed;
        RefreshPaletteList(_manager.ActivePalette);
    }

    private bool CanEdit => _selected is { IsBuiltIn: false };

    private void RefreshPaletteList(NewtonColorPalette? select)
    {
        PaletteList.ItemsSource = null;
        PaletteList.ItemsSource = select is not null && !_manager.Palettes.Contains(select)
            ? new[] { select }.Concat(_manager.Palettes).ToList()
            : _manager.Palettes;
        PaletteList.SelectedItem = select ?? _manager.Palettes.FirstOrDefault();
    }

    private void PaletteList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PaletteList.SelectedItem is not NewtonColorPalette palette) return;
        _selected = palette;
        NameBox.Text = palette.Name;
        GradientBox.IsChecked = palette.IsGradient;
        _backgroundColor = palette.BackgroundColor;
        _editingColors.Clear();
        _editingColors.AddRange(NewtonPaletteManager.AdjustColors(palette, _roots.Count));
        RefreshColors(0);
        UpdateEditState();
    }

    private void New_OnClick(object sender, RoutedEventArgs e)
    {
        var palette = new NewtonColorPalette
        {
            Name = UniqueName("Новая палитра"),
            RootColors = NewtonPaletteManager.GenerateHarmonicColors(Math.Max(1, _roots.Count)),
            BackgroundColor = Colors.Black,
            IsGradient = true
        };
        _manager.Palettes.Add(palette);
        RefreshPaletteList(palette);
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        NewtonColorPalette copy = _selected.Clone(UniqueName($"{_selected.Name} копия"));
        copy.RootColors = NewtonPaletteManager.AdjustColors(_selected, _roots.Count);
        if (copy.ExpansionMode == NewtonPaletteExpansionMode.Harmonic)
            copy.ExpansionMode = NewtonPaletteExpansionMode.CyclicRamp;
        _manager.Palettes.Add(copy);
        RefreshPaletteList(copy);
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || _selected is null) return;
        if (MessageBox.Show(this, $"Удалить «{_selected.Name}»?", "Палитра",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        bool active = ReferenceEquals(_selected, _manager.ActivePalette);
        _manager.Palettes.Remove(_selected);
        if (active) _manager.ActivePalette = _manager.Palettes[0];
        _manager.SaveCustomPalettes();
        RefreshPaletteList(_manager.ActivePalette);
    }

    private void EditRoot_OnClick(object sender, RoutedEventArgs e) => EditRoot(RootColorsList.SelectedIndex);

    private void RootColorsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CanEdit) EditRoot(RootColorsList.SelectedIndex);
    }

    private void RootColorsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEditState();

    private void PreviewRootColors_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!CanEdit || _editingColors.Count == 0 || PreviewRootColors.ActualWidth <= 0) return;
        int index = Math.Clamp((int)(e.GetPosition(PreviewRootColors).X / PreviewRootColors.ActualWidth * _editingColors.Count), 0, _editingColors.Count - 1);
        RootColorsList.SelectedIndex = index;
        EditRoot(index);
    }

    private void EditRoot(int index)
    {
        if (!CanEdit || index < 0 || index >= _editingColors.Count) return;
        if (!_colorSelectionService.TrySelectColor(this, _editingColors[index], out Color selected)) return;
        _editingColors[index] = selected;
        RefreshColors(index);
    }

    private void AutoAdjust_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || _selected is null) return;
        var temporary = _selected.Clone(_selected.Name);
        temporary.RootColors = [.. _editingColors];
        _editingColors.Clear();
        _editingColors.AddRange(NewtonPaletteManager.AdjustColors(temporary, _roots.Count));
        RefreshColors(0);
    }

    private void BackgroundButton_OnClick(object sender, RoutedEventArgs e) => EditBackground();
    private void BackgroundPreview_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EditBackground();

    private void EditBackground()
    {
        if (!CanEdit || !_colorSelectionService.TrySelectColor(this, _backgroundColor, out Color selected)) return;
        _backgroundColor = selected;
        UpdatePreview();
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ApplyEdits()) return;
        _manager.SaveCustomPalettes();
        RefreshPaletteList(_selected);
        Status("Изменения палитры сохранены.");
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || (!ApplyEdits() && !_selected.IsBuiltIn)) return;
        _manager.ActivePalette = _selected;
        _manager.SaveCustomPalettes();
        PaletteApplied?.Invoke(this, EventArgs.Empty);
        Status($"Применена палитра «{_selected.Name}».");
    }

    private bool ApplyEdits()
    {
        if (_selected is null) return false;
        if (_selected.IsBuiltIn) return true;
        string name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || _manager.Palettes.Any(palette =>
                !ReferenceEquals(palette, _selected) && palette.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Введите непустое уникальное имя палитры.", "Палитра", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _selected.Name = name;
        _selected.RootColors = [.. _editingColors];
        _selected.BackgroundColor = _backgroundColor;
        _selected.IsGradient = GradientBox.IsChecked == true;
        return true;
    }

    private void RefreshColors(int selectedIndex)
    {
        List<NewtonRootColorItem> items = _editingColors.Select((color, index) =>
            new NewtonRootColorItem(index, index < _roots.Count ? _roots[index] : Complex.Zero, color,
                _labels is not null && index < _labels.Count ? _labels[index] : null)).ToList();
        RootColorsList.ItemsSource = items;
        PreviewRootColors.ItemsSource = items;
        RootColorsList.SelectedIndex = items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, items.Count - 1);
        UpdatePreview();
        UpdateEditState();
    }

    private void UpdatePreview()
    {
        var brush = new SolidColorBrush(_backgroundColor);
        brush.Freeze();
        BackgroundPreview.Background = brush;
    }

    private void UpdateEditState()
    {
        bool editable = CanEdit;
        NameBox.IsEnabled = editable;
        GradientBox.IsEnabled = editable;
        RootColorsList.IsHitTestVisible = editable;
        PreviewRootColors.IsHitTestVisible = editable;
        BackgroundPreview.IsHitTestVisible = editable;
        DeleteButton.IsEnabled = editable;
        AutoAdjustButton.IsEnabled = editable && _roots.Count > 0;
        BackgroundButton.IsEnabled = editable;
        EditRootButton.IsEnabled = editable && RootColorsList.SelectedIndex >= 0;
        EditHint.Text = editable
            ? _labels is null
                ? "Карточки цветов и секции превью кликабельны. Число цветов привязано к найденным корням формулы."
                : "Карточки цветов и секции превью кликабельны. Число цветов привязано к найденным бассейнам."
            : "Встроенная палитра доступна только для просмотра и применения. Создайте копию для редактирования.";
    }

    private string UniqueName(string basis)
    {
        string name = basis;
        int suffix = 1;
        while (_manager.Palettes.Any(palette => palette.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            name = $"{basis} {suffix++}";
        return name;
    }

    private void Status(string message) => EditHint.Text = message;
}
