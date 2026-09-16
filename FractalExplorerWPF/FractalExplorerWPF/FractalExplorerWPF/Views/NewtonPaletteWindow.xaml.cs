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
    private readonly bool _gradientApplies;
    private NewtonColorPalette? _selected;
    private Color _backgroundColor;
    private bool _changingSelection;

    public event EventHandler? PaletteApplied;

    public NewtonPaletteWindow(NewtonPaletteManager manager, IReadOnlyList<Complex> roots)
        : this(manager, roots, null, null, showGradientOption: true)
    {
    }

    /// <summary>
    /// Палитра для произвольного списка бассейнов: окно бассейнов передаёт свои подписи строк,
    /// заголовок и название окна. Яркостью там управляет режим раскраски окна, поэтому флажок
    /// градиента (<paramref name="showGradientOption"/> = false) показывается выключенным с пояснением.
    /// Список палитр общий с бассейнами Ньютона и хранится в одном файле.
    /// </summary>
    public NewtonPaletteWindow(NewtonPaletteManager manager, IReadOnlyList<Complex> targets,
        IReadOnlyList<string>? targetLabels, string? heading, bool showGradientOption, string? windowTitle = null)
    {
        InitializeComponent();
        _manager = manager;
        _roots = targets;
        _labels = targetLabels;
        _gradientApplies = showGradientOption;
        RootCountText.Text = heading ?? $"Найдено корней в формуле: {_roots.Count}";
        if (windowTitle is not null) Title = windowTitle;
        if (targetLabels is not null)
        {
            EditorGroup.Header = "Редактор палитры";
            PreviewLabel.Text = "Превью: цвета бассейнов и цвет фона";
        }
        GradientNote.Visibility = showGradientOption ? Visibility.Collapsed : Visibility.Visible;
        // Другое окно могло изменить общий файл палитр, пока это было открыто.
        _manager.ReloadCustomPalettes();
        RefreshPaletteList(_manager.ActivePalette);
    }

    private bool CanEdit => _selected is { IsBuiltIn: false };

    private void RefreshPaletteList(NewtonColorPalette? select)
    {
        _changingSelection = true;
        try
        {
            PaletteList.ItemsSource = null;
            PaletteList.ItemsSource = select is not null && !_manager.Palettes.Contains(select)
                ? new[] { select }.Concat(_manager.Palettes).ToList()
                : _manager.Palettes;
        }
        finally { _changingSelection = false; }
        PaletteList.SelectedItem = select ?? _manager.Palettes.FirstOrDefault();
    }

    /// <summary>Есть ли у редактируемой палитры изменения, не перенесённые в неё кнопками.</summary>
    private bool HasUnsavedEdits() =>
        CanEdit && _selected is not null &&
        (NameBox.Text.Trim() != _selected.Name || _backgroundColor != _selected.BackgroundColor ||
         (GradientBox.IsChecked == true) != _selected.IsGradient ||
         !_editingColors.SequenceEqual(NewtonPaletteManager.AdjustColors(_selected, _roots.Count)));

    /// <summary>Спрашивает о несохранённых правках; false — пользователь отменил действие.</summary>
    private bool ConfirmLeavingEdits()
    {
        if (!HasUnsavedEdits()) return true;
        MessageBoxResult answer = MessageBox.Show(this, $"Сохранить изменения палитры «{_selected!.Name}»?", "Палитра",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.No)
        {
            LoadEditor(_selected);
            return true;
        }
        if (!ApplyEdits()) return false;
        _manager.SaveCustomPalettes();
        // Имя в списке могло измениться; обновление — после текущей смены выделения.
        Dispatcher.BeginInvoke(() =>
        {
            _changingSelection = true;
            try { PaletteList.Items.Refresh(); }
            finally { _changingSelection = false; }
        });
        return true;
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmLeavingEdits()) e.Cancel = true;
    }

    private void PaletteList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingSelection || PaletteList.SelectedItem is not NewtonColorPalette palette) return;
        if (!ReferenceEquals(palette, _selected) && !ConfirmLeavingEdits())
        {
            _changingSelection = true;
            PaletteList.SelectedItem = _selected;
            _changingSelection = false;
            return;
        }
        LoadEditor(palette);
    }

    private void LoadEditor(NewtonColorPalette palette)
    {
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
        if (!ConfirmLeavingEdits()) return;
        _manager.Palettes.Add(palette);
        // Новая палитра сразу попадает в файл: иначе после перезапуска она молча исчезла бы.
        _manager.SaveCustomPalettes();
        RefreshPaletteList(palette);
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !ConfirmLeavingEdits()) return;
        NewtonColorPalette copy = _selected.Clone(UniqueName($"{_selected.Name} копия"));
        copy.RootColors = NewtonPaletteManager.AdjustColors(_selected, _roots.Count);
        if (copy.ExpansionMode == NewtonPaletteExpansionMode.Harmonic)
            copy.ExpansionMode = NewtonPaletteExpansionMode.CyclicRamp;
        _manager.Palettes.Add(copy);
        _manager.SaveCustomPalettes();
        RefreshPaletteList(copy);
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || _selected is null) return;
        if (MessageBox.Show(this, $"Удалить «{_selected.Name}»?", "Палитра",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        bool active = ReferenceEquals(_selected, _manager.ActivePalette);
        _manager.Palettes.Remove(_selected);
        _selected = null; // Правки удалённой палитры сохранять незачем.
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
        GradientBox.IsEnabled = editable && _gradientApplies;
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
