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
    private static async Task VerifyBulbBoxHybridAtlasAsync()
    {
        using var sandbox = DataSandbox.Create("bulbbox-hybrid-atlas");
        EnsureWindowThemeStyles();
        var source = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.BulbBoxHybrid);
        source.HybridMix = .12345678901234567;
        source.BoxFoldingLimit = 1.1234567890123457;
        source.HybridOrder = Hybrid3DOrder.BoxFirst;
        var chain = BulbBoxSequence.FromClassic(source);
        chain.Stages.Insert(1, new() { Operation = BulbBoxOperation.Rotate, X = 12, Y = 7 });
        chain.Stages.Add(new() { Operation = BulbBoxOperation.Translate, X = .15, Y = .1, Z = .05 });
        chain.Stages[0].Inversion = BoxInversionShape.RoundedCube;
        chain.Stages[0].Stretch = 1.3; chain.Stages[0].InversionPower = 6;
        foreach (int stageIndex in new[] { -1, 0, 1, 2, 3 })
        {
            var main = new Fractal3DWindow(Fractal3DKind.BulbBoxHybrid);
            BulbBoxHybridAtlasWindow? atlas = null;
            try
            {
                var view = source.Clone();
                if (stageIndex >= 0) view.HybridSequence = chain.Clone();
                main.LoadState(view);
                new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
                var editor = Element<BulbBoxHybridEditor>(main, "HybridEditor");
                if (stageIndex >= 0) Element<ListBox>(editor, "StagesList").SelectedIndex = stageIndex;
                var before = main.CaptureState("view");
                Check(before.HybridMix == source.HybridMix, "Loading rounded the classic mix.");
                atlas = CreateAtlas();
                Check(atlas.Owner == main && (stageIndex < 0 || atlas.StageIndex == stageIndex), "Wrong atlas source or selected stage.");
                var unchanged = before.Clone(); atlas.ApplySelectionTo(unchanged);
                Check(JsonSerializer.Serialize(unchanged) == JsonSerializer.Serialize(before), "Untouched atlas changed the source or rounded a fixed parameter.");
                var x = Element<ComboBox>(atlas, "XAxisBox"); var y = Element<ComboBox>(atlas, "YAxisBox");
                var boxes = Element<WrapPanel>(atlas, "FixedPointPanel").Children.OfType<StackPanel>()
                    .Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
                int count = stageIndex < 0 ? 7 : stageIndex == 0 ? 8 : stageIndex == 2 ? 6 : 4;
                Check(boxes.Length == count && boxes.All(NumericInput.IsRegistered), "Wrong parameters for this operation or unregistered numeric inputs.");
                for (int a = 0; a < count; a++) for (int b = a + 1; b < count; b++)
                {
                    x.SelectedIndex = a; y.SelectedIndex = b;
                    Check(x.SelectedIndex != y.SelectedIndex && boxes.Count(bx => bx.IsEnabled) == count - 2, "Axis selection did not retain the fixed parameters.");
                    NumericInput.CommitAll(atlas);
                }
                int repeatIndex = stageIndex < 0 ? 5 : count - 1;
                x.SelectedIndex = repeatIndex; y.SelectedIndex = 0;
                var minX = Element<TextBox>(atlas, "MinRealBox"); var maxX = Element<TextBox>(atlas, "MaxRealBox");
                minX.Text = "1"; maxX.Text = "9"; NumericInput.CommitAll(atlas);
                Check(maxX.Text == "6" && NumericInput.GetHasNotice(maxX), "Repeat counts must use integer bounds 1–6.");
                minX.Text = "1"; maxX.Text = "2";
                await Build(atlas);
                var tiles = Element<UniformGrid>(atlas, "TilesPanel");
                Check(tiles.Children.Count == 6 && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "Integer repeats were duplicated or a preview failed.");
                Click((Button)tiles.Children[0]);
                var expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Check(JsonSerializer.Serialize(view.HybridSequence) == JsonSerializer.Serialize(stageIndex < 0 ? null : chain), "Rendering mutated the source sequence.");
                if (stageIndex >= 0)
                {
                    for (int i = 0; i < chain.Stages.Count; i++)
                        if (i != stageIndex) Check(expected.HybridSequence!.Stages[i] == chain.Stages[i], "Atlas changed an unselected stage.");
                }
                var preview = expected.Clone();
                preview.Iterations = Math.Clamp(preview.Iterations, 1, 32); preview.MaxSteps = Math.Min(preview.MaxSteps, 160);
                preview.Detail = Fractal3DParameterAtlasWindow.PreviewDetail(preview.Detail);
                preview.Ssaa = 1; preview.SoftShadows = preview.AmbientOcclusion = false;
                using (var renderer = new Fractal3DRenderer())
                {
                    var reference = await renderer.RenderPixelsAsync(preview, 144, 108, null, null, CancellationToken.None);
                    var actual = new byte[144 * 108 * 4];
                    ((BitmapSource)Element<Image>(atlas, "SelectedPreview").Source).CopyPixels(actual, 144 * 4, 0);
                    Check(actual.SequenceEqual(reference.Buffer), "Hybrid atlas differs from direct rendering of its selected parameters.");
                }
                // Смена фиксированного параметра снимает старый выбор; неверный ввод восстанавливается.
                int fixedIndex = Enumerable.Range(0, boxes.Length).First(i => boxes[i].IsEnabled);
                boxes[fixedIndex].Text = "NaN"; NumericInput.CommitAll(atlas);
                Check(NumericInput.IsValid(boxes[fixedIndex]) && NumericInput.GetHasNotice(boxes[fixedIndex]) &&
                    !Element<Button>(atlas, "ApplyButton").IsEnabled, "Invalid fixed input retained a stale selection.");
                await Build(atlas);
                Click((Button)tiles.Children[0]); expected = before.Clone(); atlas.ApplySelectionTo(expected);
                Click(Element<Button>(atlas, "ApplyButton")); atlas = null;
                var after = main.CaptureState("view"); after.Timestamp = before.Timestamp;
                Check(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(expected), "Applying changed unrelated settings or rounded selected parameters.");
                if (stageIndex >= 0)
                {
                    Check(editor.SelectedStageIndex == stageIndex, "Applying the atlas reset stage selection.");
                    Click(Element<Button>(editor, "UndoButton"));
                    Check(JsonSerializer.Serialize(editor.Capture()) == JsonSerializer.Serialize(before.HybridSequence), "The real atlas application was not undone in one step.");
                    editor.ApplyAtlasStage(stageIndex, after.HybridSequence!.Stages[stageIndex]);
                }
                var store = new Fractal3DSaveStore(view.Kind); after.SaveName = "stage-" + stageIndex; store.Save(after);
                var saved = store.Load().Single(s => s.SaveName == after.SaveName);
                Check(saved.Power == after.Power && saved.HybridMix == after.HybridMix && saved.BoxFoldingLimit == after.BoxFoldingLimit &&
                    JsonSerializer.Serialize(saved.HybridSequence) == JsonSerializer.Serialize(after.HybridSequence), "Save roundtrip lost hybrid parameters.");
                main.LoadState(saved);
                var loaded = main.CaptureState("view");
                Check(loaded.HybridMix == after.HybridMix && JsonSerializer.Serialize(loaded.HybridSequence) == JsonSerializer.Serialize(after.HybridSequence), "Loading lost hybrid parameters.");
                if (stageIndex >= 0)
                {
                    // Заново применить к тому же этапу и проверить реальный возврат редактора одним шагом.
                    Element<ListBox>(editor, "StagesList").SelectedIndex = stageIndex;
                    var current = editor.Capture()!;
                    var changed = current.Stages[stageIndex] with { Repeats = current.Stages[stageIndex].Repeats == 6 ? 5 : 6 };
                    editor.ApplyAtlasStage(stageIndex, changed);
                    Check(editor.SelectedStageIndex == stageIndex, "Atlas application reset the selected stage.");
                    Click(Element<Button>(editor, "UndoButton"));
                    Check(JsonSerializer.Serialize(editor.Capture()) == JsonSerializer.Serialize(current), "Undo did not restore the complete previous chain.");
                }
                var beforeClose = main.CaptureState("view");
                atlas = CreateAtlas(); Task closing = Build(atlas); atlas.Close(); await closing; atlas = null;
                var closed = main.CaptureState("view"); closed.Timestamp = beforeClose.Timestamp;
                Check(JsonSerializer.Serialize(closed) == JsonSerializer.Serialize(beforeClose), "Closing without application changed the source view.");
            }
            finally { atlas?.Close(); main.Close(); }

            BulbBoxHybridAtlasWindow CreateAtlas() => (BulbBoxHybridAtlasWindow)typeof(Fractal3DWindow)
                .GetMethod("CreateBulbBoxHybridAtlas", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        }
        Console.WriteLine("PASS (bulbbox-hybrid-atlas): classic and all four stage operations, axis pairs, integer repeats, precision, direct GPU previews, apply, undo, saves and close.");
        static Task Build(ParameterAtlasWindow window) => (Task)typeof(ParameterAtlasWindow)
            .GetMethod("BuildAtlasAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        static T Element<T>(FrameworkElement element, string name) where T : class => (T)element.FindName(name);
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
