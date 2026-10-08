using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyMandelbrotSavesAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("mandelbrot-saves");
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        string? output = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
        if (output is not null) Directory.CreateDirectory(output);
        var report = new StringBuilder("Variant,Case,Zoom,ColourMode,DistinctColours,PixelHash,Result\n");
        int cases = 0;
        const int width = 64, height = 44;
        JsonSerializerOptions options = JsonOptionsFactory.Create();
        static byte[] ImageBytes(BitmapSource bitmap)
        {
            var bytes = new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];
            bitmap.CopyPixels(bytes,bitmap.PixelWidth*4,0); return bytes;
        }
        string ViewJson(MandelbrotState state)
        {
            var node = JsonSerializer.SerializeToNode(state,options)!.AsObject();
            node.Remove("SaveName"); node.Remove("Timestamp");
            if (state.CenterXExact is not null)
            {
                node.Remove("CenterXExact"); node.Remove("CenterYExact");
                node.Remove("CenterX"); node.Remove("CenterY");
            }
            return node.ToJsonString();
        }
        void NoGpu(MandelbrotWindow window)
        {
            const BindingFlags flags = BindingFlags.NonPublic|BindingFlags.Instance;
            var gpu = window.GetType().GetField("_gpuPreview",flags)!.GetValue(window)!;
            var host = gpu.GetType().GetField("_host",flags)!.GetValue(gpu)!;
            Check(host.GetType().GetField("_device",flags)!.GetValue(host) is null,
                "Save previews must not create a GPU device, including at shallow Julia zoom.");
            Check(!Directory.Exists(AppPaths.ShaderCacheDirectory) || !Directory.EnumerateFiles(AppPaths.ShaderCacheDirectory,"*.cso").Any(),
                "Save previews must not compile GPU shaders.");
        }
        foreach (MandelbrotVariant variant in Enum.GetValues<MandelbrotVariant>())
        {
            var window = new MandelbrotWindow(variant);
            var store = new MandelbrotSaveStore(variant);
            try
            {
                async Task Run(MandelbrotState seed,string label,bool png = false)
                {
                    seed.SaveName = label;
                    seed.Timestamp = new DateTime(2026,10,7,12,34,56,DateTimeKind.Utc).AddTicks(1234567);
                    var slot = store.Save(seed);
                    string original = File.ReadAllText(slot.FilePath);
                    var read = store.LoadSlots().Slots.Single(s => s.State.SaveName == label);
                    Check(JsonSerializer.Serialize(seed,options) == JsonSerializer.Serialize(read.State,options),
                        $"{variant}/{label}: JSON save/load must preserve every serialized field exactly.");
                    window.LoadState(read.State);
                    MandelbrotState captured = window.CaptureState(label);
                    Check(ViewJson(seed) == ViewJson(captured),
                        $"{variant}/{label}: loading into WPF changed render settings.\nExpected {ViewJson(seed)}\nActual {ViewJson(captured)}");
                    if (seed.CenterXExact is not null)
                    {
                        using var precision = new BigFloat.PrecisionScope(4096);
                        Check(captured.CenterXExact is not null && captured.CenterYExact is not null,
                            "Loading an extreme save must retain its exact centre.");
                        FloatExp dx = FloatExp.Abs(FloatExp.FromBigFloat(BigFloat.Parse(seed.CenterXExact)-BigFloat.Parse(captured.CenterXExact!)));
                        FloatExp dy = FloatExp.Abs(FloatExp.FromBigFloat(BigFloat.Parse(seed.CenterYExact!)-BigFloat.Parse(captured.CenterYExact!)));
                        Check(dx*seed.Zoom < 1e-12 && dy*seed.Zoom < 1e-12,
                            "Exact-centre UI drift must stay below one trillionth of the view width.");
                    }
                    NoGpu(window);
                    BitmapSource image = await window.RenderStatePreviewAsync(read.State,width,height,CancellationToken.None);
                    byte[] pixels = ImageBytes(image);
                    byte[] restored = ImageBytes(await window.RenderStatePreviewAsync(captured,width,height,CancellationToken.None));
                    Check(pixels.SequenceEqual(restored), $"{variant}/{label}: saved and loaded preview frames differ.");
                    var reference = new byte[pixels.Length];
                    await Task.Run(() => MandelbrotFamilyRenderer.Render(seed,reference,width,height,width*4,CancellationToken.None));
                    Check(reference.SequenceEqual(pixels), $"{variant}/{label}: preview must match the direct CPU frame.");
                    NoGpu(window);
                    Check(File.ReadAllText(slot.FilePath) == original, "Preview rendering must not rewrite save JSON.");
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using (var stream = File.Create(slot.PreviewPath)) encoder.Save(stream);
                    Check(ImageBytes(LoadPng(slot.PreviewPath)).SequenceEqual(pixels), "Stored and reloaded PNG pixels must match.");
                    int colours = pixels.Chunk(4).Select(p => BitConverter.ToUInt32(p)).Distinct().Count();
                    string hash = Convert.ToHexString(SHA256.HashData(pixels));
                    report.AppendLine($"{variant},{label},{seed.Zoom.ToInvariantString()},{seed.ColoringMode},{colours},{hash},PASS");
                    cases++;
                    if (png && output is not null)
                    {
                        File.Copy(slot.PreviewPath,Path.Combine(output,$"{variant}-{label}.png"),true);
                    }
                }
                foreach (int exponent in new[] { 0,6,9,10,12,26,50,300,1000 })
                {
                    MandelbrotState state = window.CaptureState("depth");
                    state.Zoom = FloatExp.Pow10(exponent);
                    state.CenterX = MandelbrotVariantDefinition.IsJulia(variant) ? 1m : -2m;
                    state.CenterY = 0;
                    state.JuliaCReal = 0; state.JuliaCImaginary = 0;
                    state.Power = MandelbrotVariantDefinition.ParameterVariant(variant) == MandelbrotVariant.Generalized ? 3m : 2m;
                    state.Iterations = exponent >= 300 ? 3800 : 240;
                    if (exponent >= 26)
                    {
                        // An exact centre deliberately contains a displacement lost in decimal.
                        using var precision = new BigFloat.PrecisionScope(Math.Max(384,(int)Math.Ceiling(exponent*Math.Log2(10))+128));
                        state.CenterXExact = (BigFloat.FromDecimal(state.CenterX)+BigFloat.Parse("1e-"+(exponent+2))).ToInvariantString();
                        state.CenterYExact = BigFloat.Parse("-2e-"+(exponent+2)).ToInvariantString();
                    }
                    state.Palette = new MandelbrotPalette { Name = "Test palette", Colors = [Colors.DarkBlue,Colors.Orange,Colors.White],
                        InteriorColor = Colors.DarkSlateGray, ColorPeriod = 73, Gamma = 1.2 };
                    state.PaletteName = state.Palette.Name; state.InteriorColor = state.Palette.InteriorColor;
                    state.PaletteScale = 3.1; state.PalettePhaseOffset = 0.17;
                    await Run(state,"zoom-1e"+exponent,png:true);
                }
                foreach (MandelbrotColoringMode mode in Enum.GetValues<MandelbrotColoringMode>())
                {
                    MandelbrotState state = PresetManager.GetMandelbrotPresets(variant)[0];
                    state.SaveName = "colour"; state.ColoringMode = mode; state.Iterations = 350;
                    state.Palette = new MandelbrotPalette { Name = "Custom", Colors = [Colors.MidnightBlue,Colors.Coral,Colors.LightYellow],
                        InteriorColor = Colors.DarkSlateGray, Gamma = 1.35, ColorPeriod = 127, IsGradient = false };
                    state.PaletteName = state.Palette.Name; state.UseCustomInteriorColor = true; state.InteriorColor = Colors.DarkGreen;
                    state.HistogramContrast = 1.7; state.HistogramEnabledEqualization = true; state.HistogramInputUseSmooth = false;
                    state.SmoothBlendPower = 1.3; state.SmoothIterationOffset = -0.4;
                    state.PalettePhaseOffset = 0.27; state.PaletteScale = 1.8; state.PaletteWrapMode = MandelbrotPaletteWrapMode.Mirror;
                    state.OrbitTrapStrength = 1.4; state.OrbitTrapBias = -0.12;
                    state.StripeFrequency = 4.2; state.StripeStrength = 0.65; state.StripeBias = 0.17;
                    state.PolynomialA = 8.2; state.PolynomialB = 13.3; state.PolynomialC = 7.4;
                    state.PolynomialGamma = 1.2; state.PolynomialBlend = 0.7; state.PolynomialBias = 0.08;
                    state.DistanceReliefStrength = 1.7; state.DistanceLightAzimuth = -42;
                    state.DistanceLightElevation = 37; state.DistanceAmbient = 0.31; state.DistanceDiffuse = 0.74;
                    state.DistanceSpecular = 0.41; state.DistanceShininess = 27; state.DistanceContoursEnabled = false;
                    state.DistanceContourSpacing = 15; state.DistanceContourStrength = 0.23;
                    await Run(state,"colour-"+mode,png:true);
                }
                foreach (var preset in PresetManager.GetMandelbrotPresets(variant))
                    await Run(preset,"preset-"+cases);
                if (MandelbrotVariantDefinition.ParameterVariant(variant) is MandelbrotVariant.Simonobrot or MandelbrotVariant.Generalized)
                {
                    var extra = window.CaptureState("power"); extra.Zoom = 0.75;
                    extra.CenterXExact = null; extra.CenterYExact = null; extra.CenterX = 0; extra.CenterY = 0;
                    extra.Power = MandelbrotVariantDefinition.ParameterVariant(variant) == MandelbrotVariant.Simonobrot ? -3m : 3.1m;
                    extra.UseInversion = MandelbrotVariantDefinition.ParameterVariant(variant) == MandelbrotVariant.Simonobrot;
                    await Run(extra,"negative-or-fractional-power");
                }
                // Exercise the actual save-manager selection, PNG cache, manual CPU rerender and Load button.
                var view = new SaveManagerControl(); var managerWindow = new Window { Content = view };
                using var controller = new SaveManagerController<MandelbrotState>(managerWindow,view,SaveManagerConfigurations.ForMandelbrot(window,store));
                var entry = ((System.Windows.Controls.ListBox)view.FindName("SavesList")).Items.Cast<SaveManagerEntry<MandelbrotState>>().First(s => s.State.SaveName=="zoom-1e26");
                view.SelectedItem = entry;
                Check(Image(view) is BitmapSource, "Selecting a deep save must display its cached PNG.");
                Click(view,"RenderPreviewButton");
                for (int attempt=0; attempt<1000; attempt++)
                {
                    await Task.Delay(10);
                    if (((System.Windows.Controls.TextBlock)view.FindName("RenderButtonText")).Text == "Рендер превью") break;
                }
                Check(((System.Windows.Controls.TextBlock)view.FindName("RenderButtonText")).Text == "Рендер превью" &&
                    Image(view) is BitmapSource && !((System.Windows.Controls.TextBlock)view.FindName("StatusText")).Text.Contains("Ошибка"),
                    "A real deep-save rerender must finish without error.");
                var managerImage = (BitmapSource)Image(view)!;
                var managerReference = await window.RenderStatePreviewAsync(entry.State,managerImage.PixelWidth,managerImage.PixelHeight,CancellationToken.None);
                Check(ImageBytes(managerImage).SequenceEqual(ImageBytes(managerReference)), "Manual save-manager rerender must show the selected CPU frame.");
                NoGpu(window);
                managerWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                managerWindow.Left = -30000; managerWindow.Top = -30000;
                managerWindow.ShowInTaskbar = false; managerWindow.ShowActivated = false;
                _ = managerWindow.Dispatcher.BeginInvoke(new Action(() => Click(view,"LoadButton")));
                Check(managerWindow.ShowDialog() == true, "Load must complete a real modal save-manager dialog.");
                Check(window.CaptureState("loaded").Zoom == entry.State.Zoom, "Save-manager Load must restore the selected deep zoom.");
                Console.WriteLine($"{variant}: save/CPU preview/load checks complete ({cases} cases total).");
                // Compare the actual completed CPU canvas with the full-frame save preview.
                // Small off-screen surfaces keep this UI regression check inexpensive.
                window.Left = -30000; window.Top = -30000; window.ShowInTaskbar = false; window.ShowActivated = false;
                window.Width = 920; window.Height = 580;
                var canvas = (FrameworkElement)window.FindName("CanvasHost");
                canvas.Width = 128; canvas.Height = 88;
                window.Show(); window.UpdateLayout();
                const BindingFlags flags = BindingFlags.Instance|BindingFlags.NonPublic;
                foreach (var mode in Enum.GetValues<MandelbrotColoringMode>())
                {
                    var state = PresetManager.GetMandelbrotPresets(variant)[0];
                    state.ColoringMode = mode; state.Iterations = 4100; // Force CPU even for shallow Julia.
                    state.HistogramEnabledEqualization = true;
                    window.LoadState(state);
                    ((System.Windows.Threading.DispatcherTimer)window.GetType().GetField("_renderTimer",flags)!.GetValue(window)!).Stop();
                    await (Task)window.GetType().GetMethod("RenderPreviewAsync",flags)!.Invoke(window,null)!;
                    var main = (BitmapSource)window.GetType().GetField("_stableBitmap",flags)!.GetValue(window)!;
                    var preview = await window.RenderStatePreviewAsync(window.CaptureState("main"),main.PixelWidth,main.PixelHeight,CancellationToken.None);
                    int difference = ImageBytes(main).Zip(ImageBytes(preview)).Count(pair => pair.First != pair.Second);
                    Check(difference == 0,$"{variant}/{mode}: main CPU view differs from its save preview: {difference} channels.");
                    NoGpu(window);
                }
                Console.WriteLine($"{variant}: all seven main CPU canvases match save previews exactly.");
            }
            finally { window.Close(); }
        }
        if (output is not null) File.WriteAllText(Path.Combine(output,"results.csv"),report.ToString());
        Console.WriteLine($"PASS (mandelbrot-saves): {cases} cases, {Enum.GetValues<MandelbrotVariant>().Length} variants, nine zoom depths to 1e1000, seven colour modes, presets, exact JSON/PNG/WPF round-trips, CPU-only previews and real manager controls.");
    }
}
