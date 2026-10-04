namespace FractalExplorerWPF.Models;

/// <summary>The omitted coordinate after a rotation of (Re z, Im z, Re c, Im c).</summary>
public enum Buddhabrot4DProjection { HideCi, HideCr, HideZi, HideZr }

public sealed record Buddhabrot4DSettings
{
    public int SampleCount { get; set; } = 200_000;
    public int Seed { get; set; } = 42;
    public int MinIterations { get; set; } = 12;
    public int MaxIterations { get; set; } = 2000;
    public int RedLimit { get; set; } = 50;
    public int GreenLimit { get; set; } = 300;
    public int BlueLimit { get; set; } = 2000;
    public Buddhabrot4DProjection Projection { get; set; }
    public double ZrZi { get; set; }
    public double ZrCr { get; set; }
    public double ZrCi { get; set; }
    public double ZiCr { get; set; }
    public double ZiCi { get; set; }
    public double CrCi { get; set; }
    public double Exposure { get; set; } = 1.6;
    public double Gamma { get; set; } = 2.2;
    public double Density { get; set; } = .55;
    public double Saturation { get; set; } = 1;

    public bool SameSampling(Buddhabrot4DSettings other) =>
        SampleCount == other.SampleCount && Seed == other.Seed &&
        MinIterations == other.MinIterations && MaxIterations == other.MaxIterations;

    public bool SameVolume(Buddhabrot4DSettings other) => SameSampling(other) &&
        Projection == other.Projection && ZrZi == other.ZrZi && ZrCr == other.ZrCr &&
        ZrCi == other.ZrCi && ZiCr == other.ZiCr && ZiCi == other.ZiCi && CrCi == other.CrCi &&
        RedLimit == other.RedLimit && GreenLimit == other.GreenLimit && BlueLimit == other.BlueLimit;

    public void Validate()
    {
        if (SampleCount is < 1000 or > 2_000_000 || MinIterations < 1 ||
            MaxIterations is < 2 or > 10_000 || MinIterations >= MaxIterations ||
            RedLimit is < 1 or > 10_000 || GreenLimit is < 1 or > 10_000 || BlueLimit is < 1 or > 10_000)
            throw new ArgumentException("Затравки: 1 000–2 000 000. Пределы итераций: 1–10 000; минимум должен быть меньше максимума.");
        if (!Enum.IsDefined(Projection) || new[] { ZrZi, ZrCr, ZrCi, ZiCr, ZiCi, CrCi }
            .Any(a => !double.IsFinite(a) || Math.Abs(a) > 180))
            throw new ArgumentException("Углы 4D-поворота должны лежать между −180° и 180°.");
        if (!Valid(Exposure, .05, 20) || !Valid(Gamma, .5, 4) ||
            !Valid(Density, .05, 10) || !Valid(Saturation, 0, 2))
            throw new ArgumentException("Проверьте экспозицию, гамму, плотность и насыщенность.");
        static bool Valid(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
    }

    public static string ProjectionName(Buddhabrot4DProjection projection) => projection switch
    {
        Buddhabrot4DProjection.HideCr => "Re z · Im z · Im c",
        Buddhabrot4DProjection.HideZi => "Re z · Re c · Im c",
        Buddhabrot4DProjection.HideZr => "Im z · Re c · Im c",
        _ => "Re z · Im z · Re c"
    };
}
