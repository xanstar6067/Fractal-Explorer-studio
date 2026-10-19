using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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

public partial class InverseCollatzTreeWindow : Window
{
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _animationTimer = new();
    private readonly InverseCollatzPaletteManager _paletteManager = new();
    private readonly InverseCollatzSaveStore _saveStore = new();
    private readonly TransformGroup _previewTransform = new();
    private readonly ScaleTransform _previewScale = new(1, 1);
    private readonly TranslateTransform _previewTranslation = new();
    private CancellationTokenSource? _renderCts;
    private InverseCollatzTree? _tree;
    private InverseCollatzPoint[]? _layoutPoints;
    private InverseCollatzLayout _cachedLayout;
    private int _builtDepth;
    private int _builtMaxNodes;
    private int _visibleDepth = 28;
    private double _centerX;
    private double _centerY;
    private double _zoom = 1;
    private double _renderedCenterX;
    private double _renderedCenterY;
    private double _renderedZoom = 1;
    private InverseCollatzLayout _renderedLayout;
    private bool _hasRenderedFrame;
    private bool _isRendering;
    private bool _renderPending;
    private bool _isAnimating;
    private bool _isPanning;
    private bool _updatingControls;
    private bool _controlsVisible = true;
    private Point _lastPanPoint;
    private readonly FullscreenToggle _fullscreen = new();

    public InverseCollatzTreeWindow()
    {
        InitializeComponent();
        _previewTransform.Children.Add(_previewScale);
        _previewTransform.Children.Add(_previewTranslation);
        CanvasImage.RenderTransformOrigin = new Point(0.5, 0.5);
        CanvasImage.RenderTransform = _previewTransform;
        _updatingControls = true;
        DepthBox.Text = "28";
        MaxNodesBox.Text = "100000";
        LayoutBox.SelectedIndex = 0;
        ModulusBox.SelectedIndex = 0;
        FilterBehaviorBox.SelectedIndex = 0;
        NodeRadiusBox.Text = "2.2";
        LineThicknessBox.Text = "0.8";
        SsaaBox.SelectedIndex = 0;
        AnimationIntervalBox.Text = "140";
        BackgroundColorSelector.SelectedColor = Colors.Black;
        UpdateResidueOptions(0, -1);
        _animationTimer.Interval = TimeSpan.FromMilliseconds(140);
        _animationTimer.Tick += AnimationTimer_OnTick;
        _renderTimer.Tick += RenderTimer_OnTick;
        _updatingControls = false;
        Loaded += (_, _) => ScheduleRender();
        InitializeNumericInput();
    }

    public InverseCollatzState CaptureState(string name)
    {
        NumericInput.Prepare(this);
        int depth = ReadInt(DepthBox.Text, "максимальная глубина", 1, 500);
        int maxNodes = ReadInt(MaxNodesBox.Text, "лимит узлов", 10, 1_000_000);
        int interval = ReadInt(AnimationIntervalBox.Text, "интервал роста", 30, 5_000);
        int modulus = SelectedModulus;
        int residue = modulus <= 0 || ResidueBox.SelectedIndex <= 0 ? -1 : ResidueBox.SelectedIndex - 1;
        InverseCollatzPalette palette = _paletteManager.ActivePalette.Clone(_paletteManager.ActivePalette.Name);
        return new InverseCollatzState
        {
            SaveName = name,
            Timestamp = DateTime.Now,
            Depth = depth,
            VisibleDepth = Math.Clamp(_visibleDepth, 0, depth),
            MaxNodes = maxNodes,
            Layout = (InverseCollatzLayout)Math.Clamp(LayoutBox.SelectedIndex, 0, 1),
            Modulus = modulus,
            Residue = residue,
            FilterBehavior = (InverseCollatzFilterBehavior)Math.Clamp(FilterBehaviorBox.SelectedIndex, 0, 1),
            NodeRadius = ReadDouble(NodeRadiusBox.Text, "радиус узла", 0.4, 20),
            LineThickness = ReadDouble(LineThicknessBox.Text, "толщина линий", 0.2, 8),
            AnimationIntervalMs = interval,
            CenterX = _centerX,
            CenterY = _centerY,
            Zoom = _zoom,
            BackgroundColor = BackgroundColorSelector.SelectedColor,
            Palette = palette
        };
    }

