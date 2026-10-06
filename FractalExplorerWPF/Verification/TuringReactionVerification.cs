using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyTuringReactionsAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("turing-reactions");
        string? output = args.Length > 1 ? Directory.CreateDirectory(Path.GetFullPath(args[1])).FullName : null;
        using var renderer = new Fractal3DRenderer();
        foreach (var model in Enum.GetValues<TuringReactionModel>().Where(m => m != TuringReactionModel.McCabe))
        {
            var reaction = TuringReactionSettings.Default(model);
            foreach (var boundary in Enum.GetValues<TuringBoundary>())
            {
                var state = new TuringState { GridSize = 32, WarmupSteps = 0, Reaction = reaction, Boundary = boundary, DetailSize = 2 };
                using var cpu = new TuringCpuEngine(state); using var gpu = new TuringGpuEngine(state);
                var start = cpu.Snapshot();
                var expected = ReactionOracle(start.U, start.V, 32, 2, 256, state.DetailSize, state.EvolutionRate, boundary, reaction);
                cpu.Advance(1, state, CancellationToken.None); gpu.Advance(1, state, CancellationToken.None);
                AssertReaction(expected.U, expected.V, cpu.Snapshot().U, cpu.Snapshot().V, 2e-5, $"{model} 2D CPU {boundary}");
                AssertReaction(expected.U, expected.V, gpu.Snapshot().U, gpu.Snapshot().V, 2e-5, $"{model} 2D GPU {boundary}");
                state.Checkpoint = gpu.Snapshot(); state.SaveName = model + " " + boundary;
                var store = new TuringSaveStore(); store.Save(state); var restored = store.Load().Single(s => s.SaveName == state.SaveName);
                using var continued = new TuringGpuEngine(restored);
                gpu.Advance(2, state, CancellationToken.None); continued.Advance(2, restored, CancellationToken.None);
                Check(gpu.Snapshot().U.SequenceEqual(continued.Snapshot().U) && gpu.Snapshot().V.SequenceEqual(continued.Snapshot().V), "2D disk continuation preserves both species exactly.");
                using var switchToCpu = new TuringCpuEngine(restored);
                switchToCpu.Advance(2, restored, CancellationToken.None);
                AssertReaction(gpu.Snapshot().U.Select(v => (double)v).ToArray(), gpu.Snapshot().V.Select(v => (double)v).ToArray(), switchToCpu.Snapshot().U, switchToCpu.Snapshot().V, 4e-5, "GPU to CPU continuation");
                var resized = TuringSimulation.Resize(start, 48);
                Check(resized.Model == model && resized.U.Length == 48 * 48 && resized.V.Length == resized.U.Length && resized.StepCount == start.StepCount, "2D resize retains both concentrations and time.");
                var painted = gpu.Snapshot(); gpu.Paint(.5, .5, .1, .7, TuringBrush.Light, state);
                Check(!gpu.Snapshot().U.SequenceEqual(painted.U) && gpu.Snapshot().V.SequenceEqual(painted.V), "2D brush changes U and retains V.");

                var settings = new Turing3DSettings { Size = 32, WarmupSteps = 0, DetailSize = 2, Reaction = reaction, Boundary = boundary };
                using var volume = new Turing3DGpuSimulation(renderer.DeviceHost, settings);
                var start3 = volume.ReadCurrent();
                var expected3 = ReactionOracle(start3.ConcentrationU.ToArray(), start3.ConcentrationV.ToArray(), 32, 3, 128, settings.DetailSize, settings.EvolutionRate, boundary, reaction);
                volume.Advance(1, CancellationToken.None); var field = volume.ReadCurrent();
                AssertReaction(expected3.U, expected3.V, field.ConcentrationU.ToArray(), field.ConcentrationV.ToArray(), 3e-5, $"{model} 3D {boundary}");
                var slot = volume.Publish(); volume.Advance(1, CancellationToken.None); volume.Publish(slot);
                Check(volume.ReadCheckpoint(slot).ConcentrationU.SequenceEqual(field.ConcentrationU) && volume.ReadCheckpoint(slot).ConcentrationV.SequenceEqual(field.ConcentrationV), "3D publication preserves both species of the shown frame.");
                string json = JsonSerializer.Serialize(settings with { Field = field });
                var loaded = JsonSerializer.Deserialize<Turing3DSettings>(json)!; loaded.Validate();
                using var resumed = new Turing3DGpuSimulation(renderer.DeviceHost, loaded);
                using var original = new Turing3DGpuSimulation(renderer.DeviceHost, settings with { Field = field });
                resumed.Advance(2, CancellationToken.None); original.Advance(2, CancellationToken.None);
                Check(resumed.ReadCurrent().ConcentrationU.SequenceEqual(original.ReadCurrent().ConcentrationU) && resumed.ReadCurrent().ConcentrationV.SequenceEqual(original.ReadCurrent().ConcentrationV), "3D Brotli continuation preserves both species exactly.");
                var resized3 = Turing3DField.Resize(field, 40);
                Check(resized3.Model == model && resized3.ConcentrationU.Length == 40 * 40 * 40 && resized3.Step == field.Step, "3D resize retains kinetics and time.");
                var before = volume.ReadCurrent(); volume.Paint(.5, .5, .5, .15, .6, TuringBrush.Dark);
                Check(!volume.ReadCurrent().ConcentrationU.SequenceEqual(before.ConcentrationU) && volume.ReadCurrent().ConcentrationV.SequenceEqual(before.ConcentrationV), "3D brush changes U and retains V.");
            }

            var equilibrium = reaction.Equilibrium;
            var constant = new TuringState { GridSize = 32, Reaction = reaction, WarmupSteps = 0 };
            constant.Checkpoint = new TuringCheckpoint { Size = 32, Model = model, Field = new float[1024], Scales = new byte[1024],
                U = Enumerable.Repeat((float)equilibrium.U, 1024).ToArray(), V = Enumerable.Repeat((float)equilibrium.V, 1024).ToArray() };
            using (var homogeneous = new TuringCpuEngine(constant))
            {
                homogeneous.Advance(1, constant, CancellationToken.None);
                Check(homogeneous.Snapshot().U.All(u => Math.Abs(u-equilibrium.U) < 2e-5) && homogeneous.Snapshot().V.All(v => Math.Abs(v-equilibrium.V) < 2e-5), "The reaction equilibrium stays homogeneous without field normalization.");
            }
            var symmetricVolumeSettings = new Turing3DSettings { Size = 32, WarmupSteps = 0, Reaction = reaction, Symmetry = Turing3DSymmetry.Octahedral, Mirror = true, Region = Turing3DRegion.Shell };
            using (var symmetricVolume = new Turing3DGpuSimulation(renderer.DeviceHost, symmetricVolumeSettings))
            {
                symmetricVolume.Advance(2, CancellationToken.None); var field = symmetricVolume.ReadCurrent();
                var group = Turing3DSymmetryGroup.Build(Turing3DSymmetry.Octahedral, 1, true);
                // The existing independent grid symmetry checker can inspect each concentration separately.
                var uField = new Turing3DField(32, field.Step, field.ConcentrationU.ToArray().Select(x => (float)(x/(x+equilibrium.U)*2-1)).ToArray(), new byte[32*32*32]);
                var vField = new Turing3DField(32, field.Step, field.ConcentrationV.ToArray().Select(x => (float)(x/(x+equilibrium.V)*2-1)).ToArray(), new byte[32*32*32]);
                Check(SymmetryError(uField, group) < 3e-5 && SymmetryError(vField, group) < 3e-5, "Both reaction species respect polyhedral symmetry in a shell.");
                long step = symmetricVolume.Step;
                symmetricVolume.Configure(symmetricVolumeSettings with { Reaction = reaction with { DiffusionV = reaction.DiffusionV * .9 } });
                Check(symmetricVolume.Step == step, "Changing diffusion retains 3D time.");
            }

            // Nonlinear growth, rather than a noise-only or homogeneous picture.
            var grown = new TuringState { GridSize = 128, WarmupSteps = 0, Reaction = reaction };
            using var evolution = new TuringGpuEngine(grown);
            evolution.Advance(400, grown, CancellationToken.None); var picture = evolution.Snapshot();
            grown.Checkpoint = picture; grown.Validate();
            double amplitude = picture.Field.Max() - picture.Field.Min();
            Console.WriteLine($"{model}: 2D developed amplitude {amplitude:F4}");
            Check(amplitude > .12, $"{model} must develop visible patterns.");
            if (output is not null) SaveTuringPng(System.Windows.Media.Imaging.BitmapSource.Create(512, 512, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null,
                evolution.RenderFrame(grown, 512, 512, CancellationToken.None), 2048), Path.Combine(output, model + ".png"));

            var volumeSettings = new Turing3DSettings { Size = 64, WarmupSteps = 0, Reaction = reaction };
            using (var developedVolume = new Turing3DGpuSimulation(renderer.DeviceHost, volumeSettings))
            {
                await Task.Run(() => developedVolume.Advance(400, CancellationToken.None));
                var field = developedVolume.ReadCurrent();
                double range = field.Field.ToArray().Max() - field.Field.ToArray().Min();
                Check(range > .12, model + " must develop a nonuniform 3D volume");
                var view = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Turing3D);
                view.Turing = volumeSettings with { Field = field, Live = null, CutAxis = 2, CutPosition = 0 };
                var bitmap = await renderer.RenderAsync(view, 320, 320, null, CancellationToken.None);
                Check(HasFractal3DStructure(Pixels3D(bitmap), 320, 320), model + " must render a visible 3D pattern");
                if (output is not null) SaveTuring3DPng(bitmap, Path.Combine(output, model + "-3D.png"));
                var liveView = view.Clone(); liveView.Turing = view.Turing with { Field = null, Live = developedVolume.Publish() };
                var liveBitmap = await renderer.RenderAsync(liveView, 320, 320, null, CancellationToken.None);
                Check(Pixels3D(bitmap).SequenceEqual(Pixels3D(liveBitmap)), "Live and checkpoint reaction volumes render identically.");
            }

            var symmetric = grown.Clone(includeCheckpoint: false); symmetric.GridSize = 32; symmetric.Symmetry = 4; symmetric.Mirror = true;
            using var symmetricCpu = new TuringCpuEngine(symmetric); using var symmetricGpu = new TuringGpuEngine(symmetric);
            symmetricCpu.Advance(2, symmetric, CancellationToken.None); symmetricGpu.Advance(2, symmetric, CancellationToken.None);
            AssertReaction(symmetricCpu.Snapshot().U.Select(v => (double)v).ToArray(), symmetricCpu.Snapshot().V.Select(v => (double)v).ToArray(), symmetricGpu.Snapshot().U, symmetricGpu.Snapshot().V, 3e-5, "Symmetric CPU/GPU concentrations");
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); var committed = symmetricCpu.Snapshot();
            try { symmetricCpu.Advance(100, symmetric, cancel.Token); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
            Check(committed.U.SequenceEqual(symmetricCpu.Snapshot().U) && committed.V.SequenceEqual(symmetricCpu.Snapshot().V), "Canceled steps retain both species.");
            using var canceledDuring = new CancellationTokenSource(); canceledDuring.CancelAfter(5);
            try { symmetricCpu.Advance(2000, symmetric, canceledDuring.Token); } catch (OperationCanceledException) { }
            var after = symmetricCpu.Snapshot(); var referenceState = symmetric.Clone(); referenceState.Checkpoint = committed;
            using var reference = new TuringCpuEngine(referenceState); reference.Advance((int)(after.StepCount - committed.StepCount), referenceState, CancellationToken.None);
            Check(after.U.SequenceEqual(reference.Snapshot().U) && after.V.SequenceEqual(reference.Snapshot().V), "Mid-step cancellation commits only complete reaction steps.");
            symmetricCpu.Advance(1, symmetric, CancellationToken.None); reference.Advance(1, referenceState, CancellationToken.None);
            Check(symmetricCpu.Snapshot().U.SequenceEqual(reference.Snapshot().U), "Scratch buffers are reusable after cancellation.");
        }
        // Old JSON has one field and no reaction metadata.
        var old = new TuringState { GridSize = 32 }; old.Checkpoint = new TuringSimulation(old).Snapshot();
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(old))!.AsObject(); legacy.Remove("Reaction");
        var legacyCp = legacy["Checkpoint"]!.AsObject(); legacyCp.Remove("Model"); legacyCp.Remove("U"); legacyCp.Remove("V");
        JsonSerializer.Deserialize<TuringState>(legacy.ToJsonString())!.Validate();
        var old3 = new Turing3DSettings { Size = 32, Field = new Turing3DField(32, 9, new float[32 * 32 * 32], new byte[32 * 32 * 32]) };
        var legacy3 = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(old3))!.AsObject(); legacy3.Remove("Reaction");
        JsonSerializer.Deserialize<Turing3DSettings>(legacy3.ToJsonString())!.Validate();
        await VerifyReactionEditorWindowsAsync();
        Console.WriteLine("PASS (turing-reactions): independent 2D/3D equations, boundaries, CPU/GPU, growth, symmetry, exact saves, publication, brushes, resizing, cancellation and WPF selection.");
    }

    private static void AssertReaction(double[] eu, double[] ev, float[] au, float[] av, double tolerance, string label)
    {
        double error = eu.Zip(au, (e, a) => Math.Abs(e - a)).Concat(ev.Zip(av, (e, a) => Math.Abs(e - a))).Max();
        Console.WriteLine($"{label}: maximum concentration error {error:E2}");
        Check(error < tolerance, label + " must match independent equations");
    }

    // Independent double oracle, including both species and all spatial axes; does not call production kinetics or integration.
    private static (double[] U, double[] V) ReactionOracle(float[] initialU, float[] initialV, int size, int dimensions, int referenceSize, double detail, double rate, TuringBoundary boundary, TuringReactionSettings r)
    {
        double[] u = initialU.Select(x => (double)x).ToArray(), v = initialV.Select(x => (double)x).ToArray(), nu = new double[u.Length], nv = new double[v.Length];
        double spatial = detail * size / referenceSize; spatial *= spatial;
        double limit = Math.Min(.01, .4 / (dimensions * Math.Max(r.DiffusionU, r.DiffusionV) * spatial));
        int steps = (int)Math.Ceiling(.5 * rate / limit); double dt = .5 * rate / steps;
        int Edge(int x) => boundary == TuringBoundary.Wrap ? (x + size) % size : Math.Clamp(x, 0, size - 1);
        int Index(int x, int y, int z) => (z * size + y) * size + x;
        for (int sub = 0; sub < steps; sub++)
        {
            for (int z = 0; z < (dimensions == 3 ? size : 1); z++) for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                int i = Index(x, y, z); double lu = 0, lv = 0;
                for (int axis = 0; axis < dimensions; axis++)
                {
                    int lo = axis == 0 ? Index(Edge(x - 1), y, z) : axis == 1 ? Index(x, Edge(y - 1), z) : Index(x, y, Edge(z - 1));
                    int hi = axis == 0 ? Index(Edge(x + 1), y, z) : axis == 1 ? Index(x, Edge(y + 1), z) : Index(x, y, Edge(z + 1));
                    lu += u[lo] + u[hi] - 2 * u[i]; lv += v[lo] + v[hi] - 2 * v[i];
                }
                double f, g;
                if (r.Model == TuringReactionModel.Brusselator) { f = r.A - (r.B + 1) * u[i] + u[i] * u[i] * v[i]; g = r.B * u[i] - u[i] * u[i] * v[i]; }
                else if (r.Model == TuringReactionModel.Schnakenberg) { f = r.A - u[i] + u[i] * u[i] * v[i]; g = r.B - u[i] * u[i] * v[i]; }
                else { f = r.A - u[i] + u[i] * u[i] / Math.Max(v[i], 1e-6); g = u[i] * u[i] - r.B * v[i]; }
                nu[i] = Math.Max(1e-6, u[i] + dt * (r.DiffusionU * spatial * lu + f)); nv[i] = Math.Max(1e-6, v[i] + dt * (r.DiffusionV * spatial * lv + g));
            }
            (u, nu) = (nu, u); (v, nv) = (nv, v);
        }
        return (u, v);
    }

    private static async Task VerifyReactionEditorWindowsAsync()
    {
        var theme = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(object window, string name) => (T)window.GetType().GetField(name, flags)!.GetValue(window)!;
        var window = new TuringWindow();
        try
        {
            Field<ComboBox>(Field<TuringReactionEditor>(window, "ReactionEditor"), "ModelBox").SelectedIndex = (int)TuringReactionModel.Schnakenberg;
            await WaitForWindowIdleAsync(() => Field<bool>(window, "_resetting"), TimeSpan.FromSeconds(45));
            var state = window.CaptureState("reaction");
            Check(state.Reaction.Model == TuringReactionModel.Schnakenberg && state.Checkpoint!.U.Length > 0 && Field<StackPanel>(window, "McCabeDepthPanel").Visibility == Visibility.Collapsed, "2D model selection uses two-species evolution and hides McCabe controls.");
        }
        finally { window.Close(); }
        var volume = new Fractal3DWindow(Fractal3DKind.Turing3D);
        try
        {
            Field<ComboBox>(Field<TuringReactionEditor>(volume, "TuringReactionEditor"), "ModelBox").SelectedIndex = (int)TuringReactionModel.Brusselator;
            await WaitForWindowIdleAsync(() => Field<bool>(volume, "_turingBusy"), TimeSpan.FromSeconds(45));
            Check(Field<Turing3DSettings>(volume, "_turingSettings").Reaction.Model == TuringReactionModel.Brusselator && Field<StackPanel>(volume, "TuringMcCabePanel").Visibility == Visibility.Collapsed, "3D model selection uses the existing Turing panel.");
        }
        finally { volume.Close(); }
    }

    private static async Task WaitForWindowIdleAsync(Func<bool> busy, TimeSpan timeout)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (busy()) { if (watch.Elapsed > timeout) throw new TimeoutException("Reaction window preparation timed out"); await Task.Delay(30); }
    }
}
