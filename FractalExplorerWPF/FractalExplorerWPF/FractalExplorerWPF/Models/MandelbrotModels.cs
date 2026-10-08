using System.Text.Json.Serialization;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Infrastructure.Serialization;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace FractalExplorerWPF.Models;

public enum MandelbrotVariant
{
    Mandelbrot,
    BurningShip,
    Tricorn,
    Buffalo,
    Celtic,
    Simonobrot,
    Generalized,
    Julia,
    JuliaBurningShip,
    JuliaGeneralized,
    JuliaTricorn,
    JuliaBuffalo,
    JuliaCeltic,
    JuliaSimonobrot,
    PerpendicularMandelbrot,
    PerpendicularBurningShip,
    PerpendicularCeltic,
    PerpendicularBuffalo,
    JuliaPerpendicularMandelbrot,
    JuliaPerpendicularBurningShip,
    JuliaPerpendicularCeltic,
    JuliaPerpendicularBuffalo,
    CelticMandelbar,
    CubicQuasiBurningShip,
    CubicFlyingSquirrel,
    JuliaCelticMandelbar,
    JuliaCubicQuasiBurningShip,
    JuliaCubicFlyingSquirrel,
    CubicBurningShip,
    JuliaCubicBurningShip,
    CubicBuffalo,
    JuliaCubicBuffalo,
    CubicCeltic,
    JuliaCubicCeltic,
    CubicMandelbar,
    JuliaCubicMandelbar,
    QuarticBurningShip,
    JuliaQuarticBurningShip,
    QuarticBuffalo,
    JuliaQuarticBuffalo,
    QuarticCeltic,
    JuliaQuarticCeltic,
    QuarticMandelbar,
    JuliaQuarticMandelbar,
    QuinticBurningShip,
    JuliaQuinticBurningShip,
    QuinticBuffalo,
    JuliaQuinticBuffalo,
    QuinticCeltic,
    JuliaQuinticCeltic,
    QuinticMandelbar,
    JuliaQuinticMandelbar,
    CubicPartialBurningShipReal,
    JuliaCubicPartialBurningShipReal,
    CubicPartialBurningShipImag,
    JuliaCubicPartialBurningShipImag,
    CubicQuasiPerpendicular,
    JuliaCubicQuasiPerpendicular,
    CubicCelticQuasiPerpendicular,
    JuliaCubicCelticQuasiPerpendicular,
    CubicQuasiPerpendicularBurningShip,
    JuliaCubicQuasiPerpendicularBurningShip,
    CubicQuasiPerpendicularBuffalo,
    JuliaCubicQuasiPerpendicularBuffalo,
    QuarticPartialBurningShipImag,
    JuliaQuarticPartialBurningShipImag,
    QuarticPartialBurningShipReal,
    JuliaQuarticPartialBurningShipReal,
    QuarticPartialBurningShipRealMandelbar,
    JuliaQuarticPartialBurningShipRealMandelbar,
    QuarticCelticPartialBurningShipImag,
    JuliaQuarticCelticPartialBurningShipImag,
    QuarticCelticPartialBurningShipReal,
    JuliaQuarticCelticPartialBurningShipReal,
    QuarticCelticPartialBurningShipRealMandelbar,
    JuliaQuarticCelticPartialBurningShipRealMandelbar,
    QuarticBuffaloPartialImag,
    JuliaQuarticBuffaloPartialImag,
    QuarticCelticMandelbar,
    JuliaQuarticCelticMandelbar,
    QuarticFalseQuasiPerpendicular,
    JuliaQuarticFalseQuasiPerpendicular,
    QuarticFalseQuasiHeart,
    JuliaQuarticFalseQuasiHeart,
    QuarticCelticFalseQuasiPerpendicular,
    JuliaQuarticCelticFalseQuasiPerpendicular,
    QuarticCelticFalseQuasiHeart,
    JuliaQuarticCelticFalseQuasiHeart,
    QuarticImagQuasi,
    JuliaQuarticImagQuasi,
    QuarticRealQuasiPerpendicular,
    JuliaQuarticRealQuasiPerpendicular,
    QuarticRealQuasiHeart,
    JuliaQuarticRealQuasiHeart,
    QuarticCelticImagQuasi,
    JuliaQuarticCelticImagQuasi,
    QuarticCelticRealQuasiPerpendicular,
    JuliaQuarticCelticRealQuasiPerpendicular,
    QuarticCelticRealQuasiHeart,
    JuliaQuarticCelticRealQuasiHeart,
    QuinticPartialBurningShipReal,
    JuliaQuinticPartialBurningShipReal,
    QuinticPartialBurningShipRealMandelbar,
    JuliaQuinticPartialBurningShipRealMandelbar,
    QuinticCelticMandelbar,
    JuliaQuinticCelticMandelbar,
    QuinticQuasiBurningShip,
    JuliaQuinticQuasiBurningShip,
    QuinticQuasiPerpendicular,
    JuliaQuinticQuasiPerpendicular,
    QuinticQuasiHeart,
    JuliaQuinticQuasiHeart,
    QuinticQuasiPerpendicularBurningShip,
    JuliaQuinticQuasiPerpendicularBurningShip,
    QuinticQuasiPerpendicularBuffalo,
    JuliaQuinticQuasiPerpendicularBuffalo,
    QuinticCelticQuasiPerpendicular,
    JuliaQuinticCelticQuasiPerpendicular,
    QuinticCelticQuasiHeart,
    JuliaQuinticCelticQuasiHeart,
    Hybrid,
    JuliaHybrid
}

