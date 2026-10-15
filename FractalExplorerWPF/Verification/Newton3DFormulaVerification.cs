using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyNewtonFormulasAsync(Fractal3DRenderer renderer, Fractal3DState initial, string output)
    {
        // Compare actual GPU function/automatic derivative against hand-written System.Numerics formulas.
        (string Text, Func<Complex, Complex> F, Func<Complex, Complex> D)[] probes = [
            ("2z+(1-2i)", z => 2*z+new Complex(1,-2), _ => 2),
            ("z^5-z+1", z => Complex.Pow(z,5)-z+1, z => 5*Complex.Pow(z,4)-1),
            ("(z^3+2i)/(z^2+2)", z => (z*z*z+2*Complex.ImaginaryOne)/(z*z+2),
                z => (3*z*z*(z*z+2)-(z*z*z+2*Complex.ImaginaryOne)*2*z)/Complex.Pow(z*z+2,2)),
            ("z^(-2)", z => Complex.Pow(z,-2), z => -2*Complex.Pow(z,-3)),
            ("(z+.3)^(1.5+.2i)", z => Complex.Pow(z+.3,new Complex(1.5,.2)),
                z => new Complex(1.5,.2)*Complex.Pow(z+.3,new Complex(.5,.2))),
            ("sin(z)", Complex.Sin, Complex.Cos),
            ("cos(z)", Complex.Cos, z => -Complex.Sin(z)),
            ("tan(z)", Complex.Tan, z => 1/Complex.Pow(Complex.Cos(z),2)),
            ("asin(z)", Complex.Asin, z => 1/Complex.Sqrt(1-z*z)),
            ("acos(z)", Complex.Acos, z => -1/Complex.Sqrt(1-z*z)),
            ("atan(z)", Complex.Atan, z => 1/(1+z*z)),
            ("sinh(z)", Complex.Sinh, Complex.Cosh),
            ("cosh(z)", Complex.Cosh, Complex.Sinh),
            ("tanh(z)", Complex.Tanh, z => 1/Complex.Pow(Complex.Cosh(z),2)),
            ("exp(z)", Complex.Exp, Complex.Exp),
            ("log(z)", Complex.Log, z => 1/z),
            ("ln(z)", Complex.Log, z => 1/z),
            ("sqrt(z)", Complex.Sqrt, z => .5/Complex.Sqrt(z)),
            ("pi*z+e+i", z => Math.PI*z+Math.E+Complex.ImaginaryOne, _ => Math.PI)
        ];
        foreach (var probe in probes)
        {
            var state = initial.Clone();
            state.Newton = new() { Formula = probe.Text, Size = 32, Extent = .8 };
            float[] values = await Task.Run(() => renderer.ReadNewtonField(state, CancellationToken.None, expressionProbe: true));
            double error = 0;
            for (int y = 1; y < 32; y += 4) for (int x = 1; x < 32; x += 4)
            {
                Complex z = new(((x+.5)/32*2-1)*.8, ((y+.5)/32*2-1)*.8);
                int offset = (y*32+x)*4;
                Complex actual = new(values[offset],values[offset+1]), derivative = new(values[offset+2],values[offset+3]);
                Complex f = probe.F(z), d = probe.D(z);
                error = Math.Max(error, Complex.Abs(actual-f)/(1+f.Magnitude));
                error = Math.Max(error, Complex.Abs(derivative-d)/(1+d.Magnitude));
            }
            Check(values.All(float.IsFinite) && error < 5e-4, $"GPU formula and symbolic derivative / {probe.Text}: relative error {error}.");
            Console.WriteLine($"GPU expression {probe.Text}: error {error:E2}.");
        }

        var candidates = new[] { "z^5-z+1", "(z-1)*(z+1)*(z-i)*(z+i)", "sin(z)", "exp(z)-1", "(z^3-1)/(z^2+2)" };
        var store = new Fractal3DSaveStore(Fractal3DKind.Newton3D);
        int index = 0;
        foreach (string formula in candidates)
        {
            var settings = await Task.Run(() => Newton3DComputeShader.Prepare(new() { Formula = formula, Size = 32, RootSearchRadius = 8 }, CancellationToken.None));
            Check(settings.Roots is not null && settings.RootCount is >= 2 and <= 32, "Discovered roots include non-polynomial and complex roots.");
            if (formula == "sin(z)") Check(settings.RootCount == 5, "sin(z): five roots within radius 8.");
            if (formula == "exp(z)-1") Check(settings.RootCount == 3, "exp(z)-1: three roots within radius 8.");
            var state = initial.Clone(); state.Newton = settings;
            var first = Pixels(await renderer.RenderAsync(state, 160, 160, null, CancellationToken.None)).ToArray();
            float[] field = await Task.Run(() => renderer.ReadNewtonField(state, CancellationToken.None));
            var program = Newton3DFormula.Parse(formula);
            int compared = 0, wrong = 0;
            for (int z=1; z<32; z+=4) for (int y=1; y<32; y+=4) for (int x=1; x<32; x+=4)
            {
                double px=((x+.5)/32*2-1)*settings.Extent,py=((y+.5)/32*2-1)*settings.Extent,pz=((z+.5)/32*2-1)*settings.Extent;
                var a=NewtonFormulaReference(new(px+settings.SliceW,py-pz), settings, program);
                var b=NewtonFormulaReference(new(px-settings.SliceW,py+pz), settings, program);
                if(a.Root<0 || b.Root<0 || a.Steps>25 || b.Steps>25)continue;
                int cell=((z*32+y)*32+x)*4;
                compared++;
                if(field[cell+1]!=a.Root+1 || field[cell+3]!=b.Root+1)wrong++;
            }
            Check(compared>100 && wrong < Math.Max(1,compared*.01), $"GPU Newton / {formula}: {wrong}/{compared} disagreements.");
            state.Newton = settings with { Size = 96 };
            var image = await renderer.RenderAsync(state, 360, 360, null, CancellationToken.None);
            Check(HasFractal3DStructure(Pixels(image),360,360), $"Custom 3D formula {formula} renders.");
            PhysarumSavePng(image,Path.Combine(output,$"formula-{index++}.png"));
            state.Newton = settings; state.SaveName="formula " + formula;
            store.Save(state);
            var restored = store.Load().Single(s=>s.SaveName==state.SaveName);
            Check(restored.Newton==settings && restored.Newton.Roots!.Equals(settings.Roots),"Exact formula/root-order JSON roundtrip.");
            var second=Pixels(await renderer.RenderAsync(restored,160,160,null,CancellationToken.None)).ToArray();
            Check(first.SequenceEqual(second),"Restoring a formula preserves the rendered frame and root colors.");
            Console.WriteLine($"Formula basins {formula}: {settings.RootCount} roots, {compared} CPU/GPU samples.");
        }
        foreach(var example in Newton3DFormulas.Examples)
        {
            var prepared = await Task.Run(()=>Newton3DComputeShader.Prepare(new() { Formula=example.Formula },CancellationToken.None));
            Check(prepared.RootCount>0, $"Example {example.Name} is usable.");
        }
        var random=new Random(4137);
        for(int k=0;k<12;k++)
        {
            string formula=Newton3DFormulas.RandomFormula(random);
            var prepared=Newton3DFormula.Prepare(new() { Formula=formula });
            Check(prepared.RootCount is >=3 and <=6,"Random factored polynomials have the expected root count.");
        }
        foreach(string invalid in new[]{"", "z+unknown", "bad(z)", "1", "0", "exp(z)", "z^33-1", "z;return 0", new string('(',33)+"z"+new string(')',33)})
        {
            bool rejected=false;
            try {Newton3DComputeShader.Prepare(new() { Formula=invalid },CancellationToken.None);}catch(ArgumentException){rejected=true;}
            Check(rejected,$"Invalid or unsupported formula rejected: {invalid}.");
        }
        var old=JsonSerializer.Deserialize<Newton3DSettings>("{\"Degree\":5}",JsonOptionsFactory.Create())!;
        Check(old.Formula is null && old.Expression=="z^5-1" && Newton3DFormula.Prepare(old)==old,"Released degree-only saves preserve the old kernel and root order.");
        await VerifyNewtonFormulaEditorAsync(initial,output);
    }

    private static (int Root,int Steps) NewtonFormulaReference(Complex z,Newton3DSettings s,Newton3DFormula p)
    {
        for(int k=0;k<=s.MaxIterations;k++)
        {
            Complex f=p.Compiled.Evaluate(z);
            if(!double.IsFinite(f.Magnitude))break;
            if(f.Magnitude<1e-5)
            {
                var nearest=s.Roots!.Values.Select((root,i)=>(Index:i,Distance:Complex.Abs(z-root.Value))).MinBy(r=>r.Distance);
                if(nearest.Distance<=1e-3*(1+s.Roots.Values[nearest.Index].Value.Magnitude))return(nearest.Index,k);
            }
            Complex d=p.CompiledDerivative.Evaluate(z);
            if(k==s.MaxIterations || !double.IsFinite(d.Magnitude) || d.Magnitude<1e-12 || d.Magnitude>1e15 || Math.Abs(z.Real)>1e6 || Math.Abs(z.Imaginary)>1e6)break;
            Complex correction=f/d;
            if(f.Magnitude<1e-5 && correction.Magnitude<1e-5)return(-1,k);
            z-=s.Relaxation*correction;
        }
        return(-1,s.MaxIterations);
    }

    private static async Task VerifyNewtonFormulaEditorAsync(Fractal3DState initial,string output)
    {
        EnsureWindowThemeStyles();
        var window=new Fractal3DWindow(Fractal3DKind.Newton3D);
        try
        {
            window.LoadState(initial);
            var editor=(Newton3DEditor)window.FindName("NewtonEditor");
            var box=(TextBox)editor.FindName("FormulaBox");
            box.Text="z^5-z+1";
            Check(editor.Capture().Formula is null,"Draft text does not change the displayed/applied formula.");
            Check(await editor.ApplyFormulaAsync(),"Apply compiles the shader and discovers roots asynchronously.");
            var applied=window.CaptureState("formula");
            Check(applied.Newton.Formula==box.Text && applied.Newton.RootCount==5,"Applied expression and roots are captured.");
            box.Text="sin(";
            Check(!await editor.ApplyFormulaAsync() && editor.Capture()==applied.Newton,"Invalid input preserves the previous formula/root catalog.");
            box.Text="sin(z)";
            Task<bool> pending=editor.ApplyFormulaAsync();
            window.LoadState(initial);
            Check(!await pending && editor.Capture()==initial.Newton,"Loading a scene cancels pending application without stale publication.");
            box.Text="cos(z)-z";
            pending=editor.ApplyFormulaAsync();
            ((Button)editor.FindName("CancelFormulaButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!await pending && editor.Capture()==initial.Newton,"Explicit cancellation preserves the previous formula.");
            ((Button)editor.FindName("RandomFormulaButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for(int k=0;k<1000 && !((Button)editor.FindName("ApplyFormulaButton")).IsEnabled;k++)await Task.Delay(10);
            Check(editor.Capture().Formula is not null && editor.Capture().RootCount is >=3 and <=6,"The random-formula button generates and applies a GPU formula.");
            box.Text="exp(z)-1";
            Check(await editor.ApplyFormulaAsync(),"A non-polynomial expression can be applied.");
            var saved=editor.Capture(); window.LoadState(window.CaptureState("saved"));
            Check(editor.Capture()==saved,"Loading retains expression and exact root order.");
            var root=(FrameworkElement)window.Content;
            root.Measure(new Size(1200,900)); root.Arrange(new Rect(0,0,1200,900)); root.UpdateLayout();
            var render=typeof(Fractal3DWindow).GetMethod("RenderFrameAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
            await (Task)render.Invoke(window,[Enum.Parse(render.GetParameters()[0].ParameterType,"Draft")])!;
            var bitmap=(BitmapSource)((Image)window.FindName("CanvasImage")).Source;
            Check(bitmap is not null && bitmap.PixelWidth>100 && HasFractal3DStructure(Pixels(bitmap),bitmap.PixelWidth,bitmap.PixelHeight),
                $"The actual WPF formula frame has structure: {bitmap?.PixelWidth}×{bitmap?.PixelHeight}; {((TextBlock)window.FindName("StatusText")).Text}.");
            await DrainAsync(); root.UpdateLayout();
            var snapshot=new RenderTargetBitmap(1200,900,96,96,PixelFormats.Pbgra32); snapshot.Render(root);
            PhysarumSavePng(snapshot,Path.Combine(output,"formula-editor.png"));
            ((ComboBox)editor.FindName("DegreeBox")).SelectedItem=7;
            Check(editor.Capture().Formula is null && editor.Capture().Degree==7,"Quick powers switch back to the released analytic family.");
        }
        finally{window.Close();}
        Console.WriteLine("PASS (Newton formulas): GPU functions/derivatives, polynomial/transcendental basins, examples/random, compatibility, JSON and transactional WPF apply.");
    }
}
