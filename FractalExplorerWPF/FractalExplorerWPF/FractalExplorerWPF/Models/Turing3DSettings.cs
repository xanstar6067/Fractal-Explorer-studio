using System.Text.Json.Serialization;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Models;

/// <summary>Группа симметрии объёмного узора: ось с N лучами или вращения правильного многогранника.</summary>
public enum Turing3DSymmetry { None, Axial, Tetrahedral, Octahedral, Icosahedral }

/// <summary>
/// Где растёт узор: во всём кубе, в шаре или в сферической оболочке. Сферы шара и оболочки —
/// зеркальные границы, как грани куба: клетка за ними повторяет своё радиальное отражение, и
/// узор подходит к ним под прямым углом, а тонкую оболочку пробивает насквозь.
/// </summary>
public enum Turing3DRegion { Cube, Sphere, Shell }

/// <summary>Один масштаб модели Маккейба. Радиус активатора — в клетках опорной сетки 128.</summary>
public sealed record Turing3DScale(bool Enabled, double Radius, double Amount);

/// <summary>Неизменяемая контрольная точка: поле в [-1, 1] и номер победившего масштаба каждой клетки.</summary>
[JsonConverter(typeof(Turing3DFieldConverter))]
public sealed class Turing3DField
{
    public const int MinSize = 32, MaxSize = 160;

    public int Size { get; }
    public long Step { get; }
    internal float[] Values { get; }
    internal byte[] Scales { get; }
    public ReadOnlySpan<float> Field => Values;
    public ReadOnlySpan<byte> ScaleMap => Scales;

    public Turing3DField(int size, long step, ReadOnlySpan<float> values, ReadOnlySpan<byte> scales)
        : this(size, step, values.ToArray(), scales.ToArray(), true) { }

    internal Turing3DField(int size, long step, float[] values, byte[] scales, bool takeOwnership)
    {
        long count = (long)size * size * size;
        if (size is < MinSize or > MaxSize || step is < 0 or > 1_000_000_000 || values.Length != count || scales.Length != count)
            throw new ArgumentException("Некорректное поле узора Тьюринга 3D.");
        // NaN fails both comparisons, so the plain loop rejects non-finite values too.
        foreach (float value in values) if (!(value >= -1.001f && value <= 1.001f)) throw new ArgumentException("Значения поля Тьюринга 3D вне [-1, 1].");
        foreach (byte scale in scales) if (scale >= Turing3DSettings.MaxLayers) throw new ArgumentException("Некорректная карта масштабов Тьюринга 3D.");
        Size = size; Step = step;
        Values = takeOwnership ? values : (float[])values.Clone();
        Scales = takeOwnership ? scales : (byte[])scales.Clone();
    }

    /// <summary>
    /// Перенос поля на другую сетку с сохранением времени: трилинейная выборка по центрам клеток,
    /// карта масштабов — по ближайшей клетке. Узор не меняет своего размера относительно куба.
    /// </summary>
    public static Turing3DField Resize(Turing3DField source, int size)
    {
        if (size == source.Size) return source;
        if (size is < MinSize or > MaxSize) throw new ArgumentOutOfRangeException(nameof(size));
        int n = source.Size; long count = (long)size * size * size;
        var values = new float[count]; var scales = new byte[count];
        double ratio = (double)n / size;
        Parallel.For(0, size, z =>
        {
            double sz = Math.Clamp((z + .5) * ratio - .5, 0, n - 1);
            for (int y = 0; y < size; y++)
            {
                double sy = Math.Clamp((y + .5) * ratio - .5, 0, n - 1);
                for (int x = 0; x < size; x++)
                {
                    double sx = Math.Clamp((x + .5) * ratio - .5, 0, n - 1);
                    long i = ((long)z * size + y) * size + x;
                    values[i] = (float)Math.Clamp(Trilinear(source.Values, n, sx, sy, sz), -1, 1);
                    scales[i] = source.Scales[((int)Math.Round(sz) * n + (int)Math.Round(sy)) * n + (int)Math.Round(sx)];
                }
            }
        });
        return new Turing3DField(size, source.Step, values, scales, true);
    }

