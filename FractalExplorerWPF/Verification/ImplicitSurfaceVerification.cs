using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Cloud;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyImplicitSurfaceAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("implicit-surface");
        string output = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "VerificationData", "ImplicitSurface");
        Directory.CreateDirectory(output);
        var kind = Fractal3DKind.ImplicitSurface;
        Check(Math.Abs(CompiledRealSpaceExpression.Parse("-x^2+2*y+z^(-2)=a").Evaluate(-3, 2, 2, 1)+5.75)<1e-12,
            "Precedence, signed coordinates, negative integer powers and equations must evaluate in real arithmetic.");
        Check(Math.Abs(CompiledRealSpaceExpression.Parse("sin(pi*x)+cos(y)+abs(z)+sqrt(a)+exp(b)+log(c)+min(x,y)+max(y,z)")
            .Evaluate(0, 0, -2, 4, 0, 1)-6)<1e-12, "All supported functions must use real arithmetic.");
        foreach (string formula in new[] { "", "x;return 0", "unknown(x)", "x^0.5", "x^17", "x^a", "x=y=z", "sin()", "xy", new string('(',40)+"x"+new string(')',40), string.Join('+',Enumerable.Repeat("x",100)) })
        {
            bool rejected=false;
            try { CompiledRealSpaceExpression.Parse(formula); } catch(ArgumentException) { rejected=true; }
            Check(rejected,$"Invalid/over-budget formula accepted: {formula}");
        }
        foreach(var invalid in new[] { new ImplicitSurfaceSettings { Extent=0 }, new() { A=double.NaN },
            new() { B=double.PositiveInfinity }, new() { CutAxis=4 }, new() { Level=101 } })
        {
            bool rejected=false; try { invalid.Validate(); } catch(ArgumentException) { rejected=true; }
            Check(rejected,"Imported settings must be validated before rendering.");
        }
        var item=FractalCatalog.Create().Single(t=>t.LaunchKey==Fractal3DCatalog.LaunchKey(kind));
        Check(item.IsThreeDimensional && CatalogPreviewLoader.IsRendered(item) && item.IntroducedAt is not null,
            "The laboratory must have a dated 3D catalog entry and generated preview.");
        var state=Fractal3DCatalog.CreateDefaultState(kind);
        Check(state.ColoringMode==Fractal3DColoringMode.Material,"Default appearance must be material.");
        using var renderer=new Fractal3DRenderer();
        int index=0;
        foreach(var preset in Fractal3DCatalog.GetPresets(kind))
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var frame=await renderer.RenderAsync(preset,256,256,null,CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(frame),256,256),$"Empty preset: {preset.SaveName}");
            WriteLSystemPng(Path.Combine(output,$"preset-{index++}.png"),frame);
            Console.WriteLine($"Implicit preset: {preset.SaveName} · {watch.Elapsed.TotalSeconds:F2} s");
        }
        // Independent analytic roots along -Z, in a cube with R=1. These include
        // even roots and two sheets closer than a uniform grid sampling step.
        var ray=state.Clone(); ray.CameraYaw=ray.CameraPitch=ray.CameraRoll=0;
        ray.CameraDistance=3; ray.TargetX=ray.TargetY=ray.TargetZ=0;
        ray.MaxSteps=1024; ray.Detail=.05;
        (string Formula,double Z)[] equations=
        [
            ("x^2+y^2+z^2-a^2",.7), ("(x^2+y^2+z^2-a^2)^2",.7),
            ("z-0.201",.201), ("(z-0.201)^2",.201),
            ("(z-0.201)*(z-0.2011)",.2011), ("cos(pi*z)-0.5",1.0/3),
            ("abs(z)-0.6",.6), ("exp(z)-exp(0.4)",.4),
            ("log(z+2)-log(2.4)",.4), ("sqrt(z+2)-sqrt(2.4)",.4),
            ("min(z-0.5,z+0.5)",.5), ("max(z-0.5,z+0.5)",-.5)
        ];
        foreach(var equation in equations)
        {
            ray.ImplicitSurface=new() { Formula=equation.Formula, A=.7, Extent=1 };
            double actual=await renderer.ProbeDistanceAsync(ray,256,256,512,512,CancellationToken.None);
            Check(double.IsFinite(actual) && Math.Abs(actual-(3-equation.Z))<.001,
                $"Analytic root {equation.Formula}: expected {3-equation.Z:R}, GPU {actual:R}");
        }
        foreach(string empty in new[] { "1/z", "1", "0", "x-x", "z^2+0.001", "sqrt(-1)", "log(-1)" })
        {
            ray.ImplicitSurface=new() { Formula=empty, Extent=1 };
            Check(double.IsNaN(await renderer.ProbeDistanceAsync(ray,256,256,512,512,CancellationToken.None)),
                $"Poles, empty sets and undefined fields must not produce false roots: {empty}");
        }
        ray.ImplicitSurface=new() { Formula="x^2+y^2+z^2-a^2", A=.7, Extent=1, CutAxis=3, CutPosition=0 };
        Check(Math.Abs(await renderer.ProbeDistanceAsync(ray,256,256,512,512,CancellationToken.None)-3.7)<.001,
            "The cut must expose the far hemisphere without adding a false cap.");
        ray.ImplicitSurface=ray.ImplicitSurface with { CutAxis=0, Extent=2, Level=.15 };
        Check(Math.Abs(await renderer.ProbeDistanceAsync(ray,256,256,512,512,CancellationToken.None)-(3-Math.Sqrt(.49+.15)/2))<.001,
            "Coordinate range and nonzero isovalue must reach the GPU.");
        ray.ImplicitSurface=ray.ImplicitSurface with { Extent=1, Level=0 };
        ray.CameraDistance=.1;
        Check(Math.Abs(await renderer.ProbeDistanceAsync(ray,256,256,512,512,CancellationToken.None)-.8)<.001,
            "A camera inside a closed surface must see its exit.");

        byte[] reference=Pixels(await renderer.RenderAsync(state,160,160,null,CancellationToken.None));
        foreach(var style in Enum.GetValues<Fractal3DShadingStyle>())
        {
            var styled=state.Clone(); styled.ShadingStyle=style;
            var frame=await renderer.RenderAsync(styled,160,160,null,CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(frame),160,160),$"Empty shading style: {style}");
            WriteLSystemPng(Path.Combine(output,$"style-{style}.png"),frame);
        }
        var curved=state.Clone(); curved.ColoringMode=Fractal3DColoringMode.OrbitTrap;
        byte[] curvedFrame=Pixels(await renderer.RenderAsync(curved,160,160,null,CancellationToken.None));
        Check(!reference.SequenceEqual(curvedFrame),
            "Curvature coloring must change the frame.");
        foreach(var change in new Func<ImplicitSurfaceSettings,ImplicitSurfaceSettings>[] {
            s=>s with { A=.8 }, s=>s with { B=.2 }, s=>s with { Level=.15 }, s=>s with { Extent=2 }, s=>s with { CutAxis=2 } })
        {
            var changed=state.Clone(); changed.ImplicitSurface=change(state.ImplicitSurface);
            byte[] changedFrame=Pixels(await renderer.RenderAsync(changed,160,160,null,CancellationToken.None));
            Check(!reference.SequenceEqual(changedFrame),
                "Shape parameters must change the rendered frame.");
        }
        Check(File.Exists(AppPaths.GetShaderCacheFile(Fractal3DRenderer.ImplicitPixelShaderEntry(state.ImplicitSurface).Key)),
            "Dynamic formulas must use the shared disk shader cache.");
        state.SaveName="Implicit roundtrip";
        state.ImplicitSurface=state.ImplicitSurface with { C=.123456789123, CutAxis=1, CutPosition=.25 };
        var store=new Fractal3DSaveStore(kind); store.Save(state);
        var restored=store.Load().Single();
        Check(restored.ImplicitSurface==state.ImplicitSurface,"JSON must preserve formula and exact parameters.");
        Check(CloudSaveRepository.ListLocal(out int unreadable).Single().Category=="Fractal3DImplicitSurface" && unreadable==0,
            "The new mode must appear in cloud saves.");
        byte[] restoredFrame=Pixels(await renderer.RenderAsync(restored,128,128,null,CancellationToken.None));
        byte[] originalFrame=Pixels(await renderer.RenderAsync(state,128,128,null,CancellationToken.None));
        Check(restoredFrame.SequenceEqual(originalFrame),"Restored frames must match.");

        var theme=new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
        if(!Application.Current.Resources.MergedDictionaries.Any(d=>d.Source==theme))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=theme });
        var window=new Fractal3DWindow(kind);
        try
        {
            Check(((Grid)window.FindName("InitialPreparationLayer")).Visibility==Visibility.Visible,"Initial preparation must be visible.");
            window.LoadState(state);
            Check(window.CaptureState("UI").ImplicitSurface==state.ImplicitSurface,"UI must retain loaded precision.");
            Check(((FrameworkElement)window.FindName("ImplicitPanel")).Visibility==Visibility.Visible &&
                ((FrameworkElement)window.FindName("BailoutPanel")).Visibility==Visibility.Collapsed,"Mode controls must be visible.");
            var formula=(TextBox)window.FindName("ImplicitFormulaBox");
            formula.Text="x;bad";
            var apply=typeof(Fractal3DWindow).GetMethod("ImplicitApply_OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!;
            apply.Invoke(window,[window,new RoutedEventArgs()]);
            Check(window.CaptureState("Invalid edit").ImplicitSurface.Formula==state.ImplicitSurface.Formula,
                "Invalid edits must preserve the applied formula and current image.");
            formula.Text="x^2+y^2+z^2-a^2"; apply.Invoke(window,[window,new RoutedEventArgs()]);
            Check(window.CaptureState("Apply").ImplicitSurface.Formula==formula.Text,"Apply must update the formula.");
            ((Slider)window.FindName("ImplicitASlider")).Value=.75;
            Check(window.CaptureState("slider").ImplicitSurface.A==.75,"The parameter slider must reach the state.");
            window.LoadState(Fractal3DCatalog.CreateDefaultState(kind));
            var root=(FrameworkElement)window.Content;
            root.Measure(new Size(1240,900)); root.Arrange(new Rect(0,0,1240,900)); root.UpdateLayout();
            var render=typeof(Fractal3DWindow).GetMethod("RenderFrameAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
            await (Task)render.Invoke(window,[Enum.Parse(render.GetParameters()[0].ParameterType,"Full")])!;
            Check(((Image)window.FindName("CanvasImage")).Source is BitmapSource &&
                ((Grid)window.FindName("InitialPreparationLayer")).Visibility==Visibility.Collapsed,"A real first WPF frame must finish preparation.");
            root.UpdateLayout();
            var bitmap=new RenderTargetBitmap(1240,900,96,96,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(root); WriteLSystemPng(Path.Combine(output,"implicit-controls.png"),bitmap);
        }
        finally { window.Close(); }
        using var canceled=new CancellationTokenSource(); canceled.Cancel();
        bool discarded=false;
        try { await renderer.RenderAsync(state,1024,1024,null,canceled.Token); } catch(OperationCanceledException) { discarded=true; }
        Check(discarded,"Cancellation must discard export output.");
        Console.WriteLine("PASS (implicit-surface): parser, analytic roots, thin/even sheets, invalid domains, eight presets, nine styles, curvature, cuts, camera, cache, saves/cloud and WPF.");
    }
}
