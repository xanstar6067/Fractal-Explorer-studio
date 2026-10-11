using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyExcitable3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("excitable3d");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        using var renderer = new Fractal3DRenderer();
        var host = renderer.DeviceHost;
        foreach (var model in Enum.GetValues<Excitable3DModel>())
        foreach (var boundary in Enum.GetValues<Excitable3DBoundary>())
        {
            var s = Excitable3DSettings.Default(model) with { Size = 33, Boundary = boundary };
            var random = new Random(712); var initial = new float[33*33*33*2];
            for (int i=0;i<initial.Length;i+=2)
            {
                initial[i] = (float)(model == Excitable3DModel.FitzHughNagumo ? -1.1+random.NextDouble()*.2 : .2+random.NextDouble()*.2);
                initial[i+1] = (float)(model == Excitable3DModel.FitzHughNagumo ? -.5+random.NextDouble()*.2 : .1+random.NextDouble()*.1);
            }
            s = s with { Field = new(33, 0, initial) };
            using var sim = new Excitable3DGpuSimulation(host, s);
            double[] reference = initial.Select(v => (double)v).ToArray();
            for (int i=0;i<12;i++) reference = ExcitableReference(reference, s);
            sim.Advance(12, CancellationToken.None);
            double error = sim.ReadCurrent().Concentrations.ToArray().Zip(reference, (a,b) => Math.Abs(a-b)).Max();
            Console.WriteLine($"{model}/{boundary}: GPU vs independent double, max error {error:E2}");
            Check(error < 2e-5, "All four reactions and both boundary conditions must match independent double evolution.");
        }
        var kind = Fractal3DKind.Excitable3D;
        var state = Fractal3DCatalog.CreateDefaultState(kind);
        using var gpu = new Excitable3DGpuSimulation(host, state.Excitable);
        gpu.Advance(20, CancellationToken.None);
        var shown = gpu.Publish(); var snapshot = gpu.ReadCheckpoint(shown);
        gpu.Advance(7, CancellationToken.None); var replaced = gpu.Publish(shown);
        gpu.Advance(8, CancellationToken.None); gpu.Publish(shown);
        Check(gpu.ReadCheckpoint(shown).Concentrations.SequenceEqual(snapshot.Concentrations), "Publication must preserve the shown field during later evolution.");
        try { gpu.ReadCheckpoint(replaced); throw new Exception("Stale checkpoint accepted."); } catch (InvalidOperationException) { }
        using(var canceled = new CancellationTokenSource())
        {
            var before = gpu.ReadCurrent(); canceled.Cancel();
            Check(gpu.Advance(80, canceled.Token) == 0 && before.Concentrations.SequenceEqual(gpu.ReadCurrent().Concentrations), "Pre-canceled work must preserve fields and time.");
        }
        // A recovery field must survive stimulation, including periodic wrap at the boundary.
        using(var brush = new Excitable3DGpuSimulation(host, state.Excitable with { Boundary = Excitable3DBoundary.Periodic, SeedShape = Excitable3DSeed.Rest }))
        {
            var before = brush.ReadCurrent().Concentrations.ToArray(); brush.Inject(0,.5,.5,.12);
            var after = brush.ReadCurrent().Concentrations.ToArray();
            Check(after.Where((v,i) => i%2==1).SequenceEqual(before.Where((v,i) => i%2==1)), "Brush must preserve recovery memory.");
            Check(after[((32*64+32)*64)*2] == 1 && after[((32*64+32)*64+63)*2] == 1, "Periodic brush must reach both cube faces.");
        }
        foreach(var model in Enum.GetValues<Excitable3DModel>())
        {
            var s = Excitable3DSettings.Default(model) with { Size=32, SeedShape=Excitable3DSeed.Rest };
            using var rest = new Excitable3DGpuSimulation(host,s);
            var before=rest.ReadCurrent(); rest.Advance(50,CancellationToken.None);
            Check(MaxArrayDifference(before.Concentrations.ToArray(), rest.ReadCurrent().Concentrations.ToArray()) < 3e-5, "The analytical rest state must remain at rest.");
        }
        foreach(int side in new[]{32,128})
        {
            using var sized = new Excitable3DGpuSimulation(host, state.Excitable with { Size=side, SeedShape=Excitable3DSeed.Rest });
            sized.Inject(.5,.5,.5,.08); sized.Advance(2,CancellationToken.None);
            Check(sized.ReadCurrent().Size==side && sized.Step==2, "All grid sizes must evolve.");
        }
        var options = JsonOptionsFactory.Create();
        state.Excitable = state.Excitable with { Field=snapshot }; state.SaveName="Exact scroll";
        string json=JsonSerializer.Serialize(state,options);
        var restored=JsonSerializer.Deserialize<Fractal3DState>(json,options)!;
        Check(restored.Excitable.Field!.Concentrations.SequenceEqual(snapshot.Concentrations) && restored.Excitable.Field.Step==snapshot.Step && !json.Contains("\"Live\""), "Lossless JSON must retain fields/time without GPU handles.");
        using(var a=new Excitable3DGpuSimulation(host,state.Excitable))
        using(var b=new Excitable3DGpuSimulation(host,restored.Excitable))
        {
            a.Advance(30,CancellationToken.None); b.Advance(30,CancellationToken.None);
            Check(a.ReadCurrent().Concentrations.SequenceEqual(b.ReadCurrent().Concentrations), "Disk continuation on the same GPU must be bit exact.");
        }
        var store=new Fractal3DSaveStore(kind); store.Save(state);
        Check(store.Load().Single().Excitable.Field!.Concentrations.SequenceEqual(snapshot.Concentrations), "Save store must retain the full checkpoint.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().Category=="Fractal3DExcitable" && unreadable==0, "Cloud must recognize the new category.");
        var presets=Fractal3DCatalog.GetPresets(kind);
        foreach(var preset in presets)
        {
            using var sim=new Excitable3DGpuSimulation(host,preset.Excitable);
            await Task.Run(() => sim.Advance(preset.Excitable.InitialSteps,CancellationToken.None));
            var early=sim.ReadCurrent();
            await Task.Run(() => sim.Advance(400,CancellationToken.None));
            var late=sim.ReadCurrent();
            var values=late.Concentrations.ToArray();
            double uMax=values.Where((v,i)=>i%2==0).Max(),uMin=values.Where((v,i)=>i%2==0).Min();
            double change=MaxArrayDifference(early.Concentrations.ToArray(),values);
            Console.WriteLine($"{preset.SaveName}: U [{uMin:F3},{uMax:F3}], change {change:F3}");
            Check(uMax-uMin>.2 && change>.01, "Every preset must retain spatially structured evolving waves.");
            var view=preset.Clone(); view.Excitable= view.Excitable with { Field=early };
            var bitmap=await renderer.RenderAsync(view,280,280,null,CancellationToken.None);
            Check(Pixels(bitmap).Where((_,i)=>i%4!=3).Distinct().Count()>40,"Every preset must render a nonempty coloured volume.");
            if(output is not null) SaveExcitablePng(bitmap,Path.Combine(output,$"preset-{presets.ToList().IndexOf(preset)}.png"));
            if(ReferenceEquals(preset,presets[0])) state=view;
        }
        var liveState=state.Clone();
        using(var live=new Excitable3DGpuSimulation(host,state.Excitable))
        {
            liveState.Excitable=state.Excitable with { Field=null,Live=live.Publish() };
            var checkpoint=liveState.Clone(); checkpoint.Excitable=state.Excitable with { Field=live.ReadCurrent() };
            var livePixels=Pixels(await renderer.RenderAsync(liveState,180,180,null,CancellationToken.None)).ToArray();
            var savedPixels=Pixels(await renderer.RenderAsync(checkpoint,180,180,null,CancellationToken.None)).ToArray();
            Check(livePixels.SequenceEqual(savedPixels),"GPU publication and disk snapshot must render identically.");
        }
        foreach(var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var view=state.Clone(); view.ShadingStyle=style;
            var bitmap=await renderer.RenderAsync(view,160,160,null,CancellationToken.None);
            if(output is not null) SaveExcitablePng(bitmap,Path.Combine(output,$"style-{(int)style}.png"));
        }
        var cut=state.Clone(); cut.Excitable=cut.Excitable with { CutAxis=3,CutPosition=0 };
        var cutPixels=Pixels(await renderer.RenderAsync(cut,180,180,null,CancellationToken.None)).ToArray();
        var wholePixels=Pixels(await renderer.RenderAsync(state,180,180,null,CancellationToken.None)).ToArray();
        Check(!cutPixels.SequenceEqual(wholePixels),"Cut must change the view without changing the field.");
        bool hit=false;
        for(int y=50;y<140 && !hit;y+=20) for(int x=50;x<140 && !hit;x+=20)
            hit=await renderer.ProbeDistanceAsync(state,x,y,180,180,CancellationToken.None)>0;
        Check(hit,"The surface probe must hit a wave front.");
        // A prescribed phase vortex must produce a filament; a constant rest field must not.
        var vortex=new float[32*32*32*2];
        for(int z=0;z<32;z++) for(int y=0;y<32;y++) for(int x=0;x<32;x++)
        {
            double angle=Math.Atan2(y-15.5,x-15.5); int i=((z*32+y)*32+x)*2;
            vortex[i]=(float)(.5+.2*Math.Cos(angle)); vortex[i+1]=(float)(.35+.2*Math.Sin(angle));
        }
        var vortexState=state.Clone(); vortexState.Excitable=new() { Size=32, Field=new(32,0,vortex), View=Excitable3DView.Filaments, Threshold=.15 };
        var restState=state.Clone(); restState.Excitable=new() { Size=32, SeedShape=Excitable3DSeed.Rest, View=Excitable3DView.Filaments, Threshold=.15 };
        byte[] vortexPixels=Pixels(await renderer.RenderAsync(vortexState,200,200,null,CancellationToken.None)).ToArray();
        byte[] restPixels=Pixels(await renderer.RenderAsync(restState,200,200,null,CancellationToken.None)).ToArray();
        Check(vortexPixels.Zip(restPixels,(a,b)=>a!=b?1:0).Sum()>100,"Phase winding must find a known vortex and leave rest empty.");
        await VerifyExcitableWindow(state, output);
        Console.WriteLine("Excitable 3D: PASS");
    }

    private static void SaveExcitablePng(System.Windows.Media.Imaging.BitmapSource bitmap,string path)
    {
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream=File.Create(path); encoder.Save(stream);
    }

    // Independently indexed double-precision seven-point stencil and four published equations.
    private static double[] ExcitableReference(double[] f, Excitable3DSettings s)
    {
        int n=s.Size; var result=new double[f.Length];
        int Bound(int v)=>s.Boundary==Excitable3DBoundary.Periodic ? (v+n)%n : Math.Clamp(v,0,n-1);
        int At(int x,int y,int z,int c)=>((Bound(z)*n+Bound(y))*n+Bound(x))*2+c;
        for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
        {
            int i=At(x,y,z,0); double u=f[i],v=f[i+1]; var lap=new double[2];
            for(int c=0;c<2;c++) lap[c]=(f[At(x-1,y,z,c)]+f[At(x+1,y,z,c)]+f[At(x,y-1,z,c)]+f[At(x,y+1,z,c)]+f[At(x,y,z-1,c)]+f[At(x,y,z+1,c)]-6*f[i+c])/(s.Spacing*s.Spacing);
            double ru,rv;
            switch(s.Model)
            {
                case Excitable3DModel.Oregonator:
                    double loss=s.A*v/(u+s.B);
                    result[i]=(u+s.TimeStep*(s.DiffusionU*lap[0]+(u+loss*s.B)/s.Epsilon))/(1+s.TimeStep*(u+loss)/s.Epsilon);
                    result[i+1]=v+s.TimeStep*(s.DiffusionV*lap[1]+u-v); continue;
                case Excitable3DModel.FitzHughNagumo: ru=(u-u*u*u/3-v)/s.Epsilon; rv=s.Epsilon*(u+s.A-s.B*v); break;
                case Excitable3DModel.AlievPanfilov: ru=-s.B*u*(u-s.A)*(u-1)-u*v; rv=-(s.Epsilon+s.Mu1*v/(u+s.Mu2))*(v+s.B*u*(u-s.A-1)); break;
                default: ru=u*(1-u)*(u-(v+s.B)/s.A)/s.Epsilon; rv=u-v; break;
            }
            result[i]=u+s.TimeStep*(s.DiffusionU*lap[0]+ru); result[i+1]=v+s.TimeStep*(s.DiffusionV*lap[1]+rv);
        }
        return result;
    }

    private static async Task VerifyExcitableWindow(Fractal3DState state,string? output)
    {
        var theme=new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml",UriKind.Absolute);
        if(!Application.Current.Resources.MergedDictionaries.Any(d=>d.Source==theme)) Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=theme});
        var window=new Fractal3DWindow(Fractal3DKind.Excitable3D);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(Fractal3DWindow).GetField(name,flags)!.GetValue(window)!;
        object? Call(string name,params object[] parameters)=>typeof(Fractal3DWindow).GetMethod(name,flags)!.Invoke(window,parameters);
        async Task Display()
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(Field<bool>("_exBusy") && watch.ElapsedMilliseconds<30000) await Task.Delay(10);
            Check(!Field<bool>("_exBusy"),"Window must finish preparation or cancel.");
            var method=typeof(Fractal3DWindow).GetMethod("RenderFrameAsync",flags)!;
            await (Task)method.Invoke(window,[Enum.Parse(method.GetParameters()[0].ParameterType,"Full")])!;
        }
        try
        {
            await Display(); window.LoadState(state); await Display();
            var loaded=window.CaptureState("loaded").Excitable;
            Check(loaded.Field!.Concentrations.SequenceEqual(state.Excitable.Field!.Concentrations),"WPF must show the exact loaded checkpoint on pause.");
            Check(((FrameworkElement)window.FindName("ExPanel")).Visibility==Visibility.Visible && ((FrameworkElement)window.FindName("IterationsBox")).Visibility==Visibility.Collapsed,"Only relevant simulation controls must be visible.");
            foreach(var model in Enum.GetValues<Excitable3DModel>())
            {
                ((ComboBox)window.FindName("ExModelBox")).SelectedIndex=(int)model;
                Check(window.CaptureState("draft").Excitable.Model==state.Excitable.Model,"Model drafts must leave the shown equation intact until Apply.");
            }
            ((ComboBox)window.FindName("ExModelBox")).SelectedIndex=0;
            ((TextBox)window.FindName("ExABox")).Text="invalid";
            Call("ExRestart_OnClick",window,new RoutedEventArgs());
            Check(window.CaptureState("invalid").Excitable.Field!.Concentrations.SequenceEqual(loaded.Field.Concentrations),"Invalid Apply must preserve the shown field.");
            Call("ExStep_OnClick",window,new RoutedEventArgs()); await Display();
            Check(window.CaptureState("step").Excitable.Field!.Step==loaded.Field.Step+loaded.StepsPerFrame,"One frame must advance the displayed snapshot exactly once.");
            var before=window.CaptureState("before").Excitable.Field!;
            Call("QueueExBrush",.5,.5,.5); await Display();
            Check(window.CaptureState("brush").Excitable.Field!.Step==before.Step,"Brush must not advance time.");
            ((ComboBox)window.FindName("ExViewBox")).SelectedIndex=1;
            Check(window.CaptureState("filaments").Excitable.View==Excitable3DView.Filaments,"Filament view must reach the shared render state.");
            window.LoadState(Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Excitable3D));
            Call("PauseExcitable"); await Display();
            Check(window.CaptureState("partial").Excitable.Field is not null && !Field<bool>("_exRunning"),"Stopping preparation must publish a usable partial field on pause.");
            // A second load while an old task is in flight must win.
            window.LoadState(state); window.LoadState(state); await Display();
            Check(window.CaptureState("reload").Excitable.Field!.Concentrations.SequenceEqual(state.Excitable.Field!.Concentrations),"A stale work result must never replace the new load.");
        }
        finally { Call("CloseExcitable"); window.Close(); }
    }
}
