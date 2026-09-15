using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Microsoft.Win32;
using Point = System.Windows.Point;
using MediaColor = System.Windows.Media.Color;
using FractalExplorerWPF.Infrastructure.Serialization;
using BigFloat = FractalExplorerWPF.Core.NewtonMath.BigFloat;
using FloatExp = FractalExplorerWPF.Core.NewtonMath.FloatExp;

namespace FractalExplorerWPF.Views;

public partial class MandelbrotWindow : Window
{
    private readonly MandelbrotVariantDefinition _definition;
    private readonly MandelbrotPaletteManager _paletteManager = new();
    private readonly MandelbrotSaveStore _saveStore;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private readonly DispatcherTimer _visualizationTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _juliaMapPreviewCts;
    private bool _isRendering;
    private bool _isPanning;
    private bool _isFullscreen;
    private bool _updatingControls;
    private bool _controlsVisible = true;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;
    private Point _lastPanPoint;
    private decimal _centerX;
    private decimal _centerY;
    private FloatExp _zoom;

    // «Второй двигатель»: при _zoom >= DeepZoomThreshold центр ведётся здесь, в
    // произвольной точности, а _centerX/_centerY держатся как ближайшее decimal-приближение
    // для отображения и грубой предпросмотровой трансформации.
    private BigFloat _centerXExact;
    private BigFloat _centerYExact;
    private bool _deepZoomEngaged;

    // Порог включения «второго двигателя» (пертурбация + BigFloat-центр).
    private const double DeepZoomThreshold = 1e25;

    // Верхняя граница зума. История: 5e28 — упор в тип decimal; 1e50 — до перехода на
    // масштабированную δ; 1e90 — пока арифметика центра на UI-потоке шла на фиксированных
    // 384 битах. Сейчас ограничений этого рода нет:
    //  • сам зум — FloatExp, поэтому потолок double (1.8e308) больше не при чём;
    //  • сетка кадра (ширина вида и δc на пиксель) — тоже FloatExp, поэтому шаг пикселя не
    //    схлопывается в ноль и не уходит в денормали;
    //  • точность мантиссы опорной орбиты адаптивна (PlanDeepZoom), а центр на UI-потоке
    //    ведётся с такой же адаптивной точностью (см. CenterPrecisionScope);
    //  • δ на пиксель за ~1e72 переходит в FloatExp, производная Distance Estimation — тоже.
    // 1e1000 — осознанный практический потолок, а не упор типа: на такой глубине опорная
    // орбита считается на ~3456 битах мантиссы, и миллион итераций занимает около 20 секунд.
    private static readonly FloatExp MaxZoom = FloatExp.Pow10(1000);

    // Нижняя граница зума — как была.
    private static readonly FloatExp MinZoom = FloatExp.FromDouble(0.01);

    // Потолок числа итераций. Совпадает с MandelbrotFamilyRenderer.BlaMaxOrbitLength: таблица
    // BLA строится только для орбит не длиннее этого значения, и выход за него лишал бы
    // глубокий зум пропуска итераций (то есть практически применимой скорости).
    private const int MaxIterations = MandelbrotFamilyRenderer.BlaMaxOrbitLength;

    // Потолок зума по вариантам:
    //  • Mandelbrot/Julia, отражённые (Burning Ship, Tricorn, Buffalo, Celtic и их Julia),
    //    Generalized и Simonobrot — все теперь на одном движке (адаптивная точность опорной
    //    орбиты + FloatExp-δ за FloatExpDeltaZoomBits + BLA, комплексная либо вещественная
    //    2×2), поэтому общий потолок MaxZoom. Дробная степень Generalized и дробная/
    //    отрицательная степень Simonobrot вне глубокого движка (BigFloat не умеет pow/имеет
    //    полюс в нуле) остаются на decimal и фактически упираются в его предел (~1e28) —
    //    граничный случай наравне с Histogram/DE на вырожденной орбите, как и раньше. У
    //    Generalized/Simonobrot высокой целой степени (до p=12) шаг опорной орбиты в BigFloat
    //    дороже (O(p) умножений), поэтому на глубоком зуме с большим числом итераций рендер
    //    объективно медленнее, чем у z²+c, — практический компромисс, а не потолок типа.
    private FloatExp EffectiveMaxZoom => _definition.Variant switch
    {
        MandelbrotVariant.Mandelbrot or MandelbrotVariant.Julia
            or MandelbrotVariant.BurningShip or MandelbrotVariant.JuliaBurningShip
            or MandelbrotVariant.Tricorn or MandelbrotVariant.Buffalo or MandelbrotVariant.Celtic
            or MandelbrotVariant.Generalized or MandelbrotVariant.Simonobrot => MaxZoom,
        _ => 5e28,
    };

    private BitmapSource? _stableBitmap;
    // Центр последнего готового кадра — в произвольной точности: на глубоком зуме разность
    // «текущий центр − отрисованный центр» тонет в 28 цифрах decimal, и грубый предпросмотр
    // (UpdateCoarsePreviewTransform) переставал двигаться. BigFloat-вычитание её сохраняет.
    private BigFloat _renderedCenterXExact;
    private BigFloat _renderedCenterYExact;
    private FloatExp _renderedZoom;
    private int _stablePixelWidth;
    private int _stablePixelHeight;
    private RenderSession? _activeSession;

    internal string SaveManagerDisplayName => _definition.DisplayName;
    internal MandelbrotVariant SaveManagerVariant => _definition.Variant;

    public MandelbrotWindow(MandelbrotVariant variant, decimal? juliaReal = null, decimal? juliaImaginary = null)
    {
        _definition = MandelbrotVariantDefinition.For(variant);
        _saveStore = new MandelbrotSaveStore(variant);
        InitializeComponent();
        Title = _definition.DisplayName;
        HeaderText.Text = _definition.DisplayName;
        _renderTimer.Tick += RenderTimer_OnTick;
        _visualizationTimer.Tick += VisualizationTimer_OnTick;
        InitializeControls();
        if (_definition.HasJuliaConstant && juliaReal.HasValue && juliaImaginary.HasValue)
        {
            _updatingControls = true;
            JuliaRealBox.Text = juliaReal.Value.ToString(CultureInfo.InvariantCulture);
            JuliaImaginaryBox.Text = juliaImaginary.Value.ToString(CultureInfo.InvariantCulture);
            _updatingControls = false;
        }
        ResetView(false);
        Loaded += (_, _) =>
        {
            ScheduleRender();
            if (_definition.HasJuliaConstant) _ = RenderJuliaMapPreviewAsync();
        };
    }

