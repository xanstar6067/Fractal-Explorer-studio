using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Windows.Media;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;

// Хеши мелких кадров пертурбационных движков: быстрая побитная сверка до и после правки
// общего кода глубокого зума.
internal static partial class Program
{
    // Быстрая сверка глубокого зума: по нескольку мелких кадров на каждый пертурбационный
    // движок (Мандельброт и его отражённые варианты, Nova, Феникс, Ньютон), каждый — во всех
    // представлениях δ, которые выбирает план кадра. Кадры крошечные, вся группа идёт
    // секунды, поэтому её можно гонять после каждой правки общего кода, а полные группы
    // deep, phoenix, newton — один раз в конце.
    //
    //   deep-frames [--record файл] [--compare файл]
    //
    // Без ключей группа только проверяет, что в каждом кадре есть структура.
    private static async Task VerifyDeepFramesAsync(string[] args)
    {
        string? record = ReadOption(args, "--record");
        string? compare = ReadOption(args, "--compare");
        const int w = 48, h = 32;

        static MandelbrotPalette Palette() => new()
        {
            Colors = [Colors.White, Colors.Black],
            InteriorColor = Colors.Black,
            IsGradient = true
        };

        var frames = new List<(string Name, Func<byte[]> Render)>();

        // Точка на границе множества с точностью double — бисекцией по отрезку от внутренней
        // точки c = 0 к внешней по 600 итерациям (там, где граница не гладкая): на зуме 1e11 вокруг неё в кадре есть структура.
        static decimal BoundaryX(Func<double, double, double, double, (double, double)> step, double y, double outsideX = 2)
        {
            bool Escapes(double cx)
            {
                double zx = 0, zy = 0;
                for (int i = 0; i < 600; i++)
                {
                    (zx, zy) = step(zx, zy, cx, y);
                    if (zx * zx + zy * zy > 16) return true;
                }
                return false;
            }
            double inside = 0, outside = outsideX;
            for (int i = 0; i < 60; i++)
            {
                double middle = (inside + outside) / 2;
                if (Escapes(middle)) outside = middle; else inside = middle;
            }
            return (decimal)inside;
        }

        // Мандельброт и отражённые варианты: double-δ, FloatExp-δ и вещественная BLA.
        foreach ((string label, MandelbrotVariant variant, decimal centerX, decimal centerY, double zoom) in new[]
                 {
                     ("mandelbrot", MandelbrotVariant.Mandelbrot,
                         -1.2628848671045503000020782246m, 0.0409687601493310685285376264m, 5.7607143988620999e25),
                     ("burning-ship", MandelbrotVariant.BurningShip,
                         BoundaryX((x, y, cx, cy) => (x * x - y * y + cx, -2 * Math.Abs(x * y) + cy), 0.05, -2.5), 0.05m, 1e11),
                     ("tricorn", MandelbrotVariant.Tricorn,
                         BoundaryX((x, y, cx, cy) => (x * x - y * y + cx, -2 * x * y + cy), 0.05, -2.5), 0.05m, 1e11)
                 })
        foreach (bool floatExp in new[] { false, true })
        {
            MandelbrotVariant capturedVariant = variant;
            decimal capturedX = centerX, capturedY = centerY;
            double capturedZoom = zoom;
            bool capturedFloatExp = floatExp;
            frames.Add(($"{label}/{(floatExp ? "floatexp" : "double")}", () =>
            {
                var state = new MandelbrotState
                {
                    Variant = capturedVariant,
                    CenterX = capturedX,
                    CenterY = capturedY,
                    Zoom = capturedZoom,
                    Iterations = 2200,
                    Threads = 2,
                    Palette = Palette()
                };
                byte[] pixels = new byte[w * h * 4];
                MandelbrotFamilyRenderer.ForceDeepZoomForTests = true;
                MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = capturedFloatExp;
                try { MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None); }
                finally
                {
                    MandelbrotFamilyRenderer.ForceDeepZoomForTests = null;
                    MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = null;
                }
                return pixels;
            }));
        }

