using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Migrations;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyLegacyJuliaSavesAsync()
    {
        using var sandbox = DataSandbox.Create("legacy-julia");
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        foreach (var (variant, real, imaginary) in new[]
                 { (MandelbrotVariant.Julia, -0.748724460601806m, 0.0599092282354832m),
                   (MandelbrotVariant.JuliaBurningShip, -1.7551867961883m, 0.01068m) })
        {
            var store = new MandelbrotSaveStore(variant);
            Directory.CreateDirectory(store.DirectoryPath);
            var legacy = new JsonObject { ["SaveFormatVersion"] = 1, ["SaveName"] = "Legacy",
                ["CRe"] = real, ["CIm"] = imaginary, ["CenterX"] = 0, ["CenterY"] = 0,
                ["Zoom"] = 1, ["Iterations"] = 1500, ["Threshold"] = 2, ["ColoringMode"] = 1,
                ["PaletteName"] = "Огонь", ["Timestamp"] = "2026-03-30T12:46:00+03:00" };
            string path = Path.Combine(store.DirectoryPath,"legacy.json"), original = legacy.ToJsonString();
            File.WriteAllText(path, original);
            var state = store.Load().Single();
            Check(state.JuliaCReal == real && state.JuliaCImaginary == imaginary,
                "Legacy top-level CRe/CIm must restore the exact Julia constant in both variants.");
            Check(File.ReadAllText(path) == original, "Reading an old save must not modify the user's JSON.");
            var window = new MandelbrotWindow(variant);
            try
            {
                window.LoadState(state);
                var loaded = window.CaptureState("test");
                Check(loaded.JuliaCReal == real && loaded.JuliaCImaginary == imaginary,
                    "Loading the old save into WPF must keep its C, not the default or zero.");
                Check(loaded.Palette.Colors.Count > 2 && loaded.Palette.Name == "Огонь",
                    "Legacy palette names must be resolved during load.");
                const int width = 160, height = 120;
                BitmapSource preview = await window.RenderStatePreviewAsync(state, width, height, CancellationToken.None);
                var pixels = new byte[width*height*4]; preview.CopyPixels(pixels,width*4,0);
                var reference = new byte[pixels.Length];
                await Task.Run(() => MandelbrotFamilyRenderer.Render(loaded,reference,width,height,width*4,CancellationToken.None));
                Check(pixels.SequenceEqual(reference), "Old-save previews must match loaded-view C and palette exactly.");
                int colored = 0;
                for (int i=0; i<pixels.Length; i+=4) if (pixels[i+2] > 0 || pixels[i+1] > 0 || pixels[i] > 0) colored++;
                Check(colored > width*height/100, "The restored legacy save must render visible structure.");
                using var gpu = new MandelbrotPreviewRenderer();
                var gpuPixels = new byte[pixels.Length];
                Check(await Task.Run(() => gpu.TryRender(loaded,gpuPixels,width,height,CancellationToken.None)),
                    $"The restored C must render on the main GPU path: {gpu.FailureReason}");
                int visible = 0;
                for (int i=0; i<gpuPixels.Length; i+=4) if (gpuPixels[i+2] > 0 || gpuPixels[i+1] > 0 || gpuPixels[i] > 0) visible++;
                Check(visible > width*height/100, "Restored GPU Julia must not be a black screen.");
                Console.WriteLine($"{variant}: C={real}+{imaginary}i, CPU visible pixels {colored}, GPU {visible}.");
            }
            finally { window.Close(); }
            // ImportLegacyExeFolder stamps current-version JSON while retaining the old field names.
            legacy["SaveFormatVersion"] = SaveFormat.CurrentVersion;
            File.WriteAllText(path,legacy.ToJsonString());
            Check(store.Load().Single().JuliaCReal == real, "Fresh legacy imports marked current must also restore C.");
            legacy["JuliaCReal"] = 0; legacy["JuliaCImaginary"] = 0;
            File.WriteAllText(path,legacy.ToJsonString());
            var modernZero = store.Load().Single();
            Check(modernZero.JuliaCReal == 0 && modernZero.JuliaCImaginary == 0,
                "Explicit modern C=0 must have priority over conflicting old aliases.");
            legacy.Remove("JuliaCReal"); legacy.Remove("JuliaCImaginary");
            File.WriteAllText(path,legacy.ToJsonString());
            var restored = store.LoadSlots().Slots.Single();
            store.Save(restored.State,restored);
            JsonObject rewritten = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Check(SaveFormat.ReadVersion(rewritten) == SaveFormat.CurrentVersion &&
                  rewritten["JuliaCReal"]!.GetValue<decimal>() == real &&
                  rewritten["JuliaCImaginary"]!.GetValue<decimal>() == imaginary,
                "Explicit re-save must persist the migrated C in the current format.");
        }
        var unrelated = new JsonObject { ["CRe"] = 123, ["CIm"] = 456 };
        SaveFormat.UpgradeInPlace("Mandelbrot", unrelated);
        Check(!unrelated.ContainsKey("JuliaCReal"), "Julia alias conversion must not affect other fractals.");
        Console.WriteLine("PASS (julia-saves): exact legacy C, palette parity, visible CPU/GPU frames, stamped imports, explicit zero, unchanged source and round-trip.");
    }
}
