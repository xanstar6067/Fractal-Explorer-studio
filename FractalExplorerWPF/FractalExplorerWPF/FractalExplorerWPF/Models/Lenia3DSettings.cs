using System.Text.Json.Serialization;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Models;

public enum Lenia3DSeed { RotatingCube, PulsatingSphere, JumpingPair, Torus, RotatingOctahedron, RandomCloud, Empty }
public enum LeniaGrowth { Polynomial, Gaussian }
public sealed record LeniaSearchProgress(int Checked, int Total, bool Finalizing);
public sealed record LeniaSearchResult(Lenia3DSettings Settings, int Checked, double Activity);

[JsonConverter(typeof(Lenia3DFieldConverter))]
public sealed class Lenia3DField
{
    public int Size { get; }
    public long Step { get; }
    public double Time { get; }
    internal float[] Values { get; }
    public ReadOnlySpan<float> Cells => Values;
    public Lenia3DField(int size, long step, ReadOnlySpan<float> values, double time = 0)
        : this(size, step, values.ToArray(), time, true) { }
    internal Lenia3DField(int size, long step, float[] values, double time, bool takeOwnership)
    {
        if (!Lenia3DSettings.IsSupportedSize(size) || step < 0 || !double.IsFinite(time) || time < 0 ||
            values.Length != size*size*size || values.Any(v => !float.IsFinite(v) || v < 0 || v > 1))
            throw new ArgumentException("Некорректное поле Lenia 3D.");
        Size = size; Step = step; Time = time; Values = takeOwnership ? values : (float[])values.Clone();
    }
}

public sealed record Lenia3DSettings
{
    public int Size { get; init; } = 64;
    public double Radius { get; init; } = 18;
    public int ShellCount { get; init; } = 2;
    public double Beta1 { get; init; } = 1;
    public double Beta2 { get; init; } = 5.0/12;
    public double Beta3 { get; init; }
    public double Beta4 { get; init; }
    public double GrowthMean { get; init; } = .17;
    public double GrowthWidth { get; init; } = .014;
    public LeniaGrowth Growth { get; init; }
    public double TimeStep { get; init; } = .1;
    public Lenia3DSeed SeedShape { get; init; }
    public int Seed { get; init; } = 42;
    public double SeedNoise { get; init; }
    public int WarmupSteps { get; init; } = 60;
    public int StepsPerFrame { get; init; } = 2;
    public double Threshold { get; init; } = .2;
    public int CutAxis { get; init; } = 3;
    public double CutPosition { get; init; } = .05;
    public Lenia3DField? Field { get; init; }
    [JsonIgnore] public Lenia3DVolume? Live { get; init; }
    [JsonIgnore] public double[] Shells => new[] { Beta1, Beta2, Beta3, Beta4 }[..Math.Clamp(ShellCount,1,4)];

    public static bool IsSupportedSize(int n) => n is 32 or 64 or 128;
    public void Validate()
    {
        static bool In(double x, double a, double b) => double.IsFinite(x) && x >= a && x <= b;
        if (!IsSupportedSize(Size) || !In(Radius,2,Size/2.0-1) || ShellCount is < 1 or > 4 ||
            !In(Beta1,0,1) || !In(Beta2,0,1) || !In(Beta3,0,1) || !In(Beta4,0,1) || Shells.Sum() <= 0 ||
            !In(GrowthMean,.01,.5) || !In(GrowthWidth,.002,.15) || !Enum.IsDefined(Growth) ||
            !In(TimeStep,.01,.2) || !Enum.IsDefined(SeedShape) || !In(SeedNoise,0,.15) ||
            WarmupSteps is < 0 or > 3000 || StepsPerFrame is < 1 or > 32 || !In(Threshold,.01,.9) ||
            CutAxis is < 0 or > 3 || !In(CutPosition,-1,1) || Field is not null && Field.Size != Size || Live is not null && Live.Size != Size)
            throw new ArgumentException("Lenia: сетка 32/64/128, радиус 2…N/2−1, 1–4 оболочки с ненулевым весом, μ 0,01–0,5, σ 0,002–0,15, Δt 0,01–0,2.");
    }
    public bool SameEvolution(Lenia3DSettings s) => Size == s.Size && Radius == s.Radius && ShellCount == s.ShellCount &&
        Beta1 == s.Beta1 && Beta2 == s.Beta2 && Beta3 == s.Beta3 && Beta4 == s.Beta4 && GrowthMean == s.GrowthMean &&
        GrowthWidth == s.GrowthWidth && Growth == s.Growth && TimeStep == s.TimeStep && SeedShape == s.SeedShape &&
        Seed == s.Seed && SeedNoise == s.SeedNoise && WarmupSteps == s.WarmupSteps && ReferenceEquals(Field,s.Field);
}
