using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Theming;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyJuliaParameterAtlasAsync()
    {
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/FractalExplorerWPF;component/Theming/ThemeStyles.xaml", UriKind.Relative) });
        ThemeManager.Initialize(Application.Current);
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        object? Invoke(object target, string method, params object[] args) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
        T Field<T>(FrameworkElement window, string name) where T : class => (T)window.FindName(name);
        var range = new ParameterAtlasRange(-1, 1, -2, 2);
        Check(range.Sample(0, 0, 5) == (-1m, 2m) && range.Sample(4, 4, 5) == (1m, -2m), "Atlas axes are reversed.");
        Check(ParameterAtlasRange.Around(10, -10, .4m, .4m).IsValid, "Edge refinement escaped the supported range.");

        var source = new MandelbrotState { Variant = MandelbrotVariant.Julia, JuliaCReal = -.8m, JuliaCImaginary = .156m,
            Iterations = 50, Zoom = 1e50, CenterXExact = "0.123456789012345678901234567890123456789", CenterX = .123m,
            Palette = new MandelbrotPalette { Name = "Atlas check" } };
        byte[] first = JuliaParameterAtlasRenderer.Render(source, 0, 0, 64, 48, CancellationToken.None);
        byte[] second = JuliaParameterAtlasRenderer.Render(source, -1, .2m, 64, 48, CancellationToken.None);
        Check(!first.SequenceEqual(second), "Different C values produced identical previews.");
        Check(source.JuliaCReal == -.8m && source.CenterXExact is not null && source.Zoom.ToDouble() == 1e50,
            "Preview rendering changed its source state.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { JuliaParameterAtlasRenderer.Render(source, 0, 0, 64, 48, cancelled.Token); throw new Exception("Cancellation ignored."); }
            catch (OperationCanceledException) { }
        }

        var main = new MandelbrotWindow(MandelbrotVariant.Julia);
        var other = new MandelbrotWindow(MandelbrotVariant.JuliaBurningShip);
        JuliaParameterAtlasWindow? atlas = null;
        try
        {
            Check(Field<Button>(main, "ParameterAtlasButton").Visibility == Visibility.Visible, "Classic Julia has no atlas button.");
            Check(Field<Button>(other, "ParameterAtlasButton").Visibility == Visibility.Collapsed, "Prototype leaked to another Julia variant.");
            Invoke(main, "ToggleControlsButton_OnClick", main, new RoutedEventArgs());
            Check(Field<Button>(main, "ParameterAtlasButton").Visibility == Visibility.Visible, "Hiding the menu hid the atlas button.");
            main.LoadState(source);
            var before = main.CaptureState("before");
            // Create only a hidden native owner handle; no window is shown or captured.
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            atlas = (JuliaParameterAtlasWindow)Invoke(main, "CreateParameterAtlas")!;
            Check(atlas.Owner == main, "Atlas lost the source window.");
            Field<ComboBox>(atlas, "GridSizeBox").SelectedIndex = 0;

            // Start two builds before awaiting: only the second generation may publish results.
            Task obsolete = (Task)Invoke(atlas, "BuildAtlasAsync")!;
            Field<TextBox>(atlas, "MinRealBox").Text = "-1.2";
            Field<TextBox>(atlas, "MaxRealBox").Text = "-0.6";
            Task current = (Task)Invoke(atlas, "BuildAtlasAsync")!;
            await Task.WhenAll(obsolete, current);
            var tiles = Field<UniformGrid>(atlas, "TilesPanel");
            Check(tiles.Children.Count == 9 && tiles.Children.OfType<Button>().All(b => b.IsEnabled), "Latest atlas did not finish.");
            Check(atlas.SelectedReal == -.9m, "An obsolete generation replaced the selected C.");
            Check(Field<Image>(atlas, "SelectedPreview").Source is BitmapSource, "Selected preview is empty.");

            // Select a noncentral tile via its actual UI event, then apply through the bound source event.
            ((Button)tiles.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            decimal selectedReal = atlas.SelectedReal, selectedImaginary = atlas.SelectedImaginary;
            int windowCount = Application.Current.Windows.Count;
            Field<Button>(atlas, "ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var applied = main.CaptureState("after");
            Check(applied.JuliaCReal == selectedReal && applied.JuliaCImaginary == selectedImaginary, "Selection was not applied to the source window.");
            Check(Application.Current.Windows.Count == windowCount, "Applying opened a new window.");
            Check(applied.CenterXExact == before.CenterXExact && applied.Zoom == before.Zoom && applied.Iterations == before.Iterations
                && applied.Palette.Name == before.Palette.Name && applied.ColoringMode == before.ColoringMode,
                "Applying C changed the view or rendering settings.");

            // Refinement and back preserve the parameter range and its Y orientation.
            Task rebuild = (Task)Invoke(atlas, "BuildAtlasAsync")!;
            Field<Button>(atlas, "CancelAtlasButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await rebuild;
            Check(!Field<Button>(atlas, "CancelAtlasButton").IsEnabled, "Cancellation left the atlas busy.");
            await (Task)Invoke(atlas, "BuildAtlasAsync")!;
            decimal widthBefore = decimal.Parse(Field<TextBox>(atlas, "MaxRealBox").Text, CultureInfo.InvariantCulture)
                - decimal.Parse(Field<TextBox>(atlas, "MinRealBox").Text, CultureInfo.InvariantCulture);
            Field<Button>(atlas, "NarrowButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (Field<Button>(atlas, "CancelAtlasButton").IsEnabled) await Task.Delay(10);
            decimal narrowedWidth = decimal.Parse(Field<TextBox>(atlas, "MaxRealBox").Text, CultureInfo.InvariantCulture)
                - decimal.Parse(Field<TextBox>(atlas, "MinRealBox").Text, CultureInfo.InvariantCulture);
            Check(narrowedWidth == widthBefore / 2, "Refinement did not halve the parameter range.");
            Field<Button>(atlas, "BackButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (Field<Button>(atlas, "CancelAtlasButton").IsEnabled) await Task.Delay(10);
            Check(decimal.Parse(Field<TextBox>(atlas, "MaxRealBox").Text, CultureInfo.InvariantCulture)
                - decimal.Parse(Field<TextBox>(atlas, "MinRealBox").Text, CultureInfo.InvariantCulture) == widthBefore,
                "Back did not restore the previous range.");

            // Invalid and comma-separated drafts are resolved by NumericInput before a build.
            Field<TextBox>(atlas, "MinRealBox").Text = "bad";
            Field<TextBox>(atlas, "MaxImaginaryBox").Text = "0,4";
            await (Task)Invoke(atlas, "BuildAtlasAsync")!;
            Check(NumericInput.IsRegistered(Field<TextBox>(atlas, "MinRealBox")) && NumericInput.IsValid(Field<TextBox>(atlas, "MinRealBox")), "Atlas bypassed NumericInput.");
            Check(decimal.Parse(Field<TextBox>(atlas, "MaxImaginaryBox").Text, CultureInfo.InvariantCulture) == .4m, "Decimal comma was lost.");
            Field<TextBox>(atlas, "MaxImaginaryBox").Text = "4e-1";
            Task closingBuild = (Task)Invoke(atlas, "BuildAtlasAsync")!;
            atlas.Close();
            await closingBuild;
        }
        finally { atlas?.Close(); other.Close(); main.Close(); }
        Console.WriteLine("PASS (julia-atlas): ranges, previews, cancellation, stale generations, numeric input and applying C in the existing window.");
    }
}
