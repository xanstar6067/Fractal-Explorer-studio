using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorer.Utilities.Coloring;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.ColorPicking;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;
using DrawingColor = System.Drawing.Color;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class LyapunovPaletteWindow : Window
{
    private static readonly ModeChoice[] Modes =
    [
        new("LegacyBuiltIn", "Классический (встроенный)"),
        new("Diverging", "Дивергентный"),
        new("Absolute", "Абсолютный"),
        new("ZeroBandHighlight", "Подсветка нулевой зоны"),
        new("HistogramEqualized", "Гистограммное выравнивание")
    ];

    private readonly DynamicPaletteStore _store;
    private readonly List<DynamicPalette> _palettes;
    private readonly Random _random = new();
    private bool _syncing;
    private Point _dragStart;

    public event EventHandler? PaletteApplied;
    public DynamicPalette? SelectedPalette => PaletteList.SelectedItem as DynamicPalette;

    public LyapunovPaletteWindow(DynamicPaletteStore store, IEnumerable<DynamicPalette> palettes, DynamicPalette? selected)
    {
        _store = store;
        CustomPaletteFile.ShowLoadWarning(this, store.LoadWarning);
        List<DynamicPalette> source = palettes.ToList();
        _palettes = source.Select(p => p.Clone(p.Name)).ToList();
        for (int i = 0; i < _palettes.Count; i++) _palettes[i].IsBuiltIn = source[i].IsBuiltIn;
        InitializeComponent();
        ModeBox.ItemsSource = Modes;
        PaletteList.ItemsSource = _palettes;
        PaletteList.SelectedItem = _palettes.FirstOrDefault(p => p.Name == selected?.Name) ?? _palettes.FirstOrDefault();
    }

    private bool CanEdit => SelectedPalette is { IsBuiltIn: false };

    private void PaletteList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedPalette is not { } palette) return;
        _syncing = true;
        NameBox.Text = palette.Name;
        ModeBox.SelectedItem = Modes.FirstOrDefault(m => m.Value == palette.Mode) ?? Modes[1];
        RangeBox.Text = palette.ExponentRange.ToString("G", CultureInfo.InvariantCulture);
        ZeroBox.Text = palette.ZeroBandWidth.ToString("G", CultureInfo.InvariantCulture);
        ColorList.ItemsSource = palette.Colors;
        ColorList.SelectedIndex = palette.Colors.Count > 0 ? 0 : -1;
        _syncing = false;
        UpdatePreview();
        UpdateEditState();
    }

    private void New_OnClick(object sender, RoutedEventArgs e)
    {
        DynamicPalette basis = _palettes.FirstOrDefault(p => p.Mode == "Diverging") ?? _palettes[0];
        var palette = basis.Clone(UniqueName("Новая палитра"));
        palette.IsBuiltIn = false;
        palette.Mode = "Diverging";
        _palettes.Add(palette);
        RefreshPalettes(palette);
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedPalette is not { } source) return;
        DynamicPalette copy = source.Clone(UniqueName($"{source.Name} (копия)"));
        copy.IsBuiltIn = false;
        _palettes.Add(copy);
        RefreshPalettes(copy);
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || SelectedPalette is not { } palette) return;
        int index = PaletteList.SelectedIndex;
        _palettes.Remove(palette);
        RefreshPalettes(_palettes[Math.Clamp(index - 1, 0, _palettes.Count - 1)]);
    }

    private void Editor_OnChanged(object sender, EventArgs e)
    {
        if (_syncing || !CanEdit || SelectedPalette is not { } palette) return;
        palette.Name = NameBox.Text;
        if (sender == ModeBox && ModeBox.SelectedItem is ModeChoice mode) palette.Mode = mode.Value;
        if (TryDouble(RangeBox.Text, out double range) && range > 0) palette.ExponentRange = range;
        if (TryDouble(ZeroBox.Text, out double zero) && zero > 0) palette.ZeroBandWidth = zero;
        UpdatePreview();
    }

    private void ColorList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEditState();
    private void ColorList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => EditColor();

    private void Add_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || SelectedPalette is not { } palette ||
            !ColorSelectionService.Default.TrySelectColor(this, Colors.White, out Color color)) return;
        palette.Colors.Add(color);
        RefreshColors(palette.Colors.Count - 1);
    }

    private void Edit_OnClick(object sender, RoutedEventArgs e) => EditColor();
    private void EditColor()
    {
        if (!CanEdit || SelectedPalette is not { } palette) return;
        int index = ColorList.SelectedIndex;
        if (index < 0 || index >= palette.Colors.Count ||
            !ColorSelectionService.Default.TrySelectColor(this, palette.Colors[index], out Color color)) return;
        palette.Colors[index] = color;
        RefreshColors(index);
    }

    private void Remove_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || SelectedPalette is not { } palette || palette.Colors.Count <= 2 || ColorList.SelectedIndex < 0) return;
        int index = ColorList.SelectedIndex;
        palette.Colors.RemoveAt(index);
        RefreshColors(Math.Min(index, palette.Colors.Count - 1));
    }

    private void Up_OnClick(object sender, RoutedEventArgs e) => MoveColor(-1);
    private void Down_OnClick(object sender, RoutedEventArgs e) => MoveColor(1);
    private void MoveColor(int delta)
    {
        if (!CanEdit || SelectedPalette is not { } palette) return;
        int source = ColorList.SelectedIndex, destination = source + delta;
        if (source < 0 || destination < 0 || destination >= palette.Colors.Count) return;
        (palette.Colors[source], palette.Colors[destination]) = (palette.Colors[destination], palette.Colors[source]);
        RefreshColors(destination);
    }

    private void Random_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || SelectedPalette is not { } palette) return;
        int count = _random.Next(3, 13);
        double start = _random.NextDouble() * 360;
        palette.Colors = Enumerable.Range(0, count)
            .Select(i => FromHsv(start + 360d * i / count + RandomBetween(-18, 18), RandomBetween(.65, 1), RandomBetween(.62, 1)))
            .ToList();
        ColorList.ItemsSource = palette.Colors;
        RefreshColors(0);
    }

    private void ColorList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(ColorList);
    private void ColorList_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!CanEdit || e.LeftButton != MouseButtonState.Pressed || ColorList.SelectedIndex < 0) return;
        Point current = e.GetPosition(ColorList);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(ColorList, ColorList.SelectedIndex, DragDropEffects.Move);
    }

    private void ColorList_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanEdit && e.Data.GetDataPresent(typeof(int)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void ColorList_OnDrop(object sender, DragEventArgs e)
    {
        if (!CanEdit || SelectedPalette is not { } palette || e.Data.GetData(typeof(int)) is not int source) return;
        int destination = IndexAt(e.GetPosition(ColorList));
        if (destination < 0) destination = palette.Colors.Count - 1;
        if (source < 0 || source >= palette.Colors.Count || source == destination) return;
        Color moved = palette.Colors[source];
        palette.Colors.RemoveAt(source);
        if (source < destination) destination--;
        destination = Math.Clamp(destination, 0, palette.Colors.Count);
        palette.Colors.Insert(destination, moved);
        RefreshColors(destination);
    }

    private int IndexAt(Point point)
    {
        DependencyObject? hit = ColorList.InputHitTest(point) as DependencyObject;
        while (hit is not null && hit is not ListBoxItem) hit = VisualTreeHelper.GetParent(hit);
        return hit is ListBoxItem item ? ColorList.ItemContainerGenerator.IndexFromContainer(item) : -1;
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (!SaveChanges()) return;
        EditHint.Text = "Изменения палитр сохранены.";
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (!SaveChanges()) return;
        PaletteApplied?.Invoke(this, EventArgs.Empty);
        EditHint.Text = $"Применена палитра «{SelectedPalette?.Name}».";
    }

    private bool SaveChanges()
    {
        if (SelectedPalette is { IsBuiltIn: false } current && !CaptureEditor(current)) return false;
        if (_palettes.Any(p => string.IsNullOrWhiteSpace(p.Name)) ||
            _palettes.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            MessageBox.Show(this, "Названия палитр должны быть непустыми и уникальными.", "Палитра", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _store.Save(_palettes);
        RefreshPalettes(SelectedPalette);
        return true;
    }

    private bool CaptureEditor(DynamicPalette palette)
    {
        string name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name) || !TryDouble(RangeBox.Text, out double range) || range <= 0 ||
            !TryDouble(ZeroBox.Text, out double zero) || zero <= 0 || palette.Colors.Count < 2)
        {
            MessageBox.Show(this, "Проверьте название, числовые параметры и оставьте минимум два цвета.", "Палитра", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        palette.Name = name;
        palette.Mode = (ModeBox.SelectedItem as ModeChoice)?.Value ?? "Diverging";
        palette.ExponentRange = range;
        palette.ZeroBandWidth = zero;
        return true;
    }

    private void RefreshPalettes(DynamicPalette? selected)
    {
        PaletteList.ItemsSource = null;
        PaletteList.ItemsSource = _palettes;
        PaletteList.SelectedItem = selected ?? _palettes.FirstOrDefault();
    }

    private void RefreshColors(int selectedIndex)
    {
        if (SelectedPalette is not { } palette) return;
        ColorList.ItemsSource = null;
        ColorList.ItemsSource = palette.Colors;
        ColorList.SelectedIndex = palette.Colors.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, palette.Colors.Count - 1);
        UpdatePreview();
        UpdateEditState();
    }

    private void UpdateEditState()
    {
        bool editable = CanEdit;
        int index = ColorList.SelectedIndex;
        NameBox.IsEnabled = editable;
        ModeBox.IsEnabled = editable;
        RangeBox.IsEnabled = editable;
        ZeroBox.IsEnabled = editable;
        ColorList.IsHitTestVisible = editable;
        DeleteButton.IsEnabled = editable;
        AddButton.IsEnabled = editable;
        RandomButton.IsEnabled = editable;
        EditButton.IsEnabled = editable && index >= 0;
        RemoveButton.IsEnabled = editable && index >= 0 && SelectedPalette!.Colors.Count > 2;
        UpButton.IsEnabled = editable && index > 0;
        DownButton.IsEnabled = editable && index >= 0 && index < SelectedPalette!.Colors.Count - 1;
        EditHint.Text = editable
            ? "Изменения сохраняются для пользовательской палитры. Нажмите «Применить», чтобы сразу обновить фрактал."
            : "Встроенная палитра доступна только для просмотра и применения. Создайте копию для редактирования.";
    }

    private void UpdatePreview()
    {
        if (SelectedPalette is not { } source) return;
        DynamicPalette palette = source.Clone();
        if (CanEdit)
        {
            if (ModeBox.SelectedItem is ModeChoice mode) palette.Mode = mode.Value;
            if (TryDouble(RangeBox.Text, out double range) && range > 0) palette.ExponentRange = range;
            if (TryDouble(ZeroBox.Text, out double zero) && zero > 0) palette.ZeroBandWidth = zero;
        }
        var mapped = new LyapunovColorPalette
        {
            Mode = Enum.TryParse(palette.Mode, out LyapunovColoringMode parsedMode) ? parsedMode : LyapunovColoringMode.Diverging,
            ExponentRange = palette.ExponentRange,
            ZeroBandWidth = palette.ZeroBandWidth,
            Colors = palette.Colors.Select(c => DrawingColor.FromArgb(c.A, c.R, c.G, c.B)).ToList()
        };
        const int width = 512;
        byte[] pixels = new byte[width * 4];
        for (int x = 0; x < width; x++)
        {
            double exponent = (x / (double)(width - 1) * 2 - 1) * mapped.ExponentRange;
            DrawingColor color = LyapunovColoring.MapExponent(exponent, mapped);
            int offset = x * 4;
            pixels[offset] = color.B; pixels[offset + 1] = color.G; pixels[offset + 2] = color.R; pixels[offset + 3] = color.A;
        }
        PreviewImage.Source = BitmapSource.Create(width, 1, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private string UniqueName(string basis)
    {
        string name = basis;
        for (int suffix = 1; _palettes.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); suffix++) name = $"{basis} {suffix}";
        return name;
    }

    private double RandomBetween(double min, double max) => min + _random.NextDouble() * (max - min);
    private static Color FromHsv(double hue, double saturation, double value)
    {
        hue = (hue % 360 + 360) % 360;
        double chroma = value * saturation, h = hue / 60, x = chroma * (1 - Math.Abs(h % 2 - 1));
        (double r, double g, double b) = h switch
        {
            < 1 => (chroma, x, 0d), < 2 => (x, chroma, 0d), < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma), < 5 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        double m = value - chroma;
        return Color.FromRgb(Channel(r + m), Channel(g + m), Channel(b + m));
    }

    private static byte Channel(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
    private static bool TryDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private sealed record ModeChoice(string Value, string DisplayName);
}
