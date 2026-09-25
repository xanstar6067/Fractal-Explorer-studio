using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static void VerifySprott(string[] args)
    {
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        Check(FractalCatalog.Create().Any(item => item.LaunchKey == "SprottQuadratic"),
            "Sprott generator should have a catalog tile.");
        for (int i = 0; i < SprottQuadraticMap.Presets.Length; i++)
        {
            (string name, string code) = SprottQuadraticMap.Presets[i];
            DynamicSystemState state = DynamicSystemState.CreateDefault(DynamicSystemKind.Attractors2D);
            SprottQuadraticMap.Analysis view = SprottQuadraticMap.ApplyCode(state, code);
            Check(SprottQuadraticMap.Encode(state.QuadraticCoefficients) == code && view.Lyapunov > .03,
                $"{name}: code should round-trip and the orbit should be chaotic.");
            state.Iterations = 150_000;
            byte[] pixels = Attractor2DRenderer.RenderBuffer(state, 256, 256, null, CancellationToken.None);
            int lit = 0;
            for (int p = 0; p < pixels.Length; p += 4)
                if (pixels[p] != state.BackgroundColor.B || pixels[p + 1] != state.BackgroundColor.G ||
                    pixels[p + 2] != state.BackgroundColor.R) lit++;
            Check(lit > 150, $"{name}: rendered density should contain a visible shape (got {lit} pixels).");
            string json = JsonSerializer.Serialize(state);
            DynamicSystemState restored = JsonSerializer.Deserialize<DynamicSystemState>(json)!;
            Check(restored.QuadraticCoefficients.SequenceEqual(state.QuadraticCoefficients) &&
                  restored.QuadraticSpan == state.QuadraticSpan, $"{name}: save should preserve the map and framing.");
            if (output is not null)
            {
                var bitmap = BitmapSource.Create(256, 256, 96, 96, System.Windows.Media.PixelFormats.Bgra32,
                    null, pixels, 256 * 4);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using FileStream file = File.Create(Path.Combine(output, $"sprott-{i:D2}.png"));
                encoder.Save(file);
            }
        }
        SprottQuadraticMap.SearchResult found = SprottQuadraticMap.Search(CancellationToken.None);
        Check(found.View.Lyapunov > .06 && SprottQuadraticMap.Encode(SprottQuadraticMap.Decode(found.Code)) == found.Code,
            "Random search should find a reproducible chaotic map.");
        if (output is not null)
        {
            DynamicSystemState state = DynamicSystemState.CreateDefault(DynamicSystemKind.Attractors2D);
            SprottQuadraticMap.ApplyCode(state, found.Code);
            state.Iterations = 250_000;
            byte[] pixels = Attractor2DRenderer.RenderBuffer(state, 256, 256, null, CancellationToken.None);
            var bitmap = BitmapSource.Create(256, 256, 96, 96, System.Windows.Media.PixelFormats.Bgra32,
                null, pixels, 256 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream file = File.Create(Path.Combine(output, "sprott-found.png"));
            encoder.Save(file);
        }
        var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == themeStyles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });
        var window = new DynamicSystemWindow(DynamicSystemKind.Attractors2D, Attractor2DKind.SprottQuadratic);
        try { Check(window.Title == "Генератор аттракторов Спротта", "Catalog launch should open the generator directly."); }
        finally { window.Close(); }
        Console.WriteLine($"PASS (sprott): {SprottQuadraticMap.Presets.Length} chaotic presets render and round-trip; search found {found.Code} in {found.Attempts} attempts.");
    }
}
