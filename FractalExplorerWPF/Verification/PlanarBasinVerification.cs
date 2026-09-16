using System.Numerics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static readonly BasinExplorerKind[] PlanarKinds =
        [BasinExplorerKind.GradientDescent, BasinExplorerKind.ComplexGradientFlow, BasinExplorerKind.PolynomialVectorField];

    private static BasinExplorerEngine PlanarEngine(BasinExplorerKind kind, PlanarBasinSettings settings,
        string formula = "z^3-1", int iterations = 8000)
    {
        var engine = new BasinExplorerEngine(kind) { MaxIterations = iterations };
        engine.ConfigurePlanar(settings, formula);
        return engine;
    }

    private static void VerifyPlanarBasins()
    {
        foreach (BasinOptimizer optimizer in Enum.GetValues<BasinOptimizer>())
        {
            var settings = new PlanarBasinSettings { Potential = "(x^2+y^2)/2", Optimizer = optimizer, LearningRate = 0.1 };
            var engine = PlanarEngine(BasinExplorerKind.GradientDescent, settings);
            Check(engine.TargetCount == 1, $"{optimizer}: a convex quadratic must have one minimum.");
            engine.MaxIterations = 1;
            var step = engine.AnalyzePoint(2, -3);
            Complex expected = optimizer == BasinOptimizer.Adam ? new(2 - 0.2 / (2 + settings.AdamEpsilon), -3 + 0.3 / (3 + settings.AdamEpsilon)) : new(1.8, -2.7);
            Check((step.FinalPoint - expected).Magnitude < 1e-12, $"{optimizer}: wrong first update: {step.FinalPoint}, expected {expected}.");
            engine.MaxIterations = 8000;
            var converged = engine.AnalyzePoint(2, -3);
            Check(converged.Outcome == BasinOrbitOutcome.Converged && converged.FinalPoint.Magnitude < 1e-4,
                $"{optimizer}: quadratic must converge, got {converged}.");
            // Память сбрасывается для каждого пикселя, не переносится между орбитами.
            Check(engine.AnalyzePoint(2, -3) == converged, $"{optimizer}: per-pixel optimizer memory leaked.");
        }
        var himmelblau = BasinExplorerWindow.CreateEngine(BasinExplorerCatalog.GetPresets(BasinExplorerKind.GradientDescent)[0]);
        Check(himmelblau.TargetCount == 4, $"Himmelblau must have four strict minima; found {himmelblau.TargetCount}.");
        foreach (Complex seed in new Complex[] { new(3.1, 2.1), new(-2.9, 3), new(-3.7, -3.2), new(3.6, -1.8) })
            Check(himmelblau.AnalyzePoint(seed.Real, seed.Imaginary).Outcome == BasinOrbitOutcome.Converged, $"Himmelblau minimum near {seed} must capture.");
        var saddle = PlanarEngine(BasinExplorerKind.GradientDescent, new() { Potential = "(x^2-1)^2+y^2" });
        Check(saddle.TargetCount == 2 && saddle.AnalyzePoint(0, 0).TargetIndex < 0, "A stationary saddle must not be a minimum.");
        var escape = PlanarEngine(BasinExplorerKind.GradientDescent, new() { Potential = "(x^2+y^2)/2", LearningRate = 3 });
        Check(escape.AnalyzePoint(1, 1).Outcome == BasinOrbitOutcome.Escaped, "An unstable discrete step must escape, not silently shrink α.");

        var flow = PlanarEngine(BasinExplorerKind.ComplexGradientFlow, new() { MaxTime = 1, IntegrationTolerance = 1e-9 }, "z");
        var decay = flow.AnalyzePoint(1, 2);
        Check((decay.FinalPoint - new Complex(1, 2) * Math.Exp(-1)).Magnitude < 2e-8 && Math.Abs(decay.SmoothIterations - 1) < 1e-10,
            $"Complex flow z′=-z must reproduce exponential decay and its time: {decay}.");
        flow = PlanarEngine(BasinExplorerKind.ComplexGradientFlow, new(), "z^3-1");
        Check(flow.TargetCount == 3 && flow.AnalyzePoint(0, 0).TargetIndex < 0, "Complex flow must find roots but reject f′=0 with f≠0.");
        var trajectory = flow.TraceOrbit(0.6, 0.4, 512);
        for (int i = 1; i < trajectory.Count; i++)
            Check(flow.EvaluatePlanarPotential(trajectory[i]) <= flow.EvaluatePlanarPotential(trajectory[i - 1]) + 1e-7,
                "Gradient flow potential must not increase.");

        var vectorPresets = BasinExplorerCatalog.GetPresets(BasinExplorerKind.PolynomialVectorField);
        foreach (int index in new[] { 0, 1, 2, 3, 4, 5 })
        {
            var field = BasinExplorerWindow.CreateEngine(vectorPresets[index]);
            int points = field.PlanarAttractors.Count(a => !a.IsCycle), cycles = field.PlanarAttractors.Count(a => a.IsCycle);
            Console.WriteLine($"[diag] Vector preset {index}: {points} points, {cycles} cycles; " + string.Join("; ", field.PlanarAttractors.Select((a, i) => a.Describe(i))));
            var expected = index switch { 0 => (2, 0), 1 => (4, 0), 2 => (0, 1), 3 => (1, 1), 4 => (0, 2), _ => (0, 1) };
            Check((points, cycles) == expected, $"Vector preset {index}: expected {expected}, found {(points, cycles)}.");
            if (index == 2)
            {
                var cycle = field.PlanarAttractors.Single();
                Check(Math.Abs(cycle.Period - 2 * Math.PI) < 1e-4 && cycle.Points.All(p => Math.Abs(p.Magnitude - 1) < 1e-4), "Hopf cycle must be the unit circle with T=2π.");
                Check(Math.Abs(Math.Log(cycle.TransverseMultiplier) + 4 * Math.PI) < 1e-3, "Hopf transverse multiplier must be exp(-4π).");
                Check(field.AnalyzePoint(0.3, 0.1).TargetIndex == 0 && field.AnalyzePoint(2, 0).TargetIndex == 0,
                    "Hopf must converge from both sides of the stable circle.");
                Check(field.AnalyzePoint(0, 0).TargetIndex < 0, "The exact repelling equilibrium must not belong to the stable cycle.");
            }
            if (index == 3)
            {
                var inner = field.AnalyzePoint(0.2, 0.1); var outer = field.AnalyzePoint(0.8, 0);
                Check(inner.TargetIndex >= 0 && !field.PlanarAttractors[inner.TargetIndex].IsCycle && outer.TargetIndex >= 0 && field.PlanarAttractors[outer.TargetIndex].IsCycle,
                    $"Point/cycle bistability must respect the unstable radius 0.5: {inner} / {outer}.");
                // На неустойчивой границе ошибка double экспоненциально растёт; проверяем
                // короткий горизонт, до численного схода с сепаратрисы.
                var boundarySettings = field.PlanarSettings; boundarySettings.MaxTime = 10;
                var boundary = new BasinExplorerEngine(BasinExplorerKind.PolynomialVectorField) { MaxIterations = 8000 };
                boundary.ConfigurePlanar(boundarySettings, "", field.PlanarAttractors);
                Check(boundary.AnalyzePoint(0.5, 0).TargetIndex < 0, "A separator must not be captured just by proximity to a stable cycle.");
            }
            if (index == 4)
            {
                var inner = field.AnalyzePoint(0.7, 0); var outer = field.AnalyzePoint(1.2, 0);
                Check(inner.TargetIndex >= 0 && outer.TargetIndex >= 0 && inner.TargetIndex != outer.TargetIndex,
                    "Nested stable cycles must retain separate basin IDs.");
            }
        }
        var center = PlanarEngine(BasinExplorerKind.PolynomialVectorField, new() { FieldX = "-y", FieldY = "x", MaxTime = 35 });
        Check(center.TargetCount == 0, "Neutral periodic orbits around a center must not be attracting cycles.");
        var repeller = PlanarEngine(BasinExplorerKind.PolynomialVectorField, new() { FieldX = "x", FieldY = "y", MaxTime = 20 });
        Check(repeller.TargetCount == 0 && repeller.AnalyzePoint(1, 1).Outcome == BasinOrbitOutcome.Escaped, "A source must be rejected and escape.");
        foreach (string invalid in new[] { "sin(x)", "1/x", "x^0.5", "z+y", "x^33" })
        {
            bool rejected = false;
            try { PlanarEngine(BasinExplorerKind.PolynomialVectorField, new() { FieldX = invalid }); }
            catch (Exception) { rejected = true; }
            Check(rejected, $"Non-polynomial field '{invalid}' must be rejected.");
        }
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        bool canceled = false;
        try { flow.PlanarOrbit(0.4, 0.2, cts.Token); } catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "Planar orbit cancellation must be immediate.");
        VerifyPlanarWindows();
        Console.WriteLine("[diag] Planar basins: four optimizers, complex gradient, adaptive integration, stable points/cycles, neutrality, windows and saves OK");
    }

    private static void VerifyPlanarWindows()
    {
        using var sandbox = DataSandbox.Create("planar-basins");
        var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == themeStyles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });
        foreach (BasinExplorerKind kind in PlanarKinds)
        {
            var window = new BasinExplorerWindow(kind);
            try
            {
                var expected = BasinExplorerCatalog.GetPresets(kind)[kind == BasinExplorerKind.PolynomialVectorField ? 3 : 1].Clone();
                window.LoadState(expected);
                var state = window.CaptureState("Planar verification");
                Check(state.UseSavedPlanarAttractors && state.PlanarAttractors.Count > 0, $"{kind}: capture must store discovered targets.");
                Check(((FrameworkElement)window.FindName("PlanarPanel")).Visibility == Visibility.Visible &&
                    ((FrameworkElement)window.FindName("AttractorsExpander")).Visibility == Visibility.Collapsed,
                    $"{kind}: planar settings must replace discrete-map controls.");
                var copy = state.Clone(); copy.Planar.LearningRate = 0.7; copy.PlanarAttractors[0].Points[0] += Complex.One;
                Check(state.Planar.LearningRate != 0.7 && copy.PlanarAttractors[0].Points[0] != state.PlanarAttractors[0].Points[0], "Planar clone must be deep.");
                var store = new BasinExplorerSaveStore(kind); store.Save(state);
                var restored = store.Load().Single(s => s.SaveName == state.SaveName);
                byte[] original = RenderBasinFrame(BasinExplorerWindow.CreateEngine(state), 24, 18);
                Check(original.AsSpan().SequenceEqual(RenderBasinFrame(BasinExplorerWindow.CreateEngine(restored), 24, 18)),
                    $"{kind}: file save must preserve rendering and cycle IDs.");
                Check(JsonSerializer.Serialize(state.Planar, JsonOptionsFactory.Create()) == JsonSerializer.Serialize(restored.Planar, JsonOptionsFactory.Create()),
                    $"{kind}: every planar parameter must survive serialization.");
                ((TextBox)window.FindName("PlanarToleranceBox")).Text = "NaN";
                bool rejected = false;
                try { window.CaptureState("invalid"); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Invalid planar UI input must not silently capture a stale model.");
            }
            finally { window.Close(); }
        }
    }

    private static void WritePlanarBasinPreviews(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (BasinExplorerKind kind in PlanarKinds)
        {
            int index = kind == BasinExplorerKind.ComplexGradientFlow ? 1 : kind == BasinExplorerKind.PolynomialVectorField ? 3 : 0;
            var state = BasinExplorerCatalog.GetPresets(kind)[index].Clone();
            state.Palette = new NewtonPaletteManager().Palettes.Single(p => p.Name == "Огонь").Clone("Огонь");
            var engine = BasinExplorerWindow.CreateEngine(state);
            byte[] pixels = RenderBasinFrame(engine, 512, 512);
            var bitmap = BitmapSource.Create(512, 512, 96, 96, PixelFormats.Bgra32, null, pixels, 2048);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = Path.Combine(directory, BasinExplorerCatalog.GetDefinition(kind).ExportFilePrefix + "_preview_sq512.png");
            using var stream = File.Create(path); encoder.Save(stream);
            Console.WriteLine($"[preview] {path}");
        }
    }
}
