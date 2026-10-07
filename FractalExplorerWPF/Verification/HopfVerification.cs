using System.IO;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyHopfAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("hopf");
        string output = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory,"VerificationData","Hopf");
        Directory.CreateDirectory(output);
        var plain = new HopfSettings();
        var rotated = plain with { XY=23,XZ=-16,XW=37,YZ=51,YW=-20,ZW=15 };
        foreach(var point in new[] { new HopfPoint(17,35),new(-140,-65),new(0,90),new(0,-90) })
        {
            Vector3 expected=point.OnSphere();
            foreach(double phase in Enumerable.Range(0,60).Select(i=>i*Math.Tau/60))
            {
                Vector4 q=HopfGeometry.PointOnS3(point,phase,plain);
                // Independent Hopf map; the fiber must have the same image for every phase.
                Vector3 image=new(2*(q.X*q.Z+q.Y*q.W),2*(q.Y*q.Z-q.X*q.W),q.X*q.X+q.Y*q.Y-q.Z*q.Z-q.W*q.W);
                Check(Vector3.Distance(image,expected)<1e-5 && Math.Abs(q.LengthSquared()-1)<1e-6,"Hopf fibers and unit S³.");
                q=HopfGeometry.PointOnS3(point,phase,rotated);
                Check(Math.Abs(q.LengthSquared()-1)<1e-6,"All six SO(4) rotations preserve S³.");
            }
        }
        foreach(var rotation in new[] { plain,rotated })
        {
            var s=rotation with { Family=HopfFamily.Custom, Points=[new(17,35),new(-140,-65),new(0,90),new(0,-90)] };
            var geometry=HopfGeometry.Build(s,CancellationToken.None);
            for(int id=0;id<s.Points.Count;id++)
                for(int j=0;j<100;j++)
                {
                    Vector4 q=HopfGeometry.PointOnS3(s.Points[id],j*Math.Tau/100,s);
                    if(1-q.W<.01) continue;
                    Vector3 p=new Vector3(q.X,q.Y,q.Z)*(HopfGeometry.Scale/(1-q.W));
                    Vector4 circle=geometry.Rings[id*3], plane=geometry.Rings[id*3+1];
                    Vector3 n=new(plane.X,plane.Y,plane.Z);
                    if(geometry.Rings[id*3+2].W>.5)
                        Check(Vector3.Cross(p,n).Length()<1e-4,"Stereographic pole is a straight line, without a closing segment.");
                    else
                    {
                        Check(Math.Abs(Vector3.Dot(p,n))<1e-4,"Circle plane contains every projected sample.");
                        Check(Math.Abs(Vector3.Distance(p,new(circle.X,circle.Y,circle.Z))-circle.W)<1e-4,"Analytic circle contains every projected sample.");
                    }
                }
            // Numerically integrate Gauss's linking integral, independently of the ring representation.
            var curves=new[] { Sample(new(0,0),rotation),Sample(new(90,0),rotation) };
            double link=0;
            for(int i=0;i<curves[0].Length;i++) for(int j=0;j<curves[1].Length;j++)
            {
                Vector3 a=curves[0][i], an=curves[0][(i+1)%curves[0].Length];
                Vector3 b=curves[1][j], bn=curves[1][(j+1)%curves[1].Length];
                Vector3 r=(a+an-b-bn)*.5f;
                link+=Vector3.Dot(r,Vector3.Cross(an-a,bn-b))/Math.Pow(r.Length(),3);
            }
            Check(Math.Abs(Math.Abs(link/Math.Tau/2)-1)<.005,"Distinct fibers link exactly once, also after SO(4) rotation.");
        }
        Console.WriteLine("Hopf map, SO(4), analytic circles/pole and Gauss linking integral passed.");

        var kind=Fractal3DKind.Hopf; var presets=Fractal3DCatalog.GetPresets(kind);
        Check(FractalCatalog.Create().Single(t=>t.LaunchKey==Fractal3DCatalog.LaunchKey(kind)).IsThreeDimensional,"Shared 3D catalog.");
        using var renderer=new Fractal3DRenderer();
        foreach(var preset in presets)
        {
            var bitmap=await renderer.RenderAsync(preset,256,256,null,CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap),256,256),$"Blank Hopf preset {preset.SaveName}.");
            WriteLSystemPng(Path.Combine(output,$"preset-{Array.IndexOf(presets.ToArray(),preset)}.png"),bitmap);
            Console.WriteLine($"GPU preset: {preset.SaveName}");
        }
        var state=presets[0].Clone();
        var baseline=Pixels(await renderer.RenderAsync(state,128,128,null,CancellationToken.None));
        foreach(var plane in Enum.GetValues<HopfRotationPlane>())
        {
            var angles=state.Hopf;
            state.Hopf=plane switch
            { HopfRotationPlane.XY=>angles with {XY=23},HopfRotationPlane.XZ=>angles with {XZ=23},
              HopfRotationPlane.XW=>angles with {XW=23},HopfRotationPlane.YZ=>angles with {YZ=23},
              HopfRotationPlane.YW=>angles with {YW=23},_=>angles with {ZW=23} };
            var pixels=Pixels(await renderer.RenderAsync(state,128,128,null,CancellationToken.None));
            Check(!baseline.SequenceEqual(pixels),$"Rotation {plane} must reach GPU."); state.Hopf=presets[0].Hopf.Copy();
        }
        foreach(var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            state=presets[4].Clone(); state.ShadingStyle=style;
            var bitmap=await renderer.RenderAsync(state,128,128,null,CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(bitmap),128,128),$"Blank Hopf style {style}.");
            WriteLSystemPng(Path.Combine(output,$"style-{style}.png"),bitmap);
        }
        state=presets[0].Clone(); state.Hopf=state.Hopf with { SelectedFiber=0,OnlySelected=true };
        var isolated=Pixels(await renderer.RenderAsync(state,128,128,null,CancellationToken.None));
        Check(!baseline.SequenceEqual(isolated),"Isolate chosen fiber.");
        object? cache=typeof(Fractal3DRenderer).GetField("_hopfNodes",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(renderer);
        state.CameraYaw+=10;
        await renderer.RenderAsync(state,96,96,null,CancellationToken.None);
        Check(ReferenceEquals(cache,typeof(Fractal3DRenderer).GetField("_hopfNodes",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(renderer)),"Camera changes reuse geometry.");
        await VerifyHopfProbe(renderer);

        state=presets[4].Clone(); state.SaveName="hopf-test";
        state.Hopf=state.Hopf with { XY=17,XW=34,SelectedFiber=1,OnlySelected=true,AnimationPlane=HopfRotationPlane.ZW,AnimationSpeed=-18 };
        var store=new Fractal3DSaveStore(kind); store.Save(state);
        var saved=store.Load().Single();
        Check(state.Hopf.GeometryEquals(saved.Hopf) && state.Hopf.AnimationSpeed==saved.Hopf.AnimationSpeed,"Save points, selection, rotations and animation controls.");
        var clone=state.Clone(); clone.Hopf.Points.Clear(); Check(state.Hopf.Points.Count==2,"Independent cloned point list.");
        var local=CloudSaveRepository.ListLocal(out int unreadable).Single(s=>s.Category==Fractal3DCatalog.GetDefinition(kind).SaveCategory);
        Check(unreadable==0 && local.JsonData.Contains("Hopf"),"Cloud envelope includes Hopf state.");

        var theme=new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml",UriKind.Absolute);
        if(!Application.Current.Resources.MergedDictionaries.Any(d=>d.Source==theme)) Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=theme});
        var window=new Fractal3DWindow(kind);
        try
        {
            window.LoadState(saved);
            var editor=(HopfEditor)window.FindName("HopfEditor");
            Check(editor.Visibility==Visibility.Visible && editor.Capture().GeometryEquals(saved.Hopf),"WPF saved fields.");
            Check(((TextBox)window.FindName("IterationsBox")).Visibility==Visibility.Collapsed,"No meaningless iterations input.");
            ((Slider)editor.FindName("XWSlider")).Value=72;
            Check(window.CaptureState("test").Hopf.XW==72,"Live 4D control.");
            editor.PickPoint(-1,new(15,30));
            Check(editor.Capture().Points.Count==3 && editor.Capture().SelectedFiber==2,"Adding a base point preserves existing fibers.");
            editor.Start(); editor.OnFrameDisplayed(); Check(editor.IsPlaying,"Animation advances with displayed frames.");
            window.LoadState(saved); Check(!editor.IsPlaying,"Loading a preset or save stops animation.");
            window.LoadState(presets[0]);
            var root=(FrameworkElement)window.Content;
            root.Measure(new Size(1240,800)); root.Arrange(new Rect(0,0,1240,800)); root.UpdateLayout();
            ((Image)window.FindName("CanvasImage")).Source=await renderer.RenderAsync(presets[0],800,740,null,CancellationToken.None);
            root.UpdateLayout();
            var image=new RenderTargetBitmap(1240,800,96,96,PixelFormats.Pbgra32); image.Render(root);
            WriteLSystemPng(Path.Combine(output,"editor.png"),image);
            ((Slider)editor.FindName("XWSlider")).BringIntoView(); root.UpdateLayout();
            var angles=new RenderTargetBitmap(1240,800,96,96,PixelFormats.Pbgra32); angles.Render(root);
            WriteLSystemPng(Path.Combine(output,"editor-angles.png"),angles);
        }
        finally { window.Close(); }
        using(var cts=new CancellationTokenSource())
        {
            cts.Cancel(); bool cancelled=false;
            try { HopfGeometry.Build(plain,cts.Token); } catch(OperationCanceledException) {cancelled=true;}
            Check(cancelled,"Cancellation.");
        }
        foreach(var bad in new[] {plain with {XW=double.NaN},plain with {Fibers=65},plain with {OnlySelected=true},plain with {Thickness=0},plain with {Family=HopfFamily.Custom,Points=[new(0,91)]}})
        {
            bool rejected=false; try {bad.Validate();} catch(ArgumentException) {rejected=true;} Check(rejected,"Invalid input rejected.");
        }
        var empty=presets[0].Clone(); empty.Hopf=new() {Family=HopfFamily.Custom};
        await renderer.RenderAsync(empty,32,32,null,CancellationToken.None);
        Console.WriteLine("PASS (hopf): six presets, nine styles, rotations, selection, probes, GPU cache, saves and WPF controls.");

        static Vector3[] Sample(HopfPoint point,HopfSettings rotation) => Enumerable.Range(0,384).Select(i=>
        {
            Vector4 q=HopfGeometry.PointOnS3(point,i*Math.Tau/384,rotation);
            return new Vector3(q.X,q.Y,q.Z)/(1-q.W);
        }).ToArray();
    }

    private static async Task VerifyHopfProbe(Fractal3DRenderer renderer)
    {
        var state=Fractal3DCatalog.CreateDefaultState(Fractal3DKind.Hopf);
        state.Hopf=new() {Family=HopfFamily.Custom,Points=[new(0,90)],Thickness=.045};
        state.CameraYaw=0; state.CameraPitch=0; state.CameraDistance=2.5; state.Detail=.05;
        Vector3 origin=Fractal3DCamera.Build(state).Position;
        int hits=0;
        for(int y=28;y<100;y+=6) for(int x=28;x<100;x+=6)
        {
            Vector3 direction=Fractal3DCamera.PixelRay(state,x+.5,y+.5,128,128);
            // The north fiber projects to x²+y²=Scale²,z=0. Dense independent sampling + bisection.
            double nearest=double.NaN;
            for(double t=.001;t<4;t+=.001)
                if(Distance(t)<0)
                {
                    double lo=t-.001,hi=t;
                    for(int i=0;i<25;i++) {double mid=(lo+hi)/2; if(Distance(mid)>0) lo=mid; else hi=mid;}
                    nearest=(lo+hi)/2; break;
                }
            double gpu=await renderer.ProbeDistanceAsync(state,x+.5,y+.5,128,128,CancellationToken.None);
            if(double.IsNaN(nearest)) continue; // Near tangencies use the renderer's pixel footprint.
            Check(double.IsFinite(gpu)&&Math.Abs(gpu-nearest)<.003,$"Hopf probe {x},{y}: {gpu} vs {nearest}"); hits++;
            double Distance(double t)
            {
                Vector3 p=origin+direction*(float)t;
                double radial=Math.Sqrt(p.X*p.X+p.Y*p.Y)-HopfGeometry.Scale;
                return Math.Sqrt(radial*radial+p.Z*p.Z)-state.Hopf.Thickness;
            }
        }
        Check(hits>10,"Probe covers ring surface, not just the background.");
    }
}
