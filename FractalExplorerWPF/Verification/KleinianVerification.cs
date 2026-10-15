using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyKleinianAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("kleinian");
        string output = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "VerificationData", "Kleinian");
        Directory.CreateDirectory(output);
        var kind = Fractal3DKind.Kleinian;
        var state = Fractal3DCatalog.CreateDefaultState(kind);
        var item = FractalCatalog.Create().Single(i => i.LaunchKey == Fractal3DCatalog.LaunchKey(kind));
        Check(item.IsThreeDimensional && CatalogPreviewLoader.IsRendered(item) && item.IntroducedAt is not null,
            "Kleinian needs a dated 3D catalog entry with a rendered preview.");
        Check(state.ColoringMode == Fractal3DColoringMode.Material, "The initial surface must use material coloring.");
        foreach (var invalid in new[] { state.Kleinian with { RadiusRatio = 1.001 }, state.Kleinian with { RadiusRatio = double.NaN },
            state.Kleinian with { MinRadius = 0 }, state.Kleinian with { Thickness = double.PositiveInfinity },
            state.Kleinian with { Thickness = -.01 }, state.Kleinian with { Configuration = (KleinianConfiguration)99 } })
        {
            bool rejected = false;
            try { invalid.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid settings must be rejected before geometry/GPU preparation.");
        }
        foreach (var configuration in Enum.GetValues<KleinianConfiguration>())
        foreach (double ratio in new[] { .65, .86, 1.0 })
        {
            var settings = state.Kleinian with { Configuration = configuration, RadiusRatio = ratio };
            var generators = KleinianGeometry.Generators(settings);
            for (int i = 0; i < generators.Length; i++)
            for (int j = i + 1; j < generators.Length; j++)
            {
                var a = generators[i]; var b = generators[j];
                double distance = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
                Check(distance + 1e-12 >= a.Radius + b.Radius, "Inversion balls must remain disjoint, including at tangency.");
                var child = KleinianGeometry.Invert(b, a);
                Check(Vector3.Distance(child.Center, a.Center) + child.Radius <= a.Radius + 2e-7,
                    "A nonrepeating word must produce a ball nested inside its parent.");
                var restored = KleinianGeometry.Invert(child, a);
                Check(Vector3.Distance(restored.Center, b.Center) < 2e-6 && Math.Abs(restored.Radius - b.Radius) < 1e-10,
                    "Sphere inversion must be an involution.");
                // Independent surface samples: transform points by point inversion and check
                // membership on the computed sphere. This does not reuse sphere-image algebra.
                foreach (Vector3 axis in new[] { Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ })
                {
                    double x = b.X + axis.X * b.Radius - a.X;
                    double y = b.Y + axis.Y * b.Radius - a.Y;
                    double z = b.Z + axis.Z * b.Radius - a.Z;
                    double factor = a.Radius * a.Radius / (x * x + y * y + z * z);
                    double error = Math.Sqrt(Math.Pow(a.X + x * factor - child.X, 2) +
                        Math.Pow(a.Y + y * factor - child.Y, 2) + Math.Pow(a.Z + z * factor - child.Z, 2));
                    Check(Math.Abs(error - child.Radius) < 1e-9, "Sphere image disagrees with independent point inversion.");
                }
            }
            // The outer centers span R³ and determine a unique common orthogonal sphere.
            // Symmetry forces its center to 0; the central generator cannot be orthogonal
            // to any real sphere centered at 0. This rules out a spherical 2D embedding.
            var central = generators[^1];
            Check(central.Center == Vector3.Zero && central.Radius > 0,
                "The central generator prevents a common invariant sphere.");
            var level1 = KleinianGeometry.Build(settings, 1, CancellationToken.None);
            var level2 = KleinianGeometry.Build(settings, 2, CancellationToken.None);
            Check(level1.BallCount == generators.Length && level2.BallCount == generators.Length * (generators.Length - 1),
                "Reduced words must have n and n(n−1) balls at the first two levels.");
        }
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            bool stopped = false;
            try { KleinianGeometry.Build(state.Kleinian, 10, canceled.Token); } catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "Geometry preparation must support cancellation.");
        }
        var limited = KleinianGeometry.Build(new() { Configuration = KleinianConfiguration.Cube, MinRadius = .001 }, 10, CancellationToken.None);
        Check(limited.BallCount <= KleinianGeometry.MaxBalls && limited.BudgetLimited,
            "The geometry budget must retain a finite complete covering and report limited refinement.");

        using var renderer = new Fractal3DRenderer();
        int index = 0;
        foreach (var preset in Fractal3DCatalog.GetPresets(kind))
        {
            var bitmap = await renderer.RenderAsync(preset, 360, 360, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap), 360, 360), $"Blank Kleinian preset: {preset.SaveName}.");
            WriteLSystemPng(Path.Combine(output, $"preset-{index++}.png"), bitmap);
            Console.WriteLine($"Kleinian preset: {preset.SaveName}; balls={renderer.KleinianBallCount}; limited={renderer.KleinianBudgetLimited}");
        }
        byte[] reference = Pixels(await renderer.RenderAsync(state, 128, 128, null, CancellationToken.None));
        foreach (var changed in new[]
        {
            state.Kleinian with { Configuration = KleinianConfiguration.Tetrahedron },
            state.Kleinian with { Configuration = KleinianConfiguration.Cube },
            state.Kleinian with { RadiusRatio = .85 }, state.Kleinian with { MinRadius = .025 },
            state.Kleinian with { Thickness = .02 }
        })
        {
            var view = state.Clone(); view.Kleinian = changed;
            byte[] changedPixels = Pixels(await renderer.RenderAsync(view, 128, 128, null, CancellationToken.None));
            Check(!reference.SequenceEqual(changedPixels),
                "Every Kleinian shape parameter must affect the GPU image.");
        }
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var view = state.Clone(); view.ShadingStyle = style;
            var bitmap = await renderer.RenderAsync(view, 128, 128, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap), 128, 128), $"Empty Kleinian style: {style}.");
        }
        foreach (var mode in Enum.GetValues<Fractal3DColoringMode>().Where(m => Fractal3DCatalog.HasColoringMode(m, kind)))
        {
            var view = state.Clone(); view.ColoringMode = mode;
            Check(HasFractal3DStructure(Pixels(await renderer.RenderAsync(view, 128, 128, null, CancellationToken.None)), 128, 128),
                $"Empty Kleinian coloring: {mode}.");
        }
        var rotated = state.Clone(); rotated.CameraYaw += 55;
        byte[] rotatedPixels = Pixels(await renderer.RenderAsync(rotated, 128, 128, null, CancellationToken.None));
        Check(!reference.SequenceEqual(rotatedPixels),
            "Camera rotation must reveal the spatial structure.");

        // GPU intersection oracle: brute-force analytic intersections with ALL level-two
        // balls, independent of the threaded GPU BVH, for multiple camera directions.
        foreach (double yaw in new[] { 0.0, 37, 90 })
        {
            var view = state.Clone(); view.Iterations = 2; view.CameraYaw = yaw; view.CameraPitch = 0;
            view.Kleinian = view.Kleinian with { Thickness = 0 };
            var geometry = KleinianGeometry.Build(view.Kleinian, 2, CancellationToken.None);
            var camera = Fractal3DCamera.Build(view);
            for (int y = 24; y <= 72; y += 24)
            for (int x = 24; x <= 72; x += 24)
            {
                Vector3 ray = Fractal3DCamera.PixelRay(view, x, y, 96, 96);
                double expected = double.PositiveInfinity;
                for (int i = 0; i < geometry.Spheres.Length; i += 2)
                {
                    Vector4 sphere = geometry.Spheres[i];
                    Vector3 delta = camera.Position - new Vector3(sphere.X, sphere.Y, sphere.Z);
                    double b = Vector3.Dot(delta, ray), c = delta.LengthSquared() - sphere.W * sphere.W;
                    double discriminant = b * b - c;
                    if (discriminant < 0) continue;
                    double root = Math.Sqrt(discriminant);
                    double t = -b - root;
                    if (t < 0) t = -b + root;
                    if (t > 0) expected = Math.Min(expected, t);
                }
                double actual = await renderer.ProbeDistanceAsync(view, x, y, 96, 96, CancellationToken.None);
                Check(double.IsFinite(expected) ? actual > 0 && Math.Abs(actual - expected) < .001 : double.IsNaN(actual),
                    $"Kleinian BVH probe disagrees with analytic spheres: {yaw}/{x}/{y}, {expected} vs {actual}.");
            }
        }
        Check(File.Exists(AppPaths.GetShaderCacheFile("fractal3d-Kleinian-pixel")), "Kleinian must use the shared disk shader cache.");
        var interior = state.Clone();
        interior.Iterations = 1;
        interior.Kleinian = interior.Kleinian with { Thickness = .04 };
        interior.CameraYaw = 90; interior.CameraPitch = 0;
        interior.CameraDistance = .25; interior.TargetX = -.25;
        double insideDistance = await renderer.ProbeDistanceAsync(interior, 48, 48, 96, 96, CancellationToken.None);
        Check(Math.Abs(insideDistance - (1 + 1 / Math.Sqrt(2) + .04)) < .001,
            $"An interior camera must see the union exit, not an internal sphere boundary: {insideDistance}.");
        Check(Fractal3DRenderer.AllShaderEntries().Any(e => e.Key == "fractal3d-Kleinian-pixel"),
            "Rebuild shaders must include Kleinian.");

        state.SaveName = "Kleinian roundtrip";
        state.Kleinian = new() { Configuration = KleinianConfiguration.Cube, RadiusRatio = .934567891234, MinRadius = .005432198765, Thickness = .007654321987 };
        var store = new Fractal3DSaveStore(kind); store.Save(state);
        var restoredState = store.Load().Single();
        Check(restoredState.Kind == kind && restoredState.Kleinian == state.Kleinian && restoredState.Iterations == state.Iterations,
            "Kleinian save roundtrip must retain precise geometry parameters.");
        var clone = state.Clone();
        Check(clone.Kleinian == state.Kleinian && !ReferenceEquals(clone.Kleinian, state.Kleinian), "Clone must own its settings.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Any(e => e.Category == "Fractal3DKleinian") && unreadable == 0,
            "The new save category must be accessible to the common cloud repository.");
        EnsureWindowThemeStyles();
        var window = new Fractal3DWindow(kind);
        try
        {
            window.LoadState(state);
            Check(window.CaptureState("UI").Kleinian == state.Kleinian, "UI must preserve precise saved settings.");
            var editor = (KleinianEditor)window.FindName("KleinianEditor");
            Check(editor.Visibility == Visibility.Visible && ((FrameworkElement)window.FindName("BailoutPanel")).Visibility == Visibility.Collapsed,
                "Kleinian must show its own editor and hide unused bailout controls.");
            ((Slider)editor.FindName("RadiusSlider")).Value = .8;
            Check(window.CaptureState("slider").Kleinian.RadiusRatio == .8, "Radius slider must update the captured view.");
            ((TextBox)editor.FindName("MinRadiusBox")).Text = "0.0031415926535";
            Check(window.CaptureState("exact").Kleinian.MinRadius == .0031415926535, "Exact text input must retain its precision.");
            editor.OnFrameDisplayed(limited.BallCount, true);
            Check(((TextBlock)editor.FindName("GeometryStatus")).Text.Contains("предел"), "The geometry budget must be explained in the panel.");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS (kleinian): discrete spatial generators, inversions, nested coverings, budget, GPU presets/styles/colors, analytic probes, cancellation, cache, saves, cloud category and UI.");
    }
}
