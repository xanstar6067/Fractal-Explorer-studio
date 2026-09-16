using System.IO;
using System.Numerics;
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
    private static readonly BasinExplorerKind[] NewBasinKinds =
        [BasinExplorerKind.ComplexLogistic, BasinExplorerKind.MagneticPendulum, BasinExplorerKind.GravityCenters];

    private static void VerifyLogisticBasins()
    {
        var state = BasinExplorerCatalog.GetPresets(BasinExplorerKind.ComplexLogistic)[0].Clone();
        state.MaxIterations = 1000;
        foreach ((double lambda, int period) in new[] { (0.5, 1), (2.0, 1), (3.2, 2), (3.5, 4), (3.55, 8) })
        {
            state.ParameterC = new(lambda, 0);
            state.LogisticPlane = LogisticPlaneMode.InitialValues;
            var dynamic = BasinExplorerWindow.CreateEngine(state);
            BasinOrbitResult basin = dynamic.AnalyzePoint(0.5, 0);
            Check(basin.Outcome == BasinOrbitOutcome.Converged && basin.CyclePeriod == period,
                $"Logistic dynamic λ={lambda}: expected period {period}, got {basin}.");
            state.LogisticPlane = LogisticPlaneMode.Parameter;
            var parameter = BasinExplorerWindow.CreateEngine(state);
            BasinOrbitResult result = parameter.AnalyzePoint(lambda, 0);
            Check(result.Outcome == BasinOrbitOutcome.Converged && result.CyclePeriod == period,
                $"Logistic parameter λ={lambda}: expected period {period}, got {result}.");
        }
        state.LogisticPlane = LogisticPlaneMode.Parameter;
        var map = BasinExplorerWindow.CreateEngine(state);
        Check(map.AnalyzePoint(1, 0).Outcome != BasinOrbitOutcome.Converged, "Neutral λ=1 must not be classified as attracting.");
        Check(map.AnalyzePoint(5, 0).Outcome == BasinOrbitOutcome.Escaped, "λ=5 critical orbit must escape.");
        Check(map.AnalyzePoint(0, 0).CyclePeriod == 1, "λ=0 is the constant map with attracting 0.");
        map.LogisticSeed = Complex.Zero;
        Check(map.AnalyzePoint(3.2, 0).Outcome != BasinOrbitOutcome.Converged, "Exact repelling fixed point must not become an attractor.");
        map.LogisticSeed = new(2, 0);
        Check(map.AnalyzePoint(3.2, 0).Outcome == BasinOrbitOutcome.Escaped, "Fixed seed must affect the parameter plane.");
        map.LogisticSeed = new(0.5, 0);
        map.PeriodFilter = 4;
        Check(map.ComputeColor(3.2, 0) == map.BackgroundColor, "Logistic period filter must hide period 2.");
        Console.WriteLine("[diag] Logistic: dynamic/parameter periods 1/2/4/8, neutrality, seed, escape and filter OK");
    }

    private static void VerifyPhysicalBasins()
    {
        var gravity = BasinExplorerCatalog.GetPresets(BasinExplorerKind.GravityCenters)[0].Clone();
        gravity.Physics.Centers = [new() { X = 0, Y = 0, CaptureRadius = 0.2 }];
        var engine = BasinExplorerWindow.CreateEngine(gravity);
        var captured = engine.AnalyzePoint(1, 0);
        Check(captured.Outcome == BasinOrbitOutcome.Converged && captured.TargetIndex == 0 && captured.SmoothIterations > 0,
            $"One gravity center must capture a stationary particle: {captured}.");
        Check(Math.Abs(captured.FinalPoint.Magnitude - 0.2) < 1e-7, "Absorption must stop on the capture boundary.");
        Check(engine.AnalyzePoint(0, 0).SmoothIterations == 0, "Particle starting inside a center is captured at t=0.");
        var spread = gravity.Clone();
        spread.Physics.Centers = [new() { X = -1000 }, new() { X = 1000 }];
        var distant = BasinExplorerWindow.CreateEngine(spread).AnalyzePoint(1000, 0);
        Check(distant.Outcome == BasinOrbitOutcome.Converged && distant.TargetIndex == 1,
            "Escape boundary must enclose every center of a widely spread configuration.");
        gravity.Physics.InitialVelocity = new(-100, 0);
        gravity.Physics.Centers[0].CaptureRadius = 0.01;
        engine = BasinExplorerWindow.CreateEngine(gravity);
        Check(engine.AnalyzePoint(0.05, 0).Outcome == BasinOrbitOutcome.Converged, "Fast particle must not tunnel through a small capture disk.");

        var magnetic = BasinExplorerCatalog.GetPresets(BasinExplorerKind.MagneticPendulum)[0].Clone();
        magnetic.Physics.Centers = [new()];
        magnetic.Physics.InitialVelocity = Complex.Zero;
        engine = BasinExplorerWindow.CreateEngine(magnetic);
        BasinOrbitResult resting = engine.AnalyzePoint(0, 0);
        Check(resting.Outcome == BasinOrbitOutcome.Converged && resting.SmoothIterations >= magnetic.Physics.SettleTime,
            "Magnetic capture must require sustained rest, not mere proximity.");
        magnetic.Physics.InitialVelocity = new(4, 0);
        magnetic.Physics.MaxTime = 0.1;
        engine = BasinExplorerWindow.CreateEngine(magnetic);
        Check(engine.AnalyzePoint(0, 0).Outcome == BasinOrbitOutcome.IterationLimit,
            "A moving pendulum over a magnet must not be captured immediately.");

        // Compare a short, uncaptured trajectory against a much finer RK4 integration.
        gravity.Physics.CaptureMode = PhysicalCaptureMode.Settle;
        gravity.Physics.InitialVelocity = new(0, 0.3);
        gravity.Physics.MaxTime = 0.6;
        gravity.Physics.TimeStep = 0.03;
        var coarse = BasinExplorerWindow.CreateEngine(gravity).AnalyzePoint(1, 0.3);
        gravity.Physics.TimeStep = 0.001;
        var fine = BasinExplorerWindow.CreateEngine(gravity).AnalyzePoint(1, 0.3);
        Check((coarse.FinalPoint - fine.FinalPoint).Magnitude < 1e-5,
            $"RK4 must agree with the fine trajectory: error {(coarse.FinalPoint - fine.FinalPoint).Magnitude}.");

        foreach (BasinExplorerKind kind in new[] { BasinExplorerKind.MagneticPendulum, BasinExplorerKind.GravityCenters })
        {
            var state = BasinExplorerCatalog.GetPresets(kind)[0].Clone();
            engine = BasinExplorerWindow.CreateEngine(state);
            for (int i = 0; i < state.Physics.Centers.Count; i++)
            {
                var c = state.Physics.Centers[i];
                var result = engine.AnalyzePoint(c.X * 0.95, c.Y * 0.95);
                Check(result.Outcome == BasinOrbitOutcome.Converged && result.TargetIndex == i,
                    $"{kind}: points close to center {i} must reach it, got {result}.");
            }
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            bool canceled = false;
            try { engine.ComputeColor(1, 1, cts.Token); }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "Physical orbit must honor cancellation inside the integration loop.");
            var cloned = state.Clone();
            cloned.Physics.Centers[0].X += 10;
            Check(cloned.Physics.Centers[0].X != state.Physics.Centers[0].X, "Physical center lists must be deep cloned.");
        }
        bool invalid = false;
        try { engine.ConfigurePhysics(new() { TimeStep = double.NaN }); }
        catch (InvalidOperationException) { invalid = true; }
        Check(invalid, "Non-finite physical parameters must be rejected.");
        Console.WriteLine("[diag] Physics: capture, dwell, fast impacts, RK4 accuracy, cancellation and deep cloning OK");
    }

    private static void VerifyNewBasinWindows()
    {
        using var sandbox = DataSandbox.Create("new-basins");
        // Real controls and state capture, without showing any window or touching user data.
        var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == themeStyles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });
        foreach (BasinExplorerKind kind in NewBasinKinds)
        {
            var window = new BasinExplorerWindow(kind);
            try
            {
                var expected = BasinExplorerCatalog.GetPresets(kind)[0].Clone();
                if (BasinExplorerCatalog.UsesPhysics(kind))
                {
                    expected.Physics.Centers[0].Strength = 1.7;
                    expected.Physics.InitialVelocity = new(0.2, -0.3);
                }
                else { expected.LogisticPlane = LogisticPlaneMode.Parameter; expected.LogisticSeed = new(0.3, 0.1); }
                window.LoadState(expected);
                var actual = window.CaptureState("round trip");
                Check(actual.Kind == kind, "Window must preserve its kind.");
                if (BasinExplorerCatalog.UsesPhysics(kind))
                {
                    Check(actual.Physics.Centers[0].Strength == 1.7 && actual.Physics.InitialVelocity == expected.Physics.InitialVelocity,
                        "Physical UI must round-trip center strength and velocity.");
                    Check(((FrameworkElement)window.FindName("PhysicsPanel")).Visibility == Visibility.Visible &&
                          ((FrameworkElement)window.FindName("FormulaPanel")).Visibility == Visibility.Collapsed,
                        "Physical window must show physics controls.");
                }
                else Check(actual.LogisticPlane == LogisticPlaneMode.Parameter && actual.LogisticSeed == expected.LogisticSeed,
                    "Logistic UI must round-trip plane and seed.");
                if (BasinExplorerCatalog.UsesPhysics(kind))
                {
                    var strength = (TextBox)window.FindName("ForceCenterStrengthBox");
                    strength.Text = "2.3";
                    Check(window.CaptureState("edited").Physics.Centers[0].Strength == 2.3,
                        "Editing a center must update the captured physical model.");
                    strength.Text = "NaN";
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        bool rejected = false;
                        try { window.CaptureState("invalid"); }
                        catch (InvalidOperationException) { rejected = true; }
                        Check(rejected, "Invalid physical input must never silently save old values.");
                    }
                    strength.Text = "1.7";
                }
                else
                {
                    var plane = (ComboBox)window.FindName("LogisticPlaneBox");
                    plane.SelectedIndex = 0;
                    var dynamicState = window.CaptureState("dynamic");
                    Check(dynamicState.LogisticPlane == LogisticPlaneMode.InitialValues && dynamicState.Attractors.Any(a => a.Period == 2),
                        "Switching from λ to z must discover the fixed λ cycle again.");
                    plane.SelectedIndex = 1;
                    ((TextBox)window.FindName("MaxPeriodBox")).Text = "24";
                    Check(window.CaptureState("periods").MaxPeriod == 24 && ((ComboBox)window.FindName("PeriodFilterBox")).Items.Count == 25,
                        "Parameter plane period limit must also update its filter.");
                    ((TextBox)window.FindName("MaxPeriodBox")).Text = "16";
                }
                var store = new BasinExplorerSaveStore(kind);
                actual.SaveName = "New basin verification";
                store.Save(actual);
                var loaded = store.Load().Single(s => s.SaveName == actual.SaveName);
                Check(RenderBasinFrame(BasinExplorerWindow.CreateEngine(actual), 16, 12).AsSpan().SequenceEqual(
                    RenderBasinFrame(BasinExplorerWindow.CreateEngine(loaded), 16, 12)), "New basin file store must preserve rendering.");
            }
            finally { window.Close(); }
        }
    }

    private static void WriteNewBasinPreviews(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (BasinExplorerKind kind in NewBasinKinds)
        {
            var state = BasinExplorerCatalog.GetPresets(kind)[kind == BasinExplorerKind.ComplexLogistic ? 4 : kind == BasinExplorerKind.MagneticPendulum ? 1 : 0];
            var engine = BasinExplorerWindow.CreateEngine(state);
            byte[] pixels = RenderBasinFrame(engine, 512, 512);
            BitmapSource bitmap = BitmapSource.Create(512, 512, 96, 96, PixelFormats.Bgra32, null, pixels, 2048);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = Path.Combine(directory, BasinExplorerCatalog.GetDefinition(kind).ExportFilePrefix + "_preview_sq512.png");
            using var stream = File.Create(path);
            encoder.Save(stream);
            Console.WriteLine($"[preview] {path}");
        }
    }
}
