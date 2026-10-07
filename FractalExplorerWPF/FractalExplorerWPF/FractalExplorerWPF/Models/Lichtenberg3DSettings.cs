using System.Text.Json.Serialization;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Models;

public enum LichtenbergElectrodes { Radial, Plane }

/// <summary>Immutable checkpoint: growth order and the warm-start potential after the last committed bond.</summary>
[JsonConverter(typeof(Lichtenberg3DFieldConverter))]
public sealed class Lichtenberg3DField
{
    public int Size { get; }
    internal int[] Cells { get; }
    internal float[] Values { get; }
    public int Count => Cells.Length - 1;
    public ReadOnlySpan<int> GrowthOrder => Cells;
    public ReadOnlySpan<float> Potential => Values;

    public Lichtenberg3DField(int size, ReadOnlySpan<int> cells, ReadOnlySpan<float> values)
    {
        if (!Lichtenberg3DSettings.IsSupportedSize(size) || cells.Length is < 1 or > Lichtenberg3DSettings.MaxSegments + 1 ||
            values.Length != size * size * size || values.ContainsAnyExceptInRange(0f, 1f))
            throw new ArgumentException("Некорректное поле пробоя диэлектрика.");
        var occupied = new HashSet<int>();
        foreach (int cell in cells)
        {
            if (cell < 0 || cell >= values.Length || !occupied.Add(cell) || !float.IsFinite(values[cell]) || values[cell] != 0)
                throw new ArgumentException("Повреждён проводящий канал.");
            int x = cell % size, y = cell / size % size, z = cell / (size * size);
            if (x == 0 || y == 0 || z == 0 || x == size - 1 || y == size - 1 || z == size - 1)
                throw new ArgumentException("Канал выходит за сетку.");
            if (occupied.Count > 1 && !Neighbors(cell, size).Any(n => n != cell && occupied.Contains(n)))
                throw new ArgumentException("Разрыв проводящего канала.");
        }
        if (values.Contains(float.NaN) || values.ToArray().Any(v => !float.IsFinite(v)))
            throw new ArgumentException("Нечисловой потенциал.");
        Size = size; Cells = cells.ToArray(); Values = values.ToArray();
    }

    internal static IEnumerable<int> Neighbors(int cell, int n)
    {
        yield return cell - 1; yield return cell + 1; yield return cell - n; yield return cell + n;
        yield return cell - n * n; yield return cell + n * n;
    }
}

public sealed record Lichtenberg3DSettings
{
    public const int MaxSegments = 4000;
    public int Size { get; init; } = 48;
    public double Eta { get; init; } = 2.5;
    public LichtenbergElectrodes Electrodes { get; init; }
    public int Seed { get; init; } = 42;
    public int SegmentCount { get; init; } = 320;
    public int TargetSegments { get; init; } = 1000;
    public Lichtenberg3DField? Field { get; init; }
    [JsonIgnore] public long SessionId { get; init; }

    public static bool IsSupportedSize(int n) => n is 32 or 48 or 64;
    public void Validate()
    {
        if (!IsSupportedSize(Size) || !double.IsFinite(Eta) || Eta is < 0 or > 8 || !Enum.IsDefined(Electrodes) ||
            SegmentCount is < 0 or > MaxSegments || TargetSegments is < 1 or > MaxSegments ||
            Field is not null && (Field.Size != Size || Field.Count > SegmentCount))
            throw new InvalidOperationException("Пробой: сетка 32, 48 или 64; η от 0 до 8; до 4000 участков.");
    }
    public bool SameGrowth(Lichtenberg3DSettings other) => Size == other.Size && Eta == other.Eta &&
        Electrodes == other.Electrodes && Seed == other.Seed;
}
