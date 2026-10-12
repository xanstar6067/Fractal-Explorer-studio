using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

// Сверхглубокий зум: арифметика FloatExp, запись зума в сохранениях и кадры до 1e1000.
internal static partial class Program
{
    // Сверхглубокий зум — отдельной группой, чтобы набор можно было запускать
    // только по ним (см. фильтр в Main).
    private static async Task VerifyExtremeZoomGroupAsync()
    {
        static MandelbrotPalette Palette() =>
            new() { Colors = [Colors.White, Colors.Black], InteriorColor = Colors.Black };

        VerifyFloatExpArithmetic();
        VerifyZoomSerialization();
        await VerifyExtremeZoomAsync(Palette);
        await VerifyFloatExpDeltaVariantsAsync(Palette);
        // В составе deep эти проверки уже выполнены из VerifyPhoenixDeepZoomAsync, поэтому
        // здесь — только при запуске одной группы extreme.
        if (!_phoenixExtremeVerified) await VerifyPhoenixExtremeZoomAsync();
    }

    private static void VerifyFloatExpArithmetic()
    {
        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "FloatExp checks must start at the default working precision.");

        // (a) Values far outside the double range stay exact to the mantissa, and the
        //     reciprocal of a huge value is a small value rather than zero.
        FloatExp huge = FloatExp.Pow10(1000);
        Check(double.IsPositiveInfinity(huge.ToDouble()), "1e1000 must overflow plain double.");
        Check(Math.Abs(huge.Log10() - 1000.0) < 1e-9, $"log10(1e1000) must be 1000, got {huge.Log10()}.");
        FloatExp tiny = 3.0 / huge;
        Check(tiny.Sign > 0 && tiny.ToDouble() == 0.0,
            "3/1e1000 must be a positive FloatExp that merely underflows when narrowed to double.");
        Check(Math.Abs(tiny.Log10() - (Math.Log10(3.0) - 1000.0)) < 1e-9,
            "3/1e1000 must keep its decimal order of magnitude.");

        // (b) The pixel grid at 1e1000: neighbouring pixels must differ. This is precisely
        //     what broke in double — the whole frame collapsed onto the centre.
        FloatExp viewWidth = 3.0 / huge;
        FloatExp left = (0.0 / 1200 - 0.5) * viewWidth;
        FloatExp next = (1.0 / 1200 - 0.5) * viewWidth;
        Check(left != next, "Neighbouring pixel offsets at 1e1000 must differ.");
        FloatExp step = next - left;
        Check(step.Sign > 0 && Math.Abs(step.Log10() - (viewWidth.Log10() - Math.Log10(1200.0))) < 1e-6,
            "The pixel step at 1e1000 must equal the view width divided by the pixel count.");

        // (c) Round-trip through the decimal string, in and out of the double range.
        foreach (FloatExp value in new[]
                 {
                     FloatExp.FromDouble(0.75), FloatExp.FromDouble(1.5e9), FloatExp.FromDouble(-2.25e-30),
                     FloatExp.Pow10(300), FloatExp.Pow10(1000), FloatExp.Pow10(-1000),
                     FloatExp.Pow10(1000) * 1.2345678901234567, -FloatExp.Pow10(700),
                 })
        {
            string text = value.ToInvariantString();
            FloatExp parsed = FloatExp.Parse(text);
            FloatExp difference = FloatExp.Abs(parsed - value);
            bool exact = parsed == value;
            // 17 significant digits are written, so a value outside the double range may lose
            // at most the last ulp; inside the range the "R" format round-trips bit for bit.
            bool withinUlp = difference.IsZero ||
                             difference.Log2() <= FloatExp.Abs(value).Log2() - 50;
            Check(exact || withinUlp, $"FloatExp round-trip lost precision: {text}.");
        }

        // (d) Comparisons and clamping must order by magnitude across the whole range.
        Check(FloatExp.Pow10(1000) > FloatExp.Pow10(999), "Ordering across huge exponents.");
        Check(FloatExp.Pow10(-1000) < FloatExp.FromDouble(1e-300), "Ordering across tiny exponents.");
        Check(-FloatExp.Pow10(1000) < FloatExp.Pow10(-1000), "Ordering across signs.");
        Check(FloatExp.Clamp(FloatExp.Pow10(2000), FloatExp.FromDouble(0.01), FloatExp.Pow10(1000))
              == FloatExp.Pow10(1000), "Clamp must cap at the upper bound.");