public enum MandelbrotColoringMode
{
    Discrete,
    Smooth,
    Histogram,
    OrbitTrap,
    StripeAverage,
    SmoothEscapePolynomial,
    DistanceEstimation
}

public enum MandelbrotPaletteWrapMode
{
    Repeat,
    Clamp,
    Mirror
}

public enum MandelbrotPaletteKind
{
    ColorSequence,
    AlgorithmicGrayscale
}

public sealed record MandelbrotVariantDefinition(
    MandelbrotVariant Variant,
    string DisplayName,
    string Identifier,
    decimal InitialCenterX,
    decimal InitialCenterY,
    double InitialZoom,
    bool HasPower = false,
    bool HasInversion = false,
    decimal DefaultPower = 2.0m,
    bool HasJuliaConstant = false,
    decimal DefaultJuliaReal = 0m,
    decimal DefaultJuliaImaginary = 0m)
{
    public static bool IsJulia(MandelbrotVariant variant) => FoldedFormulaCatalog.IsJulia(variant) || variant is
        MandelbrotVariant.Julia or MandelbrotVariant.JuliaBurningShip or MandelbrotVariant.JuliaGeneralized
        or MandelbrotVariant.JuliaTricorn or MandelbrotVariant.JuliaBuffalo or MandelbrotVariant.JuliaCeltic
        or MandelbrotVariant.JuliaSimonobrot
        or MandelbrotVariant.JuliaPerpendicularMandelbrot
        or MandelbrotVariant.JuliaPerpendicularBurningShip
        or MandelbrotVariant.JuliaPerpendicularCeltic
        or MandelbrotVariant.JuliaPerpendicularBuffalo
        or MandelbrotVariant.JuliaCelticMandelbar
        or MandelbrotVariant.JuliaCubicQuasiBurningShip
        or MandelbrotVariant.JuliaCubicFlyingSquirrel;

    /// <summary>Та же формула на параметрической плоскости C; не меняет категорию сохранения.</summary>
    public static MandelbrotVariant ParameterVariant(MandelbrotVariant variant) => variant switch
    {
        MandelbrotVariant.Julia => MandelbrotVariant.Mandelbrot,
        MandelbrotVariant.JuliaBurningShip => MandelbrotVariant.BurningShip,
        MandelbrotVariant.JuliaGeneralized => MandelbrotVariant.Generalized,
        MandelbrotVariant.JuliaTricorn => MandelbrotVariant.Tricorn,
        MandelbrotVariant.JuliaBuffalo => MandelbrotVariant.Buffalo,
        MandelbrotVariant.JuliaCeltic => MandelbrotVariant.Celtic,
        MandelbrotVariant.JuliaSimonobrot => MandelbrotVariant.Simonobrot,
        MandelbrotVariant.JuliaPerpendicularMandelbrot => MandelbrotVariant.PerpendicularMandelbrot,
        MandelbrotVariant.JuliaPerpendicularBurningShip => MandelbrotVariant.PerpendicularBurningShip,
        MandelbrotVariant.JuliaPerpendicularCeltic => MandelbrotVariant.PerpendicularCeltic,
        MandelbrotVariant.JuliaPerpendicularBuffalo => MandelbrotVariant.PerpendicularBuffalo,
        MandelbrotVariant.JuliaCelticMandelbar => MandelbrotVariant.CelticMandelbar,
        MandelbrotVariant.JuliaCubicQuasiBurningShip => MandelbrotVariant.CubicQuasiBurningShip,
        MandelbrotVariant.JuliaCubicFlyingSquirrel => MandelbrotVariant.CubicFlyingSquirrel,
        _ => FoldedFormulaCatalog.ParameterVariant(variant)
    };

    public static MandelbrotVariantDefinition For(MandelbrotVariant variant) => variant switch
    {
        MandelbrotVariant.Mandelbrot => new(variant, "Множество Мандельброта", "Mandelbrot", -0.5m, 0, 0.75),
        MandelbrotVariant.BurningShip => new(variant, "Множество «Горящий корабль»", "MandelbrotBurningShip", 0, 0.5m, 0.75),
        MandelbrotVariant.Tricorn => new(variant, "Трикорн (Mandelbar)", "Tricorn", 0, 0, 0.75),
        MandelbrotVariant.Buffalo => new(variant, "Фрактал Буффало", "Buffalo", 0, 0, 0.75),
        MandelbrotVariant.Celtic => new(variant, "Кельтский Мандельброт", "CelticMandelbrot", 0, 0, 0.75),
        MandelbrotVariant.Simonobrot => new(variant, "Симоноброт", "Simonobrot", 0, 0, 0.75, true, true, 2),
        MandelbrotVariant.Generalized => new(variant, "Обобщённый Мандельброт", "GeneralizedMandelbrot", 0, 0, 0.75, true, false, 3),
        MandelbrotVariant.Julia => new(variant, "Классическое множество Жюлиа", "Julia", 0, 0, 0.75,
            HasJuliaConstant: true, DefaultJuliaReal: -0.800m, DefaultJuliaImaginary: 0.156m),
        MandelbrotVariant.JuliaBurningShip => new(variant, "Горящий Корабль (Жюлиа)", "JuliaBurningShip", 0, 0, 0.75,
            HasJuliaConstant: true, DefaultJuliaReal: -1.7551867961883m, DefaultJuliaImaginary: 0.01068m),
        MandelbrotVariant.JuliaGeneralized => new(variant, "Обобщённое Жюлиа (Multijulia)", "JuliaGeneralized", 0, 0, 0.75,
            HasPower: true, DefaultPower: 3, HasJuliaConstant: true, DefaultJuliaReal: -0.2m, DefaultJuliaImaginary: 0.7m),
        MandelbrotVariant.JuliaTricorn => new(variant, "Трикорн (Жюлиа)", "JuliaTricorn", 0, 0, 0.75,
            HasJuliaConstant: true, DefaultJuliaReal: -0.1m, DefaultJuliaImaginary: 0.65m),
        MandelbrotVariant.JuliaBuffalo => new(variant, "Буффало (Жюлиа)", "JuliaBuffalo", 0, 0, 0.75,
            HasJuliaConstant: true, DefaultJuliaReal: -0.5m, DefaultJuliaImaginary: -0.45m),
        MandelbrotVariant.JuliaCeltic => new(variant, "Кельтское Жюлиа", "JuliaCeltic", 0, 0, 0.75,
            HasJuliaConstant: true, DefaultJuliaReal: -0.75m, DefaultJuliaImaginary: 0.12m),
        MandelbrotVariant.JuliaSimonobrot => new(variant, "Симоноброт (Жюлиа)", "JuliaSimonobrot", 0, 0, 0.75,
            HasPower: true, HasInversion: true, HasJuliaConstant: true,
            DefaultJuliaReal: -0.5m, DefaultJuliaImaginary: 0.2m),
        MandelbrotVariant.PerpendicularMandelbrot => new(variant, "Перпендикулярный Мандельброт", "PerpendicularMandelbrot", -0.4m, 0, 0.75),
        MandelbrotVariant.JuliaPerpendicularMandelbrot => new(variant, "Перпендикулярный Мандельброт (Жюлиа)", "JuliaPerpendicularMandelbrot", 0, 0, 0.75,
            HasJuliaConstant: true, DefaultJuliaReal: -0.4m, DefaultJuliaImaginary: 0.2m),
        MandelbrotVariant.PerpendicularBurningShip => new(variant, "Перпендикулярный горящий корабль", "PerpendicularBurningShip", -0.4m, 0, 0.75),
        MandelbrotVariant.JuliaPerpendicularBurningShip => new(variant, "Перпендикулярный горящий корабль (Жюлиа)", "JuliaPerpendicularBurningShip", 0, 0, 0.75,
            HasJuliaConstant: true, DefaultJuliaReal: -0.4m, DefaultJuliaImaginary: 0.3m),
        MandelbrotVariant.PerpendicularCeltic => new(variant, "Перпендикулярный Celtic", "PerpendicularCeltic", -0.4m, 0, 0.75),
        MandelbrotVariant.JuliaPerpendicularCeltic => new(variant, "Перпендикулярный Celtic (Жюлиа)", "JuliaPerpendicularCeltic", 0, 0, 0.75,
            HasJuliaConstant: true, DefaultJuliaReal: -0.6m, DefaultJuliaImaginary: 0.2m),
        MandelbrotVariant.PerpendicularBuffalo => new(variant, "Перпендикулярный Buffalo", "PerpendicularBuffalo", -0.4m, 0, 0.75),
        MandelbrotVariant.JuliaPerpendicularBuffalo => new(variant, "Перпендикулярный Buffalo (Жюлиа)", "JuliaPerpendicularBuffalo", 0, 0, 0.75,
            HasJuliaConstant: true, DefaultJuliaReal: -0.6m, DefaultJuliaImaginary: 0.3m),
        MandelbrotVariant.CelticMandelbar => new(variant, "Celtic Mandelbar", "CelticMandelbar", -0.4m, 0, 0.75),
        MandelbrotVariant.JuliaCelticMandelbar => new(variant, "Celtic Mandelbar (Жюлиа)", "JuliaCelticMandelbar", 0, 0, 0.75,
            HasJuliaConstant: true, DefaultJuliaReal: -0.75m, DefaultJuliaImaginary: 0.12m),
        MandelbrotVariant.CubicQuasiBurningShip => new(variant, "Кубический Quasi Burning Ship", "CubicQuasiBurningShip", 0, 0.25m, 0.75, DefaultPower: 3),
        MandelbrotVariant.JuliaCubicQuasiBurningShip => new(variant, "Кубический Quasi Burning Ship (Жюлиа)", "JuliaCubicQuasiBurningShip", 0, 0, 0.75,
            DefaultPower: 3, HasJuliaConstant: true, DefaultJuliaReal: -0.1m, DefaultJuliaImaginary: 0.85m),
        MandelbrotVariant.CubicFlyingSquirrel => new(variant, "Кубическая летящая белка (Flying Squirrel)", "CubicFlyingSquirrel", 0, -0.25m, 0.75, DefaultPower: 3),
        MandelbrotVariant.JuliaCubicFlyingSquirrel => new(variant, "Кубическая летящая белка (Жюлиа)", "JuliaCubicFlyingSquirrel", 0, 0, 0.75,
            DefaultPower: 3, HasJuliaConstant: true, DefaultJuliaReal: -0.1m, DefaultJuliaImaginary: -0.85m),
        _ => FoldedFormulaCatalog.Definition(variant)
    };
}

