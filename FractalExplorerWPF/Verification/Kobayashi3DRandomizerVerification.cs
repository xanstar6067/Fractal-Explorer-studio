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
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyKobayashiRandomAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("kobayashi-random");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        var settings = new Kobayashi3DSettings { CutAxis = 2, CutPosition = .3, StepsPerFrame = 7 };
        foreach (bool variation in new[] { false, true })
        {
            var a = new Random(84); var b = new Random(84);
            for (int i = 0; i < 200; i++)
            {
                var c = Kobayashi3DRandomizer.Candidate(settings, variation, a); c.Validate();
                Check(c == Kobayashi3DRandomizer.Candidate(settings, variation, b), "Candidates must reproduce with a fixed random seed.");
                Check(c.Size == settings.Size && c.StepsPerFrame == 7 && c.CutAxis == 2 && c.CutPosition == .3 && c.Field is null && c.Live is null,
                    "Candidates must preserve resolution/view and grow from a fresh initial state.");
                if (variation) Check(c.Seed == settings.Seed && c.SeedShape == settings.SeedShape &&
                    Math.Abs(Math.Log(c.Undercooling / settings.Undercooling)) <= .10001 &&
                    Math.Abs(Math.Log(c.SeedRadius / settings.SeedRadius)) <= .08001, "Variation must stay close to current physics and initial layout.");
            }
        }
        const int n = 48;
        Kobayashi3DField Shape(Func<double,double,double,bool> f)
        {
            var v = new float[n*n*n*2];
            for (int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
                v[((z*n+y)*n+x)*2] = f(x+.5-n/2.0,y+.5-n/2.0,z+.5-n/2.0) ? 1 : 0;
            return new(n, 6000, v);
        }
        Check(Kobayashi3DRandomizer.Measure(Shape((x,y,z)=>false),1).Score==0,"Empty/melted nuclei must be rejected.");
        Check(Kobayashi3DRandomizer.Measure(Shape((x,y,z)=>true),1).Score==0,"Filled volumes must be rejected.");
        Check(Kobayashi3DRandomizer.Measure(Shape((x,y,z)=>x*x+y*y+z*z<100),100).Score==0,"Round balls must be rejected.");
        var branched=Shape((x,y,z)=>(Math.Abs(x)<15 && y*y+z*z<9) || (Math.Abs(y)<15 && x*x+z*z<9) || (Math.Abs(z)<15 && x*x+y*y<9));
        Check(Kobayashi3DRandomizer.Measure(branched,100).Score>0,"A connected six-arm crystal must pass the shape filter.");
        Check(Kobayashi3DRandomizer.Measure(branched,Kobayashi3DRandomizer.SolidMass(branched)).Score==0,"A stalled field must not pass as growth.");
        Check(Kobayashi3DRandomizer.Measure(Shape((x,y,z)=>Math.Pow(Math.Abs(x)-8,2)+Math.Pow(Math.Abs(y)-8,2)+Math.Pow(Math.Abs(z)-8,2)<16),100).Score==0,
            "Multiple disconnected balls alone must not be classified as dendrites.");
        var seeded = settings with { SeedShape = Kobayashi3DSeed.RandomSpheres, SeedCount = 3, SeedRadius = .07, SeedSpread = .12 };
        using var renderer = new Fractal3DRenderer(); var host = renderer.DeviceHost;
        using(var a = new Kobayashi3DGpuSimulation(host,seeded))
        using(var b = new Kobayashi3DGpuSimulation(host,seeded))
        {
            Check(KobDifference(a.ReadCurrent(),b.ReadCurrent())==0,"Random nuclei must reproduce exactly.");
            b.Reset(seeded with{Seed=seeded.Seed+1});
            Check(KobDifference(a.ReadCurrent(),b.ReadCurrent())>0,"The seed must change initial positions.");
            b.Reset(seeded with{SeedCount=2}); Check(KobDifference(a.ReadCurrent(),b.ReadCurrent())>0,"Nucleus count must change the field.");
        }
        var old=JsonSerializer.Deserialize<Kobayashi3DSettings>("{}",JsonOptionsFactory.Create())!;
        Check(old.SeedRadius==.075 && old.SeedSpread==.18 && old.SeedCount==4,"Older JSON must preserve original seed geometry defaults.");
        using var original = new Kobayashi3DGpuSimulation(host,settings);
        original.Advance(32,CancellationToken.None);var shown=original.Publish();var before=original.ReadCheckpoint(shown);
        if(args.Contains("--probe") || args.Contains("--probe-variation"))
        {
            bool variation=args.Contains("--probe-variation");
            if(variation){var baseline=new Random(1729);for(int k=0;k<=5;k++)settings=Kobayashi3DRandomizer.Candidate(settings,false,baseline);}
            var rng=new Random(variation ? 42 : 1729);
            for(int i=0;i<8;i++)
            {
                var c=Kobayashi3DRandomizer.Candidate(settings,variation,rng);
                foreach(int size in new[]{48,64})
                {
                    var probe=c with{Size=size,WarmupSteps=(int)(c.WarmupSteps*Math.Pow(size/64.0,1.5))};
                    original.Reset(probe);double mass=Kobayashi3DRandomizer.SolidMass(original.ReadCurrent());
                    var watch=Stopwatch.StartNew();await Task.Run(()=>original.Advance(probe.WarmupSteps,CancellationToken.None));
                    var field=original.ReadCurrent();Console.WriteLine($"Probe {i} {size}: {c.SeedShape}, cold {c.Undercooling:F3}, delta {c.Anisotropy:F3}, heat {c.LatentHeat:F3}, steps {field.Step}, {Kobayashi3DRandomizer.Measure(field,mass)}, {watch.Elapsed.TotalSeconds:F1}s");
                    if(output is not null){var view=Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Kobayashi3D);view.Kobayashi=probe with{Field=field,CutAxis=0};PhysarumSavePng(await renderer.RenderAsync(view,320,320,null,CancellationToken.None),Path.Combine(output,$"probe-{i}-{size}.png"));}
                }
            }
            return;
        }
        if(args.Contains("--large"))
        {
            var large=settings with{Size=128};var watch=Stopwatch.StartNew();int last=-1;
            var progress=new InlineKobProgress(p=>{int percent=(int)(p.Fraction*10);if(p.Finalizing && percent!=last){last=percent;Console.WriteLine($"Full 128³ trial: {percent*10}% at {watch.Elapsed.TotalSeconds:F1}s");}});
            var selected=await Task.Run(()=>Kobayashi3DRandomizer.Search(host,large,false,progress,CancellationToken.None,1729));
            Check(selected is not null && selected.Settings.Field!.Size==128 && selected.Settings.Field.Step==selected.Settings.WarmupSteps,"Large-grid search must return its own validated full-resolution field.");
            Console.WriteLine($"PASS full-grid search 128³: {watch.Elapsed.TotalSeconds:F1}s, {selected!.Settings.SeedShape}, step {selected.Settings.WarmupSteps}");
            return;
        }
        var small=await Task.Run(()=>Kobayashi3DRandomizer.Search(host,settings with{Size=32},false,null,CancellationToken.None,1729));
        Check(small is not null && small.Settings.Field!.Size==32,"Minimum-grid search must find a validated growing form.");
        var clock=Stopwatch.StartNew();
        var found=await Task.Run(()=>Kobayashi3DRandomizer.Search(host,settings,false,null,CancellationToken.None,1729));
        Check(found is not null,"Search must find a validated grown crystal.");found!.Settings.Validate();
        Check(found.Checked==Kobayashi3DRandomizer.TrialCount && found.Settings.Field!.Size==settings.Size &&
            found.Settings.Field.Step==found.Settings.WarmupSteps,"The result must contain the full-resolution grown field without another warmup.");
        Check(KobDifference(before,original.ReadCheckpoint(shown))==0,"Search must preserve the original displayed field.");
        Console.WriteLine($"Random search: {clock.Elapsed.TotalSeconds:F2}s, {found.Settings.SeedShape}, step {found.Settings.Field!.Step}");
        var state=Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Kobayashi3D);state.Kobayashi=found.Settings with{CutAxis=0};state.CameraDistance=3.8;
        if(output is not null)PhysarumSavePng(await renderer.RenderAsync(state,600,600,null,CancellationToken.None),Path.Combine(output,"random.png"));
        var near=await Task.Run(()=>Kobayashi3DRandomizer.Search(host,found.Settings,true,null,CancellationToken.None,42));
        Check(near is not null && near.Settings.Seed==found.Settings.Seed && near.Settings.SeedShape==found.Settings.SeedShape,"Variation must find a nearby growing crystal with the same seed layout.");
        state.Kobayashi=near!.Settings with{CutAxis=0};
        if(output is not null)PhysarumSavePng(await renderer.RenderAsync(state,600,600,null,CancellationToken.None),Path.Combine(output,"variation.png"));
        using(var cancel=new CancellationTokenSource())
        {
            bool stopped=false;
            try{await Task.Run(()=>Kobayashi3DRandomizer.Search(host,settings,false,new InlineKobProgress(_=>cancel.Cancel()),cancel.Token,12));}
            catch(OperationCanceledException){stopped=true;}
            Check(stopped,"Cancellation during GPU trials must not return a partial result.");
        }
        await VerifyKobSearchWindowAsync(found.Settings,output);
        Console.WriteLine("PASS (kobayashi-random): reproducible nuclei/candidates, shape and growth filters, GPU search/variation, exact history, cancel, stale load and close.");
    }

    private sealed class InlineKobProgress(Action<KobayashiSearchProgress> action):IProgress<KobayashiSearchProgress>
    { public void Report(KobayashiSearchProgress value)=>action(value); }

    private static async Task VerifyKobSearchWindowAsync(Kobayashi3DSettings settings,string? output)
    {
        var theme=new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if(!Application.Current.Resources.MergedDictionaries.Any(d=>d.Source==theme))Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=theme});
        var window=new Fractal3DWindow(Fractal3DKind.Kobayashi3D);var root=(FrameworkElement)window.Content;
        root.Measure(new Size(1180,800));root.Arrange(new Rect(0,0,1180,800));root.UpdateLayout();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Get<T>(string name)=>(T)typeof(Fractal3DWindow).GetField(name,flags)!.GetValue(window)!;
        void Click(string name)=>((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Task Search(bool variation)=>(Task)typeof(Fractal3DWindow).GetMethod("RunKobSearchAsync",flags)!.Invoke(window,[variation])!;
        async Task Display()
        {
            var watch=Stopwatch.StartNew();while(Get<bool>("_kobBusy") && watch.Elapsed.TotalSeconds<120)await Task.Delay(10);
            Check(!Get<bool>("_kobBusy"),"Window simulation must finish.");
            var m=typeof(Fractal3DWindow).GetMethod("RenderFrameAsync",flags)!;
            await (Task)m.Invoke(window,[Enum.Parse(m.GetParameters()[0].ParameterType,"Full")])!;
        }
        try
        {
            await Display();var state=Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Kobayashi3D);state.Kobayashi=settings;
            window.LoadState(state);await Display();var beforeView=window.CaptureState("before");var initial=beforeView.Kobayashi;
            ((TextBox)window.FindName("KobColdBox")).Text="invalid draft";
            await Search(true);await Display();
            Check(Get<List<(Kobayashi3DSettings Settings, Fractal3DPose Pose)>>("_kobSearchHistory").Count==1,"Search must use applied parameters despite invalid drafts.");
            Check(((Button)window.FindName("KobSearchUndoButton")).IsEnabled,"Successful search must enable undo.");
            var foundView=window.CaptureState("found");var found=foundView.Kobayashi;
            Check(foundView.CameraDistance>beforeView.CameraDistance,"Fitting must make room for a large found crystal.");
            Check(found.Field is not null && found.Field.Step==found.WarmupSteps,"Loading a search result must not grow it again.");
            if(output is not null)
            {
                var bmp=new RenderTargetBitmap(1180,800,96,96,PixelFormats.Pbgra32);root.UpdateLayout();bmp.Render(root);
                PhysarumSavePng(bmp,Path.Combine(output,"window.png"));
            }
            Click("KobSearchUndoButton");await Display();var restoredView=window.CaptureState("undo");var restored=restoredView.Kobayashi;
            Check(restoredView.CameraDistance==beforeView.CameraDistance && restoredView.TargetX==beforeView.TargetX &&
                restoredView.TargetY==beforeView.TargetY && restoredView.TargetZ==beforeView.TargetZ,"Undo must restore the view changed by fitting.");
            Check(KobDifference(restored.Field!,initial.Field!)==0 && restored.Field!.Step==initial.Field!.Step &&
                restored.Undercooling==initial.Undercooling && restored.SeedRadius==initial.SeedRadius && restored.CutAxis==initial.CutAxis,
                "Undo must restore exact phase, temperature, time, seed geometry and view.");
            var searching=Search(false);
            Check(!((Button)window.FindName("KobPlayButton")).IsEnabled && ((Button)window.FindName("CancelButton")).IsEnabled,"Search must disable evolution and enable general cancellation.");
            Click("CancelButton");await searching;await Display();
            Check(KobDifference(window.CaptureState("cancel").Kobayashi.Field!,initial.Field!)==0,"Cancellation must preserve the shown crystal.");
            searching=Search(false);window.LoadState(state);await searching;await Display();
            Check(Get<List<(Kobayashi3DSettings Settings, Fractal3DPose Pose)>>("_kobSearchHistory").Count==0 &&
                KobDifference(window.CaptureState("load").Kobayashi.Field!,settings.Field!)==0,"Loading must win over an older search result.");
            searching=Search(false);window.Close();await searching;
            Check(Get<CancellationTokenSource?>("_kobSearchCts") is null,"Closing must cancel and release search work.");
        }
        finally{window.Close();}
    }
}