        // (e) Square root and the BigFloat bridge: ToBigFloat must carry the mantissa whole,
        //     which is what lets a sub-1e-1000 pan shift reach the exact centre at all.
        FloatExp root = FloatExp.Sqrt(FloatExp.Pow10(1000));
        Check(Math.Abs(root.Log10() - 500.0) < 1e-9, $"sqrt(1e1000) must be 1e500, got log10 {root.Log10()}.");
        FloatExp shift = 3.0 / FloatExp.Pow10(1000) / 1200.0;
        BigFloat asBig = shift.ToBigFloat();
        Check(!asBig.Equals(BigFloat.Zero), "A 1e-1003 pixel shift must survive the move into BigFloat.");
        Check(FloatExp.FromBigFloat(asBig) == shift, "FloatExp -> BigFloat -> FloatExp must be lossless.");

        // (f) A pan shift of that size must actually move a BigFloat centre. On 384 bits it
        //     would vanish entirely — this is the reason the window sizes its precision scope
        //     from the zoom (CenterPrecisionScope).
        BigFloat centre = BigFloat.Parse("-1.25");
        Check((centre + asBig).Equals(centre),
            "On the default 384-bit precision a 1e-1003 shift is expected to vanish.");
        using (new BigFloat.PrecisionScope(3456))
        {
            BigFloat wide = BigFloat.Parse("-1.25");
            Check(!(wide + asBig).Equals(wide),
                "At 3456-bit precision a 1e-1003 shift must move the centre.");
        }
        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "The precision scope must restore the thread default.");

        // (g) An absurd exponent must saturate instead of expanding a BigInteger of that many
        //     digits — the zoom box accepts free text, so "1e99999999" is reachable input.
        var guardTimer = Stopwatch.StartNew();
        Check(!FloatExp.Parse("1e99999999").IsFinite, "An absurd positive exponent must saturate to infinity.");
        Check(FloatExp.Parse("1e-99999999").IsZero, "An absurd negative exponent must saturate to zero.");
        Check(!FloatExp.Parse("-1e2000000000").IsFinite, "An absurd negative-signed exponent must saturate.");
        guardTimer.Stop();
        Check(guardTimer.ElapsedMilliseconds < 1000,
            $"Parsing an absurd exponent must be refused immediately, took {guardTimer.ElapsedMilliseconds} ms.");
        Check(FloatExp.Parse("1e1000") == FloatExp.Pow10(1000),
            "A plausible exponent must still be parsed exactly.");

        Console.WriteLine("[diag] FloatExp: range, pixel grid, round-trip, ordering, BigFloat bridge and parse guard OK");
    }

    // The zoom field is serialized as a JSON number while it fits double (so save files
    // written before the type change keep loading, and new ones keep loading in older
    // builds) and as a scientific-notation string beyond it.
    private static void VerifyZoomSerialization()
    {
        System.Text.Json.JsonSerializerOptions options = JsonOptionsFactory.Create();

        foreach (FloatExp zoom in new[]
                 { FloatExp.FromDouble(0.75), FloatExp.FromDouble(5.7607143988621e25), FloatExp.Pow10(1000) })
        {
            var state = new MandelbrotState { Zoom = zoom, Iterations = 123 };
            string json = System.Text.Json.JsonSerializer.Serialize(state, options);
            MandelbrotState? restored =
                System.Text.Json.JsonSerializer.Deserialize<MandelbrotState>(json, options);
            Check(restored is not null, "Deserialization must succeed.");
            Check(restored!.Zoom == zoom,
                $"Zoom round-trip failed: {zoom.ToInvariantString()} -> {restored.Zoom.ToInvariantString()}.");
        }

        // A legacy file stores the zoom as a plain JSON number; it must still load.
        MandelbrotState? legacy = System.Text.Json.JsonSerializer.Deserialize<MandelbrotState>(
            "{\"Zoom\": 1e25, \"Iterations\": 500}", options);
        Check(legacy is not null && legacy.Zoom == FloatExp.FromDouble(1e25),
            "A zoom stored as a JSON number (older save format) must still load.");

        Console.WriteLine("[diag] zoom serialization: double-range numbers and out-of-range strings round-trip OK");
    }

    // A Julia set is self-similar around a repelling fixed point, so a view centred exactly
    // there resolves structure at ANY magnification — which makes it the only fixture that can
    // prove the engine still sees detail at 1e1000. The fixed point is computed in BigFloat to
    // well over a thousand digits; the Mandelbrot case can only be a "runs and agrees with the
    // exact reference" check, because genuine structure that deep needs a minibrot of period
    // ~1e5, far beyond what a test may spend.
    private static async Task VerifyExtremeZoomAsync(Func<MandelbrotPalette> palette)
    {
        static (int Differing, int MaxDelta) Compare(byte[] a, byte[] b)
        {
            int differing = 0, maxDelta = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                int d = Math.Max(Math.Abs(a[o] - b[o]),
                    Math.Max(Math.Abs(a[o + 1] - b[o + 1]), Math.Abs(a[o + 2] - b[o + 2])));
                if (d != 0) differing++;
                maxDelta = Math.Max(maxDelta, d);
            }
            return (differing, maxDelta);
        }

        static int DistinctColours(byte[] pixels)
        {
            var seen = new HashSet<int>();
            for (int pixel = 0; pixel * 4 < pixels.Length; pixel++)
            {
                int o = pixel * 4;
                seen.Add(pixels[o] | (pixels[o + 1] << 8) | (pixels[o + 2] << 16));
            }
            return seen.Count;
        }

        // Repelling fixed point of z^2+c: z* = (1 + sqrt(1-4c))/2, |2z*| > 1.
        const decimal juliaReal = -0.8m, juliaImaginary = 0.156m;
        string fixedPointX, fixedPointY;
        using (new BigFloat.PrecisionScope(4096))
        {
            var c = ComplexBigFloat.FromDecimal(juliaReal, juliaImaginary);
            ComplexBigFloat discriminant = ComplexBigFloat.One - c * 4L;
            ComplexBigFloat root = ComplexBigFloat.Pow(discriminant, ComplexBigFloat.FromDouble(0.5, 0.0));
            ComplexBigFloat fixedPoint = (ComplexBigFloat.One + root) / 2L;

            // Verify it really is a fixed point and really is repelling.
            ComplexBigFloat image = fixedPoint * fixedPoint + c;
            ComplexBigFloat residual = image - fixedPoint;
            FloatExp residualMagnitude = FloatExp.Sqrt(FloatExp.FromBigFloat(residual.MagnitudeSquared));
            Check(residualMagnitude.IsZero || residualMagnitude.Log10() < -1100,
                $"The Julia fixed point is not accurate enough: residual 1e{residualMagnitude.Log10():F0}.");
            FloatExp multiplier = FloatExp.Sqrt(FloatExp.FromBigFloat((fixedPoint * 2L).MagnitudeSquared));
            Check(multiplier > FloatExp.One,
                "The chosen fixed point must be repelling, otherwise it is not on the Julia set.");

            fixedPointX = fixedPoint.Real.ToInvariantString();
            fixedPointY = fixedPoint.Imaginary.ToInvariantString();
        }
        Check(fixedPointX.Length > 1000 && fixedPointY.Length > 1000,
            "The fixed point must be serialized with over a thousand digits.");

        MandelbrotState JuliaAt(FloatExp zoom, int iterations, MandelbrotColoringMode mode) => new()
        {
            Variant = MandelbrotVariant.Julia,
            JuliaCReal = juliaReal,
            JuliaCImaginary = juliaImaginary,
            CenterXExact = fixedPointX,
            CenterYExact = fixedPointY,
            CenterX = BigFloat.Parse(fixedPointX).ToDecimalClamped(),
            CenterY = BigFloat.Parse(fixedPointY).ToDecimalClamped(),
            Zoom = zoom,
            Iterations = iterations,
            ColoringMode = mode,
            Threads = 2,
            Palette = palette()
        };

        async Task<byte[]> RenderAsync(MandelbrotState state, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            return pixels;
        }

        // (a) 1e300 — past the point where 3/zoom leaves the normal double range. Small enough
        //     frame that the BigFloat reference renderer is affordable.
        {
            MandelbrotState state = JuliaAt(FloatExp.Pow10(300), 1200, MandelbrotColoringMode.Smooth);
            const int w = 48, h = 32, total = w * h;
            byte[] engine = await RenderAsync(state, w, h);
            byte[] exact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
            (int differing, int maxDelta) = Compare(engine, exact);
            int colours = DistinctColours(engine);
            Console.WriteLine($"[diag] extreme 1e300 Julia: {differing}/{total} px differ, max delta {maxDelta}, {colours} colours");
            Check(colours >= 8, $"A view at 1e300 must resolve structure, got {colours} distinct colours.");
            Check(differing * 100 <= total * 5,
                $"At 1e300 the engine diverges from the exact reference on {differing}/{total} px (>5%).");
        }

        // (b) 1e1000 — the new ceiling. Structural check on a usable frame, plus a tiny frame
        //     compared against the exact BigFloat reference (~3456-bit mantissa per pixel,
        //     which is why it has to stay tiny).
        {
            MandelbrotState state = JuliaAt(FloatExp.Pow10(1000), 4000, MandelbrotColoringMode.Smooth);
            const int w = 96, h = 64, total = w * h;
            var stopwatch = Stopwatch.StartNew();
            byte[] engine = await RenderAsync(state, w, h);
            stopwatch.Stop();
            int colours = DistinctColours(engine);
            // Smooth colouring maps the escape time, so a wide spread of colours is exactly the
            // evidence wanted: neighbouring pixels 1e-1003 apart must get different escape
            // times. In double the whole frame collapsed onto the centre and produced one
            // colour. (An interior/escaped split would prove nothing here: next to the Julia
            // set the escape time exceeds any iteration budget, so "interior" would only mean
            // "did not escape in time".)
            Console.WriteLine($"[diag] extreme 1e1000 Julia: {colours} colours over {total} px, {stopwatch.ElapsedMilliseconds} ms");
            Check(engine.Where((_, index) => index % 4 == 3).All(value => value == 255),
                "A 1e1000 render must fill every pixel.");
            Check(colours >= 32, $"A view at 1e1000 must resolve structure, got {colours} distinct colours.");
            Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
                "A 1e1000 render must restore the calling thread working precision.");

            const int rw = 12, rh = 8, rtotal = rw * rh;
            byte[] smallEngine = await RenderAsync(state, rw, rh);
            byte[] smallExact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, rw, rh, CancellationToken.None));
            (int differing, int maxDelta) = Compare(smallEngine, smallExact);
            Console.WriteLine($"[diag] extreme 1e1000 vs exact reference: {differing}/{rtotal} px differ, max delta {maxDelta}");
            Check(differing * 100 <= rtotal * 10,
                $"At 1e1000 the engine diverges from the exact reference on {differing}/{rtotal} px (>10%).");
        }

        // (c) Distance Estimation at extreme depth exercises the extended-range derivative
        //     (Jacobian2Exp): |D| grows roughly like the zoom, so in plain double it would
        //     overflow to infinity and the relief would vanish into a flat fill.
        {
            MandelbrotState state = JuliaAt(FloatExp.Pow10(1000), 3000, MandelbrotColoringMode.DistanceEstimation);
            const int w = 64, h = 44;
            byte[] engine = await RenderAsync(state, w, h);
            int colours = DistinctColours(engine);
            Console.WriteLine($"[diag] extreme 1e1000 Distance Estimation: {colours} colours");
            Check(colours >= 8,
                $"Distance Estimation at 1e1000 must produce relief, got {colours} distinct colours.");
        }

        // (d) Mandelbrot at the ceiling: the centre is only known to 28 digits, so the view is
        //     effectively an arbitrary point at scale 1e-1000 and the image itself carries no
        //     meaning. What is checked is that the path runs, fills the frame, restores the
        //     precision and still agrees with the exact reference.
        {
            var state = new MandelbrotState
            {
                Variant = MandelbrotVariant.Mandelbrot,
                CenterX = -1.2628848671045503000020782246m,
                CenterY = 0.0409687601493310685285376264m,
                Zoom = FloatExp.Pow10(1000),
                Iterations = 1500,
                Threads = 2,
                Palette = palette()
            };
            const int w = 16, h = 12, total = w * h;
            byte[] engine = await RenderAsync(state, w, h);
            byte[] exact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
            (int differing, int maxDelta) = Compare(engine, exact);
            Console.WriteLine($"[diag] extreme 1e1000 Mandelbrot: {differing}/{total} px differ, max delta {maxDelta}");
            Check(engine.Where((_, index) => index % 4 == 3).All(value => value == 255),
                "A 1e1000 Mandelbrot render must fill every pixel.");
            Check(differing * 100 <= total * 10,
                $"At 1e1000 Mandelbrot diverges from the exact reference on {differing}/{total} px (>10%).");
            Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
                "A 1e1000 Mandelbrot render must restore the calling thread working precision.");
        }
    }
}
