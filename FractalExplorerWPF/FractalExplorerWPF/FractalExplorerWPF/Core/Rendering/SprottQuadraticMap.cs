using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Two coupled quadratic maps in Sprott's coefficient order and A–Y code.
/// Search method and coding follow J. C. Sprott, Comput. &amp; Graphics 17, 325–332 (1993):
/// https://sprott.physics.wisc.edu/pubs/PAPER203.HTM
/// </summary>
public static class SprottQuadraticMap
{
    public const int CoefficientCount = 12;

    public static readonly (string Name, string Code)[] Presets =
    [
        ("Ледяной плащ", "EXJNXAIFANNEN"),
        ("Петля кометы", "EDFLQJGDGMSJV"),
        ("Двойное лезвие", "EJETCOHRSIQFN"),
        ("Три острова", "EQVHVRXREMJED"),
        ("Игла и петля", "ERKKCUNHERKAV")
    ];

    public static Analysis ApplyCode(DynamicSystemState state, string code, CancellationToken token = default)
    {
        double[] coefficients = Decode(code.Trim().ToUpperInvariant());
        Analysis view = Analyze(coefficients, token) ??
            throw new InvalidOperationException("Эта карта не дала ограниченную траекторию. Проверьте код или начальную точку.");
        state.Attractor2DMode = nameof(Attractor2DKind.SprottQuadratic);
        state.QuadraticCoefficients = coefficients;
        state.QuadraticSpan = view.Span;
        state.CenterX = view.CenterX;
        state.CenterY = view.CenterY;
        state.Zoom = 1;
        state.X0 = .05;
        state.Y0 = .05;
        return view;
    }

    public static double[] Decode(string code)
    {
        if (code.Length != 13 || code[0] != 'E' || code.Skip(1).Any(c => c is < 'A' or > 'Y'))
            throw new ArgumentException("Код Спротта должен содержать E и 12 букв от A до Y.", nameof(code));
        return code.Skip(1).Select(c => (c - 'M') / 10d).ToArray();
    }

    public static string? Encode(IReadOnlyList<double> coefficients)
    {
        if (coefficients.Count != CoefficientCount) return null;
        Span<char> code = stackalloc char[13];
        code[0] = 'E';
        for (int i = 0; i < CoefficientCount; i++)
        {
            double scaled = coefficients[i] * 10;
            int step = (int)Math.Round(scaled);
            if (!double.IsFinite(scaled) || step is < -12 or > 12 || Math.Abs(scaled - step) > 1e-8)
                return null;
            code[i + 1] = (char)('M' + step);
        }
        return new string(code);
    }

    public static void Iterate(IReadOnlyList<double> a, ref double x, ref double y)
    {
        double nextX = a[0] + x * (a[1] + a[2] * x + a[3] * y) + y * (a[4] + a[5] * y);
        double nextY = a[6] + x * (a[7] + a[8] * x + a[9] * y) + y * (a[10] + a[11] * y);
        x = nextX;
        y = nextY;
    }

    public static Analysis? Analyze(IReadOnlyList<double> a, CancellationToken token, int iterations = 4_000)
    {
        if (a.Count != CoefficientCount || a.Any(v => !double.IsFinite(v))) return null;
        double x = .05, y = .05, tx = 1, ty = 0, sum = 0;
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        int measured = 0;
        for (int i = 0; i < iterations + 600; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            double j11 = a[1] + 2 * a[2] * x + a[3] * y;
            double j12 = a[3] * x + a[4] + 2 * a[5] * y;
            double j21 = a[7] + 2 * a[8] * x + a[9] * y;
            double j22 = a[9] * x + a[10] + 2 * a[11] * y;
            double vx = j11 * tx + j12 * ty, vy = j21 * tx + j22 * ty;
            double length = Math.Sqrt(vx * vx + vy * vy);
            if (!double.IsFinite(length) || length < 1e-30) return null;
            tx = vx / length; ty = vy / length;
            Iterate(a, ref x, ref y);
            if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 100 || Math.Abs(y) > 100)
                return null;
            if (i < 600) continue;
            sum += Math.Log2(length);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            measured++;
        }
        double width = maxX - minX, height = maxY - minY;
        if (measured == 0 || width < .02 || height < .02 || width > 30 || height > 30) return null;
        return new Analysis((minX + maxX) / 2, (minY + maxY) / 2,
            Math.Max(width, height) * 1.18, sum / measured);
    }

    public static SearchResult Search(CancellationToken token, IProgress<int>? progress = null)
    {
        const int attempts = 12_000;
        double[] a = new double[CoefficientCount];
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < a.Length; i++) a[i] = Random.Shared.Next(-12, 13) / 10d;
            Analysis? analysis = Analyze(a, token, 2_000);
            if (analysis is { Lyapunov: > .06 and < .8, Span: > .35 and < 12 })
            {
                Analysis? confirmed = Analyze(a, token, 8_000);
                if (confirmed is { Lyapunov: > .06 and < .8, Span: > .35 and < 12 } &&
                    CountOccupiedCells(a, confirmed.Value, token) >= 180)
                {
                    string code = Encode(a)!;
                    return new SearchResult(code, confirmed.Value, attempt);
                }
            }
            if (attempt % 100 == 0) progress?.Report(attempt * 100 / attempts);
        }
        throw new InvalidOperationException("Не удалось найти устойчивую хаотическую карту. Запустите поиск ещё раз.");
    }

    public static int CountOccupiedCells(IReadOnlyList<double> a, Analysis view, CancellationToken token)
    {
        const int side = 64;
        bool[] occupied = new bool[side * side];
        double x = .05, y = .05;
        double minX = view.CenterX - view.Span / 2, minY = view.CenterY - view.Span / 2;
        int count = 0;
        for (int i = 0; i < 12_600; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            Iterate(a, ref x, ref y);
            if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 100 || Math.Abs(y) > 100)
                return 0;
            if (i < 600) continue;
            int px = (int)((x - minX) / view.Span * side);
            int py = (int)((y - minY) / view.Span * side);
            if ((uint)px >= side || (uint)py >= side) continue;
            int index = py * side + px;
            if (!occupied[index]) { occupied[index] = true; count++; }
        }
        return count;
    }

    public readonly record struct Analysis(double CenterX, double CenterY, double Span, double Lyapunov);
    public readonly record struct SearchResult(string Code, Analysis View, int Attempts);
}
