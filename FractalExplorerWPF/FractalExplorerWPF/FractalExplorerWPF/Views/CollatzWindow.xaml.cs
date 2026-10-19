using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Microsoft.Win32;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class CollatzWindow : Window
{
    private const decimal BaseScale = 4m;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly CollatzPaletteManager _paletteManager = new();
    private readonly CollatzSaveStore _saveStore = new();
    private readonly DecimalDeepZoomViewport _viewport = new((double)BaseScale, MinZoom, MaxZoom, DeepZoomThreshold);
    private CancellationTokenSource? _renderCts;
    private TiledRenderSession? _activeSession;
    private bool _isRendering, _controlsVisible = true, _updatingControls;

    /// <summary>Зум, с которого включается ступень BigFloat — тот же порог, что и у рендера.</summary>
    private const double DeepZoomThreshold = CollatzRenderer.BigFloatZoomThreshold;

    private const double MinZoom = 1e-15;

    /// <summary>
    /// Потолок зума. Прежний 1e15 был недостижим на практике: и double-, и decimal-ступень
    /// считали тригонометрию через Math.Cos/Math.Sin, поэтому картинка разваливалась уже
    /// около 1e10. Ступень BigFloat считает и координаты, и саму формулу с точностью,
    /// подобранной под глубину; ограничение теперь не в точности, а во времени кадра —
    /// каждый шаг орбиты стоит десятки микросекунд.
    /// </summary>
    private const double MaxZoom = 1e50;
    private readonly FullscreenToggle _fullscreen = new();

    public CollatzWindow()
    {
        InitializeComponent();
        _updatingControls = true;
        _viewport.AttachPreview(StablePreviewImage, CanvasHost);
        _renderTimer.Tick += RenderTimer_OnTick;
        IterationsBox.Text = "150";
        ThresholdBox.Text = "100";
        ZoomBox.Text = "1";
        PParameterBox.Text = "3";
        QRealParameterBox.Text = "0";
        QImaginaryParameterBox.Text = "0";
        VariationBox.SelectedIndex = 0;
        ColoringModeBox.SelectedIndex = 0;
        EscapeSmoothingBox.SelectedIndex = 1;
        ArgumentCyclesBox.Text = "1";
        MagnitudeScaleBox.Text = "1";
        TrapScaleBox.Text = "4";
        CycleToleranceBox.Text = "1e-6";
        MaximumPeriodBox.Text = "32";
        DensityExposureBox.Text = "1";
        DensitySampleStepBox.Text = "2";
        DensityEscapedOnlyBox.IsChecked = true;
        InteriorFillModeBox.SelectedIndex = 0;
        CustomInteriorColorSelector.SelectedColor = Colors.Black;
        SsaaBox.SelectedIndex = 0;
        RenderThreadsBox.Fill(ThreadsBox);
        UpdateVariationControls();
        UpdateColoringControls();
        _updatingControls = false;
        Loaded += (_, _) => ScheduleRender();
        InitializeNumericInput();
    }

    public CollatzState CaptureState(string name)
    {
        NumericInput.Prepare(this);
        if (!int.TryParse(IterationsBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations) || iterations is < 10 or > 100_000)
            throw new InvalidOperationException("Итерации должны быть целым числом от 10 до 100000.");
        if (!TryRead(ThresholdBox.Text, out decimal threshold) || threshold is < 2 or > 10_000)
            throw new InvalidOperationException("Порог выхода должен быть от 2 до 10000.");
        if (!TryRead(PParameterBox.Text, out decimal p) || p is < -100 or > 100)
            throw new InvalidOperationException("Параметр p должен быть от −100 до 100.");
        if (!TryRead(QRealParameterBox.Text, out decimal qReal) || qReal is < -100 or > 100)
            throw new InvalidOperationException("Re(q) должна быть от −100 до 100.");
        if (!TryRead(QImaginaryParameterBox.Text, out decimal qImaginary) || qImaginary is < -100 or > 100)
            throw new InvalidOperationException("Im(q) должна быть от −100 до 100.");
        double argumentCycles = ReadDouble(ArgumentCyclesBox.Text, "обороты аргумента", 0.1, 20);
        double magnitudeScale = ReadDouble(MagnitudeScaleBox.Text, "масштаб модуля", 0.01, 20);
        double trapScale = ReadDouble(TrapScaleBox.Text, "чувствительность ловушки", 0.01, 100);
        double cycleTolerance = ReadDouble(CycleToleranceBox.Text, "допуск цикла", 1e-12, 0.1);
        int maximumPeriod = ReadInt(MaximumPeriodBox.Text, "максимальный период", 1, 64);
        double densityExposure = ReadDouble(DensityExposureBox.Text, "экспозиция плотности", 0.1, 10);
        int densitySampleStep = ReadInt(DensitySampleStepBox.Text, "шаг начальных точек", 1, 8);
        (decimal centerX, decimal centerY, string? exactX, string? exactY) = _viewport.CaptureCenter();
        return new CollatzState
        {
            SaveName = name,
            Timestamp = DateTime.Now,
            CenterX = centerX,
            CenterY = centerY,
            CenterXExact = exactX,
            CenterYExact = exactY,
            Zoom = _viewport.Zoom.ToDouble(),
            Threshold = threshold,
            Iterations = iterations,
            Variation = (CollatzVariation)Math.Clamp(VariationBox.SelectedIndex, 0,
                (int)CollatzVariation.GeneralizedPQ),
            PParameter = p,
            QRealParameter = qReal,
            QImaginaryParameter = qImaginary,
            ColoringMode = (CollatzColoringMode)Math.Clamp(ColoringModeBox.SelectedIndex, 0,
                (int)CollatzColoringMode.PeriodDetection),
            UseSmoothColoring = EscapeSmoothingBox.SelectedIndex == 1,
            ArgumentCycles = argumentCycles,
            MagnitudeScale = magnitudeScale,
            TrapScale = trapScale,
            CycleTolerance = cycleTolerance,
            MaximumDetectedPeriod = maximumPeriod,
            OrbitDensityExposure = densityExposure,
            OrbitDensitySampleStep = densitySampleStep,
            OrbitDensityEscapedOnly = DensityEscapedOnlyBox.IsChecked == true,
            InteriorFillMode = (CollatzInteriorFillMode)Math.Clamp(InteriorFillModeBox.SelectedIndex, 0,
                (int)CollatzInteriorFillMode.Custom),
            CustomInteriorColor = CustomInteriorColorSelector.SelectedColor,
            Palette = _paletteManager.ActivePalette.Clone(_paletteManager.ActivePalette.Name)
        };
    }

    public void LoadState(CollatzState state)
    {
        _renderCts?.Cancel();
        _updatingControls = true;
        _viewport.Restore(state.CenterX, state.CenterY, state.CenterXExact, state.CenterYExact, state.Zoom);
        IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
        ThresholdBox.Text = Format(state.Threshold);
        ZoomBox.Text = FormatZoom(_viewport.Zoom.ToDouble());
        PParameterBox.Text = Format(state.PParameter);
        QRealParameterBox.Text = Format(state.QRealParameter);
        QImaginaryParameterBox.Text = Format(state.QImaginaryParameter);
        VariationBox.SelectedIndex = Math.Clamp((int)state.Variation, 0,
            (int)CollatzVariation.GeneralizedPQ);
        ColoringModeBox.SelectedIndex = Math.Clamp((int)state.ColoringMode, 0,
            (int)CollatzColoringMode.PeriodDetection);
        EscapeSmoothingBox.SelectedIndex = state.UseSmoothColoring ? 1 : 0;
        ArgumentCyclesBox.Text = Format(state.ArgumentCycles);
        MagnitudeScaleBox.Text = Format(state.MagnitudeScale);
        TrapScaleBox.Text = Format(state.TrapScale);
        CycleToleranceBox.Text = state.CycleTolerance.ToString("G8", CultureInfo.InvariantCulture);
        MaximumPeriodBox.Text = state.MaximumDetectedPeriod.ToString(CultureInfo.InvariantCulture);
        DensityExposureBox.Text = Format(state.OrbitDensityExposure);
        DensitySampleStepBox.Text = state.OrbitDensitySampleStep.ToString(CultureInfo.InvariantCulture);
        DensityEscapedOnlyBox.IsChecked = state.OrbitDensityEscapedOnly;
        InteriorFillModeBox.SelectedIndex = Math.Clamp((int)state.InteriorFillMode, 0,
            (int)CollatzInteriorFillMode.Custom);
        CustomInteriorColorSelector.SelectedColor = state.CustomInteriorColor;
        _paletteManager.ActivePalette = state.Palette.Clone($"Загружено: {state.SaveName}");
        UpdateVariationControls();
        UpdateColoringControls();
        _updatingControls = false;
        UpdatePreviewTransform();
        ScheduleRender();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StablePreviewImage, CanvasImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        CollatzState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        RenderBitmapAsync(state, width, height, 1, token, progress);

    private void VariationBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateVariationControls();
        if (!_updatingControls) ScheduleRender();
    }

    private void UpdateVariationControls()
    {
        CollatzVariation variation = (CollatzVariation)Math.Clamp(VariationBox.SelectedIndex, 0,
            (int)CollatzVariation.GeneralizedPQ);
        if (PParameterPanel is not null)
            PParameterPanel.Visibility = variation is CollatzVariation.ParityBranchVariation or
                CollatzVariation.GeneralizedP or CollatzVariation.GeneralizedPQ
                ? Visibility.Visible : Visibility.Collapsed;
        if (QParameterPanel is not null)
            QParameterPanel.Visibility = variation == CollatzVariation.GeneralizedPQ
                ? Visibility.Visible : Visibility.Collapsed;
        if (VariationHintText is not null)
            VariationHintText.Text = VariationDescription(variation);
    }

    private void ColoringMode_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateColoringControls();
        if (!_updatingControls) ScheduleRender();
    }

    private void InteriorFillMode_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateInteriorFillControls();
        if (!_updatingControls) ScheduleRender();
    }

    private void UpdateColoringControls()
    {
        CollatzColoringMode mode = (CollatzColoringMode)Math.Clamp(ColoringModeBox.SelectedIndex, 0,
            (int)CollatzColoringMode.PeriodDetection);
        if (EscapeColoringPanel is not null)
            EscapeColoringPanel.Visibility = mode == CollatzColoringMode.EscapeTime
                ? Visibility.Visible : Visibility.Collapsed;
        if (ArgumentColoringPanel is not null)
            ArgumentColoringPanel.Visibility = mode == CollatzColoringMode.FinalArgument
                ? Visibility.Visible : Visibility.Collapsed;
        if (MagnitudeColoringPanel is not null)
            MagnitudeColoringPanel.Visibility = mode == CollatzColoringMode.FinalMagnitude
                ? Visibility.Visible : Visibility.Collapsed;
        if (TrapColoringPanel is not null)
            TrapColoringPanel.Visibility = mode is CollatzColoringMode.IntegerTrap or
                CollatzColoringMode.RealAxisTrap ? Visibility.Visible : Visibility.Collapsed;
        if (CycleColoringPanel is not null)
            CycleColoringPanel.Visibility = mode is CollatzColoringMode.CycleBasins or
                CollatzColoringMode.PeriodDetection ? Visibility.Visible : Visibility.Collapsed;
        if (DensityColoringPanel is not null)
            DensityColoringPanel.Visibility = mode == CollatzColoringMode.OrbitDensity
                ? Visibility.Visible : Visibility.Collapsed;
        if (ColoringDescriptionText is not null)
            ColoringDescriptionText.Text = ColoringModeDescription(mode);
        UpdateInteriorFillControls();
    }

    private void UpdateInteriorFillControls()
    {
        if (CustomInteriorColorSelector is null) return;
        CollatzInteriorFillMode mode = (CollatzInteriorFillMode)Math.Clamp(
            InteriorFillModeBox.SelectedIndex, 0, (int)CollatzInteriorFillMode.Custom);
        CustomInteriorColorSelector.Visibility = mode == CollatzInteriorFillMode.Custom
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Parameter_OnChanged(object sender, EventArgs e)
    {
        if (!_updatingControls) ScheduleRender();
    }

    private void ZoomBox_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (!NumericInput.IsValid(ZoomBox)) return;
        if (_updatingControls || !TryReadDouble(ZoomBox.Text, out double zoom) ||
            !double.IsFinite(zoom) || zoom <= 0) return;
        _viewport.SetZoom(zoom);
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void RenderButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); _renderTimer.Stop(); _ = RenderPreviewAsync(); }
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();

    private void PaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new MandelbrotPaletteWindow(_paletteManager) { Owner = this };
        dialog.PaletteApplied += (_, _) => ScheduleRender();
        dialog.ShowDialog();
    }

    private void SavesButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); SaveManagerWindow.Open(this, SaveManagerConfigurations.ForCollatz(this, _saveStore)); }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        if (_isRendering) CommitAndBakePreview();
        else _renderCts?.Cancel();
        CollatzState state;
        try { state = CaptureState("export"); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "collatz",
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            MaxSsaaFactor = 4,
            RenderAsync = (request, token, progress) => RenderBitmapAsync(state, request.Width,
                request.Height, request.SsaaFactor, token, progress)
        });
    }

    private void ScheduleRender()
    {
        if (!IsLoaded) return;
        // Запекание само выставляет трансформацию в единичную, но вызвать её надо в любом
        // случае: _isRendering сбрасывается только в finally уже отменённого рендера, и до
        // тех пор повторное запекание — пустая операция.
        if (_isRendering) CommitAndBakePreview();
        UpdatePreviewTransform();
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private void RenderTimer_OnTick(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        _ = RenderPreviewAsync();
    }

    private async Task RenderPreviewAsync()
    {
        if (_isRendering) { ScheduleRender(); return; }
        CollatzState state;
        try { state = CaptureState("preview"); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }

        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        CancellationToken token = _renderCts.Token;
        var watch = Stopwatch.StartNew();
        SetRendering(true, $"Рендеринг: {VariationDisplayName(state.Variation)} · " +
                           $"{ColoringModeDisplayName(state.ColoringMode)}...");
        TiledRenderSession? session = null;
        try
        {
            int factor = SsaaBox.SelectedItem is ComboBoxItem item ? Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture) : 1;
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            DpiScale dpi = surface.Dpi;
            int pixelWidth = surface.PixelWidth;
            int pixelHeight = surface.PixelHeight;
            int renderWidth = checked(pixelWidth * factor);
            int renderHeight = checked(pixelHeight * factor);
            BitmapSource completed;
            string renderDetails;
            if (state.ColoringMode == CollatzColoringMode.OrbitDensity)
            {
                CanvasImage.Source = null;
                completed = await RenderBitmapAsync(state, pixelWidth, pixelHeight, factor, token,
                    new Progress<int>(value => RenderProgress.Value = value));
                renderDetails = "глобальное накопление орбит";
            }
            else
            {
                TileSchedulingStrategy strategy = RenderPatternSettings.SelectedPattern;
                IReadOnlyList<MandelbrotRenderTile> tiles = MandelbrotTileScheduler.Create(
                    renderWidth, renderHeight, 16 * factor, strategy);
                WriteableBitmap bitmap = ProgressiveRenderBitmap.CreateOverlay(
                    renderWidth, renderHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
                session = TiledRenderSession.Begin(bitmap, tiles.Count, RenderOverlay, RenderProgress);
                _activeSession = session;
                CanvasImage.Source = bitmap;
                await session.RenderTilesAsync(tiles, GetThreadCount(),
                    (tile, tileToken) => CollatzRenderer.RenderTile(state, renderWidth, renderHeight, tile, tileToken), token);
                session.Flush();
                completed = session.Snapshot();
                renderDetails = $"стратегия: {strategy}";
            }

            if (token.IsCancellationRequested)
            {
                CanvasImage.Source = null;
                StatusText.Text = "Рендер отменён";
                return;
            }
            StablePreviewImage.Source = completed;
            CanvasImage.Source = null;
            _viewport.MarkRendered(state.CenterX, state.CenterY, state.CenterXExact, state.CenterYExact, state.Zoom);
            UpdatePreviewTransform();
            // На ступени BigFloat время кадра определяется выбранной точностью, поэтому
            // её видно в статусе — иначе непонятно, почему кадр вдруг стал дороже.
            if (CollatzRenderer.UsesBigFloat(state))
                renderDetails += $"; точность {CollatzRenderer.PlanPrecisionBits(state)} бит";
            StatusText.Text = $"Готово за {watch.Elapsed.TotalSeconds:F3} сек.; {renderDetails}.";
        }
        catch (OperationCanceledException) { CanvasImage.Source = null; StatusText.Text = "Рендер отменён"; }
        catch (Exception ex) { StatusText.Text = "Ошибка рендера"; MessageBox.Show(this, ex.Message, "Коллатц", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally
        {
            session?.End();
            _activeSession = null;
            SetRendering(false);
        }
    }

    private Task<BitmapSource> RenderBitmapAsync(CollatzState state, int width, int height, int ssaa,
        CancellationToken token, IProgress<int>? progress)
    {
        // Элементы окна принадлежат UI-потоку: число потоков читается до расчёта.
        int threads = GetThreadCount();
        return SsaaBitmapRenderer.RenderAsync(width, height, ssaa, 4, (pixels, renderWidth, renderHeight, stride, report) =>
            CollatzRenderer.Render(state, pixels, renderWidth, renderHeight, stride, threads, token, report), token, progress);
    }

    private int GetThreadCount() => RenderThreadsBox.Read(ThreadsBox);

    private void SetRendering(bool value, string? status = null)
    {
        _isRendering = value;
        CancelButton.IsEnabled = value;
        if (!value) RenderProgress.Value = 0;
        if (status is not null) StatusText.Text = status;
    }

    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e) { UpdatePreviewTransform(); ScheduleRender(); }

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Запекаем до изменения зума: снимок должен соответствовать прежнему виду, иначе
        // грубый предпросмотр будет двигать не то, что нарисовано.
        CommitAndBakePreview();
        _viewport.ZoomAt(e.GetPosition(CanvasHost), CanvasHost.ActualWidth, CanvasHost.ActualHeight,
            _viewport.WheelFactor(e.Delta));
        UpdatePreviewTransform();
        _updatingControls = true;
        ZoomBox.Text = FormatZoom(_viewport.Zoom.ToDouble());
        _updatingControls = false;
        ScheduleRender();
        e.Handled = true;
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CommitAndBakePreview();
        _viewport.BeginPan(e.GetPosition(CanvasHost));
        CanvasHost.CaptureMouse();
        Mouse.OverrideCursor = Cursors.SizeAll;
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_viewport.PanTo(e.GetPosition(CanvasHost), CanvasHost.ActualWidth)) UpdatePreviewTransform();
    }

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_viewport.EndPan()) return;
        CanvasHost.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        ScheduleRender();
    }

    /// <summary>
    /// Останавливает текущий рендер и «запекает» то, что уже видно на холсте (сдвинутый
    /// прежний кадр плюс успевшие лечь тайлы), в стабильный предпросмотр. После этого
    /// снимок считается отрисованным для текущего вида, поэтому дальнейшие зум и
    /// перетаскивание двигают именно его.
    ///
    /// Без этого при зуме терялся уже посчитанный кадр, а при перетаскивании рендер
    /// продолжал идти поверх уезжающего фона.
    /// </summary>
    private void CommitAndBakePreview()
    {
        TiledRenderSession? session = _activeSession;
        _renderCts?.Cancel();
        // Полнокадровые режимы (Orbit Density) сессию тайлов не заводят: запекать нечего,
        // достаточно отменить рендер.
        if (session is null) return;

        session.Flush();
        if (TiledRenderSession.TryCaptureLayer(SavePreviewLayer) is { } baked)
        {
            StablePreviewImage.Source = baked;
            _viewport.MarkRendered();
            UpdatePreviewTransform();
        }
        CanvasImage.Source = null;
        session.End();
        if (ReferenceEquals(_activeSession, session)) _activeSession = null;
    }

    private void UpdatePreviewTransform() => _viewport.UpdatePreview();

    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e)
    {
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost,
            ToggleControlsButton, 290);
        ScheduleRender();
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        _fullscreen.HandleKey(this, e.Key);
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _renderTimer.Stop();
        _activeSession?.End();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
    }

    private static string VariationDisplayName(CollatzVariation variation) => variation switch
    {
        CollatzVariation.SineVariation => "синусная арт-вариация",
        CollatzVariation.ParityBranchVariation => "арт-вариация с ветвями 1 / (p−1)n",
        CollatzVariation.GeneralizedP => "обобщённая Cₚ",
        CollatzVariation.GeneralizedPQ => "семейство C(p,q)",
        _ => "стандартная вариация"
    };

    private static string VariationDescription(CollatzVariation variation) => variation switch
    {
        CollatzVariation.SineVariation => "Художественная вариация; не сохраняет отображение Коллатца на целых.",
        CollatzVariation.ParityBranchVariation => "Прежний режим Generalized P: чётное n → 1, нечётное n → (p−1)n.",
        CollatzVariation.GeneralizedP => "Аналитическое продолжение: чётное n → n/2, нечётное n → pn+1.",
        CollatzVariation.GeneralizedPQ => "То же отображение на целых; комплексный q меняет только продолжение между ними.",
        _ => "Классическое продолжение Коллатца (p = 3)."
    };

    private static string ColoringModeDisplayName(CollatzColoringMode mode) => mode switch
    {
        CollatzColoringMode.FinalArgument => "Final Argument",
        CollatzColoringMode.FinalMagnitude => "Final Magnitude",
        CollatzColoringMode.CycleBasins => "Cycle Basins",
        CollatzColoringMode.IntegerTrap => "Integer Trap",
        CollatzColoringMode.RealAxisTrap => "Real Axis Trap",
        CollatzColoringMode.OrbitDensity => "Orbit Density",
        CollatzColoringMode.PeriodDetection => "Period Detection",
        _ => "Escape Time"
    };

    private static string ColoringModeDescription(CollatzColoringMode mode) => mode switch
    {
        CollatzColoringMode.FinalArgument => "Цвет определяется аргументом последнего значения z.",
        CollatzColoringMode.FinalMagnitude => "Цвет определяется логарифмом модуля последнего значения z.",
        CollatzColoringMode.CycleBasins => "Одинаковым найденным циклам назначается одинаковый цвет.",
        CollatzColoringMode.IntegerTrap => "Яркость показывает минимальное расстояние орбиты до вещественного целого.",
        CollatzColoringMode.RealAxisTrap => "Яркость показывает минимальное расстояние орбиты до вещественной оси.",
        CollatzColoringMode.OrbitDensity => "Накопление посещений пикселей всеми траекториями, как в Buddhabrot.",
        CollatzColoringMode.PeriodDetection => "Цвет определяется найденным периодом орбиты.",
        _ => "Текущий режим: цвет по числу итераций до выхода."
    };

    private static bool TryReadDouble(string text, out double value) => (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) && double.IsFinite(value);

    private static bool TryRead(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static double ReadDouble(string text, string parameterName, double minimum, double maximum)
    {
        bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                      double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        if (!parsed || !double.IsFinite(value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"Параметр «{parameterName}» должен быть от {minimum:G8} до {maximum:G8}.");
        return value;
    }

    private static int ReadInt(string text, string parameterName, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
            value < minimum || value > maximum)
            throw new InvalidOperationException($"Параметр «{parameterName}» должен быть целым числом от {minimum} до {maximum}.");
        return value;
    }

    /// <summary>
    /// Зум показывается восемью значащими цифрами: так большое значение уходит в
    /// экспоненциальную запись (8.1707708E+09) и помещается в поле целиком, а не обрезается.
    /// </summary>
    private static string FormatZoom(double value) => value.ToString("G8", CultureInfo.InvariantCulture);

    private static string Format(decimal value) => value.ToString("G15", CultureInfo.InvariantCulture);
    private static string Format(double value) => value.ToString("G15", CultureInfo.InvariantCulture);
}
