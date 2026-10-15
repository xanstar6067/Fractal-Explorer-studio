using System.IO;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyAntoineAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("antoine");
        string output = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "VerificationData", "Antoine");
        Directory.CreateDirectory(output);
        var kind = Fractal3DKind.AntoineNecklace;
        var state = Fractal3DCatalog.CreateDefaultState(kind);
        var root = new AntoineGeometry.Ring(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 1);
        var item = FractalCatalog.Create().Single(i => i.LaunchKey == Fractal3DCatalog.LaunchKey(kind));
        Check(item.IsThreeDimensional && item.IntroducedAt is not null && CatalogPreviewLoader.IsRendered(item), "Dated 3D catalog entry.");
        Check(state.ColoringMode == Fractal3DColoringMode.Material, "Initial surface uses material coloring.");
        foreach (var bad in new[] { new AntoineSettings { Links = 25 }, new() { Links = 50 }, new() { ScaleFactor = double.NaN },
            new() { Thickness = -1 }, new() { Tilt = double.PositiveInfinity }, new() { Tilt = 59 } })
        {
            bool rejected = false;
            try { bad.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid Antoine input must be rejected.");
        }

        // Independent dense geometry samples at the limits AND intermediate settings.
        // Circle-to-circle distances and the Gauss integral do not use GPU traversal.
        foreach (int n in new[] { 24, 26, 32, 40, 48 })
        foreach (double scale in new[] { 1.35, 1.425, 1.5 })
        foreach (double tilt in new[] { 60.0, 75, 90 })
        {
            var settings = new AntoineSettings { Links = n, ScaleFactor = scale, Tilt = tilt, Thickness = 1 };
            var dimensions = AntoineGeometry.Measure(settings);
            Check(dimensions.Scale * (1 + dimensions.TubeRatio) < dimensions.TubeRatio,
                "Every child solid torus must be strictly nested inside its parent.");
            var rings = Enumerable.Range(0, n).Select(i => AntoineGeometry.Child(root, i, settings, dimensions.Scale)).ToArray();
            for (int i = 0; i < n; i++)
            {
                var a = rings[i]; var b = rings[(i + 1) % n];
                for (int k = 0; k < 384; k++)
                {
                    Vector3 p = a.Point(k * Math.Tau / 384);
                    Check(CircleDistance(p, b) > 2 * a.Radius * dimensions.TubeRatio,
                        "Neighbouring torus surfaces must be disjoint, including the closing link.");
                    Check(CircleDistance(p, root) + a.Radius * dimensions.TubeRatio < dimensions.TubeRatio,
                        "The complete tube, not just its center, must be nested.");
                }
            }
            // All non-neighbours: independent bounding-sphere separation.
            for (int i = 0; i < n; i++)
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                Check(Vector3.Distance(rings[i].Center, rings[j].Center) > 2 * dimensions.Scale * (1 + dimensions.TubeRatio),
                    "Non-neighbouring solid tori must be separated.");
            }
            if (scale == 1.35 || scale == 1.5)
            {
                Check(Math.Abs(Math.Abs(Link(rings[0], rings[1])) - 1) < .004, "Adjacent core circles must have linking number ±1.");
                Check(Math.Abs(Math.Abs(Link(rings[0], rings[^1])) - 1) < .004, "Closing core circles must also be linked.");
                Check(Math.Abs(Link(rings[0], rings[2])) < .004, "Non-neighbours must be unlinked.");
            }
        }
        Console.WriteLine("Antoine: certified clearances, nested solid tori and independent linking integrals passed.");
        for (int depth = 0; depth <= 3; depth++)
        {
            var geometry = AntoineGeometry.Build(state.Antoine, depth, CancellationToken.None);
            Check(geometry.RingCount == (int)Math.Pow(state.Antoine.Links, depth), "Each generation replaces ALL parent tori.");
            Check((int)geometry.Nodes[2].X * 3 == geometry.Nodes.Length, "Threaded root must span the entire hierarchy.");
        }
        Check(AntoineGeometry.Build(new() { Links = 48 }, 3, CancellationToken.None).RingCount == 110_592,
            "The largest permitted generation must retain every branch.");
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel(); bool stopped = false;
            try { AntoineGeometry.Build(state.Antoine, 3, cts.Token); } catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "Geometry preparation supports cancellation.");
        }
        using var renderer = new Fractal3DRenderer();
        int index = 0;
        foreach (var preset in Fractal3DCatalog.GetPresets(kind))
        {
            var bitmap = await renderer.RenderAsync(preset, 480, 480, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap), 480, 480), $"Blank Antoine preset: {preset.SaveName}.");
            WriteLSystemPng(Path.Combine(output, $"preset-{index++}.png"), bitmap);
            Console.WriteLine($"Antoine preset: {preset.SaveName}; rings={renderer.AntoineRingCount}");
        }
        byte[] reference = Pixels(await renderer.RenderAsync(state, 128, 128, null, CancellationToken.None));
        int builds = renderer.AntoineGeometryBuilds;
        var camera = state.Clone(); camera.CameraYaw += 30;
        byte[] cameraPixels = Pixels(await renderer.RenderAsync(camera, 128, 128, null, CancellationToken.None));
        Check(!reference.SequenceEqual(cameraPixels), "Camera reveals spatial structure.");
        Check(renderer.AntoineGeometryBuilds == builds, "Camera changes must reuse GPU geometry.");
        foreach (var changed in new[] { state.Antoine with { Links = 28 }, state.Antoine with { ScaleFactor = 1.5 },
            state.Antoine with { Thickness = 0 }, state.Antoine with { Tilt = 60 } })
        {
            var view = state.Clone(); view.Antoine = changed;
            byte[] changedPixels = Pixels(await renderer.RenderAsync(view, 128, 128, null, CancellationToken.None));
            Check(!reference.SequenceEqual(changedPixels), "Every shape parameter reaches the GPU.");
        }
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var view = state.Clone(); view.Iterations = 1; view.ShadingStyle = style;
            Check(HasFractal3DStructure(Pixels(await renderer.RenderAsync(view, 128, 128, null, CancellationToken.None)), 128, 128), $"Empty Antoine style: {style}.");
        }
        foreach (var coloring in Enum.GetValues<Fractal3DColoringMode>().Where(c => Fractal3DCatalog.HasColoringMode(c, kind)))
        {
            var view = state.Clone(); view.ColoringMode = coloring;
            Check(HasFractal3DStructure(Pixels(await renderer.RenderAsync(view, 128, 128, null, CancellationToken.None)), 128, 128), $"Empty Antoine coloring: {coloring}.");
        }
        await VerifyAntoineProbes(renderer, state);
        Check(File.Exists(AppPaths.GetShaderCacheFile("fractal3d-AntoineNecklace-pixel")), "Antoine participates in the common shader cache.");
        var exact = state.Clone();
        exact.SaveName = "Antoine roundtrip";
        exact.Antoine = new() { Links = 26, ScaleFactor = 1.412345678901, Thickness = .456789012345, Tilt = 72.34567890123 };
        new Fractal3DSaveStore(kind).Save(exact);
        var restored = new Fractal3DSaveStore(kind).Load().Single();
        Check(restored.Kind == kind && restored.Antoine == exact.Antoine && restored.Iterations == exact.Iterations, "Precise disk roundtrip.");
        Check(exact.Clone().Antoine == exact.Antoine && !ReferenceEquals(exact.Clone().Antoine, exact.Antoine), "Settings cloned independently.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Any(e => e.Category == "Fractal3DAntoineNecklace") && unreadable == 0, "Common cloud category.");
        EnsureWindowThemeStyles();
        var window = new Fractal3DWindow(kind);
        try
        {
            window.LoadState(exact);
            Check(window.CaptureState("UI").Antoine == exact.Antoine, "UI preserves exact saved parameters.");
            var editor = (AntoineEditor)window.FindName("AntoineEditor");
            Check(editor.Visibility == Visibility.Visible && ((FrameworkElement)window.FindName("BailoutPanel")).Visibility == Visibility.Collapsed, "Own panel and no bailout.");
            ((ComboBox)editor.FindName("LinksBox")).SelectedItem = 32;
            ((TextBox)editor.FindName("TiltBox")).Text = "67.1234567890123";
            Check(window.CaptureState("input").Antoine is { Links: 32, Tilt: 67.1234567890123 }, "Controls capture edited values exactly.");
            editor.OnFrameDisplayed(576);
            Check(((TextBlock)editor.FindName("GeometryStatus")).Text.Contains("576"), "Final generation count is visible.");
            window.LoadState(state);
            var rootElement = (FrameworkElement)window.Content;
            rootElement.Measure(new Size(1240, 800)); rootElement.Arrange(new Rect(0, 0, 1240, 800)); rootElement.UpdateLayout();
            var layer = (Grid)window.FindName("InitialPreparationLayer");
            Check(layer.Visibility == Visibility.Visible, "The first Antoine frame must have a preparation screen.");
            var render = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)render.Invoke(window, [Enum.Parse(render.GetParameters()[0].ParameterType, "Draft")])!;
            Check(layer.Visibility == Visibility.Collapsed && ((Image)window.FindName("CanvasImage")).Source is BitmapSource,
                "Preparation closes only after the actual first WPF frame is displayed.");
            rootElement.UpdateLayout();
            var snapshot = new RenderTargetBitmap(1240, 800, 96, 96, PixelFormats.Pbgra32);
            snapshot.Render(rootElement);
            WriteLSystemPng(Path.Combine(output, "editor.png"), snapshot);
        }
        finally { window.Close(); }
        Console.WriteLine("PASS (antoine): nesting, disjoint linked tori, recursion, GPU frames/probes/styles/colors, cancellation, geometry reuse, cache, saves/cloud and WPF controls.");

        static double CircleDistance(Vector3 p, AntoineGeometry.Ring ring)
        {
            Vector3 d = p - ring.Center;
            double z = Vector3.Dot(d, ring.Normal);
            double r = Math.Sqrt(Math.Max(0, d.LengthSquared() - z * z)) - ring.Radius;
            return Math.Sqrt(r * r + z * z);
        }
        static double Link(AntoineGeometry.Ring a, AntoineGeometry.Ring b)
        {
            const int n = 128;
            double total = 0;
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                Vector3 a0 = a.Point(i * Math.Tau / n), a1 = a.Point((i + 1) * Math.Tau / n);
                Vector3 b0 = b.Point(j * Math.Tau / n), b1 = b.Point((j + 1) * Math.Tau / n);
                Vector3 d = (a0 + a1 - b0 - b1) * .5f;
                total += Vector3.Dot(d, Vector3.Cross(a1 - a0, b1 - b0)) / Math.Pow(d.LengthSquared(), 1.5);
            }
            return total / (4 * Math.PI);
        }
    }

    private static async Task VerifyAntoineProbes(Fractal3DRenderer renderer, Fractal3DState initial)
    {
        // Independent ray scan through all first-generation torus equations; no hierarchy.
        var state = initial.Clone(); state.Iterations = 1; state.Detail = .05;
        var dimensions = AntoineGeometry.Measure(state.Antoine);
        var root = new AntoineGeometry.Ring(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 1);
        var rings = Enumerable.Range(0, state.Antoine.Links).Select(i => AntoineGeometry.Child(root, i, state.Antoine, dimensions.Scale)).ToArray();
        Vector3 origin = Fractal3DCamera.Build(state).Position;
        int hits = 0;
        for (int y = 22; y < 78; y += 5)
        for (int x = 18; x < 82; x += 5)
        {
            Vector3 ray = Fractal3DCamera.PixelRay(state, x + .5, y + .5, 96, 96);
            double nearest = double.NaN;
            for (double t = .002; t < 5; t += .002)
                if (Distance(t) < 0)
                {
                    double lo = t - .002, hi = t;
                    for (int j = 0; j < 24; j++) { double mid = (lo + hi) / 2; if (Distance(mid) > 0) lo = mid; else hi = mid; }
                    nearest = (lo + hi) / 2; break;
                }
            if (!double.IsFinite(nearest)) continue;
            // A probe renders one pixel; a larger logical image reduces the footprint
            // tolerance without requiring an expensive large rendered frame.
            double actual = await renderer.ProbeDistanceAsync(state, (x + .5) * 10, (y + .5) * 10, 960, 960, CancellationToken.None);
            Check(double.IsFinite(actual) && Math.Abs(actual - nearest) < .003, $"Antoine probe {x},{y}: {actual} vs {nearest}.");
            hits++;

            double Distance(double t)
            {
                Vector3 p = origin + ray * (float)t;
                double best = double.PositiveInfinity;
                foreach (var ring in rings)
                {
                    Vector3 d = p - ring.Center;
                    double height = Vector3.Dot(d, ring.Normal);
                    double radial = Math.Sqrt(Math.Max(0, d.LengthSquared() - height * height)) - ring.Radius;
                    best = Math.Min(best, Math.Sqrt(radial * radial + height * height) - ring.Radius * dimensions.TubeRatio);
                }
                return best;
            }
        }
        Check(hits > 8, "Independent probes must cover the surface.");
        // Starting inside a tube traces its exit instead of reporting a hit at zero.
        var inside = initial.Clone(); inside.Iterations = 0;
        inside.TargetX = 1; inside.CameraDistance = .01; inside.CameraPitch = 0; inside.CameraYaw = 0;
        inside.Detail = .05;
        double exit = await renderer.ProbeDistanceAsync(inside, 480, 480, 960, 960, CancellationToken.None);
        Check(Math.Abs(exit - (.01 + dimensions.TubeRatio)) < .002, $"Inside-torus exit: {exit}.");
    }
}
