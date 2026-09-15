using System.Numerics;
using System.Text.Json;
using System.Windows.Media;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

// Бассейны Мюллера, Лагерра, секущих, рациональных отображений и периодических циклов.
internal static partial class Program
{
    private static void VerifyBasinExplorers()
    {
        VerifyBasinRootMethods();
        VerifyBasinAttractors();
        VerifyBasinRendering();
        Console.WriteLine("[diag] Basin explorers: methods, cycle discovery, poles, phases, tiles, presets and saves OK");
    }

    private static BasinExplorerEngine RootEngine(BasinExplorerKind kind, string formula, Action<BasinExplorerEngine>? configure = null)
    {
        var engine = new BasinExplorerEngine(kind) { MaxIterations = 200, RootTolerance = 1e-6 };
        configure?.Invoke(engine);
        Check(engine.SetRootFormula(formula, out string debug), $"{kind}: formula {formula} must parse: {debug}");
        return engine;
    }

    private static void VerifyBasinRootMethods()
    {
        // Парабола через три точки квадратичного полинома — сам полином: Мюллер попадает в корень за шаг
        // из любой тройки, в том числе с якорями.
        foreach (MullerSeedMode mode in Enum.GetValues<MullerSeedMode>())
        {
            BasinExplorerEngine muller = RootEngine(BasinExplorerKind.Muller, "z^2-2", engine =>
            {
                engine.MullerSeedMode = mode;
                engine.MullerOffset = new Complex(0.3, -0.2);
                engine.MullerAnchorA = new Complex(-0.7, 1.1);
                engine.MullerAnchorB = new Complex(2.5, 0.4);
            });
            foreach ((double x, double y) in new[] { (1.3, 0.2), (-2.0, 1.7), (0.4, -3.1) })
            {
                BasinOrbitResult result = muller.AnalyzePoint(x, y);
                Check(result.Outcome == BasinOrbitOutcome.Converged && result.Iterations == 1,
                    $"Muller ({mode}) must solve a quadratic in one step from {x}+{y}i, got {result}.");
            }
        }

        // Лагерр для полинома степени n = 2 точен за шаг; при n = 1 шаг совпадает с Ньютоном.
        BasinExplorerEngine laguerre = RootEngine(BasinExplorerKind.Laguerre, "z^2-2");
        BasinOrbitResult quadratic = laguerre.AnalyzePoint(0.1, 3.0);
        Check(quadratic.Outcome == BasinOrbitOutcome.Converged && quadratic.Iterations == 1,
            $"Laguerre must solve a quadratic in one step, got {quadratic}.");
        laguerre = RootEngine(BasinExplorerKind.Laguerre, "z^5-z^2+1", engine =>
        {
            engine.LaguerreAutoDegree = false;
            engine.LaguerreDegree = 1;
        });
        Check(laguerre.PolynomialDegree == 5, "Laguerre must detect the polynomial degree 5.");
        BasinExplorerEngine newton = RootEngine(BasinExplorerKind.Laguerre, "z^5-z^2+1",
            engine => engine.LaguerreComparison = LaguerreComparisonMode.Newton);
        for (int y = 0; y < 30; y++)
        for (int x = 0; x < 30; x++)
        {
            double px = -2 + 4.0 * x / 29, py = -2 + 4.0 * y / 29;
            BasinOrbitResult a = laguerre.AnalyzePoint(px, py);
            BasinOrbitResult b = newton.AnalyzePoint(px, py);
            Check(a.Outcome == b.Outcome && a.TargetIndex == b.TargetIndex && a.Iterations == b.Iterations,
                $"Laguerre with n = 1 must coincide with Newton at {px}+{py}i: {a} vs {b}.");
        }

        // Карта расхождения: у z³ − 2z + 2 Ньютон застревает в цикле {0, 1}, Лагерр — нет.
        BasinExplorerEngine disagreement = RootEngine(BasinExplorerKind.Laguerre, "z^3-2*z+2",
            engine => engine.LaguerreComparison = LaguerreComparisonMode.Disagreement);
        disagreement.TargetColors = [Colors.Red, Colors.Lime, Colors.Blue];
        Check(disagreement.NewtonOrbit(new Complex(0.01, 0)).Outcome == BasinOrbitOutcome.Cycle,
            "Newton for z^3-2z+2 must be caught by the {0, 1} cycle near 0.");
        Check(disagreement.ComputeColor(0.01, 0) == Colors.White,
            "Disagreement map must mark points where only Laguerre converges white.");

        // Секущие: для линейной функции шаг точен; диагональ x₀ = x₁ среза вырождена; оси среза.
        BasinExplorerEngine secant = RootEngine(BasinExplorerKind.Secant, "2*z-3");
        BasinOrbitResult linear = secant.AnalyzePoint(-5, 4);
        Check(linear.Outcome == BasinOrbitOutcome.Converged && linear.Iterations == 1,
            $"Secant must solve a linear equation in one step, got {linear}.");
        secant = RootEngine(BasinExplorerKind.Secant, "z^3-1", engine => engine.SecantPlaneMode = SecantPlaneMode.StateSlice);
        Check(secant.AnalyzePoint(0.8, 0.8).Outcome == BasinOrbitOutcome.Degenerate,
            "Secant state slice must be degenerate on the diagonal x0 = x1.");
        secant.SecantHorizontalAxis = SecantStateAxis.ReX0;
        secant.SecantVerticalAxis = SecantStateAxis.ImX1;
        secant.SecantBaseX0 = new Complex(1, 2);
        secant.SecantBaseX1 = new Complex(3, 4);
        (Complex x0, Complex x1) = secant.SecantSeeds(5, 6);
        Check(x0 == new Complex(5, 2) && x1 == new Complex(3, 6), $"Secant slice axes map wrongly: {x0}, {x1}.");
    }