    private void InitializeControls()
    {
        _updatingControls = true;
        IterationsBox.Text = "500";
        ThresholdBox.Text = "2";
        PowerBox.Text = _definition.DefaultPower.ToString(CultureInfo.InvariantCulture);
        PowerPanel.Visibility = _definition.HasPower ? Visibility.Visible : Visibility.Collapsed;
        InversionBox.Visibility = _definition.HasInversion ? Visibility.Visible : Visibility.Collapsed;
        JuliaConstantPanel.Visibility = _definition.HasJuliaConstant ? Visibility.Visible : Visibility.Collapsed;
        JuliaRealBox.Text = _definition.DefaultJuliaReal.ToString(CultureInfo.InvariantCulture);
        JuliaImaginaryBox.Text = _definition.DefaultJuliaImaginary.ToString(CultureInfo.InvariantCulture);

        for (int count = 1; count <= Environment.ProcessorCount; count++) ThreadsBox.Items.Add(count);
        ThreadsBox.Items.Add("Auto");
        ThreadsBox.SelectedItem = "Auto";
        ColoringModeBox.SelectedIndex = 1;
        SmoothBlendPowerBox.Text = "1";
        SmoothIterationOffsetBox.Text = "0";
        HistogramContrastBox.Text = "1";
        HistogramEqualizationBox.IsChecked = true;
        HistogramSmoothInputBox.IsChecked = true;
        OrbitStrengthBox.Text = "1";
        OrbitBiasBox.Text = "0";
        StripeFrequencyBox.Text = "3";
        StripeStrengthBox.Text = "0.5";
        StripeBiasBox.Text = "0";
        PolyABox.Text = "9";
        PolyBBox.Text = "15";
        PolyCBox.Text = "8.5";
        PolyGammaBox.Text = "1";
        PolyBlendBox.Text = "1";
        PolyBiasBox.Text = "0";
        DistanceReliefBox.Text = "1.35";
        DistanceLightAzimuthBox.Text = "135";
        DistanceLightElevationBox.Text = "45";
        DistanceAmbientBox.Text = "0.28";
        DistanceDiffuseBox.Text = "0.9";
        DistanceSpecularBox.Text = "0.3";
        DistanceShininessBox.Text = "32";
        DistanceContoursBox.IsChecked = true;
        DistanceContourSpacingBox.Text = "12";
        DistanceContourStrengthBox.Text = "0.45";
        PalettePhaseBox.Text = "0";
        PaletteScaleBox.Text = "1";
        PaletteWrapBox.SelectedIndex = 0;
        CustomInteriorBox.IsChecked = false;
        InteriorColorSelector.SelectedColor = Colors.Black;
        _updatingControls = false;
    }

    public MandelbrotState CaptureState(string saveName)
    {
        // Потолок совпадает с BlaMaxOrbitLength: за ним таблица BLA не строится и глубокий
        // зум проваливается в рендер без пропуска итераций, то есть становится неприменимо
        // медленным. На 1e1000 счёт идёт о сотнях тысяч итераций, так что запас есть.
        int iterations = ReadInt(IterationsBox.Text, "итерации", 50, MaxIterations);
        decimal threshold = ReadDecimal(ThresholdBox.Text, "порог выхода", 0.1m, 1_000m);
        decimal power = _definition.HasPower
            ? ReadDecimal(PowerBox.Text, "степень", _definition.Variant == MandelbrotVariant.Simonobrot ? -12m : 0.1m, 12m)
            : 2m;
        if (_definition.Variant == MandelbrotVariant.Simonobrot && Math.Abs(power) < 0.1m)
            throw new InvalidOperationException("Для Симоноброта модуль степени должен быть не меньше 0.1.");
        MandelbrotPalette palette = _paletteManager.ActivePalette.Clone(_paletteManager.ActivePalette.Name);

        return new MandelbrotState
        {
            SaveName = saveName,
            Timestamp = DateTime.Now,
            Variant = _definition.Variant,
            CenterX = _deepZoomEngaged ? _centerXExact.ToDecimalClamped() : _centerX,
            CenterY = _deepZoomEngaged ? _centerYExact.ToDecimalClamped() : _centerY,
            CenterXExact = _deepZoomEngaged ? _centerXExact.ToInvariantString() : null,
            CenterYExact = _deepZoomEngaged ? _centerYExact.ToInvariantString() : null,
            Zoom = _zoom,
            Iterations = iterations,
            Threshold = threshold,
            Threads = GetThreadCount(),
            ColoringMode = SelectedColoringMode,
            PaletteName = palette.Name,
            Palette = palette,
            Power = power,
            UseInversion = InversionBox.IsChecked == true,
            JuliaCReal = _definition.HasJuliaConstant
                ? ReadDecimal(JuliaRealBox.Text, "действительная часть C", -10m, 10m)
                : 0m,
            JuliaCImaginary = _definition.HasJuliaConstant
                ? ReadDecimal(JuliaImaginaryBox.Text, "мнимая часть C", -10m, 10m)
                : 0m,
            HistogramContrast = ReadDouble(HistogramContrastBox.Text, "контраст", 0.1, 4),
            HistogramEnabledEqualization = HistogramEqualizationBox.IsChecked == true,
            HistogramInputUseSmooth = HistogramSmoothInputBox.IsChecked == true,
            SmoothBlendPower = ReadDouble(SmoothBlendPowerBox.Text, "степень смешивания", 0.1, 5),
            SmoothIterationOffset = ReadDouble(SmoothIterationOffsetBox.Text, "сдвиг итерации", -100, 100),
            PalettePhaseOffset = ReadDouble(PalettePhaseBox.Text, "фаза палитры", -2, 2),
            PaletteScale = ReadDouble(PaletteScaleBox.Text, "масштаб палитры", -5, 5),
            PaletteWrapMode = PaletteWrapBox.SelectedIndex < 0
                ? MandelbrotPaletteWrapMode.Repeat
                : (MandelbrotPaletteWrapMode)PaletteWrapBox.SelectedIndex,
            UseCustomInteriorColor = CustomInteriorBox.IsChecked == true,
            InteriorColor = CustomInteriorBox.IsChecked == true
                ? InteriorColorSelector.SelectedColor
                : palette.InteriorColor,
            OrbitTrapStrength = ReadDouble(OrbitStrengthBox.Text, "сила ловушки", 0, 5),
            OrbitTrapBias = ReadDouble(OrbitBiasBox.Text, "смещение ловушки", -1, 1),
            StripeFrequency = ReadDouble(StripeFrequencyBox.Text, "частота полос", 0.1, 20),
            StripeStrength = ReadDouble(StripeStrengthBox.Text, "сила полос", 0, 1),
            StripeBias = ReadDouble(StripeBiasBox.Text, "смещение полос", -1, 1),
            PolynomialA = ReadDouble(PolyABox.Text, "коэффициент A", 0, 30),
            PolynomialB = ReadDouble(PolyBBox.Text, "коэффициент B", 0, 30),
            PolynomialC = ReadDouble(PolyCBox.Text, "коэффициент C", 0, 30),
            PolynomialGamma = ReadDouble(PolyGammaBox.Text, "гамма полинома", 0.1, 5),
            PolynomialBlend = ReadDouble(PolyBlendBox.Text, "смешивание полинома", 0, 1),
            PolynomialBias = ReadDouble(PolyBiasBox.Text, "смещение полинома", -1, 1),
            DistanceReliefStrength = ReadDouble(DistanceReliefBox.Text, "сила рельефа", 0, 20),
            DistanceLightAzimuth = ReadDouble(DistanceLightAzimuthBox.Text, "азимут света", -360, 360),
            DistanceLightElevation = ReadDouble(DistanceLightElevationBox.Text, "высота света", 0, 90),
            DistanceAmbient = ReadDouble(DistanceAmbientBox.Text, "ambient", 0, 1),
            DistanceDiffuse = ReadDouble(DistanceDiffuseBox.Text, "diffuse", 0, 2),
            DistanceSpecular = ReadDouble(DistanceSpecularBox.Text, "specular", 0, 2),
            DistanceShininess = ReadDouble(DistanceShininessBox.Text, "блеск", 1, 256),
            DistanceContoursEnabled = DistanceContoursBox.IsChecked == true,
            DistanceContourSpacing = ReadDouble(DistanceContourSpacingBox.Text, "шаг контуров", 2, 100),
            DistanceContourStrength = ReadDouble(DistanceContourStrengthBox.Text, "сила контуров", 0, 1)
        };
    }

