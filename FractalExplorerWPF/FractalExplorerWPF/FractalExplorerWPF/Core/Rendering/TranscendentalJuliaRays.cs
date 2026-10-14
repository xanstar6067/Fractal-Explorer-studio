using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public sealed record TranscendentalRay(Complex[] Points, long[] Address, int Depth);

/// <summary>
/// Лучи с конечным адресом и нулевым хвостом. F(t)=exp(t)−1; обратные ветви применяются
/// к асимптотике при большом потенциале. Разрывы и критические значения не соединяются.
/// </summary>
public sealed class TranscendentalJuliaRays
{
    private readonly TranscendentalJuliaState _state;
    private readonly TranscendentalMap _map;
    private readonly Complex _kappa;
    private readonly double _pixelsPerUnit;
    private readonly int _width, _height;
    private readonly int _budget;
    private readonly int _rayLimit;
    private int _work;
    public int Work => _work;
    public int SplitCount { get; private set; }
    public int CompletedDepth { get; private set; }
    public bool BudgetReached { get; private set; }
    private const int TailIterations = 1024;
    private sealed record Tail(int Depth, double Potential, Complex[] ZeroValues);
    private readonly Dictionary<(long T, long Target), Tail> _tails = [];

    public TranscendentalJuliaRays(TranscendentalJuliaState state, int width, int height, int? budget = null)
    {
        state.Validate();
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        _state = state.Clone(); _width = width; _height = height;
        _budget = Math.Clamp(budget ?? state.PointBudget, 1, state.PointBudget);
        // Мелкое превью не нуждается в сотнях неразличимых кривых; точность увеличивает плотность.
        _rayLimit = Math.Min(state.RayLimit, (int)Math.Clamp(width * (double)height /
            (400 * Math.Max(.25, state.RayAccuracy / .35)), 48, 10000));
        _map = new(state.Kind, new(state.LambdaReal, state.LambdaImaginary));
        _kappa = Complex.Log(_map.Lambda / (state.Kind == TranscendentalJuliaKind.Sine ? 2 : 1));
        _pixelsPerUnit = width * state.Zoom / TranscendentalJuliaRenderer.BaseViewWidth;
    }

    public static double PotentialStep(double t) => t < 1e-3
        ? t * (1 + t * (0.5 + t * (1.0 / 6 + t * (1.0 / 24 + t / 120)))) : Math.Exp(t) - 1;

    public Complex Evaluate(double t, IReadOnlyList<long> address, bool negative = false)
    {
        if (_map.Lambda == Complex.Zero || !double.IsFinite(t) || t <= 0 || address.Count > 80) return Invalid;
        long maxBranch = 0;
        foreach (long branch in address) maxBranch = Math.Max(maxBranch, Math.Abs(branch));
        double target = Math.Max(40 + 4 * _kappa.Magnitude, 40 + Math.Log(1 + 2 * Math.PI * maxBranch));
        // Нулевой хвост общий для всех конечных адресов. Без кэша каждый луч заново
        // выполнял сотни log/asin около конечной точки при тех же значениях t.
        var key = (BitConverter.DoubleToInt64Bits(t), BitConverter.DoubleToInt64Bits(target));
        if (!_tails.TryGetValue(key, out Tail? tail))
        {
            int count = 0; double value = t;
            while (value < target && count < TailIterations)
            { value = PotentialStep(value); count++; if (!double.IsFinite(value)) return Invalid; }
            if (value < target) return Invalid;
            var zeros = new Complex[Math.Min(count, 80) + 1];
            Complex zero = _state.Kind == TranscendentalJuliaKind.Exponential ? value - _kappa : Complex.ImaginaryOne * (value - _kappa);
            if (count <= 80) zeros[count] = zero;
            for (int i = count - 1; i >= 0; i--)
            {
                if (_state.Kind == TranscendentalJuliaKind.Exponential) zero = Complex.Log(zero) - _kappa;
                else
                {
                    Complex w = -Complex.ImaginaryOne * zero;
                    Complex u = -Complex.ImaginaryOne * Complex.Asin(Complex.ImaginaryOne * w / _map.Lambda);
                    if (u.Real < 0) u = -u + new Complex(0, Math.PI);
                    u += new Complex(0, 2 * Math.PI * Math.Round((-_kappa.Imaginary - u.Imaginary) / (2 * Math.PI)));
                    zero = Complex.ImaginaryOne * u;
                }
                if (i <= 80) zeros[i] = zero;
            }
            tail = new(count, value, zeros);
            if (_tails.Count < 25000) _tails.Add(key, tail);
        }
        int depth = Math.Min(tail.Depth, address.Count);
        double potential = tail.Potential;
        Complex z;
        if (_state.Kind == TranscendentalJuliaKind.Exponential)
        {
            z = tail.Depth >= address.Count && address.Count <= 80 ? tail.ZeroValues[address.Count] :
                potential - _kappa + new Complex(0, depth < address.Count ? 2 * Math.PI * address[depth] : 0);
            for (int i = depth - 1; i >= 0; i--)
            {
                if (z.Magnitude < 1e-250) return Invalid;
                z = Complex.Log(z) - _kappa + new Complex(0, i < address.Count ? 2 * Math.PI * address[i] : 0);
                if (!TranscendentalJuliaDynamics.IsFinite(z)) return Invalid;
            }
        }
        else
        {
            // z=i·w сопрягает λ sin(z) с λ sinh(w). Правый хвост имеет асимптотику w=t−log(λ/2).
            Complex w = potential - _kappa;
            if (depth < address.Count)
            {
                int followingSign = 1;
                int lambdaSign = Math.Cos(_kappa.Imaginary) >= 0 ? 1 : -1;
                for (int j = address.Count - 1; j > depth; j--)
                    followingSign *= ((address[j] & 1) == 0 ? 1 : -1) * lambdaSign;
                Complex principal = PrincipalAsymptote(w);
                z = new Complex(address[depth] * Math.PI, 0) +
                    ((address[depth] & 1) == 0 ? 1 : -1) * followingSign * Complex.ImaginaryOne * principal;
            }
            else z = tail.ZeroValues[address.Count];
            for (int i = depth - 1; i >= 0; i--)
            {
                if (i < address.Count) z = _map.Preimage(z, address[i]);
                else
                {
                    w = -Complex.ImaginaryOne * z;
                    Complex u = -Complex.ImaginaryOne * Complex.Asin(Complex.ImaginaryOne * w / _map.Lambda);
                    if (u.Real < 0) u = -u + new Complex(0, Math.PI);
                    u += new Complex(0, 2 * Math.PI * Math.Round((-_kappa.Imaginary - u.Imaginary) / (2 * Math.PI)));
                    z = Complex.ImaginaryOne * u;
                }
                if (!TranscendentalJuliaDynamics.IsFinite(z)) return Invalid;
            }
            if (negative) z = -z;
        }
        return z;
    }

