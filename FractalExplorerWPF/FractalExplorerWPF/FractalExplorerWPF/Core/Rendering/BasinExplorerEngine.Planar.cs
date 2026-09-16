using System.Numerics;
using System.Windows.Media;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

public sealed partial class BasinExplorerEngine
{
    private PlanarBasinSettings _planar = new();
    private Func<Complex, Complex>? _planarField;
    private Func<double, double, double>? _potential;
    private Func<Complex, (double A, double B, double C, double D)>? _planarJacobian;
    private List<PlanarBasinAttractor> _planarAttractors = [];
    private Complex[] _sectionNormals = [];
    public bool IsPlanar => BasinExplorerCatalog.UsesPlanar(Kind);
    public IReadOnlyList<PlanarBasinAttractor> PlanarAttractors => _planarAttractors;
    public PlanarBasinSettings PlanarSettings => _planar.Clone();

    public void ConfigurePlanar(PlanarBasinSettings settings, string complexFormula,
        IReadOnlyList<PlanarBasinAttractor>? savedAttractors = null, CancellationToken token = default)
    {
        _planarField = null;
        _planarAttractors = [];
        _sectionNormals = [];
        _planar = settings.Clone();
        ValidatePlanarSettings();
        RootSearchRadius = _planar.SearchRadius;
        if (Kind == BasinExplorerKind.ComplexGradientFlow)
        {
            if (!SetRootFormula(complexFormula, out string debug, savedAttractors is null))
                throw new InvalidOperationException(debug);
            _potential = (x, y) => 0.5 * MagnitudeSquared(_function!.Evaluate(new(x, y)));
            _planarField = z => -_function!.Evaluate(z) * Complex.Conjugate(_firstDerivative!.Evaluate(z));
            _planarJacobian = z =>
            {
                double squared = MagnitudeSquared(_firstDerivative!.Evaluate(z));
                Complex product = _function!.Evaluate(z) * Complex.Conjugate(_secondDerivative!.Evaluate(z));
                return (-squared - product.Real, -product.Imaginary, -product.Imaginary, -squared + product.Real);
            };
            if (savedAttractors is null)
                foreach (Complex root in Roots)
                    if (_function!.Evaluate(root).Magnitude < 1e-7)
                        AddPlanarAttractor(new() { Points = [root] });
        }
        else
        {
            ExpressionNode fx, fy;
            if (Kind == BasinExplorerKind.GradientDescent)
            {
                ExpressionNode potential = CompiledRealPlaneExpression.Parse(_planar.Potential);
                _potential = CompiledRealPlaneExpression.Compile(potential);
                fx = new UnaryOpNode("-", potential.Differentiate("x")).Simplify();
                fy = new UnaryOpNode("-", potential.Differentiate("y")).Simplify();
            }
            else
            {
                fx = CompiledRealPlaneExpression.Parse(_planar.FieldX, polynomialOnly: true);
                fy = CompiledRealPlaneExpression.Parse(_planar.FieldY, polynomialOnly: true);
                _potential = null;
            }
            var xField = CompiledRealPlaneExpression.Compile(fx);
            var yField = CompiledRealPlaneExpression.Compile(fy);
            var xx = CompiledRealPlaneExpression.Compile(fx.Differentiate("x").Simplify());
            var xy = CompiledRealPlaneExpression.Compile(fx.Differentiate("y").Simplify());
            var yx = CompiledRealPlaneExpression.Compile(fy.Differentiate("x").Simplify());
            var yy = CompiledRealPlaneExpression.Compile(fy.Differentiate("y").Simplify());
            _planarField = z => new(xField(z.Real, z.Imaginary), yField(z.Real, z.Imaginary));
            _planarJacobian = z => (xx(z.Real, z.Imaginary), xy(z.Real, z.Imaginary), yx(z.Real, z.Imaginary), yy(z.Real, z.Imaginary));
        }
        if (savedAttractors is not null)
        {
            foreach (PlanarBasinAttractor attractor in savedAttractors)
            {
                if (attractor.Points.Count == 0 || attractor.Points.Count > 20000 || attractor.Points.Any(p => !IsFinite(p)) ||
                    !double.IsFinite(attractor.Period) || attractor.Period < 0 ||
                    (attractor.IsCycle && (attractor.Points.Count < 4 || !double.IsFinite(attractor.TransverseMultiplier) ||
                                           attractor.TransverseMultiplier is < 0 or >= 1)))
                    throw new InvalidOperationException("Некорректный сохранённый аттрактор.");
                _planarAttractors.Add(attractor.Clone());
            }
        }
        else DiscoverPlanarAttractors(token);
        RebuildPlanarSections();
        DebugInfo = Kind switch
        {
            BasinExplorerKind.GradientDescent => $"V(x,y) = {_planar.Potential}\nМетод: {_planar.Optimizer}; α = {_planar.LearningRate:G6}; начальная память = 0.\nМинимумы: положительно определённый гессиан; остановка по градиенту, расстоянию и шагу, не по пересечению окрестности.",
            BasinExplorerKind.ComplexGradientFlow => $"V(z) = |{complexFormula}|²/2\nz′ = −f(z)·conj(f′(z)). RK 5(4), контроль локальной ошибки.\nКритические точки с f ≠ 0 не считаются минимумами.",
            _ => $"x′ = {_planar.FieldX}\ny′ = {_planar.FieldY}\nRK 5(4). Равновесия: tr J < 0, det J > 0. Циклы: повторные возвраты на секцию и exp(∮div F dt) < 1."
        };
        DebugInfo += $"\nАттракторов: {_planarAttractors.Count}. Поиск по конечной сетке ±{_planar.SearchRadius:G5}, полнота не гарантируется.\nЛимит времени/шагов оставляет медленные и нераспознанные траектории фоном.";
    }

