using System.IO;
using System.Text.Json;
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
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    private static async Task VerifyKifsAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("kifs");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        var kind = Fractal3DKind.Kifs;
        Check(FractalCatalog.Create().Single(t => t.LaunchKey == Fractal3DCatalog.LaunchKey(kind)).IsThreeDimensional,
            "KIFS must be discoverable in the shared 3D catalog.");
        var state = Fractal3DCatalog.CreateDefaultState(kind);
        var clone = state.Clone(); clone.Kifs.OffsetX = .4;
        Check(state.Kifs.OffsetX == 1, "KIFS snapshots must clone independently.");
        var invalid = new KifsSettings { Scale = double.NaN, Radius = double.PositiveInfinity,
            Sectors = -4, Symmetry = (KifsSymmetry)200 }.Normalized();
        Check(invalid.Scale == 2 && invalid.Radius == 1 && invalid.Sectors == 3 && invalid.Symmetry == KifsSymmetry.Tetrahedral,
            "Invalid imported parameters must produce finite bounded GPU constants.");
        using var renderer = new Fractal3DRenderer();
        int index = 0;
        foreach (var preset in Fractal3DCatalog.GetPresets(kind))
        {
            var bitmap = await renderer.RenderAsync(preset, 320, 320, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap), 320, 320), $"{preset.SaveName}: empty frame.");
            if (output is not null)
            {
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"kifs-{index:D2}.png")); encoder.Save(file);
            }
            index++;
        }
        byte[] reference = Pixels(await renderer.RenderAsync(state, 140, 140, null, CancellationToken.None));
        Action<Fractal3DState>[] changes =
        [
            s => s.Kifs.Scale = 2.3, s => s.Kifs.Symmetry = KifsSymmetry.Cubic,
            s => s.Kifs.RotationX = 20, s => s.Kifs.RotationY = 20, s => s.Kifs.RotationZ = 20,
            s => s.Kifs.OffsetX = .6, s => s.Kifs.OffsetY = .6, s => s.Kifs.OffsetZ = .6,
            s => s.Kifs.Radius = .5, s => s.Kifs.Seed = KifsSeed.Cube,
            s => s.Kifs.Seed = KifsSeed.Octahedron, s => s.Iterations = 2
        ];
        foreach (var change in changes)
        {
            var changed = state.Clone(); change(changed);
            var frame = Pixels(await renderer.RenderAsync(changed, 140, 140, null, CancellationToken.None));
            Check(!reference.SequenceEqual(frame), "Each shape parameter must reach the shader.");
        }
        var star = Fractal3DCatalog.GetPresets(kind)[3].Clone();
        var six = Pixels(await renderer.RenderAsync(star, 140, 140, null, CancellationToken.None));
        star.Kifs.Sectors = 9;
        byte[] nine = Pixels(await renderer.RenderAsync(star, 140, 140, null, CancellationToken.None));
        Check(!six.SequenceEqual(nine),
            "The radial mirror count must change the star.");
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var styled = state.Clone(); styled.ShadingStyle = style;
            Check(HasFractal3DStructure(Pixels(await renderer.RenderAsync(styled, 120, 120, null, CancellationToken.None)), 120, 120),
                $"KIFS {style}: shared shading must render the form.");
        }
        for (int seed = 0; seed < 64; seed++)
        {
            var generated = state.Clone(); generated.Kifs = KifsRandomizer.Create(new Random(seed));
            Check(HasFractal3DStructure(Pixels(await renderer.RenderAsync(generated, 120, 120, null, CancellationToken.None)), 120, 120),
                $"Random KIFS seed {seed}: empty frame.");
        }
        bool hit = false;
        for (int y = 30; y < 140 && !hit; y += 20)
        for (int x = 30; x < 140 && !hit; x += 20)
            hit = await renderer.ProbeDistanceAsync(state, x, y, 160, 160, CancellationToken.None) is > 0;
        Check(hit, "The shared camera surface probe must hit KIFS.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("fractal3d-Kifs-pixel")), "KIFS must persist in the shared shader cache.");
        state.SaveName = "KIFS roundtrip";
        state.Kifs = star.Kifs.Clone();
        state.Kifs.OffsetX = 1.23456789;
        var store = new Fractal3DSaveStore(kind); store.Save(state);
        Check(JsonSerializer.Serialize(store.Load().Single().Kifs) == JsonSerializer.Serialize(state.Kifs), "KIFS save round trip failed.");
        var local = CloudSaveRepository.ListLocal(out int unreadable).Single();
        Check(unreadable == 0 && local.Category == "Fractal3DKifs", "KIFS must be available to the common cloud repository.");
        var theme = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        var window = new Fractal3DWindow(kind);
        try
        {
            window.LoadState(state);
            Check(JsonSerializer.Serialize(window.CaptureState("UI").Kifs) == JsonSerializer.Serialize(state.Kifs), "KIFS UI round trip failed.");
            Check(((FrameworkElement)window.FindName("KifsPanel")).Visibility == Visibility.Visible &&
                ((FrameworkElement)window.FindName("BailoutPanel")).Visibility == Visibility.Collapsed,
                "KIFS must show its own controls and hide the unused bailout.");
            var x = (Slider)window.FindName("KifsOffsetXSlider"); x.Value = .75;
            Check(window.CaptureState("Slider").Kifs.OffsetX == .75, "Slider must update the captured offset.");
            var exact = (TextBox)window.FindName("KifsRotationYBox"); exact.Text = "27.5";
            Check(window.CaptureState("Exact").Kifs.RotationY == 27.5, "Exact rotation entry must update the state.");
            var root = (FrameworkElement)window.Content;
            root.Measure(new System.Windows.Size(1240, 800));
            root.Arrange(new Rect(0, 0, 1240, 800)); root.UpdateLayout();
            var map = (Canvas)window.FindName("KifsOffsetMap");
            Check(map.ActualWidth > 100 && map.ActualHeight > 100 && map.Children.Count > 10,
                "The interactive map must have a visible grid and marker.");
            var before = window.CaptureState("Before map");
            typeof(Fractal3DWindow).GetMethod("SetKifsMapPoint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, [new System.Windows.Point(map.ActualWidth * .25, map.ActualHeight * .25)]);
            var after = window.CaptureState("After map");
            Check(after.Kifs.OffsetX == -1 && after.Kifs.OffsetY == 1 && after.Kifs.OffsetZ == before.Kifs.OffsetZ,
                "XY map must update X/Y and retain Z.");
            var plane = (ComboBox)window.FindName("KifsPlaneBox"); plane.SelectedIndex = 1;
            Check(window.CaptureState("After plane").Kifs.OffsetZ == before.Kifs.OffsetZ, "Changing the map plane must retain the form.");
            var beforeRandom = window.CaptureState("Before random");
            var privateMethods = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(Fractal3DWindow).GetMethod("KifsRandom_OnClick", privateMethods)!.Invoke(window, [window, new RoutedEventArgs()]);
            var afterRandom = window.CaptureState("After random");
            Check(afterRandom.CameraYaw == beforeRandom.CameraYaw && afterRandom.LightYaw == beforeRandom.LightYaw &&
                afterRandom.ResolvePalette().Name == beforeRandom.ResolvePalette().Name,
                "Randomization must retain camera, light and palette.");
            typeof(Fractal3DWindow).GetMethod("KifsUndo_OnClick", privateMethods)!.Invoke(window, [window, new RoutedEventArgs()]);
            Check(JsonSerializer.Serialize(window.CaptureState("Undo").Kifs) == JsonSerializer.Serialize(beforeRandom.Kifs),
                "Undo must restore the exact shape before randomization.");
            if (output is not null)
            {
                ((System.Windows.Controls.Image)window.FindName("CanvasImage")).Source =
                    await renderer.RenderAsync(window.CaptureState("UI preview"), 640, 560, null, CancellationToken.None);
                root.UpdateLayout();
                var ui = new RenderTargetBitmap(1240, 800, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                ui.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(ui));
                using var file = File.Create(Path.Combine(output, "kifs-controls.png")); encoder.Save(file);
            }
        }
        finally { window.Close(); }
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await renderer.RenderAsync(state, 2048, 2048, null, canceled.Token); throw new Exception("Canceled KIFS export succeeded."); }
            catch (OperationCanceledException) { }
        }
        Console.WriteLine("PASS (kifs): presets, shape parameters, radial mirrors, styles, random forms, surface probe, cache, saves, cloud, WPF controls and cancellation.");
    }
}
