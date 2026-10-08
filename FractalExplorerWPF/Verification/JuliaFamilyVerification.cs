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
    private static async Task VerifyJuliaFamilyAsync()
    {
        using var sandbox = DataSandbox.Create("julia-family");
        EnsureThemeStyles();
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        MandelbrotVariant[] variants = [MandelbrotVariant.JuliaGeneralized, MandelbrotVariant.JuliaTricorn,
            MandelbrotVariant.JuliaBuffalo, MandelbrotVariant.JuliaCeltic, MandelbrotVariant.JuliaSimonobrot];
        var iterate = typeof(MandelbrotFamilyRenderer).GetMethod("Iterate", BindingFlags.NonPublic | BindingFlags.Static)!;
        var iterateDecimal = typeof(MandelbrotFamilyRenderer).GetMethod("IterateDecimal", BindingFlags.NonPublic | BindingFlags.Static)!;
        int expectedCases = 0;
        int Expected(MandelbrotState state, Complex z)
        {
            Complex c = new((double)state.JuliaCReal, (double)state.JuliaCImaginary);
            if (state.Variant == MandelbrotVariant.JuliaSimonobrot && state.UseInversion)
                c = new(-c.Real, c.Imaginary);
            int n = 0;
            while (n < state.Iterations && z.Magnitude <= (double)state.Threshold)
            {
                z = state.Variant switch
                {
                    MandelbrotVariant.JuliaGeneralized => Complex.Pow(z, (double)state.Power) + c,
                    MandelbrotVariant.JuliaTricorn => Complex.Conjugate(z) * Complex.Conjugate(z) + c,
                    MandelbrotVariant.JuliaBuffalo => Complex.Pow(new Complex(Math.Abs(z.Real), Math.Abs(z.Imaginary)), 2) + c,
                    MandelbrotVariant.JuliaCeltic => new Complex(Math.Abs((z*z).Real), (z*z).Imaginary) + c,
                    MandelbrotVariant.JuliaSimonobrot => z == Complex.Zero ? c : Complex.Pow(z, (double)state.Power) * Math.Pow(z.Magnitude, (double)state.Power) + c,
                    _ => throw new InvalidOperationException()
                };
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
            Check(definition.Identifier == variant.ToString() && definition.HasJuliaConstant,
                $"{variant}: own save category and C required.");
            var window = new MandelbrotWindow(variant);
            try
            {
                var state = window.CaptureState("independent");
                Check(((UIElement)window.FindName("PowerPanel")).Visibility ==
                    (definition.HasPower ? Visibility.Visible : Visibility.Collapsed), "Power panel visibility.");
                state.Iterations = 90;
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
                            $"{variant}, p={power}, inverse={inversion}, z={re}+{im}i: independent orbit disagrees.");
                        expectedCases++;
                    }
                }
                // All supported power/reflection kernels must agree with direct iteration when C is non-zero.
                state.Power = definition.DefaultPower;
                state.UseInversion = definition.HasInversion;
                state.Zoom = 3; state.CenterX = 0.15m; state.CenterY = 0.1m;
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
                // c=0 has the unit circle as its filled-set boundary for these positive powers.
                state.JuliaCReal = 0; state.JuliaCImaginary = 0;
                state.CenterX = 1; state.CenterY = 0; state.ColoringMode = MandelbrotColoringMode.Smooth;
                state.Iterations = 3600;
                foreach (int exponent in new[] { 12, 80, 350 })
                {
                    state.Zoom = FloatExp.Pow10(exponent);
                    using (var precision = new BigFloat.PrecisionScope((int)Math.Ceiling(exponent*Math.Log2(10))+384))
                    {
                        state.CenterXExact = (BigFloat.FromInt(1)+BigFloat.Parse("1e-"+(exponent+2))).ToInvariantString();
                        state.CenterYExact = BigFloat.Zero.ToInvariantString();
                    }
                    byte[] deep = await Render(state);
                    byte[] reference = await Task.Run(() => MandelbrotFamilyRenderer.RenderExactReferenceForTests(state,24,18,CancellationToken.None));
                    Check(Difference(deep,reference) < 0.015, $"{variant}/1e{exponent}: exact reference differs.");
                    Check(deep.Chunk(4).Select(p => BitConverter.ToUInt32(p)).Distinct().Count() > 1,
                        $"{variant}/1e{exponent}: circle boundary collapsed.");
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
        Console.WriteLine($"PASS (julia-family): {expectedCases} independent orbits and five complete Julia modes.");
    }

}
