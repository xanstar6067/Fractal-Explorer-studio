using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class LSystemWindow : Window
{
    private readonly LSystemSaveStore _saveStore = new();
    private readonly DispatcherTimer _redrawTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch _animationClock = new();
    private readonly TransformGroup _previewTransform = new();
    private readonly ScaleTransform _previewScale = new(1, 1);
    private readonly TranslateTransform _previewTranslation = new();
    private CancellationTokenSource? _buildCts;
    private CancellationTokenSource? _frameCts;
    private LSystemDefinition _activeDefinition = new();
    private readonly Stack<LSystemDefinition> _randomUndo = new();
    private LSystemScene? _scene;
    private bool _initializing;
    private bool _isBuilding;
    private bool _isClosing;
    private bool _isFrameRendering;
    private bool _isAnimating;
    private bool _isPanning;
    private bool _controlsVisible = true;
    private bool _isFullscreen;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;
    private Point _lastPanPoint;
    private double _viewZoom = 1;
    private double _panX;
    private double _panY;
    private double _animationDurationSeconds = 6;
    private bool _hasRenderedFrame;
    private double _renderedViewZoom = 1;
    private double _renderedPanX;
    private double _renderedPanY;

    public LSystemWindow()
    {
        InitializeComponent();
        Randomizer.CapturePlanar = () => _activeDefinition.Clone();
        Randomizer.Generated += (_, result) =>
        {
            _randomUndo.Push(_activeDefinition.Clone());
            ApplyRandomDefinition(result.Planar!, result.Description);
            Randomizer.SetUndoAvailable(true);
        };
        Randomizer.UndoRequested += (_, _) =>
        {
            if (!_randomUndo.TryPop(out var previous)) return;
            ApplyRandomDefinition(previous, "Предыдущая форма восстановлена");
            Randomizer.SetUndoAvailable(_randomUndo.Count > 0);
        };
        _previewTransform.Children.Add(_previewScale);
        _previewTransform.Children.Add(_previewTranslation);
        CanvasImage.RenderTransformOrigin = new Point(0.5, 0.5);
        CanvasImage.RenderTransform = _previewTransform;
        _redrawTimer.Tick += RedrawTimer_OnTick;
        _animationTimer.Tick += AnimationTimer_OnTick;
        PresetBox.ItemsSource = LSystemPresets.All;
        StyleModeBox.ItemsSource = new StyleModeOption[]
        {
            new(LSystemStyleMode.Generation, "По поколению символа"),
            new(LSystemStyleMode.BranchDepth, "По глубине ветвления"),
            new(LSystemStyleMode.DrawingOrder, "По ходу построения"),
            new(LSystemStyleMode.Uniform, "Один стиль")
        };
        PresetBox.SelectedIndex = 0;
        Loaded += async (_, _) => await BuildSceneAsync(false);
    }

    private void Preset_OnChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is not LSystemPreset preset)
        {
            return;
        }

        ApplyPreset(preset);
        Randomizer.CancelWork();
        _randomUndo.Clear();
        Randomizer.SetUndoAvailable(false);
        if (IsLoaded)
        {
            _ = BuildSceneAsync(false);
        }
    }

    private void ApplyPreset(LSystemPreset preset)
    {
        _initializing = true;
        try
        {
            LSystemDefinition definition = preset.Definition.Clone();
            _activeDefinition = definition.Clone();
            CanvasHost.Background = new SolidColorBrush(definition.BackgroundColor);
            PresetDescription.Text = preset.Description;
            AxiomBox.Text = definition.Axiom;
            RulesBox.Text = definition.RulesText;
            DrawSymbolsBox.Text = definition.DrawSymbols;
            DepthBox.Text = definition.Depth.ToString(CultureInfo.InvariantCulture);
            AngleBox.Text = definition.AngleDegrees.ToString("0.####", CultureInfo.InvariantCulture);
            InitialAngleBox.Text = definition.InitialAngleDegrees.ToString("0.####", CultureInfo.InvariantCulture);
            StartColorSelector.SelectedColor = definition.StartColor;
            EndColorSelector.SelectedColor = definition.EndColor;
            BackgroundColorSelector.SelectedColor = definition.BackgroundColor;
            StartThicknessBox.Text = definition.StartThickness.ToString("0.##", CultureInfo.InvariantCulture);
            EndThicknessBox.Text = definition.EndThickness.ToString("0.##", CultureInfo.InvariantCulture);
            StyleModeBox.SelectedItem = StyleModeBox.Items
                .Cast<StyleModeOption>()
                .First(option => option.Mode == definition.StyleMode);
            ResetView();
        }
        finally
        {
            _initializing = false;
        }
    }

    private void ApplyRandomDefinition(LSystemDefinition definition, string description)
    {
        _viewZoom = 1; _panX = _panY = 0;
        ApplyPreset(new LSystemPreset { Id = "generated", Name = "Случайная форма", Description = description, Definition = definition });
        _ = BuildSceneAsync(false);
    }

    private void Input_OnChanged(object sender, EventArgs e)
    {
        if (!_initializing) Randomizer?.CancelWork();
        if (_initializing || !IsLoaded)
        {
            return;
        }

        StopAnimation();
        StatusText.Text = "Параметры изменены — нажмите «Построить».";
    }

    private void Appearance_OnChanged(object sender, EventArgs e)
    {
        if (_initializing || !IsLoaded || _scene is null ||
            !TryReadDouble(StartThicknessBox.Text, out double startThickness) ||
            !TryReadDouble(EndThicknessBox.Text, out double endThickness) ||
            startThickness is < 0.1 or > 50 || endThickness is < 0.1 or > 50 ||
            StyleModeBox.SelectedItem is not StyleModeOption style)
        {
            return;
        }

        _activeDefinition.StartColor = StartColorSelector.SelectedColor;
        _activeDefinition.EndColor = EndColorSelector.SelectedColor;
        _activeDefinition.BackgroundColor = BackgroundColorSelector.SelectedColor;
        CanvasHost.Background = new SolidColorBrush(_activeDefinition.BackgroundColor);
        _activeDefinition.StartThickness = startThickness;
        _activeDefinition.EndThickness = endThickness;
        _activeDefinition.StyleMode = style.Mode;
        ScheduleRedraw();
    }

    private void Build_OnClick(object sender, RoutedEventArgs e) => _ = BuildSceneAsync(false);

    private void Animate_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isAnimating)
        {
            StopAnimation();
            ScheduleRedraw();
            return;
        }

        _ = BuildSceneAsync(true);
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        _buildCts?.Cancel();
        if (_isAnimating)
        {
            StopAnimation();
            ScheduleRedraw();
        }
        _frameCts?.Cancel();
    }

    private async Task BuildSceneAsync(bool animate)
    {
        if (_isClosing) return;
        if (!TryReadDefinition(out LSystemDefinition definition, out double animationDuration))
        {
            return;
        }

        StopAnimation();
        _buildCts?.Cancel();
        var cts = new CancellationTokenSource();
        CancellationToken token = cts.Token;
        _buildCts = cts;
        _isBuilding = true;
        UpdateActivityState();
        RenderProgress.Value = 0;
        StatusText.Text = "Развёртка L‑системы…";
        var stopwatch = Stopwatch.StartNew();
        IProgress<int> buildProgress = new Progress<int>(value =>
        {
            if (!_isClosing && ReferenceEquals(_buildCts, cts)) RenderProgress.Value = value * .65;
        });

        try
        {
            LSystemScene scene = await Task.Run(() =>
                LSystemEngine.BuildScene(definition, token, value => buildProgress.Report(value)), token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_buildCts, cts))
            {
                return;
            }

            _scene = scene;
            _activeDefinition = definition;
            _animationDurationSeconds = animationDuration;
            if (animate)
            {
                bool animationStarted = await StartAnimationAsync(token);
                if (!animationStarted && ReferenceEquals(_buildCts, cts))
                {
                    StatusText.Text = "Построение отменено.";
                    RenderProgress.Value = 0;
                }
            }
            else
            {
                StatusText.Text = "Отрисовка отрезков…";
                var drawProgress = new Progress<int>(value =>
                {
                    if (!_isClosing && ReferenceEquals(_buildCts, cts)) RenderProgress.Value = 65 + value * .35;
                });
                double renderZoom = _viewZoom;
                double renderPanX = _panX;
                double renderPanY = _panY;
                BitmapSource? bitmap = await RenderBitmapAsync(
                    scene, definition, scene.Segments.Count, CurrentSurface(),
                    renderZoom, renderPanX, renderPanY, token, drawProgress);
                if (bitmap is null || token.IsCancellationRequested || !ReferenceEquals(_buildCts, cts))
                {
                    if (ReferenceEquals(_buildCts, cts))
                    {
                        StatusText.Text = "Построение отменено.";
                        RenderProgress.Value = 0;
                    }

                    return;
                }

                PresentBitmap(bitmap, renderZoom, renderPanX, renderPanY);
                stopwatch.Stop();
                RenderProgress.Value = 100;
                StatusText.Text = BuildReadyStatus(scene, stopwatch.Elapsed);
            }
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_buildCts, cts))
            {
                StatusText.Text = "Построение отменено.";
                RenderProgress.Value = 0;
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_buildCts, cts))
            {
                StatusText.Text = "Не удалось построить L‑систему.";
                RenderProgress.Value = 0;
                MessageBox.Show(this, ex.Message, "L‑система", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            if (ReferenceEquals(_buildCts, cts))
            {
                _isBuilding = false;
                _buildCts = null;
                UpdateActivityState();
            }
            cts.Dispose();
        }
    }

    private async Task<bool> StartAnimationAsync(CancellationToken cancellationToken)
    {
        if (_scene is null)
        {
            return false;
        }

        AnimationBadge.Visibility = Visibility.Visible;
        AnimationBadgeText.Text = "Построено 0%";
        RenderProgress.Value = 0;
        double renderZoom = _viewZoom;
        double renderPanX = _panX;
        double renderPanY = _panY;
        BitmapSource? bitmap = await RenderBitmapAsync(
            _scene, _activeDefinition, 0, CurrentSurface(), renderZoom, renderPanX, renderPanY,
            cancellationToken, null);
        if (bitmap is null || cancellationToken.IsCancellationRequested)
        {
            AnimationBadge.Visibility = Visibility.Collapsed;
            return false;
        }

        _isAnimating = true;
        UpdateActivityState();
        PresentBitmap(bitmap, renderZoom, renderPanX, renderPanY);
        _animationClock.Restart();
        _animationTimer.Start();
        StatusText.Text = $"Анимация: {_scene.Segments.Count:N0} отрезков за {_animationDurationSeconds:0.#} сек.";
        return true;
    }

    private async void AnimationTimer_OnTick(object? sender, EventArgs e)
    {
        if (!_isAnimating || _scene is null || _isFrameRendering)
        {
            return;
        }

        double fraction = Math.Clamp(_animationClock.Elapsed.TotalSeconds / _animationDurationSeconds, 0, 1);
        int visible = (int)Math.Round(_scene.Segments.Count * fraction);
        RenderProgress.Value = fraction * 100;
        AnimationBadgeText.Text = $"Построено {fraction:P0}";
        await RenderAnimationFrameAsync(visible);

        if (fraction >= 1 && _isAnimating)
        {
            _animationTimer.Stop();
            _animationClock.Stop();
            _isAnimating = false;
            AnimationBadge.Visibility = Visibility.Collapsed;
            RenderProgress.Value = 100;
            StatusText.Text = BuildReadyStatus(_scene, TimeSpan.FromSeconds(_animationDurationSeconds));
            UpdateActivityState();
        }
    }

    private async Task RenderAnimationFrameAsync(int visibleSegments)
    {
        if (_isClosing || _scene is null)
        {
            return;
        }

        _isFrameRendering = true;
        _frameCts?.Cancel();
        var cts = new CancellationTokenSource();
        CancellationToken token = cts.Token;
        _frameCts = cts;
        try
        {
            double renderZoom = _viewZoom;
            double renderPanX = _panX;
            double renderPanY = _panY;
            BitmapSource? bitmap = await RenderBitmapAsync(
                _scene, _activeDefinition, visibleSegments, CurrentSurface(),
                renderZoom, renderPanX, renderPanY, token, null);
            if (bitmap is not null && !token.IsCancellationRequested && ReferenceEquals(_frameCts, cts))
            {
                PresentBitmap(bitmap, renderZoom, renderPanX, renderPanY);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_frameCts, cts))
            {
                StopAnimation();
                StatusText.Text = "Ошибка отрисовки кадра анимации.";
                CrashLogger.Log("LSystemWindow.RenderAnimationFrameAsync", ex);
            }
        }
        finally
        {
            if (ReferenceEquals(_frameCts, cts))
            {
                _frameCts = null;
                _isFrameRendering = false;
            }
            cts.Dispose();
        }
    }

    private void StopAnimation()
    {
        _animationTimer.Stop();
        _animationClock.Stop();
        _isAnimating = false;
        _frameCts?.Cancel();
        AnimationBadge.Visibility = Visibility.Collapsed;
        UpdateActivityState();
    }

    private void ScheduleRedraw()
    {
        if (!IsLoaded || _scene is null || _isBuilding)
        {
            return;
        }
        if (_isAnimating)
        {
            return;
        }

        _redrawTimer.Stop();
        _redrawTimer.Start();
    }

    private void RedrawTimer_OnTick(object? sender, EventArgs e)
    {
        _redrawTimer.Stop();
        _ = RedrawSceneAsync();
    }

    private async Task RedrawSceneAsync()
    {
        if (_isClosing || _scene is null || _isBuilding)
        {
            return;
        }

        _frameCts?.Cancel();
        var cts = new CancellationTokenSource();
        CancellationToken token = cts.Token;
        _frameCts = cts;
        _isFrameRendering = true;
        try
        {
            double renderZoom = _viewZoom;
            double renderPanX = _panX;
            double renderPanY = _panY;
            BitmapSource? bitmap = await RenderBitmapAsync(
                _scene, _activeDefinition, _scene.Segments.Count, CurrentSurface(),
                renderZoom, renderPanX, renderPanY, token, null);
            if (bitmap is not null && !token.IsCancellationRequested && ReferenceEquals(_frameCts, cts))
            {
                PresentBitmap(bitmap, renderZoom, renderPanX, renderPanY);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_frameCts, cts))
            {
                StatusText.Text = "Не удалось перерисовать сцену.";
                CrashLogger.Log("LSystemWindow.RedrawSceneAsync", ex);
            }
        }
        finally
        {
            if (ReferenceEquals(_frameCts, cts))
            {
                _frameCts = null;
                _isFrameRendering = false;
            }
            cts.Dispose();
        }
    }

    private static async Task<BitmapSource?> RenderBitmapAsync(
        LSystemScene scene,
        LSystemDefinition definition,
        int visibleSegments,
        RenderSurfaceMetrics surface,
        double viewZoom,
        double panX,
        double panY,
        CancellationToken cancellationToken,
        IProgress<int>? progress)
    {
        byte[]? pixels = await Task.Run(() => LSystemRasterizer.Render(
            scene, definition, surface.PixelWidth, surface.PixelHeight, visibleSegments,
            viewZoom, panX, panY, cancellationToken, value => progress?.Report(value)));
        if (pixels is null || cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        int stride = checked(surface.PixelWidth * 4);
        BitmapSource bitmap = BitmapSource.Create(
            surface.PixelWidth, surface.PixelHeight,
            surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY,
            PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static Task<BitmapSource> RenderExportBitmapAsync(
        LSystemScene scene,
        LSystemDefinition definition,
        int width,
        int height,
        double viewZoom,
        double panX,
        double panY,
        CancellationToken cancellationToken,
        IProgress<int>? progress)
    {
        var surface = new RenderSurfaceMetrics(
            new System.Windows.Controls.Border(), width, height, width, height, new DpiScale(1, 1));
        var completion = new TaskCompletionSource<BitmapSource>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _ = CompleteExportRenderAsync();
        return completion.Task;

        async Task CompleteExportRenderAsync()
        {
            try
            {
                BitmapSource? bitmap = await RenderBitmapAsync(
                    scene, definition, scene.Segments.Count, surface,
                    viewZoom, panX, panY, cancellationToken, progress);
                if (bitmap is null || cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                else
                {
                    completion.TrySetResult(bitmap);
                }
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }
    }

    private void Export_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadDefinition(out LSystemDefinition definition, out _))
        {
            return;
        }

        RenderSurfaceMetrics surface = CurrentSurface();
        double zoom = _viewZoom;
        double panX = _panX;
        double panY = _panY;
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "l_system",
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            MaxSsaaFactor = 4,
            HasNativeSsaa = false,
            RenderAsync = async (request, token, progress) =>
            {
                IProgress<int> buildProgress = new Progress<int>(value => progress.Report(value / 2));
                LSystemScene scene = await Task.Run(() =>
                    LSystemEngine.BuildScene(definition, token, value => buildProgress.Report(value)), token);
                var drawProgress = new Progress<int>(value => progress.Report(50 + value / 2));
                return await RenderExportBitmapAsync(scene, definition, request.Width, request.Height,
                    zoom, panX, panY, token, drawProgress);
            }
        });
    }

    private bool TryReadDefinition(out LSystemDefinition definition, out double animationDuration)
    {
        definition = new LSystemDefinition();
        animationDuration = 6;
        if (!int.TryParse(DepthBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int depth) ||
            depth is < 0 or > 20)
        {
            ShowParameterError("Глубина должна быть целым числом от 0 до 20.");
            return false;
        }
        if (!TryReadDouble(AngleBox.Text, out double angle) || !double.IsFinite(angle))
        {
            ShowParameterError("Введите корректный угол поворота.");
            return false;
        }
        if (!TryReadDouble(InitialAngleBox.Text, out double initialAngle) || !double.IsFinite(initialAngle))
        {
            ShowParameterError("Введите корректный стартовый угол.");
            return false;
        }
        if (!TryReadDouble(StartThicknessBox.Text, out double startThickness) || startThickness is < 0.1 or > 50 ||
            !TryReadDouble(EndThicknessBox.Text, out double endThickness) || endThickness is < 0.1 or > 50)
        {
            ShowParameterError("Толщина линии должна быть от 0,1 до 50.");
            return false;
        }
        if (!TryReadDouble(AnimationDurationBox.Text, out animationDuration) ||
            animationDuration is < 0.5 or > 120)
        {
            ShowParameterError("Длительность анимации должна быть от 0,5 до 120 секунд.");
            return false;
        }
        if (StyleModeBox.SelectedItem is not StyleModeOption style)
        {
            ShowParameterError("Выберите способ распределения стиля.");
            return false;
        }

        definition = new LSystemDefinition
        {
            Axiom = AxiomBox.Text,
            RulesText = RulesBox.Text,
            DrawSymbols = DrawSymbolsBox.Text,
            Depth = depth,
            AngleDegrees = angle,
            InitialAngleDegrees = initialAngle,
            StartColor = StartColorSelector.SelectedColor,
            EndColor = EndColorSelector.SelectedColor,
            BackgroundColor = BackgroundColorSelector.SelectedColor,
            StartThickness = startThickness,
            EndThickness = endThickness,
            StyleMode = style.Mode
        };
        return true;
    }

    private void ShowParameterError(string message)
    {
        StatusText.Text = message;
        MessageBox.Show(this, message, "Параметры L‑системы", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static bool TryReadDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static string BuildReadyStatus(LSystemScene scene, TimeSpan elapsed) =>
        $"Готово: {scene.ExpandedSymbolCount:N0} символов, {scene.Segments.Count:N0} отрезков, {elapsed.TotalSeconds:F2} сек.";

    private void UpdateActivityState()
    {
        CancelButton.IsEnabled = _isBuilding || _isAnimating || _isFrameRendering;
        AnimateButton.Content = _isAnimating ? "■ Остановить" : "▶ Анимация";
    }

    private RenderSurfaceMetrics CurrentSurface() => RenderSurfaceMetrics.Measure(CanvasHost);

    private void PresentBitmap(BitmapSource bitmap, double viewZoom, double panX, double panY)
    {
        CanvasImage.Source = bitmap;
        _renderedViewZoom = viewZoom;
        _renderedPanX = panX;
        _renderedPanY = panY;
        _hasRenderedFrame = true;
        UpdatePreviewTransform();
    }

    private void UpdatePreviewTransform()
    {
        if (!_hasRenderedFrame || CanvasHost.ActualWidth <= 0 || CanvasHost.ActualHeight <= 0)
        {
            return;
        }

        double scale = _viewZoom / Math.Max(1e-12, _renderedViewZoom);
        _previewScale.ScaleX = scale;
        _previewScale.ScaleY = scale;
        _previewTranslation.X = (_panX - _renderedPanX * scale) * CanvasHost.ActualWidth;
        _previewTranslation.Y = (_panY - _renderedPanY * scale) * CanvasHost.ActualHeight;
        bool isIdentity = Math.Abs(scale - 1) < 1e-9 &&
                          Math.Abs(_previewTranslation.X) < 0.01 &&
                          Math.Abs(_previewTranslation.Y) < 0.01;
        RenderOptions.SetBitmapScalingMode(CanvasImage,
            isIdentity ? BitmapScalingMode.HighQuality : BitmapScalingMode.LowQuality);
    }

    private void ResetView_OnClick(object sender, RoutedEventArgs e)
    {
        ResetView();
        ScheduleRedraw();
    }

    private void ResetView()
    {
        _viewZoom = 1;
        _panX = 0;
        _panY = 0;
        UpdatePreviewTransform();
    }

    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePreviewTransform();
        ScheduleRedraw();
    }

    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e) =>
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost,
            ToggleControlsButton, 370, ScheduleRedraw);

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _frameCts?.Cancel();
        Point mouse = e.GetPosition(CanvasHost);
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double height = Math.Max(1, CanvasHost.ActualHeight);
        double normalizedX = mouse.X / width;
        double normalizedY = mouse.Y / height;
        double oldZoom = _viewZoom;
        double newZoom = Math.Clamp(oldZoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), 0.02, 1_000);
        double ratio = newZoom / oldZoom;
        _panX = normalizedX - 0.5 - (normalizedX - 0.5 - _panX) * ratio;
        _panY = normalizedY - 0.5 - (normalizedY - 0.5 - _panY) * ratio;
        _viewZoom = newZoom;
        UpdatePreviewTransform();
        ScheduleRedraw();
        e.Handled = true;
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _frameCts?.Cancel();
        _redrawTimer.Stop();
        _isPanning = true;
        _lastPanPoint = e.GetPosition(CanvasHost);
        CanvasHost.Cursor = Cursors.SizeAll;
        if (!CanvasHost.CaptureMouse())
        {
            _isPanning = false;
            CanvasHost.Cursor = null;
        }
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopPanning();
            return;
        }

        Point current = e.GetPosition(CanvasHost);
        _panX += (current.X - _lastPanPoint.X) / Math.Max(1, CanvasHost.ActualWidth);
        _panY += (current.Y - _lastPanPoint.Y) / Math.Max(1, CanvasHost.ActualHeight);
        _lastPanPoint = current;
        UpdatePreviewTransform();
        ScheduleRedraw();
    }

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        StopPanning();
    }

    private void CanvasHost_OnLostMouseCapture(object sender, MouseEventArgs e) => StopPanning();

    private void StopPanning(bool scheduleRedraw = true)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        if (CanvasHost.IsMouseCaptured)
        {
            CanvasHost.ReleaseMouseCapture();
        }

        CanvasHost.Cursor = null;
        if (scheduleRedraw)
        {
            ScheduleRedraw();
        }
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 || e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
        }
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _previousWindowStyle = WindowStyle;
            _previousWindowState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = _previousWindowStyle;
            WindowState = _previousWindowState;
        }
        _isFullscreen = !_isFullscreen;
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        _isClosing = true;
        Randomizer.CancelWork();
        StopPanning(scheduleRedraw: false);
        _redrawTimer.Stop();
        _animationTimer.Stop();
        _buildCts?.Cancel();
        _frameCts?.Cancel();
        // Each asynchronous operation disposes its own source only after its worker has exited.
        _buildCts = null;
        _frameCts = null;
    }

    private sealed record StyleModeOption(LSystemStyleMode Mode, string Label)
    {
        public override string ToString() => Label;
    }
}