    private void ValidatePlanarSettings()
    {
        static void Range(double v, double lo, double hi, string name)
        {
            if (!double.IsFinite(v) || v < lo || v > hi) throw new InvalidOperationException($"{name}: допустимо {lo:G4}…{hi:G4}.");
        }
        Range(_planar.LearningRate, 1e-6, 10, "Шаг оптимизации");
        Range(_planar.Momentum, 0, 0.9999, "Momentum / β₁");
        Range(_planar.Beta2, 0, 0.99999, "β₂");
        Range(_planar.AdamEpsilon, 1e-12, 0.1, "ε Adam");
        Range(_planar.TimeStep, 0.001, 1, "Максимальный шаг времени");
        Range(_planar.MaxTime, 0.01, 2000, "Максимальное время");
        Range(_planar.IntegrationTolerance, 1e-10, 1e-3, "Точность интегратора");
        Range(_planar.ConvergenceTolerance, 1e-8, 0.01, "Допуск сходимости");
        Range(_planar.SearchRadius, 0.1, 100, "Радиус поиска");
        Range(_planar.EscapeRadius, 2, 1e6, "Радиус ухода");
        if (!Enum.IsDefined(_planar.Optimizer)) throw new InvalidOperationException("Неизвестный оптимизатор.");
    }

    internal Complex EvaluatePlanarField(Complex point) => _planarField!(point);
    internal double EvaluatePlanarPotential(Complex point) => _potential!(point.Real, point.Imaginary);

    private void DiscoverPlanarAttractors(CancellationToken token)
    {
        if (Kind != BasinExplorerKind.ComplexGradientFlow)
        {
            for (int iy = 0; iy <= 12; iy++)
            for (int ix = 0; ix <= 12; ix++)
            {
                token.ThrowIfCancellationRequested();
                Complex seed = new(_planar.SearchRadius * (ix / 6.0 - 1), _planar.SearchRadius * (iy / 6.0 - 1));
                if (TryPlanarEquilibrium(seed, out Complex root)) AddPlanarAttractor(new() { Points = [root] });
            }
        }
        if (Kind == BasinExplorerKind.PolynomialVectorField)
        {
            // Несколько масштабов затравок нужны, в частности, для вложенных циклов.
            foreach (double fraction in new[] { 0.08, 0.2, 0.45, 0.8 })
            for (int i = 0; i < 8; i++)
            {
                token.ThrowIfCancellationRequested();
                Complex seed = Complex.FromPolarCoordinates(_planar.SearchRadius * fraction, i * Math.PI / 4);
                PlanarBasinAttractor? cycle = FindPlanarCycle(seed, token);
                if (cycle is not null) AddPlanarAttractor(cycle);
            }
        }
        _planarAttractors = _planarAttractors.OrderBy(a => a.IsCycle).ThenBy(a => a.Points.Average(p => p.Real))
            .ThenBy(a => a.Points.Average(p => p.Imaginary)).ThenBy(a => a.Period).ToList();
    }

