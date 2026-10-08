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
    private static async Task VerifyFoldedCubicFamilyAsync()
    {
        using var sandbox = DataSandbox.Create("folded-cubic");
        EnsureThemeStyles();
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        MandelbrotVariant[] variants = [
            MandelbrotVariant.CelticMandelbar,
            MandelbrotVariant.CubicQuasiBurningShip,
            MandelbrotVariant.CubicFlyingSquirrel,
            MandelbrotVariant.JuliaCelticMandelbar,
            MandelbrotVariant.JuliaCubicQuasiBurningShip,
            MandelbrotVariant.JuliaCubicFlyingSquirrel];
        var iterate = typeof(MandelbrotFamilyRenderer).GetMethod("Iterate", BindingFlags.NonPublic | BindingFlags.Static)!;
        var iterateDecimal = typeof(MandelbrotFamilyRenderer).GetMethod("IterateDecimal", BindingFlags.NonPublic | BindingFlags.Static)!;
        int expectedCases = 0;
        // Independent complex arithmetic from the Kalles Fraktaler manual.
        // The scalar production kernels never call this oracle.
        Complex Step(MandelbrotState state, Complex z, Complex c)
        {
            if (state.Variant is MandelbrotVariant.CelticMandelbar or MandelbrotVariant.JuliaCelticMandelbar)
            {
                Complex w = Complex.Conjugate(z) * Complex.Conjugate(z);
                return new Complex(Math.Abs(w.Real), w.Imaginary) + c;
            }
            bool quasi = state.Variant is MandelbrotVariant.CubicQuasiBurningShip or MandelbrotVariant.JuliaCubicQuasiBurningShip;
            Complex folded = quasi ? new Complex(Math.Abs(z.Real), z.Imaginary) : z;
            Complex cube = folded * folded * folded;
            return new Complex(cube.Real, (quasi ? -1 : 1) * Math.Abs(cube.Imaginary)) + c;
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
        async Task<byte[]> Render(MandelbrotState state, bool? deep = null, bool? extended = null, int w = 24, int h = 18)
        {
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
                decimal[] powers = [definition.DefaultPower];
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
                        if (expected < state.Iterations)
                        {
                            bool julia = definition.HasJuliaConstant;
                            Complex z = julia ? new((double)re, (double)im) : Complex.Zero;
                            Complex c = julia ? new((double)state.JuliaCReal, (double)state.JuliaCImaginary) : new((double)re, (double)im);
                            for (int n=0; n<expected; n++) z = Step(state,z,c);
                            double degree = variant is MandelbrotVariant.CelticMandelbar or MandelbrotVariant.JuliaCelticMandelbar ? 2 : 3;
                            double smooth = expected + 1 - Math.Log(Math.Log(z.Magnitude)/Math.Log(degree))/Math.Log(degree);
                            double actualSmooth = (double)actual.GetType().GetProperty("Smooth")!.GetValue(actual)!;
                            Check(Math.Abs(actualSmooth-smooth) < 1e-9, $"{variant}: smoothing must use its fixed degree {degree}.");
                        }
                        expectedCases++;
                    }
                }
                // One step on both axes, both diagonals and every quadrant catches the fold order.
                foreach (decimal re in new[] { -0.7m, -0.4m, 0m, 0.4m, 0.7m })
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
                // c=0 Julia has the unit circle. Cubic parameter tips are at +/-i*sqrt(2),
                // not c=-2: retaining their exact irrational centres is essential beyond decimal.
                state.JuliaCReal = 0; state.JuliaCImaginary = 0;
                state.ColoringMode = MandelbrotColoringMode.Smooth;
                state.Iterations = 3600;
                foreach (int exponent in new[] { 12, 80, 350, 1000 })
                {
                    state.Zoom = FloatExp.Pow10(exponent);
                    using (var precision = new BigFloat.PrecisionScope((int)Math.Ceiling(exponent*Math.Log2(10))+384))
                    {
                        BigFloat x = MandelbrotVariantDefinition.IsJulia(variant) ? BigFloat.One : BigFloat.FromInt(-2);
                        BigFloat y = BigFloat.Zero;
                        if (variant is MandelbrotVariant.CubicQuasiBurningShip or MandelbrotVariant.CubicFlyingSquirrel)
                        {
                            x = BigFloat.Zero;
                            y = BigFloat.Sqrt(BigFloat.FromInt(2));
                            if (variant == MandelbrotVariant.CubicFlyingSquirrel) y = -y;
                        }
                        state.CenterX = x.ToDecimalClamped(); state.CenterY = y.ToDecimalClamped();
                        // A 0.73-pixel-scale shift tests a substantial tiny reference X without
                        // putting a sample exactly on the circle's tangent (whose radial
                        // displacement is second order and needs twice the oracle precision).
                        state.CenterXExact = (x + BigFloat.Parse("0.73e-"+exponent)).ToInvariantString();
                        state.CenterYExact = y.ToInvariantString();
                    }
                    // The arbitrary-precision oracle is much costlier at the ceiling;
                    // 48 independent samples cover both sides of the same boundary.
                    int w = exponent == 1000 ? 8 : 24, h = exponent == 1000 ? 6 : 18;
                    foreach (MandelbrotColoringMode mode in exponent >= 350
                        ? new[] { MandelbrotColoringMode.Smooth, MandelbrotColoringMode.DistanceEstimation }
                        : new[] { MandelbrotColoringMode.Smooth })
                    {
                        state.ColoringMode = mode;
                        MandelbrotPalette originalPalette = state.Palette;
                        decimal originalThreshold = state.Threshold;
                        if (mode == MandelbrotColoringMode.DistanceEstimation)
                        {
                            // Keep an illuminated base colour after hundreds/thousands of
                            // iterations; algorithmic grayscale can clamp it to black.
                            // At c=-2 the critical orbit lies on radius 2 itself; wait for
                            // radius 4 to obtain a meaningful exterior distance estimate.
                            state.Threshold = 4;
                            state.Palette = new MandelbrotPalette { Name = "Deep distance verification",
                                Colors = [Colors.LightSkyBlue,Colors.Orange,Colors.White], ColorPeriod = 257 };
                            // Test the distance field in the exterior. Inside the parameter
                            // tip, long chaotic orbits amplify rounding and lighting amplifies
                            // it further; that is not a stable derivative-accuracy fixture.
                            using var precision = new BigFloat.PrecisionScope((int)Math.Ceiling(exponent*Math.Log2(10))+384);
                            BigFloat offset = BigFloat.Parse("1e-"+exponent);
                            BigFloat x, y = BigFloat.Zero;
                            if (definition.HasJuliaConstant) x = BigFloat.One + offset*BigFloat.FromInt(10);
                            else if (variant == MandelbrotVariant.CelticMandelbar) x = BigFloat.FromInt(-2)-offset*BigFloat.FromInt(10);
                            else
                            {
                                x = offset;
                                y = BigFloat.Sqrt(BigFloat.FromInt(2))+offset*BigFloat.FromInt(10);
                                if (variant == MandelbrotVariant.CubicFlyingSquirrel) y = -y;
                            }
                            state.CenterXExact = x.ToInvariantString(); state.CenterYExact = y.ToInvariantString();
                        }
                        byte[] deep = await Render(state,w:w,h:h);
                        byte[] reference = await Task.Run(() => MandelbrotFamilyRenderer.RenderExactReferenceForTests(state,w,h,CancellationToken.None));
                        Check(Difference(deep,reference) < 0.015,
                            $"{variant}/1e{exponent}/{mode}: exact reference differs by {Difference(deep,reference):P1}.");
                        if (mode == MandelbrotColoringMode.Smooth)
                            Check(deep.Chunk(4).Select(p => BitConverter.ToUInt32(p)).Distinct().Count() > 1,
                                $"{variant}/1e{exponent}: boundary collapsed.");
                        else
                        {
                            // The exterior distance is almost a plane and can have a uniform
                            // shade. Switching off its relief must still change the frame.
                            double strength = state.DistanceReliefStrength;
                            state.DistanceReliefStrength = 0;
                            byte[] flat = await Render(state,w:w,h:h);
                            state.DistanceReliefStrength = strength;
                            Check(Difference(deep,flat) > 0.05,
                                $"{variant}/1e{exponent}: extended distance relief vanished.");
                            state.Palette = originalPalette;
                            state.Threshold = originalThreshold;
                        }
                    }
                    state.ColoringMode = MandelbrotColoringMode.Smooth;
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
                        // An off-axis circle has real BLA skips before escape; unlike the
                        // attracting fixture above, its image detects wrong skip matrices.
                        state.JuliaCReal = 0; state.JuliaCImaginary = 0;
                        state.CenterX = 0.8m; state.CenterY = 0.6m; state.Iterations = 90;
                        MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests = 0;
                        MandelbrotFamilyRenderer.ForceBlaForTests = true;
                        accelerated = await Render(state);
                        Check(MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests > 0,
                            $"{variant}: off-axis escaping BLA did not skip.");
                        MandelbrotFamilyRenderer.ForceBlaForTests = false;
                        Check(Difference(accelerated,await Render(state)) < 0.015,
                            $"{variant}: BLA changes an escaping orbit.");
                        Check(accelerated.Chunk(4).Select(p => BitConverter.ToUInt32(p)).Distinct().Count() > 1,
                            $"{variant}: escaping BLA fixture must have a visible boundary.");
                    }
                    finally
                    {
                        MandelbrotFamilyRenderer.ForceBlaForTests = null;
                        MandelbrotFamilyRenderer.CountRealBlaSkipsForTests = false;
                    }
                }
                if (variant is MandelbrotVariant.JuliaCubicQuasiBurningShip or MandelbrotVariant.JuliaCubicFlyingSquirrel)
                {
                    using (var precision = new BigFloat.PrecisionScope(4096))
                    {
                        state.CenterXExact = BigFloat.FromDecimal(0.5m).ToInvariantString();
                        state.CenterYExact = (BigFloat.Sqrt(BigFloat.FromInt(3))/BigFloat.FromInt(2)).ToInvariantString();
                    }
                    state.Zoom = FloatExp.Pow10(350); state.Iterations = 350;
                    state.JuliaCReal = 0; state.JuliaCImaginary = 0;
                    object orbit = typeof(MandelbrotFamilyRenderer).GetMethod("GetReferenceOrbit",BindingFlags.NonPublic|BindingFlags.Static)!
                        .Invoke(null,[state,4096])!;
                    object table = orbit.GetType().GetField("RealBla")!.GetValue(orbit)!;
                    double[][] radii = (double[][])table.GetType().GetField("R2")!.GetValue(table)!;
                    Check(radii[0][0] == 0, $"{variant}: BLA must not skip across the cubic output fold at pi/3.");
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
                Console.WriteLine($"  {variant}: independent formula, 7 colourings, double/FloatExp kernels, 1e1000, map and preview.");
            }
            finally { window.Close(); }
        }
        Console.WriteLine($"PASS (folded-cubic): {expectedCases} independent orbits, folds, Jacobians, seven colourings, BLA, FloatExp, 1e1000, maps and six separate modes.");
    }

}
