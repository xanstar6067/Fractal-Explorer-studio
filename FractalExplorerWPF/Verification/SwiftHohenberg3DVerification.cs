using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifySwiftHohenberg3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("swift-hohenberg3d");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        using var renderer = new Fractal3DRenderer(); var host = renderer.DeviceHost;
        var kind = Fractal3DKind.SwiftHohenberg3D; var state = Fractal3DCatalog.CreateDefaultState(kind);
        foreach(var invalid in new SwiftHohenberg3DSettings[] { new() { Size=96 }, new() { R=double.NaN },
            new() { Quadratic=2 }, new() { WaveNumber=1.5 }, new() { TimeStep=.1 }, new() { Spacing=2 },
            new() { DisplayRange=0 }, new() { SeedShape=(SwiftHohenberg3DSeed)99 } })
        {
            try { invalid.Validate(); throw new Exception("Invalid parameters accepted."); } catch(InvalidOperationException) { }
        }
        const int n=32; var initial=new float[n*n*n];
        for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            initial[(z*n+y)*n+x]=(float)(.05+.12*Math.Cos(Math.Tau*(2*x+3*y+z)/n)+.07*Math.Cos(Math.Tau*(3*x-y+2*z)/n));
        var s=new SwiftHohenberg3DSettings { Size=n, R=.4, Quadratic=.5, WaveNumber=.9, Spacing=.8, TimeStep=.04, Field=new(n,17,initial) };
        using var gpu=new SwiftHohenberg3DGpuSimulation(host,s);
        double[] reference=initial.Select(v=>(double)v).ToArray(); double energy=SwiftEnergy(reference,s);
        for(int step=0;step<5;step++)
        {
            reference=SwiftReferenceStep(reference,s); gpu.Advance(1,CancellationToken.None);
            var field=gpu.ReadCurrent(); double error=field.Samples.ToArray().Zip(reference,(a,b)=>Math.Abs(a-b)).Max();
            double nextEnergy=SwiftEnergy(field.Samples.ToArray().Select(v=>(double)v).ToArray(),s);
            Console.WriteLine($"Step {step}: GPU/direct DFT error {error:E2}, energy {energy:F5} -> {nextEnergy:F5}");
            Check(error<3e-6,"ETD1, cubic Galerkin projection and 3D FFT must match independent direct double DFT.");
            Check(nextEnergy<energy,"Free energy must decrease for this evolving gradient-flow field."); energy=nextEnergy;
        }
        // Analytic nonlinear Fourier coefficients check both FFT layouts and all supported grids.
        foreach(int side in new[]{32,64,128}) foreach(bool tiled in new[]{false,true})
        {
            const double mean=.12, amplitude=.2; var wave=new float[side*side*side]; var expected=new float[wave.Length];
            var rule=s with { Size=side, Field=null };
            double fundamental=Math.Pow(Math.Tau/(side*rule.Spacing),2)*6;
            double[] nonlinear=[rule.Quadratic*(mean*mean+amplitude*amplitude/2)-Math.Pow(mean,3)-1.5*mean*amplitude*amplitude,
                2*rule.Quadratic*mean*amplitude-3*mean*mean*amplitude-.75*Math.Pow(amplitude,3),
                .5*rule.Quadratic*amplitude*amplitude-1.5*mean*amplitude*amplitude,-.25*Math.Pow(amplitude,3)];
            double[] coefficients=new double[4];
            for(int j=0;j<4;j++) {
                double lambda=rule.R-Math.Pow(rule.WaveNumber*rule.WaveNumber-j*j*fundamental,2);
                coefficients[j]=Math.Exp(rule.TimeStep*lambda)*(j==0 ? mean : j==1 ? amplitude : 0)+SwiftWeight(lambda,rule.TimeStep)*nonlinear[j];
            }
            for(int z=0;z<side;z++) for(int y=0;y<side;y++) for(int x=0;x<side;x++) {
                int i=(z*side+y)*side+x; double phase=Math.Tau*(x+2*y+z)/side;
                wave[i]=(float)(mean+amplitude*Math.Cos(phase)); expected[i]=(float)(coefficients[0]+coefficients[1]*Math.Cos(phase)+coefficients[2]*Math.Cos(2*phase)+coefficients[3]*Math.Cos(3*phase));
            }
            using var sim=new SwiftHohenberg3DGpuSimulation(host,rule with { Field=new(side,0,wave) },false,tiled);
            sim.Advance(1,CancellationToken.None);
            Check(MaxArrayDifference(expected,sim.ReadCurrent().Samples.ToArray())<3e-6,"Both FFT layouts must match analytic cubic harmonics on every grid.");
        }
        foreach(double r in new[]{-.2,0,.45})
        {
            // Preferred linear mode: lambda=r, including a neutral mode where the ETD weight needs a limit.
            double q=Math.Tau*3/n; var wave=new float[n*n*n]; const double amplitude=1e-5;
            for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++) wave[(z*n+y)*n+x]=(float)(amplitude*Math.Cos(Math.Tau*3*x/n));
            var rule=new SwiftHohenberg3DSettings { Size=n, R=r, WaveNumber=q, TimeStep=.05, Field=new(n,0,wave) };
            using var sim=new SwiftHohenberg3DGpuSimulation(host,rule); sim.Advance(100,CancellationToken.None);
            var actual=sim.ReadCurrent().Samples.ToArray();
            double error=actual.Zip(wave,(a,b)=>Math.Abs(a-b*Math.Exp(5*r))).Max();
            Check(error<2e-8,"Linear growth/decay and the neutral mode must match the analytic dispersion relation.");
        }
        double UniformError(double dt)
        {
            var rule=new SwiftHohenberg3DSettings { Size=n, R=1, WaveNumber=.6, TimeStep=dt, Field=new(n,0,Enumerable.Repeat(.2f,n*n*n).ToArray()) };
            using var sim=new SwiftHohenberg3DGpuSimulation(host,rule); sim.Advance((int)Math.Round(2/dt),CancellationToken.None);
            double g=rule.R-Math.Pow(rule.WaveNumber,4), e=Math.Exp(4*g), exact=.2*Math.Sqrt(e/(1+.04/g*(e-1)));
            return sim.ReadCurrent().Samples.ToArray().Max(v=>Math.Abs(v-exact));
        }
        double coarse=UniformError(.05), fine=UniformError(.025);
        Console.WriteLine($"Analytic homogeneous solution: dt=.05 error {coarse:E2}, dt=.025 error {fine:E2}");
        Check(fine<coarse*.7 && fine<.01,"Time step refinement must converge to the analytic nonlinear solution.");
        foreach(int side in new[]{32,64,128})
        {
            double g=.9-Math.Pow(.7,4), b=.7, equilibrium=(b+Math.Sqrt(b*b+4*g))/2;
            using var sim=new SwiftHohenberg3DGpuSimulation(host,new() { Size=side, R=.9, WaveNumber=.7, Quadratic=b,
                Field=new(side,0,Enumerable.Repeat((float)equilibrium,side*side*side).ToArray()) });
            sim.Advance(10,CancellationToken.None);
            Check(sim.ReadCurrent().Samples.ToArray().All(v=>Math.Abs(v-equilibrium)<2e-6),"Nonzero homogeneous equilibrium must remain stationary.");
        }
        using(var mean=new SwiftHohenberg3DGpuSimulation(host,new() { Size=n, Noise=0, SeedShape=SwiftHohenberg3DSeed.Uniform }))
        {
            double before=mean.ReadCurrent().Samples.ToArray().Average(v=>(double)v); mean.Advance(10,CancellationToken.None);
            Check(Math.Abs(mean.ReadCurrent().Samples.ToArray().Average(v=>(double)v)-before)>.01,"Swift–Hohenberg must evolve its zero Fourier mode, without imposing conserved mass.");
        }
        using(var projected=new SwiftHohenberg3DGpuSimulation(host,new() { Size=n }))
        {
            var spectrum=CahnDft(projected.ReadCurrent().Samples.ToArray().Select(v=>new Complex(v,0)).ToArray(),n,false);
            double outside=0;
            for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
                if(Math.Max(Math.Max(Math.Min(x,n-x),Math.Min(y,n-y)),Math.Min(z,n-z))>=n/4) outside+=Math.Pow(spectrum[(z*n+y)*n+x].Magnitude,2);
            Check(outside/(n*n*n)<1e-9,"Initial noise must be projected into the alias-free cubic band.");
        }
        foreach(double r in new[]{-.5,1.0}) foreach(double b in new[]{-1.5,1.5})
        {
            using var sim=new SwiftHohenberg3DGpuSimulation(host,new() { Size=n, R=r, Quadratic=b, Noise=.3 });
            await Task.Run(()=>sim.Advance(1000,CancellationToken.None));
            Check(sim.ReadCurrent().Samples.ToArray().All(float.IsFinite),"Extreme valid parameters must keep finite fields during long runs.");
        }
        var shown=gpu.Publish(); var snapshot=gpu.ReadCheckpoint(shown);
        gpu.Advance(3,CancellationToken.None); var stale=gpu.Publish(shown); gpu.Advance(2,CancellationToken.None); gpu.Publish(shown);
        Check(gpu.ReadCheckpoint(shown).Samples.SequenceEqual(snapshot.Samples),"The displayed publication slot must survive later evolution.");
        try { gpu.ReadCheckpoint(stale); throw new Exception("Stale checkpoint accepted."); } catch(InvalidOperationException) { }
        using(var cancel=new CancellationTokenSource()) {
            cancel.Cancel(); var before=gpu.ReadCurrent();
            Check(gpu.Advance(100,cancel.Token)==0 && gpu.Step==before.Step && gpu.ReadCurrent().Samples.SequenceEqual(before.Samples),"Pre-cancelled work must preserve time and field.");
        }
        foreach(double target in new[]{-1.0,0,1.0})
        {
            using var brush=new SwiftHohenberg3DGpuSimulation(host,new() { Size=n, Noise=0, SeedShape=SwiftHohenberg3DSeed.Uniform });
            double[] expected=brush.ReadCurrent().Samples.ToArray().Select(v=>(double)v).ToArray();
            for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++) {
                double dx=(x+.5)/n; dx=Math.Min(dx,1-dx);
                double dy=Math.Abs((y+.5)/n-.5),dz=Math.Abs((z+.5)/n-.5);
                double radius=Math.Sqrt(dx*dx+dy*dy+dz*dz)/.12;
                if(radius>=1) continue;
                double weight=1-radius*radius*(3-2*radius); int i=(z*n+y)*n+x;
                expected[i]=expected[i]*(1-weight)+target*weight;
            }
            var spectrum=CahnDft(expected.Select(v=>new Complex(v,0)).ToArray(),n,false);
            for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
                if(Math.Max(Math.Max(Math.Min(x,n-x),Math.Min(y,n-y)),Math.Min(z,n-z))>=n/4) spectrum[(z*n+y)*n+x]=0;
            expected=CahnDft(spectrum,n,true).Select(v=>v.Real).ToArray();
            brush.Inject(0,.5,.5,.12,target); var f=brush.ReadCurrent(); var values=f.Samples.ToArray();
            int first=(16*n+16)*n, last=first+n-1;
            Check(values.Zip(expected,(a,b)=>Math.Abs(a-b)).Max()<3e-6 && Math.Abs(values[first]-values[last])<2e-6 && f.Step==0,
                "Periodic smooth brushes and their band projection must match independent direct DFT and preserve time.");
        }
        var options=JsonOptionsFactory.Create(); state.SwiftHohenberg=s with { Field=snapshot }; state.SaveName="Exact Swift–Hohenberg";
        string json=JsonSerializer.Serialize(state,options); var restored=JsonSerializer.Deserialize<Fractal3DState>(json,options)!;
        Check(restored.SwiftHohenberg.Field!.Step==snapshot.Step && restored.SwiftHohenberg.Field.Samples.SequenceEqual(snapshot.Samples) && !json.Contains("\"Live\""),"JSON must preserve the signed field and step without GPU objects.");
        using(var a=new SwiftHohenberg3DGpuSimulation(host,state.SwiftHohenberg))
        using(var b=new SwiftHohenberg3DGpuSimulation(host,restored.SwiftHohenberg)) {
            a.Advance(12,CancellationToken.None); b.Advance(12,CancellationToken.None);
            Check(a.ReadCurrent().Samples.SequenceEqual(b.ReadCurrent().Samples),"Continuation from disk on the same GPU must be bit exact.");
        }
        foreach(string corrupt in new[]{"{}", "{\"Size\":96,\"Step\":0}", "{\"Size\":32,\"Step\":-1}",
            "{\"Size\":32,\"Step\":0,\"Data\":\"!\"}", "{\"Size\":32,\"Step\":0,\"Data\":\"AAAA\"}"})
        { try { JsonSerializer.Deserialize<SwiftHohenberg3DField>(corrupt,options); throw new Exception("Corrupted checkpoint accepted."); } catch(JsonException) { } }
        var store=new Fractal3DSaveStore(kind); store.Save(state);
        Check(store.Load().Single().SwiftHohenberg.Field!.Samples.SequenceEqual(snapshot.Samples),"The standard save store must retain the full field.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().Category=="Fractal3DSwiftHohenberg" && unreadable==0,"The common cloud repository must recognize the category.");
        JsonSerializer.Deserialize<Fractal3DState>("{\"Kind\":0}",options)!.Clone().SwiftHohenberg.Validate();
        int index=0;
        foreach(var preset in Fractal3DCatalog.GetPresets(kind))
        {
            using var sim=new SwiftHohenberg3DGpuSimulation(host,preset.SwiftHohenberg);
            await Task.Run(()=>sim.Advance(preset.SwiftHohenberg.WarmupSteps,CancellationToken.None));
            var early=sim.ReadCurrent(); await Task.Run(()=>sim.Advance(100,CancellationToken.None)); var late=sim.ReadCurrent();
            var values=early.Samples.ToArray(); double change=MaxArrayDifference(values,late.Samples.ToArray());
            Console.WriteLine($"{preset.SaveName}: phi [{values.Min():F3},{values.Max():F3}], change {change:E2}");
            Check(values.Max()-values.Min()>.3 && change>1e-5,"Every preset must show a structured field that continues to evolve.");
            var view=preset.Clone(); view.SwiftHohenberg=view.SwiftHohenberg with { Field=early };
            var image=await renderer.RenderAsync(view,300,300,null,CancellationToken.None);
            var empty=view.Clone(); empty.SwiftHohenberg=empty.SwiftHohenberg with { Field=new(early.Size,0,new float[early.Samples.Length]) };
            byte[] pixels=CahnPixels(image), blank=CahnPixels(await renderer.RenderAsync(empty,300,300,null,CancellationToken.None));
            Check(pixels.Zip(blank,(a,b)=>a!=b?1:0).Sum()>500,"Each preset must render visible geometry, beyond the background gradient.");
            if(output is not null) CahnSaveImage(image,Path.Combine(output,$"preset-{index}.png"));
            if(index++==0) state=view;
        }
        using(var sim=new SwiftHohenberg3DGpuSimulation(host,state.SwiftHohenberg)) {
            var live=state.Clone(); live.SwiftHohenberg=live.SwiftHohenberg with { Field=null,Live=sim.Publish() };
            byte[] a=CahnPixels(await renderer.RenderAsync(live,180,180,null,CancellationToken.None));
            byte[] b=CahnPixels(await renderer.RenderAsync(state,180,180,null,CancellationToken.None));
            Check(a.SequenceEqual(b),"Live publication and a saved checkpoint must render identically.");
        }
        foreach(var style in Enum.GetValues<Fractal3DShadingStyle>()) {
            var view=state.Clone(); view.ShadingStyle=style; var image=await renderer.RenderAsync(view,180,180,null,CancellationToken.None);
            if(output is not null) CahnSaveImage(image,Path.Combine(output,$"style-{(int)style}.png"));
        }
        byte[] original=CahnPixels(await renderer.RenderAsync(state,180,180,null,CancellationToken.None));
        foreach(var viewMode in Enum.GetValues<SwiftHohenberg3DView>()) {
            var view=state.Clone(); view.SwiftHohenberg=view.SwiftHohenberg with { View=viewMode,Threshold=viewMode==SwiftHohenberg3DView.Interfaces ? .85 : .15 };
            var image=await renderer.RenderAsync(view,180,180,null,CancellationToken.None);
            if(viewMode!=SwiftHohenberg3DView.Positive) Check(!original.SequenceEqual(CahnPixels(image)),"Complementary phase and interface views must change geometry.");
            Check(Fractal3DRenderer.SameVolumeGeometry(state,view),"Presentation changes must reuse the evolved field.");
            if(output is not null) CahnSaveImage(image,Path.Combine(output,$"view-{(int)viewMode}.png"));
        }
        foreach(int axis in new[]{1,2,3}) {
            var view=state.Clone(); view.SwiftHohenberg=view.SwiftHohenberg with { CutAxis=axis,CutPosition=0 };
            byte[] pixels=CahnPixels(await renderer.RenderAsync(view,180,180,null,CancellationToken.None));
            Check(!original.SequenceEqual(pixels),"Every cut axis must affect the image.");
        }
        bool hit=false; for(int y=50;y<140 && !hit;y+=20) for(int x=50;x<140 && !hit;x+=20)
            hit=await renderer.ProbeDistanceAsync(state,x,y,180,180,CancellationToken.None)>0;
        Check(hit,"The common navigation probe must hit the pattern.");
        await VerifySwiftWindow(state);
        Check(SwiftHohenberg3DComputeShader.CacheEntries.All(e=>File.Exists(AppPaths.GetShaderCacheFile(e.Key))) &&
            File.Exists(AppPaths.GetShaderCacheFile("swift-hohenberg3d-pixel")),"All mode shaders must use the common disk cache.");
        Console.WriteLine("Swift–Hohenberg 3D: PASS");
    }

    private static double SwiftWeight(double lambda,double dt) => Math.Abs(lambda*dt)<1e-7
        ? dt*(1+lambda*dt/2+Math.Pow(lambda*dt,2)/6) : (Math.Exp(lambda*dt)-1)/lambda;

    // Independent direct DFT shared with the Cahn verification; no GPU butterfly implementation.
    private static double[] SwiftReferenceStep(double[] field,SwiftHohenberg3DSettings s)
    {
        int n=s.Size;
        var spectrum=CahnDft(field.Select(v=>new Complex(v,0)).ToArray(),n,false);
        var nonlinear=CahnDft(field.Select(v=>new Complex(s.Quadratic*v*v-v*v*v,0)).ToArray(),n,false);
        for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++) {
            int i=(z*n+y)*n+x, kx=Math.Min(x,n-x),ky=Math.Min(y,n-y),kz=Math.Min(z,n-z);
            if(Math.Max(Math.Max(kx,ky),kz)>=n/4) { spectrum[i]=0; continue; }
            double k2=Math.Pow(Math.Tau/(n*s.Spacing),2)*(kx*kx+ky*ky+kz*kz);
            double lambda=s.R-Math.Pow(s.WaveNumber*s.WaveNumber-k2,2);
            spectrum[i]=Math.Exp(s.TimeStep*lambda)*spectrum[i]+SwiftWeight(lambda,s.TimeStep)*nonlinear[i];
        }
        return CahnDft(spectrum,n,true).Select(v=>v.Real).ToArray();
    }

    private static double SwiftEnergy(double[] field,SwiftHohenberg3DSettings s)
    {
        int n=s.Size; double energy=field.Sum(u=>-s.R*u*u/2-s.Quadratic*u*u*u/3+Math.Pow(u,4)/4);
        var spectrum=CahnDft(field.Select(v=>new Complex(v,0)).ToArray(),n,false);
        for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++) {
            double k2=Math.Pow(Math.Tau/(n*s.Spacing),2)*(Math.Pow(Math.Min(x,n-x),2)+Math.Pow(Math.Min(y,n-y),2)+Math.Pow(Math.Min(z,n-z),2));
            energy+=.5*Math.Pow(s.WaveNumber*s.WaveNumber-k2,2)*Math.Pow(spectrum[(z*n+y)*n+x].Magnitude,2)/field.Length;
        }
        return energy;
    }

    private static async Task VerifySwiftWindow(Fractal3DState state)
    {
        EnsureWindowThemeStyles(); var window=new Fractal3DWindow(Fractal3DKind.SwiftHohenberg3D);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var editor=SimulationEditor<SwiftHohenberg3DEditor>(window); var live=editor.Simulation;
        async Task Display() {
            var clock=System.Diagnostics.Stopwatch.StartNew();
            while(live.Busy && clock.ElapsedMilliseconds<60000) await Task.Delay(10);
            Check(!live.Busy,"Preparation must finish or cancel.");
            var method=typeof(Fractal3DWindow).GetMethod("RenderFrameAsync",flags)!;
            await (Task)method.Invoke(window,[Enum.Parse(method.GetParameters()[0].ParameterType,"Full")])!;
        }
        try {
            Check(((Grid)window.FindName("InitialPreparationLayer")).Visibility==Visibility.Visible,"The new mode must start with the common preparation screen.");
            await Display(); window.LoadState(state); await Display(); var loaded=window.CaptureState("loaded").SwiftHohenberg;
            Check(loaded.Field!.Samples.SequenceEqual(state.SwiftHohenberg.Field!.Samples),"Loaded WPF state must pause on the exact checkpoint.");
            Check(editor.Visibility==Visibility.Visible && ((FrameworkElement)window.FindName("IterationsBox")).Visibility==Visibility.Collapsed,"The mode must use its simulation editor.");
            ((TextBox)FindAny(window,"ShRBox")).Text="invalid";
            InvokeEditor(editor,"ShRestart_OnClick",window,new RoutedEventArgs());
            Check(window.CaptureState("invalid").SwiftHohenberg.Field!.Samples.SequenceEqual(loaded.Field.Samples),"Invalid drafts must preserve the displayed field.");
            live.StepOnce(); await Display();
            Check(window.CaptureState("step").SwiftHohenberg.Field!.Step==loaded.Field.Step+loaded.StepsPerFrame,"One frame must advance the shown checkpoint exactly once.");
            var before=window.CaptureState("before").SwiftHohenberg.Field!; InvokeEditor(editor,"Brush",.5,.5,.5); await Display();
            Check(window.CaptureState("brush").SwiftHohenberg.Field!.Step==before.Step,"Brush projection must not advance time.");
            ((ComboBox)FindAny(window,"ShViewBox")).SelectedIndex=1;
            Check(window.CaptureState("negative").SwiftHohenberg.View==SwiftHohenberg3DView.Negative,"View changes must reach the common render state.");
            window.LoadState(Fractal3DCatalog.CreateDefaultState(Fractal3DKind.SwiftHohenberg3D)); live.Pause(); await Display();
            Check(window.CaptureState("partial").SwiftHohenberg.Field is not null && !live.Running,"Stopping preparation must leave a usable partial checkpoint.");
            window.LoadState(state); window.LoadState(state); await Display();
            Check(window.CaptureState("reload").SwiftHohenberg.Field!.Samples.SequenceEqual(state.SwiftHohenberg.Field.Samples),"Stale async results must never replace a later load.");
        } finally { live.Close(); window.Close(); }
    }
}
