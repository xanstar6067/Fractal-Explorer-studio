using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models;

public enum SnowCrystalColoring { Age, Mass, Radius }

/// <summary>Exact state of the independent sites of a sixfold symmetric hexagonal lattice.</summary>
public sealed class SnowCrystalCheckpoint
{
    public int Radius { get; set; }
    public int StepCount { get; set; }
    public double[] Mass { get; set; } = [];
    public int[] FrozenAt { get; set; } = [];

    public SnowCrystalCheckpoint Clone() => new()
    {
        Radius = Radius, StepCount = StepCount, Mass = [.. Mass], FrozenAt = [.. FrozenAt]
    };
}

public sealed class SnowCrystalState
{
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? PresetId { get; set; } = "stellar";
    public double Diffusion { get; set; } = 1;
    public double Vapor { get; set; } = .4;
    public double Deposition { get; set; } = .001;
    public int Radius { get; set; } = 160;
    public int SeedRadius { get; set; }
    public int StepsPerFrame { get; set; } = 24;
    public SnowCrystalColoring Coloring { get; set; } = SnowCrystalColoring.Age;
    public bool ShowVapor { get; set; }
    public Color CenterColor { get; set; } = Color.FromRgb(36, 115, 194);
    public Color TipColor { get; set; } = Color.FromRgb(224, 251, 255);
    public Color BackgroundColor { get; set; } = Color.FromRgb(3, 10, 24);
    public double Zoom { get; set; } = 1;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public SnowCrystalCheckpoint? Checkpoint { get; set; }

    public SnowCrystalState Clone(string? name = null) => new()
    {
        SaveName = name ?? SaveName, Timestamp = Timestamp, PresetId = PresetId,
        Diffusion = Diffusion, Vapor = Vapor, Deposition = Deposition,
        Radius = Radius, SeedRadius = SeedRadius, StepsPerFrame = StepsPerFrame,
        Coloring = Coloring, ShowVapor = ShowVapor,
        CenterColor = CenterColor, TipColor = TipColor, BackgroundColor = BackgroundColor,
        Zoom = Zoom, PanX = PanX, PanY = PanY, Checkpoint = Checkpoint?.Clone()
    };

    public void Validate()
    {
        if (!double.IsFinite(Diffusion) || Diffusion is < .05 or > 2 ||
            !double.IsFinite(Vapor) || Vapor is < 0 or > .99 ||
            !double.IsFinite(Deposition) || Deposition is < 0 or > .05)
            throw new ArgumentException("Диффузия: 0,05–2; насыщенность пара: 0–0,99; осаждение: 0–0,05.");
        if (Radius is < 32 or > 320 || SeedRadius is < 0 or > 8 || StepsPerFrame is < 1 or > 128)
            throw new ArgumentException("Радиус поля: 32–320; радиус затравки: 0–8; шагов на кадр: 1–128.");
        if (!double.IsFinite(Zoom) || Zoom is < .25 or > 20 ||
            !double.IsFinite(PanX) || !double.IsFinite(PanY) || Math.Abs(PanX) > 1_000_000 || Math.Abs(PanY) > 1_000_000 || !Enum.IsDefined(Coloring))
            throw new ArgumentException("Некорректный вид или режим окраски.");
    }
}

public sealed record SnowCrystalPreset(string Id, string Name, double Vapor, double Deposition, int PreviewSteps)
{
    public SnowCrystalState CreateState() => new() { PresetId = Id, Vapor = Vapor, Deposition = Deposition };
    public override string ToString() => Name;
}

public static class SnowCrystalPresets
{
    public static IReadOnlyList<SnowCrystalPreset> All { get; } =
    [
        new("stellar", "Звёздный дендрит", .4, .001, 2200),
        new("fern", "Тонкие ветви", .35, 0, 3600),
        new("branches", "Разветвлённая снежинка", .4, .002, 1800),
        new("lace", "Ажурный кристалл", .8, .002, 700),
        new("plate", "Шестиугольная пластина", .95, 0, 360)
    ];
}
