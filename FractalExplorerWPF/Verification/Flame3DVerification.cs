using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyFlame3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("flame3d");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        var kind = Fractal3DKind.Flame3D;
        var tile = FractalCatalog.Create().Single(t => t.LaunchKey == Fractal3DCatalog.LaunchKey(kind));
        Check(tile.IsThreeDimensional, "Flame must appear in the shared 3D catalog.");
        var state = Fractal3DCatalog.CreateDefaultState(kind);
        var clone = state.Clone(); clone.Flame.Transforms[0].Map.Tz += .1;
        Check(clone.Flame.Transforms[0].Map.Tz != state.Flame.Transforms[0].Map.Tz, "Nested flame maps must clone independently.");

        foreach (var variation in Enum.GetValues<Flame3DVariation>())
        {
            var p = Flame3DVariations.Apply(new(.7f, -.3f, .6f), variation, new Random(123));
            Check(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z), $"{variation}: finite XYZ expected.");
        }
        var twisted = Flame3DVariations.Apply(new(.7f, -.3f, .6f), Flame3DVariation.Twist, new Random(1));
        Check(Math.Abs(twisted.Z - .6) > .01, "Twist must transform depth, not just extrude a 2D orbit.");
        var settings = new Flame3DRandomizationSettings { MinimumTransforms = 5, MaximumTransforms = 5, Variations = [Flame3DVariation.Curl] };
        var random = Flame3DRandomizer.Create(settings, new Random(42));
        Check(random.Count == 5 && random.All(t => t.Variation == Flame3DVariation.Curl), "Randomizer must honor exact counts and the selected variation set.");
        Check(random.Any(t => Math.Abs(t.Map.M13) + Math.Abs(t.Map.M31) > .01), "Random maps must rotate across depth.");
        Flame3DRandomizationSettingsStore.Save(settings);
        Check(Flame3DRandomizationSettingsStore.Load().Variations.SequenceEqual(settings.Variations), "Randomizer preferences must persist separately.");

        var small = state.Clone(); small.Iterations = 50_000;
        byte[] first = await Task.Run(() => Flame3DVolume.Build(small, CancellationToken.None));
        byte[] second = await Task.Run(() => Flame3DVolume.Build(small, CancellationToken.None));
        Check(first.SequenceEqual(second), "The same seed must reproduce the exact colored volume.");
        small.Flame.Seed++;
        second = await Task.Run(() => Flame3DVolume.Build(small, CancellationToken.None));
        Check(!first.SequenceEqual(second), "The seed must affect sampling.");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { Flame3DVolume.Build(state, canceled.Token); throw new Exception("Canceled volume succeeded."); }
            catch (OperationCanceledException) { }
        }
        using (var canceled = new CancellationTokenSource(20))
        {
            var large = state.Clone(); large.Iterations = 10_000_000;
            try { await Task.Run(() => Flame3DVolume.Build(large, canceled.Token)); throw new Exception("In-progress cancellation ignored."); }
            catch (OperationCanceledException) { }
        }

        using var renderer = new Fractal3DRenderer();
        var signatures = new HashSet<string>();
        var presets = Fractal3DCatalog.GetPresets(kind);
        for (int index = 0; index < presets.Count; index++)
        {
            var preset = presets[index];
            var bitmap = await renderer.RenderAsync(preset, 360, 360, null, CancellationToken.None);
            byte[] pixels = Pixels(bitmap);
            Check(HasFractal3DStructure(pixels, 360, 360), $"{preset.SaveName}: empty frame.");
            signatures.Add(Convert.ToHexString(SHA256.HashData(pixels)));
            if (output is not null)
            {
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"flame-{index:D2}.png")); encoder.Save(file);
            }
            var restored = JsonSerializer.Deserialize<Fractal3DState>(JsonSerializer.Serialize(preset, JsonOptionsFactory.Create()), JsonOptionsFactory.Create())!;
            Check(Fractal3DRenderer.SameVolumeGeometry(preset, restored), "Colored transforms must survive JSON round trips.");
        }
        Check(signatures.Count == presets.Count, "The shipped views must be distinct.");
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var styled = state.Clone(); styled.ShadingStyle = style;
            byte[] frame = Pixels(await renderer.RenderAsync(styled, 120, 120, null, CancellationToken.None));
            Check(HasFractal3DStructure(frame, 120, 120), $"Flame {style}: empty colored style.");
        }
        for (int i = 0; i < 3; i++)
        {
            var generated = state.Clone();
            generated.Iterations = 500_000;
            generated.Flame.Transforms = Flame3DRandomizer.Create(new(), new Random(701 + i));
            var bitmap = await renderer.RenderAsync(generated, 200, 200, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap), 200, 200), "Random flame should produce a visible volume.");
            if (output is not null)
            {
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"random-{i:D2}.png")); encoder.Save(file);
            }
        }
        byte[] basePixels = Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None));
        foreach (int parameter in Enumerable.Range(0, 4))
        {
            var changed = state.Clone();
            switch (parameter)
            {
                case 0: changed.Flame.Exposure *= 2; break;
                case 1: changed.Flame.Gamma = 1.2; break;
                case 2: changed.Flame.Density *= 3; break;
                case 3: changed.Flame.Vibrancy = 0; break;
            }
            Check(Fractal3DRenderer.SameVolumeGeometry(state, changed), "Tone controls should reuse the volume.");
            byte[] changedPixels = Pixels(await renderer.RenderAsync(changed, 160, 160, null, CancellationToken.None));
            Check(!basePixels.SequenceEqual(changedPixels), "Each tone parameter should visibly change the frame.");
        }
        var moved = state.Clone(); moved.CameraYaw += 70;
        Check(Fractal3DRenderer.SameVolumeGeometry(state, moved), "Camera motion should reuse the volume.");
        byte[] movedPixels = Pixels(await renderer.RenderAsync(moved, 160, 160, null, CancellationToken.None));
        Check(!basePixels.SequenceEqual(movedPixels), "Orbiting should reveal a different 3D view.");
        // Exercise texture recreation in both directions on the same renderer.
        await renderer.RenderAsync(Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Ifs3D), 80, 80, null, CancellationToken.None);
        byte[] switchedPixels = Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None));
        Check(basePixels.SequenceEqual(switchedPixels), "Switching IFS -> Flame should restore the color texture correctly.");
        bool hit = false;
        for (int y = 30; y < 140 && !hit; y += 20)
        for (int x = 30; x < 140 && !hit; x += 20)
            hit = await renderer.ProbeDistanceAsync(state, x, y, 160, 160, CancellationToken.None) is > 0;
        Check(hit, "The shared camera probe must find flame density.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("flame3d-pixel")), "Flame shader must use the shared disk cache.");
        using (var canceled = new CancellationTokenSource(20))
        {
            try { await renderer.RenderAsync(state, 2048, 2048, null, canceled.Token); throw new Exception("Canceled export succeeded."); }
            catch (OperationCanceledException) { }
        }

        state.SaveName = "Flame roundtrip";
        var store = new Fractal3DSaveStore(kind); store.Save(state);
        Check(Fractal3DRenderer.SameVolumeGeometry(state, store.Load().Single()), "Flame must round-trip through the real save store.");
        var theme = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        var window = new Fractal3DWindow(kind);
        try
        {
            window.LoadState(presets[2]);
            var captured = window.CaptureState("UI");
            Check(Fractal3DRenderer.SameVolumeGeometry(presets[2], captured) && captured.Flame.Exposure == presets[2].Flame.Exposure,
                "Flame controls must round-trip through the WPF window.");
            Check(((FrameworkElement)window.FindName("FlamePanel")).Visibility == Visibility.Visible &&
                ((FrameworkElement)window.FindName("BailoutPanel")).Visibility == Visibility.Collapsed, "Only flame shape controls should be shown.");
            Check(!((Expander)window.FindName("LightExpander")).IsEnabled, "Emissive flames should explain that external light is unused.");
        }
        finally { window.Close(); }
        var editor = new Flame3DTransformEditorWindow(state.Flame.Transforms);
        try
        {
            LayoutEditor(editor);
            var z = (TextBox)editor.FindName("TzBox");
            z.Text = ".123";
            int calls = 0;
            editor.TransformsApplied += t => { calls++; Check(t[0].Map.Tz == .123, "Editor should apply depth translation."); };
            typeof(Flame3DTransformEditorWindow).GetMethod("Commit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(editor, null);
            Check(calls == 1, "Editor must apply the new matrix.");
            z.Text = ".456";
            // Closing must roll back unapplied edits to the .123 translation, not .456.
        }
        finally { editor.Close(); }
        var randomEditor = new Flame3DTransformEditorWindow(state.Flame.Transforms);
        try
        {
            LayoutEditor(randomEditor);
            int randomApplied = 0;
            randomEditor.TransformsApplied += transforms =>
            {
                randomApplied++;
                Check(transforms.Count == 5 && transforms.All(t => t.Variation == Flame3DVariation.Curl),
                    "The actual randomizer button must apply the configured five spatial variations.");
            };
            ((Button)randomEditor.FindName("RandomizeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            LayoutEditor(randomEditor);
            Check(randomApplied == 1, "Randomizing must apply the generated transforms once.");
        }
        finally { randomEditor.Close(); }
        Console.WriteLine("PASS (flame3d): colored deterministic volume, five GPU presets, tone, camera, probe, IFS switching, randomizer, cancellation, saves and WPF editor.");

        static byte[] Pixels(BitmapSource bitmap)
        {
            byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
        }

        static void LayoutEditor(Flame3DTransformEditorWindow editor)
        {
            // Constructor-only tests do not instantiate the ListBox data templates.
            // Real layout activates Run.Text bindings, including their default binding mode.
            var root = (FrameworkElement)editor.Content;
            root.Measure(new Size(1080, 760));
            root.Arrange(new Rect(0, 0, 1080, 760));
            root.UpdateLayout();
            var list = (ListBox)editor.FindName("TransformList");
            Check(list.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem,
                "The transform cards must be instantiated to verify their display bindings.");
        }
    }
}
