using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyBuddhabrot4DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("buddhabrot4d");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        var kind = Fractal3DKind.Buddhabrot4D;
        Check(FractalCatalog.Create().Single(t => t.LaunchKey == Fractal3DCatalog.LaunchKey(kind)).IsThreeDimensional,
            "Buddhabrot projections must appear in the 3D catalog.");

        // Independent complex arithmetic checks the simultaneous recurrence and first escape.
        foreach (Complex c in new Complex[] { new(1, 0), new(.3, .6), new(-.2, .9), new(-1, 0), Complex.Zero })
        {
            var history = new Vector2[128];
            int actual = Buddhabrot4DOrbitCloud.TraceOrbit(c.Real, c.Imaginary, history, CancellationToken.None);
            Complex z = Complex.Zero; int expected = 0;
            for (int i = 0; i < history.Length; i++)
            {
                z = z * z + c;
                Check(Vector2.Distance(history[i], new((float)z.Real, (float)z.Imaginary)) < 1e-5,
                    "An orbit visit must use the previous real AND imaginary coordinate.");
                if (z.Magnitude > 2) { expected = i + 1; break; }
            }
            Check(actual == expected, "Interior points must be excluded; escape is the first |z| > 2.");
        }
        var settings = new Buddhabrot4DSettings { SampleCount = 8000, MaxIterations = 400, BlueLimit = 400 };
        var cloud = await Task.Run(() => Buddhabrot4DOrbitCloud.Build(settings, CancellationToken.None, 10_000));
        var again = await Task.Run(() => Buddhabrot4DOrbitCloud.Build(settings, CancellationToken.None, 10_000));
        Check(cloud.Points.Span.SequenceEqual(again.Points.Span), "The seed must reproduce the retained 4D sample exactly.");
        Check(cloud.TotalVisits > cloud.Points.Length && cloud.Points.Length == 10_000,
            "Long orbit sets must use the bounded reservoir, not a biased prefix or unbounded memory.");
        Check(cloud.Points.ToArray().All(p => p.EscapeIteration >= settings.MinIterations &&
            p.EscapeIteration <= settings.MaxIterations && p.Position.X * p.Position.X + p.Position.Y * p.Position.Y <= 4.00001f),
            "Only pre-escape visits of selected escaping orbits belong to the cloud.");
        var fewer = await Task.Run(() => Buddhabrot4DOrbitCloud.Build(settings, CancellationToken.None, 2000));
        Check(cloud.TotalVisits == fewer.TotalVisits, "Reservoir capacity must not change the chosen c samples.");
        var different = await Task.Run(() => Buddhabrot4DOrbitCloud.Build(settings with { Seed = 43 }, CancellationToken.None, 10_000));
        Check(!cloud.Points.Span.SequenceEqual(different.Points.Span), "Changing the seed must change the sample.");

        Vector4 point = new(1, 2, 3, 4);
        var quarter = Buddhabrot4DVolume.Rotation(settings with { ZrCr = 90 });
        Check(Vector4.Distance(Vector4.Transform(point, quarter), new(-3, 2, 1, 4)) < 1e-5,
            "A 90-degree mixed rotation must exchange a z coordinate and a c coordinate.");
        var rotatedSettings = settings with { ZrZi = 13, ZrCr = 37, ZrCi = -54, ZiCr = 81, ZiCi = 29, CrCi = -17 };
        var rotation = Buddhabrot4DVolume.Rotation(rotatedSettings);
        Check(Math.Abs(Vector4.Transform(point, rotation).LengthSquared() - point.LengthSquared()) < 1e-4,
            "The six 4D rotations must preserve distance before projection.");
        Check(Buddhabrot4DVolume.Project(point, Matrix4x4.Identity, Buddhabrot4DProjection.HideCi) == new Vector3(1, 2, 3) &&
            Buddhabrot4DVolume.Project(point, Matrix4x4.Identity, Buddhabrot4DProjection.HideZi) == new Vector3(1, 3, 4),
            "Projection must omit the selected coordinate rather than extrude a 2D bitmap.");
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            try { Buddhabrot4DVolume.Build(cloud, settings, cts.Token); throw new Exception("Canceled projection succeeded."); }
            catch (OperationCanceledException) { }
        }
        using (var cts = new CancellationTokenSource(15))
        {
            try { await Task.Run(() => Buddhabrot4DOrbitCloud.Build(settings with { SampleCount = 2_000_000 }, cts.Token)); throw new Exception("Canceled sampling succeeded."); }
            catch (OperationCanceledException) { }
        }
        foreach (var invalid in new[] { settings with { MaxIterations = 1 }, settings with { ZrCr = double.NaN } })
        {
            try { invalid.Validate(); throw new Exception("Invalid settings accepted."); }
            catch (ArgumentException) { }
        }

        using var renderer = new Fractal3DRenderer();
        var presets = Fractal3DCatalog.GetPresets(kind);
        var signatures = new HashSet<string>();
        foreach (var (preset, i) in presets.Select((p, i) => (p, i)))
        {
            var state = preset.Clone();
            state.Buddhabrot.SampleCount = 40_000;
            BitmapSource bitmap = await renderer.RenderAsync(state, 320, 320, null, CancellationToken.None);
            byte[] pixels = Pixels(bitmap);
            Check(HasFractal3DStructure(pixels, 320, 320), $"{preset.SaveName}: empty cloud.");
            signatures.Add(Convert.ToHexString(SHA256.HashData(pixels)));
            if (output is not null) Save(bitmap, Path.Combine(output, $"buddhabrot-{i:D2}.png"));
        }
        Check(signatures.Count == presets.Count, "All six shipped projections must have distinct frames.");
        Check(renderer.BuddhabrotSamplingBuilds == 1, "Changing projection must reuse sampled orbits.");
        var baseState = Fractal3DCatalog.CreateDefaultState(kind); baseState.Buddhabrot.SampleCount = 40_000;
        byte[] basePixels = Pixels(await renderer.RenderAsync(baseState, 160, 160, null, CancellationToken.None));
        foreach (int parameter in Enumerable.Range(0, 5))
        {
            var changed = baseState.Clone();
            switch (parameter)
            {
                case 0: changed.Buddhabrot.Exposure *= 2; break;
                case 1: changed.Buddhabrot.Gamma = 1.2; break;
                case 2: changed.Buddhabrot.Density *= 3; break;
                case 3: changed.Buddhabrot.Saturation = 0; break;
                case 4: changed.CameraYaw += 70; break;
            }
            Check(Fractal3DRenderer.SameVolumeGeometry(baseState, changed), "Tone and camera must reuse the uploaded volume.");
            byte[] changedPixels = Pixels(await renderer.RenderAsync(changed, 160, 160, null, CancellationToken.None));
            Check(!basePixels.SequenceEqual(changedPixels),
                "Every tone control and camera orbit must visibly change the frame.");
        }
        var channels = baseState.Clone(); channels.Buddhabrot.RedLimit = 2000; channels.Buddhabrot.BlueLimit = 50;
        byte[] channelPixels = Pixels(await renderer.RenderAsync(channels, 160, 160, null, CancellationToken.None));
        Check(!basePixels.SequenceEqual(channelPixels),
            "Escape thresholds must affect the color channels.");
        Check(renderer.BuddhabrotSamplingBuilds == 1, "Recoloring must reuse the same 4D sample.");
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var styled = baseState.Clone(); styled.ShadingStyle = style;
            BitmapSource bitmap = await renderer.RenderAsync(styled, 160, 160, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap), 160, 160), $"Buddhabrot {style}: empty style.");
            if (output is not null) Save(bitmap, Path.Combine(output, $"style-{(int)style}.png"));
        }
        bool hit = false;
        for (int y = 25; y < 140 && !hit; y += 20)
        for (int x = 25; x < 140 && !hit; x += 20)
            hit = await renderer.ProbeDistanceAsync(baseState, x, y, 160, 160, CancellationToken.None) is > 0;
        Check(hit, "The shared surface probe must find cloud density.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("buddhabrot4d-pixel")), "The shader must use the shared disk cache.");
        await renderer.RenderAsync(Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Ifs3D), 80, 80, null, CancellationToken.None);
        byte[] switchedPixels = Pixels(await renderer.RenderAsync(baseState, 160, 160, null, CancellationToken.None));
        Check(basePixels.SequenceEqual(switchedPixels),
            "Switching scalar and colored textures must restore exactly the same frame.");

        var clone = baseState.Clone(); clone.Buddhabrot.ZrCi = 15;
        Check(baseState.Buddhabrot.ZrCi == 0, "Nested projection settings must clone independently.");
        var options = JsonOptionsFactory.Create();
        var restored = JsonSerializer.Deserialize<Fractal3DState>(JsonSerializer.Serialize(clone, options), options)!;
        Check(clone.Buddhabrot == restored.Buddhabrot, "All projection and tone settings must survive JSON.");
        clone.SaveName = "4D roundtrip";
        var store = new Fractal3DSaveStore(kind); store.Save(clone);
        Check(store.Load().Single().Buddhabrot == clone.Buddhabrot, "The real save store must preserve the projection.");
        var local = CloudSaveRepository.ListLocal(out int unreadable).Single(s => s.Name == clone.SaveName);
        Check(unreadable == 0 && local.Category == Fractal3DCatalog.GetDefinition(kind).SaveCategory,
            "The new mode must participate in the existing cloud repository.");

        var theme = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        var window = new Fractal3DWindow(kind);
        try
        {
            window.LoadState(clone);
            var captured = window.CaptureState("UI");
            Check(captured.Buddhabrot == clone.Buddhabrot, "The WPF editor must round-trip the complete settings.");
            var editor = (Buddhabrot4DEditor)window.FindName("BuddhabrotEditor");
            Check(editor.Visibility == Visibility.Visible && ((FrameworkElement)window.FindName("BailoutPanel")).Visibility == Visibility.Collapsed &&
                ((FrameworkElement)window.FindName("IterationsBox")).Visibility == Visibility.Collapsed,
                "Only the Buddhabrot shape parameters should be visible.");
            ((Slider)editor.FindName("ZrCrSlider")).Value = 55;
            Check(window.CaptureState("changed").Buddhabrot.ZrCr == 55, "The actual 4D slider must feed the renderer state.");
            if (output is not null)
            {
                window.LoadState(Fractal3DCatalog.CreateDefaultState(kind));
                window.CanvasImage.Source = await renderer.RenderAsync(Fractal3DCatalog.CreateDefaultState(kind), 880, 720, null, CancellationToken.None);
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(1240, 800)); root.Arrange(new Rect(0, 0, 1240, 800)); root.UpdateLayout();
                var ui = new RenderTargetBitmap(1240, 800, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); ui.Render(root);
                Save(ui, Path.Combine(output, "window.png"));
                root.Measure(new Size(960, 600)); root.Arrange(new Rect(0, 0, 960, 600)); root.UpdateLayout();
                var compact = new RenderTargetBitmap(960, 600, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); compact.Render(root);
                Save(compact, Path.Combine(output, "window-compact.png"));
            }
        }
        finally { window.Close(); }
        if (output is not null)
        {
            var front = Fractal3DCatalog.CreateDefaultState(kind);
            Save(await renderer.RenderAsync(front, 640, 640, null, CancellationToken.None), Path.Combine(output, "default-front.png"));
        }
        Console.WriteLine("PASS (buddhabrot4d): independent orbit rule, reservoir, 4D rotations, six GPU projections, nine styles, sample reuse, tone, camera, probe, cache, saves, cloud and WPF controls.");

        static byte[] Pixels(BitmapSource bitmap)
        {
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
        }
        static void Save(BitmapSource bitmap, string path)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path); encoder.Save(file);
        }
    }
}
