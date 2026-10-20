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
        var other = new MandelbrotWindow(MandelbrotVariant.Mandelbrot);
        JuliaParameterAtlasWindow? atlas = null;
        try
        {
            Check(Field<Button>(main, "ParameterAtlasButton").Visibility == Visibility.Visible, "Classic Julia has no atlas button.");
            Check(Field<Button>(other, "ParameterAtlasButton").Visibility == Visibility.Collapsed, "Atlas leaked to the parameter plane.");
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

        int variantsChecked = 0;
        foreach (var variant in Enum.GetValues<MandelbrotVariant>().Where(MandelbrotVariantDefinition.IsJulia))
        {
            var window = new MandelbrotWindow(variant);
            JuliaParameterAtlasWindow? familyAtlas = null;
            try
            {
                var definition = MandelbrotVariantDefinition.For(variant);
                Button button = Field<Button>(window, "ParameterAtlasButton");
                Check(button.Visibility == Visibility.Visible && button.Content is System.Windows.Shapes.Path { Data: not null }
                    && System.Windows.Automation.AutomationProperties.GetName(button) == "Атлас параметров",
                    $"{variant}: atlas icon or its accessible label is missing.");
                var state = window.CaptureState("atlas-family");
                state.Iterations = 50;
                if (definition.HasPower) state.Power = 2.5m;
                if (definition.HasInversion) state.UseInversion = true;
                if (FoldedFormulaCatalog.IsHybrid(variant)) state.Hybrid = new()
                {
                    Steps = [new() { Formula = MandelbrotVariant.Tricorn, Power = 3, Repeats = 2 },
                        new() { Formula = MandelbrotVariant.Buffalo, Power = 2 }]
                };
                window.LoadState(state);
                var before = window.CaptureState("before-family");
                familyAtlas = new JuliaParameterAtlasWindow(before);
                Check(familyAtlas.Title.Contains(definition.DisplayName) && Field<TextBlock>(familyAtlas, "HeaderText").Text.Contains(definition.DisplayName),
                    $"{variant}: atlas still displays the classical Julia title.");
                Check(ParameterAtlasRange.JuliaOverview(variant).IsValid, $"{variant}: overview range is invalid.");

                // Tiny frames exercise the atlas adapter for each family without full renders or save tests.
                var expected = before.Clone();
                expected.CenterX = expected.CenterY = 0;
                expected.CenterXExact = expected.CenterYExact = null;
                expected.Zoom = .8;
                expected.Threads = 1;
                const int width = 32, height = 24;
                byte[] pixels = JuliaParameterAtlasRenderer.Render(before, before.JuliaCReal, before.JuliaCImaginary,
                    width, height, CancellationToken.None);
                var reference = new byte[width * height * 4];
                MandelbrotFamilyRenderer.Render(expected, reference, width, height, width * 4, CancellationToken.None);
                Check(pixels.SequenceEqual(reference), $"{variant}: atlas dropped the variant, power, inversion or hybrid formula.");
                Invoke(window, "ApplyAtlasConstant", -.45m, .125m);
                var applied = window.CaptureState("after-family");
                Check(applied.Variant == variant && applied.JuliaCReal == -.45m && applied.JuliaCImaginary == .125m
                    && applied.Power == before.Power && applied.UseInversion == before.UseInversion
                    && applied.Hybrid.Steps.Select(s => (s.Formula, s.Power, s.Repeats))
                        .SequenceEqual(before.Hybrid.Steps.Select(s => (s.Formula, s.Power, s.Repeats))),
                    $"{variant}: applying C changed the source formula.");
                variantsChecked++;
            }
            finally { familyAtlas?.Close(); window.Close(); }
            await Task.Yield();
        }

        // A formula change in the owner is forwarded to an already-open atlas.
        foreach (var variant in new[] { MandelbrotVariant.JuliaGeneralized, MandelbrotVariant.JuliaSimonobrot, MandelbrotVariant.JuliaHybrid })
        {
            var window = new MandelbrotWindow(variant);
            JuliaParameterAtlasWindow? formulaAtlas = null;
            try
            {
                new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                formulaAtlas = (JuliaParameterAtlasWindow)Invoke(window, "CreateParameterAtlas")!;
                var previous = window.CaptureState("formula-before");
                previous.Iterations = 50;
                Field<ComboBox>(formulaAtlas, "GridSizeBox").SelectedIndex = 0;
                Task inFlight = (Task)Invoke(formulaAtlas, "BuildAtlasAsync")!;
                var changed = previous.Clone();
                changed.Power = 2.5m;
                changed.UseInversion = true;
                changed.Hybrid.Steps[0].Formula = MandelbrotVariant.Tricorn;
                window.LoadState(changed);
                var copiedSource = (MandelbrotState)typeof(JuliaParameterAtlasWindow)
                    .GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(formulaAtlas)!;
                Check(copiedSource.Power == changed.Power && copiedSource.UseInversion == changed.UseInversion
                    && copiedSource.Hybrid.Steps[0].Formula == MandelbrotVariant.Tricorn,
                    $"{variant}: an open atlas kept obsolete formula parameters.");
                await inFlight;
                // The in-flight generation must keep its original formula for every tile.
                var bitmap = (BitmapSource)Field<Image>(formulaAtlas, "SelectedPreview").Source;
                var actual = new byte[144 * 108 * 4];
                bitmap.CopyPixels(actual, 144 * 4, 0);
                previous.Iterations = 500; // Initial owner state used by that generation (preview caps at 250).
                var oldFrame = JuliaParameterAtlasRenderer.Render(previous, formulaAtlas.SelectedReal, formulaAtlas.SelectedImaginary,
                    144, 108, CancellationToken.None);
                Check(actual.SequenceEqual(oldFrame), $"{variant}: changing the owner mixed formulas in an in-flight generation.");
            }
            finally { formulaAtlas?.Close(); window.Close(); }
        }
        Console.WriteLine($"PASS (julia-atlas): {variantsChecked} Julia variants, icon, formula propagation, ranges, previews, cancellation, stale generations, numeric input and applying C in the existing window.");
    }
}
