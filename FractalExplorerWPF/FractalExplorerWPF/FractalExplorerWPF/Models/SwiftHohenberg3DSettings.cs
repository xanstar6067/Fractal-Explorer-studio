using System.Text.Json.Serialization;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Models;

public enum SwiftHohenberg3DSeed { Noise, Layers, TiltedLayers, HexagonalColumns, CubicSpots, Localized, Uniform }
public enum SwiftHohenberg3DView { Positive, Negative, Interfaces }

/// <summary>Immutable signed scalar checkpoint. The mean is free to evolve.</summary>
[JsonConverter(typeof(SwiftHohenberg3DFieldConverter))]
public sealed class SwiftHohenberg3DField
{
    public int Size { get; }
    public long Step { get; }
    internal float[] Values { get; }
    public ReadOnlySpan<float> Samples => Values;

    public SwiftHohenberg3DField(int size, long step, ReadOnlySpan<float> values)
        : this(size, step, values.ToArray(), true) { }

    internal SwiftHohenberg3DField(int size, long step, float[] values, bool takeOwnership)
    {
        if (!SwiftHohenberg3DSettings.IsSupportedSize(size) || step < 0 || values.Length != size * size * size ||
            values.Any(v => !float.IsFinite(v) || Math.Abs(v) > 3))
            throw new ArgumentException("Некорректное поле Свифта–Хоэнберга 3D.");
        Size = size; Step = step; Values = takeOwnership ? values : (float[])values.Clone();
    }
}

/// <summary>∂t φ = rφ − (q₀²+∇²)²φ + bφ² − φ³, with periodic boundaries.</summary>
public sealed record SwiftHohenberg3DSettings : IGpuSimulationSettings
{
    public int Size { get; init; } = 64;
    public double R { get; init; } = .35;
    public double Quadratic { get; init; }
    public double WaveNumber { get; init; } = 1;
    public double Spacing { get; init; } = 1;
    public double TimeStep { get; init; } = .05;
    public SwiftHohenberg3DSeed SeedShape { get; init; }
    public int Seed { get; init; } = 937;
    public double Noise { get; init; } = .12;
    public int WarmupSteps { get; init; } = 500;
    public int StepsPerFrame { get; init; } = 4;
    public SwiftHohenberg3DView View { get; init; }
    public double DisplayRange { get; init; } = 1.5;
    public double Threshold { get; init; } = .15;
    public int CutAxis { get; init; } = 3;
    public double CutPosition { get; init; } = .45;
    public SwiftHohenberg3DField? Field { get; init; }
    [JsonIgnore] public SwiftHohenberg3DVolume? Live { get; init; }

    public static bool IsSupportedSize(int size) => size is 32 or 64 or 128;

    public void Validate()
    {
        static bool Range(double v, double lo, double hi) => double.IsFinite(v) && v >= lo && v <= hi;
        // Strict |integer frequency| < N/4 permits alias-free projection of cubic products.
        // Keep the preferred wave number comfortably inside this Galerkin band, also at N=32.
        if (!IsSupportedSize(Size) || !Range(R, -.5, 1) || !Range(Quadratic, -1.5, 1.5) ||
            !Range(WaveNumber, .4, 1.5) || !Range(Spacing, .5, 2) || WaveNumber * Spacing > 1.2 + 1e-12 ||
            !Range(TimeStep, .002, .05) || !Range(Noise, 0, .3) || !Enum.IsDefined(SeedShape) || !Enum.IsDefined(View) ||
            WarmupSteps is < 0 or > 5000 || StepsPerFrame is < 1 or > 64 || !Range(DisplayRange, .5, 3) ||
            !Range(Threshold, .02, .95) || CutAxis is < 0 or > 3 || !Range(CutPosition, -1, 1) ||
            Field is not null && Field.Size != Size || Live is not null && Live.Size != Size)
            throw new InvalidOperationException("Свифт–Хоэнберг: сетка 32, 64 или 128; r от −0,5 до 1, b от −1,5 до 1,5; Δt 0,002–0,05; q₀·Δx ≤ 1,2. Уменьшите Δx для коротких волн.");
    }

    public bool SameEvolution(SwiftHohenberg3DSettings other) => Size == other.Size && R == other.R &&
        Quadratic == other.Quadratic && WaveNumber == other.WaveNumber && Spacing == other.Spacing &&
        TimeStep == other.TimeStep && SeedShape == other.SeedShape && Seed == other.Seed && Noise == other.Noise &&
        WarmupSteps == other.WarmupSteps && ReferenceEquals(Field, other.Field);
}
