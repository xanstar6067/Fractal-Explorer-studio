using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
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
        }
        finally { window.Close(); }
        Console.WriteLine($"PASS (terrain): deterministic geometry, controls, PNG Gray16, GPU presets, probe, saves, cancellation. Images: {output}");
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