    private Complex PrincipalAsymptote(Complex right)
    {
        if (right.Imaginary < -Math.PI / 2) return -right - new Complex(0, Math.PI);
        if (right.Imaginary > Math.PI / 2) return -right + new Complex(0, Math.PI);
        return right;
    }
    private static Complex Invalid => new(double.NaN, double.NaN);

    /// <summary>Аналитическое продолжение одного прообраза вдоль уже выбранной ветви.</summary>
    public Complex ContinuePreimage(Complex w, Complex anchor, long initialBranch)
    {
        if (_state.Kind == TranscendentalJuliaKind.Exponential)
        {
            Complex principal = _map.Preimage(w, initialBranch);
            return principal + new Complex(0, 2 * Math.PI * Math.Round((anchor.Imaginary - principal.Imaginary) / (2 * Math.PI)));
        }
        Complex asin = Complex.Asin(w / _map.Lambda);
        long middle = (long)Math.Clamp(Math.Round(anchor.Real / Math.PI), -10_000_000_000.0, 10_000_000_000.0);
        Complex best = Invalid; double distance = double.PositiveInfinity;
        for (long n = middle - 2; n <= middle + 2; n++)
        {
            Complex candidate = new Complex(n * Math.PI, 0) + ((n & 1) == 0 ? 1 : -1) * asin;
            double next = (candidate - anchor).Magnitude;
            if (next < distance) { distance = next; best = candidate; }
        }
        return best;
    }

    public bool CrossesSingularValue(Complex a, Complex b)
    {
        double tolerance = 1e-9 * Math.Max(1, _map.Lambda.Magnitude);
        if (_state.Kind == TranscendentalJuliaKind.Exponential) return SegmentDistance(Complex.Zero, a, b) < tolerance;
        return SegmentDistance(_map.Lambda, a, b) < tolerance || SegmentDistance(-_map.Lambda, a, b) < tolerance;
    }
    private static double SegmentDistance(Complex p, Complex a, Complex b)
    {
        Complex delta = b - a; double length = delta.Magnitude;
        if (length == 0) return (p - a).Magnitude;
        Complex unit = delta / length;
        double t = Math.Clamp(((p - a) * Complex.Conjugate(unit)).Real, 0, length);
        return (p - a - t * unit).Magnitude;
    }

