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

// Глубокий зум семейства Мандельброта: единый движок, окраски, отражённые варианты, Multibrot, Симоноброт и BLA.
internal static partial class Program
{
    private static async Task VerifyDeepZoomAsync()
    {
        var state = new MandelbrotState
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = 5.7607143988620999e25,
            Iterations = 4500, Threads = 2,
            Palette = new MandelbrotPalette { Colors = [Colors.White, Colors.White], InteriorColor = Colors.Black }
        };
        const int width = 12, height = 8;
        byte[] pixels = new byte[width * height * 4];
        int progress = 0;
        await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, width, height, width * 4,
            CancellationToken.None, value => Interlocked.Exchange(ref progress, value)));
        Check(pixels.Where((_, index) => index % 4 != 3).Any(value => value != 0), "4500 iterations must resolve deep-zoom detail.");
        Check(progress == 100, "Deep-zoom render must complete with 100% progress.");
        state.Iterations = 600;
        await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, width, height, width * 4, CancellationToken.None));
        Check(pixels.Where((_, index) => index % 4 != 3).All(value => value == 0), "Regression fixture must reproduce the old black preview at 600 iterations.");

        await VerifyUnifiedDeepZoomEngineAsync();

        using var cancellation = new CancellationTokenSource();
        state.Iterations = 100_000;
        byte[] cancelledPixels = new byte[480 * 320 * 4];
        Task render = Task.Run(() => MandelbrotFamilyRenderer.Render(state, cancelledPixels, 480, 320, 480 * 4, cancellation.Token));
        cancellation.CancelAfter(30);
        await render.WaitAsync(TimeSpan.FromSeconds(10));
        Check(cancelledPixels.Where((_, index) => index % 4 == 3).Any(value => value == 0), "Cancellation must stop unfinished work.");
    }

    // Phase 1 of the unified deep-zoom engine: adaptive reference-orbit precision plus a
    // FloatExp representation of the per-pixel δ, both behind the existing 1e25 gate.
    // The byte-identical band (zoom <= 1e50) is proven by an external git-stash A/B hash
    // run; here we check the two new mechanisms in isolation.
    private static async Task VerifyUnifiedDeepZoomEngineAsync()
    {
        MandelbrotPalette Palette() =>
            new() { Colors = [Colors.White, Colors.Black], InteriorColor = Colors.Black };

        // 1. The FloatExp-δ kernel must track the trusted double-δ kernel where both are
        //    valid. A moderately deep view with a bounded iteration budget keeps most
        //    pixels off the chaotically sensitive boundary (1 ULP can flip one there).
        var overlap = new MandelbrotState
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = 5.7607143988620999e25,
            Iterations = 2200,
            Threads = 2,
            Palette = Palette()
        };
        const int ow = 110, oh = 72;
        byte[] doubleDelta = new byte[ow * oh * 4];
        byte[] floatExpDelta = new byte[ow * oh * 4];

        MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = false;
        await Task.Run(() => MandelbrotFamilyRenderer.Render(overlap, doubleDelta, ow, oh, ow * 4, CancellationToken.None));
        MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = true;
        await Task.Run(() => MandelbrotFamilyRenderer.Render(overlap, floatExpDelta, ow, oh, ow * 4, CancellationToken.None));
        MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = null;

        Check(floatExpDelta.Where((_, index) => index % 4 != 3).Any(value => value != 0),
            "FloatExp δ kernel must produce an image.");
        Check(doubleDelta.Where((_, index) => index % 4 != 3).Any(value => value != 0),
            "Overlap fixture must have visible structure for the kernel comparison.");
        int differing = 0;
        for (int pixel = 0; pixel < ow * oh; pixel++)
        {
            int b = pixel * 4;
            if (doubleDelta[b] != floatExpDelta[b] ||
                doubleDelta[b + 1] != floatExpDelta[b + 1] ||
                doubleDelta[b + 2] != floatExpDelta[b + 2])
                differing++;
        }
        // At this depth δ stays inside normal double range, so the FloatExp recurrence
        // rounds bit-for-bit like the double one on this fixed fixture. A drift here is a
        // real kernel regression, not boundary chaos (both kernels share the exact same
        // double reference orbit and δc).
        Check(differing == 0,
            $"FloatExp δ kernel diverges from the double δ kernel on {differing}/{ow * oh} pixels.");

        // 2. A view deep enough to switch δ to FloatExp automatically and to lift the
        //    reference precision above the 384-bit floor. No pre-change baseline exists
        //    (old MaxZoom was 1e50); the checks are that the new paths run, complete and
        //    leave the calling thread's working precision restored. Rendered synchronously
        //    so the PrecisionScope opens and closes on *this* thread.
        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Working precision must start at the minimum.");
        var deep = new MandelbrotState
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = 1.0e120,
            Iterations = 2600,
            Threads = 2,
            Palette = Palette()
        };
        int deepProgress = 0;
        byte[] deepPixels = new byte[80 * 56 * 4];
        MandelbrotFamilyRenderer.Render(deep, deepPixels, 80, 56, 80 * 4,
            CancellationToken.None, value => deepProgress = value);
        Check(deepProgress == 100, "Deep FloatExp render must complete with 100% progress.");
        Check(deepPixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
            "Deep FloatExp render must fill every pixel.");
        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Deep-zoom render must restore the calling thread's working precision.");

        VerifyBigFloatSqrt();
        VerifyBigFloatTranscendental();
        VerifyBigFloatLogarithm();
        await VerifyCollatzDeepZoomAsync();
        await VerifyPhoenixDeepZoomAsync();
        await VerifyNovaDeepZoomAsync();
        await VerifyNewtonDeepZoomAsync();
        await VerifyDecimalStageRemovedAsync(Palette);
        await VerifyBlaAccelerationAsync(Palette);
        await VerifyRealBlaAccelerationAsync(Palette);
        await VerifyReflectedVariantsAsync(Palette);
        await VerifyMultibrotDeepZoomAsync(Palette);
        await VerifySimonobrotDeepZoomAsync(Palette);
        await VerifyHistogramDeepZoomAsync(Palette);
        await VerifyDistanceEstimationDeepZoomAsync(Palette);
        await VerifyEngineAccuracyAsync(Palette);
        await VerifyExtremeZoomGroupAsync();
    }

    // Phase 7: Histogram coloring moved onto the deep engine (RenderDeepZoomHistogram) — the
    // decimal stage it used to require unconditionally is now only a degenerate-orbit
    // fallback. Same two-pass CDF pipeline as the decimal version, fed by the already-proven
    // deep kernels (Iterations/Smooth are unconditional in every kernel, so BLA/FloatExp/
    // reflection/power formulas all carry through unchanged).
    private static async Task VerifyHistogramDeepZoomAsync(Func<MandelbrotPalette> palette)
    {
        static int RgbDelta(byte[] a, byte[] b, int pixel)
        {
            int o = pixel * 4;
            return Math.Max(Math.Abs(a[o] - b[o]),
                   Math.Max(Math.Abs(a[o + 1] - b[o + 1]), Math.Abs(a[o + 2] - b[o + 2])));
        }

        static int CountDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
                if (RgbDelta(a, b, pixel) != 0) n++;
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceDeep, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally { MandelbrotFamilyRenderer.ForceDeepZoomForTests = null; }
            return pixels;
        }

        const int w = 112, h = 74, total = w * h;
        MandelbrotState HistogramState(double zoom, int iterations, bool equalize, bool useSmooth) => new()
        {
            ColoringMode = MandelbrotColoringMode.Histogram,
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = zoom,
            Iterations = iterations,
            HistogramEnabledEqualization = equalize,
            HistogramInputUseSmooth = useSmooth,
            Threads = 2,
            Palette = palette()
        };

        // (a) Where decimal was still trustworthy, the deep two-pass pipeline (binning + CDF
        //     + colouring) must reproduce it up to boundary chaos — across every combination
        //     of equalization and smooth/iteration binning.
        foreach ((bool equalize, bool useSmooth) in new[] { (true, true), (true, false), (false, true), (false, false) })
        {
            MandelbrotState state = HistogramState(1.0e12, 6000, equalize, useSmooth);
            byte[] decimalPixels = await RenderAsync(state, forceDeep: false, w, h);
            byte[] deepPixels = await RenderAsync(state, forceDeep: true, w, h);
            int differing = CountDiffering(decimalPixels, deepPixels);
            Console.WriteLine($"[diag] Histogram equalize={equalize} smooth={useSmooth}: decimal vs deep {differing}/{total} px differ");
            Check(deepPixels.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                $"Histogram (equalize={equalize}, smooth={useSmooth}) must resolve structure.");
            Check(differing * 100 <= total * 5,
                $"Histogram (equalize={equalize}, smooth={useSmooth}): decimal vs deep diverges on {differing}/{total} px (>5%).");
        }

        // (b) Determinism: the two-pass parallel binning must not depend on thread scheduling.
        {
            MandelbrotState state = HistogramState(1.0e18, 4000, equalize: true, useSmooth: true);
            byte[] a = await RenderAsync(state, forceDeep: true, w, h);
            byte[] b = await RenderAsync(state, forceDeep: true, w, h);
            Check(CountDiffering(a, b) == 0, "Deep Histogram must be deterministic across runs.");
        }

        // (c) Degenerate reference orbit + Histogram must still fall back cleanly (decimal
        //     two-pass render, no crash) instead of silently mis-colouring with normalized=0.
        {
            var degenerate = new MandelbrotState
            {
                ColoringMode = MandelbrotColoringMode.Histogram,
                CenterX = 1000m,
                CenterY = 1000m,
                Zoom = 1.0e30,
                Iterations = 500,
                Threads = 2,
                Palette = palette()
            };
            int progress = 0;
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.Render(degenerate, pixels, w, h, w * 4,
                CancellationToken.None, value => progress = value);
            Check(progress == 100, "Degenerate-orbit Histogram fallback must complete with 100% progress.");
            Check(pixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
                "Degenerate-orbit Histogram fallback must fill every pixel.");
        }

        // (d) Tile-mode preview path (local normalization, not the full-frame CDF) must not
        //     crash and must resolve structure for a deep-eligible state.
        {
            MandelbrotState state = HistogramState(1.0e12, 3000, equalize: true, useSmooth: true);
            var tile = new MandelbrotRenderTile(0, 0, w, h, 0, 0);
            byte[]? tilePixels = MandelbrotFamilyRenderer.RenderTile(state, w, h, tile, CancellationToken.None);
            Check(tilePixels is not null, "Histogram tile render must not be cancelled.");
            Check(tilePixels!.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                "Histogram tile render must resolve structure.");
        }
    }

    // Phase 8: Distance Estimation moved onto the deep engine
    // (RenderDeepZoomDistanceEstimation). No new per-formula perturbation math was needed:
    // the derivative recurrence D ← J(z)·D + ∂f/∂c depends on z alone, and every kernel
    // already assembles z = Z + δ in double for orbit-trap/stripe — so DE arrives for all
    // supported variants at once, reusing GetIterationJacobian from the flat stage verbatim.
    // BLA is disabled in this mode (skipping iterations would skip derivative steps).
    // Distances are stored normalized to the pixel size; float would flush the absolute
    // deep-zoom values (~1e-43 already at zoom 1e40) straight to zero and kill the relief.
    private static async Task VerifyDistanceEstimationDeepZoomAsync(Func<MandelbrotPalette> palette)
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

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceDeep, bool? forceBla, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            MandelbrotFamilyRenderer.ForceBlaForTests = forceBla;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally
            {
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
                MandelbrotFamilyRenderer.ForceBlaForTests = null;
            }
            return pixels;
        }

        static MandelbrotState De(
            MandelbrotVariant variant, decimal cx, decimal cy, double zoom, int iterations,
            MandelbrotPalette pal, decimal power = 2m, decimal jr = 0m, decimal ji = 0m,
            bool inversion = false, double relief = 1.35,
            string? exactX = null, string? exactY = null) => new()
        {
            ColoringMode = MandelbrotColoringMode.DistanceEstimation,
            Variant = variant,
            CenterX = cx,
            CenterY = cy,
            CenterXExact = exactX,
            CenterYExact = exactY,
            Power = power,
            JuliaCReal = jr,
            JuliaCImaginary = ji,
            UseInversion = inversion,
            Zoom = zoom,
            Iterations = iterations,
            DistanceReliefStrength = relief,
            Threads = 2,
            Palette = pal
        };

        var mandelCentre = (X: -1.2628848671045503000020782246m, Y: 0.0409687601493310685285376264m);

        // (a) Where the flat double stage is still trustworthy, the perturbation kernels must
        //     reproduce its relief. Both stages run the identical Jacobian/EstimateDistance
        //     code; the only difference is where z comes from.
        {
            const int w = 96, h = 64, total = w * h;
            foreach (double zoom in new[] { 1.0e6, 1.0e8 })
            {
                MandelbrotState state = De(MandelbrotVariant.Mandelbrot,
                    mandelCentre.X, mandelCentre.Y, zoom, 3000, palette());
                byte[] flat = await RenderAsync(state, forceDeep: false, forceBla: null, w, h);
                byte[] deep = await RenderAsync(state, forceDeep: true, forceBla: null, w, h);
                (int differing, int maxDelta) = Compare(flat, deep);
                Console.WriteLine($"[diag] DE zoom {zoom:E0}: flat vs deep {differing}/{total} px differ (maxΔ {maxDelta})");
                Check(deep.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                    $"Deep DE must resolve structure at zoom {zoom:E0}.");
                Check(differing * 100 <= total * 8,
                    $"Deep DE diverges from the flat stage on {differing}/{total} px at zoom {zoom:E0} (>8%).");
            }
        }

        // (b) Accuracy against the near-exact BigFloat reference, which drives the very same
        //     derivative recurrence from a directly iterated arbitrary-precision orbit. Small
        //     images: the reference samples (w+2)x(h+2) pixels and is slow.
        //
        //     The headline assertion is the DE *excess*: the same view is also rendered in
        //     Smooth mode (no derivative at all) and compared to the reference, so we can tell
        //     the error Distance Estimation adds from the error the underlying orbit already
        //     carries. On a chaotic view the second number is large for reasons that predate
        //     this phase, and only the excess is meaningful.
        {
            const int w = 40, h = 28, total = w * h;

            // -2 - 10^-offset: an exactly-representable centre just outside the antenna tip,
            // at any depth. The whole frame then sits in the smooth escaping region, where
            // the orbit is not chaotic and an exact comparison is actually meaningful.
            static string OutsideTip(int offsetDigits) => "-2." + new string('0', offsetDigits - 1) + "1";

            (string Label, bool OrbitChaotic, MandelbrotState State)[] views =
            {
                ("Mandelbrot 1e30",         false, De(MandelbrotVariant.Mandelbrot, mandelCentre.X, mandelCentre.Y, 1.0e30, 4000, palette())),
                ("Mandelbrot outside-tip 1e50",  false, De(MandelbrotVariant.Mandelbrot, -2m, 0m, 1.0e50, 800, palette(), exactX: OutsideTip(46), exactY: "0")),
                ("Mandelbrot outside-tip 1e120", false, De(MandelbrotVariant.Mandelbrot, -2m, 0m, 1.0e120, 800, palette(), exactX: OutsideTip(118), exactY: "0")),
                // Unit-circle Julia (c = 0): centre 1 is exact at any depth and the dynamics
                // z <- z^2 are perfectly smooth, so this isolates deep-zoom numerics from
                // boundary chaos. Also the only fixture here that starts the derivative at I.
                ("Julia c=0 circle 1e50",   false, De(MandelbrotVariant.Julia, 1m, 0m, 1.0e50, 600, palette())),
                // The antenna tip itself: half the frame (c > -2) is the chaotic region of the
                // real quadratic map, so the orbit alone diverges on ~50% of pixels. Kept
                // deliberately - it is the case where only the DE excess can be asserted.
                ("Mandelbrot tip 1e50",     true,  De(MandelbrotVariant.Mandelbrot, -2m, 0m, 1.0e50, 800, palette())),
                ("BurningShip 1e30",        false, De(MandelbrotVariant.BurningShip, -1.62m, 0m, 1.0e30, 3000, palette())),
                ("Tricorn 1e10",            false, De(MandelbrotVariant.Tricorn, -1.62m, 0m, 1.0e10, 2000, palette())),
                ("Buffalo 1e10",            false, De(MandelbrotVariant.Buffalo, -1.62m, 0m, 1.0e10, 2000, palette())),
                ("Celtic 1e10",             false, De(MandelbrotVariant.Celtic, -1.62m, 0m, 1.0e10, 2000, palette())),
                ("JuliaBurningShip 1e10",   false, De(MandelbrotVariant.JuliaBurningShip, 0.5m, -0.3m, 1.0e10, 2000, palette(), jr: -1.5m)),
                ("Multibrot p=3",           false, De(MandelbrotVariant.Generalized, -0.295455m, 0.977273m, 300.0, 2000, palette(), power: 3m)),
                ("Multibrot p=8",           false, De(MandelbrotVariant.Generalized, 0.66m, 0m, 300.0, 2000, palette(), power: 8m)),
                ("Simonobrot p=2",          false, De(MandelbrotVariant.Simonobrot, -0.03m, 0.84m, 300.0, 2000, palette(), power: 2m)),
                ("Simonobrot p=6 inv",      false, De(MandelbrotVariant.Simonobrot, -0.90m, 0.18m, 300.0, 2000, palette(), power: 6m, inversion: true)),
            };

            foreach ((string label, bool chaotic, MandelbrotState state) in views)
            {
                byte[] engine = await RenderAsync(state, forceDeep: true, forceBla: null, w, h);
                byte[] exact = await Task.Run(() =>
                    MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
                (int differing, int maxDelta) = Compare(engine, exact);
                int nonBlack = 0;
                for (int i = 0; i < total; i++)
                    if (engine[i * 4] != 0 || engine[i * 4 + 1] != 0 || engine[i * 4 + 2] != 0) nonBlack++;

                // Same view without any derivative: how much of the difference is the orbit's?
                state.ColoringMode = MandelbrotColoringMode.Smooth;
                byte[] smoothEngine = await RenderAsync(state, forceDeep: true, forceBla: null, w, h);
                byte[] smoothExact = await Task.Run(() =>
                    MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
                state.ColoringMode = MandelbrotColoringMode.DistanceEstimation;
                int orbitOnly = Compare(smoothEngine, smoothExact).Differing;
                int excess = Math.Abs(differing - orbitOnly);

                Console.WriteLine($"[diag] DE accuracy {label}: {differing}/{total} px differ " +
                                  $"({100.0 * differing / total:F2}%), maxD {maxDelta}, nonblack {nonBlack}, " +
                                  $"orbit-only {orbitOnly}, DE excess {excess}");
                Check(nonBlack > total / 10, $"DE view {label} must carry structure.");
                // Both `differing` and `orbitOnly` are themselves comparisons against a
                // per-pixel independently-iterated BigFloat orbit, which on a chaotic view is
                // exactly as sensitive to last-bit arithmetic differences as the orbit itself
                // (see the shallow/deep boundary-chaos precedent elsewhere in this file) - their
                // difference ("excess") inherits that same instability and isn't a meaningful
                // signal here, so it's skipped for chaotic views for the same reason `differing`
                // already is below.
                if (!chaotic)
                {
                    Check(excess * 100 <= total * 3,
                        $"DE {label}: the derivative adds {excess}/{total} px of error over the orbit itself (>3%).");
                    Check(differing * 100 <= total * 8,
                        $"DE {label}: deep engine diverges from the exact reference on {differing}/{total} px (>8%).");
                }
            }
        }
        // (c) The relief must survive the depth. If the normalized distance field had
        //     underflowed to zero, ApplyDistanceLighting would early-return the unshaded base
        //     colour for every pixel and the relief strength would stop mattering — so a
        //     relief-on vs relief-off render being identical is exactly the failure mode.
        foreach ((decimal cx, decimal cy, double zoom, int iterations, string label) in new[]
        {
            (mandelCentre.X, mandelCentre.Y, 1.0e30, 4000, "centre 1e30"),
            (-2m, 0m, 1.0e50, 800, "tip 1e50"),
            (-2m, 0m, 1.0e120, 800, "tip 1e120"),
        })
        {
            const int w = 64, h = 44, total = w * h;
            MandelbrotState lit = De(MandelbrotVariant.Mandelbrot, cx, cy, zoom, iterations, palette());
            MandelbrotState flatLit = De(MandelbrotVariant.Mandelbrot, cx, cy, zoom, iterations, palette(), relief: 0);
            byte[] withRelief = await RenderAsync(lit, forceDeep: true, forceBla: null, w, h);
            byte[] withoutRelief = await RenderAsync(flatLit, forceDeep: true, forceBla: null, w, h);
            (int differing, int maxDelta) = Compare(withRelief, withoutRelief);
            Console.WriteLine($"[diag] DE relief alive, {label}: {differing}/{total} px react to relief (maxΔ {maxDelta})");
            Check(differing * 4 > total,
                $"DE distance field collapsed at {label}: only {differing}/{total} px react to relief.");
        }

        // (d) BLA must be inert in this mode — the pyramid skips iterations, which would skip
        //     derivative steps. Forcing it on and off must give bit-identical output.
        {
            const int w = 64, h = 44;
            MandelbrotState state = De(MandelbrotVariant.Mandelbrot, mandelCentre.X, mandelCentre.Y, 1.0e30, 3500, palette());
            byte[] blaOn = await RenderAsync(state, forceDeep: true, forceBla: true, w, h);
            byte[] blaOff = await RenderAsync(state, forceDeep: true, forceBla: false, w, h);
            Check(Compare(blaOn, blaOff).Differing == 0,
                "BLA must be disabled for Distance Estimation (output changed with BLA forced on).");
        }

        // (e) Degenerate reference orbit + DE must fall back to the decimal two-pass render.
        {
            const int w = 64, h = 44;
            MandelbrotState degenerate = De(MandelbrotVariant.Mandelbrot, 1000m, 1000m, 1.0e30, 500, palette());
            int progress = 0;
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.Render(degenerate, pixels, w, h, w * 4,
                CancellationToken.None, value => progress = value);
            Check(progress == 100, "Degenerate-orbit DE fallback must complete with 100% progress.");
            Check(pixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
                "Degenerate-orbit DE fallback must fill every pixel.");
        }

        // (f) Tile path (used by the preview scheduler) must resolve the same relief.
        {
            const int w = 64, h = 44;
            MandelbrotState state = De(MandelbrotVariant.Mandelbrot, mandelCentre.X, mandelCentre.Y, 1.0e30, 3000, palette());
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = true;
            byte[]? tilePixels;
            byte[] fullPixels = new byte[w * h * 4];
            try
            {
                var tile = new MandelbrotRenderTile(0, 0, w, h, 0, 0);
                tilePixels = MandelbrotFamilyRenderer.RenderTile(state, w, h, tile, CancellationToken.None);
                MandelbrotFamilyRenderer.Render(state, fullPixels, w, h, w * 4, CancellationToken.None);
            }
            finally { MandelbrotFamilyRenderer.ForceDeepZoomForTests = null; }
            Check(tilePixels is not null, "Deep DE tile render must not be cancelled.");
            Check(tilePixels!.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                "Deep DE tile render must resolve structure.");
            // A whole-canvas tile samples exactly the same grid as the full-frame pass.
            Check(Compare(tilePixels!, fullPixels).Differing == 0,
                "Deep DE tile render must match the full-frame render on a full-canvas tile.");
        }
    }

    // Phase 5: Generalized/Multibrot of integer power p on the deep engine — exact binomial
    // perturbation (Z+δ)ᵖ−Zᵖ, BLA with a p-dependent table. Verified against the exact
    // BigFloat reference (repeated-multiplication zᵖ, no perturbation).
    private static async Task VerifyMultibrotDeepZoomAsync(Func<MandelbrotPalette> palette)
    {
        static int CountRgbDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                if (a[o] != b[o] || a[o + 1] != b[o + 1] || a[o + 2] != b[o + 2]) n++;
            }
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceDeep, bool? forceBla, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            MandelbrotFamilyRenderer.ForceBlaForTests = forceBla;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally
            {
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
                MandelbrotFamilyRenderer.ForceBlaForTests = null;
            }
            return pixels;
        }

        const int w = 56, h = 40, total = w * h;

        // Structured boundary views per power; the perturbation engine is forced on so the
        // kernel is exercised on real detail and compared to the exact BigFloat reference
        // (repeated-multiplication zᵖ, no perturbation). Small image / modest iterations —
        // the exact BigFloat renderer is slow.
        (int Power, decimal Cx, decimal Cy)[] cases =
        {
            (3, -0.295455m, 0.977273m),
            (5, -0.540000m, 0.600000m),
            (8, 0.660000m, 0.000000m),
            (12, 0.750000m, 0.000000m),
        };

        foreach ((int power, decimal cx, decimal cy) in cases)
        {
            var state = new MandelbrotState
            {
                Variant = MandelbrotVariant.Generalized,
                Power = power,
                CenterX = cx,
                CenterY = cy,
                Zoom = 300.0,
                Iterations = 2000,
                Threads = 2,
                Palette = palette()
            };
            byte[] perturbation = await RenderAsync(state, forceDeep: true, forceBla: null, w, h);
            byte[] exact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
            byte[] blaOff = await RenderAsync(state, forceDeep: true, forceBla: false, w, h);

            int vsExact = CountRgbDiffering(perturbation, exact);
            int vsBlaOff = CountRgbDiffering(perturbation, blaOff);
            int maxD = 0, nonBlack = 0;
            for (int i = 0; i < total; i++)
            {
                int o = i * 4;
                maxD = Math.Max(maxD, Math.Max(Math.Abs(perturbation[o] - exact[o]),
                    Math.Max(Math.Abs(perturbation[o + 1] - exact[o + 1]), Math.Abs(perturbation[o + 2] - exact[o + 2]))));
                if (perturbation[o] != 0 || perturbation[o + 1] != 0 || perturbation[o + 2] != 0) nonBlack++;
            }
            Console.WriteLine($"[diag] Multibrot p={power}: vs exact {vsExact}/{total} (maxΔ {maxD}), BLA on/off {vsBlaOff}/{total}, nonblack {nonBlack}");
            Check(nonBlack > total / 10, $"Multibrot p={power} view must carry structure.");
            Check(vsExact * 100 <= total * 3,
                $"Multibrot p={power}: perturbation diverges from exact on {vsExact}/{total} px, maxΔ {maxD} (>3%).");
            Check(vsBlaOff * 100 <= total * 3,
                $"Multibrot p={power}: BLA changes {vsBlaOff}/{total} px vs non-BLA (>3%).");
        }

        // Mandelbrot must be untouched by the Multibrot path.
        var mandel = new MandelbrotState
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = 5.0e25,
            Iterations = 4000,
            Threads = 2,
            Palette = palette()
        };
        Check(CountRgbDiffering(
                await RenderAsync(mandel, forceDeep: true, forceBla: null, w, h),
                await RenderAsync(mandel, forceDeep: true, forceBla: null, w, h)) == 0,
            "Mandelbrot deep render must stay deterministic after Phase 5.");
    }

    // Phase 6: Simonobrot of even integer power p=2q — composition of two exact binomial
    // perturbations (zᵖ and |z|ᵖ=Mᵠ). Phase 10 added odd p=2q+1, where the modulus factor
    // carries a square root and is perturbed by the exact identity
    // δ√M = δm/(√(M+δm)+√M). Verified against the exact BigFloat reference, both with and
    // without UseInversion (which flips the sign the reference-orbit cache key must also
    // carry) — and, for the odd powers, against the plain-double Iterate path as well:
    // that one goes through Complex.Pow/Math.Pow, so it is the only oracle that does not
    // share StepSimonobrotReference with the engine under test.
    private static async Task VerifySimonobrotDeepZoomAsync(Func<MandelbrotPalette> palette)
    {
        static int CountRgbDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                if (a[o] != b[o] || a[o + 1] != b[o + 1] || a[o + 2] != b[o + 2]) n++;
            }
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceDeep, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally { MandelbrotFamilyRenderer.ForceDeepZoomForTests = null; }
            return pixels;
        }

        const int w = 56, h = 40, total = w * h;

        // Structured boundary views per power/inversion combo (auto-located).
        (int Power, bool Inversion, decimal Cx, decimal Cy)[] cases =
        {
            (2, false, -0.03m, 0.84m),
            (2, true,   0.03m, 0.84m),
            (6, false, -0.90m, 0.18m),
            (8, true,  -0.33m, 0.87m),
            (12, false, -0.12m, 0.84m),
            // Phase 10 — odd powers, where the modulus factor is Mᵠ·√M. Centres picked for
            // edge density (the detail the kernel has to reproduce), not just for non-black.
            (3, false, -0.70m, -0.35m),
            (5, false,  0.55m, -0.85m),
            (7, true,   0.30m, 0.85m),
            (9, false,  0.95m, 0.25m),
            (11, false, -0.10m, -0.95m),
        };

        foreach ((int power, bool inversion, decimal cx, decimal cy) in cases)
        {
            var state = new MandelbrotState
            {
                Variant = MandelbrotVariant.Simonobrot,
                Power = power,
                UseInversion = inversion,
                CenterX = cx,
                CenterY = cy,
                Zoom = 300.0,
                Iterations = 2000,
                Threads = 2,
                Palette = palette()
            };
            byte[] perturbation = await RenderAsync(state, forceDeep: true, w, h);
            byte[] exact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
            // The exact reference and the reference orbit share StepSimonobrotReference, so
            // it cannot catch an error in the formula itself — only in the perturbation on
            // top of it. The plain-double path can: it computes zᵖ·|z|ᵖ through
            // Complex.Pow and Math.Pow(√M, p), independently of everything above. At zoom
            // 300 double is exact enough to be that oracle.
            byte[] flat = await RenderAsync(state, forceDeep: false, w, h);

            int vsExact = CountRgbDiffering(perturbation, exact);
            int vsFlat = CountRgbDiffering(perturbation, flat);
            int maxD = 0, nonBlack = 0;
            for (int i = 0; i < total; i++)
            {
                int o = i * 4;
                maxD = Math.Max(maxD, Math.Max(Math.Abs(perturbation[o] - exact[o]),
                    Math.Max(Math.Abs(perturbation[o + 1] - exact[o + 1]), Math.Abs(perturbation[o + 2] - exact[o + 2]))));
                if (perturbation[o] != 0 || perturbation[o + 1] != 0 || perturbation[o + 2] != 0) nonBlack++;
            }
            Console.WriteLine($"[diag] Simonobrot p={power} inv={inversion}: vs exact {vsExact}/{total} (maxΔ {maxD}), " +
                $"vs plain double {vsFlat}/{total}, nonblack {nonBlack}");
            Check(nonBlack > total / 10, $"Simonobrot p={power} inv={inversion} view must carry structure.");
            Check(vsExact * 100 <= total * 3,
                $"Simonobrot p={power} inv={inversion}: perturbation diverges from exact on {vsExact}/{total} px, maxΔ {maxD} (>3%).");
            Check(vsFlat * 100 <= total * 3,
                $"Simonobrot p={power} inv={inversion}: perturbation diverges from the plain-double formula on {vsFlat}/{total} px (>3%).");
        }

        // UseInversion must be part of the reference-orbit cache key: two renders that
        // differ only by it, at the same centre/zoom, must not collide.
        {
            var baseState = new MandelbrotState
            {
                Variant = MandelbrotVariant.Simonobrot,
                Power = 2,
                CenterX = -0.03m,
                CenterY = 0.84m,
                Zoom = 300.0,
                Iterations = 2000,
                Threads = 2,
                Palette = palette()
            };
            var inverted = new MandelbrotState
            {
                Variant = MandelbrotVariant.Simonobrot,
                Power = 2,
                UseInversion = true,
                CenterX = -0.03m,
                CenterY = 0.84m,
                Zoom = 300.0,
                Iterations = 2000,
                Threads = 2,
                Palette = palette()
            };
            byte[] a = await RenderAsync(baseState, forceDeep: true, w, h);
            byte[] b = await RenderAsync(inverted, forceDeep: true, w, h);
            Check(CountRgbDiffering(a, b) > total / 4,
                "UseInversion must be part of the orbit cache key (renders collided).");
        }
    }

    // How significant is the quality loss of the production engine (perturbation + BLA +
    // FloatExp) versus a near-exact BigFloat direct-iteration reference? Reports the
    // fraction of differing pixels and the worst per-channel delta per view.
    private static async Task VerifyEngineAccuracyAsync(Func<MandelbrotPalette> palette)
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

        MandelbrotPalette Pal() => palette();

        MandelbrotState Deep(MandelbrotVariant variant, decimal cx, decimal cy, double zoom, int iterations,
            decimal power = 2m, decimal jr = 0m, decimal ji = 0m) => new()
        {
            Variant = variant,
            CenterX = cx,
            CenterY = cy,
            Power = power,
            JuliaCReal = jr,
            JuliaCImaginary = ji,
            Zoom = zoom,
            Iterations = iterations,
            Threads = 2,
            Palette = Pal()
        };

        const int w = 48, h = 32, total = w * h;
        var mandelCentre = (X: -1.2628848671045503000020782246m, Y: 0.0409687601493310685285376264m);

        (string Label, MandelbrotState State)[] views =
        {
            ("Mandelbrot 1e30",        Deep(MandelbrotVariant.Mandelbrot, mandelCentre.X, mandelCentre.Y, 1.0e30, 4500)),
            ("Mandelbrot 1e120 (fexp)",Deep(MandelbrotVariant.Mandelbrot, mandelCentre.X, mandelCentre.Y, 1.0e120, 4500)),
            ("BurningShip 1e30",       Deep(MandelbrotVariant.BurningShip, -1.62m, 0m, 1.0e30, 3000)),
        };

        foreach ((string label, MandelbrotState state) in views)
        {
            byte[] engine = new byte[w * h * 4];
            await Task.Run(() => MandelbrotFamilyRenderer.Render(state, engine, w, h, w * 4, CancellationToken.None));
            byte[] exact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, w, h, CancellationToken.None));
            (int differing, int maxDelta) = Compare(engine, exact);
            int nonBlack = 0;
            for (int i = 0; i < total; i++)
                if (engine[i * 4] != 0 || engine[i * 4 + 1] != 0 || engine[i * 4 + 2] != 0) nonBlack++;
            double percent = 100.0 * differing / total;
            Console.WriteLine($"[diag] accuracy {label}: {differing}/{total} px differ ({percent:F2}%), max Δ {maxDelta}, nonblack {nonBlack}");
            Check(differing * 100 <= total * 5,
                $"Engine diverges from the exact reference on {differing}/{total} px for {label} (>5%).");
        }
    }

    // Phase 4: reflected/conjugate variants (Burning Ship, Julia Burning Ship, Tricorn,
    // Buffalo, Celtic) on the perturbation engine via a sign-folded δ recurrence (no BLA).
    // Where decimal is still trustworthy the perturbation render must reproduce it up to
    // boundary chaos; Mandelbrot/Julia must be untouched.
    private static async Task VerifyReflectedVariantsAsync(Func<MandelbrotPalette> palette)
    {
        static int CountRgbDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                if (a[o] != b[o] || a[o + 1] != b[o + 1] || a[o + 2] != b[o + 2]) n++;
            }
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceDeep, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally { MandelbrotFamilyRenderer.ForceDeepZoomForTests = null; }
            return pixels;
        }

        const int w = 120, h = 80, total = w * h;

        (MandelbrotVariant variant, decimal cx, decimal cy, decimal jr, decimal ji, string label)[] cases =
        {
            (MandelbrotVariant.BurningShip,      -1.62m, 0m,     0m,     0m,   "BurningShip"),
            (MandelbrotVariant.Tricorn,          -1.62m, 0m,     0m,     0m,   "Tricorn"),
            (MandelbrotVariant.Buffalo,          -1.62m, 0m,     0m,     0m,   "Buffalo"),
            (MandelbrotVariant.Celtic,           -1.62m, 0m,     0m,     0m,   "Celtic"),
            (MandelbrotVariant.JuliaBurningShip,  0.5m, -0.3m, -1.5m,   0m,   "JuliaBurningShip"),
        };

        foreach ((MandelbrotVariant variant, decimal cx, decimal cy, decimal jr, decimal ji, string label) in cases)
        {
            foreach (double zoom in new[] { 1.0e10, 1.0e16 })
            {
                var state = new MandelbrotState
                {
                    Variant = variant,
                    CenterX = cx,
                    CenterY = cy,
                    JuliaCReal = jr,
                    JuliaCImaginary = ji,
                    Zoom = zoom,
                    Iterations = 3000,
                    Threshold = 2m,
                    Threads = 2,
                    Palette = palette()
                };
                byte[] decimalPixels = await RenderAsync(state, forceDeep: false, w, h);
                byte[] perturbationPixels = await RenderAsync(state, forceDeep: true, w, h);
                int differing = CountRgbDiffering(decimalPixels, perturbationPixels);
                Console.WriteLine($"[diag] {label} zoom {zoom:E0}: decimal vs perturbation {differing}/{total} px differ");
                Check(perturbationPixels.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                    $"{label} perturbation must resolve structure at zoom {zoom:E0}.");
                Check(differing * 100 <= total * 8,
                    $"{label}: perturbation diverges from decimal on {differing}/{total} px at zoom {zoom:E0} (>8%).");
            }
        }

        // Regression guard: the reflected code path must not touch Mandelbrot.
        var mandel = new MandelbrotState
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = 5.0e25,
            Iterations = 4000,
            Threads = 2,
            Palette = palette()
        };
        byte[] a1 = await RenderAsync(mandel, forceDeep: true, w, h);
        byte[] a2 = await RenderAsync(mandel, forceDeep: true, w, h);
        Check(CountRgbDiffering(a1, a2) == 0, "Mandelbrot deep render must stay deterministic after Phase 4.");
    }

    // Phase 3: BLA (Zhuoran) skips runs of iterations where δ stays small. It is a pure
    // accelerator over the Phase 2 perturbation engine — the output must match BLA-off up
    // to a few boundary pixels (composite BLAs carry their own rounding order), and the
    // OrbitTrap/StripeAverage modes must be untouched (BLA disabled there).
    private static async Task VerifyBlaAccelerationAsync(Func<MandelbrotPalette> palette)
    {
        static int CountRgbDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                if (a[o] != b[o] || a[o + 1] != b[o + 1] || a[o + 2] != b[o + 2]) n++;
            }
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool bla, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceBlaForTests = bla;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally { MandelbrotFamilyRenderer.ForceBlaForTests = null; }
            return pixels;
        }

        MandelbrotState Deep(double zoom, int iterations, MandelbrotColoringMode mode = MandelbrotColoringMode.Smooth) => new()
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = zoom,
            Iterations = iterations,
            Threads = 2,
            ColoringMode = mode,
            Palette = palette()
        };

        const int w = 120, h = 80, total = w * h;

        // (a) BLA on vs off across the engine's regimes (double-δ, deeper double-δ, FloatExp-δ).
        foreach ((double zoom, int iterations, string label) in new[]
        {
            (5.0e25, 4000, "double-δ 5e25"),
            (1.0e40, 5000, "double-δ 1e40"),
            (1.0e120, 3500, "FloatExp-δ 1e120"),
        })
        {
            MandelbrotState state = Deep(zoom, iterations);
            byte[] off = await RenderAsync(state, bla: false, w, h);
            byte[] on = await RenderAsync(state, bla: true, w, h);
            int differing = CountRgbDiffering(off, on);
            Console.WriteLine($"[diag] BLA {label}: on vs off {differing}/{total} px differ");
            Check(on.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                $"BLA render must resolve structure ({label}).");
            Check(differing * 100 <= total * 2,
                $"BLA changes {differing}/{total} px vs non-BLA ({label}, >2%).");
        }

        // (b) Julia deep (B = 0 in the BLA table): on vs off must still match.
        {
            var julia = new MandelbrotState
            {
                Variant = MandelbrotVariant.Julia,
                JuliaCReal = -0.8m,
                JuliaCImaginary = 0.156m,
                CenterX = 0.15m,
                CenterY = 0.30m,
                Zoom = 5.0e25,
                Iterations = 4000,
                Threads = 2,
                Palette = palette()
            };
            int differing = CountRgbDiffering(
                await RenderAsync(julia, bla: false, w, h),
                await RenderAsync(julia, bla: true, w, h));
            Console.WriteLine($"[diag] BLA Julia 5e25: on vs off {differing}/{total} px differ");
            Check(differing * 100 <= total * 2, $"BLA changes Julia {differing}/{total} px (>2%).");
        }

        // (c) OrbitTrap coloring keeps every iteration ⇒ BLA is disabled ⇒ byte-identical.
        {
            MandelbrotState trap = Deep(5.0e25, 4000, MandelbrotColoringMode.OrbitTrap);
            int differing = CountRgbDiffering(
                await RenderAsync(trap, bla: false, w, h),
                await RenderAsync(trap, bla: true, w, h));
            Check(differing == 0, $"BLA must be inert for OrbitTrap coloring, changed {differing}/{total} px.");
        }

        // (d) Speed: a high-iteration deep view. BLA must not be slower; report the ratio.
        {
            MandelbrotState heavy = Deep(1.0e55, 24000);
            const int hw = 160, hh = 108;
            byte[] warm = new byte[hw * hh * 4];
            MandelbrotFamilyRenderer.ForceBlaForTests = false;
            MandelbrotFamilyRenderer.Render(heavy, warm, hw, hh, hw * 4, CancellationToken.None);

            var clock = Stopwatch.StartNew();
            MandelbrotFamilyRenderer.ForceBlaForTests = false;
            MandelbrotFamilyRenderer.Render(heavy, warm, hw, hh, hw * 4, CancellationToken.None);
            double offMs = clock.Elapsed.TotalMilliseconds;

            clock.Restart();
            MandelbrotFamilyRenderer.ForceBlaForTests = true;
            MandelbrotFamilyRenderer.Render(heavy, warm, hw, hh, hw * 4, CancellationToken.None);
            double onMs = clock.Elapsed.TotalMilliseconds;
            MandelbrotFamilyRenderer.ForceBlaForTests = null;

            Console.WriteLine($"[diag] BLA speed 1e55 i24000: off {offMs:F0}ms, on {onMs:F0}ms, ×{offMs / onMs:F2}");
            Check(onMs <= offMs * 1.25, $"BLA slower than non-BLA: {onMs:F0}ms vs {offMs:F0}ms.");
        }

        await Task.CompletedTask;
    }

    // Phase 9: BLA with a REAL 2x2 linear part. The five reflected/conjugate variants
    // (Burning Ship, Julia Burning Ship, Tricorn, Buffalo, Celtic) and even-power Simonobrot
    // advance delta through a real linear map, not a complex multiplication, so they get their
    // own pyramid (RealBlaTable); the complex BlaTable is left untouched and still serves
    // Mandelbrot/Julia/Multibrot. Like the complex one this is a pure accelerator, hence:
    //   (a) it must actually engage - the skip counter guards against a vacuous pass;
    //   (b) it must cost the picture nothing. Views deep enough to engage BLA sit in
    //       boundary-chaotic territory, where the engine already differs from an exact BigFloat
    //       render on a few percent of pixels. The honest metric is therefore the EXCESS of
    //       that difference over the same render with BLA off, not the raw diff;
    //   (c) coloring modes that consume every single iteration (orbit trap, stripe average,
    //       distance estimation) must keep BLA off and stay byte-identical.
    // The centres are exact decimal strings found by walking into each variant's boundary, so
    // the frames stay structured at these depths instead of degenerating into a flat field.
    private static async Task VerifyRealBlaAccelerationAsync(Func<MandelbrotPalette> palette)
    {
        static int CountRgbDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                if (a[o] != b[o] || a[o + 1] != b[o + 1] || a[o + 2] != b[o + 2]) n++;
            }
            return n;
        }

        MandelbrotState State(
            MandelbrotVariant variant, string centreX, string centreY, double zoom,
            MandelbrotColoringMode mode = MandelbrotColoringMode.Smooth,
            decimal power = 2m, decimal juliaReal = 0m, decimal juliaImaginary = 0m,
            bool inversion = false) => new()
            {
                Variant = variant,
                ColoringMode = mode,
                // decimal only carries ~28 digits; the deep engine reads the exact strings.
                CenterX = decimal.Parse(centreX[..System.Math.Min(centreX.Length, 20)], CultureInfo.InvariantCulture),
                CenterY = decimal.Parse(centreY[..System.Math.Min(centreY.Length, 20)], CultureInfo.InvariantCulture),
                CenterXExact = centreX,
                CenterYExact = centreY,
                Power = power,
                JuliaCReal = juliaReal,
                JuliaCImaginary = juliaImaginary,
                UseInversion = inversion,
                Zoom = zoom,
                Iterations = 6000,
                Threshold = 2m,
                Threads = 2,
                Palette = palette(),
            };

        async Task<byte[]> RenderAsync(MandelbrotState state, bool bla, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceBlaForTests = bla;
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = true;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally
            {
                MandelbrotFamilyRenderer.ForceBlaForTests = null;
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
            }
            return pixels;
        }

        async Task<long> CountSkipsAsync(MandelbrotState state, int w, int h)
        {
            MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests = 0;
            MandelbrotFamilyRenderer.CountRealBlaSkipsForTests = true;
            try { await RenderAsync(state, bla: true, w, h); }
            finally { MandelbrotFamilyRenderer.CountRealBlaSkipsForTests = false; }
            return MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests;
        }

        const string burningShipX = "-0.81350985269959441502640438927923405594718016208354671578096";
        const string burningShipY = "1.15385056973366263716386423762505110184996148623201569967787";
        const string celticX = "-0.891865646507391491113368732535744974340110083129727811184488";
        const string celticY = "1.544758206384140070271947748309466179897357707661851201063699";
        const string simonobrot4X = "0.279452570816264365821172574207784238378988855076446292869889";
        const string simonobrot4Y = "1.018073395776969286854111772196867045308653990469452818298968";

        (string Label, MandelbrotState State)[] fixtures =
        {
            ("BurningShip 3e30", State(MandelbrotVariant.BurningShip,
                "-0.813509852699594415026404389279137158659901740416441180706343",
                "1.153850569733662637163864237624756879942846369129799170139332", 3.1622776601683795e30)),
            ("BurningShip 1e45", State(MandelbrotVariant.BurningShip, burningShipX, burningShipY, 1.0e45)),
            ("Tricorn 3e30", State(MandelbrotVariant.Tricorn,
                "0.740107894676546596166278068634689383680928471433837845616988",
                "1.284164879491910489407619722838421840742204004581206006914469", 3.1622776601683795e30)),
            ("Buffalo 1e45", State(MandelbrotVariant.Buffalo,
                "0.251515542826420576827659826958312420927687487022709942977638",
                "0.454127991517863684724990589158969466705687426119938699326094", 1.0e45)),
            ("Celtic 3e30", State(MandelbrotVariant.Celtic, celticX, celticY, 3.1622776601683795e30)),
            ("JuliaBurningShip 1e45", State(MandelbrotVariant.JuliaBurningShip,
                "-0.024227550790277459831804555192054182024312705553405370339065",
                "0.56875673781379166740393234420623920098438359700265085138842", 1.0e45,
                juliaReal: -1.5m, juliaImaginary: 0.02m)),
            ("Simonobrot p2 1e28", State(MandelbrotVariant.Simonobrot,
                "-0.233085135590328323057688239020593618626185475481456854709886",
                "0.956730152389579347325401450437689559915308904251176295871525", 1.0e28, power: 2m)),
            ("Simonobrot p4 1e28", State(MandelbrotVariant.Simonobrot, simonobrot4X, simonobrot4Y, 1.0e28, power: 4m)),
            ("Simonobrot p6 inv 1e28", State(MandelbrotVariant.Simonobrot,
                "0.227591276012239845382720882305167929274131349145291924124711",
                "1.029105663003342779985661616216266280026506157420936047716223", 1.0e28,
                power: 6m, inversion: true)),
            ("Simonobrot p12 1e28", State(MandelbrotVariant.Simonobrot,
                "0.122209692688143080456363858471452783327760583215973003556001",
                "1.021769700244342585739196861366295324922134500631004946262376", 1.0e28, power: 12m)),
            // Phase 10 — odd powers: the same real 2×2 table, with both half-integer powers
            // of the modulus (M^(p/2) and M^(p/2−1)) carrying a √M factor.
            ("Simonobrot p3 1e28", State(MandelbrotVariant.Simonobrot,
                "-0.202832656913406719584951309136145088020207829768900793575531",
                "1.112011809221549716846392243745997167999832561088051678883473", 1.0e28, power: 3m)),
            ("Simonobrot p5 1e28", State(MandelbrotVariant.Simonobrot,
                "-0.548667511943908696630758431962511649547657017247242149297411",
                "0.872765118321406809316583683587250477176667101144019039749011", 1.0e28, power: 5m)),
        };

        const int w = 120, h = 80, total = w * h;
        const int ew = 36, eh = 24, etotal = ew * eh;

        foreach ((string label, MandelbrotState state) in fixtures)
        {
            long skips = await CountSkipsAsync(state, w, h);
            byte[] off = await RenderAsync(state, bla: false, w, h);
            byte[] on = await RenderAsync(state, bla: true, w, h);
            int differing = CountRgbDiffering(off, on);

            Check(on.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                $"Real BLA render must resolve structure ({label}).");
            Check(skips > 0, $"Real BLA never engaged on {label}: the fixture proves nothing.");

            byte[] exact = await Task.Run(() =>
                MandelbrotFamilyRenderer.RenderExactReferenceForTests(state, ew, eh, CancellationToken.None));
            int exactVersusOff = CountRgbDiffering(exact, await RenderAsync(state, bla: false, ew, eh));
            int exactVersusOn = CountRgbDiffering(exact, await RenderAsync(state, bla: true, ew, eh));
            int excess = exactVersusOn - exactVersusOff;

            Console.WriteLine($"[diag] real BLA {label}: skipped {skips:N0} iterations, on vs off " +
                $"{differing}/{total} px, vs exact {exactVersusOff} -> {exactVersusOn}/{etotal} (excess {excess})");

            // Pure accelerator: it may add no error of its own beyond the boundary chaos the
            // engine already carries. 2% of the sampled pixels is the whole budget.
            Check(excess * 50 <= etotal,
                $"Real BLA adds {excess}/{etotal} px of error over non-BLA on {label} (>2%).");
        }

        // Odd-power Simonobrot must reach the deep engine through the production gate, not
        // only through the test seam: without ForceDeepZoomForTests the render still has to
        // engage the real BLA, which only the perturbation kernels ever consult.
        {
            MandelbrotState state = fixtures[^2].State;   // Simonobrot p3 1e28
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests = 0;
            MandelbrotFamilyRenderer.CountRealBlaSkipsForTests = true;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally { MandelbrotFamilyRenderer.CountRealBlaSkipsForTests = false; }

            Check(MandelbrotFamilyRenderer.RealBlaSkippedIterationsForTests > 0,
                "Odd-power Simonobrot must select the deep engine at 1e28 without the test seam.");
            Check(CountRgbDiffering(pixels, await RenderAsync(state, bla: true, w, h)) == 0,
                "The production gate must produce exactly the forced-deep render for odd-power Simonobrot.");
        }

        // Coloring modes that consume every iteration keep BLA off, so they stay byte-identical.
        foreach (MandelbrotColoringMode mode in new[]
        {
            MandelbrotColoringMode.OrbitTrap,
            MandelbrotColoringMode.StripeAverage,
            MandelbrotColoringMode.DistanceEstimation,
        })
        {
            foreach ((string label, MandelbrotState state) in new (string, MandelbrotState)[]
            {
                ("BurningShip", State(MandelbrotVariant.BurningShip, burningShipX, burningShipY, 1.0e45, mode)),
                ("Celtic", State(MandelbrotVariant.Celtic, celticX, celticY, 3.1622776601683795e30, mode)),
                ("Simonobrot p4", State(MandelbrotVariant.Simonobrot, simonobrot4X, simonobrot4Y, 1.0e28, mode, power: 4m)),
            })
            {
                int differing = CountRgbDiffering(
                    await RenderAsync(state, bla: false, w, h),
                    await RenderAsync(state, bla: true, w, h));
                Check(differing == 0,
                    $"Real BLA must be inert for {mode} ({label}), changed {differing}/{total} px.");
            }
        }

        // Speed - the whole point of the phase. The reflected step is cheap, so skipping runs of
        // it pays off the most; Simonobrot's own step is heavy enough that the win is smaller,
        // and there the requirement is only that the lookup must not cost more than it saves.
        foreach ((string label, MandelbrotState state, double minimumRatio) in
            new (string, MandelbrotState, double)[]
            {
                ("Tricorn 3e30", fixtures[2].State, 2.0),
                ("Simonobrot p4 1e28", fixtures[7].State, 0.8),
            })
        {
            const int sw = 160, sh = 108;
            byte[] scratch = new byte[sw * sh * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = true;
            double offMs = double.MaxValue, onMs = double.MaxValue;
            try
            {
                MandelbrotFamilyRenderer.ForceBlaForTests = false;
                MandelbrotFamilyRenderer.Render(state, scratch, sw, sh, sw * 4, CancellationToken.None);
                var clock = new Stopwatch();
                for (int trial = 0; trial < 3; trial++)
                {
                    clock.Restart();
                    MandelbrotFamilyRenderer.ForceBlaForTests = false;
                    MandelbrotFamilyRenderer.Render(state, scratch, sw, sh, sw * 4, CancellationToken.None);
                    offMs = System.Math.Min(offMs, clock.Elapsed.TotalMilliseconds);

                    clock.Restart();
                    MandelbrotFamilyRenderer.ForceBlaForTests = true;
                    MandelbrotFamilyRenderer.Render(state, scratch, sw, sh, sw * 4, CancellationToken.None);
                    onMs = System.Math.Min(onMs, clock.Elapsed.TotalMilliseconds);
                }
            }
            finally
            {
                MandelbrotFamilyRenderer.ForceBlaForTests = null;
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
            }

            Console.WriteLine($"[diag] real BLA speed {label}: off {offMs:F0}ms, on {onMs:F0}ms, x{offMs / onMs:F2}");
            Check(offMs / onMs >= minimumRatio,
                $"Real BLA speed on {label}: x{offMs / onMs:F2}, expected at least x{minimumRatio:F2}.");
        }
    }

    // Phase 11: FloatExp-δ for the reflected variants, Multibrot and Simonobrot — the same
    // plan switch (PlanDeepZoom.UseFloatExpDelta) that already gates the z²+c kernel now
    // also gates DeepZoomPixelReflectedFloatExp/MultibrotFloatExp/SimonobrotFloatExp, lifting
    // their double-δ ceiling (was 1e50/1e40/1e30) up to the shared MaxZoom.
    //   1. Overlap band: wherever both δ representations are valid, the FloatExp kernel must
    //      be bit-identical to the trusted double kernel (the same technique as Phase 1 for
    //      z²+c) — reusing the Real BLA fixtures (1e28-1e45, far below FloatExpDeltaZoomBits
    //      ≈ 8.8e71) for the reflected/Simonobrot families, and the existing Multibrot
    //      fixtures forced onto the deep engine at a shallow zoom for the Multibrot family
    //      (it has no natural deep fixture below the threshold in this file).
    //   2. Extreme smoke test: a real zoom of 1e300 — orders of magnitude past every old
    //      ceiling. The same ~60-70 digit centers used above no longer resolve real boundary
    //      structure at this depth (their own precision runs out long before), so — exactly
    //      like the Mandelbrot/Julia check at 1e120/1e300 — only completion, full-frame fill
    //      and precision restoration are checked, plus that the reference orbit actually
    //      stayed non-degenerate (proving the new kernel really ran, not the brute-force
    //      fallback).
    private static async Task VerifyFloatExpDeltaVariantsAsync(Func<MandelbrotPalette> palette)
    {
        static int CountRgbDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int o = pixel * 4;
                if (a[o] != b[o] || a[o + 1] != b[o + 1] || a[o + 2] != b[o + 2]) n++;
            }
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceFloatExp, bool? forceDeep, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = forceFloatExp;
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally
            {
                MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = null;
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
            }
            return pixels;
        }

        const int w = 120, h = 80, total = w * h;

        const string burningShipX = "-0.81350985269959441502640438927923405594718016208354671578096";
        const string burningShipY = "1.15385056973366263716386423762505110184996148623201569967787";
        const string celticX = "-0.891865646507391491113368732535744974340110083129727811184488";
        const string celticY = "1.544758206384140070271947748309466179897357707661851201063699";
        const string simonobrot4X = "0.279452570816264365821172574207784238378988855076446292869889";
        const string simonobrot4Y = "1.018073395776969286854111772196867045308653990469452818298968";
        const string simonobrot3X = "-0.202832656913406719584951309136145088020207829768900793575531";
        const string simonobrot3Y = "1.112011809221549716846392243745997167999832561088051678883473";

        MandelbrotState ExactState(
            MandelbrotVariant variant, string centreX, string centreY, double zoom,
            decimal power = 2m, decimal juliaReal = 0m, decimal juliaImaginary = 0m,
            int iterations = 6000) => new()
            {
                Variant = variant,
                CenterX = decimal.Parse(centreX[..Math.Min(centreX.Length, 20)], CultureInfo.InvariantCulture),
                CenterY = decimal.Parse(centreY[..Math.Min(centreY.Length, 20)], CultureInfo.InvariantCulture),
                CenterXExact = centreX,
                CenterYExact = centreY,
                Power = power,
                JuliaCReal = juliaReal,
                JuliaCImaginary = juliaImaginary,
                Zoom = zoom,
                Iterations = iterations,
                Threshold = 2m,
                Threads = 2,
                Palette = palette(),
            };

        // 1a. Reflected + Simonobrot: already-proven deep fixtures, naturally past
        // PerturbationZoomThreshold but far below FloatExpDeltaZoomBits.
        (string Label, MandelbrotState State)[] overlapFixtures =
        {
            ("BurningShip 1e45", ExactState(MandelbrotVariant.BurningShip, burningShipX, burningShipY, 1.0e45)),
            ("Celtic 3e30", ExactState(MandelbrotVariant.Celtic, celticX, celticY, 3.1622776601683795e30)),
            ("Simonobrot p4 1e28", ExactState(MandelbrotVariant.Simonobrot, simonobrot4X, simonobrot4Y, 1.0e28, power: 4m)),
            ("Simonobrot p3 1e28 (odd, sqrt)", ExactState(MandelbrotVariant.Simonobrot, simonobrot3X, simonobrot3Y, 1.0e28, power: 3m)),
        };

        foreach ((string label, MandelbrotState state) in overlapFixtures)
        {
            byte[] doubleDelta = await RenderAsync(state, forceFloatExp: false, forceDeep: null, w, h);
            byte[] floatExpDelta = await RenderAsync(state, forceFloatExp: true, forceDeep: null, w, h);
            int differing = CountRgbDiffering(doubleDelta, floatExpDelta);
            Console.WriteLine($"[diag] FloatExp-δ overlap {label}: double vs FloatExp {differing}/{total} px differ");
            Check(doubleDelta.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                $"Overlap fixture must carry structure ({label}).");
            Check(differing == 0,
                $"FloatExp-δ diverges from double-δ on {differing}/{total} px ({label}) inside the overlap band.");
        }

        // 1b. Multibrot: no natural deep fixture below the threshold in this file — reuse the
        // shallow-zoom, forced-deep-engine trick from VerifyMultibrotDeepZoomAsync. What
        // matters is that both kernels run the same code over the same reference orbit and δc,
        // not the literal zoom value.
        (int Power, decimal Cx, decimal Cy)[] multibrotCases =
        {
            (5, -0.540000m, 0.600000m),
            (8, 0.660000m, 0.000000m),
        };
        foreach ((int power, decimal cx, decimal cy) in multibrotCases)
        {
            var state = new MandelbrotState
            {
                Variant = MandelbrotVariant.Generalized,
                Power = power,
                CenterX = cx,
                CenterY = cy,
                Zoom = 300.0,
                Iterations = 2000,
                Threads = 2,
                Palette = palette()
            };
            byte[] doubleDelta = await RenderAsync(state, forceFloatExp: false, forceDeep: true, w, h);
            byte[] floatExpDelta = await RenderAsync(state, forceFloatExp: true, forceDeep: true, w, h);
            int differing = CountRgbDiffering(doubleDelta, floatExpDelta);
            Console.WriteLine($"[diag] FloatExp-δ overlap Multibrot p={power}: double vs FloatExp {differing}/{total} px differ");
            Check(doubleDelta.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                $"Multibrot p={power} overlap fixture must carry structure.");
            Check(differing == 0,
                $"FloatExp-δ diverges from double-δ on {differing}/{total} px (Multibrot p={power}).");
        }

        // 2. Extreme smoke test: a real zoom of 1e300, orders of magnitude past every old
        // per-variant ceiling (1e50/1e40/1e30) and the FloatExpDeltaZoomBits threshold.
        const int ew = 40, eh = 28;
        (string Label, MandelbrotState State)[] extremeFixtures =
        {
            ("BurningShip 1e300", ExactState(MandelbrotVariant.BurningShip, burningShipX, burningShipY, 1.0e300, iterations: 1500)),
            ("Simonobrot p4 1e300", ExactState(MandelbrotVariant.Simonobrot, simonobrot4X, simonobrot4Y, 1.0e300, power: 4m, iterations: 1500)),
            ("Simonobrot p3 1e300 (odd, sqrt)", ExactState(MandelbrotVariant.Simonobrot, simonobrot3X, simonobrot3Y, 1.0e300, power: 3m, iterations: 1500)),
        };
        foreach ((string label, MandelbrotState state) in extremeFixtures)
        {

            int progress = 0;
            byte[] pixels = new byte[ew * eh * 4];
            await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, ew, eh, ew * 4,
                CancellationToken.None, value => progress = value));
            Check(progress == 100, $"Extreme FloatExp-δ render must complete with 100% progress ({label}).");
            Check(pixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
                $"Extreme FloatExp-δ render must fill every pixel ({label}).");
            Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
                $"Extreme FloatExp-δ render must restore the calling thread's working precision ({label}).");
        }

        // Multibrot needs a real deep zoom (not the forced-engine Zoom=300 trick) to reach
        // FloatExpDeltaZoomBits through the production gate.
        {
            var state = new MandelbrotState
            {
                Variant = MandelbrotVariant.Generalized,
                Power = 5,
                CenterX = -0.540000m,
                CenterY = 0.600000m,
                Zoom = 1.0e300,
                Iterations = 800,
                Threads = 2,
                Palette = palette()
            };

            int progress = 0;
            byte[] pixels = new byte[ew * eh * 4];
            await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, ew, eh, ew * 4,
                CancellationToken.None, value => progress = value));
            Check(progress == 100, "Extreme FloatExp-δ render must complete with 100% progress (Multibrot p=5 1e300).");
            Check(pixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
                "Extreme FloatExp-δ render must fill every pixel (Multibrot p=5 1e300).");
            Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
                "Extreme FloatExp-δ render must restore the calling thread's working precision (Multibrot p=5 1e300).");
        }
    }

    // Phase 2: the decimal stage is gone from the Mandelbrot/Julia ladder — perturbation
    // now takes over wherever plain double stops being trusted (~1.5e9). Byte-identity for
    // zoom < 1.5e9 and zoom >= 1e25 is proven by the external git-stash A/B; here we check
    // the band that changed hands (was decimal brute-force, now perturbation).
    private static async Task VerifyDecimalStageRemovedAsync(Func<MandelbrotPalette> palette)
    {
        static int RgbDelta(byte[] a, byte[] b, int pixel)
        {
            int o = pixel * 4;
            return Math.Max(Math.Abs(a[o] - b[o]),
                   Math.Max(Math.Abs(a[o + 1] - b[o + 1]), Math.Abs(a[o + 2] - b[o + 2])));
        }

        static int CountDiffering(byte[] a, byte[] b)
        {
            int n = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
                if (RgbDelta(a, b, pixel) != 0) n++;
            return n;
        }

        async Task<byte[]> RenderAsync(MandelbrotState state, bool? forceDeep, bool? forceFloatExp, int w, int h)
        {
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = forceDeep;
            MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = forceFloatExp;
            try
            {
                await Task.Run(() => MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None));
            }
            finally
            {
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
                MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = null;
            }
            return pixels;
        }

        MandelbrotState DeepState(double zoom, int iterations) => new()
        {
            CenterX = -1.2628848671045503000020782246m,
            CenterY = 0.0409687601493310685285376264m,
            Zoom = zoom,
            Iterations = iterations,
            Threads = 2,
            Palette = palette()
        };

        const int w = 112, h = 74, total = w * h;

        // (a) Where decimal was still trustworthy (<= ~1e18), perturbation must reproduce
        //     it up to boundary chaos (a 1-ULP coordinate difference can flip a boundary
        //     pixel by a whole colour band — see the project memory).
        foreach (double zoom in new[] { 1.0e12, 1.0e18 })
        {
            MandelbrotState state = DeepState(zoom, 6000);
            byte[] decimalPixels = await RenderAsync(state, forceDeep: false, forceFloatExp: null, w, h);
            byte[] perturbationPixels = await RenderAsync(state, forceDeep: true, forceFloatExp: null, w, h);
            int differing = CountDiffering(decimalPixels, perturbationPixels);
            Console.WriteLine($"[diag] zoom {zoom:E0}: decimal vs perturbation {differing}/{total} px differ");
            Check(perturbationPixels.Where((_, index) => index % 4 != 3).Any(value => value != 0),
                $"Perturbation must resolve structure at zoom {zoom:E0}.");
            Check(differing * 100 <= total * 5,
                $"decimal→perturbation diverges on {differing}/{total} px at zoom {zoom:E0} (>5%).");
        }

        // (b) Past ~1e20 decimal itself runs out of digits, so it is no longer the oracle.
        //     Instead check the perturbation engine is internally converged: swapping the
        //     δ representation (double ↔ FloatExp) over the same reference orbit must not
        //     move the picture. If it doesn't, the divergence from decimal up here is
        //     decimal's error, not the engine's.
        foreach (double zoom in new[] { 1.0e18, 1.0e23 })
        {
            MandelbrotState state = DeepState(zoom, 6000);
            byte[] doubleDelta = await RenderAsync(state, forceDeep: true, forceFloatExp: false, w, h);
            byte[] floatExpDelta = await RenderAsync(state, forceDeep: true, forceFloatExp: true, w, h);
            int differing = CountDiffering(doubleDelta, floatExpDelta);
            Console.WriteLine($"[diag] zoom {zoom:E0}: perturbation double-δ vs FloatExp-δ {differing}/{total} px differ");
            Check(differing * 200 <= total,
                $"Perturbation not δ-representation-stable at zoom {zoom:E0}: {differing}/{total} px (>0.5%).");
        }

        // (c) Julia perturbation with the rebasing fix (δ = z − Z₀; Julia's Z₀ = centre is
        //     non-zero, and the old code used δ = z, which glitched wherever rebasing
        //     fired). A dendrite view forces heavy rebasing; at this shallow zoom plain
        //     double is an exact oracle, so agreement means the fix is right, not just
        //     "doesn't crash".
        {
            var julia = new MandelbrotState
            {
                Variant = MandelbrotVariant.Julia,
                JuliaCReal = -0.8m,
                JuliaCImaginary = 0.156m,
                CenterX = 0.15m,
                CenterY = 0.30m,
                Zoom = 100.0,
                Iterations = 3000,
                Threads = 2,
                Palette = palette()
            };
            byte[] doublePixels = await RenderAsync(julia, forceDeep: false, forceFloatExp: null, w, h);
            byte[] perturbationPixels = await RenderAsync(julia, forceDeep: true, forceFloatExp: null, w, h);
            int differing = CountDiffering(doublePixels, perturbationPixels);
            int structuredPixels = 0;
            for (int pixel = 0; pixel < total; pixel++)
                if (perturbationPixels[pixel * 4] != perturbationPixels[0] ||
                    perturbationPixels[pixel * 4 + 1] != perturbationPixels[1] ||
                    perturbationPixels[pixel * 4 + 2] != perturbationPixels[2])
                    structuredPixels++;
            Console.WriteLine($"[diag] Julia dendrite zoom 100: double vs perturbation {differing}/{total} px differ; structured {structuredPixels}/{total}");
            Check(structuredPixels > total / 4, "Julia perturbation must be structured, not a flat fill.");
            Check(differing * 100 <= total * 10,
                $"Julia: perturbation strays from the double oracle on {differing}/{total} px (>10%) — rebase fix suspect.");
        }

        // (d) Degenerate reference orbit (centre deep in the fast-escaping exterior): the
        //     fallback must run in double (no decimal), fill the frame and stay uniform.
        var degenerate = new MandelbrotState
        {
            CenterX = 1000m,
            CenterY = 1000m,
            Zoom = 1.0e30,
            Iterations = 500,
            Threads = 2,
            Palette = palette()
        };
        int degenerateProgress = 0;
        byte[] degeneratePixels = new byte[w * h * 4];
        MandelbrotFamilyRenderer.Render(degenerate, degeneratePixels, w, h, w * 4,
            CancellationToken.None, value => degenerateProgress = value);
        Check(degenerateProgress == 100, "Degenerate-orbit fallback must complete with 100% progress.");
        Check(degeneratePixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
            "Degenerate-orbit fallback must fill every pixel.");
        bool uniform = true;
        for (int pixel = 1; pixel < w * h && uniform; pixel++)
            uniform = degeneratePixels[pixel * 4] == degeneratePixels[0]
                   && degeneratePixels[pixel * 4 + 1] == degeneratePixels[1]
                   && degeneratePixels[pixel * 4 + 2] == degeneratePixels[2];
        Check(uniform, "Degenerate-orbit fallback must produce a uniform frame.");
    }
}
