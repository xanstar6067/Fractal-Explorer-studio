using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.ColorPicking;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class MandelbrotPaletteWindow : Window
{
    private const string ColorDragFormat = "FractalExplorerWPF.MandelbrotPaletteColorIndex";

    private readonly MandelbrotPaletteManager _manager;
    private readonly ColorSelectionService _colorSelectionService = ColorSelectionService.Default;
    private readonly List<Color> _editingColors = [];
    private MandelbrotPalette? _selected;
    private Point _dragStartPoint;
    private int _dragSourceIndex = -1;

    public event EventHandler? PaletteApplied;

    public MandelbrotPaletteWindow(MandelbrotPaletteManager manager)
    {
        InitializeComponent();
        _manager = manager;
        if (_manager.LoadWarning is { } warning)
        {
            PaletteLoadWarning.Text = warning;
            PaletteLoadWarning.Visibility = Visibility.Visible;
        }
        SavePaletteButton.IsEnabled = _manager.CanSaveCustomPalettes;
        RefreshList(_manager.ActivePalette);
    }

    private void RefreshList(MandelbrotPalette? select)
    {
        PaletteList.ItemsSource = null;
        PaletteList.ItemsSource = select is not null && !_manager.Palettes.Contains(select)
            ? new[] { select }.Concat(_manager.Palettes).ToList()
            : _manager.Palettes;
        PaletteList.SelectedItem = select ?? _manager.Palettes.FirstOrDefault();
    }

    private void PaletteList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PaletteList.SelectedItem is not MandelbrotPalette palette) return;

        _selected = palette;
        NameBox.Text = palette.Name;
        _editingColors.Clear();
        _editingColors.AddRange(palette.Colors);
        ColorList.ItemsSource = null;
        ColorList.ItemsSource = _editingColors;
        ColorList.SelectedIndex = _editingColors.Count > 0 ? 0 : -1;
        InteriorColorSelector.SelectedColor = palette.InteriorColor;
        GradientBox.IsChecked = palette.IsGradient;
        AlignIterationsBox.IsChecked = palette.AlignWithRenderIterations;
        GammaBox.Text = palette.Gamma.ToString(CultureInfo.InvariantCulture);
        PeriodBox.Text = palette.ColorPeriod.ToString(CultureInfo.InvariantCulture);
        UpdateEditState();
        UpdateColorButtons();
        UpdatePreview();
    }

    private void New_OnClick(object sender, RoutedEventArgs e)
    {
        var palette = new MandelbrotPalette { Name = UniqueName("Новая палитра") };
        _manager.Palettes.Add(palette);
        RefreshList(palette);
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        MandelbrotPalette copy = _selected.Clone(UniqueName($"{_selected.Name} копия"));
        _manager.Palettes.Add(copy);
        RefreshList(copy);
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.IsBuiltIn || !_manager.CanSaveCustomPalettes) return;
        if (MessageBox.Show(this, $"Удалить «{_selected.Name}»?", "Палитра",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        MandelbrotPalette selected = _selected;
        bool active = ReferenceEquals(selected, _manager.ActivePalette);
        int index = _manager.Palettes.IndexOf(selected);
        _manager.Palettes.RemoveAt(index);
        if (!TrySaveCustomPalettes())
        {
            _manager.Palettes.Insert(index, selected);
            return;
        }
        if (active) _manager.ActivePalette = _manager.Palettes[0];
        RefreshList(_manager.ActivePalette);
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_manager.CanSaveCustomPalettes || !ApplyEdits()) return;
        if (_selected is not null && !_manager.Palettes.Contains(_selected))
        {
            if (_manager.Palettes.Any(palette => palette.Name.Equals(_selected.Name, StringComparison.OrdinalIgnoreCase)))
                _selected.Name = UniqueName($"{_selected.Name} копия");
            _manager.Palettes.Add(_selected);
        }
        if (!TrySaveCustomPalettes()) return;
        RefreshList(_selected);
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        if (!_selected.IsBuiltIn && !ApplyEdits()) return;

        if (_manager.CanSaveCustomPalettes && !TrySaveCustomPalettes()) return;
        _manager.ActivePalette = _selected;
        PaletteList.Items.Refresh();
        PaletteApplied?.Invoke(this, EventArgs.Empty);
    }

    private bool TrySaveCustomPalettes()
    {
        try
        {
            _manager.SaveCustomPalettes();
            return true;
        }
        catch (Exception exception)
        {
            CrashLogger.Log("MandelbrotPaletteWindow.SaveCustomPalettes", exception);
            MessageBox.Show(this, exception.Message, "Не удалось сохранить палитры",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void AddColor_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || !TryChooseColor(Colors.White, out Color color)) return;
        _editingColors.Add(color);
        RefreshColorList(_editingColors.Count - 1);
    }

    private void EditColor_OnClick(object sender, RoutedEventArgs e) => EditSelectedColor();

    private void ColorList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CanEdit && ColorList.SelectedIndex >= 0) EditSelectedColor();
    }

    private void ColorSwatch_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!CanEdit || sender is not DependencyObject source) return;
        int index = ItemIndexAt(source);
        if (index < 0) return;
        ColorList.SelectedIndex = index;
        EditSelectedColor();
        e.Handled = true;
    }

    private void EditSelectedColor()
    {
        int index = ColorList.SelectedIndex;
        if (!CanEdit || index < 0 || !TryChooseColor(_editingColors[index], out Color color)) return;
        _editingColors[index] = color;
        RefreshColorList(index);
    }

    private void RemoveColor_OnClick(object sender, RoutedEventArgs e)
    {
        int index = ColorList.SelectedIndex;
        if (!CanEdit || index < 0 || _editingColors.Count <= 1) return;
        _editingColors.RemoveAt(index);
        RefreshColorList(Math.Min(index, _editingColors.Count - 1));
    }

    private void MoveColorUp_OnClick(object sender, RoutedEventArgs e) => MoveSelectedColor(-1);

    private void MoveColorDown_OnClick(object sender, RoutedEventArgs e) => MoveSelectedColor(1);

    private void MoveSelectedColor(int offset)
    {
        int sourceIndex = ColorList.SelectedIndex;
        int destinationIndex = sourceIndex + offset;
        if (!CanEdit || sourceIndex < 0 || destinationIndex < 0 || destinationIndex >= _editingColors.Count) return;

        Color color = _editingColors[sourceIndex];
        _editingColors.RemoveAt(sourceIndex);
        _editingColors.Insert(destinationIndex, color);
        RefreshColorList(destinationIndex);
    }

    private void ColorList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(ColorList);
        _dragSourceIndex = ItemIndexAt(e.OriginalSource as DependencyObject);
    }

    private void ColorList_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!CanEdit || e.LeftButton != MouseButtonState.Pressed || _dragSourceIndex < 0) return;
        Point current = e.GetPosition(ColorList);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        int sourceIndex = _dragSourceIndex;
        _dragSourceIndex = -1;
        DragDrop.DoDragDrop(ColorList, new DataObject(ColorDragFormat, sourceIndex), DragDropEffects.Move);
    }

    private void ColorList_OnDragOver(object sender, DragEventArgs e)
    {
        if (!CanEdit || !e.Data.GetDataPresent(ColorDragFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        int insertionIndex = GetInsertionIndex(e.GetPosition(ColorList));
        ColorList.SelectedIndex = Math.Clamp(insertionIndex, 0, Math.Max(0, _editingColors.Count - 1));
        e.Handled = true;
    }

    private void ColorList_OnDrop(object sender, DragEventArgs e)
    {
        if (!CanEdit || e.Data.GetData(ColorDragFormat) is not int sourceIndex ||
            sourceIndex < 0 || sourceIndex >= _editingColors.Count) return;

        int insertionIndex = GetInsertionIndex(e.GetPosition(ColorList));
        Color color = _editingColors[sourceIndex];
        _editingColors.RemoveAt(sourceIndex);
        if (insertionIndex > sourceIndex) insertionIndex--;
        insertionIndex = Math.Clamp(insertionIndex, 0, _editingColors.Count);
        _editingColors.Insert(insertionIndex, color);
        RefreshColorList(insertionIndex);
        e.Handled = true;
    }

    private int GetInsertionIndex(Point position)
    {
        for (int index = 0; index < ColorList.Items.Count; index++)
        {
            if (ColorList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem item) continue;
            Point point = ColorList.TranslatePoint(position, item);
            if (point.Y < item.ActualHeight / 2) return index;
            if (point.Y <= item.ActualHeight) return index + 1;
        }
        return ColorList.Items.Count;
    }

    private int ItemIndexAt(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null && current is not ListBoxItem)
            current = VisualTreeHelper.GetParent(current);
        return current is ListBoxItem item ? ColorList.ItemContainerGenerator.IndexFromContainer(item) : -1;
    }

    private void ColorList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateColorButtons();

    private void Randomize_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit) return;
        int colorCount = Random.Shared.Next(3, 13);
        double baseHue = Random.Shared.NextDouble() * 360;
        _editingColors.Clear();
        for (int index = 0; index < colorCount; index++)
        {
            double hue = (baseHue + index * 360.0 / colorCount + Random.Shared.NextDouble() * 36 - 18 + 360) % 360;
            double saturation = 0.65 + Random.Shared.NextDouble() * 0.35;
            double value = 0.62 + Random.Shared.NextDouble() * 0.38;
            _editingColors.Add(FromHsv(hue, saturation, value));
        }
        RefreshColorList(0);
    }

    private bool ApplyEdits()
    {
        if (_selected is null || _selected.IsBuiltIn) return _selected is not null;
        if (string.IsNullOrWhiteSpace(NameBox.Text) || _editingColors.Count == 0 ||
            !double.TryParse(GammaBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double gamma) || gamma is < 0.1 or > 5 ||
            !int.TryParse(PeriodBox.Text, out int period) || period is < 2 or > 100_000)
        {
            MessageBox.Show(this, "Проверьте имя, цвета, гамму и период палитры.", "Палитра",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _selected.Name = NameBox.Text.Trim();
        _selected.Colors = [.. _editingColors];
        _selected.InteriorColor = InteriorColorSelector.SelectedColor;
        _selected.IsGradient = GradientBox.IsChecked == true;
        _selected.AlignWithRenderIterations = AlignIterationsBox.IsChecked == true;
        _selected.Gamma = gamma;
        _selected.ColorPeriod = period;
        return true;
    }

    private void RefreshColorList(int selectedIndex)
    {
        ColorList.Items.Refresh();
        ColorList.SelectedIndex = selectedIndex;
        ColorList.ScrollIntoView(ColorList.SelectedItem);
        UpdateColorButtons();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_editingColors.Count == 0)
        {
            GradientPreview.Background = MediaBrushes.Transparent;
            return;
        }
        if (_editingColors.Count == 1)
        {
            GradientPreview.Background = new SolidColorBrush(_editingColors[0]);
            return;
        }

        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int index = 0; index < _editingColors.Count; index++)
            gradient.GradientStops.Add(new GradientStop(_editingColors[index], (double)index / (_editingColors.Count - 1)));
        GradientPreview.Background = gradient;
    }

    private void UpdateEditState()
    {
        bool editable = CanEdit;
        NameBox.IsEnabled = editable;
        ColorList.AllowDrop = editable;
        InteriorColorSelector.IsEnabled = editable;
        GradientBox.IsEnabled = editable;
        GammaBox.IsEnabled = editable;
        PeriodBox.IsEnabled = editable;
        AlignIterationsBox.IsEnabled = editable;
        RandomizeButton.IsEnabled = editable;
        DeletePaletteButton.IsEnabled = editable && _manager.CanSaveCustomPalettes;
        EditHint.Text = !_manager.CanSaveCustomPalettes
            ? "Палитру можно применить к текущему изображению. Запись на диск заблокирована из-за ошибки загрузки."
            : editable
            ? "Пользовательскую палитру можно редактировать, сохранять и применять."
            : "Встроенную палитру можно применить или скопировать для редактирования.";
    }

    private void UpdateColorButtons()
    {
        bool selected = CanEdit && ColorList.SelectedIndex >= 0;
        AddColorButton.IsEnabled = CanEdit;
        EditColorButton.IsEnabled = selected;
        RemoveColorButton.IsEnabled = selected && _editingColors.Count > 1;
        MoveColorUpButton.IsEnabled = selected && ColorList.SelectedIndex > 0;
        MoveColorDownButton.IsEnabled = selected && ColorList.SelectedIndex < _editingColors.Count - 1;
    }

    private bool CanEdit => _selected is { IsBuiltIn: false };

    private bool TryChooseColor(Color initial, out Color selected)
        => _colorSelectionService.TrySelectColor(this, initial, out selected);

    private string UniqueName(string basis)
    {
        string candidate = basis;
        int suffix = 1;
        while (_manager.Palettes.Any(palette => palette.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = $"{basis} {suffix++}";
        return candidate;
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        double chroma = value * saturation;
        double sector = hue / 60;
        double x = chroma * (1 - Math.Abs(sector % 2 - 1));
        (double red, double green, double blue) = sector switch
        {
            < 1 => (chroma, x, 0d),
            < 2 => (x, chroma, 0d),
            < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma),
            < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        double match = value - chroma;
        return Color.FromRgb(
            (byte)Math.Round((red + match) * 255),
            (byte)Math.Round((green + match) * 255),
            (byte)Math.Round((blue + match) * 255));
    }

}
