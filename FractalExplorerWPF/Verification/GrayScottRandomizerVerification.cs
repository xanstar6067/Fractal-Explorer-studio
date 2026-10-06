using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyGrayScottRandomizerAsync(string[] args)
    {
        string? output = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
            ? Directory.CreateDirectory(Path.GetFullPath(args[1])).FullName : null;
        VerifyGrayScottPatternMetrics();
        await Task.Run(() => VerifyGrayScottPresetProbes());
        await Task.Run(() => VerifyGrayScottSearch2D(output));
        await VerifyGrayScottSearch3DAsync();
        await VerifyGrayScottSearchWindowsAsync();
        Console.WriteLine("PASS (gray-scott-random): metrics of synthetic fields, preset probes, 2D search by target, variation, cancellation, 3D GPU search and both windows.");
    }

    /// <summary>Классификатор на заведомо известных полях: пятна, полосы, отверстия, пустое и однородное.</summary>
    private static void VerifyGrayScottPatternMetrics()
    {
        const int n = 128;
        float[] Field2D(Func<int, int, bool> inside, float on = .35f, float off = 0f)
        {
            var v = new float[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) v[y * n + x] = inside(x, y) ? on : off;
            return v;
        }
        bool Disk(int x, int y) { int cx = x % 16 - 8, cy = y % 16 - 8; return cx * cx + cy * cy <= 20; }
        var spots = GrayScottRandomizer.Measure(Field2D(Disk), null, n, 2);
        var holes = GrayScottRandomizer.Measure(Field2D((x, y) => !Disk(x, y)), null, n, 2);
        var stripes = GrayScottRandomizer.Measure(Field2D((x, y) => (x + y / 3) % 16 < 7), null, n, 2);
        var worms = GrayScottRandomizer.Measure(Field2D((x, y) => y % 24 < 5 && (x + y) % 64 < 40), null, n, 2);
        var empty = GrayScottRandomizer.Measure(new float[n * n], null, n, 2);
        var uniform = GrayScottRandomizer.Measure(Field2D((_, _) => true, .3f), null, n, 2);
        Check(spots.IsAlive && spots.Shape == GrayScottPatternShape.Spots && spots.Blobs == 64, $"Disks must be spots: {spots}.");
        Check(holes.IsAlive && holes.Shape == GrayScottPatternShape.Holes && holes.Holes == 64, $"Inverted disks must be holes: {holes}.");
        Check(stripes.IsAlive && stripes.Shape == GrayScottPatternShape.Stripes, $"Stripes must be stripes: {stripes}.");
        Check(worms.IsAlive && worms.Shape == GrayScottPatternShape.Stripes, $"Separate elongated worms must not count as spots: {worms}.");
        Check(!empty.IsAlive && !uniform.IsAlive, "Empty and uniform fields must be rejected.");
        var shifted = Field2D((x, y) => Disk(x + 3, y));
        var moving = GrayScottRandomizer.Measure(shifted, Field2D(Disk), n, 2);
        Check(moving.IsMoving && !GrayScottRandomizer.Measure(Field2D(Disk), Field2D(Disk), n, 2).IsMoving,
            "Activity must separate a moved pattern from a frozen one.");

        const int m = 48;
        float[] Field3D(Func<int, int, int, bool> inside)
        {
            var v = new float[m * m * m];
            for (int z = 0; z < m; z++) for (int y = 0; y < m; y++) for (int x = 0; x < m; x++)
                v[(z * m + y) * m + x] = inside(x, y, z) ? .3f : 0;
            return v;
        }
        var balls = GrayScottRandomizer.Measure(Field3D((x, y, z) =>
        { int a = x % 12 - 6, b = y % 12 - 6, c = z % 12 - 6; return a * a + b * b + c * c <= 10; }), null, m, 3);
        var slabs = GrayScottRandomizer.Measure(Field3D((x, _, _) => x % 12 < 5), null, m, 3);
        Check(balls.Shape == GrayScottPatternShape.Spots && balls.Blobs == 64, $"Balls must be droplets: {balls}.");
        Check(slabs.Shape == GrayScottPatternShape.Stripes, $"Slabs must be membranes: {slabs}.");
        Console.WriteLine("Gray–Scott random: synthetic spots, holes, stripes, worms, droplets, membranes and motion classified.");
    }

    /// <summary>Пробный прогон узнаёт известные режимы: митоз — пятна, хаос — движение, мёртвая зона — пусто.</summary>
    private static void VerifyGrayScottPresetProbes()
    {
        foreach (GrayScottPreset preset in GrayScottPresets.All)
        {
            var m = GrayScottRandomizer.Probe2D(preset.State.Clone(), CancellationToken.None);
            Console.WriteLine($"  {preset.Id,-9} alive={m.IsAlive} {m.Describe(false),-34} dev={m.Deviation:F3} cov={m.Coverage:F3} act={m.Activity:F3} blobs={m.Blobs}/{m.CompactBlobShare:F2} holes={m.Holes}/{m.CompactHoleShare:F2} peak={m.Peak:F2}");
            switch (preset.Id)
            {
                case "mitosis": Check(m.IsAlive && m.Shape == GrayScottPatternShape.Spots, "Mitosis must probe as spots."); break;
                case "coral": Check(m.IsAlive && m.Shape != GrayScottPatternShape.Spots, "Coral must probe as a labyrinth."); break;
                case "chaos": Check(m.IsAlive && m.IsMoving, "Chaos must probe as moving."); break;
                case "worms": Check(m.IsAlive, "Worms must probe as a live pattern."); break;
            }
        }
        var dead = new GrayScottState { Feed = .02, Kill = .075, SeedMode = GrayScottSeedMode.Noise };
        var flooded = new GrayScottState { Feed = .08, Kill = .03, SeedMode = GrayScottSeedMode.Noise };
        Check(!GrayScottRandomizer.Probe2D(dead, CancellationToken.None).IsAlive, "Seeds beyond the pattern band must die out.");
        Check(!GrayScottRandomizer.Probe2D(flooded, CancellationToken.None).IsAlive, "A flooded uniform field must be rejected.");
    }

    private static void VerifyGrayScottSearch2D(string? output)
    {
        var current = GrayScottPresets.All[0].State.Clone();
        current.GridSize = 384; current.Backend = GrayScottBackend.Cpu; current.StepsPerFrame = 12; current.TargetFps = 60;
        foreach (GrayScottPatternTarget target in Enum.GetValues<GrayScottPatternTarget>())
        {
            var reports = new List<GrayScottSearchProgress>();
            var found = GrayScottRandomizer.Search2D(current, target, false, new SyncProgress<GrayScottSearchProgress>(reports.Add),
                CancellationToken.None, new Random(1000 + (int)target));
            Check(found is not null, $"Search for {target} must find a live pattern.");
            var state = found!.State; state.Validate();
            Check(found.MatchesTarget && found.Metrics.Matches(target), $"Search for {target} must return a matching pattern: {found.Metrics}.");
            Check(state.GridSize == 384 && state.Backend == GrayScottBackend.Cpu && state.StepsPerFrame == 12 && state.TargetFps == 60 &&
                state.Checkpoint is null && state.PresetId is null, "Search must keep engine, grid and speed and start a fresh field.");
            Check(state.DiffusionU == 1 && state.DiffusionV == .5 && state.Kill > GrayScottRandomizer.SaddleNodeKill(state.Feed) - .006,
                "Random patterns must use matched diffusion and stay near the saddle-node band.");
            Check(reports.Count > 0 && reports[^1].Checked == found.Checked, "Progress must report every checked batch.");
            var again = GrayScottRandomizer.Probe2D(state, CancellationToken.None);
            Check(again == found.Metrics, "The probe must be reproducible.");
            Console.WriteLine($"  2D {target,-7} → {found.Metrics.Describe(false)} · F {state.Feed} · K {state.Kill} · {state.SeedMode} · проверено {found.Checked}");
            if (output is not null)
            {
                var preview = state.Clone(); preview.GridSize = 256;
                var engine = new GrayScottSimulation(preview); engine.Advance(5000, CancellationToken.None);
                WriteGrayScottFrame(engine.Snapshot(), preview, Path.Combine(output, $"random-{target}.png"));
            }
        }

        var source = new GrayScottState { Feed = .0367, Kill = .0649, SeedMode = GrayScottSeedMode.RandomSpots, RandomSeed = 77, SeedCount = 9 };
        var variation = GrayScottRandomizer.Search2D(source, GrayScottPatternTarget.Any, true, null, CancellationToken.None, new Random(5));
        Check(variation is not null, "Variation of mitosis must stay alive.");
        var v = variation!.State;
        Check(v.SeedMode == source.SeedMode && v.RandomSeed == 77 && v.SeedCount == 9 &&
            Math.Abs(v.Feed - source.Feed) <= .0026 && Math.Abs(v.Kill - source.Kill) <= .0011 && (v.Feed, v.Kill) != (source.Feed, source.Kill),
            "Variation must move F/K slightly and keep the seed.");

        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool threw = false;
        try { GrayScottRandomizer.Search2D(current, GrayScottPatternTarget.Any, false, null, cancelled.Token); }
        catch (OperationCanceledException) { threw = true; }
        Check(threw, "A cancelled search must stop with OperationCanceledException.");
    }

    private static async Task VerifyGrayScott3DSearchCoreAsync(Direct3DDeviceHost host)
    {
        var current = new GrayScott3DSettings { Size = 48, StepsPerFrame = 40, CutAxis = 2, CutPosition = .25 };
        foreach (var target in new[] { GrayScottPatternTarget.Any, GrayScottPatternTarget.Spots, GrayScottPatternTarget.Stripes })
        {
            var found = await Task.Run(() => GrayScottRandomizer.Search3D(host, current, target, false, null, CancellationToken.None, new Random(30 + (int)target)));
            Check(found is not null, $"3D search for {target} must find a live pattern.");
            var s = found!.State; s.Validate();
            Check(s.Size == 48 && s.StepsPerFrame == 40 && s.CutAxis == 2 && s.CutPosition == .25 && s.Field is null && s.Live is null,
                "3D search must keep grid and view and start from a seed.");
            Check(s.Threshold is >= .02 and <= .8 && s.SeedShape != GrayScott3DSeed.Empty, "3D search must pick a visible surface level.");
            // Окно запускает тот же вид на той же сетке: поверхность на выбранном уровне есть.
            using var simulation = new GrayScott3DGpuSimulation(host, s);
            simulation.Advance(s.InitialSteps, CancellationToken.None);
            var field = simulation.ReadCurrent().Concentrations;
            int above = 0; for (int i = 1; i < field.Length; i += 2) if (field[i] > s.Threshold) above++;
            Check(above > 0 && above < field.Length / 2, $"The found 3D pattern must cross its surface level: {above} cells.");
            Check(!found.MatchesTarget || found.Metrics.Matches(target), "3D match flag must agree with metrics.");
            Console.WriteLine($"  3D {target,-7} → {found.Metrics.Describe(true)} · F {s.Feed} · K {s.Kill} · Du {s.DiffusionU} · {s.SeedShape} · уровень {s.Threshold} · проверено {found.Checked}{(found.MatchesTarget ? "" : " (без совпадения)")}");
        }
        var source = new GrayScott3DSettings { Size = 48, Seed = 9 };
        var variation = await Task.Run(() => GrayScottRandomizer.Search3D(host, source, GrayScottPatternTarget.Any, true, null, CancellationToken.None, new Random(3)));
        Check(variation is not null && variation.State.Seed == 9 && variation.State.SeedShape == source.SeedShape &&
            variation.State.DiffusionU == source.DiffusionU && Math.Abs(variation.State.Feed - source.Feed) <= .0026,
            "3D variation must keep seed, shape and diffusion.");
    }

    private static async Task VerifyGrayScottSearch3DAsync()
    {
        using var renderer = new Fractal3DRenderer();
        await VerifyGrayScott3DSearchCoreAsync(renderer.DeviceHost);
    }



    /// <summary>Окна: поиск применяет находку, «Вернуть предыдущий» восстанавливает поле, повторный щелчок останавливает поиск.</summary>
    private static async Task VerifyGrayScottSearchWindowsAsync()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);

        var window = new GrayScottWindow();
        object? Invoke(string name, params object[] p) => typeof(GrayScottWindow).GetMethod(name, flags)!.Invoke(window, p);
        T Field<T>(string name) => (T)typeof(GrayScottWindow).GetField(name, flags)!.GetValue(window)!;
        async Task Idle()
        {
            await Task.Delay(100); var watch = Stopwatch.StartNew();
            while ((Field<bool>("_resetting") || Field<bool>("_frameBusy")) && watch.ElapsedMilliseconds < 10000) await Task.Delay(10);
            Check(watch.ElapsedMilliseconds < 10000, "Window operation must finish.");
        }
        try
        {
            await (Task)Invoke("InstallEngineAsync", new GrayScottState { GridSize = 160, Backend = GrayScottBackend.Cpu }, false, true)!;
            await Idle(); await (Task)Invoke("ProduceFrameAsync", (int?)30)!;
            var before = window.CaptureState("before");
            ((ComboBox)window.FindName("SearchTargetBox")).SelectedIndex = (int)GrayScottPatternTarget.Spots;
            await (Task)Invoke("RunSearchAsync", false)!; await Idle();
            var found = window.CaptureState("found");
            string status = ((TextBlock)window.FindName("SearchStatus")).Text;
            Check((found.Feed, found.Kill) != (before.Feed, before.Kill) && found.GridSize == 160 && found.Backend == GrayScottBackend.Cpu &&
                Field<bool>("_running") && ((ComboBox)window.FindName("PresetBox")).SelectedItem is null,
                $"A found pattern must run on the current grid and engine: {status}");
            Check(((TextBox)window.FindName("FeedBox")).Text == found.Feed.ToString("G15", System.Globalization.CultureInfo.InvariantCulture) &&
                status.StartsWith("Пятна", StringComparison.Ordinal) && ((Button)window.FindName("SearchUndoButton")).IsEnabled,
                $"Controls must show the found pattern and allow undo: {status}");

            Invoke("SetRunning", false); await Idle();
            Invoke("SearchUndo_OnClick", window, new RoutedEventArgs()); await Idle();
            var restored = window.CaptureState("restored");
            Check(restored.Feed == before.Feed && restored.Kill == before.Kill && restored.Checkpoint!.StepCount == before.Checkpoint!.StepCount &&
                restored.Checkpoint.V.SequenceEqual(before.Checkpoint.V) && !((Button)window.FindName("SearchUndoButton")).IsEnabled,
                "Undo must restore the exact previous field and parameters.");

            var running = (Task)Invoke("RunSearchAsync", true)!;
            Check(((Button)window.FindName("VariationButton")).Content as string == "Остановить поиск" &&
                !((Button)window.FindName("SearchButton")).IsEnabled, "A running search must offer to stop it.");
            await (Task)Invoke("RunSearchAsync", true)!; await running; await Idle();
            Check(((TextBlock)window.FindName("SearchStatus")).Text == "Поиск остановлен." && window.CaptureState("stopped").Feed == before.Feed &&
                ((Button)window.FindName("VariationButton")).Content as string == "Вариация", "Stopping must keep the current pattern.");
        }
        finally { window.Close(); }

        var window3D = new Fractal3DWindow(Fractal3DKind.GrayScott3D);
        object? Invoke3D(string name, params object[] p) => typeof(Fractal3DWindow).GetMethod(name, flags)!.Invoke(window3D, p);
        T Field3D<T>(string name) => (T)typeof(Fractal3DWindow).GetField(name, flags)!.GetValue(window3D)!;
        async Task Settle()
        {
            var watch = Stopwatch.StartNew();
            while (Field3D<bool>("_grayBusy") && watch.ElapsedMilliseconds < 30000) await Task.Delay(10);
            var method = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync", flags)!;
            await (Task)method.Invoke(window3D, [Enum.Parse(method.GetParameters()[0].ParameterType, "Full")])!;
        }
        try
        {
            await Settle();
            var before = window3D.CaptureState("before").GrayScott;
            await (Task)Invoke3D("RunGraySearchAsync", false)!; await Settle();
            var found = window3D.CaptureState("found").GrayScott;
            string status = ((TextBlock)window3D.FindName("GraySearchStatus")).Text;
            Check((found.Feed, found.Kill) != (before.Feed, before.Kill) && found.Size == before.Size && found.Field!.Step == found.InitialSteps &&
                ((Slider)window3D.FindName("GrayThresholdSlider")).Value == found.Threshold && ((Button)window3D.FindName("GraySearchUndoButton")).IsEnabled,
                $"A 3D find must restart from its seed with the chosen surface level: {status}");
            Invoke3D("GraySearchUndo_OnClick", window3D, new RoutedEventArgs()); await Settle();
            var restored = window3D.CaptureState("restored").GrayScott;
            Check(restored.Feed == before.Feed && restored.Kill == before.Kill && restored.Field!.Step == before.Field!.Step &&
                restored.Field.Concentrations.SequenceEqual(before.Field.Concentrations), "3D undo must restore the exact previous volume.");
            Console.WriteLine($"Gray–Scott random windows: 2D and 3D find, undo and stop · 3D: {status}");
        }
        finally { window3D.Close(); }
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
