using System.IO;
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
    private static async Task VerifyKobayashi3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("kobayashi3d");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        using var renderer = new Fractal3DRenderer(); var host = renderer.DeviceHost;
        var kind = Fractal3DKind.Kobayashi3D;
        Console.WriteLine($"Catalog: {FractalCatalog.Create().Count} entries, {Enum.GetValues<Fractal3DKind>().Length} 3D modes.");
        if (args.Contains("--probe"))
        {
            int number=0;
            foreach(var parameters in new[] { (.6,.5,1.8,.5,.055),(.6,.5,1.8,.5,-.055),(.6,.5,1.4,.2,.04),(.5,.5,1.8,.5,.04) })
            {
                var probe=Fractal3DCatalog.CreateDefaultState(kind);
                probe.Kobayashi=new(){Size=96,InterfaceWidth=parameters.Item1,Undercooling=parameters.Item2,LatentHeat=parameters.Item3,ThermalDiffusion=parameters.Item4,Noise=.1,Anisotropy=parameters.Item5,WarmupSteps=12000};
                using var engine=new Kobayashi3DGpuSimulation(host,probe.Kobayashi);
                for(int stage=0;stage<2;stage++)
                {
                    await Task.Run(()=>engine.Advance(6000,CancellationToken.None));
                    probe.Kobayashi=probe.Kobayashi with{Field=engine.ReadCurrent()};
                    Console.WriteLine($"Probe {number}, {parameters}, step {engine.Step}");
                    if(output is not null)Save(await renderer.RenderAsync(probe,320,320,null,CancellationToken.None),Path.Combine(output,$"large-{number}-{stage}.png"));
                }
                number++;
            }
            return;
        }
        const int n = 32; var random = new Random(123); var values = new float[n*n*n*2];
        for (int i=0;i<values.Length;i+=2) { values[i]=(float)(.2+.6*random.NextDouble()); values[i+1]=(float)(-.5+.2*random.NextDouble()); }
        var s = new Kobayashi3DSettings { Size=n, Noise=0, Anisotropy=.035, Field=new(n,17,values) };
        using var gpu = new Kobayashi3DGpuSimulation(host,s);
        var expected = KobReference(values,n,s);
        gpu.Advance(1,CancellationToken.None); var one=gpu.ReadCurrent();
        double error=one.Concentrations.ToArray().Zip(expected).Max(v=>Math.Abs(v.First-v.Second));
        Console.WriteLine($"Independent energy derivative / GPU error: {error:G5}");
        Check(error<2e-6 && one.Step==18,"GPU must match the independent energy derivative, no-flux faces and latent heat.");
        double enthalpy=KobEnthalpy(one,s.LatentHeat); gpu.Advance(50,CancellationToken.None);
        Check(Math.Abs(KobEnthalpy(gpu.ReadCurrent(),s.LatentHeat)-enthalpy)<1e-5,"Insulated walls must conserve mean T-Lφ.");
        var shown=gpu.Publish(); var snapshot=gpu.ReadCheckpoint(shown);
        gpu.Advance(3,CancellationToken.None); var discarded=gpu.Publish(shown);
        gpu.Advance(3,CancellationToken.None); gpu.Publish(shown);
        Check(KobDifference(snapshot,gpu.ReadCheckpoint(shown))==0,"The displayed slot must survive later frames.");
        try { gpu.ReadCheckpoint(discarded); throw new Exception("Replaced publication accepted"); } catch(InvalidOperationException) { }
        using(var cts=new CancellationTokenSource()) { cts.Cancel();long step=gpu.Step;Check(gpu.Advance(20,cts.Token)==0 && gpu.Step==step,"Cancellation must preserve time."); }
        var state=Fractal3DCatalog.CreateDefaultState(kind); state.SaveName="Exact crystal";
        state.Kobayashi=s with { Field=gpu.ReadCurrent() };
        var options=JsonOptionsFactory.Create();string json=JsonSerializer.Serialize(state,options);
        var restored=JsonSerializer.Deserialize<Fractal3DState>(json,options)!;
        Check(KobDifference(state.Kobayashi.Field!,restored.Kobayashi.Field!)==0,"Brotli must preserve both fields.");
        using(var resume=new Kobayashi3DGpuSimulation(host,restored.Kobayashi))
        { gpu.Advance(5,CancellationToken.None);resume.Advance(5,CancellationToken.None);Check(KobDifference(gpu.ReadCurrent(),resume.ReadCurrent())==0,"JSON must continue exactly."); }
        var bad=JsonNode.Parse(json)!;bad["Kobayashi"]!["Field"]!["Data"]="AAAA";
        try { JsonSerializer.Deserialize<Fractal3DState>(bad.ToJsonString(),options);throw new Exception("Corrupt field accepted"); } catch(JsonException) { }
        var noisy=s with { Noise=.07, Field=new(n,(1L<<32)+42,gpu.ReadCurrent().Concentrations) };
        using(var noisyA=new Kobayashi3DGpuSimulation(host,noisy))
        {
            noisyA.Advance(7,CancellationToken.None);
            var checkpoint=JsonSerializer.Deserialize<Kobayashi3DSettings>(JsonSerializer.Serialize(noisy with { Field=noisyA.ReadCurrent() },options),options)!;
            using var noisyB=new Kobayashi3DGpuSimulation(host,checkpoint);
            noisyA.Advance(11,CancellationToken.None); noisyB.Advance(11,CancellationToken.None);
            Check(KobDifference(noisyA.ReadCurrent(),noisyB.ReadCurrent())==0,"Noise continuation must retain both halves of the 64-bit step.");
        }
        var store=new Fractal3DSaveStore(kind);store.Save(state);
        Check(KobDifference(store.Load().Single().Kobayashi.Field!,state.Kobayashi.Field!)==0,"Save storage must retain the crystal.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().Category=="Fractal3DKobayashi" && unreadable==0,"Cloud must recognize the category.");
        foreach(int side in new[]{33,128})
        {
            using var engine=new Kobayashi3DGpuSimulation(host,new(){Size=side,SeedShape=Kobayashi3DSeed.Empty});
            engine.Inject(0,.5,.5,.08);var f=engine.ReadCurrent().Concentrations.ToArray();
            Check(f.Where((v,i)=>i%2==0).Sum()>0,"Brush must add a nucleus.");
            Check(f[((side/2*side+side/2)*side+side-1)*2]==0,"Brush must not wrap to the opposite wall.");
            engine.Advance(2,CancellationToken.None);Check(engine.ReadCurrent().Step==2,"Maximum and partial groups must evolve.");
        }
        var presets=Fractal3DCatalog.GetPresets(kind);int index=0;
        foreach(var preset in presets)
        {
            using var engine=new Kobayashi3DGpuSimulation(host,preset.Kobayashi);var early=engine.ReadCurrent();
            await Task.Run(()=>engine.Advance(preset.Kobayashi.InitialSteps,CancellationToken.None));var field=engine.ReadCurrent();
            double solid=field.Concentrations.ToArray().Where((_,i)=>i%2==0).Sum(),initial=early.Concentrations.ToArray().Where((_,i)=>i%2==0).Sum();
            Console.WriteLine($"Preset {index} {preset.SaveName}: solid {initial:F0} -> {solid:F0}, step {field.Step}");
            Check(solid>initial*1.1,"Every preset must grow.");
            Check(Math.Abs(KobEnthalpy(field,preset.Kobayashi.LatentHeat)-KobEnthalpy(early,preset.Kobayashi.LatentHeat))<2e-5,"Presets must conserve enthalpy.");
            var view=preset.Clone();view.Kobayashi=view.Kobayashi with{Field=field};
            var bitmap=await renderer.RenderAsync(view,320,320,null,CancellationToken.None);
            Check(Pixels(bitmap).Where((_,i)=>i%4!=3).Distinct().Count()>40,"Every preset must produce a nonuniform frame.");
            if(output is not null)Save(bitmap,Path.Combine(output,$"preset-{index}.png"));
            if(index++==0)state=view;
        }
        byte[] basePixels=Pixels(await renderer.RenderAsync(state,200,200,null,CancellationToken.None));
        using(var engine=new Kobayashi3DGpuSimulation(host,state.Kobayashi))
        {
            var live=state.Clone();live.Kobayashi=live.Kobayashi with{Field=null,Live=engine.Publish()};
            var livePixels=Pixels(await renderer.RenderAsync(live,200,200,null,CancellationToken.None));
            Check(basePixels.SequenceEqual(livePixels),"Live and checkpoint must render identically.");
            using var foreign=new Fractal3DRenderer();
            try { await foreign.RenderAsync(live,40,40,null,CancellationToken.None);throw new Exception("Foreign frame accepted"); } catch(InvalidOperationException) { }
        }
        var recolored=state.Clone();var recolorValues=state.Kobayashi.Field!.Concentrations.ToArray();
        for(int i=1;i<recolorValues.Length;i+=2)recolorValues[i]=.3f;
        recolored.Kobayashi=recolored.Kobayashi with{Field=new(state.Kobayashi.Size,state.Kobayashi.Field.Step,recolorValues)};
        var recoloredPixels=Pixels(await renderer.RenderAsync(recolored,200,200,null,CancellationToken.None));
        Check(!basePixels.SequenceEqual(recoloredPixels),"Temperature must affect the selected surface colouring.");
        var cut=state.Clone();cut.Kobayashi=cut.Kobayashi with{CutAxis=3,CutPosition=0};
        var cutPixels=Pixels(await renderer.RenderAsync(cut,200,200,null,CancellationToken.None));
        Check(!basePixels.SequenceEqual(cutPixels),"Cut must expose interior.");
        foreach(var style in Enum.GetValues<Fractal3DShadingStyle>())
        { var view=state.Clone();view.ShadingStyle=style;var b=await renderer.RenderAsync(view,200,200,null,CancellationToken.None);if(output is not null)Save(b,Path.Combine(output,$"style-{(int)style}.png")); }
        bool hit=false;
        for(int y=70;y<150 && !hit;y+=10)for(int x=70;x<150 && !hit;x+=10)hit=await renderer.ProbeDistanceAsync(state,x,y,200,200,CancellationToken.None)>0;
        Check(hit,"Probe must hit crystal.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("kobayashi3d-pixel")) && Kobayashi3DComputeShader.CacheEntries.All(e=>File.Exists(AppPaths.GetShaderCacheFile(e.Key))),"Compute and display must use common cache.");
        var theme = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        var window = new Fractal3DWindow(kind);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Invoke(string name, params object[] parameters) => typeof(Fractal3DWindow).GetMethod(name, flags)!.Invoke(window, parameters);
        T Field<T>(string name) => (T)typeof(Fractal3DWindow).GetField(name, flags)!.GetValue(window)!;
        Kobayashi3DVolume? Shown() => (Kobayashi3DVolume?)typeof(Fractal3DWindow).GetField("_kobShown", flags)!.GetValue(window);
        Kobayashi3DVolume? Pending() => (Kobayashi3DVolume?)typeof(Fractal3DWindow).GetField("_kobPending", flags)!.GetValue(window);
        async Task Idle()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (Field<bool>("_kobBusy") && watch.ElapsedMilliseconds < 120000) await Task.Delay(10);
            Check(!Field<bool>("_kobBusy"), "Background simulation must finish or cancel.");
        }
        async Task Display()
        {
            var method = typeof(Fractal3DWindow).GetMethod("RenderFrameAsync", flags)!;
            await (Task)method.Invoke(window, [Enum.Parse(method.GetParameters()[0].ParameterType, "Full")])!;
        }
        try
        {
            Check(window.FindName("KobBackendBox") is null, "The 3D window must not offer a CPU computation backend.");
            await Idle(); await Display();
            Check(Shown() is { Step: > 0 } && Pending() is null, "Opening the window must prepare and show the first preset on the GPU.");
            window.LoadState(state);
            Check(Shown() is null, "Loading must drop the previous live frame.");
            await Idle(); await Display();
            var loaded = window.CaptureState("ui").Kobayashi;
            Check(Shown() is not null && loaded.Live is null && KobDifference(loaded.Field!, state.Kobayashi.Field!) == 0 &&
                loaded.Field!.Step == state.Kobayashi.Field!.Step, "WPF loading must preserve the exact field on pause.");
            Check(((FrameworkElement)window.FindName("KobPanel")).Visibility == Visibility.Visible &&
                ((FrameworkElement)window.FindName("IterationsBox")).Visibility == Visibility.Collapsed &&
                ((FrameworkElement)window.FindName("BailoutPanel")).Visibility == Visibility.Collapsed,
                "The window must show chemical controls rather than unrelated fractal fields.");
            ((Slider)window.FindName("KobThresholdSlider")).Value = .22;
            Check(window.CaptureState("ui").Kobayashi.Threshold == .22 && window.CaptureState("ui").Kobayashi.Field!.Step == state.Kobayashi.Field!.Step,
                "View changes must preserve the simulation time and concentrations.");
            ((TextBox)window.FindName("KobColdBox")).Text = "invalid";
            Check(window.CaptureState("draft").Kobayashi.Undercooling == state.Kobayashi.Undercooling, "Unapplied equation drafts must not corrupt saves or camera frames.");
            var rootForTest = (FrameworkElement)window.Content;
            rootForTest.Measure(new Size(960, 600)); rootForTest.Arrange(new Rect(0, 0, 960, 600)); rootForTest.UpdateLayout();
            var beforeStep = window.CaptureState("before").Kobayashi.Field!;
            ((Button)window.FindName("KobStepButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Idle();
            Check(Pending() is not null && window.CaptureState("during").Kobayashi.Field!.Step == beforeStep.Step,
                "Saves must retain the displayed field until the computed frame is shown.");
            await Display();
            Check(window.CaptureState("after").Kobayashi.Field!.Step == beforeStep.Step + 32 && !Field<bool>("_kobRunning"),
                "One frame must advance exactly the selected number of steps and stay paused.");
            ((Button)window.FindName("KobPlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Invoke("PauseKobayashi"); await Idle();
            Check(!Field<bool>("_kobRunning"), "Pausing during computation must terminate the live simulation.");
            if (Pending() is not null) await Display();
            Check(Pending() is null, "Steps submitted before a pause must be published and shown.");
            ((Button)window.FindName("KobPlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Invoke("PauseKobayashi");
            ((Button)window.FindName("KobPlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Idle();
            Check(Field<bool>("_kobRunning") && Pending() is not null,
                "Rapid pause/resume during a canceled batch must resume computation.");
            Invoke("PauseKobayashi"); await Display(); await Idle();
            if (Pending() is not null) await Display();

            // The brush edits the GPU field in place without advancing time.
            var beforeBrush = window.CaptureState("brush-before").Kobayashi.Field!;
            Invoke("QueueKobBrush", .8, .5, .5);
            await Idle(); await Display();
            var afterBrush = window.CaptureState("brush-after").Kobayashi.Field!;
            Check(afterBrush.Step == beforeBrush.Step && KobDifference(afterBrush, beforeBrush) > .00001,
                "The central brush must change the displayed field without advancing time.");

            var saved = window.CaptureState("Saved live frame");
            store.Save(saved);
            var reloaded = store.Load().Single(s => s.SaveName == "Saved live frame");
            Check(KobDifference(reloaded.Kobayashi.Field!, afterBrush) == 0 && reloaded.Kobayashi.Field!.Step == afterBrush.Step,
                "Saving from the window must store the exact displayed GPU field.");

            window.LoadState(state); await Idle(); await Display();
            if (output is not null)
            {
                window.CanvasImage.Source = await renderer.RenderAsync(state, 720, 640, null, CancellationToken.None);
                var root = (FrameworkElement)window.Content;
                foreach (var size in new[] { new Size(1180, 800), new Size(960, 600) })
                {
                    root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                    Save(bitmap, Path.Combine(output, $"window-{size.Width}.png"));
                }
                Save(await renderer.RenderAsync(cut, 600, 600, null, CancellationToken.None), Path.Combine(output, "cut.png"));
            }
        }
        finally { window.Close(); }
        Console.WriteLine("PASS (kobayashi3d): independent energy derivative, thermal balance, insulated faces, slots, exact continuation, five presets, nine styles, cuts, probe, saves/cloud and WPF.");
        static byte[] Pixels(BitmapSource b) { var p=new byte[b.PixelWidth*b.PixelHeight*4];b.CopyPixels(p,b.PixelWidth*4,0);return p; }
        static void Save(BitmapSource b,string path) {var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(b));using var f=File.Create(path);e.Save(f);}
    }
    private static double KobDifference(Kobayashi3DField a,Kobayashi3DField b)=>MaxArrayDifference(a.Concentrations.ToArray(),b.Concentrations.ToArray());
    private static double KobEnthalpy(Kobayashi3DField f,double heat)
    {var v=f.Concentrations.ToArray();double sum=0;for(int i=0;i<v.Length;i+=2)sum+=v[i+1]-heat*v[i];return sum/(v.Length/2);}
    // Independent oracle: numerical energy derivative and conservative accumulation through faces.
    private static double[] KobReference(float[] values,int n,Kobayashi3DSettings s)
    {
        var div=new double[n*n*n];var lap=new double[div.Length];
        int I(int x,int y,int z)=>(Math.Clamp(z,0,n-1)*n+Math.Clamp(y,0,n-1))*n+Math.Clamp(x,0,n-1);
        double V(int[] p,int c=0)=>values[I(p[0],p[1],p[2])*2+c];
        double Energy(double[] g) {double r=g.Sum(x=>x*x);if(r<1e-18)return 0;double a=1-3*s.Anisotropy+4*s.Anisotropy*g.Sum(x=>x*x*x*x)/(r*r);return .5*s.InterfaceWidth*s.InterfaceWidth*a*a*r;}
        for(int z=0;z<n;z++)for(int y=0;y<n;y++)for(int x=0;x<n;x++)for(int axis=0;axis<3;axis++)
        {
            int[] p=[x,y,z];if(p[axis]==n-1)continue;int[] q=(int[])p.Clone();q[axis]++;
            var g=new double[3];g[axis]=V(q)-V(p);
            for(int t=0;t<3;t++)if(t!=axis)
            {int[] ph=(int[])p.Clone(),pl=(int[])p.Clone(),qh=(int[])q.Clone(),ql=(int[])q.Clone();ph[t]++;pl[t]--;qh[t]++;ql[t]--;g[t]=.25*(V(ph)-V(pl)+V(qh)-V(ql));}
            const double h=1e-6;g[axis]+=h;double plus=Energy(g);g[axis]-=2*h;double minus=Energy(g);
            double flux=(plus-minus)/(2*h);int a=I(x,y,z),b=I(q[0],q[1],q[2]);div[a]+=flux;div[b]-=flux;
            double temperature=V(q,1)-V(p,1);lap[a]+=temperature;lap[b]-=temperature;
        }
        var result=new double[values.Length];double dt=s.EffectiveTimeStep;
        for(int i=0;i<div.Length;i++)
        {double p=values[i*2],t=values[i*2+1],m=.9/Math.PI*Math.Atan(-10*t);double next=Math.Clamp(p+dt*s.Mobility*(div[i]+p*(1-p)*(p-.5+m)),0,1);result[i*2]=next;result[i*2+1]=t+dt*s.ThermalDiffusion*lap[i]+s.LatentHeat*(next-p);}
        return result;
    }
}
