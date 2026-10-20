using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Theming;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyPhoenixParameterAtlasAsync()
    {
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/FractalExplorerWPF;component/Theming/ThemeStyles.xaml", UriKind.Relative) });
        ThemeManager.Initialize(Application.Current);
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        object? Invoke(object target, string method, params object[] args) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
        T Field<T>(FrameworkElement window, string name) where T : class => (T)window.FindName(name);
        var source = new PhoenixState { Iterations = 30, C1Real = .56667m, C1Imaginary = .12m, C2Real = -.5m, C2Imaginary = -.03m,
            PlaneMode = PhoenixPlaneMode.ParameterC1, Zoom = FloatExp.Pow10(1000),
            CenterXExact = ".123456789012345678901234567890123456789", InitialPreviousReal = .07m, SecondaryPower = 1 };
        var point = PhoenixAtlasParameters.From(source);
        foreach (var variant in Enum.GetValues<PhoenixVariant>())
        {
            source.Variant = variant;
            var expected = source.Clone();
            expected.PlaneMode = PhoenixPlaneMode.Julia;
            expected.CenterX = expected.CenterY = 0; expected.CenterXExact = expected.CenterYExact = null; expected.Zoom = 1;
            byte[] actual = PhoenixParameterAtlasRenderer.Render(source, point, 40, 30, CancellationToken.None);
            var reference = new byte[40 * 30 * 4];
            PhoenixRenderer.Render(expected, reference, 40, 30, 40 * 4, 1, CancellationToken.None);
            Check(actual.SequenceEqual(reference), $"{variant}: atlas lost the formula or used a parameter map instead of a dynamic plane.");
        }
        Check(source.Zoom == FloatExp.Pow10(1000) && source.PlaneMode == PhoenixPlaneMode.ParameterC1 && source.CenterXExact is not null,
            "Atlas rendering changed the source viewport or plane.");
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            try { PhoenixParameterAtlasRenderer.Render(source, point, 40, 30, cts.Token); throw new Exception("Cancellation ignored."); }
            catch (OperationCanceledException) { }
        }
        Check(ParameterAtlasRange.Around(decimal.MinValue, decimal.MaxValue, .4m, .4m).IsValid, "Extreme source values overflowed range initialization.");

        var main = new PhoenixWindow();
        PhoenixParameterAtlasWindow? atlas = null;
        try
        {
            var input = main.CaptureState("input");
            input.CenterXExact = source.CenterXExact;
            input.Zoom = FloatExp.Pow10(1000); input.CenterX = .123m; input.CenterY = -.2m;
            input.Iterations = 30; input.PrimaryPower = 3; input.SecondaryPower = 1;
            input.InitialPreviousReal = .07m; input.PlaneMode = PhoenixPlaneMode.ParameterC1;
            main.LoadState(input);
            var before = main.CaptureState("before");
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            atlas = (PhoenixParameterAtlasWindow)Invoke(main, "CreateParameterAtlas")!;
            Check(atlas.Owner == main, "Atlas does not belong to the source Phoenix window.");
            Check(Field<Button>(main, "ParameterAtlasButton").Content is System.Windows.Shapes.Path, "Atlas icon missing.");
            Invoke(main, "ToggleControlsButton_OnClick", main, new RoutedEventArgs());
            Check(Field<Button>(main, "ParameterAtlasButton").Visibility == Visibility.Visible, "Hiding the menu hid the atlas.");
            Field<ComboBox>(atlas, "GridSizeBox").SelectedIndex = 0;
            var xAxis = Field<ComboBox>(atlas, "XAxisBox");
            var yAxis = Field<ComboBox>(atlas, "YAxisBox");
            var fixedPanel = Field<WrapPanel>(atlas, "FixedPointPanel");
            var boxes = fixedPanel.Children.OfType<StackPanel>().Select(p => p.Children.OfType<TextBox>().Single()).ToArray();
            Check(boxes.Length == 4 && boxes.All(NumericInput.IsRegistered), "Phoenix fixed components bypass NumericInput.");

            // All six distinct pairs; reversing their order is supported by the same selectors.
            for (int x = 0; x < 4; x++)
            for (int y = x + 1; y < 4; y++)
            {
                xAxis.SelectedIndex = x; yAxis.SelectedIndex = y;
                Check(xAxis.SelectedIndex != yAxis.SelectedIndex, "Duplicate axes were accepted.");
                Check(boxes.Count(b => b.IsEnabled) == 2 && !boxes[x].IsEnabled && !boxes[y].IsEnabled,
                    "Axis fields were mistaken for fixed components.");
            }
            xAxis.SelectedIndex = 0; yAxis.SelectedIndex = 2;
            boxes[1].Text = "0,1234567890123456789012345678";
            boxes[3].Text = "-3e-2";
            Task oldBuild = (Task)Invoke(atlas, "BuildAtlasAsync")!;
            Field<TextBox>(atlas, "MinRealBox").Text = "0.2";
            Field<TextBox>(atlas, "MaxRealBox").Text = "0.8";
            Task newBuild = (Task)Invoke(atlas, "BuildAtlasAsync")!;
            await Task.WhenAll(oldBuild, newBuild);
            Check(!atlas.IsRendering, "Atlas is stuck busy.");
            var tiles = Field<UniformGrid>(atlas, "TilesPanel");
            Check(tiles.Children.Count == 9 && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "Latest Phoenix generation is incomplete.");
            Check(atlas.SelectedParameters.C1Real == .5m && atlas.SelectedParameters.C1Imaginary == .1234567890123456789012345678m
                && atlas.SelectedParameters.C2Imaginary == -.03m, "Fixed parameters or latest axis range were lost.");
            ((Button)tiles.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PhoenixAtlasParameters selected = atlas.SelectedParameters;
            int windowsBefore = Application.Current.Windows.Count;
            Field<Button>(atlas, "ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var applied = main.CaptureState("applied");
            Check(PhoenixAtlasParameters.From(applied) == selected && applied.PlaneMode == PhoenixPlaneMode.Julia,
                "Applying did not transfer all four components to the dynamic plane.");
            Check(Application.Current.Windows.Count == windowsBefore && applied.Zoom == before.Zoom
                && applied.CenterXExact == before.CenterXExact && applied.CenterY == before.CenterY
                && applied.PrimaryPower == before.PrimaryPower && applied.SecondaryPower == before.SecondaryPower
                && applied.InitialPreviousReal == before.InitialPreviousReal && applied.Palette.Name == before.Palette.Name,
                "Applying opened a new window or changed the viewport/formula/palette.");

            var bitmap = (BitmapSource)Field<Image>(atlas, "SelectedPreview").Source;
            var bytes = new byte[144 * 108 * 4]; bitmap.CopyPixels(bytes, 144 * 4, 0);
            Check(bytes.SequenceEqual(PhoenixParameterAtlasRenderer.Render(before, selected, 144, 108, CancellationToken.None)),
                "A displayed tile does not match its full four-component selection.");
            boxes[1].Text = "0.2";
            Check(!Field<Button>(atlas, "ApplyButton").IsEnabled, "Changing a fixed component left a stale selection applicable.");
            await (Task)Invoke(atlas, "BuildAtlasAsync")!;
            Field<Button>(atlas, "NarrowButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (atlas.IsRendering) await Task.Delay(10);
            Check(Field<Button>(atlas, "BackButton").IsEnabled, "Refinement lost its range history.");
            Field<Button>(atlas, "BackButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (atlas.IsRendering) await Task.Delay(10);
            xAxis.SelectedIndex = yAxis.SelectedIndex;
            Check(xAxis.SelectedIndex != yAxis.SelectedIndex && !Field<Button>(atlas, "ApplyButton").IsEnabled,
                "Axis swap retained a stale selection.");
            Task cancelledBuild = (Task)Invoke(atlas, "BuildAtlasAsync")!;
            Field<Button>(atlas, "CancelAtlasButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await cancelledBuild;
            Task closingBuild = (Task)Invoke(atlas, "BuildAtlasAsync")!;
            main.Close();
            await closingBuild;
            Check(!atlas.IsRendering, "Closing the owner left the atlas rendering.");
        }
        finally { atlas?.Close(); main.Close(); }
        Console.WriteLine("PASS (phoenix-atlas): five formulas, six axis pairs, fixed components, exact apply, dynamic plane, 1e1000 viewport, stale generations, cancellation and owner close.");
    }
}