    public void LoadState(InverseCollatzState state)
    {
        StopAnimation();
        _renderCts?.Cancel();
        _updatingControls = true;
        DepthBox.Text = Math.Clamp(state.Depth, 1, 500).ToString(CultureInfo.InvariantCulture);
        MaxNodesBox.Text = Math.Clamp(state.MaxNodes, 10, 1_000_000).ToString(CultureInfo.InvariantCulture);
        LayoutBox.SelectedIndex = Math.Clamp((int)state.Layout, 0, 1);
        int modulusIndex = state.Modulus switch { 3 => 1, 6 => 2, 12 => 3, _ => 0 };
        ModulusBox.SelectedIndex = modulusIndex;
        UpdateResidueOptions(state.Modulus, state.Residue);
        FilterBehaviorBox.SelectedIndex = Math.Clamp((int)state.FilterBehavior, 0, 1);
        NodeRadiusBox.Text = Format(state.NodeRadius);
        LineThicknessBox.Text = Format(state.LineThickness);
        AnimationIntervalBox.Text = Math.Clamp(state.AnimationIntervalMs, 30, 5_000)
            .ToString(CultureInfo.InvariantCulture);
        BackgroundColorSelector.SelectedColor = state.BackgroundColor;
        _centerX = state.CenterX;
        _centerY = state.CenterY;
        _zoom = Math.Clamp(state.Zoom, 0.05, 1_000);
        _visibleDepth = Math.Clamp(state.VisibleDepth, 0, Math.Clamp(state.Depth, 1, 500));
        _animationTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(state.AnimationIntervalMs, 30, 5_000));
        _paletteManager.ActivePalette = state.Palette.Clone($"Загружено: {state.SaveName}");
        InvalidateTree();
        _updatingControls = false;
        UpdatePreviewTransform();
        UpdateDepthStatus();
        ScheduleRender();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, CanvasImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        InverseCollatzState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        RenderIndependentBitmapAsync(CloneState(state), width, height, 1, token, progress);

    private void TreeParameter_OnChanged(object sender, EventArgs e)
    {
        if (_updatingControls) return;
        StopAnimation();
        InvalidateTree();
        if (int.TryParse(DepthBox.Text, out int depth)) _visibleDepth = Math.Clamp(depth, 0, 500);
        ScheduleRender();
    }

    private void RenderParameter_OnChanged(object sender, EventArgs e)
    {
        if (_updatingControls) return;
        if (ReferenceEquals(sender, LayoutBox))
        {
            _layoutPoints = null;
            UpdatePreviewTransform();
        }
        ScheduleRender();
    }

