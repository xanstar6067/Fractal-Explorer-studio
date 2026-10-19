using FractalExplorerWPF.Controls;
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

public partial class GrayScottPaletteWindow : Window
{
    private readonly GrayScottPaletteManager _manager;
    private readonly ColorSelectionService _colorSelection = ColorSelectionService.Default;
    private readonly List<Color> _editingColors = [];
    private GrayScottPalette? _selected;

    public event EventHandler? PaletteApplied;

    public GrayScottPaletteWindow(GrayScottPaletteManager manager)
    {
        InitializeComponent();
        _manager = manager;
        CustomPaletteFile.ShowLoadWarning(this, manager.LoadWarning);
        RefreshList(manager.ActivePalette);
    }

    private bool CanEdit => _selected is { IsBuiltIn: false };

    private void RefreshList(GrayScottPalette? selection)
    {
        PaletteList.ItemsSource = null;
        PaletteList.ItemsSource = _manager.Palettes;
        PaletteList.SelectedItem = selection ?? _manager.Palettes[0];
    }

    private void PaletteList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PaletteList.SelectedItem is not GrayScottPalette palette) return;
        _selected = palette;
        NameBox.Text = palette.Name;
        GradientBox.IsChecked = palette.IsGradient;
        GammaBox.Text = palette.Gamma.ToString(CultureInfo.InvariantCulture);
        _editingColors.Clear();
        _editingColors.AddRange(palette.Colors);
        RefreshColors(0);
        UpdateEditState();
        InitializeNumericInput();
    }

    private void New_OnClick(object sender, RoutedEventArgs e)
    {
        var palette = new GrayScottPalette { Name = UniqueName("Новая палитра") };
        _manager.Palettes.Add(palette);
        RefreshList(palette);
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        GrayScottPalette copy = _selected.Clone(UniqueName($"{_selected.Name} копия"));
        _manager.Palettes.Add(copy);
        RefreshList(copy);
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || _selected is null) return;
        if (MessageBox.Show(this, $"Удалить «{_selected.Name}»?", "Палитра Gray–Scott",
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
        _manager.SaveCustomPalettes();
        PaletteList.Items.Refresh();
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !ApplyEdits()) return;
        _manager.ActivePalette = _selected;
        _manager.SaveCustomPalettes();
        PaletteApplied?.Invoke(this, EventArgs.Empty);
    }

    private void Add_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || !_colorSelection.TrySelectColor(this, Colors.White, out Color color)) return;
        _editingColors.Add(color);
        RefreshColors(_editingColors.Count - 1);
    }

    private void Edit_OnClick(object sender, RoutedEventArgs e) => EditSelected();

    private void ColorList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelected();

    private void EditSelected()
    {
        int index = ColorList.SelectedIndex;
        if (!CanEdit || index < 0 || !_colorSelection.TrySelectColor(this, _editingColors[index], out Color color)) return;
        _editingColors[index] = color;
        RefreshColors(index);
    }

    private void Remove_OnClick(object sender, RoutedEventArgs e)
    {
        int index = ColorList.SelectedIndex;
        if (!CanEdit || index < 0 || _editingColors.Count <= 1) return;
        _editingColors.RemoveAt(index);
        RefreshColors(Math.Min(index, _editingColors.Count - 1));
    }

    private void Up_OnClick(object sender, RoutedEventArgs e) => MoveColor(-1);
    private void Down_OnClick(object sender, RoutedEventArgs e) => MoveColor(1);

    private void MoveColor(int offset)
    {
        int source = ColorList.SelectedIndex;
        int destination = source + offset;
        if (!CanEdit || source < 0 || destination < 0 || destination >= _editingColors.Count) return;
        (_editingColors[source], _editingColors[destination]) = (_editingColors[destination], _editingColors[source]);
        RefreshColors(destination);
    }

    private void Random_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit) return;
        int count = Random.Shared.Next(3, 9);
        double baseHue = Random.Shared.NextDouble() * 360;
        _editingColors.Clear();
        for (int index = 0; index < count; index++)
            _editingColors.Add(FromHsv((baseHue + index * 360d / count) % 360,
                0.62 + Random.Shared.NextDouble() * 0.35,
                0.55 + Random.Shared.NextDouble() * 0.45));
        RefreshColors(0);
    }

    private bool ApplyEdits()
    {
        NumericInput.CommitAll(this);
        if (_selected is null) return false;
        if (_selected.IsBuiltIn) return true;
        if (string.IsNullOrWhiteSpace(NameBox.Text) || _editingColors.Count == 0 ||
            !double.TryParse(GammaBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double gamma) ||
            gamma is < 0.1 or > 5)
        {
            MessageBox.Show(this, "Проверьте название, цвета и гамму палитры.", "Палитра Gray–Scott",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _selected.Name = NameBox.Text.Trim();
        _selected.Colors = [.. _editingColors];
        _selected.IsGradient = GradientBox.IsChecked == true;
        _selected.Gamma = gamma;
        return true;
    }

    private void RefreshColors(int selection)
    {
        ColorList.ItemsSource = null;
        ColorList.ItemsSource = _editingColors;
        ColorList.SelectedIndex = _editingColors.Count == 0 ? -1 : Math.Clamp(selection, 0, _editingColors.Count - 1);
        UpdatePreview();
        UpdateButtons();
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
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int index = 0; index < _editingColors.Count; index++)
            brush.GradientStops.Add(new GradientStop(_editingColors[index], index / (double)(_editingColors.Count - 1)));
        GradientPreview.Background = brush;
    }

    private void UpdateEditState()
    {
        bool editable = CanEdit;
        NameBox.IsEnabled = editable;
        GradientBox.IsEnabled = editable;
        GammaBox.IsEnabled = editable;
        DeleteButton.IsEnabled = editable;
        RandomButton.IsEnabled = editable;
        EditHint.Text = editable
            ? "Пользовательская палитра хранится отдельно для Gray–Scott и доступна как пресет."
            : "Встроенную палитру можно применить или скопировать для редактирования.";
        UpdateButtons();
    }

    private void ColorList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        int index = ColorList.SelectedIndex;
        AddButton.IsEnabled = CanEdit;
        EditButton.IsEnabled = CanEdit && index >= 0;
        RemoveButton.IsEnabled = CanEdit && index >= 0 && _editingColors.Count > 1;
        UpButton.IsEnabled = CanEdit && index > 0;
        DownButton.IsEnabled = CanEdit && index >= 0 && index < _editingColors.Count - 1;
    }

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
            < 1 => (chroma, x, 0d), < 2 => (x, chroma, 0d), < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma), < 5 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        double match = value - chroma;
        return Color.FromRgb((byte)Math.Round((red + match) * 255),
            (byte)Math.Round((green + match) * 255), (byte)Math.Round((blue + match) * 255));
    }
}
