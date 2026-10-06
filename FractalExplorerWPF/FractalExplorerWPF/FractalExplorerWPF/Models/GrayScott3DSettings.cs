using System.Text.Json.Serialization;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Models;

public enum GrayScott3DSeed { Spheres, Noise, Ring, Empty }

/// <summary>Immutable checkpoint; arrays are owned by the field and never edited after publication.</summary>
[JsonConverter(typeof(GrayScott3DFieldConverter))]
public sealed class GrayScott3DField
{
    public int Size { get; }
    public long Step { get; }
    internal float[] Values { get; }
    public ReadOnlySpan<float> Concentrations => Values;

    public GrayScott3DField(int size, long step, ReadOnlySpan<float> values)
        : this(size, step, values.ToArray(), true) { }

    internal GrayScott3DField(int size, long step, float[] values, bool takeOwnership)
    {
        if (size is < 32 or > 128 || step < 0 || values.Length != size * size * size * 2 || !InUnitRange(values))
            throw new ArgumentException("Некорректное поле Gray–Scott 3D.");
        Size = size; Step = step; Values = takeOwnership ? values : (float[])values.Clone();
    }

    private static bool InUnitRange(float[] values)
    {
        // NaN fails both comparisons, so the plain loop rejects non-finite values too.
        foreach (float value in values) if (!(value >= 0 && value <= 1)) return false;
        return true;
    }
}

public sealed record GrayScott3DSettings
{
    public int Size { get; init; } = 64;
    public double Feed { get; init; } = .030;
    public double Kill { get; init; } = .062;
    public double DiffusionU { get; init; } = .12;
    public double DiffusionV { get; init; } = .06;
    public GrayScott3DSeed SeedShape { get; init; } = GrayScott3DSeed.Noise;
    public int Seed { get; init; } = 42;
    public int StepsPerFrame { get; init; } = 16;
    public double Threshold { get; init; } = .18;
    /// <summary>0 = no clipping, 1/2/3 = X/Y/Z; keep coordinates below CutPosition.</summary>
    public int CutAxis { get; init; }
    public double CutPosition { get; init; }
    /// <summary>Сохранённая контрольная точка U/V; null — стартовая затравка по параметрам.</summary>
    public GrayScott3DField? Field { get; init; }
    /// <summary>Живой кадр моделирования на ГП окна; рендер берёт его вместо <see cref="Field"/>.</summary>
    [JsonIgnore]
    public GrayScott3DVolume? Live { get; init; }
    [JsonIgnore]
    public int InitialSteps => SeedShape switch { GrayScott3DSeed.Empty => 0, GrayScott3DSeed.Ring => 400, GrayScott3DSeed.Spheres => 1200, _ => 2400 };

    public void Validate()
    {
        if (Size is < 32 or > 128 || StepsPerFrame is < 1 or > 256 ||
            !double.IsFinite(Feed) || Feed is < 0 or > .1 || !double.IsFinite(Kill) || Kill is < 0 or > .1 ||
            !double.IsFinite(DiffusionU) || DiffusionU is < .001 or > .16 ||
            !double.IsFinite(DiffusionV) || DiffusionV is < .001 or > .16 ||
            !double.IsFinite(Threshold) || Threshold is < .01 or > .9 ||
            !double.IsFinite(CutPosition) || CutPosition is < -1 or > 1 || CutAxis is < 0 or > 3 ||
            !Enum.IsDefined(SeedShape) || Field is not null && Field.Size != Size || Live is not null && Live.Size != Size)
            throw new InvalidOperationException("Проверьте параметры Gray–Scott 3D: сетка 32–128, F/K 0–0,1, диффузия 0,001–0,16.");
    }

    /// <summary>Одинаковая эволюция: уравнение, сетка, затравка и контрольная точка; вид не учитывается.</summary>
    public bool SameEvolution(GrayScott3DSettings other) =>
        Size == other.Size && Feed == other.Feed && Kill == other.Kill &&
        DiffusionU == other.DiffusionU && DiffusionV == other.DiffusionV &&
        SeedShape == other.SeedShape && Seed == other.Seed && ReferenceEquals(Field, other.Field);
}
