using System.Numerics;

namespace FractalExplorerWPF.Models;

public enum BasinOptimizer { GradientDescent, Momentum, Nesterov, Adam }

/// <summary>Дискретная оптимизация и автономные потоки на вещественной плоскости.</summary>
public sealed class PlanarBasinSettings
{
    public string Potential { get; set; } = "(x^2+y-11)^2+(x+y^2-7)^2";
    public string FieldX { get; set; } = "x-x^3";
    public string FieldY { get; set; } = "-y";
    public BasinOptimizer Optimizer { get; set; }
    public double LearningRate { get; set; } = 0.01;
    public double Momentum { get; set; } = 0.9;
    public double Beta2 { get; set; } = 0.999;
    public double AdamEpsilon { get; set; } = 1e-8;
    public double TimeStep { get; set; } = 0.1;
    public double MaxTime { get; set; } = 80;
    public double IntegrationTolerance { get; set; } = 1e-7;
    public double ConvergenceTolerance { get; set; } = 1e-5;
    public double SearchRadius { get; set; } = 5;
    public double EscapeRadius { get; set; } = 100;

    public PlanarBasinSettings Clone() => (PlanarBasinSettings)MemberwiseClone();
}

/// <summary>Равновесие (одна точка) или замкнутая орбита с периодом в единицах времени.</summary>
public sealed class PlanarBasinAttractor
{
    public List<Complex> Points { get; set; } = [];
    public double Period { get; set; }
    public double TransverseMultiplier { get; set; }
    public bool IsCycle => Period > 0;
    public PlanarBasinAttractor Clone() => new()
    {
        Points = [.. Points], Period = Period, TransverseMultiplier = TransverseMultiplier
    };
    public string Describe(int index) => IsCycle
        ? $"{index + 1}. Предельный цикл · T = {Period:G5} · μ = {TransverseMultiplier:G4}"
        : $"{index + 1}. Устойчивая точка · {BasinExplorerFormatting.Complex(Points[0])}";
}

public static partial class BasinExplorerCatalog
{
    private const string PlanarHint = "Колесо: масштаб (Ctrl — ×10, Shift — точно). Левая кнопка: перемещение. Правая: траектория. Маркеры и линии включаются вверху панели. F11: полный экран.";

    private static IReadOnlyList<BasinExplorerState> PlanarPresets(BasinExplorerKind kind)
    {
        BasinExplorerState Make(string name, Action<BasinExplorerState>? configure = null)
        {
            var state = new BasinExplorerState
            {
                Kind = kind, SaveName = name, MaxIterations = 8000, Zoom = 0.28,
                ShadingScale = kind == BasinExplorerKind.GradientDescent ? 40 : 4,
                Palette = FirePalette(), MarkerMode = BasinMarkerMode.Hidden
            };
            configure?.Invoke(state);
            return state;
        }

        if (kind == BasinExplorerKind.GradientDescent)
            return
            [
                Make("Химмельблау · четыре минимума · GD"),
                Make("Химмельблау · momentum", s => { s.Planar.Optimizer = BasinOptimizer.Momentum; s.Planar.LearningRate = 0.005; }),
                Make("Химмельблау · Nesterov", s => { s.Planar.Optimizer = BasinOptimizer.Nesterov; s.Planar.LearningRate = 0.005; }),
                Make("Химмельблау · Adam", s => { s.Planar.Optimizer = BasinOptimizer.Adam; s.Planar.LearningRate = 0.04; s.ShadingScale = 200; }),
                Make("Четыре ямы · обычный спуск", s => { s.Planar.Potential = "(x^2-1)^2+(y^2-1)^2+0.2*x*y"; s.Planar.LearningRate = 0.04; s.Zoom = 0.65; }),
                Make("Розенброк · Adam", s => { s.Planar.Potential = "100*(y-x^2)^2+(1-x)^2"; s.Planar.Optimizer = BasinOptimizer.Adam; s.Planar.LearningRate = 0.01; s.Zoom = 0.6; s.MaxIterations = 20000; s.ShadingScale = 1500; }),
                Make("Растригин · много локальных минимумов", s => { s.Planar.Potential = "20+x^2+y^2-10*cos(2*pi*x)-10*cos(2*pi*y)"; s.Planar.LearningRate = 0.002; s.Planar.SearchRadius = 3; s.Zoom = 0.5; })
            ];
        if (kind == BasinExplorerKind.ComplexGradientFlow)
            return
            [
                Make("Поток |z³ − 1|²/2", s => { s.Formula = "z^3-1"; s.Zoom = 0.7; }),
                Make("Поток |z⁵ − 1|²/2", s => { s.Formula = "z^5-1"; s.Zoom = 0.8; }),
                Make("Поток |z³ − 2z + 2|²/2", s => { s.Formula = "z^3-2*z+2"; s.Zoom = 0.5; }),
                Make("Поток |sin(z)|²/2", s => { s.Formula = "sin(z)"; s.Planar.SearchRadius = 8; s.Zoom = 0.22; }),
                Make("Близкие корни · |(z−1)(z+1)(z−0.3i)|²/2", s => { s.Formula = "(z-1)*(z+1)*(z-0.3*i)"; s.Zoom = 0.7; })
            ];
        return
        [
            Make("Два устойчивых равновесия", s => { s.Zoom = 0.65; }),
            Make("Четыре равновесия · связанные ямы", s => { s.Planar.FieldX = "x-x^3-0.2*y"; s.Planar.FieldY = "y-y^3-0.2*x"; s.Zoom = 0.7; }),
            Make("Предельный цикл · нормальная форма Хопфа", s => { s.Planar.FieldX = "x*(1-x^2-y^2)-y"; s.Planar.FieldY = "y*(1-x^2-y^2)+x"; s.Zoom = 0.65; s.ShadingScale = 60; }),
            Make("Точка и цикл · два бассейна", s => { s.Planar.FieldX = "-x*(x^2+y^2-0.25)*(x^2+y^2-1)-y"; s.Planar.FieldY = "-y*(x^2+y^2-0.25)*(x^2+y^2-1)+x"; s.Zoom = 0.8; s.Planar.MaxTime = 120; }),
            Make("Два вложенных устойчивых цикла", s => { s.Planar.FieldX = "-x*(x^2+y^2-0.25)*(x^2+y^2-1)*(x^2+y^2-2.25)-y"; s.Planar.FieldY = "-y*(x^2+y^2-0.25)*(x^2+y^2-1)*(x^2+y^2-2.25)+x"; s.Zoom = 0.65; s.Planar.SearchRadius = 2; s.Planar.MaxTime = 140; }),
            Make("Осциллятор Ван дер Поля", s => { s.Planar.FieldX = "y"; s.Planar.FieldY = "(1-x^2)*y-x"; s.Zoom = 0.5; s.ShadingScale = 60; })
        ];
    }
}
