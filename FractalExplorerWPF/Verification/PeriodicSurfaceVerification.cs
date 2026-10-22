using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Text.Json;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyPeriodicSurfacesAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("periodic-surfaces");
        string output = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "VerificationData", "PeriodicSurfaces");
        Directory.CreateDirectory(output);
        var kind = Fractal3DKind.PeriodicSurfaces;
        var item = FractalCatalog.Create().Single(t => t.LaunchKey == Fractal3DCatalog.LaunchKey(kind));
        Check(item.IsThreeDimensional && CatalogPreviewLoader.IsRendered(item), "Periodic surfaces need a GPU catalog preview.");
        Check(item.IntroducedAt is not null,
            "Periodic surfaces must retain their introduction date.");
        var state = Fractal3DCatalog.CreateDefaultState(kind);
        Check(state.ColoringMode == Fractal3DColoringMode.Material, "The starting surface must use material coloring.");
        foreach (var invalid in new[] { state.PeriodicSurface with { Period = double.NaN },
            state.PeriodicSurface with { StretchX = 0 }, state.PeriodicSurface with { CutAxis = 4 },
            state.PeriodicSurface with { Family = (PeriodicSurfaceFamily)99 },
            state.PeriodicSurface with { BlendFamily = (PeriodicSurfaceFamily)99 },
            state.PeriodicSurface with { Blend = double.NaN },
            state.PeriodicSurface with { Blend = 1.01 },
            state.PeriodicSurface with { ThicknessAxis = 4 },
            state.PeriodicSurface with { ThicknessGradient = -.91 },
            state.PeriodicSurface with { Thickness = double.PositiveInfinity } })
        {
            bool rejected = false;
            try { invalid.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid imports must be rejected before reaching GPU constants.");
        }
        using var renderer = new Fractal3DRenderer();
        int index = 0;
        foreach (var preset in Fractal3DCatalog.GetPresets(kind))
        {
            var bitmap = await renderer.RenderAsync(preset, 320, 320, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap), 320, 320), $"Blank periodic preset: {preset.SaveName}.");
            WriteLSystemPng(Path.Combine(output, $"preset-{index++}.png"), bitmap);
            Console.WriteLine($"Periodic preset: {preset.SaveName}");
        }
        byte[] reference = Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None));
        Func<PeriodicSurfaceSettings, PeriodicSurfaceSettings>[] changes =
        [
            s => s with { Family = PeriodicSurfaceFamily.SchwarzP },
            s => s with { Family = PeriodicSurfaceFamily.SchwarzD },
            s => s with { Period = 1.6 }, s => s with { Level = .4 }, s => s with { Thickness = .6 },
            s => s with { StretchX = 1.5 }, s => s with { StretchY = 1.5 }, s => s with { StretchZ = 1.5 },
            s => s with { CutAxis = 1 }, s => s with { CutAxis = 2 }, s => s with { CutAxis = 3, CutPosition = -.2 },
            s => s with { Region = PeriodicSurfaceRegion.Surface }, s => s with { Region = PeriodicSurfaceRegion.Positive },
            s => s with { Region = PeriodicSurfaceRegion.Negative },
            s => s with { Blend = .5 }, s => s with { Region = PeriodicSurfaceRegion.Both },
            s => s with { ThicknessAxis = 1, ThicknessGradient = .9 },
            s => s with { ThicknessAxis = 2, ThicknessGradient = -.9 },
            s => s with { ThicknessAxis = 3, ThicknessGradient = .9 }
        ];
        foreach (var change in changes)
        {
            var changed = state.Clone(); changed.PeriodicSurface = change(state.PeriodicSurface);
            byte[] changedFrame = Pixels(await renderer.RenderAsync(changed, 160, 160, null, CancellationToken.None));
            Check(!reference.SequenceEqual(changedFrame),
                "Every shape parameter must change the GPU frame.");
        }
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var styled = state.Clone(); styled.ShadingStyle = style;
            var frame = await renderer.RenderAsync(styled, 160, 160, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(frame), 160, 160), $"Empty periodic style: {style}.");
            WriteLSystemPng(Path.Combine(output, $"style-{style}.png"), frame);
        }

        // Independent analytic rays: for Schwarz P at x=y=.5 and period 2,
        // F=cos(πz). For Schwarz D at x=y=0, F also equals cos(πz).
        // No DE or marching algorithm from the application is reused here.
        foreach (var family in Enum.GetValues<PeriodicSurfaceFamily>())
        foreach (var region in Enum.GetValues<PeriodicSurfaceRegion>())
        {
            var rayState = state.Clone();
            double coordinate = family == PeriodicSurfaceFamily.SchwarzP ? .5 : 0;
            rayState.CameraYaw = rayState.CameraPitch = 0;
            rayState.CameraDistance = 3;
            rayState.TargetX = rayState.TargetY = coordinate;
            rayState.TargetZ = 0;
            rayState.Detail = .05; rayState.MaxSteps = 1024;
            rayState.PeriodicSurface = new() { Family = family, Region = region, Period = 2, Level = .25, Thickness = .3 };
            double expectedZ = family == PeriodicSurfaceFamily.Gyroid
                ? 1 - Math.Asin(region == PeriodicSurfaceRegion.Sheet ? .1 : .25) / Math.PI
                : Math.Acos(region == PeriodicSurfaceRegion.Sheet ? .1 : .25) / Math.PI;
            // A filled region already present on z=1 hits the clipping cap.
            if (region is PeriodicSurfaceRegion.Negative or PeriodicSurfaceRegion.Both) expectedZ = 1;
            double? actual = await renderer.ProbeDistanceAsync(rayState, 256, 256, 512, 512, CancellationToken.None);
            Check(actual is > 0 && Math.Abs(actual.Value - (3 - expectedZ)) < .003,
                $"{family}/{region} analytic intersection: expected {3-expectedZ:G8}, GPU {actual:G8}.");
        }
        // General off-axis rays with unequal stretch: each field reduces independently
        // to A + B cos(kz) + C sin(kz). Solve all roots, then choose the frontmost one.
        foreach (var family in Enum.GetValues<PeriodicSurfaceFamily>())
        foreach (var region in Enum.GetValues<PeriodicSurfaceRegion>())
        {
            var rayState = state.Clone();
            rayState.CameraYaw = rayState.CameraPitch = 0; rayState.CameraDistance = 3;
            rayState.TargetX = rayState.TargetY = .33;
            rayState.Detail = .05; rayState.MaxSteps = 1024;
            var settings = new PeriodicSurfaceSettings { Family = family, Region = region, Period = 1.8,
                StretchX = 1.2, StretchY = .8, StretchZ = 1.4, Level = .15, Thickness = .2 };
            rayState.PeriodicSurface = settings;
            // Match the representable float camera target; calculations of the field and roots remain double.
            double x = Math.Tau * (double)(float).33 / (settings.Period * settings.StretchX);
            double y = Math.Tau * (double)(float).33 / (settings.Period * settings.StretchY);
            (double a, double b, double c) = family switch
            {
                PeriodicSurfaceFamily.SchwarzP => (Math.Cos(x) + Math.Cos(y), 1, 0),
                PeriodicSurfaceFamily.SchwarzD => (0, Math.Cos(x)*Math.Cos(y), -Math.Sin(x)*Math.Sin(y)),
                _ => (Math.Sin(x)*Math.Cos(y), Math.Sin(y), Math.Cos(x))
            };
            double k = Math.Tau / (settings.Period * settings.StretchZ);
            double frontField = a + b*Math.Cos(k) + c*Math.Sin(k) - settings.Level;
            bool frontFilled = region switch {
                PeriodicSurfaceRegion.Sheet => Math.Abs(frontField) <= settings.Thickness*.5,
                PeriodicSurfaceRegion.Positive => frontField >= 0,
                PeriodicSurfaceRegion.Negative => frontField <= 0, _ => false };
            if (region == PeriodicSurfaceRegion.Both) frontFilled = Math.Abs(frontField) >= settings.Thickness*.5;
            var roots = new List<double>();
            double[] targets = region is PeriodicSurfaceRegion.Sheet or PeriodicSurfaceRegion.Both
                ? [settings.Level - settings.Thickness*.5, settings.Level + settings.Thickness*.5] : [settings.Level];
            foreach (double target in targets)
            {
                double ratio = (target - a) / Math.Sqrt(b*b + c*c);
                if (Math.Abs(ratio) > 1) continue;
                double angle = Math.Acos(ratio), phase = Math.Atan2(c, b);
                for (int winding = -2; winding <= 2; winding++)
                foreach (int sign in new[] { -1, 1 })
                {
                    double z = (phase + sign*angle + winding*Math.Tau) / k;
                    if (z is >= -1 and <= 1) roots.Add(z);
                }
            }
            Check(frontFilled || roots.Count > 0, "Off-axis reference ray must cross a surface.");
            double expected = 3 - (frontFilled ? 1 : roots.Max());
            double actual = await renderer.ProbeDistanceAsync(rayState, 256, 256, 512, 512, CancellationToken.None);
            Check(actual > 0 && Math.Abs(actual - expected) < .003,
                $"{family}/{region} off-axis stretched ray: expected {expected:G8}, GPU {actual:G8}.");
        }
        var clipped = state.Clone();
        clipped.CameraYaw = clipped.CameraPitch = 0;
        clipped.CameraDistance = 3; clipped.TargetX = clipped.TargetY = .5;
        clipped.Detail = .05; clipped.MaxSteps = 1024;
        clipped.PeriodicSurface = new() { Family = PeriodicSurfaceFamily.SchwarzP, Region = PeriodicSurfaceRegion.Positive,
            Period = 2, Level = .25, CutAxis = 3, CutPosition = .2 };
        var cap = await renderer.ProbeDistanceAsync(clipped, 256, 256, 512, 512, CancellationToken.None);
        Check(cap is > 0 && Math.Abs(cap - 2.8) < .003, "The cut must create a cap in the filled labyrinth.");
        // Camera starts in solid material: its first hit must be the exit, not distance zero.
        clipped.CameraDistance = .1; clipped.PeriodicSurface = clipped.PeriodicSurface with { CutAxis = 0 };
        cap = await renderer.ProbeDistanceAsync(clipped, 256, 256, 512, 512, CancellationToken.None);
        Check(cap is > .4 && Math.Abs(cap - (.1 + Math.Acos(.25)/Math.PI)) < .003,
            "A camera inside material must trace the exit surface.");
        await VerifyPeriodicExtensionsAsync(renderer, state);
        Check(File.Exists(AppPaths.GetShaderCacheFile("fractal3d-PeriodicSurfaces-pixel")), "The shader must use the shared disk cache.");

        state.SaveName = "Periodic roundtrip";
        state.PeriodicSurface = new() { Family = PeriodicSurfaceFamily.SchwarzD, Region = PeriodicSurfaceRegion.Sheet,
            Period = 1.2345678901234567, Level = .17, Thickness = .23456789012345678,
            BlendFamily = PeriodicSurfaceFamily.Gyroid, Blend = .34567890123456789,
            ThicknessAxis = 3, ThicknessGradient = -.6789012345678901,
            StretchX = .9, StretchY = 1.3, StretchZ = 1.7, CutAxis = 2, CutPosition = -.15 };
        var store = new Fractal3DSaveStore(kind); store.Save(state);
        var restored = store.Load().Single();
        Check(restored.PeriodicSurface == state.PeriodicSurface, "JSON must retain all surface parameters exactly.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().Category == "Fractal3DPeriodicSurfaces" && unreadable == 0,
            "Periodic saves must appear in the shared cloud repository.");
        byte[] restoredFrame = Pixels(await renderer.RenderAsync(restored, 160, 160, null, CancellationToken.None));
        byte[] originalFrame = Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None));
        Check(restoredFrame.SequenceEqual(originalFrame), "Restored saves must render identically.");

        var theme = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        var window = new Fractal3DWindow(kind);
        try
        {
            Check(((Grid)window.FindName("InitialPreparationLayer")).Visibility == Visibility.Visible,
                "The new mode must show the shared first-frame preparation.");
            window.LoadState(state);
            var editor = (PeriodicSurfaceEditor)window.FindName("PeriodicEditor");
            object Field(string name) => editor.FindName(name);
            Check(window.CaptureState("UI").PeriodicSurface == state.PeriodicSurface, "UI must retain imported parameter precision.");
            Check(editor.Visibility == Visibility.Visible &&
                ((FrameworkElement)window.FindName("BailoutPanel")).Visibility == Visibility.Collapsed,
                "Only the mode's shape controls should be visible.");
            ((Slider)Field("PeriodicPeriodSlider")).Value = 1.75;
            Check(window.CaptureState("slider").PeriodicSurface.Period == 1.75, "Period slider must reach the state.");
            ((TextBox)Field("PeriodicLevelBox")).Text = "0.123456789";
            Check(window.CaptureState("text").PeriodicSurface.Level == .123456789, "Exact numeric entry must reach the state.");
            ((ComboBox)Field("PeriodicFamilyBox")).SelectedIndex = 0;
            Check(window.CaptureState("family").PeriodicSurface.Family == PeriodicSurfaceFamily.Gyroid, "Family selection must reach the state.");
            ((Slider)Field("PeriodicBlendSlider")).Value = .75;
            Check(window.CaptureState("blend").PeriodicSurface.Blend == .75, "Blend slider must reach the state.");
            ((ComboBox)Field("PeriodicRegionBox")).SelectedIndex = 4;
            Check(window.CaptureState("both").ColoringMode == Fractal3DColoringMode.OrbitTrap,
                "Selecting both labyrinths should enable their two-color palette.");
            ((TextBox)Field("PeriodicGradientBox")).Text = "-0.12345678901234567";
            Check(window.CaptureState("gradient").PeriodicSurface.ThicknessGradient == -.12345678901234567,
                "Gradient entry must retain precision.");
            ((ComboBox)Field("PeriodicRegionBox")).SelectedIndex = 1;
            Check(!((FrameworkElement)Field("PeriodicThicknessPanel")).IsEnabled, "Surface-only mode has no thickness.");
            ((TextBox)Field("PeriodicThicknessBox")).Text = "invalid inactive draft";
            ((TextBox)Field("PeriodicGradientBox")).Text = "NaN";
            NumericInput.CommitAll(window);
            Check(window.CaptureState("inactive").PeriodicSurface.Region == PeriodicSurfaceRegion.Surface,
                "Inactive width parameters must not block surface rendering.");
            ((ComboBox)Field("PeriodicCutBox")).SelectedIndex = 0;
            Check(!((Slider)Field("PeriodicCutPositionSlider")).IsEnabled, "Cut position requires a selected axis.");
            var presetsBox = (ComboBox)window.FindName("PresetBox");
            presetsBox.SelectedIndex = 7;
            var tunnelView = window.CaptureState("Tunnel preset");
            Check(tunnelView.NavigationMode == Fractal3DNavigationMode.Cad,
                "Selecting the tunnel preset must retain the normal left-button CAD rotation.");
            double tunnelDistance = await renderer.ProbeDistanceAsync(tunnelView, 160, 160, 320, 320, CancellationToken.None);
            Check(tunnelDistance > 2, "The tunnel preset must show the entrance from a distance, without a wall close to the camera.");
            window.LoadState(Fractal3DCatalog.CreateDefaultState(kind));
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(1240, 900)); root.Arrange(new Rect(0, 0, 1240, 900)); root.UpdateLayout();
            var render = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            await (Task)render.Invoke(window, [Enum.Parse(render.GetParameters()[0].ParameterType, "Full")])!;
            Check(((Image)window.FindName("CanvasImage")).Source is BitmapSource &&
                ((Grid)window.FindName("InitialPreparationLayer")).Visibility == Visibility.Collapsed,
                "The first actual WPF frame must dismiss the preparation screen.");
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1240, 900, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(root); WriteLSystemPng(Path.Combine(output, "periodic-controls.png"), bitmap);
        }
        finally { window.Close(); }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        bool canceledRender = false;
        try { await renderer.RenderAsync(state, 1024, 1024, null, canceled.Token); }
        catch (OperationCanceledException) { canceledRender = true; }
        Check(canceledRender, "Canceled exports must discard their output.");
        Console.WriteLine("PASS (periodic-surfaces): three families and blends, five regions, graded widths, twelve presets, independent intersections, UI, nine styles, saves/cloud, cache and cancellation.");
    }

    private static async Task VerifyPeriodicExtensionsAsync(Fractal3DRenderer renderer, Fractal3DState state)
    {
        var legacy = JsonSerializer.Deserialize<PeriodicSurfaceSettings>("{\"Family\":2,\"Thickness\":0.4}")!;
        Check(legacy.Blend == 0 && legacy.ThicknessAxis == 0 && legacy.ThicknessGradient == 0,
            "Saves without new properties must retain the original geometry.");
        foreach (var first in Enum.GetValues<PeriodicSurfaceFamily>())
        foreach (var second in Enum.GetValues<PeriodicSurfaceFamily>())
        {
            var endpoint = state.Clone();
            endpoint.PeriodicSurface = new() { Family = first, BlendFamily = second, Blend = 1 };
            var pure = state.Clone(); pure.PeriodicSurface = new() { Family = second };
            byte[] purePixels = Pixels(await renderer.RenderAsync(pure, 96, 96, null, CancellationToken.None));
            byte[] endpointPixels = Pixels(await renderer.RenderAsync(endpoint, 96, 96, null, CancellationToken.None));
            Check(endpointPixels.SequenceEqual(purePixels),
                $"Blend endpoint {first} → {second} must exactly equal the second family.");
            endpoint.PeriodicSurface = endpoint.PeriodicSurface with { Blend = 0, Family = second, BlendFamily = first };
            endpointPixels = Pixels(await renderer.RenderAsync(endpoint, 96, 96, null, CancellationToken.None));
            Check(endpointPixels.SequenceEqual(purePixels),
                "Zero blend must ignore the second family.");
        }

        // Independent double field evaluation with a dense 1D bracket and bisection,
        // without reusing the shader's distance estimator or gradient bound.
        foreach (var region in Enum.GetValues<PeriodicSurfaceRegion>())
        foreach (int axis in new[] { 1, 2, 3 })
        foreach (double gradient in new[] { -.9, .9 })
        {
            var ray = state.Clone(); ray.CameraYaw = ray.CameraPitch = 0;
            ray.CameraDistance = 3; ray.TargetX = .23; ray.TargetY = .33; ray.TargetZ = 0;
            ray.Detail = .03; ray.MaxSteps = 2048;
            var settings = new PeriodicSurfaceSettings { Blend = .43, Region = region, Period = 1.7,
                Level = .15, Thickness = .6, ThicknessAxis = axis, ThicknessGradient = gradient,
                StretchX = 1.2, StretchY = .8, StretchZ = 1.4, CutAxis = 3, CutPosition = .4 };
            ray.PeriodicSurface = settings;
            double x = (float)ray.TargetX, y = (float)ray.TargetY;
            double Field(double z)
            {
                double qx = Math.Tau*x/(settings.Period*settings.StretchX);
                double qy = Math.Tau*y/(settings.Period*settings.StretchY);
                double qz = Math.Tau*z/(settings.Period*settings.StretchZ);
                double g = Math.Sin(qx)*Math.Cos(qy) + Math.Sin(qy)*Math.Cos(qz) + Math.Sin(qz)*Math.Cos(qx);
                double p = Math.Cos(qx) + Math.Cos(qy) + Math.Cos(qz);
                double f = (1-settings.Blend)*g + settings.Blend*p - settings.Level;
                double w = settings.Thickness*.5*(1+gradient*(axis == 1 ? x : axis == 2 ? y : z));
                return region switch { PeriodicSurfaceRegion.Sheet => Math.Abs(f)-w,
                    PeriodicSurfaceRegion.Surface => f, PeriodicSurfaceRegion.Positive => -f,
                    PeriodicSurfaceRegion.Negative => f, _ => w-Math.Abs(f) };
            }
            double front = settings.CutPosition, expectedZ = double.NaN;
            if (region != PeriodicSurfaceRegion.Surface && Field(front) <= 0) expectedZ = front;
            else
            {
                double previous = front;
                for (int i = 1; i <= 4096; i++)
                {
                    double current = front-(front+1)*i/4096;
                    if (Math.Sign(Field(current)) != Math.Sign(Field(previous)))
                    {
                        double lo = current, hi = previous;
                        for (int j = 0; j < 45; j++)
                        {
                            double mid = (lo+hi)*.5;
                            if (Math.Sign(Field(mid)) == Math.Sign(Field(lo))) lo = mid; else hi = mid;
                        }
                        expectedZ = (lo+hi)*.5; break;
                    }
                    previous = current;
                }
            }
            double actual = await renderer.ProbeDistanceAsync(ray, 256, 256, 512, 512, CancellationToken.None);
            Check(double.IsFinite(expectedZ) && actual > 0 && Math.Abs(actual-(3-expectedZ)) < .004,
                $"Blended/{region}/axis {axis}/{gradient}: expected {3-expectedZ:G8}, GPU {actual:G8}.");
        }
        var colors = state.Clone();
        colors.PeriodicSurface = new() { Region = PeriodicSurfaceRegion.Both, CutAxis = 3 };
        byte[] material = Pixels(await renderer.RenderAsync(colors, 128, 128, null, CancellationToken.None));
        colors.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        byte[] colored = Pixels(await renderer.RenderAsync(colors, 128, 128, null, CancellationToken.None));
        Check(!material.SequenceEqual(colored),
            "Labyrinth palette must change the material frame.");
    }
}
