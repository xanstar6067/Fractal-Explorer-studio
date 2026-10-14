using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Theming;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyTranscendentalJuliaAsync(string[] args)
    {
        string directory = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(AppContext.BaseDirectory, "VerificationData", "TranscendentalJulia", "Frames");
        Directory.CreateDirectory(directory);
        var catalog = FractalCatalog.Create();
        foreach (var kind in Enum.GetValues<TranscendentalJuliaKind>())
        {
            var item = catalog.Single(i => i.LaunchKey == TranscendentalJuliaCatalog.LaunchKey(kind));
            Check(item.CategoryPath.Last() == "Семейство Жюлиа" && item.IntroducedAt is not null &&
                MainWindow.GetWindowFactory(item.LaunchKey) is not null && CatalogPreviewLoader.IsRendered(item), "New Julia modes must have catalog dates, previews and launchers.");
            var catalogImage = await CatalogPreviewLoader.RenderAsync(item, CancellationToken.None);
            Check(catalogImage.PixelWidth == 512 && catalogImage.PixelHeight == 512 && catalogImage.IsFrozen,
                "The actual catalog renderer must return a frozen square CPU preview.");
            var catalogEncoder = new PngBitmapEncoder(); catalogEncoder.Frames.Add(BitmapFrame.Create(catalogImage));
            using (var file = File.Create(Path.Combine(directory, $"{kind}-catalog.png"))) catalogEncoder.Save(file);
            var state = TranscendentalJuliaCatalog.CreateDefault(kind);
            var dynamics = new TranscendentalJuliaDynamics(state);
            var cycles = dynamics.FindAttractors(CancellationToken.None);
            Check(cycles.Count == 1 && cycles[0].Points.Length == 1 && cycles[0].Multiplier < 1,
                "The two starting bouquets must have an attracting fixed point.");
            Check(kind == TranscendentalJuliaKind.Sine ? cycles[0].Points[0].Magnitude < 1e-10 :
                Math.Abs(cycles[0].Points[0].Real - 0.489402227180215) < 1e-9, "Starting attractor must agree with the analytic reference.");
            Complex returning = kind == TranscendentalJuliaKind.Exponential ? new(100, Math.PI) : new(100000 * Math.PI, 0);
            Check(dynamics.Classify(returning, cycles).Outcome == TranscendentalOrbitOutcome.Converged,
                "A large modulus must not be treated as a polynomial escape radius: this orbit returns and converges.");
            Complex beyond = kind == TranscendentalJuliaKind.Exponential ? new(1000, 0) : new(0, 1000);
            Check(dynamics.Classify(beyond, cycles).Outcome == TranscendentalOrbitOutcome.NumericalLimit,
                "Overflow must be reported as a numerical limit.");
            var tinyLambda = state.Clone(); tinyLambda.LambdaReal = 1e-300;
            Check(new TranscendentalJuliaDynamics(tinyLambda).TryStep(beyond, out Complex scaled) &&
                TranscendentalJuliaDynamics.IsFinite(scaled) && scaled.Magnitude > 1e100,
                "A tiny lambda must be applied before exponential growth to avoid premature intermediate overflow.");
            var shortState = state.Clone(); shortState.MaxIterations = 1;
            Check(new TranscendentalJuliaDynamics(shortState).Classify(new(1, 1), cycles).Outcome == TranscendentalOrbitOutcome.IterationLimit,
                "An unfinished orbit must remain unclassified.");
            var seeds = dynamics.FindRepellingSeeds(CancellationToken.None);
            Check(seeds.Count > 0, "Every starting mode must find repelling seeds.");
            foreach (Complex seed in seeds)
            {
                Complex first = dynamics.Evaluate(seed), second = dynamics.Evaluate(first);
                bool fixedPoint = (first - seed).Magnitude < 1e-8;
                Check((fixedPoint || (second - seed).Magnitude < 1e-8) &&
                    (fixedPoint ? dynamics.Derivative(seed).Magnitude : (dynamics.Derivative(seed) * dynamics.Derivative(first)).Magnitude) > 1,
                    "Inverse iteration must begin at verified repelling periodic points, never in an attracting basin.");
            }
            var complexState = state.Clone(); complexState.LambdaReal = .31; complexState.LambdaImaginary = -.27;
            var complexDynamics = new TranscendentalJuliaDynamics(complexState);
            foreach (Complex w in new Complex[] { new(.8, .6), new(-1.3, .2), new(2.2, -3), new(0, 2) })
                for (int branch = -8; branch <= 8; branch++)
                    Check((complexDynamics.Evaluate(complexDynamics.Preimage(w, branch)) - w).Magnitude < 1e-11,
                        "Every inverse branch must map back to w for a complex lambda.");

            var frames = new List<byte[]>();
            foreach (var preset in TranscendentalJuliaCatalog.Presets(kind))
            {
                var renderer = new TranscendentalJuliaRenderer(preset);
                var frame = new byte[256 * 192 * 4];
                renderer.Render(frame, 256, 192, 256 * 4, 1, CancellationToken.None);
                Check(renderer.SeedCount > 0 && (preset.Drawing == TranscendentalJuliaDrawing.Points ? renderer.CompletedDepth >= 8 :
                    renderer.RayCount > 20 && renderer.CompletedDepth >= 3), "Presets must retain a meaningful inverse tree or a family of continued rays.");
                int hairs = Enumerable.Range(0, 256 * 192).Count(i => frame[i * 4 + 2] > 130 && frame[i * 4 + 1] > 100 && frame[i * 4] < frame[i * 4 + 1]);
                Check(hairs > 150 && frames.All(f => !f.SequenceEqual(frame)), $"Preset {preset.SaveName} must have visible Julia points and a distinct picture; got {hairs}.");
                frames.Add(frame);
                SaveTranscendentalPng(Path.Combine(directory, $"{kind}-{frames.Count}.png"), frame, 256, 192);
                if (kind == TranscendentalJuliaKind.Sine && preset.LambdaReal == 0)
                    Check(renderer.Attractors.Any(c => c.Points.Length == 4), "The existing sine preset must recover its period-four cycle.");
            }

            var reference = new byte[160 * 120 * 4];
            new TranscendentalJuliaRenderer(state).Render(reference, 160, 120, 160 * 4, 1, CancellationToken.None);
            var threaded = new byte[reference.Length];
            new TranscendentalJuliaRenderer(state).Render(threaded, 160, 120, 160 * 4, Environment.ProcessorCount, CancellationToken.None);
            Check(reference.SequenceEqual(threaded), "Inverse sampling and orbital pixels must be deterministic across CPU thread counts.");
            var tiled = new TranscendentalJuliaRenderer(state); tiled.Prepare(160, 120, CancellationToken.None);
            var assembled = new byte[reference.Length];
            foreach (var tile in MandelbrotTileScheduler.Create(160, 120, 16, TileSchedulingStrategy.Spiral))
            {
                byte[] pixels = tiled.RenderTile(tile, CancellationToken.None);
                for (int y = 0; y < tile.Height; y++) Buffer.BlockCopy(pixels, y * tile.Width * 4, assembled,
                    ((tile.Y + y) * 160 + tile.X) * 4, tile.Width * 4);
            }
            Check(reference.SequenceEqual(assembled), "Progressive tiles must match saved previews pixel for pixel.");
            var juliaOnly = state.Clone(); juliaOnly.Layer = TranscendentalJuliaLayer.Julia;
            var orbitsOnly = state.Clone(); orbitsOnly.Layer = TranscendentalJuliaLayer.Orbits;
            byte[] JuliaFrame(TranscendentalJuliaState s) { var f = new byte[reference.Length]; new TranscendentalJuliaRenderer(s).Render(f, 160, 120, 640, 1, CancellationToken.None); return f; }
            Check(!JuliaFrame(juliaOnly).SequenceEqual(reference) && !JuliaFrame(orbitsOnly).SequenceEqual(reference), "All three display layers must differ.");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); bool caught = false;
                try { tiled.Prepare(160, 120, cancelled.Token); } catch (OperationCanceledException) { caught = true; }
                Check(caught, "Preparation must honor cancellation.");
            }
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.CancelAfter(5); bool caught = false;
                var expensive = state.Clone(); expensive.PointBudget = 10_000_000; expensive.InverseDepth = 80;
                try { new TranscendentalJuliaRenderer(expensive).Prepare(512, 512, cancelled.Token); } catch (OperationCanceledException) { caught = true; }
                Check(caught, "Inverse sampling must be cancellable while running.");
            }

            state.SaveName = "Round trip " + kind; state.Timestamp = new(2026, 10, 14, 16, 0, 0);
            state.CenterX += .12345678901234567; state.CenterY -= .23456789012345678;
            state.LambdaReal += .01234567890123456; state.Zoom = 1.1234567890123457; state.SamplingSeed = 314;
            var clone = state.Clone(); clone.Palette.RootColors[0] = Colors.Magenta;
            Check(clone.Palette.RootColors[0] != state.Palette.RootColors[0], "Saved palettes must be cloned, not shared.");
            var store = new TranscendentalJuliaSaveStore(kind);
            var slot = store.Save(state);
            var loaded = store.LoadSlots().Slots.Single(s => s.FilePath == slot.FilePath).State;
            Check(JsonSerializer.Serialize(state, JsonOptionsFactory.Create()) == JsonSerializer.Serialize(loaded, JsonOptionsFactory.Create()),
                "Disk saves must preserve all parameters, inverse sampling and palette fields.");

            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml") });
            ThemeManager.Initialize(Application.Current);
            var window = new TranscendentalJuliaWindow(kind);
            try
            {
                window.LoadState(loaded);
                var captured = window.CaptureState(loaded.SaveName);
                captured.Timestamp = loaded.Timestamp;
                Check(JsonSerializer.Serialize(captured, JsonOptionsFactory.Create()) == JsonSerializer.Serialize(loaded, JsonOptionsFactory.Create()),
                    "LoadState/CaptureState must preserve the full view and numerical settings.");
                var configuration = SaveManagerConfigurations.ForTranscendentalJulia(window, store);
                Check(configuration.PointsOfInterest.Count == 3 && configuration.GetDetails(loaded).Contains("λ"), "Save manager must expose presets and meaningful details.");
                var preview = await window.RenderStatePreviewAsync(loaded, 96, 72, CancellationToken.None);
                Check(preview.IsFrozen && preview.PixelWidth == 96, "Preview must return a frozen CPU bitmap.");
                var supersampled = await TranscendentalJuliaRenderer.RenderStateAsync(loaded, 96, 72, CancellationToken.None, ssaa: 2);
                Check(supersampled.PixelWidth == 96 && supersampled.PixelHeight == 72, "SSAA export must retain requested dimensions.");
                ((TextBox)window.FindName("LambdaRealBox")).Text = "0.27";
                Check(window.CaptureState("edited").LambdaReal == .27, "Editable lambda must reach the renderer state.");
                ((ComboBox)window.FindName("KindBox")).SelectedIndex = kind == TranscendentalJuliaKind.Sine ? 0 : 1;
                Check(window.Kind != kind, "Formula switching must update kind, title and defaults.");
                bool mismatchedStore = false;
                try { configuration.CaptureState("wrong category"); } catch (InvalidOperationException) { mismatchedStore = true; }
                Check(mismatchedStore, "An already open save manager must not write another formula into its original category.");

                // Не показывая окно: отменённая подготовка не должна публиковать старый кадр после загрузки.
                var content = (FrameworkElement)window.Content; content.Measure(new(1100, 740)); content.Arrange(new Rect(0, 0, 1100, 740));
                var render = typeof(TranscendentalJuliaWindow).GetMethod("RenderPreviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var pending = (Task)render.Invoke(window, null)!;
                window.LoadState(loaded);
                await pending;
                Check(((Image)window.FindName("StablePreviewImage")).Source is null, "A cancelled frame must not replace the restored view.");
            }
            finally { window.Close(); }
        }
        var constant = TranscendentalJuliaCatalog.CreateDefault(TranscendentalJuliaKind.Exponential);
        constant.LambdaReal = 0;
        var zero = new TranscendentalJuliaDynamics(constant);
        Check(zero.FindRepellingSeeds(CancellationToken.None).Count == 0 && double.IsNaN(zero.Preimage(Complex.One, 0).Real) &&
            zero.Classify(new(1e200, 0), zero.FindAttractors(CancellationToken.None)).Outcome == TranscendentalOrbitOutcome.Converged,
            "Lambda zero must behave as a constant map with no inverse Julia layer.");
        Console.WriteLine($"PASS (transcendental-julia): inverse branches, repellers, singular orbits, return from large modulus, layers, presets, deterministic tiles, cancellation, saves, UI and SSAA. PNG: {directory}");
    }

    private static void SaveTranscendentalPng(string path, byte[] pixels, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