    public void LoadState(MandelbrotState state)
    {
        if (state.Variant != _definition.Variant) return;
        _renderCts?.Cancel();
        _updatingControls = true;
        _centerX = state.CenterX;
        _centerY = state.CenterY;
        _zoom = FloatExp.Clamp(state.Zoom, MinZoom, EffectiveMaxZoom);
        _deepZoomEngaged = false;
        if (state.CenterXExact is { Length: > 0 } exactX && state.CenterYExact is { Length: > 0 } exactY)
        {
            try
            {
                using var precision = CenterPrecisionScope();
                _centerXExact = BigFloat.Parse(exactX);
                _centerYExact = BigFloat.Parse(exactY);
                _deepZoomEngaged = _zoom >= DeepZoomThreshold;
                if (_deepZoomEngaged)
                {
                    _centerX = _centerXExact.ToDecimalClamped();
                    _centerY = _centerYExact.ToDecimalClamped();
                }
            }
            catch (FormatException)
            {
                _deepZoomEngaged = false;
            }
        }
        // Старое глубокое сохранение без точных координат: заводим «второй двигатель»
        // из decimal-центра, если один только зум уже перешёл порог.
        if (!_deepZoomEngaged) SyncDeepZoomState();
        IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
        ThresholdBox.Text = state.Threshold.ToString(CultureInfo.InvariantCulture);
        ZoomBox.Text = FloatExpJsonConverter.ToDisplay(_zoom);
        PowerBox.Text = state.Power.ToString(CultureInfo.InvariantCulture);
        InversionBox.IsChecked = state.UseInversion;
        JuliaRealBox.Text = state.JuliaCReal.ToString(CultureInfo.InvariantCulture);
        JuliaImaginaryBox.Text = state.JuliaCImaginary.ToString(CultureInfo.InvariantCulture);
        ColoringModeBox.SelectedIndex = (int)state.ColoringMode;
        HistogramContrastBox.Text = state.HistogramContrast.ToString(CultureInfo.InvariantCulture);
        HistogramEqualizationBox.IsChecked = state.HistogramEnabledEqualization;
        HistogramSmoothInputBox.IsChecked = state.HistogramInputUseSmooth;
        SmoothBlendPowerBox.Text = state.SmoothBlendPower.ToString(CultureInfo.InvariantCulture);
        SmoothIterationOffsetBox.Text = state.SmoothIterationOffset.ToString(CultureInfo.InvariantCulture);
        PalettePhaseBox.Text = state.PalettePhaseOffset.ToString(CultureInfo.InvariantCulture);
        PaletteScaleBox.Text = state.PaletteScale.ToString(CultureInfo.InvariantCulture);
        PaletteWrapBox.SelectedIndex = (int)state.PaletteWrapMode;
        CustomInteriorBox.IsChecked = state.UseCustomInteriorColor;
        InteriorColorSelector.SelectedColor = state.InteriorColor;
        OrbitStrengthBox.Text = state.OrbitTrapStrength.ToString(CultureInfo.InvariantCulture);
        OrbitBiasBox.Text = state.OrbitTrapBias.ToString(CultureInfo.InvariantCulture);
        StripeFrequencyBox.Text = state.StripeFrequency.ToString(CultureInfo.InvariantCulture);
        StripeStrengthBox.Text = state.StripeStrength.ToString(CultureInfo.InvariantCulture);
        StripeBiasBox.Text = state.StripeBias.ToString(CultureInfo.InvariantCulture);
        PolyABox.Text = state.PolynomialA.ToString(CultureInfo.InvariantCulture);
        PolyBBox.Text = state.PolynomialB.ToString(CultureInfo.InvariantCulture);
        PolyCBox.Text = state.PolynomialC.ToString(CultureInfo.InvariantCulture);
        PolyGammaBox.Text = state.PolynomialGamma.ToString(CultureInfo.InvariantCulture);
        PolyBlendBox.Text = state.PolynomialBlend.ToString(CultureInfo.InvariantCulture);
        PolyBiasBox.Text = state.PolynomialBias.ToString(CultureInfo.InvariantCulture);
        DistanceReliefBox.Text = state.DistanceReliefStrength.ToString(CultureInfo.InvariantCulture);
        DistanceLightAzimuthBox.Text = state.DistanceLightAzimuth.ToString(CultureInfo.InvariantCulture);
        DistanceLightElevationBox.Text = state.DistanceLightElevation.ToString(CultureInfo.InvariantCulture);
        DistanceAmbientBox.Text = state.DistanceAmbient.ToString(CultureInfo.InvariantCulture);
        DistanceDiffuseBox.Text = state.DistanceDiffuse.ToString(CultureInfo.InvariantCulture);
        DistanceSpecularBox.Text = state.DistanceSpecular.ToString(CultureInfo.InvariantCulture);
        DistanceShininessBox.Text = state.DistanceShininess.ToString(CultureInfo.InvariantCulture);
        DistanceContoursBox.IsChecked = state.DistanceContoursEnabled;
        DistanceContourSpacingBox.Text = state.DistanceContourSpacing.ToString(CultureInfo.InvariantCulture);
        DistanceContourStrengthBox.Text = state.DistanceContourStrength.ToString(CultureInfo.InvariantCulture);
        MandelbrotPalette loadedPalette = state.Palette;
        if (!string.IsNullOrWhiteSpace(state.PaletteName) &&
            (loadedPalette.Colors.Count == 0 || loadedPalette.Name == "Новая палитра"))
        {
            loadedPalette = _paletteManager.Palettes.FirstOrDefault(palette =>
                palette.Name.Equals(state.PaletteName, StringComparison.OrdinalIgnoreCase)) ?? loadedPalette;
        }
        _paletteManager.ActivePalette = loadedPalette.Clone(
            string.IsNullOrWhiteSpace(state.PaletteName) ? loadedPalette.Name : state.PaletteName);
        _updatingControls = false;
        UpdateJuliaMapMarker();
        ScheduleRender();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StablePreviewImage, CanvasImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        MandelbrotState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        RenderBitmapAsync(CloneState(state), width, height, 1, token, progress);

    private MandelbrotColoringMode SelectedColoringMode =>
        ColoringModeBox.SelectedIndex < 0 ? MandelbrotColoringMode.Smooth : (MandelbrotColoringMode)ColoringModeBox.SelectedIndex;

    private void Parameter_OnChanged(object sender, EventArgs e)
    {
        if (!_updatingControls && IsLoaded)
        {
            UpdateJuliaMapMarker();
            ScheduleRender();
        }
    }

    private void ColoringMode_OnChanged(object sender, EventArgs e)
    {
        if (IsInitialized)
        {
            DistanceEstimationExpander.Visibility = SelectedColoringMode == MandelbrotColoringMode.DistanceEstimation
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        Parameter_OnChanged(sender, e);
    }

    // Ввод зума принимает и обычное число, и научную нотацию любой глубины («1e750»):
    // колесом мыши до 1e1000 пришлось бы щёлкнуть несколько тысяч раз, поэтому прямой ввод
    // показателя — основной способ попасть на большую глубину.
    private void ZoomBox_OnTextChanged(object sender, EventArgs e)
    {
        if (!_updatingControls && FloatExp.TryParse(ZoomBox.Text, out FloatExp zoom) && zoom.Sign > 0)
        {
            _zoom = FloatExp.Clamp(zoom, MinZoom, EffectiveMaxZoom);
            SyncDeepZoomState();
            ScheduleRender();
        }
    }

    // ------------------------------------------------------------ поиск ядра (Newton)

    private CancellationTokenSource? _nucleusCts;

    /// <summary>
    /// Нижняя граница предлагаемого зума относительно текущего. Оценка размера ядра надёжна,
    /// но если она всё-таки промахнётся на много порядков вниз, пользователь не должен терять
    /// вид целиком — возврат больше чем на два порядка назад не применяется.
    /// </summary>
    private const double NucleusZoomOutLimit = 100.0;

    private async void NucleusButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await FindNucleusAsync();
        }
        catch (OperationCanceledException)
        {
            // Отмена — штатный путь (повторное нажатие или закрытие окна).
        }
        catch (Exception exception)
        {
            SetNucleusStatus($"Поиск ядра не удался: {exception.Message}");
        }
    }

