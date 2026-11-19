using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyParametricSurfaceAsync(string[] args)
    {
        const Fractal3DKind kind = Fractal3DKind.ParametricSurface;
        using var renderer = new Fractal3DRenderer();
        var presets = Fractal3DCatalog.GetPresets(kind);
        foreach (var preset in presets)
        {
            var mesh = ParametricSurfaceMesh.Build(preset.ParametricSurface!, CancellationToken.None);
            Check(mesh.Triangles.Length > 120 && mesh.SkippedSamples == 0, $"Valid mesh for {preset.SaveName}.");
        }

        var sphere = new ParametricSurfaceSettings { Longitude = new(), Latitude = new(), Fit = false };
        var globe = ParametricSurfaceMesh.Build(sphere, CancellationToken.None);
        Check(Math.Abs(globe.Radius - 1) < 1e-6 && globe.Triangles.Length == 2 * sphere.USegments * (sphere.VSegments - 1),
            "Unit sphere has both poles and no zero-area triangles.");
        for (int i = 0; i < globe.Vertices.Length; i += 2)
        {
            var p = globe.Vertices[i];
            Check(Math.Abs(new Vector3(p.X, p.Y, p.Z).Length() - 1) < 1e-6, "All sphere points are on the unit sphere.");
        }

        var plane = sphere with
        {
            Family = ParametricSurfaceFamily.Equations, Equations = new("u", "0", "v"),
            Domain = new(-1, 1, -1, 1), USegments = 32, VSegments = 16
        };
        var patch = ParametricSurfaceMesh.Build(plane, CancellationToken.None);
        Check(patch.Triangles.Length == 2 * plane.USegments * plane.VSegments, "Plane samples every cell.");
        Check(patch.Vertices.Where((_, i) => i % 2 == 0).All(v => v.Y == 0), "Custom equation evaluates in u/v.");
        var singular = ParametricSurfaceMesh.Build(plane with { Equations = new("u", ".1/u", "v") }, CancellationToken.None);
        Check(singular.SkippedSamples == plane.VSegments + 1 && singular.Triangles.Length == 2 * (plane.USegments - 2) * plane.VSegments,
            "Undefined samples create a hole across the entire singular column.");

        foreach (var bad in new[]
        {
            plane with { Domain = new(1, -1, -1, 1) },
            plane with { Equations = new("u", "0", "u") },
            plane with { Equations = new("x", "0", "v") }
        })
        {
            bool rejected = false;
            try { ParametricSurfaceMesh.Build(bad, CancellationToken.None); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid domains, degenerate maps and unknown variables are reported.");
        }
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            bool stopped = false;
            try { ParametricSurfaceMesh.Build(plane, cancelled.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "Sampling honors cancellation.");
        }

        const int size = 144;
        string? output = ReadOption(args, "--out");
        if (output is not null) Directory.CreateDirectory(output);
        var representative = new[] { presets[0], presets[6], presets[7], presets[10] };
        for (int i = 0; i < representative.Length; i++)
        {
            var frame = await renderer.RenderAsync(representative[i], size, size, null, CancellationToken.None);
            if (output is not null) WriteLSystemPng(Path.Combine(output, $"parametric-{i}.png"), frame);
            Check(HasFractal3DStructure(Pixels(frame), size, size, size / 6), $"Visible GPU preset: {representative[i].SaveName}.");
        }

        var state = Fractal3DCatalog.CreateDefaultState(kind);
        state.ParametricSurface = sphere;
        double reference = Fractal3DCamera.Build(state).Position.Length() - 1;
        double actual = await renderer.ProbeDistanceAsync(state, size / 2.0, size / 2.0, size, size, CancellationToken.None);
        Check(double.IsFinite(actual) && Math.Abs(actual - reference) < .007, $"GPU sphere probe: {actual} vs {reference}.");
        double miss = await renderer.ProbeDistanceAsync(state, 0, 0, size, size, CancellationToken.None);
        Check(double.IsNaN(miss), "Background probe is a miss.");

        state.ParametricSurface = plane;
        state.CameraPitch = 60;
        byte[] solid = Pixels(await renderer.RenderAsync(state, size, size, null, CancellationToken.None));
        int builds = renderer.ParametricMeshBuilds;
        state.ParametricSurface = plane with { ShowGrid = true, GridLines = 8 };
        byte[] grid = Pixels(await renderer.RenderAsync(state, size, size, null, CancellationToken.None));
        Check(!solid.SequenceEqual(grid) && builds == renderer.ParametricMeshBuilds, "Grid changes pixels without rebuilding geometry.");
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        byte[] byU = Pixels(await renderer.RenderAsync(state, size, size, null, CancellationToken.None));
        state.ColoringMode = Fractal3DColoringMode.CrossTrap;
        byte[] byV = Pixels(await renderer.RenderAsync(state, size, size, null, CancellationToken.None));
        Check(!byU.SequenceEqual(byV) && builds == renderer.ParametricMeshBuilds, "U and V are independent GPU coloring sources.");
        state.CameraYaw += 41;
        await renderer.RenderAsync(state, size, size, null, CancellationToken.None);
        Check(builds == renderer.ParametricMeshBuilds, "A new camera position reuses geometry.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("fractal3d-ParametricSurface-pixel")), "Mode uses the shared shader cache.");

        state.ParametricSurface = presets[7].ParametricSurface! with { A = 1.123456789012345, ShowGrid = true, GridLines = 15 };
        state.SaveName = "Параметрическая проверка";
        var saves = new Fractal3DSaveStore(kind);
        saves.Save(state);
        Check(saves.Load().Single(s => s.SaveName == state.SaveName).ParametricSurface == state.ParametricSurface,
            "Save round trip preserves exact parameters, equations, domain and grid settings.");

        EnsureWindowThemeStyles();
        var window = new Fractal3DWindow(kind);
        try
        {
            window.LoadState(state);
            Check(window.CaptureState("ui").ParametricSurface == state.ParametricSurface, "Window restores exact settings.");
            var editor = (ParametricSurfaceEditor)window.FindName("ParametricEditor");
            Check(editor.Visibility == Visibility.Visible &&
                ((FrameworkElement)window.FindName("ShapeExpander")).Visibility == Visibility.Collapsed,
                "Mode exposes its own editor.");
            var z = (TextBox)editor.FindName("ZBox");
            var apply = (Button)editor.FindName("ApplyButton");
            z.Text = "unknown(";
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(NumericInput.GetHasNotice(z) && editor.Capture().Equations == state.ParametricSurface.Equations,
                "Invalid draft points to the field and leaves active equations in place.");
            z.Text = "u/2 + c*v";
            Check(editor.Capture().Equations == state.ParametricSurface.Equations, "An unapplied valid draft does not alter a save.");
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(editor.Capture().Equations.Z == z.Text && !NumericInput.GetHasNotice(z), "Applying a corrected draft replaces the equation.");
            foreach (var family in new[] { 0, 1 })
                ((ComboBox)editor.FindName("FamilyBox")).SelectedIndex = family;
            Check(editor.Capture().Equations.Z == z.Text, "Switching methods retains both settings.");

            ((ComboBox)editor.FindName("FamilyBox")).SelectedIndex = 0;
            var link = (CheckBox)editor.FindName("LinkBox");
            var latitude = LogicalTextBoxes((DependencyObject)editor.FindName("LatitudePanel")).Single(box =>
                AutomationProperties.GetName(box) == "Широта, m · повторение");
            latitude.Text = "13.123456789";
            link.IsChecked = true;
            Check(editor.Capture().Latitude.M == 13.123456789, "Linking before a preview preserves the last valid latitude edit.");
            latitude.Text = "NaN";
            Check(editor.Capture().Latitude.M == 13.123456789, "An inactive profile draft cannot block or change the saved settings.");
        }
        finally { window.Close(); }
        Console.WriteLine($"PASS (parametric-surface): {presets.Count} meshes, four GPU views, probe, cache, UI drafts and saves.");
    }

    private static IEnumerable<TextBox> LogicalTextBoxes(DependencyObject parent)
    {
        if (parent is TextBox box) yield return box;
        foreach (DependencyObject child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            foreach (TextBox descendant in LogicalTextBoxes(child)) yield return descendant;
    }
}
