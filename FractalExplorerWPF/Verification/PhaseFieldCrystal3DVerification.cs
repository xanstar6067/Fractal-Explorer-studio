using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private static async Task VerifyPhaseFieldCrystal3DAsync(string[] args)
    {
        using var sandbox=DataSandbox.Create("phase-field-crystal3d");
        string? output=args.Length>1 ? args[1] : null; if(output is not null) Directory.CreateDirectory(output);
        using var renderer=new Fractal3DRenderer(); var host=renderer.DeviceHost;
        var kind=Fractal3DKind.PhaseFieldCrystal3D; var state=Fractal3DCatalog.CreateDefaultState(kind);
        foreach(var invalid in new PhaseFieldCrystal3DSettings[] { new() { Size=96 }, new() { Undercooling=double.NaN },
            new() { Mean=.7 },new() { WaveNumber=1.4 },new() { Mobility=2 },new() { CutPosition=2 },new() { View=(PhaseFieldCrystal3DView)99 } })
        { try { invalid.Validate(); throw new Exception("Invalid PFC settings accepted."); } catch(InvalidOperationException) { } }
        const int n=32; var initial=new float[n*n*n];
        for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++)
            initial[(z*n+y)*n+x]=(float)(.12+.15*Math.Cos(Math.Tau*(2*x+3*y+z)/n)+.07*Math.Cos(Math.Tau*(3*x-y+2*z)/n));
        var rule=new PhaseFieldCrystal3DSettings { Size=n, Mean=.12, Undercooling=.4, WaveNumber=.9, Spacing=.8, Mobility=.7, TimeStep=.02, Field=new(n,17,initial,.12) };
        using var gpu=new PhaseFieldCrystal3DGpuSimulation(host,rule);
        double[] reference=initial.Select(v=>(double)v).ToArray(); double energy=PfcEnergy(reference,rule);
        for(int step=0;step<5;step++) {
            reference=PfcReferenceStep(reference,rule); gpu.Advance(1,CancellationToken.None);
            var actual=gpu.ReadCurrent(); double error=actual.Density.ToArray().Zip(reference,(a,b)=>Math.Abs(a-b)).Max();
            double next=PfcEnergy(actual.Density.ToArray().Select(v=>(double)v).ToArray(),rule);
            Console.WriteLine($"PFC step {step}: GPU/direct DFT {error:E2}; E {energy:F5} -> {next:F5}");
            Check(error<3e-6,"Conserved sixth-order spectral step must match independent direct double DFT.");
            Check(next<energy && Math.Abs(actual.Density.ToArray().Average(v=>(double)v)-rule.Mean)<2e-7,"Energy must decrease while the mean remains constant."); energy=next;
        }
        foreach(int side in new[]{32,64,128}) foreach(bool tiled in new[]{false,true}) {
            const double mean=.12, amplitude=.15; var wave=new float[side*side*side]; var expected=new float[wave.Length];
            double k2=Math.Pow(Math.Tau/(side*rule.Spacing),2)*6;
            double[] cubic=[0,3*mean*mean*amplitude+.75*Math.Pow(amplitude,3),1.5*mean*amplitude*amplitude,.25*Math.Pow(amplitude,3)];
            double[] coefficient=new double[4]; coefficient[0]=(float)mean;
            for(int j=1;j<4;j++) {
                double q=j*j*k2, lambda=q*(rule.Undercooling-Math.Pow(rule.WaveNumber*rule.WaveNumber-q,2)), dt=rule.TimeStep*rule.Mobility;
                coefficient[j]=Math.Exp(dt*lambda)*(j==1 ? amplitude : 0)-SwiftWeight(lambda,dt)*q*cubic[j];
            }
            for(int z=0;z<side;z++) for(int y=0;y<side;y++) for(int x=0;x<side;x++) {
                int i=(z*side+y)*side+x; double phase=Math.Tau*(x+2*y+z)/side;
                wave[i]=(float)(mean+amplitude*Math.Cos(phase)); expected[i]=(float)(coefficient[0]+coefficient[1]*Math.Cos(phase)+coefficient[2]*Math.Cos(2*phase)+coefficient[3]*Math.Cos(3*phase));
            }
            using var sim=new PhaseFieldCrystal3DGpuSimulation(host,rule with { Size=side,Field=new(side,0,wave,mean) },false,tiled);
            sim.Advance(1,CancellationToken.None);
            Check(MaxArrayDifference(expected,sim.ReadCurrent().Density.ToArray())<3e-6,"Both FFT layouts and every grid must match analytic cubic harmonics.");
        }
        foreach(int side in new[]{32,64,128}) {
            float mean=-.31f; float[] liquid=Enumerable.Repeat(mean,side*side*side).ToArray();
            using var sim=new PhaseFieldCrystal3DGpuSimulation(host,new() { Size=side,Mean=mean,Field=new(side,0,liquid,mean) });
            sim.Advance(50,CancellationToken.None);
            Check(sim.ReadCurrent().Density.SequenceEqual(liquid),"Every uniform density must be stationary and bit exact, even with nonzero chemical potential.");
            Check(ReadPfcDisplay(host,sim.Publish()).Where((_,i)=>i%2==1).All(v=>v==0),"Uniform liquid must have zero modulation.");
        }
        // Exact Galerkin amplitude solution: third harmonic of m=3 lies outside the N/4 band.
        double AmplitudeError(double dt) {
            double k=Math.Tau*3/n, k2=k*k, epsilon=.4, q0=.6, t=6, amplitude=.15;
            float[] wave=new float[n*n*n];
            for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++) wave[(z*n+y)*n+x]=(float)(amplitude*Math.Cos(k*x));
            using var sim=new PhaseFieldCrystal3DGpuSimulation(host,new() { Size=n,Mean=0,Undercooling=epsilon,WaveNumber=q0,TimeStep=dt,Field=new(n,0,wave,0) });
            sim.Advance((int)Math.Round(t/dt),CancellationToken.None);
            double alpha=k2*(epsilon-Math.Pow(q0*q0-k2,2)), beta=.75*k2, e=Math.Exp(2*alpha*t);
            double exact=amplitude*Math.Sqrt(e/(1+beta*amplitude*amplitude/alpha*(e-1)));
            return Math.Abs(sim.ReadCurrent().Density[0]-exact);
        }
        double coarse=AmplitudeError(.03), fine=AmplitudeError(.015);
        Console.WriteLine($"PFC amplitude convergence: {coarse:E2} -> {fine:E2}");
        Check(fine<coarse*.7 && fine<.001,"Time refinement must converge towards the analytic conserved Galerkin amplitude.");
        // Independent analytic Gaussian average of a squared sinusoidal modulation.
        {
            const int side=64; const double mean=.12, amplitude=.15;
            var wave=new float[side*side*side];
            for(int z=0;z<side;z++) for(int y=0;y<side;y++) for(int x=0;x<side;x++)
                wave[(z*side+y)*side+x]=(float)(mean+amplitude*Math.Cos(Math.Tau*(x+2*y+z)/side));
            using var sim=new PhaseFieldCrystal3DGpuSimulation(host,rule with { Size=side,Field=new(side,0,wave,mean) });
            var volume=sim.Publish(); var display=ReadPfcDisplay(host,volume);
            double k2=Math.Pow(Math.Tau/(side*rule.Spacing),2)*6, gaussian=Math.Exp(-4*k2/(.18*rule.WaveNumber*rule.WaveNumber));
            for(int z=0;z<side;z++) for(int y=0;y<side;y++) for(int x=0;x<side;x++) {
                int i=(z*side+y)*side+x; double phase=Math.Tau*(x+2*y+z)/side;
                double rms=Math.Sqrt(amplitude*amplitude/2*(1+gaussian*Math.Cos(2*phase)));
                Check(Math.Abs(display[2*i]-wave[i])<1e-7 && Math.Abs(display[2*i+1]-rms)<3e-6,"Published raw density and Gaussian RMS must match their independent analytic values.");
            }
        }
        foreach(double mean in new[]{-.6,0,.6}) foreach(double epsilon in new[]{-.2,.6}) {
            using var sim=new PhaseFieldCrystal3DGpuSimulation(host,new() { Size=n,Mean=mean,Undercooling=epsilon,Noise=.1 });
            await Task.Run(()=>sim.Advance(1000,CancellationToken.None)); var field=sim.ReadCurrent();
            Check(field.Density.ToArray().All(float.IsFinite) && Math.Abs(field.Density.ToArray().Average(v=>(double)v)-mean)<2e-7,"Long runs at parameter extremes must remain finite and conserve mean.");
        }
        var shown=gpu.Publish(); var checkpoint=gpu.ReadCheckpoint(shown);
        gpu.Advance(3,CancellationToken.None); var stale=gpu.Publish(shown); gpu.Advance(2,CancellationToken.None); gpu.Publish(shown);
        Check(gpu.ReadCheckpoint(shown).Density.SequenceEqual(checkpoint.Density),"Publication and modulation computation must preserve the shown field.");
        try { gpu.ReadCheckpoint(stale); throw new Exception("Stale checkpoint accepted."); } catch(InvalidOperationException) { }
        using(var cancel=new CancellationTokenSource()) {
            cancel.Cancel(); var before=gpu.ReadCurrent();
            Check(gpu.Advance(50,cancel.Token)==0 && gpu.Step==before.Step && gpu.ReadCurrent().Density.SequenceEqual(before.Density),"Cancelled work must preserve density and time.");
        }
        foreach(var mode in Enum.GetValues<PhaseFieldCrystal3DBrush>()) {
            using var sim=new PhaseFieldCrystal3DGpuSimulation(host,new() { Size=n,Noise=0 });
            var before=sim.ReadCurrent(); sim.Inject(0,.5,.5,.2,mode); var after=sim.ReadCurrent();
            Check(after.Step==before.Step && Math.Abs(after.Density.ToArray().Average(v=>(double)v)-after.Mean)<2e-7 &&
                MaxArrayDifference(before.Density.ToArray(),after.Density.ToArray())>.01,"Both brush modes must modify density without advancing time or changing the conserved mean.");
        }
        var options=JsonOptionsFactory.Create(); state.PhaseFieldCrystal=rule with { Field=checkpoint }; state.SaveName="Exact PFC";
        string json=JsonSerializer.Serialize(state,options); var restored=JsonSerializer.Deserialize<Fractal3DState>(json,options)!;
        Check(restored.PhaseFieldCrystal.Field!.Mean==checkpoint.Mean && restored.PhaseFieldCrystal.Field.Step==checkpoint.Step &&
            restored.PhaseFieldCrystal.Field.Density.SequenceEqual(checkpoint.Density) && !json.Contains("\"Live\""),"JSON must preserve density, mean and step without GPU objects.");
        using(var a=new PhaseFieldCrystal3DGpuSimulation(host,state.PhaseFieldCrystal))
        using(var b=new PhaseFieldCrystal3DGpuSimulation(host,restored.PhaseFieldCrystal)) {
            a.Advance(15,CancellationToken.None); b.Advance(15,CancellationToken.None);
            Check(a.ReadCurrent().Density.SequenceEqual(b.ReadCurrent().Density),"Same-GPU continuation must be bit exact.");
        }
        var damaged=JsonNode.Parse(json)!; damaged["PhaseFieldCrystal"]!["Field"]!["Mean"]=.5;
        try { JsonSerializer.Deserialize<Fractal3DState>(damaged.ToJsonString(),options); throw new Exception("Wrong conserved mean accepted."); } catch(JsonException) { }
        foreach(string corrupt in new[]{"{}","{\"Size\":96,\"Step\":0,\"Mean\":0}","{\"Size\":32,\"Step\":0,\"Mean\":0,\"Data\":\"AAAA\"}"})
        { try { JsonSerializer.Deserialize<PhaseFieldCrystal3DField>(corrupt,options); throw new Exception("Corrupt PFC field accepted."); } catch(JsonException) { } }
        var store=new Fractal3DSaveStore(kind); store.Save(state);
        Check(store.Load().Single().PhaseFieldCrystal.Field!.Density.SequenceEqual(checkpoint.Density),"The save store must retain the exact PFC checkpoint.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().Category=="Fractal3DPhaseFieldCrystal" && unreadable==0,"The common cloud repository must recognize PFC.");
        JsonSerializer.Deserialize<Fractal3DState>("{\"Kind\":0}",options)!.Clone().PhaseFieldCrystal.Validate();
        int index=0;
        foreach(var preset in Fractal3DCatalog.GetPresets(kind)) {
            using var sim=new PhaseFieldCrystal3DGpuSimulation(host,preset.PhaseFieldCrystal);
            await Task.Run(()=>sim.Advance(preset.PhaseFieldCrystal.WarmupSteps,CancellationToken.None));
            var early=sim.ReadCurrent(); await Task.Run(()=>sim.Advance(100,CancellationToken.None)); var late=sim.ReadCurrent();
            float[] values=early.Density.ToArray(); double change=MaxArrayDifference(values,late.Density.ToArray());
            Console.WriteLine($"{preset.SaveName}: psi [{values.Min():F3},{values.Max():F3}], mean {values.Average(v=>(double)v):F8}, change {change:E2}");
            Check(values.Max()-values.Min()>.3 && change>1e-6 && Math.Abs(late.Density.ToArray().Average(v=>(double)v)-late.Mean)<3e-7,"Every preset must form a structured conserved field and continue evolving.");
            var view=preset.Clone(); view.PhaseFieldCrystal=view.PhaseFieldCrystal with { Field=early };
            var image=await renderer.RenderAsync(view,300,300,null,CancellationToken.None);
            var liquid=view.Clone(); liquid.PhaseFieldCrystal=liquid.PhaseFieldCrystal with
                { Field=new(early.Size,0,Enumerable.Repeat((float)early.Mean,values.Length).ToArray(),early.Mean) };
            byte[] pixels=CahnPixels(image), blank=CahnPixels(await renderer.RenderAsync(liquid,300,300,null,CancellationToken.None));
            Check(pixels.Zip(blank,(a,b)=>a!=b?1:0).Sum()>500,"Every preset must show a visible crystalline structure beyond the background.");
            if(output is not null) CahnSaveImage(image,Path.Combine(output,$"preset-{index}.png"));
            if(index++==0) state=view;
        }
        using(var sim=new PhaseFieldCrystal3DGpuSimulation(host,state.PhaseFieldCrystal)) {
            var live=state.Clone(); live.PhaseFieldCrystal=live.PhaseFieldCrystal with { Field=null,Live=sim.Publish() };
            byte[] a=CahnPixels(await renderer.RenderAsync(live,180,180,null,CancellationToken.None));
            byte[] b=CahnPixels(await renderer.RenderAsync(state,180,180,null,CancellationToken.None));
            Check(a.SequenceEqual(b),"Live density/modulation must render identically to the same disk checkpoint.");
        }
        foreach(var style in Enum.GetValues<Fractal3DShadingStyle>()) {
            var view=state.Clone(); view.ShadingStyle=style; var image=await renderer.RenderAsync(view,180,180,null,CancellationToken.None);
            if(output is not null) CahnSaveImage(image,Path.Combine(output,$"style-{(int)style}.png"));
        }
        byte[] original=CahnPixels(await renderer.RenderAsync(state,180,180,null,CancellationToken.None));
        foreach(var mode in Enum.GetValues<PhaseFieldCrystal3DView>()) {
            var view=state.Clone(); view.PhaseFieldCrystal=view.PhaseFieldCrystal with { View=mode,Threshold=mode==PhaseFieldCrystal3DView.Troughs ? .15 : .35 };
            var image=await renderer.RenderAsync(view,180,180,null,CancellationToken.None);
            if(mode!=PhaseFieldCrystal3DView.Peaks) Check(!original.SequenceEqual(CahnPixels(image)),"Trough and modulation views must affect the image.");
            Check(Fractal3DRenderer.SameVolumeGeometry(state,view),"Presentation changes must preserve the evolved volume.");
            if(output is not null) CahnSaveImage(image,Path.Combine(output,$"view-{(int)mode}.png"));
        }
        foreach(int axis in new[]{1,2,3}) {
            var view=state.Clone(); view.PhaseFieldCrystal=view.PhaseFieldCrystal with { CutAxis=axis,CutPosition=0 };
            byte[] pixels=CahnPixels(await renderer.RenderAsync(view,180,180,null,CancellationToken.None));
            Check(!original.SequenceEqual(pixels),"Every cut axis must change the crystalline view.");
        }
        bool hit=false; for(int y=50;y<140 && !hit;y+=20) for(int x=50;x<140 && !hit;x+=20)
            hit=await renderer.ProbeDistanceAsync(state,x,y,180,180,CancellationToken.None)>0;
        Check(hit,"The common navigation probe must hit a density peak.");
        await VerifyPfcWindow(state);
        Check(PhaseFieldCrystal3DComputeShader.CacheEntries.All(e=>File.Exists(AppPaths.GetShaderCacheFile(e.Key))) &&
            File.Exists(AppPaths.GetShaderCacheFile("phase-field-crystal3d-pixel")),"All nine PFC shaders must use the common disk cache.");
        Console.WriteLine("Phase field crystal 3D: PASS");
    }
    private static double[] PfcReferenceStep(double[] field,PhaseFieldCrystal3DSettings s)
    {
        int n=s.Size; var spectrum=CahnDft(field.Select(v=>new Complex(v,0)).ToArray(),n,false);
        var cubic=CahnDft(field.Select(v=>new Complex(v*v*v,0)).ToArray(),n,false);
        for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++) {
            int i=(z*n+y)*n+x, kx=Math.Min(x,n-x),ky=Math.Min(y,n-y),kz=Math.Min(z,n-z);
            if(i==0) { spectrum[i]=(float)s.Mean*n*n*n; continue; }
            if(Math.Max(Math.Max(kx,ky),kz)>=n/4) { spectrum[i]=0; continue; }
            double k2=Math.Pow(Math.Tau/(n*s.Spacing),2)*(kx*kx+ky*ky+kz*kz);
            double lambda=k2*(s.Undercooling-Math.Pow(s.WaveNumber*s.WaveNumber-k2,2)),dt=s.TimeStep*s.Mobility;
            spectrum[i]=Math.Exp(dt*lambda)*spectrum[i]-SwiftWeight(lambda,dt)*k2*cubic[i];
        }
        return CahnDft(spectrum,n,true).Select(v=>v.Real).ToArray();
    }
    private static double PfcEnergy(double[] field,PhaseFieldCrystal3DSettings s)
    {
        int n=s.Size; double energy=field.Sum(v=>-s.Undercooling*v*v/2+Math.Pow(v,4)/4);
        var spectrum=CahnDft(field.Select(v=>new Complex(v,0)).ToArray(),n,false);
        for(int z=0;z<n;z++) for(int y=0;y<n;y++) for(int x=0;x<n;x++) {
            double k2=Math.Pow(Math.Tau/(n*s.Spacing),2)*(Math.Pow(Math.Min(x,n-x),2)+Math.Pow(Math.Min(y,n-y),2)+Math.Pow(Math.Min(z,n-z),2));
            energy+=.5*Math.Pow(s.WaveNumber*s.WaveNumber-k2,2)*Math.Pow(spectrum[(z*n+y)*n+x].Magnitude,2)/field.Length;
        }
        return energy;
    }
    private static float[] ReadPfcDisplay(Direct3DDeviceHost host,PhaseFieldCrystal3DVolume volume)
    {
        var type=typeof(GpuSpectralScalarSimulation<PhaseFieldCrystal3DSettings,PhaseFieldCrystal3DField,PhaseFieldCrystal3DVolume>);
        var slots=(GpuFieldSlot?[])type.GetField("_slots",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(volume.Source)!;
        host.Gate.Wait();
        try {
            var texture=slots[volume.Slot]!.Display.Texture; var desc=texture.Description;
            desc.Usage=ResourceUsage.Staging; desc.BindFlags=BindFlags.None; desc.CPUAccessFlags=CpuAccessFlags.Read;
            using var staging=host.Device.CreateTexture3D(desc); host.Context.CopyResource(staging,texture);
            var map=host.Context.Map(staging,0,MapMode.Read); int n=volume.Size; float[] data=new float[n*n*n*2];
            try { for(int z=0;z<n;z++) for(int y=0;y<n;y++) Marshal.Copy(map.DataPointer+(int)(z*map.DepthPitch+y*map.RowPitch),data,(z*n+y)*n*2,n*2); }
            finally { host.Context.Unmap(staging,0); } return data;
        } finally { host.Gate.Release(); }
    }
    private static async Task VerifyPfcWindow(Fractal3DState state)
    {
        EnsureWindowThemeStyles(); var window=new Fractal3DWindow(Fractal3DKind.PhaseFieldCrystal3D);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var editor=SimulationEditor<PhaseFieldCrystal3DEditor>(window); var live=editor.Simulation;
        async Task Display() {
            var clock=System.Diagnostics.Stopwatch.StartNew(); while(live.Busy && clock.ElapsedMilliseconds<60000) await Task.Delay(10);
            Check(!live.Busy,"PFC preparation must finish or cancel.");
            var method=typeof(Fractal3DWindow).GetMethod("RenderFrameAsync",flags)!;
            await (Task)method.Invoke(window,[Enum.Parse(method.GetParameters()[0].ParameterType,"Full")])!;
        }
        try {
            Check(((Grid)window.FindName("InitialPreparationLayer")).Visibility==Visibility.Visible,"The mode must start with the common preparation screen.");
            await Display(); window.LoadState(state); await Display(); var loaded=window.CaptureState("loaded").PhaseFieldCrystal;
            Check(loaded.Field!.Density.SequenceEqual(state.PhaseFieldCrystal.Field!.Density),"Loaded PFC must pause on its exact checkpoint.");
            ((TextBox)FindAny(window,"PfcMeanBox")).Text="invalid"; InvokeEditor(editor,"PfcRestart_OnClick",window,new RoutedEventArgs());
            await Display();
            var corrected = window.CaptureState("corrected").PhaseFieldCrystal;
            Check(FractalExplorerWPF.Controls.NumericInput.IsValid((TextBox)FindAny(window,"PfcMeanBox")) && corrected.Field is not null,
                "Restart must correct the numeric draft and publish a fresh usable field.");
            live.StepOnce(); await Display(); Check(window.CaptureState("step").PhaseFieldCrystal.Field!.Step==corrected.Field!.Step+corrected.StepsPerFrame,"One frame must advance exactly once.");
            var before=window.CaptureState("before").PhaseFieldCrystal.Field!; InvokeEditor(editor,"Brush",.5,.5,.5); await Display();
            var brushed=window.CaptureState("brush").PhaseFieldCrystal.Field!; Check(brushed.Step==before.Step && brushed.Mean==before.Mean,"The UI brush must preserve time and mean.");
            ((ComboBox)FindAny(window,"PfcViewBox")).SelectedIndex=2;
            Check(window.CaptureState("modulation").PhaseFieldCrystal.View==PhaseFieldCrystal3DView.Modulation,"Modulation view must reach the common renderer.");
            window.LoadState(Fractal3DCatalog.CreateDefaultState(Fractal3DKind.PhaseFieldCrystal3D)); live.Pause(); await Display();
            Check(window.CaptureState("partial").PhaseFieldCrystal.Field is not null && !live.Running,"Stopping preparation must publish a usable partial field.");
            window.LoadState(state); window.LoadState(state); await Display();
            Check(window.CaptureState("reload").PhaseFieldCrystal.Field!.Density.SequenceEqual(state.PhaseFieldCrystal.Field.Density),"Old asynchronous results must never replace a later load.");
        } finally { live.Close(); window.Close(); }
    }
}
