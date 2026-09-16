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

public partial class DomainColoringPaletteWindow : Window
{
    private const string ColorDragFormat = "FractalExplorerWPF.DomainColoringPaletteColorIndex";
    private readonly DomainColoringPaletteManager _manager;
    private readonly ColorSelectionService _colorSelectionService = ColorSelectionService.Default;
    private readonly List<Color> _editingColors = [];
    private DomainColoringPalette? _selected;
    private Point _dragStartPoint;
    private int _dragSourceIndex = -1;
    private bool _updating;

    public event EventHandler? PaletteApplied;

    public DomainColoringPaletteWindow(DomainColoringPaletteManager manager)
    {
        InitializeComponent();
        _manager = manager;
        RefreshList(_manager.ActivePalette);
    }

    private void RefreshList(DomainColoringPalette? select)
    {
        PaletteList.ItemsSource = null;
        PaletteList.ItemsSource = select is not null && !_manager.Palettes.Contains(select)
            ? new[] { select }.Concat(_manager.Palettes).ToList()
            : _manager.Palettes;
        PaletteList.SelectedItem = select ?? _manager.Palettes.FirstOrDefault();
    }

    private void PaletteList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PaletteList.SelectedItem is not DomainColoringPalette palette) return;
        _selected = palette;
        _updating = true;
        NameBox.Text = palette.Name;
        _editingColors.Clear();
        _editingColors.AddRange(palette.Colors);
        ColorList.ItemsSource = null;
        ColorList.ItemsSource = _editingColors;
        ColorList.SelectedIndex = _editingColors.Count > 0 ? 0 : -1;
        GradientBox.IsChecked = palette.IsGradient;
        GammaBox.Text = palette.Gamma.ToString(CultureInfo.InvariantCulture);
        ReverseBox.IsChecked = palette.Reverse;
        _updating = false;
        UpdateEditState();
        UpdateColorButtons();
        UpdatePreview();
    }

    private void New_OnClick(object sender, RoutedEventArgs e)
    {
        var palette = new DomainColoringPalette { Name = UniqueName("Новая палитра") };
        _manager.Palettes.Add(palette);
        RefreshList(palette);
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        DomainColoringPalette copy = _selected.Clone(UniqueName($"{_selected.Name} копия"));
        _manager.Palettes.Add(copy);
        RefreshList(copy);
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.IsBuiltIn) return;
        if (MessageBox.Show(this, $"Удалить «{_selected.Name}»?", "Палитра Domain Coloring",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        bool active = ReferenceEquals(_selected, _manager.ActivePalette);
        _manager.Palettes.Remove(_selected);
        if (active) _manager.ActivePalette = _manager.Palettes[0];
        _manager.SaveCustomPalettes();
        RefreshList(_manager.ActivePalette);
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ApplyEdits()) return;
        if (_selected is not null && !_manager.Palettes.Contains(_selected))
        {
            if (_manager.Palettes.Any(palette => palette.Name.Equals(
                    _selected.Name, StringComparison.OrdinalIgnoreCase)))
                _selected.Name = UniqueName($"{_selected.Name} копия");
            _manager.Palettes.Add(_selected);
        }
        _manager.SaveCustomPalettes();
        RefreshList(_selected);
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !_selected.IsBuiltIn && !ApplyEdits()) return;
        _manager.ActivePalette = _selected;
        _manager.SaveCustomPalettes();
        PaletteList.Items.Refresh();
        PaletteApplied?.Invoke(this, EventArgs.Empty);
    }

    private bool ApplyEdits()
    {
        if (_selected is null || _selected.IsBuiltIn) return _selected is not null;
        bool gammaValid = double.TryParse(GammaBox.Text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out double gamma) && gamma is >= 0.1 and <= 5;
        if (string.IsNullOrWhiteSpace(NameBox.Text) || _editingColors.Count == 0 || !gammaValid)
        {
            MessageBox.Show(this, "Проверьте имя, цвета и гамму (0.1–5).",
                "Палитра Domain Coloring", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        string name = NameBox.Text.Trim();
        if (_manager.Palettes.Any(palette => !ReferenceEquals(palette, _selected) &&
                palette.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Палитра с таким именем уже существует.", "Палитра Domain Coloring",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _selected.Name = name;
        _selected.Colors = [.. _editingColors];
        _selected.IsGradient = GradientBox.IsChecked == true;
        _selected.Gamma = gamma;
        _selected.Reverse = ReverseBox.IsChecked == true;
        return true;
    }

    private void AddColor_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || !TryChooseColor(Colors.White, out Color color)) return;
        _editingColors.Add(color);
        RefreshColorList(_editingColors.Count - 1);
    }

    private void EditColor_OnClick(object sender, RoutedEventArgs e) => EditSelectedColor();
    private void ColorList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelectedColor();

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
        int source = ColorList.SelectedIndex;
        int destination = source + offset;
        if (!CanEdit || source < 0 || destination < 0 || destination >= _editingColors.Count) return;
        Color color = _editingColors[source];
        _editingColors.RemoveAt(source);
        _editingColors.Insert(destination, color);
        RefreshColorList(destination);
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
        int source = _dragSourceIndex;
        _dragSourceIndex = -1;
        DragDrop.DoDragDrop(ColorList, new DataObject(ColorDragFormat, source), DragDropEffects.Move);
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
        int insertion = GetInsertionIndex(e.GetPosition(ColorList));
        ColorList.SelectedIndex = Math.Clamp(insertion, 0, Math.Max(0, _editingColors.Count - 1));
        e.Handled = true;
    }

    private void ColorList_OnDrop(object sender, DragEventArgs e)
    {
        if (!CanEdit || e.Data.GetData(ColorDragFormat) is not int source ||
            source < 0 || source >= _editingColors.Count) return;
        int insertion = GetInsertionIndex(e.GetPosition(ColorList));
        Color color = _editingColors[source];
        _editingColors.RemoveAt(source);
        if (insertion > source) insertion--;
        insertion = Math.Clamp(insertion, 0, _editingColors.Count);
        _editingColors.Insert(insertion, color);
        RefreshColorList(insertion);
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
        return current is ListBoxItem item
            ? ColorList.ItemContainerGenerator.IndexFromContainer(item) : -1;
    }

    private void ColorList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateColorButtons();

    private void Randomize_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit) return;
        int count = Random.Shared.Next(3, 9);
        double baseHue = Random.Shared.NextDouble() * 360;
        _editingColors.Clear();
        for (int index = 0; index < count; index++)
        {
            double hue = (baseHue + index * 360.0 / count + Random.Shared.NextDouble() * 28 - 14 + 360) % 360;
            _editingColors.Add(FromHsv(hue, 0.72 + Random.Shared.NextDouble() * 0.28,
                0.68 + Random.Shared.NextDouble() * 0.32));
        }
        RefreshColorList(0);
    }

    private void PreviewOption_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating) UpdatePreview();
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
        if (GradientPreview is null || _editingColors.Count == 0)
        {
            if (GradientPreview is not null) GradientPreview.Background = MediaBrushes.Transparent;
            return;
        }
        List<Color> colors = ReverseBox.IsChecked == true
            ? _editingColors.AsEnumerable().Reverse().ToList() : [.. _editingColors];
        if (colors.Count == 1)
        {
            GradientPreview.Background = new SolidColorBrush(colors[0]);
            return;
        }
        // Замкнутый круг: последний цвет плавно переходит обратно в первый,
        // поэтому в превью добавляем первый цвет ещё раз в конец.
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        if (GradientBox.IsChecked == true)
        {
            List<Color> cyclic = [.. colors, colors[0]];
            for (int index = 0; index < cyclic.Count; index++)
                brush.GradientStops.Add(new GradientStop(cyclic[index], index / (double)(cyclic.Count - 1)));
        }
        else
        {
            for (int index = 0; index < colors.Count; index++)
            {
                double start = index / (double)colors.Count;
                double end = (index + 1) / (double)colors.Count;
                brush.GradientStops.Add(new GradientStop(colors[index], start));
                brush.GradientStops.Add(new GradientStop(colors[index], end));
            }
        }
        GradientPreview.Background = brush;
    }

    private void UpdateEditState()
    {
        bool editable = CanEdit;
        NameBox.IsEnabled = editable;
        ColorList.AllowDrop = editable;
        GradientBox.IsEnabled = editable;
        GammaBox.IsEnabled = editable;
        ReverseBox.IsEnabled = editable;
        RandomizeButton.IsEnabled = editable;
        DeletePaletteButton.IsEnabled = editable;
        EditHint.Text = editable
            ? "Пользовательская палитра принадлежит только Domain Coloring."
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
    private bool TryChooseColor(Color initial, out Color selected) =>
        _colorSelectionService.TrySelectColor(this, initial, out selected);

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
        return Color.FromRgb((byte)Math.Round((red + match) * 255),
            (byte)Math.Round((green + match) * 255), (byte)Math.Round((blue + match) * 255));
    }
}