    private async Task FindNucleusAsync()
    {
        MandelbrotState state;
        try
        {
            state = CaptureState(string.Empty);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!MandelbrotNewtonZoom.IsSupported(state.Variant, state.Power))
        {
            SetNucleusStatus(MandelbrotNewtonZoom.UnsupportedReason(state.Variant));
            return;
        }

        _nucleusCts?.Cancel();
        using var cts = new CancellationTokenSource();
        _nucleusCts = cts;
        NucleusButton.IsEnabled = false;
        SetNucleusStatus("Поиск ядра: определяется период…");

        try
        {
            var progress = new Progress<int>(percent =>
                SetNucleusStatus($"Поиск ядра: {percent}%"));
            MandelbrotNucleusResult result = await Task.Run(
                () => MandelbrotNewtonZoom.FindNucleus(state, cts.Token, value => ((IProgress<int>)progress).Report(value)),
                cts.Token);

            if (!result.Found)
            {
                SetNucleusStatus(result.Message);
                return;
            }

            ApplyNucleus(result);
        }
        finally
        {
            // Кнопку возвращает только актуальный поиск: иначе завершение отменённого
            // предыдущего разблокировало бы её посреди нового.
            if (ReferenceEquals(_nucleusCts, cts))
            {
                _nucleusCts = null;
                NucleusButton.IsEnabled = true;
            }
        }
    }

    /// <summary>
    /// Переносит вид на найденное ядро. Зум выставляется первым: от него зависит разрядность
    /// мантиссы, с которой разбирается точный центр (см. <see cref="CenterPrecisionScope"/>),
    /// и разбор на старой точности потерял бы как раз те цифры, ради которых всё и считалось.
    /// </summary>
    private void ApplyNucleus(MandelbrotNucleusResult result)
    {
        FloatExp minimum = FloatExp.Max(MinZoom, _zoom / NucleusZoomOutLimit);
        FloatExp target = FloatExp.Clamp(result.SuggestedZoom, minimum, EffectiveMaxZoom);
        _zoom = target;

        using (CenterPrecisionScope())
        {
            BigFloat nucleusX = BigFloat.Parse(result.CenterX);
            BigFloat nucleusY = BigFloat.Parse(result.CenterY);
            if (_zoom >= DeepZoomThreshold)
            {
                _centerXExact = nucleusX;
                _centerYExact = nucleusY;
                _centerX = nucleusX.ToDecimalClamped();
                _centerY = nucleusY.ToDecimalClamped();
                _deepZoomEngaged = true;
            }
            else
            {
                _centerX = nucleusX.ToDecimalClamped();
                _centerY = nucleusY.ToDecimalClamped();
                _deepZoomEngaged = false;
            }
        }

        SetZoomText();
        UpdateJuliaMapMarker();
        UpdateCoarsePreviewTransform();
        ScheduleRender();

        string zoomText = FloatExpJsonConverter.ToDisplay(target);
        string suggestedText = FloatExpJsonConverter.ToDisplay(result.SuggestedZoom);
        SetNucleusStatus(target == result.SuggestedZoom
            ? $"{result.Message} Зум: {zoomText}."
            : $"{result.Message} Зум: {zoomText} (оценка по размеру ядра — {suggestedText}, " +
              "ограничена потолком варианта или защитой от возврата назад).");
    }

    private void SetNucleusStatus(string text)
    {
        NucleusStatusText.Text = text;
        NucleusStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new MandelbrotPaletteWindow(_paletteManager) { Owner = this };
        dialog.PaletteApplied += (_, _) => ScheduleRender();
        dialog.ShowDialog();
    }

    private void JuliaConstantButton_OnClick(object sender, RoutedEventArgs e)
    {
        decimal real;
        decimal imaginary;
        try
        {
            real = ReadDecimal(JuliaRealBox.Text, "действительная часть C", -10m, 10m);
            imaginary = ReadDecimal(JuliaImaginaryBox.Text, "мнимая часть C", -10m, 10m);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MandelbrotVariant sourceVariant = _definition.Variant == MandelbrotVariant.JuliaBurningShip
            ? MandelbrotVariant.BurningShip
            : MandelbrotVariant.Mandelbrot;
        var dialog = new JuliaConstantPickerWindow(sourceVariant, real, imaginary) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _updatingControls = true;
        JuliaRealBox.Text = dialog.SelectedReal.ToString(CultureInfo.InvariantCulture);
        JuliaImaginaryBox.Text = dialog.SelectedImaginary.ToString(CultureInfo.InvariantCulture);
        _updatingControls = false;
        UpdateJuliaMapMarker();
        ScheduleRender();
    }

    private void JuliaMapPreviewHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        JuliaConstantButton_OnClick(sender, e);
        e.Handled = true;
    }

