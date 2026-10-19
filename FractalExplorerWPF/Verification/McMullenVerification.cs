using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Theming;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyMcMullenAsync(string[] args)
    {
        string directory = Path.Combine(AppPaths.DataRoot, "McMullenFrames");
        if (args.Length > 1) directory = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(directory);
        var random = new Random(314159);
        foreach (int p in new[] { 2, 3, 7, 12 })
        foreach (int q in new[] { 1, 2, 5, 12 })
        {
            var state = new McMullenState { P = p, Q = q, LambdaReal = -.031, LambdaImaginary = .017 };
            var dynamics = new McMullenDynamics(state);
            for (int sample = 0; sample < 12; sample++)
            {
                Complex z = Complex.FromPolarCoordinates(.4 + random.NextDouble(), random.NextDouble() * 6);
                Complex reference = Complex.Pow(z, p) + dynamics.Lambda / Complex.Pow(z, q);
                Check((reference - dynamics.Evaluate(z)).Magnitude < 1e-10 * (1 + reference.Magnitude), "McMullen integer powers must agree with independent complex powers.");
            }
            for (int k = 0; k < p + q; k++)
            {
                Complex critical = dynamics.CriticalPoint(k);
                Check((Complex.Pow(critical, p + q) - (double)q / p * dynamics.Lambda).Magnitude < 1e-12,
                    "All free critical points must solve p*z^(p+q) = q*lambda.");
                Complex a = dynamics.CriticalPoint(), b = critical;
                for (int n = 0; n < 4 && a.Magnitude < 2 && a.Magnitude > .05; n++)
                {
                    Check(Math.Abs(a.Magnitude - b.Magnitude) < 1e-9, "Rotated critical orbits must have equal moduli; the parameter map may use one representative.");
                    a = dynamics.Evaluate(a); b = dynamics.Evaluate(b);
                }
            }
        }
        var defaults = McMullenPresets.All[0];
        var d = new McMullenDynamics(defaults);
        Check(d.Analyze(Complex.Zero).Outcome == McMullenOutcome.Pole && d.Analyze(new(1e-100, 0)).Outcome == McMullenOutcome.Pole,
            "The exact pole and tiny nonzero points must escape through the pole without overflow.");
        Check(d.Analyze(new(10, 0)).Outcome == McMullenOutcome.Escaped, "The exterior must escape.");
        var zero = defaults.Clone(); zero.LambdaReal = zero.LambdaImaginary = 0;
        Check(new McMullenDynamics(zero).Evaluate(Complex.Zero) == Complex.Zero &&
            new McMullenDynamics(zero).Analyze(Complex.Zero).Outcome == McMullenOutcome.IterationLimit,
            "At lambda=0 the singularity is removable and zero stays fixed.");
        zero.Coloring = McMullenColoring.Period;
        Check(new McMullenDynamics(zero).Analyze(new(.1, 0)).Period == 1, "The polynomial interior must converge numerically to its fixed point.");
        var boundary = zero.Clone(); boundary.MaxIterations = 1;
        Check(new McMullenDynamics(boundary).Analyze(Complex.ImaginaryOne).Outcome == McMullenOutcome.IterationLimit,
            "An unfinished boundary orbit must not be declared bounded or periodic.");
        var huge = defaults.Clone(); huge.LambdaReal = 1e6; huge.P = 2; huge.Q = 1; huge.EscapeRadius = 2;
        var hd = new McMullenDynamics(huge);
        Check(hd.EscapeRadius > 100 && hd.Analyze(new(3, 0)).Iterations > 0,
            "Large lambda needs a parameter-dependent escape bound, never the user radius alone.");
        var tiny = defaults.Clone(); tiny.LambdaReal = 1e-320; tiny.Q = 12;
        var td = new McMullenDynamics(tiny);
        Complex tinyZ = new(1e-27, 0);
        Check(double.IsFinite(td.Evaluate(tinyZ).Real) && td.Evaluate(tinyZ).Real > 1000 && td.CriticalPoint().Magnitude > 0,
            "Subnormal lambda and an underflowing denominator must retain a finite quotient and nonzero critical root.");

        byte[] Frame(McMullenState state, int threads = 1)
        {
            const int w = 192, h = 144;
            var renderer = new McMullenRenderer(state, w, h);
            var pixels = new byte[w * h * 4];
            Parallel.ForEach(MandelbrotTileScheduler.Create(w, h, 16, TileSchedulingStrategy.Spiral),
                new ParallelOptions { MaxDegreeOfParallelism = threads }, tile =>
                {
                    byte[] buffer = renderer.RenderTile(tile, CancellationToken.None);
                    for (int y = 0; y < tile.Height; y++) Buffer.BlockCopy(buffer, y * tile.Width * 4,
                        pixels, ((tile.Y + y) * w + tile.X) * 4, tile.Width * 4);
                });
            return pixels;
        }
        var frames = new List<byte[]>();
        foreach (var state in McMullenPresets.All)
        {
            var frame = Frame(state);
            int colors = Enumerable.Range(0, frame.Length / 4).Select(i => BitConverter.ToInt32(frame, 4 * i)).Distinct().Count();
            Check(colors > 30 && frames.All(f => !f.SequenceEqual(frame)), $"Preset {state.SaveName} must be varied and distinct; colors={colors}.");
            frames.Add(frame);
            var image = BitmapSource.Create(192, 144, 96, 96, PixelFormats.Bgra32, null, frame, 192 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(directory, $"{frames.Count:00}.png")); encoder.Save(stream);
        }
        Check(Frame(defaults).SequenceEqual(Frame(defaults, Environment.ProcessorCount)), "Tiled output must be deterministic across thread counts.");
        var expected = Frame(defaults);
        var exported = await McMullenRenderer.RenderStateAsync(defaults, 192, 144, CancellationToken.None);
        var actual = new byte[expected.Length]; exported.CopyPixels(actual, 192 * 4, 0);
        Check(expected.SequenceEqual(actual), "Tiled preview and full export must match pixel for pixel.");
        var colored = new HashSet<string>();
        foreach (var coloring in Enum.GetValues<McMullenColoring>())
        {
            var state = McMullenPresets.All[3]; state.Coloring = coloring;
            colored.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Frame(state))));
        }
        Check(colored.Count >= 5, "Escape, route, phase and both trap styles must produce different images.");
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel(); bool cancelled = false;
            try { d.Analyze(Complex.One, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "Orbit work must honor cancellation.");
            cancelled = false;
            try { await McMullenRenderer.RenderStateAsync(defaults, 96, 72, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "Exports must honor cancellation.");
        }
        var ssaa = await McMullenRenderer.RenderStateAsync(defaults, 96, 72, CancellationToken.None, ssaa: 2);
        Check(ssaa.IsFrozen && ssaa.PixelWidth == 96 && ssaa.PixelHeight == 72, "SSAA export must return the requested frozen image.");
        var invalid = defaults.Clone(); invalid.Q = 0;
        bool rejected = false;
        try { invalid.Validate(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Invalid powers must be rejected.");
        var stateToSave = defaults.Clone(); stateToSave.SaveName = "McMullen round trip " + Guid.NewGuid().ToString("N");
        stateToSave.Timestamp = new DateTime(2026, 10, 16, 11, 0, 0); stateToSave.Coloring = McMullenColoring.CircleTrap;
        stateToSave.ColorOffset = .234; stateToSave.ShowCriticalPoints = true; stateToSave.Plane = McMullenPlane.Parameter;
        stateToSave.ParameterCenterX = .0123456789012345; stateToSave.ParameterZoom = 123.456789;
        var store = new McMullenSaveStore(); var slot = store.Save(stateToSave);
        var loaded = store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State;
        string Json(McMullenState s) => JsonSerializer.Serialize(s, JsonOptionsFactory.Create());
        Check(Json(loaded) == Json(stateToSave), "Disk saves must preserve every parameter, both views and palette.");
        Check(CloudSaveRepository.ReadLocal(slot.FilePath).Category == "McMullen", "The shared cloud repository must recognize the save category.");
        var clone = loaded.Clone(); clone.Palette.RootColors[0] = Colors.Magenta;
        Check(clone.Palette.RootColors[0] != loaded.Palette.RootColors[0], "Palettes must not be shared between states.");

        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml") });
        ThemeManager.Initialize(Application.Current);
        var window = new McMullenWindow();
        try
        {
            window.LoadState(loaded); var captured = window.CaptureState(loaded.SaveName); captured.Timestamp = loaded.Timestamp;
            Check(Json(captured) == Json(loaded), "WPF load/capture must preserve every parameter and both views.");
            window.ExploreParameter(-.03, .012);
            var chosen = window.CaptureState("chosen");
            Check(chosen.Plane == McMullenPlane.Julia && chosen.LambdaReal == -.03 && chosen.LambdaImaginary == .012 &&
                chosen.ParameterZoom == loaded.ParameterZoom, "Picking lambda must open its Julia and preserve the map view.");
            var configuration = SaveManagerConfigurations.ForMcMullen(window, store);
            Check(configuration.PointsOfInterest.Count == 8 && configuration.GetDetails(loaded).Contains("λ"), "Save manager must contain presets and readable details.");
            ((TextBox)window.FindName("PBox")).Text = "4";
            Check(window.CaptureState("edited").P == 4, "Power edits must reach the state.");
            var qBox = (TextBox)window.FindName("QBox");
            qBox.Text = "2.5";
            var corrected = window.CaptureState("corrected pole order");
            Check(corrected.Q == chosen.Q && qBox.Text == chosen.Q.ToString() && NumericInput.GetHasNotice(qBox),
                $"A fractional pole order must visibly restore the last valid integer before saving or rendering. Expected {chosen.Q}; state {corrected.Q}; text '{qBox.Text}'; notice '{NumericInput.GetMessage(qBox)}'.");
            var correctedSlot = store.Save(corrected);
            Check(Json(store.LoadSlots().Slots.Single(s => s.FilePath == correctedSlot.FilePath).State) == Json(corrected),
                "Saving and loading a corrected draft must preserve the corrected state exactly.");
            window.LoadState(defaults);
            var root = (FrameworkElement)window.Content;
            root.Measure(new(720, 360)); root.Arrange(new Rect(0, 0, 720, 360));
            var render = typeof(McMullenWindow).GetMethod("RenderPreviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var pending = (Task)render.Invoke(window, null)!;
            window.LoadState(loaded); await pending;
            Check(((Image)window.FindName("StablePreviewImage")).Source is null, "Cancelled tiles must not replace a newly loaded state.");
            window.LoadState(defaults); await (Task)render.Invoke(window, null)!;
            Check(((Image)window.FindName("StablePreviewImage")).Source is BitmapSource, "A successful headless WPF frame must publish its bitmap.");
        }
        finally { window.Close(); }
        var item = FractalCatalog.Create().Single(i => i.LaunchKey == "McMullen");
        Check(item.IntroducedAt is not null && MainWindow.GetWindowFactory(item.LaunchKey) is not null && CatalogPreviewLoader.IsRendered(item),
            "The catalog must provide introduction date, launcher and rendered preview.");
        var catalogImage = await CatalogPreviewLoader.RenderAsync(item, CancellationToken.None);
        Check(catalogImage.IsFrozen && catalogImage.PixelWidth == 512, "The actual catalog preview must render successfully.");
        Console.WriteLine($"PASS (mcmullen): equations, critical orbits, escape bounds, poles/zero, presets, colors, tiles, cancellation, SSAA, saves, catalog and WPF. Frames: {directory}");
    }
}
