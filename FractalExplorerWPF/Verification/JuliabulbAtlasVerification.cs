using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyJuliabulbAtlasAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("juliabulb-atlas");
        EnsureWindowThemeStyles();
        string output = ReadOption(args, "--out") ?? Path.Combine(AppPaths.DataRoot, "JuliabulbAtlasVerification");
        Directory.CreateDirectory(output);
        var source = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Juliabulb);
        source.JuliaCZ = .12345678901234567;
        source.Power = 8.123456789012345;
        var main = new Fractal3DWindow(Fractal3DKind.Juliabulb);
        JuliabulbAtlasWindow? atlas = null;
        try
        {
            main.LoadState(source);
            var before = main.CaptureState("before");
            Check(before.JuliaCZ == source.JuliaCZ && before.Power == source.Power, "Loading rounded the atlas parameters.");
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            Check(Field<Button>(main, "JuliabulbAtlasButton").Visibility == Visibility.Visible, "Juliabulb atlas entry is hidden.");
            atlas = CreateAtlas();
            Check(atlas.Owner == main, "Atlas owner must be the source window.");
            var unchanged = before.Clone(); atlas.ApplySelectionTo(unchanged);
            Check(unchanged.JuliaCZ == before.JuliaCZ && unchanged.Power == before.Power, "Atlas rounded untouched double values.");
            var xAxis = Field<ComboBox>(atlas, "XAxisBox"); var yAxis = Field<ComboBox>(atlas, "YAxisBox");
            var boxes = Field<WrapPanel>(atlas, "FixedPointPanel").Children.OfType<StackPanel>()
                .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
            Check(boxes.Length == 4 && boxes.All(NumericInput.IsRegistered) && xAxis.SelectedIndex == 0 && yAxis.SelectedIndex == 1,
                "Atlas needs validated Cx/Cy/Cz/power fields and initial Cx/Cy axes.");
            for (int x = 0; x < 4; x++) for (int y = x + 1; y < 4; y++)
            {
                xAxis.SelectedIndex = x; yAxis.SelectedIndex = y;
                Check(xAxis.SelectedIndex != yAxis.SelectedIndex && boxes.Count(b => b.IsEnabled) == 2,
                    "Every axis pair must retain two fixed values.");
                NumericInput.CommitAll(atlas);
            }
            xAxis.SelectedIndex = 3; yAxis.SelectedIndex = 1;
            var minX = Field<TextBox>(atlas, "MinRealBox"); var maxX = Field<TextBox>(atlas, "MaxRealBox");
            minX.Text = "10"; maxX.Text = "12"; NumericInput.CommitAll(atlas);
            Check(minX.Text == "10" && maxX.Text == "12", "Power axis incorrectly kept the C limit of 8.");
            minX.Text = "40"; maxX.Text = "50"; NumericInput.CommitAll(atlas);
            Check(decimal.Parse(minX.Text, CultureInfo.InvariantCulture) < decimal.Parse(maxX.Text, CultureInfo.InvariantCulture)
                && decimal.Parse(maxX.Text, CultureInfo.InvariantCulture) <= 32 && NumericInput.GetHasNotice(maxX),
                "Power limits and ordered bounds must recover together.");
            xAxis.SelectedIndex = 0;
            maxX.Text = "12"; NumericInput.CommitAll(atlas);
            Check(maxX.Text == "8" && NumericInput.GetHasNotice(maxX), "Switching back to C retained power bounds.");
            boxes[2].Text = "NaN"; NumericInput.CommitAll(atlas);
            Check(NumericInput.GetHasNotice(boxes[2]) && NumericInput.IsValid(boxes[2]), "Invalid fixed C must restore with feedback.");
            boxes[3].Text = "40"; NumericInput.CommitAll(atlas);
            Check(boxes[3].Text == "32", "Fixed power must use its own limit.");
            boxes[3].Text = "8";
            boxes[2].Text = source.JuliaCZ.ToString("R", CultureInfo.InvariantCulture);
            Task stale = Build(atlas);
            minX.Text = "-.4"; maxX.Text = "0";
            Task latest = Build(atlas); await Task.WhenAll(stale, latest);
            var tiles = Field<UniformGrid>(atlas, "TilesPanel");
            Check(tiles.Children.Count == 9 && tiles.Children.OfType<Button>().All(b => b.IsEnabled) && !atlas.IsRendering,
                "Latest generation must contain nine completed GPU previews.");
            Check(atlas.SelectedParameters[0] == -.2m && atlas.SelectedParameters[3] == 8m, "Stale generation replaced the current selection.");
            var expected = before.Clone(); atlas.ApplySelectionTo(expected);
            Check(expected.JuliaCZ == source.JuliaCZ, "Fixed component lost precision after rendering.");
            expected.Iterations = Math.Clamp(expected.Iterations, 1, 32); expected.MaxSteps = Math.Min(expected.MaxSteps, 160);
            expected.Ssaa = 1; expected.SoftShadows = expected.AmbientOcclusion = false;
            using (var renderer = new Fractal3DRenderer())
            {
                var reference = await renderer.RenderPixelsAsync(expected, 144, 108, null, null, CancellationToken.None);
                var actual = new byte[144 * 108 * 4]; ((BitmapSource)Field<Image>(atlas, "SelectedPreview").Source).CopyPixels(actual, 144 * 4, 0);
                Check(actual.SequenceEqual(reference.Buffer), "Atlas must render the dynamic Juliabulb with the source camera and lighting.");
                var other = expected.Clone(); other.Power = 3;
                var changed = await renderer.RenderPixelsAsync(other, 144, 108, null, null, CancellationToken.None);
                Check(!reference.Buffer.SequenceEqual(changed.Buffer), "Changing power did not affect the preview.");
            }
            // Степень действительно участвует в сетке, а не только в фиксированных полях.
            xAxis.SelectedIndex = 3;
            minX.Text = "3"; maxX.Text = "5";
            await Build(atlas);
            Check(atlas.SelectedParameters[3] == 4m, "Power axis was not sampled.");
            // Проверить уточнение, историю и сброс выбора после изменения фиксированной C.
            Click(Field<Button>(atlas, "NarrowButton"));
            while (atlas.IsRendering) await Task.Delay(10);
            Check(Field<Button>(atlas, "BackButton").IsEnabled, "Refinement lost range history.");
            Click(Field<Button>(atlas, "BackButton"));
            while (atlas.IsRendering) await Task.Delay(10);
            boxes[0].Text = "-.3";
            Check(!Field<Button>(atlas, "ApplyButton").IsEnabled, "A fixed parameter edit left a stale selection applicable.");
            await Build(atlas);
            var root = (FrameworkElement)atlas.Content;
            root.Measure(new Size(1060, 820)); root.Arrange(new Rect(0, 0, 1060, 820)); root.UpdateLayout();
            var image = new RenderTargetBitmap(1060, 820, 96, 96, PixelFormats.Pbgra32); image.Render(root);
            WriteLSystemPng(Path.Combine(output, "atlas.png"), image);
            before = main.CaptureState("apply"); expected = before.Clone(); atlas.ApplySelectionTo(expected);
            Click(Field<Button>(atlas, "ApplyButton")); atlas = null;
            var after = main.CaptureState("apply");
            Check(after.JuliaCX == expected.JuliaCX && after.JuliaCY == expected.JuliaCY && after.JuliaCZ == expected.JuliaCZ && after.Power == expected.Power,
                "Applying the atlas lost C or power.");
            // Сравнить все остальные поля DTO, включая камеру, качество, свет и оформление.
            after.JuliaCX = before.JuliaCX; after.JuliaCY = before.JuliaCY; after.JuliaCZ = before.JuliaCZ; after.Power = before.Power;
            after.Timestamp = before.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(before), "Atlas application changed unrelated view parameters.");
            var save = main.CaptureState("Juliabulb atlas");
            var store = new Fractal3DSaveStore(Fractal3DKind.Juliabulb); store.Save(save);
            var loaded = store.Load().Single();
            Check(loaded.JuliaCZ == expected.JuliaCZ && loaded.Power == expected.Power, "Save roundtrip lost atlas parameters.");
            main.LoadState(loaded);
            Check(main.CaptureState("loaded").JuliaCZ == expected.JuliaCZ && main.CaptureState("loaded").Power == expected.Power,
                "Loading the saved view rounded the selected parameters.");
            before = main.CaptureState("cancel");
            atlas = CreateAtlas();
            Task cancelled = Build(atlas); Click(Field<Button>(atlas, "CancelAtlasButton")); await cancelled;
            Check(!atlas.IsRendering, "Cancellation left the atlas busy.");
            Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
            Check(main.CaptureState("cancel").Power == before.Power && main.CaptureState("cancel").JuliaCX == before.JuliaCX,
                "Closing without applying changed the source.");
            var edge = source.Clone(); edge.JuliaCX = 8; edge.JuliaCY = -8; edge.Power = 32;
            atlas = new JuliabulbAtlasWindow(edge);
            Field<ComboBox>(atlas, "XAxisBox").SelectedIndex = 3;
            Check(new[] { "MinRealBox", "MaxRealBox", "MinImaginaryBox", "MaxImaginaryBox" }
                .All(name => NumericInput.IsValid(Field<TextBox>(atlas, name))), "Edge parameters produced invalid initial bounds.");
        }
        finally { atlas?.Close(); main.Close(); }
        Console.WriteLine($"PASS (juliabulb-atlas): C/power axis pairs, numeric limits, precise fixed values, GPU previews, generations, history, apply/cancel/close and saves. PNG: {output}");

        JuliabulbAtlasWindow CreateAtlas() => (JuliabulbAtlasWindow)typeof(Fractal3DWindow)
            .GetMethod("CreateJuliabulbAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Field<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
