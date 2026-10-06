using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>
/// Поиск интересных F/K для Gray–Scott 2D и 3D. Узоры живут в узкой полосе карты Пирсона
/// у кривой седло-узла K = √F/2 − F: левее поле заливается однородно, правее затравки гаснут.
/// Кандидаты берутся около известных классов Пирсона–Мунафо или из этой полосы, затем
/// каждый проходит короткую пробную симуляцию; пустые, однородные и залитые поля отбрасываются.
/// </summary>
public static class GrayScottRandomizer
{
    /// <summary>Сетка пробного прогона 2D: на ней узор успевает заполнить поле за несколько тысяч шагов.</summary>
    public const int ProbeGrid2D = 128;
    public const int ProbeSteps2D = 4000;
    /// <summary>Интервал перед последним снимком, по которому меряется подвижность.</summary>
    public const int ActivityInterval = 400;
    public const int MaxProbeGrid3D = 64;
    public const int LateSteps3D = 1200;

    // Классы Пирсона–Мунафо при Dv/Du = 1/2: волны, хаос, черви, лабиринты, митоз, кораллы,
    // U-skate, отверстия. Однородная кинетика одинакова в 2D и 3D, поэтому якоря общие.
    private static readonly (double Feed, double Kill)[] Anchors =
    [
        (.010, .047), (.014, .045), (.014, .050), (.018, .051), (.022, .051), (.026, .051),
        (.026, .055), (.030, .057), (.030, .062), (.034, .0618), (.0367, .0649), (.039, .058),
        (.042, .059), (.046, .063), (.050, .065), (.0545, .062), (.058, .065), (.060, .062),
        (.062, .0609), (.078, .061), (.090, .059), (.098, .057)
    ];

    /// <summary>Правая граница существования однородного «синего» состояния.</summary>
    public static double SaddleNodeKill(double feed) => Math.Sqrt(feed) / 2 - feed;

    internal static (double Feed, double Kill) SampleParameters(Random random, double maxValue)
    {
        double feed, kill;
        if (random.NextDouble() < .6)
        {
            var anchor = Anchors[random.Next(Anchors.Length)];
            feed = anchor.Feed + (random.NextDouble() * 2 - 1) * .003;
            kill = anchor.Kill + (random.NextDouble() * 2 - 1) * .0015;
        }
        else
        {
            feed = .01 + .09 * Math.Pow(random.NextDouble(), 1.3);
            kill = SaddleNodeKill(feed) + (-.004 + random.NextDouble() * .0115);
        }
        return (Math.Round(Math.Clamp(feed, .004, maxValue), 4), Math.Round(Math.Clamp(kill, .03, maxValue), 4));
    }

    internal static (double Feed, double Kill) Jitter(Random random, double feed, double kill, double maxValue) =>
        (Math.Round(Math.Clamp(feed + (random.NextDouble() * 2 - 1) * .0025, .002, maxValue), 4),
         Math.Round(Math.Clamp(kill + (random.NextDouble() * 2 - 1) * .001, .02, maxValue), 4));

