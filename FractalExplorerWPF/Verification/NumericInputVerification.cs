using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Theming;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyNumericInputAsync()
    {
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/FractalExplorerWPF;component/Theming/ThemeStyles.xaml", UriKind.Relative) });
        ThemeManager.Initialize(Application.Current);
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        var scope = new StackPanel();
        var count = new TextBox { Text = "500" };
        var exact = new TextBox { Text = "0.1234567890123456789012345678" };
        var zoom = new TextBox { Text = "1" };
        var lower = new TextBox { Text = "-2" };
        var upper = new TextBox { Text = "2" };
        NumericInput.Integer(scope, count, "Итерации", 50, 2000000);
        NumericInput.Decimal(scope, exact, "C", -10, 10);
        NumericInput.Zoom(scope, zoom, FloatExp.FromDouble(1e-6), FloatExp.Pow10(1000));
        var preciseZoom = FloatExp.FromDouble(1.2345678912345) * FloatExp.Pow10(750);
        var originalZoom = preciseZoom;
        var preciseZoomBox = new TextBox { Text = FractalExplorerWPF.Infrastructure.Serialization.FloatExpJsonConverter.ToDisplay(preciseZoom) };
        NumericInput.Zoom(scope, preciseZoomBox, FloatExp.FromDouble(1e-6), FloatExp.Pow10(1000), () => preciseZoom);
        preciseZoomBox.TextChanged += (_, _) =>
        {
            if (FloatExp.TryParse(preciseZoomBox.Text, out var value) && value.IsFinite && value.Sign > 0) preciseZoom = value;
        };
        preciseZoomBox.Text = "abc"; NumericInput.Commit(preciseZoomBox);
        Require(Math.Abs((preciseZoom / originalZoom).ToDouble() - 1) < 1e-14,
            "Deep zoom fallback used the rounded display instead of the applied mantissa.");
        NumericInput.Real(scope, lower, "Нижняя", -100, 100);
        NumericInput.Real(scope, upper, "Верхняя", -100, 100);
        NumericInput.OrderedPair(scope, lower, upper);
        var slider = new Slider { Minimum = .1, Maximum = 5, Value = 1 };
        var sliderBox = new TextBox();
        sliderBox.SetBinding(TextBox.TextProperty, new Binding("Value")
        { Source = slider, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        NumericInput.SetUseSliderBounds(sliderBox, true);
        sliderBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
        sliderBox.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Require(NumericInput.IsRegistered(sliderBox), "Direct slider binding did not declare numeric bounds.");
        sliderBox.Text = "abc"; NumericInput.Commit(sliderBox);
        Require(slider.Value == 1 && !Validation.GetHasError(sliderBox), "Bound field did not restore the slider/clear its binding error.");
        var preciseSlider = new Slider { Minimum = .1, Maximum = 5, Value = 1.2345678912345 };
        var preciseBox = new TextBox();
        preciseBox.SetBinding(TextBox.TextProperty, new Binding("Value")
        { Source = preciseSlider, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit, StringFormat = "F2" });
        NumericInput.SetUseSliderBounds(preciseBox, true);
        preciseBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
        preciseBox.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        NumericInput.Prepare(preciseBox);
        Require(preciseSlider.Value == 1.2345678912345, "Automatic capture rounded a slider through its displayed text.");
        preciseBox.Text = "abc"; NumericInput.Commit(preciseBox);
        Require(preciseSlider.Value == 1.2345678912345, "Restoring a formatted binding lost the applied precision.");
        preciseBox.Text = "2"; NumericInput.Commit(preciseBox);
        Require(preciseSlider.Value == 2, "Enter/apply did not update an explicit binding with a valid draft.");
        preciseSlider.Maximum = .75;
        preciseBox.Text = "1";
        Require(!NumericInput.IsValid(preciseBox), "Slider limits were captured once instead of following the current domain.");
        NumericInput.Commit(preciseBox);
        Require(preciseSlider.Value == .75, "Changed slider bounds were not used for correction.");
        var nestedEditor = new UserControl();
        var nestedPanel = new StackPanel();
        nestedEditor.Content = nestedPanel;
        var nestedBox = new TextBox();
        nestedPanel.Children.Add(nestedBox);
        nestedBox.SetBinding(TextBox.TextProperty, new Binding("Value")
        { Source = slider, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit });
        NumericInput.SetUseSliderBounds(nestedBox, true);
        nestedBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
        nestedBox.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        nestedBox.Text = "abc"; NumericInput.CommitAll(nestedEditor);
        Require(nestedBox.Text == "1" && slider.Value == 1 && NumericInput.GetHasNotice(nestedBox),
            "An editor's apply command did not complete its automatically registered slider field.");
        var conditionalScope = new StackPanel();
        var inactiveMinimum = new TextBox { Text = "0" };
        var inactiveMaximum = new TextBox { Text = "1" };
        bool pairActive = false;
        NumericInput.Real(conditionalScope, inactiveMinimum, "Минимум", 0, 10);
        NumericInput.Real(conditionalScope, inactiveMaximum, "Максимум", 0, 10);
        NumericInput.Pair(conditionalScope, inactiveMinimum, inactiveMaximum,
            (a, b) => double.Parse(a, CultureInfo.InvariantCulture) < double.Parse(b, CultureInfo.InvariantCulture),
            "Минимум меньше максимума.", () => pairActive);
        inactiveMinimum.Text = "2"; NumericInput.CommitAll(conditionalScope);
        Require(inactiveMinimum.Text == "2", "Inactive group modified the active parameters.");
        pairActive = true; NumericInput.CommitAll(conditionalScope);
        Require(inactiveMinimum.Text == "0" && inactiveMaximum.Text == "1", "Activating a group did not restore its last valid pair.");
        var reactiveScope = new StackPanel();
        var reactiveBox = new TextBox { Text = "3" };
        reactiveBox.TextChanged += (_, _) => NumericInput.Prepare(reactiveScope);
        NumericInput.Integer(reactiveScope, reactiveBox, "Порядок полюса", 1, 12);
        reactiveBox.Text = "2.5";
        Require(reactiveBox.Text == "3" && NumericInput.GetHasNotice(reactiveBox),
            "A synchronous capture in TextChanged erased the correction feedback.");
        reactiveBox.Text = "4";
        Require(!NumericInput.GetHasNotice(reactiveBox), "Editing a corrected field did not clear its old feedback.");
        foreach (string culture in new[] { "ru-RU", "en-US" })
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                lower.Text = "-0,5"; NumericInput.Commit(lower);
                Require(lower.Text == "-0.5", "Decimal comma is not accepted in both cultures.");
                lower.Text = "-2";
                foreach (string draft in new[] { "-", "", "1e-", "abc", "NaN", "Infinity", "1e309" })
                {
                    zoom.Text = draft;
                    Require(zoom.Text == draft, "Draft was rewritten before completion.");
                    NumericInput.Commit(zoom);
                    Require(FloatExp.TryParse(zoom.Text, out var z) && z.IsFinite && z.Sign > 0, "Invalid zoom after commit.");
                }
                zoom.Text = "1e1000"; NumericInput.Commit(zoom);
                Require(zoom.Text == "1e1000", "Deep zoom was rounded or overflowed.");
                string digits = exact.Text; NumericInput.Commit(exact);
                Require(exact.Text == digits, "Exact coordinate digits changed.");
                count.Text = "0";
                count.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, count, null)
                { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
                Require(count.Text == "50" && NumericInput.GetMessage(count).Length > 0, "Blur did not clamp/explain.");
                count.Text = "999999999999999999999999"; NumericInput.Commit(count);
                Require(count.Text == "2000000", "Integer overflow did not clamp.");
                count.Text = "abc"; NumericInput.Commit(count);
                Require(count.Text == "2000000", "Last valid value was lost.");
                lower.Text = "4"; NumericInput.CommitAll(scope);
                Require(lower.Text == "-2" && upper.Text == "2", "Invalid pair was not restored together.");
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        var factories = new List<Func<Window>>
        {
            () => new MandelbrotWindow(MandelbrotVariant.Mandelbrot), () => new NewtonPoolsWindow(),
            () => new CollatzWindow(), () => new NovaWindow(NovaVariant.Julia), () => new PhoenixWindow(),
            () => new DomainColoringWindow(), () => new BuddhabrotWindow(), () => new SerpinskyWindow(),
            () => new ApollonianWindow(), () => new IfsWindow(), () => new FlameWindow(),
            () => new DlaWindow(), () => new InverseCollatzTreeWindow(), () => new McMullenWindow(),
            () => new TranscendentalJuliaWindow()
        };
        foreach (var kind in Enum.GetValues<BasinExplorerKind>()) factories.Add(() => new BasinExplorerWindow(kind));
        foreach (var kind in Enum.GetValues<MathematicalLaboratoryKind>()) factories.Add(() => new MathematicalLaboratoryWindow(kind));
        foreach (var kind in Enum.GetValues<Fractal3DKind>()) factories.Add(() => new Fractal3DWindow(kind));
        foreach (var kind in Enum.GetValues<DynamicSystemKind>()) factories.Add(() => new DynamicSystemWindow(kind));
        foreach (var variant in Enum.GetValues<MandelbrotVariant>())
            if (variant != MandelbrotVariant.Mandelbrot) factories.Add(() => new MandelbrotWindow(variant));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        int fields = 0;
        foreach (var factory in factories)
        {
            using var cleanup = new NumericWindowCleanup(factory());
            Window window = cleanup.Window;
            var capture = window.GetType().GetMethod(window is Fractal3DWindow ? "CaptureViewFields" : "CaptureState", flags)!;
            try
            {
                capture.Invoke(window, ["numeric-input"]);
                var boxes = new HashSet<TextBox>();
                foreach (var field in window.GetType().GetFields(flags))
                    if (field.GetValue(window) is TextBox box) boxes.Add(box);
                void Collect(DependencyObject parent)
                {
                    if (parent is TextBox text) boxes.Add(text);
                    foreach (object child in LogicalTreeHelper.GetChildren(parent))
                        if (child is DependencyObject node) Collect(node);
                }
                Collect(window);
                foreach (TextBox box in boxes)
                {
                    if (!NumericInput.IsRegistered(box) || box.IsReadOnly) continue;
                    string original = box.Text;
                    foreach (string bad in new[] { "abc", "NaN", "1e309" })
                    {
                        box.Text = bad;
                        NumericInput.CommitAll(window);
                        Require(NumericInput.IsValid(box), $"{window.GetType().Name}.{box.Name} still invalid: {box.Text}");
                        capture.Invoke(window, ["numeric-input"]);
                    }
                    box.Text = original; fields++;
                }
            }
            catch (TargetInvocationException e) { throw new Exception(window.GetType().Name, e.InnerException); }
        }
        foreach (var (factory, field, bad, expected) in new (Func<Window>, string, string, string)[]
        {
            (() => new NewtonPoolsWindow(), "IterationsBox", "0", "1"),
            (() => new ApollonianWindow(), "DepthBox", "0", "1"),
            (() => new CollatzWindow(), "MaximumPeriodBox", "0", "1"),
            (() => new PhoenixWindow(), "OrbitTrapRadiusBox", "-1", "0"),
            (() => new DlaWindow(), "GridSizeBox", "202", "203")
        })
        {
            using var cleanup = new NumericWindowCleanup(factory());
            var box = (TextBox)cleanup.Window.FindName(field);
            box.Text = bad;
            cleanup.Window.GetType().GetMethod("CaptureState", flags)!.Invoke(cleanup.Window, ["numeric-input"]);
            Require(box.Text == expected, $"{field}: expected {expected}, got {box.Text}");
        }
        using (var cleanup = new NumericWindowCleanup(new MathematicalLaboratoryWindow(MathematicalLaboratoryKind.Rauzy)))
        {
            ((ComboBox)cleanup.Window.FindName("ModeBox")).SelectedIndex = 0;
            var box = (TextBox)cleanup.Window.FindName("TertiaryBox"); box.Text = "abc";
            cleanup.Window.GetType().GetMethod("CaptureState", flags)!.Invoke(cleanup.Window, ["numeric-input"]);
            Require(!box.IsEnabled && NumericInput.IsValid(box), "Disabled Rauzy field still blocks capture.");
        }
        // The original symptom: a bad numeric value must not finish before starting a new render.
        using (var cleanup = new NumericWindowCleanup(new MandelbrotWindow(MandelbrotVariant.Mandelbrot)))
        {
            var window = cleanup.Window;
            var iterations = (TextBox)window.FindName("IterationsBox");
            var ambient = (TextBox)window.FindName("DistanceAmbientBox");
            var image = (Image)window.FindName("StablePreviewImage");
            var old = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, new byte[4], 4);
            image.Source = old;
            iterations.Text = "0"; ambient.Text = "2";
            NumericInput.CommitAll(window);
            await (Task)window.GetType().GetMethod("RenderPreviewAsync", flags)!.Invoke(window, null)!;
            Require(iterations.Text == "50" && ambient.Text == "1", "Build did not correct active/hidden fields.");
            Require(image.Source is not null && !ReferenceEquals(image.Source, old), "New render did not publish a frame after correction.");
        }
        // Apply must read the completed matrix instead of silently passing the cached values.
        foreach (Window editor in new Window[]
        {
            new IfsTransformEditorWindow([new IfsAffineTransform { A = .5, E = .5, Probability = 1 }]),
            new FlameTransformEditorWindow(FlameState.CreateDefaults())
        })
        {
            using var cleanup = new NumericWindowCleanup(editor);
            var box = (TextBox)editor.FindName("ABox");
            box.Text = "NaN";
            editor.GetType().GetMethod("Commit", flags)!.Invoke(editor, null);
            Require(NumericInput.IsValid(box), "Matrix apply retained a nonfinite draft.");
        }
        using (var cleanup = new NumericWindowCleanup(new BuddhabrotPaletteWindow(new BuddhabrotPaletteManager())))
        {
            var editor = cleanup.Window;
            editor.GetType().GetMethod("Copy_OnClick", flags)!.Invoke(editor, [editor, new RoutedEventArgs()]);
            var gamma = (TextBox)editor.FindName("GammaBox");
            gamma.Text = "0";
            Require((bool)editor.GetType().GetMethod("SaveEdits", flags)!.Invoke(editor, null)! && gamma.Text == "0.1",
                "Palette apply still silently rejects the numeric draft.");
        }
        using (var cleanup = new NumericWindowCleanup(new GrayScottWindow()))
        {
            var minimum = (TextBox)cleanup.Window.FindName("RangeMinimumBox");
            var maximum = (TextBox)cleanup.Window.FindName("RangeMaximumBox");
            string a = minimum.Text, b = maximum.Text;
            minimum.Text = maximum.Text;
            NumericInput.CommitAll(cleanup.Window);
            Require(minimum.Text == a && maximum.Text == b, "Gray–Scott did not restore the color range pair.");
        }
        using (var cleanup = new NumericWindowCleanup(new DomainColoringWindow()))
        {
            var formula = (TextBox)cleanup.Window.FindName("FormulaBox");
            formula.Text = "z^";
            await (Task)cleanup.Window.GetType().GetMethod("RenderPreviewAsync", flags)!.Invoke(cleanup.Window, null)!;
            Require(formula.Text == "z^" && NumericInput.GetHasNotice(formula), "A formula error must identify the field without guessing a replacement.");
            formula.Text = "z";
            await (Task)cleanup.Window.GetType().GetMethod("RenderPreviewAsync", flags)!.Invoke(cleanup.Window, null)!;
            Require(!NumericInput.GetHasNotice(formula), "Correcting the formula did not clear its field feedback.");
        }
        using (var cleanup = new NumericWindowCleanup(new DynamicSystemWindow(DynamicSystemKind.Lyapunov)))
        {
            var boxes = (Dictionary<string, TextBox>)cleanup.Window.GetType().GetField("_boxes", flags)!.GetValue(cleanup.Window)!;
            boxes["Pattern"].Text = "xyz";
            bool rejected = false;
            try { cleanup.Window.GetType().GetMethod("CaptureState", flags)!.Invoke(cleanup.Window, ["pattern"]); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejected = true; }
            Require(rejected && NumericInput.GetHasNotice(boxes["Pattern"]), "Pattern errors must identify the field.");
        }
        foreach (Window window in new Window[] { new TuringWindow(), new SnowCrystalWindow(), new SandpileWindow(), new LSystemWindow(),
            new JuliaConstantPickerWindow(MandelbrotVariant.Mandelbrot, 0, 0),
            new Fractal3DConstantPickerWindow(Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Juliabulb)),
            new PhoenixParameterExplorerWindow(new PhoenixState()),
            new MandelbrotPaletteWindow(new MandelbrotPaletteManager()), new GrayScottPaletteWindow(new GrayScottPaletteManager()),
            new DomainColoringPaletteWindow(new DomainColoringPaletteManager()), new InverseCollatzPaletteWindow(new InverseCollatzPaletteManager()),
            new Fractal3DPaletteWindow(new Fractal3DPaletteManager(), Fractal3DPalettes.Classic()) })
        {
            using var cleanup = new NumericWindowCleanup(window);
            foreach (var field in window.GetType().GetFields(flags))
            {
                if (field.GetValue(window) is not TextBox box || !NumericInput.IsRegistered(box)) continue;
                box.Text = "NaN";
                NumericInput.CommitAll(window);
                Require(NumericInput.IsValid(box), $"{window.GetType().Name}.{box.Name} did not restore its numeric draft.");
            }
        }
        Console.WriteLine($"PASS (numeric-input): drafts, blur, cultures, clamping, fallback, exact decimals, 1e1000, pairs; {factories.Count} window configurations, {fields} registered fields.");
    }

    private sealed class NumericWindowCleanup(Window window) : IDisposable
    {
        public Window Window => window;
        public void Dispose() => window.Close();
    }
}
