using System.Numerics;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

public sealed partial class BasinExplorerEngine
{
    private PhysicalBasinSettings? _physics;
    private double _physicalCenterX, _physicalCenterY;
    private double _physicalEscapeRadius;
    public PhysicalBasinSettings Physics => _physics ?? throw new InvalidOperationException("Физическая модель не настроена.");

    public void ConfigurePhysics(PhysicalBasinSettings settings)
    {
        static bool InRange(double x, double min, double max) => double.IsFinite(x) && x >= min && x <= max;
        if (settings.Centers is null || settings.Centers.Count is < 1 or > 16 ||
            settings.Centers.Any(c => c is null || !InRange(c.X, -1e4, 1e4) || !InRange(c.Y, -1e4, 1e4) ||
                !InRange(c.Strength, 0.01, 100) || !InRange(c.CaptureRadius, 0.01, 10)) ||
            !InRange(settings.Damping, 0, 10) || !InRange(settings.RestoringForce, 0, 10) ||
            !InRange(settings.Height, 0.03, 5) || !InRange(settings.TimeStep, 0.001, 0.1) ||
            !InRange(settings.MaxTime, 0.1, 1000) || !InRange(settings.SettleSpeed, 0.001, 1) ||
            !InRange(settings.SettleTime, 0.1, 10) || !InRange(settings.EscapeDistance, 2, 1e6) ||
            !InRange(settings.InitialVelocity.Real, -100, 100) || !InRange(settings.InitialVelocity.Imaginary, -100, 100) ||
            !Enum.IsDefined(settings.CaptureMode))
            throw new InvalidOperationException("Некорректные параметры физической модели (нужно от 1 до 16 центров). ");
        _physics = settings.Clone();
        if (Kind == BasinExplorerKind.MagneticPendulum) _physics.CaptureMode = PhysicalCaptureMode.Settle;
        else _physics.RestoringForce = 0;
        _physicalCenterX = _physics.Centers.Average(c => c.X);
        _physicalCenterY = _physics.Centers.Average(c => c.Y);
        // The escape boundary must enclose every editable center, even a widely spread layout.
        _physicalEscapeRadius = _physics.EscapeDistance + _physics.Centers.Max(c =>
            Math.Sqrt(Math.Pow(c.X - _physicalCenterX, 2) + Math.Pow(c.Y - _physicalCenterY, 2)) + c.CaptureRadius);
        DebugInfo = "Условные единицы. Неподвижные центры; взаимное движение центров не моделируется.\n" +
            "r″ = Σ mᵢ(rᵢ−r)/(|rᵢ−r|²+h²)^(3/2) − γr′ − kr.\n" +
            "Магнитный маятник — упрощённая модель притяжения, k — возвращающая сила подвеса.\n" +
            "RK4 с ограничением шага по локальному времени движения. Время и лимит шагов ограничивают каждую орбиту.\n" +
            "Захват после успокоения требует малой скорости и силы внутри радиуса в течение заданного времени.";
    }

    private readonly record struct Motion(double X, double Y, double Vx, double Vy)
    {
        public Motion Add(Motion delta, double scale) => new(X + scale * delta.X, Y + scale * delta.Y,
            Vx + scale * delta.Vx, Vy + scale * delta.Vy);
        public Complex Position => new(X, Y);
    }

    private Motion Derivative(Motion s)
    {
        PhysicalBasinSettings p = Physics;
        double ax = -p.RestoringForce * s.X - p.Damping * s.Vx;
        double ay = -p.RestoringForce * s.Y - p.Damping * s.Vy;
        foreach (BasinForceCenter center in p.Centers)
        {
            double dx = center.X - s.X, dy = center.Y - s.Y;
            double r2 = dx * dx + dy * dy + p.Height * p.Height;
            double force = center.Strength / (r2 * Math.Sqrt(r2));
            ax += force * dx;
            ay += force * dy;
        }
        return new(s.Vx, s.Vy, ax, ay);
    }

    private Motion Integrate(Motion s, double dt)
    {
        Motion a = Derivative(s);
        Motion b = Derivative(s.Add(a, dt / 2));
        Motion c = Derivative(s.Add(b, dt / 2));
        Motion d = Derivative(s.Add(c, dt));
        return new(s.X + dt / 6 * (a.X + 2 * b.X + 2 * c.X + d.X),
            s.Y + dt / 6 * (a.Y + 2 * b.Y + 2 * c.Y + d.Y),
            s.Vx + dt / 6 * (a.Vx + 2 * b.Vx + 2 * c.Vx + d.Vx),
            s.Vy + dt / 6 * (a.Vy + 2 * b.Vy + 2 * c.Vy + d.Vy));
    }

    private double StableTimeStep(Motion s)
    {
        PhysicalBasinSettings p = Physics;
        double step = p.TimeStep;
        double speed = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
        double inverseTimeSquared = p.RestoringForce;
        foreach (BasinForceCenter c in p.Centers)
        {
            double dx = s.X - c.X, dy = s.Y - c.Y;
            double r2 = dx * dx + dy * dy + p.Height * p.Height;
            inverseTimeSquared += c.Strength / (r2 * Math.Sqrt(r2));
            step = Math.Min(step, 0.2 * Math.Sqrt(r2) / Math.Max(speed, 1e-12));
        }
        step = Math.Min(step, 0.2 / Math.Sqrt(Math.Max(1e-12, inverseTimeSquared)));
        return Math.Min(step, 0.2 / Math.Max(1e-12, p.Damping));
    }

