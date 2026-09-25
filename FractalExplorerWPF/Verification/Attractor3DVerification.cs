using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyAttractors3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("attractors3d");
        FractalCatalogItem catalogItem = FractalCatalog.Create().Single(item =>
            item.LaunchKey == Fractal3DCatalog.LaunchKey(Fractal3DKind.StrangeAttractor));
        Check(catalogItem.IsThreeDimensional && catalogItem.CategoryPath.SequenceEqual(
            new[] { "Динамические системы и хаос", "Аттракторы" }),
            "The attractor tile should appear in the dynamics section and the shared 3D menu.");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        using var renderer = new Fractal3DRenderer();
        var signatures = new HashSet<string>();
        IReadOnlyList<Fractal3DState> presets = Fractal3DCatalog.GetPresets(Fractal3DKind.StrangeAttractor);
        for (int index = 0; index < presets.Count; index++)
        {
            Fractal3DState state = presets[index];
            Attractor3DSystem system = state.Attractor.System;
            BitmapSource bitmap = await renderer.RenderAsync(state, 280, 280, null, CancellationToken.None);
            byte[] pixels = new byte[280 * 280 * 4];
            bitmap.CopyPixels(pixels, 280 * 4, 0);
            Check(HasFractal3DStructure(pixels, 280, 280), $"{state.SaveName}: density frame is empty.");
            signatures.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
            if (output is not null)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using FileStream file = File.Create(Path.Combine(output, $"attractor-{index:D2}-{system}.png"));
                encoder.Save(file);
            }
            Fractal3DState restored = JsonSerializer.Deserialize<Fractal3DState>(JsonSerializer.Serialize(state))!;
            Check(restored.Attractor.System == system && restored.Attractor.TimeStep == state.Attractor.TimeStep,
                $"{system}: settings did not survive serialization.");
        }
        Check(signatures.Count == presets.Count && presets.Select(preset => preset.Attractor.System).Distinct().Count() == 6,
            "The seven shipped views should be distinct and include all six systems.");
        var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == themeStyles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });
        var window = new Fractal3DWindow(Fractal3DKind.StrangeAttractor);
        try
        {
            Fractal3DState source = Fractal3DCatalog.GetPresets(Fractal3DKind.StrangeAttractor)[4];
            window.LoadState(source);
            Fractal3DState captured = window.CaptureState("UI");
            Check(captured.Attractor.System == source.Attractor.System &&
                  captured.Attractor.A == source.Attractor.A && captured.Attractor.TimeStep == source.Attractor.TimeStep,
                "Attractor settings should survive a WPF window round trip.");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS (attractors): six systems render distinctly; settings round-trip through JSON and the WPF window.");
    }
}
