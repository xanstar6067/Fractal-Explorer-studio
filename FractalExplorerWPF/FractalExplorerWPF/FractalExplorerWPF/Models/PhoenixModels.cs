using System.Text.Json.Serialization;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Models;

public enum PhoenixPlaneMode
{
    Julia,
    ParameterC1
}

public enum PhoenixVariant
{
    Classic,
    Tricorn,
    BurningShip,
    Celtic,
    Buffalo
}

public enum PhoenixColoringMode
{
    Discrete,
    Smooth,
    OrbitTrap,
    StripeAverage,
    TriangleInequalityAverage,
    FinalArgument,
    Period
}

public enum PhoenixOrbitTrapMode
{
    Axes,
    Circle,
    Point
}

public enum PhoenixParameterPlane
{
    C1,
    C2
}

public sealed class PhoenixState
{
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string FractalType { get; set; } = "Phoenix";
    public decimal CenterX { get; set; }
    public decimal CenterY { get; set; }

    /// <summary>
    /// Центр области в произвольной точности — инвариантная строка, заполняется только на
    /// глубоком зуме (см. <c>PhoenixRenderer.DeepZoom</c>). У обычных сохранений null, и тогда
    /// центр берётся из <see cref="CenterX"/>/<see cref="CenterY"/>. За пределами decimal
    /// (28 знаков) только эти строки несут положение области.
    /// </summary>
    public string? CenterXExact { get; set; }

    /// <inheritdoc cref="CenterXExact"/>
    public string? CenterYExact { get; set; }

    /// <summary>
    /// Масштаб. <see cref="FloatExp"/>, а не double: сверхглубокий зум уходит за 1.8e308, где
    /// double уже не представим. В диапазоне double значение пишется в JSON обычным числом,
    /// поэтому прежние сохранения (где зум был decimal, затем double) читаются без изменений.
    /// </summary>
    [JsonConverter(typeof(FloatExpJsonConverter))]
    public FloatExp Zoom { get; set; } = FloatExp.One;
    public decimal Threshold { get; set; } = 4;
    public int Iterations { get; set; } = 100;
    public decimal C1Real { get; set; } = 0.56m;
    public decimal C1Imaginary { get; set; }
    public decimal C2Real { get; set; } = -0.5m;
    public decimal C2Imaginary { get; set; }
    public PhoenixPlaneMode PlaneMode { get; set; } = PhoenixPlaneMode.Julia;
    public PhoenixVariant Variant { get; set; } = PhoenixVariant.Classic;
    public int PrimaryPower { get; set; } = 2;
    public int SecondaryPower { get; set; }
    public decimal InitialZReal { get; set; }
    public decimal InitialZImaginary { get; set; }
    public decimal InitialPreviousReal { get; set; }
    public decimal InitialPreviousImaginary { get; set; }
    public PhoenixColoringMode ColoringMode { get; set; } = PhoenixColoringMode.Smooth;
    public PhoenixOrbitTrapMode OrbitTrapMode { get; set; } = PhoenixOrbitTrapMode.Axes;
    public double OrbitTrapRadius { get; set; } = 0.5;
    public double OrbitTrapStrength { get; set; } = 1.5;
    public double StripeFrequency { get; set; } = 3;
    public double StripeStrength { get; set; } = 0.65;
    public double CycleTolerance { get; set; } = 1e-7;
    public int MaximumDetectedPeriod { get; set; } = 32;
    public MandelbrotPalette Palette { get; set; } = new();
}

public sealed class PhoenixSliceRange
{
    public double MinX { get; set; } = -2;
    public double MaxX { get; set; } = 2;
    public double MinY { get; set; } = -2;
    public double MaxY { get; set; } = 2;

    public void Reset() => (MinX, MaxX, MinY, MaxY) = (-2, 2, -2, 2);
}
