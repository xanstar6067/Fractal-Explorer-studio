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

// Менеджер сохранений, хранилище по файлу на сохранение, Корзина и миграции.
internal static partial class Program
{
    private static async Task VerifyManagerAsync()
    {
        using var sandbox = DataSandbox.Create("manager");
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
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
        var progressText = (TextBlock)view.FindName("RenderButtonText");
        Check(progressText.Text.StartsWith("60%"), "Renderer progress must reach the button.");
        oldJob.Progress.Report(30); await DrainAsync();
        Check(progressText.Text.StartsWith("60%"), "Out-of-order progress must not go backwards.");
        Select(view, "B");
        Check(oldJob.Token.IsCancellationRequested && jobs.Count == 1, "Switching cancels without starting another render.");
        Click(view, "RenderPreviewButton");
        PendingRender newJob = jobs[^1];
        oldJob.Progress.Report(95);
        oldJob.Completion.SetResult(Pixel(99));
        await DrainAsync();
        Check(Image(view) is null && progressText.Text.StartsWith("Вычисление"), "Stale image and progress must be discarded.");
        Check(File.ReadAllBytes(snapshotPath).SequenceEqual(originalPng), "Stale render must not rewrite the original PNG.");
        newJob.Completion.SetResult(Pixel(42)); await DrainAsync();
        Check(ReadPixel(Image(view)!) == 42 && File.Exists(SavePreviewPath(store, "B")), "Manual render must update its own entry.");

        byte[] beforeCancel = File.ReadAllBytes(SavePreviewPath(store, "B"));
        Click(view, "RenderPreviewButton");
        PendingRender cancelled = jobs[^1];
        Click(view, "RenderPreviewButton");
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
        Check(sandbox.Recycled.Count == 1 && IsReplacementBackup(sandbox.Recycled[0].Original, SavePreviewPath(store, "B")) &&
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
        Check(jobs.Count == beforeSelection + 1, "Selecting an uncached preset must render automatically.");
        Check(((Button)view.FindName("RenderPreviewButton")).IsEnabled && progressText.Text.Contains("отмены"), "Automatic rendering must expose cancellation through the render button.");
        jobs[^1].Completion.SetResult(Pixel(55)); await DrainAsync();
        Check(((Button)view.FindName("RenderPreviewButton")).IsEnabled, "Presets must support manual rerendering.");
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

    private sealed record State(string Name, DateTime Timestamp);

    private sealed record PendingRender(State State, CancellationToken Token, IProgress<int> Progress)
    {
        public TaskCompletionSource<BitmapSource> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
