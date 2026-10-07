using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyJuliaGpuAsync()
    {
        using var sandbox = DataSandbox.Create("julia-gpu");
        using var renderer = new MandelbrotPreviewRenderer();
        const int width = 161, height = 113;
        foreach (var variant in new[] { MandelbrotVariant.Julia, MandelbrotVariant.JuliaBurningShip,
                     MandelbrotVariant.Mandelbrot, MandelbrotVariant.BurningShip })
        foreach (var mode in Enum.GetValues<MandelbrotColoringMode>())
        {
            var def = MandelbrotVariantDefinition.For(variant);
            var state = new MandelbrotState { Variant = variant, CenterX = def.InitialCenterX,
                CenterY = def.InitialCenterY, Zoom = 0.85, JuliaCReal = def.DefaultJuliaReal,
                JuliaCImaginary = def.DefaultJuliaImaginary, ColoringMode = mode, Iterations = 180,
                Palette = new MandelbrotPalette { Colors = [Colors.DarkBlue, Colors.Orange, Colors.White],
                    ColorPeriod = 110, Gamma = 1.3 }, StripeFrequency = 4.3, PalettePhaseOffset = 0.12,
                PaletteScale = 1.7, PaletteWrapMode = MandelbrotPaletteWrapMode.Mirror };
            var gpu = new byte[width * height * 4];
            Check(await Task.Run(() => renderer.TryRender(state, gpu, width, height, CancellationToken.None)),
                $"Hardware GPU preview failed: {renderer.FailureReason}");
            var cpu = new byte[gpu.Length];
            await Task.Run(() => MandelbrotFamilyRenderer.Render(state, cpu, width, height, width * 4, CancellationToken.None));
            long error = 0; int significant = 0;
            for (int i = 0; i < gpu.Length; i += 4)
            {
                int max = 0;
                for (int c = 0; c < 3; c++) { int delta = Math.Abs(gpu[i+c]-cpu[i+c]); error += delta; max = Math.Max(max, delta); }
                if (max > 8) significant++;
            }
            double mean = error / (double)(width * height * 3), fraction = significant / (double)(width * height);
            Console.WriteLine($"{variant}/{mode}: mean error {mean:F4}, pixels >8 {fraction:P3}");
            Check(mean < 1.0 && fraction < 0.015, "GPU orbits and all colour modes must agree with the CPU reference.");
        }
        var boundary = new MandelbrotState { Variant = MandelbrotVariant.Julia,
            Zoom = MandelbrotPreviewRenderer.MaximumZoom, JuliaCReal = -0.8m, JuliaCImaginary = 0.156m };
        Check(renderer.CanRender(boundary), "The precision boundary must still use GPU.");
        foreach (var variant in new[] { MandelbrotVariant.Julia, MandelbrotVariant.JuliaBurningShip })
        {
            boundary.Variant = variant;
            boundary.CenterX = 0.7m;
            boundary.CenterY = 0.3m;
            var gpu = new byte[width*height*4];
            var cpu = new byte[gpu.Length];
            Check(renderer.TryRender(boundary, gpu, width, height, CancellationToken.None), "GPU boundary render must succeed.");
            MandelbrotFamilyRenderer.Render(boundary, cpu, width, height, width*4, CancellationToken.None);
            Check(gpu.Zip(cpu).Average(pair => Math.Abs(pair.First-pair.Second)) < 1,
                "At the zoom boundary the pixel grid and colours must match the CPU path.");
        }
        boundary.Zoom *= 1.01;
        var untouched = Enumerable.Repeat((byte)77, width*height*4).ToArray();
        Check(!renderer.TryRender(boundary, untouched, width, height, CancellationToken.None) && untouched.All(b => b == 77),
            "Deep zoom must select CPU without submitting or changing pixels.");
        boundary.Zoom = 1;
        Check(renderer.CanRender(boundary), "Zooming out must restore GPU eligibility.");
        boundary.Iterations = MandelbrotPreviewRenderer.MaximumIterations + 1;
        Check(!renderer.CanRender(boundary), "Long orbits must stay cancellable on CPU.");
        boundary.Iterations = 500;
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel(); bool canceled = false;
            try { renderer.TryRender(boundary, untouched, width, height, cts.Token); }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "Cancellation must not disable a healthy GPU.");
        }
        Check(renderer.IsAvailable, "Canceled work must leave GPU available.");
        // Fail the hardware path deterministically; the caller must be able to use CPU and
        // further live input must not attempt to keep up on that backend.
        using (var failed = new MandelbrotPreviewRenderer())
        {
            typeof(MandelbrotPreviewRenderer).GetField("_failed", BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(failed, true);
            Check(!failed.TryRender(boundary, untouched, width, height, CancellationToken.None), "Failed GPU must return CPU fallback.");
        }
        var watch = Stopwatch.StartNew();
        for (int i=0; i<4; i++)
        {
            boundary.JuliaCReal = -0.8m + i*0.002m;
            await Task.Run(() => renderer.TryRender(boundary, new byte[800*600*4], 800, 600, CancellationToken.None));
        }
        Console.WriteLine($"GPU 800x600/500 iterations: {watch.Elapsed.TotalMilliseconds/4:F1} ms/frame (orbit, readback and palette).");
        await VerifyJuliaPickerLifecycleAsync();
        Console.WriteLine("PASS (julia-gpu): hardware/CPU colours, precision fallback, cancellation, persistent picker, latest C and CPU live gating.");
    }

    private static async Task VerifyJuliaPickerLifecycleAsync()
    {
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object? Field(object o, string name) => o.GetType().GetField(name, flags)!.GetValue(o);
        void Set(object o, string name, object value) => o.GetType().GetField(name, flags)!.SetValue(o, value);
        object? Invoke(object o, string name, params object?[] args) => o.GetType().GetMethod(name, flags)!.Invoke(o, args);
        async Task WaitFrame(MandelbrotWindow window)
        {
            for (int i=0; i<100; i++)
            {
                await Task.Delay(40);
                if (!(bool)Field(window, "_isRendering")! &&
                    !((System.Windows.Threading.DispatcherTimer)Field(window, "_renderTimer")!).IsEnabled &&
                    Field(window, "_stableBitmap") is BitmapSource) return;
            }
            throw new InvalidOperationException("Julia window did not finish: " + ((TextBlock)window.FindName("StatusText")).Text);
        }
        foreach (var variant in new[] { MandelbrotVariant.Julia, MandelbrotVariant.JuliaBurningShip })
        {
            var owner = new MandelbrotWindow(variant) { WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false, Width = 850, Height = 600 };
            try
            {
                owner.Show();
                await WaitFrame(owner);
                Invoke(owner, "JuliaConstantButton_OnClick", owner, new RoutedEventArgs());
                var picker = (JuliaConstantPickerWindow)Field(owner, "_constantPicker")!;
                picker.WindowStartupLocation = WindowStartupLocation.Manual;
                picker.Left = -30000; picker.Top = -30000; picker.ShowActivated = false; picker.ShowInTaskbar = false;
                ((TextBox)picker.FindName("RealBox")).Text = "-0.72";
                ((TextBox)picker.FindName("ImaginaryBox")).Text = "0.18";
                Invoke(picker, "Accept_OnClick", picker, new RoutedEventArgs());
                Check(picker.IsVisible && owner.IsEnabled, "Apply must leave a nonmodal picker open and the owner enabled.");
                await WaitFrame(owner);
                Check(owner.CaptureState("test").JuliaCReal == -0.72m, "Apply must update the owner immediately.");
                Invoke(owner, "JuliaConstantButton_OnClick", owner, new RoutedEventArgs());
                Check(ReferenceEquals(picker, Field(owner,"_constantPicker")), "Repeated open must reuse the same picker.");
                ((CheckBox)picker.FindName("LivePreviewBox")).IsChecked = true;
                Set(picker, "_selecting", true);
                for (int i=0; i<12; i++)
                {
                    ((TextBox)picker.FindName("RealBox")).Text = (-0.7m + i*0.003m).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Set(picker, "_selectionChanged", true);
                    Invoke(picker, "LiveTimer_OnTick", null, EventArgs.Empty);
                    await Task.Delay(3);
                }
                await WaitFrame(owner);
                Check(owner.CaptureState("test").JuliaCReal == -0.667m && !(bool)Field(owner,"_liveRenderPending")!,
                    "Rapid live input must render the latest C with no pending queue.");
                var draft = (BitmapSource)Field(owner, "_stableBitmap")!;
                var surface = RenderSurfaceMetrics.Measure((FrameworkElement)owner.FindName("CanvasHost"));
                Check(draft.PixelWidth < surface.PixelWidth, "Live pointer input must render a fast adaptive frame before release.");
                // Capture loss follows the same commit path as leaving the map during a drag.
                Invoke(picker, "MapHost_OnLostMouseCapture", picker,
                    new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0));
                await WaitFrame(owner);
                var completed = (BitmapSource)Field(owner,"_stableBitmap")!;
                Check(completed.PixelWidth == surface.PixelWidth && completed.PixelHeight == surface.PixelHeight,
                    "The final drag point must restore full image size.");
                ((ComboBox)owner.FindName("PreviewSsaaBox")).SelectedIndex = 1;
                await WaitFrame(owner);
                Check(((BitmapSource)Field(owner,"_stableBitmap")!).PixelWidth == surface.PixelWidth,
                    "GPU SSAA must return a DPI-sized image, not an oversized intermediate.");
                ((ComboBox)owner.FindName("PreviewSsaaBox")).SelectedIndex = 0;
                await WaitFrame(owner);
                var before = owner.CaptureState("test").JuliaCReal;
                Set(owner, "_zoom", FloatExp.FromDouble(2_000_000));
                Check(picker.CanLivePreview?.Invoke() == false &&
                      MandelbrotPreviewRenderer.Supports((MandelbrotState)Invoke(picker,"CreateMapState")!),
                    "CPU main view and GPU map must be independent.");
                ((TextBox)picker.FindName("RealBox")).Text = "-0.6";
                Invoke(owner,"ApplyPickerConstant", picker, true);
                Check(owner.CaptureState("test").JuliaCReal == before, "CPU main view must ignore live drag events.");
                Invoke(picker,"Accept_OnClick", picker, new RoutedEventArgs());
                Check(owner.CaptureState("test").JuliaCReal == -0.6m && picker.IsVisible,
                    "Explicit Apply must still work with CPU main view.");
                Set(owner,"_zoom", FloatExp.FromDouble(1));
                Check(picker.CanLivePreview?.Invoke() == true, "Zoom out must resume live eligibility.");
                // Conversely, zooming the map beyond the GPU boundary does not disable GPU Julia.
                Set(picker,"_zoom", (decimal)MandelbrotPreviewRenderer.MaximumZoom * 10m);
                Invoke(picker,"ScheduleRender");
                for (int i=0; i<250; i++)
                {
                    await Task.Delay(40);
                    if (Field(picker,"_renderCts") is null && !((System.Windows.Threading.DispatcherTimer)Field(picker,"_renderTimer")!).IsEnabled) break;
                }
                Check(((TextBlock)picker.FindName("StatusText")).Text.StartsWith("ЦП") && picker.CanLivePreview?.Invoke() == true,
                    $"A zoomed CPU parameter map must work independently of GPU Julia: {((TextBlock)picker.FindName("StatusText")).Text}; live={picker.CanLivePreview?.Invoke()}, gpu={((MandelbrotPreviewRenderer)Field(owner,"_gpuPreview")!).FailureReason}");
                owner.Close();
                Check(!picker.IsVisible, "Closing the owner must close and cancel the picker.");
                await Task.Delay(80);
            }
            finally { owner.Close(); }
        }
    }
}
