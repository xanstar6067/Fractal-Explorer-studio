using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.ColorPicking;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class Ifs3DWindow : Window
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly Ifs3DSaveStore _saves = new();
    private readonly List<Ifs3DTransform> _transforms = [];
    private CancellationTokenSource? _renderCts;
    private int _generation;
    private bool _syncing;
    private string? _presetId;
    private Color _pointColor = Colors.Aquamarine;
    private Color _backgroundColor = Colors.Black;
    private Point _dragStart;
    private MouseButton? _dragButton;
    private double _panX, _panY;

    public Ifs3DWindow()
    {
        InitializeComponent();
        PresetBox.ItemsSource = Ifs3DPresets.All;
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RenderAsync(); };
        ApplyPreset(Ifs3DPresets.All[0]);
        Loaded += (_, _) => Schedule();
    }

    public Ifs3DState CaptureState(string name)
    {
        if (TransformList.SelectedIndex >= 0 &&
            (MatrixBoxes().Any(box => !Read(box.Text, out double value) || !double.IsFinite(value)) ||
             !Read(ProbabilityBox.Text, out double selectedProbability) || selectedProbability < 0))
            throw new InvalidOperationException("Закончите ввод матрицы; вероятность не может быть отрицательной.");
        if (!int.TryParse(IterationsBox.Text, out int iterations) || iterations is < 10_000 or > 10_000_000)
            throw new InvalidOperationException("Итерации должны быть от 10 000 до 10 000 000.");
        if (!Read(YawBox.Text, out double yaw) || !Read(PitchBox.Text, out double pitch) ||
            !Read(ZoomBox.Text, out double zoom) || !double.IsFinite(yaw) || !double.IsFinite(pitch) ||
            !double.IsFinite(zoom) || zoom is < .1 or > 20)
            throw new InvalidOperationException("Проверьте азимут, наклон и масштаб (0,1–20).");
        if (_transforms.Count == 0) throw new InvalidOperationException("Добавьте хотя бы одно преобразование.");
        if (_transforms.Any(t => Values(t).Any(v => !double.IsFinite(v) || Math.Abs(v) > 1000) || t.Probability < 0) ||
            _transforms.Sum(t => t.Probability) <= 0)
            throw new InvalidOperationException("Матрица и перенос должны быть конечными (|значение| ≤ 1000), а сумма весов — положительной.");

        return new Ifs3DState
        {
            SaveName = name, Timestamp = DateTime.Now, PointOfInterestId = _presetId,
            Iterations = iterations, Yaw = yaw, Pitch = pitch, Zoom = zoom, PanX = _panX, PanY = _panY,
            PointColor = _pointColor, BackgroundColor = _backgroundColor,
            Transforms = _transforms.Select(t => t.Clone()).ToList()
        };
    }

    public void LoadState(Ifs3DState state)
    {
        _syncing = true;
        try
        {
            _presetId = state.PointOfInterestId;
            IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
            YawBox.Text = Format(state.Yaw);
            PitchBox.Text = Format(state.Pitch);
            ZoomBox.Text = Format(state.Zoom);
            _panX = state.PanX; _panY = state.PanY;
            _pointColor = state.PointColor; _backgroundColor = state.BackgroundColor;
            _transforms.Clear();
            _transforms.AddRange(state.Transforms.Select(t => t.Clone()));
            PresetBox.SelectedItem = Ifs3DPresets.All.FirstOrDefault(p => p.Id == _presetId);
            RebindTransforms(0);
        }
        finally { _syncing = false; }
        UpdateColors();
        Schedule();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, CurrentImage);

    public Task<BitmapSource> RenderStatePreviewAsync(Ifs3DState state, int width, int height,
        CancellationToken token, IProgress<int>? progress = null) =>
        Ifs3DRenderer.RenderBitmapAsync(state.Clone(), width, height, token, progress);

    private void ApplyPreset(Ifs3DPreset preset)
    {
        Ifs3DState state = preset.State.Clone();
        state.PointOfInterestId = preset.Id;
        LoadState(state);
    }

    private void Preset_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && PresetBox.SelectedItem is Ifs3DPreset preset) ApplyPreset(preset);
    }

    private void RebindTransforms(int selected)
    {
        _syncing = true;
        TransformList.ItemsSource = null;
        TransformList.ItemsSource = _transforms;
        TransformList.SelectedIndex = _transforms.Count == 0 ? -1 : Math.Clamp(selected, 0, _transforms.Count - 1);
        _syncing = false;
        ShowSelectedTransform();
    }

    private void TransformList_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing) ShowSelectedTransform();
    }

    private void ShowSelectedTransform()
    {
        _syncing = true;
        try
        {
            Ifs3DTransform? t = TransformList.SelectedIndex is int i && i >= 0 && i < _transforms.Count ? _transforms[i] : null;
            TextBox[] boxes = MatrixBoxes();
            double[] values = t is null ? new double[13] : Values(t);
            for (int j = 0; j < boxes.Length; j++)
            {
                boxes[j].IsEnabled = t is not null;
                boxes[j].Text = t is null ? string.Empty : Format(values[j]);
            }
        }
        finally { _syncing = false; }
    }

    private void Matrix_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || TransformList.SelectedIndex < 0) return;
        TextBox[] boxes = MatrixBoxes();
        double[] values = new double[13];
        for (int i = 0; i < boxes.Length; i++)
            if (!Read(boxes[i].Text, out values[i]) || !double.IsFinite(values[i]))
            { StatusText.Text = "Закончите ввод матрицы и вероятности."; return; }
        if (values[12] < 0) { StatusText.Text = "Вероятность не может быть отрицательной."; return; }
        Ifs3DTransform t = _transforms[TransformList.SelectedIndex];
        (t.M11, t.M12, t.M13, t.Tx, t.M21, t.M22, t.M23, t.Ty,
            t.M31, t.M32, t.M33, t.Tz, t.Probability) =
            (values[0], values[1], values[2], values[3], values[4], values[5], values[6],
                values[7], values[8], values[9], values[10], values[11], values[12]);
        _presetId = null;
        int selected = TransformList.SelectedIndex;
        _syncing = true;
        PresetBox.SelectedItem = null;
        TransformList.ItemsSource = null;
        TransformList.ItemsSource = _transforms;
        TransformList.SelectedIndex = selected;
        _syncing = false;
        Schedule();
    }

    private void AddTransform_OnClick(object sender, RoutedEventArgs e)
    {
        _transforms.Add(Ifs3DTransform.Contract(.5, .25, .25, .25));
        _presetId = null;
        PresetBox.SelectedItem = null;
        RebindTransforms(_transforms.Count - 1);
        Schedule();
    }

    private void DuplicateTransform_OnClick(object sender, RoutedEventArgs e)
    {
        int i = TransformList.SelectedIndex;
        if (i < 0) return;
        _transforms.Insert(i + 1, _transforms[i].Clone());
        _presetId = null;
        PresetBox.SelectedItem = null;
        RebindTransforms(i + 1);
        Schedule();
    }

    private void DeleteTransform_OnClick(object sender, RoutedEventArgs e)
    {
        int i = TransformList.SelectedIndex;
        if (i < 0) return;
        _transforms.RemoveAt(i);
        _presetId = null;
        PresetBox.SelectedItem = null;
        RebindTransforms(i);
        Schedule();
    }

    private void Parameter_OnChanged(object sender, TextChangedEventArgs e) { if (!_syncing) Schedule(); }

    private void Schedule()
    {
        if (!IsLoaded) return;
        _generation++;
        _renderCts?.Cancel();
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task RenderAsync()
    {
        Ifs3DState state;
        try { state = CaptureState("preview"); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }
        int generation = _generation;
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        var watch = Stopwatch.StartNew();
        try
        {
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            int width = surface.PixelWidth, height = surface.PixelHeight;
            RenderProgress.Value = 0;
            StatusText.Text = "Построение объёмной орбиты…";
            var progress = new Progress<int>(value =>
            {
                if (generation == _generation) RenderProgress.Value = value;
            });
            BitmapSource bitmap = await Ifs3DRenderer.RenderBitmapAsync(state, width, height, cts.Token, progress);
            if (generation != _generation || cts.IsCancellationRequested) return;
            CurrentImage.Source = bitmap;
            StatusText.Text = $"{state.Iterations:N0} точек · {state.Transforms.Count} преобразований · {watch.Elapsed.TotalSeconds:F2} с";
            RenderProgress.Value = 100;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _generation) StatusText.Text = ex.Message; }
        finally
        {
            if (ReferenceEquals(_renderCts, cts)) _renderCts = null;
            cts.Dispose();
        }
    }

    private void UpdateColors()
    {
        PointColorSwatch.Background = new SolidColorBrush(_pointColor);
        BackgroundColorSwatch.Background = new SolidColorBrush(_backgroundColor);
        CanvasHost.Background = new SolidColorBrush(_backgroundColor);
    }

    private void PointColor_OnClick(object sender, RoutedEventArgs e)
    {
        if (ColorSelectionService.Default.TrySelectColor(this, _pointColor, out Color color))
        { _pointColor = color; UpdateColors(); Schedule(); }
    }

    private void BackgroundColor_OnClick(object sender, RoutedEventArgs e)
    {
        if (ColorSelectionService.Default.TrySelectColor(this, _backgroundColor, out Color color))
        { _backgroundColor = color; UpdateColors(); Schedule(); }
    }

    private void Saves_OnClick(object sender, RoutedEventArgs e) =>
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForIfs3D(this, _saves));

    private void Export_OnClick(object sender, RoutedEventArgs e)
    {
        Ifs3DState state;
        try { state = CaptureState("export"); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        double sourcePixels = surface.PixelWidth * (double)surface.PixelHeight;
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = "ifs3d", InitialWidth = surface.PixelWidth, InitialHeight = surface.PixelHeight,
            MaxSsaaFactor = 4, HasNativeSsaa = false,
            RenderAsync = (request, token, progress) =>
            {
                Ifs3DState snapshot = state.Clone();
                snapshot.Iterations = (int)Math.Min(10_000_000,
                    Math.Ceiling(state.Iterations * Math.Max(1, request.Width * (double)request.Height / sourcePixels)));
                return Ifs3DRenderer.RenderBitmapAsync(snapshot, request.Width, request.Height, token, progress);
            }
        });
    }

    private void ResetView_OnClick(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        YawBox.Text = "35"; PitchBox.Text = "25"; ZoomBox.Text = "1";
        _panX = _panY = 0;
        _syncing = false;
        Schedule();
    }

    private void Canvas_OnSizeChanged(object sender, SizeChangedEventArgs e) => Schedule();

    private void Canvas_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragButton = e.ChangedButton;
        _dragStart = e.GetPosition(CanvasHost);
        CanvasHost.CaptureMouse();
    }

    private void Canvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragButton is null) return;
        Point current = e.GetPosition(CanvasHost);
        double dx = current.X - _dragStart.X, dy = current.Y - _dragStart.Y;
        _dragStart = current;
        if (_dragButton == MouseButton.Left && Read(YawBox.Text, out double yaw) && Read(PitchBox.Text, out double pitch))
        {
            _syncing = true;
            YawBox.Text = Format(yaw + dx * .45);
            PitchBox.Text = Format(Math.Clamp(pitch + dy * .45, -89, 89));
            _syncing = false;
            Schedule();
        }
        else if (_dragButton == MouseButton.Right)
        {
            _panX += dx / Math.Max(1, CanvasHost.ActualWidth);
            _panY += dy / Math.Max(1, CanvasHost.ActualHeight);
            Schedule();
        }
    }

    private void Canvas_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragButton = null;
        CanvasHost.ReleaseMouseCapture();
    }

    private void Canvas_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Read(ZoomBox.Text, out double zoom)) return;
        _syncing = true;
        ZoomBox.Text = Format(Math.Clamp(zoom * Math.Pow(1.15, e.Delta / 120d), .1, 20));
        _syncing = false;
        Schedule();
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _debounce.Stop();
        _renderCts?.Cancel();
    }

    private TextBox[] MatrixBoxes() =>
        [M11Box, M12Box, M13Box, TxBox, M21Box, M22Box, M23Box, TyBox,
            M31Box, M32Box, M33Box, TzBox, ProbabilityBox];

    private static double[] Values(Ifs3DTransform t) =>
        [t.M11, t.M12, t.M13, t.Tx, t.M21, t.M22, t.M23, t.Ty,
            t.M31, t.M32, t.M33, t.Tz, t.Probability];

    private static bool Read(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static string Format(double value) => value.ToString("G10", CultureInfo.InvariantCulture);
}
