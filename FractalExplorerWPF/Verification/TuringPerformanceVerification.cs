using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task MeasureTuringFramesAsync()
    {
        using var sandbox = DataSandbox.Create("turing-perf");
        await Task.Run(() =>
        {
            var standard = new TuringState { WarmupSteps = 0 };
            var symmetric = standard.Clone(); symmetric.Symmetry = 4; symmetric.Mirror = true;
            var shortBatch = new TuringState { GridSize = 128, WarmupSteps = 0, Layers = [new() { Radius = 8, Amount = .017 }] };
            MeasureTuringFrames(new TuringCpuEngine(standard), standard, "CPU standard");
            MeasureTuringFrames(new TuringGpuEngine(standard), standard, "GPU standard");
            MeasureTuringFrames(new TuringGpuEngine(symmetric), symmetric, "GPU fourfold mirror");
            MeasureTuringFrames(new TuringGpuEngine(shortBatch), shortBatch, "GPU short batch");
        });
        await MeasureTuringWindowFramesAsync();
    }

    private static void MeasureTuringFrames(ITuringEngine engine, TuringState state, string label)
    {
        using (engine)
        {
            const int width = 1024, height = 768, frames = 8;
            Console.WriteLine($"Turing {label}: {engine.DeviceName}, {state.GridSize}², {state.StepsPerFrame} steps/frame, {width}×{height} pixels.");
            engine.Advance(1, state, CancellationToken.None); engine.Snapshot();
            engine.RenderFrame(state, width, height, CancellationToken.None);
            double advance = 0, snapshot = 0, render = 0, slowest = 0;
            var watch = new Stopwatch();
            for (int i = 0; i < frames; i++)
            {
                var frameWatch = Stopwatch.StartNew();
                watch.Restart(); engine.Advance(state.StepsPerFrame, state, CancellationToken.None); advance += watch.Elapsed.TotalMilliseconds;
                watch.Restart(); engine.Snapshot(); snapshot += watch.Elapsed.TotalMilliseconds;
                watch.Restart(); engine.RenderFrame(state, width, height, CancellationToken.None); render += watch.Elapsed.TotalMilliseconds;
                slowest = Math.Max(slowest, frameWatch.Elapsed.TotalMilliseconds);
            }
            double total = (advance + snapshot + render) / frames;
            Console.WriteLine($"Turing {label}: evolve {advance / frames:F2} ms, snapshot {snapshot / frames:F2} ms, render/readback {render / frames:F2} ms; total {total:F2} ms, slowest {slowest:F2} ms ({1000 / total:F1} frames/s before WPF presentation).");
        }
    }

    private static async Task MeasureTuringWindowFramesAsync()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        var window = new TuringWindow();
        object? Invoke(string name, params object[] p) => typeof(TuringWindow).GetMethod(name, flags)!.Invoke(window, p);
        T Field<T>(string name) => (T)typeof(TuringWindow).GetField(name, flags)!.GetValue(window)!;
        try
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new System.Windows.Size(1280, 800)); root.Arrange(new Rect(0, 0, 1280, 800)); root.UpdateLayout();
            var state = new TuringState { WarmupSteps = 0 };
            await (Task)Invoke("ResetAsync", state, false)!;
            Check(Field<ITuringEngine>("_simulation").Backend == TuringBackend.Gpu, "Window benchmark requires hardware GPU.");
            await Task.Delay(250); await (Task)Invoke("WaitForFrameIdleAsync")!;
            var before = window.CaptureState("before");
            Invoke("SetRunning", true); await Task.Delay(3000); Invoke("SetRunning", false);
            await (Task)Invoke("WaitForFrameIdleAsync")!;
            var after = window.CaptureState("after"); var bitmap = Field<WriteableBitmap>("_bitmap");
            Console.WriteLine($"Turing WPF GPU: {Field<double>("_measuredFps"):F1} FPS, {state.GridSize}², {state.StepsPerFrame} steps/frame, {bitmap.PixelWidth}×{bitmap.PixelHeight} pixels; advanced {after.Checkpoint!.StepCount - before.Checkpoint!.StepCount} steps (hidden window, including timer and WritePixels).");
        }
        finally { window.Close(); }
    }
}