    private static double Trilinear(float[] field, int n, double x, double y, double z)
    {
        int x0 = (int)x, y0 = (int)y, z0 = (int)z;
        int x1 = Math.Min(x0 + 1, n - 1), y1 = Math.Min(y0 + 1, n - 1), z1 = Math.Min(z0 + 1, n - 1);
        double tx = x - x0, ty = y - y0, tz = z - z0;
        double V(int a, int b, int c) => field[(c * n + b) * n + a];
        double Lerp(double a, double b, double t) => a + (b - a) * t;
        return Lerp(
            Lerp(Lerp(V(x0, y0, z0), V(x1, y0, z0), tx), Lerp(V(x0, y1, z0), V(x1, y1, z0), tx), ty),
            Lerp(Lerp(V(x0, y0, z1), V(x1, y0, z1), tx), Lerp(V(x0, y1, z1), V(x1, y1, z1), tx), ty), tz);
    }
}

/// <summary>
/// Многомасштабные узоры Тьюринга в кубе: правило Маккейба по трём осям, симметрия, вид и кисть.
/// Изменение параметров эволюции действует на текущее поле со следующего шага; <see cref="Field"/> —
/// контрольная точка, с которой начато моделирование (null — случайное поле по <see cref="Seed"/>).
/// </summary>
public sealed record Turing3DSettings
{
    public const int MaxLayers = 6;
    /// <summary>Радиусы заданы в клетках этой сетки: смена сетки не меняет размера узора в кубе.</summary>
    public const int ReferenceSize = 128;

    private static readonly Turing3DScale[] ReferenceLayers =
    [
        new(true, 2.5, .012), new(true, 5, .02), new(true, 10, .035),
        new(true, 20, .05), new(true, 36, .08), new(true, 56, .1)
    ];

    public int Size { get; init; } = 96;
    public int Seed { get; init; } = 1729;
    public int WarmupSteps { get; init; } = 120;
    public int StepsPerFrame { get; init; } = 2;
    public double DetailSize { get; init; } = 1;
    public double EvolutionRate { get; init; } = 1;
    public double InhibitorRatio { get; init; } = 2;
    public IReadOnlyList<Turing3DScale> Layers { get; init; } = DefaultLayers(4);
    public Turing3DSymmetry Symmetry { get; init; }
    /// <summary>Число лучей осевой симметрии вокруг Y (1–16).</summary>
    public int Arms { get; init; } = 6;
    public bool Mirror { get; init; }
    public TuringBoundary Boundary { get; init; } = TuringBoundary.Wrap;
    public Turing3DRegion Region { get; init; }
    /// <summary>Толщина оболочки в долях радиуса шара (0,05–0,6).</summary>
    public double ShellThickness { get; init; } = .2;

    /// <summary>Уровень поверхности на шкале поля [0, 1]: 0,5 — граница между двумя фазами.</summary>
    public double Level { get; init; } = .5;
    /// <summary>0 — показ заполненной фазы; иначе — тонкий лист вокруг уровня этой полутолщины на шкале поля.</summary>
    public double SheetThickness { get; init; }
    /// <summary>0 — без среза, 1/2/3 — X/Y/Z; видны координаты ниже <see cref="CutPosition"/>.</summary>
    public int CutAxis { get; init; }
    public double CutPosition { get; init; }

    public TuringBrush Brush { get; init; }
    public double BrushRadius { get; init; } = .08;
    public double BrushStrength { get; init; } = .7;

    public Turing3DField? Field { get; init; }
    /// <summary>Живой кадр моделирования на ГП окна; рендер берёт его вместо <see cref="Field"/>.</summary>
    [JsonIgnore]
    public Turing3DVolume? Live { get; init; }

    public static Turing3DScale[] DefaultLayers(int depth) => ReferenceLayers.Take(Math.Clamp(depth, 1, MaxLayers)).ToArray();

    /// <summary>Включённые масштабы в клетках сетки <paramref name="size"/>: номер, радиусы активатора и подавления, отклик.</summary>
    public IReadOnlyList<(int Layer, int Radius, int Inhibitor, double Amount)> EffectiveLayers(int size)
    {
        var result = new List<(int, int, int, double)>();
        for (int i = 0; i < Layers.Count; i++)
        {
            Turing3DScale scale = Layers[i];
            if (!scale.Enabled) continue;
            int radius = Math.Clamp((int)Math.Round(scale.Radius * DetailSize * size / ReferenceSize), 1, size - 1);
            int inhibitor = Math.Clamp((int)Math.Round(radius * InhibitorRatio), radius + 1, size);
            result.Add((i, radius, inhibitor, scale.Amount * EvolutionRate));
        }
        return result;
    }

