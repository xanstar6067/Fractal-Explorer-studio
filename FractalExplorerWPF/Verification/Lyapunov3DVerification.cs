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
    private static async Task VerifyLyapunov3DAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("lyapunov3d");
        string? output = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null;
        if (output is not null) Directory.CreateDirectory(output);
        using var renderer = new Fractal3DRenderer();
        var state = Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Lyapunov3D);
        if (args.Contains("--window-only"))
        {
            state.Lyapunov=state.Lyapunov with {SliceOnly=true,CutAxis=3};
            state.CameraYaw=0;state.CameraPitch=0;
            await VerifyLyapunovWindowAsync(state,output);
            Console.WriteLine("PASS (lyapunov3d window): settings, bitmap structure, layout and invalid input.");
            return;
        }
        if (args.Contains("--probe"))
        {
            int probe=0;
            foreach(string pattern in new[]{"ABCCAAB","ABCBBACC","ABBBCA"})
            foreach(double low in new[]{3.0,3.2,3.4})
            {
                state.Lyapunov=new() {Pattern=pattern,MinA=low,MinB=low,MinC=low,MaxA=3.8,MaxB=3.8,MaxC=3.8,
                    Region=Lyapunov3DRegion.Stable,ColorRange=.4};
                var image=await renderer.RenderAsync(state,400,400,null,CancellationToken.None);
                if(output is not null)PhysarumSavePng(image,Path.Combine(output,$"probe-{probe++}-{pattern}-{low:F1}.png"));
            }
            return;
        }
        var watch = Stopwatch.StartNew();
        var stable = new Lyapunov3DSettings {
            Size = 32, MinA = 2.4, MaxA = 2.8, MinB = 2.2, MaxB = 2.5, MinC = 2.5, MaxC = 2.7,
            Transient = 97, Samples = 113, InitialX = .413
        };
        // Independent double orbit, with an odd warmup to expose sequence phase resets.
        foreach (string pattern in new[] { "A", "ABC", "BCABAC", new string('A', 62)+"BC" })
        {
            state.Lyapunov = stable with { Pattern = pattern };
            float[] field = await Task.Run(() => renderer.ReadLyapunovField(state, CancellationToken.None));
            double error = 0;
            for (int z = 0; z < 32; z += 3) for (int y = 0; y < 32; y += 3) for (int x = 0; x < 32; x += 3)
            {
                double a = stable.MinA+(stable.MaxA-stable.MinA)*(x+.5)/32;
                double b = stable.MinB+(stable.MaxB-stable.MinB)*(y+.5)/32;
                double c = stable.MinC+(stable.MaxC-stable.MinC)*(z+.5)/32;
                double expected = LyapunovReference(a,b,c,pattern,97,113,.413);
                error = Math.Max(error,Math.Abs(field[(z*32+y)*32+x]-expected));
                if (pattern == "A") Check(Math.Abs(expected-Math.Log(Math.Abs(2-a))) < 1e-9,"Fixed point has analytic λ = log|2-r|.");
            }
            Check(error < 2e-4,$"Independent double orbit / {pattern}: error {error}.");
            Check(field.All(float.IsFinite),"The raw exponent field is finite.");
            Console.WriteLine($"Independent orbit {pattern}: max error {error:E3}.");
        }
        foreach (int size in new[] { 64, 128 })
        {
            state.Lyapunov = stable with { Size = size, Pattern = "A", Transient = 64, Samples = 64 };
            var field = await Task.Run(() => renderer.ReadLyapunovField(state,CancellationToken.None));
            Check(field.Length == size*size*size && field.All(float.IsFinite),$"Grid {size}³ is complete.");
            for (int x=0; x<size; x+=11)
                Check(Math.Abs(field[x]-Math.Log(Math.Abs(2-(stable.MinA+(stable.MaxA-stable.MinA)*(x+.5)/size)))) < 2e-4,
                    $"Grid {size}: texel centre and exponent agree with fixed point.");
        }
        // A two-letter sequence must be an exact extrusion along C, not invented 3D dynamics.
        state.Lyapunov = stable with { Pattern = "AB" };
        var ab = await Task.Run(() => renderer.ReadLyapunovField(state,CancellationToken.None));
        for(int z=1;z<32;z++) Check(ab.AsSpan(0,1024).SequenceEqual(ab.AsSpan(z*1024,1024)),"AB is invariant along C.");
        state.Lyapunov = stable with { Pattern = "A", Transient = 0, Samples = 16, InitialX = .5 };
        var singular = await Task.Run(() => renderer.ReadLyapunovField(state,CancellationToken.None));
        Check(Math.Abs(singular[0]-LyapunovReference(2.40625,2.2,2.5,"A",0,16,.5)) < 2e-4,"Zero derivative uses the documented finite logarithm floor.");

        var invalid = new[] { stable with { Pattern="ABQ" }, stable with { Pattern="" }, stable with { Pattern=new string('A',65) },
            stable with { MinA=3,MaxA=2 }, stable with { MinA=double.NaN }, stable with { MaxC=4.1 },
            stable with { Samples=0 }, stable with { Size=96 }, stable with { SliceOnly=true,CutAxis=0 },
            stable with { InitialX=1 }, stable with { ColorRange=0 } };
        foreach(var settings in invalid)
        {
            bool rejected=false; try {settings.Validate();} catch(ArgumentException){rejected=true;}
            Check(rejected,"Invalid field/view settings are rejected before allocating GPU resources.");
        }
        Check((stable with {Pattern=" a b c \n"}).Sequence=="ABC","Input accepts whitespace and lowercase Latin letters.");
        Console.WriteLine($"Numerical checks, grids and validation: {watch.Elapsed.TotalSeconds:F2}s; {renderer.DeviceHost.AdapterName}.");

        int index=0;
        foreach(var preset in Fractal3DCatalog.GetPresets(Fractal3DKind.Lyapunov3D))
        {
            var image=await renderer.RenderAsync(preset,360,360,null,CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(image),360,360),$"Preset {preset.SaveName} has structure.");
            if(output is not null) PhysarumSavePng(image,Path.Combine(output,$"preset-{index}.png"));
            Console.WriteLine($"Rendered preset {index++}: {preset.SaveName}.");
        }
        state=Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Lyapunov3D);
        byte[] first=Pixels(await renderer.RenderAsync(state,160,160,null,CancellationToken.None)).ToArray();
        int builds=renderer.LyapunovFieldBuilds;
        foreach(var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            state.ShadingStyle=style;
            var image=await renderer.RenderAsync(state,160,160,null,CancellationToken.None);
            if(output is not null) PhysarumSavePng(image,Path.Combine(output,$"style-{style}.png"));
            Check(HasFractal3DStructure(Pixels(image),160,160),$"Style {style} renders.");
        }
        state.ShadingStyle=Fractal3DShadingStyle.Studio;
        var cut=state.Clone();cut.Lyapunov=cut.Lyapunov with {CutAxis=3,CutPosition=-.3};
        byte[] second=Pixels(await renderer.RenderAsync(cut,160,160,null,CancellationToken.None)).ToArray();
        Check(!first.SequenceEqual(second),"Clipping reveals a different part of the field.");
        foreach(int axis in new[]{1,2,3})
        {
            cut.Lyapunov=cut.Lyapunov with {SliceOnly=true,CutAxis=axis,CutPosition=.2};
            cut.CameraYaw=axis==1?90:0;cut.CameraPitch=axis==2?89.9:0;
            var image=await renderer.RenderAsync(cut,160,160,null,CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(image),160,160),$"Slice axis {axis} renders.");
            if(output is not null) PhysarumSavePng(image,Path.Combine(output,$"slice-{axis}.png"));
        }
        state.ColoringMode=Fractal3DColoringMode.CrossTrap;
        second=Pixels(await renderer.RenderAsync(state,160,160,null,CancellationToken.None)).ToArray();
        Check(!first.SequenceEqual(second),"Color by A differs from exponent coloring.");
        Check(renderer.LyapunovFieldBuilds==builds,"Styles, clipping, slices and coloring reuse the GPU field.");
        double hit=await renderer.ProbeDistanceAsync(cut,80,80,160,160,CancellationToken.None);
        Check(double.IsFinite(hit)&&hit>0,"The camera probe hits the slice plane.");

        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();
            var frame=await renderer.RenderPixelsAsync(state,120,90,null,null,cancel.Token);
            Check(!frame.Completed && renderer.LyapunovFieldBuilds==builds,"Pre-cancelled render leaves the field intact.");
        }
        using(var cancel=new CancellationTokenSource())
        {
            var different=state.Clone();different.Lyapunov=state.Lyapunov with {Pattern="ABC"};
            var progress=new InlineLyapunovProgress(value=>{if(value>0)cancel.Cancel();});
            var frame=await renderer.RenderPixelsAsync(different,120,90,null,progress,cancel.Token);
            Check(!frame.Completed && renderer.LyapunovFieldBuilds==builds,"Cancellation during calculation publishes no partial field.");
        }
        await renderer.RenderAsync(state,120,90,null,CancellationToken.None);
        Check(renderer.LyapunovFieldBuilds==builds,"The previous complete field survives cancellation.");
        state.Lyapunov=state.Lyapunov with {InitialX=.413,Samples=256};
        await renderer.RenderAsync(state,120,90,null,CancellationToken.None);
        Check(renderer.LyapunovFieldBuilds==builds+1,"Orbit parameters invalidate the field.");

        state.SaveName="Ляпунов · проверка";state.CameraRoll=17;state.Lyapunov=cut.Lyapunov;
        var store=new Fractal3DSaveStore(Fractal3DKind.Lyapunov3D);store.Save(state);
        var loaded=store.Load().Single();
        Check(loaded.Kind==state.Kind && loaded.Lyapunov==state.Lyapunov && loaded.CameraRoll==17,"JSON preserves field, view and camera settings.");
        byte[] original=Pixels(await renderer.RenderAsync(state,160,160,null,CancellationToken.None)).ToArray();
        byte[] roundtrip=Pixels(await renderer.RenderAsync(loaded,160,160,null,CancellationToken.None)).ToArray();
        Check(original.SequenceEqual(roundtrip),"A loaded save renders the same image.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Any(s=>s.Category=="Fractal3DLyapunov")&&unreadable==0,"Cloud repository finds the separate Lyapunov category.");
        var old=JsonSerializer.Deserialize<Fractal3DState>("{\"Kind\":0}",JsonOptionsFactory.Create())!;
        Check(old.Kind==Fractal3DKind.Mandelbulb && old.Lyapunov is not null,"Existing enum numbers and old 3D saves still load.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("lyapunov3d-pixel")) && File.Exists(AppPaths.GetShaderCacheFile("lyapunov3d-field")),"Both shaders use the common disk cache.");
        await VerifyLyapunovWindowAsync(loaded,output);
        Console.WriteLine("PASS (lyapunov3d): independent orbit, analytic exponent, all grids, five presets, nine styles, slices, probe, reuse, cancellation, saves, cloud and WPF.");
    }

    private sealed class InlineLyapunovProgress(Action<int> callback):IProgress<int>
    { public void Report(int value)=>callback(value); }

    private static double LyapunovReference(double a,double b,double c,string pattern,int transient,int samples,double x)
    {
        double total=0;
        for(int i=0;i<transient+samples;i++)
        {
            double r=pattern[i%pattern.Length] switch {'A'=>a,'B'=>b,_=>c};
            if(i>=transient) total+=Math.Log(Math.Max(Math.Abs(r*(1-2*x)),1e-30));
            x=r*x*(1-x);
        }
        return total/samples;
    }

    private static async Task VerifyLyapunovWindowAsync(Fractal3DState state,string? output)
    {
        var theme=new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if(!Application.Current.Resources.MergedDictionaries.Any(d=>d.Source==theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=theme});
        var window=new Fractal3DWindow(Fractal3DKind.Lyapunov3D);
        try
        {
            window.LoadState(state);
            Check(window.CaptureState("window").Lyapunov==state.Lyapunov,"WPF loads and captures every setting.");
            Check(((FrameworkElement)window.FindName("IterationsBox")).Visibility==Visibility.Collapsed,"Generic DE iterations are hidden.");
            var root=(FrameworkElement)window.Content;root.Measure(new Size(1180,850));root.Arrange(new Rect(0,0,1180,850));root.UpdateLayout();
            var method=typeof(Fractal3DWindow).GetMethod("RenderFrameAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            await(Task)method.Invoke(window,[Enum.Parse(method.GetParameters()[0].ParameterType,"Full")])!;
            var frame=((Image)window.FindName("CanvasImage")).Source as BitmapSource;
            Check(frame is not null && HasFractal3DStructure(Pixels(frame),frame.PixelWidth,frame.PixelHeight),"The actual WPF frame has structure.");
            root.UpdateLayout();
            if(output is not null)
            {
                var image=new RenderTargetBitmap(1180,850,96,96,PixelFormats.Pbgra32);image.Render(root);
                PhysarumSavePng(image,Path.Combine(output,"window.png"));
            }
            ((ComboBox)window.FindName("LyapunovCutBox")).SelectedIndex=0;
            Check(window.CaptureState("slice").Lyapunov.CutAxis==3,"Slice mode always keeps a valid plane.");
            ((TextBox)window.FindName("LyapunovPatternBox")).Text="bad input";
            await(Task)method.Invoke(window,[Enum.Parse(method.GetParameters()[0].ParameterType,"Full")])!;
            Check(((TextBlock)window.FindName("StatusText")).Text.Contains("Последовательность"),"Invalid formula is reported without replacing the frame.");
            window.LoadState(state);
            ((TextBox)window.FindName("LyapunovMinABox")).Text="-";
            await(Task)method.Invoke(window,[Enum.Parse(method.GetParameters()[0].ParameterType,"Full")])!;
            Check(((TextBlock)window.FindName("StatusText")).Text.Contains("Минимум A"),"An unfinished numeric edit is reported without an event-handler failure.");
        }
        finally {window.Close();}
    }
}
