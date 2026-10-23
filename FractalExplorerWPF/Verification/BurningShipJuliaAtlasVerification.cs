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
    private static async Task VerifyBurningShipJuliaAtlasAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("burning-ship-julia-atlas");
        EnsureWindowThemeStyles();
        string output = ReadOption(args, "--out") ?? Path.Combine(AppPaths.DataRoot, "BurningShipJuliaAtlasVerification");
        Directory.CreateDirectory(output);
        var source = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.BurningShipJulia);
        source.JuliaCZ = .12345678901234567;
        // Проверить каждую формулу через реальный снимок GPU-источника, без больших сеток.
        using (var renderer = new Fractal3DRenderer())
        foreach (var formula in Enum.GetValues<BurningShip3DFormula>())
        {
            source.BurningShipFormula = formula;
            var window = new BurningShipJuliaAtlasWindow(source);
            try
            {
                var render = (Func<decimal[], CancellationToken, Task<byte[]>>)typeof(Fractal3DParameterAtlasWindow)
                    .GetMethod("CreateAsyncRenderSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
                var point = new decimal[] { -.3m, .25m, (decimal)source.JuliaCZ, 3m };
                var actual = await render(point, CancellationToken.None);
                var expected = source.Clone(); expected.JuliaCX = -.3; expected.JuliaCY = .25; expected.Power = 3;
                expected.Iterations = Math.Clamp(expected.Iterations, 1, 32); expected.MaxSteps = Math.Min(expected.MaxSteps, 160);
                expected.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(expected.Detail);
                expected.Ssaa = 1; expected.SoftShadows = expected.AmbientOcclusion = false;
                var direct = await renderer.RenderPixelsAsync(expected, 144, 108, null, null, CancellationToken.None);
                Check(actual.SequenceEqual(direct.Buffer), $"{formula}: atlas lost the selected formula, C, power or view.");
                Check(((TextBlock)window.FindName("ApplicationHintText")).Text.Contains(Fractal3DCatalog.BurningShipFormulaName(formula)),
                    "Atlas must identify its fixed formula.");
            }
            finally { window.Close(); }
        }
        source.BurningShipFormula = BurningShip3DFormula.SphericalFullFold;
        source.Power = 4;
        var main = new Fractal3DWindow(Fractal3DKind.BurningShipJulia);
        BurningShipJuliaAtlasWindow? atlas = null;
        try
        {
            source.BurningShipThickness = .0375;
            main.LoadState(source);
            Check(main.CaptureState("exact").JuliaCZ == source.JuliaCZ, "Loading rounded the fixed C.");
            Check(main.CaptureState("exact").BurningShipThickness == .0375, "Shell thickness was lost by the window.");
            Check(JsonSerializer.Deserialize<Fractal3DState>("{}")!.BurningShipThickness == .02,
                "Old saves without a shell thickness must open with the default 0.02.");
            Check(Field<Button>(main, "BurningShipJuliaAtlasButton").Visibility == Visibility.Visible &&
                Field<Button>(main, "JuliabulbAtlasButton").Visibility == Visibility.Collapsed, "Wrong atlas entry is visible.");
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            atlas = CreateAtlas();
            Check(atlas.Owner == main, "Atlas owner must be the source window.");
            var xAxis = Field<ComboBox>(atlas, "XAxisBox"); var yAxis = Field<ComboBox>(atlas, "YAxisBox");
            var boxes = Field<WrapPanel>(atlas, "FixedPointPanel").Children.OfType<StackPanel>()
                .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
            Check(boxes.Length == 4 && boxes.All(NumericInput.IsRegistered), "All fixed values must use NumericInput.");
            for (int x = 0; x < 4; x++) for (int y = x + 1; y < 4; y++)
            {
                xAxis.SelectedIndex = x; yAxis.SelectedIndex = y;
                Check(xAxis.SelectedIndex != yAxis.SelectedIndex && boxes.Count(b => b.IsEnabled) == 2,
                    "Every axis pair must retain two fixed values.");
            }
            xAxis.SelectedIndex = 0; yAxis.SelectedIndex = 1;
            boxes[3].Text = "3.5"; NumericInput.CommitAll(atlas);
            Check(boxes[3].Text == "4" && NumericInput.GetHasNotice(boxes[3]), "Fractional fixed power must restore with feedback.");
            boxes[3].Text = "20"; NumericInput.CommitAll(atlas);
            Check(boxes[3].Text == "16", "Fixed power must clamp to 16.");
            boxes[3].Text = "3";
            xAxis.SelectedIndex = 3;
            var minX = Field<TextBox>(atlas, "MinRealBox"); var maxX = Field<TextBox>(atlas, "MaxRealBox");
            minX.Text = "2.5"; NumericInput.CommitAll(atlas);
            Check(minX.Text == "2" && NumericInput.GetHasNotice(minX), "Fractional power bound must restore with feedback.");
            // Проверить дискретизацию всех размеров, включая диапазон выше прежнего общего лимита 10.
            foreach (int size in new[] { 3, 5, 7 })
            {
                var values = (decimal[])typeof(ParameterAtlasWindow).GetMethod("SampleAxis", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(atlas, [3, 2m, 16m, size, false])!;
                Check(values.Length == size && values.Distinct().Count() == size && values.All(v => v == decimal.Truncate(v)) &&
                    values[0] == 2 && values[^1] == 16, "Integer axis sampling must be unique and span the requested range.");
            }
            minX.Text = "15"; maxX.Text = "16";
            await Build(atlas);
            var tiles = Field<UniformGrid>(atlas, "TilesPanel");
            Check(tiles.Columns == 2 && tiles.Children.Count == 6 && tiles.Children.OfType<Button>().All(b => b.IsEnabled),
                "A two-value power range must render two columns, without duplicates or a limit of 10.");
            Check(TilePower(0) == 15 && TilePower(1) == 16, "Column powers differ from their labels.");
            Click(Field<Button>(atlas, "NarrowButton"));
            while (atlas.IsRendering) await Task.Delay(10);
            Check(minX.Text == "15" && maxX.Text == "16" && tiles.Columns == 2 && Field<Button>(atlas, "BackButton").IsEnabled,
                "Refining a narrow integer axis must keep valid distinct powers and retain history.");
            Click(Field<Button>(atlas, "BackButton"));
            while (atlas.IsRendering) await Task.Delay(10);
            typeof(ParameterAtlasWindow).GetMethod("Overview_OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(atlas, [atlas, new RoutedEventArgs()]);
            while (atlas.IsRendering) await Task.Delay(10);
            Check(minX.Text == "2" && maxX.Text == "16" && TilePower(0) == 2 && TilePower(2) == 16,
                "Overview must render the entire degree range, including values above 10.");
            // На вертикальной оси большие степени идут сверху вниз.
            xAxis.SelectedIndex = 0; yAxis.SelectedIndex = 3;
            Field<TextBox>(atlas, "MinImaginaryBox").Text = "15";
            Field<TextBox>(atlas, "MaxImaginaryBox").Text = "16";
            await Build(atlas);
            Check(tiles.Columns == 3 && tiles.Children.Count == 6 && TilePower(0) == 16 && TilePower(3) == 15,
                "Integer Y axis must have two distinct descending rows.");
            yAxis.SelectedIndex = 1;
            maxX.Text = "12"; NumericInput.CommitAll(atlas);
            Check(maxX.Text == "8", "Switching from power to C retained power bounds.");
            boxes[3].Text = "3";
            Task stale = Build(atlas);
            minX.Text = "-.4"; maxX.Text = "-.2";
            Task latest = Build(atlas); await Task.WhenAll(stale, latest);
            Check(tiles.Children.Count == 9 && tiles.Children.OfType<Button>().All(b => b.IsEnabled) && atlas.SelectedParameters[0] == -.3m,
                "A stale generation replaced the current C range.");
            boxes[2].Text = ".15";
            Check(!Field<Button>(atlas, "ApplyButton").IsEnabled, "Changing fixed C left a stale selection applicable.");
            await Build(atlas);
            var before = main.CaptureState("apply"); var expected = before.Clone(); atlas.ApplySelectionTo(expected);
            var root = (FrameworkElement)atlas.Content;
            root.Measure(new Size(1060, 900)); root.Arrange(new Rect(0, 0, 1060, 900)); root.UpdateLayout();
            var image = new RenderTargetBitmap(1060, 900, 96, 96, PixelFormats.Pbgra32); image.Render(root);
            WriteLSystemPng(Path.Combine(output, "atlas.png"), image);
            Click(Field<Button>(atlas, "ApplyButton")); atlas = null;
            var after = main.CaptureState("apply");
            Check(after.JuliaCX == expected.JuliaCX && after.JuliaCY == expected.JuliaCY && after.JuliaCZ == expected.JuliaCZ &&
                after.Power == expected.Power && after.Power == Math.Truncate(after.Power), "Applying lost C or integral power.");
            after.JuliaCX = before.JuliaCX; after.JuliaCY = before.JuliaCY; after.JuliaCZ = before.JuliaCZ; after.Power = before.Power;
            after.Timestamp = before.Timestamp;
            Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(before), "Application changed formula, camera, lighting or other unrelated state.");
            var save = main.CaptureState("Ship atlas");
            var store = new Fractal3DSaveStore(Fractal3DKind.BurningShipJulia); store.Save(save);
            var loaded = store.Load().Single(); main.LoadState(loaded);
            Check(main.CaptureState("loaded").JuliaCZ == expected.JuliaCZ && loaded.BurningShipFormula == source.BurningShipFormula &&
                loaded.Power == expected.Power, "Save/load lost selected parameters or formula.");
            atlas = CreateAtlas();
            Task cancelled = Build(atlas); Click(Field<Button>(atlas, "CancelAtlasButton")); await cancelled;
            Check(!atlas.IsRendering, "Cancellation left the atlas busy.");
            Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
            Check(main.CaptureState("cancel").JuliaCX == expected.JuliaCX && main.CaptureState("cancel").Power == expected.Power,
                "Closing without applying changed the source.");
            foreach (double edgePower in new[] { 2d, 16d })
            {
                var edge = source.Clone(); edge.Power = edgePower;
                atlas = new BurningShipJuliaAtlasWindow(edge); Field<ComboBox>(atlas, "XAxisBox").SelectedIndex = 3;
                Check(NumericInput.IsValid(Field<TextBox>(atlas, "MinRealBox")) && NumericInput.IsValid(Field<TextBox>(atlas, "MaxRealBox")),
                    "Edge power produced invalid initial bounds.");
                atlas.Close(); atlas = null;
            }

            decimal TilePower(int index)
            {
                Click((Button)tiles.Children[index]); return atlas!.SelectedParameters[3];
            }
        }
        finally { atlas?.Close(); main.Close(); }
        Console.WriteLine($"PASS (burning-ship-julia-atlas): four formulas, C/power axes, unique integer columns/rows, numeric limits, precise constants, GPU snapshots, apply/cancel/close and saves. PNG: {output}");

        BurningShipJuliaAtlasWindow CreateAtlas() => (BurningShipJuliaAtlasWindow)typeof(Fractal3DWindow)
            .GetMethod("CreateBurningShipJuliaAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Field<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