public sealed class MandelbrotPalette
{
    public string Name { get; set; } = "Новая палитра";
    public List<Color> Colors { get; set; } = [MediaColors.Black, MediaColors.White];
    public Color InteriorColor { get; set; } = MediaColors.Black;
    public bool IsGradient { get; set; } = true;
    public bool IsBuiltIn { get; set; }
    public double Gamma { get; set; } = 1.0;
    public int ColorPeriod { get; set; } = 500;
    public bool AlignWithRenderIterations { get; set; }
    public MandelbrotPaletteKind Kind { get; set; }

    [JsonIgnore]
    public bool UsesAlgorithmicGrayscale => Kind == MandelbrotPaletteKind.AlgorithmicGrayscale ||
                                               string.Equals(Name, "Стандартный серый", StringComparison.OrdinalIgnoreCase) ||
                                               LooksLikeLegacyLoadedGrayscale();

    private bool LooksLikeLegacyLoadedGrayscale() =>
        Kind == MandelbrotPaletteKind.ColorSequence &&
        Name?.StartsWith("Загружено:", StringComparison.OrdinalIgnoreCase) == true &&
        IsGradient && ColorPeriod == 800 && Colors.Count == 2 &&
        Colors[0] == MediaColors.Black && Colors[1] == MediaColors.White;

