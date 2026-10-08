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
    private static async Task VerifyLenia3DAsync(string[] args)
    {
        using var sandbox=DataSandbox.Create("lenia3d");
        string? output=args.Length>1 && !args[1].StartsWith("--")?args[1]:null; if(output is not null) Directory.CreateDirectory(output);
        using var renderer=new Fractal3DRenderer();
        if(args.Contains("--window-only"))
        {
            var state=Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Lenia3D);
            using var simulation=new Lenia3DGpuSimulation(renderer.DeviceHost,state.Lenia);
            simulation.Advance(240,CancellationToken.None);state.Lenia=state.Lenia with{Field=simulation.ReadCurrent()};
            await VerifyLeniaWindowAsync(state,output);Console.WriteLine("PASS (lenia3d window): lifecycle, search, undo, cancellation, load and close.");return;
        }
        const int n=32;
        var settings=new Lenia3DSettings {Size=n,Radius=5,ShellCount=3,Beta1=1,Beta2=.4,Beta3=.7,GrowthMean=.2,GrowthWidth=.05,WarmupSteps=0};
        var kernel=Lenia3DMath.Kernel(settings);
        Check(Math.Abs(kernel.Sum(v=>(double)v)-1)<1e-6,"Kernel is normalized.");
        Check(kernel[1]==kernel[n-1] && kernel[1]==kernel[n] && kernel[1]==kernel[n*n],"Kernel is radial with wrapped origin.");
        var input=new float[n*n*n]; var random=new Random(18);
        for(int i=0;i<input.Length;i++) input[i]=(float)(.1+.2*random.NextDouble());
        // Independent direct periodic stencil in double: no FFT or production kernel builder.
        var offsets=new List<(int X,int Y,int Z,double Weight)>(); double sum=0;
        for(int z=-5;z<=5;z++) for(int y=-5;y<=5;y++) for(int x=-5;x<=5;x++)
        {
            double r=Math.Sqrt(x*x+y*y+z*z)/5.0; if(r>=1) continue;
            double t=3*r; int shell=(int)t; t-=shell;
            double w=Math.Pow(4*t*(1-t),4)*new[]{1.0,.4,.7}[shell];
            if(w>0) {offsets.Add((x,y,z,w));sum+=w;}
        }
        foreach(var growth in Enum.GetValues<LeniaGrowth>())
        {
            var s=settings with {Growth=growth,Field=new(n,37,input,3.7)};
            using var simulation=new Lenia3DGpuSimulation(renderer.DeviceHost,s);
            var cpu=(float[])input.Clone();
            for(int step=0;step<3;step++)
            {
                var next=new float[cpu.Length];
                for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
                {
                    double u=0;
                    foreach(var k in offsets) u+=k.Weight/sum*cpu[(((z-k.Z+n)%n*n+(y-k.Y+n)%n)*n+(x-k.X+n)%n)];
                    double d=(u-s.GrowthMean)/s.GrowthWidth;
                    double g=growth==LeniaGrowth.Gaussian?2*Math.Exp(-.5*d*d)-1:2*Math.Pow(Math.Max(0,1-d*d/9),4)-1;
                    int i=(z*n+y)*n+x; next[i]=(float)Math.Clamp(cpu[i]+s.TimeStep*g,0,1);
                }
                cpu=next; simulation.Advance(1,CancellationToken.None);
                var field=simulation.ReadCurrent(); double error=0;
                for(int i=0;i<cpu.Length;i++) error=Math.Max(error,Math.Abs(cpu[i]-field.Cells[i]));
                Check(error<3e-5,$"{growth}: direct periodic convolution/growth differs from GPU by {error}.");
            }
            var shown=simulation.Publish(); var checkpoint=simulation.ReadCheckpoint(shown);
            simulation.Advance(4,CancellationToken.None); var pending=simulation.Publish(shown);
            Check(simulation.ReadCheckpoint(shown).Cells.SequenceEqual(checkpoint.Cells),"Pending work preserves the shown field.");
            var options=JsonOptionsFactory.Create();
            var decoded=JsonSerializer.Deserialize<Lenia3DField>(JsonSerializer.Serialize(checkpoint,options),options)!;
            Check(decoded.Cells.SequenceEqual(checkpoint.Cells) && decoded.Time==checkpoint.Time && decoded.Step==40,"Brotli preserves cells and time exactly.");
            using var resumed=new Lenia3DGpuSimulation(renderer.DeviceHost,s with {Field=decoded});
            resumed.Advance(4,CancellationToken.None);
            Check(resumed.ReadCurrent().Cells.SequenceEqual(simulation.ReadCheckpoint(pending).Cells),"Checkpoint resumes exactly on the same GPU.");
            using var cancel=new CancellationTokenSource(); cancel.Cancel(); long before=resumed.Step;
            Check(resumed.Advance(10,cancel.Token)==0 && resumed.Step==before,"Pre-cancelled simulation adds no steps.");
        }
        Console.WriteLine("Independent direct convolution, both growth rules, publication and exact continuation passed.");
        foreach(int size in new[]{32,64,128})
        {
            var uniform=new float[size*size*size]; Array.Fill(uniform,.2f);
            var s=settings with {Size=size,Field=new(size,0,uniform)};
            using var simulation=new Lenia3DGpuSimulation(renderer.DeviceHost,s); simulation.Advance(1,CancellationToken.None);
            Check(simulation.ReadCurrent().Cells.ToArray().All(v=>Math.Abs(v-.3)<1e-5),$"Grid {size}: uniform convolution stays uniform; growth changes mass.");
        }

        Fractal3DState? saved=null; int index=0;
        foreach(var preset in Fractal3DCatalog.GetPresets(Fractal3DKind.Lenia3D))
        {
            var watch=Stopwatch.StartNew(); using var simulation=new Lenia3DGpuSimulation(renderer.DeviceHost,preset.Lenia);
            simulation.Advance(preset.Lenia.WarmupSteps,CancellationToken.None);
            var early=simulation.ReadCurrent(); simulation.Advance(180,CancellationToken.None); var late=simulation.ReadCurrent();
            double mass=late.Cells.ToArray().Sum(v=>(double)v),activity=early.Cells.ToArray().Zip(late.Cells.ToArray(),(a,b)=>Math.Abs(a-b)).Sum();
            Check(mass>20 && mass/late.Cells.Length<.1,$"Preset {preset.SaveName} survives without filling the volume; mass={mass}.");
            Check(activity>1,$"Preset {preset.SaveName} evolves, instead of using a static display.");
            preset.Lenia=preset.Lenia with {Live=simulation.Publish()};
            var image=await renderer.RenderAsync(preset,480,480,null,CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(image),480,480),$"Preset {preset.SaveName} is empty.");
            if(output is not null) PhysarumSavePng(image,Path.Combine(output,$"preset-{index}.png"));
            if(index++==0) {saved=preset.Clone();saved.Lenia=preset.Lenia with {Live=null,Field=late};}
            Console.WriteLine($"{preset.SaveName}: mass {mass:F1}, activity {activity:F1}, {watch.Elapsed.TotalSeconds:F2}s");
        }
        var sample=saved!;
        foreach(var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            sample.ShadingStyle=style; var image=await renderer.RenderAsync(sample,200,200,null,CancellationToken.None);
            if(output is not null) PhysarumSavePng(image,Path.Combine(output,$"style-{style}.png"));
            Check(HasFractal3DStructure(Pixels(image),200,200),$"Lenia style {style} renders.");
        }
        sample.ShadingStyle=Fractal3DShadingStyle.Glow;
        var cut=sample.Clone();cut.Lenia=cut.Lenia with {CutAxis=0};
        byte[] first=Pixels(await renderer.RenderAsync(sample,200,200,null,CancellationToken.None)).ToArray();
        byte[] second=Pixels(await renderer.RenderAsync(cut,200,200,null,CancellationToken.None)).ToArray();
        Check(!first.SequenceEqual(second),"Slice changes the visible field without changing the simulation.");
        var store=new Fractal3DSaveStore(Fractal3DKind.Lenia3D);store.Save(sample);var loaded=store.Load().Single();
        Check(loaded.Lenia.Field!.Cells.SequenceEqual(sample.Lenia.Field!.Cells),"Save store preserves full field.");
        Check(CloudSaveRepository.ListLocal(out _).Any(s=>s.Category=="Fractal3DLenia"),"Cloud repository includes Lenia.");
        Check(Lenia3DComputeShader.CacheEntries.All(e=>File.Exists(AppPaths.GetShaderCacheFile(e.Key))) && File.Exists(AppPaths.GetShaderCacheFile("lenia3d-pixel")),"All Lenia shaders use the common disk cache.");
        var rebuilt=new HashSet<string>();
        await Task.Run(()=>Fractal3DRenderer.RebuildShaderCache((done,total,key)=>rebuilt.Add(key)));
        Check(rebuilt.Count(key=>key.StartsWith("lenia3d-"))==5,"Rebuild includes every Lenia shader variant.");
        await VerifyLeniaSearchAsync(renderer,sample,output);
        await VerifyLeniaWindowAsync(sample,output);
        Console.WriteLine("PASS (lenia3d): direct convolution, growth, all grids, publication, exact continuation, five species, nine styles, saves/cloud/cache, search and WPF lifecycle.");
    }

    private static async Task VerifyLeniaSearchAsync(Fractal3DRenderer renderer,Fractal3DState sample,string? output)
    {
        var watch=Stopwatch.StartNew();
        var found=await Task.Run(()=>Lenia3DRandomizer.Search(renderer.DeviceHost,sample.Lenia,false,null,CancellationToken.None,42));
        Check(found is not null && found.Settings.Field is not null,"Random search finds a grown surviving form.");
        var state=sample.Clone();state.Lenia=found!.Settings;
        var image=await renderer.RenderAsync(state,480,480,null,CancellationToken.None);
        if(output is not null) PhysarumSavePng(image,Path.Combine(output,"search.png"));
        var nearby=await Task.Run(()=>Lenia3DRandomizer.Search(renderer.DeviceHost,sample.Lenia,true,null,CancellationToken.None,18));
        Check(nearby is not null && nearby.Settings.SeedShape==sample.Lenia.SeedShape && nearby.Settings.Field!.Step>sample.Lenia.Field!.Step,"Variation grows from the exact current field.");
        using var cts=new CancellationTokenSource(); var progress=new InlineLeniaProgress(_=>cts.Cancel());bool canceled=false;
        try {await Task.Run(()=>Lenia3DRandomizer.Search(renderer.DeviceHost,sample.Lenia,false,progress,cts.Token,2));}
        catch(OperationCanceledException){canceled=true;}
        Check(canceled,"Cancelled search never applies a partial candidate.");
        Console.WriteLine($"Search, variation and cancellation: {watch.Elapsed.TotalSeconds:F2}s.");
    }
    private sealed class InlineLeniaProgress(Action<LeniaSearchProgress> callback):IProgress<LeniaSearchProgress>
    { public void Report(LeniaSearchProgress p)=>callback(p); }

    private static async Task VerifyLeniaWindowAsync(Fractal3DState sample,string? output)
    {
        var theme=new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if(!Application.Current.Resources.MergedDictionaries.Any(d=>d.Source==theme)) Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=theme});
        var window=new Fractal3DWindow(Fractal3DKind.Lenia3D);var root=(FrameworkElement)window.Content;
        root.Measure(new Size(1180,800));root.Arrange(new Rect(0,0,1180,800));root.UpdateLayout();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Get<T>(string name)=>(T)typeof(Fractal3DWindow).GetField(name,flags)!.GetValue(window)!;
        void Click(string name)=>((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Task Search(bool variation)=>(Task)typeof(Fractal3DWindow).GetMethod("RunLeniaSearchAsync",flags)!.Invoke(window,[variation])!;
        async Task Display()
        {
            var watch=Stopwatch.StartNew();while(Get<bool>("_leniaBusy")&&watch.Elapsed.TotalSeconds<60) await Task.Delay(10);
            Check(!Get<bool>("_leniaBusy"),"Lenia simulation finishes.");
            var m=typeof(Fractal3DWindow).GetMethod("RenderFrameAsync",flags)!;
            await(Task)m.Invoke(window,[Enum.Parse(m.GetParameters()[0].ParameterType,"Full")])!;
        }
        try
        {
            await Display(); Check(!Get<bool>("_leniaPreparing")&&!Get<bool>("_leniaRunning"),"First shown frame ends preparation on pause.");
            window.LoadState(sample);await Display();var before=window.CaptureState("before").Lenia;
            ((TextBox)window.FindName("LeniaRadiusBox")).Text="invalid draft";
            ((Slider)window.FindName("LeniaLevelSlider")).Value=.3;
            Check(window.CaptureState("draft").Lenia.Field!.Cells.SequenceEqual(before.Field!.Cells),"Unapplied draft and threshold don't alter field.");
            Click("LeniaStepButton");await Display();
            Check(window.CaptureState("step").Lenia.Field!.Step==before.Field.Step+2,"One frame grows exactly two steps.");
            window.LoadState(sample);await Display();await Search(true);await Display();
            Check(Get<List<Lenia3DSettings>>("_leniaSearchHistory").Count==1,"Search applies settings and records exact history.");
            Click("LeniaSearchUndoButton");await Display();
            Check(window.CaptureState("undo").Lenia.Field!.Cells.SequenceEqual(before.Field.Cells),"Undo returns the exact shown cells.");
            var task=Search(false);Click("CancelButton");await task;
            Check(window.CaptureState("cancel").Lenia.Field!.Cells.SequenceEqual(before.Field.Cells),"Cancel preserves shown field.");
            task=Search(false);window.LoadState(sample);await task;await Display();
            Check(window.CaptureState("load").Lenia.Field!.Cells.SequenceEqual(before.Field.Cells),"Loaded state wins over stale search.");
            Check(!((TextBlock)window.FindName("LeniaSearchStatus")).Text.StartsWith("Ищем"),"Loaded state clears stale search status.");
            if(output is not null){var image=new RenderTargetBitmap(1180,800,96,96,PixelFormats.Pbgra32);image.Render(root);PhysarumSavePng(image,Path.Combine(output,"window.png"));}
            task=Search(false);window.Close();await task;
            Check(Get<CancellationTokenSource?>("_leniaSearchCts") is null,"Closing cancels search.");
        }
        finally{window.Close();}
    }
}