    private int SettledCenter(Motion s)
    {
        PhysicalBasinSettings p = Physics;
        if (s.Vx * s.Vx + s.Vy * s.Vy > p.SettleSpeed * p.SettleSpeed) return -1;
        Motion force = Derivative(s);
        double forceLimit = 2 * p.SettleSpeed;
        if (force.Vx * force.Vx + force.Vy * force.Vy > forceLimit * forceLimit) return -1;
        int nearest = -1;
        double distance = double.PositiveInfinity;
        for (int i = 0; i < p.Centers.Count; i++)
        {
            BasinForceCenter c = p.Centers[i];
            double dx = s.X - c.X, dy = s.Y - c.Y, d2 = dx * dx + dy * dy;
            if (d2 <= c.CaptureRadius * c.CaptureRadius && d2 < distance) { nearest = i; distance = d2; }
        }
        return nearest;
    }

    // Earliest segment/disk intersection prevents fast particles tunnelling through a center.
    private int AbsorbedCenter(Motion a, Motion b, out double fraction)
    {
        fraction = double.PositiveInfinity;
        int hit = -1;
        double dx = b.X - a.X, dy = b.Y - a.Y, length2 = dx * dx + dy * dy;
        for (int i = 0; i < Physics.Centers.Count; i++)
        {
            BasinForceCenter c = Physics.Centers[i];
            double ox = a.X - c.X, oy = a.Y - c.Y;
            double outside = ox * ox + oy * oy - c.CaptureRadius * c.CaptureRadius;
            double t;
            if (outside <= 0) t = 0;
            else
            {
                if (length2 == 0) continue;
                double projection = ox * dx + oy * dy;
                double discriminant = projection * projection - length2 * outside;
                if (discriminant < 0) continue;
                t = (-projection - Math.Sqrt(discriminant)) / length2;
                if (t < 0 || t > 1) continue;
            }
            if (t < fraction) { fraction = t; hit = i; }
        }
        return hit;
    }

    private BasinOrbitResult PhysicalOrbit(double x, double y, CancellationToken token = default,
        List<Complex>? trace = null, int traceLimit = 512)
    {
        PhysicalBasinSettings p = Physics;
        Motion s = new(x, y, p.InitialVelocity.Real, p.InitialVelocity.Imaginary);
        double time = 0, heldTime = 0, nextTrace = 0;
        int heldCenter = -1, iteration = 0;
        trace?.Add(s.Position);
        for (; iteration < MaxIterations && time < p.MaxTime; iteration++)
        {
            if ((iteration & 31) == 0) token.ThrowIfCancellationRequested();
            if (!double.IsFinite(s.X) || !double.IsFinite(s.Y) || !double.IsFinite(s.Vx) || !double.IsFinite(s.Vy))
                return Finish(BasinOrbitOutcome.NonFinite);
            // Escape is measured about the center of the configuration, not the viewport.
            double cx = s.X - _physicalCenterX, cy = s.Y - _physicalCenterY;
            if (cx * cx + cy * cy > _physicalEscapeRadius * _physicalEscapeRadius) return Finish(BasinOrbitOutcome.Escaped);
            double dt = Math.Min(StableTimeStep(s), p.MaxTime - time);
            Motion next = Integrate(s, dt);
            if (p.CaptureMode == PhysicalCaptureMode.Absorb)
            {
                int hit = AbsorbedCenter(s, next, out double fraction);
                if (hit >= 0)
                {
                    s = new(s.X + fraction * (next.X - s.X), s.Y + fraction * (next.Y - s.Y), 0, 0);
                    time += dt * fraction;
                    return Finish(BasinOrbitOutcome.Converged, hit);
                }
            }
            else
            {
                int center = SettledCenter(next);
                heldTime = center >= 0 && center == heldCenter ? heldTime + dt : 0;
                heldCenter = center;
                if (heldTime >= p.SettleTime)
                { s = next; time += dt; return Finish(BasinOrbitOutcome.Converged, center); }
            }
            s = next;
            time += dt;
            if (trace is not null && time >= nextTrace && trace.Count < traceLimit - 1)
            { trace.Add(s.Position); nextTrace = time + p.MaxTime / Math.Max(2, traceLimit - 2); }
        }
        return Finish(BasinOrbitOutcome.IterationLimit);

        BasinOrbitResult Finish(BasinOrbitOutcome outcome, int target = -1)
        {
            if (IsFinite(s.Position)) trace?.Add(s.Position);
            // SmoothIterations stores physical time for these two kinds (also the shading scale).
            return new(outcome, iteration, time, s.Position, target);
        }
    }

    private Color PhysicalResultColor(BasinOrbitResult result) => ColoringMode switch
    {
        BasinColoringMode.OrbitOutcome => OutcomeColor(result),
        BasinColoringMode.IterationCount => result.Outcome is BasinOrbitOutcome.Converged or BasinOrbitOutcome.Escaped
            ? ColorFromHsv(240 * (1 - Math.Clamp(result.SmoothIterations / Physics.MaxTime, 0, 1)), 0.9, 0.95) : BackgroundColor,
        _ => RootResultColor(result)
    };

    private IReadOnlyList<Complex> TracePhysicalOrbit(double x, double y, int limit)
    {
        var trace = new List<Complex>();
        PhysicalOrbit(x, y, trace: trace, traceLimit: Math.Clamp(limit, 2, 2048));
        return trace;
    }
}