    private static void VerifyBasinAttractors()
    {
        // Отображение Ньютона для z³ − 2z + 2: три сверхпритягивающих корня и 2-цикл {0, 1}; ∞ не притягивает.
        var rational = new BasinExplorerEngine(BasinExplorerKind.RationalMap) { MaxIterations = 300, MaxPeriod = 12 };
        Check(rational.SetRationalMap("2*z^3-2", "3*z^2-2", out string debug), $"Newton map must parse: {debug}");
        Check(rational.Attractors.Count(attractor => attractor.Period == 1) == 3, $"Newton map must have 3 fixed points: {debug}");
        BasinAttractor? two = rational.Attractors.SingleOrDefault(attractor => attractor.Period == 2);
        Check(two is not null && (two.Points[0] - Complex.Zero).Magnitude < 1e-12 && (two.Points[1] - Complex.One).Magnitude < 1e-12 &&
              two.Multiplier.Magnitude < 1e-12,
            $"Newton map must have the superattracting cycle (0, 1) in canonical order: {debug}");
        Check(!rational.InfinityIsAttractor, "∞ of the Newton map (|λ| = 1.5) must not be an attractor.");
        BasinOrbitResult nearZero = rational.AnalyzePoint(0.001, 0);
        BasinOrbitResult nearOne = rational.AnalyzePoint(0.999, 0);
        Check(nearZero.Outcome == BasinOrbitOutcome.Converged && nearOne.Outcome == BasinOrbitOutcome.Converged &&
              nearZero.TargetIndex == nearOne.TargetIndex && rational.Attractors[nearZero.TargetIndex].Period == 2,
            $"Points near 0 and 1 must be captured by the 2-cycle: {nearZero}, {nearOne}.");
        Check(nearZero.Phase == 0 && nearOne.Phase == 1, $"Cycle phases near 0 and 1 must be 0 and 1: {nearZero}, {nearOne}.");

        // z² − 1: цикл {−1, 0} и притягивающая ∞.
        Check(rational.SetRationalMap("z^2-1", "1", out debug), "z^2-1 must parse.");
        Check(rational.Attractors.Count == 2 && rational.Attractors[0].Period == 2 && rational.InfinityIsAttractor,
            $"z^2-1 must have the 2-cycle and ∞: {debug}");
        BasinOrbitResult escaping = rational.AnalyzePoint(3, 0);
        Check(escaping.Outcome == BasinOrbitOutcome.Converged && rational.Attractors[escaping.TargetIndex].IsInfinity,
            $"3 must be attracted by ∞: {escaping}.");

        // Полюс при deg P = deg Q: орбита из полюса идёт в R(∞) = 0.5 и дальше к сверхпритягивающему 0.
        Check(rational.SetRationalMap("0.5*z^2", "z^2-1", out debug), $"Pole fixture must parse: {debug}");
        Check(!rational.InfinityIsAttractor, "∞ maps to a finite point here and must not be an attractor.");
        BasinOrbitResult pole = rational.AnalyzePoint(1, 0);
        Check(pole.Outcome == BasinOrbitOutcome.Converged && rational.Attractors[pole.TargetIndex].Points[0].Magnitude < 1e-12,
            $"An orbit through a pole must continue from R(∞) and reach 0: {pole}.");

        // Кролик Дуади: 3-цикл находится по орбите критической точки.
        rational.ParameterC = new Complex(-0.122561, 0.744862);
        Check(rational.SetRationalMap("z^2+c", "1", out debug) && rational.Attractors.Any(attractor => attractor.Period == 3),
            $"The Douady rabbit must have an attracting 3-cycle: {debug}");

        // Периодические циклы: по умолчанию уход — цвет фона, а ∞ не попадает в список.
        var cycles = new BasinExplorerEngine(BasinExplorerKind.PeriodicCycles)
        {
            MaxIterations = 400,
            MaxPeriod = 8,
            ParameterC = new Complex(-1.3107, 0),
            BackgroundColor = Colors.Black,
            TargetColors = [Colors.Red]
        };
        Check(cycles.SetMapFormula("z^2+c", out debug) && cycles.Attractors.Count == 1 && cycles.Attractors[0].Period == 4,
            $"z^2 - 1.3107 must have exactly one attracting cycle of period 4: {debug}");
        Check(cycles.AnalyzePoint(5, 0).Outcome == BasinOrbitOutcome.Escaped && cycles.ComputeColor(5, 0) == Colors.Black,
            "Escaping orbits must be background in the periodic cycles mode.");
        cycles.PeriodFilter = 3;
        Check(cycles.ComputeColor(0, 0) == Colors.Black, "A period filter must hide cycles of other periods.");

        // Нейтральные циклы не принимаются ни с символьной, ни с численной производной.
        cycles.ParameterC = Complex.Zero;
        Check(cycles.SetMapFormula("z+z^2", out debug) && cycles.Attractors.All(attractor => (attractor.Points[0]).Magnitude > 1e-3),
            $"The parabolic fixed point of z + z^2 must be rejected: {debug}");
        Check(cycles.SetMapFormula("z^z", out debug) && cycles.Attractors.All(attractor => attractor.Multiplier.Magnitude < 1 - 1e-5),
            $"z^z uses a numeric derivative and must reject its neutral fixed point 1: {debug}");

        // Трансцендентное отображение: неподвижная точка c·exp(z) при c = 0.3.
        cycles.ParameterC = new Complex(0.3, 0);
        Check(cycles.SetMapFormula("c*exp(z)", out debug) &&
              cycles.Attractors.Any(attractor => attractor.Period == 1 && Math.Abs(attractor.Points[0].Real - 0.4894022) < 1e-6),
            $"0.3·exp(z) must have the attracting fixed point 0.4894: {debug}");
    }

