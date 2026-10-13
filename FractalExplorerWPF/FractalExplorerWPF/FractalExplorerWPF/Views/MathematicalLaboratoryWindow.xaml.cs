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

public partial class MathematicalLaboratoryWindow : Window
{
    private readonly MathematicalLaboratoryDefinition _definition;
    private readonly MathematicalLaboratorySaveStore _saveStore;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(190) };
    private readonly DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private readonly TransformGroup _previewTransform = new();
    private readonly ScaleTransform _previewScale = new(1, 1);
    private readonly RotateTransform _previewRotation = new();
    private readonly TranslateTransform _previewTranslation = new();
    private readonly List<LaboratoryPoint> _inputPoints = [];
    private CancellationTokenSource? _renderCts;
    private bool _rendering;
    private bool _renderPending;
    private bool _syncing;
    private bool _panning;
    private bool _drawing;
    private bool _controlsVisible = true;
    private Point _pointerStart;
    private LaboratoryPoint _worldStart;
    private double _viewCenterX;
    private double _viewCenterY;
    private double _zoom = 1;
    private double _phase;
    private double _anchorX;
    private double _anchorY;
    private double _renderedCenterX;
    private double _renderedCenterY;
    private double _renderedZoom = 1;
    private double _renderedRotation;
    private int _animationStep;
    private bool _hasRenderedFrame;
    private int _lastModeIndex = -1;
    private readonly FullscreenToggle _fullscreen = new();

    public MathematicalLaboratoryWindow(MathematicalLaboratoryKind kind)
    {
        LaboratoryKind = kind;
        _definition = MathematicalLaboratoryCatalog.GetDefinition(kind);
        _saveStore = new MathematicalLaboratorySaveStore(kind);
        InitializeComponent();
        _previewTransform.Children.Add(_previewScale);
        _previewTransform.Children.Add(_previewRotation);
        _previewTransform.Children.Add(_previewTranslation);
        CanvasImage.RenderTransformOrigin = new Point(0.5, 0.5);
        CanvasImage.RenderTransform = _previewTransform;
        ConfigureWindow();
        LoadState(MathematicalLaboratoryCatalog.CreateDefaultState(kind));

        _renderTimer.Tick += (_, _) =>
        {
            _renderTimer.Stop();
            _renderPending = false;
            _ = RenderAsync();
        };
        _animationTimer.Tick += AnimationTimer_OnTick;
        Loaded += (_, _) =>
        {
            if (AnimateCheck.IsChecked == true)
            {
                _animationTimer.Start();
                _ = RenderAsync();
            }
            else
            {
                ScheduleRender();
            }
        };
    }

    public MathematicalLaboratoryKind LaboratoryKind { get; }
    public string LaboratoryTitle => _definition.Title;

    public string GetModeName(int mode) => mode >= 0 && mode < _definition.Modes.Length
        ? _definition.Modes[mode]
        : _definition.Modes[0];

    public string GetStateDetails(MathematicalLaboratoryState state)
    {
        if (state.Kind == MathematicalLaboratoryKind.ChladniWaveInterference && state.Mode >= 3)
        {
            string sourceDetails = state.Mode switch
            {
                3 => "Источники: 2",
                4 => $"Источники: {state.PrimaryValue:N0}",
                _ => $"Щели: {state.PrimaryValue:N0}"
            };
            return $"{sourceDetails} · Частота: {state.SecondaryValue:N0} · " +
                   $"Сдвиг фазы: {state.TertiaryValue:N0}° · Геометрия: {state.Parameter:G7}";
        }

        return $"{_definition.PrimaryLabel}: {state.PrimaryValue:N0} · " +
               $"{_definition.SecondaryLabel}: {state.SecondaryValue:N0} · " +
               $"{_definition.ParameterLabel}: {state.Parameter:G7}";
    }

    public MathematicalLaboratoryState CaptureState(string name)
    {
        if (!int.TryParse(PrimaryBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int primary) ||
            primary < _definition.PrimaryMinimum || primary > _definition.PrimaryMaximum)
            throw new InvalidOperationException(
                $"«{_definition.PrimaryLabel}»: допустимы значения {_definition.PrimaryMinimum:N0}–{_definition.PrimaryMaximum:N0}.");
        if (!int.TryParse(SecondaryBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int secondary) ||
            secondary < _definition.SecondaryMinimum || secondary > _definition.SecondaryMaximum)
            throw new InvalidOperationException(
                $"«{_definition.SecondaryLabel}»: допустимы значения {_definition.SecondaryMinimum:N0}–{_definition.SecondaryMaximum:N0}.");
        if (!int.TryParse(TertiaryBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int tertiary) ||
            tertiary < _definition.TertiaryMinimum || tertiary > _definition.TertiaryMaximum)
            throw new InvalidOperationException(
                $"«{_definition.TertiaryLabel}»: допустимы значения {_definition.TertiaryMinimum:N0}–{_definition.TertiaryMaximum:N0}.");
        if (!ReadDouble(ParameterBox.Text, out double parameter) ||
            !double.IsFinite(parameter) ||
            parameter < _definition.ParameterMinimum || parameter > _definition.ParameterMaximum)
            throw new InvalidOperationException(
                $"«{_definition.ParameterLabel}»: допустимы значения {_definition.ParameterMinimum:G7}–{_definition.ParameterMaximum:G7}.");
        if (!ReadDouble(RotationBox.Text, out double rotation) || !double.IsFinite(rotation) || Math.Abs(rotation) > 1_000_000)
            throw new InvalidOperationException("Поворот должен быть конечным числом от −1 000 000 до 1 000 000 градусов.");
        if (LaboratoryKind == MathematicalLaboratoryKind.HyperbolicGeometry &&
            ModeBox.SelectedIndex == 3 && (secondary - 2) * (tertiary - 2) <= 4)
            throw new InvalidOperationException("Для гиперболической мозаики должно выполняться (p−2)(q−2) > 4.");

        return new MathematicalLaboratoryState
        {
            SaveName = name,
            Timestamp = DateTime.Now,
            Kind = LaboratoryKind,
            Mode = Math.Clamp(ModeBox.SelectedIndex, 0, _definition.Modes.Length - 1),
            PrimaryValue = primary,
            SecondaryValue = secondary,
            TertiaryValue = tertiary,
            Parameter = parameter,
            Phase = _phase,
            ShowGuides = ShowGuidesCheck.IsChecked == true,
            Filled = FilledCheck.IsChecked == true,
            Animate = AnimateCheck.IsChecked == true,
            ViewCenterX = _viewCenterX,
            ViewCenterY = _viewCenterY,
            Zoom = _zoom,
            Rotation = rotation,
            AnchorX = _anchorX,
            AnchorY = _anchorY,
            BackgroundColor = BackgroundColorSelector.SelectedColor,
            PrimaryColor = PrimaryColorSelector.SelectedColor,
            SecondaryColor = SecondaryColorSelector.SelectedColor,
            AccentColor = AccentColorSelector.SelectedColor,
            InputPoints = [.. _inputPoints]
        };
    }

    public void LoadState(MathematicalLaboratoryState state)
    {
        if (state.Kind != LaboratoryKind)
            throw new InvalidOperationException("Сохранение относится к другой математической лаборатории.");
        _renderCts?.Cancel();
        _syncing = true;
        try
        {
            ModeBox.SelectedIndex = Math.Clamp(state.Mode, 0, _definition.Modes.Length - 1);
            _lastModeIndex = ModeBox.SelectedIndex;
            PrimaryBox.Text = state.PrimaryValue.ToString(CultureInfo.InvariantCulture);
            SecondaryBox.Text = state.SecondaryValue.ToString(CultureInfo.InvariantCulture);
            TertiaryBox.Text = state.TertiaryValue.ToString(CultureInfo.InvariantCulture);
            ParameterBox.Text = Format(state.Parameter);
            RotationBox.Text = Format(state.Rotation);
            ShowGuidesCheck.IsChecked = state.ShowGuides;
            FilledCheck.IsChecked = state.Filled;
            AnimateCheck.IsChecked = state.Animate;
            _viewCenterX = state.ViewCenterX;
            _viewCenterY = state.ViewCenterY;
            _zoom = Math.Clamp(state.Zoom <= 0 ? 1 : state.Zoom, 0.05, 1_000);
            _phase = state.Phase;
            _anchorX = state.AnchorX;
            _anchorY = state.AnchorY;
            _inputPoints.Clear();
            _inputPoints.AddRange(state.InputPoints ?? []);
            BackgroundColorSelector.SelectedColor = state.BackgroundColor;
            PrimaryColorSelector.SelectedColor = state.PrimaryColor;
            SecondaryColorSelector.SelectedColor = state.SecondaryColor;
            AccentColorSelector.SelectedColor = state.AccentColor;
        }
        finally
        {
            _syncing = false;
        }
        UpdateModeUi();
        UpdateAnimation();
        UpdatePreviewTransform();
        ScheduleRender();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, CanvasImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        MathematicalLaboratoryState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        MathematicalLaboratoryRenderer.RenderBitmapAsync(state.Clone(), width, height, token, progress);

    private void ConfigureWindow()
    {
        Title = _definition.Title;
        TitleText.Text = _definition.Title;
        DescriptionText.Text = _definition.Description;
        InteractionText.Text = _definition.InteractionHint + " F11: полноэкранный режим.";
        PrimaryLabel.Text = _definition.PrimaryLabel;
        SecondaryLabel.Text = _definition.SecondaryLabel;
        TertiaryLabel.Text = _definition.TertiaryLabel;
        ParameterLabel.Text = _definition.ParameterLabel;
        ModeBox.ItemsSource = _definition.Modes;
        ClearInputButton.Visibility = LaboratoryKind is MathematicalLaboratoryKind.CircleInversion
            or MathematicalLaboratoryKind.FourierEpicycles
            or MathematicalLaboratoryKind.VoronoiLloyd ? Visibility.Visible : Visibility.Collapsed;
        NewVariantButton.Visibility = LaboratoryKind is MathematicalLaboratoryKind.VoronoiLloyd
            or MathematicalLaboratoryKind.StochasticMotion
            or MathematicalLaboratoryKind.KleinianSchottky ? Visibility.Visible : Visibility.Collapsed;
        AnimateCheck.Visibility = LaboratoryKind is MathematicalLaboratoryKind.ModularArithmetic
            or MathematicalLaboratoryKind.Phyllotaxis
            or MathematicalLaboratoryKind.FourierEpicycles
            or MathematicalLaboratoryKind.HyperbolicGeometry
            or MathematicalLaboratoryKind.ChladniWaveInterference
            or MathematicalLaboratoryKind.VoronoiLloyd
            or MathematicalLaboratoryKind.RecamanSequence
            or MathematicalLaboratoryKind.KnotStudio
            or MathematicalLaboratoryKind.StochasticMotion
            or MathematicalLaboratoryKind.KleinianSchottky ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Parameter_OnChanged(object sender, EventArgs e)
    {
        if (_syncing) return;
        if (ReferenceEquals(sender, ModeBox)) ApplyModeDefaults();
        UpdateModeUi();
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void ApplyModeDefaults()
    {
        int newMode = Math.Clamp(ModeBox.SelectedIndex, 0, _definition.Modes.Length - 1);
        if (newMode == _lastModeIndex) return;
        if (LaboratoryKind == MathematicalLaboratoryKind.StochasticMotion)
        {
            _syncing = true;
            try
            {
                if (newMode == 3 && _lastModeIndex != 3 && SecondaryBox.Text.Trim() == "12")
                    SecondaryBox.Text = "400";
                else if (_lastModeIndex == 3 && newMode != 3 && SecondaryBox.Text.Trim() == "400")
                    SecondaryBox.Text = "12";
            }
            finally
            {
                _syncing = false;
            }
        }
        _lastModeIndex = newMode;
    }

    private void AnimateCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        UpdateAnimation();
        ScheduleRender();
    }

    private void UpdateModeUi()
    {
        PrimaryLabel.Text = _definition.PrimaryLabel;
        SecondaryLabel.Text = _definition.SecondaryLabel;
        TertiaryLabel.Text = _definition.TertiaryLabel;
        ParameterLabel.Text = _definition.ParameterLabel;
        FilledCheck.Content = "Заливка элементов";
        FilledCheck.IsEnabled = LaboratoryKind is MathematicalLaboratoryKind.RationalNumbers
            or MathematicalLaboratoryKind.CircleInversion
            or MathematicalLaboratoryKind.AperiodicTilings
            or MathematicalLaboratoryKind.HyperbolicGeometry
            or MathematicalLaboratoryKind.ChladniWaveInterference
            or MathematicalLaboratoryKind.VoronoiLloyd
            or MathematicalLaboratoryKind.KnotStudio
            or MathematicalLaboratoryKind.KleinianSchottky;
        if (LaboratoryKind == MathematicalLaboratoryKind.FourierEpicycles)
            ShowGuidesCheck.Content = "Показывать окружности эпициклов";
        else if (LaboratoryKind == MathematicalLaboratoryKind.PascalModulo)
            ShowGuidesCheck.Content = "Показывать границы разрядов";
        else if (LaboratoryKind == MathematicalLaboratoryKind.ChladniWaveInterference)
        {
            int mode = Math.Clamp(ModeBox.SelectedIndex, 0, 5);
            bool standingWave = mode <= 2;
            ShowGuidesCheck.Content = standingWave
                ? "Показывать границу пластины"
                : "Показывать источники волн";
            FilledCheck.Content = "Непрерывное волновое поле";
            if (standingWave)
            {
                PrimaryLabel.Text = mode == 2 ? "Угловая мода m" : "Номер моды m";
                SecondaryLabel.Text = mode == 2 ? "Радиальная мода n" : "Номер моды n";
                TertiaryLabel.Text = "Контурные полосы";
                ParameterLabel.Text = "Порог узлов";
            }
            else
            {
                PrimaryLabel.Text = mode switch
                {
                    3 => "Источников (фикс. 2)",
                    4 => "Число источников",
                    _ => "Число щелей"
                };
                SecondaryLabel.Text = "Частота волны";
                TertiaryLabel.Text = "Сдвиг фазы, °";
                ParameterLabel.Text = "Геометрия источников";
            }
        }
        else if (LaboratoryKind == MathematicalLaboratoryKind.VoronoiLloyd)
        {
            ShowGuidesCheck.Content = ModeBox.SelectedIndex == 4
                ? "Показывать центры и сеть Делоне"
                : "Показывать центры ячеек";
            FilledCheck.Content = "Заливать ячейки";
            ClearInputButton.Content = "Очистить ручные центры";
        }
        else if (LaboratoryKind == MathematicalLaboratoryKind.RecamanSequence)
            ShowGuidesCheck.Content = "Показывать числовую ось / оболочку";
        else if (LaboratoryKind == MathematicalLaboratoryKind.KnotStudio)
        {
            ShowGuidesCheck.Content = "Показывать опорную геометрию";
            FilledCheck.Content = "Объёмная нить";
        }
        else if (LaboratoryKind == MathematicalLaboratoryKind.StochasticMotion)
        {
            ShowGuidesCheck.Content = "Показывать начало и масштаб";
            int mode = Math.Clamp(ModeBox.SelectedIndex, 0, 4);
            SecondaryLabel.Text = mode == 3 ? "Число частиц" : "Число траекторий";
            ParameterLabel.Text = mode == 1 ? "Показатель α Леви" :
                mode == 4 ? "Инерция направления" : "Коэффициент диффузии";
        }
        else if (LaboratoryKind == MathematicalLaboratoryKind.KleinianSchottky)
        {
            ShowGuidesCheck.Content = "Показывать окружности генераторов";
            FilledCheck.Content = "Плотностное свечение";
        }
        else
            ShowGuidesCheck.Content = "Направляющие / связи";
    }

    private void UpdateAnimation()
    {
        if (AnimateCheck.IsChecked == true && IsVisible) _animationTimer.Start();
        else _animationTimer.Stop();
    }

    private void AnimationTimer_OnTick(object? sender, EventArgs e)
    {
        if (_rendering) return;
        _syncing = true;
        try
        {
            switch (LaboratoryKind)
            {
                case MathematicalLaboratoryKind.ModularArithmetic:
                    if (++_animationStep % 4 != 0) return;
                    if (int.TryParse(SecondaryBox.Text, out int coefficient))
                    {
                        int maximum = Math.Max(2, int.TryParse(PrimaryBox.Text, out int modulus) ? modulus : 240);
                        coefficient = coefficient >= maximum ? 1 : coefficient + 1;
                        SecondaryBox.Text = coefficient.ToString(CultureInfo.InvariantCulture);
                    }
                    break;
                case MathematicalLaboratoryKind.Phyllotaxis:
                    if (ModeBox.SelectedIndex != 4) ModeBox.SelectedIndex = 4;
                    if (ReadDouble(ParameterBox.Text, out double angle))
                        ParameterBox.Text = Format(angle + 0.035);
                    break;
                case MathematicalLaboratoryKind.FourierEpicycles:
                    double speed = ReadDouble(ParameterBox.Text, out double parsedSpeed) ? parsedSpeed : 0.7;
                    _phase = (_phase + 0.012 * speed) % 1;
                    break;
                case MathematicalLaboratoryKind.HyperbolicGeometry:
                    if (ReadDouble(RotationBox.Text, out double rotation))
                        RotationBox.Text = Format(rotation + 0.45);
                    break;
                case MathematicalLaboratoryKind.ChladniWaveInterference:
                    _phase = (_phase + 0.018) % 1;
                    break;
                case MathematicalLaboratoryKind.VoronoiLloyd:
                case MathematicalLaboratoryKind.RecamanSequence:
                case MathematicalLaboratoryKind.StochasticMotion:
                    _phase = (_phase + 0.025) % 1;
                    break;
                case MathematicalLaboratoryKind.KnotStudio:
                case MathematicalLaboratoryKind.KleinianSchottky:
                    _phase = (_phase + 0.012) % 1;
                    break;
            }
        }
        finally
        {
            _syncing = false;
        }
        _ = RenderAsync();
    }

    private void ScheduleRender()
    {
        if (!IsLoaded || AnimateCheck.IsChecked == true) return;
        _renderPending = true;
        _renderCts?.Cancel();
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private void Render_OnClick(object sender, RoutedEventArgs e)
    {
        _renderTimer.Stop();
        _renderPending = false;
        _renderCts?.Cancel();
        _ = RenderAsync();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();

    private async Task RenderAsync()
    {
        if (_rendering)
        {
            _renderPending = true;
            return;
        }

        _renderPending = false;
        MathematicalLaboratoryState state;
        try
        {
            state = CaptureState("preview");
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            return;
        }

        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        CancellationToken token = _renderCts.Token;
        _rendering = true;
        CancelButton.IsEnabled = true;
        RenderBadge.Visibility = Visibility.Visible;
        ProgressBar.Value = 0;
        ProgressText.Text = "Подготовка…";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
            var progress = new Progress<int>(value =>
            {
                ProgressBar.Value = value;
                ProgressText.Text = $"Построение: {value}%";
                RenderBadgeText.Text = $"{value}%";
            });
            BitmapSource bitmap = await MathematicalLaboratoryRenderer.RenderBitmapAsync(
                state, surface.PixelWidth, surface.PixelHeight, token, progress);
            if (token.IsCancellationRequested) return;
            CanvasImage.Source = bitmap;
            _renderedCenterX = state.ViewCenterX;
            _renderedCenterY = state.ViewCenterY;
            _renderedZoom = state.Zoom;
            _renderedRotation = state.Rotation;
            _hasRenderedFrame = true;
            UpdatePreviewTransform();
            ProgressBar.Value = 100;
            ProgressText.Text = "Рендер завершён";
            StatusText.Text = $"Готово за {stopwatch.Elapsed.TotalSeconds:F3} сек.";
        }
        catch (OperationCanceledException)
        {
            if (!_renderPending && !_panning && AnimateCheck.IsChecked != true)
                StatusText.Text = "Рендер отменён";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, _definition.Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _rendering = false;
            CancelButton.IsEnabled = false;
            RenderBadge.Visibility = Visibility.Collapsed;
            if (_renderPending && IsLoaded && AnimateCheck.IsChecked != true)
            {
                _renderTimer.Stop();
                _renderTimer.Start();
            }
        }
    }

    private void ResetView_OnClick(object sender, RoutedEventArgs e)
    {
        _viewCenterX = _viewCenterY = 0;
        _zoom = 1;
        _anchorX = _anchorY = 0;
        _phase = 0;
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void ClearInputButton_OnClick(object sender, RoutedEventArgs e)
    {
        _inputPoints.Clear();
        ScheduleRender();
    }

    private void NewVariantButton_OnClick(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        try
        {
            TertiaryBox.Text = Random.Shared.Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
            if (LaboratoryKind == MathematicalLaboratoryKind.VoronoiLloyd)
                _inputPoints.Clear();
            _phase = 0;
        }
        finally
        {
            _syncing = false;
        }
        ScheduleRender();
    }

    private void Saves_OnClick(object sender, RoutedEventArgs e) =>
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForMathematicalLaboratory(this, _saveStore));

    private void Export_OnClick(object sender, RoutedEventArgs e)
    {
        MathematicalLaboratoryState state;
        try
        {
            state = CaptureState("export");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Параметры экспорта",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = $"math-{LaboratoryKind.ToString().ToLowerInvariant()}",
            WindowTitle = $"Экспорт: {_definition.Title}",
            InitialWidth = surface.PixelWidth,
            InitialHeight = surface.PixelHeight,
            HasNativeSsaa = false,
            MaxSsaaFactor = 4,
            RenderAsync = (request, token, progress) =>
                MathematicalLaboratoryRenderer.RenderBitmapAsync(state, request.Width, request.Height, token, progress)
        });
    }

    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePreviewTransform();
        ScheduleRender();
    }

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point point = e.GetPosition(CanvasHost);
        LaboratoryPoint before = ScreenToWorld(point);
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.22 : 0.82), 0.05, 1_000);
        LaboratoryPoint after = ScreenToWorld(point);
        _viewCenterX += before.X - after.X;
        _viewCenterY += before.Y - after.Y;
        UpdatePreviewTransform();
        ScheduleRender();
        e.Handled = true;
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _renderCts?.Cancel();
        Point point = e.GetPosition(CanvasHost);
        bool interactive = LaboratoryKind is MathematicalLaboratoryKind.CircleInversion
            or MathematicalLaboratoryKind.FourierEpicycles
            or MathematicalLaboratoryKind.VoronoiLloyd;
        if (interactive && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) == false)
        {
            LaboratoryPoint world = ScreenToWorld(point);
            if (LaboratoryKind is MathematicalLaboratoryKind.CircleInversion
                or MathematicalLaboratoryKind.VoronoiLloyd)
            {
                _inputPoints.Add(world);
                ScheduleRender();
                return;
            }
            _drawing = true;
            _inputPoints.Clear();
            _inputPoints.Add(world);
            CanvasHost.CaptureMouse();
            return;
        }

        _panning = true;
        _pointerStart = point;
        _worldStart = ScreenToWorld(point);
        CanvasHost.CaptureMouse();
    }

    private void CanvasHost_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (LaboratoryKind is not (MathematicalLaboratoryKind.CircleInversion
            or MathematicalLaboratoryKind.StochasticMotion
            or MathematicalLaboratoryKind.KleinianSchottky)) return;
        LaboratoryPoint point = ScreenToWorld(e.GetPosition(CanvasHost));
        _anchorX = point.X;
        _anchorY = point.Y;
        ScheduleRender();
        e.Handled = true;
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        Point point = e.GetPosition(CanvasHost);
        if (_drawing)
        {
            LaboratoryPoint world = ScreenToWorld(point);
            if (_inputPoints.Count == 0 || Distance(_inputPoints[^1], world) > 0.004 / _zoom)
                _inputPoints.Add(world);
            return;
        }
        if (!_panning) return;
        LaboratoryPoint current = ScreenToWorld(point);
        _viewCenterX += _worldStart.X - current.X;
        _viewCenterY += _worldStart.Y - current.Y;
        _worldStart = ScreenToWorld(point);
        _pointerStart = point;
        UpdatePreviewTransform();
    }

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning && !_drawing) return;
        _panning = _drawing = false;
        CanvasHost.ReleaseMouseCapture();
        ScheduleRender();
    }

    private LaboratoryPoint ScreenToWorld(Point point)
    {
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double height = Math.Max(1, CanvasHost.ActualHeight);
        double scale = Math.Min(width, height) * 0.5;
        double rotatedX = (point.X - width / 2) / scale;
        double rotatedY = -(point.Y - height / 2) / scale;
        double rotation = ReadDouble(RotationBox.Text, out double degrees) ? degrees * Math.PI / 180 : 0;
        double cosine = Math.Cos(rotation), sine = Math.Sin(rotation);
        double dx = rotatedX * cosine + rotatedY * sine;
        double dy = -rotatedX * sine + rotatedY * cosine;
        return new LaboratoryPoint(_viewCenterX + dx / _zoom, _viewCenterY + dy / _zoom);
    }

    private void UpdatePreviewTransform()
    {
        if (!_hasRenderedFrame || _renderedZoom <= 0 || _zoom <= 0 ||
            CanvasHost.ActualWidth <= 0 || CanvasHost.ActualHeight <= 0 ||
            !ReadDouble(RotationBox.Text, out double currentRotation)) return;

        double width = CanvasHost.ActualWidth;
        double height = CanvasHost.ActualHeight;
        double pixelsPerUnit = Math.Min(width, height) * 0.5 * _zoom;
        double rotationRadians = currentRotation * Math.PI / 180;
        double cosine = Math.Cos(rotationRadians);
        double sine = Math.Sin(rotationRadians);
        double centerDx = _renderedCenterX - _viewCenterX;
        double centerDy = _renderedCenterY - _viewCenterY;
        double rotatedCenterX = centerDx * cosine - centerDy * sine;
        double rotatedCenterY = centerDx * sine + centerDy * cosine;
        double scale = _zoom / _renderedZoom;

        _previewScale.ScaleX = scale;
        _previewScale.ScaleY = scale;
        _previewRotation.Angle = _renderedRotation - currentRotation;
        _previewTranslation.X = rotatedCenterX * pixelsPerUnit;
        _previewTranslation.Y = -rotatedCenterY * pixelsPerUnit;

        bool identity = Math.Abs(scale - 1) < 1e-12 &&
                        Math.Abs(_previewRotation.Angle) < 1e-10 &&
                        Math.Abs(_previewTranslation.X) < 0.01 &&
                        Math.Abs(_previewTranslation.Y) < 0.01;
        RenderOptions.SetBitmapScalingMode(CanvasImage,
            identity ? BitmapScalingMode.HighQuality : BitmapScalingMode.LowQuality);
    }

    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e) =>
        FractalControlPanel.Toggle(ref _controlsVisible, ControlsColumn, ControlsHost,
            ToggleControlsButton, 320, () =>
            {
                UpdatePreviewTransform();
                ScheduleRender();
            });

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

    private static double Distance(LaboratoryPoint left, LaboratoryPoint right)
    {
        double dx = left.X - right.X, dy = left.Y - right.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool ReadDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static string Format(double value) => value.ToString("G15", CultureInfo.InvariantCulture);
}
