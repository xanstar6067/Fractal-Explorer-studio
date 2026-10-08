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
    private static async Task VerifyPerpendicularFamilyAsync()
    {
        using var sandbox = DataSandbox.Create("perpendicular-family");
        EnsureThemeStyles();
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        MandelbrotVariant[] variants = [
            MandelbrotVariant.PerpendicularMandelbrot,
            MandelbrotVariant.PerpendicularBurningShip,
            MandelbrotVariant.PerpendicularCeltic,
            MandelbrotVariant.PerpendicularBuffalo,
            MandelbrotVariant.JuliaPerpendicularMandelbrot,
            MandelbrotVariant.JuliaPerpendicularBurningShip,
            MandelbrotVariant.JuliaPerpendicularCeltic,
            MandelbrotVariant.JuliaPerpendicularBuffalo];
        var iterate = typeof(MandelbrotFamilyRenderer).GetMethod("Iterate", BindingFlags.NonPublic | BindingFlags.Static)!;
        var iterateDecimal = typeof(MandelbrotFamilyRenderer).GetMethod("IterateDecimal", BindingFlags.NonPublic | BindingFlags.Static)!;
        int expectedCases = 0;
        // Formula convention: Kalles Fraktaler manual, Perpendicular Mandelbrot/Ship/Celtic/Buffalo.
        // This oracle squares a Complex value; production uses scalar components and folded deltas.
        Complex Step(MandelbrotState state, Complex z, Complex c)
        {
            Complex w = MandelbrotVariantDefinition.ParameterVariant(state.Variant) switch
            {
                MandelbrotVariant.PerpendicularMandelbrot or MandelbrotVariant.PerpendicularCeltic =>
                    new Complex(Math.Abs(z.Real), -z.Imaginary),
                MandelbrotVariant.PerpendicularBurningShip or MandelbrotVariant.PerpendicularBuffalo =>
                    new Complex(z.Real, -Math.Abs(z.Imaginary)),
                _ => throw new InvalidOperationException()
            };
            w *= w;
            if (MandelbrotVariantDefinition.ParameterVariant(state.Variant) is
                MandelbrotVariant.PerpendicularCeltic or MandelbrotVariant.PerpendicularBuffalo)
                w = new Complex(Math.Abs(w.Real), w.Imaginary);
            return w + c;
        }
        int Expected(MandelbrotState state, Complex pixel)
        {
            bool julia = MandelbrotVariantDefinition.IsJulia(state.Variant);
            Complex c = julia ? new((double)state.JuliaCReal, (double)state.JuliaCImaginary) : pixel;
            Complex z = julia ? pixel : Complex.Zero;
            int n = 0;
            while (n < state.Iterations && z.Magnitude <= (double)state.Threshold)
            {
                z = Step(state, z, c);
                n++;
            }
            return n;
        }
        async Task<byte[]> Render(MandelbrotState state, bool? deep = null, bool? extended = null)
        {
            const int w = 24, h = 18;
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = deep;
            MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = extended;
            try
            {
                byte[] bytes = new byte[w*h*4];
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, bytes, w, h, w*4, CancellationToken.None));
                return bytes;
            }
            finally
            {
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
                MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = null;
            }
        }
        static double Difference(byte[] a, byte[] b)
        {
            int different = 0;
            for (int i=0; i<a.Length; i+=4)
                if (Math.Abs(a[i]-b[i]) > 2 || Math.Abs(a[i+1]-b[i+1]) > 2 || Math.Abs(a[i+2]-b[i+2]) > 2) different++;
            return different / (a.Length/4.0);
        }
        foreach (MandelbrotVariant variant in variants)
        {
            var definition = MandelbrotVariantDefinition.For(variant);
            Check(FractalCatalog.Create().Count(item => item.LaunchKey == variant.ToString()) == 1,
                $"{variant}: separate catalog tile required.");
            Check(definition.Identifier == variant.ToString() && definition.HasJuliaConstant == MandelbrotVariantDefinition.IsJulia(variant),
                $"{variant}: own save category and C required.");
            var window = new MandelbrotWindow(variant);
            try
            {
                var state = window.CaptureState("independent");
                Check(((UIElement)window.FindName("PowerPanel")).Visibility ==
                    (definition.HasPower ? Visibility.Visible : Visibility.Collapsed), "Power panel visibility.");
                // Exact count equality across double/decimal uses short orbits; chaotic boundary
                // orbits can separate after dozens of steps solely from rounding. Long images
                // below are checked separately with bounded pixel error and a BigFloat oracle.
                state.Iterations = 24;
                decimal[] powers = definition.HasPower ? [2m, 3m, 2.5m] : [2m];
                foreach (decimal power in powers)
                foreach (bool inversion in definition.HasInversion ? new[] { false, true } : new[] { false })
                {
                    state.Power = power; state.UseInversion = inversion;
                    for (int y=0; y<7; y++) for (int x=0; x<9; x++)
                    {
                        decimal re = -1.3m + x*0.31m, im = -0.9m + y*0.29m;
                        int expected = Expected(state, new Complex((double)re, (double)im));
                        object actual = iterate.Invoke(null, [state, (double)re, (double)im, CancellationToken.None])!;
                        object exact = iterateDecimal.Invoke(null, [state, re, im, CancellationToken.None])!;
                        int Value(object metric) => (int)metric.GetType().GetProperty("Iterations")!.GetValue(metric)!;
                        Check(Value(actual) == expected && Value(exact) == expected,
                            $"{variant}, p={power}, inverse={inversion}, z={re}+{im}i: expected={expected}, double={Value(actual)}, decimal={Value(exact)}.");
                        expectedCases++;
                    }
                }
                // One step on both axes, both diagonals and every quadrant catches the fold order.
                foreach (decimal re in new[] { -0.7m, 0m, 0.7m })
                foreach (decimal im in new[] { -0.7m, -0.2m, 0m, 0.2m, 0.7m })
                {
                    Complex expected = Step(state, new((double)re, (double)im), new(0.17, -0.23));
                    object[] args = [state, (double)re, (double)im, 0.17, -0.23];
                    typeof(MandelbrotFamilyRenderer).GetMethod("IterateOnce", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
                    Check(Complex.Abs(new Complex((double)args[1], (double)args[2]) - expected) < 1e-12,
                        $"{variant}: fold order disagrees at {re},{im}.");
                    object[] exactArgs = [state, re, im, 0.17m, -0.23m];
                    typeof(MandelbrotFamilyRenderer).GetMethod("IterateOnceDecimal", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, exactArgs);
                    Check(Complex.Abs(new Complex((double)(decimal)exactArgs[1], (double)(decimal)exactArgs[2]) - expected) < 1e-12,
                        $"{variant}: decimal fold order disagrees.");
                    if (re == 0 || im == 0 || Math.Abs(re) == Math.Abs(im)) continue;
                    const double epsilon = 1e-6;
                    Complex z = new((double)re, (double)im);
                    Complex dx = (Step(state,z + epsilon,Complex.Zero)-Step(state,z - epsilon,Complex.Zero))/(2*epsilon);
                    Complex dy = (Step(state,z + Complex.ImaginaryOne*epsilon,Complex.Zero)-Step(state,z - Complex.ImaginaryOne*epsilon,Complex.Zero))/(2*epsilon);
                    object jac = typeof(MandelbrotFamilyRenderer).GetMethod("GetIterationJacobian", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,[state,z.Real,z.Imaginary])!;
                    double Entry(string name) => (double)jac.GetType().GetProperty(name)!.GetValue(jac)!;
                    Check(Math.Abs(Entry("M11")-dx.Real) < 1e-8 && Math.Abs(Entry("M21")-dx.Imaginary) < 1e-8 &&
                          Math.Abs(Entry("M12")-dy.Real) < 1e-8 && Math.Abs(Entry("M22")-dy.Imaginary) < 1e-8,
                        $"{variant}: distance Jacobian disagrees with independent finite differences.");
                }
                // All supported power/reflection kernels must agree with direct iteration when C is non-zero.
                state.Power = definition.DefaultPower;
                state.UseInversion = definition.HasInversion;
                state.Zoom = 0.9; state.CenterX = -0.35m; state.CenterY = 0.17m;
                // Attracting nonzero C gives stable long-orbit comparisons across precisions.
                // Default dust-like Julia constants can amplify rounding into distinct escapes;
                // their formula and initial conditions are covered by the independent steps above.
                state.JuliaCReal = -0.1m; state.JuliaCImaginary = 0.2m;
                state.Iterations = 180;
                foreach (MandelbrotColoringMode mode in Enum.GetValues<MandelbrotColoringMode>())
                {
                    state.ColoringMode = mode;
                    byte[] direct = await Render(state, false);
                    foreach (bool extended in new[] { false, true })
                    {
                        byte[] deep = await Render(state, true, extended);
                        Check(Difference(direct, deep) < 0.04,
                            $"{variant}/{mode}/FloatExp={extended}: perturbation differs from CPU by {Difference(direct,deep):P1}.");
                    }
                }
                // Julia at c=0 has the unit-circle boundary; parameter planes have a real boundary at c=-2.
                state.JuliaCReal = 0; state.JuliaCImaginary = 0;
                state.CenterX = MandelbrotVariantDefinition.IsJulia(variant) ? 1m : -2m; state.CenterY = 0; state.ColoringMode = MandelbrotColoringMode.Smooth;
                state.Iterations = 3600;
                foreach (int exponent in new[] { 12, 80, 350 })
                {
                    state.Zoom = FloatExp.Pow10(exponent);
                    using (var precision = new BigFloat.PrecisionScope((int)Math.Ceiling(exponent*Math.Log2(10))+384))
                    {
                        state.CenterXExact = (BigFloat.FromDecimal(state.CenterX)+BigFloat.Parse("1e-"+(exponent+2))).ToInvariantString();
                        state.CenterYExact = BigFloat.Zero.ToInvariantString();
                    }
                    byte[] deep = await Render(state);
                    byte[] reference = await Task.Run(() => MandelbrotFamilyRenderer.RenderExactReferenceForTests(state,24,18,CancellationToken.None));
                    Check(Difference(deep,reference) < 0.015, $"{variant}/1e{exponent}: exact reference differs.");
                    Check(deep.Chunk(4).Select(p => BitConverter.ToUInt32(p)).Distinct().Count() > 1,
                        $"{variant}/1e{exponent}: boundary collapsed.");
                }
                // BLA must really accelerate the new reflected kernels, and retain the same result.
                if (MandelbrotVariantDefinition.IsJulia(variant))
                {
                    state.CenterXExact = null; state.CenterYExact = null;
                    state.CenterX = 0.45m; state.CenterY = 0.3m;
                    state.JuliaCReal = -0.1m; state.JuliaCImaginary = 0.2m;
                    state.Zoom = 1e12; state.Iterations = 350;
                    MandelbrotFamilyRenderer.CountRealBlaSkipsForTests = true;
                    MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests = 0;
                    try
                    {
                        MandelbrotFamilyRenderer.ForceBlaForTests = true;
                        byte[] accelerated = await Render(state);
                        Check(MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests > 0,
                            $"{variant}: reflected BLA did not skip any iterations.");
                        MandelbrotFamilyRenderer.ForceBlaForTests = false;
                        Check(Difference(accelerated,await Render(state)) < 0.015,
                            $"{variant}: BLA changes the image.");
                    }
                    finally
                    {
                        MandelbrotFamilyRenderer.ForceBlaForTests = null;
                        MandelbrotFamilyRenderer.CountRealBlaSkipsForTests = false;
                    }
                }
                // The picker must use precisely the parameter family and the current p/inversion.
                var picker = new JuliaConstantPickerWindow(
                    MandelbrotVariantDefinition.ParameterVariant(variant), definition.DefaultJuliaReal,
                    definition.DefaultJuliaImaginary, 3, definition.HasInversion);
                try
                {
                    MandelbrotState Map() => (MandelbrotState)picker.GetType().GetMethod("CreateMapState",flags)!.Invoke(picker,null)!;
                    Check(Map().Variant == MandelbrotVariantDefinition.ParameterVariant(variant) && Map().Power == 3 &&
                        Map().UseInversion == definition.HasInversion, "Picker lost formula parameters.");
                    picker.UpdateFormulaParameters(4,false);
                    Check(Map().Power == 4 && !Map().UseInversion, "Open picker did not update parameters.");
                }
                finally { picker.Close(); }
                window.LoadState(PresetManager.GetMandelbrotPresets(variant)[0]);
                var item = FractalCatalog.Create().Single(item => item.LaunchKey == variant.ToString());
                var image = await CatalogPreviewLoader.RenderAsync(item,CancellationToken.None);
                byte[] pixels = new byte[image.PixelWidth*image.PixelHeight*4]; image.CopyPixels(pixels,image.PixelWidth*4,0);
                Check(pixels.Chunk(4).Select(p => BitConverter.ToUInt32(p)).Distinct().Count() > 8,
                    $"{variant}: catalog preview must show its own fractal.");
                Console.WriteLine($"  {variant}: independent formula, 7 colourings, double/FloatExp kernels, 1e350, map and preview.");
            }
            finally { window.Close(); }
        }
        Console.WriteLine($"PASS (perpendicular-family): {expectedCases} independent orbits, folds, Jacobians, seven colourings, BLA, FloatExp, 1e350, maps and eight separate modes.");
    }

}