    public IReadOnlyList<TranscendentalRay> Build(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _work = SplitCount = CompletedDepth = 0; BudgetReached = false;
        if (_map.Lambda == Complex.Zero) return [];
        var result = new List<TranscendentalRay>();
        var current = new List<(long[] Address, bool Negative)> { ([], false) };
        if (_state.Kind == TranscendentalJuliaKind.Sine) current.Add(([], true));
        int perDepth = Math.Max(4, _rayLimit / Math.Max(1, _state.InverseDepth));
        long middle = (long)Math.Round((_state.Kind == TranscendentalJuliaKind.Exponential ? _state.CenterY / (2 * Math.PI) : _state.CenterX / Math.PI));
        long[] branches = Enumerable.Range(-_state.BranchRadius, 2 * _state.BranchRadius + 1)
            .SelectMany(k => new[] { (long)k, middle + k }).Distinct().OrderBy(k => Math.Abs(k - middle)).ThenBy(k => k).ToArray();
        for (int depth = 0; depth <= _state.InverseDepth && result.Count < _rayLimit; depth++)
        {
            var candidates = new List<((long[] Address, bool Negative) Definition, List<TranscendentalRay> Parts, double Score)>();
            foreach (var definition in current)
            {
                token.ThrowIfCancellationRequested();
                var parts = Trace(definition.Address, definition.Negative, token);
                if (parts.Count > 0) candidates.Add((definition, parts, Score(parts)));
                if (_work >= _budget) { BudgetReached = true; break; }
            }
            // Ячейки конечных точек дают равномерную выборку вместо предпочтения первых адресов.
            var pending = candidates.OrderBy(c => c.Score).ToList();
            var selected = new List<((long[] Address, bool Negative) Definition, List<TranscendentalRay> Parts, double Score)>();
            var occupied = new Dictionary<(int X, int Y), int>();
            (int X, int Y) Cell(List<TranscendentalRay> parts)
            {
                Complex point = parts.Select(p => p.Points[^1]).OrderBy(p => (p - new Complex(_state.CenterX, _state.CenterY)).Magnitude).First();
                return ((int)Math.Clamp(Math.Floor((point.Real - _state.CenterX) * _pixelsPerUnit / _width * 12 + 6), -1, 12),
                    (int)Math.Clamp(Math.Floor((point.Imaginary - _state.CenterY) * _pixelsPerUnit / _height * 8 + 4), -1, 8));
            }
            while (pending.Count > 0 && selected.Count < perDepth)
            {
                token.ThrowIfCancellationRequested();
                int best = 0; var bestRank = (Outside: int.MaxValue, Used: int.MaxValue, Score: double.PositiveInfinity);
                for (int i = 0; i < pending.Count; i++)
                {
                    var rank = (Outside: pending[i].Score > 0 ? 1 : 0, Used: occupied.GetValueOrDefault(Cell(pending[i].Parts)), Score: pending[i].Score);
                    if (rank.CompareTo(bestRank) < 0) { bestRank = rank; best = i; }
                }
                var chosen = pending[best]; pending.RemoveAt(best); selected.Add(chosen);
                var cell = Cell(chosen.Parts); occupied[cell] = occupied.GetValueOrDefault(cell) + 1;
            }
            foreach (var candidate in selected)
            {
                foreach (var part in candidate.Parts) { if (result.Count >= _rayLimit) break; result.Add(part); }
            }
            CompletedDepth = depth;
            if (BudgetReached || selected.Count == 0) break;
            current.Clear();
            foreach (var candidate in selected)
                foreach (long branch in branches)
                {
                    // Нулевой прообраз основного луча лишь повторяет этот же луч.
                    if (candidate.Definition.Address.Length == 0 && branch == 0) continue;
                    current.Add(([branch, .. candidate.Definition.Address], candidate.Definition.Negative));
                }
        }
        token.ThrowIfCancellationRequested();
        return result;
    }

    private double Score(IEnumerable<TranscendentalRay> parts)
    {
        double best = double.PositiveInfinity;
        double halfX = _width / (2 * _pixelsPerUnit), halfY = _height / (2 * _pixelsPerUnit);
        foreach (var part in parts)
            foreach (Complex z in part.Points)
            {
                double x = Math.Max(0, Math.Abs(z.Real - _state.CenterX) - halfX);
                double y = Math.Max(0, Math.Abs(z.Imaginary - _state.CenterY) - halfY);
                best = Math.Min(best, x * x + y * y);
            }
        return best;
    }

