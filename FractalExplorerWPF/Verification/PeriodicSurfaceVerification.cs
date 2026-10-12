using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
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
            s => s with { Region = PeriodicSurfaceRegion.Negative }
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
            if (region == PeriodicSurfaceRegion.Negative) expectedZ = 1;
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
            var roots = new List<double>();
            double[] targets = region == PeriodicSurfaceRegion.Sheet
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
        Check(File.Exists(AppPaths.GetShaderCacheFile("fractal3d-PeriodicSurfaces-pixel")), "The shader must use the shared disk cache.");

        state.SaveName = "Periodic roundtrip";
        state.PeriodicSurface = new() { Family = PeriodicSurfaceFamily.SchwarzD, Region = PeriodicSurfaceRegion.Sheet,
            Period = 1.2345678901234567, Level = .17, Thickness = .23456789012345678,
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
            Check(window.CaptureState("UI").PeriodicSurface == state.PeriodicSurface, "UI must retain imported parameter precision.");
            Check(((FrameworkElement)window.FindName("PeriodicPanel")).Visibility == Visibility.Visible &&
                ((FrameworkElement)window.FindName("BailoutPanel")).Visibility == Visibility.Collapsed,
                "Only the mode's shape controls should be visible.");
            ((Slider)window.FindName("PeriodicPeriodSlider")).Value = 1.75;
            Check(window.CaptureState("slider").PeriodicSurface.Period == 1.75, "Period slider must reach the state.");
            ((TextBox)window.FindName("PeriodicLevelBox")).Text = "0.123456789";
            Check(window.CaptureState("text").PeriodicSurface.Level == .123456789, "Exact numeric entry must reach the state.");
            ((ComboBox)window.FindName("PeriodicFamilyBox")).SelectedIndex = 0;
            Check(window.CaptureState("family").PeriodicSurface.Family == PeriodicSurfaceFamily.Gyroid, "Family selection must reach the state.");
            ((ComboBox)window.FindName("PeriodicRegionBox")).SelectedIndex = 1;
            Check(!((FrameworkElement)window.FindName("PeriodicThicknessPanel")).IsEnabled, "Thickness only applies to walls.");
            ((ComboBox)window.FindName("PeriodicCutBox")).SelectedIndex = 0;
            Check(!((Slider)window.FindName("PeriodicCutPositionSlider")).IsEnabled, "Cut position requires a selected axis.");
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
        Console.WriteLine("PASS (periodic-surfaces): three families, eight presets, analytic ray intersections, inside camera, shape controls, nine styles, saves/cloud, cache and cancellation.");
    }
}
