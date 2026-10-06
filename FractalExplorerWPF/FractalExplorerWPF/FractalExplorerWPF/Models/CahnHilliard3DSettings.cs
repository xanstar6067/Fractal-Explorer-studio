using System.Text.Json.Serialization;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Models;

public enum CahnHilliard3DSeed { Noise, Gyroid, Layers }

/// <summary>Immutable, lossless checkpoint, including the conserved zero Fourier mode.</summary>
[JsonConverter(typeof(CahnHilliard3DFieldConverter))]
public sealed class CahnHilliard3DField
{
    public int Size { get; }
    public long Step { get; }
    public double Time { get; }
    public double Mean { get; }
    internal float[] Values { get; }
    public ReadOnlySpan<float> Concentrations => Values;

    public CahnHilliard3DField(int size, long step, ReadOnlySpan<float> values, double time = 0, double? mean = null)
        : this(size, step, values.ToArray(), time, mean, true) { }

    internal CahnHilliard3DField(int size, long step, float[] values, double time, double? mean, bool takeOwnership)
    {
        if (!CahnHilliard3DSettings.IsSupportedSize(size) || step < 0 || !double.IsFinite(time) || time < 0 ||
            values.Length != size * size * size || values.Any(v => !float.IsFinite(v) || Math.Abs(v) > 4))
            throw new ArgumentException("Некорректное поле Кана–Хиллиарда 3D.");
        double average = values.Average(v => (double)v);
        double mass = mean ?? average;
        if (!double.IsFinite(mass) || Math.Abs(mass) > 1 || Math.Abs(mass - average) > 1e-4)
            throw new ArgumentException("Нарушен средний состав поля Кана–Хиллиарда 3D.");
        Size = size; Step = step; Time = time; Mean = mass;
        Values = takeOwnership ? values : (float[])values.Clone();
    }
}

public sealed record CahnHilliard3DSettings
{
    public int Size { get; init; } = 64;
    public double Mean { get; init; }
    public double Noise { get; init; } = .08;
    public double Kappa { get; init; } = 1;
    public double Mobility { get; init; } = 1;
    public double TimeStep { get; init; } = 1;
    public CahnHilliard3DSeed SeedShape { get; init; }
    public int Seed { get; init; } = 42;
    public int WarmupSteps { get; init; } = 450;
    public int StepsPerFrame { get; init; } = 4;
    public double Level { get; init; }
    /// <summary>Render the other phase without modifying the conserved field.</summary>
    public bool Invert { get; init; }
    public int CutAxis { get; init; } = 3;
    public double CutPosition { get; init; } = .55;
    public CahnHilliard3DField? Field { get; init; }
    [JsonIgnore] public CahnHilliard3DVolume? Live { get; init; }

    public static bool IsSupportedSize(int size) => size is 32 or 64 or 128;

    public void Validate()
    {
        if (!IsSupportedSize(Size) || !double.IsFinite(Mean) || Mean is < -.55 or > .55 ||
            !double.IsFinite(Noise) || Noise is < .001 or > .2 || !double.IsFinite(Kappa) || Kappa is < .5 or > 8 ||
            !double.IsFinite(Mobility) || Mobility is < .01 or > 2 || !double.IsFinite(TimeStep) || TimeStep is < .01 or > 2 ||
            TimeStep * Mobility > 2 || WarmupSteps is < 0 or > 5000 || StepsPerFrame is < 1 or > 64 ||
            !double.IsFinite(Level) || Level is < -.9 or > .9 || CutAxis is < 0 or > 3 ||
            !double.IsFinite(CutPosition) || CutPosition is < -1 or > 1 || !Enum.IsDefined(SeedShape) ||
            Field is not null && Field.Size != Size || Live is not null && Live.Size != Size)
            throw new InvalidOperationException("Кан–Хиллиард: сетка 32, 64 или 128; состав −0,55…0,55; κ 0,5–8; M·Δt ≤ 2.");
    }

    public bool SameEvolution(CahnHilliard3DSettings other) => Size == other.Size && Mean == other.Mean &&
        Noise == other.Noise && Kappa == other.Kappa && Mobility == other.Mobility && TimeStep == other.TimeStep &&
        SeedShape == other.SeedShape && Seed == other.Seed && WarmupSteps == other.WarmupSteps && ReferenceEquals(Field, other.Field);
}