    public void Validate()
    {
        static bool Range(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
        if (Size is < Turing3DField.MinSize or > Turing3DField.MaxSize || StepsPerFrame is < 1 or > 16 || WarmupSteps is < 0 or > 2000)
            throw new InvalidOperationException("Сетка: 32–160 по каждой оси; шагов на кадр: 1–16; начальное развитие: 0–2000 шагов.");
        if (!Range(DetailSize, .25, 3) || !Range(EvolutionRate, .1, 3) || !Range(InhibitorRatio, 1.2, 4) ||
            !Enum.IsDefined(Symmetry) || Arms is < 1 or > 16 || !Enum.IsDefined(Boundary))
            throw new InvalidOperationException("Проверьте размер деталей (0,25–3), отклик (0,1–3), отношение радиусов (1,2–4) и симметрию (1–16 лучей).");
        if (Layers is null || Layers.Count is < 1 or > MaxLayers || Layers.Any(l => l is null) || !Layers.Any(l => l.Enabled) ||
            Layers.Any(l => !Range(l.Radius, .5, 64) || !Range(l.Amount, .001, .15)))
            throw new InvalidOperationException("Включите хотя бы один масштаб. Радиусы: 0,5–64; отклик: 0,001–0,15.");
        if (!Range(Level, .05, .95) || !Enum.IsDefined(Region) || !Range(ShellThickness, .05, .6) ||
            SheetThickness != 0 && !Range(SheetThickness, .02, .3) || CutAxis is < 0 or > 3 || !Range(CutPosition, -1, 1) ||
            !Enum.IsDefined(Brush) || !Range(BrushRadius, .02, .3) || !Range(BrushStrength, .05, 1))
            throw new InvalidOperationException("Некорректная форма объёма, уровень поверхности, мембрана, срез или кисть.");
        if (Field is not null && Field.Size != Size || Live is not null && Live.Size != Size)
            throw new InvalidOperationException("Сохранённое поле не соответствует размеру сетки.");
    }

    /// <summary>Одинаковое правило эволюции: вид, кисть и скорость показа не учитываются.</summary>
    public bool SameRule(Turing3DSettings other) =>
        Size == other.Size && DetailSize == other.DetailSize && EvolutionRate == other.EvolutionRate &&
        InhibitorRatio == other.InhibitorRatio && Symmetry == other.Symmetry && Arms == other.Arms &&
        Mirror == other.Mirror && Boundary == other.Boundary && Layers.SequenceEqual(other.Layers) &&
        Region == other.Region && (Region != Turing3DRegion.Shell || ShellThickness == other.ShellThickness);

    /// <summary>Одинаковая эволюция: правило, стартовое поле и контрольная точка.</summary>
    public bool SameEvolution(Turing3DSettings other) =>
        SameRule(other) && Seed == other.Seed && WarmupSteps == other.WarmupSteps && ReferenceEquals(Field, other.Field);

    /// <summary>Сколько шагов подготовить для показа состояния без контрольной точки.</summary>
    [JsonIgnore]
    public int InitialSteps => Field is null ? WarmupSteps : 0;

    public static string SymmetryName(Turing3DSymmetry symmetry, int arms, bool mirror) => symmetry switch
    {
        Turing3DSymmetry.Axial when arms == 1 => mirror ? "зеркальная плоскость" : "без симметрии",
        Turing3DSymmetry.Axial => $"{arms} {(arms < 5 ? "луча" : "лучей")} вокруг оси Y" + (mirror ? " и отражения" : ""),
        Turing3DSymmetry.Tetrahedral => mirror ? "тетраэдр с отражениями" : "вращения тетраэдра",
        Turing3DSymmetry.Octahedral => mirror ? "куб/октаэдр с отражениями" : "вращения куба/октаэдра",
        Turing3DSymmetry.Icosahedral => mirror ? "икосаэдр с отражениями" : "вращения икосаэдра",
        _ => "без симметрии"
    };
}