    private void ModulusBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int oldResidue = ResidueBox?.SelectedIndex > 0 ? ResidueBox.SelectedIndex - 1 : -1;
        UpdateResidueOptions(SelectedModulus, oldResidue);
        if (!_updatingControls) ScheduleRender();
    }

    private void AnimationIntervalBox_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(AnimationIntervalBox.Text, out int interval) && interval is >= 30 and <= 5_000)
            _animationTimer.Interval = TimeSpan.FromMilliseconds(interval);
    }

    private int SelectedModulus
    {
        get
        {
            if (ModulusBox?.SelectedItem is not ComboBoxItem item) return 0;
            return int.TryParse(item.Tag?.ToString(), out int modulus) ? modulus : 0;
        }
    }

    private void UpdateResidueOptions(int modulus, int selectedResidue)
    {
        if (ResidueBox is null || ResiduePanel is null) return;
        ResidueBox.Items.Clear();
        ResidueBox.Items.Add("Все остатки");
        for (int residue = 0; residue < modulus; residue++) ResidueBox.Items.Add($"r = {residue}");
        ResidueBox.SelectedIndex = selectedResidue >= 0 && selectedResidue < modulus
            ? selectedResidue + 1 : 0;
        ResiduePanel.Visibility = modulus > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); _renderTimer.Stop(); _ = RenderCurrentAsync(); }
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();

    private void PaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new InverseCollatzPaletteWindow(_paletteManager) { Owner = this };
        dialog.PaletteApplied += (_, _) => ScheduleRender();
        dialog.ShowDialog();
    }

    private void SavesButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); SaveManagerWindow.Open(this, SaveManagerConfigurations.ForInverseCollatz(this, _saveStore)); }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        InverseCollatzState state;
        try { state = CaptureState("export"); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "inverse_collatz_tree",
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            MaxSsaaFactor = 2,
            RenderAsync = (request, token, progress) => RenderIndependentBitmapAsync(
                state, request.Width, request.Height, request.SsaaFactor, token, progress)
        });
    }

    private void ResetViewButton_OnClick(object sender, RoutedEventArgs e)
    {
        _centerX = 0;
        _centerY = 0;
        _zoom = 1;
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void ScheduleRender()
    {
        if (!IsLoaded) return;
        _renderCts?.Cancel();
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private void RenderTimer_OnTick(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        _ = RenderCurrentAsync();
    }

    private async Task RenderCurrentAsync()
    {
        if (_isRendering)
        {
            _renderPending = true;
            return;
        }

        InverseCollatzState state;
        try { state = CaptureState("preview"); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }

        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        CancellationToken token = cts.Token;
        _isRendering = true;
        CancelButton.IsEnabled = true;
        StatusText.Text = "Построение точного обратного дерева...";
        var totalWatch = Stopwatch.StartNew();
        int factor = SsaaBox.SelectedItem is ComboBoxItem item &&
                     int.TryParse(item.Tag?.ToString(), out int selectedFactor)
            ? Math.Clamp(selectedFactor, 1, 2) : 1;
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        int renderWidth = checked(surface.PixelWidth * factor);
        int renderHeight = checked(surface.PixelHeight * factor);
        InverseCollatzTree? cachedTree = _tree is not null && _builtDepth == state.Depth &&
                                         _builtMaxNodes == state.MaxNodes ? _tree : null;
        InverseCollatzPoint[]? cachedPoints = cachedTree is not null &&
                                                ReferenceEquals(cachedTree, _tree) &&
                                                _layoutPoints is not null && _cachedLayout == state.Layout
            ? _layoutPoints : null;
        IProgress<int> progress = new Progress<int>(value => RenderProgress.Value = value);

        try
        {
            WindowRenderOutput output = await Task.Run(() =>
            {
                InverseCollatzTree tree = cachedTree ?? InverseCollatzTreeRenderer.BuildTree(
                    state.Depth, state.MaxNodes, token, value => progress.Report(value));
                InverseCollatzPoint[] points = cachedPoints ??
                    InverseCollatzTreeRenderer.CalculateLayout(tree, state.Layout, token);
                if (cachedTree is not null) progress.Report(45);
                InverseCollatzRenderResult render = InverseCollatzTreeRenderer.Render(tree, state,
                    renderWidth, renderHeight, state.VisibleDepth, token,
                    value => progress.Report(value), points, factor);
                return new WindowRenderOutput(tree, points, render);
            }, token);

            if (token.IsCancellationRequested) return;
            _tree = output.Tree;
            _layoutPoints = output.Points;
            _cachedLayout = state.Layout;
            _builtDepth = state.Depth;
            _builtMaxNodes = state.MaxNodes;
            _visibleDepth = Math.Min(_visibleDepth, output.Tree.MaximumDepth);
            BitmapSource bitmap = CreateBitmap(output.Render.Pixels, renderWidth, renderHeight);
            if (factor > 1)
                bitmap = await Task.Run(() => BitmapResampler.ResizeLanczos3(bitmap,
                    surface.PixelWidth, surface.PixelHeight, token));
            if (token.IsCancellationRequested) return;
            CanvasImage.Source = bitmap;
            _renderedCenterX = state.CenterX;
            _renderedCenterY = state.CenterY;
            _renderedZoom = state.Zoom;
            _renderedLayout = state.Layout;
            _hasRenderedFrame = true;
            UpdatePreviewTransform();
            totalWatch.Stop();
            string truncated = output.Tree.Truncated ? "; достигнут лимит узлов" : string.Empty;
            StatusText.Text = $"Узлов: {output.Tree.Nodes.Count:N0}; показано: {output.Render.DrawnNodes:N0}{truncated}. " +
                              $"Построение: {output.Tree.BuildDuration.TotalMilliseconds:F1} мс; " +
                              $"отрисовка: {output.Render.DrawDuration.TotalMilliseconds:F1} мс; " +
                              $"всего: {totalWatch.Elapsed.TotalMilliseconds:F1} мс.";
            UpdateDepthStatus();
        }
        catch (OperationCanceledException) { StatusText.Text = "Рендер отменён"; }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка построения дерева";
            MessageBox.Show(this, ex.Message, "Обратное дерево Коллатца",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isRendering = false;
            CancelButton.IsEnabled = false;
            RenderProgress.Value = 0;
            if (ReferenceEquals(_renderCts, cts)) _renderCts = null;
            cts.Dispose();
            if (_renderPending)
            {
                _renderPending = false;
                ScheduleRender();
            }
        }
    }

    private async void AnimationButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isAnimating)
            {
                StopAnimation();
                return;
            }
            if (_tree is null)
            {
                _visibleDepth = 0;
                await RenderCurrentAsync();
            }
            int maximum = _tree?.MaximumDepth ?? ReadInt(DepthBox.Text, "максимальная глубина", 1, 500);
            if (_visibleDepth >= maximum) _visibleDepth = 0;
            _isAnimating = true;
            AnimationButton.Content = "Пауза";
            _animationTimer.Start();
            UpdateDepthStatus();
            if (!_isRendering) await RenderCurrentAsync();
        }
        catch (Exception exception)
        {
            StopAnimation();
            StatusText.Text = exception.Message;
        }
    }

    private void RestartAnimationButton_OnClick(object sender, RoutedEventArgs e)
    {
        _visibleDepth = 0;
        UpdateDepthStatus();
        ScheduleRender();
    }

    private void AnimationTimer_OnTick(object? sender, EventArgs e)
    {
        if (_isRendering) return;
        int maximum = _tree?.MaximumDepth ?? 0;
        if (_visibleDepth >= maximum)
        {
            StopAnimation();
            return;
        }
        _visibleDepth++;
        UpdateDepthStatus();
        _ = RenderCurrentAsync();
    }

    private void StopAnimation()
    {
        _isAnimating = false;
        _animationTimer.Stop();
        if (AnimationButton is not null) AnimationButton.Content = "Запустить рост";
    }

    private void UpdateDepthStatus()
    {
        if (DepthStatusText is null) return;
        int maximum = _tree?.MaximumDepth ??
            (int.TryParse(DepthBox?.Text, out int depth) ? depth : 0);
        DepthStatusText.Text = $"Видимая глубина: {_visibleDepth} / {maximum}";
    }

    private void InvalidateTree()
    {
        _tree = null;
        _layoutPoints = null;
        _builtDepth = 0;
        _builtMaxNodes = 0;
    }

    private static async Task<BitmapSource> RenderIndependentBitmapAsync(InverseCollatzState state,
        int width, int height, int ssaa, CancellationToken token, IProgress<int>? progress)
    {
        int factor = Math.Clamp(ssaa, 1, 2);
        int renderWidth = checked(width * factor);
        int renderHeight = checked(height * factor);
        InverseCollatzRenderResult result = await Task.Run(() =>
        {
            InverseCollatzTree tree = InverseCollatzTreeRenderer.BuildTree(state.Depth,
                state.MaxNodes, token, value => progress?.Report(value * 90 / 100));
            InverseCollatzPoint[] points = InverseCollatzTreeRenderer.CalculateLayout(tree, state.Layout, token);
            return InverseCollatzTreeRenderer.Render(tree, state, renderWidth, renderHeight,
                Math.Clamp(state.VisibleDepth, 0, tree.MaximumDepth), token,
                value => progress?.Report(value * 90 / 100), points, factor);
        }, token);
        BitmapSource bitmap = CreateBitmap(result.Pixels, renderWidth, renderHeight);
        if (factor > 1 && !token.IsCancellationRequested)
            bitmap = await Task.Run(() => BitmapResampler.ResizeLanczos3(bitmap, width, height, token,
                value => progress?.Report(value)));
        progress?.Report(100);
        return bitmap;
    }

    private static BitmapSource CreateBitmap(byte[] pixels, int width, int height)
    {
        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96,
            PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static InverseCollatzState CloneState(InverseCollatzState source) => new()
    {
        SaveName = source.SaveName,
        Timestamp = source.Timestamp,
        Depth = source.Depth,
        VisibleDepth = source.VisibleDepth,
        MaxNodes = source.MaxNodes,
        Layout = source.Layout,
        Modulus = source.Modulus,
        Residue = source.Residue,
        FilterBehavior = source.FilterBehavior,
        NodeRadius = source.NodeRadius,
        LineThickness = source.LineThickness,
        AnimationIntervalMs = source.AnimationIntervalMs,
        CenterX = source.CenterX,
        CenterY = source.CenterY,
        Zoom = source.Zoom,
        BackgroundColor = source.BackgroundColor,
        Palette = source.Palette.Clone(source.Palette.Name)
    };

    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        InverseCollatzLayout layout = (InverseCollatzLayout)Math.Clamp(LayoutBox.SelectedIndex, 0, 1);
        Point mouse = e.GetPosition(CanvasHost);
        (double x, double y) before = ScreenToWorld(mouse, layout);
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), 0.05, 1_000);
        (double x, double y) after = ScreenToWorld(mouse, layout);
        _centerX += before.x - after.x;
        _centerY += before.y - after.y;
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _renderCts?.Cancel();
        _renderTimer.Stop();
        _isPanning = true;
        _lastPanPoint = e.GetPosition(CanvasHost);
        CanvasHost.CaptureMouse();
        Mouse.OverrideCursor = Cursors.SizeAll;
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        InverseCollatzLayout layout = (InverseCollatzLayout)Math.Clamp(LayoutBox.SelectedIndex, 0, 1);
        Point current = e.GetPosition(CanvasHost);
        (double x, double y) before = ScreenToWorld(_lastPanPoint, layout);
        (double x, double y) after = ScreenToWorld(current, layout);
        _centerX += before.x - after.x;
        _centerY += before.y - after.y;
        _lastPanPoint = current;
        UpdatePreviewTransform();
    }

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        CanvasHost.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        ScheduleRender();
    }

    private (double x, double y) ScreenToWorld(Point point, InverseCollatzLayout layout)
    {
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double height = Math.Max(1, CanvasHost.ActualHeight);
        if (layout == InverseCollatzLayout.Radial)
        {
            double scale = Math.Min(width, height) * 0.46 * _zoom;
            return (_centerX + (point.X - width / 2) / scale,
                _centerY + (point.Y - height / 2) / scale);
        }
        return (_centerX + (point.X - width / 2) / (width * 0.46 * _zoom),
            _centerY + (point.Y - height / 2) / (height * 0.46 * _zoom));
    }

    private void UpdatePreviewTransform()
    {
        if (!_hasRenderedFrame || _renderedZoom <= 0 || _zoom <= 0 ||
            CanvasHost.ActualWidth <= 0 || CanvasHost.ActualHeight <= 0) return;

        InverseCollatzLayout currentLayout =
            (InverseCollatzLayout)Math.Clamp(LayoutBox.SelectedIndex, 0, 1);
        if (_renderedLayout != currentLayout)
        {
            ResetPreviewTransform();
            return;
        }

        double width = CanvasHost.ActualWidth;
        double height = CanvasHost.ActualHeight;
        double scale = _zoom / _renderedZoom;
        double horizontalPixelsPerUnit;
        double verticalPixelsPerUnit;
        if (currentLayout == InverseCollatzLayout.Radial)
        {
            horizontalPixelsPerUnit = verticalPixelsPerUnit =
                Math.Min(width, height) * 0.46 * _zoom;
        }
        else
        {
            horizontalPixelsPerUnit = width * 0.46 * _zoom;
            verticalPixelsPerUnit = height * 0.46 * _zoom;
        }

        _previewScale.ScaleX = scale;
        _previewScale.ScaleY = scale;
        _previewTranslation.X = (_renderedCenterX - _centerX) * horizontalPixelsPerUnit;
        _previewTranslation.Y = (_renderedCenterY - _centerY) * verticalPixelsPerUnit;
        bool identity = Math.Abs(scale - 1) < 1e-12 &&
                        Math.Abs(_previewTranslation.X) < 0.01 &&
                        Math.Abs(_previewTranslation.Y) < 0.01;
        RenderOptions.SetBitmapScalingMode(CanvasImage,
            identity ? BitmapScalingMode.HighQuality : BitmapScalingMode.LowQuality);
    }

    private void ResetPreviewTransform()
    {
        _previewScale.ScaleX = 1;
        _previewScale.ScaleY = 1;
        _previewTranslation.X = 0;
        _previewTranslation.Y = 0;
        RenderOptions.SetBitmapScalingMode(CanvasImage, BitmapScalingMode.HighQuality);
    }

    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e) =>
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost,
            ToggleControlsButton, 310, ScheduleRender);

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        _fullscreen.HandleKey(this, e.Key);
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _renderTimer.Stop();
        _animationTimer.Stop();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
    }

    private static int ReadInt(string text, string parameter, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
            value < minimum || value > maximum)
            throw new InvalidOperationException($"Параметр «{parameter}» должен быть целым числом от {minimum} до {maximum}.");
        return value;
    }

    private static double ReadDouble(string text, string parameter, double minimum, double maximum)
    {
        bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                      double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        if (!parsed || !double.IsFinite(value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"Параметр «{parameter}» должен быть от {minimum:G8} до {maximum:G8}.");
        return value;
    }

    private static string Format(double value) => value.ToString("G15", CultureInfo.InvariantCulture);

    private sealed record WindowRenderOutput(InverseCollatzTree Tree, InverseCollatzPoint[] Points,
        InverseCollatzRenderResult Render);
}
