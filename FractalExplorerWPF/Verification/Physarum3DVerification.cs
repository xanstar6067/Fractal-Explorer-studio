using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
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
    private static async Task VerifyPhysarum3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("physarum3d");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        using var renderer = new Fractal3DRenderer();
        if(args.Contains("--probe"))
        {
            var state=Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Physarum3D);
            foreach(int sensor in new[] {2,3,4,6})
            {
                state.ColorScale=1;
                state.Physarum=new() {Size=128,AgentCount=16384,SensorDistance=sensor,Exposure=.12,Threshold=.35};
                using var simulation=new Physarum3DGpuSimulation(renderer.DeviceHost,state.Physarum);
                foreach(int stage in new[] {80,160})
                {
                    simulation.Advance(stage,CancellationToken.None);
                    state.Physarum=state.Physarum with {Live=simulation.Publish(state.Physarum.Live)};
                    var image=await renderer.RenderAsync(state,640,640,null,CancellationToken.None);
                    if(output is not null) PhysarumSavePng(image,Path.Combine(output,$"sensor-{sensor}-stage-{simulation.Step}.png"));
                }
            }
            return;
        }
        const int n = 48;
        var settings = new Physarum3DSettings { Size = n, AgentCount = 4096, WarmupSteps = 0 };
        using var gpu = new Physarum3DGpuSimulation(renderer.DeviceHost, settings);
        gpu.Advance(1, CancellationToken.None);
        var first = gpu.ReadCurrent();
        double expectedMass = settings.AgentCount * Math.Round(settings.Deposit*256)/256 * (1-settings.Decay);
        Check(Math.Abs(first.Trail.ToArray().Sum(v => (double)v)-expectedMass)<.01,
            "Atomic deposits preserve total mass even when agents collide.");
        gpu.Advance(1, CancellationToken.None);
        var second = gpu.ReadCurrent();
        // Independent face-flux reference in double, including reflecting faces and integer deposits.
        double[] reference = first.Trail.ToArray().Select(v => (double)v).ToArray();
        for (int z=0; z<n; z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
        {
            int a=(z*n+y)*n+x;
            foreach(int b in new[] {x+1<n?a+1:-1,y+1<n?a+n:-1,z+1<n?a+n*n:-1})
            {
                if(b<0) continue;
                double flux=(first.Trail[b]-first.Trail[a])*settings.Diffusion/6;
                reference[a]+=flux; reference[b]-=flux;
            }
        }
        for(int i=0;i<second.AgentCount;i++)
        {
            var a=second.AgentState;
            int cell=((int)a[i*8+2]*n+(int)a[i*8+1])*n+(int)a[i*8];
            reference[cell]+=Math.Round(settings.Deposit*256)/256;
        }
        double error=0;
        for(int i=0;i<reference.Length;i++) error=Math.Max(error,Math.Abs(second.Trail[i]-reference[i]*(1-settings.Decay)));
        Check(error<2e-5,$"GPU diffusion/decay differs from independent face flux: {error}");
        var before = gpu.Publish();
        gpu.Advance(11,CancellationToken.None);
        var pending = gpu.Publish(before);
        Check(gpu.ReadCheckpoint(before).Trail.SequenceEqual(second.Trail),"Pending frame must not alter shown trail.");
        Check(gpu.ReadCheckpoint(before).AgentState.SequenceEqual(second.AgentState),"Pending frame must not alter shown agents.");
        var checkpoint=gpu.ReadCheckpoint(pending);
        var options=JsonOptionsFactory.Create();
        var decoded=JsonSerializer.Deserialize<Physarum3DField>(JsonSerializer.Serialize(checkpoint,options),options)!;
        Check(decoded.Trail.SequenceEqual(checkpoint.Trail) && decoded.AgentState.SequenceEqual(checkpoint.AgentState),"Brotli checkpoint is lossless.");
        using var resumed = new Physarum3DGpuSimulation(renderer.DeviceHost, settings with { Field=decoded });
        gpu.Advance(7,CancellationToken.None); resumed.Advance(7,CancellationToken.None);
        var continued=gpu.ReadCurrent(); var restarted=resumed.ReadCurrent();
        Check(continued.Trail.SequenceEqual(restarted.Trail) && continued.AgentState.SequenceEqual(restarted.AgentState),"Exact continuation includes headings and step-dependent random steering.");
        using(var cts=new CancellationTokenSource())
        {
            cts.Cancel(); long step=resumed.Step;
            Check(resumed.Advance(100,cts.Token)==0 && resumed.Step==step,"Cancellation must not add steps.");
        }
        foreach(int size in new[] {48,64,96,128})
        {
            using var sized=new Physarum3DGpuSimulation(renderer.DeviceHost, settings with {Size=size});
            sized.Advance(2,CancellationToken.None);
            Check(sized.ReadCurrent().Trail.ToArray().Any(v=>v>0),$"Grid {size} evolves.");
        }
        var kind=Fractal3DKind.Physarum3D;
        var presets=Fractal3DCatalog.GetPresets(kind);
        Check(presets.Count==5 && FractalCatalog.Create().Single(i=>i.LaunchKey==Fractal3DCatalog.LaunchKey(kind)).IsThreeDimensional,"Catalog exposes five presets and 3D marker.");
        Fractal3DState? saved=null;
        for(int i=0;i<presets.Count;i++)
        {
            var state=presets[i].Clone();
            using var simulation=new Physarum3DGpuSimulation(renderer.DeviceHost,state.Physarum);
            var watch=Stopwatch.StartNew();
            await Task.Run(()=>simulation.Advance(state.Physarum.WarmupSteps,CancellationToken.None));
            state.Physarum=state.Physarum with {Live=simulation.Publish()};
            var image=await renderer.RenderAsync(state,400,400,null,CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(image),400,400),$"Preset {i} has no structure.");
            if(output is not null) PhysarumSavePng(image,Path.Combine(output,$"preset-{i}.png"));
            Console.WriteLine($"{state.SaveName}: {watch.Elapsed.TotalSeconds:F2}s");
            if(i==0) { saved=state.Clone(); saved.Physarum=state.Physarum with {Live=null,Field=simulation.ReadCheckpoint(state.Physarum.Live!)}; }
        }
        var sample=saved!;
        foreach(var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            sample.ShadingStyle=style;
            Check(HasFractal3DStructure(Pixels(await renderer.RenderAsync(sample,160,160,null,CancellationToken.None)),160,160),$"Empty style {style}");
        }
        sample.ShadingStyle=Fractal3DShadingStyle.Glow;
        byte[] full=Pixels(await renderer.RenderAsync(sample,160,160,null,CancellationToken.None)).ToArray();
        var cut=sample.Clone(); cut.Physarum=cut.Physarum with {CutAxis=3,CutPosition=0};
        byte[] cutPixels=Pixels(await renderer.RenderAsync(cut,160,160,null,CancellationToken.None)).ToArray();
        Check(!full.SequenceEqual(cutPixels),"Cut reveals the network interior.");
        var dim=sample.Clone(); dim.Physarum=dim.Physarum with {Exposure=.15};
        byte[] dimPixels=Pixels(await renderer.RenderAsync(dim,160,160,null,CancellationToken.None)).ToArray();
        Check(!full.SequenceEqual(dimPixels),"Exposure changes the view.");
        var store=new Fractal3DSaveStore(kind); store.Save(sample);
        var loaded=store.Load().Single();
        Check(loaded.Physarum.Field!.AgentState.SequenceEqual(sample.Physarum.Field!.AgentState),"Save store keeps agents.");
        Check(CloudSaveRepository.ListLocal(out _).Any(s=>s.Category=="Fractal3DPhysarum"),"Cloud sees Physarum category.");
        Check(Physarum3DComputeShader.CacheEntries.All(e=>File.Exists(AppPaths.GetShaderCacheFile(e.Key))) && File.Exists(AppPaths.GetShaderCacheFile("physarum3d-pixel")),"Compute/display use the common cache.");
        var rebuilt=new HashSet<string>();
        await Task.Run(()=>Fractal3DRenderer.RebuildShaderCache((done,total,key)=>rebuilt.Add(key)));
        Check(rebuilt.Count(key=>key.StartsWith("physarum3d-"))==4,"Rebuild shaders includes all Physarum variants.");
        await VerifyPhysarumWindowAsync(sample,output);
        Console.WriteLine("PASS (physarum3d): independent diffusion, mass, all grids, publication, exact continuation, presets, nine styles, saves/cloud/cache and WPF lifecycle.");
    }

    private static void PhysarumSavePng(BitmapSource image,string path)
    {
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream=File.Create(path); encoder.Save(stream);
    }

    private static async Task VerifyPhysarumWindowAsync(Fractal3DState state,string? output)
    {
        var theme=new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if(!Application.Current.Resources.MergedDictionaries.Any(d=>d.Source==theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=theme});
        var window=new Fractal3DWindow(Fractal3DKind.Physarum3D);
        var root=(FrameworkElement)window.Content;
        root.Measure(new Size(1180,800)); root.Arrange(new Rect(0,0,1180,800)); root.UpdateLayout();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Get<T>(string name)=>(T)typeof(Fractal3DWindow).GetField(name,flags)!.GetValue(window)!;
        async Task Idle()
        {
            var watch=Stopwatch.StartNew();
            while(Get<bool>("_physarumBusy") && watch.Elapsed.TotalSeconds<60) await Task.Delay(10);
            Check(!Get<bool>("_physarumBusy"),"Physarum GPU work must finish.");
        }
        async Task Display()
        {
            var m=typeof(Fractal3DWindow).GetMethod("RenderFrameAsync",flags)!;
            await (Task)m.Invoke(window,[Enum.Parse(m.GetParameters()[0].ParameterType,"Full")])!;
        }
        try
        {
            await Idle(); await Display();
            Check(!Get<bool>("_physarumPreparing") && Get<Physarum3DVolume>("_physarumShown").Step==80,"First frame finishes preparation on pause.");
            window.LoadState(state); await Idle(); await Display();
            var before=window.CaptureState("before").Physarum.Field!;
            Check(before.AgentState.SequenceEqual(state.Physarum.Field!.AgentState),"Window loads exact checkpoint.");
            Check(((FrameworkElement)window.FindName("PhysarumPanel")).Visibility==Visibility.Visible && ((FrameworkElement)window.FindName("IterationsBox")).Visibility==Visibility.Collapsed,"Dedicated panel hides unrelated controls.");
            ((TextBox)window.FindName("PhysarumSensorBox")).Text="invalid";
            ((Slider)window.FindName("PhysarumExposureSlider")).Value=.8;
            Check(window.CaptureState("draft").Physarum.Field!.Trail.SequenceEqual(before.Trail),"Unapplied parameters and brightness preserve the field.");
            ((Button)window.FindName("PhysarumStepButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            Check(window.CaptureState("pending").Physarum.Field!.Step==before.Step,"Save captures shown frame during pending work.");
            await Display(); Check(window.CaptureState("after").Physarum.Field!.Step==before.Step+4,"One frame advances four steps.");
            window.LoadState(Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Physarum3D));
            typeof(Fractal3DWindow).GetMethod("PhysarumStopPreparation_OnClick",flags)!.Invoke(window,[window,new RoutedEventArgs()]);
            await Idle(); await Display();
            Check(!Get<bool>("_physarumPreparing") && !Get<bool>("_physarumRunning") && window.CaptureState("partial").Physarum.Field is not null,"Stopping publishes a consistent partial field.");
            window.LoadState(state); await Idle(); await Display();
            if(output is not null)
            {
                var image=new RenderTargetBitmap(1180,800,96,96,PixelFormats.Pbgra32); image.Render(root);
                PhysarumSavePng(image,Path.Combine(output,"window.png"));
            }
        }
        finally {window.Close();}
    }
}
