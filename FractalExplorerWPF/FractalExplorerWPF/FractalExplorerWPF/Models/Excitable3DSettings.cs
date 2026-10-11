using System.Text.Json.Serialization;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Models;

public enum Excitable3DModel { Barkley, Oregonator, FitzHughNagumo, AlievPanfilov }
public enum Excitable3DSeed { Scroll, Ring, CrossedScrolls, Sphere, Rest }
public enum Excitable3DView { Waves, Filaments, WavesAndFilaments }
public enum Excitable3DBoundary { NoFlux, Periodic }

/// <summary>Immutable, lossless two-field checkpoint. FHN fields are signed.</summary>
[JsonConverter(typeof(Excitable3DFieldConverter))]
public sealed class Excitable3DField
{
    public int Size { get; }
    public long Step { get; }
    internal float[] Values { get; }
    public ReadOnlySpan<float> Concentrations => Values;
    public Excitable3DField(int size, long step, ReadOnlySpan<float> values)
        : this(size, step, values.ToArray(), true) { }
    internal Excitable3DField(int size, long step, float[] values, bool takeOwnership)
    {
        if (size is < 32 or > 128 || step < 0 || values.Length != size * size * size * 2 ||
            values.Any(v => !float.IsFinite(v) || Math.Abs(v) > 100))
            throw new ArgumentException("Некорректное поле возбудимой среды 3D.");
        Size = size; Step = step; Values = takeOwnership ? values : (float[])values.Clone();
    }
}

public sealed record Excitable3DSettings
{
    public Excitable3DModel Model { get; init; }
    public int Size { get; init; } = 64;
    /// <summary>Barkley: a/b; Oregonator: f/q; FHN: beta/gamma; AP: a/k.</summary>
    public double A { get; init; } = .75;
    public double B { get; init; } = .06;
    public double Epsilon { get; init; } = .02;
    public double Mu1 { get; init; } = .2;
    public double Mu2 { get; init; } = .3;
    public double DiffusionU { get; init; } = 1;
    public double DiffusionV { get; init; }
    public double Spacing { get; init; } = .75;
    public double TimeStep { get; init; } = .005;
    public Excitable3DBoundary Boundary { get; init; }
    public Excitable3DSeed SeedShape { get; init; }
    public int WarmupSteps { get; init; } = 3600;
    public int StepsPerFrame { get; init; } = 32;
    public double Threshold { get; init; } = .5;
    public Excitable3DView View { get; init; }
    public int CutAxis { get; init; }
    public double CutPosition { get; init; }
    public Excitable3DField? Field { get; init; }
    [JsonIgnore] public Excitable3DVolume? Live { get; init; }
    [JsonIgnore] public int InitialSteps => SeedShape == Excitable3DSeed.Rest ? 0 : WarmupSteps;
    [JsonIgnore] public string Name => Model switch
    {
        Excitable3DModel.Oregonator => "Oregonator · Белоусов–Жаботинский",
        Excitable3DModel.FitzHughNagumo => "FitzHugh–Nagumo",
        Excitable3DModel.AlievPanfilov => "Aliev–Panfilov",
        _ => "Баркли"
    };

    public static Excitable3DSettings Default(Excitable3DModel model) => model switch
    {
        Excitable3DModel.Oregonator => new() { Model = model, A = 1.4, B = .002, Epsilon = .05, WarmupSteps = 2400 },
        Excitable3DModel.FitzHughNagumo => new() { Model = model, A = .58, B = .8, Epsilon = .22, TimeStep = .01, WarmupSteps = 2400 },
        Excitable3DModel.AlievPanfilov => new() { Model = model, A = .1, B = 8, Epsilon = .01, TimeStep = .02, WarmupSteps = 1800 },
        _ => new()
    };

    public void Validate()
    {
        static bool R(double v, double lo, double hi) => double.IsFinite(v) && v >= lo && v <= hi;
        bool reaction = Model switch
        {
            Excitable3DModel.Barkley => R(A, .4, 1.2) && R(B, .001, .2),
            Excitable3DModel.Oregonator => R(A, .5, 3) && R(B, .0001, .02),
            Excitable3DModel.FitzHughNagumo => R(A, .4, 1.2) && R(B, .5, 1.5),
            Excitable3DModel.AlievPanfilov => R(A, .01, .3) && R(B, 1, 12),
            _ => false
        };
        // Conservative explicit-Euler bounds for reaction stiffness and the 3D diffusion CFL.
        double reactionDt = Model == Excitable3DModel.AlievPanfilov ? .02 : .25 * Epsilon;
        if (!reaction || !Enum.IsDefined(SeedShape) || !Enum.IsDefined(Boundary) || !Enum.IsDefined(View) ||
            Size is < 32 or > 128 || WarmupSteps is < 0 or > 20000 || StepsPerFrame is < 1 or > 256 ||
            !R(Epsilon, .005, .5) || !R(Mu1, .01, .5) || !R(Mu2, .1, 1) ||
            !R(DiffusionU, .01, 2) || !R(DiffusionV, 0, 1) || !R(Spacing, .25, 2) ||
            !R(TimeStep, .0001, .02) || TimeStep > reactionDt + 1e-12 ||
            TimeStep * Math.Max(DiffusionU, DiffusionV) / (Spacing * Spacing) > .15 ||
            !R(Threshold, .05, .95) || CutAxis is < 0 or > 3 || !R(CutPosition, -1, 1) ||
            Field is not null && Field.Size != Size || Live is not null && Live.Size != Size)
            throw new InvalidOperationException("Проверьте параметры среды: сетка 32–128, шаг времени 0,0001–0,02; уменьшите шаг при малом ε или мелкой пространственной сетке.");
    }

    public bool SameEvolution(Excitable3DSettings o) => Model == o.Model && Size == o.Size &&
        A == o.A && B == o.B && Epsilon == o.Epsilon && Mu1 == o.Mu1 && Mu2 == o.Mu2 &&
        DiffusionU == o.DiffusionU && DiffusionV == o.DiffusionV && Spacing == o.Spacing &&
        TimeStep == o.TimeStep && Boundary == o.Boundary && SeedShape == o.SeedShape &&
        WarmupSteps == o.WarmupSteps && ReferenceEquals(Field, o.Field);
}
