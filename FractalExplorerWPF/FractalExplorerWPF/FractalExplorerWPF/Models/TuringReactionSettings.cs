namespace FractalExplorerWPF.Models;

public enum TuringReactionModel { McCabe, Brusselator, Schnakenberg, GiererMeinhardt }

/// <summary>Two-species kinetics. Diffusivities use reference-grid cells squared per unit time.</summary>
public sealed record TuringReactionSettings
{
    public TuringReactionModel Model { get; init; }
    public double A { get; init; } = 1;
    public double B { get; init; } = 2.8;
    public double DiffusionU { get; init; } = 1;
    public double DiffusionV { get; init; } = 16;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsClassical => Model != TuringReactionModel.McCabe;
    [System.Text.Json.Serialization.JsonIgnore]
    public string Name => Model switch
    {
        TuringReactionModel.Brusselator => "Брюсселятор",
        TuringReactionModel.Schnakenberg => "Шнакенберг",
        TuringReactionModel.GiererMeinhardt => "Гирер–Мейнхардт",
        _ => "Маккейб — многомасштабная"
    };

    public static TuringReactionSettings Default(TuringReactionModel model) => model switch
    {
        TuringReactionModel.Schnakenberg => new() { Model = model, A = .1, B = .9, DiffusionU = 1, DiffusionV = 20 },
        TuringReactionModel.GiererMeinhardt => new() { Model = model, A = .02, B = 2, DiffusionU = .25, DiffusionV = 8 },
        _ => new() { Model = model, A = 2, B = 4.8, DiffusionU = 1, DiffusionV = 16 }
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public (double U, double V) Equilibrium => Model switch
    {
        TuringReactionModel.Schnakenberg => (A + B, B / ((A + B) * (A + B))),
        TuringReactionModel.GiererMeinhardt => (A + B, (A + B) * (A + B) / B),
        _ => (A, B / A)
    };

    public void Validate()
    {
        static bool Range(double x, double lo, double hi) => double.IsFinite(x) && x >= lo && x <= hi;
        if (!Enum.IsDefined(Model) || !Range(A, .001, 5) || !Range(B, .01, 8) ||
            !Range(DiffusionU, .01, 4) || !Range(DiffusionV, .01, 40))
            throw new ArgumentException("Реакция: A = 0,001–5, B = 0,01–8; диффузия U = 0,01–4, V = 0,01–40.");
        if (IsClassical && (Equilibrium.U > 1000 || Equilibrium.V > 1000))
            throw new ArgumentException("Равновесная концентрация слишком велика: увеличьте A или уменьшите B.");
    }

    /// <summary>A displayed step is 0.5 time units at rate 1. Substeps satisfy the explicit diffusion CFL bound.</summary>
    public (int Count, double Dt, double DiffusionScale) Integration(int size, int referenceSize, double detail, double rate, int dimensions)
    {
        double scale = Math.Pow(detail * size / referenceSize, 2);
        double maxDt = Math.Min(.01, .4 / (dimensions * Math.Max(DiffusionU, DiffusionV) * scale));
        int count = (int)Math.Ceiling(.5 * rate / maxDt);
        return (count, .5 * rate / count, scale);
    }
}
