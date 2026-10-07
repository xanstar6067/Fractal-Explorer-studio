using System.Diagnostics;
using System.IO;
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
    private static async Task VerifyLichtenberg3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("lichtenberg3d");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        var kind = Fractal3DKind.Lichtenberg3D;
        var state = Fractal3DCatalog.CreateDefaultState(kind);
        Check(FractalCatalog.Create().Single(t => t.LaunchKey == Fractal3DCatalog.LaunchKey(kind)).IsThreeDimensional,
            "Lichtenberg must belong to the shared 3D catalog.");
        using var renderer = new Fractal3DRenderer();
        var host = renderer.DeviceHost;
        var settings = new Lichtenberg3DSettings { Size = 32, SegmentCount = 30 };
        Lichtenberg3DField saved = null!;
        await Task.Run(() =>
        {
            host.Gate.Wait();
            try
            {
                foreach (var electrodes in Enum.GetValues<LichtenbergElectrodes>())
                {
                    var s = settings with { Electrodes = electrodes };
                    using var simulation = new Lichtenberg3DGpuSimulation(host, s);
                    var gpu = simulation.Solve(CancellationToken.None);
                    var mask = Lichtenberg3DGpuSimulation.InitialMask(s);
                    mask[Lichtenberg3DGpuSimulation.SeedCell(s)] = 1;
                    // Independent double Jacobi solve, without GPU SOR, helper residual or shader formulas.
                    double[] current = new double[gpu.Length], next = new double[current.Length];
                    for (int i=0; i<current.Length; i++) current[i] = mask[i] == 1 || mask[i] == 3 ? 0 :
                        electrodes == LichtenbergElectrodes.Radial ? 1 : 1 - (i/32%32)/31d;
                    for (int iteration = 0; iteration < 3000; iteration++)
                    {
                        double change = 0;
                        for (int z = 0; z < 32; z++) for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                        {
                            int i = (z * 32 + y) * 32 + x;
                            if (mask[i] != 0) { next[i] = mask[i] == 2 ? 1 : 0; continue; }
                            double sum = 0;
                            foreach (var d in LaplaceDirections)
                                sum += current[(Math.Clamp(z+d.Item3,0,31)*32+Math.Clamp(y+d.Item2,0,31))*32+Math.Clamp(x+d.Item1,0,31)];
                            next[i] = sum / 6;
                            change = Math.Max(change, Math.Abs(next[i] - current[i]));
                        }
                        (current,next) = (next,current);
                        if (change < 1e-9) break;
                    }
                    Check(gpu.Zip(current).Max(v => Math.Abs(v.First-v.Second)) < .007,
                        $"{electrodes}: GPU potential must match an independent harmonic solution.");
                    Check(gpu.All(float.IsFinite) && gpu.All(v => v >= 0 && v <= 1), "Maximum principle.");
                }
                using var whole = new Lichtenberg3DGpuSimulation(host, settings);
                using var batched = new Lichtenberg3DGpuSimulation(host, settings);
                whole.GrowTo(30, CancellationToken.None);
                for (int i = 5; i <= 30; i += 5) batched.GrowTo(i, CancellationToken.None);
                Check(whole.Snapshot().GrowthOrder.SequenceEqual(batched.Snapshot().GrowthOrder), "Batching preserves the channel.");
                saved = batched.Snapshot();
                using var cts = new CancellationTokenSource(5);
                try { batched.GrowTo(40, cts.Token); } catch (OperationCanceledException) { }
                whole.GrowTo(45, CancellationToken.None); batched.GrowTo(45, CancellationToken.None);
                Check(whole.Snapshot().GrowthOrder.SequenceEqual(batched.Snapshot().GrowthOrder), "Cancel and retry preserve the channel.");
                var json = JsonSerializer.Serialize(saved, JsonOptionsFactory.Create());
                var roundtrip = JsonSerializer.Deserialize<Lichtenberg3DField>(json, JsonOptionsFactory.Create())!;
                Check(roundtrip.Potential.SequenceEqual(saved.Potential), "Potential must survive JSON bit for bit.");
                using var resumed = new Lichtenberg3DGpuSimulation(host, settings with { Field = roundtrip, SegmentCount = 45 });
                resumed.GrowTo(45, CancellationToken.None);
                Check(resumed.Snapshot().GrowthOrder.SequenceEqual(whole.Snapshot().GrowthOrder), "Disk continuation is exact.");
                // Known frontier with one/two incoming bonds checks multiplicity and eta independently.
                var weightsMask = new uint[32*32*32]; var potentials = new float[weightsMask.Length];
                int a = (10*32+10)*32+10, b = a+5; potentials[a]=.5f; potentials[b]=1;
                weightsMask[a-1]=1; weightsMask[b-1]=weightsMask[b+1]=1;
                var w0 = Lichtenberg3DGpuSimulation.BondWeights([a,b], potentials, 0, 32, weightsMask);
                var w2 = Lichtenberg3DGpuSimulation.BondWeights([a,b], potentials, 2, 32, weightsMask);
                Check(w0.SequenceEqual(new double[] { 1,2 }) && w2.SequenceEqual(new double[] { .25,2 }), "DBM bond probabilities.");
                using var other = new Lichtenberg3DGpuSimulation(host, settings with { Eta = 4 });
                other.GrowTo(30, CancellationToken.None);
                Check(!saved.GrowthOrder.SequenceEqual(other.Snapshot().GrowthOrder), "Eta changes growth.");
                foreach (int n in new[] { 48,64 })
                {
                    using var sized = new Lichtenberg3DGpuSimulation(host, settings with { Size=n });
                    sized.GrowTo(2,CancellationToken.None);
                    Check(sized.Count==2, $"Grid {n} must solve and grow.");
                }
            }
            finally { host.Gate.Release(); }
        });
        Console.WriteLine("Laplace, probabilities, grids, cancellation and exact continuation: PASS");
        var signatures = new HashSet<string>();
        var presets = Fractal3DCatalog.GetPresets(kind);
        for (int i = 0; i < presets.Count; i++)
        {
            var watch = Stopwatch.StartNew();
            BitmapSource image = await renderer.RenderAsync(presets[i], 320,320,null,CancellationToken.None);
            if (output is not null) WriteDlaPng(image,Path.Combine(output,$"lichtenberg-{i}.png"));
            Check(HasFractal3DStructure(Pixels(image),320,320), $"{presets[i].SaveName}: empty frame.");
            signatures.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Pixels(image))));
            Console.WriteLine($"{presets[i].SaveName}: {renderer.LichtenbergDisplayedField!.Count} segments, {watch.Elapsed.TotalSeconds:F2}s");
        }
        Check(signatures.Count == presets.Count, "Five distinct presets.");
        state.Lichtenberg = settings with { Field = saved };
        state.CameraDistance = .7;
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            state.ShadingStyle = style;
            var image = await renderer.RenderAsync(state,160,160,null,CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(image),160,160), $"Lichtenberg {style}: empty frame.");
        }
        Check(File.Exists(AppPaths.GetShaderCacheFile("lichtenberg3d-relax")) &&
            File.Exists(AppPaths.GetShaderCacheFile("lichtenberg3d-pixel")), "Both shaders use the shared cache.");
        var exact = state.Clone(); exact.Lichtenberg = settings with { Field = saved };
        byte[] before = Pixels(await renderer.RenderAsync(exact,160,160,null,CancellationToken.None));
        var fieldMember = typeof(Fractal3DRenderer).GetField("_lichtenberg",BindingFlags.Instance|BindingFlags.NonPublic)!;
        object? cached = fieldMember.GetValue(renderer);
        exact.Lichtenberg = exact.Lichtenberg with { Field = renderer.LichtenbergDisplayedField };
        exact.CameraYaw += 20;
        await renderer.RenderAsync(exact,160,160,null,CancellationToken.None);
        Check(ReferenceEquals(cached,fieldMember.GetValue(renderer)), "Camera reuses growth.");
        exact.CameraYaw -= 20;
        var decoded = JsonSerializer.Deserialize<Lichtenberg3DField>(JsonSerializer.Serialize(saved,JsonOptionsFactory.Create()),JsonOptionsFactory.Create())!;
        exact.Lichtenberg = settings with { Field = decoded };
        byte[] afterDecode = Pixels(await renderer.RenderAsync(exact,160,160,null,CancellationToken.None));
        Check(before.SequenceEqual(afterDecode), "A decoded checkpoint renders identically.");
        Check(!ReferenceEquals(cached,fieldMember.GetValue(renderer)), "Imported potential replaces cached simulation even with the same growth order.");
        var store = new Fractal3DSaveStore(kind); state.SaveName="saved-discharge"; store.Save(state);
        var loaded = store.Load().Single();
        Check(loaded.Lichtenberg.Field!.GrowthOrder.SequenceEqual(saved.GrowthOrder), "Save store persists the channel.");
        Check(CloudSaveRepository.ListLocal(out _).Any(s=>s.Category=="Fractal3DLichtenberg"), "Cloud sees the new category.");
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == themeStyles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });
        var window = new Fractal3DWindow(kind);
        try
        {
            window.LoadState(state);
            Check(window.CaptureState("capture").Lichtenberg.Field!.Potential.SequenceEqual(saved.Potential), "WPF saves displayed checkpoint.");
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(1240,800)); root.Arrange(new Rect(0,0,1240,800)); root.UpdateLayout();
            Check(window.LichtenbergPanel.Visibility==Visibility.Visible && window.DlaPanel.Visibility==Visibility.Collapsed, "Dedicated controls.");
            // Editing drafts must not modify the field, eta or save until Apply.
            window.LichtenbergEtaBox.Text="3";
            Check(window.CaptureState("draft").Lichtenberg.Eta==settings.Eta, "Draft eta is not applied prematurely.");
            var render = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var quality = Enum.Parse(typeof(Fractal3DWindow).GetNestedType("FrameQuality",BindingFlags.NonPublic)!,"Full");
            await (Task)render.Invoke(window,[quality])!;
            typeof(Fractal3DWindow).GetMethod("LichtenbergStep_OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[window,new RoutedEventArgs()]);
            await (Task)render.Invoke(window,[quality])!;
            Check(window.CaptureState("step").Lichtenberg.SegmentCount==31,"One-step control advances exactly one segment.");
            window.LichtenbergPlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await (Task)render.Invoke(window,[quality])!;
            window.LichtenbergPlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            int paused = window.CaptureState("pause").Lichtenberg.SegmentCount;
            await (Task)render.Invoke(window,[quality])!;
            Check(window.CaptureState("still paused").Lichtenberg.SegmentCount==paused,"Pause keeps displayed field.");
            window.LichtenbergSpeedSlider.Value = 25;
            window.LichtenbergPlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Task stale = (Task)render.Invoke(window,[quality])!;
            typeof(Fractal3DWindow).GetMethod("LichtenbergRestart_OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[window,new RoutedEventArgs()]);
            await stale;
            Check(window.CaptureState("restart").Lichtenberg.SegmentCount == 0 &&
                window.CaptureState("restart").Lichtenberg.Field is null, "Late results cannot replace a restarted session.");
            if (output is not null)
            {
                window.LoadState(presets[0]);
                await (Task)render.Invoke(window,[quality])!;
                root.UpdateLayout(); var ui = new RenderTargetBitmap(1240,800,96,96,PixelFormats.Pbgra32); ui.Render(root);
                WriteDlaPng(ui,Path.Combine(output,"lichtenberg-ui.png"));
            }
        }
        finally { window.Close(); }
        Console.WriteLine("PASS (lichtenberg3d): independent Laplace, DBM weights, exact saves/resume, five presets, nine styles, cache, cloud and WPF.");
    }
    private static readonly (int,int,int)[] LaplaceDirections = [(1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1)];
}
