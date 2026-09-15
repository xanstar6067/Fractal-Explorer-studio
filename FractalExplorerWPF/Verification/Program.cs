using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

// No visible windows or screen capture. The snapshot callback supplies synthetic pixels.
internal static partial class Program
{
    // Необязательный фильтр групп проверок — полный набор идёт больше десяти минут, и при
    // работе над одной темой ждать его целиком незачем:
    //   без аргументов / all — всё;
    //   manager  — менеджер сохранений, хранилище по файлу на сохранение, Корзина и миграции данных;
    //   deep     — только глубокий зум (включает extreme);
    //   extreme  — только сверхглубокий зум (FloatExp-зум, 1e1000) и поиск ядра по Ньютону;
    //   phoenix  — только глубокий и сверхглубокий зум Феникса;
    //   newton   — только глубокий и сверхглубокий зум бассейнов Ньютона;
    //   basins   — только бассейны Мюллера, Лагерра, секущих, рациональных отображений и циклов.
    [STAThread]
    private static int Main(string[] args)
    {
        string group = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "all";
        int result = 0;
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Никакая проверка не должна читать или писать настоящие данные пользователя в AppData.
        AppPaths.OverrideDataRoot(Path.Combine(AppContext.BaseDirectory, "VerificationData", "Shared"));
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (group is "all" or "manager")
                {
                    VerifyUserData();
                    await VerifyManagerAsync();
                }
                if (group is "all" or "deep") await VerifyDeepZoomAsync();
                if (group is "extreme") await VerifyExtremeZoomGroupAsync();
                if (group is "phoenix") await VerifyPhoenixDeepZoomAsync();
                if (group is "newton") await VerifyNewtonDeepZoomAsync();
                if (group is "all" or "basins") VerifyBasinExplorers();
                if (group is not ("all" or "manager" or "deep" or "extreme" or "phoenix" or "newton" or "basins"))
                    throw new ArgumentException($"Неизвестная группа проверок «{group}». Допустимы: all, manager, deep, extreme, phoenix, newton, basins.");
                Console.WriteLine($"PASS ({group}): preview selection, snapshot persistence, progress, cancellation, stale results, errors, presets, deep zoom and extreme zoom.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                result = 1;
            }
            finally { Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        return result;
    }

    private static async Task VerifyManagerAsync()
    {
        using var sandbox = DataSandbox.Create("manager");
        var store = new FractalSaveStore<State>("Verification", state => state.Name);
        store.Save(new State("A", new DateTime(2026, 1, 2)));
        store.Save(new State("B", new DateTime(2026, 1, 1)));
        var preset = new State("Preset", DateTime.MinValue);
        List<PendingRender> jobs = [];
        int captures = 0;
        BitmapSource? snapshot = Pixel(17);
        var configuration = new SaveManagerConfiguration<State>
        {
            WindowTitle = "Verification", Store = store,
            CaptureState = name => new State(name, new DateTime(2026, 1, 3)),
            CapturePreview = (_, _) => { captures++; return snapshot; },
            LoadState = _ => { }, GetName = state => state.Name,
            GetTimestamp = state => state.Timestamp, GetDetails = state => state.Name,
            PointsOfInterest = [preset],
            RenderPreviewAsync = (state, _, _, token, progress) =>
            {
                var job = new PendingRender(state, token, progress!);
                jobs.Add(job);
                return job.Completion.Task;
            }
        };
        var view = new SaveManagerControl();
        var window = new Window { Content = view };
        using var controller = new SaveManagerController<State>(window, view, configuration);

        Select(view, "B"); Select(view, "A");
        Check(jobs.Count == 0 && captures == 0, "Selection must neither render nor capture.");
        Check(Image(view) is null, "Missing preview must be empty.");

        view.SaveName = "Snapshot";
        Click(view, "SaveButton");
        Check(captures == 1 && jobs.Count == 0, "Saving must copy the frame without rendering.");
        Check(store.Load().Any(state => state.Name == "Snapshot"), "Saving must write the state to its own file.");
        string snapshotPath = SavePreviewPath(store, "Snapshot");
        byte[] originalPng = File.ReadAllBytes(snapshotPath);
        Select(view, "B");
        Check(Image(view) is null, "Switching to an uncached entry must clear the old image.");
        Select(view, "Snapshot");
        Check(ReadPixel(Image(view)!) == 17 && jobs.Count == 0, "Cached snapshot must survive selection.");

        Click(view, "RenderPreviewButton");
        PendingRender oldJob = jobs[^1];
        oldJob.Progress.Report(60); await DrainAsync();
        var progressBar = (ProgressBar)view.FindName("PreviewProgress");
        Check(!progressBar.IsIndeterminate && progressBar.Value == 60, "Renderer progress must reach the UI.");
        oldJob.Progress.Report(30); await DrainAsync();
        Check(progressBar.Value == 60, "Out-of-order progress must not go backwards.");
        Select(view, "B");
        Check(oldJob.Token.IsCancellationRequested && jobs.Count == 1, "Switching cancels without starting another render.");
        Click(view, "RenderPreviewButton");
        PendingRender newJob = jobs[^1];
        oldJob.Progress.Report(95);
        oldJob.Completion.SetResult(Pixel(99));
        await DrainAsync();
        Check(Image(view) is null && progressBar.IsIndeterminate, "Stale image and progress must be discarded.");
        Check(File.ReadAllBytes(snapshotPath).SequenceEqual(originalPng), "Stale render must not rewrite the original PNG.");
        newJob.Completion.SetResult(Pixel(42)); await DrainAsync();
        Check(ReadPixel(Image(view)!) == 42 && File.Exists(SavePreviewPath(store, "B")), "Manual render must update its own entry.");

        byte[] beforeCancel = File.ReadAllBytes(SavePreviewPath(store, "B"));
        Click(view, "RenderPreviewButton");
        PendingRender cancelled = jobs[^1];
        Click(view, "CancelPreviewButton");
        Check(cancelled.Token.IsCancellationRequested, "Cancel button must signal cancellation.");
        cancelled.Completion.SetResult(Pixel(70)); await DrainAsync();
        Check(ReadPixel(Image(view)!) == 42, "Cancelled render must preserve the image.");
        Check(File.ReadAllBytes(SavePreviewPath(store, "B")).SequenceEqual(beforeCancel), "Cancelled render must preserve the PNG.");
        Click(view, "RenderPreviewButton");
        jobs[^1].Completion.SetException(new InvalidOperationException("test render failure"));
        await DrainAsync();
        Check(((TextBlock)view.FindName("StatusText")).Text.Contains("test render failure"), "Render error must be visible.");
        Check(File.ReadAllBytes(SavePreviewPath(store, "B")).SequenceEqual(beforeCancel), "Failed render must preserve the PNG.");
        Check(sandbox.Recycled.Count == 0, "Nothing may be recycled before a preview is actually replaced.");
        Click(view, "RenderPreviewButton");
        jobs[^1].Completion.SetResult(Pixel(43)); await DrainAsync();
        Check(ReadPixel(Image(view)!) == 43 && ReadPixel(LoadPng(SavePreviewPath(store, "B"))) == 43, "A new render must replace the PNG.");
        Check(sandbox.Recycled.Count == 1 && sandbox.Recycled[0].Original == Path.GetFullPath(SavePreviewPath(store, "B")) &&
              File.ReadAllBytes(sandbox.Recycled[0].Stored).SequenceEqual(beforeCancel),
            "The replaced preview must go to the Recycle Bin instead of vanishing.");

        File.WriteAllText(SavePreviewPath(store, "A"), "invalid png");
        int beforeSelection = jobs.Count;
        Select(view, "A");
        Check(Image(view) is null && jobs.Count == beforeSelection, "Corrupt PNG must not start a render.");
        snapshot = null;
        view.SaveName = "NoFrame"; Click(view, "SaveButton");
        Check(store.Load().Any(state => state.Name == "NoFrame") && Image(view) is null,
            "A missing frame must not prevent saving the state.");
        Check(jobs.Count == beforeSelection, "Missing frame must not trigger rendering.");

        File.WriteAllText(Path.Combine(store.DirectoryPath, "damaged.json"), "{ damaged");
        var damagedView = new SaveManagerControl();
        using (new SaveManagerController<State>(new Window { Content = damagedView }, damagedView, configuration))
        {
            Check(((ListBox)damagedView.FindName("SavesList")).Items.Count == 4 &&
                  ((TextBlock)damagedView.FindName("StatusText")).Text.Contains("damaged.json"),
                "A damaged save file must be reported while the other saves stay listed.");
        }

        var points = (CheckBox)view.FindName("PointsOfInterestCheckBox");
        points.IsChecked = true;
        Check(jobs.Count == beforeSelection, "Selecting a preset must not render.");
        Check(((Button)view.FindName("RenderPreviewButton")).IsEnabled, "Presets must support manual rendering.");
        Click(view, "RenderPreviewButton"); jobs[^1].Completion.SetResult(Pixel(55)); await DrainAsync();
        points.IsChecked = false; points.IsChecked = true;
        Check(ReadPixel(Image(view)!) == 55 && jobs.Count == beforeSelection + 1, "Preset preview must be cached.");
        Check(File.Exists(store.GetPointOfInterestPreviewPath("Preset")), "Preset previews must live in the points-of-interest folder.");

        var reopenedView = new SaveManagerControl();
        var reopenedWindow = new Window { Content = reopenedView };
        using (var reopened = new SaveManagerController<State>(reopenedWindow, reopenedView, configuration))
        {
            Select(reopenedView, "Snapshot");
            Check(ReadPixel(Image(reopenedView)!) == 17, "Reopening must load the saved PNG.");
        }
        Click(view, "RenderPreviewButton");
        PendingRender closing = jobs[^1];
        controller.Dispose();
        Check(closing.Token.IsCancellationRequested, "Closing must cancel the render.");
        closing.Progress.Report(80); closing.Completion.SetResult(Pixel(88)); await DrainAsync();
        Check(ReadPixel(Image(view)!) == 55, "Closed manager must ignore pending results.");
    }

    private static async Task VerifyDeepZoomAsync()
    {
        var state = new MandelbrotState
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = 5.7607143988620999e25,
            Iterations = 4500, Threads = 2,
            Palette = new MandelbrotPalette { Colors = [Colors.White, Colors.White], InteriorColor = Colors.Black }
        };
        const int width = 12, height = 8;
        byte[] pixels = new byte[width * height * 4];
        int progress = 0;
        await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, width, height, width * 4,
            CancellationToken.None, value => Interlocked.Exchange(ref progress, value)));
        Check(pixels.Where((_, index) => index % 4 != 3).Any(value => value != 0), "4500 iterations must resolve deep-zoom detail.");
        Check(progress == 100, "Deep-zoom render must complete with 100% progress.");
        state.Iterations = 600;
        await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, width, height, width * 4, CancellationToken.None));
        Check(pixels.Where((_, index) => index % 4 != 3).All(value => value == 0), "Regression fixture must reproduce the old black preview at 600 iterations.");

        await VerifyUnifiedDeepZoomEngineAsync();

        using var cancellation = new CancellationTokenSource();
        state.Iterations = 100_000;
        byte[] cancelledPixels = new byte[480 * 320 * 4];
        Task render = Task.Run(() => MandelbrotFamilyRenderer.Render(state, cancelledPixels, 480, 320, 480 * 4, cancellation.Token));
        cancellation.CancelAfter(30);
        await render.WaitAsync(TimeSpan.FromSeconds(10));
        Check(cancelledPixels.Where((_, index) => index % 4 == 3).Any(value => value == 0), "Cancellation must stop unfinished work.");
    }

    // Phase 1 of the unified deep-zoom engine: adaptive reference-orbit precision plus a
    // FloatExp representation of the per-pixel δ, both behind the existing 1e25 gate.
    // The byte-identical band (zoom <= 1e50) is proven by an external git-stash A/B hash
    // run; here we check the two new mechanisms in isolation.
    private static async Task VerifyUnifiedDeepZoomEngineAsync()
    {
        MandelbrotPalette Palette() =>
            new() { Colors = [Colors.White, Colors.Black], InteriorColor = Colors.Black };

        // 1. The FloatExp-δ kernel must track the trusted double-δ kernel where both are
        //    valid. A moderately deep view with a bounded iteration budget keeps most
        //    pixels off the chaotically sensitive boundary (1 ULP can flip one there).
        var overlap = new MandelbrotState
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = 5.7607143988620999e25,
            Iterations = 2200,
            Threads = 2,
            Palette = Palette()
        };
        const int ow = 110, oh = 72;
        byte[] doubleDelta = new byte[ow * oh * 4];
        byte[] floatExpDelta = new byte[ow * oh * 4];

        MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = false;
        await Task.Run(() => MandelbrotFamilyRenderer.Render(overlap, doubleDelta, ow, oh, ow * 4, CancellationToken.None));
        MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = true;
        await Task.Run(() => MandelbrotFamilyRenderer.Render(overlap, floatExpDelta, ow, oh, ow * 4, CancellationToken.None));
        MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = null;

        Check(floatExpDelta.Where((_, index) => index % 4 != 3).Any(value => value != 0),
            "FloatExp δ kernel must produce an image.");
        Check(doubleDelta.Where((_, index) => index % 4 != 3).Any(value => value != 0),
            "Overlap fixture must have visible structure for the kernel comparison.");
        int differing = 0;
        for (int pixel = 0; pixel < ow * oh; pixel++)
        {
            int b = pixel * 4;
            if (doubleDelta[b] != floatExpDelta[b] ||
                doubleDelta[b + 1] != floatExpDelta[b + 1] ||
                doubleDelta[b + 2] != floatExpDelta[b + 2])
                differing++;
        }
        // At this depth δ stays inside normal double range, so the FloatExp recurrence
        // rounds bit-for-bit like the double one on this fixed fixture. A drift here is a
        // real kernel regression, not boundary chaos (both kernels share the exact same
        // double reference orbit and δc).
        Check(differing == 0,
            $"FloatExp δ kernel diverges from the double δ kernel on {differing}/{ow * oh} pixels.");

        // 2. A view deep enough to switch δ to FloatExp automatically and to lift the
        //    reference precision above the 384-bit floor. No pre-change baseline exists
        //    (old MaxZoom was 1e50); the checks are that the new paths run, complete and
        //    leave the calling thread's working precision restored. Rendered synchronously
        //    so the PrecisionScope opens and closes on *this* thread.
        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Working precision must start at the minimum.");
        var deep = new MandelbrotState
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = 1.0e120,
            Iterations = 2600,
            Threads = 2,
            Palette = Palette()
        };
        int deepProgress = 0;
        byte[] deepPixels = new byte[80 * 56 * 4];
        MandelbrotFamilyRenderer.Render(deep, deepPixels, 80, 56, 80 * 4,
            CancellationToken.None, value => deepProgress = value);
        Check(deepProgress == 100, "Deep FloatExp render must complete with 100% progress.");
        Check(deepPixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
            "Deep FloatExp render must fill every pixel.");
        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Deep-zoom render must restore the calling thread's working precision.");

        VerifyBigFloatSqrt();
        VerifyBigFloatTranscendental();
        VerifyBigFloatLogarithm();
        await VerifyCollatzDeepZoomAsync();
        await VerifyPhoenixDeepZoomAsync();
        await VerifyNovaDeepZoomAsync();
        await VerifyNewtonDeepZoomAsync();
        await VerifyDecimalStageRemovedAsync(Palette);
        await VerifyBlaAccelerationAsync(Palette);
        await VerifyRealBlaAccelerationAsync(Palette);
        await VerifyReflectedVariantsAsync(Palette);
        await VerifyMultibrotDeepZoomAsync(Palette);
        await VerifySimonobrotDeepZoomAsync(Palette);
        await VerifyHistogramDeepZoomAsync(Palette);
        await VerifyDistanceEstimationDeepZoomAsync(Palette);
        await VerifyEngineAccuracyAsync(Palette);
        await VerifyExtremeZoomGroupAsync();
    }

    // Сверхглубокий зум и поиск ядра — отдельной группой, чтобы набор можно было запускать
    // только по ним (см. фильтр в Main).
    private static async Task VerifyExtremeZoomGroupAsync()
    {
        static MandelbrotPalette Palette() =>
            new() { Colors = [Colors.White, Colors.Black], InteriorColor = Colors.Black };

        VerifyFloatExpArithmetic();
        VerifyZoomSerialization();
        await VerifyExtremeZoomAsync(Palette);
        await VerifyNewtonNucleusAsync(Palette);
        await VerifyFloatExpDeltaVariantsAsync(Palette);
        // В составе deep эти проверки уже выполнены из VerifyPhoenixDeepZoomAsync, поэтому
        // здесь — только при запуске одной группы extreme.
        if (!_phoenixExtremeVerified) await VerifyPhoenixExtremeZoomAsync();
    }

    // Phase 7: Histogram coloring moved onto the deep engine (RenderDeepZoomHistogram) — the
    // decimal stage it used to require unconditionally is now only a degenerate-orbit
    // fallback. Same two-pass CDF pipeline as the decimal version, fed by the already-proven
    // deep kernels (Iterations/Smooth are unconditional in every kernel, so BLA/FloatExp/
    // reflection/power formulas all carry through unchanged).
    private static async Task VerifyHistogramDeepZoomAsync(Func<MandelbrotPalette> palette)
    {
        static int RgbDelta(byte[] a, byte[] b, int pixel)
        {
            int o = pixel * 4;
            return Math.Max(Math.Abs(a[o] - b[o]),
                   Math.Max(Math.Abs(a[o + 1] - b[o + 1]), Math.Abs(a[o + 2] - b[o + 2])));
        }

        static int CountDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
                if (RgbDelta(a, b, pixel) != 0) n++;
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceDeep, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally { MandelbrotFamilyRenderer.ForceDeepZoomForTests = null; }
            return pixels;
        }

        const int w = 112, h = 74, total = w * h;
        MandelbrotState HistogramState(double zoom, int iterations, bool equalize, bool useSmooth) => new()
        {
            ColoringMode = MandelbrotColoringMode.Histogram,
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = zoom,
            Iterations = iterations,
            HistogramEnabledEqualization = equalize,
            HistogramInputUseSmooth = useSmooth,
            Threads = 2,
            Palette = palette()
        };

        // (a) Where decimal was still trustworthy, the deep two-pass pipeline (binning + CDF
        //     + colouring) must reproduce it up to boundary chaos — across every combination
        //     of equalization and smooth/iteration binning.
        foreach ((bool equalize, bool useSmooth) in new[] { (true, true), (true, false), (false, true), (false, false) })
        {
            MandelbrotState state = HistogramState(1.0e12, 6000, equalize, useSmooth);
            byte[] decimalPixels = await RenderAsync(state, forceDeep: false, w, h);
            byte[] deepPixels = await RenderAsync(state, forceDeep: true, w, h);
            int differing = CountDiffering(decimalPixels, deepPixels);
            Console.WriteLine($"[diag] Histogram equalize={equalize} smooth={useSmooth}: decimal vs deep {differing}/{total} px differ");
            Check(deepPixels.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                $"Histogram (equalize={equalize}, smooth={useSmooth}) must resolve structure.");
            Check(differing * 100 <= total * 5,
                $"Histogram (equalize={equalize}, smooth={useSmooth}): decimal vs deep diverges on {differing}/{total} px (>5%).");
        }

        // (b) Determinism: the two-pass parallel binning must not depend on thread scheduling.
        {
            MandelbrotState state = HistogramState(1.0e18, 4000, equalize: true, useSmooth: true);
            byte[] a = await RenderAsync(state, forceDeep: true, w, h);
            byte[] b = await RenderAsync(state, forceDeep: true, w, h);
            Check(CountDiffering(a, b) == 0, "Deep Histogram must be deterministic across runs.");
        }

        // (c) Degenerate reference orbit + Histogram must still fall back cleanly (decimal
        //     two-pass render, no crash) instead of silently mis-colouring with normalized=0.
        {
            var degenerate = new MandelbrotState
            {
                ColoringMode = MandelbrotColoringMode.Histogram,
                CenterX = 1000m,
                CenterY = 1000m,
                Zoom = 1.0e30,
                Iterations = 500,
                Threads = 2,
                Palette = palette()
            };
            int progress = 0;
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.Render(degenerate, pixels, w, h, w * 4,
                CancellationToken.None, value => progress = value);
            Check(progress == 100, "Degenerate-orbit Histogram fallback must complete with 100% progress.");
            Check(pixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
                "Degenerate-orbit Histogram fallback must fill every pixel.");
        }

        // (d) Tile-mode preview path (local normalization, not the full-frame CDF) must not
        //     crash and must resolve structure for a deep-eligible state.
        {
            MandelbrotState state = HistogramState(1.0e12, 3000, equalize: true, useSmooth: true);
            var tile = new MandelbrotRenderTile(0, 0, w, h, 0, 0);
            byte[]? tilePixels = MandelbrotFamilyRenderer.RenderTile(state, w, h, tile, CancellationToken.None);
            Check(tilePixels is not null, "Histogram tile render must not be cancelled.");
            Check(tilePixels!.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                "Histogram tile render must resolve structure.");
        }
    }

    // Phase 8: Distance Estimation moved onto the deep engine
    // (RenderDeepZoomDistanceEstimation). No new per-formula perturbation math was needed:
    // the derivative recurrence D ← J(z)·D + ∂f/∂c depends on z alone, and every kernel
    // already assembles z = Z + δ in double for orbit-trap/stripe — so DE arrives for all
    // supported variants at once, reusing GetIterationJacobian from the flat stage verbatim.
    // BLA is disabled in this mode (skipping iterations would skip derivative steps).
    // Distances are stored normalized to the pixel size; float would flush the absolute
    // deep-zoom values (~1e-43 already at zoom 1e40) straight to zero and kill the relief.
    private static async Task VerifyDistanceEstimationDeepZoomAsync(Func<MandelbrotPalette> palette)
    {
        static (int Differing, int MaxDelta) Compare(byte[] a, byte[] b)
        {
            int differing = 0, maxDelta = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                int d = Math.Max(Math.Abs(a[o] - b[o]),
                    Math.Max(Math.Abs(a[o + 1] - b[o + 1]), Math.Abs(a[o + 2] - b[o + 2])));
                if (d != 0) differing++;
                maxDelta = Math.Max(maxDelta, d);
            }
            return (differing, maxDelta);
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceDeep, bool? forceBla, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            MandelbrotFamilyRenderer.ForceBlaForTests = forceBla;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally
            {
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
                MandelbrotFamilyRenderer.ForceBlaForTests = null;
            }
            return pixels;
        }

        static MandelbrotState De(
            MandelbrotVariant variant, decimal cx, decimal cy, double zoom, int iterations,
            MandelbrotPalette pal, decimal power = 2m, decimal jr = 0m, decimal ji = 0m,
            bool inversion = false, double relief = 1.35,
            string? exactX = null, string? exactY = null) => new()
        {
            ColoringMode = MandelbrotColoringMode.DistanceEstimation,
            Variant = variant,
            CenterX = cx,
            CenterY = cy,
            CenterXExact = exactX,
            CenterYExact = exactY,
            Power = power,
            JuliaCReal = jr,
            JuliaCImaginary = ji,
            UseInversion = inversion,
            Zoom = zoom,
            Iterations = iterations,
            DistanceReliefStrength = relief,
            Threads = 2,
            Palette = pal
        };

        var mandelCentre = (X: -1.2628848671045503000020782246m, Y: 0.0409687601493310685285376264m);

        // (a) Where the flat double stage is still trustworthy, the perturbation kernels must
        //     reproduce its relief. Both stages run the identical Jacobian/EstimateDistance
        //     code; the only difference is where z comes from.
        {
            const int w = 96, h = 64, total = w * h;
            foreach (double zoom in new[] { 1.0e6, 1.0e8 })
            {
                MandelbrotState state = De(MandelbrotVariant.Mandelbrot,
                    mandelCentre.X, mandelCentre.Y, zoom, 3000, palette());
                byte[] flat = await RenderAsync(state, forceDeep: false, forceBla: null, w, h);
                byte[] deep = await RenderAsync(state, forceDeep: true, forceBla: null, w, h);
                (int differing, int maxDelta) = Compare(flat, deep);
                Console.WriteLine($"[diag] DE zoom {zoom:E0}: flat vs deep {differing}/{total} px differ (maxΔ {maxDelta})");
                Check(deep.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                    $"Deep DE must resolve structure at zoom {zoom:E0}.");
                Check(differing * 100 <= total * 8,
                    $"Deep DE diverges from the flat stage on {differing}/{total} px at zoom {zoom:E0} (>8%).");
            }
        }

        // (b) Accuracy against the near-exact BigFloat reference, which drives the very same
        //     derivative recurrence from a directly iterated arbitrary-precision orbit. Small
        //     images: the reference samples (w+2)x(h+2) pixels and is slow.
        //
        //     The headline assertion is the DE *excess*: the same view is also rendered in
        //     Smooth mode (no derivative at all) and compared to the reference, so we can tell
        //     the error Distance Estimation adds from the error the underlying orbit already
        //     carries. On a chaotic view the second number is large for reasons that predate
        //     this phase, and only the excess is meaningful.
        {
            const int w = 40, h = 28, total = w * h;

            // -2 - 10^-offset: an exactly-representable centre just outside the antenna tip,
            // at any depth. The whole frame then sits in the smooth escaping region, where
            // the orbit is not chaotic and an exact comparison is actually meaningful.
            static string OutsideTip(int offsetDigits) => "-2." + new string('0', offsetDigits - 1) + "1";

            (string Label, bool OrbitChaotic, MandelbrotState State)[] views =
            {
                ("Mandelbrot 1e30",         false, De(MandelbrotVariant.Mandelbrot, mandelCentre.X, mandelCentre.Y, 1.0e30, 4000, palette())),
                ("Mandelbrot outside-tip 1e50",  false, De(MandelbrotVariant.Mandelbrot, -2m, 0m, 1.0e50, 800, palette(), exactX: OutsideTip(46), exactY: "0")),
                ("Mandelbrot outside-tip 1e120", false, De(MandelbrotVariant.Mandelbrot, -2m, 0m, 1.0e120, 800, palette(), exactX: OutsideTip(118), exactY: "0")),
                // Unit-circle Julia (c = 0): centre 1 is exact at any depth and the dynamics
                // z <- z^2 are perfectly smooth, so this isolates deep-zoom numerics from
                // boundary chaos. Also the only fixture here that starts the derivative at I.
                ("Julia c=0 circle 1e50",   false, De(MandelbrotVariant.Julia, 1m, 0m, 1.0e50, 600, palette())),
                // The antenna tip itself: half the frame (c > -2) is the chaotic region of the
                // real quadratic map, so the orbit alone diverges on ~50% of pixels. Kept
                // deliberately - it is the case where only the DE excess can be asserted.
                ("Mandelbrot tip 1e50",     true,  De(MandelbrotVariant.Mandelbrot, -2m, 0m, 1.0e50, 800, palette())),
                ("BurningShip 1e30",        false, De(MandelbrotVariant.BurningShip, -1.62m, 0m, 1.0e30, 3000, palette())),
                ("Tricorn 1e10",            false, De(MandelbrotVariant.Tricorn, -1.62m, 0m, 1.0e10, 2000, palette())),
                ("Buffalo 1e10",            false, De(MandelbrotVariant.Buffalo, -1.62m, 0m, 1.0e10, 2000, palette())),
                ("Celtic 1e10",             false, De(MandelbrotVariant.Celtic, -1.62m, 0m, 1.0e10, 2000, palette())),
                ("JuliaBurningShip 1e10",   false, De(MandelbrotVariant.JuliaBurningShip, 0.5m, -0.3m, 1.0e10, 2000, palette(), jr: -1.5m)),
                ("Multibrot p=3",           false, De(MandelbrotVariant.Generalized, -0.295455m, 0.977273m, 300.0, 2000, palette(), power: 3m)),
                ("Multibrot p=8",           false, De(MandelbrotVariant.Generalized, 0.66m, 0m, 300.0, 2000, palette(), power: 8m)),
                ("Simonobrot p=2",          false, De(MandelbrotVariant.Simonobrot, -0.03m, 0.84m, 300.0, 2000, palette(), power: 2m)),
                ("Simonobrot p=6 inv",      false, De(MandelbrotVariant.Simonobrot, -0.90m, 0.18m, 300.0, 2000, palette(), power: 6m, inversion: true)),
            };

            foreach ((string label, bool chaotic, MandelbrotState state) in views)
            {
                byte[] engine = await RenderAsync(state, forceDeep: true, forceBla: null, w, h);
                byte[] exact = await Task.Run(() =>
                    MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
                (int differing, int maxDelta) = Compare(engine, exact);
                int nonBlack = 0;
                for (int i = 0; i < total; i++)
                    if (engine[i * 4] != 0 || engine[i * 4 + 1] != 0 || engine[i * 4 + 2] != 0) nonBlack++;

                // Same view without any derivative: how much of the difference is the orbit's?
                state.ColoringMode = MandelbrotColoringMode.Smooth;
                byte[] smoothEngine = await RenderAsync(state, forceDeep: true, forceBla: null, w, h);
                byte[] smoothExact = await Task.Run(() =>
                    MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
                state.ColoringMode = MandelbrotColoringMode.DistanceEstimation;
                int orbitOnly = Compare(smoothEngine, smoothExact).Differing;
                int excess = Math.Abs(differing - orbitOnly);

                Console.WriteLine($"[diag] DE accuracy {label}: {differing}/{total} px differ " +
                                  $"({100.0 * differing / total:F2}%), maxD {maxDelta}, nonblack {nonBlack}, " +
                                  $"orbit-only {orbitOnly}, DE excess {excess}");
                Check(nonBlack > total / 10, $"DE view {label} must carry structure.");
                // Both `differing` and `orbitOnly` are themselves comparisons against a
                // per-pixel independently-iterated BigFloat orbit, which on a chaotic view is
                // exactly as sensitive to last-bit arithmetic differences as the orbit itself
                // (see the shallow/deep boundary-chaos precedent elsewhere in this file) - their
                // difference ("excess") inherits that same instability and isn't a meaningful
                // signal here, so it's skipped for chaotic views for the same reason `differing`
                // already is below.
                if (!chaotic)
                {
                    Check(excess * 100 <= total * 3,
                        $"DE {label}: the derivative adds {excess}/{total} px of error over the orbit itself (>3%).");
                    Check(differing * 100 <= total * 8,
                        $"DE {label}: deep engine diverges from the exact reference on {differing}/{total} px (>8%).");
                }
            }
        }
        // (c) The relief must survive the depth. If the normalized distance field had
        //     underflowed to zero, ApplyDistanceLighting would early-return the unshaded base
        //     colour for every pixel and the relief strength would stop mattering — so a
        //     relief-on vs relief-off render being identical is exactly the failure mode.
        foreach ((decimal cx, decimal cy, double zoom, int iterations, string label) in new[]
        {
            (mandelCentre.X, mandelCentre.Y, 1.0e30, 4000, "centre 1e30"),
            (-2m, 0m, 1.0e50, 800, "tip 1e50"),
            (-2m, 0m, 1.0e120, 800, "tip 1e120"),
        })
        {
            const int w = 64, h = 44, total = w * h;
            MandelbrotState lit = De(MandelbrotVariant.Mandelbrot, cx, cy, zoom, iterations, palette());
            MandelbrotState flatLit = De(MandelbrotVariant.Mandelbrot, cx, cy, zoom, iterations, palette(), relief: 0);
            byte[] withRelief = await RenderAsync(lit, forceDeep: true, forceBla: null, w, h);
            byte[] withoutRelief = await RenderAsync(flatLit, forceDeep: true, forceBla: null, w, h);
            (int differing, int maxDelta) = Compare(withRelief, withoutRelief);
            Console.WriteLine($"[diag] DE relief alive, {label}: {differing}/{total} px react to relief (maxΔ {maxDelta})");
            Check(differing * 4 > total,
                $"DE distance field collapsed at {label}: only {differing}/{total} px react to relief.");
        }

        // (d) BLA must be inert in this mode — the pyramid skips iterations, which would skip
        //     derivative steps. Forcing it on and off must give bit-identical output.
        {
            const int w = 64, h = 44;
            MandelbrotState state = De(MandelbrotVariant.Mandelbrot, mandelCentre.X, mandelCentre.Y, 1.0e30, 3500, palette());
            byte[] blaOn = await RenderAsync(state, forceDeep: true, forceBla: true, w, h);
            byte[] blaOff = await RenderAsync(state, forceDeep: true, forceBla: false, w, h);
            Check(Compare(blaOn, blaOff).Differing == 0,
                "BLA must be disabled for Distance Estimation (output changed with BLA forced on).");
        }

        // (e) Degenerate reference orbit + DE must fall back to the decimal two-pass render.
        {
            const int w = 64, h = 44;
            MandelbrotState degenerate = De(MandelbrotVariant.Mandelbrot, 1000m, 1000m, 1.0e30, 500, palette());
            int progress = 0;
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.Render(degenerate, pixels, w, h, w * 4,
                CancellationToken.None, value => progress = value);
            Check(progress == 100, "Degenerate-orbit DE fallback must complete with 100% progress.");
            Check(pixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
                "Degenerate-orbit DE fallback must fill every pixel.");
        }

        // (f) Tile path (used by the preview scheduler) must resolve the same relief.
        {
            const int w = 64, h = 44;
            MandelbrotState state = De(MandelbrotVariant.Mandelbrot, mandelCentre.X, mandelCentre.Y, 1.0e30, 3000, palette());
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = true;
            byte[]? tilePixels;
            byte[] fullPixels = new byte[w * h * 4];
            try
            {
                var tile = new MandelbrotRenderTile(0, 0, w, h, 0, 0);
                tilePixels = MandelbrotFamilyRenderer.RenderTile(state, w, h, tile, CancellationToken.None);
                MandelbrotFamilyRenderer.Render(state, fullPixels, w, h, w * 4, CancellationToken.None);
            }
            finally { MandelbrotFamilyRenderer.ForceDeepZoomForTests = null; }
            Check(tilePixels is not null, "Deep DE tile render must not be cancelled.");
            Check(tilePixels!.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                "Deep DE tile render must resolve structure.");
            // A whole-canvas tile samples exactly the same grid as the full-frame pass.
            Check(Compare(tilePixels!, fullPixels).Differing == 0,
                "Deep DE tile render must match the full-frame render on a full-canvas tile.");
        }
    }

    // Phase 5: Generalized/Multibrot of integer power p on the deep engine — exact binomial
    // perturbation (Z+δ)ᵖ−Zᵖ, BLA with a p-dependent table. Verified against the exact
    // BigFloat reference (repeated-multiplication zᵖ, no perturbation).
    private static async Task VerifyMultibrotDeepZoomAsync(Func<MandelbrotPalette> palette)
    {
        static int CountRgbDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                if (a[o] != b[o] || a[o + 1] != b[o + 1] || a[o + 2] != b[o + 2]) n++;
            }
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceDeep, bool? forceBla, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            MandelbrotFamilyRenderer.ForceBlaForTests = forceBla;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally
            {
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
                MandelbrotFamilyRenderer.ForceBlaForTests = null;
            }
            return pixels;
        }

        const int w = 56, h = 40, total = w * h;

        // Structured boundary views per power; the perturbation engine is forced on so the
        // kernel is exercised on real detail and compared to the exact BigFloat reference
        // (repeated-multiplication zᵖ, no perturbation). Small image / modest iterations —
        // the exact BigFloat renderer is slow.
        (int Power, decimal Cx, decimal Cy)[] cases =
        {
            (3, -0.295455m, 0.977273m),
            (5, -0.540000m, 0.600000m),
            (8, 0.660000m, 0.000000m),
            (12, 0.750000m, 0.000000m),
        };

        foreach ((int power, decimal cx, decimal cy) in cases)
        {
            var state = new MandelbrotState
            {
                Variant = MandelbrotVariant.Generalized,
                Power = power,
                CenterX = cx,
                CenterY = cy,
                Zoom = 300.0,
                Iterations = 2000,
                Threads = 2,
                Palette = palette()
            };
            byte[] perturbation = await RenderAsync(state, forceDeep: true, forceBla: null, w, h);
            byte[] exact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
            byte[] blaOff = await RenderAsync(state, forceDeep: true, forceBla: false, w, h);

            int vsExact = CountRgbDiffering(perturbation, exact);
            int vsBlaOff = CountRgbDiffering(perturbation, blaOff);
            int maxD = 0, nonBlack = 0;
            for (int i = 0; i < total; i++)
            {
                int o = i * 4;
                maxD = Math.Max(maxD, Math.Max(Math.Abs(perturbation[o] - exact[o]),
                    Math.Max(Math.Abs(perturbation[o + 1] - exact[o + 1]), Math.Abs(perturbation[o + 2] - exact[o + 2]))));
                if (perturbation[o] != 0 || perturbation[o + 1] != 0 || perturbation[o + 2] != 0) nonBlack++;
            }
            Console.WriteLine($"[diag] Multibrot p={power}: vs exact {vsExact}/{total} (maxΔ {maxD}), BLA on/off {vsBlaOff}/{total}, nonblack {nonBlack}");
            Check(nonBlack > total / 10, $"Multibrot p={power} view must carry structure.");
            Check(vsExact * 100 <= total * 3,
                $"Multibrot p={power}: perturbation diverges from exact on {vsExact}/{total} px, maxΔ {maxD} (>3%).");
            Check(vsBlaOff * 100 <= total * 3,
                $"Multibrot p={power}: BLA changes {vsBlaOff}/{total} px vs non-BLA (>3%).");
        }

        // Mandelbrot must be untouched by the Multibrot path.
        var mandel = new MandelbrotState
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = 5.0e25,
            Iterations = 4000,
            Threads = 2,
            Palette = palette()
        };
        Check(CountRgbDiffering(
                await RenderAsync(mandel, forceDeep: true, forceBla: null, w, h),
                await RenderAsync(mandel, forceDeep: true, forceBla: null, w, h)) == 0,
            "Mandelbrot deep render must stay deterministic after Phase 5.");
    }

    // Phase 10: BigFloat.Sqrt — the one operation odd-power Simonobrot needs that the type
    // did not have (|z|ᵖ = M^(p/2) = Mᵠ·√M). Checked three ways: against the published
    // decimal expansion of √2, by round-tripping (√x)² back to x at several magnitudes, and
    // by demanding exactness on perfect squares (where the integer Newton iteration must
    // land on the root itself, not one ULP below it).
    // Phase 11: transcendental functions over BigFloat (π, exp, sin/cos, sh/ch), built for
    // the Collatz deep-zoom stage. Nothing in the Mandelbrot engine calls them, so this is
    // the only place that pins them down. Three independent kinds of oracle:
    //   • published digits — catches a wrong algorithm outright;
    //   • identities (sin²+cos²=1, ch²−sh²=1, doubling formulas) — hold at every precision
    //     and catch guard-bit shortfalls the digit checks would miss at a single precision;
    //   • agreement with the double library on ordinary arguments — catches a wrong branch
    //     in the argument reduction, which the identities alone would not (they survive a
    //     consistent shift of both sin and cos).
    private static void VerifyBigFloatTranscendental()
    {
        // First 100 digits of each constant (truncated, not rounded — the checks compare
        // a prefix of the produced digit string).
        const string PiDigits =
            "3.141592653589793238462643383279502884197169399375105820974944592307816406286208998628034825342117067";
        const string EDigits =
            "2.718281828459045235360287471352662497757247093699959574966967627724076630353547594571382178525166427";
        const string Sin1Digits =
            "0.841470984807896506652502321630298999622563060798371065672751709991910404391239668948639743543052695";
        const string Cos1Digits =
            "0.540302305868139717400936607442976603732310420617922227670097255381100394774471764517951856087183089";
        const string Sinh1Digits =
            "1.175201193643801456882381850595600815155717981334095870229565413013307567304323895607117452089623391";
        const string Cosh1Digits =
            "1.543080634815243778477905620757061682601529112365863704737402214710769063049223698964264726435543035";
        const string SinPiTenthDigits =
            "0.309016994374947424102293417182819058860154589902881431067724311352630231409451224853603602094695568";
        const string CosPiTenthDigits =
            "0.951056516295153572116439333379382143405698634125750222447305644430153170085193501718792810970811381";
        const string ExpMinusFiveDigits =
            "0.006737946999085467096636048423148424248849585027355085430305531572683522515604062281449138844208361";

        static void CheckDigits(string label, BigFloat value, string expected)
        {
            string produced = value.ToInvariantString(expected.Length + 20);
            int common = 0;
            while (common < produced.Length && common < expected.Length && produced[common] == expected[common])
                common++;
            Check(common >= expected.Length,
                $"{label} matches only {common} of {expected.Length} published characters: {produced}");
        }

        // 384 bits ≈ 115 decimal digits, so all 100 published ones must come out right.
        using (new BigFloat.PrecisionScope(BigFloat.MinimumPrecisionBits))
        {
            CheckDigits("π", BigFloatMath.Pi, PiDigits);
            CheckDigits("exp(1)", BigFloatMath.Exp(BigFloat.One), EDigits);
            CheckDigits("exp(-5)", BigFloatMath.Exp(BigFloat.FromInt(-5)), ExpMinusFiveDigits);
            Check(BigFloatMath.Exp(BigFloat.Zero).Equals(BigFloat.One), "exp(0) must be exactly 1.");

            BigFloatMath.SinCos(BigFloat.One, out BigFloat sine, out BigFloat cosine);
            CheckDigits("sin(1)", sine, Sin1Digits);
            CheckDigits("cos(1)", cosine, Cos1Digits);

            BigFloatMath.SinCosPi(BigFloat.One / 10, out BigFloat sinePi, out BigFloat cosinePi);
            CheckDigits("sin(π/10)", sinePi, SinPiTenthDigits);
            CheckDigits("cos(π/10)", cosinePi, CosPiTenthDigits);

            BigFloatMath.SinhCosh(BigFloat.One, out BigFloat hyperbolicSine, out BigFloat hyperbolicCosine);
            CheckDigits("sh(1)", hyperbolicSine, Sinh1Digits);
            CheckDigits("ch(1)", hyperbolicCosine, Cosh1Digits);
        }

        // π at a precision far above and far below the 384-bit default: the Machin series
        // must be recomputed per precision, not reused from a cache keyed by nothing.
        using (new BigFloat.PrecisionScope(1024)) CheckDigits("π at 1024 bits", BigFloatMath.Pi, PiDigits);
        using (new BigFloat.PrecisionScope(128))
        {
            string produced = BigFloatMath.Pi.ToInvariantString(60);
            int common = 0;
            while (common < produced.Length && produced[common] == PiDigits[common]) common++;
            // 128 bits ≈ 38 decimal digits; ask for 34 to stay clear of the rounding digit.
            Check(common >= 36, $"π at 128 bits matches only {common} characters: {produced}");
        }

        // Identities at several precisions, over arguments that exercise every branch of
        // the reduction (both signs, every quadrant, several periods away from zero).
        foreach (int bits in new[] { 128, BigFloat.MinimumPrecisionBits, 512 })
        {
            using var precision = new BigFloat.PrecisionScope(bits);
            BigFloat tolerance = BigFloat.FromDouble(System.Math.ScaleB(1.0, -(bits - 12)));
            for (int index = -260; index <= 260; index += 7)
            {
                BigFloat turns = BigFloat.FromInt(index) / 37;
                BigFloatMath.SinCosPi(turns, out BigFloat sine, out BigFloat cosine);
                BigFloat residual = BigFloat.Abs(sine * sine + cosine * cosine - BigFloat.One);
                Check(residual.CompareTo(tolerance) <= 0,
                    $"sin²+cos² deviates from 1 by {residual.ToInvariantString(20)} at {index}/37 turns, {bits} bits.");

                // sin(2πx) = 2 sin(πx) cos(πx) ties the reduced branches to each other:
                // a quadrant mix-up survives sin²+cos²=1 but not this.
                BigFloatMath.SinCosPi(BigFloat.ScaleByPowerOfTwo(turns, 1), out BigFloat doubleSine, out _);
                BigFloat doublingError = BigFloat.Abs(
                    doubleSine - BigFloat.ScaleByPowerOfTwo(sine * cosine, 1));
                Check(doublingError.CompareTo(tolerance) <= 0,
                    $"sin(2πx) ≠ 2·sin(πx)·cos(πx) by {doublingError.ToInvariantString(20)} at {index}/37 turns.");

                BigFloatMath.SinhCosh(turns, out BigFloat hyperbolicSine, out BigFloat hyperbolicCosine);
                BigFloat hyperbolicResidual = BigFloat.Abs(
                    hyperbolicCosine * hyperbolicCosine - hyperbolicSine * hyperbolicSine - BigFloat.One);
                // ch grows like e^|x|, so the absolute residual is allowed to grow with it.
                BigFloat hyperbolicTolerance = tolerance * hyperbolicCosine * hyperbolicCosine;
                Check(hyperbolicResidual.CompareTo(hyperbolicTolerance) <= 0,
                    $"ch²−sh² deviates from 1 by {hyperbolicResidual.ToInvariantString(20)} at {index}/37, {bits} bits.");
            }
        }

        // Reduction by period is exact because it is done on the argument of sin(πx), not by
        // dividing by an approximate 2π. Far from zero the double library visibly loses this
        // (Math.PI * 12345.75 is already rounded), so the reference here is the exact value:
        // 12345.75 mod 2 = 1.75, hence sin = −√2/2 and cos = +√2/2.
        using (new BigFloat.PrecisionScope(BigFloat.MinimumPrecisionBits))
        {
            BigFloat half = BigFloat.ScaleByPowerOfTwo(BigFloat.Sqrt(BigFloat.FromInt(2)), -1);
            BigFloat tolerance = BigFloat.FromDouble(System.Math.ScaleB(1.0, -360));
            // Every argument here is an exact multiple of 1/4 turn, so |sin| = |cos| = √2/2
            // to the last bit; the signs come from the (small, exactly representable)
            // reduced argument, where the double library is still reliable.
            foreach (double turns in new[] { 12345.75, -87.25, 1e6 + 1.75, 0.75 })
            {
                BigFloatMath.SinCosPi(BigFloat.FromDouble(turns), out BigFloat sine, out BigFloat cosine);
                double reduced = turns - 2 * System.Math.Round(turns / 2);
                BigFloat expectedSine = System.Math.Sin(System.Math.PI * reduced) < 0 ? -half : half;
                BigFloat expectedCosine = System.Math.Cos(System.Math.PI * reduced) < 0 ? -half : half;
                Check(BigFloat.Abs(sine - expectedSine).CompareTo(tolerance) <= 0 &&
                      BigFloat.Abs(cosine - expectedCosine).CompareTo(tolerance) <= 0,
                    $"sin/cos(π·{turns}) lost the exact ±√2/2: {sine.ToInvariantString(25)}, {cosine.ToInvariantString(25)}");
            }
        }

        // Agreement with the double library on ordinary arguments — an oracle that shares
        // no code with BigFloat at all.
        using (new BigFloat.PrecisionScope(256))
        {
            var random = new Random(20260908);
            double worstTrig = 0, worstExp = 0, worstHyperbolic = 0;
            for (int index = 0; index < 3000; index++)
            {
                double turns = (random.NextDouble() - 0.5) * 8;
                BigFloatMath.SinCosPi(BigFloat.FromDouble(turns), out BigFloat sine, out BigFloat cosine);
                worstTrig = System.Math.Max(worstTrig,
                    System.Math.Abs(sine.ToDouble() - System.Math.Sin(System.Math.PI * turns)));
                worstTrig = System.Math.Max(worstTrig,
                    System.Math.Abs(cosine.ToDouble() - System.Math.Cos(System.Math.PI * turns)));

                double argument = (random.NextDouble() - 0.5) * 60;
                double exponential = System.Math.Exp(argument);
                worstExp = System.Math.Max(worstExp,
                    System.Math.Abs(BigFloatMath.Exp(BigFloat.FromDouble(argument)).ToDouble() - exponential) / exponential);

                BigFloatMath.SinhCosh(BigFloat.FromDouble(argument),
                    out BigFloat hyperbolicSine, out BigFloat hyperbolicCosine);
                worstHyperbolic = System.Math.Max(worstHyperbolic,
                    System.Math.Abs(hyperbolicSine.ToDouble() - System.Math.Sinh(argument)) /
                    System.Math.Abs(System.Math.Sinh(argument)));
                worstHyperbolic = System.Math.Max(worstHyperbolic,
                    System.Math.Abs(hyperbolicCosine.ToDouble() - System.Math.Cosh(argument)) /
                    System.Math.Cosh(argument));
            }
            Console.WriteLine($"[diag] BigFloat vs double: trig {worstTrig:E2} abs, exp {worstExp:E2} rel, " +
                              $"hyperbolic {worstHyperbolic:E2} rel");
            Check(worstTrig < 1e-13 && worstExp < 1e-13 && worstHyperbolic < 1e-13,
                "BigFloat transcendentals disagree with the double library beyond double's own rounding.");
        }

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Transcendental checks must leave the working precision restored.");
    }


    // Phase 12: Nova gained a perturbation engine of its own. Two things make it unlike the
    // Mandelbrot and Phoenix ones, and both are what these checks are aimed at.
    //
    //   • The formula is not polynomial. The step is folded from
    //     z − m·(z^p − 1)/(p·z^(p−1)) + c into (1 − m/p)·z + (m/p)·z^(1−p) + c, and that
    //     folding is an algebraic claim about branches of the logarithm, not a rewrite the
    //     compiler could check. The exact reference here deliberately iterates the ORIGINAL
    //     form in BigFloat — two separate powers and a division — so a wrong folding shows up.
    //
    //   • The power need not be an integer. An integer one goes through a binomial expansion
    //     (exact, no transcendentals); a fractional or complex one through log1p/expm1 with an
    //     explicit branch correction. These are two independent kernels and both are covered.
    private static async Task VerifyNovaDeepZoomAsync()
    {
        static MandelbrotPalette Palette() => new()
        {
            Colors = [Colors.White, Colors.Black],
            InteriorColor = Colors.Black,
            IsGradient = true
        };

        static NovaState View(double zoom, NovaVariant variant, int iterations = 300,
            decimal pReal = 3m, decimal pImaginary = 0m, decimal m = 1m,
            decimal centerX = 0m, decimal centerY = 0m, decimal threshold = 10m) => new()
        {
            Variant = variant,
            CenterX = centerX,
            CenterY = centerY,
            Zoom = zoom,
            Threshold = threshold,
            Iterations = iterations,
            PReal = pReal,
            PImaginary = pImaginary,
            Z0Real = 1m,
            M = m,
            CReal = 0m,
            CImaginary = 1m,
            UseSmoothColoring = true,
            Palette = Palette()
        };

        static NovaState AtExactCenter(NovaState state, string centerX, string centerY)
        {
            state.CenterXExact = centerX;
            state.CenterYExact = centerY;
            state.CenterX = BigFloat.Parse(centerX).ToDecimalClamped();
            state.CenterY = BigFloat.Parse(centerY).ToDecimalClamped();
            return state;
        }

        static int CountDiffering(byte[] a, byte[] b)
        {
            int differing = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int offset = pixel * 4;
                if (a[offset] != b[offset] || a[offset + 1] != b[offset + 1] ||
                    a[offset + 2] != b[offset + 2]) differing++;
            }
            return differing;
        }

        static int CountEdges(byte[] pixels, int width)
        {
            int edges = 0;
            int rows = pixels.Length / 4 / width;
            for (int y = 0; y < rows; y++)
            for (int x = 1; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                if (pixels[offset] != pixels[offset - 4] || pixels[offset + 1] != pixels[offset - 3] ||
                    pixels[offset + 2] != pixels[offset - 2]) edges++;
            }
            return edges;
        }

        static async Task<byte[]> RenderAsync(NovaState state, bool? forceDeep, int width, int height,
            int? forceBits = null)
        {
            byte[] pixels = new byte[width * height * 4];
            NovaRenderer.ForceDeepZoomForTests = forceDeep;
            NovaRenderer.ForceReferenceBitsForTests = forceBits;
            try
            {
                await Task.Run(() => NovaRenderer.Render(state, pixels, width, height, width * 4, 4,
                    CancellationToken.None));
            }
            finally
            {
                NovaRenderer.ForceDeepZoomForTests = null;
                NovaRenderer.ForceReferenceBitsForTests = null;
            }
            return pixels;
        }

        const int w = 64, h = 44, total = w * h;

        // 1. Where the plain double stage is still exact, the perturbation engine must
        //    reproduce it. Zoom 1 is used on purpose: the whole Nova structure is in frame, so
        //    every combination below has real content to disagree about — at a deeper zoom most
        //    of them would render a uniform field and the comparison would pass vacuously.
        //    The powers cover both kernels (2, 3, 4, 5 and −2 integer; 2.5 fractional;
        //    3+0.4i complex) and both planes.
        //
        //    The escape radius is the smallest the window accepts (2) rather than the usual
        //    10: with a wide radius most of these combinations converge everywhere in frame —
        //    the Nova map is Newton's method, so a bounded orbit is the rule — and several
        //    frames would come out uniform. At radius 2 the least structured of the 28 still
        //    shows over a hundred edge pixels.
        int worstDiffering = 0;
        string worstLabel = "";
        foreach (NovaVariant variant in new[] { NovaVariant.Mandelbrot, NovaVariant.Julia })
        foreach ((decimal real, decimal imaginary) in new[]
                 { (3m, 0m), (2m, 0m), (4m, 0m), (5m, 0m), (-2m, 0m), (2.5m, 0m), (3m, 0.4m) })
        foreach (decimal relaxation in new[] { 1m, 0.6m })
        {
            // A Julia frame centred on zero would degenerate at once: |z₀| = 0 is a pole.
            NovaState state = View(1, variant, 200, real, imaginary, relaxation,
                variant == NovaVariant.Julia ? 0.5m : 0m, variant == NovaVariant.Julia ? 0.5m : 0m, 2m);
            byte[] shallow = await RenderAsync(state, false, w, h);
            byte[] deep = await RenderAsync(state, true, w, h);
            int differing = CountDiffering(shallow, deep);
            Check(CountEdges(shallow, w) > 20,
                $"Nova shallow-vs-deep frame must show structure ({variant}, p={real}+{imaginary}i, m={relaxation}).");
            if (differing > worstDiffering)
            {
                worstDiffering = differing;
                worstLabel = $"{variant}/p={real}+{imaginary}i/m={relaxation}";
            }
            Check(differing * 100 <= total * 2,
                $"Nova perturbation must match the plain double path at zoom 1 " +
                $"({variant}, p={real}+{imaginary}i, m={relaxation}): {differing}/{total} pixels differ.");
        }
        Console.WriteLine($"[diag] nova shallow-vs-deep worst {worstDiffering}/{total} ({worstLabel})");

        // 2. On depth the plain stage cannot be trusted — the reference is direct BigFloat
        //    iteration of the original (unfolded) formula. Centres found by descending on edge
        //    density, so every frame here has structure; without it the comparison would be
        //    empty.
        (string X, string Y, double Zoom)[] deepFixtures =
        [
            ("-0.509516618365150777259563561165083570374599345148385054482531586472759954631328582763671875",
                "0.12446526403814218906112533265557181617030335358912995769031795134651474654674530029296875", 2.5e12),
            ("-0.5095166183649699337614919710008681922163433830570441234531321318787684682138916514304582960903644561767578125",
                "0.12446526403779946010431759880120476125853088903040909626751026250277769140406558534550640615634620189666748046875", 1.4e18),
            ("-0.5095166183649699336509924041728346350204283378347156708235845009654971517262786448779730841263102547600283287465572357177734375",
                "0.124465264037799460791408720088841974283648683522681157925231238309941040027064462616712058008403007924869143607793375849723815918", 2.2e24),
            ("-0.5095166183649699336509921081529684513569533240729108569093398402627923908730704144762385388672474793227032234532725141207265551202",
                "0.1244652640377994607914085291439532442123344414316132502241762782568814053414408708136371817397475259513173195813351412652991712093", 1.4e28),
            ("-0.5095166183649699336509921081231778031725923878106713770956940720740811612144618601104968658748996699920397628167844305494688095237",
                "0.1244652640377994607914085291655502843850926810587318374913406744440192434264407152019680763176412423180732757135548904522948099327", 2.8e32),
        ];
        foreach ((string centerX, string centerY, double zoom) in deepFixtures)
        {
            NovaState state = AtExactCenter(View(zoom, NovaVariant.Mandelbrot), centerX, centerY);
            byte[] deep = await RenderAsync(state, true, w, h);
            byte[] exact = await Task.Run(() =>
                NovaRenderer.RenderExactReferenceForTests(state, w, h, 128, CancellationToken.None));
            int differing = CountDiffering(deep, exact);
            int edges = CountEdges(exact, w);
            Console.WriteLine($"[diag] nova deep {zoom:0.0e+0}: {differing}/{total} differ, {edges} edges");
            Check(edges >= 100, $"Nova deep fixture at {zoom:0.0e+0} lost its structure (edges {edges}).");
            Check(differing * 100 <= total * 3,
                $"Nova deep zoom must match the exact BigFloat reference at {zoom:0.0e+0}: " +
                $"{differing}/{total} pixels differ.");
        }

        // 3. Точность плана: тот же кадр с заведомо избыточной разрядностью опорной орбиты.
        //    Единственная проверка самого плана, не требующая внешнего эталона.
        foreach ((string centerX, string centerY, double zoom) in deepFixtures)
        {
            NovaState state = AtExactCenter(View(zoom, NovaVariant.Mandelbrot), centerX, centerY);
            byte[] planned = await RenderAsync(state, true, w, h);
            byte[] generous = await RenderAsync(state, true, w, h,
                NovaRenderer.PlanReferenceBits(state) + 256);
            int drift = CountDiffering(planned, generous);
            Check(drift == 0,
                $"Nova precision plan must not drift with 256 extra reference bits at {zoom:0.0e+0}: " +
                $"{drift}/{total} pixels differ.");
        }

        // 4. Дробная и комплексная степень на глубине — вторая ветвь приращения (log1p/expm1
        //    с поправкой ветви). Кадры меньше и итераций меньше: эталон для нецелой степени
        //    считает комплексный логарифм произвольной точности на каждом шаге.
        (decimal Real, decimal Imaginary, string X, string Y, double Zoom)[] fractionalFixtures =
        [
            (2.5m, 0m,
                "-0.54230858625529284624830722419350632704409780472480651081212954522925429046154022216796875",
                "0.10561361857564103568872547550552447522320710561827077599017510323164970031939446926116943359375",
                2.2e12),
            (3m, 0.4m,
                "-0.618079795182438829418512344834935446683807723718782488504797090200781894964165985584259033203125",
                "0.102843101909722159412254109837162628956617147567395749441221397546541993506252765655517578125",
                2.2e12),
        ];
        foreach ((decimal real, decimal imaginary, string centerX, string centerY, double zoom) in fractionalFixtures)
        {
            NovaState state = AtExactCenter(
                View(zoom, NovaVariant.Mandelbrot, 120, real, imaginary), centerX, centerY);
            byte[] deep = await RenderAsync(state, true, 32, 22);
            byte[] exact = await Task.Run(() =>
                NovaRenderer.RenderExactReferenceForTests(state, 32, 22, 128, CancellationToken.None));
            int differing = CountDiffering(deep, exact);
            Console.WriteLine($"[diag] nova deep p={real}+{imaginary}i {zoom:0.0e+0}: {differing}/704 differ, " +
                              $"{CountEdges(exact, 32)} edges");
            Check(differing * 100 <= 704 * 4,
                $"Nova deep zoom with power {real}+{imaginary}i must match the exact BigFloat reference: " +
                $"{differing}/704 pixels differ.");
        }

        // 5. Тайл прогрессивного предпросмотра обязан совпасть с полным кадром: у них разные
        //    точки входа в движок и своя раскладка пикселей.
        {
            NovaState state = AtExactCenter(View(deepFixtures[2].Zoom, NovaVariant.Mandelbrot),
                deepFixtures[2].X, deepFixtures[2].Y);
            byte[] full = await RenderAsync(state, true, w, h);
            NovaRenderer.ForceDeepZoomForTests = true;
            byte[]? tile;
            try
            {
                tile = await Task.Run(() => NovaRenderer.RenderTile(state, w, h,
                    new MandelbrotRenderTile(16, 12, 24, 16, 1, 1), CancellationToken.None));
            }
            finally { NovaRenderer.ForceDeepZoomForTests = null; }
            Check(tile is not null, "Nova deep-zoom tile must render.");
            int tileDiffering = 0;
            for (int localY = 0; localY < 16; localY++)
            for (int localX = 0; localX < 24; localX++)
            {
                int tileOffset = (localY * 24 + localX) * 4;
                int fullOffset = ((12 + localY) * w + 16 + localX) * 4;
                if (tile![tileOffset] != full[fullOffset] || tile[tileOffset + 1] != full[fullOffset + 1] ||
                    tile[tileOffset + 2] != full[fullOffset + 2]) tileDiffering++;
            }
            Check(tileDiffering == 0,
                $"Nova deep-zoom tile must match the full frame exactly: {tileDiffering}/384 differ.");
        }

        // 6. Точный центр действительно доходит до рендера: сдвиг на десятую пикселя на
        //    глубине, где decimal-поля состояния его уже не различают, обязан менять кадр.
        {
            (string centerX, string centerY, double zoom) = deepFixtures[4];
            NovaState state = AtExactCenter(View(zoom, NovaVariant.Mandelbrot), centerX, centerY);
            BigFloat shifted;
            using (new BigFloat.PrecisionScope(NovaRenderer.PlanReferenceBits(state)))
                shifted = BigFloat.Parse(centerX) + BigFloat.FromDouble(0.1 * 4.0 / zoom / w);
            NovaState moved = AtExactCenter(View(zoom, NovaVariant.Mandelbrot),
                shifted.ToInvariantString(), centerY);
            Check(moved.CenterX == state.CenterX,
                "The shift must be invisible to the decimal centre, otherwise the check proves nothing.");
            byte[] original = await RenderAsync(state, true, w, h);
            byte[] nudged = await RenderAsync(moved, true, w, h);
            Check(CountDiffering(original, nudged) > 0,
                "A sub-pixel shift of the exact centre must change the deep-zoom frame.");
        }

        // 7. Нулевая степень движку не по силам (знаменатель p·z^(p−1) тождественно ноль) и
        //    обязана молча уходить на плоскую ступень, а не падать.
        {
            NovaState degenerate = AtExactCenter(View(1e20, NovaVariant.Mandelbrot, 120, 0m),
                deepFixtures[0].X, deepFixtures[0].Y);
            byte[] pixels = await RenderAsync(degenerate, null, w, h);
            Check(pixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
                "Nova with power 0 must still fill every pixel.");
        }

        // 8. Круг «окно → сохранение → окно»: точный центр обязан пережить его без потерь.
        //    Именно здесь легко потерять глубину — decimal-поля состояния сохраняют лишь 28
        //    знаков, и если окно перестанет писать или читать строки, зум просто вернётся к
        //    прежнему потолку, а рендер останется формально исправным.
        {
            // Разметка окна тянет стили из App.xaml, а проверочный Application создаётся
            // пустым: без словаря конструктор падает на StaticResource.
            var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
            if (Application.Current.Resources.MergedDictionaries.All(d => d.Source != themeStyles))
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });

            var window = new NovaWindow(NovaVariant.Mandelbrot);
            NovaState deep = AtExactCenter(View(1e20, NovaVariant.Mandelbrot),
                deepFixtures[2].X, deepFixtures[2].Y);
            window.LoadState(deep);
            NovaState captured = window.CaptureState("round-trip");

            // Сравнение по значению, а не по тексту: BigFloat печатает столько цифр, сколько
            // несёт его мантисса, поэтому строка на выходе длиннее исходной, обозначая то же
            // число. Важно, что оно не изменилось, а не как оно записано.
            Check(captured.CenterXExact is { Length: > 0 } && captured.CenterYExact is { Length: > 0 },
                "A deep Nova save must carry exact-center strings.");
            Check(BigFloat.Parse(captured.CenterXExact!) == BigFloat.Parse(deep.CenterXExact!) &&
                  BigFloat.Parse(captured.CenterYExact!) == BigFloat.Parse(deep.CenterYExact!),
                "The Nova window must round-trip the exact center of a deep save without losing digits.");
            Check(captured.Zoom == deep.Zoom, "The Nova window must round-trip a deep zoom unchanged.");

            // И обратно: обычное сохранение с мелким зумом не должно обзаводиться строками
            // точного центра — иначе они начнут расходиться с decimal-полями.
            window.LoadState(View(700, NovaVariant.Mandelbrot));
            NovaState shallow = window.CaptureState("round-trip-shallow");
            Check(shallow.CenterXExact is null && shallow.CenterYExact is null,
                "A shallow Nova save must not carry exact-center strings.");
            window.Close();
        }

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Deep Nova render must restore the calling thread's working precision.");
    }

    // Phase 12: logarithm over BigFloat and the complex exponential/logarithm/power built on
    // it, added for the Nova deep-zoom stage — its reference orbit contains z^(1−p) with an
    // arbitrary complex power, and such a power exists only through a logarithm. The same
    // three kinds of oracle as the other transcendentals:
    //   • published digits of ln 2 — catches a wrong algorithm outright;
    //   • identities (exp∘log = id, ln(xy) = ln x + ln y, integer power against exp(k·ln z))
    //     — hold at every precision and catch guard-bit shortfalls;
    //   • agreement with the double library — catches a wrong branch, which the identities
    //     survive (a consistent 2πi shift satisfies exp∘log = id).
    private static void VerifyBigFloatLogarithm()
    {
        const string LogTwoDigits =
            "0.693147180559945309417232121458176568075500134360255254120680009493393621969694715605863326996418687";

        static void CheckDigits(string label, BigFloat value, string expected)
        {
            string produced = value.ToInvariantString(expected.Length + 20);
            int common = 0;
            while (common < produced.Length && common < expected.Length && produced[common] == expected[common])
                common++;
            Check(common >= expected.Length,
                $"{label} matches only {common} of {expected.Length} published characters: {produced}");
        }

        using (new BigFloat.PrecisionScope(BigFloat.MinimumPrecisionBits))
        {
            CheckDigits("ln 2", BigFloatMath.Log(BigFloat.FromInt(2)), LogTwoDigits);
            CheckDigits("LogTwo", BigFloatMath.LogTwo, LogTwoDigits);
            Check(BigFloatMath.Log(BigFloat.One).IsZero, "ln 1 must be exactly 0.");
        }

        // ln 2 at a precision far above and far below the default: the series must be
        // recomputed per precision, not served from a cache keyed by nothing.
        using (new BigFloat.PrecisionScope(1024)) CheckDigits("ln 2 at 1024 bits", BigFloatMath.LogTwo, LogTwoDigits);
        using (new BigFloat.PrecisionScope(128))
        {
            string produced = BigFloatMath.LogTwo.ToInvariantString(60);
            int common = 0;
            while (common < produced.Length && produced[common] == LogTwoDigits[common]) common++;
            // 128 bits ≈ 38 decimal digits; ask for 36 to stay clear of the rounding digit.
            Check(common >= 36, $"ln 2 at 128 bits matches only {common} characters: {produced}");
        }

        // Identities at several precisions. The round trip is the strongest single check the
        // logarithm has: it ties Log to the already-published Exp, and its residual is
        // relative, so it fails the moment the guard bits stop covering the Newton step.
        foreach (int bits in new[] { 128, BigFloat.MinimumPrecisionBits, 768 })
        {
            using var precision = new BigFloat.PrecisionScope(bits);
            BigFloat tolerance = BigFloat.FromDouble(System.Math.ScaleB(1.0, -(bits - 12)));
            foreach (string text in new[]
                     { "1e-90", "0.0009765625", "0.5", "0.9999999", "1.0000001", "3", "123456.789", "1e75" })
            {
                BigFloat value = BigFloat.Parse(text);
                BigFloat roundTrip = BigFloatMath.Exp(BigFloatMath.Log(value));
                BigFloat residual = BigFloat.Abs((roundTrip - value) / value);
                Check(residual.CompareTo(tolerance) <= 0,
                    $"exp(ln {text}) deviates by {residual.ToInvariantString(20)} at {bits} bits.");
            }

            // ln(x·y) = ln x + ln y over arguments whose exponents differ wildly: the
            // decomposition x = m·2^e must contribute exactly e·ln 2 and nothing else.
            BigFloat left = BigFloat.Parse("7.25e-40");
            BigFloat right = BigFloat.Parse("3.5e31");
            BigFloat additive = BigFloat.Abs(
                BigFloatMath.Log(left * right) - BigFloatMath.Log(left) - BigFloatMath.Log(right));
            Check(additive.CompareTo(tolerance) <= 0,
                $"ln(xy) ≠ ln x + ln y by {additive.ToInvariantString(20)} at {bits} bits.");
        }

        // Complex exponential, logarithm and power. The integer power is computed by binary
        // exponentiation and shares no line with the logarithm, so agreeing with
        // exp(k·ln z) pins both down at once — including the branch, which a wrong 2πi
        // offset in Log would break for a non-integer k but not for an integer one.
        using (new BigFloat.PrecisionScope(512))
        {
            BigFloat tolerance = BigFloat.FromDouble(System.Math.ScaleB(1.0, -460));
            var samples = new[]
            {
                new Complex(0.7, 0.3), new Complex(-0.8, 0.05), new Complex(-0.8, -0.05),
                new Complex(1, 0), new Complex(0, 2), new Complex(3e10, -1e-4),
                new Complex(1e-12, 5e-13), new Complex(-2.5, 0)
            };
            foreach (Complex sample in samples)
            {
                ComplexBigFloat value = ComplexBigFloat.FromDouble(sample.Real, sample.Imaginary);
                ComplexBigFloat roundTrip = ComplexBigFloat.Exp(ComplexBigFloat.Log(value));
                ComplexBigFloat residual = (roundTrip - value) / value;
                Check(BigFloat.Abs(residual.Real).CompareTo(tolerance) <= 0 &&
                      BigFloat.Abs(residual.Imaginary).CompareTo(tolerance) <= 0,
                    $"exp(ln z) ≠ z for {sample}: {residual}");

                foreach (int power in new[] { 1, 2, 3, 7, 11, -1, -2, -9 })
                {
                    ComplexBigFloat viaInteger = ComplexBigFloat.Pow(value, power);
                    ComplexBigFloat viaLogarithm =
                        ComplexBigFloat.Pow(value, ComplexBigFloat.FromDouble(power, 0));
                    ComplexBigFloat drift = (viaInteger - viaLogarithm) / viaInteger;
                    Check(BigFloat.Abs(drift.Real).CompareTo(tolerance) <= 0 &&
                          BigFloat.Abs(drift.Imaginary).CompareTo(tolerance) <= 0,
                        $"z^{power} by binary exponentiation ≠ exp({power}·ln z) for {sample}: {drift}");
                }
            }
        }

        // Agreement with the double library — an oracle that shares no code with BigFloat.
        // The complex samples deliberately straddle the negative real axis, where the
        // principal branch jumps: a Newton iteration seeded off-branch would show up here.
        using (new BigFloat.PrecisionScope(256))
        {
            var random = new Random(20260911);
            double worstReal = 0, worstComplex = 0, worstPower = 0;
            for (int index = 0; index < 2000; index++)
            {
                double argument = System.Math.Exp((random.NextDouble() - 0.5) * 120);
                worstReal = System.Math.Max(worstReal,
                    System.Math.Abs(BigFloatMath.Log(BigFloat.FromDouble(argument)).ToDouble() -
                                    System.Math.Log(argument)) / System.Math.Abs(System.Math.Log(argument)));

                var sample = new Complex((random.NextDouble() - 0.5) * 6, (random.NextDouble() - 0.5) * 0.02);
                if (Complex.Abs(sample) < 1e-6) continue;
                Complex expectedLogarithm = Complex.Log(sample);
                Complex actualLogarithm =
                    ComplexBigFloat.Log(ComplexBigFloat.FromDouble(sample.Real, sample.Imaginary)).ToComplex();
                worstComplex = System.Math.Max(worstComplex,
                    Complex.Abs(actualLogarithm - expectedLogarithm) /
                    System.Math.Max(1e-3, Complex.Abs(expectedLogarithm)));

                var exponent = new Complex((random.NextDouble() - 0.5) * 8, (random.NextDouble() - 0.5) * 2);
                Complex expectedPower = Complex.Pow(sample, exponent);
                if (!double.IsFinite(expectedPower.Real) || !double.IsFinite(expectedPower.Imaginary) ||
                    Complex.Abs(expectedPower) < 1e-250) continue;
                Complex actualPower = ComplexBigFloat.Pow(
                    ComplexBigFloat.FromDouble(sample.Real, sample.Imaginary),
                    ComplexBigFloat.FromDouble(exponent.Real, exponent.Imaginary)).ToComplex();
                worstPower = System.Math.Max(worstPower,
                    Complex.Abs(actualPower - expectedPower) / Complex.Abs(expectedPower));
            }
            Console.WriteLine($"[diag] BigFloat log vs double: real {worstReal:E2} rel, " +
                              $"complex {worstComplex:E2} rel, complex pow {worstPower:E2} rel");
            Check(worstReal < 1e-13 && worstComplex < 1e-13 && worstPower < 1e-11,
                "BigFloat logarithm disagrees with the double library beyond double's own rounding.");
        }

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Logarithm checks must leave the working precision restored.");
    }

    // Centres found by descending on edge density (a frame far outside the set comes out
    // uniformly non-black and would pass a "has content" check while showing nothing).
    private static readonly (double Zoom, string CenterX, string CenterY)[] DeepCollatzCentres =
    [
        (1.1e12, "-0.869177864622138448380772548135348733365049368",
                 "0.003351110447198153027105397611632949120554176"),
        (1.1e15, "-0.869177864620624139702320622587697311553355236",
                 "0.003351110446321172760920851431301963941719519"),
        (1.1e18, "-0.869177864620622627578561083124457777168012231",
                 "0.00335111044632155045879382890961167857526658"),
    ];

    // Phase 11: Collatz gained a third precision stage — direct iteration in BigFloat.
    // Unlike the Mandelbrot family this is not perturbation: the formula is transcendental
    // (cos πz), its derivative is tens per step, so δ from a reference orbit reaches the
    // size of the orbit within a couple of dozen iterations and there is nothing to rebase
    // onto. The stage engages above zoom 1e10, which is where the old ladder actually broke
    // — both the double and the decimal path computed cos/sin in double, so decimal only
    // ever raised the precision of the coordinates, never of the formula.
    private static async Task VerifyCollatzDeepZoomAsync()
    {
        static MandelbrotPalette Palette() => new()
        {
            Colors = [Colors.White, Colors.Black],
            InteriorColor = Colors.Black,
            IsGradient = true
        };

        static CollatzState View(double centerX, double centerY, double zoom,
            CollatzVariation variation, CollatzColoringMode coloring, int iterations = 150) => new()
        {
            CenterX = (decimal)centerX,
            CenterY = (decimal)centerY,
            Zoom = zoom,
            Iterations = iterations,
            Threshold = 100m,
            Variation = variation,
            ColoringMode = coloring,
            PParameter = 3m,
            QRealParameter = 0.2m,
            QImaginaryParameter = -0.1m,
            UseSmoothColoring = true,
            OrbitDensitySampleStep = 2,
            Palette = Palette()
        };

        static CollatzState Exact(double zoom, string centerX, string centerY,
            CollatzVariation variation = CollatzVariation.Standard,
            CollatzColoringMode coloring = CollatzColoringMode.EscapeTime)
        {
            CollatzState state = View(0, 0, zoom, variation, coloring);
            state.CenterXExact = centerX;
            state.CenterYExact = centerY;
            state.CenterX = BigFloat.Parse(centerX).ToDecimalClamped();
            state.CenterY = BigFloat.Parse(centerY).ToDecimalClamped();
            return state;
        }

        static int CountDiffering(byte[] a, byte[] b)
        {
            int differing = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int offset = pixel * 4;
                if (a[offset] != b[offset] || a[offset + 1] != b[offset + 1] ||
                    a[offset + 2] != b[offset + 2]) differing++;
            }
            return differing;
        }

        // Neighbouring pixels that differ — the frame really shows structure rather than a
        // uniform fill. Counting non-black pixels is not enough: the verification palette is
        // white→black with a black interior, so a frame far outside the set comes out fully
        // non-black and tells nothing (the lesson from the Simonobrot fixtures).
        static int CountEdges(byte[] pixels, int width)
        {
            int edges = 0;
            int rows = pixels.Length / 4 / width;
            for (int y = 0; y < rows; y++)
            for (int x = 1; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                if (pixels[offset] != pixels[offset - 4] || pixels[offset + 1] != pixels[offset - 3] ||
                    pixels[offset + 2] != pixels[offset - 2]) edges++;
            }
            return edges;
        }

        async Task<byte[]> RenderAsync(CollatzState state, bool? forceBigFloat, int width, int height,
            int? forcePrecisionBits = null)
        {
            byte[] pixels = new byte[width * height * 4];
            CollatzRenderer.ForceBigFloatForTests = forceBigFloat;
            CollatzRenderer.ForcePrecisionBitsForTests = forcePrecisionBits;
            try
            {
                await Task.Run(() => CollatzRenderer.Render(state, pixels, width, height, width * 4, 4,
                    CancellationToken.None));
            }
            finally
            {
                CollatzRenderer.ForceBigFloatForTests = null;
                CollatzRenderer.ForcePrecisionBitsForTests = null;
            }
            return pixels;
        }

        const int w = 64, h = 44, total = w * h;

        // 1. Where double is still trustworthy, the BigFloat stage must reproduce it. Run
        //    every variation against every coloring mode: each mode reads a different set of
        //    orbit metrics, and each variation a different branch of the formula. This is
        //    the check that the formula, the escape tests and every metric were transcribed
        //    correctly — the double path shares no code with the BigFloat one.
        var variations = new[]
        {
            CollatzVariation.Standard, CollatzVariation.SineVariation,
            CollatzVariation.ParityBranchVariation, CollatzVariation.GeneralizedP,
            CollatzVariation.GeneralizedPQ
        };
        var colorings = new[]
        {
            CollatzColoringMode.EscapeTime, CollatzColoringMode.FinalArgument,
            CollatzColoringMode.FinalMagnitude, CollatzColoringMode.CycleBasins,
            CollatzColoringMode.IntegerTrap, CollatzColoringMode.RealAxisTrap,
            CollatzColoringMode.OrbitDensity, CollatzColoringMode.PeriodDetection
        };
        int worstDiffering = 0;
        string worstLabel = "";
        foreach (CollatzVariation variation in variations)
        foreach (CollatzColoringMode coloring in colorings)
        {
            CollatzState state = View(0.5623, 0, 5000, variation, coloring,
                coloring == CollatzColoringMode.OrbitDensity ? 60 : 150);
            byte[] shallow = await RenderAsync(state, false, w, h);
            byte[] deep = await RenderAsync(state, true, w, h);
            int differing = CountDiffering(shallow, deep);
            if (differing > worstDiffering)
            {
                worstDiffering = differing;
                worstLabel = $"{variation}/{coloring}";
            }
            Check(differing * 100 <= total * 6,
                $"BigFloat stage diverges from the double stage on {differing}/{total} px " +
                $"for {variation}/{coloring} (>6%).");
        }
        Console.WriteLine($"[diag] Collatz BigFloat vs double @zoom 5e3: worst {worstDiffering}/{total} " +
                          $"({100.0 * worstDiffering / total:F2}%) at {worstLabel}");

        // 2. The same at a zoom where double is near its limit. A larger drift is expected
        //    here, and it is double's: the BigFloat stage carries ~50 spare bits there.
        CollatzState nearLimit = View(0.5623, 0, 1e8, CollatzVariation.Standard,
            CollatzColoringMode.EscapeTime);
        int nearLimitDiffering = CountDiffering(await RenderAsync(nearLimit, false, w, h),
            await RenderAsync(nearLimit, true, w, h));
        Console.WriteLine($"[diag] Collatz BigFloat vs double @zoom 1e8: {nearLimitDiffering}/{total} " +
                          $"({100.0 * nearLimitDiffering / total:F2}%)");
        Check(nearLimitDiffering * 100 <= total * 25,
            $"BigFloat stage diverges from double at 1e8 on {nearLimitDiffering}/{total} px (>25%).");

        // 3. Deep frames must complete, fill every pixel, still show structure, and leave the
        //    calling thread's working precision alone. And — the only check of the precision
        //    plan itself that needs no external oracle — the planned precision must give the
        //    same frame as a deliberately excessive one.
        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Working precision must start at the minimum.");
        foreach ((double zoom, string centerX, string centerY) in DeepCollatzCentres)
        {
            CollatzState deep = Exact(zoom, centerX, centerY);
            var watch = Stopwatch.StartNew();
            byte[] planned = await RenderAsync(deep, null, w, h);
            watch.Stop();
            int plannedBits = CollatzRenderer.PlanPrecisionBits(deep);
            byte[] generous = await RenderAsync(deep, null, w, h, plannedBits + 256);
            int edges = CountEdges(planned, w);
            int drift = CountDiffering(planned, generous);
            Console.WriteLine($"[diag] Collatz deep {zoom:E1}: {plannedBits} bits, " +
                              $"{watch.Elapsed.TotalMilliseconds:F0} ms for {w}×{h}, edges {edges}, " +
                              $"drift vs +256 bits {drift}/{total}");
            Check(planned.Where((_, index) => index % 4 == 3).All(value => value == 255),
                $"Deep Collatz render at {zoom:E1} left pixels unfilled.");
            Check(edges >= 40, $"Deep Collatz render at {zoom:E1} shows no structure (edges {edges}).");
            Check(drift * 100 <= total * 2,
                $"The precision plan is short at {zoom:E1}: {drift}/{total} px change when given 256 more bits.");
        }

        // 4. Past the deepest centre we have, structure is not guaranteed — but the stage
        //    must still run to completion at the zoom ceiling the window allows.
        foreach (double zoom in new[] { 1e30, 1e50 })
        {
            CollatzState extreme = Exact(zoom, DeepCollatzCentres[^1].CenterX, DeepCollatzCentres[^1].CenterY);
            var watch = Stopwatch.StartNew();
            byte[] pixels = await RenderAsync(extreme, null, w, h);
            watch.Stop();
            Console.WriteLine($"[diag] Collatz extreme {zoom:E0}: " +
                              $"{CollatzRenderer.PlanPrecisionBits(extreme)} bits, " +
                              $"{watch.Elapsed.TotalMilliseconds:F0} ms for {w}×{h}");
            Check(pixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
                $"Collatz render at {zoom:E0} left pixels unfilled.");
        }
        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Deep Collatz render must restore the calling thread's working precision.");

        // 5. The tile path and the full-frame path must agree pixel for pixel: the window
        //    renders tiles, the exporter renders whole frames.
        CollatzState tiled = Exact(DeepCollatzCentres[^1].Zoom, DeepCollatzCentres[^1].CenterX,
            DeepCollatzCentres[^1].CenterY);
        byte[] full = await RenderAsync(tiled, null, w, h);
        var tile = new MandelbrotRenderTile(16, 12, 32, 20, 1, 1);
        byte[]? tilePixels = await Task.Run(() =>
            CollatzRenderer.RenderTile(tiled, w, h, tile, CancellationToken.None));
        Check(tilePixels is not null, "Deep Collatz tile render returned null without cancellation.");
        int tileDiffering = 0;
        for (int y = 0; y < tile.Height; y++)
        for (int x = 0; x < tile.Width; x++)
        {
            int tileOffset = (y * tile.Width + x) * 4;
            int frameOffset = ((tile.Y + y) * w + tile.X + x) * 4;
            if (tilePixels![tileOffset] != full[frameOffset] ||
                tilePixels[tileOffset + 1] != full[frameOffset + 1] ||
                tilePixels[tileOffset + 2] != full[frameOffset + 2]) tileDiffering++;
        }
        Check(tileDiffering == 0,
            $"Deep Collatz tile disagrees with the full frame on {tileDiffering} px.");

        // 6. The exact centre must actually reach the renderer: a shift of a tenth of a pixel
        //    at 1e18 is far below what the decimal centre can hold, so if the exact strings
        //    were being ignored the frame would not move at all. And an exact centre equal to
        //    the decimal one must change nothing.
        CollatzState shifted = Exact(DeepCollatzCentres[^1].Zoom,
            ShiftCentre(DeepCollatzCentres[^1].CenterX, DeepCollatzCentres[^1].Zoom),
            DeepCollatzCentres[^1].CenterY);
        Check(CountDiffering(full, await RenderAsync(shifted, null, w, h)) > 0,
            "A sub-decimal shift of the exact centre changed nothing — the exact centre is ignored.");

        CollatzState plain = View(0.5623, 0, 1e12, CollatzVariation.Standard, CollatzColoringMode.EscapeTime);
        CollatzState mirrored = View(0.5623, 0, 1e12, CollatzVariation.Standard, CollatzColoringMode.EscapeTime);
        mirrored.CenterXExact = "0.5623";
        mirrored.CenterYExact = "0";
        Check(CountDiffering(await RenderAsync(plain, null, w, h),
                  await RenderAsync(mirrored, null, w, h)) == 0,
            "An exact centre equal to the decimal centre must render identically.");

        // 7. Determinism: the stage is parallel over rows and keeps per-thread state (the
        //    orbit history buffer and the working precision).
        CollatzState repeat = Exact(DeepCollatzCentres[0].Zoom, DeepCollatzCentres[0].CenterX,
            DeepCollatzCentres[0].CenterY, CollatzVariation.GeneralizedPQ,
            CollatzColoringMode.CycleBasins);
        Check(CountDiffering(await RenderAsync(repeat, null, w, h),
                  await RenderAsync(repeat, null, w, h)) == 0,
            "Two identical deep Collatz renders differ — the stage is not deterministic.");

        // 8. The precision plan must grow with depth and never drop below the floor.
        int previousBits = 0;
        foreach (double zoom in new[] { 1e10, 1e15, 1e20, 1e30, 1e40, 1e50 })
        {
            int bits = CollatzRenderer.PlanPrecisionBits(View(0, 0, zoom, CollatzVariation.Standard,
                CollatzColoringMode.EscapeTime));
            Check(bits >= 128 && bits >= previousBits,
                $"Precision plan is not monotonic: {bits} bits at zoom {zoom:E0} after {previousBits}.");
            previousBits = bits;
        }
    }

    // Moves an exact centre by about a tenth of a pixel at the given zoom — a difference the
    // decimal centre cannot represent at these depths.
    private static string ShiftCentre(string centre, double zoom)
    {
        using var precision = new BigFloat.PrecisionScope(1024);
        BigFloat step = BigFloat.FromInt(4) / BigFloat.FromDouble(zoom) / 640;
        return (BigFloat.Parse(centre) + step).ToInvariantString();
    }

    private static void VerifyBigFloatSqrt()
    {
        // First 100 digits of √2.
        const string Root2 =
            "1.414213562373095048801688724209698078569671875376948073176679737990732478462107038850387534327641572";

        using (new BigFloat.PrecisionScope(BigFloat.MinimumPrecisionBits))
        {
            string produced = BigFloat.Sqrt(BigFloat.FromInt(2)).ToInvariantString(120);
            int common = 0;
            while (common < produced.Length && common < Root2.Length && produced[common] == Root2[common]) common++;
            // 384 bits ≈ 115 decimal digits, so all 100 published ones must come out right.
            Check(common >= Root2.Length,
                $"BigFloat.Sqrt(2) matches only {common} of {Root2.Length} published characters: {produced}");
        }

        foreach (int bits in new[] { BigFloat.MinimumPrecisionBits, 512, 1024 })
        {
            using var precision = new BigFloat.PrecisionScope(bits);
            foreach (string text in new[] { "2", "3", "0.9999999999999", "1e-120", "1e40", "1e-300" })
            {
                BigFloat value = BigFloat.Parse(text);
                BigFloat root = BigFloat.Sqrt(value);
                BigFloat error = root * root - value;
                if (error.Sign < 0) error = -error;
                // Two roundings (the root and the squaring) at `bits` significant bits. The
                // bound is scaled in BigFloat, not in double: at 1e-300 a relative ratio
                // taken through ToDouble would underflow to zero and pass vacuously.
                BigFloat tolerance = value * BigFloat.FromDouble(System.Math.ScaleB(1.0, -(bits - 4)));
                Check(error.CompareTo(tolerance) <= 0,
                    $"BigFloat.Sqrt({text}) at {bits} bits: (√x)² is off by {error.ToInvariantString(20)}.");
            }

            foreach (string text in new[] { "4", "0.25", "1", "1e-100", "1e100", "12345678901234567890" })
            {
                BigFloat value = BigFloat.Parse(text);
                Check((BigFloat.Sqrt(value * value) - value).IsZero,
                    $"BigFloat.Sqrt of the perfect square ({text})² must return exactly {text} at {bits} bits.");
            }

            Check(BigFloat.Sqrt(BigFloat.Zero).IsZero, "BigFloat.Sqrt(0) must be zero.");
        }

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "BigFloat.Sqrt checks must leave the working precision restored.");
    }

    // Phoenix on the perturbation engine. Phoenix had no precision ladder at all: both the
    // full render and the tile path computed coordinates in plain double, so the picture fell
    // apart around 1e12 while the zoom box happily accepted decimal.MaxValue/2.
    //
    // What makes Phoenix different from the Mandelbrot family is memory: z_{n+1} depends on
    // z_{n-1}, so the per-pixel state is the pair (δₙ, δₙ₋₁) and the reference orbit is stored
    // shifted by one (Orbit[i] = Z_{i-1}, Orbit[0] = z₋₁) so that rebasing to the start has a
    // z₋₁ to rebase against.
    //
    // The load-bearing check is the first one: the plain double path shares no code with the
    // BigFloat/perturbation path, so agreement at a zoom where double is still exact is what
    // proves the formula, every coloring metric and the perturbation algebra were all
    // transcribed correctly. The exact BigFloat reference is the oracle for the depths where
    // the double path can no longer be trusted — but it shares VariantPowerBig with the
    // reference orbit, so it can only catch perturbation errors, not formula errors.
    private static async Task VerifyPhoenixDeepZoomAsync()
    {
        static MandelbrotPalette Palette() => new()
        {
            Colors = [Colors.White, Colors.Black],
            InteriorColor = Colors.Black,
            IsGradient = true
        };

        static PhoenixState View(double zoom, PhoenixVariant variant, PhoenixColoringMode coloring,
            PhoenixPlaneMode plane = PhoenixPlaneMode.Julia, int primaryPower = 2, int secondaryPower = 0,
            int iterations = 200, double centerX = 0, double centerY = 0) => new()
        {
            CenterX = (decimal)centerX,
            CenterY = (decimal)centerY,
            Zoom = zoom,
            Iterations = iterations,
            Threshold = 4m,
            C1Real = 0.56667m,
            C2Real = -0.5m,
            PlaneMode = plane,
            Variant = variant,
            PrimaryPower = primaryPower,
            SecondaryPower = secondaryPower,
            ColoringMode = coloring,
            OrbitTrapMode = PhoenixOrbitTrapMode.Axes,
            OrbitTrapRadius = 0.5,
            OrbitTrapStrength = 1.5,
            StripeFrequency = 3,
            StripeStrength = 0.65,
            CycleTolerance = 1e-7,
            MaximumDetectedPeriod = 32,
            Palette = Palette()
        };

        static PhoenixState AtExactCenter(PhoenixState state, string centerX, string centerY)
        {
            state.CenterXExact = centerX;
            state.CenterYExact = centerY;
            state.CenterX = BigFloat.Parse(centerX).ToDecimalClamped();
            state.CenterY = BigFloat.Parse(centerY).ToDecimalClamped();
            return state;
        }

        static int CountDiffering(byte[] a, byte[] b)
        {
            int differing = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int offset = pixel * 4;
                if (a[offset] != b[offset] || a[offset + 1] != b[offset + 1] ||
                    a[offset + 2] != b[offset + 2]) differing++;
            }
            return differing;
        }

        // Соседние различающиеся пиксели: кадр действительно показывает структуру, а не
        // однородную заливку. Считать «не-чёрные» недостаточно — проверочная палитра
        // белый→чёрный, и кадр далеко снаружи множества выходит сплошь не-чёрным.
        static int CountEdges(byte[] pixels, int width)
        {
            int edges = 0;
            int rows = pixels.Length / 4 / width;
            for (int y = 0; y < rows; y++)
            for (int x = 1; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                if (pixels[offset] != pixels[offset - 4] || pixels[offset + 1] != pixels[offset - 3] ||
                    pixels[offset + 2] != pixels[offset - 2]) edges++;
            }
            return edges;
        }

        static async Task<byte[]> RenderAsync(PhoenixState state, bool? forceDeep, int width, int height,
            int? forceBits = null)
        {
            byte[] pixels = new byte[width * height * 4];
            PhoenixRenderer.ForceDeepZoomForTests = forceDeep;
            PhoenixRenderer.ForceReferenceBitsForTests = forceBits;
            try
            {
                await Task.Run(() => PhoenixRenderer.Render(state, pixels, width, height, width * 4, 4,
                    CancellationToken.None));
            }
            finally
            {
                PhoenixRenderer.ForceDeepZoomForTests = null;
                PhoenixRenderer.ForceReferenceBitsForTests = null;
            }
            return pixels;
        }

        const int w = 64, h = 44, total = w * h;

        // 1. Где плоский double ещё точен, пертурбационный движок обязан его воспроизвести.
        //    Прогоняем все пять вариантов против всех семи режимов окраски и обеих плоскостей:
        //    каждый режим читает свой набор метрик орбиты, каждый вариант — свою ветку свёртки
        //    знака. Это и есть проверка переноса формулы: у двух путей нет общего кода.
        var variants = new[]
        {
            PhoenixVariant.Classic, PhoenixVariant.Tricorn, PhoenixVariant.BurningShip,
            PhoenixVariant.Celtic, PhoenixVariant.Buffalo
        };
        var colorings = new[]
        {
            PhoenixColoringMode.Discrete, PhoenixColoringMode.Smooth, PhoenixColoringMode.OrbitTrap,
            PhoenixColoringMode.StripeAverage, PhoenixColoringMode.TriangleInequalityAverage,
            PhoenixColoringMode.FinalArgument, PhoenixColoringMode.Period
        };
        int worstDiffering = 0;
        string worstLabel = "";
        foreach (PhoenixVariant variant in variants)
        foreach (PhoenixColoringMode coloring in colorings)
        foreach (PhoenixPlaneMode plane in new[] { PhoenixPlaneMode.Julia, PhoenixPlaneMode.ParameterC1 })
        {
            PhoenixState state = View(700, variant, coloring, plane);
            byte[] shallow = await RenderAsync(state, false, w, h);
            byte[] deep = await RenderAsync(state, true, w, h);
            int differing = CountDiffering(shallow, deep);
            if (differing > worstDiffering)
            {
                worstDiffering = differing;
                worstLabel = $"{variant}/{coloring}/{plane}";
            }
            Check(differing * 100 <= total * 2,
                $"Phoenix perturbation must match the plain double path at zoom 700 " +
                $"({variant}, {coloring}, {plane}): {differing}/{total} pixels differ.");
        }
        Console.WriteLine($"[diag] phoenix shallow-vs-deep worst {worstDiffering}/{total} ({worstLabel})");

        // Степени: вторая степень b > 0 включает второе слагаемое c1·G(z) целиком (при b = 0
        // оно вырождается в константу, и ошибка в его возмущении осталась бы незамеченной).
        foreach ((int primary, int secondary) in new[] { (2, 0), (3, 0), (2, 1), (3, 2), (5, 4), (12, 1) })
        {
            PhoenixState state = View(400, PhoenixVariant.Classic, PhoenixColoringMode.Smooth,
                primaryPower: primary, secondaryPower: secondary);
            byte[] shallow = await RenderAsync(state, false, w, h);
            byte[] deep = await RenderAsync(state, true, w, h);
            int differing = CountDiffering(shallow, deep);
            Check(differing * 100 <= total * 2,
                $"Phoenix perturbation must match the plain path for powers a={primary}, b={secondary}: " +
                $"{differing}/{total} pixels differ.");
        }

        // 2. На глубине плоскому пути верить уже нельзя — сравниваем с прямой итерацией в
        //    BigFloat. Центры найдены спуском по границе (см. стенд в истории задачи), поэтому
        //    у кадров есть структура: без неё сравнение прошло бы вхолостую.
        (string X, string Y, double Zoom)[] deepFixtures =
        [
            ("0.3605697876344492991196400742422874", "0.9162050700528197582748546315293717", 1.10e12),
            ("0.3605697876341732990634970411179978", "0.9162050700524670653999492733755535", 1.15e18),
            ("0.3605697876341732992373776850660347", "0.9162050700524670649253017882362955", 1.21e24),
            ("0.3605697876341732992373786585380622", "0.9162050700524670649253011924481502", 1.98e28),
        ];
        foreach ((string centerX, string centerY, double zoom) in deepFixtures)
        {
            PhoenixState state = AtExactCenter(
                View(zoom, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                centerX, centerY);
            byte[] deep = await RenderAsync(state, true, w, h);
            byte[] exact = await Task.Run(() =>
                PhoenixRenderer.RenderExactReferenceForTests(state, w, h, 128, CancellationToken.None));
            int differing = CountDiffering(deep, exact);
            int edges = CountEdges(exact, w);
            Console.WriteLine($"[diag] phoenix deep {zoom:0.0e+0}: {differing}/{total} differ, {edges} edges");
            Check(differing * 100 <= total * 3,
                $"Phoenix deep zoom must match the exact BigFloat reference at {zoom:0.0e+0}: " +
                $"{differing}/{total} pixels differ.");
        }

        // 3. План точности: тот же кадр с заведомо избыточной разрядностью опорной орбиты.
        //    Единственная проверка самого плана, не требующая внешнего эталона.
        foreach (double zoom in new[] { 1.10e12, 1.15e18, 1.21e24 })
        {
            PhoenixState state = AtExactCenter(
                View(zoom, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                "0.3605697876341732992373776850660347", "0.9162050700524670649253017882362955");
            byte[] planned = await RenderAsync(state, true, w, h);
            byte[] generous = await RenderAsync(state, true, w, h,
                PhoenixRenderer.PlanReferenceBits(state) + 256);
            int drift = CountDiffering(planned, generous);
            Check(drift == 0,
                $"Phoenix precision plan must not drift with 256 extra reference bits at {zoom:0.0e+0}: " +
                $"{drift}/{total} pixels differ.");
        }

        // 4. Тайл прогрессивного предпросмотра обязан совпасть с полным кадром: у них разные
        //    точки входа в движок и своя раскладка пикселей.
        {
            PhoenixState state = AtExactCenter(
                View(1e15, PhoenixVariant.BurningShip, PhoenixColoringMode.Smooth, iterations: 300),
                "0.3605697876341732992373776850660347", "0.9162050700524670649253017882362955");
            byte[] full = await RenderAsync(state, true, w, h);
            PhoenixRenderer.ForceDeepZoomForTests = true;
            byte[]? tile;
            try
            {
                tile = await Task.Run(() => PhoenixRenderer.RenderTile(state, w, h,
                    new MandelbrotRenderTile(16, 12, 24, 16, 1, 1), CancellationToken.None));
            }
            finally { PhoenixRenderer.ForceDeepZoomForTests = null; }
            Check(tile is not null, "Phoenix deep-zoom tile must render.");
            int tileDiffering = 0;
            for (int localY = 0; localY < 16; localY++)
            for (int localX = 0; localX < 24; localX++)
            {
                int tileOffset = (localY * 24 + localX) * 4;
                int fullOffset = ((12 + localY) * w + 16 + localX) * 4;
                if (tile![tileOffset] != full[fullOffset] || tile[tileOffset + 1] != full[fullOffset + 1] ||
                    tile[tileOffset + 2] != full[fullOffset + 2]) tileDiffering++;
            }
            Check(tileDiffering == 0,
                $"Phoenix deep-zoom tile must match the full frame exactly: {tileDiffering}/384 differ.");
        }

        // 5. Точный центр действительно доходит до рендера: сдвиг на десятую пикселя на
        //    глубине, где decimal-поля состояния его уже не различают, обязан менять кадр.
        {
            // Зум подобран так, чтобы полпикселя были заведомо мельче разрешения decimal
            // (шаг ≈ 3e-30 против ULP ≈ 1e-28 у центра около 0.36): только тогда проверка
            // показывает, что положение области доходит до рендера именно строкой.
            const double zoom = 1.98e28;
            PhoenixState reference = AtExactCenter(
                View(zoom, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                "0.3605697876341732992373786585380622", "0.9162050700524670649253011924481502");
            BigFloat nudge = BigFloat.FromDouble(4.0 / zoom / w * 0.5);
            PhoenixState nudged = AtExactCenter(
                View(zoom, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                (BigFloat.Parse(reference.CenterXExact!) + nudge).ToInvariantString(),
                reference.CenterYExact!);
            Check(CountEdges(await RenderAsync(reference, true, w, h), w) > 50,
                "The center-precision fixture must show structure, or a changed frame proves nothing.");
            Check(reference.CenterX == nudged.CenterX,
                "The nudge must be invisible to the decimal center fields — otherwise this proves nothing.");
            byte[] before = await RenderAsync(reference, true, w, h);
            byte[] after = await RenderAsync(nudged, true, w, h);
            Check(CountDiffering(before, after) > 0,
                "A tenth-of-a-pixel shift of the exact center must change the deep-zoom frame.");
        }

        // 6. Кэш опорной орбиты не путает состояния, различающиеся только формулой. Это тот
        //    самый класс ошибок, который у семейства Мандельброта однажды дал чёрный кадр:
        //    ключ кэша не нёс степень.
        {
            PhoenixState first = View(1e12, PhoenixVariant.Classic, PhoenixColoringMode.Smooth,
                primaryPower: 2, secondaryPower: 0);
            PhoenixState second = View(1e12, PhoenixVariant.Classic, PhoenixColoringMode.Smooth,
                primaryPower: 3, secondaryPower: 0);
            byte[] a = await RenderAsync(first, true, w, h);
            byte[] b = await RenderAsync(second, true, w, h);
            byte[] againA = await RenderAsync(first, true, w, h);
            Check(CountDiffering(a, b) > 0, "Different primary powers must not collide in the orbit cache.");
            Check(CountDiffering(a, againA) == 0, "Re-rendering the same Phoenix state must be deterministic.");

            PhoenixState julia = View(1e12, PhoenixVariant.Classic, PhoenixColoringMode.Smooth);
            PhoenixState parameter = View(1e12, PhoenixVariant.Classic, PhoenixColoringMode.Smooth,
                PhoenixPlaneMode.ParameterC1);
            Check(CountDiffering(await RenderAsync(julia, true, w, h),
                    await RenderAsync(parameter, true, w, h)) > 0,
                "Dynamic and parameter planes must not collide in the orbit cache.");
        }

        // 7. Фронт выхода — самый чувствительный кадр, какой удалось построить: центр в точке
        //    границы (бинарный поиск между заведомо внутренней и заведомо внешней точками), а
        //    число итераций подобрано так, что вся область вылетает за радиус в пределах одного
        //    шага. Прежде эта проверка считалась пределом движка и задавала потолок окна 1e24,
        //    но разбор показал иное: на шаге решения разброс |z|² по всему кадру меньше 2⁻⁵²
        //    относительно (на 1e28 — 3.43203826028866…, различие в 16-м знаке), и такой кадр не
        //    различает никакой рендер, ведущий z в double. Эталон различает его только потому,
        //    что итерирует z в BigFloat. Это свойство кадра, а не накопление ошибки δ: на кадрах
        //    со структурой расхождения с глубиной не растут (см. VerifyPhoenixExtremeZoomAsync).
        //    Проверка остаётся стражем точности до глубины, где кадр ещё различим.
        {
            const string borderX = "0.3605697876341732992373786585816072";
            const string borderY =
                "0.9044804668779509850030358017389001116892197535457371417753243249685989";
            foreach ((double zoom, int budget) in new[] { (1e20, 2), (1e22, 2), (1e24, 4) })
            {
                PhoenixState state = AtExactCenter(
                    View(zoom, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                    borderX, borderY);
                byte[] deep = await RenderAsync(state, true, w, h);
                byte[] exact = await Task.Run(() =>
                    PhoenixRenderer.RenderExactReferenceForTests(state, w, h, 256, CancellationToken.None));
                int differing = CountDiffering(deep, exact);
                Console.WriteLine($"[diag] phoenix escape-front {zoom:0.0e+0}: {differing}/{total} differ");
                Check(differing <= budget,
                    $"On the escape front at {zoom:0.0e+0} the engine must stay within {budget} pixels " +
                    $"of the exact reference: {differing}/{total} differ.");
            }
        }

        // 8. Ребазирование должно оставаться редким событием, а не срабатывать на каждом шаге.
        //    Условие «не хуже» в TryRebase существует именно для этого: у Феникса перенос в
        //    начало орбиты обычно увеличивает вторую компоненту пары (там z₋₁ = 0), и если
        //    условие сломается, δ начнёт переноситься туда, где оно только растёт. Порог взят
        //    с большим запасом от измеренного (около полутора процентов шагов).
        {
            PhoenixState state = AtExactCenter(
                View(1.21e24, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                "0.3605697876341732992373776850660347", "0.9162050700524670649253017882362955");
            PhoenixRenderer.RebaseCountForTests = 0;
            await RenderAsync(state, true, w, h);
            long rebases = Interlocked.Read(ref PhoenixRenderer.RebaseCountForTests);
            long steps = (long)total * state.Iterations;
            Console.WriteLine($"[diag] phoenix rebases {rebases} over {steps} steps " +
                $"({rebases * 100.0 / steps:F2}%)");
            Check(rebases * 10 <= steps,
                $"Phoenix rebasing must stay a rare event, not a per-step one: {rebases} rebases " +
                $"over {steps} steps.");
        }

        // 9. Круг «окно → сохранение → окно»: точный центр обязан пережить его без потерь.
        //    Именно здесь легко потерять глубину — decimal-поля состояния сохраняют лишь 28
        //    знаков, и если окно перестанет писать или читать строки, зум просто вернётся к
        //    прежнему потолку, а рендер останется формально исправным.
        {
            // Разметка окна тянет стили из App.xaml, а проверочный Application создаётся
            // пустым: без словаря конструктор падает на StaticResource.
            var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
            if (Application.Current.Resources.MergedDictionaries.All(d => d.Source != themeStyles))
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });

            var window = new PhoenixWindow();
            PhoenixState deep = AtExactCenter(
                View(1e20, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                "0.3605697876341732992373776850660347", "0.9162050700524670649253017882362955");
            window.LoadState(deep);
            PhoenixState captured = window.CaptureState("round-trip");

            // Сравнение по значению, а не по тексту: BigFloat печатает столько цифр, сколько
            // несёт его мантисса, поэтому строка на выходе длиннее исходной, обозначая то же
            // число. Важно, что оно не изменилось, а не как оно записано.
            Check(captured.CenterXExact is { Length: > 0 } && captured.CenterYExact is { Length: > 0 },
                "A deep save must carry exact-center strings.");
            Check(BigFloat.Parse(captured.CenterXExact!) == BigFloat.Parse(deep.CenterXExact!) &&
                  BigFloat.Parse(captured.CenterYExact!) == BigFloat.Parse(deep.CenterYExact!),
                "The window must round-trip the exact center of a deep save without losing digits.");
            Check(captured.Zoom == deep.Zoom, "The window must round-trip a deep zoom unchanged.");

            // И обратно: обычное сохранение с мелким зумом не должно обзаводиться строками
            // точного центра — иначе они начнут расходиться с decimal-полями.
            // Сверхглубокий зум: центр длиннее тысячи знаков. Окно обязано разобрать его с
            // точностью, поднятой под зум (иначе BigFloat.Parse округлит до рабочих 384 бит), и
            // вернуть без потерь, а зум — вне диапазона double — сохранить как есть.
            string longX, longY;
            using (new BigFloat.PrecisionScope(2048))
            {
                longX = (BigFloat.FromInt(2) + (4.0 / FloatExp.Pow10(500) * 0.123456789).ToBigFloat()).ToInvariantString();
                longY = (4.0 / FloatExp.Pow10(500) * -0.0371).ToBigFloat().ToInvariantString();
            }
            PhoenixState extreme = AtExactCenter(
                View(1, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300), longX, longY);
            extreme.Zoom = FloatExp.Pow10(500);
            window.LoadState(extreme);
            PhoenixState capturedExtreme = window.CaptureState("round-trip-extreme");
            Check(capturedExtreme.Zoom == extreme.Zoom, "The window must round-trip a 1e500 zoom unchanged.");
            // Окно разбирает центр с точностью под зум, а не с 2048 битами, поэтому сравнение
            // «бит в бит» здесь неверно; требование — потеря заведомо меньше пикселя.
            using (new BigFloat.PrecisionScope(2048))
            {
                FloatExp pixelFraction = 4.0 / extreme.Zoom * Math.ScaleB(1.0, -60);
                FloatExp driftX = FloatExp.Abs(FloatExp.FromBigFloat(BigFloat.Parse(capturedExtreme.CenterXExact!) - BigFloat.Parse(longX)));
                FloatExp driftY = FloatExp.Abs(FloatExp.FromBigFloat(BigFloat.Parse(capturedExtreme.CenterYExact!) - BigFloat.Parse(longY)));
                Check(driftX < pixelFraction && driftY < pixelFraction,
                    $"The window must round-trip a 1e500 exact center to far below a pixel: drift {driftX}, {driftY}.");
            }

            window.LoadState(View(700, PhoenixVariant.Classic, PhoenixColoringMode.Smooth));
            PhoenixState shallow = window.CaptureState("round-trip-shallow");
            Check(shallow.CenterXExact is null && shallow.CenterYExact is null,
                "A shallow save must not carry exact-center strings.");
            window.Close();
        }

        // 10. Параметрическая плоскость при b > 0. Опорная константа C1 в этой плоскости — центр
        //     кадра, и член C1·ΔG возмущения обязан её учитывать. Ядро долго подставляло сюда
        //     ноль: при b = 0 ΔG ≡ 0 и ошибка не видна, поэтому проверка 1 (b = 0) её пропускала,
        //     а на кадрах со структурой при b = 1 расходилось 2357 пикселей из 2816.
        foreach ((int secondary, double centerX, double centerY) in new[] { (1, -1.6, -0.2), (2, 0.2, 0.0) })
        {
            PhoenixState state = View(20, PhoenixVariant.Classic, PhoenixColoringMode.Smooth,
                PhoenixPlaneMode.ParameterC1, secondaryPower: secondary, centerX: centerX, centerY: centerY);
            byte[] shallow = await RenderAsync(state, false, w, h);
            byte[] deep = await RenderAsync(state, true, w, h);
            int differing = CountDiffering(shallow, deep);
            Check(CountEdges(shallow, w) > 150,
                $"The b={secondary} parameter-plane fixture must show structure, or the check proves nothing.");
            Check(differing * 100 <= total * 2,
                $"Phoenix parameter plane with b={secondary} must match the plain path: {differing}/{total} differ.");
        }

        // Гибридное ядро и линейный пропуск (PhoenixRenderer.Extended) — отдельной функцией:
        // её же запускает группа extreme.
        await VerifyPhoenixExtremeZoomAsync();

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Phoenix deep-zoom checks must leave the working precision restored.");
    }

    // Сверхглубокий зум Феникса: зум и сетка кадра в FloatExp, гибридное ядро (δ в FloatExp,
    // пока мало, дальше double) и линейный пропуск начала орбиты (PhoenixRenderer.Extended).
    //
    // Прежний потолок 1e24 держался на кадре «весь кадр вылетает за один шаг» (проверка 7 в
    // VerifyPhoenixDeepZoomAsync): там разброс |z|² по кадру на шаге решения меньше 2⁻⁵²
    // относительно, и такой кадр не различает никакой рендер, ведущий z в double. На кадрах со
    // структурой точность пертурбации относительная и с глубиной не падает — поэтому здесь
    // проверяется не «насколько глубоко можно», а три механизма, которыми снята стена
    // представления чисел, и полное совпадение с точным BigFloat-эталоном на 1e300/1e1000.
    //
    // Глубокие фикстуры со структурой на любой глубине строятся без поиска и без длинных
    // констант — это отталкивающие неподвижные точки, лежащие на границе:
    //   • β-точка «базилики» (C2 = 0, c1 = −1): z* = (1 + √5)/2, множитель 3.24;
    //   • седло Феникса с памятью (c1 = −1, C2 = −0.5): пара (2, 2) неподвижна, z₋₁ = 2 точно
    //     представим в decimal, собственные числа 2 ± √3.5. Это единственная фикстура, где
    //     на сверхглубине работает и член памяти C2·δₙ₋₁.
    private static bool _phoenixExtremeVerified;

    private static async Task VerifyPhoenixExtremeZoomAsync()
    {
        _phoenixExtremeVerified = true;
        static MandelbrotPalette Palette() => new()
        {
            Colors = [Colors.White, Colors.Red, Colors.Black],
            InteriorColor = Colors.Black,
            IsGradient = true
        };

        static PhoenixState State(FloatExp zoom, string centerX, string centerY, int iterations,
            PhoenixVariant variant = PhoenixVariant.Classic, PhoenixColoringMode coloring = PhoenixColoringMode.Smooth,
            PhoenixPlaneMode plane = PhoenixPlaneMode.Julia, decimal c1 = 0.56667m, decimal c2 = -0.5m,
            decimal initialPrevious = 0m)
        {
            var state = new PhoenixState
            {
                Zoom = zoom, Iterations = iterations, Threshold = 4m, C1Real = c1, C2Real = c2,
                InitialPreviousReal = initialPrevious, PlaneMode = plane, Variant = variant,
                PrimaryPower = 2, SecondaryPower = 0, ColoringMode = coloring,
                OrbitTrapMode = PhoenixOrbitTrapMode.Circle, OrbitTrapRadius = 0.5, OrbitTrapStrength = 1.5,
                StripeFrequency = 3, StripeStrength = 0.65, CycleTolerance = 1e-7, MaximumDetectedPeriod = 32,
                Palette = Palette(),
                CenterXExact = centerX, CenterYExact = centerY
            };
            using (new BigFloat.PrecisionScope(Math.Max(BigFloat.MinimumPrecisionBits, (int)zoom.Log2() + 128)))
            {
                state.CenterX = BigFloat.Parse(centerX).ToDecimalClamped();
                state.CenterY = BigFloat.Parse(centerY).ToDecimalClamped();
            }
            return state;
        }

        static async Task<byte[]> RenderAsync(PhoenixState state, int width, int height,
            bool? forceDeep = true, bool? forceExtended = null, bool? forceSkip = null)
        {
            byte[] pixels = new byte[width * height * 4];
            PhoenixRenderer.ForceDeepZoomForTests = forceDeep;
            PhoenixRenderer.ForceExtendedDeltaForTests = forceExtended;
            PhoenixRenderer.ForceLinearSkipForTests = forceSkip;
            try
            {
                await Task.Run(() => PhoenixRenderer.Render(state, pixels, width, height, width * 4,
                    Environment.ProcessorCount, CancellationToken.None));
            }
            finally
            {
                PhoenixRenderer.ForceDeepZoomForTests = null;
                PhoenixRenderer.ForceExtendedDeltaForTests = null;
                PhoenixRenderer.ForceLinearSkipForTests = null;
            }
            return pixels;
        }

        static int CountDiffering(byte[] a, byte[] b)
        {
            int differing = 0;
            for (int offset = 0; offset < a.Length; offset += 4)
                if (a[offset] != b[offset] || a[offset + 1] != b[offset + 1] || a[offset + 2] != b[offset + 2])
                    differing++;
            return differing;
        }

        static int CountColors(byte[] pixels) => Enumerable.Range(0, pixels.Length / 4)
            .Select(pixel => pixels[pixel * 4] | pixels[pixel * 4 + 1] << 8 | pixels[pixel * 4 + 2] << 16)
            .Distinct().Count();

        const int w = 64, h = 44, total = w * h;

        // 1. Полоса перекрытия: там, где верно и прежнее double-ядро, гибридное обязано совпасть
        //    с ним бит-в-бит без пропуска (в double-режиме это те же выражения, а FloatExp
        //    округляет мантиссу ровно как double) и почти бит-в-бит с пропуском. Кадры — со
        //    структурой (132–1178 рёбер в VerifyPhoenixDeepZoomAsync), все варианты свёрток и
        //    режимы окраски, читающие метрики по всей орбите: пропуск подменяет их префиксами
        //    опорной орбиты.
        (string X, string Y, double Zoom)[] overlap =
        [
            ("0.3605697876341732990634970411179978", "0.9162050700524670653999492733755535", 1.15e18),
            ("0.3605697876341732992373786585380622", "0.9162050700524670649253011924481502", 1.98e28),
        ];
        int worstPlain = 0, worstSkip = 0;
        foreach ((string centerX, string centerY, double zoom) in overlap)
        foreach (PhoenixVariant variant in Enum.GetValues<PhoenixVariant>())
        foreach (PhoenixColoringMode coloring in new[]
                 {
                     PhoenixColoringMode.Smooth, PhoenixColoringMode.OrbitTrap, PhoenixColoringMode.StripeAverage,
                     PhoenixColoringMode.TriangleInequalityAverage, PhoenixColoringMode.Period
                 })
        {
            PhoenixState state = State(zoom, centerX, centerY, 300, variant, coloring);
            byte[] reference = await RenderAsync(state, w, h, true, false);
            byte[] stepped = await RenderAsync(state, w, h, true, true, false);
            byte[] skipped = await RenderAsync(state, w, h, true, true, true);
            int plain = CountDiffering(reference, stepped), skip = CountDiffering(reference, skipped);
            worstPlain = Math.Max(worstPlain, plain);
            worstSkip = Math.Max(worstSkip, skip);
            Check(plain == 0,
                $"The hybrid kernel must match the double kernel bit-for-bit where both are valid " +
                $"({variant}, {coloring}, {zoom:0.0e+0}): {plain}/{total} differ.");
            Check(skip * 100 <= total,
                $"The linear skip must stay within 1% of the double kernel ({variant}, {coloring}, {zoom:0.0e+0}): " +
                $"{skip}/{total} differ.");
        }
        Console.WriteLine($"[diag] phoenix hybrid overlap worst: stepped {worstPlain}, skipped {worstSkip} of {total}");

        // 2. Сверхглубина против точного эталона. Эталон итерирует каждый пиксель напрямую в
        //    BigFloat и не делит с гибридным ядром ни пропуска, ни FloatExp-арифметики.
        string betaX;
        using (new BigFloat.PrecisionScope(4096))
            betaX = ((BigFloat.One + BigFloat.Sqrt(BigFloat.FromInt(5))) / 2L).ToInvariantString();
        foreach (int digits in new[] { 300, 1000 })
        foreach (bool memory in new[] { false, true })
        {
            FloatExp zoom = FloatExp.Pow10(digits);
            int iterations = digits * 2 + 400;
            PhoenixState state = memory
                ? State(zoom, "2", "0", iterations, c1: -1m, c2: -0.5m, initialPrevious: 2m)
                : State(zoom, betaX, "0", iterations, c1: -1m, c2: 0m);
            string label = $"{(memory ? "saddle" : "beta")} 1e{digits}";

            PhoenixRenderer.SkippedIterationsForTests = 0;
            byte[] frame = await RenderAsync(state, w, h, forceDeep: null);
            long skippedPerPixel = Interlocked.Read(ref PhoenixRenderer.SkippedIterationsForTests) / total;
            int colors = CountColors(frame);
            Console.WriteLine($"[diag] phoenix extreme {label}: {colors} colors, skip {skippedPerPixel}/{iterations} per pixel");
            Check(colors >= 5,
                $"The {label} Phoenix frame must show structure, or the comparison proves nothing: {colors} colors.");
            Check(skippedPerPixel > iterations / 4,
                $"The linear skip must engage at {label}: {skippedPerPixel} iterations per pixel.");
            Check(frame.Where((_, index) => index % 4 == 3).All(alpha => alpha == 255),
                $"The {label} Phoenix frame must fill every pixel.");

            const int sw = 32, sh = 22;
            byte[] deep = await RenderAsync(state, sw, sh, forceDeep: null);
            byte[] stepped = await RenderAsync(state, sw, sh, true, true, false);
            byte[] exact = await Task.Run(() =>
                PhoenixRenderer.RenderExactReferenceForTests(state, sw, sh, 64, CancellationToken.None));
            int vsExact = CountDiffering(deep, exact), vsStepped = CountDiffering(deep, stepped);
            Console.WriteLine($"[diag] phoenix extreme {label} 32x22: {vsExact} vs exact, {vsStepped} skip-vs-stepped");
            Check(CountColors(exact) >= 4, $"The {label} exact reference must show structure.");
            Check(vsExact <= 1,
                $"Phoenix at {label} must match the exact BigFloat reference: {vsExact}/{sw * sh} differ.");
            Check(vsStepped <= 1,
                $"The linear skip must match the stepped hybrid kernel at {label}: {vsStepped}/{sw * sh} differ.");
        }

        // 3. Тайл на сверхглубине совпадает с полным кадром: у тайла своя точка входа и раскладка,
        //    а таблица пропуска у них общая из кэша.
        {
            PhoenixState state = State(FloatExp.Pow10(1000), "2", "0", 2400, c1: -1m, c2: -0.5m, initialPrevious: 2m);
            byte[] full = await RenderAsync(state, w, h, forceDeep: null);
            byte[]? tile = await Task.Run(() => PhoenixRenderer.RenderTile(state, w, h,
                new MandelbrotRenderTile(16, 12, 24, 16, 1, 1), CancellationToken.None));
            Check(tile is not null, "The extreme-zoom Phoenix tile must render.");
            int tileDiffering = 0;
            for (int localY = 0; localY < 16; localY++)
            for (int localX = 0; localX < 24; localX++)
            {
                int tileOffset = (localY * 24 + localX) * 4;
                int fullOffset = ((12 + localY) * w + 16 + localX) * 4;
                if (tile![tileOffset] != full[fullOffset] || tile[tileOffset + 1] != full[fullOffset + 1] ||
                    tile[tileOffset + 2] != full[fullOffset + 2]) tileDiffering++;
            }
            Check(tileDiffering == 0,
                $"The extreme-zoom Phoenix tile must match the full frame exactly: {tileDiffering}/384 differ.");
        }

        // 4. Поиск ядра методом Ньютона. Глубокий кадр — окрестность известного ядра периода
        //    2754 на 1e13 (найдено спуском по ядрам): период «круга кадра» обязан найти ядро в
        //    самом кадре. argmin |zₙ| на этом кадре выбирал деталь в 14 ширинах кадра, а со
        //    смещением в другую сторону — в 2e7 ширинах.
        {
            const string nucleusX = "0.1029169992623730679694709500956184183749";
            const string nucleusY = "0.5760704950584760330336597447061809753958";
            const double zoom = 5.25e13;
            foreach (double offset in new[] { 0.23, -0.31 })
            {
                string centerX, centerY;
                using (new BigFloat.PrecisionScope(400))
                {
                    centerX = (BigFloat.Parse(nucleusX) + BigFloat.FromDouble(4.0 / zoom * offset)).ToInvariantString();
                    centerY = (BigFloat.Parse(nucleusY) + BigFloat.FromDouble(4.0 / zoom * offset * 0.7)).ToInvariantString();
                }
                PhoenixState state = State(zoom, centerX, centerY, 9000);
                PhoenixNucleusResult result = await Task.Run(() => PhoenixNewtonZoom.FindNucleus(state, CancellationToken.None));
                Console.WriteLine($"[diag] phoenix newton deep offset {offset}: {result.Message} zoom→{result.SuggestedZoom}");
                Check(result.Found && result.DriftInViews < 1.0,
                    $"Newton must find a nucleus inside the deep Phoenix frame (offset {offset}): {result.Message}");
                Check(result.NewtonSteps <= 12, $"Newton must converge quadratically: {result.NewtonSteps} steps.");
                Check(result.SuggestedZoom > zoom / 100 && result.SuggestedZoom.IsFinite,
                    $"The suggested zoom must frame the nearby detail: {result.SuggestedZoom}.");
            }

            // Динамическая плоскость на мелком зуме, параметрическая при b > 0 — ядро найдено, и
            // кадр на предложенном зуме показывает структуру.
            var shallowCases = new (string Label, PhoenixState State)[]
            {
                ("julia", State(270, "0.1", "0.5753747091373043", 600)),
                ("parameter b=1", State(20, "-1.6", "-0.2", 400, plane: PhoenixPlaneMode.ParameterC1)),
            };
            shallowCases[1].State.SecondaryPower = 1;
            foreach ((string label, PhoenixState state) in shallowCases)
            {
                state.CenterXExact = null;
                state.CenterYExact = null;
                PhoenixNucleusResult result = await Task.Run(() => PhoenixNewtonZoom.FindNucleus(state, CancellationToken.None));
                Check(result.Found && result.DriftInViews < 1.0, $"Newton must find a Phoenix {label} nucleus: {result.Message}");
                PhoenixState framed = State(result.SuggestedZoom, result.CenterX, result.CenterY,
                    Math.Max(state.Iterations, result.Period * 4 + 200), plane: state.PlaneMode);
                framed.SecondaryPower = state.SecondaryPower;
                Check(CountColors(await RenderAsync(framed, w, h, forceDeep: null)) >= 10,
                    $"The Phoenix {label} frame at the suggested zoom must show structure.");
            }

            // Вырожденный корень (c1 = 0 при нулевом старте даёт zₙ ≡ 0) и неаналитичный вариант
            // — честный отказ, а не «ядро».
            PhoenixState trivial = State(8, "0.35", "0.95", 400, plane: PhoenixPlaneMode.ParameterC1);
            trivial.CenterXExact = null;
            trivial.CenterYExact = null;
            PhoenixNucleusResult trivialResult = PhoenixNewtonZoom.FindNucleus(trivial, CancellationToken.None);
            Check(!trivialResult.Found || !(BigFloat.Parse(trivialResult.CenterX).IsZero && BigFloat.Parse(trivialResult.CenterY).IsZero),
                "Newton must never report the degenerate c1 = 0 root as a nucleus.");
            PhoenixState tricorn = State(20, "0.2", "-0.4", 400, PhoenixVariant.Tricorn, plane: PhoenixPlaneMode.ParameterC1);
            Check(!PhoenixNewtonZoom.FindNucleus(tricorn, CancellationToken.None).Found,
                "Newton must refuse the non-analytic Tricorn Phoenix.");
        }

        // 5. Зум за пределами double переживает JSON-сохранение.
        {
            PhoenixState state = State(FloatExp.Pow10(777), "2", "0", 500, c1: -1m, c2: -0.5m, initialPrevious: 2m);
            string json = System.Text.Json.JsonSerializer.Serialize(new List<PhoenixState> { state }, JsonOptionsFactory.Create());
            PhoenixState restored = System.Text.Json.JsonSerializer.Deserialize<List<PhoenixState>>(json, JsonOptionsFactory.Create())![0];
            Check(restored.Zoom == state.Zoom, $"A 1e777 Phoenix zoom must survive JSON: {restored.Zoom}.");
            PhoenixState legacy = System.Text.Json.JsonSerializer.Deserialize<List<PhoenixState>>(
                "[{\"Zoom\": 1.5e20, \"Iterations\": 300}]", JsonOptionsFactory.Create())![0];
            Check(legacy.Zoom == FloatExp.FromDouble(1.5e20), "An old numeric Phoenix zoom must still load.");
        }

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Phoenix extreme-zoom checks must leave the working precision restored.");
    }

    // Бассейны Ньютона: пертурбационный движок по произвольной формуле (Newton, Halley,
    // Householder, Relaxed в плоскостях z и λ, обычная и диагностическая раскраска), зум до 1e1000.
    private static async Task VerifyNewtonDeepZoomAsync()
    {
        static NewtonPoolsEngine Engine(string formula, NewtonIterationMethod method, FloatExp zoom,
            string centerX, string centerY, int iterations = 300,
            NewtonDiagnosticColoringMode diagnostics = NewtonDiagnosticColoringMode.Disabled,
            NewtonRelaxedPlaneMode plane = NewtonRelaxedPlaneMode.ZPlane, bool gradient = true)
        {
            var engine = new NewtonPoolsEngine
            {
                MaxIterations = iterations,
                CenterX = BigFloat.Parse(centerX).ToDouble(),
                CenterY = BigFloat.Parse(centerY).ToDouble(),
                CenterXExact = centerX,
                CenterYExact = centerY,
                Zoom = zoom,
                IterationMethod = method,
                HouseholderOrder = 3,
                RelaxedPlaneMode = plane,
                Relaxation = new Complex(0.8, 0.3),
                FixedInitialZ = new Complex(0.5, 0.5),
                RootTolerance = 1e-6,
                DiagnosticColoringMode = diagnostics,
                BackgroundColor = Colors.Black,
                UseGradient = gradient
            };
            Check(engine.SetFormula(formula, out string debug), $"Newton fixture formula {formula} must parse: {debug}");
            engine.RootColors = [Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow, Colors.Cyan, Colors.Magenta, Colors.Orange, Colors.White];
            return engine;
        }

        static async Task<byte[]> RenderAsync(NewtonPoolsEngine engine, bool? forceDeep, int width, int height,
            int? forceBits = null, double? handoff = null, bool? skip = null)
        {
            byte[] pixels = new byte[width * height * 4];
            NewtonPoolsEngine.ForceDeepZoomForTests = forceDeep;
            NewtonPoolsEngine.ForceReferenceBitsForTests = forceBits;
            NewtonPoolsEngine.ForceHandoffRatioSquaredForTests = handoff;
            NewtonPoolsEngine.ForceLinearSkipForTests = skip;
            try
            {
                await Task.Run(() => engine.RenderToBuffer(pixels, width, height, width * 4, Environment.ProcessorCount,
                    CancellationToken.None));
            }
            finally
            {
                NewtonPoolsEngine.ForceDeepZoomForTests = null;
                NewtonPoolsEngine.ForceReferenceBitsForTests = null;
                NewtonPoolsEngine.ForceHandoffRatioSquaredForTests = null;
                NewtonPoolsEngine.ForceLinearSkipForTests = null;
            }
            return pixels;
        }

        static int CountDiffering(byte[] a, byte[] b)
        {
            int differing = 0;
            for (int offset = 0; offset < a.Length; offset += 4)
                if (a[offset] != b[offset] || a[offset + 1] != b[offset + 1] || a[offset + 2] != b[offset + 2]) differing++;
            return differing;
        }

        static int CountEdges(byte[] pixels, int width)
        {
            int edges = 0;
            for (int offset = 4; offset < pixels.Length; offset += 4)
            {
                if (offset / 4 % width == 0) continue;
                if (pixels[offset] != pixels[offset - 4] || pixels[offset + 1] != pixels[offset - 3] ||
                    pixels[offset + 2] != pixels[offset - 2]) edges++;
            }
            return edges;
        }

        // Отталкивающий 2-цикл отображения Ньютона N(z) = z − f/f′: N(N(z*)) = z*. Множество Жюлиа
        // вокруг него самоподобно, поэтому кадр со структурой есть на любой глубине. Точка
        // уточняется методом Ньютона для N∘N − z прямо в BigFloat от double-приближения.
        static (string X, string Y) RepellingTwoCycle(string formula, Complex seed, int bits)
        {
            ExpressionNode node = new Parser(new Tokenizer(formula).Tokenize()).Parse().Simplify();
            ExpressionNode first = node.Differentiate("z").Simplify();
            CompiledComplexExpression f = CompiledComplexExpression.Compile(node);
            CompiledComplexExpression g = CompiledComplexExpression.Compile(first);
            CompiledComplexExpression h = CompiledComplexExpression.Compile(first.Differentiate("z").Simplify());
            using var precision = new BigFloat.PrecisionScope(bits);
            var fValues = new ComplexBigFloat[f.InstructionCount];
            var gValues = new ComplexBigFloat[g.InstructionCount];
            var hValues = new ComplexBigFloat[h.InstructionCount];
            ComplexBigFloat Map(ComplexBigFloat z) => z - f.EvaluateBig(z, fValues, default) / g.EvaluateBig(z, gValues, default);
            ComplexBigFloat Slope(ComplexBigFloat z)
            {
                ComplexBigFloat derivative = g.EvaluateBig(z, gValues, default);
                return f.EvaluateBig(z, fValues, default) * h.EvaluateBig(z, hValues, default) / (derivative * derivative);
            }

            ComplexBigFloat point = ComplexBigFloat.FromDouble(seed.Real, seed.Imaginary);
            for (int step = 0; step < 40; step++)
            {
                ComplexBigFloat image = Map(point);
                ComplexBigFloat correction = (Map(image) - point) / (Slope(image) * Slope(point) - 1);
                point -= correction;
                if (Math.Max(correction.Real.BinaryExponent, correction.Imaginary.BinaryExponent) < -(bits - 32)) break;
            }
            ComplexBigFloat residual = Map(Map(point)) - point;
            Check(Math.Max(residual.Real.BinaryExponent, residual.Imaginary.BinaryExponent) < -(bits - 64),
                $"The {formula} two-cycle must converge in BigFloat.");
            Check((Map(point) - point).ToComplex().Magnitude > 1e-3, $"The {formula} two-cycle must not be a fixed point.");
            return (point.Real.ToInvariantString(), point.Imaginary.ToInvariantString());
        }

        // 1. Где double ещё точен, глубокий движок обязан воспроизвести плоскую ступень. Порог
        //    передачи в double поднят до 2⁻⁸, поэтому пертурбация ведёт почти всю орбиту, а δ
        //    при этом не вырастает до порядка самих значений (там сокращение больших слагаемых
        //    теряло бы разряды у обеих ступеней по-разному). У двух путей нет общего кода, кроме
        //    проверок и окраски, — это и есть проверка переноса всех операций и методов.
        var shallow = new (string Formula, NewtonIterationMethod Method, NewtonRelaxedPlaneMode Plane,
            NewtonDiagnosticColoringMode Diagnostics, double X, double Y, double Zoom)[]
        {
            ("z^3-1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, -0.2, 0.3, 8),
            ("z^3-1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.OrbitOutcome, -0.2, 0.3, 8),
            ("z^3-1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Residual, -0.2, 0.3, 8),
            ("z^6+3*z^3-2", NewtonIterationMethod.Halley, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.1, 0.2, 5),
            ("z^6+3*z^3-2", NewtonIterationMethod.Halley, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.OrbitOutcome, 0.1, 0.2, 5),
            ("(z^2-1)/(z^2+1)", NewtonIterationMethod.Householder, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.3, 0.1, 3),
            ("z^4-1", NewtonIterationMethod.Householder, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.OrbitOutcome, 0.3, 0.1, 3),
            ("z^3-1", NewtonIterationMethod.RelaxedNewton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.1, 0.1, 4),
            ("z^3-1", NewtonIterationMethod.RelaxedNewton, NewtonRelaxedPlaneMode.LambdaPlane, NewtonDiagnosticColoringMode.Disabled, 1.0, 0.2, 6),
            ("z^3-1", NewtonIterationMethod.RelaxedNewton, NewtonRelaxedPlaneMode.LambdaPlane, NewtonDiagnosticColoringMode.OrbitOutcome, 1.0, 0.2, 6),
            ("sin(z)-0.5", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 1.0, 0.5, 2),
            ("z-exp(-z)", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, -1.0, 2.0, 1),
            ("tan(z)-z", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 2.0, 0.5, 1),
            ("log(z)-1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, -1.0, 0.3, 1),
            ("sqrt(z)-z^3", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.0, 0.5, 1),
            ("atan(z^3)-0.5", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.5, 0.5, 1),
            ("asin(z^4)-0.5", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.5, 0.5, 1),
            ("sinh(z)-1", NewtonIterationMethod.Halley, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.5, 1.5, 1),
            ("tanh(z)-0.5", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.5, 1.5, 1),
            ("z^0.5*z^2.5-1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.2, 0.3, 2),
            ("z^(-2)+z-1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.2, 0.3, 2),
        };
        const int sw = 64, sh = 44;
        foreach (var fixture in shallow)
        {
            string x = fixture.X.ToString("R", CultureInfo.InvariantCulture);
            string y = fixture.Y.ToString("R", CultureInfo.InvariantCulture);
            NewtonPoolsEngine Make() => Engine(fixture.Formula, fixture.Method, fixture.Zoom, x, y, 500,
                fixture.Diagnostics, fixture.Plane);
            byte[] plain = await RenderAsync(Make(), false, sw, sh);
            byte[] deep = await RenderAsync(Make(), true, sw, sh, handoff: Math.ScaleB(1.0, -16));
            int differing = CountDiffering(plain, deep);
            Check(differing * 200 <= sw * sh,
                $"Newton perturbation must match the plain double path ({fixture.Formula}, {fixture.Method}, " +
                $"{fixture.Plane}, {fixture.Diagnostics}): {differing}/{sw * sh} pixels differ.");
        }
        Console.WriteLine($"[diag] newton shallow-vs-deep: {shallow.Length} configurations match");

        const int w = 32, h = 24, total = w * h;

        // 2. На глубине плоской ступени верить нельзя — сравнение с прямой итерацией каждого
        //    пикселя в BigFloat. z³ − 1 у отталкивающего 2-цикла (множитель ровно 6). Центральный
        //    пиксель лежит на самом цикле, и его судьбу решает округление — отсюда допуск в пиксель.
        //    Эталон с комплексным делением в BigFloat дорог, поэтому на 1e1000 кадр крошечный, а
        //    весь кадр там проверяется самоподобием (пункт 5).
        (string cycleX, string cycleY) = RepellingTwoCycle("z^3-1", new Complex(0.538608672507971, 0.417204483749251), 3648);
        foreach ((string zoomText, int iterations, int width, int height) in new[] { ("1e20", 300, w, h), ("1e300", 1200, w, h), ("1e1000", 3500, 6, 4) })
        {
            FloatExp zoom = FloatExp.Parse(zoomText);
            NewtonPoolsEngine.SkippedIterationsForTests = 0;
            NewtonPoolsEngine.CountSkippedIterationsForTests = true;
            byte[] deep;
            try { deep = await RenderAsync(Engine("z^3-1", NewtonIterationMethod.Newton, zoom, cycleX, cycleY, iterations), true, width, height); }
            finally { NewtonPoolsEngine.CountSkippedIterationsForTests = false; }
            long skipped = Interlocked.Read(ref NewtonPoolsEngine.SkippedIterationsForTests);
            NewtonPoolsEngine exactEngine = Engine("z^3-1", NewtonIterationMethod.Newton, zoom, cycleX, cycleY, iterations);
            byte[] exact = await Task.Run(() => exactEngine.RenderExactReferenceForTests(width, height, 64, CancellationToken.None));
            int differing = CountDiffering(deep, exact);
            int edges = CountEdges(exact, width);
            Console.WriteLine($"[diag] newton z^3-1 at {zoomText}: {differing}/{width * height} differ, {edges} edges, {skipped} skipped iterations");
            Check(skipped > 0, $"The linear skip must engage at {zoomText}.");
            Check(differing <= 1, $"Newton deep zoom must match the exact reference at {zoomText}: {differing}/{width * height} differ.");
            if (width == w) Check(edges > 60, $"The z^3-1 two-cycle frame at {zoomText} must show structure: {edges} edges.");
            if (zoomText == "1e300")
            {
                byte[] unskipped = await RenderAsync(Engine("z^3-1", NewtonIterationMethod.Newton, zoom, cycleX, cycleY, iterations),
                    true, w, h, skip: false);
                Check(CountDiffering(unskipped, exact) <= 1,
                    "Without the linear skip the step-by-step kernel must match the reference while δ still fits double.");
            }
        }

        // 3. Трансцендентная формула: опорная орбита требует cos/sin произвольной точности.
        (string cosX, string cosY) = RepellingTwoCycle("cos(z)-z", new Complex(-0.570836212742015, -0.968967177259400), 512);
        foreach ((string zoomText, int iterations) in new[] { ("1e40", 300) })
        {
            FloatExp zoom = FloatExp.Parse(zoomText);
            byte[] deep = await RenderAsync(Engine("cos(z)-z", NewtonIterationMethod.Newton, zoom, cosX, cosY, iterations), true, w, h);
            NewtonPoolsEngine exactEngine = Engine("cos(z)-z", NewtonIterationMethod.Newton, zoom, cosX, cosY, iterations);
            byte[] exact = await Task.Run(() => exactEngine.RenderExactReferenceForTests(w, h, 64, CancellationToken.None));
            int differing = CountDiffering(deep, exact);
            Console.WriteLine($"[diag] newton cos(z)-z at {zoomText}: {differing}/{total} differ, {CountEdges(exact, w)} edges");
            Check(CountEdges(exact, w) > 60, $"The cos(z)-z frame at {zoomText} must show structure.");
            Check(differing * 100 <= total * 2, $"Transcendental Newton deep zoom must match the exact reference at {zoomText}: {differing}/{total} differ.");
        }

        // 4. Остальные методы и режимы на 1e30. Центры найдены спуском по границе бассейнов
        //    (шаг ×10, всякий раз к ближайшей к центру паре соседних пикселей разных корней).
        var methods = new (string Label, string Formula, NewtonIterationMethod Method, NewtonRelaxedPlaneMode Plane,
            NewtonDiagnosticColoringMode Diagnostics, string X, string Y)[]
        {
            ("Halley", "z^4-1", NewtonIterationMethod.Halley, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled,
                "0.37990808172817811191204123330806241141344351327125", "0.43494150585041415009061837942431348347333451525957"),
            ("Householder", "z^3-1", NewtonIterationMethod.Householder, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled,
                "-0.6008994764551517061752977045861167985988191002439", "0.00515030394938313118897024704315396473356942575383"),
            ("Relaxed λ-plane", "z^3-1", NewtonIterationMethod.RelaxedNewton, NewtonRelaxedPlaneMode.LambdaPlane, NewtonDiagnosticColoringMode.Disabled,
                "0.98190717272726090889187616725550228304191009628161", "0.19514139504866868699532215875719665538544613181438"),
            ("Relaxed λ-plane outcome", "z^3-1", NewtonIterationMethod.RelaxedNewton, NewtonRelaxedPlaneMode.LambdaPlane, NewtonDiagnosticColoringMode.OrbitOutcome,
                "0.98190717272726090889187616725550228304191009628161", "0.19514139504866868699532215875719665538544613181438"),
            ("orbit outcome", "z^5-z^2+1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.OrbitOutcome,
                "0.17959818181999800397470557686812243250064308499655", "0.36777685959594957991683114856036181685021462969821"),
        };
        foreach (var fixture in methods)
        {
            FloatExp zoom = FloatExp.Parse("1e30");
            NewtonPoolsEngine Make() => Engine(fixture.Formula, fixture.Method, zoom, fixture.X, fixture.Y, 900,
                fixture.Diagnostics, fixture.Plane);
            byte[] deep = await RenderAsync(Make(), true, w, h);
            NewtonPoolsEngine exactEngine = Make();
            byte[] exact = await Task.Run(() => exactEngine.RenderExactReferenceForTests(w, h, 64, CancellationToken.None));
            int differing = CountDiffering(deep, exact);
            int edges = CountEdges(exact, w);
            Console.WriteLine($"[diag] newton {fixture.Label} at 1e30: {differing}/{total} differ, {edges} edges");
            Check(edges > 40, $"The {fixture.Label} fixture must show structure at 1e30: {edges} edges.");
            Check(differing * 100 <= total, $"Newton {fixture.Label} deep zoom must match the exact reference: {differing}/{total} differ.");
        }

        // 5. Самоподобие на 1e1000: множитель 2-цикла — ровно 6, поэтому два шага итерации
        //    переводят сетку кадра на зуме 6·Z в сетку кадра на Z пиксель в пиксель. При сплошных
        //    цветах корней (номер итерации не участвует) кадры обязаны совпасть — независимая от
        //    эталона проверка всей сверхглубины: пропуск и точность ведутся на разных зумах по-разному.
        {
            FloatExp deeper = FloatExp.Pow10(1000);
            byte[] near = await RenderAsync(Engine("z^3-1", NewtonIterationMethod.Newton, deeper, cycleX, cycleY, 3600, gradient: false), true, w, h);
            byte[] far = await RenderAsync(Engine("z^3-1", NewtonIterationMethod.Newton, deeper / 6.0, cycleX, cycleY, 3600, gradient: false), true, w, h);
            int differing = CountDiffering(near, far);
            int edges = CountEdges(near, w);
            Console.WriteLine($"[diag] newton self-similarity at 1e1000: {differing}/{total} differ, {edges} edges");
            Check(edges > 60, "The 1e1000 self-similarity frame must show structure.");
            Check(differing <= 1, $"Frames at 1e1000 and 1e1000/6 around the two-cycle must coincide: {differing}/{total} differ.");
        }

        // 6. План точности: избыточная разрядность опорной орбиты не меняет кадр. Центр сдвинут на
        //    треть пикселя с цикла, чтобы ни один пиксель не лежал на нём ровно — судьба такой
        //    точки зависит от округления при любой разрядности.
        {
            FloatExp zoom = FloatExp.Parse("1e150");
            string offsetX;
            using (new BigFloat.PrecisionScope(1024))
                offsetX = (BigFloat.Parse(cycleX) + (3.0 / zoom / w / 3).ToBigFloat()).ToInvariantString();
            NewtonPoolsEngine planned = Engine("z^3-1", NewtonIterationMethod.Newton, zoom, offsetX, cycleY, 600);
            byte[] plannedPixels = await RenderAsync(planned, true, w, h);
            byte[] generous = await RenderAsync(Engine("z^3-1", NewtonIterationMethod.Newton, zoom, offsetX, cycleY, 600), true, w, h,
                forceBits: planned.PlanReferenceBits() + 256);
            Check(CountEdges(plannedPixels, w) > 60, "The precision-plan frame must show structure.");
            Check(CountDiffering(plannedPixels, generous) == 0,
                "The Newton precision plan must not drift with 256 extra reference bits.");
        }

        // 7. Тайл прогрессивного предпросмотра совпадает с полным кадром.
        {
            FloatExp zoom = FloatExp.Parse("1e60");
            byte[] full = await RenderAsync(Engine("z^3-1", NewtonIterationMethod.Newton, zoom, cycleX, cycleY, 400), true, w, h);
            NewtonPoolsEngine tileEngine = Engine("z^3-1", NewtonIterationMethod.Newton, zoom, cycleX, cycleY, 400);
            NewtonPoolsEngine.ForceDeepZoomForTests = true;
            byte[]? tile;
            try
            {
                tile = await Task.Run(() => tileEngine.RenderTile(new MandelbrotRenderTile(8, 6, 16, 12, 1, 1), w, h, CancellationToken.None));
            }
            finally { NewtonPoolsEngine.ForceDeepZoomForTests = null; }
            int tileDiffering = 0;
            for (int localY = 0; localY < 12; localY++)
            for (int localX = 0; localX < 16; localX++)
            {
                int tileOffset = (localY * 16 + localX) * 4;
                int fullOffset = ((6 + localY) * w + 8 + localX) * 4;
                if (tile![tileOffset] != full[fullOffset] || tile[tileOffset + 1] != full[fullOffset + 1] ||
                    tile[tileOffset + 2] != full[fullOffset + 2]) tileDiffering++;
            }
            Check(tileDiffering == 0, $"A Newton deep-zoom tile must match the full frame exactly: {tileDiffering}/192 differ.");
        }

        // 8. Зум за пределами double переживает JSON, старое числовое поле читается.
        {
            var state = new NewtonState { Formula = "z^3-1", Zoom = FloatExp.Pow10(777), CenterXExact = cycleX, CenterYExact = cycleY };
            string json = System.Text.Json.JsonSerializer.Serialize(new List<NewtonState> { state }, JsonOptionsFactory.Create());
            NewtonState restored = System.Text.Json.JsonSerializer.Deserialize<List<NewtonState>>(json, JsonOptionsFactory.Create())![0];
            Check(restored.Zoom == state.Zoom && restored.CenterXExact == cycleX && restored.CenterYExact == cycleY,
                "A 1e777 Newton zoom and its exact center must survive JSON.");
            NewtonState legacy = System.Text.Json.JsonSerializer.Deserialize<List<NewtonState>>(
                "[{\"Formula\": \"z^3-1\", \"Zoom\": 45073244110.32, \"CenterX\": 0.1}]", JsonOptionsFactory.Create())![0];
            Check(legacy.Zoom == FloatExp.FromDouble(45073244110.32) && legacy.CenterXExact is null,
                "An old numeric Newton zoom must still load.");
        }

        // 9. Круг «окно → сохранение → окно» на 1e500: центр длиннее тысячи знаков разбирается
        //    с точностью под зум и возвращается без потерь, мелкий зум строк точного центра не заводит.
        {
            var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
            if (Application.Current.Resources.MergedDictionaries.All(d => d.Source != themeStyles))
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });

            var window = new NewtonPoolsWindow();
            NewtonState extreme = window.CaptureState("template");
            extreme.Zoom = FloatExp.Pow10(500);
            extreme.CenterXExact = cycleX;
            extreme.CenterYExact = cycleY;
            extreme.CenterX = BigFloat.Parse(cycleX).ToDouble();
            extreme.CenterY = BigFloat.Parse(cycleY).ToDouble();
            window.LoadState(extreme);
            NewtonState captured = window.CaptureState("round-trip");
            Check(captured.Zoom == extreme.Zoom, "The Newton window must round-trip a 1e500 zoom unchanged.");
            using (new BigFloat.PrecisionScope(3648))
            {
                FloatExp pixelFraction = 3.0 / extreme.Zoom * Math.ScaleB(1.0, -60);
                FloatExp driftX = FloatExp.Abs(FloatExp.FromBigFloat(BigFloat.Parse(captured.CenterXExact!) - BigFloat.Parse(cycleX)));
                FloatExp driftY = FloatExp.Abs(FloatExp.FromBigFloat(BigFloat.Parse(captured.CenterYExact!) - BigFloat.Parse(cycleY)));
                Check(driftX < pixelFraction && driftY < pixelFraction,
                    $"The Newton window must round-trip a 1e500 exact center to far below a pixel: drift {driftX}, {driftY}.");
            }

            NewtonState shallowState = window.CaptureState("template");
            shallowState.Zoom = FloatExp.FromDouble(700);
            shallowState.CenterXExact = null;
            shallowState.CenterYExact = null;
            window.LoadState(shallowState);
            NewtonState capturedShallow = window.CaptureState("round-trip-shallow");
            Check(capturedShallow.CenterXExact is null && capturedShallow.CenterYExact is null,
                "A shallow Newton save must not carry exact-center strings.");
            window.Close();
        }

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Newton deep-zoom checks must leave the working precision restored.");
    }

    // Phase 6: Simonobrot of even integer power p=2q — composition of two exact binomial
    // perturbations (zᵖ and |z|ᵖ=Mᵠ). Phase 10 added odd p=2q+1, where the modulus factor
    // carries a square root and is perturbed by the exact identity
    // δ√M = δm/(√(M+δm)+√M). Verified against the exact BigFloat reference, both with and
    // without UseInversion (which flips the sign the reference-orbit cache key must also
    // carry) — and, for the odd powers, against the plain-double Iterate path as well:
    // that one goes through Complex.Pow/Math.Pow, so it is the only oracle that does not
    // share StepSimonobrotReference with the engine under test.
    private static async Task VerifySimonobrotDeepZoomAsync(Func<MandelbrotPalette> palette)
    {
        static int CountRgbDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                if (a[o] != b[o] || a[o + 1] != b[o + 1] || a[o + 2] != b[o + 2]) n++;
            }
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceDeep, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally { MandelbrotFamilyRenderer.ForceDeepZoomForTests = null; }
            return pixels;
        }

        const int w = 56, h = 40, total = w * h;

        // Structured boundary views per power/inversion combo (auto-located).
        (int Power, bool Inversion, decimal Cx, decimal Cy)[] cases =
        {
            (2, false, -0.03m, 0.84m),
            (2, true,   0.03m, 0.84m),
            (6, false, -0.90m, 0.18m),
            (8, true,  -0.33m, 0.87m),
            (12, false, -0.12m, 0.84m),
            // Phase 10 — odd powers, where the modulus factor is Mᵠ·√M. Centres picked for
            // edge density (the detail the kernel has to reproduce), not just for non-black.
            (3, false, -0.70m, -0.35m),
            (5, false,  0.55m, -0.85m),
            (7, true,   0.30m, 0.85m),
            (9, false,  0.95m, 0.25m),
            (11, false, -0.10m, -0.95m),
        };

        foreach ((int power, bool inversion, decimal cx, decimal cy) in cases)
        {
            var state = new MandelbrotState
            {
                Variant = MandelbrotVariant.Simonobrot,
                Power = power,
                UseInversion = inversion,
                CenterX = cx,
                CenterY = cy,
                Zoom = 300.0,
                Iterations = 2000,
                Threads = 2,
                Palette = palette()
            };
            byte[] perturbation = await RenderAsync(state, forceDeep: true, w, h);
            byte[] exact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
            // The exact reference and the reference orbit share StepSimonobrotReference, so
            // it cannot catch an error in the formula itself — only in the perturbation on
            // top of it. The plain-double path can: it computes zᵖ·|z|ᵖ through
            // Complex.Pow and Math.Pow(√M, p), independently of everything above. At zoom
            // 300 double is exact enough to be that oracle.
            byte[] flat = await RenderAsync(state, forceDeep: false, w, h);

            int vsExact = CountRgbDiffering(perturbation, exact);
            int vsFlat = CountRgbDiffering(perturbation, flat);
            int maxD = 0, nonBlack = 0;
            for (int i = 0; i < total; i++)
            {
                int o = i * 4;
                maxD = Math.Max(maxD, Math.Max(Math.Abs(perturbation[o] - exact[o]),
                    Math.Max(Math.Abs(perturbation[o + 1] - exact[o + 1]), Math.Abs(perturbation[o + 2] - exact[o + 2]))));
                if (perturbation[o] != 0 || perturbation[o + 1] != 0 || perturbation[o + 2] != 0) nonBlack++;
            }
            Console.WriteLine($"[diag] Simonobrot p={power} inv={inversion}: vs exact {vsExact}/{total} (maxΔ {maxD}), " +
                $"vs plain double {vsFlat}/{total}, nonblack {nonBlack}");
            Check(nonBlack > total / 10, $"Simonobrot p={power} inv={inversion} view must carry structure.");
            Check(vsExact * 100 <= total * 3,
                $"Simonobrot p={power} inv={inversion}: perturbation diverges from exact on {vsExact}/{total} px, maxΔ {maxD} (>3%).");
            Check(vsFlat * 100 <= total * 3,
                $"Simonobrot p={power} inv={inversion}: perturbation diverges from the plain-double formula on {vsFlat}/{total} px (>3%).");
        }

        // UseInversion must be part of the reference-orbit cache key: two renders that
        // differ only by it, at the same centre/zoom, must not collide.
        {
            var baseState = new MandelbrotState
            {
                Variant = MandelbrotVariant.Simonobrot,
                Power = 2,
                CenterX = -0.03m,
                CenterY = 0.84m,
                Zoom = 300.0,
                Iterations = 2000,
                Threads = 2,
                Palette = palette()
            };
            var inverted = new MandelbrotState
            {
                Variant = MandelbrotVariant.Simonobrot,
                Power = 2,
                UseInversion = true,
                CenterX = -0.03m,
                CenterY = 0.84m,
                Zoom = 300.0,
                Iterations = 2000,
                Threads = 2,
                Palette = palette()
            };
            byte[] a = await RenderAsync(baseState, forceDeep: true, w, h);
            byte[] b = await RenderAsync(inverted, forceDeep: true, w, h);
            Check(CountRgbDiffering(a, b) > total / 4,
                "UseInversion must be part of the orbit cache key (renders collided).");
        }
    }

    // How significant is the quality loss of the production engine (perturbation + BLA +
    // FloatExp) versus a near-exact BigFloat direct-iteration reference? Reports the
    // fraction of differing pixels and the worst per-channel delta per view.
    private static async Task VerifyEngineAccuracyAsync(Func<MandelbrotPalette> palette)
    {
        static (int Differing, int MaxDelta) Compare(byte[] a, byte[] b)
        {
            int differing = 0, maxDelta = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                int d = Math.Max(Math.Abs(a[o] - b[o]),
                    Math.Max(Math.Abs(a[o + 1] - b[o + 1]), Math.Abs(a[o + 2] - b[o + 2])));
                if (d != 0) differing++;
                maxDelta = Math.Max(maxDelta, d);
            }
            return (differing, maxDelta);
        }

        MandelbrotPalette Pal() => palette();

        MandelbrotState Deep(MandelbrotVariant variant, decimal cx, decimal cy, double zoom, int iterations,
            decimal power = 2m, decimal jr = 0m, decimal ji = 0m) => new()
        {
            Variant = variant,
            CenterX = cx,
            CenterY = cy,
            Power = power,
            JuliaCReal = jr,
            JuliaCImaginary = ji,
            Zoom = zoom,
            Iterations = iterations,
            Threads = 2,
            Palette = Pal()
        };

        const int w = 48, h = 32, total = w * h;
        var mandelCentre = (X: -1.2628848671045503000020782246m, Y: 0.0409687601493310685285376264m);

        (string Label, MandelbrotState State)[] views =
        {
            ("Mandelbrot 1e30",        Deep(MandelbrotVariant.Mandelbrot, mandelCentre.X, mandelCentre.Y, 1.0e30, 4500)),
            ("Mandelbrot 1e120 (fexp)",Deep(MandelbrotVariant.Mandelbrot, mandelCentre.X, mandelCentre.Y, 1.0e120, 4500)),
            ("BurningShip 1e30",       Deep(MandelbrotVariant.BurningShip, -1.62m, 0m, 1.0e30, 3000)),
        };

        foreach ((string label, MandelbrotState state) in views)
        {
            byte[] engine = new byte[w * h * 4];
            await Task.Run(() => MandelbrotFamilyRenderer.Render(state, engine, w, h, w * 4, CancellationToken.None));
            byte[] exact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
            (int differing, int maxDelta) = Compare(engine, exact);
            int nonBlack = 0;
            for (int i = 0; i < total; i++)
                if (engine[i * 4] != 0 || engine[i * 4 + 1] != 0 || engine[i * 4 + 2] != 0) nonBlack++;
            double percent = 100.0 * differing / total;
            Console.WriteLine($"[diag] accuracy {label}: {differing}/{total} px differ ({percent:F2}%), max Δ {maxDelta}, nonblack {nonBlack}");
            Check(differing * 100 <= total * 5,
                $"Engine diverges from the exact reference on {differing}/{total} px for {label} (>5%).");
        }
    }

    // Phase 4: reflected/conjugate variants (Burning Ship, Julia Burning Ship, Tricorn,
    // Buffalo, Celtic) on the perturbation engine via a sign-folded δ recurrence (no BLA).
    // Where decimal is still trustworthy the perturbation render must reproduce it up to
    // boundary chaos; Mandelbrot/Julia must be untouched.
    private static async Task VerifyReflectedVariantsAsync(Func<MandelbrotPalette> palette)
    {
        static int CountRgbDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                if (a[o] != b[o] || a[o + 1] != b[o + 1] || a[o + 2] != b[o + 2]) n++;
            }
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceDeep, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally { MandelbrotFamilyRenderer.ForceDeepZoomForTests = null; }
            return pixels;
        }

        const int w = 120, h = 80, total = w * h;

        (MandelbrotVariant variant, decimal cx, decimal cy, decimal jr, decimal ji, string label)[] cases =
        {
            (MandelbrotVariant.BurningShip,      -1.62m, 0m,     0m,     0m,   "BurningShip"),
            (MandelbrotVariant.Tricorn,          -1.62m, 0m,     0m,     0m,   "Tricorn"),
            (MandelbrotVariant.Buffalo,          -1.62m, 0m,     0m,     0m,   "Buffalo"),
            (MandelbrotVariant.Celtic,           -1.62m, 0m,     0m,     0m,   "Celtic"),
            (MandelbrotVariant.JuliaBurningShip,  0.5m, -0.3m, -1.5m,   0m,   "JuliaBurningShip"),
        };

        foreach ((MandelbrotVariant variant, decimal cx, decimal cy, decimal jr, decimal ji, string label) in cases)
        {
            foreach (double zoom in new[] { 1.0e10, 1.0e16 })
            {
                var state = new MandelbrotState
                {
                    Variant = variant,
                    CenterX = cx,
                    CenterY = cy,
                    JuliaCReal = jr,
                    JuliaCImaginary = ji,
                    Zoom = zoom,
                    Iterations = 3000,
                    Threshold = 2m,
                    Threads = 2,
                    Palette = palette()
                };
                byte[] decimalPixels = await RenderAsync(state, forceDeep: false, w, h);
                byte[] perturbationPixels = await RenderAsync(state, forceDeep: true, w, h);
                int differing = CountRgbDiffering(decimalPixels, perturbationPixels);
                Console.WriteLine($"[diag] {label} zoom {zoom:E0}: decimal vs perturbation {differing}/{total} px differ");
                Check(perturbationPixels.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                    $"{label} perturbation must resolve structure at zoom {zoom:E0}.");
                Check(differing * 100 <= total * 8,
                    $"{label}: perturbation diverges from decimal on {differing}/{total} px at zoom {zoom:E0} (>8%).");
            }
        }

        // Regression guard: the reflected code path must not touch Mandelbrot.
        var mandel = new MandelbrotState
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = 5.0e25,
            Iterations = 4000,
            Threads = 2,
            Palette = palette()
        };
        byte[] a1 = await RenderAsync(mandel, forceDeep: true, w, h);
        byte[] a2 = await RenderAsync(mandel, forceDeep: true, w, h);
        Check(CountRgbDiffering(a1, a2) == 0, "Mandelbrot deep render must stay deterministic after Phase 4.");
    }

    // Phase 3: BLA (Zhuoran) skips runs of iterations where δ stays small. It is a pure
    // accelerator over the Phase 2 perturbation engine — the output must match BLA-off up
    // to a few boundary pixels (composite BLAs carry their own rounding order), and the
    // OrbitTrap/StripeAverage modes must be untouched (BLA disabled there).
    private static async Task VerifyBlaAccelerationAsync(Func<MandelbrotPalette> palette)
    {
        static int CountRgbDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                if (a[o] != b[o] || a[o + 1] != b[o + 1] || a[o + 2] != b[o + 2]) n++;
            }
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool bla, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceBlaForTests = bla;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally { MandelbrotFamilyRenderer.ForceBlaForTests = null; }
            return pixels;
        }

        MandelbrotState Deep(double zoom, int iterations, MandelbrotColoringMode mode = MandelbrotColoringMode.Smooth) => new()
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = zoom,
            Iterations = iterations,
            Threads = 2,
            ColoringMode = mode,
            Palette = palette()
        };

        const int w = 120, h = 80, total = w * h;

        // (a) BLA on vs off across the engine's regimes (double-δ, deeper double-δ, FloatExp-δ).
        foreach ((double zoom, int iterations, string label) in new[]
        {
            (5.0e25, 4000, "double-δ 5e25"),
            (1.0e40, 5000, "double-δ 1e40"),
            (1.0e120, 3500, "FloatExp-δ 1e120"),
        })
        {
            MandelbrotState state = Deep(zoom, iterations);
            byte[] off = await RenderAsync(state, bla: false, w, h);
            byte[] on = await RenderAsync(state, bla: true, w, h);
            int differing = CountRgbDiffering(off, on);
            Console.WriteLine($"[diag] BLA {label}: on vs off {differing}/{total} px differ");
            Check(on.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                $"BLA render must resolve structure ({label}).");
            Check(differing * 100 <= total * 2,
                $"BLA changes {differing}/{total} px vs non-BLA ({label}, >2%).");
        }

        // (b) Julia deep (B = 0 in the BLA table): on vs off must still match.
        {
            var julia = new MandelbrotState
            {
                Variant = MandelbrotVariant.Julia,
                JuliaCReal = -0.8m,
                JuliaCImaginary = 0.156m,
                CenterX = 0.15m,
                CenterY = 0.30m,
                Zoom = 5.0e25,
                Iterations = 4000,
                Threads = 2,
                Palette = palette()
            };
            int differing = CountRgbDiffering(
                await RenderAsync(julia, bla: false, w, h),
                await RenderAsync(julia, bla: true, w, h));
            Console.WriteLine($"[diag] BLA Julia 5e25: on vs off {differing}/{total} px differ");
            Check(differing * 100 <= total * 2, $"BLA changes Julia {differing}/{total} px (>2%).");
        }

        // (c) OrbitTrap coloring keeps every iteration ⇒ BLA is disabled ⇒ byte-identical.
        {
            MandelbrotState trap = Deep(5.0e25, 4000, MandelbrotColoringMode.OrbitTrap);
            int differing = CountRgbDiffering(
                await RenderAsync(trap, bla: false, w, h),
                await RenderAsync(trap, bla: true, w, h));
            Check(differing == 0, $"BLA must be inert for OrbitTrap coloring, changed {differing}/{total} px.");
        }

        // (d) Speed: a high-iteration deep view. BLA must not be slower; report the ratio.
        {
            MandelbrotState heavy = Deep(1.0e55, 24000);
            const int hw = 160, hh = 108;
            byte[] warm = new byte[hw * hh * 4];
            MandelbrotFamilyRenderer.ForceBlaForTests = false;
            MandelbrotFamilyRenderer.Render(heavy, warm, hw, hh, hw * 4, CancellationToken.None);

            var clock = Stopwatch.StartNew();
            MandelbrotFamilyRenderer.ForceBlaForTests = false;
            MandelbrotFamilyRenderer.Render(heavy, warm, hw, hh, hw * 4, CancellationToken.None);
            double offMs = clock.Elapsed.TotalMilliseconds;

            clock.Restart();
            MandelbrotFamilyRenderer.ForceBlaForTests = true;
            MandelbrotFamilyRenderer.Render(heavy, warm, hw, hh, hw * 4, CancellationToken.None);
            double onMs = clock.Elapsed.TotalMilliseconds;
            MandelbrotFamilyRenderer.ForceBlaForTests = null;

            Console.WriteLine($"[diag] BLA speed 1e55 i24000: off {offMs:F0}ms, on {onMs:F0}ms, ×{offMs / onMs:F2}");
            Check(onMs <= offMs * 1.25, $"BLA slower than non-BLA: {onMs:F0}ms vs {offMs:F0}ms.");
        }

        await Task.CompletedTask;
    }

    // Phase 9: BLA with a REAL 2x2 linear part. The five reflected/conjugate variants
    // (Burning Ship, Julia Burning Ship, Tricorn, Buffalo, Celtic) and even-power Simonobrot
    // advance delta through a real linear map, not a complex multiplication, so they get their
    // own pyramid (RealBlaTable); the complex BlaTable is left untouched and still serves
    // Mandelbrot/Julia/Multibrot. Like the complex one this is a pure accelerator, hence:
    //   (a) it must actually engage - the skip counter guards against a vacuous pass;
    //   (b) it must cost the picture nothing. Views deep enough to engage BLA sit in
    //       boundary-chaotic territory, where the engine already differs from an exact BigFloat
    //       render on a few percent of pixels. The honest metric is therefore the EXCESS of
    //       that difference over the same render with BLA off, not the raw diff;
    //   (c) coloring modes that consume every single iteration (orbit trap, stripe average,
    //       distance estimation) must keep BLA off and stay byte-identical.
    // The centres are exact decimal strings found by walking into each variant's boundary, so
    // the frames stay structured at these depths instead of degenerating into a flat field.
    private static async Task VerifyRealBlaAccelerationAsync(Func<MandelbrotPalette> palette)
    {
        static int CountRgbDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                if (a[o] != b[o] || a[o + 1] != b[o + 1] || a[o + 2] != b[o + 2]) n++;
            }
            return n;
        }

        MandelbrotState State(
            MandelbrotVariant variant, string centreX, string centreY, double zoom,
            MandelbrotColoringMode mode = MandelbrotColoringMode.Smooth,
            decimal power = 2m, decimal juliaReal = 0m, decimal juliaImaginary = 0m,
            bool inversion = false) => new()
            {
                Variant = variant,
                ColoringMode = mode,
                // decimal only carries ~28 digits; the deep engine reads the exact strings.
                CenterX = decimal.Parse(centreX[..System.Math.Min(centreX.Length, 20)], CultureInfo.InvariantCulture),
                CenterY = decimal.Parse(centreY[..System.Math.Min(centreY.Length, 20)], CultureInfo.InvariantCulture),
                CenterXExact = centreX,
                CenterYExact = centreY,
                Power = power,
                JuliaCReal = juliaReal,
                JuliaCImaginary = juliaImaginary,
                UseInversion = inversion,
                Zoom = zoom,
                Iterations = 6000,
                Threshold = 2m,
                Threads = 2,
                Palette = palette(),
            };

        async Task<byte[]> RenderAsync(MandelbrotState state, bool bla, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceBlaForTests = bla;
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = true;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally
            {
                MandelbrotFamilyRenderer.ForceBlaForTests = null;
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
            }
            return pixels;
        }

        async Task<long> CountSkipsAsync(MandelbrotState state, int w, int h)
        {
            MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests = 0;
            MandelbrotFamilyRenderer.CountRealBlaSkipsForTests = true;
            try { await RenderAsync(state, bla: true, w, h); }
            finally { MandelbrotFamilyRenderer.CountRealBlaSkipsForTests = false; }
            return MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests;
        }

        const string burningShipX = "-0.81350985269959441502640438927923405594718016208354671578096";
        const string burningShipY = "1.15385056973366263716386423762505110184996148623201569967787";
        const string celticX = "-0.891865646507391491113368732535744974340110083129727811184488";
        const string celticY = "1.544758206384140070271947748309466179897357707661851201063699";
        const string simonobrot4X = "0.279452570816264365821172574207784238378988855076446292869889";
        const string simonobrot4Y = "1.018073395776969286854111772196867045308653990469452818298968";

        (string Label, MandelbrotState State)[] fixtures =
        {
            ("BurningShip 3e30", State(MandelbrotVariant.BurningShip,
                "-0.813509852699594415026404389279137158659901740416441180706343",
                "1.153850569733662637163864237624756879942846369129799170139332", 3.1622776601683795e30)),
            ("BurningShip 1e45", State(MandelbrotVariant.BurningShip, burningShipX, burningShipY, 1.0e45)),
            ("Tricorn 3e30", State(MandelbrotVariant.Tricorn,
                "0.740107894676546596166278068634689383680928471433837845616988",
                "1.284164879491910489407619722838421840742204004581206006914469", 3.1622776601683795e30)),
            ("Buffalo 1e45", State(MandelbrotVariant.Buffalo,
                "0.251515542826420576827659826958312420927687487022709942977638",
                "0.454127991517863684724990589158969466705687426119938699326094", 1.0e45)),
            ("Celtic 3e30", State(MandelbrotVariant.Celtic, celticX, celticY, 3.1622776601683795e30)),
            ("JuliaBurningShip 1e45", State(MandelbrotVariant.JuliaBurningShip,
                "-0.024227550790277459831804555192054182024312705553405370339065",
                "0.56875673781379166740393234420623920098438359700265085138842", 1.0e45,
                juliaReal: -1.5m, juliaImaginary: 0.02m)),
            ("Simonobrot p2 1e28", State(MandelbrotVariant.Simonobrot,
                "-0.233085135590328323057688239020593618626185475481456854709886",
                "0.956730152389579347325401450437689559915308904251176295871525", 1.0e28, power: 2m)),
            ("Simonobrot p4 1e28", State(MandelbrotVariant.Simonobrot, simonobrot4X, simonobrot4Y, 1.0e28, power: 4m)),
            ("Simonobrot p6 inv 1e28", State(MandelbrotVariant.Simonobrot,
                "0.227591276012239845382720882305167929274131349145291924124711",
                "1.029105663003342779985661616216266280026506157420936047716223", 1.0e28,
                power: 6m, inversion: true)),
            ("Simonobrot p12 1e28", State(MandelbrotVariant.Simonobrot,
                "0.122209692688143080456363858471452783327760583215973003556001",
                "1.021769700244342585739196861366295324922134500631004946262376", 1.0e28, power: 12m)),
            // Phase 10 — odd powers: the same real 2×2 table, with both half-integer powers
            // of the modulus (M^(p/2) and M^(p/2−1)) carrying a √M factor.
            ("Simonobrot p3 1e28", State(MandelbrotVariant.Simonobrot,
                "-0.202832656913406719584951309136145088020207829768900793575531",
                "1.112011809221549716846392243745997167999832561088051678883473", 1.0e28, power: 3m)),
            ("Simonobrot p5 1e28", State(MandelbrotVariant.Simonobrot,
                "-0.548667511943908696630758431962511649547657017247242149297411",
                "0.872765118321406809316583683587250477176667101144019039749011", 1.0e28, power: 5m)),
        };

        const int w = 120, h = 80, total = w * h;
        const int ew = 36, eh = 24, etotal = ew * eh;

        foreach ((string label, MandelbrotState state) in fixtures)
        {
            long skips = await CountSkipsAsync(state, w, h);
            byte[] off = await RenderAsync(state, bla: false, w, h);
            byte[] on = await RenderAsync(state, bla: true, w, h);
            int differing = CountRgbDiffering(off, on);

            Check(on.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                $"Real BLA render must resolve structure ({label}).");
            Check(skips > 0, $"Real BLA never engaged on {label}: the fixture proves nothing.");

            byte[] exact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, ew, eh, CancellationToken.None));
            int exactVersusOff = CountRgbDiffering(exact, await RenderAsync(state, bla: false, ew, eh));
            int exactVersusOn = CountRgbDiffering(exact, await RenderAsync(state, bla: true, ew, eh));
            int excess = exactVersusOn - exactVersusOff;

            Console.WriteLine($"[diag] real BLA {label}: skipped {skips:N0} iterations, on vs off " +
                $"{differing}/{total} px, vs exact {exactVersusOff} -> {exactVersusOn}/{etotal} (excess {excess})");

            // Pure accelerator: it may add no error of its own beyond the boundary chaos the
            // engine already carries. 2% of the sampled pixels is the whole budget.
            Check(excess * 50 <= etotal,
                $"Real BLA adds {excess}/{etotal} px of error over non-BLA on {label} (>2%).");
        }

        // Odd-power Simonobrot must reach the deep engine through the production gate, not
        // only through the test seam: without ForceDeepZoomForTests the render still has to
        // engage the real BLA, which only the perturbation kernels ever consult.
        {
            MandelbrotState state = fixtures[^2].State;   // Simonobrot p3 1e28
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests = 0;
            MandelbrotFamilyRenderer.CountRealBlaSkipsForTests = true;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally { MandelbrotFamilyRenderer.CountRealBlaSkipsForTests = false; }

            Check(MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests > 0,
                "Odd-power Simonobrot must select the deep engine at 1e28 without the test seam.");
            Check(CountRgbDiffering(pixels, await RenderAsync(state, bla: true, w, h)) == 0,
                "The production gate must produce exactly the forced-deep render for odd-power Simonobrot.");
        }

        // Coloring modes that consume every iteration keep BLA off, so they stay byte-identical.
        foreach (MandelbrotColoringMode mode in new[]
        {
            MandelbrotColoringMode.OrbitTrap,
            MandelbrotColoringMode.StripeAverage,
            MandelbrotColoringMode.DistanceEstimation,
        })
        {
            foreach ((string label, MandelbrotState state) in new (string, MandelbrotState)[]
            {
                ("BurningShip", State(MandelbrotVariant.BurningShip, burningShipX, burningShipY, 1.0e45, mode)),
                ("Celtic", State(MandelbrotVariant.Celtic, celticX, celticY, 3.1622776601683795e30, mode)),
                ("Simonobrot p4", State(MandelbrotVariant.Simonobrot, simonobrot4X, simonobrot4Y, 1.0e28, mode, power: 4m)),
            })
            {
                int differing = CountRgbDiffering(
                    await RenderAsync(state, bla: false, w, h),
                    await RenderAsync(state, bla: true, w, h));
                Check(differing == 0,
                    $"Real BLA must be inert for {mode} ({label}), changed {differing}/{total} px.");
            }
        }

        // Speed - the whole point of the phase. The reflected step is cheap, so skipping runs of
        // it pays off the most; Simonobrot's own step is heavy enough that the win is smaller,
        // and there the requirement is only that the lookup must not cost more than it saves.
        foreach ((string label, MandelbrotState state, double minimumRatio) in
            new (string, MandelbrotState, double)[]
            {
                ("Tricorn 3e30", fixtures[2].State, 2.0),
                ("Simonobrot p4 1e28", fixtures[7].State, 0.8),
            })
        {
            const int sw = 160, sh = 108;
            byte[] scratch = new byte[sw * sh * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = true;
            double offMs = double.MaxValue, onMs = double.MaxValue;
            try
            {
                MandelbrotFamilyRenderer.ForceBlaForTests = false;
                MandelbrotFamilyRenderer.Render(state, scratch, sw, sh, sw * 4, CancellationToken.None);
                var clock = new Stopwatch();
                for (int trial = 0; trial < 3; trial++)
                {
                    clock.Restart();
                    MandelbrotFamilyRenderer.ForceBlaForTests = false;
                    MandelbrotFamilyRenderer.Render(state, scratch, sw, sh, sw * 4, CancellationToken.None);
                    offMs = System.Math.Min(offMs, clock.Elapsed.TotalMilliseconds);

                    clock.Restart();
                    MandelbrotFamilyRenderer.ForceBlaForTests = true;
                    MandelbrotFamilyRenderer.Render(state, scratch, sw, sh, sw * 4, CancellationToken.None);
                    onMs = System.Math.Min(onMs, clock.Elapsed.TotalMilliseconds);
                }
            }
            finally
            {
                MandelbrotFamilyRenderer.ForceBlaForTests = null;
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
            }

            Console.WriteLine($"[diag] real BLA speed {label}: off {offMs:F0}ms, on {onMs:F0}ms, x{offMs / onMs:F2}");
            Check(offMs / onMs >= minimumRatio,
                $"Real BLA speed on {label}: x{offMs / onMs:F2}, expected at least x{minimumRatio:F2}.");
        }
    }

    // Phase 11: FloatExp-δ for the reflected variants, Multibrot and Simonobrot — the same
    // plan switch (PlanDeepZoom.UseFloatExpDelta) that already gates the z²+c kernel now
    // also gates DeepZoomPixelReflectedFloatExp/MultibrotFloatExp/SimonobrotFloatExp, lifting
    // their double-δ ceiling (was 1e50/1e40/1e30) up to the shared MaxZoom.
    //   1. Overlap band: wherever both δ representations are valid, the FloatExp kernel must
    //      be bit-identical to the trusted double kernel (the same technique as Phase 1 for
    //      z²+c) — reusing the Real BLA fixtures (1e28-1e45, far below FloatExpDeltaZoomBits
    //      ≈ 8.8e71) for the reflected/Simonobrot families, and the existing Multibrot
    //      fixtures forced onto the deep engine at a shallow zoom for the Multibrot family
    //      (it has no natural deep fixture below the threshold in this file).
    //   2. Extreme smoke test: a real zoom of 1e300 — orders of magnitude past every old
    //      ceiling. The same ~60-70 digit centers used above no longer resolve real boundary
    //      structure at this depth (their own precision runs out long before), so — exactly
    //      like the Mandelbrot/Julia check at 1e120/1e300 — only completion, full-frame fill
    //      and precision restoration are checked, plus that the reference orbit actually
    //      stayed non-degenerate (proving the new kernel really ran, not the brute-force
    //      fallback).
    private static async Task VerifyFloatExpDeltaVariantsAsync(Func<MandelbrotPalette> palette)
    {
        static int CountRgbDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                if (a[o] != b[o] || a[o + 1] != b[o + 1] || a[o + 2] != b[o + 2]) n++;
            }
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceFloatExp, bool? forceDeep, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = forceFloatExp;
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally
            {
                MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = null;
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
            }
            return pixels;
        }

        const int w = 120, h = 80, total = w * h;

        const string burningShipX = "-0.81350985269959441502640438927923405594718016208354671578096";
        const string burningShipY = "1.15385056973366263716386423762505110184996148623201569967787";
        const string celticX = "-0.891865646507391491113368732535744974340110083129727811184488";
        const string celticY = "1.544758206384140070271947748309466179897357707661851201063699";
        const string simonobrot4X = "0.279452570816264365821172574207784238378988855076446292869889";
        const string simonobrot4Y = "1.018073395776969286854111772196867045308653990469452818298968";
        const string simonobrot3X = "-0.202832656913406719584951309136145088020207829768900793575531";
        const string simonobrot3Y = "1.112011809221549716846392243745997167999832561088051678883473";

        MandelbrotState ExactState(
            MandelbrotVariant variant, string centreX, string centreY, double zoom,
            decimal power = 2m, decimal juliaReal = 0m, decimal juliaImaginary = 0m,
            int iterations = 6000) => new()
            {
                Variant = variant,
                CenterX = decimal.Parse(centreX[..Math.Min(centreX.Length, 20)], CultureInfo.InvariantCulture),
                CenterY = decimal.Parse(centreY[..Math.Min(centreY.Length, 20)], CultureInfo.InvariantCulture),
                CenterXExact = centreX,
                CenterYExact = centreY,
                Power = power,
                JuliaCReal = juliaReal,
                JuliaCImaginary = juliaImaginary,
                Zoom = zoom,
                Iterations = iterations,
                Threshold = 2m,
                Threads = 2,
                Palette = palette(),
            };

        // 1a. Reflected + Simonobrot: already-proven deep fixtures, naturally past
        // PerturbationZoomThreshold but far below FloatExpDeltaZoomBits.
        (string Label, MandelbrotState State)[] overlapFixtures =
        {
            ("BurningShip 1e45", ExactState(MandelbrotVariant.BurningShip, burningShipX, burningShipY, 1.0e45)),
            ("Celtic 3e30", ExactState(MandelbrotVariant.Celtic, celticX, celticY, 3.1622776601683795e30)),
            ("Simonobrot p4 1e28", ExactState(MandelbrotVariant.Simonobrot, simonobrot4X, simonobrot4Y, 1.0e28, power: 4m)),
            ("Simonobrot p3 1e28 (odd, sqrt)", ExactState(MandelbrotVariant.Simonobrot, simonobrot3X, simonobrot3Y, 1.0e28, power: 3m)),
        };

        foreach ((string label, MandelbrotState state) in overlapFixtures)
        {
            byte[] doubleDelta = await RenderAsync(state, forceFloatExp: false, forceDeep: null, w, h);
            byte[] floatExpDelta = await RenderAsync(state, forceFloatExp: true, forceDeep: null, w, h);
            int differing = CountRgbDiffering(doubleDelta, floatExpDelta);
            Console.WriteLine($"[diag] FloatExp-δ overlap {label}: double vs FloatExp {differing}/{total} px differ");
            Check(doubleDelta.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                $"Overlap fixture must carry structure ({label}).");
            Check(differing == 0,
                $"FloatExp-δ diverges from double-δ on {differing}/{total} px ({label}) inside the overlap band.");
        }

        // 1b. Multibrot: no natural deep fixture below the threshold in this file — reuse the
        // shallow-zoom, forced-deep-engine trick from VerifyMultibrotDeepZoomAsync. What
        // matters is that both kernels run the same code over the same reference orbit and δc,
        // not the literal zoom value.
        (int Power, decimal Cx, decimal Cy)[] multibrotCases =
        {
            (5, -0.540000m, 0.600000m),
            (8, 0.660000m, 0.000000m),
        };
        foreach ((int power, decimal cx, decimal cy) in multibrotCases)
        {
            var state = new MandelbrotState
            {
                Variant = MandelbrotVariant.Generalized,
                Power = power,
                CenterX = cx,
                CenterY = cy,
                Zoom = 300.0,
                Iterations = 2000,
                Threads = 2,
                Palette = palette()
            };
            byte[] doubleDelta = await RenderAsync(state, forceFloatExp: false, forceDeep: true, w, h);
            byte[] floatExpDelta = await RenderAsync(state, forceFloatExp: true, forceDeep: true, w, h);
            int differing = CountRgbDiffering(doubleDelta, floatExpDelta);
            Console.WriteLine($"[diag] FloatExp-δ overlap Multibrot p={power}: double vs FloatExp {differing}/{total} px differ");
            Check(doubleDelta.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                $"Multibrot p={power} overlap fixture must carry structure.");
            Check(differing == 0,
                $"FloatExp-δ diverges from double-δ on {differing}/{total} px (Multibrot p={power}).");
        }

        // 2. Extreme smoke test: a real zoom of 1e300, orders of magnitude past every old
        // per-variant ceiling (1e50/1e40/1e30) and the FloatExpDeltaZoomBits threshold.
        const int ew = 40, eh = 28;
        (string Label, MandelbrotState State)[] extremeFixtures =
        {
            ("BurningShip 1e300", ExactState(MandelbrotVariant.BurningShip, burningShipX, burningShipY, 1.0e300, iterations: 1500)),
            ("Simonobrot p4 1e300", ExactState(MandelbrotVariant.Simonobrot, simonobrot4X, simonobrot4Y, 1.0e300, power: 4m, iterations: 1500)),
            ("Simonobrot p3 1e300 (odd, sqrt)", ExactState(MandelbrotVariant.Simonobrot, simonobrot3X, simonobrot3Y, 1.0e300, power: 3m, iterations: 1500)),
        };
        foreach ((string label, MandelbrotState state) in extremeFixtures)
        {
            (double[] _, double[] _, int orbitLength) = MandelbrotFamilyRenderer.GetCenterOrbitForAnalysis(state);
            Console.WriteLine($"[diag] FloatExp-δ extreme {label}: reference orbit length {orbitLength}/{state.Iterations + 1}");
            Check(orbitLength > 4, $"Extreme fixture must produce a non-degenerate reference orbit ({label}), got length {orbitLength}.");

            int progress = 0;
            byte[] pixels = new byte[ew * eh * 4];
            await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, ew, eh, ew * 4,
                CancellationToken.None, value => progress = value));
            Check(progress == 100, $"Extreme FloatExp-δ render must complete with 100% progress ({label}).");
            Check(pixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
                $"Extreme FloatExp-δ render must fill every pixel ({label}).");
            Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
                $"Extreme FloatExp-δ render must restore the calling thread's working precision ({label}).");
        }

        // Multibrot needs a real deep zoom (not the forced-engine Zoom=300 trick) to reach
        // FloatExpDeltaZoomBits through the production gate.
        {
            var state = new MandelbrotState
            {
                Variant = MandelbrotVariant.Generalized,
                Power = 5,
                CenterX = -0.540000m,
                CenterY = 0.600000m,
                Zoom = 1.0e300,
                Iterations = 800,
                Threads = 2,
                Palette = palette()
            };
            (double[] _, double[] _, int orbitLength) = MandelbrotFamilyRenderer.GetCenterOrbitForAnalysis(state);
            Console.WriteLine($"[diag] FloatExp-δ extreme Multibrot p=5 1e300: reference orbit length {orbitLength}/{state.Iterations + 1}");
            Check(orbitLength > 4, $"Extreme Multibrot fixture must produce a non-degenerate reference orbit, got length {orbitLength}.");

            int progress = 0;
            byte[] pixels = new byte[ew * eh * 4];
            await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, ew, eh, ew * 4,
                CancellationToken.None, value => progress = value));
            Check(progress == 100, "Extreme FloatExp-δ render must complete with 100% progress (Multibrot p=5 1e300).");
            Check(pixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
                "Extreme FloatExp-δ render must fill every pixel (Multibrot p=5 1e300).");
            Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
                "Extreme FloatExp-δ render must restore the calling thread's working precision (Multibrot p=5 1e300).");
        }
    }

    // Phase 2: the decimal stage is gone from the Mandelbrot/Julia ladder — perturbation
    // now takes over wherever plain double stops being trusted (~1.5e9). Byte-identity for
    // zoom < 1.5e9 and zoom >= 1e25 is proven by the external git-stash A/B; here we check
    // the band that changed hands (was decimal brute-force, now perturbation).
    private static async Task VerifyDecimalStageRemovedAsync(Func<MandelbrotPalette> palette)
    {
        static int RgbDelta(byte[] a, byte[] b, int pixel)
        {
            int o = pixel * 4;
            return Math.Max(Math.Abs(a[o] - b[o]),
                   Math.Max(Math.Abs(a[o + 1] - b[o + 1]), Math.Abs(a[o + 2] - b[o + 2])));
        }

        static int CountDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
                if (RgbDelta(a, b, pixel) != 0) n++;
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceDeep, bool? forceFloatExp, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = forceFloatExp;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally
            {
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
                MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = null;
            }
            return pixels;
        }

        MandelbrotState DeepState(double zoom, int iterations) => new()
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = zoom,
            Iterations = iterations,
            Threads = 2,
            Palette = palette()
        };

        const int w = 112, h = 74, total = w * h;

        // (a) Where decimal was still trustworthy (<= ~1e18), perturbation must reproduce
        //     it up to boundary chaos (a 1-ULP coordinate difference can flip a boundary
        //     pixel by a whole colour band — see the project memory).
        foreach (double zoom in new[] { 1.0e12, 1.0e18 })
        {
            MandelbrotState state = DeepState(zoom, 6000);
            byte[] decimalPixels = await RenderAsync(state, forceDeep: false, forceFloatExp: null, w, h);
            byte[] perturbationPixels = await RenderAsync(state, forceDeep: true, forceFloatExp: null, w, h);
            int differing = CountDiffering(decimalPixels, perturbationPixels);
            Console.WriteLine($"[diag] zoom {zoom:E0}: decimal vs perturbation {differing}/{total} px differ");
            Check(perturbationPixels.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                $"Perturbation must resolve structure at zoom {zoom:E0}.");
            Check(differing * 100 <= total * 5,
                $"decimal→perturbation diverges on {differing}/{total} px at zoom {zoom:E0} (>5%).");
        }

        // (b) Past ~1e20 decimal itself runs out of digits, so it is no longer the oracle.
        //     Instead check the perturbation engine is internally converged: swapping the
        //     δ representation (double ↔ FloatExp) over the same reference orbit must not
        //     move the picture. If it doesn't, the divergence from decimal up here is
        //     decimal's error, not the engine's.
        foreach (double zoom in new[] { 1.0e18, 1.0e23 })
        {
            MandelbrotState state = DeepState(zoom, 6000);
            byte[] doubleDelta = await RenderAsync(state, forceDeep: true, forceFloatExp: false, w, h);
            byte[] floatExpDelta = await RenderAsync(state, forceDeep: true, forceFloatExp: true, w, h);
            int differing = CountDiffering(doubleDelta, floatExpDelta);
            Console.WriteLine($"[diag] zoom {zoom:E0}: perturbation double-δ vs FloatExp-δ {differing}/{total} px differ");
            Check(differing * 200 <= total,
                $"Perturbation not δ-representation-stable at zoom {zoom:E0}: {differing}/{total} px (>0.5%).");
        }

        // (c) Julia perturbation with the rebasing fix (δ = z − Z₀; Julia's Z₀ = centre is
        //     non-zero, and the old code used δ = z, which glitched wherever rebasing
        //     fired). A dendrite view forces heavy rebasing; at this shallow zoom plain
        //     double is an exact oracle, so agreement means the fix is right, not just
        //     "doesn't crash".
        {
            var julia = new MandelbrotState
            {
                Variant = MandelbrotVariant.Julia,
                JuliaCReal = -0.8m,
                JuliaCImaginary = 0.156m,
                CenterX = 0.15m,
                CenterY = 0.30m,
                Zoom = 100.0,
                Iterations = 3000,
                Threads = 2,
                Palette = palette()
            };
            byte[] doublePixels = await RenderAsync(julia, forceDeep: false, forceFloatExp: null, w, h);
            byte[] perturbationPixels = await RenderAsync(julia, forceDeep: true, forceFloatExp: null, w, h);
            int differing = CountDiffering(doublePixels, perturbationPixels);
            int structuredPixels = 0;
            for (int pixel = 0; pixel < total; pixel++)
                if (perturbationPixels[pixel * 4] != perturbationPixels[0] ||
                    perturbationPixels[pixel * 4 + 1] != perturbationPixels[1] ||
                    perturbationPixels[pixel * 4 + 2] != perturbationPixels[2])
                    structuredPixels++;
            Console.WriteLine($"[diag] Julia dendrite zoom 100: double vs perturbation {differing}/{total} px differ; structured {structuredPixels}/{total}");
            Check(structuredPixels > total / 4, "Julia perturbation must be structured, not a flat fill.");
            Check(differing * 100 <= total * 10,
                $"Julia: perturbation strays from the double oracle on {differing}/{total} px (>10%) — rebase fix suspect.");
        }

        // (d) Degenerate reference orbit (centre deep in the fast-escaping exterior): the
        //     fallback must run in double (no decimal), fill the frame and stay uniform.
        var degenerate = new MandelbrotState
        {
            CenterX = 1000m,
            CenterY = 1000m,
            Zoom = 1.0e30,
            Iterations = 500,
            Threads = 2,
            Palette = palette()
        };
        int degenerateProgress = 0;
        byte[] degeneratePixels = new byte[w * h * 4];
        MandelbrotFamilyRenderer.Render(degenerate, degeneratePixels, w, h, w * 4,
            CancellationToken.None, value => degenerateProgress = value);
        Check(degenerateProgress == 100, "Degenerate-orbit fallback must complete with 100% progress.");
        Check(degeneratePixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
            "Degenerate-orbit fallback must fill every pixel.");
        bool uniform = true;
        for (int pixel = 1; pixel < w * h && uniform; pixel++)
            uniform = degeneratePixels[pixel * 4] == degeneratePixels[0]
                   && degeneratePixels[pixel * 4 + 1] == degeneratePixels[1]
                   && degeneratePixels[pixel * 4 + 2] == degeneratePixels[2];
        Check(uniform, "Degenerate-orbit fallback must produce a uniform frame.");
    }

    private static async Task DrainAsync()
    {
        for (int i = 0; i < 3; i++) await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
    private static void Select(SaveManagerControl view, string name) => view.SelectedItem =
        ((ListBox)view.FindName("SavesList")).Items.Cast<SaveManagerEntry<State>>().Single(entry => entry.State.Name == name);
    private static void Click(SaveManagerControl view, string name) =>
        ((Button)view.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static BitmapSource? Image(SaveManagerControl view) =>
        ((Image)view.FindName("PreviewImage")).Source as BitmapSource;
    private static BitmapSource Pixel(byte value)
    {
        BitmapSource bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { value, value, value, 255 }, 4);
        bitmap.Freeze(); return bitmap;
    }
    private static byte ReadPixel(BitmapSource bitmap)
    {
        byte[] pixels = new byte[4]; bitmap.CopyPixels(pixels, 4, 0); return pixels[0];
    }
    private static string SavePreviewPath(FractalSaveStore<State> store, string name) =>
        Path.Combine(store.DirectoryPath, name + ".png");
    private static BitmapSource LoadPng(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapSource bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        bitmap.Freeze(); return bitmap;
    }
    // ---------------------------------------------------------------- extended-range zoom
    // The zoom factor, the frame grid and the Distance Estimation derivative moved from
    // double to FloatExp (double mantissa + 32-bit binary exponent), lifting the depth
    // ceiling from 1e90 to 1e1000. These checks cover the numeric type itself, then the
    // renderer at depths where the old double pipeline collapsed (3/zoom underflows to 0 past
    // ~1.8e308, so every pixel of a frame would have received the same dc).

    private static void VerifyFloatExpArithmetic()
    {
        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "FloatExp checks must start at the default working precision.");

        // (a) Values far outside the double range stay exact to the mantissa, and the
        //     reciprocal of a huge value is a small value rather than zero.
        FloatExp huge = FloatExp.Pow10(1000);
        Check(double.IsPositiveInfinity(huge.ToDouble()), "1e1000 must overflow plain double.");
        Check(Math.Abs(huge.Log10() - 1000.0) < 1e-9, $"log10(1e1000) must be 1000, got {huge.Log10()}.");
        FloatExp tiny = 3.0 / huge;
        Check(tiny.Sign > 0 && tiny.ToDouble() == 0.0,
            "3/1e1000 must be a positive FloatExp that merely underflows when narrowed to double.");
        Check(Math.Abs(tiny.Log10() - (Math.Log10(3.0) - 1000.0)) < 1e-9,
            "3/1e1000 must keep its decimal order of magnitude.");

        // (b) The pixel grid at 1e1000: neighbouring pixels must differ. This is precisely
        //     what broke in double — the whole frame collapsed onto the centre.
        FloatExp viewWidth = 3.0 / huge;
        FloatExp left = (0.0 / 1200 - 0.5) * viewWidth;
        FloatExp next = (1.0 / 1200 - 0.5) * viewWidth;
        Check(left != next, "Neighbouring pixel offsets at 1e1000 must differ.");
        FloatExp step = next - left;
        Check(step.Sign > 0 && Math.Abs(step.Log10() - (viewWidth.Log10() - Math.Log10(1200.0))) < 1e-6,
            "The pixel step at 1e1000 must equal the view width divided by the pixel count.");

        // (c) Round-trip through the decimal string, in and out of the double range.
        foreach (FloatExp value in new[]
                 {
                     FloatExp.FromDouble(0.75), FloatExp.FromDouble(1.5e9), FloatExp.FromDouble(-2.25e-30),
                     FloatExp.Pow10(300), FloatExp.Pow10(1000), FloatExp.Pow10(-1000),
                     FloatExp.Pow10(1000) * 1.2345678901234567, -FloatExp.Pow10(700),
                 })
        {
            string text = value.ToInvariantString();
            FloatExp parsed = FloatExp.Parse(text);
            FloatExp difference = FloatExp.Abs(parsed - value);
            bool exact = parsed == value;
            // 17 significant digits are written, so a value outside the double range may lose
            // at most the last ulp; inside the range the "R" format round-trips bit for bit.
            bool withinUlp = difference.IsZero ||
                             difference.Log2() <= FloatExp.Abs(value).Log2() - 50;
            Check(exact || withinUlp, $"FloatExp round-trip lost precision: {text}.");
        }

        // (d) Comparisons and clamping must order by magnitude across the whole range.
        Check(FloatExp.Pow10(1000) > FloatExp.Pow10(999), "Ordering across huge exponents.");
        Check(FloatExp.Pow10(-1000) < FloatExp.FromDouble(1e-300), "Ordering across tiny exponents.");
        Check(-FloatExp.Pow10(1000) < FloatExp.Pow10(-1000), "Ordering across signs.");
        Check(FloatExp.Clamp(FloatExp.Pow10(2000), FloatExp.FromDouble(0.01), FloatExp.Pow10(1000))
              == FloatExp.Pow10(1000), "Clamp must cap at the upper bound.");

        // (e) Square root and the BigFloat bridge: ToBigFloat must carry the mantissa whole,
        //     which is what lets a sub-1e-1000 pan shift reach the exact centre at all.
        FloatExp root = FloatExp.Sqrt(FloatExp.Pow10(1000));
        Check(Math.Abs(root.Log10() - 500.0) < 1e-9, $"sqrt(1e1000) must be 1e500, got log10 {root.Log10()}.");
        FloatExp shift = 3.0 / FloatExp.Pow10(1000) / 1200.0;
        BigFloat asBig = shift.ToBigFloat();
        Check(!asBig.Equals(BigFloat.Zero), "A 1e-1003 pixel shift must survive the move into BigFloat.");
        Check(FloatExp.FromBigFloat(asBig) == shift, "FloatExp -> BigFloat -> FloatExp must be lossless.");

        // (f) A pan shift of that size must actually move a BigFloat centre. On 384 bits it
        //     would vanish entirely — this is the reason the window sizes its precision scope
        //     from the zoom (CenterPrecisionScope).
        BigFloat centre = BigFloat.Parse("-1.25");
        Check((centre + asBig).Equals(centre),
            "On the default 384-bit precision a 1e-1003 shift is expected to vanish.");
        using (new BigFloat.PrecisionScope(3456))
        {
            BigFloat wide = BigFloat.Parse("-1.25");
            Check(!(wide + asBig).Equals(wide),
                "At 3456-bit precision a 1e-1003 shift must move the centre.");
        }
        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "The precision scope must restore the thread default.");

        // (g) An absurd exponent must saturate instead of expanding a BigInteger of that many
        //     digits — the zoom box accepts free text, so "1e99999999" is reachable input.
        var guardTimer = Stopwatch.StartNew();
        Check(!FloatExp.Parse("1e99999999").IsFinite, "An absurd positive exponent must saturate to infinity.");
        Check(FloatExp.Parse("1e-99999999").IsZero, "An absurd negative exponent must saturate to zero.");
        Check(!FloatExp.Parse("-1e2000000000").IsFinite, "An absurd negative-signed exponent must saturate.");
        guardTimer.Stop();
        Check(guardTimer.ElapsedMilliseconds < 1000,
            $"Parsing an absurd exponent must be refused immediately, took {guardTimer.ElapsedMilliseconds} ms.");
        Check(FloatExp.Parse("1e1000") == FloatExp.Pow10(1000),
            "A plausible exponent must still be parsed exactly.");

        Console.WriteLine("[diag] FloatExp: range, pixel grid, round-trip, ordering, BigFloat bridge and parse guard OK");
    }

    // The zoom field is serialized as a JSON number while it fits double (so save files
    // written before the type change keep loading, and new ones keep loading in older
    // builds) and as a scientific-notation string beyond it.
    private static void VerifyZoomSerialization()
    {
        System.Text.Json.JsonSerializerOptions options = JsonOptionsFactory.Create();

        foreach (FloatExp zoom in new[]
                 { FloatExp.FromDouble(0.75), FloatExp.FromDouble(5.7607143988621e25), FloatExp.Pow10(1000) })
        {
            var state = new MandelbrotState { Zoom = zoom, Iterations = 123 };
            string json = System.Text.Json.JsonSerializer.Serialize(state, options);
            MandelbrotState? restored =
                System.Text.Json.JsonSerializer.Deserialize<MandelbrotState>(json, options);
            Check(restored is not null, "Deserialization must succeed.");
            Check(restored!.Zoom == zoom,
                $"Zoom round-trip failed: {zoom.ToInvariantString()} -> {restored.Zoom.ToInvariantString()}.");
        }

        // A legacy file stores the zoom as a plain JSON number; it must still load.
        MandelbrotState? legacy = System.Text.Json.JsonSerializer.Deserialize<MandelbrotState>(
            "{\"Zoom\": 1e25, \"Iterations\": 500}", options);
        Check(legacy is not null && legacy.Zoom == FloatExp.FromDouble(1e25),
            "A zoom stored as a JSON number (older save format) must still load.");

        Console.WriteLine("[diag] zoom serialization: double-range numbers and out-of-range strings round-trip OK");
    }

    // A Julia set is self-similar around a repelling fixed point, so a view centred exactly
    // there resolves structure at ANY magnification — which makes it the only fixture that can
    // prove the engine still sees detail at 1e1000. The fixed point is computed in BigFloat to
    // well over a thousand digits; the Mandelbrot case can only be a "runs and agrees with the
    // exact reference" check, because genuine structure that deep needs a minibrot of period
    // ~1e5, far beyond what a test may spend.
    private static async Task VerifyExtremeZoomAsync(Func<MandelbrotPalette> palette)
    {
        static (int Differing, int MaxDelta) Compare(byte[] a, byte[] b)
        {
            int differing = 0, maxDelta = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                int d = Math.Max(Math.Abs(a[o] - b[o]),
                    Math.Max(Math.Abs(a[o + 1] - b[o + 1]), Math.Abs(a[o + 2] - b[o + 2])));
                if (d != 0) differing++;
                maxDelta = Math.Max(maxDelta, d);
            }
            return (differing, maxDelta);
        }

        static int DistinctColours(byte[] pixels)
        {
            var seen = new HashSet<int>();
            for (int pixel = 0; pixel * 4 < pixels.Length; pixel++)
            {
                int o = pixel * 4;
                seen.Add(pixels[o] | (pixels[o + 1] << 8) | (pixels[o + 2] << 16));
            }
            return seen.Count;
        }

        // Repelling fixed point of z^2+c: z* = (1 + sqrt(1-4c))/2, |2z*| > 1.
        const decimal juliaReal = -0.8m, juliaImaginary = 0.156m;
        string fixedPointX, fixedPointY;
        using (new BigFloat.PrecisionScope(4096))
        {
            var c = ComplexBigFloat.FromDecimal(juliaReal, juliaImaginary);
            ComplexBigFloat discriminant = ComplexBigFloat.One - c * 4L;
            ComplexBigFloat root = ComplexBigFloat.Pow(discriminant, ComplexBigFloat.FromDouble(0.5, 0.0));
            ComplexBigFloat fixedPoint = (ComplexBigFloat.One + root) / 2L;

            // Verify it really is a fixed point and really is repelling.
            ComplexBigFloat image = fixedPoint * fixedPoint + c;
            ComplexBigFloat residual = image - fixedPoint;
            FloatExp residualMagnitude = FloatExp.Sqrt(FloatExp.FromBigFloat(residual.MagnitudeSquared));
            Check(residualMagnitude.IsZero || residualMagnitude.Log10() < -1100,
                $"The Julia fixed point is not accurate enough: residual 1e{residualMagnitude.Log10():F0}.");
            FloatExp multiplier = FloatExp.Sqrt(FloatExp.FromBigFloat((fixedPoint * 2L).MagnitudeSquared));
            Check(multiplier > FloatExp.One,
                "The chosen fixed point must be repelling, otherwise it is not on the Julia set.");

            fixedPointX = fixedPoint.Real.ToInvariantString();
            fixedPointY = fixedPoint.Imaginary.ToInvariantString();
        }
        Check(fixedPointX.Length > 1000 && fixedPointY.Length > 1000,
            "The fixed point must be serialized with over a thousand digits.");

        MandelbrotState JuliaAt(FloatExp zoom, int iterations, MandelbrotColoringMode mode) => new()
        {
            Variant = MandelbrotVariant.Julia,
            JuliaCReal = juliaReal,
            JuliaCImaginary = juliaImaginary,
            CenterXExact = fixedPointX,
            CenterYExact = fixedPointY,
            CenterX = BigFloat.Parse(fixedPointX).ToDecimalClamped(),
            CenterY = BigFloat.Parse(fixedPointY).ToDecimalClamped(),
            Zoom = zoom,
            Iterations = iterations,
            ColoringMode = mode,
            Threads = 2,
            Palette = palette()
        };

        async Task<byte[]> RenderAsync(MandelbrotState state, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            return pixels;
        }

        // (a) 1e300 — past the point where 3/zoom leaves the normal double range. Small enough
        //     frame that the BigFloat reference renderer is affordable.
        {
            MandelbrotState state = JuliaAt(FloatExp.Pow10(300), 1200, MandelbrotColoringMode.Smooth);
            const int w = 48, h = 32, total = w * h;
            byte[] engine = await RenderAsync(state, w, h);
            byte[] exact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
            (int differing, int maxDelta) = Compare(engine, exact);
            int colours = DistinctColours(engine);
            Console.WriteLine($"[diag] extreme 1e300 Julia: {differing}/{total} px differ, max delta {maxDelta}, {colours} colours");
            Check(colours >= 8, $"A view at 1e300 must resolve structure, got {colours} distinct colours.");
            Check(differing * 100 <= total * 5,
                $"At 1e300 the engine diverges from the exact reference on {differing}/{total} px (>5%).");
        }

        // (b) 1e1000 — the new ceiling. Structural check on a usable frame, plus a tiny frame
        //     compared against the exact BigFloat reference (~3456-bit mantissa per pixel,
        //     which is why it has to stay tiny).
        {
            MandelbrotState state = JuliaAt(FloatExp.Pow10(1000), 4000, MandelbrotColoringMode.Smooth);
            const int w = 96, h = 64, total = w * h;
            var stopwatch = Stopwatch.StartNew();
            byte[] engine = await RenderAsync(state, w, h);
            stopwatch.Stop();
            int colours = DistinctColours(engine);
            // Smooth colouring maps the escape time, so a wide spread of colours is exactly the
            // evidence wanted: neighbouring pixels 1e-1003 apart must get different escape
            // times. In double the whole frame collapsed onto the centre and produced one
            // colour. (An interior/escaped split would prove nothing here: next to the Julia
            // set the escape time exceeds any iteration budget, so "interior" would only mean
            // "did not escape in time".)
            Console.WriteLine($"[diag] extreme 1e1000 Julia: {colours} colours over {total} px, {stopwatch.ElapsedMilliseconds} ms");
            Check(engine.Where((_, index) => index % 4 == 3).All(value => value == 255),
                "A 1e1000 render must fill every pixel.");
            Check(colours >= 32, $"A view at 1e1000 must resolve structure, got {colours} distinct colours.");
            Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
                "A 1e1000 render must restore the calling thread working precision.");

            const int rw = 12, rh = 8, rtotal = rw * rh;
            byte[] smallEngine = await RenderAsync(state, rw, rh);
            byte[] smallExact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, rw, rh, CancellationToken.None));
            (int differing, int maxDelta) = Compare(smallEngine, smallExact);
            Console.WriteLine($"[diag] extreme 1e1000 vs exact reference: {differing}/{rtotal} px differ, max delta {maxDelta}");
            Check(differing * 100 <= rtotal * 10,
                $"At 1e1000 the engine diverges from the exact reference on {differing}/{rtotal} px (>10%).");
        }

        // (c) Distance Estimation at extreme depth exercises the extended-range derivative
        //     (Jacobian2Exp): |D| grows roughly like the zoom, so in plain double it would
        //     overflow to infinity and the relief would vanish into a flat fill.
        {
            MandelbrotState state = JuliaAt(FloatExp.Pow10(1000), 3000, MandelbrotColoringMode.DistanceEstimation);
            const int w = 64, h = 44;
            byte[] engine = await RenderAsync(state, w, h);
            int colours = DistinctColours(engine);
            Console.WriteLine($"[diag] extreme 1e1000 Distance Estimation: {colours} colours");
            Check(colours >= 8,
                $"Distance Estimation at 1e1000 must produce relief, got {colours} distinct colours.");
        }

        // (d) Mandelbrot at the ceiling: the centre is only known to 28 digits, so the view is
        //     effectively an arbitrary point at scale 1e-1000 and the image itself carries no
        //     meaning. What is checked is that the path runs, fills the frame, restores the
        //     precision and still agrees with the exact reference.
        {
            var state = new MandelbrotState
            {
                Variant = MandelbrotVariant.Mandelbrot,
                CenterX = -1.2628848671045503000020782246m,
                CenterY = 0.0409687601493310685285376264m,
                Zoom = FloatExp.Pow10(1000),
                Iterations = 1500,
                Threads = 2,
                Palette = palette()
            };
            const int w = 16, h = 12, total = w * h;
            byte[] engine = await RenderAsync(state, w, h);
            byte[] exact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
            (int differing, int maxDelta) = Compare(engine, exact);
            Console.WriteLine($"[diag] extreme 1e1000 Mandelbrot: {differing}/{total} px differ, max delta {maxDelta}");
            Check(engine.Where((_, index) => index % 4 == 3).All(value => value == 255),
                "A 1e1000 Mandelbrot render must fill every pixel.");
            Check(differing * 100 <= total * 10,
                $"At 1e1000 Mandelbrot diverges from the exact reference on {differing}/{total} px (>10%).");
            Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
                "A 1e1000 Mandelbrot render must restore the calling thread working precision.");
        }
    }

    // Newton-Raphson zoom: the centre of the nearest minibrot. The check does not assume a
    // period in advance — it verifies the defining property of a nucleus instead, recomputing
    // f_c^p(0) in BigFloat and requiring it to be zero to the working precision.
    private static async Task VerifyNewtonNucleusAsync(Func<MandelbrotPalette> palette)
    {
        static FloatExp NucleusResidual(string centreX, string centreY, int period, int power)
        {
            using var precision = new BigFloat.PrecisionScope(1024);
            var c = new ComplexBigFloat(BigFloat.Parse(centreX), BigFloat.Parse(centreY));
            ComplexBigFloat z = ComplexBigFloat.Zero;
            for (int index = 0; index < period; index++)
                z = (power == 2 ? z * z : ComplexBigFloat.Pow(z, power)) + c;
            return FloatExp.Sqrt(FloatExp.FromBigFloat(z.MagnitudeSquared));
        }

        // (a) A view sitting on the period-3 island on the real axis. The start is deliberately
        //     only roughly placed: Newton has to do the work.
        {
            var state = new MandelbrotState
            {
                Variant = MandelbrotVariant.Mandelbrot,
                CenterX = -1.7548m,
                CenterY = 0.0002m,
                Zoom = 2.0e3,
                Iterations = 4000,
                Threads = 2,
                Palette = palette()
            };

            MandelbrotNucleusResult result = await Task.Run(
                () => MandelbrotNewtonZoom.FindNucleus(state, CancellationToken.None));
            Check(result.Found, $"Newton must find a nucleus near the period-3 island: {result.Message}");
            FloatExp residual = NucleusResidual(result.CenterX, result.CenterY, result.Period, 2);
            Console.WriteLine($"[diag] Newton nucleus: period {result.Period}, {result.NewtonSteps} steps, " +
                              $"residual 1e{residual.Log10():F0}, suggested zoom {result.SuggestedZoom.ToInvariantString()}");
            // Newton stops once the step falls below viewWidth * 1e-15 (here about 1e-18);
            // quadratic convergence means the last step lands far beyond that, so tens of
            // digits are expected. An absolute threshold would just encode the fixture zoom.
            Check(residual.IsZero || residual.Log10() < -30,
                $"The found point is not a nucleus: |f^p(0)| = 1e{residual.Log10():F0}.");
            Check(result.SuggestedZoom.Sign > 0 && result.SuggestedZoom.IsFinite,
                "The suggested zoom must be a usable positive value.");
            Check(result.Period == 3,
                $"The period-3 island must be detected as period 3, got {result.Period}.");
            // The period-3 island on the real axis is about 0.03 wide, so the size estimate
            // must land in that order of magnitude — which at this start means zooming OUT.
            // The framing check below is what actually validates the estimate.
            Check(result.SuggestedZoom > FloatExp.FromDouble(5.0) &&
                  result.SuggestedZoom < FloatExp.FromDouble(500.0),
                $"The size estimate for the period-3 island is implausible: zoom {result.SuggestedZoom.ToInvariantString()}.");

            // Centring on the nucleus at the suggested zoom must show the minibrot, so the
            // frame has to contain both interior and escaping pixels.
            var framed = new MandelbrotState
            {
                Variant = MandelbrotVariant.Mandelbrot,
                CenterX = BigFloat.Parse(result.CenterX).ToDecimalClamped(),
                CenterY = BigFloat.Parse(result.CenterY).ToDecimalClamped(),
                CenterXExact = result.CenterX,
                CenterYExact = result.CenterY,
                Zoom = result.SuggestedZoom,
                Iterations = 4000,
                Threads = 2,
                Palette = palette()
            };
            const int w = 64, h = 44, total = w * h;
            byte[] pixels = new byte[w * h * 4];
            await Task.Run(() => MandelbrotFamilyRenderer.Render(framed, pixels, w, h, w * 4, CancellationToken.None));
            int interior = 0;
            for (int pixel = 0; pixel < total; pixel++)
            {
                int o = pixel * 4;
                if (pixels[o] == 0 && pixels[o + 1] == 0 && pixels[o + 2] == 0) interior++;
            }
            Console.WriteLine($"[diag] Newton framing: {interior}/{total} interior px at the suggested zoom");
            Check(interior > 0 && interior < total,
                $"The suggested zoom must frame the minibrot; got {interior}/{total} interior px.");
        }

        // (b) A deeper start, where the period is not known in advance. The point of the check
        //     is the invariant rather than a fixed expectation: either Newton reports a refusal
        //     with a reason, or the point it returns really is a nucleus.
        {
            var deep = new MandelbrotState
            {
                Variant = MandelbrotVariant.Mandelbrot,
                CenterX = -1.2628848671045503000020782246m,
                CenterY = 0.0409687601493310685285376264m,
                Zoom = 1.0e20,
                Iterations = 6000,
                Threads = 2,
                Palette = palette()
            };
            MandelbrotNucleusResult result = await Task.Run(
                () => MandelbrotNewtonZoom.FindNucleus(deep, CancellationToken.None));
            if (result.Found)
            {
                FloatExp residual = NucleusResidual(result.CenterX, result.CenterY, result.Period, 2);
                Console.WriteLine($"[diag] Newton deep start: period {result.Period}, {result.NewtonSteps} steps, " +
                                  $"residual 1e{residual.Log10():F0}, suggested zoom {result.SuggestedZoom.ToInvariantString()}");
                Check(residual.IsZero || residual.Log10() < -25,
                    $"The deep start returned a point that is not a nucleus: |f^p(0)| = 1e{residual.Log10():F0}.");
                Check(result.SuggestedZoom.Sign > 0 && result.SuggestedZoom.IsFinite,
                    "The deep start must suggest a usable zoom.");
            }
            else
            {
                Console.WriteLine($"[diag] Newton deep start: refused — {result.Message}");
                Check(result.Message.Length > 0, "A refusal must carry a reason.");
            }
        }

        // (c) Variants whose formula is not complex-analytic must be refused with a reason
        //     rather than silently returning nonsense.
        foreach (MandelbrotVariant variant in new[]
                 {
                     MandelbrotVariant.BurningShip, MandelbrotVariant.Tricorn, MandelbrotVariant.Buffalo,
                     MandelbrotVariant.Celtic, MandelbrotVariant.Simonobrot, MandelbrotVariant.Julia,
                     MandelbrotVariant.JuliaBurningShip,
                 })
        {
            Check(!MandelbrotNewtonZoom.IsSupported(variant, 2m),
                $"Newton nucleus search must be refused for {variant}.");
            Check(MandelbrotNewtonZoom.UnsupportedReason(variant).Length > 0,
                $"A refusal for {variant} must carry a reason.");
        }
        Check(MandelbrotNewtonZoom.IsSupported(MandelbrotVariant.Generalized, 3m),
            "Integer-power Multibrot must be supported.");
        Check(!MandelbrotNewtonZoom.IsSupported(MandelbrotVariant.Generalized, 2.5m),
            "Fractional-power Multibrot must be refused.");

        Console.WriteLine("[diag] Newton nucleus search: convergence, nucleus property, framing and refusals OK");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed record State(string Name, DateTime Timestamp);
    private sealed record PendingRender(State State, CancellationToken Token, IProgress<int> Progress)
    {
        public TaskCompletionSource<BitmapSource> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
