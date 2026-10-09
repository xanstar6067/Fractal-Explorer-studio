using System.Text.Json.Serialization;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Models;

public enum Kobayashi3DSeed { Sphere, EightSpheres, Ring, Empty }

/// <summary>Immutable, lossless solid fraction / dimensionless temperature checkpoint.</summary>
[JsonConverter(typeof(Kobayashi3DFieldConverter))]
public sealed class Kobayashi3DField
{
    public int Size { get; }
    public long Step { get; }
    internal float[] Values { get; }
    public ReadOnlySpan<float> Concentrations => Values;
    public Kobayashi3DField(int size, long step, ReadOnlySpan<float> values)
        : this(size, step, values.ToArray(), true) { }
    internal Kobayashi3DField(int size, long step, float[] values, bool takeOwnership)
    {
        if (size is < 32 or > 128 || step < 0 || values.Length != size * size * size * 2)
            throw new ArgumentException("Некорректный размер поля Кобаяси.");
        for (int i = 0; i < values.Length; i += 2)
            if (!(values[i] >= 0 && values[i] <= 1) || !float.IsFinite(values[i + 1]))
                throw new ArgumentException("Некорректные фаза или температура поля Кобаяси.");
        Size = size; Step = step; Values = takeOwnership ? values : (float[])values.Clone();
    }
}

/// <summary>Kobayashi kinetics with cubic 3D gradient energy; cell spacing is one.</summary>
public sealed record Kobayashi3DSettings
{
    public int Size { get; init; } = 64;
    public double InterfaceWidth { get; init; } = .6;
    public double Anisotropy { get; init; } = .055;
    public double Mobility { get; init; } = 1;
    public double ThermalDiffusion { get; init; } = .5;
    public double LatentHeat { get; init; } = 1.8;
    public double Undercooling { get; init; } = .5;
    public double Noise { get; init; } = .02;
    public double TimeStep { get; init; } = .04;
    public int WarmupSteps { get; init; } = 6000;
    public Kobayashi3DSeed SeedShape { get; init; } = Kobayashi3DSeed.Sphere;
    public int Seed { get; init; } = 42;
    public int StepsPerFrame { get; init; } = 32;
    public double Threshold { get; init; } = .5;
    public int CutAxis { get; init; }
    public double CutPosition { get; init; }
    public Kobayashi3DField? Field { get; init; }
    [JsonIgnore] public Kobayashi3DVolume? Live { get; init; }
    [JsonIgnore] public int InitialSteps => SeedShape == Kobayashi3DSeed.Empty ? 0 : WarmupSteps;
    // Conservative explicit CFL bound includes the orientation derivative of the energy.
    [JsonIgnore] public double EffectiveTimeStep => Math.Min(TimeStep,
        .12 / Math.Max(ThermalDiffusion, Mobility * (1 + 40 * Math.Abs(Anisotropy)) * InterfaceWidth * InterfaceWidth));
    public void Validate()
    {
        if (Size is < 32 or > 128 || StepsPerFrame is < 1 or > 256 || WarmupSteps is < 0 or > 20000 ||
            !In(InterfaceWidth, .5, 2) || !In(Anisotropy, -.06, .06) || !In(Mobility, .1, 4) ||
            !In(ThermalDiffusion, .1, 4) || !In(LatentHeat, 0, 3) || !In(Undercooling, .05, 1.5) ||
            !In(Noise, 0, .1) || !In(TimeStep, .001, .1) || !In(Threshold, .05, .95) ||
            !In(CutPosition, -1, 1) || CutAxis is < 0 or > 3 || !Enum.IsDefined(SeedShape) ||
            Field is not null && Field.Size != Size || Live is not null && Live.Size != Size)
            throw new InvalidOperationException("Проверьте параметры фазового поля Кобаяси и сетку 32–128.");
    }
    private static bool In(double v, double min, double max) => double.IsFinite(v) && v >= min && v <= max;
    public bool SameEvolution(Kobayashi3DSettings s) => Size == s.Size && InterfaceWidth == s.InterfaceWidth &&
        Anisotropy == s.Anisotropy && Mobility == s.Mobility && ThermalDiffusion == s.ThermalDiffusion &&
        LatentHeat == s.LatentHeat && Undercooling == s.Undercooling && Noise == s.Noise && TimeStep == s.TimeStep &&
        Seed == s.Seed && SeedShape == s.SeedShape && WarmupSteps == s.WarmupSteps && ReferenceEquals(Field, s.Field);
}
