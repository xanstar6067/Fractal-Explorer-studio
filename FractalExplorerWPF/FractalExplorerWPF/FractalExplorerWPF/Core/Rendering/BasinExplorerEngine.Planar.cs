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
    private EquilibriumTrap[] _equilibriumTraps = [];
    public bool IsPlanar => BasinExplorerCatalog.UsesPlanar(Kind);
    public IReadOnlyList<PlanarBasinAttractor> PlanarAttractors => _planarAttractors;
    public PlanarBasinSettings PlanarSettings => _planar.Clone();

    public void ConfigurePlanar(PlanarBasinSettings settings, string complexFormula,
        IReadOnlyList<PlanarBasinAttractor>? savedAttractors = null, CancellationToken token = default)
    {
        _planarField = null;
        _planarAttractors = [];
        _sectionNormals = [];
        _equilibriumTraps = [];
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
        DebugInfo += $"\nАттракторов: {_planarAttractors.Count}. Поиск по конечной сетке ±{_planar.SearchRadius:G5}, полнота не гарантируется.\nЛимит времени/шагов оставляет медленные и нераспознанные траектории фоном." +
            (Kind == BasinExplorerKind.GradientDescent ? string.Empty
                : "\nУстойчивое равновесие захватывает и эллипс Ляпунова, в котором поле направлено внутрь; цикл — сжатие возвратов на секцию в μ раз за оборот.");
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
            // Шаг сетки — 1/20 радиуса: при редкой сетке Ньютон из затравок у потенциалов с частыми
            // минимумами приходит в сёдла и максимумы, и часть минимумов теряется.
            const int half = 20;
            for (int iy = 0; iy <= 2 * half; iy++)
            for (int ix = 0; ix <= 2 * half; ix++)
            {
                token.ThrowIfCancellationRequested();
                Complex seed = new(_planar.SearchRadius * ((double)ix / half - 1), _planar.SearchRadius * ((double)iy / half - 1));
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
                (DistanceToCycle(candidate.Points[0], known.Points) < tolerance * (1 + candidate.Points[0].Magnitude) ||
                 PassesThroughCycleAnchor(candidate.Points[0], known))) return;
        }
        if (_planarAttractors.Count < 256) _planarAttractors.Add(candidate);
    }

    private void RebuildPlanarSections()
    {
        _sectionNormals = _planarAttractors.Select(a =>
        {
            Complex speed = _planarField!(a.Points[0]);
            return a.IsCycle && speed.Magnitude > 0 ? speed / speed.Magnitude : Complex.Zero;
        }).ToArray();
        _equilibriumTraps = new EquilibriumTrap[_planarAttractors.Count];
        // Ловушки опираются на непрерывность времени; у дискретного оптимизатора шаг может
        // перепрыгнуть границу эллипса, поэтому там остаётся только допуск сходимости.
        if (Kind == BasinExplorerKind.GradientDescent) return;
        for (int i = 0; i < _planarAttractors.Count; i++)
            if (!_planarAttractors[i].IsCycle) _equilibriumTraps[i] = BuildEquilibriumTrap(i);
    }

    private int PlanarPointTarget(Complex z, Complex field)
    {
        for (int i = 0; i < _equilibriumTraps.Length; i++)
            if (_equilibriumTraps[i].Contains(z - _planarAttractors[i].Points[0])) return i;
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
        double time = 0, h = _planar.TimeStep, endTime = PlanarEndTime;
        int cycleCandidate = -1, nearHits = 0, contractions = 0;
        double previousHit = -1, previousOffset = 0;
        trace?.Add(z);
        int i = 0;
        for (; i < MaxIterations && time < endTime; i++)
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
                Complex anchor = a.Points[0], normal = _sectionNormals[index];
                if (!a.IsCycle || !CrossesSection(z, next, anchor, normal)) continue;
                (Complex crossing, double fraction) = RefineSection(z, dt, anchor, normal);
                double hitTime = time + dt * fraction;
                double distance = (crossing - anchor).Magnitude;
                // Дальше этой окрестности возвраты на секцию не сравниваются с линейной моделью цикла.
                if (distance > 0.1 * (1 + anchor.Magnitude))
                {
                    if (cycleCandidate == index) { nearHits = 0; contractions = 0; }
                    continue;
                }
                double tolerance = Math.Max(10 * _planar.ConvergenceTolerance, 30 * _planar.IntegrationTolerance) * (1 + crossing.Magnitude);
                double offset = Dot(crossing - anchor, new Complex(-normal.Imaginary, normal.Real));
                bool sameCycle = cycleCandidate == index;
                double returnTime = hitTime - previousHit;
                if (distance > tolerance) nearHits = 0;
                else nearHits = sameCycle && nearHits > 0 &&
                    Math.Abs(returnTime - a.Period) < Math.Max(1e-3, tolerance) * (1 + a.Period) ? nearHits + 1 : 1;
                // Слабо притягивающий цикл не успевает подойти на допуск за отведённое время, но уже
                // в линейном режиме: смещение вдоль секции за оборот убывает ровно в μ раз. Сильно
                // притягивающим циклам (μ < 0.01) это не нужно: они и так за пару оборотов входят в
                // допуск, а их отношение смещений в нелинейной зоне шумит и дробило бы яркость.
                bool contracting = sameCycle && a.TransverseMultiplier >= 0.01 && Math.Abs(returnTime - a.Period) < 0.05 * (1 + a.Period) &&
                    offset * previousOffset > 0 && Math.Abs(offset) < Math.Abs(previousOffset) &&
                    Math.Abs(Math.Abs(offset / previousOffset) - a.TransverseMultiplier) <= 0.25 * a.TransverseMultiplier + 0.02;
                contractions = contracting ? contractions + 1 : 0;
                cycleCandidate = index; previousHit = hitTime; previousOffset = offset;
                if (nearHits >= 3 || contractions >= 2)
                {
                    trace?.Add(crossing);
                    // Время для яркости — как у захвата по допуску: момент третьего возврата в допуск,
                    // предсказанный по μ. Иначе оно зависело бы от того, на каком обороте сработала
                    // проверка сжатия, и яркость бассейна шла бы зубцами.
                    double captureTime = nearHits >= 3 ? hitTime
                        : hitTime + a.Period * (2 + Math.Max(0, Math.Log(distance / tolerance) / -Math.Log(Math.Max(1e-300, a.TransverseMultiplier))));
                    return new(BasinOrbitOutcome.Converged, i + 1, captureTime, crossing, index);
                }
            }
            z = next; time += dt;
            trace?.Add(z);
        }
        return new(BasinOrbitOutcome.IterationLimit, i, time, z);
    }

    /// <summary>
    /// Момент остановки чуть раньше лимита: иначе остаток времени после округлений (~1e-13)
    /// меньше минимального шага интегратора, и исчерпанный лимит выглядел бы вырожденным шагом.
    /// </summary>
    private double PlanarEndTime => _planar.MaxTime * (1 - 1e-9);

    /// <summary>
    /// Эллипс δᵀPδ ≤ s² вокруг устойчивого равновесия, на границах которого поле во всех
    /// проверенных направлениях заметно смотрит внутрь. P решает уравнение Ляпунова
    /// JᵀP + PJ = −I; для вырожденного J (например, у кратного корня) берётся P = I. Эллипсы
    /// проверяются от окрестности допуска наружу с шагом ×1.5, поэтому вошедшая траектория
    /// из эллипса не выходит и приходит к равновесию. Так медленная спираль или кратный корень
    /// не остаются фоном лишь потому, что не успели подойти на допуск за отведённое время.
    /// Проверка выборочная (48 направлений на кольцо) — численная оценка, а не доказательство.
    /// </summary>
    private EquilibriumTrap BuildEquilibriumTrap(int index)
    {
        Complex point = _planarAttractors[index].Points[0];
        var (a, b, c, d) = _planarJacobian!(point);
        double p11 = 1, p12 = 0, p22 = 1;
        // Правило Крамера для [2a 2c 0; b a+d c; 0 2b 2d]·(p11, p12, p22) = (−1, 0, −1).
        double det = 2 * a * (2 * d * (a + d) - 2 * b * c) - 4 * b * c * d;
        double size = Math.Abs(a) + Math.Abs(b) + Math.Abs(c) + Math.Abs(d);
        if (double.IsFinite(det) && size > 0 && Math.Abs(det) > 1e-12 * size * size * size)
        {
            double q11 = (-2 * d * (a + d) + 2 * b * c - 2 * c * c) / det;
            double q12 = (2 * a * c + 2 * b * d) / det;
            double q22 = (-2 * a * (a + d) + 2 * b * c - 2 * b * b) / det;
            if (double.IsFinite(q11) && double.IsFinite(q12) && double.IsFinite(q22) && q11 > 0 && q11 * q22 - q12 * q12 > 0)
            {
                double norm = Math.Max(q11, q22);
                (p11, p12, p22) = (q11 / norm, q12 / norm, q22 / norm);
            }
        }
        var shape = new EquilibriumTrap(p11, p12, p22, 0);
        double spread = Math.Sqrt(Math.Pow((p11 - p22) / 2, 2) + p12 * p12);
        double lambdaMin = (p11 + p22) / 2 - spread;
        if (!(lambdaMin > 1e-12)) return shape;
        double l11 = Math.Sqrt(p11), l21 = p12 / l11, l22 = Math.Sqrt(Math.Max(1e-300, p22 - l21 * l21));
        double level = 0;
        // Первое кольцо — на границе обычного допуска сходимости, последнее — в пределах радиуса ухода.
        for (double s = 20 * _planar.ConvergenceTolerance * (1 + point.Magnitude) * Math.Sqrt(lambdaMin);
             s / Math.Sqrt(lambdaMin) < _planar.EscapeRadius / 2; s *= 1.5)
        {
            bool inward = true;
            for (int k = 0; k < 48 && inward; k++)
            {
                double angle = k * Math.PI / 24, ux = Math.Cos(angle), uy = Math.Sin(angle);
                double dx = s * (ux / l11 - l21 * uy / (l11 * l22)), dy = s * uy / l22;
                Complex field = _planarField!(point + new Complex(dx, dy));
                if (!IsFinite(field)) { inward = false; break; }
                double derivative = 2 * ((p11 * dx + p12 * dy) * field.Real + (p12 * dx + p22 * dy) * field.Imaginary);
                double fieldNorm = Math.Sqrt(Math.Max(0,
                    p11 * field.Real * field.Real + 2 * p12 * field.Real * field.Imaginary + p22 * field.Imaginary * field.Imaginary));
                // Косинус угла между полем и внешней нормалью эллипса (в метрике P) не больше −0.02.
                inward = derivative < -0.04 * s * fieldNorm;
            }
            var candidate = shape with { Level = s * s };
            for (int other = 0; other < _planarAttractors.Count && inward; other++)
                if (other != index && _planarAttractors[other].Points.Any(p => candidate.Contains(p - point)))
                    inward = false;
            if (!inward) break;
            level = s * s;
        }
        return shape with { Level = level };
    }

    private readonly record struct EquilibriumTrap(double P11, double P12, double P22, double Level)
    {
        public bool Contains(Complex offset) => Level > 0 &&
            P11 * offset.Real * offset.Real + 2 * P12 * offset.Real * offset.Imaginary + P22 * offset.Imaginary * offset.Imaginary <= Level;
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
