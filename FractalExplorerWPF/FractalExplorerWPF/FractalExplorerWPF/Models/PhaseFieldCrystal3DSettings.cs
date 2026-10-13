using System.Text.Json.Serialization;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Models;

public enum PhaseFieldCrystal3DSeed { Bcc, Nucleus, TwoGrains, GrainBoundary, Noise, Layers, Columns, Liquid }
public enum PhaseFieldCrystal3DView { Peaks, Troughs, Modulation }
public enum PhaseFieldCrystal3DBrush { Crystal, Melt }

/// <summary>Lossless immutable signed density checkpoint and its conserved zero mode.</summary>
[JsonConverter(typeof(PhaseFieldCrystal3DFieldConverter))]
public sealed class PhaseFieldCrystal3DField
{
    public int Size { get; }
    public long Step { get; }
    public double Mean { get; }
    internal float[] Values { get; }
    public ReadOnlySpan<float> Density => Values;
    public PhaseFieldCrystal3DField(int size, long step, ReadOnlySpan<float> values, double? mean = null)
        : this(size, step, values.ToArray(), mean, true) { }
    internal PhaseFieldCrystal3DField(int size, long step, float[] values, double? mean, bool takeOwnership)
    {
        if (!PhaseFieldCrystal3DSettings.IsSupportedSize(size) || step < 0 || values.Length != size * size * size ||
            values.Any(v => !float.IsFinite(v) || Math.Abs(v) > 3))
            throw new ArgumentException("Некорректное поле кристалла 3D.");
        double average = values.Average(v => (double)v), mass = mean ?? average;
        if (!double.IsFinite(mass) || Math.Abs(mass) > .6 || Math.Abs(mass - average) > 1e-4)
            throw new ArgumentException("Нарушена средняя плотность поля кристалла 3D.");
        Size = size; Step = step; Mean = mass; Values = takeOwnership ? values : (float[])values.Clone();
    }
}

/// <summary>∂t ψ = M ∇²[(-ε+(q₀²+∇²)²)ψ+ψ³], periodic one-mode PFC model.</summary>
public sealed record PhaseFieldCrystal3DSettings : IGpuSimulationSettings
{
    public int Size { get; init; } = 64;
    public double Undercooling { get; init; } = .35;
    public double Mean { get; init; } = -.22;
    public double WaveNumber { get; init; } = 1;
    public double Spacing { get; init; } = 1;
    public double Mobility { get; init; } = 1;
    public double TimeStep { get; init; } = .025;
    public PhaseFieldCrystal3DSeed SeedShape { get; init; }
    public int Seed { get; init; } = 937;
    public double Noise { get; init; } = .005;
    public int WarmupSteps { get; init; } = 800;
    public int StepsPerFrame { get; init; } = 4;
    public PhaseFieldCrystal3DView View { get; init; }
    public double DisplayRange { get; init; } = 1.5;
    public double Threshold { get; init; } = .35;
    public int CutAxis { get; init; } = 3;
    public double CutPosition { get; init; } = .45;
    public PhaseFieldCrystal3DField? Field { get; init; }
    [JsonIgnore] public PhaseFieldCrystal3DVolume? Live { get; init; }
    public static bool IsSupportedSize(int size) => size is 32 or 64 or 128;
    public void Validate()
    {
        static bool Range(double v, double lo, double hi) => double.IsFinite(v) && v >= lo && v <= hi;
        if (!IsSupportedSize(Size) || !Range(Undercooling, -.2, .6) || !Range(Mean, -.6, .6) ||
            !Range(WaveNumber, .6, 1.4) || !Range(Spacing, .5, 1.5) || WaveNumber * Spacing > 1.2 + 1e-12 ||
            !Range(Mobility, .1, 2) || !Range(TimeStep, .002, .03) || Mobility * TimeStep > .03 + 1e-12 ||
            !Range(Noise, 0, .2) || !Enum.IsDefined(SeedShape) || !Enum.IsDefined(View) || WarmupSteps is < 0 or > 6000 ||
            StepsPerFrame is < 1 or > 64 || !Range(DisplayRange, .5, 3) || !Range(Threshold, .02, .95) ||
            CutAxis is < 0 or > 3 || !Range(CutPosition, -1, 1) ||
            Field is not null && (Field.Size != Size || Math.Abs(Field.Mean - Mean) > 1e-6) || Live is not null && Live.Size != Size)
            throw new InvalidOperationException("PFC: сетка 32, 64 или 128; ε −0,2…0,6; средняя плотность −0,6…0,6; M·Δt ≤ 0,03; q₀·Δx ≤ 1,2. Поле сохранения должно иметь ту же среднюю плотность.");
    }
    public bool SameEvolution(PhaseFieldCrystal3DSettings other) => Size == other.Size && Undercooling == other.Undercooling &&
        Mean == other.Mean && WaveNumber == other.WaveNumber && Spacing == other.Spacing && Mobility == other.Mobility &&
        TimeStep == other.TimeStep && SeedShape == other.SeedShape && Seed == other.Seed && Noise == other.Noise &&
        WarmupSteps == other.WarmupSteps && ReferenceEquals(Field, other.Field);
}
