using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;
using Vortice.Direct3D11;

internal static partial class Program
{
    private static async Task VerifyGinzburgLandau3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("ginzburg-landau3d");
        string? output = args.Length > 1 ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        var kind = Fractal3DKind.GinzburgLandau3D;
        var state = Fractal3DCatalog.CreateDefaultState(kind);
        foreach (var bad in new[] { new GinzburgLandau3DSettings { C1 = double.NaN }, new() { C3 = 4 },
            new() { Size = 129 }, new() { C1 = 3, TimeStep = .02 }, new() { Spacing = .5, TimeStep = .05 },
            new() { CutPosition = double.PositiveInfinity }, new() { View = (GinzburgLandau3DView)99 } })
        {
            try { bad.Validate(); throw new Exception("Invalid equation accepted."); }
            catch (InvalidOperationException) { }
        }
        using var renderer = new Fractal3DRenderer(); var host = renderer.DeviceHost;
        foreach (var boundary in Enum.GetValues<GinzburgLandau3DBoundary>())
        foreach (double c1 in new[] { -2.0, 0, 1.5 })
        {
            var s = new GinzburgLandau3DSettings { Size = 33, C1 = c1, C3 = -1.3, Boundary = boundary, TimeStep = .01 };
            var rng = new Random(937); float[] initial = Enumerable.Range(0, 33 * 33 * 33 * 2)
                .Select(_ => (float)(rng.NextDouble() - .5)).ToArray();
            using var sim = new GinzburgLandau3DGpuSimulation(host, s with { Field = new(33, 0, initial) });
            double[] reference = initial.Select(v => (double)v).ToArray();
            for (int i = 0; i < 12; i++) reference = GinzburgReferenceStep(reference, s);
            sim.Advance(12, CancellationToken.None);
            double error = sim.ReadCurrent().Components.ToArray().Zip(reference, (a,b) => Math.Abs(a-b)).Max();
            Console.WriteLine($"{boundary}, c1={c1}: GPU/double max error {error:E2}");
            Check(error < 2e-5, "Complex diffusion, cubic frequency and both boundaries must match independent double arithmetic.");
        }
        // Exact spatially uniform logistic amplitude and nonlinear phase, including the zero solution.
        foreach (Complex start in new[] { Complex.Zero, new Complex(.4, .3), new Complex(-2, 1) })
        {
            float[] field = new float[32 * 32 * 32 * 2];
            for (int i = 0; i < field.Length; i += 2) { field[i] = (float)start.Real; field[i+1] = (float)start.Imaginary; }
            var s = new GinzburgLandau3DSettings { Size = 32, C1 = 2, C3 = 1.3, TimeStep = .01, Field = new(32, 0, field) };
            using var sim = new GinzburgLandau3DGpuSimulation(host, s); sim.Advance(200, CancellationToken.None);
            double denominator = 1 + start.Magnitude * start.Magnitude * (Math.Exp(4) - 1);
            Complex exact = start * Math.Sqrt(Math.Exp(4) / denominator) * Complex.FromPolarCoordinates(1, -.5 * s.C3 * Math.Log(denominator));
            float[] after = sim.ReadCurrent().Components.ToArray();
            Check(after.Where((_,i) => i % 2 == 0).All(v => Math.Abs(v - exact.Real) < 4e-5) &&
                after.Where((_,i) => i % 2 == 1).All(v => Math.Abs(v - exact.Imaginary) < 4e-5), "Uniform complex field must match the analytic logistic amplitude and phase.");
        }
        // A single Fourier mode is an exact solution of the semidiscrete PDE; halve dt to verify convergence.
        double PlaneWaveError(double dt)
        {
            const int n = 33; double k = Math.Tau * 2 / n, lambda = 4 * Math.Pow(Math.Sin(k/2), 2), t = 2, mu = 1-lambda;
            float[] initial = new float[n*n*n*2];
            for (int z=0; z<n; z++) for(int y=0; y<n; y++) for(int x=0; x<n; x++)
            { int i=((z*n+y)*n+x)*2; initial[i]=(float)(.7*Math.Cos(k*x)); initial[i+1]=(float)(.7*Math.Sin(k*x)); }
            var s = new GinzburgLandau3DSettings { Size=n, TimeStep=dt, C1=1.5, C3=-1.2,
                Boundary=GinzburgLandau3DBoundary.Periodic, Field=new(n,0,initial) };
            using var sim=new GinzburgLandau3DGpuSimulation(host,s); sim.Advance((int)Math.Round(t/dt),CancellationToken.None);
            double den=1+.49*(Math.Exp(2*mu*t)-1)/mu;
            double amplitude=.7*Math.Sqrt(Math.Exp(2*mu*t)/den), phase=-s.C1*lambda*t-.5*s.C3*Math.Log(den);
            float[] values=sim.ReadCurrent().Components.ToArray(); double max=0;
            for(int i=0; i<values.Length; i+=2)
            { Complex exact=Complex.FromPolarCoordinates(amplitude,k*((i/2)%n)+phase); max=Math.Max(max,(new Complex(values[i],values[i+1])-exact).Magnitude); }
            return max;
        }
        double coarse=PlaneWaveError(.02), fine=PlaneWaveError(.01);
        Console.WriteLine($"Analytic Fourier solution: dt=.02 error {coarse:E2}, dt=.01 error {fine:E2}");
        Check(fine < coarse * .7 && fine < .003, "Halving dt must converge towards the analytic semidiscrete wave.");
        foreach(double c1 in new[]{-3.0,3.0}) foreach(double c3 in new[]{-3.0,3.0})
        {
            var s=new GinzburgLandau3DSettings { Size=32, C1=c1, C3=c3, TimeStep=.01,
                Boundary=GinzburgLandau3DBoundary.Periodic, SeedShape=GinzburgLandau3DSeed.Random };
            using var sim=new GinzburgLandau3DGpuSimulation(host,s); sim.Advance(2048,CancellationToken.None);
            float[] f=sim.ReadCurrent().Components.ToArray(); double meanSquare=f.Sum(v=>(double)v*v)/(f.Length/2);
            Check(f.All(float.IsFinite) && meanSquare is > .001 and < 2,"Extreme valid dispersion/frequency must retain finite evolving fields over long runs.");
        }

