using System.IO;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyHyperbolicHoneycombAsync(string[] args)
    {
        using var sandbox=DataSandbox.Create("hyperbolic-honeycomb");
        string output=args.Length>1 ? args[1] : Path.Combine(AppContext.BaseDirectory,"VerificationData","HyperbolicHoneycomb");
        Directory.CreateDirectory(output);
        var kind=Fractal3DKind.HyperbolicHoneycomb;
        var state=Fractal3DCatalog.CreateDefaultState(kind);
        var random=new Random(435534);
        foreach(var family in Enum.GetValues<HyperbolicHoneycombKind>())
        {
            var geometry=new HyperbolicHoneycombGeometry(family);
            var (p,q,r)=new HyperbolicHoneycombSettings { Tessellation=family }.Symbol;
            int[] orders=[p,q,r];
            for(int i=0;i<4;i++) for(int j=0;j<4;j++)
            {
                double expected=i==j ? 1 : Math.Abs(i-j)==1 ? -Math.Cos(Math.PI/orders[Math.Min(i,j)]) : 0;
                Check(Math.Abs(geometry.Mirrors[i].Dot(geometry.Mirrors[j])-expected)<1e-12,"Coxeter Gram matrix must match [p,q,r].");
            }
            Check(Math.Abs(geometry.Vertex.Dot(geometry.Vertex)+1)<1e-12 && geometry.Vertex.W>1,"Compact material vertex.");
            Check(Math.Abs(geometry.EdgeCentre.Dot(geometry.EdgeCentre)+1)<1e-12,"Material edge midpoint.");
            for(int j=1;j<4;j++) Check(Math.Abs(geometry.Vertex.Dot(geometry.Mirrors[j]))<1e-12,"Vertex must lie on its three mirrors.");
            for(int i=0;i<100;i++)
            {
                Vector3 ball=new((float)(random.NextDouble()-.5),(float)(random.NextDouble()-.5),(float)(random.NextDouble()-.5));
                var lifted=HyperbolicHoneycombGeometry.Lift(ball);
                var n=geometry.Mirrors[i%4];
                var reflected=HyperbolicHoneycombGeometry.Reflect(lifted,n);
                Check(Math.Abs(reflected.Dot(reflected)+1)<1e-10,"Reflection preserves the hyperboloid.");
                var twice=HyperbolicHoneycombGeometry.Reflect(reflected,n);
                Check(Vector3.Distance(twice.Ball,ball)<1e-6,"Reflection is an involution.");
                var boost=HyperbolicHoneycombGeometry.Boost(lifted,new(.3f,-.2f,.4f));
                Check(Math.Abs(boost.Dot(boost)+1)<1e-10,"Navigation boost preserves the metric.");
                var back=HyperbolicHoneycombGeometry.Boost(boost,new(-.3f,.2f,-.4f));
                Check(Vector3.Distance(back.Ball,ball)<1e-6,"Inverse boost restores the point.");
            }
            Check(Enumerable.Range(1,6).Select(geometry.ClipRadius).Zip(Enumerable.Range(2,5).Select(geometry.ClipRadius)).All(pair=>pair.First<pair.Second),"Depth grows the radial domain.");
        }
        foreach(var bad in new[] { state.HyperbolicHoneycomb with { Depth=0 }, state.HyperbolicHoneycomb with { ObserverX=.9 },
            state.HyperbolicHoneycomb with { ObserverY=double.NaN }, state.HyperbolicHoneycomb with { EdgeRadius=0 },
            state.HyperbolicHoneycomb with { Tessellation=(HyperbolicHoneycombKind)99 }, state.HyperbolicHoneycomb with { FaceOpacity=2 } })
        {
            bool rejected=false; try { bad.Validate(); } catch(ArgumentException) { rejected=true; }
            Check(rejected,"Invalid imports must be rejected.");
        }
        Console.WriteLine("PASS: Coxeter mirrors, material vertices, Lorentz reflections and navigation.");
        Vector3 observer=Vector3.Zero;
        for(int i=0;i<100;i++)
        {
            observer=HyperbolicHoneycombGeometry.Move(observer,Vector3.UnitX,.3);
            (state.HyperbolicHoneycomb with { ObserverX=observer.X,ObserverY=observer.Y,ObserverZ=observer.Z }).Validate();
        }
        using var renderer=new Fractal3DRenderer();
        int index=0;
        foreach(var preset in Fractal3DCatalog.GetPresets(kind))
        {
            var frame=await renderer.RenderAsync(preset,256,256,null,CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(frame),256,256),$"Blank hyperbolic preset: {preset.SaveName}");
            WriteLSystemPng(Path.Combine(output,$"preset-{index++}.png"),frame);
            Console.WriteLine($"Hyperbolic preset: {preset.SaveName}");
        }

        // Independent analytic radial ray to the central cell's face: its hyperbolic
        // inradius is asinh(n3.time). In the ball, d maps to tanh(d/2).
        foreach(var family in Enum.GetValues<HyperbolicHoneycombKind>())
        {
            var geometry=new HyperbolicHoneycombGeometry(family);
            var ray=state.Clone(); ray.CameraYaw=ray.CameraPitch=0; ray.CameraDistance=.001;
            ray.MaxSteps=1024; ray.Detail=.05;
            ray.HyperbolicHoneycomb=new() { Tessellation=family, Depth=1, ShowEdges=false, ShowFaces=true,FaceOpacity=1 };
            double expected=.001+Math.Tanh((Math.Asinh(geometry.Mirrors[3].W)-ray.HyperbolicHoneycomb.FaceThickness*.5)*.5);
            double actual=await renderer.ProbeDistanceAsync(ray,256,256,512,512,CancellationToken.None);
            Check(double.IsFinite(actual) && Math.Abs(expected-actual)<.001,$"Analytic {family} cell face: {actual:G8} vs {expected:G8}");
            // A boost along the same radial geodesic subtracts precisely 2 atanh(.1)
            // from the inradius. This independently checks the GPU navigation transform.
            ray.HyperbolicHoneycomb=ray.HyperbolicHoneycomb with { ObserverZ=-.1 };
            expected=.001+Math.Tanh((Math.Asinh(geometry.Mirrors[3].W)-2*Math.Atanh(.1)-ray.HyperbolicHoneycomb.FaceThickness*.5)*.5);
            actual=await renderer.ProbeDistanceAsync(ray,256,256,512,512,CancellationToken.None);
            Check(double.IsFinite(actual) && Math.Abs(expected-actual)<.001,$"Analytic {family} translated face: {actual:G8} vs {expected:G8}");
            ray.HyperbolicHoneycomb=ray.HyperbolicHoneycomb with { ObserverZ=0 };
            // The radial ray to the edge midpoint is orthogonal to its geodesic.
            // A tube of radius e is first reached at radial distance d(midpoint)-e.
            Vector3 edge=geometry.EdgeCentre.Ball;
            Vector3 back=-Vector3.Normalize(edge);
            ray.CameraYaw=Math.Atan2(back.X,back.Z)*180/Math.PI;
            ray.CameraPitch=Math.Asin(back.Y)*180/Math.PI;
            ray.HyperbolicHoneycomb=ray.HyperbolicHoneycomb with { ShowEdges=true,ShowFaces=false };
            expected=.001+Math.Tanh((Math.Acosh(geometry.EdgeCentre.W)-ray.HyperbolicHoneycomb.EdgeRadius)*.5);
            actual=await renderer.ProbeDistanceAsync(ray,256,256,512,512,CancellationToken.None);
            Check(double.IsFinite(actual) && Math.Abs(expected-actual)<.001,$"Analytic {family} edge tube: {actual:G8} vs {expected:G8}");
            Vector3 forward=Vector3.Normalize(edge);
            Vector3 target=edge+forward*.001f;
            ray.TargetX=target.X; ray.TargetY=target.Y; ray.TargetZ=target.Z;
            expected=Math.Tanh((Math.Acosh(geometry.EdgeCentre.W)+ray.HyperbolicHoneycomb.EdgeRadius)*.5)-edge.Length();
            actual=await renderer.ProbeDistanceAsync(ray,256,256,512,512,CancellationToken.None);
            Check(double.IsFinite(actual) && actual>0 && Math.Abs(expected-actual)<.001,
                $"Camera inside {family} tube must trace its exit: {actual:G8} vs {expected:G8}");
        }
        Console.WriteLine("PASS: independent analytic face and edge intersections from inside the cell.");

        var visible=state.Clone(); visible.HyperbolicHoneycomb=visible.HyperbolicHoneycomb with { ShowFaces=true };
        byte[] reference=Pixels(await renderer.RenderAsync(visible,128,128,null,CancellationToken.None));
        Func<HyperbolicHoneycombSettings,HyperbolicHoneycombSettings>[] changes=[
            s=>s with { Depth=1 },s=>s with { Depth=6 },s=>s with { EdgeRadius=.14 },s=>s with { ShowEdges=false },
            s=>s with { FaceOpacity=1 },s=>s with { FaceOpacity=0 },s=>s with { FaceThickness=.065 },
            s=>s with { CutAxis=1 },s=>s with { CutAxis=2 },s=>s with { CutAxis=3,CutPosition=-.2 },
            s=>s with { ObserverX=.45 },s=>s with { ObserverY=.45 },s=>s with { ObserverZ=.45 } ];
        foreach(var change in changes)
        {
            var changed=visible.Clone(); changed.HyperbolicHoneycomb=change(visible.HyperbolicHoneycomb);
            byte[] result=Pixels(await renderer.RenderAsync(changed,128,128,null,CancellationToken.None));
            Check(!result.SequenceEqual(reference),"Every geometry and navigation parameter must reach the GPU.");
        }
        foreach(var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var styled=visible.Clone(); styled.ShadingStyle=style;
            var frame=await renderer.RenderAsync(styled,128,128,null,CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(frame),128,128),$"Blank hyperbolic style {style}");
            WriteLSystemPng(Path.Combine(output,$"style-{style}.png"),frame);
        }
        var empty=state.Clone(); empty.HyperbolicHoneycomb=new() { ShowEdges=false,ShowFaces=false };
        Check(double.IsNaN(await renderer.ProbeDistanceAsync(empty,128,128,256,256,CancellationToken.None)),"Hidden geometry cannot produce a phantom ball shell.");
        var zeroOpacity=empty.Clone(); zeroOpacity.HyperbolicHoneycomb=empty.HyperbolicHoneycomb with { ShowFaces=true,FaceOpacity=0 };
        byte[] emptyPixels=Pixels(await renderer.RenderAsync(empty,128,128,null,CancellationToken.None));
        byte[] transparentPixels=Pixels(await renderer.RenderAsync(zeroOpacity,128,128,null,CancellationToken.None));
        Check(emptyPixels.SequenceEqual(transparentPixels),"Zero-opacity faces must be completely invisible.");
        Check(File.Exists(AppPaths.GetShaderCacheFile("fractal3d-HyperbolicHoneycomb-pixel")),"Shared shader bytecode cache.");

        visible.SaveName="Hyperbolic roundtrip";
        visible.HyperbolicHoneycomb=visible.HyperbolicHoneycomb with { EdgeRadius=.0712345678912345,ObserverX=.234567891234,ObserverY=-.1 };
        var store=new Fractal3DSaveStore(kind); store.Save(visible);
        var restored=store.Load().Single();
        Check(restored.HyperbolicHoneycomb==visible.HyperbolicHoneycomb,"Exact JSON parameter roundtrip.");
        byte[] restoredPixels=Pixels(await renderer.RenderAsync(restored,128,128,null,CancellationToken.None));
        byte[] originalPixels=Pixels(await renderer.RenderAsync(visible,128,128,null,CancellationToken.None));
        Check(restoredPixels.SequenceEqual(originalPixels),"Identical saved image.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().Category=="Fractal3DHyperbolicHoneycomb" && unreadable==0,"Cloud category.");

        var theme=new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if(!Application.Current.Resources.MergedDictionaries.Any(d=>d.Source==theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=theme });
        var window=new Fractal3DWindow(kind);
        try
        {
            Check(((Grid)window.FindName("InitialPreparationLayer")).Visibility==Visibility.Visible,"Shared first-frame preparation.");
            window.LoadState(visible);
            var editor=(HyperbolicHoneycombEditor)window.FindName("HyperbolicEditor");
            Check(window.CaptureState("UI").HyperbolicHoneycomb==visible.HyperbolicHoneycomb,"UI keeps imported precision.");
            ((Slider)editor.FindName("DepthSlider")).Value=4;
            Check(window.CaptureState("depth").HyperbolicHoneycomb.Depth==4,"Live depth control.");
            ((ComboBox)editor.FindName("KindBox")).SelectedIndex=0;
            Check(window.CaptureState("family").HyperbolicHoneycomb.Tessellation==HyperbolicHoneycombKind.Cubic435,"Live family control.");
            var apply=typeof(HyperbolicHoneycombEditor).GetMethod("ApplyPosition",BindingFlags.Instance|BindingFlags.NonPublic)!;
            ((TextBox)editor.FindName("ObserverXBox")).Text=".95";
            apply.Invoke(editor,[editor,new RoutedEventArgs()]);
            Check(editor.Capture().ObserverX==visible.HyperbolicHoneycomb.ObserverX,"Invalid observer edits must preserve the previous position.");
            ((TextBox)editor.FindName("ObserverXBox")).Text=".3";
            apply.Invoke(editor,[editor,new RoutedEventArgs()]);
            Check(editor.Capture().ObserverX==.3,"Apply observer position.");
            var root=(FrameworkElement)window.Content;
            root.Measure(new Size(1240,900)); root.Arrange(new Rect(0,0,1240,900)); root.UpdateLayout();
            var render=typeof(Fractal3DWindow).GetMethod("RenderFrameAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            await (Task)render.Invoke(window,[Enum.Parse(render.GetParameters()[0].ParameterType,"Full")])!;
            Check(((Image)window.FindName("CanvasImage")).Source is BitmapSource &&
                ((Grid)window.FindName("InitialPreparationLayer")).Visibility==Visibility.Collapsed,"First shown WPF frame dismisses preparation.");
            root.UpdateLayout();
            var bitmap=new RenderTargetBitmap(1240,900,96,96,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(root); WriteLSystemPng(Path.Combine(output,"hyperbolic-controls.png"),bitmap);
        }
        finally { window.Close(); }
        using var canceled=new CancellationTokenSource(); canceled.Cancel();
        bool stopped=false; try { await renderer.RenderAsync(visible,512,512,null,canceled.Token); }
        catch(OperationCanceledException) { stopped=true; }
        Check(stopped,"Canceled exports discard the output.");
        var item=FractalCatalog.Create().Single(t=>t.LaunchKey==Fractal3DCatalog.LaunchKey(kind));
        Check(item.IsThreeDimensional && CatalogPreviewLoader.IsRendered(item) && item.IntroducedAt is not null,"Catalog preview and history.");
        Console.WriteLine("PASS (hyperbolic-honeycomb): four honeycombs, analytic rays, boosts, eight presets, nine styles, transparent faces, cuts, saves/cloud, cache and WPF.");
    }
}
