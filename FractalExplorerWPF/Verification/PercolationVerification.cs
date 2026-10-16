using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.PercolationMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyPercolationAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("percolation");
        string? output = ReadOption(args, "--out");
        if (output is not null) Directory.CreateDirectory(output);
        VerifyCatalogData();
        var watch = Stopwatch.StartNew();
        foreach (var lattice in Enum.GetValues<PercolationLattice>())
        {
            var empty = Percolation.Generate(32, 0, 0, lattice);
            Check(empty.Statistics.Occupied == 0 && empty.Statistics.ClusterCount == 0 && empty.Path.Length == 0,
                "p=0 must be empty, with no crossing or largest cluster.");
            var full = Percolation.Generate(32, 1, int.MinValue, lattice);
            Check(full.Statistics.ClusterCount == 1 && full.Statistics.LargestSize == 1024 &&
                full.Statistics.HorizontalCrossing && full.Statistics.VerticalCrossing && full.Path.Length == 32,
                "p=1 must form a single cluster, crossing both directions, with a 31-edge shortest path.");
            // Геометрическая проверка соседства: все и только пары на расстоянии 1.
            var buffers = new int[6];
            for (int i = 0; i < 25; i++)
            {
                int count = Percolation.GetNeighbors(i, 5, lattice, buffers);
                for (int j = 0; j < 25; j++)
                {
                    double x1 = i % 5, x2 = j % 5, y1 = i / 5, y2 = j / 5;
                    if (lattice == PercolationLattice.Triangular)
                    {
                        x1 += ((int)y1 & 1) * 0.5; x2 += ((int)y2 & 1) * 0.5;
                        y1 *= Math.Sqrt(3) / 2; y2 *= Math.Sqrt(3) / 2;
                    }
                    bool adjacent = Math.Abs((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2) - 1) < 1e-9;
                    Check(buffers.AsSpan(0, count).Contains(j) == adjacent,
                        "Neighbors must be geometric nearest neighbors, with no wraparound or false diagonals.");
                }
            }
            // Все конфигурации 2×2, независимое замыкание отношения связности.
            for (int mask = 0; mask < 16; mask++)
            {
                bool[] cells = Enumerable.Range(0, 4).Select(i => (mask & (1 << i)) != 0).ToArray();
                var edges = new bool[4, 4];
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 4; j++)
                        edges[i, j] = cells[i] && cells[j] && (i == j ||
                            (i == 0 && j == 1) || (i == 1 && j == 0) || (i == 2 && j == 3) || (i == 3 && j == 2) ||
                            Math.Abs(i - j) == 2 || (lattice == PercolationLattice.Triangular && ((i == 1 && j == 2) || (i == 2 && j == 1))));
                for (int k = 0; k < 4; k++)
                    for (int i = 0; i < 4; i++)
                        for (int j = 0; j < 4; j++) edges[i, j] |= edges[i, k] && edges[k, j];
                var result = Percolation.Analyze(2, cells, lattice);
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 4; j++)
                        Check(edges[i, j] == (result.Labels[i] != 0 && result.Labels[i] == result.Labels[j]),
                            "Component labels must agree with independent transitive closure.");
                bool horizontal = edges[0, 1] || edges[0, 3] || edges[2, 1] || edges[2, 3];
                bool vertical = edges[0, 2] || edges[0, 3] || edges[1, 2] || edges[1, 3];
                Check(result.Statistics.HorizontalCrossing == horizontal && result.Statistics.VerticalCrossing == vertical,
                    "Crossings must use real connected components, not virtual connections along a boundary.");
            }
            foreach (int seed in new[] { 0, 1729, -17, int.MinValue, int.MaxValue })
            {
                var low = Percolation.Generate(128, 0.4, seed, lattice);
                var critical = Percolation.Generate(128, Percolation.CriticalProbability(lattice), seed, lattice);
                var high = Percolation.Generate(128, 0.8, seed, lattice);
                Check(critical.Labels.SequenceEqual(Percolation.Generate(128, Percolation.CriticalProbability(lattice), seed, lattice).Labels),
                    "Seed must reproduce exact occupancy and cluster labels.");
                Check(Enumerable.Range(0, low.Labels.Length).All(i =>
                    (low.Labels[i] == 0 || critical.Labels[i] != 0) && (critical.Labels[i] == 0 || high.Labels[i] != 0)),
                    "Increasing p must only add occupied sites.");
                Check(Math.Abs(critical.Statistics.Occupied / 16384.0 - Percolation.CriticalProbability(lattice)) < 0.02 &&
                    critical.ClusterSizes.Sum() == critical.Statistics.Occupied,
                    "Density must match p; component sizes must partition the occupied sites.");
                foreach (var result in new[] { low, critical, high })
                {
                    Check(result.Path.Distinct().Count() == result.Path.Length && result.Path.All(i => result.Labels[i] != 0),
                        "A path cannot revisit a vertex or traverse a vacant site.");
                    for (int k = 1; k < result.Path.Length; k++)
                    {
                        int count = Percolation.GetNeighbors(result.Path[k - 1], 128, lattice, buffers);
                        Check(buffers.AsSpan(0, count).Contains(result.Path[k]), "Path edges must connect nearest neighbors.");
                    }
                    if (result.Path.Length > 0)
                        Check(result.Statistics.VerticalCrossing
                            ? result.Path[0] / 128 == 0 && result.Path[^1] / 128 == 127
                            : result.Path[0] % 128 == 0 && result.Path[^1] % 128 == 127,
                            "A path must connect opposite physical boundaries.");
                }
            }
            var state = MathematicalLaboratoryCatalog.CreateDefaultState(MathematicalLaboratoryKind.Percolation);
            state.Mode = (int)lattice; state.Parameter = Percolation.CriticalProbability(lattice);
            state.PrimaryValue = 128;
            for (int coloring = 0; coloring < 4; coloring++)
            {
                state.SecondaryValue = coloring;
                BitmapSource bitmap = await MathematicalLaboratoryRenderer.RenderBitmapAsync(state, 512, 512, CancellationToken.None);
                byte[] pixels = PercolationPixels(bitmap);
                Check(bitmap.IsFrozen && Enumerable.Range(0, 512 * 512).Select(i => BitConverter.ToInt32(pixels, i * 4)).Distinct().Count() > 1,
                    "Every lattice/color mode must produce a visible frozen image.");
                if (output is not null)
                {
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"percolation-{lattice}-{coloring}.png")); encoder.Save(file);
                }
            }
        }
        // Извилистый единственный путь длиной 10 рёбер — короче пройти нельзя.
        bool[] corridor = new bool[25];
        foreach (int i in new[] { 2, 7, 6, 5, 10, 15, 16, 17, 18, 19, 24 }) corridor[i] = true;
        var winding = Percolation.Analyze(5, corridor, PercolationLattice.Square);
        Check(winding.Path.Length == 11, "Shortest path must respect a winding corridor.");
        var saved = MathematicalLaboratoryCatalog.CreateDefaultState(MathematicalLaboratoryKind.Percolation);
        saved.SaveName = "percolation round-trip"; saved.Mode = 1; saved.Parameter = 0.51;
        saved.SecondaryValue = 2; saved.TertiaryValue = -1729; saved.Zoom = 1.7; saved.Rotation = 23;
        saved.ViewCenterX = 0.15; saved.Animate = true; saved.Phase = -1; saved.PrimaryColor = Colors.Lime;
        var store = new MathematicalLaboratorySaveStore(saved.Kind); store.Save(saved);
        var restored = store.Load().Single();
        byte[] savedPixels = PercolationPixels(await MathematicalLaboratoryRenderer.RenderBitmapAsync(saved, 256, 192, CancellationToken.None));
        byte[] restoredPixels = PercolationPixels(await MathematicalLaboratoryRenderer.RenderBitmapAsync(restored, 256, 192, CancellationToken.None));
        Check(savedPixels.SequenceEqual(restoredPixels),
            "Disk round-trip must preserve occupancy, coloring, path and camera.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool caught = false;
        try { Percolation.Generate(768, 0.5, 1, PercolationLattice.Square, cancelled.Token); }
        catch (OperationCanceledException) { caught = true; }
        Check(caught, "Generation must honour cancellation.");
        caught = false;
        try { await MathematicalLaboratoryRenderer.RenderBitmapAsync(saved, 512, 512, cancelled.Token); }
        catch (OperationCanceledException) { caught = true; }
        Check(caught, "Rendering must honour cancellation.");
        var max = Percolation.Generate(768, 1, 1, PercolationLattice.Triangular);
        Check(max.Statistics.LargestSize == 768 * 768 && max.Path.Length == 768, "Maximum size must be supported.");
        var fullFrame = MathematicalLaboratoryCatalog.CreateDefaultState(MathematicalLaboratoryKind.Percolation);
        fullFrame.PrimaryValue = 32; fullFrame.Parameter = 1;
        byte[] withPath = PercolationPixels(await MathematicalLaboratoryRenderer.RenderBitmapAsync(fullFrame, 256, 256, CancellationToken.None));
        fullFrame.Filled = false;
        byte[] withoutPath = PercolationPixels(await MathematicalLaboratoryRenderer.RenderBitmapAsync(fullFrame, 256, 256, CancellationToken.None));
        fullFrame.ShowGuides = true;
        byte[] withGrid = PercolationPixels(await MathematicalLaboratoryRenderer.RenderBitmapAsync(fullFrame, 256, 256, CancellationToken.None));
        Check(!withPath.SequenceEqual(withoutPath) && !withGrid.SequenceEqual(withoutPath),
            "Path and grid switches must independently affect the image.");
        var item = FractalCatalog.Create().Single(item => item.LaunchKey == "MathLab:Percolation");
        Check(CatalogPreviewLoader.IsRendered(item) && (await CatalogPreviewLoader.RenderAsync(item, CancellationToken.None)).PixelWidth == 512,
            "Catalog must render the new thumbnail.");
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        var window = new MathematicalLaboratoryWindow(saved.Kind);
        try
        {
            window.LoadState(restored);
            var captured = window.CaptureState("window");
            Check(captured.Mode == 1 && captured.SecondaryValue == 2 && captured.TertiaryValue == -1729 &&
                captured.Parameter == 0.51 && captured.Phase == -1 && captured.PrimaryColor == Colors.Lime,
                "WPF panel must restore exact saved parameters.");
            var panel = (PercolationEditor)window.FindName("PercolationPanel");
            ((Slider)panel.FindName("ProbabilitySlider")).Value = 0.47;
            Check(window.CaptureState("slider").Parameter == 0.47, "Slider must update probability.");
            ((ComboBox)panel.FindName("ColoringBox")).SelectedIndex = 3;
            Check(window.CaptureState("coloring").SecondaryValue == 3, "Color selector must update captured state.");
            ((ComboBox)window.FindName("ModeBox")).SelectedIndex = 0;
            Check(Math.Abs(window.CaptureState("critical").Parameter - Percolation.CriticalProbability(PercolationLattice.Square)) < 1e-8,
                "Changing lattice must select its own critical probability.");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            async Task FinishRender()
            {
                var timeout = Stopwatch.StartNew();
                while ((bool)typeof(MathematicalLaboratoryWindow).GetField("_rendering", flags)!.GetValue(window)! && timeout.ElapsedMilliseconds < 3000)
                    await Task.Delay(10);
                Check(!(bool)typeof(MathematicalLaboratoryWindow).GetField("_rendering", flags)!.GetValue(window)!, "UI render must finish.");
            }
            saved.Animate = true; saved.Phase = 1; saved.Parameter = 0.999;
            window.LoadState(saved);
            var tick = typeof(MathematicalLaboratoryWindow).GetMethod("AnimationTimer_OnTick", flags)!;
            tick.Invoke(window, new object?[] { null, EventArgs.Empty }); await FinishRender();
            Check(window.CaptureState("upper").Parameter == 1 && window.CaptureState("upper").Phase == -1,
                "Animation must reach p=1 and reverse without changing seed.");
            tick.Invoke(window, new object?[] { null, EventArgs.Empty }); await FinishRender();
            Check(window.CaptureState("reverse").Parameter == 0.998 && window.CaptureState("reverse").TertiaryValue == saved.TertiaryValue,
                "Animation must decrease p on the same realization after the upper endpoint.");
            saved.Phase = -1; saved.Parameter = 0.001;
            window.LoadState(saved);
            tick.Invoke(window, new object?[] { null, EventArgs.Empty }); await FinishRender();
            Check(window.CaptureState("lower").Parameter == 0 && window.CaptureState("lower").Phase == 1,
                "Animation must reverse at p=0.");
            saved = MathematicalLaboratoryCatalog.CreateDefaultState(MathematicalLaboratoryKind.Percolation);
            window.LoadState(saved);
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(1180, 760)); content.Arrange(new Rect(0, 0, 1180, 760)); content.UpdateLayout();
            await (Task)typeof(MathematicalLaboratoryWindow).GetMethod("RenderAsync", flags)!.Invoke(window, null)!;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            content.UpdateLayout();
            var shown = (BitmapSource)((System.Windows.Controls.Image)window.FindName("CanvasImage")).Source;
            byte[] shownPixels = PercolationPixels(shown);
            Check(Enumerable.Range(0, shown.PixelWidth * shown.PixelHeight).Select(i => BitConverter.ToInt32(shownPixels, i * 4)).Distinct().Count() > 1,
                $"Window must present the current nonempty frame after p=0; p={window.CaptureState("shown").Parameter}, dimensions={shown.PixelWidth}×{shown.PixelHeight}.");
            if (output is not null)
            {
                var image = new RenderTargetBitmap(1180, 760, 96, 96, PixelFormats.Pbgra32); image.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var file = File.Create(Path.Combine(output, "percolation-window.png")); encoder.Save(file);
            }
        }
        finally { window.Close(); }
        Console.WriteLine($"PASS (percolation): geometric neighbors, exhaustive components, crossings, shortest paths, monotonicity, seeds, density, 8 frames, maximum grid, cancellation, saves, catalog and WPF controls ({watch.ElapsedMilliseconds} ms).");
    }

    private static byte[] PercolationPixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
    }
}
