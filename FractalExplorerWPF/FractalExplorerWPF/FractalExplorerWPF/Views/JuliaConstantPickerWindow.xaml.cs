using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;
using Point = System.Windows.Point;
using MediaColors = System.Windows.Media.Colors;
using MediaColor = System.Windows.Media.Color;

namespace FractalExplorerWPF.Views;

public partial class JuliaConstantPickerWindow : Window
{
    /// <summary>Половина длины луча перекрестья в пикселях — как у маркеров других карт выбора.</summary>
    private const double MarkerArm = 9;

    private readonly MandelbrotVariant _sourceVariant;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(260) };
    private CancellationTokenSource? _renderCts;
    private bool _updatingText;
    private bool _panning;
    private Point _lastPoint;
    private decimal _panSelectedReal;
    private decimal _panSelectedImaginary;
    private decimal _centerX;
    private decimal _centerY;
    private decimal _zoom;
    private readonly decimal _minReal;
    private readonly decimal _maxReal;
    private readonly decimal _minImaginary;
    private readonly decimal _maxImaginary;

    /// <summary>
    /// Вид, которым посчитан показанный кадр. Пока новый кадр не готов, прежний растягивается и
    /// сдвигается под текущий вид (<see cref="UpdatePreviewTransform"/>) — зум и перетаскивание
    /// отзываются сразу, а не после рендера, и маркер стоит на картинке, а не обгоняет её.
    /// </summary>
    private decimal _renderedCenterX;
    private decimal _renderedCenterY;
    private decimal _renderedZoom;
    private double _renderedAspect = 1;
    private bool _hasRenderedFrame;

    public decimal SelectedReal { get; private set; }
    public decimal SelectedImaginary { get; private set; }

    public JuliaConstantPickerWindow(MandelbrotVariant sourceVariant, decimal selectedReal, decimal selectedImaginary)
    {
        if (sourceVariant is not (MandelbrotVariant.Mandelbrot or MandelbrotVariant.BurningShip))
            throw new ArgumentOutOfRangeException(nameof(sourceVariant));

        _sourceVariant = sourceVariant;
        SelectedReal = selectedReal;
        SelectedImaginary = selectedImaginary;
        if (sourceVariant == MandelbrotVariant.BurningShip)
        {
            (_minReal, _maxReal, _minImaginary, _maxImaginary) = (-2m, 1.5m, -1m, 1.5m);
        }
        else
        {
            (_minReal, _maxReal, _minImaginary, _maxImaginary) = (-2m, 1m, -1.2m, 1.2m);
        }

        InitializeComponent();
        HeaderText.Text = sourceVariant == MandelbrotVariant.BurningShip
            ? "Карта «Горящего корабля»"
            : "Карта множества Мандельброта";
        _renderTimer.Tick += RenderTimer_OnTick;
        SetConstantText();
        ResetView();
        Loaded += (_, _) => ScheduleRender();
    }

    private void ResetView()
    {
        _centerX = (_minReal + _maxReal) / 2m;
        _centerY = (_minImaginary + _maxImaginary) / 2m;
        _zoom = 3m / (_maxReal - _minReal);
        UpdateView();
    }

    private void ScheduleRender()
    {
        if (!IsLoaded) return;
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private async void RenderTimer_OnTick(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(MapHost);
        int width = surface.PixelWidth;
        int height = surface.PixelHeight;
        StatusText.Text = "Рендер карты...";
        try
        {
            // Вид фиксируется до рендера: пока кадр считается, пользователь может сдвинуть или
            // приблизить карту, и готовый кадр надо сопоставить с тем видом, которым он посчитан.
            MandelbrotState state = CreateMapState();
            decimal renderedZoom = _zoom;
            byte[] pixels = new byte[checked(width * height * 4)];
            await Task.Run(() => MandelbrotFamilyRenderer.Render(
                state, pixels, width, height, width * 4, cts.Token));
            if (cts.Token.IsCancellationRequested) return;
            BitmapSource bitmap = BitmapSource.Create(width, height,
                surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY,
                PixelFormats.Bgra32, null, pixels, width * 4);
            bitmap.Freeze();
            PreviewImage.Source = bitmap;
            _renderedCenterX = state.CenterX;
            _renderedCenterY = state.CenterY;
            _renderedZoom = renderedZoom;
            _renderedAspect = (double)height / Math.Max(1, width);
            _hasRenderedFrame = true;
            StatusText.Text = $"C = {SelectedReal:G10} {(SelectedImaginary < 0 ? "−" : "+")} {Math.Abs(SelectedImaginary):G10}i";
            UpdateView();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_renderCts, cts)) _renderCts = null;
            cts.Dispose();
        }
    }

    private MandelbrotState CreateMapState() => new()
    {
        Variant = _sourceVariant,
        CenterX = _centerX,
        CenterY = _centerY,
        Zoom = (double)_zoom,
        Iterations = 110,
        Threshold = 2,
        Threads = 0,
        ColoringMode = MandelbrotColoringMode.Smooth,
        Palette = new MandelbrotPalette
        {
            Name = "Карта выбора C",
            Colors =
            [
                MediaColors.Black,
                MediaColor.FromRgb(200, 50, 30),
                MediaColors.White
            ],
            InteriorColor = MediaColors.Black,
            IsGradient = true,
            ColorPeriod = 110,
            AlignWithRenderIterations = true
        }
    };

    /// <summary>
    /// Множитель зума на щелчок колеса: без модификаторов прежние ×1.35, с Ctrl — ×10, с Shift —
    /// точные ×1.05. Та же раскладка модификаторов, что у окон фракталов.
    /// </summary>
    private static decimal WheelZoomStep =>
        (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 10m
        : (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 1.05m
        : 1.35m;

    private void MapHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point mouse = e.GetPosition(MapHost);
        (decimal X, decimal Y) before = ScreenToWorld(mouse);
        decimal step = WheelZoomStep;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? step : 1m / step), 0.05m, 1_000_000m);
        (decimal X, decimal Y) after = ScreenToWorld(mouse);
        _centerX += before.X - after.X;
        _centerY += before.Y - after.Y;
        UpdateView();
        ScheduleRender();
        e.Handled = true;
    }

    private void MapHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning || e.ChangedButton != MouseButton.Left) return;
        (decimal x, decimal y) = ScreenToWorld(e.GetPosition(MapHost));
        if (x >= _minReal && x <= _maxReal && y >= _minImaginary && y <= _maxImaginary)
        {
            SelectedReal = x;
            SelectedImaginary = y;
            SetConstantText();
            UpdateMarker();
        }
        e.Handled = true;
    }

    private void MapHost_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = true;
            _lastPoint = e.GetPosition(MapHost);
            _panSelectedReal = SelectedReal;
            _panSelectedImaginary = SelectedImaginary;
            _renderCts?.Cancel();
            MapHost.CaptureMouse();
            Mouse.OverrideCursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    private void MapHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning || e.MiddleButton != MouseButtonState.Pressed) return;
        Point current = e.GetPosition(MapHost);
        (decimal X, decimal Y) before = ScreenToWorld(_lastPoint);
        (decimal X, decimal Y) after = ScreenToWorld(current);
        _centerX += before.X - after.X;
        _centerY += before.Y - after.Y;
        _lastPoint = current;
        SelectedReal = _panSelectedReal;
        SelectedImaginary = _panSelectedImaginary;
        UpdateView();
        e.Handled = true;
    }

    private void MapHost_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !_panning) return;
        _panning = false;
        SelectedReal = _panSelectedReal;
        SelectedImaginary = _panSelectedImaginary;
        MapHost.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        ScheduleRender();
        e.Handled = true;
    }

    private (decimal X, decimal Y) ScreenToWorld(Point point)
    {
        decimal width = (decimal)Math.Max(1, MapHost.ActualWidth);
        decimal height = (decimal)Math.Max(1, MapHost.ActualHeight);
        decimal viewWidth = 3m / _zoom;
        decimal viewHeight = viewWidth * height / width;
        return (_centerX + ((decimal)point.X / width - 0.5m) * viewWidth,
            _centerY + (0.5m - (decimal)point.Y / height) * viewHeight);
    }

    /// <summary>
    /// Картинка и маркер обновляются одним вызовом: обе величины считаются от одного и того же
    /// текущего вида, поэтому при зуме и перетаскивании они двигаются синхронно.
    /// </summary>
    private void UpdateView()
    {
        UpdatePreviewTransform();
        UpdateMarker();
    }

    /// <summary>
    /// Растягивает и сдвигает уже посчитанный кадр под текущий вид. Картинка растянута на всю
    /// карту, поэтому точка изображения (u, v) — это мировая точка отрисованного вида; её место
    /// на экране в текущем виде линейно по u и v, отсюда матрица без поворота.
    /// </summary>
    private void UpdatePreviewTransform()
    {
        if (!IsInitialized || !_hasRenderedFrame || _renderedZoom <= 0 || _zoom <= 0) return;
        double width = Math.Max(1, MapHost.ActualWidth);
        double height = Math.Max(1, MapHost.ActualHeight);

        double currentViewWidth = 3.0 / (double)_zoom;
        double currentViewHeight = currentViewWidth * height / width;
        double renderedViewWidth = 3.0 / (double)_renderedZoom;
        double renderedViewHeight = renderedViewWidth * _renderedAspect;

        double scaleX = renderedViewWidth / currentViewWidth;
        double scaleY = renderedViewHeight / currentViewHeight;
        double offsetX = ((double)(_renderedCenterX - _centerX) - renderedViewWidth / 2 + currentViewWidth / 2)
                         * width / currentViewWidth;
        double offsetY = ((double)(_centerY - _renderedCenterY) + currentViewHeight / 2 - renderedViewHeight / 2)
                         * height / currentViewHeight;
        if (!double.IsFinite(scaleX) || !double.IsFinite(scaleY) ||
            !double.IsFinite(offsetX) || !double.IsFinite(offsetY)) return;

        PreviewImage.RenderTransform = new MatrixTransform(scaleX, 0, 0, scaleY, offsetX, offsetY);
    }

    /// <summary>
    /// Маркер выбранной C — небольшое перекрестье в точке, а не линии через всю карту: на
    /// развёрнутой карте длинные лучи закрывали как раз ту структуру, по которой C выбирают.
    /// </summary>
    private void UpdateMarker()
    {
        if (!IsInitialized) return;
        double width = Math.Max(1, MapHost.ActualWidth);
        double height = Math.Max(1, MapHost.ActualHeight);
        decimal viewWidth = 3m / Math.Max(_zoom, 0.000001m);
        decimal viewHeight = viewWidth * (decimal)height / (decimal)width;
        double x = (double)((SelectedReal - (_centerX - viewWidth / 2m)) / viewWidth) * width;
        double y = (double)(((_centerY + viewHeight / 2m) - SelectedImaginary) / viewHeight) * height;
        bool visible = x >= -MarkerArm && x <= width + MarkerArm && y >= -MarkerArm && y <= height + MarkerArm;
        MarkerLayer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        HorizontalMarker.X1 = x - MarkerArm; HorizontalMarker.X2 = x + MarkerArm;
        HorizontalMarker.Y1 = y; HorizontalMarker.Y2 = y;
        VerticalMarker.X1 = x; VerticalMarker.X2 = x;
        VerticalMarker.Y1 = y - MarkerArm; VerticalMarker.Y2 = y + MarkerArm;
    }

    private void SetConstantText()
    {
        _updatingText = true;
        RealBox.Text = SelectedReal.ToString("G15", CultureInfo.InvariantCulture);
        ImaginaryBox.Text = SelectedImaginary.ToString("G15", CultureInfo.InvariantCulture);
        _updatingText = false;
    }

    private void ConstantText_OnChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updatingText || !IsInitialized) return;
        if (TryReadDecimal(RealBox.Text, out decimal real) && TryReadDecimal(ImaginaryBox.Text, out decimal imaginary) &&
            real >= _minReal && real <= _maxReal && imaginary >= _minImaginary && imaginary <= _maxImaginary)
        {
            SelectedReal = real;
            SelectedImaginary = imaginary;
            UpdateMarker();
        }
    }

    private static bool TryReadDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private void Reset_OnClick(object sender, RoutedEventArgs e)
    {
        ResetView();
        ScheduleRender();
    }

    private void Accept_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadDecimal(RealBox.Text, out decimal real) || !TryReadDecimal(ImaginaryBox.Text, out decimal imaginary) ||
            real < _minReal || real > _maxReal || imaginary < _minImaginary || imaginary > _maxImaginary)
        {
            MessageBox.Show(this, "Константа C должна находиться внутри допустимого диапазона карты.",
                "Константа C", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SelectedReal = real;
        SelectedImaginary = imaginary;
        DialogResult = true;
    }

    private void MapHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateView();
        ScheduleRender();
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _renderTimer.Stop();
        _renderCts?.Cancel();
    }
}
