using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.ColorPicking;
using FractalExplorerWPF.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

/// <summary>
/// Менеджер палитр трёхмерных фракталов: список библиотеки слева, редактор градиента и окружения
/// справа. Рядом с цветами идёт настоящий кадр фрактала — палитра видна сразу на той фигуре и с
/// того ракурса, что открыты в окне, поэтому подбирать цвета вслепую не приходится.
/// </summary>
public partial class Fractal3DPaletteWindow : Window
{
    /// <summary>Пауза перед пересчётом предпросмотра: правка цвета не должна дёргать GPU.</summary>
    private const double PreviewDelayMs = 140;

    private const int PreviewWidth = 260;
    private const int PreviewHeight = 180;

    private readonly Fractal3DPaletteManager _manager;
    private readonly ColorSelectionService _colorSelection = ColorSelectionService.Default;
    private readonly List<Color> _editingColors = [];
    private readonly Func<Fractal3DPalette, int, int, CancellationToken, Task<BitmapSource>>? _renderPreview;
    private readonly DispatcherTimer _previewTimer;

    private CancellationTokenSource? _previewCts;
    private Fractal3DPalette? _selected;
    private bool _updatingUi;
    private bool _closing;

    public Fractal3DPaletteWindow(
        Fractal3DPaletteManager manager,
        Fractal3DPalette current,
        Func<Fractal3DPalette, int, int, CancellationToken, Task<BitmapSource>>? renderPreview = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(current);
        InitializeComponent();
        _manager = manager;
        _renderPreview = renderPreview;
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PreviewDelayMs) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            _ = RefreshPreviewAsync();
        };

        // Палитра открытого вида может быть не из библиотеки — например, пришла из сохранения.
        // Тогда она показывается первой строкой списка, чтобы её можно было скопировать и править.
        if (_manager.Find(current.Name) is null) _manager.Palettes.Insert(0, current.Clone());
        RefreshList(_manager.Find(current.Name));
    }

    /// <summary>Палитра, которую пользователь применил к открытому виду.</summary>
    public event EventHandler<Fractal3DPalette>? PaletteApplied;

    private bool CanEdit => _selected is { IsBuiltIn: false };

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        _previewTimer.Stop();
        _previewCts?.Cancel();
        base.OnClosed(e);
    }

    private void RefreshList(Fractal3DPalette? selection)
    {
        PaletteList.ItemsSource = null;
        PaletteList.ItemsSource = _manager.Palettes;
        PaletteList.SelectedItem = selection ?? _manager.Palettes[0];
    }

    private void PaletteList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PaletteList.SelectedItem is not Fractal3DPalette palette) return;
        _selected = palette;
        _updatingUi = true;
        NameBox.Text = palette.Name;
        GradientBox.IsChecked = palette.IsGradient;
        GammaBox.Text = palette.Gamma.ToString(CultureInfo.InvariantCulture);
        EnvironmentBox.IsChecked = palette.OverridesEnvironment;
        BackgroundTopSelector.SelectedColor = palette.BackgroundTop;
        BackgroundBottomSelector.SelectedColor = palette.BackgroundBottom;
        LightColorSelector.SelectedColor = palette.LightColor;
        SurfaceColorSelector.SelectedColor = palette.SurfaceColor;
        _editingColors.Clear();
        _editingColors.AddRange(palette.Colors);
        _updatingUi = false;
        RefreshColors(0);
        UpdateEditState();
    }

    private void New_OnClick(object sender, RoutedEventArgs e)
    {
        var palette = new Fractal3DPalette { Name = UniqueName("Новая палитра") };
        _manager.Palettes.Add(palette);
        RefreshList(palette);
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        Fractal3DPalette copy = _selected.Clone(UniqueName($"{_selected.Name} копия"));
        _manager.Palettes.Add(copy);
        RefreshList(copy);
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || _selected is null) return;
        if (MessageBox.Show(this, $"Удалить «{_selected.Name}»?", Title,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _manager.Palettes.Remove(_selected);
        _manager.SaveCustomPalettes();
        RefreshList(_manager.Palettes[0]);
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
        _manager.SaveCustomPalettes();
        PaletteList.Items.Refresh();
        PaletteApplied?.Invoke(this, _selected.Clone());
    }

    private void Add_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || _editingColors.Count >= Fractal3DPalette.MaxColors ||
            !_colorSelection.TrySelectColor(this, Colors.White, out Color color)) return;
        _editingColors.Add(color);
        RefreshColors(_editingColors.Count - 1);
    }

    private void Edit_OnClick(object sender, RoutedEventArgs e) => EditSelected();

    private void ColorList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelected();

    private void EditSelected()
    {
        int index = ColorList.SelectedIndex;
        if (!CanEdit || index < 0 ||
            !_colorSelection.TrySelectColor(this, _editingColors[index], out Color color)) return;
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

    private void Reverse_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit) return;
        _editingColors.Reverse();
        RefreshColors(_editingColors.Count - 1 - Math.Max(ColorList.SelectedIndex, 0));
    }

    private void Random_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanEdit) return;
        int count = Random.Shared.Next(3, 7);
        double baseHue = Random.Shared.NextDouble() * 360;
        double spread = 40 + Random.Shared.NextDouble() * 280;
        _editingColors.Clear();
        for (int index = 0; index < count; index++)
        {
            double position = index / (double)(count - 1);
            _editingColors.Add(FromHsv(
                (baseHue + spread * position) % 360,
                0.35 + Random.Shared.NextDouble() * 0.6,
                0.18 + 0.8 * position));
        }
        RefreshColors(0);
    }

    private void Editor_OnChanged(object sender, EventArgs e)
    {
        if (_updatingUi) return;
        UpdatePreview();
        SchedulePreview();
    }

    private void ColorList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private bool ApplyEdits()
    {
        if (_selected is null) return false;
        if (_selected.IsBuiltIn) return true;
        if (string.IsNullOrWhiteSpace(NameBox.Text) || _editingColors.Count == 0 ||
            !double.TryParse(GammaBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double gamma) ||
            gamma is < 0.1 or > 5)
        {
            MessageBox.Show(this, "Проверьте название, цвета и гамму палитры.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _selected.Name = NameBox.Text.Trim();
        _selected.Colors = [.. _editingColors];
        _selected.IsGradient = GradientBox.IsChecked == true;
        _selected.Gamma = gamma;
        _selected.OverridesEnvironment = EnvironmentBox.IsChecked == true;
        _selected.BackgroundTop = BackgroundTopSelector.SelectedColor;
        _selected.BackgroundBottom = BackgroundBottomSelector.SelectedColor;
        _selected.LightColor = LightColorSelector.SelectedColor;
        _selected.SurfaceColor = SurfaceColorSelector.SelectedColor;
        return true;
    }

    /// <summary>Палитра ровно в том виде, в каком она сейчас в редакторе, — без проверок.</summary>
    private Fractal3DPalette BuildEditingPalette() => new()
    {
        Name = NameBox.Text,
        Colors = _editingColors.Count > 0 ? [.. _editingColors] : [Colors.White],
        IsGradient = GradientBox.IsChecked == true,
        Gamma = double.TryParse(GammaBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double gamma)
            ? Math.Clamp(gamma, 0.1, 5)
            : 1,
        OverridesEnvironment = EnvironmentBox.IsChecked == true,
        BackgroundTop = BackgroundTopSelector.SelectedColor,
        BackgroundBottom = BackgroundBottomSelector.SelectedColor,
        LightColor = LightColorSelector.SelectedColor,
        SurfaceColor = SurfaceColorSelector.SelectedColor
    };

    private void RefreshColors(int selection)
    {
        ColorList.ItemsSource = null;
        ColorList.ItemsSource = _editingColors;
        ColorList.SelectedIndex = _editingColors.Count == 0
            ? -1
            : Math.Clamp(selection, 0, _editingColors.Count - 1);
        UpdatePreview();
        UpdateButtons();
        SchedulePreview();
    }

    private void UpdatePreview()
    {
        if (_editingColors.Count == 0)
        {
            GradientPreview.Background = MediaBrushes.Transparent;
            return;
        }
        if (_editingColors.Count == 1 || GradientBox.IsChecked != true)
        {
            GradientPreview.Background = BuildBandBrush();
            return;
        }
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int index = 0; index < _editingColors.Count; index++)
            brush.GradientStops.Add(new GradientStop(_editingColors[index], index / (double)(_editingColors.Count - 1)));
        GradientPreview.Background = brush;
    }

    /// <summary>Полосы рисуются жёсткими границами — так же, как их берёт шейдер.</summary>
    private Brush BuildBandBrush()
    {
        if (_editingColors.Count == 1) return new SolidColorBrush(_editingColors[0]);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int index = 0; index < _editingColors.Count; index++)
        {
            double start = index / (double)_editingColors.Count;
            double end = (index + 1) / (double)_editingColors.Count;
            brush.GradientStops.Add(new GradientStop(_editingColors[index], start));
            brush.GradientStops.Add(new GradientStop(_editingColors[index], end));
        }
        return brush;
    }

    private void SchedulePreview()
    {
        if (_closing || _renderPreview is null) return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async Task RefreshPreviewAsync()
    {
        if (_closing || _renderPreview is null) return;
        _previewCts?.Cancel();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        try
        {
            BitmapSource preview = await _renderPreview(
                BuildEditingPalette(), PreviewWidth, PreviewHeight, cts.Token);
            if (cts.IsCancellationRequested || _closing) return;
            PreviewImage.Source = preview;
            PreviewHint.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PreviewHint.Text = exception.Message;
            PreviewHint.Visibility = Visibility.Visible;
        }
        finally
        {
            if (ReferenceEquals(_previewCts, cts)) _previewCts = null;
            cts.Dispose();
        }
    }

    private void UpdateEditState()
    {
        bool editable = CanEdit;
        NameBox.IsEnabled = editable;
        GradientBox.IsEnabled = editable;
        GammaBox.IsEnabled = editable;
        DeleteButton.IsEnabled = editable;
        RandomButton.IsEnabled = editable;
        ReverseButton.IsEnabled = editable;
        EnvironmentBox.IsEnabled = editable;
        BackgroundTopSelector.IsEnabled = editable;
        BackgroundBottomSelector.IsEnabled = editable;
        LightColorSelector.IsEnabled = editable;
        SurfaceColorSelector.IsEnabled = editable;
        EditHint.Text = editable
            ? "Палитра хранится отдельно от сохранений и доступна всем шести видам трёхмерных фракталов. " +
              "Окружение применяется только при включённом флажке — встроенные палитры свет и фон не трогают."
            : "Встроенную палитру можно применить или скопировать для редактирования.";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        int index = ColorList.SelectedIndex;
        AddButton.IsEnabled = CanEdit && _editingColors.Count < Fractal3DPalette.MaxColors;
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
