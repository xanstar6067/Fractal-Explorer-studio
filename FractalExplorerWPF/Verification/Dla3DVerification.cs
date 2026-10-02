using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyDla3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("dla3d");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        var kind = Fractal3DKind.Dla3D;
        var state = Fractal3DCatalog.CreateDefaultState(kind);
        Check(FractalCatalog.Create().Single(t => t.LaunchKey == Fractal3DCatalog.LaunchKey(kind)).IsThreeDimensional,
            "DLA must belong to the shared 3D catalog.");
        var cloned = state.Clone(); cloned.Dla.Stickiness = .1;
        Check(state.Dla.Stickiness == 1, "DLA settings must clone independently.");
        var settings = new Dla3DSettings { ParticleCount = 800 };
        var whole = new Dla3DCluster(settings);
        var batched = new Dla3DCluster(settings);
        await Task.Run(() =>
        {
            whole.GrowTo(800, CancellationToken.None);
            for (int i = 50; i <= 800; i += 50) batched.GrowTo(i, CancellationToken.None);
        });
        Check(whole.Points.SequenceEqual(batched.Points), "Batching must preserve the exact cluster.");
        var occupied = new HashSet<Vector3>();
        Vector3[] neighbors = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        foreach (Vector3 p in whole.Points)
        {
            Check(occupied.Count == 0 || neighbors.Any(d => occupied.Contains(p + d)), "Every grown particle must touch the existing cluster.");
            Check(occupied.Add(p), "A particle must never overlap an occupied cell.");
        }
        Check(whole.Points.Max(p => p.Z) - whole.Points.Min(p => p.Z) > 10 &&
              whole.Points.Max(p => p.Y) - whole.Points.Min(p => p.Y) > 10, "DLA must grow in three dimensions.");
        using (var cts = new CancellationTokenSource(20))
        {
            try { await Task.Run(() => batched.GrowTo(20_000, cts.Token)); throw new Exception("DLA cancellation ignored."); }
            catch (OperationCanceledException) { }
        }
        int afterCancel = batched.Count;
        await Task.Run(() => { whole.GrowTo(afterCancel + 100, CancellationToken.None); batched.GrowTo(afterCancel + 100, CancellationToken.None); });
        Check(whole.Points.SequenceEqual(batched.Points), "Cancellation and continuation must keep the same random streams.");
        foreach (var variant in new[]
        {
            settings with { Seed = 73 }, settings with { Stickiness = .1 },
            settings with { FlowStrength = .3, FlowYaw = 90, FlowPitch = 0 }
        })
        {
            var cluster = new Dla3DCluster(variant);
            await Task.Run(() => cluster.GrowTo(800, CancellationToken.None));
            Check(!cluster.Points.Take(801).SequenceEqual(whole.Points.Take(801)), "Seed, sticking and flow must affect growth.");
        }
        using var renderer = new Fractal3DRenderer();
        var signatures = new HashSet<string>();
        var presets = Fractal3DCatalog.GetPresets(kind);
        for (int i = 0; i < presets.Count; i++)
        {
            var watch = Stopwatch.StartNew();
            BitmapSource image = await renderer.RenderAsync(presets[i], 400, 400, null, CancellationToken.None);
            Check(renderer.DlaParticleCount == presets[i].Dla.ParticleCount, "Preset growth should reach its target.");
            byte[] pixels = Pixels(image);
            if (output is not null) WriteDlaPng(image, Path.Combine(output, $"dla-{i:D2}.png"));
            Check(HasFractal3DStructure(pixels, 400, 400), $"{presets[i].SaveName}: empty volume frame.");
            signatures.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
            Console.WriteLine($"DLA {presets[i].SaveName}: {renderer.DlaParticleCount} particles, {watch.Elapsed.TotalSeconds:F2}s");
        }
        Check(signatures.Count == presets.Count, "DLA presets must render distinctly.");
        byte[] ageFrame = Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None));
        var clusterField = typeof(Fractal3DRenderer).GetField("_dlaCluster", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? cached = clusterField.GetValue(renderer);
        var recolored = state.Clone(); recolored.ColoringMode = Fractal3DColoringMode.Material;
        byte[] materialFrame = Pixels(await renderer.RenderAsync(recolored, 160, 160, null, CancellationToken.None));
        Check(!ageFrame.SequenceEqual(materialFrame),
            "Age and material colors must differ.");
        var rotated = state.Clone(); rotated.CameraYaw += 45;
        await renderer.RenderAsync(rotated, 120, 120, null, CancellationToken.None);
        Check(ReferenceEquals(cached, clusterField.GetValue(renderer)), "Camera and recoloring must reuse the simulation.");
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var styled = state.Clone(); styled.ShadingStyle = style;
            Check(HasFractal3DStructure(Pixels(await renderer.RenderAsync(styled, 160, 160, null, CancellationToken.None)), 160, 160),
                $"DLA {style}: empty frame.");
        }
        Check(File.Exists(AppPaths.GetShaderCacheFile("dla3d-pixel")), "DLA shader must use the common disk cache.");
        double hit = await renderer.ProbeDistanceAsync(state, 80, 80, 160, 160, CancellationToken.None);
        Check(double.IsFinite(hit) && hit > 0, "DLA's seed should be navigable with the surface probe.");
        using (var cts = new CancellationTokenSource(10))
        {
            var unfinished = state.Clone(); unfinished.Dla.ParticleCount = 20_000;
            try { await renderer.RenderAsync(unfinished, 160, 160, null, cts.Token); throw new Exception("Growth cancellation ignored by renderer."); }
            catch (OperationCanceledException) { }
        }
        byte[] pausedFrame = Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None));
        Check(renderer.DlaParticleCount == state.Dla.ParticleCount && ageFrame.SequenceEqual(pausedFrame),
            "A canceled batch must preserve the uploaded frame and its particle-count metadata.");
        // Switch texture formats and dimensions on the same device.
        var flame = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Flame3D); flame.Iterations = 50_000;
        await renderer.RenderAsync(flame, 100, 100, null, CancellationToken.None);
        byte[] afterSwitch = Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None));
        Check(ageFrame.SequenceEqual(afterSwitch),
            "Flame/DLA format changes must preserve the DLA frame.");
        var store = new Fractal3DSaveStore(kind);
        state.SaveName = "DLA stage";
        state.Dla.ParticleCount = 500;
        store.Save(state);
        var restored = store.Load().Single();
        Check(restored.Dla == state.Dla, "Save files must preserve the growth stage and all DLA settings.");
        var local = CloudSaveRepository.ListLocal(out int unreadable).Single();
        Check(unreadable == 0 && local.Category == "Fractal3DDla", "DLA saves must be discoverable by the cloud repository.");
        var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == themeStyles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });
        var window = new Fractal3DWindow(kind);
        try
        {
            var source = presets[4].Clone(); source.Dla.ParticleCount = 250;
            window.LoadState(source);
            Check(window.CaptureState("UI").Dla == source.Dla, "WPF controls must preserve all growth parameters.");
            Check(window.DlaPanel.Visibility == Visibility.Visible && window.IterationsBox.Visibility == Visibility.Collapsed,
                "DLA must show its own controls and hide irrelevant iteration input.");
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(1240, 800)); root.Arrange(new Rect(0, 0, 1240, 800)); root.UpdateLayout();
            // Exercise the actual preview-frame path without showing a native window.
            var renderMethod = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object draft = Enum.ToObject(renderMethod.GetParameters()[0].ParameterType, 0);
            window.DlaPlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 3; i++) await (Task)renderMethod.Invoke(window, [draft])!;
            window.DlaPlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            int paused = window.CaptureState("pause").Dla.ParticleCount;
            Check(paused > 250 && paused < source.Dla.TargetParticles, "Live growth should advance and pause at a displayed stage.");
            await (Task)renderMethod.Invoke(window, [draft])!;
            Check(window.CaptureState("pause").Dla.ParticleCount == paused, "A paused preview must preserve the particle count.");
            window.DlaPlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await (Task)renderMethod.Invoke(window, [draft])!;
            window.DlaPlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.CaptureState("resume").Dla.ParticleCount > paused, "Resume must continue the displayed cluster.");
            int beforeInterrupt = window.CaptureState("before interrupt").Dla.ParticleCount;
            window.DlaSpeedSlider.Value = 5;
            window.DlaPlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Task interruptedFrame = (Task)renderMethod.Invoke(window, [draft])!;
            await Task.Delay(20);
            window.DlaPlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await interruptedFrame;
            int displayedAtPause = window.CaptureState("interrupt").Dla.ParticleCount;
            Check(displayedAtPause >= beforeInterrupt, "Interrupting growth must not lose displayed particles.");
            await (Task)renderMethod.Invoke(window, [draft])!;
            Check(window.CaptureState("interrupt paused").Dla.ParticleCount == displayedAtPause,
                "Canceled growth must never leak unfinished particles into the paused frame's counter.");
            window.DlaTargetSlider.Value += 100;
            Check(window.CaptureState("target").Dla.ParticleCount > paused, "Changing the target must keep the existing cluster.");
            window.DlaFlowSlider.Value = 20;
            Check(window.CaptureState("flow").Dla.ParticleCount == 0, "Changing growth physics must reset the simulation.");
            if (output is not null)
            {
                window.LoadState(presets[4]);
                window.CanvasImage.Source = await renderer.RenderAsync(presets[4], 880, 720, null, CancellationToken.None);
                root.UpdateLayout();
                var ui = new RenderTargetBitmap(1240, 800, 96, 96, PixelFormats.Pbgra32); ui.Render(root);
                WriteDlaPng(ui, Path.Combine(output, "dla-ui.png"));
                // Expand the flow section and inspect its dial below the fold separately.
                var flowPanel = window.DlaFlowSlider.Parent as FrameworkElement;
                flowPanel!.Measure(new Size(260, 420)); flowPanel.Arrange(new Rect(0, 0, 260, 420)); flowPanel.UpdateLayout();
                var flowUi = new RenderTargetBitmap(260, 420, 96, 96, PixelFormats.Pbgra32); flowUi.Render(flowPanel);
                WriteDlaPng(flowUi, Path.Combine(output, "dla-flow-ui.png"));
            }
        }
        finally { window.Close(); }
        Console.WriteLine("PASS (dla3d): connected reproducible 3D growth, cancellation/resume, five GPU presets, nine styles, cache, probe, formats, saves/cloud and live WPF controls.");
    }

    private static void WriteDlaPng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
