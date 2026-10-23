using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyMandelShapeAtlasAsync(Fractal3DKind kind)
    {
        string prefix = kind == Fractal3DKind.Mandelbulb ? "Mandelbulb" : "Mandelbox";
        using var sandbox = DataSandbox.Create(prefix + "Atlas");
        EnsureWindowThemeStyles();
        var source = Fractal3DCatalog.CreateDefaultState(kind);
        source.Power = 8.123456789012345;
        source.BoxFoldingLimit = 1.1234567890123457;
        source.BoxInversionShape = BoxInversionShape.Ellipsoid;
        source.BoxInversionStretch = 1.3;
        var main = new Fractal3DWindow(kind);
        Fractal3DParameterAtlasWindow? atlas = null;
        try
        {
            main.LoadState(source);
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            var before = main.CaptureState("view");
            Check(before.Power == source.Power && before.BoxFoldingLimit == source.BoxFoldingLimit, "Loading rounded the source parameters.");
            Check(Element<Button>(main, prefix + "AtlasButton").Visibility == Visibility.Visible, "Atlas entry must be visible.");
            string otherPrefix = kind == Fractal3DKind.Mandelbulb ? "Mandelbox" : "Mandelbulb";
            Check(Element<Button>(main, otherPrefix + "AtlasButton").Visibility == Visibility.Collapsed, "Wrong mode's atlas is exposed.");
            atlas = CreateAtlas();
            Check(atlas.Owner == main, "Atlas lost the source owner.");
            var unchanged = before.Clone(); atlas.ApplySelectionTo(unchanged);
            Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(before), "An untouched atlas rounded source parameters.");
            var minX = Element<TextBox>(atlas, "MinRealBox"); var maxX = Element<TextBox>(atlas, "MaxRealBox");
            var minY = Element<TextBox>(atlas, "MinImaginaryBox"); var maxY = Element<TextBox>(atlas, "MaxImaginaryBox");
            Check(new[] { minX, maxX, minY, maxY }.All(NumericInput.IsRegistered), "Axis bounds need NumericInput.");
            if (kind == Fractal3DKind.Mandelbulb)
            {
                minX.Text = "5.2"; maxX.Text = "8.2";
                minY.Text = "63"; maxY.Text = "80"; NumericInput.CommitAll(atlas);
                Check(maxY.Text == "64" && NumericInput.GetHasNotice(maxY), "Iterations must clamp at the mode limit of 64.");
                Check(minX.Text == "5.2", "Power must retain fractional values.");
            }
            else
            {
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                var fixedBoxes = Element<WrapPanel>(atlas, "FixedPointPanel").Children.OfType<StackPanel>()
                    .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
                Check(fixedBoxes.Length == 3 && fixedBoxes.All(NumericInput.IsRegistered), "Three validated folding parameters are required.");
                for (int a = 0; a < 3; a++) for (int b = a + 1; b < 3; b++)
                {
                    x.SelectedIndex = a; y.SelectedIndex = b;
                    Check(x.SelectedIndex != y.SelectedIndex && fixedBoxes.Count(box => box.IsEnabled) == 1, "Each pair must leave one fixed parameter.");
                }
                x.SelectedIndex = 0; y.SelectedIndex = 1;
                minX.Text = "-20"; maxY.Text = "20"; NumericInput.CommitAll(atlas);
                Check(minX.Text == "-8" && maxY.Text == "4", "Scale and radius bounds must differ.");
                fixedBoxes[2].Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(NumericInput.IsValid(fixedBoxes[2]) && NumericInput.GetHasNotice(fixedBoxes[2]), "Invalid fixed input must recover with feedback.");
                minX.Text = "1.8"; maxX.Text = "2.2"; minY.Text = ".5"; maxY.Text = ".9";
            }
            await Build(atlas);
            var tiles = Element<UniformGrid>(atlas, "TilesPanel");
            int count = kind == Fractal3DKind.Mandelbulb ? 6 : 9;
            Check(tiles.Children.Count == count && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "Preview generation is incomplete or repeats integer rows.");
            // Верхняя строка Мандельбульба обязана использовать 64 итерации, а не общий лимит превью 32.
            Click((Button)tiles.Children[0]);
            var expected = before.Clone(); atlas.ApplySelectionTo(expected);
            Check(kind != Fractal3DKind.Mandelbulb || expected.Iterations == 64, "Selected iterations were truncated to the preview budget.");
            Check(kind != Fractal3DKind.Mandelbox || expected.BoxFoldingLimit == before.BoxFoldingLimit, "The fixed folding limit lost double precision.");
            var preview = expected.Clone();
            if (kind == Fractal3DKind.Mandelbox) preview.Iterations = Math.Clamp(preview.Iterations, 1, 32);
            preview.MaxSteps = Math.Min(preview.MaxSteps, 160);
            preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
            preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false;
            using (var renderer = new Fractal3DRenderer())
            {
                var reference = await renderer.RenderPixelsAsync(preview, 144, 108, null, null, CancellationToken.None);
                var actual = new byte[144 * 108 * 4];
                ((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source).CopyPixels(actual, 144 * 4, 0);
                Check(actual.SequenceEqual(reference.Buffer), "Atlas preview differs from direct rendering with the selected parameters.");
                Click((Button)tiles.Children[tiles.Children.Count - 1]);
                var other = new byte[actual.Length];
                ((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source).CopyPixels(other, 144 * 4, 0);
                Check(!actual.SequenceEqual(other), "Different parameter samples produced identical previews.");
            }
            // Применение через реальное событие сохраняет всю остальную модель.
            expected = before.Clone(); atlas.ApplySelectionTo(expected);
            Click(Element<Button>(atlas, "ApplyButton")); atlas = null;
            var after = main.CaptureState("view"); after.Timestamp = expected.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Applying the atlas changed unrelated view settings or rounded parameters.");
            var store = new Fractal3DSaveStore(kind); store.Save(after);
            var loaded = store.Load().Single(); main.LoadState(loaded);
            var restored = main.CaptureState("view"); restored.Timestamp = after.Timestamp;
            // Загрузка камеры проходит через float-кватернион, её углы допускают погрешность обратного преобразования.
            Check(Math.Abs(restored.CameraYaw - after.CameraYaw) < .0001 && Math.Abs(restored.CameraPitch - after.CameraPitch) < .0001 &&
                Math.Abs(restored.CameraRoll - after.CameraRoll) < .0001, "Saving and loading changed the camera orientation.");
            restored.CameraYaw = after.CameraYaw; restored.CameraPitch = after.CameraPitch; restored.CameraRoll = after.CameraRoll;
            Check(JsonSerializer.Serialize(restored) == JsonSerializer.Serialize(after), "Saving and loading lost selected parameters or view settings.");
            var beforeClose = main.CaptureState("view");
            atlas = CreateAtlas();
            Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
            var cancelled = main.CaptureState("view"); cancelled.Timestamp = beforeClose.Timestamp;
            Check(JsonSerializer.Serialize(cancelled) == JsonSerializer.Serialize(beforeClose), "Closing without applying changed the source view.");
        }
        finally { atlas?.Close(); main.Close(); }
        Console.WriteLine($"PASS ({prefix.ToLowerInvariant()}-atlas): bounds, integer samples, precise fixed values, GPU previews, apply, close and saves.");

        Fractal3DParameterAtlasWindow CreateAtlas() => (Fractal3DParameterAtlasWindow)typeof(Fractal3DWindow)
            .GetMethod("Create" + prefix + "Atlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