    private List<TranscendentalRay> Trace(long[] address, bool negative, CancellationToken token)
    {
        var parts = new List<TranscendentalRay>();
        var points = new List<Complex>();
        // Геометрическая сетка разрешает и длинный хвост, и окрестность конечной точки.
        double extent = _state.Kind == TranscendentalJuliaKind.Exponential
            ? _state.CenterX + _width / (2 * _pixelsPerUnit) + _kappa.Real
            : Math.Abs(_state.CenterY) + _height / (2 * _pixelsPerUnit) + _kappa.Real;
        double maximum = Math.Clamp(extent + 3, 8, 1e12), minimum = 0.006;
        Complex Sample(double t) { _work++; if ((_work & 255) == 0) token.ThrowIfCancellationRequested(); return Evaluate(t, address, negative); }
        void Finish()
        {
            if (points.Count > 1) parts.Add(new(points.ToArray(), [.. address], address.Length));
            points.Clear();
        }
        void Segment(double a, Complex za, double b, Complex zb, int level)
        {
            if (_work >= _budget) return;
            double middle = Math.Sqrt(a * b);
            Complex zm = Sample(middle);
            if (address.Length > 0 && _map.TryStep(za, out Complex criticalA) && _map.TryStep(zm, out Complex criticalM) &&
                _map.TryStep(zb, out Complex criticalB) && CrossesSingularValue(criticalA, criticalA) &&
                CrossesSingularValue(criticalM, criticalM) && CrossesSingularValue(criticalB, criticalB))
            {
                // Вся порция уже у критического значения/полюса. Дальнейшее деление
                // не восстанавливает однозначный лист и лишь расходует бюджет около конца.
                Finish(); SplitCount++; return;
            }
            if (address.Length > 0 && _map.TryStep(za, out Complex wa) && _map.TryStep(zm, out Complex wm) &&
                _map.TryStep(zb, out Complex wb) && !CrossesSingularValue(wa, wm) && !CrossesSingularValue(wm, wb))
            {
                // Поднимаем кривую на тот же лист, даже если principal Log/asin поменял ветвь.
                if (points.Count > 0) za = ContinuePreimage(wa, points[^1], address[0]);
                zm = ContinuePreimage(wm, za, address[0]);
                zb = ContinuePreimage(wb, zm, address[0]);
            }
            bool finite = TranscendentalJuliaDynamics.IsFinite(za) && TranscendentalJuliaDynamics.IsFinite(zb) && TranscendentalJuliaDynamics.IsFinite(zm);
            double deviation = finite ? SegmentDistance(zm, za, zb) * _pixelsPerUnit : double.PositiveInfinity;
            double length = finite ? (za - zb).Magnitude * _pixelsPerUnit : double.PositiveInfinity;
            // Внутренние образы тоже должны продолжаться непрерывно: скрытый перескок ветви не маскируем.
            bool jump = finite && HasBranchJump(za, zm, zb, address.Length);
            bool nearby = finite && NearView(za, zb);
            if (level < 18 && (!finite || jump || deviation > _state.RayAccuracy || nearby && length > 12))
            { Segment(a, za, middle, zm, level + 1); Segment(middle, zm, b, zb, level + 1); return; }
            if (!finite || jump || deviation > _state.RayAccuracy * 2)
            { Finish(); SplitCount++; return; }
            if (points.Count == 0) points.Add(za);
            points.Add(zb);
        }
        const int intervals = 48;
        double previous = maximum; Complex last = Sample(previous);
        for (int i = 1; i <= intervals && _work < _budget; i++)
        {
            double next = maximum * Math.Pow(minimum / maximum, i / (double)intervals);
            Complex value = Sample(next);
            Segment(previous, last, next, value, 0);
            previous = next; last = value;
        }
        Finish(); return parts;
    }

    private bool NearView(Complex a, Complex b)
    {
        double halfX = _width / _pixelsPerUnit, halfY = _height / _pixelsPerUnit;
        return Math.Min(a.Real, b.Real) < _state.CenterX + halfX && Math.Max(a.Real, b.Real) > _state.CenterX - halfX &&
            Math.Min(a.Imaginary, b.Imaginary) < _state.CenterY + halfY && Math.Max(a.Imaginary, b.Imaginary) > _state.CenterY - halfY;
    }
    private bool HasBranchJump(Complex a, Complex middle, Complex b, int depth)
    {
        for (int i = 0; i < depth; i++)
        {
            if (!_map.TryStep(a, out Complex na) || !_map.TryStep(middle, out Complex nm) || !_map.TryStep(b, out Complex nb)) return false;
            if (CrossesSingularValue(na, nm) || CrossesSingularValue(nm, nb)) return true;
            double span = _state.Kind == TranscendentalJuliaKind.Exponential
                ? Math.Max(Math.Abs(a.Imaginary - middle.Imaginary), Math.Abs(middle.Imaginary - b.Imaginary))
                : Math.Max(Math.Abs(a.Real - middle.Real), Math.Abs(middle.Real - b.Real));
            if (span > Math.PI) return true;
            a = na; middle = nm; b = nb;
        }
        return false;
    }
}
