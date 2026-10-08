using System.IO;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    // Independent expanded powers and explicit published folds. This oracle does
    // not compile/evaluate the production expression graph.
    private static Complex PolynomialOracle(FoldedFormulaDefinition d, double x, double y)
    {
        double x2 = x*x, y2 = y*y, x4 = x2*x2, y4 = y2*y2;
        double a = d.Degree == 3 ? x2-3*y2 : x4+5*y4-10*x2*y2;
        double b = d.Degree == 3 ? 3*x2-y2 : 5*x4+y4-10*x2*y2;
        double u = x4+y4-6*x2*y2, v = x2-y2;
        Complex Pow(double xx,double yy)
        {
            if (d.Degree==3) return new(xx*(xx*xx-3*yy*yy),yy*(3*xx*xx-yy*yy));
            if (d.Degree==4) return new(Math.Pow(xx,4)+Math.Pow(yy,4)-6*xx*xx*yy*yy,4*xx*yy*(xx*xx-yy*yy));
            return new(xx*(Math.Pow(xx,4)+5*Math.Pow(yy,4)-10*xx*xx*yy*yy),yy*(5*Math.Pow(xx,4)+Math.Pow(yy,4)-10*xx*xx*yy*yy));
        }
        return d.Kind switch
        {
            "BurningShip" => Pow(Math.Abs(x),Math.Abs(y)), "Mandelbar" => Pow(x,-y),
            "Buffalo" => new(Math.Abs(Pow(x,y).Real),Math.Abs(Pow(x,y).Imaginary)),
            "Celtic" => new(Math.Abs(Pow(x,y).Real),Pow(x,y).Imaginary),
            "PartialBurningShipReal" => Pow(Math.Abs(x),y), "PartialBurningShipImag" => Pow(x,Math.Abs(y)),
            "PartialBurningShipRealMandelbar" => Pow(Math.Abs(x),-y),
            "CelticPartialBurningShipImag" => new(Math.Abs(Pow(x,Math.Abs(y)).Real),Pow(x,Math.Abs(y)).Imaginary),
            "CelticPartialBurningShipReal" => new(Math.Abs(Pow(Math.Abs(x),y).Real),Pow(Math.Abs(x),y).Imaginary),
            "CelticPartialBurningShipRealMandelbar" => new(Math.Abs(Pow(Math.Abs(x),-y).Real),Pow(Math.Abs(x),-y).Imaginary),
            "BuffaloPartialImag" => new(Pow(x,y).Real,Math.Abs(Pow(x,y).Imaginary)),
            "CelticMandelbar" => new(Math.Abs(Pow(x,-y).Real),Pow(x,-y).Imaginary),
            "QuasiBurningShip" => new(Pow(Math.Abs(x),y).Real,-Math.Abs(Pow(Math.Abs(x),y).Imaginary)),
            "QuasiPerpendicular" => new(Math.Abs(x)*a,-y*Math.Abs(b)), "QuasiHeart" => new(Math.Abs(x)*a,y*Math.Abs(b)),
            "CelticQuasiPerpendicular" => new(Math.Abs(x*a),-y*Math.Abs(b)), "CelticQuasiHeart" => new(Math.Abs(x*a),y*Math.Abs(b)),
            "QuasiPerpendicularBurningShip" => new(Math.Abs(y)*b,-x*Math.Abs(a)),
            "QuasiPerpendicularBuffalo" => new(Math.Abs(y)*Math.Abs(b),-(d.Degree==5 ? Math.Abs(x) : x)*Math.Abs(a)),
            "FalseQuasiPerpendicular" => new(u,-4*x*y*Math.Abs(v)), "FalseQuasiHeart" => new(u,4*x*y*Math.Abs(v)),
            "CelticFalseQuasiPerpendicular" => new(Math.Abs(u),-4*x*y*Math.Abs(v)), "CelticFalseQuasiHeart" => new(Math.Abs(u),4*x*y*Math.Abs(v)),
            "ImagQuasi" => new(u,4*x*Math.Abs(y*v)), "CelticImagQuasi" => new(Math.Abs(u),4*x*Math.Abs(y*v)),
            "RealQuasiPerpendicular" => new(u,-4*y*Math.Abs(x*v)), "RealQuasiHeart" => new(u,4*y*Math.Abs(x*v)),
            "CelticRealQuasiPerpendicular" => new(Math.Abs(u),-4*y*Math.Abs(x*v)), "CelticRealQuasiHeart" => new(Math.Abs(u),4*y*Math.Abs(x*v)),
            _ => throw new InvalidOperationException(d.Kind)
        };
    }
    private static async Task ProbePolynomialPresets()
    {
        var output = new List<object>();
        foreach (var d in FoldedFormulaCatalog.All)
        {
            var p = FoldedPolynomialProgram.For(d.Parameter);
            int Escape(double x,double y,double cx,double cy,int limit)
            {
                int n=0;while(n<limit && x*x+y*y<4) {var v=p.Evaluate(x,y);x=v.Real+cx;y=v.Imaginary+cy;n++;}return n;
            }
            var candidates=new List<(double X,double Y,int N)>();
            for(int yy=0;yy<81;yy++)for(int xx=0;xx<81;xx++)
            {
                double x=-1.7+xx*3.4/80,y=-1.7+yy*3.4/80;
                int n=Escape(0,0,x,y,160);if(n>=20 && n<160)candidates.Add((x,y,n));
            }
            var finalists=new List<(double X,double Y,double Score)>();
            foreach(var c in candidates.OrderByDescending(c=>c.N).Take(32))
            {
                const int w=56;var inside=new bool[w*w];int area=0,edges=0;
                for(int yy=0;yy<w;yy++)for(int xx=0;xx<w;xx++)
                {
                    inside[yy*w+xx]=Escape(-1.5+3.0*xx/w,-1.5+3.0*yy/w,c.X,c.Y,350)==350;
                    if(inside[yy*w+xx])area++;
                }
                for(int yy=1;yy<w;yy++)for(int xx=1;xx<w;xx++)if(inside[yy*w+xx])
                {if(!inside[yy*w+xx-1])edges++;if(!inside[(yy-1)*w+xx])edges++;}
                double fraction=(double)area/(w*w);
                double score=fraction is >0.025 and <0.5 ? edges/Math.Sqrt(Math.Max(1,area)) : 0;
                finalists.Add((c.X,c.Y,score));
            }
            var best=finalists.OrderByDescending(c=>c.Score).First();
            var second=finalists.OrderByDescending(c=>c.Score).FirstOrDefault(c=>Math.Abs(c.X-best.X)+Math.Abs(c.Y-best.Y)>0.3);
            if(second.Score==0)second=best;
            output.Add(new{Key=d.Parameter.ToString(),X=Math.Round(best.X,4),Y=Math.Round(best.Y,4),X2=Math.Round(second.X,4),Y2=Math.Round(second.Y,4),best.Score});
            Console.WriteLine($"{d.Parameter}: C={best.X:0.####},{best.Y:0.####}; score={best.Score:0.00}");
            await Task.Yield();
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"polynomial-presets.json"),System.Text.Json.JsonSerializer.Serialize(output));
    }

    private static async Task VerifyFoldedPolynomialsAsync(string[] args)
    {
        using var sandbox = DataSandbox.Create("folded-polynomials"); EnsureThemeStyles();
        string folder = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine(AppContext.BaseDirectory,"VerificationData","FoldedPolynomials");
        Directory.CreateDirectory(folder); int steps=0, renders=0;
        double[] coordinates = [-1.1,-0.79,-0.31,0,0.24,0.68,1.05];
        foreach (var d in FoldedFormulaCatalog.All)
        {
            var program = FoldedPolynomialProgram.For(d.Parameter);
            foreach (double x in coordinates) foreach (double y in coordinates)
            {
                var expected = PolynomialOracle(d,x,y); var actual = program.Evaluate(x,y,true);
                Check(Complex.Abs(expected-new Complex(actual.Real,actual.Imaginary)) < 2e-11,$"{d.Name}: independent polynomial at {x},{y}");
                var dec = program.EvaluateDecimal((decimal)x,(decimal)y);
                Check(Complex.Abs(expected-new Complex(dec.Real,dec.Imaginary)) < 2e-11,$"{d.Name}: decimal polynomial");
                using var precision = new BigFloat.PrecisionScope(384);
                var workspace = new BigFloat[program.Nodes.Length];
                var big = program.EvaluateBig(BigFloat.FromDecimal((decimal)x),BigFloat.FromDecimal((decimal)y),workspace);
                Check(Complex.Abs(expected-new Complex(big.Real.ToDouble(),big.Imaginary.ToDouble())) < 2e-11,$"{d.Name}: BigFloat polynomial");
                var reference = workspace.Select(FloatExp.FromBigFloat).ToArray();
                var delta = program.Delta(reference,1e-5,-2e-5,true);
                var shifted=PolynomialOracle(d,x+1e-5,y-2e-5);
                Check(Complex.Abs(shifted-expected-new Complex(delta.Real.ToDouble(),delta.Imaginary.ToDouble()))<2e-11,$"{d.Name}: stable delta");
                if (x!=0 && y!=0 && Math.Abs(x*x-y*y)>0.1)
                {
                    const double h=1e-6; var ddx=(PolynomialOracle(d,x+h,y)-PolynomialOracle(d,x-h,y))/(2*h);
                    var ddy=(PolynomialOracle(d,x,y+h)-PolynomialOracle(d,x,y-h))/(2*h);
                    Check(Complex.Abs(ddx-new Complex(actual.Xx,actual.Yx))<2e-5 && Complex.Abs(ddy-new Complex(actual.Xy,actual.Yy))<2e-5,$"{d.Name}: finite difference Jacobian");
                }
                steps++;
            }
            foreach (var variant in new[]{d.Parameter,d.Julia})
            {
                var def=MandelbrotVariantDefinition.For(variant);
                Check(def.Identifier==variant.ToString() && def.DefaultPower==d.Degree && !def.HasPower,"separate fixed-degree modes");
                var item=FractalCatalog.Create().Single(i=>i.LaunchKey==variant.ToString()); Check(CatalogPreviewLoader.IsRendered(item),"own generated preview");
                var presets=PresetManager.GetMandelbrotPresets(variant); Check(presets.Count==2,"two own presets");
                foreach(var preset in presets)
                {
                    byte[] bytes=await PolynomialRender(preset,56,42);
                    Check(bytes.Chunk(4).Select(p=>Convert.ToHexString(p)).Distinct().Count()>4,$"{variant}: useful preset");
                    SavePolynomialPng(Path.Combine(folder,variant+"-"+presets.ToList().IndexOf(preset)+".png"),bytes,56,42); renders++;
                }
                var window=new MandelbrotWindow(variant);
                try
                {
                    var state=presets[0]; window.LoadState(state); var copy=window.CaptureState("roundtrip");
                    Check(copy.Variant==variant && copy.Power==d.Degree,"WPF state roundtrip");
                    Check(((FrameworkElement)window.FindName("PowerPanel")).Visibility==Visibility.Collapsed,"fixed-degree UI");
                    var store=new MandelbrotSaveStore(variant); store.Save(copy, store.LoadSlots().Slots.FirstOrDefault()); var loaded=store.Load().Single();
                    Check(loaded.Variant==variant && loaded.Power==d.Degree,"own save category roundtrip");
                }
                finally { window.Close(); }
            }
            var sample=PresetManager.GetMandelbrotPresets(d.Parameter)[0]; sample.Iterations=70;
            foreach(var color in Enum.GetValues<MandelbrotColoringMode>())
            {
                sample.ColoringMode=color;
                byte[] direct=await PolynomialRender(sample,24,18,false);
                byte[] deep=await PolynomialRender(sample,24,18,true);
                Check(PolynomialDifference(direct,deep)<0.07,$"{d.Name} {color}: direct vs perturbation"); renders++;
            }
            // Homogeneous published folds preserve |z|^p. C=0 Julia has a unit-circle
            // boundary with both escaping and interior pixels at arbitrary depth.
            if (!args.Contains("--fast")) foreach(int exponent in new[]{12,80,350,1000})
            {
                var state=PresetManager.GetMandelbrotPresets(d.Julia)[0]; state.JuliaCReal=0;state.JuliaCImaginary=0;
                state.CenterX=0.6m;state.CenterY=0.8m;state.CenterXExact="0.6";state.CenterYExact="0.8";
                state.Zoom=FloatExp.Pow10(exponent); state.Iterations=(int)Math.Ceiling(exponent*Math.Log(10)/Math.Log(d.Degree))+100;
                state.PaletteScale=1; state.Palette.ColorPeriod=state.Iterations; state.Palette.AlignWithRenderIterations=true;
                byte[] actual=await PolynomialRender(state,4,3);
                byte[] exact=await Task.Run(()=>MandelbrotFamilyRenderer.RenderProgramExactForTests(state,4,3));
                Check(PolynomialDifference(actual,exact)<0.09,$"{d.Name}: exact boundary at 1e{exponent}");
                Check(actual.Chunk(4).Select(p=>Convert.ToHexString(p)).Distinct().Count()>1,$"{d.Name}: resolved deep boundary 1e{exponent}");
                renders++;
            }
            Console.WriteLine($"PASS {d.Name}: oracles, seven colours, presets, saves, 1e1000");
        }
        await VerifyPolynomialBlaAndTiles();
        await VerifyPolynomialHybrid(folder);
        Console.WriteLine($"PASS folded-polynomials: {steps} independent steps, {renders} renders, 46 pairs and hybrid editor.");
    }
    private static async Task<byte[]> PolynomialRender(MandelbrotState state,int w,int h,bool? force=null)
    {
        MandelbrotFamilyRenderer.ForceDeepZoomForTests=force;
        try { var bytes=new byte[w*h*4]; await Task.Run(()=>MandelbrotFamilyRenderer.Render(state,bytes,w,h,w*4,CancellationToken.None)); return bytes; }
        finally { MandelbrotFamilyRenderer.ForceDeepZoomForTests=null; }
    }
    private static double PolynomialDifference(byte[] a,byte[] b) => a.Chunk(4).Zip(b.Chunk(4)).Count(p=>
        Math.Abs(p.First[0]-p.Second[0])>3 || Math.Abs(p.First[1]-p.Second[1])>3 || Math.Abs(p.First[2]-p.Second[2])>3)/(a.Length/4.0);
    private static void SavePolynomialPng(string path,byte[] bytes,int w,int h)
    {
        var image=System.Windows.Media.Imaging.BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,bytes,w*4);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var stream=File.Create(path);encoder.Save(stream);
    }
    private static async Task VerifyFoldedFinalAsync()
    {
        using var sandbox=DataSandbox.Create("folded-final");EnsureThemeStyles();
        string folder=Path.Combine(AppContext.BaseDirectory,"VerificationData","FoldedFinal");Directory.CreateDirectory(folder);
        foreach(var d in FoldedFormulaCatalog.All)
        {
            foreach(var variant in new[]{d.Parameter,d.Julia})
            {
                int index=0;
                foreach(var state in PresetManager.GetMandelbrotPresets(variant))
                {
                    byte[] image=await PolynomialRender(state,96,72);
                    Check(image.Chunk(4).Select(p=>Convert.ToHexString(p)).Distinct().Count()>8,$"{variant}: final useful preset");
                    SavePolynomialPng(Path.Combine(folder,variant+"-"+(index++)+".png"),image,96,72);
                }
                var deep=PresetManager.GetMandelbrotPresets(variant)[0];deep.Zoom=FloatExp.Pow10(1000);deep.CenterXExact="0.6000000000000000000000000000000000000000000000000000000001";deep.CenterYExact="0.8";
                var window=new MandelbrotWindow(variant);
                try
                {
                    window.LoadState(deep);var copy=window.CaptureState("deep");
                    using var precision = new BigFloat.PrecisionScope(4000);
                    Check(copy.Zoom==deep.Zoom && BigFloat.Abs(BigFloat.Parse(copy.CenterXExact!)-BigFloat.Parse(deep.CenterXExact!)) < BigFloat.Parse("1e-1006") && copy.Power==d.Degree,"deep WPF roundtrip, fixed degree");
                    var store=new MandelbrotSaveStore(variant);store.Save(copy);var saved=store.Load().Single();
                    Check(saved.Zoom==deep.Zoom && saved.CenterXExact==copy.CenterXExact && saved.Variant==variant,"deep JSON roundtrip");
                }
                finally{window.Close();}
            }
        }
        await VerifyPolynomialBlaAndTiles();await VerifyPolynomialHybrid(folder);
        Console.WriteLine("PASS folded-final: all 188 presets, 92 deep WPF/JSON roundtrips, BLA, tile seams, hybrid editor, independent hybrid orbits and mixed-power 1e1000.");
    }

    private static async Task VerifyPolynomialBlaAndTiles()
    {
        long skippedTotal = 0;
        foreach (var d in FoldedFormulaCatalog.All)
        {
            var state = PresetManager.GetMandelbrotPresets(d.Julia)[0];
            state.JuliaCReal = 0; state.JuliaCImaginary = 0; state.CenterX = 0.6m; state.CenterY = 0.8m;
            state.Zoom = 1e12; state.Iterations = 180;
            MandelbrotFamilyRenderer.CountRealBlaSkipsForTests = true; MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests = 0;
            try
            {
                MandelbrotFamilyRenderer.ForceBlaForTests = true;
                byte[] accelerated = await PolynomialRender(state,16,12);
                skippedTotal += MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests;
                MandelbrotFamilyRenderer.ForceBlaForTests = false;
                byte[] scalar = await PolynomialRender(state,16,12);
                Check(PolynomialDifference(accelerated,scalar)<0.025,$"{d.Name}: BLA error bound");
            }
            finally { MandelbrotFamilyRenderer.ForceBlaForTests=null; MandelbrotFamilyRenderer.CountRealBlaSkipsForTests=false; }
        }
        Check(skippedTotal>100,"polynomial BLA must actually skip iterations");
        var sample=PresetManager.GetMandelbrotPresets(MandelbrotVariant.QuarticCelticRealQuasiHeart)[0];
        foreach(var mode in new[]{MandelbrotColoringMode.Smooth,MandelbrotColoringMode.OrbitTrap,MandelbrotColoringMode.DistanceEstimation})
        {
            sample.ColoringMode=mode;
            byte[] full=await PolynomialRender(sample,40,30);
            var tile=new MandelbrotRenderTile(11,7,13,12,0,0);
            byte[] tiled=MandelbrotFamilyRenderer.RenderTile(sample,40,30,tile,CancellationToken.None)!;
            var crop=new byte[tiled.Length];
            for(int y=0;y<tile.Height;y++) Buffer.BlockCopy(full,((tile.Y+y)*40+tile.X)*4,crop,y*tile.Width*4,tile.Width*4);
            Check(tiled.SequenceEqual(crop),$"{mode}: full canvas and tile seams");
        }
        Console.WriteLine($"PASS polynomial BLA: {skippedTotal} skipped iterations; tile seams match.");
    }

    private static async Task VerifyPolynomialHybrid(string folder)
    {
        foreach(var variant in new[]{MandelbrotVariant.Hybrid,MandelbrotVariant.JuliaHybrid})
        {
            var window=new MandelbrotWindow(variant);
            try
            {
                foreach(var state in PresetManager.GetMandelbrotPresets(variant))
                {
                    window.LoadState(state); var copy=window.CaptureState("hybrid-save");
                    Check(copy.Hybrid.Steps.Select(s=>(s.Formula,s.Power,s.Repeats)).SequenceEqual(state.Hybrid.Steps.Select(s=>(s.Formula,s.Power,s.Repeats))),"hybrid WPF roundtrip");
                    byte[] direct=await PolynomialRender(copy,48,36,false),deep=await PolynomialRender(copy,48,36,true);
                    Check(PolynomialDifference(direct,deep)<0.06,"phase-correct hybrid reference reset");
                    SavePolynomialPng(Path.Combine(folder,variant+"-"+state.Hybrid.Steps[0].Formula+".png"),direct,48,36);
                    var store=new MandelbrotSaveStore(variant);store.Save(copy, store.LoadSlots().Slots.FirstOrDefault());
                    var saved=store.Load().Single(s=>s.SaveName=="hybrid-save");
                    Check(saved.Hybrid.Steps.Select(s=>(s.Formula,s.Power,s.Repeats)).SequenceEqual(copy.Hybrid.Steps.Select(s=>(s.Formula,s.Power,s.Repeats))),"hybrid JSON");
                    copy.Hybrid.Steps[0].Repeats=1; Check(state.Hybrid.Steps[0].Repeats==2,"state owns independent sequence");
                }
                var panel=(StackPanel)window.FindName("HybridRowsPanel");
                ((Button)window.FindName("HybridAddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(panel.Children.Count==3,"add editor step");
                var row=(Grid)panel.Children[0];((Button)row.Children[4]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.CaptureState("editor").Hybrid.Steps[1].Formula==MandelbrotVariant.Celtic,"reorder editor step");
                ((Button)((Grid)panel.Children[2]).Children[5]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(panel.Children.Count==2,"remove editor step");
                var stateMap=window.CaptureState("editor");
                var picker=new JuliaConstantPickerWindow(MandelbrotVariant.Hybrid,0,0,2,false,stateMap.Hybrid);
                var map=(MandelbrotState)typeof(JuliaConstantPickerWindow).GetMethod("CreateMapState",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(picker,null)!;
                Check(map.Hybrid.Steps[1].Formula==MandelbrotVariant.Celtic,"picker owns same hybrid formula");picker.Close();
            }
            finally{window.Close();}
        }
        var contextMethod=typeof(MandelbrotFamilyRenderer).GetMethod("GetProgramContext",BindingFlags.NonPublic|BindingFlags.Static)!;
        var pixelMethod=typeof(MandelbrotFamilyRenderer).GetMethod("ProgramPixel",BindingFlags.NonPublic|BindingFlags.Static)!;
        foreach(bool julia in new[]{false,true})
        {
            var state=PresetManager.GetMandelbrotPresets(julia ? MandelbrotVariant.JuliaHybrid : MandelbrotVariant.Hybrid)[0];
            state.Hybrid=new(){Steps=[new(){Repeats=2},new(){Formula=MandelbrotVariant.BurningShip,Power=3},new(){Formula=MandelbrotVariant.Celtic,Power=4}]};
            state.Iterations=30;state.JuliaCReal=-0.4m;state.JuliaCImaginary=0.2m;
            state.CenterX=0;state.CenterY=0;
            object context=contextMethod.Invoke(null,[state,CancellationToken.None])!;
            foreach(double x in new[]{-1.2,-0.5,0.0,0.4,1.1}) foreach(double y in new[]{-0.9,-0.2,0.0,0.6})
            {
                Complex z=julia ? new(x,y) : Complex.Zero,c=julia ? new(-0.4,0.2) : new(x,y);
                int n=0;while(n<30 && z.Magnitude<=2)
                {
                    int phase=n%4;
                    if(phase<2) z=z*z+c;
                    else if(phase==2) {var w=new Complex(Math.Abs(z.Real),-Math.Abs(z.Imaginary));z=w*w*w+c;}
                    else {var w=z*z;w*=w;z=new Complex(Math.Abs(w.Real),w.Imaginary)+c;}
                    n++;
                }
                object metrics=pixelMethod.Invoke(null,[state,context,(FloatExp)x,(FloatExp)y,FloatExp.One,CancellationToken.None])!;
                Check((int)metrics.GetType().GetProperty("Iterations")!.GetValue(metrics)! == n,"independent phase-ordered hybrid orbit");
            }
        }
        var mixed=PresetManager.GetMandelbrotPresets(MandelbrotVariant.JuliaHybrid)[0];
        mixed.Hybrid=new(){Steps=[new(){Power=2,Repeats=2},new(){Formula=MandelbrotVariant.BurningShip,Power=3}]};
        mixed.JuliaCReal=0;mixed.JuliaCImaginary=0;mixed.CenterX=0.6m;mixed.CenterY=0.8m;mixed.CenterXExact="0.6";mixed.CenterYExact="0.8";
        mixed.Zoom=FloatExp.Pow10(1000);mixed.Iterations=3200;mixed.Palette.ColorPeriod=3200;mixed.Palette.AlignWithRenderIterations=true;mixed.PaletteScale=1;
        byte[] actual=await PolynomialRender(mixed,4,3),exact=await Task.Run(()=>MandelbrotFamilyRenderer.RenderProgramExactForTests(mixed,4,3));
        Check(PolynomialDifference(actual,exact)<0.09,"mixed powers: exact phase-correct Julia boundary at 1e1000");
        var invalid=new HybridFormulaSettings{Steps=[]};bool rejected=false;try{invalid.Validate();}catch(ArgumentException){rejected=true;}Check(rejected,"empty hybrid rejected");
        invalid=new(){Steps=[new(){Formula=MandelbrotVariant.JuliaHybrid}]};rejected=false;try{invalid.Validate();}catch(ArgumentException){rejected=true;}Check(rejected,"recursive hybrid rejected");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();bool canceled=false;
        try{await Task.Run(()=>MandelbrotFamilyRenderer.Render(PresetManager.GetMandelbrotPresets(MandelbrotVariant.Hybrid)[0],new byte[32*32*4],32,32,128,cancellation.Token));}catch(OperationCanceledException){canceled=true;}
        Check(canceled,"cancellation during preparation");
    }
}