    private bool TryPlanarEquilibrium(Complex seed, out Complex root)
    {
        root = seed;
        for (int i = 0; i < 80; i++)
        {
            Complex value = _planarField!(root);
            if (!IsFinite(value) || root.Magnitude > _planar.EscapeRadius) return false;
            if (value.Magnitude < 1e-10)
            {
                var (a, b, c, d) = _planarJacobian!(root);
                double det = a * d - b * c, scale = Math.Max(1, Math.Max(Math.Abs(a) + Math.Abs(b), Math.Abs(c) + Math.Abs(d)));
                return double.IsFinite(det) && a + d < -1e-8 * scale && det > 1e-10 * scale * scale;
            }
            var (aa, bb, cc, dd) = _planarJacobian!(root);
            double determinant = aa * dd - bb * cc;
            if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-18) return false;
            Complex step = new((dd * value.Real - bb * value.Imaginary) / determinant,
                (-cc * value.Real + aa * value.Imaginary) / determinant);
            if (!IsFinite(step)) return false;
            if (step.Magnitude > _planar.SearchRadius) step *= _planar.SearchRadius / step.Magnitude;
            bool accepted = false;
            for (int backtrack = 0; backtrack < 16; backtrack++)
            {
                Complex candidate = root - step;
                if (_planarField(candidate).Magnitude < value.Magnitude)
                { root = candidate; accepted = true; break; }
                step *= 0.5;
            }
            if (!accepted) return false;
        }
        return false;
    }

    private void AddPlanarAttractor(PlanarBasinAttractor candidate)
    {
        double tolerance = Math.Max(1e-4, 20 * _planar.ConvergenceTolerance);
        foreach (PlanarBasinAttractor known in _planarAttractors)
        {
            if (candidate.IsCycle != known.IsCycle) continue;
            if (!candidate.IsCycle && (candidate.Points[0] - known.Points[0]).Magnitude < tolerance) return;
            if (candidate.IsCycle && Math.Abs(candidate.Period - known.Period) < tolerance * (1 + known.Period) &&
                DistanceToCycle(candidate.Points[0], known.Points) < tolerance * (1 + candidate.Points[0].Magnitude)) return;
        }
        if (_planarAttractors.Count < 256) _planarAttractors.Add(candidate);
    }

    private void RebuildPlanarSections() => _sectionNormals = _planarAttractors.Select(a =>
    {
        Complex speed = _planarField!(a.Points[0]);
        return a.IsCycle && speed.Magnitude > 0 ? speed / speed.Magnitude : Complex.Zero;
    }).ToArray();

    private int PlanarPointTarget(Complex z, Complex field)
    {
        double tolerance = _planar.ConvergenceTolerance * (1 + z.Magnitude);
        if (field.Magnitude > tolerance) return -1;
        for (int i = 0; i < _planarAttractors.Count; i++)
            if (!_planarAttractors[i].IsCycle && (z - _planarAttractors[i].Points[0]).Magnitude <= 10 * tolerance)
                return i;
        return -1;
    }

    public BasinOrbitResult PlanarOrbit(double x, double y, CancellationToken token = default) =>
        Kind == BasinExplorerKind.GradientDescent ? OptimizerOrbit(new(x, y), token) : FlowOrbit(new(x, y), token);

    private BasinOrbitResult OptimizerOrbit(Complex z, CancellationToken token, List<Complex>? trace = null)
    {
        Complex velocity = Complex.Zero, moment = Complex.Zero, square = Complex.Zero;
        double beta1Power = 1, beta2Power = 1;
        int stable = 0;
        trace?.Add(z);
        for (int i = 0; i <= MaxIterations; i++)
        {
            if ((i & 31) == 0) token.ThrowIfCancellationRequested();
            if (!IsFinite(z)) return new(BasinOrbitOutcome.NonFinite, i, i, z);
            if (z.Magnitude > _planar.EscapeRadius) return new(BasinOrbitOutcome.Escaped, i, i, z);
            Complex gradient = -_planarField!(z);
            if (!IsFinite(gradient)) return new(BasinOrbitOutcome.NonFinite, i, i, z);
            int target = PlanarPointTarget(z, -gradient);
            stable = target >= 0 && velocity.Magnitude <= _planar.ConvergenceTolerance * (1 + z.Magnitude) ? stable + 1 : 0;
            if (stable >= 4) return new(BasinOrbitOutcome.Converged, i, i, z, target);
            if (i == MaxIterations) break;
            double rate = _planar.LearningRate, beta1 = _planar.Momentum, beta2 = _planar.Beta2;
            switch (_planar.Optimizer)
            {
                case BasinOptimizer.Momentum:
                    velocity = beta1 * velocity - rate * gradient;
                    break;
                case BasinOptimizer.Nesterov:
                    velocity = beta1 * velocity + rate * _planarField(z + beta1 * velocity);
                    break;
                case BasinOptimizer.Adam:
                    moment = beta1 * moment + (1 - beta1) * gradient;
                    square = beta2 * square + (1 - beta2) * new Complex(gradient.Real * gradient.Real, gradient.Imaginary * gradient.Imaginary);
                    beta1Power *= beta1; beta2Power *= beta2;
                    Complex corrected = moment / (1 - beta1Power), variance = square / (1 - beta2Power);
                    velocity = new(-rate * corrected.Real / (Math.Sqrt(variance.Real) + _planar.AdamEpsilon),
                        -rate * corrected.Imaginary / (Math.Sqrt(variance.Imaginary) + _planar.AdamEpsilon));
                    break;
                default: velocity = -rate * gradient; break;
            }
            z += velocity;
            trace?.Add(z);
        }
        return new(BasinOrbitOutcome.IterationLimit, MaxIterations, MaxIterations, z);
    }

    private BasinOrbitResult FlowOrbit(Complex z, CancellationToken token, List<Complex>? trace = null)
    {
        double time = 0, h = _planar.TimeStep;
        int cycleCandidate = -1, cycleHits = 0;
        double previousHit = -1;
        trace?.Add(z);
        int i = 0;
        for (; i < MaxIterations && time < _planar.MaxTime; i++)
        {
            if ((i & 15) == 0) token.ThrowIfCancellationRequested();
            if (!IsFinite(z)) return new(BasinOrbitOutcome.NonFinite, i, time, z);
            if (z.Magnitude > _planar.EscapeRadius) return new(BasinOrbitOutcome.Escaped, i, time, z);
            Complex speed = _planarField!(z);
            if (!IsFinite(speed)) return new(BasinOrbitOutcome.NonFinite, i, time, z);
            int target = PlanarPointTarget(z, speed);
            if (target >= 0) return new(BasinOrbitOutcome.Converged, i, time, z, target);
            if (!PlanarFlowIntegrator.Step(_planarField, z, ref h, _planar.TimeStep, _planar.IntegrationTolerance,
                    _planar.MaxTime - time, out Complex next, out double dt, token))
                return new(BasinOrbitOutcome.Degenerate, i, time, z);
            for (int index = 0; index < _planarAttractors.Count; index++)
            {
                PlanarBasinAttractor a = _planarAttractors[index];
                if (!a.IsCycle || !CrossesSection(z, next, a.Points[0], _sectionNormals[index])) continue;
                (Complex crossing, double fraction) = RefineSection(z, dt, a.Points[0], _sectionNormals[index]);
                double tolerance = Math.Max(10 * _planar.ConvergenceTolerance, 30 * _planar.IntegrationTolerance) * (1 + crossing.Magnitude);
                double hitTime = time + dt * fraction;
                if ((crossing - a.Points[0]).Magnitude > tolerance) { if (cycleCandidate == index) cycleHits = 0; continue; }
                cycleHits = cycleCandidate == index && Math.Abs(hitTime - previousHit - a.Period) < Math.Max(1e-3, tolerance) * (1 + a.Period)
                    ? cycleHits + 1 : 1;
                cycleCandidate = index; previousHit = hitTime;
                if (cycleHits >= 3)
                {
                    trace?.Add(crossing);
                    return new(BasinOrbitOutcome.Converged, i + 1, hitTime, crossing, index);
                }
            }
            z = next; time += dt;
            trace?.Add(z);
        }
        return new(BasinOrbitOutcome.IterationLimit, i, time, z);
    }

    private Color PlanarResultColor(BasinOrbitResult result)
    {
        if (ColoringMode == BasinColoringMode.OrbitOutcome) return OutcomeColor(result);
        if (ColoringMode == BasinColoringMode.IterationCount)
        {
            if (result.Outcome is not (BasinOrbitOutcome.Converged or BasinOrbitOutcome.Escaped)) return BackgroundColor;
            double budget = Kind == BasinExplorerKind.GradientDescent ? MaxIterations : _planar.MaxTime;
            double t = Math.Clamp(Math.Log(1 + result.SmoothIterations) / Math.Log(1 + budget), 0, 1);
            return ColorFromHsv(240 * (1 - t), 0.9, 0.95);
        }
        if (result.Outcome != BasinOrbitOutcome.Converged || result.TargetIndex < 0) return BackgroundColor;
        Color color = TargetColor(result.TargetIndex);
        return ColoringMode == BasinColoringMode.Basins ? color : ShadeBySpeed(color, result.SmoothIterations);
    }

    private IReadOnlyList<Complex> TracePlanarOrbit(double x, double y, int maxPoints)
    {
        var trace = new List<Complex>();
        if (Kind == BasinExplorerKind.GradientDescent) OptimizerOrbit(new(x, y), default, trace);
        else FlowOrbit(new(x, y), default, trace);
        int count = Math.Clamp(maxPoints, 2, 4096);
        return trace.Count <= count ? trace : Enumerable.Range(0, count)
            .Select(i => trace[(int)((long)i * (trace.Count - 1) / (count - 1))]).ToArray();
    }
}