    private static void VerifyBasinRendering()
    {
        var palette = BasinExplorerCatalog.ClassicPalette();
        foreach (BasinExplorerKind kind in Enum.GetValues<BasinExplorerKind>())
        {
            IReadOnlyList<BasinExplorerState> presets = BasinExplorerCatalog.GetPresets(kind);
            Check(presets.Count > 0, $"{kind} must have presets.");
            foreach (BasinExplorerState preset in presets)
            {
                BasinExplorerEngine engine = BasinExplorerWindow.CreateEngine(preset);
                Check(engine.TargetCount > 0, $"Preset «{preset.SaveName}» must find roots or attractors.");
                byte[] frame = RenderBasinFrame(engine, 48, 36);
                Check(frame.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                    $"Preset «{preset.SaveName}» must render something besides the black background.");
            }

            // Тайлы собирают тот же кадр, что и сплошной рендер.
            BasinExplorerState state = presets[^1].Clone();
            state.Palette = palette;
            BasinExplorerEngine reference = BasinExplorerWindow.CreateEngine(state);
            const int width = 70, height = 45;
            byte[] full = RenderBasinFrame(reference, width, height);
            byte[] tiled = new byte[full.Length];
            IReadOnlyList<MandelbrotRenderTile> tiles = MandelbrotTileScheduler.Create(width, height, 16, TileSchedulingStrategy.Classic);
            foreach (MandelbrotRenderTile tile in tiles)
            {
                byte[] pixels = reference.RenderTile(tile, width, height, CancellationToken.None)!;
                for (int row = 0; row < tile.Height; row++)
                    Buffer.BlockCopy(pixels, row * tile.Width * 4, tiled, ((tile.Y + row) * width + tile.X) * 4, tile.Width * 4);
            }
            Check(full.AsSpan().SequenceEqual(tiled), $"{kind}: tiles must reproduce the full frame bit for bit.");

            // Состояние, снятое с найденными корнями/циклами, переживает JSON и даёт тот же кадр без повторного поиска.
            state.Roots = [.. reference.Roots];
            state.Attractors = reference.Attractors.Where(attractor => !attractor.IsInfinity).Select(attractor => attractor.Clone()).ToList();
            state.UseSavedAttractors = !BasinExplorerCatalog.UsesRoots(kind);
            string json = JsonSerializer.Serialize(state, JsonOptionsFactory.Create());
            BasinExplorerState restored = JsonSerializer.Deserialize<BasinExplorerState>(json, JsonOptionsFactory.Create())!;
            Check(JsonSerializer.Serialize(restored, JsonOptionsFactory.Create()) == json, $"{kind}: state JSON must round-trip.");
            byte[] fromSave = RenderBasinFrame(BasinExplorerWindow.CreateEngine(restored), width, height);
            Check(full.AsSpan().SequenceEqual(fromSave), $"{kind}: a saved state must reproduce the frame.");
        }
    }

    private static byte[] RenderBasinFrame(BasinExplorerEngine engine, int width, int height)
    {
        byte[] buffer = new byte[width * height * 4];
        engine.RenderToBuffer(buffer, width, height, width * 4, Environment.ProcessorCount, CancellationToken.None);
        return buffer;
    }
}
