using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Migrations;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyLegacyPhoenixSavesAsync()
    {
        using var sandbox = DataSandbox.Create("legacy-phoenix");
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        var custom = new MandelbrotPalette
        {
            Name = "Огонь и лед (копия) 2", Colors = [Colors.Red, Colors.Cyan, Colors.Yellow],
            ColorPeriod = 23, Gamma = 0.8, IsGradient = false
        };
        var library = new MandelbrotPaletteManager();
        library.Palettes.Add(custom);
        library.SaveCustomPalettes();
        var store = new PhoenixSaveStore();
        Directory.CreateDirectory(store.DirectoryPath);
        string path = Path.Combine(store.DirectoryPath, "legacy.json");
        foreach (var (p, q, paletteName) in new[]
                 { (0.56m, -0.5m, custom.Name), (-1.76m, 0m, "Зеленый"),
                   (0.56667m, -0.5m, "Стандартный серый"), (0.56m, -0.5m, "Удалённая палитра") })
        {
            string expectedPalette = paletteName == "Удалённая палитра" ? "Стандартный серый" : paletteName;
            var legacy = new JsonObject
            {
                ["SaveName"] = "22 градиент вкл", ["CenterX"] = 0.24321430897485996m,
                ["CenterY"] = -0.18701956673654788m, ["Zoom"] = 1.5, ["Threshold"] = 4,
                ["Iterations"] = 400, ["C1Re"] = p, ["C1Im"] = q,
                // C2 was reserved in the old engine and must not affect its formula.
                ["C2Re"] = 7, ["C2Im"] = 8, ["PaletteName"] = paletteName,
                ["PreviewParametersJson"] = "{\"C1Re\":999,\"Iterations\":75}"
            };
            foreach (int version in new[] { 1, SaveFormat.CurrentVersion - 1, SaveFormat.CurrentVersion })
            {
                legacy["SaveFormatVersion"] = version;
                string original = legacy.ToJsonString();
                File.WriteAllText(path, original);
                PhoenixState state = store.Load().Single();
                Check(state.C1Real == p && state.C1Imaginary == 0 && state.C2Real == q && state.C2Imaginary == 0,
                    "Legacy Phoenix must restore z² + P + Q*z(previous), including Q=0 and stamped imports.");
                Check(state.Iterations == 400 && File.ReadAllText(path) == original,
                    "Legacy preview parameters must not replace the full state; loading must not rewrite the file.");
                var window = new PhoenixWindow();
                try
                {
                    window.LoadState(state);
                    PhoenixState loaded = window.CaptureState("loaded");
                    Check(loaded.C1Real == p && loaded.C1Imaginary == 0 && loaded.C2Real == q && loaded.C2Imaginary == 0,
                        "Window load must keep the migrated real coefficients.");
                    Check(loaded.Palette.Name == expectedPalette,
                        "Legacy palettes must resolve by name; missing palettes must use visible algorithmic grayscale.");
                    if (paletteName == custom.Name)
                        Check(loaded.Palette.Colors.SequenceEqual(custom.Colors) && !loaded.Palette.IsGradient &&
                              loaded.Palette.ColorPeriod == custom.ColorPeriod && loaded.Palette.Gamma == custom.Gamma,
                            "Custom palette colors and coloring settings must survive load.");
                    const int width = 96, height = 72;
                    var preview = await window.RenderStatePreviewAsync(state, width, height, CancellationToken.None);
                    var pixels = new byte[width * height * 4];
                    preview.CopyPixels(pixels, width * 4, 0);
                    var reference = new byte[pixels.Length];
                    PhoenixRenderer.Render(loaded, reference, width, height, width * 4, 1, CancellationToken.None);
                    Check(pixels.SequenceEqual(reference), "Save preview and loaded Phoenix must render identical pixels.");
                    Check(Enumerable.Range(0, width * height).Count(i =>
                            pixels[i * 4] + pixels[i * 4 + 1] + pixels[i * 4 + 2] > 30) > width * height / 100,
                        "Old Phoenix saves must show visible structure, not a nearly black rectangle.");
                    var slot = store.LoadSlots().Slots.Single();
                    store.Save(loaded, slot);
                    PhoenixState roundTrip = store.Load().Single();
                    Check(roundTrip.C1Real == p && roundTrip.C2Real == q && roundTrip.Palette.Name == expectedPalette,
                        "Explicit re-save must persist migrated coefficients and the complete palette.");
                }
                finally { window.Close(); }
            }
            // Modern properties take precedence independently, including explicit zeros.
            legacy["C1Real"] = 0; legacy["C1Imaginary"] = 0.125m;
            legacy["C2Real"] = 0; legacy["C2Imaginary"] = -0.25m;
            File.WriteAllText(path, legacy.ToJsonString());
            PhoenixState modern = store.Load().Single();
            Check(modern.C1Real == 0 && modern.C1Imaginary == 0.125m &&
                  modern.C2Real == 0 && modern.C2Imaginary == -0.25m,
                "Modern complex constants must win over conflicting legacy coefficients.");
        }
        var unrelated = new JsonObject { ["C1Re"] = 1, ["C1Im"] = 2 };
        SaveFormat.UpgradeInPlace("Fractal3DPhoenix", unrelated);
        Check(!unrelated.ContainsKey("C1Real"), "Legacy Phoenix conversion must not affect other categories.");
        Console.WriteLine("PASS (phoenix-saves): legacy P/Q semantics, built-in/custom palettes, preview/load parity, stamped imports, modern precedence and round-trip.");
    }
}
