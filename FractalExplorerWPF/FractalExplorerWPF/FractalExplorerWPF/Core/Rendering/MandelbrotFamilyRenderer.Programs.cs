using System.Globalization;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public static partial class MandelbrotFamilyRenderer
{
    private sealed record ProgramReferenceStep(FoldedPolynomialProgram Program, FloatExp[] Values,
        FloatExp X, FloatExp Y, FloatExp OutputX, FloatExp OutputY);
    private sealed record ProgramContext(FoldedPolynomialProgram[] Cycle, ProgramReferenceStep[] Orbit,
        ProgramReferenceStep[] Anchors, FloatExp CenterX, FloatExp CenterY, bool Deep, RealBlaTable? Bla);
    private static readonly Dictionary<string, ProgramContext> ProgramContexts = [];
    private static readonly object ProgramContextGate = new();
    private static FoldedPolynomialProgram[] ProgramCycle(MandelbrotState state)
    {
        if (!FoldedFormulaCatalog.IsHybrid(state.Variant)) return [FoldedPolynomialProgram.For(state.Variant)];
        state.Hybrid.Validate();
        return state.Hybrid.Steps.SelectMany(s => Enumerable.Repeat(FoldedPolynomialProgram.For(s.Formula, s.Power), s.Repeats)).ToArray();
    }
    private static ProgramContext GetProgramContext(MandelbrotState state, CancellationToken token)
    {
        bool deep = ForceDeepZoomForTests ?? state.Zoom > PerturbationZoomThreshold;
        int bits = PlanDeepZoom(state).ReferenceBits;
        string key = FormattableString.Invariant($"{state.Variant}|{state.CenterXExact ?? state.CenterX.ToString(CultureInfo.InvariantCulture)}|{state.CenterYExact ?? state.CenterY.ToString(CultureInfo.InvariantCulture)}|{state.JuliaCReal}|{state.JuliaCImaginary}|{state.Iterations}|{bits}|{state.Threshold}|{state.Zoom}|{deep}") +
            (FoldedFormulaCatalog.IsHybrid(state.Variant) ? string.Join(';', state.Hybrid.Steps.Select(s => $"{(int)s.Formula},{s.Power},{s.Repeats}")) : "");
        lock (ProgramContextGate) if (ProgramContexts.TryGetValue(key, out var cached)) return cached;
        var cycle = ProgramCycle(state);
        using var precision = new BigFloat.PrecisionScope(bits);
        BigFloat cx = BigFloat.Parse(state.CenterXExact ?? state.CenterX.ToString(CultureInfo.InvariantCulture));
        BigFloat cy = BigFloat.Parse(state.CenterYExact ?? state.CenterY.ToString(CultureInfo.InvariantCulture));
        bool julia = IsJuliaVariant(state.Variant);
        BigFloat cr = julia ? BigFloat.FromDecimal(state.JuliaCReal) : cx;
        BigFloat ci = julia ? BigFloat.FromDecimal(state.JuliaCImaginary) : cy;
        BigFloat seedX = julia ? cx : BigFloat.Zero, seedY = julia ? cy : BigFloat.Zero;
        ProgramReferenceStep Make(FoldedPolynomialProgram program, BigFloat x, BigFloat y)
        {
            var values = new BigFloat[program.Nodes.Length];
            var output = program.EvaluateBig(x, y, values);
            return new(program, values.Select(FloatExp.FromBigFloat).ToArray(), FloatExp.FromBigFloat(x), FloatExp.FromBigFloat(y),
                FloatExp.FromBigFloat(output.Real + cr), FloatExp.FromBigFloat(output.Imaginary + ci));
        }
        var anchors = deep ? cycle.Distinct().ToDictionary(p => p, p => Make(p, seedX, seedY)) : [];
        var orbit = new List<ProgramReferenceStep>();
        if (deep)
        {
            BigFloat zx = seedX, zy = seedY;
            int limit = System.Math.Min(state.Iterations, 16384);
            double maximum = System.Math.Max(16, (double)state.Threshold * (double)state.Threshold * 4);
            for (int n = 0; n < limit; n++)
            {
                token.ThrowIfCancellationRequested();
                var program = cycle[n % cycle.Length];
                var step = Make(program, zx, zy); orbit.Add(step);
                var big = new BigFloat[program.Nodes.Length];
                var next = program.EvaluateBig(zx, zy, big); zx = next.Real + cr; zy = next.Imaginary + ci;
                double x = zx.ToDouble(), y = zy.ToDouble();
                if (!double.IsFinite(x * x + y * y) || x * x + y * y > maximum) break;
            }
        }
        var orbitArray = orbit.ToArray();
        RealBlaTable? bla = deep && orbitArray.Length >= 4 ? RealBlaTable.Build(
            orbitArray.Select(s => s.X.ToDouble()).ToArray(), orbitArray.Select(s => s.Y.ToDouble()).ToArray(),
            orbitArray.Length, julia, (double)(state.Threshold * state.Threshold), 30.0 / state.Zoom,
            null, 0, programs: orbitArray) : null;
        var result = new ProgramContext(cycle, orbitArray, deep ? cycle.Select(p => anchors[p]).ToArray() : [],
            FloatExp.FromBigFloat(cx), FloatExp.FromBigFloat(cy), deep, bla);
        token.ThrowIfCancellationRequested();
        lock (ProgramContextGate)
        {
            if (ProgramContexts.Count >= 8) ProgramContexts.Remove(ProgramContexts.Keys.First());
            ProgramContexts[key] = result;
        }
        return result;
    }
    private static PixelMetrics ProgramPixel(MandelbrotState state, ProgramContext context, FloatExp offsetX,
        FloatExp offsetY, FloatExp distanceScale, CancellationToken token)
    {
        bool julia = IsJuliaVariant(state.Variant), de = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        bool trap = state.ColoringMode == MandelbrotColoringMode.OrbitTrap, stripes = state.ColoringMode == MandelbrotColoringMode.StripeAverage;
        double escape2 = de ? DistanceEstimationEscapeSquared(state) : (double)(state.Threshold * state.Threshold);
        FloatExp cr = julia ? (double)state.JuliaCReal : context.CenterX + offsetX;
        FloatExp ci = julia ? (double)state.JuliaCImaginary : context.CenterY + offsetY;
        FloatExp zx = julia ? context.CenterX + offsetX : FloatExp.Zero;
        FloatExp zy = julia ? context.CenterY + offsetY : FloatExp.Zero;
        FloatExp dx = julia ? offsetX : FloatExp.Zero, dy = julia ? offsetY : FloatExp.Zero;
        FloatExp addX = julia ? FloatExp.Zero : offsetX, addY = julia ? FloatExp.Zero : offsetY;
        var derivative = julia ? Jacobian2Exp.Identity : Jacobian2Exp.Zero;
        var parameter = julia ? Jacobian2.Zero : Jacobian2.Identity;
        double minTrap = double.MaxValue, stripe = 0, magnitude2 = zx.ToDouble() * zx.ToDouble() + zy.ToDouble() * zy.ToDouble();
        int referenceIndex = 0, iteration = 0; double power = context.Cycle[0].Degree;
        double degreeSum = 0, meanLogDegree = context.Cycle.Average(p => System.Math.Log(p.Degree));
        while (iteration < state.Iterations && magnitude2 <= escape2)
        {
            if ((iteration & 63) == 0 && token.IsCancellationRequested) return default;
            double x = zx.ToDouble(), y = zy.ToDouble();
            if (trap) minTrap = System.Math.Min(minTrap, System.Math.Min(System.Math.Abs(x), System.Math.Abs(y)));
            if (stripes) stripe += 0.5 + 0.5 * System.Math.Sin(state.StripeFrequency * System.Math.Atan2(y, x));
            var bla = context.Deep && !de && !trap && !stripes && ForceBlaForTests != false && FloatExp.Abs(addX) + FloatExp.Abs(addY) <= 30.0 / state.Zoom ? context.Bla : null;
            double delta2 = dx.ToDouble() * dx.ToDouble() + dy.ToDouble() * dy.ToDouble();
            if (referenceIndex >= 0 && bla is not null && bla.CanSkip(referenceIndex, delta2) &&
                bla.TryLookup(referenceIndex, delta2, state.Iterations - iteration,
                    out double a11, out double a12, out double a21, out double a22,
                    out double b11, out double b12, out double b21, out double b22, out int skipped))
            {
                FloatExp newX = dx * a11 + dy * a12 + addX * b11 + addY * b12;
                dy = dx * a21 + dy * a22 + addX * b21 + addY * b22; dx = newX;
                degreeSum += (skipped / context.Cycle.Length) * meanLogDegree * context.Cycle.Length;
                for (int k = 0; k < skipped % context.Cycle.Length; k++) degreeSum += System.Math.Log(context.Cycle[(iteration + k) % context.Cycle.Length].Degree);
                power = context.Cycle[(iteration + skipped - 1) % context.Cycle.Length].Degree;
                referenceIndex += skipped; iteration += skipped;
                zx = context.Orbit[referenceIndex].X + dx; zy = context.Orbit[referenceIndex].Y + dy;
                magnitude2 = zx.ToDouble() * zx.ToDouble() + zy.ToDouble() * zy.ToDouble();
                if (CountRealBlaSkipsForTests) Interlocked.Add(ref RealBlaSkippedIterationsForTests, skipped);
                continue;
            }
            int phase = iteration % context.Cycle.Length;
            var program = context.Cycle[phase]; power = program.Degree; degreeSum += System.Math.Log(power);
            if (context.Deep)
            {
                var reference = referenceIndex >= 0 ? context.Orbit[referenceIndex] : context.Anchors[phase];
                var delta = program.Delta(reference.Values, dx, dy, de);
                if (de)
                {
                    var j = delta.Jacobian;
                    derivative = Jacobian2Exp.Multiply(new(j.Xx, j.Xy, j.Yx, j.Yy), derivative) + parameter;
                }
                dx = delta.Real + addX; dy = delta.Imaginary + addY;
                zx = reference.OutputX + dx; zy = reference.OutputY + dy;
                referenceIndex++;
                int nextPhase = (iteration + 1) % context.Cycle.Length;
                double d2 = dx.ToDouble() * dx.ToDouble() + dy.ToDouble() * dy.ToDouble();
                double z2 = zx.ToDouble() * zx.ToDouble() + zy.ToDouble() * zy.ToDouble();
                // Reset to an anchor with the same next formula. Reusing phase zero
                // for a different phase changes a hybrid's orbit.
                if (referenceIndex <= 0 || referenceIndex >= context.Orbit.Length || z2 < d2 ||
                    !ReferenceEquals(context.Orbit[referenceIndex].Program, context.Cycle[nextPhase]))
                {
                    var anchor = context.Anchors[nextPhase];
                    dx = zx - anchor.X; dy = zy - anchor.Y; referenceIndex = -1;
                }
            }
            else
            {
                var value = program.Evaluate(x, y, de);
                if (de) derivative = Jacobian2Exp.Multiply(new(value.Xx, value.Xy, value.Yx, value.Yy), derivative) + parameter;
                zx = value.Real + cr; zy = value.Imaginary + ci;
            }
            iteration++;
            double rx = zx.ToDouble(), iy = zy.ToDouble(); magnitude2 = rx * rx + iy * iy;
            if (!double.IsFinite(magnitude2)) { magnitude2 = double.MaxValue; break; }
        }
        if (iteration >= state.Iterations) return new(state.Iterations, state.Iterations, 0, 0);
        var result = FinishDeepZoomPixelExp(iteration, magnitude2, minTrap, stripe, de,
            zx.ToDouble(), zy.ToDouble(), derivative, distanceScale, power);
        if (context.Cycle.Length > 1 && magnitude2 > 1)
        {
            double smooth = 1 + (degreeSum + System.Math.Log(meanLogDegree) - System.Math.Log(System.Math.Log(magnitude2) / 2)) / meanLogDegree;
            if (double.IsFinite(smooth)) result = result with { Smooth = smooth };
        }
        return result;
    }
    internal static byte[] RenderProgramExactForTests(MandelbrotState state, int width, int height)
    {
        var buffer = new byte[width * height * 4];
        int bits = PlanDeepZoom(state).ReferenceBits + 192;
        var cycle = ProgramCycle(state);
        FloatExp viewWidth = 3.0 / state.Zoom, viewHeight = viewWidth * height / width;
        bool julia = IsJuliaVariant(state.Variant);
        Parallel.For(0, height, y =>
        {
            using var precision = new BigFloat.PrecisionScope(bits);
            BigFloat centerX = BigFloat.Parse(state.CenterXExact ?? state.CenterX.ToString(CultureInfo.InvariantCulture));
            BigFloat centerY = BigFloat.Parse(state.CenterYExact ?? state.CenterY.ToString(CultureInfo.InvariantCulture));
            BigFloat py = centerY + ((0.5 - (double)y / height) * viewHeight).ToBigFloat();
            foreach (int x in Enumerable.Range(0, width))
            {
                BigFloat px = centerX + (((double)x / width - 0.5) * viewWidth).ToBigFloat();
                BigFloat cr = julia ? BigFloat.FromDecimal(state.JuliaCReal) : px;
                BigFloat ci = julia ? BigFloat.FromDecimal(state.JuliaCImaginary) : py;
                BigFloat zx = julia ? px : BigFloat.Zero, zy = julia ? py : BigFloat.Zero;
                int n = 0; double magnitude = zx.ToDouble() * zx.ToDouble() + zy.ToDouble() * zy.ToDouble(), degreeSum = 0;
                while (n < state.Iterations && magnitude <= (double)(state.Threshold * state.Threshold))
                {
                    var program = cycle[n % cycle.Length]; var workspace = new BigFloat[program.Nodes.Length];
                    var next = program.EvaluateBig(zx, zy, workspace); zx = next.Real + cr; zy = next.Imaginary + ci;
                    degreeSum += System.Math.Log(program.Degree); n++;
                    magnitude = zx.ToDouble() * zx.ToDouble() + zy.ToDouble() * zy.ToDouble();
                }
                double smooth = n;
                if (n < state.Iterations && magnitude > 1)
                {
                    double logDegree = cycle.Average(p => System.Math.Log(p.Degree));
                    smooth = 1 + (degreeSum + System.Math.Log(logDegree) - System.Math.Log(System.Math.Log(magnitude) / 2)) / logDegree;
                }
                WriteColor(buffer, (y * width + x) * 4, ResolveColor(state, new(n, smooth, 0, 0), 0));
            }
        });
        return buffer;
    }

    private static void RenderProgram(MandelbrotState state, byte[] buffer, int width, int height, int stride,
        CancellationToken token, Action<int>? progress) => RenderProgramRegion(state, buffer, width, height,
            new MandelbrotRenderTile(0, 0, width, height, 0, 0), stride, token, progress);
    private static byte[]? RenderProgramTile(MandelbrotState state, int canvasWidth, int canvasHeight,
        MandelbrotRenderTile tile, CancellationToken token)
    {
        var buffer = new byte[checked(tile.Width * tile.Height * 4)];
        RenderProgramRegion(state, buffer, canvasWidth, canvasHeight, tile, tile.Width * 4, token, null);
        return token.IsCancellationRequested ? null : buffer;
    }
    private static void RenderProgramRegion(MandelbrotState state, byte[] buffer, int canvasWidth, int canvasHeight,
        MandelbrotRenderTile tile, int stride, CancellationToken token, Action<int>? progress)
    {
        var context = GetProgramContext(state, token);
        var options = new ParallelOptions { MaxDegreeOfParallelism = state.Threads <= 0 ? Environment.ProcessorCount : state.Threads };
        FloatExp viewWidth = 3.0 / state.Zoom, viewHeight = viewWidth * canvasHeight / canvasWidth;
        bool de = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        bool histogram = state.ColoringMode == MandelbrotColoringMode.Histogram;
        int pad = de ? 1 : 0, w = tile.Width + pad * 2, h = tile.Height + pad * 2;
        var values = new PixelMetrics[checked(w * h)];
        FloatExp scale = de ? PixelDistanceScale(viewWidth, canvasWidth) : FloatExp.One;
        int rows = 0;
        Parallel.For(0, h, options, (yy, loop) =>
        {
            if (token.IsCancellationRequested) { loop.Stop(); return; }
            int y = tile.Y + yy - pad;
            FloatExp offsetY = (0.5 - (double)y / canvasHeight) * viewHeight;
            for (int xx = 0; xx < w; xx++)
            {
                if ((xx & 31) == 0 && token.IsCancellationRequested) { loop.Stop(); return; }
                int x = tile.X + xx - pad;
                values[yy * w + xx] = ProgramPixel(state, context, ((double)x / canvasWidth - 0.5) * viewWidth, offsetY, scale, token);
            }
            progress?.Invoke(Interlocked.Increment(ref rows) * 75 / h);
        });
        if (token.IsCancellationRequested) return;
        double[]? cdf = null;
        if (histogram)
        {
            var bins = new long[state.Iterations + 1];
            foreach (var value in values) bins[System.Math.Clamp((int)System.Math.Floor(state.HistogramInputUseSmooth ? value.Smooth : value.Iterations), 0, state.Iterations)]++;
            cdf = new double[bins.Length]; long sum = 0;
            for (int i = 0; i < bins.Length; i++) { sum += bins[i]; cdf[i] = (double)sum / values.Length; }
        }
        var distances = de ? values.Select(v => StoreDistance(v.Distance)).ToArray() : [];
        Parallel.For(0, tile.Height, options, (y, loop) =>
        {
            if (token.IsCancellationRequested) { loop.Stop(); return; }
            for (int x = 0; x < tile.Width; x++)
            {
                var v = values[(y + pad) * w + x + pad];
                int bin = System.Math.Clamp((int)System.Math.Floor(state.HistogramInputUseSmooth ? v.Smooth : v.Iterations), 0, state.Iterations);
                double normalized = !histogram || v.Iterations >= state.Iterations ? 0 : state.HistogramEnabledEqualization ? cdf![bin] : (double)bin / state.Iterations;
                var color = de ? ResolveDistanceBaseColor(state, v) : ResolveColor(state, v, normalized);
                WriteColor(buffer, y * stride + x * 4, color);
            }
        });
        if (de) ShadeDistanceField(state, buffer, tile.Width, tile.Height, stride, distances, 1.0, options, token, null);
        if (!token.IsCancellationRequested) progress?.Invoke(100);
    }
}
