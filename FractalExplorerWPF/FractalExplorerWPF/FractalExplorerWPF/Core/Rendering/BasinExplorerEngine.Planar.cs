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
    private DescentTrap[] _descentTraps = [];
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
        _descentTraps = [];
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
            BasinExplorerKind.GradientDescent => $"V(x,y) = {_planar.Potential}\nМетод: {_planar.Optimizer}; α = {_planar.LearningRate:G6}; начальная память = 0.\nМинимумы: положительно определённый гессиан, а вырожденные — если антиградиент на вложенных окружностях смотрит внутрь. Остановка по градиенту, расстоянию и шагу; у вырожденного минимума — по удержанию ниже уровня V на границе проверенной окрестности.",
            BasinExplorerKind.ComplexGradientFlow => $"V(z) = |{complexFormula}|²/2\nz′ = −f(z)·conj(f′(z)). RK 5(4), контроль локальной ошибки.\nКритические точки с f ≠ 0 не считаются минимумами.",
            _ => $"x′ = {_planar.FieldX}\ny′ = {_planar.FieldY}\nRK 5(4). Равновесия: tr J < 0, det J > 0 или, при вырожденном J, поле внутрь на вложенных окружностях. Циклы: повторные возвраты на секцию и exp(∮div F dt) < 1."
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
        // Сходимость — по длине шага, а не по |F|: у вырожденного равновесия Ньютон сходится
        // линейно, и малое |F| достигается далеко от точки (у x³ при |F| = 1e-10 — в 3e-4 от неё).
        for (int i = 0; i < 400; i++)
        {
            Complex value = _planarField!(root);
            if (!IsFinite(value) || root.Magnitude > _planar.EscapeRadius) return false;
            if (value == Complex.Zero) return IsStableEquilibrium(root);
            var (a, b, c, d) = _planarJacobian!(root);
            double determinant = a * d - b * c;
            if (!double.IsFinite(determinant) || determinant == 0) return false;
            Complex step = new((d * value.Real - b * value.Imaginary) / determinant,
                (-c * value.Real + a * value.Imaginary) / determinant);
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
            // Без уменьшения |F| дальше только шум округления: это корень, если |F| уже мало.
            if (!accepted) return value.Magnitude < 1e-10 && IsStableEquilibrium(root);
            if (step.Magnitude <= 1e-13 * (1 + root.Magnitude)) return IsStableEquilibrium(root);
        }
        return false;
    }

    /// <summary>
    /// Гиперболическое равновесие классифицирует якобиан: tr J &lt; 0, det J &gt; 0. Явное седло или
    /// источник отбрасываются. Остальное вырождено (у минимума x⁴ + y⁴ гессиан нулевой), и
    /// линеаризация ничего не решает — тогда нужна ловушка: поле смотрит внутрь хотя бы на шести
    /// вложенных окружностях от окрестности допуска (до ~7.6 её радиуса). Так проходят вырожденные
    /// минимумы и устойчивые узлы, но не «обезьянье седло» и не линии минимумов вроде V = x².
    /// </summary>
    private bool IsStableEquilibrium(Complex point) => EquilibriumClass(point) switch
    {
        EquilibriumKind.Stable => true,
        EquilibriumKind.Degenerate => BuildEquilibriumTrap(point, -1, maxRings: 6).Level >= Math.Pow(Math.Pow(1.5, 5) * FirstTrapRing(point), 2),
        _ => false
    };

    private enum EquilibriumKind { Stable, Unstable, Degenerate }

    private EquilibriumKind EquilibriumClass(Complex point)
    {
        var (a, b, c, d) = _planarJacobian!(point);
        double det = a * d - b * c, trace = a + d;
        double scale = Math.Max(1, Math.Max(Math.Abs(a) + Math.Abs(b), Math.Abs(c) + Math.Abs(d)));
        if (!double.IsFinite(det) || !double.IsFinite(trace)) return EquilibriumKind.Unstable;
        if (trace < -1e-8 * scale && det > 1e-10 * scale * scale) return EquilibriumKind.Stable;
        if (det < -1e-10 * scale * scale || trace > 1e-8 * scale) return EquilibriumKind.Unstable;
        return EquilibriumKind.Degenerate;
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
        _descentTraps = new DescentTrap[_planarAttractors.Count];
        for (int i = 0; i < _planarAttractors.Count; i++)
        {
            if (_planarAttractors[i].IsCycle) continue;
            // Ловушки потоков опираются на непрерывность времени; у дискретного оптимизатора шаг
            // может перепрыгнуть границу эллипса. Поэтому у строгих минимумов там остаётся допуск
            // сходимости, а к вырожденным, куда спуск идёт сублинейно, — ловушка с ограничением шага.
            if (Kind != BasinExplorerKind.GradientDescent) _equilibriumTraps[i] = BuildEquilibriumTrap(_planarAttractors[i].Points[0], i);
            else if (EquilibriumClass(_planarAttractors[i].Points[0]) != EquilibriumKind.Stable) _descentTraps[i] = BuildDescentTrap(i);
        }
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
        int stable = 0, heldTrap = -1, held = 0;
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
            if (_descentTraps.Length > 0)
            {
                int trap = DescentTrapTarget(z, velocity, out bool certain);
                held = trap >= 0 && trap == heldTrap ? held + 1 : trap >= 0 ? 1 : 0;
                heldTrap = trap;
                if (trap >= 0 && (certain || held >= DescentHoldIterations))
                    return new(BasinOrbitOutcome.Converged, i, i, z, trap);
            }
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
    private EquilibriumTrap BuildEquilibriumTrap(Complex point, int self, int maxRings = int.MaxValue)
    {
        var (a, b, c, d) = _planarJacobian!(point);
        double p11 = 1, p12 = 0, p22 = 1;
        // Правило Крамера для [2a 2c 0; b a+d c; 0 2b 2d]·(p11, p12, p22) = (−1, 0, −1).
        double det = 2 * a * (2 * d * (a + d) - 2 * b * c) - 4 * b * c * d;
        double size = Math.Abs(a) + Math.Abs(b) + Math.Abs(c) + Math.Abs(d);
        // У вырожденного равновесия якобиан почти нулевой, и решение уравнения Ляпунова по нему —
        // произвольно вытянутый эллипс; там проверяется круг.
        if (EquilibriumClass(point) == EquilibriumKind.Stable &&
            double.IsFinite(det) && size > 0 && Math.Abs(det) > 1e-12 * size * size * size)
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
        double level = 0;
        // Первое кольцо — на границе обычного допуска сходимости, последнее — в пределах радиуса ухода.
        int rings = 0;
        for (double s = FirstTrapRing(point) * Math.Sqrt(lambdaMin); s / Math.Sqrt(lambdaMin) < _planar.EscapeRadius / 2 && rings < maxRings; s *= 1.5, rings++)
        {
            bool inward = true;
            for (int k = 0; k < 48 && inward; k++)
            {
                Complex offset = shape.Offset(s, k * Math.PI / 24);
                double dx = offset.Real, dy = offset.Imaginary;
                Complex field = _planarField!(point + offset);
                if (!IsFinite(field)) { inward = false; break; }
                double derivative = 2 * ((p11 * dx + p12 * dy) * field.Real + (p12 * dx + p22 * dy) * field.Imaginary);
                double fieldNorm = Math.Sqrt(Math.Max(0,
                    p11 * field.Real * field.Real + 2 * p12 * field.Real * field.Imaginary + p22 * field.Imaginary * field.Imaginary));
                // Косинус угла между полем и внешней нормалью эллипса (в метрике P) не больше −0.02.
                inward = derivative < -0.04 * s * fieldNorm;
            }
            var candidate = shape with { Level = s * s };
            for (int other = 0; other < _planarAttractors.Count && inward; other++)
                if (other != self && _planarAttractors[other].Points.Any(p => candidate.Contains(p - point)))
                    inward = false;
            if (!inward) break;
            level = s * s;
        }
        return shape with { Level = level };
    }

    /// <summary>Радиус первого кольца ловушки — граница обычного допуска сходимости.</summary>
    private double FirstTrapRing(Complex point) => 20 * _planar.ConvergenceTolerance * (1 + point.Magnitude);

    private readonly record struct EquilibriumTrap(double P11, double P12, double P22, double Level)
    {
        public bool Contains(Complex offset, double fraction = 1) => Level > 0 &&
            P11 * offset.Real * offset.Real + 2 * P12 * offset.Real * offset.Imaginary + P22 * offset.Imaginary * offset.Imaginary <= Level * fraction;

        /// <summary>Точка эллипса δᵀPδ = s² в направлении угла (через разложение Холецкого P = LLᵀ).</summary>
        public Complex Offset(double s, double angle)
        {
            double l11 = Math.Sqrt(P11), l21 = P12 / l11, l22 = Math.Sqrt(Math.Max(1e-300, P22 - l21 * l21));
            double ux = Math.Cos(angle), uy = Math.Sin(angle);
            return new(s * (ux / l11 - l21 * uy / (l11 * l22)), s * uy / l22);
        }

        /// <summary>Наименьшая полуось эллипса уровня <see cref="Level"/> на плоскости.</summary>
        public double MinorRadius => Math.Sqrt(Level / ((P11 + P22) / 2 + Math.Sqrt(Math.Pow((P11 - P22) / 2, 2) + P12 * P12)));
    }

    /// <summary>
    /// Окрестность вырожденного минимума для дискретных оптимизаторов: эллипс ловушки потока −∇V,
    /// порог потенциала посередине между V(p) и минимумом V на его границе, наибольшие кривизна
    /// (норма гессиана) и |∇V| внутри.
    /// </summary>
    private readonly record struct DescentTrap(EquilibriumTrap Shape, double PotentialBound, double MinimumPotential,
        double Curvature, double MaxGradient)
    {
        public bool IsActive => Shape.Level > 0;
    }

    private const int DescentHoldIterations = 64;

    private DescentTrap BuildDescentTrap(int index)
    {
        Complex point = _planarAttractors[index].Points[0];
        EquilibriumTrap shape = BuildEquilibriumTrap(point, index);
        if (shape.Level <= 0 || _potential is null) return default;
        double outer = Math.Sqrt(shape.Level), minimum = EvaluatePlanarPotential(point);
        double boundary = double.PositiveInfinity, curvature = 0, gradient = 0;
        for (double s = FirstTrapRing(point); ; s = Math.Min(outer, s * 1.5))
        {
            for (int k = 0; k < 96; k++)
            {
                Complex q = point + shape.Offset(s, k * Math.PI / 48);
                var (a, b, c, d) = _planarJacobian!(q);
                curvature = Math.Max(curvature, Math.Sqrt(a * a + b * b + c * c + d * d));
                gradient = Math.Max(gradient, _planarField!(q).Magnitude);
                if (s == outer) boundary = Math.Min(boundary, EvaluatePlanarPotential(q));
            }
            if (s == outer) break;
        }
        if (!(boundary > minimum) || !double.IsFinite(curvature) || !double.IsFinite(gradient)) return default;
        return new(shape, minimum + 0.5 * (boundary - minimum), minimum, curvature, gradient);
    }

    /// <summary>
    /// Вырожденный минимум, из окрестности которого оптимизатор уже не выйдет. <paramref name="certain"/>:
    /// для GD при α·L ≤ 1 потенциал монотонно убывает, для momentum при α·L ≤ 1 − β не растёт энергия
    /// тяжёлого шара V + β|v|²/(2α) — траектория ниже порога не поднимется до границы, а прыжок
    /// ограничен четвертью полуоси. Nesterov и Adam монотонной энергии не имеют: для них, как и
    /// при слишком большом шаге, нужно удержание в центральной части окрестности
    /// <see cref="DescentHoldIterations"/> итераций подряд с короткими шагами.
    /// </summary>
    private int DescentTrapTarget(Complex z, Complex velocity, out bool certain)
    {
        certain = false;
        for (int i = 0; i < _descentTraps.Length; i++)
        {
            DescentTrap trap = _descentTraps[i];
            if (!trap.IsActive) continue;
            Complex offset = z - _planarAttractors[i].Points[0];
            if (!trap.Shape.Contains(offset)) continue;
            double potential = EvaluatePlanarPotential(z);
            if (!(potential < trap.PotentialBound)) continue;
            double rate = _planar.LearningRate, beta = _planar.Momentum, radius = trap.Shape.MinorRadius;
            switch (_planar.Optimizer)
            {
                case BasinOptimizer.GradientDescent when rate * trap.Curvature <= 1:
                    certain = true;
                    return i;
                case BasinOptimizer.Momentum when rate * trap.Curvature <= 1 - beta && beta > 0:
                {
                    double energy = potential + beta * velocity.Magnitude * velocity.Magnitude / (2 * rate);
                    double jump = beta * Math.Sqrt(2 * rate * (trap.PotentialBound - trap.MinimumPotential) / beta) + rate * trap.MaxGradient;
                    if (energy < trap.PotentialBound && jump <= radius / 4) { certain = true; return i; }
                    break;
                }
            }
            return trap.Shape.Contains(offset, 0.25) && velocity.Magnitude <= radius / 8 ? i : -1;
        }
        return -1;
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
