using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyTerrainAsync()
    {
        using var sandbox = DataSandbox.Create("terrain");
        var settings = new TerrainSettings { Resolution = 257 };
        float[] heights = TerrainHeightField.Build(settings, CancellationToken.None);
        Check(heights.SequenceEqual(TerrainHeightField.Build(settings, CancellationToken.None)), "Terrain must be deterministic.");
        Check(heights.All(h => float.IsFinite(h) && h >= 0 && h <= (float)settings.Height), "Height bounds.");
        VerifyTerrainExtensions(settings, heights);
        foreach (TerrainSettings changed in new[]
        {
            settings with { Seed = 321 }, settings with { Roughness = 0.8 },
            settings with { Scale = 6 }, settings with { Lacunarity = 2.5 },
            settings with { Octaves = 2 }, settings with { Type = TerrainKind.Fbm },
            settings with { Type = TerrainKind.Hybrid }
        }) Check(!heights.SequenceEqual(TerrainHeightField.Build(changed, CancellationToken.None)), "Geometry parameter had no effect.");
        float[] taller = TerrainHeightField.Build(settings with { Height = settings.Height * 2 }, CancellationToken.None);
        Check(heights.Zip(taller).All(p => Math.Abs(p.First * 2 - p.Second) < 1e-6), "Height must scale without renormalizing.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            bool stopped = false;
            try { TerrainHeightField.Build(settings, cancelled.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "Height field cancellation.");
        }

        string output = Path.Combine(AppContext.BaseDirectory, "VerificationData", "Terrain");
        Directory.CreateDirectory(output);
        var png = TerrainHeightMapExport.Create(settings, CancellationToken.None);
        string path = Path.Combine(output, "heightmap.png");
        TerrainHeightMapExport.Save(path, png);
        using (var stream = File.OpenRead(path))
        {
            var decoded = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            Check(decoded.Format == PixelFormats.Gray16 && decoded.PixelWidth == 257, "PNG must preserve 16-bit grayscale.");
            var pixels = new ushort[heights.Length];
            decoded.CopyPixels(pixels, 257 * 2, 0);
            for (int i = 0; i < pixels.Length; i++)
                Check(Math.Abs(pixels[i] / 65535.0 * settings.Height - heights[i]) <= settings.Height / 65535,
                    "PNG height mismatch or transposed axes.");
        }

        var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Terrain);
        state.Terrain = settings;
        using var renderer = new Fractal3DRenderer();
        var large = state.Clone();
        large.Terrain = settings with { Resolution = 2049, Roughness = 0.95, Octaves = 12, Scale = 16 };
        Check(HasFractal3DStructure(await Fractal3DFrameAsync(renderer, large)), "Largest terrain grid failed.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var dropped = await renderer.RenderPixelsAsync(large, 120, 90, null, null, cancelled.Token);
            Check(!dropped.Completed, "Cancelled terrain frame must not be reported as complete.");
        }
        int index = 0;
        foreach (var preset in Fractal3DCatalog.GetPresets(Fractal3DKind.Terrain))
        {
            var bitmap = await renderer.RenderAsync(preset, 480, 360, null, CancellationToken.None);
            TerrainHeightMapExport.Save(Path.Combine(output, $"preset-{index++}.png"), bitmap);
            byte[] pixels = new byte[480 * 360 * 4];
            bitmap.CopyPixels(pixels, 480 * 4, 0);
            Check(HasFractal3DStructure(pixels, 480, 360), "Empty terrain preview.");
        }
        var eroded = state.Clone();
        eroded.Terrain = settings with { Shape = TerrainShape.Crater, ShapeStrength = .83,
            ShapeRadius = .63, ThermalIterations = 20, TalusAngle = 23.5 };
        var erodedHeights = TerrainHeightField.Build(eroded.Terrain, CancellationToken.None);
        eroded.CameraPitch = 90; eroded.CameraYaw = 0; eroded.CameraDistance = 10;
        eroded.TargetX = eroded.TargetY = eroded.TargetZ = 0;
        double erodedDistance = await renderer.ProbeDistanceAsync(eroded, 240, 180, 480, 360, CancellationToken.None);
        Check(Math.Abs(erodedDistance - (10 - erodedHeights[128 * 257 + 128])) < .002,
            "GPU probe must use the masked, eroded height field.");
        var heightPixels = await Fractal3DFrameAsync(renderer, eroded);
        eroded.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        var slopePixels = await Fractal3DFrameAsync(renderer, eroded);
        Check(!heightPixels.SequenceEqual(slopePixels), "Slope coloring must differ from height coloring.");
        await VerifyTerrainSlopeAsync(renderer, settings, heights);
        var erodedPng = TerrainHeightMapExport.Create(eroded.Terrain, CancellationToken.None);
        var erodedPixels = new ushort[erodedHeights.Length];
        erodedPng.CopyPixels(erodedPixels, 257 * 2, 0);
        Check(erodedPixels.Zip(erodedHeights).All(p =>
            Math.Abs(p.First / 65535.0 * eroded.Terrain.Height - p.Second) <= eroded.Terrain.Height / 65535),
            "Gray16 must export the masked and eroded field, not the original noise.");

        // Vertical central ray at exact grid nodes, including corners and outside the patch.
        state.CameraPitch = 90;
        state.CameraYaw = 0;
        state.CameraDistance = 10;
        state.TargetY = 0;
        foreach ((int x, int z) in new[] { (0, 0), (64, 123), (128, 128), (256, 256) })
        {
            state.TargetX = settings.Size * ((double)x / 256 - 0.5);
            state.TargetZ = settings.Size * ((double)z / 256 - 0.5);
            double distance = await renderer.ProbeDistanceAsync(state, 240, 180, 480, 360, CancellationToken.None);
            Check(double.IsFinite(distance) && Math.Abs(distance - (10 - heights[z * 257 + x])) < 0.002,
                $"Terrain probe mismatch at {x},{z}: {distance}.");
        }
        state.TargetX = settings.Size;
        Check(double.IsNaN(await renderer.ProbeDistanceAsync(state, 240, 180, 480, 360, CancellationToken.None)), "Terrain extends outside patch.");

        var oblique = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Terrain);
        oblique.Terrain = settings;
        foreach (int pixelX in new[] { 1, 180, 240, 310, 479 })
        {
            Vector3 origin = Fractal3DCamera.Build(oblique).Position;
            Vector3 ray = Fractal3DCamera.PixelRay(oblique, pixelX, 180, 480, 360);
            double expected = BruteTerrainRay(settings, heights, origin, ray);
            double actual = await renderer.ProbeDistanceAsync(oblique, pixelX, 180, 480, 360, CancellationToken.None);
            Check(double.IsNaN(expected) ? double.IsNaN(actual) : Math.Abs(expected - actual) < 0.003,
                $"DDA disagrees with brute force triangle intersections: {expected}, {actual}.");
        }

        state.SaveName = "terrain-roundtrip";
        var store = new Fractal3DSaveStore(Fractal3DKind.Terrain);
        store.Save(state);
        var loaded = store.Load().Single(s => s.SaveName == state.SaveName);
        Check(loaded.Terrain == state.Terrain && loaded.TargetX == state.TargetX, "Terrain save roundtrip.");
        var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == themeStyles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });
        var window = new Fractal3DWindow(Fractal3DKind.Terrain);
        try
        {
            window.LoadState(loaded);
            Check(window.CaptureState("test").Terrain == settings, "Terrain controls roundtrip.");
            eroded.SaveName = "terrain-erosion-roundtrip";
            store.Save(eroded);
            var restored = store.Load().Single(s => s.SaveName == eroded.SaveName);
            Check(restored.Terrain == eroded.Terrain && restored.ColoringMode == eroded.ColoringMode,
                "New terrain fields and slope coloring must survive save/load.");
            var local = CloudSaveRepository.ListLocal(out int unreadable).Single(s => s.Name == eroded.SaveName);
            var decoded = CloudSaveRepository.Decode(new CloudSave(Guid.NewGuid(), local.Name, 1,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, local.JsonData));
            Check(unreadable == 0 && decoded.Category == "Fractal3DTerrain" &&
                decoded.State["Terrain"]!.Deserialize<TerrainSettings>() == eroded.Terrain,
                "Cloud payload must preserve the new terrain parameters.");
            window.LoadState(restored);
            Check(window.CaptureState("extensions").Terrain == eroded.Terrain, "Extended terrain controls roundtrip.");
            var editor = (TerrainEditor)window.FindName("TerrainPanel");
            var erosionBox = (TextBox)editor.FindName("ErosionBox");
            erosionBox.Text = "1000";
            NumericInput.CommitAll(window);
            Check(window.CaptureState("clamped").Terrain.ThermalIterations == 64 && erosionBox.Text == "64",
                "Parent commands must commit/clamp the nested erosion field.");
            var talusBox = (TextBox)editor.FindName("TalusBox");
            talusBox.SetCurrentValue(TextBox.TextProperty, "NaN"); NumericInput.CommitAll(window);
            Check(window.CaptureState("restored").Terrain.TalusAngle == eroded.Terrain.TalusAngle &&
                NumericInput.GetHasNotice(talusBox), "Invalid talus must restore the loaded angle with an explanation.");
            editor.Load(settings);
            talusBox.SetCurrentValue(TextBox.TextProperty, "draft");
            ((TextBox)editor.FindName("RadiusBox")).SetCurrentValue(TextBox.TextProperty, "draft");
            Check(window.CaptureState("inactive").Terrain == settings, "Inactive shape/erosion drafts must not block capture.");
            var craterPreset = Fractal3DCatalog.GetPresets(Fractal3DKind.Terrain)[5];
            window.LoadState(craterPreset);
            Check(window.CaptureState("crater-preset").Terrain == craterPreset.Terrain,
                "Selecting a crater preset must restore all terrain fields together.");
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(1240, 900)); root.Arrange(new Rect(0, 0, 1240, 900)); root.UpdateLayout();
            var render = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)render.Invoke(window, [Enum.Parse(render.GetParameters()[0].ParameterType, "Full")])!;
            Check(((Image)window.FindName("CanvasImage")).Source is BitmapSource &&
                ((Grid)window.FindName("InitialPreparationLayer")).Visibility == Visibility.Collapsed,
                "The first terrain frame must dismiss preparation.");
            root.UpdateLayout();
            var controls = new RenderTargetBitmap(1240, 900, 96, 96, PixelFormats.Pbgra32);
            controls.Render(root); TerrainHeightMapExport.Save(Path.Combine(output, "terrain-controls.png"), controls);
        }
        finally { window.Close(); }
        Console.WriteLine($"PASS (terrain): deterministic noise, masks, conservative thermal erosion, slope, controls, PNG Gray16, GPU presets/probe, saves, preparation and cancellation. Images: {output}");
    }

    private static async Task VerifyTerrainSlopeAsync(Fractal3DRenderer renderer, TerrainSettings settings, float[] heights)
    {
        // An exact point inside the lower triangle, away from grid edges. Flat white light
        // and a black/white linear palette isolate albedo, leaving only the known fog factor.
        var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Terrain);
        state.Terrain = settings;
        state.CameraYaw = 0; state.CameraPitch = 90; state.CameraDistance = 10;
        state.TargetX = settings.Size * (64.25 / 256 - .5);
        state.TargetY = 0; state.TargetZ = settings.Size * (123.25 / 256 - .5);
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.Palette = new() { Name = "slope-test", Colors = [Colors.Black, Colors.White] };
        state.LightColor = Colors.Black; state.Ambient = 1; state.Specular = 0;
        state.BackgroundTop = state.BackgroundBottom = Colors.Black;
        state.SoftShadows = state.AmbientOcclusion = false;
        state.MaxDistance = 1000;
        double spacing = settings.Size / 256;
        int cell = 123 * 257 + 64;
        double dx = heights[cell + 1] - heights[cell], dz = heights[cell + 257] - heights[cell];
        double slope = Math.Atan(Math.Sqrt(dx * dx + dz * dz) / spacing) * 2 / Math.PI;
        double distance = 10 - (heights[cell] + .25 * (dx + dz));
        double linear = slope * (1 - distance / state.MaxDistance);
        double srgb = linear <= .0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - .055;
        var bitmap = await renderer.RenderAsync(state, 1, 1, null, CancellationToken.None);
        var pixel = new byte[4]; bitmap.CopyPixels(pixel, 4, 0);
        Check(Math.Abs(pixel[0] / 255.0 - srgb) < .012 && pixel[0] == pixel[1] && pixel[1] == pixel[2],
            $"GPU slope coloring disagrees with triangle gradient: {pixel[0] / 255.0}, expected {srgb}.");
    }

    private static void VerifyTerrainExtensions(TerrainSettings settings, float[] original)
    {
        var legacy = JsonSerializer.Deserialize<TerrainSettings>("{\"Resolution\":257}")!;
        Check(legacy.Shape == TerrainShape.Patch && legacy.ThermalIterations == 0 &&
            original.SequenceEqual(TerrainHeightField.Build(legacy, CancellationToken.None)), "Legacy terrain geometry must remain unchanged.");
        foreach (var shape in new[] { TerrainShape.Island, TerrainShape.Crater })
        {
            var maskedSettings = settings with { Shape = shape };
            float[] masked = TerrainHeightField.Build(maskedSettings, CancellationToken.None);
            Check(!masked.SequenceEqual(original) && masked.All(h => h >= 0 && h <= settings.Height), "Shape must change geometry within height bounds.");
            Check(original.SequenceEqual(TerrainHeightField.Build(maskedSettings with { ShapeStrength = 0 }, CancellationToken.None)),
                "Zero shape strength must preserve the original height field exactly.");
            Check(!masked.SequenceEqual(TerrainHeightField.Build(maskedSettings with { ShapeRadius = .45 }, CancellationToken.None)),
                "Shape radius must affect geometry.");
            if (shape == TerrainShape.Island)
                Check(Enumerable.Range(0, 257).All(i => masked[i] == 0 && masked[256 * 257 + i] == 0 &&
                    masked[i * 257] == 0 && masked[i * 257 + 256] == 0), "Island border must be at zero before erosion.");
            else
                Check(masked[128 * 257 + 128] < masked[128 * 257 + 230], "Crater must have a low floor and raised rim.");
        }

        var erode = typeof(TerrainHeightField).GetMethod("ApplyThermalErosion", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<float[], int, double, int, double, CancellationToken, float[]>>();
        float[] spike = [0, 0, 0, 0, 1, 0, 0, 0, 0];
        double angle = Math.Atan(.1) * 180 / Math.PI;
        float[] oneStep = erode(spike, 3, 1, 1, angle, CancellationToken.None);
        double axial = .9 / 16, diagonal = (1 - Math.Sqrt(2) * .1) / 16;
        Check(Math.Abs(oneStep[4] - (1 - 4 * axial - 4 * diagonal)) < 1e-7 &&
            Math.Abs(oneStep[1] - axial) < 1e-7 && Math.Abs(oneStep[0] - diagonal) < 1e-7 && spike[4] == 1,
            "One thermal step disagrees with independent transfers or mutated its input.");
        float[] ramp = Enumerable.Range(0, 81).Select(i => (float)((i % 9) * .01)).ToArray();
        Check(erode(ramp, 9, 1, 64, 30, CancellationToken.None).SequenceEqual(ramp),
            "Slopes below the talus angle must not move.");
        var erodedSettings = settings with { ThermalIterations = 32, TalusAngle = 20 };
        float[] eroded = TerrainHeightField.Build(erodedSettings, CancellationToken.None);
        Check(!eroded.SequenceEqual(original) && eroded.SequenceEqual(TerrainHeightField.Build(erodedSettings, CancellationToken.None)),
            "Thermal erosion must change rough terrain reproducibly.");
        double mass = original.Sum(h => (double)h);
        Check(Math.Abs(eroded.Sum(h => (double)h) - mass) < mass * 1e-6 && eroded.Min() >= original.Min() && eroded.Max() <= original.Max(),
            "Closed thermal transfers must conserve material and avoid overshoot.");
        Check(TerrainVariation(eroded, 257) < TerrainVariation(original, 257), "Thermal erosion must reduce steep variation.");
        Check(!eroded.SequenceEqual(TerrainHeightField.Build(erodedSettings with { TalusAngle = 50 }, CancellationToken.None)),
            "Talus angle must affect erosion.");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        bool stopped = false;
        try { erode(spike, 3, 1, 64, 30, cancel.Token); } catch (OperationCanceledException) { stopped = true; }
        Check(stopped, "Thermal erosion must honor cancellation.");
        foreach (var invalid in new[] { settings with { Shape = (TerrainShape)99 }, settings with { ShapeRadius = double.NaN },
            settings with { ShapeStrength = 2 }, settings with { ThermalIterations = 65 }, settings with { TalusAngle = double.PositiveInfinity } })
        {
            bool rejected = false;
            try { invalid.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid terrain extension was accepted.");
        }
    }

    private static double TerrainVariation(float[] heights, int side)
    {
        double result = 0;
        for (int z = 0; z < side; z++)
        for (int x = 0; x < side; x++)
        {
            double h = heights[z * side + x];
            if (x > 0) result += Math.Pow(h - heights[z * side + x - 1], 2);
            if (z > 0) result += Math.Pow(h - heights[(z - 1) * side + x], 2);
        }
        return result;
    }

    private static double BruteTerrainRay(TerrainSettings settings, float[] heights, Vector3 origin, Vector3 ray)
    {
        double nearest = double.PositiveInfinity;
        Vector3 Vertex(int x, int z) => new((float)(settings.Size * ((double)x / (settings.Resolution - 1) - 0.5)),
            heights[z * settings.Resolution + x], (float)(settings.Size * ((double)z / (settings.Resolution - 1) - 0.5)));
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(ray, e2);
            float determinant = Vector3.Dot(e1, p);
            if (Math.Abs(determinant) < 1e-10) return;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) / determinant;
            if (u < 0 || u > 1) return;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(ray, q) / determinant;
            if (v < 0 || u + v > 1) return;
            float t = Vector3.Dot(e2, q) / determinant;
            if (t >= 0) nearest = Math.Min(nearest, t);
        }
        for (int z = 0; z < settings.Resolution - 1; z++)
        for (int x = 0; x < settings.Resolution - 1; x++)
        {
            Triangle(Vertex(x, z), Vertex(x + 1, z), Vertex(x, z + 1));
            Triangle(Vertex(x + 1, z + 1), Vertex(x, z + 1), Vertex(x + 1, z));
        }
        return double.IsPositiveInfinity(nearest) ? double.NaN : nearest;
    }
}