    private void JuliaMapPreviewHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateJuliaMapMarker();
        if (IsLoaded && _definition.HasJuliaConstant) _ = RenderJuliaMapPreviewAsync();
    }

    private async Task RenderJuliaMapPreviewAsync()
    {
        if (!_definition.HasJuliaConstant || JuliaMapPreviewHost.ActualWidth < 1 ||
            JuliaMapPreviewHost.ActualHeight < 1) return;

        _juliaMapPreviewCts?.Cancel();
        var cts = new CancellationTokenSource();
        _juliaMapPreviewCts = cts;
        RenderSurfaceMetrics mapSurface = RenderSurfaceMetrics.Measure(JuliaMapPreviewHost);
        int width = mapSurface.PixelWidth;
        int height = mapSurface.PixelHeight;
        (MandelbrotVariant variant, decimal centerX, decimal centerY, decimal zoom) = GetJuliaMapView();
        var state = new MandelbrotState
        {
            Variant = variant,
            CenterX = centerX,
            CenterY = centerY,
            Zoom = (double)zoom,
            Iterations = 110,
            Threshold = 2,
            Threads = 0,
            ColoringMode = MandelbrotColoringMode.Smooth,
            Palette = new MandelbrotPalette
            {
                Name = "Карта выбора C",
                Colors =
                [
                    Colors.Black,
                    MediaColor.FromRgb(200, 50, 30),
                    Colors.White
                ],
                InteriorColor = Colors.Black,
                IsGradient = true,
                ColorPeriod = 110,
                AlignWithRenderIterations = true
            }
        };

        try
        {
            BitmapSource bitmap = await RenderBitmapAsync(state, width, height, 1, cts.Token, null,
                mapSurface.Dpi.PixelsPerInchX, mapSurface.Dpi.PixelsPerInchY);
            if (!ReferenceEquals(_juliaMapPreviewCts, cts)) return;
            JuliaMapPreviewImage.Source = bitmap;
            UpdateJuliaMapMarker();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_juliaMapPreviewCts, cts))
                CrashLogger.Log("MandelbrotWindow.RenderJuliaMapPreviewAsync", ex);
        }
        finally
        {
            if (ReferenceEquals(_juliaMapPreviewCts, cts)) _juliaMapPreviewCts = null;
            cts.Dispose();
        }
    }

    private (MandelbrotVariant Variant, decimal CenterX, decimal CenterY, decimal Zoom) GetJuliaMapView() =>
        _definition.Variant == MandelbrotVariant.JuliaBurningShip
            ? (MandelbrotVariant.BurningShip, -0.25m, 0.25m, 3m / 3.5m)
            : (MandelbrotVariant.Mandelbrot, -0.5m, 0m, 1m);

    private void UpdateJuliaMapMarker()
    {
        if (!_definition.HasJuliaConstant || !IsInitialized ||
            !TryReadDecimal(JuliaRealBox.Text, out decimal real) ||
            !TryReadDecimal(JuliaImaginaryBox.Text, out decimal imaginary)) return;

        double width = Math.Max(1, JuliaMapPreviewHost.ActualWidth);
        double height = Math.Max(1, JuliaMapPreviewHost.ActualHeight);
        (_, decimal centerX, decimal centerY, decimal zoom) = GetJuliaMapView();
        decimal viewWidth = 3m / zoom;
        decimal viewHeight = viewWidth * (decimal)height / (decimal)width;
        double x = (double)((real - (centerX - viewWidth / 2m)) / viewWidth) * width;
        double y = (double)(((centerY + viewHeight / 2m) - imaginary) / viewHeight) * height;
        bool visible = x >= 0 && x <= width && y >= 0 && y <= height;
        JuliaMapMarkerLayer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        JuliaMapHorizontalMarker.X1 = 0;
        JuliaMapHorizontalMarker.X2 = width;
        JuliaMapHorizontalMarker.Y1 = y;
        JuliaMapHorizontalMarker.Y2 = y;
        JuliaMapVerticalMarker.X1 = x;
        JuliaMapVerticalMarker.X2 = x;
        JuliaMapVerticalMarker.Y1 = 0;
        JuliaMapVerticalMarker.Y2 = height;
    }

    private void SavesButton_OnClick(object sender, RoutedEventArgs e) =>
        SaveManagerWindow.Open(this, SaveManagerConfigurations.ForMandelbrot(this, _saveStore));

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        (int canvasPixelWidth, int canvasPixelHeight, _, _) = GetCanvasPixelSize();
        if (_activeSession is not null) CommitAndBakePreview();
        else _renderCts?.Cancel();
        MandelbrotState state;
        try { state = CaptureState("export"); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Параметры экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ImageExportManagerWindow.Open(this, new ImageExportConfiguration
        {
            FileNamePrefix = _definition.Identifier,
            InitialWidth = canvasPixelWidth,
            InitialHeight = canvasPixelHeight,
            MaxSsaaFactor = 10,
            RenderAsync = (request, token, progress) => RenderBitmapAsync(state, request.Width,
                request.Height, request.SsaaFactor, token, progress)
        });
    }

    private void RenderButton_OnClick(object sender, RoutedEventArgs e) => _ = RenderPreviewAsync();
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => _renderCts?.Cancel();
    private void ResetButton_OnClick(object sender, RoutedEventArgs e) => ResetView(true);

    private void ToggleControlsButton_OnClick(object sender, RoutedEventArgs e)
    {
        FractalControlPanel.Toggle(ref _controlsVisible, ParametersColumn, ParametersBorder,
            ToggleControlsButton, 278);
        UpdateCoarsePreviewTransform();
        ScheduleRender();
    }

    private void ResetView(bool render)
    {
        _centerX = _definition.InitialCenterX;
        _centerY = _definition.InitialCenterY;
        _zoom = _definition.InitialZoom;
        _deepZoomEngaged = false;
        _updatingControls = true;
        ZoomBox.Text = FloatExpJsonConverter.ToDisplay(_zoom);
        _updatingControls = false;
        if (render) ScheduleRender();
    }

    private void ScheduleRender()
    {
        if (!IsLoaded) return;
        if (_isRendering) CommitAndBakePreview();
        else UpdateCoarsePreviewTransform();
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
        MandelbrotState state;
        try { state = CaptureState("preview"); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }

        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        int logicalWidth = Math.Max(1, (int)Math.Round(surface.LogicalWidth));
        int logicalHeight = Math.Max(1, (int)Math.Round(surface.LogicalHeight));
        int pixelWidth = surface.PixelWidth;
        int pixelHeight = surface.PixelHeight;
        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        CancellationToken token = cts.Token;
        var stopwatch = Stopwatch.StartNew();
        TileSchedulingStrategy strategy = RenderPatternSettings.SelectedPattern;
        SetRenderingState(true, $"Рендеринг: {GetStrategyDisplayName(strategy)}...");
        try
        {
            int factor = SelectedPreviewSsaaFactor;
            int renderWidth = checked(pixelWidth * factor);
            int renderHeight = checked(pixelHeight * factor);
            IReadOnlyList<MandelbrotRenderTile> tiles = MandelbrotTileScheduler.Create(
                renderWidth, renderHeight, 16 * factor, strategy);
            WriteableBitmap bitmap = ProgressiveRenderBitmap.CreateOverlay(renderWidth, renderHeight,
                surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY);
            var session = new RenderSession(bitmap, tiles.Count, renderWidth, renderHeight, cts);
            _activeSession = session;
            CanvasImage.Source = bitmap;
            RenderOverlay.BeginSession(renderWidth, renderHeight);
            _visualizationTimer.Start();

            await RenderTilesAsync(state, tiles, session, token);
            if (token.IsCancellationRequested)
            {
                if (_activeSession is not null) CommitAndBakePreview();
                if (ReferenceEquals(_renderCts, cts)) StatusText.Text = "Рендер отменён";
                return;
            }
            FlushVisualizationEvents(session, true);
            BitmapSource completed = session.Bitmap.Clone();
            completed.Freeze();
            BigFloat renderedCenterX, renderedCenterY;
            using (CenterPrecisionScope(state.Zoom))
            {
                renderedCenterX = state.CenterXExact is { Length: > 0 } exactX
                    ? BigFloat.Parse(exactX)
                    : BigFloat.FromDecimal(state.CenterX);
                renderedCenterY = state.CenterYExact is { Length: > 0 } exactY
                    ? BigFloat.Parse(exactY)
                    : BigFloat.FromDecimal(state.CenterY);
            }
            SetStableBitmap(completed, renderedCenterX, renderedCenterY, state.Zoom, logicalWidth, logicalHeight);
            CanvasImage.Source = null;
            RenderOverlay.EndSession();
            if (ReferenceEquals(_activeSession, session)) _activeSession = null;
            stopwatch.Stop();
            StatusText.Text = $"Готово за {stopwatch.Elapsed.TotalSeconds:F3} сек. " +
                              $"Стратегия: {GetStrategyDisplayName(strategy)}. Центр: {_centerX:G6}; {_centerY:G6}";
        }
        catch (OperationCanceledException)
        {
            if (_activeSession is not null) CommitAndBakePreview();
            if (ReferenceEquals(_renderCts, cts)) StatusText.Text = "Рендер отменён";
        }
        catch (Exception ex)
        {
            if (_activeSession is not null) CommitAndBakePreview();
            if (ReferenceEquals(_renderCts, cts)) StatusText.Text = "Ошибка рендера";
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _visualizationTimer.Stop();
            RenderOverlay.EndSession();
            if (ReferenceEquals(_renderCts, cts))
            {
                _renderCts = null;
                SetRenderingState(false);
            }
            cts.Dispose();
        }
    }

    private async Task RenderTilesAsync(
        MandelbrotState state,
        IReadOnlyList<MandelbrotRenderTile> tiles,
        RenderSession session,
        CancellationToken token)
    {
        var queue = new ConcurrentQueue<MandelbrotRenderTile>(tiles);
        int workerCount = state.Threads <= 0 ? Environment.ProcessorCount : state.Threads;
        workerCount = Math.Clamp(workerCount, 1, Environment.ProcessorCount);
        Task[] workers = Enumerable.Range(0, workerCount).Select(_ => Task.Run(() =>
        {
            while (queue.TryDequeue(out MandelbrotRenderTile tile))
            {
                if (token.IsCancellationRequested) return;
                session.Events.Enqueue(new TileRenderEvent(true, tile, null));
                byte[]? pixels = MandelbrotFamilyRenderer.RenderTile(
                    state, session.RenderWidth, session.RenderHeight, tile, token);
                if (pixels is null || token.IsCancellationRequested) return;
                session.Events.Enqueue(new TileRenderEvent(false, tile, pixels));
            }
        })).ToArray();
        await Task.WhenAll(workers);
    }

    private void VisualizationTimer_OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (_activeSession is not null) FlushVisualizationEvents(_activeSession, false);
        }
        catch (Exception ex)
        {
            _visualizationTimer.Stop();
            StatusText.Text = "Ошибка визуализации прогресса рендера.";
            CrashLogger.Log("MandelbrotWindow.VisualizationTimer_OnTick", ex);
        }
    }

    private void FlushVisualizationEvents(RenderSession session, bool drainAll)
    {
        int processed = 0;
        bool changed = false;
        while ((drainAll || processed < 512) && session.Events.TryDequeue(out TileRenderEvent visualEvent))
        {
            if (visualEvent.IsStart)
            {
                RenderOverlay.StartTile(visualEvent.Tile);
            }
            else if (visualEvent.Pixels is not null)
            {
                MandelbrotRenderTile tile = visualEvent.Tile;
                if (ProgressiveRenderBitmap.WriteTile(session.Bitmap, tile, visualEvent.Pixels))
                {
                    RenderOverlay.CompleteTile(tile);
                    session.CompletedTiles++;
                }
            }
            processed++;
            changed = true;
        }
        if (changed)
        {
            RenderOverlay.Refresh();
            RenderProgress.Value = session.TileCount == 0
                ? 0
                : Math.Min(100, session.CompletedTiles * 100.0 / session.TileCount);
        }
    }

    private void SetStableBitmap(
        BitmapSource bitmap,
        BigFloat centerXExact,
        BigFloat centerYExact,
        FloatExp zoom,
        int pixelWidth,
        int pixelHeight)
    {
        _stableBitmap = bitmap;
        _renderedCenterXExact = centerXExact;
        _renderedCenterYExact = centerYExact;
        _renderedZoom = zoom;
        _stablePixelWidth = Math.Max(1, pixelWidth);
        _stablePixelHeight = Math.Max(1, pixelHeight);
        StablePreviewImage.Source = bitmap;
        StablePreviewImage.RenderTransform = Transform.Identity;
        RenderOptions.SetBitmapScalingMode(StablePreviewImage, BitmapScalingMode.HighQuality);
    }

    private void CommitAndBakePreview()
    {
        RenderSession? session = _activeSession;
        if (session is null) return;

        session.Cancellation.Cancel();
        FlushVisualizationEvents(session, true);
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(ImageLayer);
        int logicalWidth = Math.Max(1, (int)Math.Round(surface.LogicalWidth));
        int logicalHeight = Math.Max(1, (int)Math.Round(surface.LogicalHeight));
        DpiScale dpi = surface.Dpi;
        int pixelWidth = surface.PixelWidth;
        int pixelHeight = surface.PixelHeight;
        try
        {
            var baked = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi.PixelsPerInchX,
                dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            baked.Render(ImageLayer);
            baked.Freeze();
            SetStableBitmap(baked,
                _deepZoomEngaged ? _centerXExact : BigFloat.FromDecimal(_centerX),
                _deepZoomEngaged ? _centerYExact : BigFloat.FromDecimal(_centerY),
                _zoom, logicalWidth, logicalHeight);
        }
        catch (InvalidOperationException)
        {
            // Layout can briefly be unavailable during minimization or a resize transition.
        }
        CanvasImage.Source = null;
        RenderOverlay.EndSession();
        if (ReferenceEquals(_activeSession, session)) _activeSession = null;
    }

    private void UpdateCoarsePreviewTransform()
    {
        if (_stableBitmap is null || _renderedZoom.Sign <= 0 || _zoom.Sign <= 0 ||
            _stablePixelWidth <= 0 || _stablePixelHeight <= 0) return;

        // Всё считается в double: на зуме > ~1e25 шаг вида (3/zoom) уходит ниже нижней
        // границы decimal (1e-28) и прежняя decimal-версия просто выходила по guard'у, не
        // трогая трансформацию. double держит масштаб до ~1e300, а разность центров берётся
        // из BigFloat — единственного места, где она не съедается 28 цифрами decimal.
        double width = Math.Max(1, ImageLayer.ActualWidth);
        double height = Math.Max(1, ImageLayer.ActualHeight);
        FloatExp renderedViewWidth = 3.0 / _renderedZoom;
        FloatExp currentViewWidth = 3.0 / _zoom;
        if (!renderedViewWidth.IsFinite || !currentViewWidth.IsFinite ||
            renderedViewWidth.Sign <= 0 || currentViewWidth.Sign <= 0) return;

        double stableAspect = (double)_stablePixelHeight / _stablePixelWidth;
        FloatExp renderedViewHeight = renderedViewWidth * stableAspect;
        FloatExp currentViewHeight = currentViewWidth * height / width;

        double scaleX = (renderedViewWidth / currentViewWidth).ToDouble();
        double scaleY = (renderedViewHeight / currentViewHeight).ToDouble();

        using var precision = CenterPrecisionScope();
        BigFloat currentCenterX = _deepZoomEngaged ? _centerXExact : BigFloat.FromDecimal(_centerX);
        BigFloat currentCenterY = _deepZoomEngaged ? _centerYExact : BigFloat.FromDecimal(_centerY);
        FloatExp centerDeltaX = FloatExp.FromBigFloat(_renderedCenterXExact - currentCenterX);
        FloatExp centerDeltaY = FloatExp.FromBigFloat(currentCenterY - _renderedCenterYExact);

        double offsetX = (centerDeltaX * width / currentViewWidth).ToDouble() + width / 2.0 * (1 - scaleX);
        double offsetY = (centerDeltaY * height / currentViewHeight).ToDouble() + height / 2.0 * (1 - scaleY);
        if (!double.IsFinite(scaleX) || !double.IsFinite(scaleY) ||
            !double.IsFinite(offsetX) || !double.IsFinite(offsetY)) return;

        StablePreviewImage.RenderTransform = new MatrixTransform(new Matrix(
            scaleX, 0,
            0, scaleY,
            offsetX, offsetY));
        RenderOptions.SetBitmapScalingMode(StablePreviewImage, BitmapScalingMode.LowQuality);
    }

    private static string GetStrategyDisplayName(TileSchedulingStrategy strategy) => strategy switch
    {
        TileSchedulingStrategy.Classic => "от центра",
        TileSchedulingStrategy.Linear => "построчно",
        TileSchedulingStrategy.Spiral => "спираль",
        TileSchedulingStrategy.Randomized => "случайно",
        TileSchedulingStrategy.Checkerboard => "шахматный",
        TileSchedulingStrategy.Diagonal => "по диагонали",
        TileSchedulingStrategy.EdgesInward => "от краёв",
        TileSchedulingStrategy.MortonCurve => "Z-кривая Мортона",
        _ => strategy.ToString()
    };

    private (int PixelWidth, int PixelHeight, double DpiScaleX, double DpiScaleY) GetCanvasPixelSize()
    {
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(CanvasHost);
        return (surface.PixelWidth, surface.PixelHeight, surface.Dpi.DpiScaleX, surface.Dpi.DpiScaleY);
    }

    private static async Task<BitmapSource> RenderBitmapAsync(MandelbrotState state, int width, int height,
        int ssaaFactor, CancellationToken token, IProgress<int>? progress,
        double dpiX = 96, double dpiY = 96)
    {
        int factor = Math.Clamp(ssaaFactor, 1, 10);
        int renderWidth = checked(width * factor);
        int renderHeight = checked(height * factor);
        int stride = checked(renderWidth * 4);
        byte[] buffer = new byte[checked(stride * renderHeight)];
        await Task.Run(() => MandelbrotFamilyRenderer.Render(state, buffer, renderWidth, renderHeight,
            stride, token, value => progress?.Report(value)));

        BitmapSource source = BitmapSource.Create(renderWidth, renderHeight, dpiX, dpiY,
            PixelFormats.Bgra32, null, buffer, stride);
        source.Freeze();

        if (factor == 1 || token.IsCancellationRequested)
        {
            return source;
        }

        byte[] downsampled = await Task.Run(() => DownsampleBox(
            buffer, renderWidth, width, height, factor, token));
        BitmapSource result = BitmapSource.Create(width, height, dpiX, dpiY,
            PixelFormats.Bgra32, null, downsampled, width * 4);
        result.Freeze();
        return result;
    }

    private static byte[] DownsampleBox(
        byte[] source,
        int sourceWidth,
        int targetWidth,
        int targetHeight,
        int factor,
        CancellationToken token)
    {
        var result = new byte[checked(targetWidth * targetHeight * 4)];
        int sampleCount = factor * factor;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };
        Parallel.For(0, targetHeight, options, (y, loopState) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return; }
            for (int x = 0; x < targetWidth; x++)
            {
                if ((x & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return; }
                int blue = 0, green = 0, red = 0;
                for (int sampleY = 0; sampleY < factor; sampleY++)
                {
                    int sourceOffset = ((y * factor + sampleY) * sourceWidth + x * factor) * 4;
                    for (int sampleX = 0; sampleX < factor; sampleX++)
                    {
                        blue += source[sourceOffset];
                        green += source[sourceOffset + 1];
                        red += source[sourceOffset + 2];
                        sourceOffset += 4;
                    }
                }
                int targetOffset = (y * targetWidth + x) * 4;
                result[targetOffset] = (byte)(blue / sampleCount);
                result[targetOffset + 1] = (byte)(green / sampleCount);
                result[targetOffset + 2] = (byte)(red / sampleCount);
                result[targetOffset + 3] = 255;
            }
        });
        return result;
    }

    private void SetRenderingState(bool rendering, string? status = null)
    {
        _isRendering = rendering;
        CancelButton.IsEnabled = rendering;
        if (!rendering) RenderProgress.Value = 0;
        if (status is not null) StatusText.Text = status;
    }

    private int GetThreadCount() => ThreadsBox.SelectedItem?.ToString() == "Auto"
        ? 0
        : Math.Max(1, Convert.ToInt32(ThreadsBox.SelectedItem, CultureInfo.InvariantCulture));

    private int SelectedPreviewSsaaFactor => PreviewSsaaBox.SelectedItem is ComboBoxItem item &&
                                             int.TryParse(item.Tag?.ToString(), out int factor)
        ? factor
        : 1;

    private void CanvasHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isRendering) CommitAndBakePreview();
        UpdateCoarsePreviewTransform();
        ScheduleRender();
    }

    private void CanvasHost_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        CommitAndBakePreview();
        Point mouse = e.GetPosition(CanvasHost);
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double height = Math.Max(1, CanvasHost.ActualHeight);
        double fractionX = mouse.X / width - 0.5;
        double fractionY = 0.5 - mouse.Y / height;

        FloatExp previousZoom = _zoom;
        double step = WheelZoomStep;
        _zoom = FloatExp.Clamp(_zoom * (e.Delta > 0 ? step : 1.0 / step), MinZoom, EffectiveMaxZoom);

        // Точка под курсором остаётся на месте: сдвиг центра = f · (viewOld − viewNew).
        // Сдвиг ведётся в расширенном диапазоне: на большой глубине обе ширины вида уже вне
        // double, а их разность тем более.
        FloatExp previousViewWidth = 3.0 / previousZoom;
        FloatExp currentViewWidth = 3.0 / _zoom;
        FloatExp previousViewHeight = previousViewWidth * height / width;
        FloatExp currentViewHeight = currentViewWidth * height / width;
        FloatExp shiftX = fractionX * (previousViewWidth - currentViewWidth);
        FloatExp shiftY = fractionY * (previousViewHeight - currentViewHeight);

        SyncDeepZoomState();
        ApplyCenterShift(shiftX, shiftY);

        SetZoomText();
        UpdateCoarsePreviewTransform();
        ScheduleRender();
        e.Handled = true;
    }

    private void CanvasHost_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CommitAndBakePreview();
        _isPanning = true;
        _lastPanPoint = e.GetPosition(CanvasHost);
        CanvasHost.CaptureMouse();
        Mouse.OverrideCursor = Cursors.SizeAll;
    }

    private void CanvasHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        Point current = e.GetPosition(CanvasHost);
        double width = Math.Max(1, CanvasHost.ActualWidth);
        double height = Math.Max(1, CanvasHost.ActualHeight);
        FloatExp viewWidth = 3.0 / _zoom;
        FloatExp viewHeight = viewWidth * height / width;
        FloatExp shiftX = (_lastPanPoint.X - current.X) / width * viewWidth;
        FloatExp shiftY = (current.Y - _lastPanPoint.Y) / height * viewHeight;
        ApplyCenterShift(shiftX, shiftY);
        _lastPanPoint = current;
        UpdateCoarsePreviewTransform();
    }

    /// <summary>
    /// Разрядность мантиссы для арифметики центра на UI-потоке. Ровно та же формула, что и у
    /// опорной орбиты рендера (<c>PlanDeepZoom</c>), иначе пан и зум «залипали» бы раньше,
    /// чем кадр теряет детали: сдвиг величиной 3/zoom просто утонул бы в округлении центра.
    /// Ниже ~1e93 возвращает <see cref="BigFloat.MinimumPrecisionBits"/>, то есть прежнее
    /// поведение сохраняется бит-в-бит.
    /// </summary>
    private static int CenterPrecisionBits(FloatExp zoom)
    {
        double zoomBits = zoom.Sign > 0 && zoom.IsFinite ? zoom.Log2() : 0;
        if (!double.IsFinite(zoomBits) || zoomBits < 0) zoomBits = 0;
        int needed = (int)Math.Ceiling(zoomBits) + CenterPrecisionGuardBits;
        int rounded = (needed + 63) / 64 * 64;
        return Math.Max(BigFloat.MinimumPrecisionBits, rounded);
    }

    // Запас сверх log2(зума): субпиксельная точность центра и люфт на накопление сдвигов
    // за сотни пан-событий без повторного рендера.
    private const int CenterPrecisionGuardBits = 96;

    /// <summary>
    /// Область точности для любой арифметики над <see cref="_centerXExact"/>/
    /// <see cref="_centerYExact"/>. <see cref="BigFloat"/> округляет результат каждой
    /// операции до рабочей точности потока, поэтому без этой области сдвиг центра на
    /// глубоком зуме терялся бы целиком.
    /// </summary>
    private BigFloat.PrecisionScope CenterPrecisionScope() => CenterPrecisionScope(_zoom);

    /// <inheritdoc cref="CenterPrecisionScope()"/>
    private static BigFloat.PrecisionScope CenterPrecisionScope(FloatExp zoom) =>
        new(CenterPrecisionBits(zoom));

    /// <summary>
    /// Прибавляет к центру небольшой сдвиг в мировых координатах. В глубоком режиме сдвиг
    /// уходит в <see cref="BigFloat"/>-центр с адаптивной точностью (decimal-приближение
    /// обновляется следом), в обычном — в decimal-центр по старой схеме.
    /// </summary>
    private void ApplyCenterShift(FloatExp shiftX, FloatExp shiftY)
    {
        if (_deepZoomEngaged)
        {
            using var precision = CenterPrecisionScope();
            // ToBigFloat переносит мантиссу сдвига целиком, в отличие от FromDouble: сам
            // сдвиг на глубоком зуме (~3/zoom) в double уже не представим.
            _centerXExact += shiftX.ToBigFloat();
            _centerYExact += shiftY.ToBigFloat();
            _centerX = _centerXExact.ToDecimalClamped();
            _centerY = _centerYExact.ToDecimalClamped();
        }
        else
        {
            _centerX += (decimal)shiftX.ToDouble();
            _centerY += (decimal)shiftY.ToDouble();
        }
    }

    /// <summary>
    /// Множитель зума на один щелчок колеса. Ctrl — крупный шаг (×10 за щелчок: от 1 до
    /// 1e1000 это тысяча щелчков вместо пяти с лишним тысяч), Shift — точная подстройка,
    /// без модификаторов — прежние ×1.5.
    /// </summary>
    private static double WheelZoomStep =>
        (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 10.0
        : (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 1.05
        : 1.5;

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        CanvasHost.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        ScheduleRender();
    }

    private void SetZoomText()
    {
        _updatingControls = true;
        ZoomBox.Text = FloatExpJsonConverter.ToDisplay(_zoom);
        _updatingControls = false;
    }

    /// <summary>
    /// Заводит или глушит «второй двигатель» в зависимости от текущего зума. При переходе
    /// через <see cref="DeepZoomThreshold"/> вверх центр переносится из decimal в
    /// <see cref="BigFloat"/> (безопасный «костыль» — старая математика не трогается), при
    /// возврате вниз decimal снова становится источником истины.
    /// </summary>
    private void SyncDeepZoomState()
    {
        bool shouldEngage = _zoom >= DeepZoomThreshold;
        if (shouldEngage && !_deepZoomEngaged)
        {
            using var precision = CenterPrecisionScope();
            _centerXExact = BigFloat.FromDecimal(_centerX);
            _centerYExact = BigFloat.FromDecimal(_centerY);
            _deepZoomEngaged = true;
        }
        else if (!shouldEngage && _deepZoomEngaged)
        {
            _centerX = _centerXExact.ToDecimalClamped();
            _centerY = _centerYExact.ToDecimalClamped();
            _deepZoomEngaged = false;
        }
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) ToggleFullscreen();
        else if (e.Key == Key.Escape && _isFullscreen) ToggleFullscreen();
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

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _renderTimer.Stop();
        _visualizationTimer.Stop();
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _juliaMapPreviewCts?.Cancel();
        _juliaMapPreviewCts?.Dispose();
        _activeSession = null;
    }

    private static int ReadInt(string text, string name, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"Параметр «{name}» должен быть от {minimum} до {maximum}.");
        return value;
    }

    private static double ReadDouble(string text, string name, double minimum, double maximum)
    {
        if (!TryReadDouble(text, out double value) || !double.IsFinite(value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"Параметр «{name}» должен быть от {minimum} до {maximum}.");
        return value;
    }

    private static bool TryReadDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static decimal ReadDecimal(string text, string name, decimal minimum, decimal maximum)
    {
        if (!TryReadDecimal(text, out decimal value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"Параметр «{name}» должен быть от {minimum} до {maximum}.");
        return value;
    }

    private static bool TryReadDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static MandelbrotState CloneState(MandelbrotState source) => new()
    {
        SaveName = source.SaveName, Timestamp = source.Timestamp, Variant = source.Variant,
        CenterX = source.CenterX, CenterY = source.CenterY, Zoom = source.Zoom,
        CenterXExact = source.CenterXExact, CenterYExact = source.CenterYExact,
        Iterations = source.Iterations, Threshold = source.Threshold, Threads = source.Threads,
        ColoringMode = source.ColoringMode, PaletteName = source.PaletteName,
        Palette = source.Palette.Clone(source.Palette.Name), Power = source.Power,
        UseInversion = source.UseInversion, HistogramContrast = source.HistogramContrast,
        JuliaCReal = source.JuliaCReal, JuliaCImaginary = source.JuliaCImaginary,
        HistogramEnabledEqualization = source.HistogramEnabledEqualization,
        HistogramInputUseSmooth = source.HistogramInputUseSmooth,
        SmoothBlendPower = source.SmoothBlendPower, SmoothIterationOffset = source.SmoothIterationOffset,
        PalettePhaseOffset = source.PalettePhaseOffset, PaletteScale = source.PaletteScale,
        PaletteWrapMode = source.PaletteWrapMode, UseCustomInteriorColor = source.UseCustomInteriorColor,
        InteriorColor = source.InteriorColor,
        OrbitTrapStrength = source.OrbitTrapStrength, OrbitTrapBias = source.OrbitTrapBias,
        StripeFrequency = source.StripeFrequency, StripeStrength = source.StripeStrength,
        StripeBias = source.StripeBias, PolynomialA = source.PolynomialA,
        PolynomialB = source.PolynomialB, PolynomialC = source.PolynomialC,
        PolynomialGamma = source.PolynomialGamma, PolynomialBlend = source.PolynomialBlend,
        PolynomialBias = source.PolynomialBias,
        DistanceReliefStrength = source.DistanceReliefStrength,
        DistanceLightAzimuth = source.DistanceLightAzimuth,
        DistanceLightElevation = source.DistanceLightElevation,
        DistanceAmbient = source.DistanceAmbient, DistanceDiffuse = source.DistanceDiffuse,
        DistanceSpecular = source.DistanceSpecular, DistanceShininess = source.DistanceShininess,
        DistanceContoursEnabled = source.DistanceContoursEnabled,
        DistanceContourSpacing = source.DistanceContourSpacing,
        DistanceContourStrength = source.DistanceContourStrength
    };

    private sealed class RenderSession(
        WriteableBitmap bitmap,
        int tileCount,
        int renderWidth,
        int renderHeight,
        CancellationTokenSource cancellation)
    {
        public WriteableBitmap Bitmap { get; } = bitmap;
        public int TileCount { get; } = tileCount;
        public int RenderWidth { get; } = renderWidth;
        public int RenderHeight { get; } = renderHeight;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public int CompletedTiles { get; set; }
        public ConcurrentQueue<TileRenderEvent> Events { get; } = new();
    }

    private readonly record struct TileRenderEvent(
        bool IsStart,
        MandelbrotRenderTile Tile,
        byte[]? Pixels);
}