    // ── 2D ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ищет узор для окна Gray–Scott. Сетка, движок, буфер, скорость и окраска берутся из
    /// <paramref name="current"/>; «Вариация» сдвигает только F/K и оставляет затравку.
    /// </summary>
    public static GrayScottCandidate<GrayScottState>? Search2D(GrayScottState current, GrayScottPatternTarget target,
        bool variation, IProgress<GrayScottSearchProgress>? progress, CancellationToken token, Random? random = null)
    {
        random ??= new Random();
        int limit = variation ? 24 : 48, batch = Math.Clamp(Environment.ProcessorCount, 2, 8);
        var alive = new List<(GrayScottState State, GrayScottPatternMetrics Metrics)>();
        int checkedCount = 0;
        while (checkedCount < limit)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(batch, limit - checkedCount);
            var states = Enumerable.Range(0, count).Select(_ => Create2D(current, random, variation)).ToArray();
            var metrics = new GrayScottPatternMetrics[count];
            Parallel.For(0, count, new ParallelOptions { CancellationToken = token }, i => metrics[i] = Probe2D(states[i], token));
            checkedCount += count;
            for (int i = 0; i < count; i++) if (metrics[i].IsAlive) alive.Add((states[i], metrics[i]));
            progress?.Report(new(checkedCount, alive.Count));
            var matches = alive.Where(item => item.Metrics.Matches(target)).ToList();
            if (matches.Count > 0) return Finish2D(Pick(matches, random), true, checkedCount);
        }
        return alive.Count == 0 ? null : Finish2D(alive.MaxBy(item => item.Metrics.Score), false, checkedCount);
    }

    private static GrayScottCandidate<GrayScottState> Finish2D((GrayScottState State, GrayScottPatternMetrics Metrics) found, bool matches, int checkedCount)
    {
        var state = found.State;
        // Подогнать шкалу V под найденный узор: у хаоса и волн пики ниже, чем у кораллов.
        if (state.FieldMode == GrayScottFieldMode.V)
        {
            state.RangeMinimum = found.Metrics.Floor > .05 ? Math.Round(found.Metrics.Floor - .02, 2) : 0;
            state.RangeMaximum = Math.Round(Math.Clamp(found.Metrics.Peak * 1.15, .15, 1), 2);
        }
        return new(state, found.Metrics, matches, checkedCount);
    }

    internal static GrayScottState Create2D(GrayScottState current, Random random, bool variation)
    {
        var state = current.Clone(string.Empty, includeCheckpoint: false);
        state.PresetId = null; state.Timestamp = default;
        if (variation)
        {
            (state.Feed, state.Kill) = Jitter(random, current.Feed, current.Kill, .2);
            return state;
        }
        (state.Feed, state.Kill) = SampleParameters(random, .2);
        state.DiffusionU = 1; state.DiffusionV = .5; state.DeltaTime = 1;
        state.RandomSeed = random.Next();
        double mode = random.NextDouble();
        state.SeedMode = mode < .45 ? GrayScottSeedMode.Noise : mode < .88 ? GrayScottSeedMode.RandomSpots : GrayScottSeedMode.Ring;
        state.SeedRadius = state.SeedMode == GrayScottSeedMode.Ring ? random.Next(3, 6) : random.Next(4, 9);
        // Одно пятно на квадрат 70–130 клеток: плотность не зависит от размера сетки.
        double spacing = 70 + random.NextDouble() * 60;
        state.SeedCount = Math.Clamp((int)Math.Round(state.GridSize * (double)state.GridSize / (spacing * spacing)), 1, 500);
        return state;
    }

    /// <summary>Тот же запуск на малой сетке: плотность пятен сохраняется, радиусы — в клетках.</summary>
    internal static GrayScottMetricsProbe ProbeField2D(GrayScottState state, CancellationToken token)
    {
        var probe = state.Clone(includeCheckpoint: false);
        double scale = ProbeGrid2D / (double)state.GridSize;
        probe.GridSize = ProbeGrid2D; probe.Backend = GrayScottBackend.Cpu;
        if (probe.SeedMode == GrayScottSeedMode.RandomSpots)
            probe.SeedCount = Math.Clamp((int)Math.Round(state.SeedCount * scale * scale), 1, 500);
        probe.SeedRadius = Math.Min(probe.SeedRadius, ProbeGrid2D / 3);
        var simulation = new GrayScottSimulation(probe);
        simulation.Advance(ProbeSteps2D - ActivityInterval, token);
        float[] earlier = [.. simulation.CurrentView().V];
        simulation.Advance(ActivityInterval, token);
        return new(simulation.CurrentView().V, earlier);
    }

    internal static GrayScottPatternMetrics Probe2D(GrayScottState state, CancellationToken token)
    {
        var field = ProbeField2D(state, token);
        return Measure(field.Late, field.Earlier, ProbeGrid2D, 2);
    }

    internal sealed record GrayScottMetricsProbe(float[] Late, float[] Earlier);

    // ── 3D ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ищет узор Gray–Scott 3D пробными прогонами на ГП того же устройства, что и окно.
    /// Форма оценивается на стартовой стадии (её и покажет окно), а выживание и подвижность —
    /// ещё через <see cref="LateSteps3D"/> шагов. Уровень поверхности подбирается под пики V.
    /// </summary>
    public static GrayScottCandidate<GrayScott3DSettings>? Search3D(Direct3DDeviceHost host, GrayScott3DSettings current,
        GrayScottPatternTarget target, bool variation, IProgress<GrayScottSearchProgress>? progress, CancellationToken token,
        Random? random = null)
    {
        random ??= new Random();
        int limit = variation ? 16 : 32, checkedCount = 0;
        var alive = new List<(GrayScott3DSettings State, GrayScottPatternMetrics Metrics)>();
        GrayScott3DGpuSimulation? simulation = null;
        try
        {
            while (checkedCount < limit)
            {
                token.ThrowIfCancellationRequested();
                var candidate = Create3D(current, random, variation);
                var probe = candidate with { Size = Math.Min(candidate.Size, MaxProbeGrid3D) };
                if (simulation is null) simulation = new GrayScott3DGpuSimulation(host, probe);
                else simulation.Reset(probe);
                var metrics = Probe3D(simulation, probe, token);
                checkedCount++;
                if (metrics.IsAlive)
                {
                    candidate = candidate with { Threshold = Math.Round(Math.Clamp(metrics.Threshold, .02, .8), 3) };
                    alive.Add((candidate, metrics));
                }
                progress?.Report(new(checkedCount, alive.Count));
                var matches = alive.Where(item => item.Metrics.Matches(target)).ToList();
                // Даём набраться нескольким подходящим, чтобы выбор не сводился к первому.
                if (matches.Count >= 3 || matches.Count > 0 && checkedCount >= 8)
                    return Finish3D(Pick(matches, random), true, checkedCount);
            }
            var matching = alive.Where(item => item.Metrics.Matches(target)).ToList();
            if (matching.Count > 0) return Finish3D(Pick(matching, random), true, checkedCount);
            return alive.Count == 0 ? null : Finish3D(alive.MaxBy(item => item.Metrics.Score), false, checkedCount);
        }
        finally { simulation?.Dispose(); }
    }

    private static GrayScottCandidate<GrayScott3DSettings> Finish3D((GrayScott3DSettings State, GrayScottPatternMetrics Metrics) found, bool matches, int checkedCount) =>
        new(found.State, found.Metrics, matches, checkedCount);

    internal static GrayScott3DSettings Create3D(GrayScott3DSettings current, Random random, bool variation)
    {
        var settings = current with { Field = null, Live = null };
        if (variation)
        {
            var (feed, kill) = Jitter(random, current.Feed, current.Kill, .1);
            return settings with { Feed = feed, Kill = kill };
        }
        var (f, k) = SampleParameters(random, .1);
        double du = Math.Round(.09 + random.NextDouble() * .06, 3);
        double shape = random.NextDouble();
        return settings with
        {
            Feed = f, Kill = k, DiffusionU = du, DiffusionV = Math.Round(du / 2, 4), Seed = random.Next(),
            SeedShape = shape < .55 ? GrayScott3DSeed.Noise : shape < .9 ? GrayScott3DSeed.Spheres : GrayScott3DSeed.Ring
        };
    }

    internal static GrayScottPatternMetrics Probe3D(GrayScott3DGpuSimulation simulation, GrayScott3DSettings probe, CancellationToken token)
    {
        void Run(int steps)
        {
            if (simulation.Advance(steps, token) < steps) token.ThrowIfCancellationRequested();
        }
        Run(probe.InitialSteps);
        float[] start = ExtractV(simulation.ReadCurrent());
        Run(LateSteps3D - ActivityInterval);
        float[] earlier = ExtractV(simulation.ReadCurrent());
        Run(ActivityInterval);
        float[] late = ExtractV(simulation.ReadCurrent());
        var shown = Measure(start, null, probe.Size, 3);
        var later = Measure(late, earlier, probe.Size, 3);
        // Показанная стадия задаёт форму, поздняя — выживание и подвижность.
        return later.IsAlive ? shown with { Activity = later.Activity } : later;
    }

    private static float[] ExtractV(GrayScott3DField field)
    {
        var values = field.Concentrations; var v = new float[values.Length / 2];
        for (int i = 0; i < v.Length; i++) v[i] = values[2 * i + 1];
        return v;
    }

    private static (TState State, GrayScottPatternMetrics Metrics) Pick<TState>(List<(TState State, GrayScottPatternMetrics Metrics)> items, Random random)
    {
        double total = items.Sum(item => Math.Max(item.Metrics.Score, 1e-6)), roll = random.NextDouble() * total;
        foreach (var item in items) if ((roll -= Math.Max(item.Metrics.Score, 1e-6)) <= 0) return item;
        return items[^1];
    }

    // ── Оценка поля ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Метрики периодического поля V (квадрат или куб со стороной <paramref name="size"/>).
    /// Связные области ищутся по осевым соседям через замкнутые края; области меньше трёх
    /// клеток считаются шумом. Компактность — квадрат радиуса инерции на площадь (объём^⅔):
    /// круг даёт 0,16, шар — 0,23, вытянутые нити и каналы заметно больше.
    /// </summary>
    public static GrayScottPatternMetrics Measure(float[] v, float[]? earlier, int size, int dimensions)
    {
        int n = v.Length;
        if (dimensions is not (2 or 3) || n != (dimensions == 2 ? size * size : size * size * size))
            throw new ArgumentException("Размер поля не совпадает со стороной сетки.");
        var histogram = new int[1024];
        double sum = 0, squares = 0;
        foreach (float value in v)
        {
            sum += value; squares += value * value;
            histogram[Math.Clamp((int)(value * 1023), 0, 1023)]++;
        }
        double mean = sum / n, deviation = Math.Sqrt(Math.Max(0, squares / n - mean * mean));
        double Percentile(double share)
        {
            int bin = 0;
            for (long accumulated = 0, limit = (long)(n * share); bin < 1023; bin++)
                if ((accumulated += histogram[bin]) > limit) break;
            return (bin + .5) / 1023;
        }
        // Фон бывает не нулевым (хаос, «синее» состояние), поэтому порог — середина размаха.
        double floor = Percentile(.01), peak = Percentile(.99), threshold = Math.Max(.04, (floor + peak) / 2);
        var mask = new bool[n]; int covered = 0;
        for (int i = 0; i < n; i++) if (mask[i] = v[i] > threshold) covered++;
        double compactLimit = dimensions == 2 ? .24 : .32;
        var blobs = Components(mask, true, size, dimensions, compactLimit);
        var holes = Components(mask, false, size, dimensions, compactLimit);
        double activity = 0;
        if (earlier is not null)
        {
            double change = 0;
            for (int i = 0; i < n; i++) change += Math.Abs(v[i] - earlier[i]);
            activity = change / n / Math.Max(deviation, 1e-6);
        }
        return new(deviation, covered / (double)n, activity, blobs.Count, blobs.CompactShare,
            holes.Count, holes.CompactShare, threshold, floor, peak);
    }

    private static (int Count, double CompactShare) Components(bool[] mask, bool value, int size, int dimensions, double compactLimit)
    {
        int n = mask.Length, plane = size * size, count = 0;
        long total = 0, compact = 0;
        var seen = new bool[n]; var queue = new int[n];
        var ux = new int[n]; var uy = new int[n]; var uz = new int[n];
        for (int start = 0; start < n; start++)
        {
            if (mask[start] != value || seen[start]) continue;
            int head = 0, tail = 0; queue[tail++] = start; seen[start] = true;
            ux[start] = uy[start] = uz[start] = 0;
            double sx = 0, sy = 0, sz = 0, sq = 0;
            while (head < tail)
            {
                int i = queue[head++];
                int x = i % size, y = i / size % size, z = i / plane;
                sx += ux[i]; sy += uy[i]; sz += uz[i];
                sq += (double)ux[i] * ux[i] + (double)uy[i] * uy[i] + (double)uz[i] * uz[i];
                for (int axis = 0; axis < dimensions; axis++)
                for (int step = -1; step <= 1; step += 2)
                {
                    int nx = x, ny = y, nz = z;
                    if (axis == 0) nx = (x + step + size) % size;
                    else if (axis == 1) ny = (y + step + size) % size;
                    else nz = (z + step + size) % size;
                    int j = nz * plane + ny * size + nx;
                    if (seen[j] || mask[j] != value) continue;
                    seen[j] = true; queue[tail++] = j;
                    ux[j] = ux[i] + (axis == 0 ? step : 0);
                    uy[j] = uy[i] + (axis == 1 ? step : 0);
                    uz[j] = uz[i] + (axis == 2 ? step : 0);
                }
            }
            if (tail < 3) continue;
            count++; total += tail;
            double c = tail, gyration = sq / c - (sx * sx + sy * sy + sz * sz) / (c * c);
            if (gyration / Math.Pow(c, 2.0 / dimensions) < compactLimit) compact += tail;
        }
        return (count, total == 0 ? 0 : compact / (double)total);
    }
}
