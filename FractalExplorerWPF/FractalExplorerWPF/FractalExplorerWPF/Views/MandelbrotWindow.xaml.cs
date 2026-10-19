using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
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
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _juliaMapPreviewCts;
    private bool _isRendering;
    private readonly MandelbrotPreviewRenderer _gpuPreview = new();
    private JuliaConstantPickerWindow? _constantPicker;
    private bool _liveRenderPending;
    private bool _liveFrameRequested;
    private bool _closed;
    private bool _updatingControls;
    private bool _controlsVisible = true;
    private readonly FullscreenToggle _fullscreen = new();

    // Вид: центр в decimal, за DeepZoomThreshold — «второй двигатель», центр в BigFloat.
    private readonly DecimalDeepZoomViewport _viewport;

    // Порог включения «второго двигателя» (пертурбация + BigFloat-центр).
    private const double DeepZoomThreshold = 1e25;

    // Верхняя граница зума. История: 5e28 — упор в тип decimal; 1e50 — до перехода на
    // масштабированную δ; 1e90 — пока арифметика центра на UI-потоке шла на фиксированных
    // 384 битах. Сейчас ограничений этого рода нет:
    //  • сам зум — FloatExp, поэтому потолок double (1.8e308) больше не при чём;
    //  • сетка кадра (ширина вида и δc на пиксель) — тоже FloatExp, поэтому шаг пикселя не
    //    схлопывается в ноль и не уходит в денормали;
    //  • точность мантиссы опорной орбиты адаптивна (PlanDeepZoom), а центр на UI-потоке
    //    ведётся с такой же адаптивной точностью (см. DeepZoomViewport.CenterPrecisionBits);
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
    private FloatExp EffectiveMaxZoom => FoldedFormulaCatalog.IsProgram(_definition.Variant) ? MaxZoom : MandelbrotVariantDefinition.ParameterVariant(_definition.Variant) switch
    {
        MandelbrotVariant.Mandelbrot or MandelbrotVariant.BurningShip
            or MandelbrotVariant.Tricorn or MandelbrotVariant.Buffalo or MandelbrotVariant.Celtic
            or MandelbrotVariant.PerpendicularMandelbrot
            or MandelbrotVariant.PerpendicularBurningShip
            or MandelbrotVariant.PerpendicularCeltic
            or MandelbrotVariant.PerpendicularBuffalo
            or MandelbrotVariant.CelticMandelbar
            or MandelbrotVariant.CubicQuasiBurningShip
            or MandelbrotVariant.CubicFlyingSquirrel
            or MandelbrotVariant.Generalized or MandelbrotVariant.Simonobrot => MaxZoom,
        _ => 5e28,
    };

    private BitmapSource? _stableBitmap;
    private int _stablePixelWidth;
    private int _stablePixelHeight;
    private TiledRenderSession? _activeSession;

    internal string SaveManagerDisplayName => _definition.DisplayName;
    internal MandelbrotVariant SaveManagerVariant => _definition.Variant;

    public MandelbrotWindow(MandelbrotVariant variant, decimal? juliaReal = null, decimal? juliaImaginary = null)
    {
        _definition = MandelbrotVariantDefinition.For(variant);
        _saveStore = new MandelbrotSaveStore(variant);
        _viewport = new DecimalDeepZoomViewport(3.0, MinZoom, EffectiveMaxZoom, DeepZoomThreshold, wheelStep: 1.5);
        InitializeComponent();
        Title = _definition.DisplayName;
        HeaderText.Text = _definition.DisplayName;
        _renderTimer.Tick += RenderTimer_OnTick;
        InitializeControls();
        InitializeHybridControls();
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
        InitializeNumericInput();
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

        RenderThreadsBox.Fill(ThreadsBox);
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

    private decimal ReadFormulaPower()
    {
        decimal power = _definition.HasPower
            ? ReadDecimal(PowerBox.Text, "степень", _definition.HasInversion ? -12m : 0.1m, 12m)
            : decimal.Truncate(_definition.DefaultPower);
        if (_definition.HasInversion && Math.Abs(power) < 0.1m)
            throw new InvalidOperationException("Для Симоноброта модуль степени должен быть не меньше 0.1.");
        return power;
    }

    public MandelbrotState CaptureState(string saveName)
    {
        NumericInput.Prepare(this);
        // Потолок совпадает с BlaMaxOrbitLength: за ним таблица BLA не строится и глубокий
        // зум проваливается в рендер без пропуска итераций, то есть становится неприменимо
        // медленным. На 1e1000 счёт идёт о сотнях тысяч итераций, так что запас есть.
        int iterations = ReadInt(IterationsBox.Text, "итерации", 50, MaxIterations);
        decimal threshold = ReadDecimal(ThresholdBox.Text, "порог выхода", 0.1m, 1_000m);
        decimal power = ReadFormulaPower();
        if (FoldedFormulaCatalog.IsHybrid(_definition.Variant)) _hybridSettings = ReadHybridControls();
        MandelbrotPalette palette = _paletteManager.ActivePalette.Clone(_paletteManager.ActivePalette.Name);

        (decimal centerX, decimal centerY, string? exactX, string? exactY) = _viewport.CaptureCenter();
        return new MandelbrotState
        {
            SaveName = saveName,
            Timestamp = DateTime.Now,
            Variant = _definition.Variant,
            CenterX = centerX,
            CenterY = centerY,
            CenterXExact = exactX,
            CenterYExact = exactY,
            Zoom = _viewport.Zoom,
            Iterations = iterations,
            Threshold = threshold,
            Threads = GetThreadCount(),
            ColoringMode = SelectedColoringMode,
            PaletteName = palette.Name,
            Palette = palette,
            Power = power,
            Hybrid = _hybridSettings.Clone(),
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
            PaletteScale = ReadDouble(PaletteScaleBox.Text, "масштаб палитры", -100, 100),
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
        _viewport.Restore(state.CenterX, state.CenterY, state.CenterXExact, state.CenterYExact, state.Zoom);
        IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
        ThresholdBox.Text = state.Threshold.ToString(CultureInfo.InvariantCulture);
        ZoomBox.Text = FloatExpJsonConverter.ToDisplay(_viewport.Zoom);
        PowerBox.Text = state.Power.ToString(CultureInfo.InvariantCulture);
        LoadHybridSettings(state.Hybrid);
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
        MandelbrotPalette loadedPalette = ResolveSavedPalette(state);
        _paletteManager.ActivePalette = loadedPalette.Clone(
            string.IsNullOrWhiteSpace(state.PaletteName) ? loadedPalette.Name : state.PaletteName);
        _updatingControls = false;
        UpdateJuliaMapMarker();
        if (_definition.HasJuliaConstant)
        {
            _constantPicker?.UpdateFormulaParameters(state.Power, state.UseInversion, _hybridSettings);
            if (IsLoaded) _ = RenderJuliaMapPreviewAsync();
        }
        ScheduleRender();
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StablePreviewImage, CanvasImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        MandelbrotState state, int width, int height, CancellationToken token, IProgress<int>? progress = null)
    {
        MandelbrotState preview = CloneState(state);
        MandelbrotPalette palette = ResolveSavedPalette(state);
        preview.Palette = palette.Clone(palette.Name);
        return RenderBitmapAsync(preview, width, height, 1, token, progress);
    }

    private MandelbrotPalette ResolveSavedPalette(MandelbrotState state)
    {
        MandelbrotPalette palette = state.Palette;
        if (!string.IsNullOrWhiteSpace(state.PaletteName) &&
            (palette.Colors.Count == 0 || palette.Name == "Новая палитра"))
            return _paletteManager.Palettes.FirstOrDefault(candidate =>
                candidate.Name.Equals(state.PaletteName, StringComparison.OrdinalIgnoreCase)) ?? palette;
        return palette;
    }

    private MandelbrotColoringMode SelectedColoringMode =>
        ColoringModeBox.SelectedIndex < 0 ? MandelbrotColoringMode.Smooth : (MandelbrotColoringMode)ColoringModeBox.SelectedIndex;

    private void Parameter_OnChanged(object sender, EventArgs e)
    {
        if (!_updatingControls && IsLoaded)
        {
            UpdateJuliaMapMarker();
            if (_definition.HasJuliaConstant && (ReferenceEquals(sender, PowerBox) || ReferenceEquals(sender, InversionBox)))
            {
                try
                {
                    _constantPicker?.UpdateFormulaParameters(ReadFormulaPower(), InversionBox.IsChecked == true, _hybridSettings);
                    _ = RenderJuliaMapPreviewAsync();
                }
                catch (Exception ex) { StatusText.Text = ex.Message; }
            }
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
        if (!NumericInput.IsValid(ZoomBox)) return;
        if (!_updatingControls && FloatExp.TryParse(ZoomBox.Text, out FloatExp zoom) && zoom.Sign > 0)
        {
            _viewport.SetZoom(zoom);
            UpdateCoarsePreviewTransform();
            ScheduleRender();
        }
    }

    private void PaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new MandelbrotPaletteWindow(_paletteManager) { Owner = this };
        dialog.PaletteApplied += (_, _) => ScheduleRender();
        dialog.ShowDialog();
    }

    private void JuliaConstantButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_constantPicker is { } existing) { existing.Activate(); return; }
        decimal real;
        decimal imaginary;
        decimal power;
        try
        {
            real = ReadDecimal(JuliaRealBox.Text, "действительная часть C", -10m, 10m);
            imaginary = ReadDecimal(JuliaImaginaryBox.Text, "мнимая часть C", -10m, 10m);
            power = ReadFormulaPower();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MandelbrotVariant sourceVariant = MandelbrotVariantDefinition.ParameterVariant(_definition.Variant);
        var dialog = new JuliaConstantPickerWindow(sourceVariant, real, imaginary, power, InversionBox.IsChecked == true, _hybridSettings) { Owner = this };
        _constantPicker = dialog;
        dialog.CanLivePreview = CanPreviewJuliaLive;
        dialog.ConstantApplied += (_, _) => ApplyPickerConstant(dialog, false);
        dialog.LiveConstantChanged += (_, _) => ApplyPickerConstant(dialog, true);
        dialog.Closed += (_, _) => { if (ReferenceEquals(_constantPicker, dialog)) _constantPicker = null; };
        dialog.Show();
    }

    private bool CanPreviewJuliaLive()
    {
        if (_closed || !_definition.HasJuliaConstant) return false;
        try { return _gpuPreview.CanRender(CaptureState("live")); }
        catch { return false; }
    }

    private void ApplyPickerConstant(JuliaConstantPickerWindow picker, bool live)
    {
        if (_closed || (live && !CanPreviewJuliaLive())) return;
        _liveFrameRequested = live;
        _updatingControls = true;
        JuliaRealBox.Text = picker.SelectedReal.ToString(CultureInfo.InvariantCulture);
        JuliaImaginaryBox.Text = picker.SelectedImaginary.ToString(CultureInfo.InvariantCulture);
        _updatingControls = false;
        UpdateJuliaMapMarker();
        if (CanPreviewJuliaLive())
        {
            // Do not cancel the frame in flight: continuously cancelling can starve presentation.
            // One pending flag retains the latest C, with no queue of obsolete frames.
            _liveRenderPending = true;
            _renderTimer.Interval = TimeSpan.FromMilliseconds(16);
            if (!_isRendering && !_renderTimer.IsEnabled) _renderTimer.Start();
        }
        else ScheduleRender();
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

        decimal power;
        try { power = ReadFormulaPower(); }
        catch { return; }
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
            Power = power,
            Hybrid = _hybridSettings.Clone(),
            UseInversion = InversionBox.IsChecked == true,
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
        MandelbrotVariantDefinition.ParameterVariant(_definition.Variant) switch
        {
            MandelbrotVariant.BurningShip => (MandelbrotVariant.BurningShip, -0.25m, 0.25m, 3m / 3.5m),
            MandelbrotVariant.Mandelbrot => (MandelbrotVariant.Mandelbrot, -0.5m, 0m, 1m),
            var source => (source, 0m, 0m, 0.75m)
        };

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

    private void SavesButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); SaveManagerWindow.Open(this, SaveManagerConfigurations.ForMandelbrot(this, _saveStore)); }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
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

    private void RenderButton_OnClick(object sender, RoutedEventArgs e) { NumericInput.CommitAll(this); _renderTimer.Stop(); _ = RenderPreviewAsync(); }
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
        _viewport.Reset(_definition.InitialCenterX, _definition.InitialCenterY, _definition.InitialZoom);
        _updatingControls = true;
        ZoomBox.Text = FloatExpJsonConverter.ToDisplay(_viewport.Zoom);
        _updatingControls = false;
        if (render) ScheduleRender();
    }

    private void ScheduleRender()
    {
        if (!IsLoaded || _closed) return;
        _liveRenderPending = false;
        _liveFrameRequested = false;
        _renderCts?.Cancel();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(CanPreviewJuliaLive() ? 16 : 320);
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
        if (_closed) return;
        if (_isRendering) { _renderTimer.Start(); return; }
        _liveRenderPending = false;
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
        TiledRenderSession? session = null;
        try
        {
            int factor = SelectedPreviewSsaaFactor;
            if (_definition.HasJuliaConstant && _gpuPreview.CanRender(state))
            {
                bool liveFrame = _liveFrameRequested;
                // Live input keeps the same DPI-sized output and selected SSAA as a settled frame.
                // Coalescing pending C values controls latency without reducing image quality.
                int gpuWidth = checked(pixelWidth * factor), gpuHeight = checked(pixelHeight * factor);
                byte[] gpuPixels = new byte[checked(gpuWidth * gpuHeight * 4)];
                bool rendered = await Task.Run(() => _gpuPreview.TryRender(state, gpuPixels, gpuWidth, gpuHeight, token));
                token.ThrowIfCancellationRequested();
                if (rendered)
                {
                    if (factor > 1)
                        gpuPixels = await Task.Run(() => DownsampleBox(gpuPixels, gpuWidth, pixelWidth, pixelHeight, factor, token));
                    token.ThrowIfCancellationRequested();
                    if (_closed) return;
                    var gpuBitmap = BitmapSource.Create(pixelWidth, pixelHeight,
                        surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY, PixelFormats.Bgra32, null, gpuPixels, pixelWidth * 4);
                    gpuBitmap.Freeze();
                    _viewport.MarkRendered(state.CenterX, state.CenterY, state.CenterXExact, state.CenterYExact, state.Zoom);
                    SetStableBitmap(gpuBitmap, logicalWidth, logicalHeight);
                    UpdateCoarsePreviewTransform();
                    StatusText.Text = $"ГП{(liveFrame ? " · Живой просмотр" : "")} · Готово за {stopwatch.Elapsed.TotalSeconds:F3} сек. Центр: {state.CenterX:G6}; {state.CenterY:G6}";
                    return;
                }
            }
            int renderWidth = checked(pixelWidth * factor);
            int renderHeight = checked(pixelHeight * factor);
            IReadOnlyList<MandelbrotRenderTile> tiles = MandelbrotTileScheduler.Create(
                renderWidth, renderHeight, 16 * factor, strategy);
            WriteableBitmap bitmap = ProgressiveRenderBitmap.CreateOverlay(renderWidth, renderHeight,
                surface.Dpi.PixelsPerInchX, surface.Dpi.PixelsPerInchY);
            session = TiledRenderSession.Begin(bitmap, tiles.Count, RenderOverlay, RenderProgress, cts);
            session.VisualizationFailed += _ => StatusText.Text = "Ошибка визуализации прогресса рендера.";
            _activeSession = session;
            CanvasImage.Source = bitmap;

            await RenderTilesAsync(state, tiles, session, token);
            if (token.IsCancellationRequested)
            {
                if (_activeSession is not null) CommitAndBakePreview();
                if (ReferenceEquals(_renderCts, cts)) StatusText.Text = "Рендер отменён";
                return;
            }
            session.Flush();
            BitmapSource completed = session.Snapshot();
            _viewport.MarkRendered(state.CenterX, state.CenterY, state.CenterXExact, state.CenterYExact, state.Zoom);
            SetStableBitmap(completed, logicalWidth, logicalHeight);
            CanvasImage.Source = null;
            session.End();
            if (ReferenceEquals(_activeSession, session)) _activeSession = null;
            stopwatch.Stop();
            StatusText.Text = $"ЦП{(_gpuPreview.FailureReason is null ? "" : " (ГП недоступен)")} · Готово за {stopwatch.Elapsed.TotalSeconds:F3} сек. " +
                              $"Стратегия: {GetStrategyDisplayName(strategy)}. Центр: {_viewport.CenterX:G6}; {_viewport.CenterY:G6}";
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
            session?.End();
            if (ReferenceEquals(_renderCts, cts))
            {
                _renderCts = null;
                SetRenderingState(false);
            }
            cts.Dispose();
            if (!_closed && _liveRenderPending)
            {
                _renderTimer.Interval = TimeSpan.FromMilliseconds(1);
                _renderTimer.Start();
            }
        }
    }

    private async Task RenderTilesAsync(
        MandelbrotState state,
        IReadOnlyList<MandelbrotRenderTile> tiles,
        TiledRenderSession session,
        CancellationToken token)
    {
        // Histogram equalization needs one CDF for the entire frame. Rendering it
        // independently per tile changes the palette after loading a saved preview.
        byte[]? histogramFrame = null;
        if (state.ColoringMode == MandelbrotColoringMode.Histogram)
        {
            StatusText.Text = "ЦП · Расчёт гистограммы кадра...";
            histogramFrame = new byte[checked(session.RenderWidth * session.RenderHeight * 4)];
            await Task.Run(() => MandelbrotFamilyRenderer.Render(state, histogramFrame,
                session.RenderWidth, session.RenderHeight, session.RenderWidth * 4, token));
            token.ThrowIfCancellationRequested();
        }
        int workerCount = state.Threads <= 0 ? Environment.ProcessorCount : state.Threads;
        await session.RenderTilesAsync(tiles, workerCount, (tile, tileToken) =>
        {
            if (histogramFrame is null)
                return MandelbrotFamilyRenderer.RenderTile(state, session.RenderWidth, session.RenderHeight, tile, tileToken);
            byte[] pixels = new byte[checked(tile.Width * tile.Height * 4)];
            for (int row = 0; row < tile.Height; row++)
                Buffer.BlockCopy(histogramFrame, ((tile.Y + row) * session.RenderWidth + tile.X) * 4,
                    pixels, row * tile.Width * 4, tile.Width * 4);
            return pixels;
        }, token);
    }

    /// <summary>
    /// Готовый кадр в стабильный предпросмотр. Центр и зум, по которым он посчитан, к этому
    /// моменту уже отмечены в виде (<see cref="DeepZoomViewport{T}.MarkRendered()"/>).
    /// </summary>
    private void SetStableBitmap(BitmapSource bitmap, int pixelWidth, int pixelHeight)
    {
        _stableBitmap = bitmap;
        _stablePixelWidth = Math.Max(1, pixelWidth);
        _stablePixelHeight = Math.Max(1, pixelHeight);
        StablePreviewImage.Source = bitmap;
        StablePreviewImage.RenderTransform = Transform.Identity;
        RenderOptions.SetBitmapScalingMode(StablePreviewImage, BitmapScalingMode.HighQuality);
    }

    private void CommitAndBakePreview()
    {
        TiledRenderSession? session = _activeSession;
        if (session is null) return;

        session.Cancellation?.Cancel();
        session.Flush();
        RenderSurfaceMetrics surface = RenderSurfaceMetrics.Measure(ImageLayer);
        int logicalWidth = Math.Max(1, (int)Math.Round(surface.LogicalWidth));
        int logicalHeight = Math.Max(1, (int)Math.Round(surface.LogicalHeight));
        if (TiledRenderSession.TryCaptureLayer(ImageLayer) is { } baked)
        {
            _viewport.MarkRendered();
            SetStableBitmap(baked, logicalWidth, logicalHeight);
        }
        CanvasImage.Source = null;
        session.End();
        if (ReferenceEquals(_activeSession, session)) _activeSession = null;
    }

    private void UpdateCoarsePreviewTransform()
    {
        if (_stableBitmap is null || !_viewport.HasRenderedFrame || _viewport.RenderedZoom.Sign <= 0 ||
            _viewport.Zoom.Sign <= 0 || _stablePixelWidth <= 0 || _stablePixelHeight <= 0) return;

        // Ширины вида и разность центров — в FloatExp и BigFloat: на глубоком зуме шаг вида
        // уходит за пределы и decimal, и double. Пропорции готового кадра могут отличаться от
        // текущего холста (окно меняло размер), поэтому масштабы по осям считаются отдельно.
        double width = Math.Max(1, ImageLayer.ActualWidth);
        double height = Math.Max(1, ImageLayer.ActualHeight);
        FloatExp renderedViewWidth = _viewport.BaseViewWidth / _viewport.RenderedZoom;
        FloatExp currentViewWidth = _viewport.ViewWidth;
        if (!renderedViewWidth.IsFinite || !currentViewWidth.IsFinite ||
            renderedViewWidth.Sign <= 0 || currentViewWidth.Sign <= 0) return;

        double stableAspect = (double)_stablePixelHeight / _stablePixelWidth;
        FloatExp renderedViewHeight = renderedViewWidth * stableAspect;
        FloatExp currentViewHeight = currentViewWidth * height / width;

        double scaleX = (renderedViewWidth / currentViewWidth).ToDouble();
        double scaleY = (renderedViewHeight / currentViewHeight).ToDouble();

        // Сдвиг в долях ширины вида; по вертикали масштаб мира тот же, что по горизонтали.
        (FloatExp shiftX, FloatExp shiftY) = _viewport.RenderedOffset();
        double offsetX = (shiftX * width).ToDouble() + width / 2.0 * (1 - scaleX);
        double offsetY = (shiftY * width).ToDouble() + height / 2.0 * (1 - scaleY);
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

    // 0 — число потоков выбирает сам рендер.
    private int GetThreadCount() => RenderThreadsBox.IsAuto(ThreadsBox) ? 0 : RenderThreadsBox.Read(ThreadsBox);

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
        _viewport.ZoomAt(e.GetPosition(CanvasHost), CanvasHost.ActualWidth, CanvasHost.ActualHeight,
            _viewport.WheelFactor(e.Delta));
        SetZoomText();
        UpdateCoarsePreviewTransform();
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
        if (_viewport.PanTo(e.GetPosition(CanvasHost), CanvasHost.ActualWidth)) UpdateCoarsePreviewTransform();
    }

    private void CanvasHost_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_viewport.EndPan()) return;
        CanvasHost.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        ScheduleRender();
    }

    private void SetZoomText()
    {
        _updatingControls = true;
        ZoomBox.Text = FloatExpJsonConverter.ToDisplay(_viewport.Zoom);
        _updatingControls = false;
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        _fullscreen.HandleKey(this, e.Key);
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closed = true;
        _constantPicker?.Close();
        _renderTimer.Stop();
        _activeSession?.End();
        _renderCts?.Cancel();
        _juliaMapPreviewCts?.Cancel();
        _activeSession = null;
        _ = Task.Run(_gpuPreview.Dispose);
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

    private static bool TryReadDouble(string text, out double value) => (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) && double.IsFinite(value);

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
        Palette = source.Palette.Clone(source.Palette.Name), Power = source.Power, Hybrid = source.Hybrid.Clone(),
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
}
