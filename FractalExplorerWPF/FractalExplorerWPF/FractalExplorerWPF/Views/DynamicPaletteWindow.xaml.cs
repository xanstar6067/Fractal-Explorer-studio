using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.ColorPicking;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class DynamicPaletteWindow : Window
{
    private readonly DynamicPaletteStore _store;
    private readonly List<DynamicPalette> _palettes;
    private readonly List<Color> _editingColors = [];
    public event EventHandler? PaletteApplied;

    public DynamicPaletteWindow(DynamicPaletteStore store, IEnumerable<DynamicPalette> palettes,
        DynamicPalette? selected) : this(store, palettes, selected, false) { }

    public DynamicPaletteWindow(DynamicPaletteStore store, IEnumerable<DynamicPalette> palettes,
        DynamicPalette? selected, bool densityOnly)
    {
        _store = store;
        _palettes = palettes.ToList();
        InitializeComponent();
        ModeBox.ItemsSource = new[]
        {
            "LegacyBuiltIn", "Diverging", "Absolute", "ZeroBandHighlight",
            "HistogramEqualized", "Cycle", "Gradient"
        };
        if (densityOnly)
        {
            Title = "Палитры симметричных орнаментов";
            ModeBox.ItemsSource = new[] { "Gradient" };
            ((FrameworkElement)ModeBox.Parent).Visibility = Visibility.Collapsed;
            ((FrameworkElement)RangeBox.Parent).Visibility = Visibility.Collapsed;
            ((FrameworkElement)ZeroBox.Parent).Visibility = Visibility.Collapsed;
        }
        PaletteList.ItemsSource = _palettes;
        PaletteList.SelectedItem = selected ?? _palettes.FirstOrDefault();
    }

    public DynamicPalette? SelectedPalette => PaletteList.SelectedItem as DynamicPalette;

    private bool CanEdit => SelectedPalette is { IsBuiltIn: false };

    private void PaletteList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedPalette is not { } palette) return;
        NameBox.Text = palette.Name;
        ModeBox.SelectedItem = palette.Mode;
        RangeBox.Text = palette.ExponentRange.ToString("G", CultureInfo.InvariantCulture);
        ZeroBox.Text = palette.ZeroBandWidth.ToString("G", CultureInfo.InvariantCulture);
        _editingColors.Clear();
        _editingColors.AddRange(palette.Colors);
        ColorList.ItemsSource = _editingColors;
        ColorList.SelectedIndex = _editingColors.Count > 0 ? 0 : -1;
        RefreshPreview();
        UpdateEditState();
    }

    private void Clone_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedPalette is not { } palette) return;
        DynamicPalette copy = palette.Clone(palette.Name + " — копия");
        _palettes.Add(copy);
        RefreshList(copy);
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedPalette is not { IsBuiltIn: false } palette) return;
        _palettes.Remove(palette);
        RefreshList(_palettes.FirstOrDefault());
        _store.Save(_palettes);
    }

    private void AddColor_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || !TryChooseColor(Colors.White, out Color color)) return;
        _editingColors.Add(color);
        RefreshColors(_editingColors.Count - 1);
    }

    private void EditColor_OnClick(object sender, RoutedEventArgs e) => EditSelectedColor();

    private void ColorList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelectedColor();

    private void EditSelectedColor()
    {
        int index = ColorList.SelectedIndex;
        if (!CanEdit || index < 0 || !TryChooseColor(_editingColors[index], out Color color)) return;
        _editingColors[index] = color;
        RefreshColors(index);
    }

    private void RemoveColor_OnClick(object sender, RoutedEventArgs e)
    {
        int index = ColorList.SelectedIndex;
        if (!CanEdit || index < 0 || _editingColors.Count <= 2) return;
        _editingColors.RemoveAt(index);
        RefreshColors(Math.Min(index, _editingColors.Count - 1));
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
        RefreshColors(destination);
    }

    private void ColorList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateColorButtons();

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (SaveEditor()) PaletteApplied?.Invoke(this, EventArgs.Empty);
    }

    private void Done_OnClick(object sender, RoutedEventArgs e)
    {
        if (!SaveEditor()) return;
        DialogResult = true;
    }

    private bool SaveEditor()
    {
        if (SelectedPalette is not { } palette) return false;
        if (palette.IsBuiltIn) return true;
        if (string.IsNullOrWhiteSpace(NameBox.Text) ||
            !double.TryParse(RangeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double range) ||
            !double.TryParse(ZeroBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double zero) ||
            _editingColors.Count < 2)
        {
            MessageBox.Show(this, "Проверьте название, числовые параметры и оставьте минимум два цвета.");
            return false;
        }

        palette.Name = NameBox.Text.Trim();
        palette.Mode = ModeBox.SelectedItem as string ?? "Diverging";
        palette.ExponentRange = Math.Max(1e-9, range);
        palette.ZeroBandWidth = Math.Max(1e-9, zero);
        palette.Colors = [.. _editingColors];
        _store.Save(_palettes);
        RefreshList(palette);
        return true;
    }

    private void RefreshList(DynamicPalette? selected)
    {
        PaletteList.ItemsSource = null;
        PaletteList.ItemsSource = _palettes;
        PaletteList.SelectedItem = selected;
    }

    private void RefreshColors(int selectedIndex)
    {
        ColorList.Items.Refresh();
        ColorList.SelectedIndex = selectedIndex;
        ColorList.ScrollIntoView(ColorList.SelectedItem);
        RefreshPreview();
        UpdateColorButtons();
    }

    private void RefreshPreview()
    {
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        for (int index = 0; index < _editingColors.Count; index++)
            gradient.GradientStops.Add(new GradientStop(_editingColors[index],
                index / (double)Math.Max(1, _editingColors.Count - 1)));
        Preview.Background = gradient;
    }

    private void UpdateEditState()
    {
        bool editable = CanEdit;
        NameBox.IsReadOnly = !editable;
        ModeBox.IsEnabled = editable;
        RangeBox.IsReadOnly = !editable;
        ZeroBox.IsReadOnly = !editable;
        DeleteButton.IsEnabled = editable;
        UpdateColorButtons();
    }

    private void UpdateColorButtons()
    {
        int index = ColorList.SelectedIndex;
        AddColorButton.IsEnabled = CanEdit;
        EditColorButton.IsEnabled = CanEdit && index >= 0;
        RemoveColorButton.IsEnabled = CanEdit && index >= 0 && _editingColors.Count > 2;
        MoveColorUpButton.IsEnabled = CanEdit && index > 0;
        MoveColorDownButton.IsEnabled = CanEdit && index >= 0 && index < _editingColors.Count - 1;
    }

    private bool TryChooseColor(Color initial, out Color selected) =>
        ColorSelectionService.Default.TrySelectColor(this, initial, out selected);
}
