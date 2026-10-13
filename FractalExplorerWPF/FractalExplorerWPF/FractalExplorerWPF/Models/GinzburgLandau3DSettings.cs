using System.Text.Json.Serialization;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Models;

public enum GinzburgLandau3DSeed { Ring, Vortex, VortexPair, Random, PlaneWave, Uniform }
public enum GinzburgLandau3DBoundary { NoFlux, Periodic }
public enum GinzburgLandau3DView { Filaments, AmplitudeDefects, Amplitude }
public enum GinzburgLandau3DBrush { Suppress, PhaseFlip }

/// <summary>Lossless immutable checkpoint: interleaved Re(A), Im(A), without phase wrapping.</summary>
[JsonConverter(typeof(GinzburgLandau3DFieldConverter))]
public sealed class GinzburgLandau3DField
{
    public int Size { get; }
    public long Step { get; }
    internal float[] Values { get; }
    public ReadOnlySpan<float> Components => Values;

    public GinzburgLandau3DField(int size, long step, ReadOnlySpan<float> values)
        : this(size, step, values.ToArray(), true) { }

    internal GinzburgLandau3DField(int size, long step, float[] values, bool takeOwnership)
    {
        if (size is < 32 or > 128 || step < 0 || values.Length != size * size * size * 2 ||
            values.Any(v => !float.IsFinite(v) || Math.Abs(v) > 100))
            throw new ArgumentException("Некорректное комплексное поле Гинзбурга–Ландау 3D.");
        Size = size; Step = step; Values = takeOwnership ? values : (float[])values.Clone();
    }
}

/// <summary>∂t A = A + (1+i c₁)∇²A − (1+i c₃)|A|²A in a cubic physical domain.</summary>
public sealed record GinzburgLandau3DSettings : IGpuSimulationSettings
{
    public int Size { get; init; } = 64;
    public double C1 { get; init; } = .5;
    public double C3 { get; init; } = 1;
    public double Spacing { get; init; } = 1;
    public double TimeStep { get; init; } = .02;
    public GinzburgLandau3DBoundary Boundary { get; init; }
    public GinzburgLandau3DSeed SeedShape { get; init; }
    public int Seed { get; init; } = 712;
    public int WarmupSteps { get; init; } = 400;
    public int StepsPerFrame { get; init; } = 8;
    public GinzburgLandau3DView View { get; init; }
    public double Threshold { get; init; } = .15;
    public int CutAxis { get; init; }
    public double CutPosition { get; init; }
    public GinzburgLandau3DField? Field { get; init; }
    [JsonIgnore] public GinzburgLandau3DVolume? Live { get; init; }

    public void Validate()
    {
        static bool Range(double v, double lo, double hi) => double.IsFinite(v) && v >= lo && v <= hi;
        // Every eigenvalue of the seven-point Laplacian lies in [-12/h²,0].
        // Forward diffusion is contractive if dt*(1+c1²)/h² <= 1/6.
        // Local growth and cubic damping are integrated exactly, without clipping the field.
        if (Size is < 32 or > 128 || !Range(C1, -3, 3) || !Range(C3, -3, 3) ||
            !Range(Spacing, .5, 2) || !Range(TimeStep, .0005, .05) ||
            TimeStep * (1 + C1 * C1) / (Spacing * Spacing) > .15 + 1e-12 ||
            !Enum.IsDefined(Boundary) || !Enum.IsDefined(SeedShape) || !Enum.IsDefined(View) ||
            WarmupSteps is < 0 or > 10000 || StepsPerFrame is < 1 or > 128 ||
            !Range(Threshold, .02, .95) || CutAxis is < 0 or > 3 || !Range(CutPosition, -1, 1) ||
            Field is not null && Field.Size != Size || Live is not null && Live.Size != Size)
            throw new InvalidOperationException("Проверьте параметры Гинзбурга–Ландау: сетка 32–128, c₁/c₃ от −3 до 3; Δt·(1+c₁²)/Δx² ≤ 0,15. При большой дисперсии уменьшите шаг времени.");
    }

    public bool SameEvolution(GinzburgLandau3DSettings other) => Size == other.Size && C1 == other.C1 &&
        C3 == other.C3 && Spacing == other.Spacing && TimeStep == other.TimeStep &&
        Boundary == other.Boundary && SeedShape == other.SeedShape && Seed == other.Seed &&
        WarmupSteps == other.WarmupSteps && ReferenceEquals(Field, other.Field);
}
