using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private readonly Stack<KifsSettings> _kifsHistory = new();

    private KifsSettings CaptureKifs() => new()
    {
        Symmetry = (KifsSymmetry)Math.Max(0, KifsSymmetryBox.SelectedIndex),
        Seed = (KifsSeed)Math.Max(0, KifsSeedBox.SelectedIndex),
        Sectors = ReadInt(KifsSectorsBox, "Число лучей", 3, 16),
        Scale = ReadDouble(KifsScaleBox, "Масштаб KIFS", 1.2, 4),
        Radius = ReadDouble(KifsRadiusBox, "Размер исходной формы", .1, 2),
        RotationX = ReadDouble(KifsRotationXBox, "Поворот X", -180, 180),
        RotationY = ReadDouble(KifsRotationYBox, "Поворот Y", -180, 180),
        RotationZ = ReadDouble(KifsRotationZBox, "Поворот Z", -180, 180),
        OffsetX = ReadDouble(KifsOffsetXBox, "Смещение X", -2, 2),
        OffsetY = ReadDouble(KifsOffsetYBox, "Смещение Y", -2, 2),
        OffsetZ = ReadDouble(KifsOffsetZBox, "Смещение Z", -2, 2)
    };

    private void LoadKifs(KifsSettings? settings)
    {
        KifsSettings s = (settings ?? new()).Normalized();
        KifsSymmetryBox.SelectedIndex = (int)s.Symmetry;
        KifsSeedBox.SelectedIndex = (int)s.Seed;
        KifsSectorsBox.Text = s.Sectors.ToString();
        KifsScaleSlider.Value = s.Scale;
        KifsRadiusSlider.Value = s.Radius;
        KifsRotationXSlider.Value = s.RotationX;
        KifsRotationYSlider.Value = s.RotationY;
        KifsRotationZSlider.Value = s.RotationZ;
        KifsOffsetXSlider.Value = s.OffsetX;
        KifsOffsetYSlider.Value = s.OffsetY;
        KifsOffsetZSlider.Value = s.OffsetZ;
        // Preserve imported precision while retaining slider bindings.
        (TextBox Box, double Value)[] fields =
        [
            (KifsScaleBox, s.Scale), (KifsRadiusBox, s.Radius),
            (KifsRotationXBox, s.RotationX), (KifsRotationYBox, s.RotationY), (KifsRotationZBox, s.RotationZ),
            (KifsOffsetXBox, s.OffsetX), (KifsOffsetYBox, s.OffsetY), (KifsOffsetZBox, s.OffsetZ)
        ];
        foreach (var field in fields)
            field.Box.SetCurrentValue(TextBox.TextProperty, field.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        UpdateKifsMap();
    }

    private void Kifs_OnChanged(object sender, EventArgs e)
    {
        if (KifsOffsetMap is null || KifsSectorsPanel is null) return;
        KifsSectorsPanel.Visibility = KifsSymmetryBox.SelectedIndex == (int)KifsSymmetry.Dihedral
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateKifsMap();
        Parameter_OnChanged(sender, e);
    }

    private (Slider Horizontal, Slider Vertical, string X, string Y) KifsMapAxes =>
        KifsPlaneBox.SelectedIndex switch
        {
            1 => (KifsOffsetXSlider, KifsOffsetZSlider, "X", "Z"),
            2 => (KifsOffsetYSlider, KifsOffsetZSlider, "Y", "Z"),
            _ => (KifsOffsetXSlider, KifsOffsetYSlider, "X", "Y")
        };

    private void UpdateKifsMap()
    {
        if (KifsOffsetMap is null || KifsOffsetXSlider is null || KifsOffsetYSlider is null ||
            KifsOffsetZSlider is null || KifsPlaneBox is null) return;
        var axes = KifsMapAxes;
        double width = KifsOffsetMap.ActualWidth, height = KifsOffsetMap.ActualHeight;
        if (width <= 0 || height <= 0) return;
        KifsOffsetMap.Children.Clear();
        Brush grid = (Brush)FindResource("Theme.BorderBrush");
        Brush accent = (Brush)FindResource("Theme.PrimaryTextBrush");
        for (int i = 0; i <= 4; i++)
        {
            KifsOffsetMap.Children.Add(new Line { X1 = width*i/4, X2 = width*i/4, Y2 = height, Stroke = grid });
            KifsOffsetMap.Children.Add(new Line { Y1 = height*i/4, Y2 = height*i/4, X2 = width, Stroke = grid });
        }
        double x = (axes.Horizontal.Value + 2) / 4 * width;
        double y = (2 - axes.Vertical.Value) / 4 * height;
        KifsOffsetMap.Children.Add(new Line { X1 = width/2, Y1 = height/2, X2 = x, Y2 = y, Stroke = accent, StrokeThickness = 2 });
        var marker = new Ellipse { Width = 12, Height = 12, Fill = accent, IsHitTestVisible = false };
        Canvas.SetLeft(marker, x - 6); Canvas.SetTop(marker, y - 6);
        KifsOffsetMap.Children.Add(marker);
        var label = new TextBlock { Text = $"{axes.X} →   {axes.Y} ↑   −2 … +2", Foreground = accent, IsHitTestVisible = false };
        Canvas.SetLeft(label, 6); Canvas.SetTop(label, 4);
        KifsOffsetMap.Children.Add(label);
    }

    private void KifsMap_OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateKifsMap();
    private void KifsMap_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _kifsHistory.Push(CaptureKifs());
        KifsOffsetMap.CaptureMouse();
        SetKifsMapPoint(e.GetPosition(KifsOffsetMap));
        e.Handled = true;
    }
    private void KifsMap_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (KifsOffsetMap.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
            SetKifsMapPoint(e.GetPosition(KifsOffsetMap));
    }
    private void KifsMap_OnMouseUp(object sender, MouseButtonEventArgs e) => KifsOffsetMap.ReleaseMouseCapture();
    private void SetKifsMapPoint(Point point)
    {
        var axes = KifsMapAxes;
        bool previous = _updatingUi;
        _updatingUi = true;
        axes.Horizontal.Value = Math.Round(Math.Clamp(point.X / Math.Max(1, KifsOffsetMap.ActualWidth) * 4 - 2, -2, 2), 3);
        axes.Vertical.Value = Math.Round(Math.Clamp(2 - point.Y / Math.Max(1, KifsOffsetMap.ActualHeight) * 4, -2, 2), 3);
        _updatingUi = previous;
        UpdateKifsMap();
        if (!previous) ScheduleRender(immediate: true);
    }

    private void ApplyKifsEdit(KifsSettings settings)
    {
        _updatingUi = true;
        LoadKifs(settings);
        _updatingUi = false;
        ScheduleRender(immediate: true);
    }
    private void KifsRandom_OnClick(object sender, RoutedEventArgs e)
    {
        KifsSettings current = CaptureKifs();
        _kifsHistory.Push(current);
        ApplyKifsEdit(KifsRandomizer.Create(Random.Shared, KifsNearbyBox.IsChecked == true ? current : null));
    }
    private void KifsUndo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_kifsHistory.TryPop(out KifsSettings? settings)) ApplyKifsEdit(settings);
    }
    private void KifsResetRotation_OnClick(object sender, RoutedEventArgs e)
    {
        KifsSettings current = CaptureKifs();
        _kifsHistory.Push(current.Clone());
        current.RotationX = current.RotationY = current.RotationZ = 0;
        ApplyKifsEdit(current);
    }
}
