using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Migrations;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyTuringGpuAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("turing-gpu");
        string? output = args.Length > 1 ? Directory.CreateDirectory(Path.GetFullPath(args[1])).FullName : null;
        var state = new TuringState { GridSize = 64, WarmupSteps = 0, Layers = [new() { Radius = 8, Amount = .017 }, new() { Radius = 24, Amount = .05 }] };
        state.Checkpoint = new TuringSimulation(state).Snapshot();
        foreach (TuringBoundary boundary in Enum.GetValues<TuringBoundary>())
        {
            state.Boundary = boundary;
            using var gpu = new TuringGpuEngine(state); using var cpu = new TuringCpuEngine(state);
            gpu.Advance(1, state, CancellationToken.None); cpu.Advance(1, state, CancellationToken.None);
            var actual = gpu.Snapshot(); var expected = cpu.Snapshot();
            double error = actual.Field.Zip(expected.Field, (a,b) => Math.Abs(a-b)).Max();
            Check(error < .0005, $"GPU {boundary} must match CPU; max error = {error:G5}.");
            Check(actual.Scales.SequenceEqual(expected.Scales), "GPU competition must select the same scales on the reference field.");
            foreach (TuringColoring mode in Enum.GetValues<TuringColoring>())
            {
                state.Coloring = mode; state.Zoom = 1.7; state.PanX = .08;
                byte[] pixels = gpu.RenderFrame(state, 400, 240, CancellationToken.None);
                byte[] reference = TuringRenderer.RenderFrame(actual, state, 400, 240, CancellationToken.None);
                double mean = pixels.Zip(reference, (a,b) => Math.Abs(a-b)).Average();
                Check(mean < 1, $"GPU {mode} view/colouring must match CPU; error = {mean:G4}.");
                byte[] manual = gpu.RenderFrame(state, 336, 192, CancellationToken.None, 1.3);
                byte[] manualReference = TuringRenderer.RenderFrame(actual, state, 336, 192, CancellationToken.None, 1.3);
                Check(manual.Zip(manualReference, (a,b) => Math.Abs(a-b)).Average() < 1,
                    "Manual buffers must preserve view geometry even when their aspect differs from the canvas.");
                Check(Enumerable.Range(0,pixels.Length/4).All(i => pixels[i*4+3] == 255), "GPU output must be opaque.");
                if (output is not null) SaveTuringPng(BitmapSource.Create(400,240,96,96,PixelFormats.Bgra32,null,pixels,1600),Path.Combine(output,$"gpu-{boundary}-{mode}.png"));
            }
            Console.WriteLine($"Turing GPU {gpu.DeviceName}, {boundary}: step error {error:G4}.");
        }
        state.Zoom = 1; state.PanX = 0; state.Coloring = TuringColoring.Relief; state.Boundary = TuringBoundary.Wrap;
        using (var gpu = new TuringGpuEngine(state))
        {
            state.Symmetry = 4; state.Mirror = true;
            gpu.Paint(.43,.32,.08,.4,TuringBrush.Noise,state); gpu.Advance(3,state,CancellationToken.None);
            var cp = gpu.Snapshot(); double error = 0;
            for (int y=0;y<64;y++) for(int x=0;x<64;x++) error=Math.Max(error,Math.Abs(cp.Field[y*64+x]-cp.Field[x*64+63-y]));
            Check(error < .0001, "GPU symmetric evolution must preserve fourfold symmetry.");
            state.Checkpoint = cp; var store = new TuringSaveStore(); state.SaveName = "GPU checkpoint"; store.Save(state);
            var loaded = store.Load().Single(); using var restored = new TuringGpuEngine(loaded);
            gpu.Advance(5,state,CancellationToken.None); restored.Advance(5,loaded,CancellationToken.None);
            Check(gpu.Snapshot().Field.SequenceEqual(restored.Snapshot().Field), "GPU disk checkpoints must continue bit-for-bit on this device.");
            var before = gpu.Snapshot(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { gpu.Advance(10,state,cancel.Token); throw new Exception("Expected cancellation."); } catch(OperationCanceledException) { }
            Check(gpu.Snapshot().Field.SequenceEqual(before.Field), "Canceled GPU iterations must not change the committed field.");
            using var during = new CancellationTokenSource(); during.CancelAfter(30);
            try { gpu.Advance(2000,state,during.Token); throw new Exception("Expected cancellation during GPU work."); } catch(OperationCanceledException) { }
            var after = gpu.Snapshot();
            var replayState = state.Clone(); replayState.Checkpoint = before;
            using var replay = new TuringGpuEngine(replayState);
            replay.Advance((int)(after.StepCount - before.StepCount), replayState, CancellationToken.None);
            Check(after.Field.SequenceEqual(replay.Snapshot().Field), "Mid-pass cancellation must retain only complete GPU iterations.");
        }
        foreach (int size in new[] { 192, 1024, 2048 })
        {
            var large = new TuringState { GridSize=size, WarmupSteps=0 };
            using var gpu = new TuringGpuEngine(large); gpu.Advance(2,large,CancellationToken.None);
            var cp = gpu.Snapshot(); Check(cp.Field.All(v=>float.IsFinite(v)&&v is >= -1 and <= 1)&&cp.StepCount==2,"Large GPU fields must be finite and normalized.");
        }
        var benchmark = new TuringState { GridSize=512, WarmupSteps=0 };
        using (var gpu = new TuringGpuEngine(benchmark))
        using (var cpu = new TuringCpuEngine(benchmark))
        {
            var watch=Stopwatch.StartNew(); cpu.Advance(5,benchmark,CancellationToken.None); double cpuMs=watch.Elapsed.TotalMilliseconds/5;
            watch.Restart(); gpu.Advance(20,benchmark,CancellationToken.None); gpu.Snapshot(); double gpuMs=watch.Elapsed.TotalMilliseconds/20;
            Console.WriteLine($"Turing 512 benchmark: CPU {cpuMs:F2} ms/step, GPU {gpuMs:F2} ms/step ({cpuMs/gpuMs:F1}×), {gpu.DeviceName}.");
        }
        var legacy = System.Text.Json.Nodes.JsonNode.Parse("{\"SaveFormatVersion\":1}")!.AsObject(); SaveFormat.UpgradeInPlace("TuringPatterns",legacy);
        Check(legacy["Backend"]!.GetValue<int>()==0,"Old checkpoints must retain their CPU backend.");
        Check(TuringComputeShader.CacheEntries.All(e=>File.Exists(AppPaths.GetShaderCacheFile(e.Key))),"All compute variants must use the common shader cache.");
        try
        {
            TuringEngineFactory.GpuFactoryOverrideForTests = _ => throw new NotSupportedException("Simulated missing GPU");
            using var fallback = TuringEngineFactory.Create(state, out string? reason);
            Check(fallback.Backend == TuringBackend.Cpu && reason?.Contains("Simulated missing GPU") == true && fallback.Snapshot().Field.SequenceEqual(state.Checkpoint!.Field),
                "Unavailable GPU must fall back to CPU with the exact checkpoint and an explanation.");
        }
        finally { TuringEngineFactory.GpuFactoryOverrideForTests = null; }
        await VerifyTuringGpuWindowAsync(output);
        Console.WriteLine("PASS (turing-gpu): hardware compute, CPU agreement, symmetric brush, exact disk continuation, large scans, cache, backend switching, custom grids and DPI/manual buffers.");
    }

    private static async Task VerifyTuringGpuWindowAsync(string? output)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var styles=new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if(!Application.Current.Resources.MergedDictionaries.Any(d=>d.Source==styles)) Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=styles});
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        var window=new TuringWindow();
        object? Invoke(string name,params object[] p)=>typeof(TuringWindow).GetMethod(name,flags)!.Invoke(window,p);
        T Field<T>(string name)=>(T)typeof(TuringWindow).GetField(name,flags)!.GetValue(window)!;
        async Task Idle() { await Task.Delay(220); await (Task)Invoke("WaitForFrameIdleAsync")!; }
        async Task SwitchIdle()
        {
            var watch=Stopwatch.StartNew();
            while ((Field<bool>("_qualityPending")||Field<bool>("_resetting"))&&watch.ElapsedMilliseconds<10000) await Task.Delay(10);
            Check(!Field<bool>("_qualityPending")&&!Field<bool>("_resetting"),"Engine switching must finish."); await Idle();
        }
        try
        {
            var state=new TuringState{GridSize=128,WarmupSteps=12};
            await (Task)Invoke("ResetAsync",state,false)!; Check(window.CaptureState("GPU").Backend==TuringBackend.Gpu,"GPU must be the default usable engine.");
            var root=(FrameworkElement)window.Content;
            void Layout(int w,int h) { root.Measure(new System.Windows.Size(w,h)); root.Arrange(new Rect(0,0,w,h)); root.UpdateLayout(); }
            Layout(1280,800); await Idle();
            var canvas=(FrameworkElement)window.FindName("CanvasHost"); var dpi=VisualTreeHelper.GetDpi(canvas); var bitmap=Field<WriteableBitmap>("_bitmap");
            Check(bitmap.PixelWidth==(int)Math.Ceiling(canvas.ActualWidth*dpi.DpiScaleX)&&bitmap.PixelHeight==(int)Math.Ceiling(canvas.ActualHeight*dpi.DpiScaleY),"Auto buffer must track physical canvas pixels.");
            var before=window.CaptureState("before"); ((ComboBox)window.FindName("BackendBox")).SelectedIndex=1; await SwitchIdle();
            var cpu=window.CaptureState("CPU"); Check(cpu.Backend==TuringBackend.Cpu&&cpu.Checkpoint!.Field.SequenceEqual(before.Checkpoint!.Field)&&cpu.Checkpoint.StepCount==before.Checkpoint.StepCount,"GPU to CPU switching must transfer the exact field and time.");
            ((ComboBox)window.FindName("BackendBox")).SelectedIndex=0; await SwitchIdle();
            Check(window.CaptureState("GPU again").Checkpoint!.Field.SequenceEqual(cpu.Checkpoint!.Field),"CPU to GPU switching must preserve all field bits.");
            ((TextBox)window.FindName("GridSizeBox")).Text="192"; Invoke("GridSize_OnApply",window,new RoutedEventArgs()); await SwitchIdle();
            Check(window.CaptureState("custom").GridSize==192&&window.CaptureState("custom").Checkpoint!.StepCount==cpu.Checkpoint.StepCount,"Custom grid input must resize without growing time.");
            ((CheckBox)window.FindName("AutoFrameBox")).IsChecked=false;
            ((TextBox)window.FindName("FrameWidthBox")).Text="336"; ((TextBox)window.FindName("FrameHeightBox")).Text="192"; Invoke("FrameSize_OnApply",window,new RoutedEventArgs()); await Idle();
            bitmap=Field<WriteableBitmap>("_bitmap"); Check(bitmap.PixelWidth==336&&bitmap.PixelHeight==192,"Manual buffer dimensions must reach the renderer.");
            ((TextBox)window.FindName("FrameWidthBox")).Text="99999"; Invoke("FrameSize_OnApply",window,new RoutedEventArgs());
            Check(window.CaptureState("invalid").FrameWidth==336&&!string.IsNullOrWhiteSpace(((TextBlock)window.FindName("FrameSizeError")).Text),"Invalid buffer input must leave the current buffer intact.");
            ((CheckBox)window.FindName("AutoFrameBox")).IsChecked=true; Invoke("FrameSize_OnApply",window,new RoutedEventArgs()); Layout(1400,900); await Idle();
            bitmap=Field<WriteableBitmap>("_bitmap"); Check(bitmap.PixelWidth==(int)Math.Ceiling(canvas.ActualWidth*dpi.DpiScaleX),"Auto mode must resume resizing after manual mode.");
            var warming = new TuringState { GridSize=128, WarmupSteps=2000 };
            Task preparation = (Task)Invoke("ResetAsync", warming, false)!;
            var preparationWatch = Stopwatch.StartNew();
            while (((ProgressBar)window.FindName("PreparationProgress")).Value == 0 && !preparation.IsCompleted && preparationWatch.ElapsedMilliseconds < 10000)
                await Task.Delay(10);
            Invoke("CancelPreparation_OnClick",window,new RoutedEventArgs());
            await preparation;
            var stopped = window.CaptureState("stopped");
            Check(stopped.Checkpoint!.StepCount is > 0 and < 2000 && !Field<bool>("_running"),
                $"Stopping GPU preparation must retain the completed steps and leave a paused usable field (step {stopped.Checkpoint.StepCount}).");
            if(output is not null)
            {
                await (Task)Invoke("ResetAsync", TuringPresets.All[0].CreateState(), false)!; await Idle();
                ((TabControl)window.FindName("SettingsTabs")).SelectedIndex=0;
                ((ScrollViewer)window.FindName("SettingsScroll")).ScrollToBottom(); root.UpdateLayout();
                var image=new RenderTargetBitmap(1400,900,96,96,PixelFormats.Pbgra32); image.Render(root); SaveTuringPng(image,Path.Combine(output,"turing-gpu-window.png"));
            }
            try
            {
                TuringEngineFactory.GpuFactoryOverrideForTests = s => new FailingTuringEngine(s);
                var failing = window.CaptureState("failure"); failing.Backend = TuringBackend.Gpu;
                await (Task)Invoke("ResetAsync", failing, false)!;
                var committed = window.CaptureState("committed");
                await (Task)Invoke("ProduceFrameAsync", 1)!;
                Check(window.CaptureState("recovered").Backend == TuringBackend.Cpu &&
                    window.CaptureState("recovered").Checkpoint!.Field.SequenceEqual(committed.Checkpoint!.Field),
                    "A GPU failure must recover the last displayed checkpoint on CPU.");
            }
            finally { TuringEngineFactory.GpuFactoryOverrideForTests = null; }
            Task worker=(Task)Invoke("ProduceFrameAsync",8)!; window.Close(); await worker;
        }
        finally {window.Close();}
    }

    private sealed class FailingTuringEngine(TuringState state) : ITuringEngine
    {
        private readonly TuringCpuEngine _cpu = new(state);
        public TuringBackend Backend => TuringBackend.Gpu;
        public string DeviceName => "Simulated device failure";
        public void Advance(int steps, TuringState state, CancellationToken token)
        {
            if (steps > 0) throw new IOException("Simulated device removal");
            _cpu.Advance(steps, state, token);
        }
        public void Paint(double x, double y, double radius, double strength, TuringBrush brush, TuringState state) => _cpu.Paint(x,y,radius,strength,brush,state);
        public TuringCheckpoint Snapshot() => _cpu.Snapshot();
        public byte[] RenderFrame(TuringState state, int width, int height, CancellationToken token, double displayAspect = 0) => _cpu.RenderFrame(state,width,height,token,displayAspect);
        public void Dispose() => _cpu.Dispose();
    }
}
