using System.IO;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyAbcFlowAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("abc-flow");
        string output = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "VerificationData", "AbcFlow");
        Directory.CreateDirectory(output);
        using var renderer = new Fractal3DRenderer();
        var settings = new AbcFlowSettings { SeedCount = 3, Samples = 64, TimeStep = .01, B = .7, C = -.4 };
        var samples = await renderer.ReadAbcSamplesAsync(settings, CancellationToken.None);
        foreach (int seed in Enumerable.Range(0, settings.SeedCount))
        {
            Vector3 initial = settings.Seeds()[seed];
            var p = new AbcReferenceVector(initial.X, initial.Y, initial.Z);
            Vector3 tangent = Vector3.Normalize(new(1, .37f, .19f));
            var q = new AbcReferenceVector(tangent.X, tangent.Y, tangent.Z);
            double logs = 0;
            // Independent double midpoint integration with sixteen times smaller steps;
            // directional derivative obtained from velocity differences, not the shader's Jacobian.
            double h = settings.TimeStep / 16;
            for (int i = 1; i <= settings.Samples; i++)
            {
                for (int j = 0; j < 64; j++)
                {
                    var mid = p + AbcReferenceVelocity(p, settings) * (h / 2);
                    var qm = q + Variation(p, q) * (h / 2);
                    p += AbcReferenceVelocity(mid, settings) * h;
                    q += Variation(mid, qm) * h;
                    double stretch = q.Length; logs += Math.Log(stretch); q *= 1 / stretch;
                }
                Vector4 actual = samples[(seed * (settings.Samples + 1) + i) * 2];
                Vector3 expected = new((float)Wrap(p.X), (float)Wrap(p.Y), (float)Wrap(p.Z));
                Check(AbcFlowSettings.Wrap(new Vector3(actual.X, actual.Y, actual.Z) - expected).Length() < 4e-5,
                    $"ABC GPU vs double midpoint: seed {seed}, sample {i}.");
                Check(Math.Abs(actual.W - logs / (i * .04)) < 2e-4, "Variational FTLE vs independent finite differences.");
            }
            AbcReferenceVector Variation(AbcReferenceVector at, AbcReferenceVector v)
            {
                const double epsilon = 1e-5;
                return (AbcReferenceVelocity(at + v * epsilon, settings) - AbcReferenceVelocity(at - v * epsilon, settings)) * (.5 / epsilon);
            }
        }
        var integrable = settings with { B = 0, C = 0, Samples = 256 };
        samples = await renderer.ReadAbcSamplesAsync(integrable, CancellationToken.None);
        Vector3 q0 = Vector3.Normalize(new(1, .37f, .19f));
        for (int seed = 0; seed < integrable.SeedCount; seed++)
        {
            var p = integrable.Seeds()[seed];
            double t = integrable.Duration;
            Vector3 expected = AbcFlowSettings.Wrap(p + new Vector3((float)Math.Sin(p.Z), (float)Math.Cos(p.Z), 0) * (float)t);
            var last = samples[(seed * (integrable.Samples + 1) + integrable.Samples) * 2];
            Check(AbcFlowSettings.Wrap(new Vector3(last.X, last.Y, last.Z) - expected).Length() < .0002, "Exact integrable ABC flow and wrapping.");
            double norm = Math.Sqrt(Math.Pow(q0.X + t * Math.Cos(p.Z) * q0.Z, 2) +
                Math.Pow(q0.Y - t * Math.Sin(p.Z) * q0.Z, 2) + q0.Z * q0.Z);
            Check(Math.Abs(last.W - Math.Log(norm) / t) < 1e-4, "Exact finite-time shear exponent.");
        }
        var crossing = new AbcFlowSettings { A = 0, B = 0, C = 1, Group = AbcSeedGroup.Neighbours,
            SeedCount = 1, StartX = .3, StartY = Math.PI / 2, StartZ = -.2, Samples = 256 };
        var crossSamples = await renderer.ReadAbcSamplesAsync(crossing, CancellationToken.None);
        var geometry = AbcFlowGeometry.Build(crossing, crossSamples, CancellationToken.None);
        Check(geometry.Sections.Length == 4, "Analytic periodic section has four positive crossings.");
        for (int i = 0; i < geometry.Sections.Length; i++)
        {
            var point = geometry.Sections[i];
            Check(Math.Abs(point.Time - (.2 + i * Math.Tau)) < .0001 &&
                Vector2.Distance(point.Position, new(.3f, (float)Math.PI / 2)) < .0001,
                "Oriented Poincaré crossing coordinates and interpolated time.");
        }
        for (int i = 0; i < geometry.Capsules.Length; i += 4)
        {
            if (geometry.Capsules[i + 3].Z > 1.5) continue; // conservative full-cube bound of an animated seed
            var a = geometry.Capsules[i]; var b = geometry.Capsules[i + 1];
            Check(new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z).Length() < .1, "No seam-spanning chords.");
            Check(new[] { a.X, a.Y, a.Z, b.X, b.Y, b.Z }.All(v => Math.Abs(v) <= 1.00001), "Segments end at the periodic cube faces.");
        }
        var reverse = crossing with { C = -1 };
        var reverseSamples = await renderer.ReadAbcSamplesAsync(reverse, CancellationToken.None);
        Check(AbcFlowGeometry.Build(reverse, reverseSamples, CancellationToken.None).Sections.Length == 0, "Negative crossings excluded.");
        Check(AbcFlowGeometry.Build(reverse with { PositiveCrossings = false }, reverseSamples, CancellationToken.None).Sections.Length == 3,
            "Both directions may be selected.");
        Console.WriteLine("ABC equations, GPU RK4/variation, double reference, exact shear, seams and oriented sections passed.");

        var kind = Fractal3DKind.AbcFlow;
        var presets = Fractal3DCatalog.GetPresets(kind);
        for (int i = 0; i < presets.Count; i++)
        {
            var watch = Stopwatch.StartNew();
            var bitmap = await renderer.RenderAsync(presets[i], 256, 256, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap), 256, 256), $"Blank ABC preset {presets[i].SaveName}.");
            WriteLSystemPng(Path.Combine(output, $"preset-{i}.png"), bitmap);
            Console.WriteLine($"GPU preset: {presets[i].SaveName} ({watch.Elapsed.TotalMilliseconds:F0} ms)");
        }
        var state = presets[0].Clone();
        var baseline = Pixels(await renderer.RenderAsync(state, 128, 128, null, CancellationToken.None));
        int builds = renderer.AbcIntegrationBuilds;
        state.CameraYaw += 17; state.AbcFlow = state.AbcFlow with { Phase = .6 };
        var moved = Pixels(await renderer.RenderAsync(state, 128, 128, null, CancellationToken.None));
        Check(builds == renderer.AbcIntegrationBuilds && !baseline.SequenceEqual(moved), "Camera/playback reuse integrated samples.");
        state.AbcFlow = state.AbcFlow with { A = .8 };
        await renderer.RenderAsync(state, 64, 64, null, CancellationToken.None);
        Check(renderer.AbcIntegrationBuilds == builds + 1, "Changing a coefficient reintegrates.");
        state = presets[0].Clone(); state.AbcFlow = state.AbcFlow with { SeedCount = 4, Samples = 128 };
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            state.ShadingStyle = style;
            var bitmap = await renderer.RenderAsync(state, 128, 128, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap), 128, 128), $"Blank ABC style {style}.");
            WriteLSystemPng(Path.Combine(output, $"style-{style}.png"), bitmap);
        }
        foreach (var mode in new[] { Fractal3DColoringMode.OrbitTrap, Fractal3DColoringMode.CrossTrap,
            Fractal3DColoringMode.IterationIndex, Fractal3DColoringMode.Escape })
        {
            state.ColoringMode = mode;
            var bitmap = await renderer.RenderAsync(state, 128, 128, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap), 128, 128), $"Blank ABC coloring {mode}.");
            WriteLSystemPng(Path.Combine(output, $"color-{mode}.png"), bitmap);
        }
        var stationary = presets[0].Clone();
        stationary.AbcFlow = new() { A = 0, B = 0, C = 0, Group = AbcSeedGroup.Neighbours,
            SeedCount = 1, StartX = 0, StartY = 0, StartZ = 0, Thickness = .04, ShowParticles = false };
        stationary.CameraYaw = 0; stationary.CameraPitch = 0; stationary.CameraDistance = 3;
        double distance = await renderer.ProbeDistanceAsync(stationary, 128, 128, 256, 256, CancellationToken.None);
        Check(Math.Abs(distance - 2.92) < .003, "Analytic probe to a stationary seed sphere.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("abc-flow-integrate")) &&
            File.Exists(AppPaths.GetShaderCacheFile("fractal3d-AbcFlow-pixel")), "Shared compute and pixel shader cache.");

        state = presets[2].Clone(); state.SaveName = "ABC roundtrip";
        state.AbcFlow = state.AbcFlow with { Phase = .61723456789, A = .9123456789123, SectionAxis = AbcSectionAxis.Y,
            SectionPosition = -.32123456789, PositiveCrossings = false, PlaybackSpeed = 3.3 };
        var store = new Fractal3DSaveStore(kind); store.Save(state);
        var saved = store.Load().Single();
        Check(saved.AbcFlow == state.AbcFlow, "Exact saved ABC parameters and playback phase.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().Category == "Fractal3DAbcFlow" && unreadable == 0,
            "ABC cloud category.");
        var original = Pixels(await renderer.RenderAsync(state, 128, 128, null, CancellationToken.None));
        var restored = Pixels(await renderer.RenderAsync(saved, 128, 128, null, CancellationToken.None));
        Check(original.SequenceEqual(restored), "Saved state reproduces the identical frame.");

        var theme = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        var window = new Fractal3DWindow(kind);
        try
        {
            Check(((Grid)window.FindName("InitialPreparationLayer")).Visibility == Visibility.Visible, "ABC first-frame preparation.");
            window.LoadState(saved);
            var editor = (AbcFlowEditor)window.FindName("AbcFlowEditor");
            Check(editor.Visibility == Visibility.Visible && window.CaptureState("UI").AbcFlow == saved.AbcFlow, "WPF exact saved fields.");
            ((Slider)editor.FindName("ASlider")).Value = .75;
            Check(window.CaptureState("UI").AbcFlow.A == .75, "Coefficient slider reaches captured state.");
            editor.Start(); editor.OnFrameDisplayed(renderer.AbcFlowSections, state); Check(editor.IsPlaying, "Particle animation starts.");
            window.LoadState(presets[0]); Check(!editor.IsPlaying, "Loading a preset stops animation.");
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(1240, 900)); root.Arrange(new Rect(0, 0, 1240, 900)); root.UpdateLayout();
            var render = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)render.Invoke(window, [Enum.Parse(render.GetParameters()[0].ParameterType, "Full")])!;
            Check(((Image)window.FindName("CanvasImage")).Source is BitmapSource &&
                ((Grid)window.FindName("InitialPreparationLayer")).Visibility == Visibility.Collapsed, "ABC first displayed WPF frame.");
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1240, 900, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(root); WriteLSystemPng(Path.Combine(output, "editor.png"), bitmap);
            ((AbcPoincarePlot)editor.FindName("SectionPlot")).BringIntoView(); root.UpdateLayout();
            bitmap = new RenderTargetBitmap(1240, 900, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(root); WriteLSystemPng(Path.Combine(output, "editor-section.png"), bitmap);
        }
        finally { window.Close(); }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool stopped = false;
        try { await renderer.ReadAbcSamplesAsync(settings, cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
        Check(stopped, "ABC cancellation.");
        foreach (var bad in new[] { settings with { A = double.NaN }, settings with { Samples = 2049 },
            settings with { SeedCount = 0 }, settings with { TimeStep = .5 }, settings with { Phase = -1 } })
        {
            bool rejected = false; try { bad.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid ABC input rejected.");
        }
        var item = FractalCatalog.Create().Single(t => t.LaunchKey == Fractal3DCatalog.LaunchKey(kind));
        Check(item.IsThreeDimensional && CatalogPreviewLoader.IsRendered(item) && item.IntroducedAt is not null, "ABC catalog/history/preview.");
        Console.WriteLine("PASS (abc-flow): numerical references, periodic sections, six presets, nine styles, animation/cache, probe, saves/cloud and WPF.");
        static double Wrap(double x) => x - Math.Tau * Math.Floor((x + Math.PI) / Math.Tau);
    }
    private static AbcReferenceVector AbcReferenceVelocity(AbcReferenceVector p, AbcFlowSettings s) => new(
        s.A * Math.Sin(p.Z) + s.C * Math.Cos(p.Y), s.B * Math.Sin(p.X) + s.A * Math.Cos(p.Z), s.C * Math.Sin(p.Y) + s.B * Math.Cos(p.X));
    private readonly record struct AbcReferenceVector(double X, double Y, double Z)
    {
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public static AbcReferenceVector operator +(AbcReferenceVector a, AbcReferenceVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static AbcReferenceVector operator -(AbcReferenceVector a, AbcReferenceVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static AbcReferenceVector operator *(AbcReferenceVector a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    }
}
