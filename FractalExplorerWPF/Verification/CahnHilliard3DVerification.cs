using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyCahnHilliard3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("cahn-hilliard3d");
        using var renderer = new Fractal3DRenderer();
        if (args[0] == "cahn-hilliard-perf") { await MeasureCahnHilliardAsync(renderer); return; }
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        const int n = 32;
        var random = new Random(911); var initial = new float[n*n*n];
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            initial[(z*n+y)*n+x] = (float)(.12 + .15*Math.Cos(2*Math.PI*(2*x+3*y+z)/n) + .1*(random.NextDouble()-.5));
        var settings = new CahnHilliard3DSettings { Size = n, TimeStep = .7, Mobility = .8, Kappa = 1.3, Field = new(n, 17, initial, 11.9) };
        using var gpu = new CahnHilliard3DGpuSimulation(renderer.DeviceHost, settings);
        double[] reference = initial.Select(v => (double)v).ToArray();
        double energy = CahnEnergy(reference,n,settings.Kappa);
        for (int step = 0; step < 5; step++)
        {
            reference = CahnReferenceStep(reference,n,settings);
            gpu.Advance(1,CancellationToken.None);
            var actual = gpu.ReadCurrent();
            Check(MaxArrayDifference(actual.Concentrations.ToArray(),reference.Select(v => (float)v).ToArray()) < 2e-6,
                "GPU FFT and the semi-implicit step must match independent double precision direct DFT in all three axes.");
            double nextEnergy = CahnEnergy(actual.Concentrations.ToArray().Select(v => (double)v).ToArray(),n,settings.Kappa);
            Check(nextEnergy < energy, "The free energy must decrease while separating the mixture."); energy = nextEnergy;
        }
        Check(gpu.Step == 22 && Math.Abs(gpu.Time-15.4) < 1e-10, "Step and physical time must continue from a checkpoint.");
        var shown = gpu.Publish(); var checkpoint = gpu.ReadCheckpoint(shown);
        gpu.Advance(4,CancellationToken.None); var overwritten = gpu.Publish(shown);
        gpu.Advance(4,CancellationToken.None); gpu.Publish(shown);
        Check(CahnDifference(checkpoint,gpu.ReadCheckpoint(shown)) == 0, "The displayed slot must survive later publications.");
        try { gpu.ReadCheckpoint(overwritten); throw new Exception("Stale checkpoint accepted."); } catch (InvalidOperationException) { }
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel(); var before = gpu.ReadCurrent();
            Check(gpu.Advance(100,canceled.Token) == 0 && CahnDifference(before,gpu.ReadCurrent()) == 0,
                "Cancellation before submission must not change the field.");
        }
        foreach (int side in new[] { 32, 64, 128 })
        {
            var constant = Enumerable.Repeat(.37f,side*side*side).ToArray();
            using var uniform = new CahnHilliard3DGpuSimulation(renderer.DeviceHost,
                new() { Size = side, Field = new(side,0,constant) });
            uniform.Advance(3,CancellationToken.None);
            Check(uniform.ReadCurrent().Concentrations.ToArray().All(v => Math.Abs(v-.37f) < 2e-7),
                "A uniform mixture must be stationary on every supported grid.");
        }
        // Analytic one-mode input: c³−c contains exactly the constant and first three
        // harmonics. This checks both FFT layouts at 64³/128³ without a large CPU FFT.
        foreach (int side in new[] { 64,128 }) foreach (bool tiled in new[] { false,true })
        {
            const double mean = .15, amplitude = .2;
            var wave = new float[side*side*side]; var expected = new float[wave.Length];
            double q = Math.Pow(2*Math.PI/side,2)*14, dt = .56;
            double c1 = ((1+3*dt*q)*amplitude-dt*q*((3*mean*mean-1)*amplitude+.75*Math.Pow(amplitude,3)))/(1+dt*q*(3+1.3*q));
            double c2 = -dt*q*4*(1.5*mean*amplitude*amplitude)/(1+dt*q*4*(3+1.3*q*4));
            double c3 = -dt*q*9*(.25*Math.Pow(amplitude,3))/(1+dt*q*9*(3+1.3*q*9));
            for (int z = 0; z < side; z++) for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
            {
                int i = (z*side+y)*side+x; double phase = 2*Math.PI*(2*x+3*y+z)/side;
                wave[i] = (float)(mean+amplitude*Math.Cos(phase));
                expected[i] = (float)(mean+c1*Math.Cos(phase)+c2*Math.Cos(2*phase)+c3*Math.Cos(3*phase));
            }
            using var simulation = new CahnHilliard3DGpuSimulation(renderer.DeviceHost,
                settings with { Size = side, Field = new(side,0,wave) },false,tiled);
            simulation.Advance(1,CancellationToken.None);
            Check(MaxArrayDifference(expected,simulation.ReadCurrent().Concentrations.ToArray()) < 2e-6,
                "Both line and tiled FFT must match analytic nonlinear harmonics on large grids.");
        }
        try { (new CahnHilliard3DSettings { Size = 96 }).Validate(); throw new Exception("Non power of two accepted."); }
        catch (InvalidOperationException) { }
        var kind = Fractal3DKind.CahnHilliard3D;
        Check(FractalCatalog.Create().Single(t => t.LaunchKey == Fractal3DCatalog.LaunchKey(kind)).IsThreeDimensional,
            "The mode must be discoverable in the catalog and 3D collection.");
        var state = Fractal3DCatalog.CreateDefaultState(kind); state.SaveName = "Exact phase separation";
        state.CahnHilliard = settings with { Field = gpu.ReadCurrent() };
        var options = JsonOptionsFactory.Create(); string json = JsonSerializer.Serialize(state,options);
        var restored = JsonSerializer.Deserialize<Fractal3DState>(json,options)!;
        Check(!json.Contains("\"Live\"",StringComparison.Ordinal) && CahnDifference(restored.CahnHilliard.Field!,state.CahnHilliard.Field!) == 0,
            "Brotli checkpoints must preserve exact signed concentrations, mass and time without GPU handles.");
        using (var continuation = new CahnHilliard3DGpuSimulation(renderer.DeviceHost,restored.CahnHilliard))
        {
            continuation.Advance(12,CancellationToken.None); gpu.Advance(12,CancellationToken.None);
            Check(CahnDifference(continuation.ReadCurrent(),gpu.ReadCurrent()) == 0, "Same-device disk continuation must be bit exact.");
        }
        var corrupt = JsonNode.Parse(json)!; corrupt["CahnHilliard"]!["Field"]!["Data"] = "AAAA";
        try { JsonSerializer.Deserialize<Fractal3DState>(corrupt.ToJsonString(),options); throw new Exception("Corrupt field accepted."); }
        catch (JsonException) { }
        var missingMass = JsonNode.Parse(json)!; missingMass["CahnHilliard"]!["Field"]!.AsObject().Remove("Mean");
        try { JsonSerializer.Deserialize<Fractal3DState>(missingMass.ToJsonString(),options); throw new Exception("Incomplete checkpoint accepted."); }
        catch (JsonException) { }
        var store = new Fractal3DSaveStore(kind); store.Save(state);
        Check(CahnDifference(store.Load().Single().CahnHilliard.Field!,state.CahnHilliard.Field!) == 0 &&
            CloudSaveRepository.ListLocal(out int unreadable).Single().Category == "Fractal3DCahnHilliard" && unreadable == 0,
            "The shared local and cloud repositories must recognize the new category.");

        int index = 0;
        foreach (var preset in Fractal3DCatalog.GetPresets(kind))
        {
            using var simulation = new CahnHilliard3DGpuSimulation(renderer.DeviceHost,preset.CahnHilliard);
            await Task.Run(() => simulation.Advance(preset.CahnHilliard.WarmupSteps,CancellationToken.None));
            var early = simulation.ReadCurrent(); float[] c = early.Concentrations.ToArray();
            double before = CahnEnergy(c.Select(v => (double)v).ToArray(),early.Size,preset.CahnHilliard.Kappa, false);
            await Task.Run(() => simulation.Advance(300,CancellationToken.None));
            var late = simulation.ReadCurrent(); float[] later = late.Concentrations.ToArray();
            double after = CahnEnergy(later.Select(v => (double)v).ToArray(),late.Size,preset.CahnHilliard.Kappa, false);
            Console.WriteLine($"{preset.SaveName}: c=[{c.Min():F3},{c.Max():F3}], mean={later.Average(v => (double)v):F8}, E={before:F1}→{after:F1}");
            Check(c.Min() < -.65 && c.Max() > .65 && later.All(float.IsFinite) && after < before &&
                Math.Abs(later.Average(v => (double)v)-late.Mean) < 1e-6 && CahnDifference(early,late) > .01,
                "Presets must separate into two bounded phases, conserve mass, decrease energy and keep evolving.");
            var view = preset.Clone(); view.CahnHilliard = preset.CahnHilliard with { Field = early };
            var image = await renderer.RenderAsync(view,320,320,null,CancellationToken.None);
            if (output is not null) CahnSaveImage(image,Path.Combine(output,$"preset-{index}.png"));
            if (index++ == 0) state = view;
        }
        using (var simulation = new CahnHilliard3DGpuSimulation(renderer.DeviceHost,state.CahnHilliard))
        {
            var live = state.Clone(); live.CahnHilliard = live.CahnHilliard with { Field = null, Live = simulation.Publish() };
            var pixels = CahnPixels(await renderer.RenderAsync(state,200,200,null,CancellationToken.None));
            var livePixels = CahnPixels(await renderer.RenderAsync(live,200,200,null,CancellationToken.None));
            Check(pixels.SequenceEqual(livePixels),
                "Live GPU rendering must match the same exact checkpoint.");
            using var foreign = new Fractal3DRenderer();
            try { await foreign.RenderAsync(live,40,40,null,CancellationToken.None); throw new Exception("Foreign device accepted."); }
            catch (InvalidOperationException) { }
            var inverse = state.Clone(); inverse.CahnHilliard = inverse.CahnHilliard with { Invert = true };
            var inversePixels = CahnPixels(await renderer.RenderAsync(inverse,200,200,null,CancellationToken.None));
            Check(!pixels.SequenceEqual(inversePixels),
                "The complementary phase must change the surface without changing its simulation.");
        }
        foreach (var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var view = state.Clone(); view.ShadingStyle = style;
            var image = await renderer.RenderAsync(view,200,200,null,CancellationToken.None);
            Check(CahnPixels(image).Where((_,i) => i%4 != 3).Distinct().Count() > 40, "Every common volume style must show a nonempty surface.");
            if (output is not null) CahnSaveImage(image,Path.Combine(output,$"style-{style}.png"));
        }
        bool hit = false;
        for (int y = 50; y < 160 && !hit; y += 20) for (int x = 50; x < 160 && !hit; x += 20)
            hit = await renderer.ProbeDistanceAsync(state,x,y,200,200,CancellationToken.None) > 0;
        Check(hit,"The common surface probe must hit a phase boundary.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("cahn-hilliard3d-pixel")) &&
            File.Exists(AppPaths.GetShaderCacheFile("fft3d-lines")) && File.Exists(AppPaths.GetShaderCacheFile("fft3d-tiled")) &&
            CahnHilliard3DComputeShader.CacheEntries.All(e => File.Exists(AppPaths.GetShaderCacheFile(e.Key))),
            "Both FFT layouts, evolution and display must be in the common disk cache.");
        await VerifyCahnWindowAsync(state,output);
        Console.WriteLine("PASS (cahn-hilliard3d): independent direct DFT, energy, mass, grids, cancellation, publication, exact continuation, presets, nine styles, two phases, saves, cloud and WPF.");
    }

    // Deliberately independent O(N^4) separable direct DFT, not an FFT or the GPU butterfly algorithm.
    private static Complex[] CahnDft(Complex[] input,int n,bool inverse)
    {
        var roots = new Complex[n,n];
        for (int k = 0; k < n; k++) for (int j = 0; j < n; j++)
            roots[k,j] = Complex.FromPolarCoordinates(inverse ? 1.0/n : 1,(inverse ? 2 : -2)*Math.PI*k*j/n);
        var current = input;
        for (int axis = 0; axis < 3; axis++)
        {
            var next = new Complex[input.Length];
            for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) for (int k = 0; k < n; k++)
            {
                Complex sum = 0;
                for (int j = 0; j < n; j++) sum += current[axis == 0 ? (a*n+b)*n+j : axis == 1 ? (a*n+j)*n+b : (j*n+a)*n+b]*roots[k,j];
                next[axis == 0 ? (a*n+b)*n+k : axis == 1 ? (a*n+k)*n+b : (k*n+a)*n+b] = sum;
            }
            current = next;
        }
        return current;
    }
    private static double[] CahnReferenceStep(double[] field,int n,CahnHilliard3DSettings s)
    {
        var c = CahnDft(field.Select(v => new Complex(v,0)).ToArray(),n,false);
        var f = CahnDft(field.Select(v => new Complex(v*v*v-v,0)).ToArray(),n,false);
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            int i = (z*n+y)*n+x; double q = Math.Pow(2*Math.PI/n,2)*(Math.Pow(Math.Min(x,n-x),2)+Math.Pow(Math.Min(y,n-y),2)+Math.Pow(Math.Min(z,n-z),2));
            double a = s.TimeStep*s.Mobility*q;
            c[i] = ((1+3*a)*c[i]-a*f[i])/(1+a*(3+s.Kappa*q));
        }
        return CahnDft(c,n,true).Select(v => v.Real).ToArray();
    }
    private static double CahnEnergy(double[] c,int n,double kappa,bool spectral = true)
    {
        double result = c.Sum(v => .25*(v*v-1)*(v*v-1));
        if (spectral)
        {
            var f = CahnDft(c.Select(v => new Complex(v,0)).ToArray(),n,false);
            for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                var v = f[(z*n+y)*n+x]; double q = Math.Pow(2*Math.PI/n,2)*(Math.Pow(Math.Min(x,n-x),2)+Math.Pow(Math.Min(y,n-y),2)+Math.Pow(Math.Min(z,n-z),2));
                result += .5*kappa*q*(v.Real*v.Real+v.Imaginary*v.Imaginary)/c.Length;
            }
        }
        else for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            double v = c[(z*n+y)*n+x];
            result += .5*kappa*(Math.Pow(c[(z*n+y)*n+(x+1)%n]-v,2)+Math.Pow(c[(z*n+(y+1)%n)*n+x]-v,2)+Math.Pow(c[(((z+1)%n)*n+y)*n+x]-v,2));
        }
        return result;
    }
    private static double CahnDifference(CahnHilliard3DField a,CahnHilliard3DField b) => MaxArrayDifference(a.Concentrations.ToArray(),b.Concentrations.ToArray());
    private static byte[] CahnPixels(BitmapSource image) { byte[] b = new byte[image.PixelWidth*image.PixelHeight*4]; image.CopyPixels(b,image.PixelWidth*4,0); return b; }
    private static void CahnSaveImage(BitmapSource image,string path)
    { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file); }

    private static async Task MeasureCahnHilliardAsync(Fractal3DRenderer renderer)
    {
        Console.WriteLine("Completed GPU work; no field readback in timings. Release build recommended.");
        foreach (int n in new[] { 32,64,128 })
        foreach (bool tiled in new[] { false,true })
        {
            var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.CahnHilliard3D);
            state.CahnHilliard = state.CahnHilliard with { Size = n };
            using var simulation = new CahnHilliard3DGpuSimulation(renderer.DeviceHost,state.CahnHilliard,false,tiled);
            Console.WriteLine($"Device: {simulation.DeviceName}, grid {n}³, FFT {(tiled ? "tiled" : "lines")}");
            await Task.Run(() => { simulation.Advance(8,CancellationToken.None); simulation.Synchronize(CancellationToken.None); });
            foreach (int batch in new[] { 1,4,16,64 })
            {
                var samples = new List<double>();
                for (int repeat = 0; repeat < 6; repeat++)
                {
                    double elapsed = await Task.Run(() => { var watch = Stopwatch.StartNew(); simulation.Advance(batch,CancellationToken.None); simulation.Synchronize(CancellationToken.None); return watch.Elapsed.TotalMilliseconds; });
                    samples.Add(elapsed);
                }
                Console.WriteLine($"{n}³ batch {batch}: {samples.Average():F2} ms ({samples.Average()/batch:F3} ms/step), max {samples.Max():F2} ms");
            }
            state.CahnHilliard = state.CahnHilliard with { Live = simulation.Publish() };
            await renderer.RenderAsync(state,512,384,null,CancellationToken.None);
            var frames = new List<double>();
            for (int repeat = 0; repeat < 8; repeat++)
            {
                var watch = Stopwatch.StartNew();
                await Task.Run(() => simulation.Advance(4,CancellationToken.None));
                state.CahnHilliard = state.CahnHilliard with { Live = simulation.Publish(state.CahnHilliard.Live) };
                await renderer.RenderAsync(state,512,384,null,CancellationToken.None);
                frames.Add(watch.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine($"{n}³ complete frame 512×384 + 4 steps: {frames.Average():F2} ms, {1000/frames.Average():F1} FPS, max {frames.Max():F2} ms");
        }
        await MeasureCahnWindowAsync();
    }

    private static async Task MeasureCahnWindowAsync()
    {
        const BindingFlags flags = BindingFlags.Instance|BindingFlags.NonPublic;
        var styles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == styles))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = styles });
        FractalExplorerWPF.Theming.ThemeManager.Initialize(Application.Current);
        foreach (int size in new[] { 64,128 })
        {
            var window = new Fractal3DWindow(Fractal3DKind.CahnHilliard3D)
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowInTaskbar = false };
            CahnHilliard3DVolume? Shown() => (CahnHilliard3DVolume?)typeof(Fractal3DWindow).GetField("_cahnShown",flags)!.GetValue(window);
            try
            {
                var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.CahnHilliard3D);
                state.CahnHilliard = state.CahnHilliard with { Size = size };
                window.LoadState(state); window.Show();
                var preparation = Stopwatch.StartNew();
                while (Shown() is null && preparation.Elapsed.TotalSeconds < 30) await Task.Delay(20);
                Check(Shown() is not null,"The hidden WPF benchmark must prepare its first frame.");
                long before = Shown()!.Step;
                ((Button)window.FindName("CahnPlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                int frames = 0; long lastStep = before;
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed.TotalSeconds < 4)
                {
                    long step = Shown()!.Step; if (step != lastStep) { frames++; lastStep = step; }
                    await Task.Delay(1);
                }
                typeof(Fractal3DWindow).GetMethod("PauseCahnHilliard",flags)!.Invoke(window,[]);
                var bitmap = window.CanvasImage.Source as BitmapSource;
                Console.WriteLine($"WPF {size}³: {frames/watch.Elapsed.TotalSeconds:F1} presented FPS, 4 steps/frame, {bitmap?.PixelWidth}×{bitmap?.PixelHeight} pixels; prep {preparation.Elapsed.TotalSeconds-watch.Elapsed.TotalSeconds:F2}s; offscreen window including composition and WritePixels.");
                Check(frames > 2 && lastStep > before,"The live WPF loop must present evolving frames.");
            }
            finally { window.Close(); }
        }
    }

    private static async Task VerifyCahnWindowAsync(Fractal3DState state,string? output)
    {
        var theme = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        var window = new Fractal3DWindow(Fractal3DKind.CahnHilliard3D);
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1180,800)); root.Arrange(new Rect(0,0,1180,800)); root.UpdateLayout();
        const BindingFlags flags = BindingFlags.Instance|BindingFlags.NonPublic;
        T Get<T>(string name) => (T)typeof(Fractal3DWindow).GetField(name,flags)!.GetValue(window)!;
        async Task Idle() { var w = Stopwatch.StartNew(); while (Get<bool>("_cahnBusy") && w.Elapsed.TotalSeconds < 60) await Task.Delay(10); Check(!Get<bool>("_cahnBusy"),"Simulation must finish."); }
        async Task Display() { var m = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync",flags)!; await (Task)m.Invoke(window,[Enum.Parse(m.GetParameters()[0].ParameterType,"Full")])!; }
        try
        {
            await Idle(); await Display();
            Check(!Get<bool>("_cahnPreparing") && Get<CahnHilliard3DVolume>("_cahnShown").Step == 450,"First frame must finish preparation on pause.");
            window.LoadState(state); await Idle(); await Display();
            var before = window.CaptureState("before").CahnHilliard.Field!;
            Check(CahnDifference(before,state.CahnHilliard.Field!) == 0,"Loading must restore exact concentrations.");
            Check(((FrameworkElement)window.FindName("IterationsBox")).Visibility == Visibility.Collapsed,"Unrelated iteration controls must be hidden.");
            ((Slider)window.FindName("CahnLevelSlider")).Value = .2;
            ((TextBox)window.FindName("CahnMeanBox")).Text = "invalid";
            Check(window.CaptureState("draft").CahnHilliard.Level == .2 && CahnDifference(before,window.CaptureState("draft").CahnHilliard.Field!) == 0,
                "View changes and unapplied drafts must preserve the exact displayed field.");
            ((Button)window.FindName("CahnStepButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            Check(window.CaptureState("pending").CahnHilliard.Field!.Step == before.Step,"Saves must keep the displayed frame while the next frame is pending.");
            await Display(); Check(window.CaptureState("after").CahnHilliard.Field!.Step == before.Step+4,"One frame must advance selected steps.");
            window.LoadState(Fractal3DCatalog.CreateDefaultState(Fractal3DKind.CahnHilliard3D));
            typeof(Fractal3DWindow).GetMethod("CahnStopPreparation_OnClick",flags)!.Invoke(window,[window,new RoutedEventArgs()]);
            await Idle(); await Display();
            Check(!Get<bool>("_cahnPreparing") && !Get<bool>("_cahnRunning") && window.CaptureState("partial").CahnHilliard.Field is not null,
                "Stopping preparation must publish the reached field on pause.");
            window.LoadState(state); await Idle(); await Display();
            if (output is not null)
            {
                root.Measure(new Size(1180,800)); root.Arrange(new Rect(0,0,1180,800)); root.UpdateLayout();
                await Display();
                var image = new RenderTargetBitmap(1180,800,96,96,PixelFormats.Pbgra32); image.Render(root);
                CahnSaveImage(image,Path.Combine(output,"window.png"));
            }
        }
        finally { window.Close(); }
    }
}
