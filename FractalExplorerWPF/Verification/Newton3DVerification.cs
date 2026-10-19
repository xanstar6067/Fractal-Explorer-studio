using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
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
    private static async Task VerifyNewton3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("newton3d");
        string output = args.Length > 1 && !args[1].StartsWith("--") ? args[1] :
            Path.Combine(AppContext.BaseDirectory, "VerificationData", "Newton3DFrames");
        Directory.CreateDirectory(output);
        using var renderer = new Fractal3DRenderer();
        var initial = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Newton3D);
        var state = initial.Clone();

        foreach (int degree in new[] { 3, 5, 8 })
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"Computing Newton degree {degree}…");
            state.Newton = new() { Size = 32, Degree = degree, SliceW = .271, Relaxation = .85 };
            float[] field = await Task.Run(() => renderer.ReadNewtonField(state, CancellationToken.None));
            Check(field.Length == 32 * 32 * 32 * 4 && field.All(float.IsFinite), "Complete finite float4 field.");
            int compared = 0, disagreements = 0;
            for (int z = 0; z < 32; z += 3) for (int y = 0; y < 32; y += 3) for (int x = 0; x < 32; x += 3)
            {
                var s = state.Newton;
                double px = ((x + .5) / 32 * 2 - 1) * s.Extent;
                double py = ((y + .5) / 32 * 2 - 1) * s.Extent;
                double pz = ((z + .5) / 32 * 2 - 1) * s.Extent;
                var a = NewtonComplexReference(new(px + s.SliceW, py - pz), s);
                var b = NewtonComplexReference(new(px - s.SliceW, py + pz), s);
                int index = ((z * 32 + y) * 32 + x) * 4;
                // Long orbits lie close to fractal boundaries: single/double can choose different basins.
                if (Math.Max(a.Steps, b.Steps) < 30)
                {
                    compared++;
                    if (field[index + 1] != a.Root + 1 || field[index + 3] != b.Root + 1) disagreements++;
                    Check(Math.Abs(field[index + 2] * s.MaxIterations - Math.Max(a.Steps, b.Steps)) <= 3,
                        "Iteration count agrees with independent Complex.Pow reference away from boundaries.");
                }
            }
            Check(compared > 500 && disagreements < compared * .005, $"Degree {degree}: {disagreements}/{compared} root disagreements.");
            Console.WriteLine($"Newton q^{degree}: {compared} independent double orbits; {disagreements} root disagreements; {watch.Elapsed.TotalSeconds:F3}s; {renderer.DeviceHost.AdapterName}.");
        }

        // i²=j²=−1, ij=ji. Verify the sign of w and both idempotent projections independently.
        var q = new[] { .37, -.61, .28, .19 };
        var power = new[] { 1.0, 0, 0, 0 };
        for (int k = 0; k < 5; k++) power = NewtonBicomplexMultiply(power, q);
        Complex plus = Complex.Pow(new(q[0] + q[3], q[1] - q[2]), 5);
        Complex minus = Complex.Pow(new(q[0] - q[3], q[1] + q[2]), 5);
        Check(Complex.Abs(new Complex(power[0] + power[3], power[1] - power[2]) - plus) < 1e-12 &&
            Complex.Abs(new Complex(power[0] - power[3], power[1] + power[2]) - minus) < 1e-12,
            "Independent four-component product agrees with both idempotent powers.");
        Check(NewtonComplexReference(Complex.One, new()).Root == 0 &&
            NewtonComplexReference(Complex.Zero, new()).Root == -1, "Exact root and singular derivative.");

        // Grid sampling is at texel centres; all offered resolutions must complete.
        foreach (int side in new[] { 64, 96, 128, 192 })
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"Computing Newton grid {side}³…");
            state.Newton = new() { Size = side };
            var field = await Task.Run(() => renderer.ReadNewtonField(state, CancellationToken.None));
            Check(field.Length == side * side * side * 4 && field.All(float.IsFinite), $"Grid {side}³.");
            Console.WriteLine($"Newton grid {side}³: {watch.Elapsed.TotalSeconds:F3}s; {renderer.DeviceHost.AdapterName}.");
        }
        foreach (var bad in new[] { new Newton3DSettings { Size = 100 }, new() { Degree = 2 }, new() { Extent = double.NaN },
            new() { SliceW = double.PositiveInfinity }, new() { Relaxation = 0 }, new() { MaxIterations = 8 },
            new() { PlusRoot = 3 }, new() { Region = (Newton3DRegion)99 }, new() { BoundaryIterations = 64 },
            new() { SliceOnly = true, CutAxis = 0 }, new() { CutPosition = double.NaN } })
        {
            bool rejected = false;
            try { bad.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid Newton parameters rejected before GPU allocation.");
        }

        int presetIndex = 0;
        foreach (var preset in Fractal3DCatalog.GetPresets(Fractal3DKind.Newton3D))
        {
            var image = await renderer.RenderAsync(preset, 420, 420, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(image), 420, 420), $"Nonempty preset {preset.SaveName}.");
            PhysarumSavePng(image, Path.Combine(output, $"preset-{presetIndex++}.png"));
            Console.WriteLine($"Newton preset: {preset.SaveName}.");
        }
        state = initial.Clone();
        var first = Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None)).ToArray();
        int builds = renderer.NewtonFieldBuilds;
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            state.ShadingStyle = style;
            var image = await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(image), 160, 160), $"Newton style {style}.");
        }
        state.ShadingStyle = initial.ShadingStyle;
        foreach (var coloring in new[] { Fractal3DColoringMode.Material, Fractal3DColoringMode.OrbitTrap,
            Fractal3DColoringMode.CrossTrap, Fractal3DColoringMode.Escape, Fractal3DColoringMode.IterationIndex })
        {
            state.ColoringMode = coloring;
            Check(HasFractal3DStructure(Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None)),
                160, 160), $"Newton coloring {coloring}.");
        }
        state.ColoringMode = initial.ColoringMode;
        foreach (int axis in new[] { 1, 2, 3 })
        {
            state.Newton = state.Newton with { CutAxis = axis, SliceOnly = true, CutPosition = .13 };
            state.CameraYaw = axis == 1 ? 90 : 0; state.CameraPitch = axis == 2 ? 89.9 : 0;
            var image = await renderer.RenderAsync(state, 240, 240, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(image), 240, 240), $"Slice {axis}.");
            PhysarumSavePng(image, Path.Combine(output, $"slice-{axis}.png"));
            double distance = await renderer.ProbeDistanceAsync(state, 120, 120, 240, 240, CancellationToken.None);
            Check(double.IsFinite(distance) && distance > 0, "Probe intersects the slice.");
        }
        Check(renderer.NewtonFieldBuilds == builds, "Camera, styles, colors and slices reuse the computed volume.");
        state = initial.Clone();
        var repeated = Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None)).ToArray();
        Check(first.SequenceEqual(repeated),
            "Repeated render is deterministic.");
        using (var cancel = new CancellationTokenSource())
        {
            var changed = state.Clone(); changed.Newton = changed.Newton with { SliceW = -.73 };
            var frame = await renderer.RenderPixelsAsync(changed, 80, 80, null,
                new NewtonProgress(value => { if (value > 0) cancel.Cancel(); }), cancel.Token);
            Check(!frame.Completed && renderer.NewtonFieldBuilds == builds, "Cancellation publishes no partial field.");
        }
        await renderer.RenderAsync(state, 80, 80, null, CancellationToken.None);
        Check(renderer.NewtonFieldBuilds == builds, "Cancelled work preserves the old field.");
        state.Newton = state.Newton with { SliceW = -.3 };
        await renderer.RenderAsync(state, 80, 80, null, CancellationToken.None);
        Check(renderer.NewtonFieldBuilds == builds + 1, "Changing w invalidates the field.");

        state.SaveName = "Newton verification";
        state.Newton = state.Newton with { Extent = 1.71234567890123, SliceW = .31234567890123 };
        var store = new Fractal3DSaveStore(state.Kind); store.Save(state);
        var loaded = store.Load().Single();
        Check(loaded.Newton == state.Newton && loaded.Kind == state.Kind, "Exact JSON roundtrip.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Any(e => e.Category == "Fractal3DNewton") && unreadable == 0,
            "Common cloud repository discovers Newton saves.");
        var old = JsonSerializer.Deserialize<Fractal3DState>("{\"Kind\":0}", JsonOptionsFactory.Create())!;
        Check(old.Kind == Fractal3DKind.Mandelbulb && old.Newton is not null, "Old numeric enum values and missing Newton settings remain valid.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("newton3d-field")) && File.Exists(AppPaths.GetShaderCacheFile("newton3d-pixel")),
            "Compute and pixel shader use the common disk cache.");
        await VerifyNewtonFormulasAsync(renderer, initial, output);
        await VerifyNewtonWindowAsync(loaded, output);
        Console.WriteLine("PASS (newton3d): independent algebra/orbits, grids, six presets, styles/colors, slices/probes, reuse, cancellation, saves/cloud and WPF.");
    }

    private sealed class NewtonProgress(Action<int> callback) : IProgress<int>
    { public void Report(int value) => callback(value); }

    private static (int Root, int Steps) NewtonComplexReference(Complex z, Newton3DSettings s)
    {
        for (int step = 0; step <= s.MaxIterations; step++)
        {
            Complex residual = Complex.Pow(z, s.Degree) - 1;
            if (residual.Magnitude < 1e-5)
                return (((int)Math.Round(z.Phase * s.Degree / Math.Tau) + s.Degree) % s.Degree, step);
            Complex derivative = s.Degree * Complex.Pow(z, s.Degree - 1);
            if (step == s.MaxIterations || derivative.Magnitude < 1e-12 || derivative.Magnitude > 1e15 ||
                Math.Abs(z.Real) > 1e6 || Math.Abs(z.Imaginary) > 1e6) break;
            z -= s.Relaxation * residual / derivative;
        }
        return (-1, s.MaxIterations);
    }

    private static double[] NewtonBicomplexMultiply(double[] a, double[] b) => [
        a[0]*b[0]-a[1]*b[1]-a[2]*b[2]+a[3]*b[3],
        a[0]*b[1]+a[1]*b[0]-a[2]*b[3]-a[3]*b[2],
        a[0]*b[2]+a[2]*b[0]-a[1]*b[3]-a[3]*b[1],
        a[0]*b[3]+a[3]*b[0]+a[1]*b[2]+a[2]*b[1]];

    private static async Task VerifyNewtonWindowAsync(Fractal3DState state, string output)
    {
        EnsureWindowThemeStyles();
        var window = new Fractal3DWindow(Fractal3DKind.Newton3D);
        try
        {
            window.LoadState(state);
            Check(window.CaptureState("UI").Newton == state.Newton, "Editor preserves exact saved doubles.");
            var editor = (Newton3DEditor)window.FindName("NewtonEditor");
            Check(editor.Visibility == Visibility.Visible && ((FrameworkElement)window.FindName("BailoutPanel")).Visibility == Visibility.Collapsed,
                "Own Newton editor; escape radius is hidden.");
            ((ComboBox)editor.FindName("DegreeBox")).SelectedItem = 5;
            ((TextBox)editor.FindName("SliceWBox")).Text = "-0,3456789012345";
            Check(window.CaptureState("input").Newton is { Degree: 5, SliceW: -.3456789012345 }, "Live degree and comma input.");
            var current = typeof(Fractal3DWindow).GetMethod("IsCurrentNewtonFrame", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Check(!(bool)current.Invoke(window, [state])!, "Stale mathematical field cannot replace the current view.");
            ((ComboBox)editor.FindName("CutBox")).SelectedIndex = 0;
            ((CheckBox)editor.FindName("SliceBox")).IsChecked = true;
            Check(editor.Capture().CutAxis == 3, "Slice mode selects a valid plane automatically.");
            ((TextBox)editor.FindName("ExtentBox")).Text = "bad";
            var correctedExtent = editor.Capture().Extent;
            Check(double.IsFinite(correctedExtent) && ((TextBox)editor.FindName("ExtentBox")).Text != "bad",
                "Numeric input must restore the last correct extent before capture.");
            window.LoadState(state);
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(1200, 800)); root.Arrange(new Rect(0, 0, 1200, 800)); root.UpdateLayout();
            var layer = (Grid)window.FindName("InitialPreparationLayer");
            Check(layer.Visibility == Visibility.Visible, "Preparation is visible before the first frame.");
            var render = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)render.Invoke(window, [Enum.Parse(render.GetParameters()[0].ParameterType, "Draft")])!;
            Check(layer.Visibility == Visibility.Collapsed && ((Image)window.FindName("CanvasImage")).Source is BitmapSource,
                "Preparation closes after the current WPF frame is displayed.");
            root.UpdateLayout();
            var image = new RenderTargetBitmap(1200, 800, 96, 96, PixelFormats.Pbgra32); image.Render(root);
            PhysarumSavePng(image, Path.Combine(output, "editor.png"));
        }
        finally { window.Close(); }
    }
}
