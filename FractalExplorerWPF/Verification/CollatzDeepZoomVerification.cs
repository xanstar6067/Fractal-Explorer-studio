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

// Глубокий зум комплексного Коллатца.
internal static partial class Program
{
    // Centres found by descending on edge density (a frame far outside the set comes out
    // uniformly non-black and would pass a "has content" check while showing nothing).
    private static readonly (double Zoom, string CenterX, string CenterY)[] DeepCollatzCentres =
    [
        (1.1e12, "-0.869177864622138448380772548135348733365049368",
                 "0.003351110447198153027105397611632949120554176"),
        (1.1e15, "-0.869177864620624139702320622587697311553355236",
                 "0.003351110446321172760920851431301963941719519"),
        (1.1e18, "-0.869177864620622627578561083124457777168012231",
                 "0.00335111044632155045879382890961167857526658"),
    ];

    // Phase 11: Collatz gained a third precision stage — direct iteration in BigFloat.
    // Unlike the Mandelbrot family this is not perturbation: the formula is transcendental
    // (cos πz), its derivative is tens per step, so δ from a reference orbit reaches the
    // size of the orbit within a couple of dozen iterations and there is nothing to rebase
    // onto. The stage engages above zoom 1e10, which is where the old ladder actually broke
    // — both the double and the decimal path computed cos/sin in double, so decimal only
    // ever raised the precision of the coordinates, never of the formula.
    private static async Task VerifyCollatzDeepZoomAsync()
    {
        static MandelbrotPalette Palette() => new()
        {
            Colors = [Colors.White, Colors.Black],
            InteriorColor = Colors.Black,
            IsGradient = true
        };

        static CollatzState View(double centerX, double centerY, double zoom,
            CollatzVariation variation, CollatzColoringMode coloring, int iterations = 150) => new()
        {
            CenterX = (decimal)centerX,
            CenterY = (decimal)centerY,
            Zoom = zoom,
            Iterations = iterations,
            Threshold = 100m,
            Variation = variation,
            ColoringMode = coloring,
            PParameter = 3m,
            QRealParameter = 0.2m,
            QImaginaryParameter = -0.1m,
            UseSmoothColoring = true,
            OrbitDensitySampleStep = 2,
            Palette = Palette()
        };

        static CollatzState Exact(double zoom, string centerX, string centerY,
            CollatzVariation variation = CollatzVariation.Standard,
            CollatzColoringMode coloring = CollatzColoringMode.EscapeTime)
        {
            CollatzState state = View(0, 0, zoom, variation, coloring);
            state.CenterXExact = centerX;
            state.CenterYExact = centerY;
            state.CenterX = BigFloat.Parse(centerX).ToDecimalClamped();
            state.CenterY = BigFloat.Parse(centerY).ToDecimalClamped();
            return state;
        }

        static int CountDiffering(byte[] a, byte[] b)
        {
            int differing = 0;
            for (int pixel = 0; pixel * 4 < a.Length; pixel++)
            {
                int offset = pixel * 4;
                if (a[offset] != b[offset] || a[offset + 1] != b[offset + 1] ||
                    a[offset + 2] != b[offset + 2]) differing++;
            }
            return differing;
        }

        // Neighbouring pixels that differ — the frame really shows structure rather than a
        // uniform fill. Counting non-black pixels is not enough: the verification palette is
        // white→black with a black interior, so a frame far outside the set comes out fully
        // non-black and tells nothing (the lesson from the Simonobrot fixtures).
        static int CountEdges(byte[] pixels, int width)
        {
            int edges = 0;
            int rows = pixels.Length / 4 / width;
            for (int y = 0; y < rows; y++)
            for (int x = 1; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                if (pixels[offset] != pixels[offset - 4] || pixels[offset + 1] != pixels[offset - 3] ||
                    pixels[offset + 2] != pixels[offset - 2]) edges++;
            }
            return edges;
        }

        async Task<byte[]> RenderAsync(CollatzState state, bool? forceBigFloat, int width, int height,
            int? forcePrecisionBits = null)
        {
            byte[] pixels = new byte[width * height * 4];
            CollatzRenderer.ForceBigFloatForTests = forceBigFloat;
            CollatzRenderer.ForcePrecisionBitsForTests = forcePrecisionBits;
            try
            {
                await Task.Run(() => CollatzRenderer.Render(state, pixels, width, height, width * 4, 4,
                    CancellationToken.None));
            }
            finally
            {
                CollatzRenderer.ForceBigFloatForTests = null;
                CollatzRenderer.ForcePrecisionBitsForTests = null;
            }
            return pixels;
        }

        const int w = 64, h = 44, total = w * h;

        // 1. Where double is still trustworthy, the BigFloat stage must reproduce it. Run
        //    every variation against every coloring mode: each mode reads a different set of
        //    orbit metrics, and each variation a different branch of the formula. This is
        //    the check that the formula, the escape tests and every metric were transcribed
        //    correctly — the double path shares no code with the BigFloat one.
        var variations = new[]
        {
            CollatzVariation.Standard, CollatzVariation.SineVariation,
            CollatzVariation.ParityBranchVariation, CollatzVariation.GeneralizedP,
            CollatzVariation.GeneralizedPQ
        };
        var colorings = new[]
        {
            CollatzColoringMode.EscapeTime, CollatzColoringMode.FinalArgument,
            CollatzColoringMode.FinalMagnitude, CollatzColoringMode.CycleBasins,
            CollatzColoringMode.IntegerTrap, CollatzColoringMode.RealAxisTrap,
            CollatzColoringMode.OrbitDensity, CollatzColoringMode.PeriodDetection
        };
        int worstDiffering = 0;
        string worstLabel = "";
        foreach (CollatzVariation variation in variations)
        foreach (CollatzColoringMode coloring in colorings)
        {
            CollatzState state = View(0.5623, 0, 5000, variation, coloring,
                coloring == CollatzColoringMode.OrbitDensity ? 60 : 150);
            byte[] shallow = await RenderAsync(state, false, w, h);
            byte[] deep = await RenderAsync(state, true, w, h);
            int differing = CountDiffering(shallow, deep);
            if (differing > worstDiffering)
            {
                worstDiffering = differing;
                worstLabel = $"{variation}/{coloring}";
            }
            Check(differing * 100 <= total * 6,
                $"BigFloat stage diverges from the double stage on {differing}/{total} px " +
                $"for {variation}/{coloring} (>6%).");
        }
        Console.WriteLine($"[diag] Collatz BigFloat vs double @zoom 5e3: worst {worstDiffering}/{total} " +
                          $"({100.0 * worstDiffering / total:F2}%) at {worstLabel}");

        // 2. The same at a zoom where double is near its limit. A larger drift is expected
        //    here, and it is double's: the BigFloat stage carries ~50 spare bits there.
        CollatzState nearLimit = View(0.5623, 0, 1e8, CollatzVariation.Standard,
            CollatzColoringMode.EscapeTime);
        int nearLimitDiffering = CountDiffering(await RenderAsync(nearLimit, false, w, h),
            await RenderAsync(nearLimit, true, w, h));
        Console.WriteLine($"[diag] Collatz BigFloat vs double @zoom 1e8: {nearLimitDiffering}/{total} " +
                          $"({100.0 * nearLimitDiffering / total:F2}%)");
        Check(nearLimitDiffering * 100 <= total * 25,
            $"BigFloat stage diverges from double at 1e8 on {nearLimitDiffering}/{total} px (>25%).");

        // 3. Deep frames must complete, fill every pixel, still show structure, and leave the
        //    calling thread's working precision alone. And — the only check of the precision
        //    plan itself that needs no external oracle — the planned precision must give the
        //    same frame as a deliberately excessive one.
        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Working precision must start at the minimum.");
        foreach ((double zoom, string centerX, string centerY) in DeepCollatzCentres)
        {
            CollatzState deep = Exact(zoom, centerX, centerY);
            var watch = Stopwatch.StartNew();
            byte[] planned = await RenderAsync(deep, null, w, h);
            watch.Stop();
            int plannedBits = CollatzRenderer.PlanPrecisionBits(deep);
            byte[] generous = await RenderAsync(deep, null, w, h, plannedBits + 256);
            int edges = CountEdges(planned, w);
            int drift = CountDiffering(planned, generous);
            Console.WriteLine($"[diag] Collatz deep {zoom:E1}: {plannedBits} bits, " +
                              $"{watch.Elapsed.TotalMilliseconds:F0} ms for {w}×{h}, edges {edges}, " +
                              $"drift vs +256 bits {drift}/{total}");
            Check(planned.Where((_, index) => index % 4 == 3).All(value => value == 255),
                $"Deep Collatz render at {zoom:E1} left pixels unfilled.");
            Check(edges >= 40, $"Deep Collatz render at {zoom:E1} shows no structure (edges {edges}).");
            Check(drift * 100 <= total * 2,
                $"The precision plan is short at {zoom:E1}: {drift}/{total} px change when given 256 more bits.");
        }

        // 4. Past the deepest centre we have, structure is not guaranteed — but the stage
        //    must still run to completion at the zoom ceiling the window allows.
        foreach (double zoom in new[] { 1e30, 1e50 })
        {
            CollatzState extreme = Exact(zoom, DeepCollatzCentres[^1].CenterX, DeepCollatzCentres[^1].CenterY);
            var watch = Stopwatch.StartNew();
            byte[] pixels = await RenderAsync(extreme, null, w, h);
            watch.Stop();
            Console.WriteLine($"[diag] Collatz extreme {zoom:E0}: " +
                              $"{CollatzRenderer.PlanPrecisionBits(extreme)} bits, " +
                              $"{watch.Elapsed.TotalMilliseconds:F0} ms for {w}×{h}");
            Check(pixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
                $"Collatz render at {zoom:E0} left pixels unfilled.");
        }
        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Deep Collatz render must restore the calling thread's working precision.");

        // 5. The tile path and the full-frame path must agree pixel for pixel: the window
        //    renders tiles, the exporter renders whole frames.
        CollatzState tiled = Exact(DeepCollatzCentres[^1].Zoom, DeepCollatzCentres[^1].CenterX,
            DeepCollatzCentres[^1].CenterY);
        byte[] full = await RenderAsync(tiled, null, w, h);
        var tile = new MandelbrotRenderTile(16, 12, 32, 20, 1, 1);
        byte[]? tilePixels = await Task.Run(() =>
            CollatzRenderer.RenderTile(tiled, w, h, tile, CancellationToken.None));
        Check(tilePixels is not null, "Deep Collatz tile render returned null without cancellation.");
        int tileDiffering = 0;
        for (int y = 0; y < tile.Height; y++)
        for (int x = 0; x < tile.Width; x++)
        {
            int tileOffset = (y * tile.Width + x) * 4;
            int frameOffset = ((tile.Y + y) * w + tile.X + x) * 4;
            if (tilePixels![tileOffset] != full[frameOffset] ||
                tilePixels[tileOffset + 1] != full[frameOffset + 1] ||
                tilePixels[tileOffset + 2] != full[frameOffset + 2]) tileDiffering++;
        }
        Check(tileDiffering == 0,
            $"Deep Collatz tile disagrees with the full frame on {tileDiffering} px.");

        // 6. The exact centre must actually reach the renderer: a shift of a tenth of a pixel
        //    at 1e18 is far below what the decimal centre can hold, so if the exact strings
        //    were being ignored the frame would not move at all. And an exact centre equal to
        //    the decimal one must change nothing.
        CollatzState shifted = Exact(DeepCollatzCentres[^1].Zoom,
            ShiftCentre(DeepCollatzCentres[^1].CenterX, DeepCollatzCentres[^1].Zoom),
            DeepCollatzCentres[^1].CenterY);
        Check(CountDiffering(full, await RenderAsync(shifted, null, w, h)) > 0,
            "A sub-decimal shift of the exact centre changed nothing — the exact centre is ignored.");

        CollatzState plain = View(0.5623, 0, 1e12, CollatzVariation.Standard, CollatzColoringMode.EscapeTime);
        CollatzState mirrored = View(0.5623, 0, 1e12, CollatzVariation.Standard, CollatzColoringMode.EscapeTime);
        mirrored.CenterXExact = "0.5623";
        mirrored.CenterYExact = "0";
        Check(CountDiffering(await RenderAsync(plain, null, w, h),
                  await RenderAsync(mirrored, null, w, h)) == 0,
            "An exact centre equal to the decimal centre must render identically.");

        // 7. Determinism: the stage is parallel over rows and keeps per-thread state (the
        //    orbit history buffer and the working precision).
        CollatzState repeat = Exact(DeepCollatzCentres[0].Zoom, DeepCollatzCentres[0].CenterX,
            DeepCollatzCentres[0].CenterY, CollatzVariation.GeneralizedPQ,
            CollatzColoringMode.CycleBasins);
        Check(CountDiffering(await RenderAsync(repeat, null, w, h),
                  await RenderAsync(repeat, null, w, h)) == 0,
            "Two identical deep Collatz renders differ — the stage is not deterministic.");

        // 8. The precision plan must grow with depth and never drop below the floor.
        int previousBits = 0;
        foreach (double zoom in new[] { 1e10, 1e15, 1e20, 1e30, 1e40, 1e50 })
        {
            int bits = CollatzRenderer.PlanPrecisionBits(View(0, 0, zoom, CollatzVariation.Standard,
                CollatzColoringMode.EscapeTime));
            Check(bits >= 128 && bits >= previousBits,
                $"Precision plan is not monotonic: {bits} bits at zoom {zoom:E0} after {previousBits}.");
            previousBits = bits;
        }
    }

    // Moves an exact centre by about a tenth of a pixel at the given zoom — a difference the
    // decimal centre cannot represent at these depths.
    private static string ShiftCentre(string centre, double zoom)
    {
        using var precision = new BigFloat.PrecisionScope(1024);
        BigFloat step = BigFloat.FromInt(4) / BigFloat.FromDouble(zoom) / 640;
        return (BigFloat.Parse(centre) + step).ToInvariantString();
    }
}
