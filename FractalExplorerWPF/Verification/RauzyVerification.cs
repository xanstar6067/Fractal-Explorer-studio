using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.RauzyMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyRauzyAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("rauzy");
        string? output = ReadOption(args, "--out");
        if (output is not null) Directory.CreateDirectory(output);
        VerifyCatalogData();
        Check(RauzyFractal.GenerateWord(7, 1).SequenceEqual(new byte[] { 0, 1, 0, 2, 0, 1, 0 }),
            "Tribonacci starts abacaba.");
        var watch = Stopwatch.StartNew();
        for (int k = 1; k <= 4; k++)
        {
            var p = RauzyFractal.CreateProjection(k);
            Check(Complex.Abs(Complex.Pow(p.Alpha, 3) - k * p.Alpha * p.Alpha - p.Alpha - 1) < 1e-12 && p.Alpha.Magnitude < 1,
                "Projection must use a contracting root of the substitution polynomial.");
            Check(Complex.Abs(k * p.A + p.B - p.Alpha * p.A) < 1e-12 &&
                  Complex.Abs(p.A + p.C - p.Alpha * p.B) < 1e-12 && Complex.Abs(p.A - p.Alpha * p.C) < 1e-12,
                "Projected substitution must commute with multiplication by alpha.");
            Check(Math.Abs(p.PeriodU.Real * p.PeriodV.Imaginary - p.PeriodV.Real * p.PeriodU.Imaginary) > 0.1,
                "Mosaic periods must span a two dimensional lattice.");
            byte[] word = RauzyFractal.GenerateWord(20_000, k);
            byte[] expanded = word.SelectMany(letter => letter switch
            {
                0 => Enumerable.Repeat((byte)0, k).Append((byte)1),
                1 => new byte[] { 0, 2 },
                _ => new byte[] { 0 }
            }).Take(word.Length).ToArray();
            Check(word.SequenceEqual(expanded), "Generated word must be a fixed point of the substitution.");
            var samples = RauzyFractal.Generate(word.Length, k);
            Check(samples.Take(137).SequenceEqual(RauzyFractal.Generate(137, k)), "Point budgets must preserve the same prefix.");
            Complex sum = Complex.Zero;
            Complex[] steps = [p.A, p.B, p.C];
            for (int i = 0; i < word.Length; i++)
            {
                Check(samples[i].Letter == word[i] && Complex.Abs(samples[i].Position - sum) < 1e-8,
                    "Points are projected prefixes coloured by the next letter.");
                Check(samples[i].Position.Magnitude < 30, "The expanding component must not leak into the projected prefix.");
                sum += steps[word[i]];
            }
            if (k == 1)
            {
                Check(Math.Abs(p.B.Real + 1.41964337760708) < 1e-12 && Math.Abs(p.B.Imaginary + 0.606290729207199) < 1e-12 &&
                      Math.Abs(p.C.Real + 0.771844506346038) < 1e-12 && Math.Abs(p.C.Imaginary - 1.11514250803994) < 1e-12,
                    "Classical projection must match the independent SageMath reference.");
                double[] frequencies = [1 / p.Beta, 1 / (p.Beta * p.Beta), 1 / Math.Pow(p.Beta, 3)];
                for (int letter = 0; letter < 3; letter++)
                    Check(Math.Abs(word.Count(value => value == letter) / (double)word.Length - frequencies[letter]) < 0.001,
                        "The three subtiles must have Tribonacci letter frequencies.");
            }
            for (int mode = 0; mode < 6; mode++)
            {
                var state = MathematicalLaboratoryCatalog.CreateDefaultState(MathematicalLaboratoryKind.Rauzy);
                state.SecondaryValue = k; state.Mode = mode;
                BitmapSource bitmap = await MathematicalLaboratoryRenderer.RenderBitmapAsync(state, 512, 512, CancellationToken.None);
                byte[] frame = RauzyPixels(bitmap);
                int lit = Enumerable.Range(0, 512 * 512).Count(i => frame[i * 4] != state.BackgroundColor.B ||
                    frame[i * 4 + 1] != state.BackgroundColor.G || frame[i * 4 + 2] != state.BackgroundColor.R);
                Check(bitmap.IsFrozen && lit > 100, $"Rauzy k={k}, mode={mode} must produce a visible frozen frame.");
                if (output is not null)
                {
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"rauzy-k{k}-mode{mode}.png")); encoder.Save(file);
                }
                if (k == 1 && mode == 0)
                {
                    var changed = state.Clone(); changed.Filled = false;
                    byte[] outline = RauzyPixels(await MathematicalLaboratoryRenderer.RenderBitmapAsync(changed, 512, 512, CancellationToken.None));
                    Check(!frame.SequenceEqual(outline),
                        "Contour mode must differ from filled tiles.");
                    changed = state.Clone(); changed.Animate = true; changed.Phase = 0;
                    byte[] animated = RauzyPixels(await MathematicalLaboratoryRenderer.RenderBitmapAsync(changed, 512, 512, CancellationToken.None));
                    Check(!frame.SequenceEqual(animated),
                        "Animation must reveal only a prefix.");
                }
            }
        }
        var saved = MathematicalLaboratoryCatalog.CreateDefaultState(MathematicalLaboratoryKind.Rauzy);
        saved.Mode = 4; saved.SecondaryValue = 3; saved.TertiaryValue = 2;
        saved.Zoom = 1.7; saved.Rotation = 31; saved.ViewCenterX = 0.12; saved.PrimaryColor = Colors.Lime;
        saved.SaveName = "Rauzy round-trip"; saved.Animate = true; saved.Phase = 0.37;
        var store = new MathematicalLaboratorySaveStore(MathematicalLaboratoryKind.Rauzy);
        store.Save(saved);
        var restored = store.Load().Single();
        byte[] originalFrame = RauzyPixels(await MathematicalLaboratoryRenderer.RenderBitmapAsync(saved, 256, 192, CancellationToken.None));
        byte[] restoredFrame = RauzyPixels(await MathematicalLaboratoryRenderer.RenderBitmapAsync(restored, 256, 192, CancellationToken.None));
        Check(originalFrame.SequenceEqual(restoredFrame),
            "Disk round-trip must preserve the exact tile, mosaic, palette, view and animation phase.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool caught = false;
        try { RauzyFractal.Generate(2_000_000, 1, cancelled.Token); } catch (OperationCanceledException) { caught = true; }
        Check(caught, "Prefix generation must honour cancellation.");
        caught = false;
        try { await MathematicalLaboratoryRenderer.RenderBitmapAsync(saved, 512, 512, cancelled.Token); }
        catch (OperationCanceledException) { caught = true; }
        Check(caught, "Rauzy rendering must honour cancellation.");
        var item = FractalCatalog.Create().Single(item => item.LaunchKey == "MathLab:Rauzy");
        Check(CatalogPreviewLoader.IsRendered(item) && (await CatalogPreviewLoader.RenderAsync(item, CancellationToken.None)).PixelWidth == 512,
            "Rauzy needs a rendered catalog thumbnail.");
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        var window = new MathematicalLaboratoryWindow(MathematicalLaboratoryKind.Rauzy);
        try
        {
            window.LoadState(restored);
            var captured = window.CaptureState("Window round-trip");
            Check(captured.Mode == 4 && captured.SecondaryValue == 3 && captured.TertiaryValue == 2 &&
                captured.PrimaryColor == saved.PrimaryColor && captured.Phase == saved.Phase && captured.Zoom == saved.Zoom,
                "The WPF panel must restore the saved parameters, colors, view and phase.");
            var modeBox = (ComboBox)window.FindName("ModeBox");
            var radiusBox = (TextBox)window.FindName("TertiaryBox");
            Check(radiusBox.IsEnabled, "Mosaic radius must be editable in mosaic mode.");
            modeBox.SelectedIndex = 1;
            Check(!radiusBox.IsEnabled && ((CheckBox)window.FindName("FilledCheck")).IsEnabled &&
                ((CheckBox)window.FindName("AnimateCheck")).Visibility == Visibility.Visible,
                "Subtile mode must retain contours and animation but disable the irrelevant mosaic radius.");
        }
        finally { window.Close(); }
        Console.WriteLine($"PASS (rauzy): fixed words, Sage projection, contracting eigenvectors, frequencies, 24 frames, contours, animation, cancellation, saves, catalog and WPF panel ({watch.ElapsedMilliseconds} ms).");
    }

    private static byte[] RauzyPixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }
}