        frames.Add(("multibrot3/double", () =>
        {
            var state = new MandelbrotState
            {
                Variant = MandelbrotVariant.Generalized,
                Power = 3,
                CenterX = BoundaryX((x, y, cx, cy) => (x * x * x - 3 * x * y * y + cx, 3 * x * x * y - y * y * y + cy), 0.3),
                CenterY = 0.3m,
                Zoom = 1e11,
                Iterations = 600,
                Threads = 2,
                Palette = Palette()
            };
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = true;
            try { MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None); }
            finally { MandelbrotFamilyRenderer.ForceDeepZoomForTests = null; }
            return pixels;
        }));

        // Nova: целая и дробная степень.
        foreach ((string label, decimal power, double zoom, decimal threshold) in new[]
                 {
                     ("nova-p3", 3m, 2.5e12, 10m),
                     ("nova-p2.5", 2.5m, 1, 2m)
                 })
        {
            decimal capturedPower = power, capturedThreshold = threshold;
            double capturedZoom = zoom;
            frames.Add((label, () =>
            {
                var state = new NovaState
                {
                    Variant = NovaVariant.Mandelbrot,
                    CenterXExact = "-0.509516618365150777259563561165083570374599345148385054482531586472759954631328582763671875",
                    CenterYExact = "0.12446526403814218906112533265557181617030335358912995769031795134651474654674530029296875",
                    Zoom = capturedZoom,
                    Threshold = capturedThreshold,
                    Iterations = 300,
                    PReal = capturedPower,
                    Z0Real = 1m,
                    M = 1m,
                    CImaginary = 1m,
                    UseSmoothColoring = true,
                    Palette = Palette()
                };
                state.CenterX = BigFloat.Parse(state.CenterXExact).ToDecimalClamped();
                state.CenterY = BigFloat.Parse(state.CenterYExact).ToDecimalClamped();
                byte[] pixels = new byte[w * h * 4];
                NovaRenderer.ForceDeepZoomForTests = true;
                try { NovaRenderer.Render(state, pixels, w, h, w * 4, 2, CancellationToken.None); }
                finally { NovaRenderer.ForceDeepZoomForTests = null; }
                return pixels;
            }));
        }

        // Феникс: double-δ и гибридное ядро с линейным пропуском.
        foreach (bool extended in new[] { false, true })
        {
            bool capturedExtended = extended;
            frames.Add(($"phoenix/{(extended ? "extended" : "double")}", () =>
            {
                var state = new PhoenixState
                {
                    CenterXExact = "0.3605697876341732990634970411179978",
                    CenterYExact = "0.9162050700524670653999492733755535",
                    Zoom = 1.15e18,
                    Iterations = 300,
                    Threshold = 4m,
                    C1Real = 0.56667m,
                    C2Real = -0.5m,
                    PlaneMode = PhoenixPlaneMode.Julia,
                    Variant = PhoenixVariant.Classic,
                    PrimaryPower = 2,
                    ColoringMode = PhoenixColoringMode.Smooth,
                    OrbitTrapMode = PhoenixOrbitTrapMode.Axes,
                    OrbitTrapRadius = 0.5,
                    OrbitTrapStrength = 1.5,
                    StripeFrequency = 3,
                    StripeStrength = 0.65,
                    CycleTolerance = 1e-7,
                    MaximumDetectedPeriod = 32,
                    Palette = Palette()
                };
                state.CenterX = BigFloat.Parse(state.CenterXExact).ToDecimalClamped();
                state.CenterY = BigFloat.Parse(state.CenterYExact).ToDecimalClamped();
                byte[] pixels = new byte[w * h * 4];
                PhoenixRenderer.ForceDeepZoomForTests = true;
                PhoenixRenderer.ForceExtendedDeltaForTests = capturedExtended;
                try { PhoenixRenderer.Render(state, pixels, w, h, w * 4, 2, CancellationToken.None); }
                finally
                {
                    PhoenixRenderer.ForceDeepZoomForTests = null;
                    PhoenixRenderer.ForceExtendedDeltaForTests = null;
                }
                return pixels;
            }));
        }

        // Ньютон: метод Ньютона у отталкивающего 2-цикла z³ − 1 и метод Галлея рядом с ним.
        foreach ((NewtonIterationMethod method, string zoom) in new[] { (NewtonIterationMethod.Newton, "1e12"), (NewtonIterationMethod.Halley, "1") })
        {
            NewtonIterationMethod capturedMethod = method;
            string capturedZoom = zoom;
            frames.Add(($"newton-{method.ToString().ToLowerInvariant()}", () =>
            {
                var engine = new NewtonPoolsEngine
                {
                    MaxIterations = 300,
                    CenterX = 0.538608672507971,
                    CenterY = 0.417204483749251,
                    CenterXExact = "0.538608672507971",
                    CenterYExact = "0.417204483749251",
                    Zoom = FloatExp.Parse(capturedZoom),
                    IterationMethod = capturedMethod,
                    HouseholderOrder = 3,
                    Relaxation = new Complex(0.8, 0.3),
                    FixedInitialZ = new Complex(0.5, 0.5),
                    RootTolerance = 1e-6,
                    BackgroundColor = Colors.Black,
                    UseGradient = true
                };
                Check(engine.SetFormula("z^3-1", out string debug), $"z^3-1 must parse: {debug}");
                engine.RootColors = [Colors.Red, Colors.Lime, Colors.Blue];
                byte[] pixels = new byte[w * h * 4];
                NewtonPoolsEngine.ForceDeepZoomForTests = true;
                try { engine.RenderToBuffer(pixels, w, h, w * 4, 2, CancellationToken.None); }
                finally { NewtonPoolsEngine.ForceDeepZoomForTests = null; }
                return pixels;
            }));
        }

        var lines = new List<string>();
        foreach ((string name, Func<byte[]> render) in frames)
        {
            byte[] pixels = await Task.Run(render);
            int colors = Enumerable.Range(0, w * h)
                .Select(pixel => BitConverter.ToInt32(pixels, pixel * 4)).Distinct().Count();
            Console.WriteLine($"[diag] deep-frames {name}: {colors} цветов");
            Check(colors > 1, $"deep-frames {name}: кадр однородный, сверять нечего.");
            lines.Add($"{name} {w}x{h}:{Convert.ToHexString(SHA256.HashData(pixels))}");
        }

        if (record is not null)
        {
            File.WriteAllLines(record, lines);
            Console.WriteLine($"Записано {lines.Count} глубоких кадров в {record}.");
        }
        if (compare is not null)
        {
            string[] expected = File.ReadAllLines(compare).Where(line => line.Length > 0).ToArray();
            var differing = lines.Where(line => !expected.Contains(line)).ToList();
            foreach (string line in differing)
                Console.WriteLine($"Кадр отличается от эталона: {line}");
            Check(expected.Length == lines.Count, $"В эталоне {expected.Length} кадров, посчитано {lines.Count}.");
            Check(differing.Count == 0, $"{differing.Count} глубоких кадров отличаются от эталона {compare}.");
            if (differing.Count == 0)
                Console.WriteLine($"Все {lines.Count} глубоких кадров совпадают с эталоном {compare}.");
        }
    }
}
