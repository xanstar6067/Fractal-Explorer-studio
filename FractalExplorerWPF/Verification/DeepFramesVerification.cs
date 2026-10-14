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
    // и оценка расстояния (с производной). У Феникса — double-ядро и гибридное с пропуском и
    // без (см. AddPhoenixDeepFrames). Плюс Nova и Ньютон. Вся группа идёт
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
            "simonobrot3/double/OrbitTrap", "simonobrot3/floatexp/OrbitTrap",
            "phoenix-classic-julia/double/Smooth", "phoenix-classic-julia/stepped/Smooth",
            "phoenix-celtic-julia/double/Period", "phoenix-celtic-julia/stepped/Period",
            "phoenix-saddle-1e300/stepped/Smooth", "phoenix-saddle-1e300/skipped/Smooth"
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

        AddPhoenixDeepFrames(frames, Palette);

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
    /// Кадры Феникса: семь конфигураций (все свёртки, обе плоскости, степени до 5 и член
    /// C1·ΔG) в пяти режимах окраски, каждый на трёх путях — double-ядре, гибридном без
    /// пропуска и гибридном с линейным пропуском. На найденных центрах (~9e9) гибридное ядро
    /// идёт в double-режиме, поэтому FloatExp-шаги проверяют сверхглубокие кадры 1e300 у
    /// отталкивающих неподвижных точек: β-точки «базилики» и седла с памятью (как в группе
    /// phoenix, где они сверяются с точным BigFloat-эталоном).
    /// </summary>
    private static void AddPhoenixDeepFrames(List<(string Name, Func<int, int, byte[]> Render)> frames,
        Func<MandelbrotPalette> palette)
    {
        PhoenixState Base(PhoenixVariant variant, PhoenixPlaneMode plane, int primaryPower, int secondaryPower,
            PhoenixColoringMode coloring) => new()
        {
            Zoom = 1,
            Iterations = 300,
            Threshold = 4m,
            C1Real = 0.56667m,
            C2Real = -0.5m,
            PlaneMode = plane,
            Variant = variant,
            PrimaryPower = primaryPower,
            SecondaryPower = secondaryPower,
            ColoringMode = coloring,
            OrbitTrapMode = PhoenixOrbitTrapMode.Axes,
            OrbitTrapRadius = 0.5,
            OrbitTrapStrength = 1.5,
            StripeFrequency = 3,
            StripeStrength = 0.65,
            CycleTolerance = 1e-7,
            MaximumDetectedPeriod = 32,
            Palette = palette()
        };

        static byte[] Render(PhoenixState state, int w, int h, bool? forceDeep, bool? forceExtended, bool? forceSkip)
        {
            byte[] pixels = new byte[w * h * 4];
            PhoenixRenderer.ForceDeepZoomForTests = forceDeep;
            PhoenixRenderer.ForceExtendedDeltaForTests = forceExtended;
            PhoenixRenderer.ForceLinearSkipForTests = forceSkip;
            try { PhoenixRenderer.Render(state, pixels, w, h, w * 4, 1, CancellationToken.None); }
            finally
            {
                PhoenixRenderer.ForceDeepZoomForTests = null;
                PhoenixRenderer.ForceExtendedDeltaForTests = null;
                PhoenixRenderer.ForceLinearSkipForTests = null;
            }
            return pixels;
        }

        (string Label, PhoenixVariant Variant, PhoenixPlaneMode Plane, int A, int B)[] configurations =
        [
            ("phoenix-classic-julia", PhoenixVariant.Classic, PhoenixPlaneMode.Julia, 2, 0),
            ("phoenix-classic-c1", PhoenixVariant.Classic, PhoenixPlaneMode.ParameterC1, 2, 1),
            ("phoenix-tricorn-julia", PhoenixVariant.Tricorn, PhoenixPlaneMode.Julia, 2, 0),
            ("phoenix-burning-ship-julia", PhoenixVariant.BurningShip, PhoenixPlaneMode.Julia, 2, 1),
            ("phoenix-buffalo-c1", PhoenixVariant.Buffalo, PhoenixPlaneMode.ParameterC1, 2, 1),
            ("phoenix-celtic-julia", PhoenixVariant.Celtic, PhoenixPlaneMode.Julia, 3, 1),
            ("phoenix-classic-julia-a5b2", PhoenixVariant.Classic, PhoenixPlaneMode.Julia, 5, 2)
        ];
        PhoenixColoringMode[] colorings =
        [
            PhoenixColoringMode.Smooth, PhoenixColoringMode.OrbitTrap, PhoenixColoringMode.StripeAverage,
            PhoenixColoringMode.TriangleInequalityAverage, PhoenixColoringMode.Period
        ];
        (string Label, bool Extended, bool Skip)[] kernels = [("double", false, false), ("stepped", true, false), ("skipped", true, true)];

        foreach ((string label, PhoenixVariant variant, PhoenixPlaneMode plane, int a, int b) in configurations)
        {
            var center = new Lazy<(decimal X, decimal Y, double Zoom)>(() =>
                FindBusyPhoenixCenter(() => Base(variant, plane, a, b, PhoenixColoringMode.Smooth)));
            foreach ((string kernel, bool extended, bool skip) in kernels)
            foreach (PhoenixColoringMode coloring in colorings)
                frames.Add(($"{label}/{kernel}/{coloring}", (w, h) =>
                {
                    PhoenixState state = Base(variant, plane, a, b, coloring);
                    (state.CenterX, state.CenterY, double zoom) = center.Value;
                    state.Zoom = zoom;
                    return Render(state, w, h, true, extended, skip);
                }));
        }

        // Сверхглубина: здесь гибридное ядро действительно шагает в FloatExp.
        string betaX;
        using (new BigFloat.PrecisionScope(4096))
            betaX = ((BigFloat.One + BigFloat.Sqrt(BigFloat.FromInt(5))) / 2L).ToInvariantString();
        foreach (bool memory in new[] { false, true })
        foreach (PhoenixColoringMode coloring in new[] { PhoenixColoringMode.Smooth, PhoenixColoringMode.TriangleInequalityAverage })
        foreach ((string kernel, bool skip) in new[] { ("stepped", false), ("skipped", true) })
        {
            string label = $"phoenix-{(memory ? "saddle" : "beta")}-1e300/{kernel}/{coloring}";
            frames.Add((label, (w, h) =>
            {
                PhoenixState state = Base(PhoenixVariant.Classic, PhoenixPlaneMode.Julia, 2, 0, coloring);
                state.Zoom = FloatExp.Pow10(300);
                state.Iterations = 1000;
                state.C1Real = -1m;
                state.C2Real = memory ? -0.5m : 0m;
                state.InitialPreviousReal = memory ? 2m : 0m;
                state.CenterXExact = memory ? "2" : betaX;
                state.CenterYExact = "0";
                using (new BigFloat.PrecisionScope(1152))
                    state.CenterX = BigFloat.Parse(state.CenterXExact).ToDecimalClamped();
                return Render(state, w, h, true, true, skip);
            }));
        }
    }

    /// <summary>
    /// Центр Феникса с богатой структурой — тот же поиск, что <see cref="FindBusyCenter"/>, на
    /// плоской ступени Феникса (ширина вида 4/зум, обе оси делятся на ширину полотна).
    /// </summary>
    private static (decimal X, decimal Y, double Zoom) FindBusyPhoenixCenter(Func<PhoenixState> create)
    {
        const int w = 40, h = 30;
        decimal x = 0, y = 0;
        double zoom = 1;
        for (int step = 0; step < 10; step++)
        {
            PhoenixState state = create();
            state.CenterX = x;
            state.CenterY = y;
            state.Zoom = zoom;
            byte[] pixels = new byte[w * h * 4];
            PhoenixRenderer.ForceDeepZoomForTests = false;
            try { PhoenixRenderer.Render(state, pixels, w, h, w * 4, 1, CancellationToken.None); }
            finally { PhoenixRenderer.ForceDeepZoomForTests = null; }

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

            decimal viewWidth = 4m / (decimal)zoom;
            x += (bestX - w / 2m) * viewWidth / w;
            y += (h / 2m - bestY) * viewWidth / w;
            zoom *= 8;
        }
        return (x, y, zoom * 8);
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
