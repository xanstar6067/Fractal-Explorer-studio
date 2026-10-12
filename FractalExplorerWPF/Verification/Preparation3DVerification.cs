using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task Verify3DPreparationAsync(string[] args)
    {
        var theme = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        var waiting = new PreparationOverlay();
        // Анимация требует IsVisible: окно вне экрана и не перехватывает фокус.
        var waitingWindow = new Window { Content = waiting, Left = -20000, Top = -20000, Width = 600, Height = 500, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            waitingWindow.Show(); waitingWindow.UpdateLayout();
            var indicator = (ProgressBar)waiting.FindName("Indicator");
            indicator.ApplyTemplate();
            var segment = (Border)indicator.Template.FindName("WaitingSegment", indicator);
            Check(segment is not null && segment.RenderTransform.HasAnimatedProperties,
                "Indeterminate preparation must animate even though the base theme has no waiting animation.");
            double before = ((TranslateTransform)segment!.RenderTransform).X;
            await Task.Delay(100);
            Check(((TranslateTransform)segment.RenderTransform).X != before,
                "The waiting segment must actually move while the dispatcher remains responsive.");
            waiting.Visibility = Visibility.Collapsed;
            await Task.Delay(100);
            Check(!segment.RenderTransform.HasAnimatedProperties, "Hidden preparation must stop its animation clock.");
            waiting.Visibility = Visibility.Visible;
            waiting.IsIndeterminate = false; waiting.Progress = 42; waiting.UpdateLayout(); indicator.ApplyTemplate();
            Check(indicator.Template.FindName("PART_Indicator", indicator) is not null && indicator.Value == 42,
                "Measurable progress must return to the normal theme template.");
        }
        finally { waitingWindow.Close(); }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var simulationFields = new[] { "_kobBusy", "_exBusy", "_grayBusy", "_turingBusy", "_cahnBusy", "_leniaBusy", "_physarumBusy" };
        foreach (var kind in Enum.GetValues<Fractal3DKind>())
        {
            var catalogWindow = new Fractal3DWindow(kind);
            Check(((Grid)catalogWindow.FindName("InitialPreparationLayer")).Visibility == Visibility.Visible,
                $"{kind} must have a loading screen before any frame can be shown.");
            catalogWindow.Close();
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (simulationFields.Any(field => (bool)typeof(Fractal3DWindow).GetField(field, flags)!.GetValue(catalogWindow)!) && timeout.ElapsedMilliseconds < 60000)
                await Task.Delay(10);
            Check(!simulationFields.Any(field => (bool)typeof(Fractal3DWindow).GetField(field, flags)!.GetValue(catalogWindow)!),
                $"{kind} must release its preparation work when closed.");
        }

        foreach (var kind in new[] { Fractal3DKind.Mandelbulb, Fractal3DKind.Lyapunov3D })
        {
            var window = new Fractal3DWindow(kind);
            try
            {
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(960, 600)); root.Arrange(new Rect(0, 0, 960, 600)); root.UpdateLayout();
                var layer = (Grid)window.FindName("InitialPreparationLayer");
                var overlay = (PreparationOverlay)window.FindName("InitialPreparationOverlay");
                var image = (Image)window.FindName("CanvasImage");
                var method = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync", flags)!;
                Task Render() => (Task)method.Invoke(window, [Enum.Parse(method.GetParameters()[0].ParameterType, "Draft")])!;
                Check(overlay.Visibility == Visibility.Visible && overlay.IsBusy && overlay.IsIndeterminate,
                    "The initial screen must explain work before there is measurable progress.");
                if (args.Length > 1)
                {
                    System.IO.Directory.CreateDirectory(args[1]);
                    var snapshot = new RenderTargetBitmap(960, 600, 96, 96, PixelFormats.Pbgra32);
                    snapshot.Render(root);
                    PhysarumSavePng(snapshot, System.IO.Path.Combine(args[1], $"{kind}-preparation.png"));
                }
                ((TextBox)window.FindName("IterationsBox")).Text = "invalid";
                await Render();
                Check(image.Source is null && layer.Visibility == Visibility.Visible && !overlay.IsBusy && overlay.Message.Contains("Итерации"),
                    "An invalid first frame must show an explanation, without an endless loading indicator.");

                var state = Fractal3DCatalog.CreateDefaultState(kind);
                state.Lyapunov = state.Lyapunov with { Size = 32, Transient = 32, Samples = 32 };
                window.LoadState(state);
                var canceled = Render();
                Check(layer.Visibility == Visibility.Visible && overlay.IsBusy, "Preparation must remain visible during rendering.");
                // The window has not been shown, so the central button has no live binding yet.
                typeof(Fractal3DWindow).GetMethod("InitialPreparationCancel_OnClick", flags)!.Invoke(window, [overlay, new RoutedEventArgs()]);
                await canceled;
                Check(image.Source is null && !overlay.IsBusy && overlay.Message.Contains("отменена"),
                    "Cancellation must discard a late frame and leave a clear status.");
                await Render();
                Check(image.Source is BitmapSource && layer.Visibility == Visibility.Collapsed,
                    "Only displaying a successful first frame closes preparation, including a draft.");
                var cachedFrame = Render();
                Check(layer.Visibility == Visibility.Collapsed, "A cached frame or camera render must not reopen the loading screen.");
                await cachedFrame;
                if (kind == Fractal3DKind.Lyapunov3D)
                {
                    ((TextBox)window.FindName("LyapunovSamplesBox")).Text = "64";
                    var rebuild = Render();
                    Check(layer.Visibility == Visibility.Visible && overlay.IsBusy, "Changing the orbit must visibly rebuild the Lyapunov field.");
                    await rebuild;
                    Check(layer.Visibility == Visibility.Collapsed, "A rebuilt field closes loading only after its frame is displayed.");
                }
            }
            finally { window.Close(); }
        }
        using var renderer = new Fractal3DRenderer();
        var stages = new List<Fractal3DRenderStage>();
        var frame = await renderer.RenderPixelsAsync(Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Mandelbulb),
            80, 60, null, null, CancellationToken.None, new PreparationStageProgress(stages.Add));
        Check(frame.Completed && stages.SequenceEqual(Enum.GetValues<Fractal3DRenderStage>()),
            "The actual renderer must report device wait, initialization, data, shaders and rendering in order.");
        Console.WriteLine("PASS (preparation3d): every 3D mode, first draft, real GPU stages, cancellation, error, retry, close and Lyapunov rebuild.");
    }

    private sealed class PreparationStageProgress(Action<Fractal3DRenderStage> report) : IProgress<Fractal3DRenderStage>
    {
        public void Report(Fractal3DRenderStage value) => report(value);
    }
}
