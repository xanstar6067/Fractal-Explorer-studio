using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Theming;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyPhoenixParameterSlicesAsync(string[] args)
    {
        const int size = 32;
        var host = new Direct3DDeviceHost();
        using var renderer = new PhoenixParameterSliceRenderer(host, size);
        host.Release();
        var pose = new Fractal3DPose(Fractal3DCamera.Orientation(25, 15, 0), 7.5, Vector3.Zero);
        PhoenixState state = new() { Iterations = 60, ColoringMode = PhoenixColoringMode.Discrete };
        Vector3 hiddenMarker = new(100, 100, 100);
        int compared = 0;
        int boundaryDifferences = 0;
        foreach (PhoenixVariant variant in Enum.GetValues<PhoenixVariant>())
        {
            state.Variant = variant;
            // Include automatic parameter start (b>0) and user-specified two-step initial data.
            foreach (int setup in new[] { 0, 1, 2 })
            {
                state.PrimaryPower = setup == 2 ? 3 : 2;
                state.SecondaryPower = setup == 0 ? 0 : 1;
                state.InitialZReal = setup == 2 ? 0.15m : 0;
                state.InitialZImaginary = setup == 2 ? -0.1m : 0;
                state.InitialPreviousReal = setup == 2 ? 0.07m : 0;
                state.InitialPreviousImaginary = setup == 2 ? 0.03m : 0;
                state.C1Imaginary = 0.125m; state.C2Imaginary = -0.125m;
                foreach (bool c1 in new[] { true, false })
                {
                    await renderer.RenderAsync(state, c1, pose, hiddenMarker, false, 40, 32, null, default);
                    float[] field = await renderer.ReadFieldAsync(default);
                    int interior = 0;
                    // Independently evaluate matching parameter points using the established 2D renderer.
                    for (int z = 2; z < size - 2; z += 5)
                    {
                        double third = -2 + 4.0 * z / (size - 1);
                        if (c1) state.C2Real = (decimal)third; else state.C1Real = (decimal)third;
                        for (int y = 3; y < size - 2; y += 6)
                            for (int x = 2; x < size - 2; x += 5)
                            {
                                double real = -2 + 4.0 * x / (size - 1);
                                double imaginary = -2 + 4.0 * y / (size - 1);
                                var range = new PhoenixSliceRange { MinX = real, MaxX = real,
                                    MinY = imaginary, MaxY = imaginary };
                                byte[] pixel = new byte[4];
                                state.Palette.InteriorColor = Colors.Magenta;
                                PhoenixRenderer.RenderParameterPlane(state, pixel, 1, 1, 4, range,
                                    c1 ? PhoenixParameterPlane.C1 : PhoenixParameterPlane.C2, 1, default);
                                bool cpu = pixel[0] == 255 && pixel[1] == 0 && pixel[2] == 255;
                                bool gpu = field[(z * size + y) * size + x] >= 0.5;
                                if (cpu != gpu)
                                {
                                    // Chaotic boundary orbits can diverge between float GPU and double CPU.
                                    // Accept only a mixed occupancy neighbourhood, never a bulk disagreement.
                                    bool mixed = false;
                                    for (int dz = -1; dz <= 1; dz++)
                                        for (int dy = -1; dy <= 1; dy++)
                                            for (int dx = -1; dx <= 1; dx++)
                                                mixed |= (field[((z + dz) * size + y + dy) * size + x + dx] >= 0.5) != gpu;
                                    Check(mixed, $"Phoenix section {variant}, setup {setup}, C{(c1 ? 1 : 2)}, ({x},{y},{z}): CPU/GPU disagree away from the sampled boundary.");
                                    boundaryDifferences++;
                                }
                                if (gpu) interior++;
                                compared++;
                            }
                    }
                    if (setup == 0 && variant == PhoenixVariant.Classic)
                        Check(interior > 0, "Classic Phoenix section must contain bounded samples.");
                }
            }
        }
        Check(boundaryDifferences < compared / 100, "More than 1% of CPU/GPU parameter samples disagree.");
        Console.WriteLine($"Phoenix CPU/GPU: {compared} samples, {boundaryDifferences} float/double differences confined to boundary voxels.");

        state = new() { Iterations = 80 };
        PhoenixSliceFrame original = await renderer.RenderAsync(state, true, pose, hiddenMarker,
            false, 160, 128, null, default);
        Check(original.Depths.Any(d => d >= 0), "3D slice has no visible surface.");
        int builds = renderer.FieldBuildCount;
        int nearest = Array.IndexOf(original.Depths, original.Depths.Where(d => d >= 0).Min());
        Vector3 surfaceMarker = pose.Position + Fractal3DCamera.PixelRay(pose,
            PhoenixParameterSliceRenderer.FieldOfView, nearest % 160 + 0.5, nearest / 160 + 0.5, 160, 128) * original.Depths[nearest];
        PhoenixSliceFrame marked = await renderer.RenderAsync(state, true, pose,
            surfaceMarker, false, 160, 128, null, default);
        Check(original.Depths.AsSpan().SequenceEqual(marked.Depths), "Marker altered surface picking depths.");
        Check(!original.Pixels.AsSpan().SequenceEqual(marked.Pixels), "Green 3D marker is absent.");
        var rotated = Fractal3DCamera.Orbit(pose, Vector3.Zero, 30, 15);
        await renderer.RenderAsync(state, true, rotated, hiddenMarker, true, 64, 48, null, default);
        Check(renderer.FieldBuildCount == builds, "Camera, marker or shadows rebuilt the parameter field.");
        state.C1Real = -0.2m; state.C1Imaginary = 0.3m; state.C2Real = -0.4m;
        await renderer.RenderAsync(state, true, pose, hiddenMarker, false, 40, 32, null, default);
        Check(renderer.FieldBuildCount == builds, "Changing a free coordinate rebuilt the C1 section.");
        state.C2Imaginary = 0.2m;
        await renderer.RenderAsync(state, true, pose, hiddenMarker, false, 40, 32, null, default);
        Check(renderer.FieldBuildCount == builds + 1, "Fixed coordinate did not rebuild the C1 section.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await renderer.RenderAsync(state, false, pose, hiddenMarker, false, 40, 32, null, cancelled.Token);
                throw new Exception("Cancelled section render completed."); }
            catch (OperationCanceledException) { }
        }
        state.C2Imaginary = 0.25m;
        int beforeCancellation = renderer.FieldBuildCount;
        using (var cancelled = new CancellationTokenSource())
        {
            try
            {
                await renderer.RenderAsync(state, true, pose, hiddenMarker, false, 40, 32,
                    new PhoenixSliceProgress(_ => cancelled.Cancel()), cancelled.Token);
                throw new Exception("Mid-build cancellation was ignored.");
            }
            catch (OperationCanceledException) { }
        }
        Check(renderer.FieldBuildCount == beforeCancellation, "A cancelled partial field was committed.");
        await renderer.RenderAsync(state, true, pose, hiddenMarker, false, 40, 32, null, default);
        Check(renderer.FieldBuildCount == beforeCancellation + 1, "Retry reused a cancelled partial field.");
        state.C2Imaginary = 10;
        PhoenixSliceFrame empty = await renderer.RenderAsync(state, true, pose, hiddenMarker, false, 40, 32, null, default);
        Check(empty.Depths.All(d => d < 0), "An empty section produced a false pickable surface.");

        // Construct the actual XAML window without showing it or touching user settings.
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/FractalExplorerWPF;component/Theming/ThemeStyles.xaml", UriKind.Relative) });
        ThemeManager.Initialize(Application.Current);
        var window = new PhoenixParameterExplorerWindow(new());
        try
        {
            ((RadioButton)window.FindName("Mode3D")).IsChecked = true;
            Check(((FrameworkElement)window.FindName("C1Slice")).Visibility == Visibility.Visible,
                "3D switch does not show the slice control.");
            Check(((FrameworkElement)window.FindName("C1PlaneHost")).Visibility == Visibility.Collapsed,
                "3D switch leaves the 2D map visible.");
            var select = typeof(PhoenixParameterExplorerWindow).GetMethod("SelectSlicePoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
            select.Invoke(window, [true, new Vector3(0.25f, -0.125f, -0.5f)]);
            Check(window.SelectedC1Real == 0.25m && window.SelectedC1Imaginary == -0.125m && window.SelectedC2Real == -0.5m && window.SelectedC2Imaginary == 0,
                "C1 section pick did not preserve its fixed fourth coordinate.");
            select.Invoke(window, [false, new Vector3(-0.75f, 0.375f, 0.5f)]);
            Check(window.SelectedC2Real == -0.75m && window.SelectedC2Imaginary == 0.375m && window.SelectedC1Real == 0.5m && window.SelectedC1Imaginary == -0.125m,
                "C2 section pick did not preserve its fixed fourth coordinate.");
            ((RadioButton)window.FindName("Mode2D")).IsChecked = true;
            Check(((FrameworkElement)window.FindName("C1Slice")).Visibility == Visibility.Collapsed && window.SelectedC2Imaginary == 0.375m,
                "Returning to 2D lost the selection or left a slice active.");
            const decimal preciseFixed = 0.1234567890123456789012345678m;
            ((TextBox)window.FindName("C2ImaginaryBox")).Text = preciseFixed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ((RadioButton)window.FindName("Mode3D")).IsChecked = true;
            select.Invoke(window, [true, new Vector3(0.25f, 0.125f, -0.5f)]);
            NumericInput.CommitAll(window);
            typeof(PhoenixParameterExplorerWindow).GetMethod("ReadSelectedValues", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Check(window.SelectedC2Imaginary == preciseFixed, "Picking three coordinates rounded the fixed fourth coordinate.");
        }
        finally { window.Close(); }

        string? output = ReadOption(args, "--out");
        if (output is not null)
        {
            System.IO.Directory.CreateDirectory(output);
            var qa = new PhoenixParameterExplorerWindow(new());
            try
            {
                ((RadioButton)qa.FindName("Mode3D")).IsChecked = true;
                var root = (FrameworkElement)qa.Content;
                root.Measure(new Size(1100, 740)); root.Arrange(new Rect(0, 0, 1100, 740)); root.UpdateLayout();
                // Exercise real controls without showing a native window or capturing the screen.
                foreach (string name in new[] { "C1Slice", "C2Slice" })
                {
                    var view = (PhoenixParameterSliceView)qa.FindName(name);
                    typeof(PhoenixParameterSliceView).GetField("_requested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, true);
                    await (Task)typeof(PhoenixParameterSliceView).GetMethod("RenderLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null)!;
                    var image = (Image)view.FindName("PreviewImage");
                    var map = (FrameworkElement)view.FindName("MapHost");
                    Check(image.Source is BitmapSource source && source.PixelWidth == RenderSurfaceMetrics.Measure(map).PixelWidth,
                        "Phoenix 3D view did not render at 100% physical resolution.");
                    Check(((PreparationOverlay)view.FindName("Preparation")).Visibility == Visibility.Collapsed,
                        "Preparation did not close after an actual 3D frame.");
                }
                root.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1100, 740, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(output, "phoenix-parameter-window.png"));
                encoder.Save(stream);
                var leftView = (PhoenixParameterSliceView)qa.FindName("C1Slice");
                var shown = (PhoenixSliceFrame)typeof(PhoenixParameterSliceView).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(leftView)!;
                int hitIndex = Array.FindIndex(shown.Depths, d => d >= 0);
                var leftMap = (FrameworkElement)leftView.FindName("MapHost");
                var point = new Point((hitIndex % shown.Width + 0.5) * leftMap.ActualWidth / shown.Width,
                    (hitIndex / shown.Width + 0.5) * leftMap.ActualHeight / shown.Height);
                Vector3 expected = shown.Pose.Position + Fractal3DCamera.PixelRay(shown.Pose,
                    PhoenixParameterSliceRenderer.FieldOfView, hitIndex % shown.Width + 0.5, hitIndex / shown.Width + 0.5,
                    shown.Width, shown.Height) * shown.Depths[hitIndex];
                typeof(PhoenixParameterSliceView).GetMethod("SelectAt", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(leftView, [point]);
                Check(Math.Abs((double)qa.SelectedC1Real - expected.X) < 1e-6 &&
                    Math.Abs((double)qa.SelectedC1Imaginary - expected.Y) < 1e-6 &&
                    Math.Abs((double)qa.SelectedC2Real - expected.Z) < 1e-6 && qa.SelectedC2Imaginary == 0,
                    "Actual control pixel picking did not reach the Phoenix fields.");
            }
            finally { qa.Close(); }
        }
        Console.WriteLine($"PASS (phoenix-slices): {compared} CPU/GPU samples, both sections, five variants, automatic/custom starts, depth picking, marker, cache, cancellation and 2D/3D selection.");
    }

    private sealed class PhoenixSliceProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
