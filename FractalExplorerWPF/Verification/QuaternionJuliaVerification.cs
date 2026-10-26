using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private static async Task VerifyQuaternionJuliaAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("quaternion-julia");
        string output = ReadOption(args, "--out") ?? Path.Combine(AppPaths.DataRoot, "QuaternionJuliaVerification");
        Directory.CreateDirectory(output); EnsureWindowThemeStyles();
        var kind = Fractal3DKind.QuaternionJulia;
        var state = Fractal3DCatalog.CreateDefaultState(kind);
        Check(state.QuaternionJulia is null && !JsonSerializer.Serialize(state).Contains("\"QuaternionJulia\""), "Legacy JSON must retain the original slice.");
        var identity = state.Clone(); identity.QuaternionJulia = new();
        Check(Fractal3DFrameConstantsHex(state, 640, 480, 0) == Fractal3DFrameConstantsHex(identity, 640, 480, 0),
            "Identity slice must retain every legacy GPU constant.");
        var tilt = new QuaternionJuliaSettings { XW = 37, YW = -24, ZW = 51 };
        var origin = tilt.SlicePoint(Vector3.Zero, .3);
        var basis = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ }.Select(v => tilt.SlicePoint(v, 0)).ToArray();
        Check(Math.Abs(origin.Length() - .3) < 1e-6, "Rotating the offset must preserve its norm.");
        for (int i = 0; i < 3; i++)
        {
            Check(Math.Abs(basis[i].Length() - 1) < 1e-6 && Math.Abs(Vector4.Dot(basis[i], origin)) < 1e-6,
                "Slice axes and offset must stay orthonormal.");
            for (int j = 0; j < i; j++) Check(Math.Abs(Vector4.Dot(basis[i], basis[j])) < 1e-6, "Slice axes lost orthogonality.");
        }
        var quarterTurn = new QuaternionJuliaSettings { XW = 90 }.SlicePoint(new Vector3(1, 2, 3), 4);
        Check(Vector4.Distance(quarterTurn, new Vector4(-4, 2, 3, 1)) < 1e-6, "XW rotation order/sign is wrong.");
        int direction = 1;
        var sweep = new QuaternionJuliaSettings { SweepMinimum = -1, SweepMaximum = 1, SweepSpeed = 1 };
        Check(Math.Abs(sweep.Advance(.9, .3, ref direction) - .8) < 1e-10 && direction == -1, "Sweep must reflect at the upper endpoint.");
        Check(Math.Abs(sweep.Advance(-.9, 2, ref direction) - .9) < 1e-10 && direction == 1, "Sweep must reflect at the lower endpoint.");
        try { (tilt with { XW = double.NaN }).Validate(); throw new Exception("Invalid rotation accepted."); } catch (ArgumentException) { }
        using var renderer = new Fractal3DRenderer();
        byte[] reference = Pixels(await renderer.RenderAsync(state, 128, 128, null, CancellationToken.None));
        byte[] identityPixels = Pixels(await renderer.RenderAsync(identity, 128, 128, null, CancellationToken.None));
        Check(reference.SequenceEqual(identityPixels), "Identity rotation changed the classic frame.");
        state.QuaternionJulia = tilt; state.QuaternionSlice = .15;
        var tiltedFrame = await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None);
        Check(HasFractal3DStructure(Pixels(tiltedFrame), 160, 160), "Tilted slice must remain visible.");
        WriteLSystemPng(Path.Combine(output, "tilted.png"), tiltedFrame);
        var tiltedSmall = Pixels(await renderer.RenderAsync(state, 128, 128, null, CancellationToken.None));
        Check(!reference.SequenceEqual(tiltedSmall), "Slice tilt must change the geometry.");
        foreach (var preset in Fractal3DCatalog.GetPresets(kind).Where(p => p.QuaternionJulia is not null))
        {
            var frame = await renderer.RenderAsync(preset, 128, 128, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(frame), 128, 128), $"Empty new slice preset: {preset.SaveName}.");
            WriteLSystemPng(Path.Combine(output, preset.QuaternionJulia!.XW == 90 ? "perpendicular.png" : "oblique-preset.png"), frame);
        }
        // For C=0 the filled Julia set is the unit 4-ball. Every rotated slice is a sphere of radius sqrt(1-w²).
        int rays = 0;
        foreach (var rotation in new[] { new QuaternionJuliaSettings(), tilt, new QuaternionJuliaSettings { XW = 90, YW = -50, ZW = 120 } })
        foreach (double offset in new[] { 0, .35, -.65 })
        {
            var sphere = Fractal3DCatalog.CreateDefaultState(kind);
            sphere.JuliaCX = sphere.JuliaCY = sphere.JuliaCZ = sphere.JuliaCW = 0;
            sphere.QuaternionJulia = rotation; sphere.QuaternionSlice = offset;
            sphere.CameraYaw = sphere.CameraPitch = sphere.CameraRoll = 0; sphere.CameraDistance = 3;
            sphere.Iterations = 20; sphere.MaxSteps = 1024; sphere.Detail = .05;
            foreach (double x in new[] { -.2, 0, .3 })
            {
                sphere.TargetX = x;
                double expected = 3 - Math.Sqrt(1 - offset * offset - x * x);
                double actual = await renderer.ProbeDistanceAsync(sphere, 256, 256, 512, 512, CancellationToken.None);
                Check(double.IsFinite(actual) && Math.Abs(actual - expected) < .004, $"Analytic 4-ball slice: expected {expected}, GPU {actual}."); rays++;
            }
        }
        // Rotations of the three imaginary components preserve quaternion multiplication.
        // Rotate both C and the affine slice: every escape norm, and thus the surface, must stay the same.
        var automorphism = new QuaternionJuliaSettings { YW = 43, ZW = -31 };
        var original = Fractal3DCatalog.CreateDefaultState(kind); original.QuaternionSlice = .12;
        original.MaxSteps = 1024; original.Detail = .05;
        var equivalent = original.Clone(); equivalent.QuaternionJulia = automorphism;
        var c = automorphism.SlicePoint(new Vector3((float)original.JuliaCX, (float)original.JuliaCY, (float)original.JuliaCZ), original.JuliaCW);
        equivalent.JuliaCX = c.X; equivalent.JuliaCY = c.Y; equivalent.JuliaCZ = c.Z; equivalent.JuliaCW = c.W;
        foreach (int x in new[] { 72, 90, 110 })
        foreach (int y in new[] { 72, 90, 110 })
        {
            double a = await renderer.ProbeDistanceAsync(original, x, y, 180, 180, CancellationToken.None);
            double b = await renderer.ProbeDistanceAsync(equivalent, x, y, 180, 180, CancellationToken.None);
            Check(double.IsNaN(a) ? double.IsNaN(b) : double.IsFinite(b) && Math.Abs(a - b) < .006, "Quaternion automorphism changed the surface."); rays++;
        }
        state.SaveName = "Tilted slice";
        var store = new Fractal3DSaveStore(kind); store.Save(state);
        var loaded = store.Load().Single();
        Check(loaded.QuaternionJulia == tilt && loaded.QuaternionSlice == .15 && loaded.Clone().QuaternionJulia == tilt, "Slice settings save/snapshot roundtrip failed.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().JsonData.Contains("\"XW\"") && unreadable == 0, "Cloud JSON lost slice angles.");
        var main = new Fractal3DWindow(kind);
        QuaternionJuliaAtlasWindow? atlas = null;
        try
        {
            main.LoadState(state);
            var editor = (QuaternionSliceEditor)main.FindName("QuaternionEditor");
            Check(main.CaptureState("UI").QuaternionJulia == tilt && !editor.IsPlaying, "Loading must retain angles and stay paused.");
            var exact = state.Clone(); exact.JuliaCX = .12345678901234567; exact.QuaternionSlice = .23456789012345678;
            main.LoadState(exact);
            Check(main.CaptureState("exact").JuliaCX == exact.JuliaCX && main.CaptureState("exact").QuaternionSlice == exact.QuaternionSlice,
                "Loading must retain the full double precision of C and the slice offset.");
            main.LoadState(state);
            ((TextBox)editor.FindName("MinimumBox")).Text = ".9";
            ((TextBox)editor.FindName("MaximumBox")).Text = "-.9";
            Check(main.CaptureState("static with invalid sweep").QuaternionJulia == tilt,
                "An invalid inactive sweep must not block rendering/capturing the static slice.");
            Check(NumericInput.GetHasNotice((TextBox)editor.FindName("MinimumBox")), "Restored sweep bounds need an explanation at the field.");
            editor.Start(); Check(editor.IsPlaying, "Sweep must start after NumericInput restores the valid pair.");
            main.LoadState(state);
            var xw = (TextBox)editor.FindName("XWBox"); xw.Text = "181"; NumericInput.CommitAll(main);
            Check(editor.Capture()!.XW == 180 && NumericInput.GetHasNotice(xw), "Slice angle must clamp through NumericInput.");
            main.LoadState(state);
            typeof(Fractal3DWindow).GetField("_quaternionDisplayedSlice", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, .15);
            Click((Button)editor.FindName("PlayButton"));
            await Task.Delay(20); editor.OnFrameDisplayed(.15);
            Check(editor.IsPlaying && main.CaptureState("shown").QuaternionSlice == .15, "Saving must capture the shown slice, not the queued next frame.");
            Click((Button)editor.FindName("PlayButton"));
            Check(!editor.IsPlaying && main.CaptureState("paused").QuaternionSlice == .15, "Pause must restore the displayed slice.");
            editor.Start(); main.LoadState(state);
            Check(!editor.IsPlaying, "Loading a scene must stop the sweep.");
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            atlas = (QuaternionJuliaAtlasWindow)typeof(Fractal3DWindow).GetMethod("CreateQuaternionAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
            var axesX = (ComboBox)atlas.FindName("XAxisBox"); var axesY = (ComboBox)atlas.FindName("YAxisBox");
            var fixedBoxes = ((WrapPanel)atlas.FindName("FixedPointPanel")).Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
            Check(fixedBoxes.Length == 8 && fixedBoxes.All(NumericInput.IsRegistered), "Atlas fixed components bypass NumericInput.");
            for (int x = 0; x < 8; x++) for (int y = x + 1; y < 8; y++)
            {
                axesX.SelectedIndex = x; axesY.SelectedIndex = y;
                Check(axesX.SelectedIndex != axesY.SelectedIndex && fixedBoxes.Count(b => b.IsEnabled) == 6, "Atlas axes must remain distinct.");
            }
            axesX.SelectedIndex = 0; axesY.SelectedIndex = 3;
            fixedBoxes[1].Text = "0.2727"; fixedBoxes[2].Text = "0.6818";
            MethodInfo build = typeof(ParameterAtlasWindow).GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Task old = (Task)build.Invoke(atlas, null)!;
            ((TextBox)atlas.FindName("MinRealBox")).Text = "-0.2";
            ((TextBox)atlas.FindName("MaxRealBox")).Text = "0.1";
            Task current = (Task)build.Invoke(atlas, null)!;
            await Task.WhenAll(old, current);
            var tiles = (UniformGrid)atlas.FindName("TilesPanel");
            Check(tiles.Children.Count == 9 && tiles.Children.OfType<Button>().All(b => b.IsEnabled) && !atlas.IsRendering, "Atlas must finish only the latest GPU generation.");
            Check(((TextBlock)atlas.FindName("StatusText")).Text.Contains("ГП"), "Atlas must report its GPU previews.");
            Check(atlas.SelectedConstant[1] == .2727m && atlas.SelectedConstant[2] == .6818m, "Atlas must retain fixed components.");
            var root = (FrameworkElement)main.Content; root.Measure(new Size(1240, 950)); root.Arrange(new Rect(0, 0, 1240, 950)); root.UpdateLayout();
            var panel = new QuaternionSliceEditor(); panel.Load(tilt);
            panel.Measure(new Size(300, double.PositiveInfinity)); panel.Arrange(new Rect(new Size(300, panel.DesiredSize.Height))); panel.UpdateLayout();
            var ui = new RenderTargetBitmap(300, (int)Math.Ceiling(panel.ActualHeight), 96, 96, PixelFormats.Pbgra32); ui.Render(panel);
            WriteLSystemPng(Path.Combine(output, "editor.png"), ui);
            var atlasRoot = (FrameworkElement)atlas.Content;
            atlasRoot.Measure(new Size(1060, 820)); atlasRoot.Arrange(new Rect(0, 0, 1060, 820)); atlasRoot.UpdateLayout();
            var atlasImage = new RenderTargetBitmap(1060, 820, 96, 96, PixelFormats.Pbgra32); atlasImage.Render(atlasRoot);
            WriteLSystemPng(Path.Combine(output, "atlas.png"), atlasImage);
            var before = main.CaptureState("before");
            var selectedConstant = atlas.SelectedConstant;
            Click((Button)atlas.FindName("ApplyButton")); atlas = null;
            var after = main.CaptureState("after");
            Check(after.QuaternionJulia == before.QuaternionJulia && after.QuaternionSlice == before.QuaternionSlice && after.CameraDistance == before.CameraDistance &&
                after.CameraYaw == before.CameraYaw && after.CameraPitch == before.CameraPitch && after.JuliaCW == (double)selectedConstant[3],
                "Applying an atlas tile with fixed slice parameters must only change C.");
            atlas = (QuaternionJuliaAtlasWindow)typeof(Fractal3DWindow).GetMethod("CreateQuaternionAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
            Task closing = (Task)build.Invoke(atlas, null)!; atlas.Close(); await closing; atlas = null;
        }
        finally { atlas?.Close(); main.Close(); }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await renderer.RenderAsync(state, 1024, 1024, null, canceled.Token); throw new Exception("Canceled slice rendered."); } catch (OperationCanceledException) { }
        Console.WriteLine($"PASS (quaternion-julia): {rays} analytic/invariant GPU rays, identity frame, tilt, sweep, shown-state pause, JSON/cloud, numeric input, atlas generations, apply and close. PNG: {output}");
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
