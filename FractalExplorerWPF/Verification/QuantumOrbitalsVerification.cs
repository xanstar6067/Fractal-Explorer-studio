using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.QuantumMath;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static async Task VerifyQuantumOrbitalsAsync(string[] args)
    {
        const Fractal3DKind kind=Fractal3DKind.QuantumOrbitals;
        string output=ReadOption(args,"--out") ?? Path.Combine(AppContext.BaseDirectory,"QuantumOrbitalsVerification");
        Directory.CreateDirectory(output);
        AppPaths.OverrideDataRoot(Path.Combine(output,"Data",Guid.NewGuid().ToString("N")));
        VerifyCatalogData(); VerifyCatalogSearch();
        Check(Math.Abs(HydrogenOrbital.Evaluate(1,0,0,QuantumOrbitalBasis.Real,0,0,0).Real-1/Math.Sqrt(Math.PI))<1e-12,"Analytic 1s amplitude at nucleus.");
        Check(HydrogenOrbital.Evaluate(2,0,0,QuantumOrbitalBasis.Real,2,0,0).Magnitude<1e-12,"2s radial node at 2 a₀.");
        Check(HydrogenOrbital.Evaluate(2,1,0,QuantumOrbitalBasis.Real,1,1,0).Magnitude<1e-12,"2p z nodal plane.");
        for(int n=1;n<=8;n++) for(int l=0;l<n;l++) {
            double sum=0,dr=12.0*n*n/8192;
            for(int i=0;i<8192;i++) { double r=(i+.5)*dr,v=HydrogenOrbital.Radial(n,l,r); sum+=v*v*r*r*dr; }
            Check(Math.Abs(sum-1)<2e-6,$"Normalized radial function n={n},l={l}: {sum}.");
            for(int m=0;m<=l;m++) {
                double angular=0;
                for(int i=0;i<8192;i++) { double a=HydrogenOrbital.Angular(l,m,-1+2*(i+.5)/8192); angular+=a*a*2/8192*2*Math.PI; }
                Check(Math.Abs(angular-1)<2e-5,$"Normalized Y({l},{m}).");
                Complex pos=HydrogenOrbital.Evaluate(n,l,m,QuantumOrbitalBasis.Complex,1,2,3);
                Complex neg=HydrogenOrbital.Evaluate(n,l,-m,QuantumOrbitalBasis.Complex,1,2,3);
                Check((neg-Complex.Conjugate(pos)*(m%2==0 ? 1 : -1)).Magnitude<1e-12,"Negative m conjugation.");
            }
        }
        foreach(var invalid in new[] {
            new QuantumOrbitalSettings { N=0 }, new QuantumOrbitalSettings { L=3 },
            new QuantumOrbitalSettings { M=-3 }, new QuantumOrbitalSettings { Level=double.NaN },
            new QuantumOrbitalSettings { Size=100 }, new QuantumOrbitalSettings { Superposition=true,Phase=181 },
            new QuantumOrbitalSettings { Superposition=true,N2=3,L2=2,M2=0,Mix=.5,Phase=180 }
        }) {
            bool rejected=false; try { invalid.Validate(); } catch(ArgumentException) { rejected=true; }
            Check(rejected,"Invalid orbital input rejected.");
        }
        using var renderer=new Fractal3DRenderer();
        var state=Fractal3DCatalog.CreateDefaultState(kind);
        foreach(var s in new[] {
            new QuantumOrbitalSettings { N=1,L=0,M=0,Extent=4,Size=64 },
            new QuantumOrbitalSettings { Size=64 },
            new QuantumOrbitalSettings { M=-2,Basis=QuantumOrbitalBasis.Complex,Size=64 },
            new QuantumOrbitalSettings { N=8,L=7,M=6,Extent=180,Size=64 },
            new QuantumOrbitalSettings { N=2,L=0,M=0,N2=2,L2=1,M2=0,Superposition=true,Phase=47,Extent=12,Size=64 }
        }) {
            state.QuantumOrbital=s;
            float[] field=renderer.ReadOrbitalField(state,CancellationToken.None);
            Check(field.All(float.IsFinite),"Finite GPU orbital field.");
            double scale=HydrogenOrbital.DisplayScale(s),error=0;
            foreach(int z in new[]{5,21,32,47}) foreach(int y in new[]{7,29,39,52}) foreach(int x in new[]{9,25,40,54}) {
                Complex psi=HydrogenOrbital.Evaluate(s,((x+.5)/s.Size*2-1)*s.Extent,((y+.5)/s.Size*2-1)*s.Extent,((z+.5)/s.Size*2-1)*s.Extent)/scale;
                int at=((z*s.Size+y)*s.Size+x)*4;
                error=Math.Max(error,Math.Abs(field[at]-psi.Magnitude*psi.Magnitude));
                error=Math.Max(error,Math.Abs(field[at+1]-psi.Real)); error=Math.Max(error,Math.Abs(field[at+2]-psi.Imaginary));
            }
            Check(error<2e-4,$"GPU/CPU agreement: {error}.");
        }
        var interference=new QuantumOrbitalSettings { N=2,L=0,M=0,N2=2,L2=1,M2=0,Superposition=true };
        double plus=HydrogenOrbital.Evaluate(interference,0,0,3).Magnitude;
        double minus=HydrogenOrbital.Evaluate(interference with { Phase=180 },0,0,3).Magnitude;
        Check(Math.Abs(plus-minus)>.01,"Relative phase changes interference density.");
        state=Fractal3DCatalog.CreateDefaultState(kind);
        byte[] reference=Pixels(await renderer.RenderAsync(state,160,160,null,CancellationToken.None));
        int builds=renderer.OrbitalFieldBuilds;
        var changed=state.Clone(); changed.CameraYaw+=45; changed.QuantumOrbital.Level=.13; changed.QuantumOrbital.CutAxis=1;
        byte[] changedPixels=Pixels(await renderer.RenderAsync(changed,160,160,null,CancellationToken.None));
        Check(!reference.SequenceEqual(changedPixels),"Camera/cut/level change image.");
        Check(renderer.OrbitalFieldBuilds==builds,"Camera, level and cut reuse GPU field.");
        int index=0;
        foreach(var preset in Fractal3DCatalog.GetPresets(kind)) {
            var bitmap=await renderer.RenderAsync(preset,240,240,null,CancellationToken.None);
            WriteLSystemPng(Path.Combine(output,$"preset-{index++}.png"),bitmap);
            Check(HasFractal3DStructure(Pixels(bitmap),240,240),$"Visible orbital preset: {preset.SaveName}.");
        }
        state.QuantumOrbital=interference with { Phase=37,CutAxis=2,CutPosition=.15,Size=96 };
        var store=new Fractal3DSaveStore(kind); state.SaveName="Orbital roundtrip"; store.Save(state);
        Check(store.Load().Single(s=>s.SaveName==state.SaveName).QuantumOrbital==state.QuantumOrbital,"Complete save roundtrip.");
        Check(!ReferenceEquals(state.Clone().QuantumOrbital,state.QuantumOrbital),"Independent settings clone.");
        using(var canceled=new CancellationTokenSource()) {
            canceled.Cancel(); bool caught=false;
            try { renderer.ReadOrbitalField(state,canceled.Token); } catch(OperationCanceledException) { caught=true; }
            Check(caught,"GPU field cancellation.");
        }
        EnsureWindowThemeStyles();
        var window=new Fractal3DWindow(kind);
        try {
            window.LoadState(state);
            Check(window.CaptureState("roundtrip").QuantumOrbital==state.QuantumOrbital,"Window load/capture preserves orbital settings.");
            var editor=(QuantumOrbitalEditor)window.FindName("OrbitalEditor");
            Check(editor.Visibility==Visibility.Visible,"Orbital panel visible.");
            editor.Load(new() { N=2,L=1,M=1 });
            ((TextBox)editor.FindName("NBox")).Text="1";
            NumericInput.CommitAll(editor);
            var clamped=editor.Capture();
            Check(clamped.N==1 && clamped.L==0 && clamped.M==0,"Coupled n/l/m limits.");
            ((TextBox)editor.FindName("ExtentBox")).Text="NaN";
            NumericInput.CommitAll(editor);
            Check(double.IsFinite(editor.Capture().Extent),"Nonfinite input restored.");
            ((TextBox)editor.FindName("N2Box")).Text="invalid";
            Check(!editor.Capture().Superposition,"Inactive second orbital does not block rendering.");
            var layer=(FrameworkElement)window.FindName("InitialPreparationLayer");
            Check(layer.Visibility==Visibility.Visible,"Shared first-frame preparation overlay.");
        }
        finally { window.Close(); }
        Console.WriteLine($"PASS (quantum-orbitals): normalization, analytic nodes, phase interference, CPU/GPU fields, presets, cache, saves and numeric UI. Images: {output}");
    }
}
