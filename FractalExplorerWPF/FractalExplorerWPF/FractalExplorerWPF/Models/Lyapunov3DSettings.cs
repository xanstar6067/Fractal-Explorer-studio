namespace FractalExplorerWPF.Models;

public enum Lyapunov3DRegion { Stable, Chaotic, Boundary }

/// <summary>Параметрический куб логистического отображения, а не пространство его орбиты.</summary>
public sealed record Lyapunov3DSettings
{
    public int Size { get; init; } = 64;
    public string Pattern { get; init; } = "ABCCAAB";
    public int Transient { get; init; } = 256;
    public int Samples { get; init; } = 512;
    public double InitialX { get; init; } = .5;
    public double MinA { get; init; } = 3.2;
    public double MaxA { get; init; } = 3.8;
    public double MinB { get; init; } = 3.2;
    public double MaxB { get; init; } = 3.8;
    public double MinC { get; init; } = 3.2;
    public double MaxC { get; init; } = 3.8;
    public Lyapunov3DRegion Region { get; init; } = Lyapunov3DRegion.Stable;
    public double Level { get; init; }
    public double BandWidth { get; init; } = .04;
    public double ColorRange { get; init; } = .4;
    public int CutAxis { get; init; }
    public double CutPosition { get; init; }
    public bool SliceOnly { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Sequence => string.Concat((Pattern ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    public void Validate()
    {
        if (Size is not (32 or 64 or 128)) throw new ArgumentException("Сетка Ляпунова: 32³, 64³ или 128³.");
        string sequence = Sequence;
        if (sequence.Length is < 1 or > 64 || sequence.Any(c => c is not ('A' or 'B' or 'C')))
            throw new ArgumentException("Последовательность: от 1 до 64 букв A, B, C (латиница).");
        if (Transient is < 0 or > 4096 || Samples is < 16 or > 4096)
            throw new ArgumentException("Прогрев: 0–4096; итераций измерения: 16–4096.");
        Range(MinA, MaxA, "A"); Range(MinB, MaxB, "B"); Range(MinC, MaxC, "C");
        if (!double.IsFinite(InitialX) || InitialX is < .000001 or > .999999)
            throw new ArgumentException("Начальное x: 0,000001–0,999999.");
        if (!Enum.IsDefined(Region) || !double.IsFinite(Level) || Level is < -2 or > 1 ||
            !double.IsFinite(BandWidth) || BandWidth is < .005 or > .5 ||
            !double.IsFinite(ColorRange) || ColorRange is < .05 or > 4 ||
            CutAxis is < 0 or > 3 || !double.IsFinite(CutPosition) || CutPosition is < -1 or > 1 ||
            SliceOnly && CutAxis == 0)
            throw new ArgumentException("Некорректные параметры поверхности, цвета или среза Ляпунова.");
    }

    private static void Range(double min, double max, string axis)
    {
        // A narrower interval cannot be represented reliably by the single precision GPU field.
        if (!double.IsFinite(min) || !double.IsFinite(max) || min < 0 || max > 4 || max - min < .00001)
            throw new ArgumentException($"Диапазон {axis}: 0 ≤ минимум < максимум ≤ 4; ширина ≥ 0,00001.");
    }

    public bool SameField(Lyapunov3DSettings other) => Size == other.Size && Sequence == other.Sequence &&
        Transient == other.Transient && Samples == other.Samples && InitialX == other.InitialX &&
        MinA == other.MinA && MaxA == other.MaxA && MinB == other.MinB && MaxB == other.MaxB &&
        MinC == other.MinC && MaxC == other.MaxC;
}
