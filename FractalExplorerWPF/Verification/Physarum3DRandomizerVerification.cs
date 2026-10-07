using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyPhysarumRandomAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("physarum-random");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        var settings = new Physarum3DSettings { Size = 128, AgentCount = 16384, CutAxis = 2, CutPosition = .3, StepsPerFrame = 7 };
        foreach (bool variation in new[] { false, true })
        {
            var a = new Random(84); var b = new Random(84);
            for (int i = 0; i < 200; i++)
            {
                var candidate = Physarum3DRandomizer.Candidate(settings, variation, a);
                candidate.Validate();
                Check(candidate == Physarum3DRandomizer.Candidate(settings, variation, b), "Candidate sequence is reproducible.");
                Check(candidate.Size == settings.Size && candidate.AgentCount == settings.AgentCount && candidate.StepsPerFrame == 7 && candidate.CutAxis == 2,
                    "Search preserves resolution, population and view controls.");
                if (variation) Check(candidate.Seed == settings.Seed && candidate.SeedShape == settings.SeedShape &&
                    Math.Abs(Math.Log(candidate.SensorDistance / settings.SensorDistance)) <= .30001, "Variation stays near applied parameters.");
            }
        }
        const int n = 48;
        var flat = new float[n*n*n];
        Check(Physarum3DRandomizer.Measure(flat, n).Score == 0, "Empty field is rejected.");
        Array.Fill(flat, 2f);
        Check(Physarum3DRandomizer.Measure(flat, n).Score == 0, "Uniform fog is rejected.");
        Array.Clear(flat); flat[flat.Length/2] = 20;
        Check(Physarum3DRandomizer.Measure(flat, n).Score == 0, "Isolated spike is rejected.");
        for (int z = 6; z < n-6; z++) for (int y = 6; y < n-6; y++) for (int x = 6; x < n-6; x++)
            if (x % 9 < 2 && y % 9 < 2) flat[(z*n+y)*n+x] = 5;
        Check(Physarum3DRandomizer.Measure(flat, n).Score > 0, "Extended sparse filaments pass the visual filter.");

        using var renderer = new Fractal3DRenderer();
        using var original = new Physarum3DGpuSimulation(renderer.DeviceHost, settings);
        original.Advance(80, CancellationToken.None); var shown = original.Publish(); var before = original.ReadCheckpoint(shown);
        var watch = Stopwatch.StartNew();
        var found = await Task.Run(() => Physarum3DRandomizer.Search(renderer.DeviceHost, settings, false, null, CancellationToken.None, 1729));
        Check(found is not null, "Random search finds a validated full-size network.");
        found!.Settings.Validate();
        Check(found.Checked == Physarum3DRandomizer.TrialCount && found.Settings.Field!.Size == 128 &&
            found.Settings.Field.Step == found.Settings.WarmupSteps, "Search returns the grown full-resolution field, without another warmup.");
        Check(original.ReadCheckpoint(shown).Trail.SequenceEqual(before.Trail) && original.ReadCheckpoint(shown).AgentState.SequenceEqual(before.AgentState),
            "Trials do not alter the shown network or its agents.");
        Console.WriteLine($"Random search: {watch.Elapsed.TotalSeconds:F2}s; seed {found.Settings.Seed}, shape {found.Settings.SeedShape}, exposure {found.Settings.Exposure:F3}.");
        var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Physarum3D);
        state.Physarum = found.Settings with { CutAxis = 0 };
        var image = await renderer.RenderAsync(state, 640, 640, null, CancellationToken.None);
        if (output is not null) PhysarumSavePng(image, Path.Combine(output, "random.png"));
        var near = await Task.Run(() => Physarum3DRandomizer.Search(renderer.DeviceHost, found.Settings, true, null, CancellationToken.None, 42));
        Check(near is not null && near.Settings.SeedShape == found.Settings.SeedShape && near.Settings.Seed == found.Settings.Seed, "Variation finds a nearby network with the same initial seed.");
        state.Physarum = near!.Settings with { CutAxis = 0 };
        image = await renderer.RenderAsync(state, 640, 640, null, CancellationToken.None);
        if (output is not null) PhysarumSavePng(image, Path.Combine(output, "variation.png"));
        using (var cancel = new CancellationTokenSource())
        {
            var progress = new InlinePhysarumProgress(_ => cancel.Cancel());
            bool stopped = false;
            try { await Task.Run(() => Physarum3DRandomizer.Search(renderer.DeviceHost, settings, false, progress, cancel.Token, 12)); }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "Cancellation during GPU trials cannot return a partial result.");
        }
        await VerifyPhysarumSearchWindowAsync(settings with { Field = before });
        Console.WriteLine("PASS (physarum-random): candidates, filters, GPU search/variation, exact undo, cancellation, stale load and close.");
    }

    private sealed class InlinePhysarumProgress(Action<PhysarumSearchProgress> action) : IProgress<PhysarumSearchProgress>
    {
        public void Report(PhysarumSearchProgress value) => action(value);
    }

    private static async Task VerifyPhysarumSearchWindowAsync(Physarum3DSettings settings)
    {
        var theme = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        var window = new Fractal3DWindow(Fractal3DKind.Physarum3D);
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1180,800)); root.Arrange(new Rect(0,0,1180,800)); root.UpdateLayout();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Get<T>(string name) => (T)typeof(Fractal3DWindow).GetField(name, flags)!.GetValue(window)!;
        void Click(string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Task Search(bool variation) => (Task)typeof(Fractal3DWindow).GetMethod("RunPhysarumSearchAsync", flags)!.Invoke(window, [variation])!;
        async Task Display()
        {
            var watch = Stopwatch.StartNew();
            while (Get<bool>("_physarumBusy") && watch.Elapsed.TotalSeconds < 60) await Task.Delay(10);
            Check(!Get<bool>("_physarumBusy"), "Window simulation finishes.");
            var m = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync", flags)!;
            await (Task)m.Invoke(window, [Enum.Parse(m.GetParameters()[0].ParameterType, "Full")])!;
        }
        try
        {
            await Display();
            var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Physarum3D); state.Physarum = settings;
            window.LoadState(state); await Display();
            var initial = window.CaptureState("before").Physarum;
            ((TextBox)window.FindName("PhysarumSensorBox")).Text = "invalid draft";
            await Search(true); await Display();
            Check(Get<List<Physarum3DSettings>>("_physarumSearchHistory").Count == 1, "Search uses applied parameters, despite invalid drafts.");
            Check(((Button)window.FindName("PhysarumSearchUndoButton")).IsEnabled, "Successful search enables undo.");
            Click("PhysarumSearchUndoButton"); await Display();
            var restored = window.CaptureState("undo").Physarum;
            Check(restored.Field!.Trail.SequenceEqual(initial.Field!.Trail) && restored.Field.AgentState.SequenceEqual(initial.Field.AgentState) &&
                restored.Field.Step == initial.Field.Step && restored.SensorDistance == initial.SensorDistance && restored.CutAxis == initial.CutAxis,
                "Undo restores exact shown trail, headings, time, parameters and slice.");
            var searching = Search(false);
            Check(!((Button)window.FindName("PhysarumPlayButton")).IsEnabled && ((Button)window.FindName("CancelButton")).IsEnabled, "Search disables evolution and enables general cancellation.");
            Click("CancelButton"); await searching; await Display();
            Check(window.CaptureState("cancel").Physarum.Field!.Trail.SequenceEqual(initial.Field.Trail), "Cancellation preserves shown field.");
            searching = Search(false);
            window.LoadState(state); await searching; await Display();
            Check(Get<List<Physarum3DSettings>>("_physarumSearchHistory").Count == 0 && window.CaptureState("load").Physarum.Seed == settings.Seed,
                "A loaded state wins over an older search result.");
            searching = Search(false); window.Close(); await searching;
            Check(Get<CancellationTokenSource?>("_physarumSearchCts") is null, "Closing cancels and releases search work.");
        }
        finally { window.Close(); }
    }
}