        // Winding detects known topology along all three orientations, including reversed charge.
        foreach(int axis in new[]{0,1,2}) foreach(int charge in new[]{-1,1})
        {
            const int n=32; float[] field=new float[n*n*n*2];
            for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            {
                double a=axis==0 ? y-15.5 : x-15.5, b=axis==2 ? y-15.5 : z-15.5;
                double phase=charge*Math.Atan2(b,a); int i=((z*n+y)*n+x)*2;
                field[i]=(float)(.8*Math.Cos(phase)); field[i+1]=(float)(.8*Math.Sin(phase));
            }
            using var sim=new GinzburgLandau3DGpuSimulation(host,new() { Size=n, Field=new(n,0,field) });
            float[] display=ReadGinzburgDisplay(host,sim.Publish());
            Check(display.Where((_,i)=>i%4==3).Count(v=>v>.5)==n,"Known unit-charge vortex must occupy exactly one cell per transverse layer.");
        }
        foreach(double amplitude in new[]{0.0,.01,1.0})
        {
            float[] field=new float[32*32*32*2]; for(int i=0;i<field.Length;i+=2) field[i]=(float)amplitude;
            using var sim=new GinzburgLandau3DGpuSimulation(host,new() { Size=32, Field=new(32,0,field) });
            Check(ReadGinzburgDisplay(host,sim.Publish()).Where((_,i)=>i%4==3).All(v=>v==0),"Zero and low-amplitude uniform fields must not create false vortices.");
        }
        using var gpu=new GinzburgLandau3DGpuSimulation(host,state.GinzburgLandau);
        gpu.Advance(20,CancellationToken.None); var shown=gpu.Publish(); var snapshot=gpu.ReadCheckpoint(shown);
        gpu.Advance(7,CancellationToken.None); var stale=gpu.Publish(shown); gpu.Advance(3,CancellationToken.None); gpu.Publish(shown);
        Check(gpu.ReadCheckpoint(shown).Components.SequenceEqual(snapshot.Components),"Publication must retain the displayed snapshot during later evolution.");
        try { gpu.ReadCheckpoint(stale); throw new Exception("Stale ticket accepted."); } catch(InvalidOperationException) { }
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel(); var before=gpu.ReadCurrent();
            Check(gpu.Advance(80,cancel.Token)==0 && gpu.Step==before.Step && gpu.ReadCurrent().Components.SequenceEqual(before.Components),"Canceled work must preserve components and time.");
        }
        foreach(var mode in Enum.GetValues<GinzburgLandau3DBrush>())
        {
            using var brush=new GinzburgLandau3DGpuSimulation(host,new() { Boundary=GinzburgLandau3DBoundary.Periodic, SeedShape=GinzburgLandau3DSeed.Uniform });
            brush.Inject(0,.5,.5,.12,mode); var f=brush.ReadCurrent(); float[] v=f.Components.ToArray();
            int first=((32*64+32)*64)*2, last=first+63*2;
            Check(v[first]<.5 && Math.Abs(v[first]-v[last])<1e-5 && f.Step==0,"Both brushes must wrap across periodic faces and preserve time.");
            if(mode==GinzburgLandau3DBrush.PhaseFlip)
                Check(Enumerable.Range(0,v.Length/2).All(i=>Math.Abs(v[i*2]*v[i*2]+v[i*2+1]*v[i*2+1]-1)<1e-5),"Phase brush must preserve amplitude.");
        }
        foreach(int size in new[]{32,128})
        {
            using var sized=new GinzburgLandau3DGpuSimulation(host,new() { Size=size, SeedShape=GinzburgLandau3DSeed.Uniform });
            sized.Advance(2,CancellationToken.None); Check(sized.ReadCurrent().Size==size && sized.Step==2,"Both grid endpoints must evolve.");
        }
        var options=JsonOptionsFactory.Create(); state.GinzburgLandau=state.GinzburgLandau with { Field=snapshot }; state.SaveName="Exact complex field";
        string json=JsonSerializer.Serialize(state,options); var restored=JsonSerializer.Deserialize<Fractal3DState>(json,options)!;
        Check(restored.GinzburgLandau.Field!.Step==snapshot.Step && restored.GinzburgLandau.Field.Components.SequenceEqual(snapshot.Components) && !json.Contains("\"Live\""),"JSON must retain signed Re/Im and time without GPU references.");
        using(var a=new GinzburgLandau3DGpuSimulation(host,state.GinzburgLandau))
        using(var b=new GinzburgLandau3DGpuSimulation(host,restored.GinzburgLandau))
        { a.Advance(30,CancellationToken.None); b.Advance(30,CancellationToken.None); Check(a.ReadCurrent().Components.SequenceEqual(b.ReadCurrent().Components),"Continuation on the same GPU must be bit exact."); }
        foreach(string data in new[]{"!", "AA==", Convert.ToBase64String(new byte[100])})
        {
            try { JsonSerializer.Deserialize<GinzburgLandau3DField>($"{{\"Size\":32,\"Step\":0,\"Data\":\"{data}\"}}",options); throw new Exception("Corrupted field accepted."); }
            catch(JsonException) { }
        }
        var store=new Fractal3DSaveStore(kind); store.Save(state);
        foreach(string malformed in new[]{"{}", "{\"Size\":129,\"Step\":0}", "{\"Size\":32,\"Step\":-1}", "{\"Size\":\"bad\",\"Step\":0}"})
        {
            try { JsonSerializer.Deserialize<GinzburgLandau3DField>(malformed,options); throw new Exception("Malformed checkpoint metadata accepted."); }
            catch(JsonException) { }
        }
        Check(store.Load().Single().GinzburgLandau.Field!.Components.SequenceEqual(snapshot.Components),"Save store must retain the checkpoint.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().Category=="Fractal3DGinzburgLandau" && unreadable==0,"The common cloud repository must recognize the category.");
        var old=JsonSerializer.Deserialize<Fractal3DState>("{\"Kind\":0}",options)!; old.Clone(); old.GinzburgLandau.Validate();
        Check(GinzburgLandau3DComputeShader.CacheEntries.All(e=>File.Exists(AppPaths.GetShaderCacheFile(e.Key))),"Compute passes must use the common disk cache.");

        int index=0;
        foreach(var preset in Fractal3DCatalog.GetPresets(kind))
        {
            preset.GinzburgLandau.Validate();
            using var sim=new GinzburgLandau3DGpuSimulation(host,preset.GinzburgLandau);
            await Task.Run(()=>sim.Advance(preset.GinzburgLandau.WarmupSteps,CancellationToken.None));
            var checkpoint=sim.ReadCurrent(); sim.Advance(20,CancellationToken.None);
            Check(MaxArrayDifference(checkpoint.Components.ToArray(),sim.ReadCurrent().Components.ToArray())>.001,"Each preset must evolve after preparation.");
            var view=preset.Clone(); view.GinzburgLandau=view.GinzburgLandau with { Field=checkpoint };
            var bitmap=await renderer.RenderAsync(view,280,280,null,CancellationToken.None);
            // Compare with empty volume under identical camera/lighting, not merely the background gradient.
            var empty=view.Clone(); empty.GinzburgLandau=view.GinzburgLandau with { Field=new(checkpoint.Size,0,new float[checkpoint.Components.Length]), View=GinzburgLandau3DView.Filaments };
            byte[] pixels=Pixels(bitmap).ToArray(), blank=Pixels(await renderer.RenderAsync(empty,280,280,null,CancellationToken.None)).ToArray();
            int changed=pixels.Zip(blank,(a,b)=>a!=b ? 1 : 0).Sum();
            Console.WriteLine($"{preset.SaveName}: {changed} coloured bytes, step {checkpoint.Step}");
            Check(changed>250,"Every preset must show a nonempty structure.");
            if(output is not null) SaveExcitablePng(bitmap,Path.Combine(output,$"preset-{index}.png"));
            if(index++==0) state=view;
        }
        using(var live=new GinzburgLandau3DGpuSimulation(host,state.GinzburgLandau))
        {
            var liveState=state.Clone(); liveState.GinzburgLandau=state.GinzburgLandau with { Field=null, Live=live.Publish() };
            byte[] livePixels=Pixels(await renderer.RenderAsync(liveState,180,180,null,CancellationToken.None)).ToArray();
            byte[] checkpointPixels=Pixels(await renderer.RenderAsync(state,180,180,null,CancellationToken.None)).ToArray();
            Check(livePixels.SequenceEqual(checkpointPixels),"Live publication and disk checkpoint must render identically.");
        }
        foreach(var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var view=state.Clone(); view.ShadingStyle=style;
            var bitmap=await renderer.RenderAsync(view,180,180,null,CancellationToken.None);
            if(output is not null) SaveExcitablePng(bitmap,Path.Combine(output,$"style-{(int)style}.png"));
        }
        foreach(var mode in new[]{Fractal3DColoringMode.OrbitTrap,Fractal3DColoringMode.CrossTrap})
        {
            var view=state.Clone(); view.ColoringMode=mode;
            var bitmap=await renderer.RenderAsync(view,180,180,null,CancellationToken.None);
            if(output is not null) SaveExcitablePng(bitmap,Path.Combine(output,$"color-{mode}.png"));
        }
        // Rotate the entire complex field: only phase colouring should change, never amplitude geometry.
        var shifted=state.Clone(); float[] rotated=state.GinzburgLandau.Field!.Components.ToArray();
        for(int i=0;i<rotated.Length;i+=2) (rotated[i],rotated[i+1])=(-rotated[i+1],rotated[i]);
        shifted.GinzburgLandau=shifted.GinzburgLandau with { Field=new(shifted.GinzburgLandau.Size,state.GinzburgLandau.Field.Step,rotated) };
        foreach(var mode in new[]{Fractal3DColoringMode.OrbitTrap,Fractal3DColoringMode.CrossTrap})
        {
            var original=state.Clone(); original.ColoringMode=mode; shifted.ColoringMode=mode;
            byte[] a=Pixels(await renderer.RenderAsync(original,180,180,null,CancellationToken.None)).ToArray();
            byte[] b=Pixels(await renderer.RenderAsync(shifted,180,180,null,CancellationToken.None)).ToArray();
            Check(a.SequenceEqual(b)==(mode==Fractal3DColoringMode.CrossTrap),"Global phase rotation must affect phase colour and preserve amplitude colour.");
        }
        foreach(int axis in new[]{1,2,3})
        {
            var cut=state.Clone(); cut.GinzburgLandau=cut.GinzburgLandau with { CutAxis=axis,CutPosition=0 };
            byte[] cutPixels=Pixels(await renderer.RenderAsync(cut,180,180,null,CancellationToken.None)).ToArray();
            byte[] wholePixels=Pixels(await renderer.RenderAsync(state,180,180,null,CancellationToken.None)).ToArray();
            Check(!cutPixels.SequenceEqual(wholePixels),"Each cutting axis must affect geometry.");
            Check(Fractal3DRenderer.SameVolumeGeometry(state,cut),"View changes must reuse the evolved field.");
        }
        bool hit=false; for(int y=50;y<140 && !hit;y+=10) for(int x=50;x<140 && !hit;x+=10)
            hit=await renderer.ProbeDistanceAsync(state,x,y,180,180,CancellationToken.None)>0;
        Check(hit,"The shared camera probe must hit a vortex filament.");
        await VerifyGinzburgWindow(state);
        Check(File.Exists(AppPaths.GetShaderCacheFile("ginzburg-landau3d-pixel")),"The display shader must use the common disk cache.");
        Console.WriteLine("Ginzburg–Landau 3D: PASS");
    }

    // Independent Complex/double formulation and independently indexed boundaries.
    private static double[] GinzburgReferenceStep(double[] field,GinzburgLandau3DSettings s)
    {
        int n=s.Size; var a=new Complex[field.Length/2]; var next=new double[field.Length];
        Complex React(Complex v) {
            double den=1+v.Magnitude*v.Magnitude*(Math.Exp(s.TimeStep)-1);
            return v*Math.Sqrt(Math.Exp(s.TimeStep)/den)*Complex.FromPolarCoordinates(1,-s.C3*Math.Log(den)/2);
        }
        for(int i=0;i<a.Length;i++) a[i]=React(new(field[2*i],field[2*i+1]));
        int Bound(int v)=>s.Boundary==GinzburgLandau3DBoundary.Periodic ? (v+n)%n : Math.Clamp(v,0,n-1);
        int At(int x,int y,int z)=>(Bound(z)*n+Bound(y))*n+Bound(x);
        for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++) {
            int i=At(x,y,z); Complex lap=a[At(x-1,y,z)]+a[At(x+1,y,z)]+a[At(x,y-1,z)]+a[At(x,y+1,z)]+a[At(x,y,z-1)]+a[At(x,y,z+1)]-6*a[i];
            Complex v=React(a[i]+s.TimeStep/(s.Spacing*s.Spacing)*new Complex(1,s.C1)*lap);
            next[2*i]=v.Real; next[2*i+1]=v.Imaginary;
        }
        return next;
    }

    private static float[] ReadGinzburgDisplay(Direct3DDeviceHost host,GinzburgLandau3DVolume volume)
    {
        var slots=(GpuFieldSlot?[])typeof(GinzburgLandau3DGpuSimulation).GetField("_slots",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(volume.Source)!;
        host.Gate.Wait();
        try {
            var texture=slots[volume.Slot]!.Display.Texture; var desc=texture.Description;
            desc.Usage=ResourceUsage.Staging; desc.BindFlags=BindFlags.None; desc.CPUAccessFlags=CpuAccessFlags.Read;
            using var staging=host.Device.CreateTexture3D(desc); host.Context.CopyResource(staging,texture);
            var mapped=host.Context.Map(staging,0,MapMode.Read); int n=volume.Size; float[] values=new float[n*n*n*4];
            try { for(int z=0;z<n;z++) for(int y=0;y<n;y++)
                Marshal.Copy(mapped.DataPointer+(int)(z*mapped.DepthPitch+y*mapped.RowPitch),values,(z*n+y)*n*4,n*4); }
            finally { host.Context.Unmap(staging,0); }
            return values;
        } finally { host.Gate.Release(); }
    }

    private static async Task VerifyGinzburgWindow(Fractal3DState state)
    {
        EnsureWindowThemeStyles(); var window=new Fractal3DWindow(Fractal3DKind.GinzburgLandau3D);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var editor=SimulationEditor<GinzburgLandau3DEditor>(window); var live=editor.Simulation;
        async Task Display() {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(live.Busy && watch.ElapsedMilliseconds<30000) await Task.Delay(10);
            Check(!live.Busy,"Preparation must finish or cancel.");
            var method=typeof(Fractal3DWindow).GetMethod("RenderFrameAsync",flags)!;
            await (Task)method.Invoke(window,[Enum.Parse(method.GetParameters()[0].ParameterType,"Full")])!;
        }
        try {
            Check(((Grid)window.FindName("InitialPreparationLayer")).Visibility==Visibility.Visible,"The mode must start with the common preparation screen.");
            await Display(); window.LoadState(state); await Display();
            var loaded=window.CaptureState("loaded").GinzburgLandau;
            Check(loaded.Field!.Components.SequenceEqual(state.GinzburgLandau.Field!.Components),"WPF must pause on the exact loaded field.");
            Check(editor.Visibility==Visibility.Visible && ((FrameworkElement)window.FindName("IterationsBox")).Visibility==Visibility.Collapsed,"Simulation editor must replace the generic iteration controls.");
            ((TextBox)FindAny(window,"GlC1Box")).Text="invalid";
            InvokeEditor(editor,"GlRestart_OnClick",window,new RoutedEventArgs());
            Check(window.CaptureState("invalid").GinzburgLandau.Field!.Components.SequenceEqual(loaded.Field.Components),"Invalid Apply and equation drafts must preserve the shown field.");
            live.StepOnce(); await Display();
            Check(window.CaptureState("step").GinzburgLandau.Field!.Step==loaded.Field.Step+loaded.StepsPerFrame,"One frame must advance the snapshot exactly once.");
            var before=window.CaptureState("before").GinzburgLandau.Field!;
            InvokeEditor(editor,"Brush",.5,.5,.5); await Display();
            Check(window.CaptureState("brush").GinzburgLandau.Field!.Step==before.Step,"Brush must preserve time.");
            ((ComboBox)FindAny(window,"GlViewBox")).SelectedIndex=1;
            Check(window.CaptureState("view").GinzburgLandau.View==GinzburgLandau3DView.AmplitudeDefects,"View choices must reach the common render state.");
            window.LoadState(Fractal3DCatalog.CreateDefaultState(Fractal3DKind.GinzburgLandau3D)); live.Pause(); await Display();
            Check(window.CaptureState("partial").GinzburgLandau.Field is not null && !live.Running,"Stopping preparation must leave a usable partial checkpoint.");
            window.LoadState(state); window.LoadState(state); await Display();
            Check(window.CaptureState("reload").GinzburgLandau.Field!.Components.SequenceEqual(state.GinzburgLandau.Field.Components),"Old asynchronous results must not replace a later load.");
        } finally { live.Close(); window.Close(); }
    }
}
