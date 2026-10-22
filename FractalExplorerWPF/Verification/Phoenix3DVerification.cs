using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyPhoenix3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("phoenix3d");
        EnsureWindowThemeStyles();
        string output = ReadOption(args, "--out") ?? Path.Combine(AppPaths.DataRoot, "Phoenix3DVerification");
        Directory.CreateDirectory(output);
        var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Phoenix);
        var parameters = Phoenix3DParameters.FromState(state);
        var random = new Random(731);
        for (int target = 0; target < 3; target++)
        for (int trial = 0; trial < 40; trial++)
        {
            var varied = parameters.Vary(random, .04, target).Components;
            var original = parameters.Components;
            for (int i = 0; i < 6; i++)
            {
                bool active = target == 0 || (target == 1 && i < 3) || (target == 2 && i >= 3);
                Check(active ? Math.Abs(varied[i] - original[i]) <= .04000000001 : varied[i] == original[i],
                    "Variation changed a fixed component or exceeded the requested radius.");
            }
        }
        var boundary = new Phoenix3DParameters(8, -8, 8, -8, 8, -8);
        Check(boundary.Vary(random, .5, 0).Components.All(x => x >= -8 && x <= 8), "Variation escaped component limits.");
        var precise = new Phoenix3DParameters(.12345678901234567, -.23456789012345678, .34567890123456789, -.45678901234567891, .56789012345678912, -.67890123456789123);
        Check(precise.WithAtlasPoint(precise.AtlasPoint) == precise, "Opening the atlas must not round untouched double components.");
        foreach (var point in new[] { new decimal[5], new decimal[] { 0, 0, 0, 9, 0, 0 } })
        {
            try { parameters.WithAtlasPoint(point); throw new Exception("Invalid atlas point accepted."); }
            catch (ArgumentException) { }
        }
        using var renderer = new Fractal3DRenderer();
        var presets = Fractal3DCatalog.GetPresets(Fractal3DKind.Phoenix);
        Check(presets.Count == 7 && presets[0].JuliaCX == state.JuliaCX && presets[0].PhoenixMemoryX == state.PhoenixMemoryX,
            "Presets must extend the existing default.");
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            for (int index = 0; index < presets.Count; index++)
            {
                var preset = presets[index];
                var image = await renderer.RenderAsync(preset, 240, 180, null, CancellationToken.None);
                WriteLSystemPng(Path.Combine(output, $"preset-{index}.png"), image);
                dc.DrawImage(image, new Rect(index % 3 * 240, index / 3 * 180, 240, 180));
                int hits = 0;
                for (int y = 60; y <= 120; y += 30)
                for (int x = 80; x <= 160; x += 40)
                    if (double.IsFinite(await renderer.ProbeDistanceAsync(preset, x, y, 240, 180, CancellationToken.None))) hits++;
                Check(hits > 0, $"Empty Phoenix 3D preset: {preset.SaveName}");
            }
        }
        var sheet = new RenderTargetBitmap(720, 540, 96, 96, PixelFormats.Pbgra32); sheet.Render(drawing);
        WriteLSystemPng(Path.Combine(output, "presets.png"), sheet);

        var main = new Fractal3DWindow(Fractal3DKind.Phoenix);
        Phoenix3DAtlasWindow? atlas = null;
        try
        {
            precise.ApplyTo(state); main.LoadState(state);
            var editor = (Phoenix3DExplorer)main.FindName("PhoenixExplorer");
            var radius = (TextBox)editor.FindName("RadiusBox");
            var undo = (Button)editor.FindName("UndoButton");
            Check(NumericInput.IsRegistered(radius), "Variation radius must use NumericInput.");
            Check(Phoenix3DParameters.FromState(main.CaptureState("exact")) == precise, "UI rounded loaded constants.");
            radius.Text = "NaN";
            var (restored, _) = editor.CaptureVariation();
            Check(restored == .04 && NumericInput.GetHasNotice(radius), "Invalid radius must restore the last valid value with feedback.");
            radius.Text = "4"; editor.CaptureVariation();
            Check(radius.Text == "0.5" && NumericInput.GetHasNotice(radius), "Variation radius must clamp.");
            radius.Text = ".02"; ((ComboBox)editor.FindName("TargetBox")).SelectedIndex = 2;
            editor.Measure(new Size(300, double.PositiveInfinity));
            editor.Arrange(new Rect(new Size(300, editor.DesiredSize.Height))); editor.UpdateLayout();
            var editorImage = new RenderTargetBitmap(300, (int)Math.Ceiling(editor.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            editorImage.Render(editor); WriteLSystemPng(Path.Combine(output, "editor.png"), editorImage);
            var before = main.CaptureState("before");
            Click((Button)editor.FindName("VariationButton"));
            var after = main.CaptureState("after");
            Check(after.JuliaCX == before.JuliaCX && after.JuliaCY == before.JuliaCY && after.JuliaCZ == before.JuliaCZ &&
                after.PhoenixMemoryX != before.PhoenixMemoryX && undo.IsEnabled, "C2-only variation or undo availability failed.");
            // Камера и оформление, изменённые после вариации, должны пережить возврат констант.
            ((TextBox)main.FindName("LightYawBox")).Text = "67";
            Click(undo);
            Check(Phoenix3DParameters.FromState(main.CaptureState("undo")) == precise && main.CaptureState("undo").LightYaw == 67 && !undo.IsEnabled,
                "Undo must restore exact constants and retain current lighting.");
            main.LoadState(Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Phoenix));
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            atlas = CreateAtlas();
            var xAxis = (ComboBox)atlas.FindName("XAxisBox"); var yAxis = (ComboBox)atlas.FindName("YAxisBox");
            var boxes = ((WrapPanel)atlas.FindName("FixedPointPanel")).Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
            Check(xAxis.SelectedIndex == 0 && yAxis.SelectedIndex == 3 && boxes.Length == 6 && boxes.All(NumericInput.IsRegistered),
                "Atlas must start with real C1/C2 axes and six validated fields.");
            for (int x = 0; x < 6; x++) for (int y = x + 1; y < 6; y++)
            {
                xAxis.SelectedIndex = x; yAxis.SelectedIndex = y;
                Check(xAxis.SelectedIndex != yAxis.SelectedIndex && boxes.Count(b => b.IsEnabled) == 4,
                    "Every pair must retain four fixed components.");
            }
            xAxis.SelectedIndex = 0; yAxis.SelectedIndex = 3;
            boxes[1].Text = ".12"; boxes[2].Text = ".08"; boxes[4].Text = ".06"; boxes[5].Text = ".1";
            Task old = Build(atlas);
            ((TextBox)atlas.FindName("MinRealBox")).Text = ".3";
            ((TextBox)atlas.FindName("MaxRealBox")).Text = ".5";
            Task current = Build(atlas); await Task.WhenAll(old, current);
            var tiles = (UniformGrid)atlas.FindName("TilesPanel");
            Check(tiles.Children.Count == 9 && tiles.Children.OfType<Button>().All(b => b.IsEnabled) && !atlas.IsRendering,
                "Latest atlas generation must finish with nine GPU previews.");
            Check(atlas.SelectedConstants[1] == .12m && atlas.SelectedConstants[5] == .1m, "Fixed atlas components were lost.");
            var atlasRoot = (FrameworkElement)atlas.Content;
            atlasRoot.Measure(new Size(1060, 900)); atlasRoot.Arrange(new Rect(0, 0, 1060, 900)); atlasRoot.UpdateLayout();
            var atlasImage = new RenderTargetBitmap(1060, 900, 96, 96, PixelFormats.Pbgra32); atlasImage.Render(atlasRoot);
            WriteLSystemPng(Path.Combine(output, "atlas.png"), atlasImage);
            before = main.CaptureState("apply");
            var expected = Phoenix3DParameters.FromState(before).WithAtlasPoint(atlas.SelectedConstants);
            Click((Button)atlas.FindName("ApplyButton")); atlas = null;
            after = main.CaptureState("apply");
            Check(Phoenix3DParameters.FromState(after) == expected && after.CameraYaw == before.CameraYaw &&
                after.CameraPitch == before.CameraPitch && after.CameraDistance == before.CameraDistance &&
                after.Iterations == before.Iterations && after.Power == before.Power && after.PhoenixSecondaryPower == before.PhoenixSecondaryPower &&
                after.Ssaa == before.Ssaa && after.LightYaw == before.LightYaw && after.ColoringMode == before.ColoringMode,
                "Atlas application must change only the six constants.");
            Click(undo);
            Check(Phoenix3DParameters.FromState(main.CaptureState("undo atlas")) == Phoenix3DParameters.FromState(before), "Atlas selection must be undoable.");
            for (int i = 0; i < 35; i++) Click((Button)editor.FindName("VariationButton"));
            int undoCount = 0;
            while (undo.IsEnabled) { Click(undo); undoCount++; }
            Check(undoCount == 32, "Undo must retain exactly the last 32 changes.");
            Click((Button)editor.FindName("VariationButton")); main.LoadState(state);
            Check(!undo.IsEnabled, "Loading another scene must clear the old history.");
            var save = main.CaptureState("Phoenix 3D roundtrip");
            var store = new Fractal3DSaveStore(Fractal3DKind.Phoenix); store.Save(save);
            var loaded = store.Load().Single();
            Check(Phoenix3DParameters.FromState(loaded) == precise && loaded.Power == save.Power, "Save roundtrip lost constants or powers.");
            var cloud = CloudSaveRepository.ListLocal(out int unreadable).Single();
            var cloudState = JsonNode.Parse(cloud.JsonData)!["state"]!.Deserialize<Fractal3DState>(JsonOptionsFactory.Create())!;
            Check(unreadable == 0 && Phoenix3DParameters.FromState(cloudState) == precise, "Cloud JSON lost exact constants.");
            atlas = CreateAtlas();
            Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
            Check(Phoenix3DParameters.FromState(main.CaptureState("cancel")) == precise, "Closing an atlas must not apply its selection.");
            var edge = state.Clone(); boundary.ApplyTo(edge);
            atlas = new Phoenix3DAtlasWindow(edge);
            foreach (string name in new[] { "MinRealBox", "MaxRealBox", "MinImaginaryBox", "MaxImaginaryBox" })
            {
                var box = (TextBox)atlas.FindName(name);
                Check(NumericInput.IsValid(box), "An atlas opened at ±8 must have valid initial bounds.");
            }
            atlas.Close(); atlas = null;
        }
        finally { atlas?.Close(); main.Close(); }
        Console.WriteLine($"PASS (phoenix3d): seven GPU presets, six-component atlas, generations, apply/cancel/close, bounded variations, exact undo, numeric input, saves/cloud. PNG: {output}");

        Phoenix3DAtlasWindow CreateAtlas() => (Phoenix3DAtlasWindow)typeof(Fractal3DWindow)
            .GetMethod("CreatePhoenixAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