    public MandelbrotPalette Clone(string name) => new()
    {
        Name = name,
        Colors = [.. Colors],
        InteriorColor = InteriorColor,
        IsGradient = IsGradient,
        Gamma = Gamma,
        ColorPeriod = ColorPeriod,
        AlignWithRenderIterations = AlignWithRenderIterations,
        Kind = UsesAlgorithmicGrayscale
            ? MandelbrotPaletteKind.AlgorithmicGrayscale
            : Kind
    };

    public override string ToString() => Name;
}

public sealed class MandelbrotState
{
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public MandelbrotVariant Variant { get; set; }
    public decimal CenterX { get; set; }
    public decimal CenterY { get; set; }

    /// <summary>
    /// Точная (за пределами ~28 цифр <see cref="decimal"/>) координата центра по X в виде
    /// десятичной строки инвариантной культуры. Заполняется только «вторым двигателем»
    /// глубокого зума; для всех обычных сохранений остаётся <c>null</c>, и источником
    /// истины служит <see cref="CenterX"/>.
    /// </summary>
    public string? CenterXExact { get; set; }

    /// <summary>Точная координата центра по Y. См. <see cref="CenterXExact"/>.</summary>
    public string? CenterYExact { get; set; }

    /// <summary>
    /// Коэффициент масштабирования. <see cref="FloatExp"/> (double-мантисса + 32-битная
    /// двоичная экспонента), а не <see cref="double"/>: сам зум — это множитель, 15–16
    /// значащих цифр которого с запасом хватает, а вот его верхняя граница ограничивала
    /// глубину напрямую — сначала потолок decimal (~7.9e28), затем потолок double (1.8e308).
    /// Расширенная экспонента снимает и его. Точность позиции по-прежнему обеспечивают
    /// <see cref="CenterXExact"/>/<see cref="CenterYExact"/>, а не зум.
    /// </summary>
    [JsonConverter(typeof(FloatExpJsonConverter))]
    public FloatExp Zoom { get; set; } = FloatExp.One;
    public int Iterations { get; set; } = 500;
    public decimal Threshold { get; set; } = 2;
    [JsonIgnore]
    public int Threads { get; set; }
    public MandelbrotColoringMode ColoringMode { get; set; } = MandelbrotColoringMode.Smooth;
    public string PaletteName { get; set; } = string.Empty;
    public MandelbrotPalette Palette { get; set; } = new();
    public HybridFormulaSettings Hybrid { get; set; } = new();
    public decimal Power { get; set; } = 2;
    public bool UseInversion { get; set; }
    public decimal JuliaCReal { get; set; }
    public decimal JuliaCImaginary { get; set; }
    public double HistogramContrast { get; set; } = 1;
    public bool HistogramEnabledEqualization { get; set; } = true;
    public bool HistogramInputUseSmooth { get; set; } = true;
    public double SmoothBlendPower { get; set; } = 1;
    public double SmoothIterationOffset { get; set; }
    public double PalettePhaseOffset { get; set; }
    public double PaletteScale { get; set; } = 1;
    public MandelbrotPaletteWrapMode PaletteWrapMode { get; set; }
    public bool UseCustomInteriorColor { get; set; }
    public Color InteriorColor { get; set; } = MediaColors.Black;
    public double OrbitTrapStrength { get; set; } = 1;
    public double OrbitTrapBias { get; set; }
    public double StripeFrequency { get; set; } = 3;
    public double StripeStrength { get; set; } = 0.5;
    public double StripeBias { get; set; }
    public double PolynomialA { get; set; } = 9;
    public double PolynomialB { get; set; } = 15;
    public double PolynomialC { get; set; } = 8.5;
    public double PolynomialGamma { get; set; } = 1;
    public double PolynomialBlend { get; set; } = 1;
    public double PolynomialBias { get; set; }
    public double DistanceReliefStrength { get; set; } = 1.35;
    public double DistanceLightAzimuth { get; set; } = 135;
    public double DistanceLightElevation { get; set; } = 45;
    public double DistanceAmbient { get; set; } = 0.28;
    public double DistanceDiffuse { get; set; } = 0.9;
    public double DistanceSpecular { get; set; } = 0.3;
    public double DistanceShininess { get; set; } = 32;
    public bool DistanceContoursEnabled { get; set; } = true;
    public double DistanceContourSpacing { get; set; } = 12;
    public double DistanceContourStrength { get; set; } = 0.45;
}
