using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    // Только для старых синхронных сценариев проверки окон: ожидание прокачивает Dispatcher.
    // Код приложения никогда не блокирует UI ожиданием задачи.
    private static void PrepareWindowForCapture(Window window)
    {
        NumericInput.Prepare(window);
        Task<bool> ready = window switch
        {
            BasinExplorerWindow basin => basin.EnsurePreparedAsync(),
            NewtonPoolsWindow newton => newton.EnsurePreparedAsync(),
            _ => Task.FromResult(true)
        };
        if (!ready.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timeout.Tick += (_, _) => frame.Continue = false;
            var dispatcher = window.Dispatcher;
            _ = ready.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            timeout.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timeout.Stop(); }
        }
        Check(ready.IsCompleted && ready.GetAwaiter().GetResult(), $"{window.GetType().Name}: preparation failed or timed out.");
    }

    private static BasinExplorerState CapturePrepared(BasinExplorerWindow window, string name)
    {
        PrepareWindowForCapture(window);
        return window.CaptureState(name);
    }

    private static NewtonState CapturePrepared(NewtonPoolsWindow window, string name)
    {
        PrepareWindowForCapture(window);
        return window.CaptureState(name);
    }

    private static async Task VerifyUiComputationAsync()
    {
        EnsureWindowThemeStyles();
        using var sandbox = DataSandbox.Create("ui-computation");
        var basins = new BasinExplorerWindow(BasinExplorerKind.GradientDescent);
        var maps = new BasinExplorerWindow(BasinExplorerKind.PeriodicCycles);
        var newton = new NewtonPoolsWindow();
        try
        {
            Check(await basins.EnsurePreparedAsync() && await maps.EnsurePreparedAsync() && await newton.EnsurePreparedAsync(),
                "Initial asynchronous preparation must succeed.");
            basins.Measure(new Size(1100, 800)); basins.Arrange(new Rect(0, 0, 1100, 800));
            maps.Measure(new Size(1100, 800)); maps.Arrange(new Rect(0, 0, 1100, 800));
            var rosenbrock = BasinExplorerCatalog.GetPresets(BasinExplorerKind.GradientDescent)[5].Clone();
            await MeasureUiComputationAsync("Rosenbrock apply", () =>
            {
                basins.LoadState(rosenbrock);
                return basins.EnsurePreparedAsync();
            });
            var prepared = basins.CaptureState("prepared");
            var serial = BasinExplorerWindow.CreateEngine(rosenbrock);
            Check(prepared.PlanarAttractors.Count == serial.PlanarAttractors.Count &&
                prepared.PlanarAttractors.SelectMany(a => a.Points).SequenceEqual(serial.PlanarAttractors.SelectMany(a => a.Points)),
                "Asynchronous preparation must preserve attractor points and their order.");
            Check(RenderBasinFrame(BasinExplorerWindow.CreateEngine(prepared), 24, 18).AsSpan().SequenceEqual(RenderBasinFrame(serial, 24, 18)),
                "Prepared saved targets must preserve rendered pixels.");

            var preview = await basins.RenderStatePreviewAsync(prepared, 24, 18, CancellationToken.None);
            byte[] previewPixels = new byte[24 * 18 * 4];
            preview.CopyPixels(previewPixels, 24 * 4, 0);
            Check(preview.IsFrozen && previewPixels.AsSpan().SequenceEqual(RenderBasinFrame(serial, 24, 18)),
                "Background preview must preserve the synchronous frame and return a frozen bitmap.");

            var slow = BasinExplorerCatalog.GetPresets(BasinExplorerKind.PeriodicCycles)[0].Clone();
            slow.Formula = "z+0.000001*sin(z)";
            slow.MaxIterations = 100000; slow.MaxPeriod = 64; slow.CycleTolerance = 1e-12;
            slow.UseSavedAttractors = true; slow.Attractors = [];
            maps.LoadState(slow);
            Check(await maps.EnsurePreparedAsync(), "Slow-map setup must succeed without discovery.");
            await MeasureUiComputationAsync("256-seed search", () => maps.SearchAttractorsAsync());
            Task search = maps.SearchAttractorsAsync();
            await Dispatcher.Yield(DispatcherPriority.Background);
            var cancelWatch = Stopwatch.StartNew();
            ((Button)maps.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await search.WaitAsync(TimeSpan.FromSeconds(2));
            Check(maps.CaptureState("cancelled").Attractors.Count == 0, "Cancelled search must not publish partial targets.");
            Console.WriteLine($"  Search cancellation: {cancelWatch.Elapsed.TotalMilliseconds:F2} ms");

            search = maps.SearchAttractorsAsync();
            var latest = BasinExplorerCatalog.GetPresets(BasinExplorerKind.PeriodicCycles)[0].Clone();
            maps.LoadState(latest);
            Check(await maps.EnsurePreparedAsync(), "Latest map must prepare.");
            await search;
            Check(maps.CaptureState("latest").Formula == latest.Formula, "Old search must not overwrite a newer formula.");
            await MeasureUiComputationAsync("Orbit analysis", () => basins.ShowOrbitAtAsync(new Point(400, 300)));

            var old = newton.CaptureState("old"); old.Formula = "z^128-1"; old.Roots = [];
            newton.LoadState(old);
            Task<bool> oldPreparation = newton.EnsurePreparedAsync();
            var newest = new NewtonState { Formula = "z^5-1" };
            newton.LoadState(newest);
            Check(await newton.EnsurePreparedAsync(), "Latest Newton formula must prepare.");
            await oldPreparation;
            Check(newton.CaptureState("latest").Formula == newest.Formula && newton.CaptureState("latest").Roots.Count == 5,
                "Old root search must not overwrite the latest roots.");
            var newtonPreview = await newton.RenderStatePreviewAsync(newton.CaptureState("preview"), 24, 18, CancellationToken.None);
            Check(newtonPreview.IsFrozen && newtonPreview.PixelWidth == 24 && newtonPreview.PixelHeight == 18,
                "Newton background preview must prepare its engine and return a frozen bitmap.");
            // Палитра получает текущий список: ручное удаление корня не восстанавливается поиском.
            var engineField = typeof(NewtonPoolsWindow).GetField("_formulaEngine", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var formulaEngine = (NewtonPoolsEngine)engineField.GetValue(newton)!;
            formulaEngine.ReplaceRoots(formulaEngine.Roots.Skip(1));
            Check(await newton.EnsurePreparedAsync() && ReferenceEquals(engineField.GetValue(newton), formulaEngine) &&
                newton.CaptureState("palette").Roots.Count == 4, "Preparing an already applied formula must reuse manual roots.");

            ((TextBox)newton.FindName("RootToleranceBox")).Text = "NaN";
            typeof(NewtonPoolsWindow).GetMethod("FindRootsButton_OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(newton, [newton, new RoutedEventArgs()]);
            Check(await newton.EnsurePreparedAsync() && ((TextBox)newton.FindName("RootToleranceBox")).Text != "NaN",
                "Correcting input must not cancel the calculation started by the same click.");

            basins.LoadState(rosenbrock);
            await basins.ShowOrbitAtAsync(new Point(400, 300));
            var traceField = typeof(BasinExplorerWindow).GetField("_orbitTrace", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Check(((IReadOnlyList<System.Numerics.Complex>)traceField.GetValue(basins)!).Count > 0,
                "An orbit requested during preparation must run when preparation finishes.");
            basins.LoadState(rosenbrock);
            Task waitingOrbit = basins.ShowOrbitAtAsync(new Point(400, 300));
            ((Button)basins.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await waitingOrbit;
            Check(((IReadOnlyList<System.Numerics.Complex>)traceField.GetValue(basins)!).Count == 0,
                "Cancellation must also discard an orbit still awaiting preparation.");

            basins.LoadState(rosenbrock);
            Task<bool> closingPreparation = basins.EnsurePreparedAsync();
            basins.Close();
            string closedStatus = ((TextBlock)basins.FindName("StatusText")).Text;
            Check(!await closingPreparation, "Closing must cancel preparation.");
            Check(((TextBlock)basins.FindName("StatusText")).Text == closedStatus, "Closed window must not receive a late status update.");
            VerifyComputationCancellation();
            Console.WriteLine("PASS (ui-computation): responsive Dispatcher, unchanged targets/pixels, cancellation, latest results, manual roots and closing.");
        }
        finally { basins.Close(); maps.Close(); newton.Close(); }
    }

    private static async Task MeasureUiComputationAsync(string name, Func<Task> start)
    {
        var watch = Stopwatch.StartNew();
        double previous = 0, maxGap = 0;
        int ticks = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(5) };
        timer.Tick += (_, _) => { double now = watch.Elapsed.TotalMilliseconds; maxGap = Math.Max(maxGap, now - previous); previous = now; ticks++; };
        timer.Start();
        try
        {
            Task operation = start();
            double callMs = watch.Elapsed.TotalMilliseconds;
            await operation.WaitAsync(TimeSpan.FromSeconds(15));
            maxGap = Math.Max(maxGap, watch.Elapsed.TotalMilliseconds - previous);
            Check(watch.ElapsedMilliseconds < 100 || ticks > 0, $"{name}: Dispatcher did not run during computation.");
            Console.WriteLine($"  {name}: UI call {callMs:F2} ms; total {watch.Elapsed.TotalMilliseconds:F2} ms; timer ticks {ticks}; max gap {maxGap:F2} ms");
        }
        finally { timer.Stop(); }
    }

    private static void VerifyComputationCancellation()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool stopped = false;
        try { new NewtonPoolsEngine().SetFormula("z^128-1", out _, token: cancelled.Token); }
        catch (OperationCanceledException) { stopped = true; }
        Check(stopped, "Root search must propagate cancellation rather than report a formula error.");
        using var cts = new CancellationTokenSource();
        var engine = new BasinExplorerEngine(BasinExplorerKind.PeriodicCycles, cts.Token) { MaxIterations = 100000 };
        Check(engine.SetMapFormula("z+0.000001*sin(z)", out _, false), "Cancellation map must parse.");
        stopped = false;
        try { engine.DiscoverAttractorsInView(1000, 700, reportProgress: _ => cts.Cancel()); }
        catch (OperationCanceledException) { stopped = true; }
        Check(stopped && engine.Attractors.Count == 0, "Search must stop between seeds and leave its published catalog intact.");
    }
}
