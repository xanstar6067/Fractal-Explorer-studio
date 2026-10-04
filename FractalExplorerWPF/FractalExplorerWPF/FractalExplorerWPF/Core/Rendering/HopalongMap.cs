using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>Barry Martin's simultaneous real-plane map. At x = 0, sign(x) = 0.</summary>
public static class HopalongMap
{
    public static void Iterate(HopalongSettings settings, ref double x, ref double y)
    {
        double oldX = x;
        x = y - Math.Sign(oldX) * Math.Sqrt(Math.Abs(settings.B * oldX - settings.C));
        y = settings.A - oldX;
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1e12 || Math.Abs(y) > 1e12)
            throw new InvalidOperationException("Орбита вышла за численный диапазон. Измените параметры или начальную точку.");
    }

    public sealed record Analysis(double CenterX, double CenterY, double Span, int OccupiedCells);
    public sealed record SearchResult(HopalongSettings Settings, Analysis View, int Attempts);

    public static Analysis Analyze(HopalongSettings settings, CancellationToken token, int points = 131_072)
    {
        token.ThrowIfCancellationRequested(); settings.Validate();
        if (points < 1024) throw new ArgumentOutOfRangeException(nameof(points));
        double x = settings.StartX, y = settings.StartY;
        double angle = settings.Rotation % 360 * Math.PI / 180;
        double cosine = Math.Cos(angle), sine = Math.Sin(angle);
        var xs = new double[points]; var ys = new double[points];
        for (int i = 0; i < points; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            Iterate(settings, ref x, ref y);
            xs[i] = x * cosine - y * sine; ys[i] = x * sine + y * cosine;
        }
        var sortedX = (double[])xs.Clone(); var sortedY = (double[])ys.Clone();
        Array.Sort(sortedX); Array.Sort(sortedY); token.ThrowIfCancellationRequested();
        // Fit 99% of the sampled orbit, so isolated outer excursions do not
        // make the interesting islands invisible. A later orbit can grow further.
        int low = points / 200, high = points - 1 - low;
        double cx = (sortedX[low] + sortedX[high]) * .5, cy = (sortedY[low] + sortedY[high]) * .5;
        double span = Math.Max(sortedX[high] - sortedX[low], sortedY[high] - sortedY[low]) * 1.12;
        if (!double.IsFinite(span) || span is < .0001 or > 1e8)
            throw new InvalidOperationException("Орбита слишком мала или велика для подбора кадра. Измените параметры.");
        var cells = new HashSet<int>();
        for (int i = 0; i < points; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            double fx = (xs[i] - cx) / span + .5, fy = (ys[i] - cy) / span + .5;
            if (fx is >= 0 and < 1 && fy is >= 0 and < 1) cells.Add((int)(fy * 128) * 128 + (int)(fx * 128));
        }
        return new(cx, cy, span, cells.Count);
    }

    public static SearchResult Search(int seed, HopalongSettings? basis, CancellationToken token,
        IProgress<int>? progress = null)
    {
        var random = new Random(seed);
        for (int attempt = 1; attempt <= 64; attempt++)
        {
            token.ThrowIfCancellationRequested();
            HopalongSettings candidate = basis?.Clone() ?? HopalongPresets.All[random.Next(HopalongPresets.All.Count)].Settings.Clone();
            candidate.A += (random.NextDouble() * 2 - 1) * Math.Max(.15, Math.Abs(candidate.A) * .12);
            candidate.B += (random.NextDouble() * 2 - 1) * Math.Max(.015, Math.Abs(candidate.B) * .15);
            candidate.C += (random.NextDouble() * 2 - 1) * Math.Max(.2, Math.Abs(candidate.C) * .15);
            try
            {
                Analysis view = Analyze(candidate, token, 32_768);
                // Accept a spatially rich orbit, without treating this
                // area-preserving map as a dissipative attractor.
                if (view.OccupiedCells >= 1_200) return new(candidate, view, attempt);
            }
            catch (InvalidOperationException) { }
            progress?.Report(attempt * 100 / 64);
        }
        throw new InvalidOperationException("Не удалось подобрать выразительную форму. Попробуйте ещё раз или выберите готовый вид.");
    }
}
