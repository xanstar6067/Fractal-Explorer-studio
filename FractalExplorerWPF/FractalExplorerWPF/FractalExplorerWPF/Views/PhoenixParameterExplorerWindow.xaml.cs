using FractalExplorerWPF.Controls;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class PhoenixParameterExplorerWindow : Window
{
    private readonly PhoenixState _template;
    private readonly DispatcherTimer _c1Timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _c2Timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly PhoenixSliceRange _c1Range = new();
    private readonly PhoenixSliceRange _c2Range = new();
    private readonly PhoenixSliceRange _renderedC1Range = new();
    private readonly PhoenixSliceRange _renderedC2Range = new();
    private bool _hasRenderedC1;
    private bool _hasRenderedC2;
    private CancellationTokenSource? _c1Cts;
    private CancellationTokenSource? _c2Cts;
    private bool _updating;
    private bool _panning;
    private bool _panC1Plane;
    private Point _panStart;
    private bool _closing;
    private bool Is3D => Mode3D?.IsChecked == true;

    public decimal SelectedC1Real { get; private set; }
    public decimal SelectedC1Imaginary { get; private set; }
    public decimal SelectedC2Real { get; private set; }
    public decimal SelectedC2Imaginary { get; private set; }
    public bool OpenC1AsJulia { get; private set; }

    public PhoenixParameterExplorerWindow(PhoenixState state)
    {
        _template = state;
        SelectedC1Real = state.C1Real;
        SelectedC1Imaginary = state.C1Imaginary;
        SelectedC2Real = state.C2Real;
        SelectedC2Imaginary = state.C2Imaginary;
        InitializeComponent();
        var deviceHost = new Direct3DDeviceHost();
        C1Slice.Initialize(deviceHost, true);
        C2Slice.Initialize(deviceHost, false);
        deviceHost.Release(); // the two views own the remaining references
        C1Slice.PointSelected += point => SelectSlicePoint(true, point);
        C2Slice.PointSelected += point => SelectSlicePoint(false, point);
        _c1Timer.Tick += (_, _) => { _c1Timer.Stop(); _ = RenderPlaneAsync(true); };
        _c2Timer.Tick += (_, _) => { _c2Timer.Stop(); _ = RenderPlaneAsync(false); };
        SetText();
        UpdateLabels();
        Loaded += (_, _) => { if (Is3D) UpdateSlices(); else { SchedulePlane(true); SchedulePlane(false); } };
        NumericInput.Decimal(this, C1RealBox, "Re C1", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, C1ImaginaryBox, "Im C1", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, C2RealBox, "Re C2", decimal.MinValue, decimal.MaxValue);
        NumericInput.Decimal(this, C2ImaginaryBox, "Im C2", decimal.MinValue, decimal.MaxValue);

    }

    private void SetText()
    {
        _updating = true;
        C1RealBox.Text = Format(SelectedC1Real);
        C1ImaginaryBox.Text = Format(SelectedC1Imaginary);
        C2RealBox.Text = Format(SelectedC2Real);
        C2ImaginaryBox.Text = Format(SelectedC2Imaginary);
        _updating = false;
    }

    private void ParameterText_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || !IsInitialized) return;
        _updating = true;
        try { NumericInput.Prepare(this); }
        finally { _updating = false; }
        if (!NumericInput.IsValid(C1RealBox) || !NumericInput.IsValid(C1ImaginaryBox) ||
            !NumericInput.IsValid(C2RealBox) || !NumericInput.IsValid(C2ImaginaryBox)) return;
        decimal oldC1Real = SelectedC1Real, oldC1Imaginary = SelectedC1Imaginary;
        decimal oldC2Real = SelectedC2Real, oldC2Imaginary = SelectedC2Imaginary;
        if (TryRead(C1RealBox.Text, out decimal c1r)) SelectedC1Real = c1r;
        if (TryRead(C1ImaginaryBox.Text, out decimal c1i)) SelectedC1Imaginary = c1i;
        if (TryRead(C2RealBox.Text, out decimal c2r)) SelectedC2Real = c2r;
        if (TryRead(C2ImaginaryBox.Text, out decimal c2i)) SelectedC2Imaginary = c2i;
        if (Is3D) UpdateSlices();
        if (oldC2Real != SelectedC2Real || oldC2Imaginary != SelectedC2Imaginary) SchedulePlane(true);
        if (oldC1Real != SelectedC1Real || oldC1Imaginary != SelectedC1Imaginary) SchedulePlane(false);
        UpdateLabels(); UpdateMarkers();
    }

    private void UpdateLabels()
    {
        if (C1FixedText is null) return;
        C1FixedText.Text = Is3D ? $"Im C2 = {SelectedC2Imaginary:G8} · X: Re C1, Y: Im C1, Z: Re C2"
            : $"C2 фиксировано: {Complex(SelectedC2Real, SelectedC2Imaginary)}";
        C2FixedText.Text = Is3D ? $"Im C1 = {SelectedC1Imaginary:G8} · X: Re C2, Y: Im C2, Z: Re C1"
            : $"C1 фиксировано: {Complex(SelectedC1Real, SelectedC1Imaginary)}";
        FormulaText.Text = $"{_template.Variant}: zₙ₊₁ = F(zₙ)^{_template.PrimaryPower} + C1·F(zₙ)^{_template.SecondaryPower} + C2·zₙ₋₁" +
                           (UsesAutomaticParameterStart()
                               ? " · Для невырожденных карт при b > 0 автоматически используется z₀ = 1, z₋₁ = 0."
                               : string.Empty);
        StatusText.Text = $"C1 = {Complex(SelectedC1Real, SelectedC1Imaginary)} · C2 = {Complex(SelectedC2Real, SelectedC2Imaginary)}";
    }

    private void SchedulePlane(bool c1Plane)
    {
        if (!IsLoaded || Is3D || _closing) return;
        CancellationTokenSource? cts = c1Plane ? _c1Cts : _c2Cts;
        cts?.Cancel();
        DispatcherTimer timer = c1Plane ? _c1Timer : _c2Timer;
        timer.Stop(); timer.Start();
    }

    private async Task RenderPlaneAsync(bool c1Plane)
    {
        if (Is3D || _closing || !IsLoaded) return;
        Border host = c1Plane ? C1PlaneHost : C2PlaneHost;
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(host);
        DpiScale dpi = surface.Dpi;
        int width = surface.PixelWidth;
        int height = surface.PixelHeight;
        var cts = new CancellationTokenSource();
        if (c1Plane) { _c1Cts?.Cancel(); _c1Cts = cts; }
        else { _c2Cts?.Cancel(); _c2Cts = cts; }
        ProgressBar progress = c1Plane ? C1Progress : C2Progress;
        try
        {
            byte[] pixels = new byte[checked(width * height * 4)];
            PhoenixSliceRange currentRange = c1Plane ? _c1Range : _c2Range;
            var range = new PhoenixSliceRange
            {
                MinX = currentRange.MinX,
                MaxX = currentRange.MaxX,
                MinY = currentRange.MinY,
                MaxY = currentRange.MaxY
            };
            PhoenixState snapshot = CreateRenderState();
            PhoenixParameterPlane plane = c1Plane ? PhoenixParameterPlane.C1 : PhoenixParameterPlane.C2;
            IProgress<int> reporter = new Progress<int>(value =>
            {
                if (!_closing && !Is3D && ReferenceEquals(c1Plane ? _c1Cts : _c2Cts, cts)) progress.Value = value;
            });
            await Task.Run(() => PhoenixRenderer.RenderParameterPlane(snapshot, pixels, width, height, width * 4,
                range, plane, Environment.ProcessorCount, cts.Token, value => reporter.Report(value)));
            if (cts.Token.IsCancellationRequested || Is3D || _closing) return;
            BitmapSource bitmap = BitmapSource.Create(width, height, dpi.PixelsPerInchX,
                dpi.PixelsPerInchY, PixelFormats.Bgra32, null, pixels, width * 4);
            bitmap.Freeze();
            if (c1Plane)
            {
                C1PlaneImage.Source = bitmap;
                CopyRange(range, _renderedC1Range);
                _hasRenderedC1 = true;
            }
            else
            {
                C2PlaneImage.Source = bitmap;
                CopyRange(range, _renderedC2Range);
                _hasRenderedC2 = true;
            }
            UpdatePlaneTransform(c1Plane);
            UpdateMarkers();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closing && !Is3D) StatusText.Text = $"Ошибка плоскости {(c1Plane ? "C1" : "C2")}: {ex.Message}"; }
        finally
        {
            if (ReferenceEquals(c1Plane ? _c1Cts : _c2Cts, cts))
            {
                if (c1Plane) _c1Cts = null; else _c2Cts = null;
                progress.Value = 0;
            }
            cts.Dispose();
        }
    }

    private PhoenixState CreateRenderState() => new()
    {
        CenterX = _template.CenterX,
        CenterY = _template.CenterY,
        Zoom = _template.Zoom,
        Threshold = _template.Threshold,
        Iterations = Math.Min(_template.Iterations, 1000),
        C1Real = SelectedC1Real,
        C1Imaginary = SelectedC1Imaginary,
        C2Real = SelectedC2Real,
        C2Imaginary = SelectedC2Imaginary,
        PlaneMode = _template.PlaneMode,
        Variant = _template.Variant,
        PrimaryPower = _template.PrimaryPower,
        SecondaryPower = _template.SecondaryPower,
        InitialZReal = _template.InitialZReal,
        InitialZImaginary = _template.InitialZImaginary,
        InitialPreviousReal = _template.InitialPreviousReal,
        InitialPreviousImaginary = _template.InitialPreviousImaginary,
        ColoringMode = _template.ColoringMode,
        OrbitTrapMode = _template.OrbitTrapMode,
        OrbitTrapRadius = _template.OrbitTrapRadius,
        OrbitTrapStrength = _template.OrbitTrapStrength,
        StripeFrequency = _template.StripeFrequency,
        StripeStrength = _template.StripeStrength,
        CycleTolerance = _template.CycleTolerance,
        MaximumDetectedPeriod = _template.MaximumDetectedPeriod,
        Palette = _template.Palette.Clone(_template.Palette.Name)
    };

    private bool UsesAutomaticParameterStart() => _template.SecondaryPower > 0 &&
                                                   _template.InitialZReal == 0 &&
                                                   _template.InitialZImaginary == 0 &&
                                                   _template.InitialPreviousReal == 0 &&
                                                   _template.InitialPreviousImaginary == 0;

    private static bool IsC1Plane(object sender) => sender is FrameworkElement { Tag: "C1" };

    private void PlaneHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning) return;
        bool c1Plane = IsC1Plane(sender);
        Border host = (Border)sender;
        PhoenixSliceRange range = c1Plane ? _c1Range : _c2Range;
        Point point = e.GetPosition(host);
        decimal real = (decimal)(range.MinX + point.X / Math.Max(1, host.ActualWidth) * (range.MaxX - range.MinX));
        decimal imaginary = (decimal)(range.MaxY - point.Y / Math.Max(1, host.ActualHeight) * (range.MaxY - range.MinY));
        if (c1Plane)
        {
            SelectedC1Real = real; SelectedC1Imaginary = imaginary; SchedulePlane(false);
        }
        else
        {
            SelectedC2Real = real; SelectedC2Imaginary = imaginary; SchedulePlane(true);
        }
        SetText(); UpdateLabels(); UpdateMarkers(); e.Handled = true;
    }

    private void PlaneHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        bool c1Plane = IsC1Plane(sender);
        Border host = (Border)sender;
        PhoenixSliceRange range = c1Plane ? _c1Range : _c2Range;
        Point point = e.GetPosition(host);
        double fx = point.X / Math.Max(1, host.ActualWidth);
        double fy = point.Y / Math.Max(1, host.ActualHeight);
        double mouseX = range.MinX + fx * (range.MaxX - range.MinX);
        double mouseY = range.MaxY - fy * (range.MaxY - range.MinY);
        double factor = e.Delta > 0 ? 1.25 : 1 / 1.25;
        double width = (range.MaxX - range.MinX) / factor;
        double height = (range.MaxY - range.MinY) / factor;
        if (width is < 1e-12 or > 1e4 || height is < 1e-12 or > 1e4) return;
        range.MinX = mouseX - fx * width; range.MaxX = range.MinX + width;
        range.MinY = mouseY - (1 - fy) * height; range.MaxY = range.MinY + height;
        UpdateMarkers(); UpdatePlaneTransform(c1Plane); SchedulePlane(c1Plane); e.Handled = true;
    }

    private void PlaneHost_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        _panning = true; _panC1Plane = IsC1Plane(sender); _panStart = e.GetPosition((Border)sender);
        ((Border)sender).CaptureMouse(); Mouse.OverrideCursor = Cursors.SizeAll; e.Handled = true;
    }

    private void PlaneHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning || e.MiddleButton != MouseButtonState.Pressed || _panC1Plane != IsC1Plane(sender)) return;
        Border host = (Border)sender;
        Point current = e.GetPosition(host);
        PhoenixSliceRange range = _panC1Plane ? _c1Range : _c2Range;
        double dx = (current.X - _panStart.X) * (range.MaxX - range.MinX) / Math.Max(1, host.ActualWidth);
        double dy = (current.Y - _panStart.Y) * (range.MaxY - range.MinY) / Math.Max(1, host.ActualHeight);
        range.MinX -= dx; range.MaxX -= dx; range.MinY += dy; range.MaxY += dy; _panStart = current;
        UpdateMarkers(); UpdatePlaneTransform(_panC1Plane); e.Handled = true;
    }

    private void PlaneHost_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !_panning) return;
        _panning = false; ((Border)sender).ReleaseMouseCapture(); Mouse.OverrideCursor = null;
        SchedulePlane(IsC1Plane(sender)); e.Handled = true;
    }

    private void PlaneHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool c1Plane = IsC1Plane(sender); UpdateMarkers(); UpdatePlaneTransform(c1Plane); SchedulePlane(c1Plane);
    }

    private void UpdateMarkers()
    {
        if (!IsInitialized) return;
        UpdateMarker(C1PlaneHost, C1MarkerLayer, C1MarkerHorizontal, C1MarkerVertical, _c1Range,
            (double)SelectedC1Real, (double)SelectedC1Imaginary);
        UpdateMarker(C2PlaneHost, C2MarkerLayer, C2MarkerHorizontal, C2MarkerVertical, _c2Range,
            (double)SelectedC2Real, (double)SelectedC2Imaginary);
    }

    private static void UpdateMarker(Border host, Canvas layer, Line horizontal, Line vertical,
        PhoenixSliceRange range, double real, double imaginary)
    {
        double width = Math.Max(1, host.ActualWidth), height = Math.Max(1, host.ActualHeight);
        double x = (real - range.MinX) / (range.MaxX - range.MinX) * width;
        double y = (range.MaxY - imaginary) / (range.MaxY - range.MinY) * height;
        bool visible = x >= 0 && x <= width && y >= 0 && y <= height;
        layer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        horizontal.X1 = 0; horizontal.X2 = width; horizontal.Y1 = y; horizontal.Y2 = y;
        vertical.X1 = x; vertical.X2 = x; vertical.Y1 = 0; vertical.Y2 = height;
    }

    private void Reset_OnClick(object sender, RoutedEventArgs e)
    {
        if (Is3D) { C1Slice.ResetView(); C2Slice.ResetView(); return; }
        _c1Range.Reset(); _c2Range.Reset(); UpdateMarkers();
        UpdatePlaneTransform(true); UpdatePlaneTransform(false);
        SchedulePlane(true); SchedulePlane(false);
    }

    private void UpdatePlaneTransform(bool c1Plane)
    {
        bool hasFrame = c1Plane ? _hasRenderedC1 : _hasRenderedC2;
        if (!hasFrame) return;
        System.Windows.Controls.Image image = c1Plane ? C1PlaneImage : C2PlaneImage;
        Border host = c1Plane ? C1PlaneHost : C2PlaneHost;
        PhoenixSliceRange rendered = c1Plane ? _renderedC1Range : _renderedC2Range;
        PhoenixSliceRange current = c1Plane ? _c1Range : _c2Range;
        double currentWidth = current.MaxX - current.MinX;
        double currentHeight = current.MaxY - current.MinY;
        if (currentWidth <= 0 || currentHeight <= 0 || host.ActualWidth <= 0 || host.ActualHeight <= 0) return;
        double scaleX = (rendered.MaxX - rendered.MinX) / currentWidth;
        double scaleY = (rendered.MaxY - rendered.MinY) / currentHeight;
        double offsetX = (rendered.MinX - current.MinX) / currentWidth * host.ActualWidth;
        double offsetY = (current.MaxY - rendered.MaxY) / currentHeight * host.ActualHeight;
        image.RenderTransformOrigin = new Point(0, 0);
        image.RenderTransform = new MatrixTransform(scaleX, 0, 0, scaleY, offsetX, offsetY);
    }

    private static void CopyRange(PhoenixSliceRange source, PhoenixSliceRange target)
    {
        target.MinX = source.MinX; target.MaxX = source.MaxX;
        target.MinY = source.MinY; target.MaxY = source.MaxY;
    }

    private bool ReadSelectedValues()
    {
        if (!TryRead(C1RealBox.Text, out decimal c1r) || !TryRead(C1ImaginaryBox.Text, out decimal c1i) ||
            !TryRead(C2RealBox.Text, out decimal c2r) || !TryRead(C2ImaginaryBox.Text, out decimal c2i))
        {
            MessageBox.Show(this, "Введите корректные комплексные значения C1 и C2.", "Параметры Phoenix",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        SelectedC1Real = c1r; SelectedC1Imaginary = c1i;
        SelectedC2Real = c2r; SelectedC2Imaginary = c2i;
        return true;
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        if (!ReadSelectedValues()) return;
        DialogResult = true;
    }

    private void OpenJulia_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        if (!ReadSelectedValues()) return;
        OpenC1AsJulia = true;
        DialogResult = true;
    }

    private static bool TryRead(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static string Format(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    private static string Complex(decimal real, decimal imaginary) =>
        $"{real:G8} {(imaginary < 0 ? '−' : '+')} {Math.Abs(imaginary):G8}i";

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _c1Timer.Stop(); _c2Timer.Stop(); _c1Cts?.Cancel(); _c2Cts?.Cancel();
        C1Slice.Dispose(); C2Slice.Dispose();
    }

    private void Representation_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || C1Slice is null || C2Slice is null) return;
        _c1Timer.Stop(); _c2Timer.Stop(); _c1Cts?.Cancel(); _c2Cts?.Cancel();
        if (_panning)
        {
            _panning = false;
            C1PlaneHost.ReleaseMouseCapture(); C2PlaneHost.ReleaseMouseCapture();
            Mouse.OverrideCursor = null;
        }
        Visibility plane = Is3D ? Visibility.Collapsed : Visibility.Visible;
        Visibility slice = Is3D ? Visibility.Visible : Visibility.Collapsed;
        C1PlaneHost.Visibility = C2PlaneHost.Visibility = C1Progress.Visibility = C2Progress.Visibility = plane;
        C1Slice.Visibility = C2Slice.Visibility = SliceOptions.Visibility = slice;
        C1Group.Header = Is3D ? "3D-срез C1 · фиксировано Im C2" : "Плоскость C1 — seed";
        C2Group.Header = Is3D ? "3D-срез C2 · фиксировано Im C1" : "Плоскость C2 — память zₙ₋₁";
        NavigationHint.Text = Is3D
            ? "Щелчок по поверхности выбирает три координаты. ЛКМ — вращение, колесо — масштаб (Ctrl — быстро, Shift — точно)."
            : "ЛКМ выбирает комплексное значение. Колесо масштабирует, средняя кнопка перемещает карту.";
        C1Slice.SetActive(Is3D); C2Slice.SetActive(Is3D);
        if (Is3D) UpdateSlices(); else { SchedulePlane(true); SchedulePlane(false); }
        UpdateLabels();
    }

    private void Shadows_OnChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized && Is3D) UpdateSlices();
    }

    private void UpdateSlices()
    {
        if (_closing || !Is3D) return;
        C1Slice.SetParameters(CreateRenderState(), ShadowsBox.IsChecked == true);
        C2Slice.SetParameters(CreateRenderState(), ShadowsBox.IsChecked == true);
        C1Slice.SetActive(true); C2Slice.SetActive(true);
    }

    private void SelectSlicePoint(bool c1, System.Numerics.Vector3 point)
    {
        if (_closing || !Is3D) return;
        if (c1)
        {
            SelectedC1Real = (decimal)point.X; SelectedC1Imaginary = (decimal)point.Y;
            SelectedC2Real = (decimal)point.Z;
        }
        else
        {
            SelectedC2Real = (decimal)point.X; SelectedC2Imaginary = (decimal)point.Y;
            SelectedC1Real = (decimal)point.Z;
        }
        SetText(); UpdateLabels(); UpdateMarkers(); UpdateSlices();
    }
}
