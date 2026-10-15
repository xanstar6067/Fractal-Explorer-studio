using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models;

public enum SandpileLattice { Square, Triangular }
public enum SandpileSeed { Center, FourPiles, Filled, Empty }
public enum SandpileColoring { Grains, Topplings, Activity }

/// <summary>The queue is part of a checkpoint: even an unfinished avalanche resumes in the same order.</summary>
public sealed class SandpileCheckpoint
{
    public long[] Grains { get; set; } = [];
    public long[] Topplings { get; set; } = [];
    public int[] Queue { get; set; } = [];
    public long Added { get; set; }
    public long Lost { get; set; }
    public long Operations { get; set; }
    public SandpileCheckpoint Clone() => new()
    {
        Grains = [.. Grains], Topplings = [.. Topplings], Queue = [.. Queue],
        Added = Added, Lost = Lost, Operations = Operations
    };
}

public sealed class SandpileState
{
    public string SaveName { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int Size { get; set; } = 257;
    public SandpileLattice Lattice { get; set; }
    public SandpileSeed Seed { get; set; }
    public long InitialGrains { get; set; } = 32_768;
    public int OperationsPerFrame { get; set; } = 100_000;
    public int BrushRadius { get; set; }
    public int BrushGrains { get; set; } = 256;
    public bool AutoDrop { get; set; }
    public int DropGrains { get; set; } = 128;
    public SandpileColoring Coloring { get; set; }
    public Color[] Colors { get; set; } = SandpilePalettes.All[0].Colors.ToArray();
    public double Zoom { get; set; } = 1.6;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public SandpileCheckpoint? Checkpoint { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int Degree => Lattice == SandpileLattice.Square ? 4 : 6;

    public SandpileState Clone(string? name = null)
    {
        var copy = (SandpileState)MemberwiseClone();
        copy.SaveName = name ?? SaveName;
        copy.Colors = Colors?.ToArray()!;
        copy.Checkpoint = Checkpoint?.Clone();
        return copy;
    }

    public void Validate()
    {
        if (Size is < 33 or > 513 || Size % 2 == 0)
            throw new ArgumentException("Размер поля: нечётное число от 33 до 513.");
        if (!Enum.IsDefined(Lattice) || !Enum.IsDefined(Seed) || !Enum.IsDefined(Coloring))
            throw new ArgumentException("Неизвестная решётка, начальное состояние или окраска.");
        if (InitialGrains is < 0 or > 10_000_000 || OperationsPerFrame is < 100 or > 500_000 ||
            BrushRadius is < 0 or > 20 || BrushGrains is < 1 or > 1_000_000 || DropGrains is < 1 or > 1_000_000)
            throw new ArgumentException("Затравка: 0–10 000 000; порция: 100–500 000; радиус кисти: 0–20; подсыпка: 1–1 000 000.");
        if (Colors is null || Colors.Length != 6 || Colors.Any(c => c.A != 255) ||
            !double.IsFinite(Zoom) || Zoom is < .25 or > 32 ||
            !double.IsFinite(PanX) || !double.IsFinite(PanY) || Math.Abs(PanX) > 100_000 || Math.Abs(PanY) > 100_000)
            throw new ArgumentException("Некорректная палитра или положение поля.");
    }
}

public sealed record SandpilePalette(string Name, IReadOnlyList<Color> Colors)
{
    public override string ToString() => Name;
}

public static class SandpilePalettes
{
    public static IReadOnlyList<SandpilePalette> All { get; } =
    [
        Make("Лазурь и золото", "#080F20", "#194F78", "#38B6BD", "#FFD47A", "#ED8454", "#F5EBDB"),
        Make("Песчаная мандала", "#160F14", "#71374D", "#DC7548", "#F5CC78", "#B4D5A0", "#FCF1D0"),
        Make("Северное сияние", "#090D24", "#433C96", "#9474D9", "#75DBCB", "#CBF59B", "#FCFCE4"),
        Make("Бумага и тушь", "#F3EBDD", "#C8B79E", "#86715F", "#242A32", "#467D88", "#BE5D46")
    ];
    private static SandpilePalette Make(string name, params string[] colors) =>
        new(name, colors.Select(c => (Color)System.Windows.Media.ColorConverter.ConvertFromString(c)).ToArray());
}

public sealed record SandpilePreset(string Name, SandpileLattice Lattice, SandpileSeed Seed, long Grains, int Size = 257)
{
    public SandpileState CreateState() => new()
    {
        Lattice = Lattice, Seed = Seed, InitialGrains = Grains, Size = Size,
        Zoom = Seed == SandpileSeed.Center ? Lattice == SandpileLattice.Triangular ? 2.8 : 1.6 : Seed == SandpileSeed.FourPiles ? 1.2 : 1
    };
    public override string ToString() => Name;
}

public static class SandpilePresets
{
    public static IReadOnlyList<SandpilePreset> All { get; } =
    [
        new("Классическая мандала", SandpileLattice.Square, SandpileSeed.Center, 32_768),
        new("Четыре источника", SandpileLattice.Square, SandpileSeed.FourPiles, 32_768),
        new("Шесть направлений", SandpileLattice.Triangular, SandpileSeed.Center, 32_768),
        new("Переполненное поле", SandpileLattice.Square, SandpileSeed.Filled, 0, 65),
        new("Чистый лист · кисть", SandpileLattice.Square, SandpileSeed.Empty, 0)
    ];
}
