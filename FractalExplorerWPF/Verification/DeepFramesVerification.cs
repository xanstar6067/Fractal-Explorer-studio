using System.Diagnostics;
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
    // Быстрая сверка глубокого зума: мелкие кадры на каждом пертурбационном ядре. У семейства
    // Мандельброта — каждое ядро (z²+c, отражённые варианты с кубическими, Multibrot,
    // Симоноброт, их Жюлиа) в обоих представлениях δ (double и FloatExp) и в четырёх режимах
    // окраски, которые по-разному проходят ядро: гладкая (с BLA), ловушка и полосы (пошагово)
    // и оценка расстояния (с производной). Плюс Nova, Феникс и Ньютон. Вся группа идёт
    // десятки секунд, поэтому её можно гонять после каждой правки ядер, а полные группы
    // deep, phoenix, newton — один раз в конце.
    //
    //   deep-frames [--record файл] [--compare файл] [--time]
    //
    // Без ключей группа только проверяет, что в каждом кадре есть структура. --time вместо
    // сверки замеряет скорость основных ядер на кадрах покрупнее.
    private static async Task VerifyDeepFramesAsync(string[] args)
    {
        string? record = ReadOption(args, "--record");
        string? compare = ReadOption(args, "--compare");
        List<(string Name, Func<int, int, byte[]> Render)> frames = DeepFrameCases();

        if (args.Contains("--time"))
        {
            await TimeDeepFramesAsync(frames);
            return;
        }

        const int w = 32, h = 24;
        var lines = new List<string>();
        foreach ((string name, Func<int, int, byte[]> render) in frames)
        {
            byte[] pixels = await Task.Run(() => render(w, h));
            int colors = Enumerable.Range(0, w * h)
                .Select(pixel => BitConverter.ToInt32(pixels, pixel * 4)).Distinct().Count();
            // Ловушка и полосы на глубоком зуме законно дают почти однотонный кадр: орбиты
            // соседних пикселей там почти совпадают. Структура обязательна у гладкой окраски.
            if (name.EndsWith("/Smooth") || !name.Contains('/'))
                Check(colors > 1, $"deep-frames {name}: кадр однородный, сверять нечего.");
            if (colors <= 1) Console.WriteLine($"[diag] однотонный кадр {name}");
            lines.Add($"{name} {w}x{h}:{Convert.ToHexString(SHA256.HashData(pixels))}");
        }
        Console.WriteLine($"[diag] deep-frames: посчитано {lines.Count} кадров.");

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
            Console.WriteLine($"Все {lines.Count} глубоких кадров совпадают с эталоном {compare}.");
        }
    }

    /// <summary>
    /// Скорость ядер: кадры 160×120 в один поток, по три прогона, в отчёт идёт лучший. Опорная
    /// орбита кэшируется после первого прогона, поэтому время — это время самих пикселей.
    /// </summary>
    private static async Task TimeDeepFramesAsync(List<(string Name, Func<int, int, byte[]> Render)> frames)
    {
        string[] timed =
        [
            "mandelbrot/double/Smooth", "mandelbrot/floatexp/Smooth",
            "mandelbrot/double/OrbitTrap", "mandelbrot/floatexp/OrbitTrap",
            "mandelbrot/double/DistanceEstimation", "mandelbrot/floatexp/DistanceEstimation",
            "burning-ship/double/OrbitTrap", "burning-ship/floatexp/OrbitTrap",
            "multibrot3/double/OrbitTrap", "multibrot3/floatexp/OrbitTrap",
            "simonobrot3/double/OrbitTrap", "simonobrot3/floatexp/OrbitTrap"
        ];
        foreach (string name in timed)
        {
            Func<int, int, byte[]> render = frames.Single(frame => frame.Name == name).Render;
            await Task.Run(() => render(160, 120));
            long best = long.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                var watch = Stopwatch.StartNew();
                await Task.Run(() => render(160, 120));
                best = Math.Min(best, watch.ElapsedMilliseconds);
            }
            Console.WriteLine($"[time] {name}: {best} мс");
        }
    }

    private static List<(string Name, Func<int, int, byte[]> Render)> DeepFrameCases()
    {
        static MandelbrotPalette Palette() => new()
        {
            Colors = [Colors.White, Colors.Black],
            InteriorColor = Colors.Black,
            IsGradient = true
        };

        var frames = new List<(string Name, Func<int, int, byte[]> Render)>();

        // Семейство Мандельброта: вариант, степень, обращение (Симоноброт).
        (string Label, MandelbrotVariant Variant, decimal Power, bool Inversion)[] variants =
        [
            ("mandelbrot", MandelbrotVariant.Mandelbrot, 2, false),
            ("julia", MandelbrotVariant.Julia, 2, false),
            ("burning-ship", MandelbrotVariant.BurningShip, 2, false),
            ("julia-burning-ship", MandelbrotVariant.JuliaBurningShip, 2, false),
            ("tricorn", MandelbrotVariant.Tricorn, 2, false),
            ("buffalo", MandelbrotVariant.Buffalo, 2, false),
            ("celtic", MandelbrotVariant.Celtic, 2, false),
            ("perpendicular-mandelbrot", MandelbrotVariant.PerpendicularMandelbrot, 2, false),
            ("perpendicular-burning-ship", MandelbrotVariant.PerpendicularBurningShip, 2, false),
            ("perpendicular-celtic", MandelbrotVariant.PerpendicularCeltic, 2, false),
            ("perpendicular-buffalo", MandelbrotVariant.PerpendicularBuffalo, 2, false),
            ("celtic-mandelbar", MandelbrotVariant.CelticMandelbar, 2, false),
            ("cubic-quasi-burning-ship", MandelbrotVariant.CubicQuasiBurningShip, 2, false),
            ("cubic-flying-squirrel", MandelbrotVariant.CubicFlyingSquirrel, 2, false),
            ("julia-cubic-flying-squirrel", MandelbrotVariant.JuliaCubicFlyingSquirrel, 2, false),
            ("multibrot3", MandelbrotVariant.Generalized, 3, false),
            ("multibrot5", MandelbrotVariant.Generalized, 5, false),
            ("julia-multibrot4", MandelbrotVariant.JuliaGeneralized, 4, false),
            ("simonobrot2", MandelbrotVariant.Simonobrot, 2, false),
            ("simonobrot3", MandelbrotVariant.Simonobrot, 3, false),
            ("julia-simonobrot3-inverted", MandelbrotVariant.JuliaSimonobrot, 3, true)
        ];
        MandelbrotColoringMode[] colorings =
        [
            MandelbrotColoringMode.Smooth, MandelbrotColoringMode.OrbitTrap,
            MandelbrotColoringMode.StripeAverage, MandelbrotColoringMode.DistanceEstimation
        ];

        foreach ((string label, MandelbrotVariant variant, decimal power, bool inversion) in variants)
        {
            MandelbrotVariantDefinition definition = MandelbrotVariantDefinition.For(variant);
            MandelbrotState Base() => new()
            {
                Variant = variant,
                Power = power,
                UseInversion = inversion,
                JuliaCReal = definition.DefaultJuliaReal,
                JuliaCImaginary = definition.DefaultJuliaImaginary,
                CenterX = definition.InitialCenterX,
                CenterY = definition.InitialCenterY,
                Zoom = definition.InitialZoom,
                Iterations = 800,
                Threads = 1,
                Palette = Palette()
            };
            var center = new Lazy<(decimal X, decimal Y, double Zoom)>(() => FindBusyCenter(Base));

            foreach (bool floatExp in new[] { false, true })
            foreach (MandelbrotColoringMode coloring in colorings)
            {
                bool capturedFloatExp = floatExp;
                MandelbrotColoringMode capturedColoring = coloring;
                frames.Add(($"{label}/{(floatExp ? "floatexp" : "double")}/{coloring}", (w, h) =>
                {
                    MandelbrotState state = Base();
                    (state.CenterX, state.CenterY, state.Zoom) = center.Value;
                    state.ColoringMode = capturedColoring;
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
        }

        // Тот же z²+c на настоящей глубине 5.8e25.
        foreach (bool floatExp in new[] { false, true })
        {
            bool capturedFloatExp = floatExp;
            frames.Add(($"mandelbrot-5.8e25/{(floatExp ? "floatexp" : "double")}", (w, h) =>
            {
                var state = new MandelbrotState
                {
                    CenterX = -1.2628848671045503000020782246m,
                    CenterY = 0.0409687601493310685285376264m,
                    Zoom = 5.7607143988620999e25,
                    Iterations = 2200,
                    Threads = 1,
                    Palette = Palette()
                };
                byte[] pixels = new byte[w * h * 4];
                MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = capturedFloatExp;
                try { MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None); }
                finally { MandelbrotFamilyRenderer.ForceFloatExpDeltaForTests = null; }
                return pixels;
            }));
        }

        // Nova: целая и дробная степень.
        foreach ((string label, decimal power, double zoom, decimal threshold) in new[]
                 {
                     ("nova-p3", 3m, 2.5e12, 10m),
                     ("nova-p2.5", 2.5m, 1, 2m)
                 })
        {
            decimal capturedPower = power, capturedThreshold = threshold;
            double capturedZoom = zoom;
            frames.Add((label, (w, h) =>
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
                try { NovaRenderer.Render(state, pixels, w, h, w * 4, 1, CancellationToken.None); }
                finally { NovaRenderer.ForceDeepZoomForTests = null; }
                return pixels;
            }));
        }

        // Феникс: double-δ и гибридное ядро с линейным пропуском.
        foreach (bool extended in new[] { false, true })
        {
            bool capturedExtended = extended;
            frames.Add(($"phoenix/{(extended ? "extended" : "double")}", (w, h) =>
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
                try { PhoenixRenderer.Render(state, pixels, w, h, w * 4, 1, CancellationToken.None); }
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
            frames.Add(($"newton-{method.ToString().ToLowerInvariant()}", (w, h) =>
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
                try { engine.RenderToBuffer(pixels, w, h, w * 4, 1, CancellationToken.None); }
                finally { NewtonPoolsEngine.ForceDeepZoomForTests = null; }
                return pixels;
            }));
        }

        return frames;
    }

    /// <summary>
    /// Центр с богатой структурой: десять приближений в 8 раз к самому пёстрому пикселю кадра
    /// (до зума ~8e8). Все шаги — плоская ступень ниже порога глубокого движка, поэтому
    /// найденный центр от правки пертурбационных ядер не зависит. Итоговый зум — ещё в 8 раз
    /// глубже (~6e9), уже за порогом.
    /// </summary>
    private static (decimal X, decimal Y, double Zoom) FindBusyCenter(Func<MandelbrotState> create)
    {
        const int w = 40, h = 30;
        MandelbrotState state = create();
        decimal x = state.CenterX, y = state.CenterY;
        double zoom = state.Zoom.ToDouble();
        for (int step = 0; step < 10; step++)
        {
            state = create();
            state.CenterX = x;
            state.CenterY = y;
            state.Zoom = zoom;
            byte[] pixels = new byte[w * h * 4];
            MandelbrotFamilyRenderer.ForceDeepZoomForTests = false;
            try { MandelbrotFamilyRenderer.Render(state, pixels, w, h, w * 4, CancellationToken.None); }
            finally { MandelbrotFamilyRenderer.ForceDeepZoomForTests = null; }

            int bestX = w / 2, bestY = h / 2, bestScore = -1;
            for (int py = 1; py < h - 1; py++)
            for (int px = 1; px < w - 1; px++)
            {
                var colors = new HashSet<int>();
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    colors.Add(BitConverter.ToInt32(pixels, ((py + dy) * w + px + dx) * 4));
                if (colors.Count > bestScore) (bestScore, bestX, bestY) = (colors.Count, px, py);
            }

            decimal viewWidth = 3m / (decimal)zoom;
            decimal viewHeight = viewWidth * h / w;
            x += ((decimal)bestX / w - 0.5m) * viewWidth;
            y += (0.5m - (decimal)bestY / h) * viewHeight;
            zoom *= 8;
        }
        return (x, y, zoom * 8);
    }
}
