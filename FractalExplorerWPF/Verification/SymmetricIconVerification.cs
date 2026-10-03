using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifySymmetricIconsAsync(string[] args)
    {
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        Check(FractalCatalog.Create().Any(item => item.LaunchKey == "SymmetricIcon"), "Icons need a catalog tile.");
        var palettes = DynamicPaletteStore.IconBuiltIns();
        for (int i = 0; i < SymmetricIconPresets.All.Count; i++)
        {
            var state = DynamicSystemState.CreateDefault(DynamicSystemKind.Attractors2D);
            SymmetricIconPresets.Apply(state, i);
            state.Iterations = 120_000;
            var analysis = SymmetricIconMap.Analyze(state.SymmetricIcon, CancellationToken.None)
                ?? throw new InvalidOperationException($"Preset {i} is unbounded or degenerate.");
            Console.WriteLine($"Icon {i}: r={analysis.Radius:F3}, λ={analysis.Lyapunov:F3}, cells={analysis.OccupiedCells}");
            Check(analysis.OccupiedCells > 50 && analysis.Radius * 2 < state.SymmetricIcon.Span,
                $"Preset {i} must contain a detailed orbit inside its frame.");
            var palette = palettes.First(p => p.Name == state.PaletteName);
            byte[] pixels = Attractor2DRenderer.RenderBuffer(state, 320, 320, palette, CancellationToken.None);
            Check(IconLitPixels(pixels) > 1_000, $"Preset {i} must be visible.");
            if (state.SymmetricIcon.Mirror)
            {
                int differences = 0;
                for (int y = 0; y < 320; y++) for (int x = 0; x < 320; x++)
                    if (!pixels.AsSpan((y * 320 + x) * 4, 4).SequenceEqual(pixels.AsSpan(((319 - y) * 320 + x) * 4, 4))) differences++;
                Check(differences < 320, $"Preset {i}: mirror symmetry differs at {differences} pixels.");
            }
            string json = JsonSerializer.Serialize(state, JsonOptionsFactory.Create());
            var restored = JsonSerializer.Deserialize<DynamicSystemState>(json, JsonOptionsFactory.Create())!;
            Check(JsonSerializer.Serialize(restored, JsonOptionsFactory.Create()) == json, "All icon parameters must round-trip.");
            state.Threads = 1;
            Check(Attractor2DRenderer.RenderBuffer(state, 320, 320, palette, CancellationToken.None).SequenceEqual(pixels),
                "Thread count must not change the picture.");
            var clone = state.Clone(); clone.SymmetricIcon.Lambda += .1;
            Check(clone.SymmetricIcon.Lambda != state.SymmetricIcon.Lambda, "Snapshots must own their settings.");
            VerifyIconFormula(state.SymmetricIcon);
            if (output is not null) SaveIconPng(pixels, 320, 320, Path.Combine(output, $"icon-{i:D2}.png"));
        }
        var basis = SymmetricIconPresets.All[0].Settings.Clone();
        var found = SymmetricIconMap.Search(basis, 42, CancellationToken.None);
        var repeated = SymmetricIconMap.Search(basis, 42, CancellationToken.None);
        Check(JsonSerializer.Serialize(found.Settings) == JsonSerializer.Serialize(repeated.Settings) && found.Analysis.Lyapunov > .015,
            "Seeded search must reproduce a bounded chaotic icon.");
        var generated = DynamicSystemState.CreateDefault(DynamicSystemKind.Attractors2D);
        generated.ApplyAttractor2DPreset(Attractor2DKind.SymmetricIcon);
        generated.SymmetricIcon = found.Settings; generated.Iterations = 150_000;
        byte[] generatedPixels = Attractor2DRenderer.RenderBuffer(generated, 320, 320, palettes[0], CancellationToken.None);
        Check(IconLitPixels(generatedPixels) > 1_000, "Generated icon must be visible.");
        if (output is not null) SaveIconPng(generatedPixels, 320, 320, Path.Combine(output, "icon-generated.png"));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        bool renderCanceled = false, searchCanceled = false;
        try { Attractor2DRenderer.RenderBuffer(generated, 64, 64, null, canceled.Token); } catch (OperationCanceledException) { renderCanceled = true; }
        try { SymmetricIconMap.Search(basis, 1, canceled.Token); } catch (OperationCanceledException) { searchCanceled = true; }
        Check(renderCanceled && searchCanceled, "Rendering and search must honor cancellation.");
        var invalid = basis.Clone(); invalid.Degree = 2;
        bool rejected = false;
        try { invalid.Validate(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Invalid symmetry degree must be rejected.");
        var legacy = JsonSerializer.Deserialize<DynamicSystemState>("{\"Kind\":7,\"Attractor2DMode\":\"Clifford\"}")!;
        Check(legacy.Clone().SymmetricIcon is not null && legacy.Attractor2DMode == "Clifford", "Old saves must still load.");
        await VerifyIconWindowAsync(generated, generatedPixels, output);
        Console.WriteLine($"PASS (symmetric-icons): {SymmetricIconPresets.All.Count} presets, symmetry, deterministic search/rendering, palettes, JSON, cancellation and WPF controls.");
    }

    private static async Task VerifyIconWindowAsync(DynamicSystemState generated, byte[] pixels, string? output)
    {
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        var window = new DynamicSystemWindow(DynamicSystemKind.Attractors2D, Attractor2DKind.SymmetricIcon);
        try
        {
            Check(window.Title.Contains("Symmetric Icons"), "Tile must open Icons directly.");
            var panel = IconField<StackPanel>(window, "_iconPanel");
            Check(panel.Visibility == Visibility.Visible, "Icon controls must be visible.");
            IconField<ComboBox>(window, "_iconDegreeBox").SelectedItem = 9;
            IconField<Dictionary<string, TextBox>>(window, "_iconBoxes")["Rotation"].Text = "23";
            var captured = (DynamicSystemState)IconInvoke(window, "CaptureState", "test")!;
            Check(captured.SymmetricIcon.Degree == 9 && captured.SymmetricIcon.Rotation == 23, "UI edits must reach the saved state.");
            var choices = IconField<Dictionary<string, ComboBox>>(window, "_choices");
            choices["Attractor2DMode"].SelectedItem = choices["Attractor2DMode"].Items.Cast<object>()
                .First(p => p.GetType().GetProperty("Value")!.GetValue(p)?.ToString() == "Clifford");
            Check(panel.Visibility == Visibility.Collapsed, "Other formulas must hide the icon controls.");
            Check(typeof(DynamicSystemWindow).GetProperty("ActivePalette", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window) is null,
                "Existing 2D attractors must retain their coloring.");
            IconInvoke(window, "LoadState", generated);
            Check(panel.Visibility == Visibility.Visible, "Loading an icon must restore its UI.");
            var loaded = (DynamicSystemState)IconInvoke(window, "CaptureState", "loaded")!;
            Check(JsonSerializer.Serialize(loaded.SymmetricIcon) == JsonSerializer.Serialize(generated.SymmetricIcon),
                "Loading and recapturing a chaotic map must preserve every coefficient bit.");
            IconInvoke(window, "StartIconSearch", true);
            Check(IconField<CancellationTokenSource?>(window, "_iconSearchCts") is not null, "Search must start asynchronously.");
            IconInvoke(window, "LoadState", generated);
            for (int i = 0; i < 200 && IconField<CancellationTokenSource?>(window, "_iconSearchCts") is not null; i++)
                await Task.Delay(10);
            Check(IconField<CancellationTokenSource?>(window, "_iconSearchCts") is null &&
                IconField<DynamicSystemState>(window, "_state").SymmetricIcon.Lambda == generated.SymmetricIcon.Lambda,
                "Canceled search must not overwrite a loaded save.");
            IconInvoke(window, "StartIconSearch", true);
            for (int i = 0; i < 200 && IconField<CancellationTokenSource?>(window, "_iconSearchCts") is not null; i++)
                await Task.Delay(10);
            Check(IconField<Button>(window, "_iconUndoButton").IsEnabled, "A generated variation must support undo.");
            IconField<Button>(window, "_iconUndoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(IconField<DynamicSystemState>(window, "_state").SymmetricIcon.Lambda == generated.SymmetricIcon.Lambda,
                "Undo must restore the previous coefficients.");
            if (output is not null)
            {
                var stable = (Image)window.FindName("StableImage");
                stable.Source = BitmapSource.Create(320, 320, 96, 96, PixelFormats.Bgra32, null, pixels, 1280);
                var root = (FrameworkElement)window.Content;
                window.Content = null;
                root.SetValue(TextElement.FontFamilyProperty, window.FontFamily);
                root.Measure(new Size(1120, 700)); root.Arrange(new Rect(0, 0, 1120, 700)); root.UpdateLayout();
                Check(root.ActualWidth > 1000 && panel.ActualHeight > 100, "Window layout must contain the controls and canvas.");
                int width = Math.Max(1, (int)stable.ActualWidth), height = Math.Max(1, (int)stable.ActualHeight);
                var palette = DynamicPaletteStore.IconBuiltIns().First(p => p.Name == generated.PaletteName);
                byte[] rectangular = Attractor2DRenderer.RenderBuffer(generated, width, height, palette, CancellationToken.None);
                stable.Source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, rectangular, width * 4);
                root.UpdateLayout();
                await DrainAsync();
                var bitmap = new RenderTargetBitmap(1120, 700, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(output, "icon-window.png")); encoder.Save(stream);
            }
        }
        finally { window.Close(); }

    }

    private static T IconField<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static object? IconInvoke(object instance, string name, params object[] arguments) => instance.GetType()
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, arguments);
    private static int IconLitPixels(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4) if (pixels[i] + pixels[i + 1] + pixels[i + 2] > 0) count++;
        return count;
    }
    private static void VerifyIconFormula(SymmetricIconSettings settings)
    {
        var z = new Complex(.23, .14);
        Complex reference = (settings.Lambda + settings.Alpha * z.Magnitude * z.Magnitude + settings.Beta * Complex.Pow(z, settings.Degree).Real +
            Complex.ImaginaryOne * (settings.Mirror ? 0 : settings.Omega)) * z + settings.Gamma * Complex.Pow(Complex.Conjugate(z), settings.Degree - 1);
        double x = z.Real, y = z.Imaginary;
        SymmetricIconMap.Iterate(settings, ref x, ref y);
        Check(Complex.Abs(new Complex(x, y) - reference) < 1e-12, "Map must match the independent complex formula.");
        Complex turn = Complex.FromPolarCoordinates(1, Math.Tau / settings.Degree), turned = z * turn;
        double tx = turned.Real, ty = turned.Imaginary;
        SymmetricIconMap.Iterate(settings, ref tx, ref ty);
        Check(Complex.Abs(new Complex(tx, ty) - reference * turn) < 1e-12, "Map must commute with the symmetry rotation.");
    }
    private static void SaveIconPng(byte[] pixels, int width, int height, string path)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
