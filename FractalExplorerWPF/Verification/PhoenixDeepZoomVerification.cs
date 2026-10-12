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

// Глубокий и сверхглубокий зум Феникса.
internal static partial class Program
{
    // Phoenix on the perturbation engine. Phoenix had no precision ladder at all: both the
    // full render and the tile path computed coordinates in plain double, so the picture fell
    // apart around 1e12 while the zoom box happily accepted decimal.MaxValue/2.
    //
    // What makes Phoenix different from the Mandelbrot family is memory: z_{n+1} depends on
    // z_{n-1}, so the per-pixel state is the pair (δₙ, δₙ₋₁) and the reference orbit is stored
    // shifted by one (Orbit[i] = Z_{i-1}, Orbit[0] = z₋₁) so that rebasing to the start has a
    // z₋₁ to rebase against.
    //
    // The load-bearing check is the first one: the plain double path shares no code with the
    // BigFloat/perturbation path, so agreement at a zoom where double is still exact is what
    // proves the formula, every coloring metric and the perturbation algebra were all
    // transcribed correctly. The exact BigFloat reference is the oracle for the depths where
    // the double path can no longer be trusted — but it shares VariantPowerBig with the
    // reference orbit, so it can only catch perturbation errors, not formula errors.
    private static async Task VerifyPhoenixDeepZoomAsync()
    {
        static MandelbrotPalette Palette() => new()
        {
            Colors = [Colors.White, Colors.Black],
            InteriorColor = Colors.Black,
            IsGradient = true
        };

        static PhoenixState View(double zoom, PhoenixVariant variant, PhoenixColoringMode coloring,
            PhoenixPlaneMode plane = PhoenixPlaneMode.Julia, int primaryPower = 2, int secondaryPower = 0,
            int iterations = 200, double centerX = 0, double centerY = 0) => new()
        {
            CenterX = (decimal)centerX,
            CenterY = (decimal)centerY,
            Zoom = zoom,
            Iterations = iterations,
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
            Palette = Palette()
        };

        static PhoenixState AtExactCenter(PhoenixState state, string centerX, string centerY)
        {
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

        // Соседние различающиеся пиксели: кадр действительно показывает структуру, а не
        // однородную заливку. Считать «не-чёрные» недостаточно — проверочная палитра
        // белый→чёрный, и кадр далеко снаружи множества выходит сплошь не-чёрным.
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

        static async Task<byte[]> RenderAsync(PhoenixState state, bool? forceDeep, int width, int height,
            int? forceBits = null)
        {
            byte[] pixels = new byte[width * height * 4];
            PhoenixRenderer.ForceDeepZoomForTests = forceDeep;
            PhoenixRenderer.ForceReferenceBitsForTests = forceBits;
            try
            {
                await Task.Run(() => PhoenixRenderer.Render(state, pixels, width, height, width * 4, 4,
                    CancellationToken.None));
            }
            finally
            {
                PhoenixRenderer.ForceDeepZoomForTests = null;
                PhoenixRenderer.ForceReferenceBitsForTests = null;
            }
            return pixels;
        }

        const int w = 64, h = 44, total = w * h;

        // 1. Где плоский double ещё точен, пертурбационный движок обязан его воспроизвести.
        //    Прогоняем все пять вариантов против всех семи режимов окраски и обеих плоскостей:
        //    каждый режим читает свой набор метрик орбиты, каждый вариант — свою ветку свёртки
        //    знака. Это и есть проверка переноса формулы: у двух путей нет общего кода.
        var variants = new[]
        {
            PhoenixVariant.Classic, PhoenixVariant.Tricorn, PhoenixVariant.BurningShip,
            PhoenixVariant.Celtic, PhoenixVariant.Buffalo
        };
        var colorings = new[]
        {
            PhoenixColoringMode.Discrete, PhoenixColoringMode.Smooth, PhoenixColoringMode.OrbitTrap,
            PhoenixColoringMode.StripeAverage, PhoenixColoringMode.TriangleInequalityAverage,
            PhoenixColoringMode.FinalArgument, PhoenixColoringMode.Period
        };
        int worstDiffering = 0;
        string worstLabel = "";
        foreach (PhoenixVariant variant in variants)
        foreach (PhoenixColoringMode coloring in colorings)
        foreach (PhoenixPlaneMode plane in new[] { PhoenixPlaneMode.Julia, PhoenixPlaneMode.ParameterC1 })
        {
            PhoenixState state = View(700, variant, coloring, plane);
            byte[] shallow = await RenderAsync(state, false, w, h);
            byte[] deep = await RenderAsync(state, true, w, h);
            int differing = CountDiffering(shallow, deep);
            if (differing > worstDiffering)
            {
                worstDiffering = differing;
                worstLabel = $"{variant}/{coloring}/{plane}";
            }
            Check(differing * 100 <= total * 2,
                $"Phoenix perturbation must match the plain double path at zoom 700 " +
                $"({variant}, {coloring}, {plane}): {differing}/{total} pixels differ.");
        }
        Console.WriteLine($"[diag] phoenix shallow-vs-deep worst {worstDiffering}/{total} ({worstLabel})");

        // Степени: вторая степень b > 0 включает второе слагаемое c1·G(z) целиком (при b = 0
        // оно вырождается в константу, и ошибка в его возмущении осталась бы незамеченной).
        foreach ((int primary, int secondary) in new[] { (2, 0), (3, 0), (2, 1), (3, 2), (5, 4), (12, 1) })
        {
            PhoenixState state = View(400, PhoenixVariant.Classic, PhoenixColoringMode.Smooth,
                primaryPower: primary, secondaryPower: secondary);
            byte[] shallow = await RenderAsync(state, false, w, h);
            byte[] deep = await RenderAsync(state, true, w, h);
            int differing = CountDiffering(shallow, deep);
            Check(differing * 100 <= total * 2,
                $"Phoenix perturbation must match the plain path for powers a={primary}, b={secondary}: " +
                $"{differing}/{total} pixels differ.");
        }

        // 2. На глубине плоскому пути верить уже нельзя — сравниваем с прямой итерацией в
        //    BigFloat. Центры найдены спуском по границе (см. стенд в истории задачи), поэтому
        //    у кадров есть структура: без неё сравнение прошло бы вхолостую.
        (string X, string Y, double Zoom)[] deepFixtures =
        [
            ("0.3605697876344492991196400742422874", "0.9162050700528197582748546315293717", 1.10e12),
            ("0.3605697876341732990634970411179978", "0.9162050700524670653999492733755535", 1.15e18),
            ("0.3605697876341732992373776850660347", "0.9162050700524670649253017882362955", 1.21e24),
            ("0.3605697876341732992373786585380622", "0.9162050700524670649253011924481502", 1.98e28),
        ];
        foreach ((string centerX, string centerY, double zoom) in deepFixtures)
        {
            PhoenixState state = AtExactCenter(
                View(zoom, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                centerX, centerY);
            byte[] deep = await RenderAsync(state, true, w, h);
            byte[] exact = await Task.Run(() =>
                PhoenixRenderer.RenderExactReferenceForTests(state, w, h, 128, CancellationToken.None));
            int differing = CountDiffering(deep, exact);
            int edges = CountEdges(exact, w);
            Console.WriteLine($"[diag] phoenix deep {zoom:0.0e+0}: {differing}/{total} differ, {edges} edges");
            Check(differing * 100 <= total * 3,
                $"Phoenix deep zoom must match the exact BigFloat reference at {zoom:0.0e+0}: " +
                $"{differing}/{total} pixels differ.");
        }

        // 3. План точности: тот же кадр с заведомо избыточной разрядностью опорной орбиты.
        //    Единственная проверка самого плана, не требующая внешнего эталона.
        foreach (double zoom in new[] { 1.10e12, 1.15e18, 1.21e24 })
        {
            PhoenixState state = AtExactCenter(
                View(zoom, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                "0.3605697876341732992373776850660347", "0.9162050700524670649253017882362955");
            byte[] planned = await RenderAsync(state, true, w, h);
            byte[] generous = await RenderAsync(state, true, w, h,
                PhoenixRenderer.PlanReferenceBits(state) + 256);
            int drift = CountDiffering(planned, generous);
            Check(drift == 0,
                $"Phoenix precision plan must not drift with 256 extra reference bits at {zoom:0.0e+0}: " +
                $"{drift}/{total} pixels differ.");
        }

        // 4. Тайл прогрессивного предпросмотра обязан совпасть с полным кадром: у них разные
        //    точки входа в движок и своя раскладка пикселей.
        {
            PhoenixState state = AtExactCenter(
                View(1e15, PhoenixVariant.BurningShip, PhoenixColoringMode.Smooth, iterations: 300),
                "0.3605697876341732992373776850660347", "0.9162050700524670649253017882362955");
            byte[] full = await RenderAsync(state, true, w, h);
            PhoenixRenderer.ForceDeepZoomForTests = true;
            byte[]? tile;
            try
            {
                tile = await Task.Run(() => PhoenixRenderer.RenderTile(state, w, h,
                    new MandelbrotRenderTile(16, 12, 24, 16, 1, 1), CancellationToken.None));
            }
            finally { PhoenixRenderer.ForceDeepZoomForTests = null; }
            Check(tile is not null, "Phoenix deep-zoom tile must render.");
            int tileDiffering = 0;
            for (int localY = 0; localY < 16; localY++)
            for (int localX = 0; localX < 24; localX++)
            {
                int tileOffset = (localY * 24 + localX) * 4;
                int fullOffset = ((12 + localY) * w + 16 + localX) * 4;
                if (tile![tileOffset] != full[fullOffset] || tile[tileOffset + 1] != full[fullOffset + 1] ||
                    tile[tileOffset + 2] != full[fullOffset + 2]) tileDiffering++;
            }
            Check(tileDiffering == 0,
                $"Phoenix deep-zoom tile must match the full frame exactly: {tileDiffering}/384 differ.");
        }

        // 5. Точный центр действительно доходит до рендера: сдвиг на десятую пикселя на
        //    глубине, где decimal-поля состояния его уже не различают, обязан менять кадр.
        {
            // Зум подобран так, чтобы полпикселя были заведомо мельче разрешения decimal
            // (шаг ≈ 3e-30 против ULP ≈ 1e-28 у центра около 0.36): только тогда проверка
            // показывает, что положение области доходит до рендера именно строкой.
            const double zoom = 1.98e28;
            PhoenixState reference = AtExactCenter(
                View(zoom, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                "0.3605697876341732992373786585380622", "0.9162050700524670649253011924481502");
            BigFloat nudge = BigFloat.FromDouble(4.0 / zoom / w * 0.5);
            PhoenixState nudged = AtExactCenter(
                View(zoom, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                (BigFloat.Parse(reference.CenterXExact!) + nudge).ToInvariantString(),
                reference.CenterYExact!);
            Check(CountEdges(await RenderAsync(reference, true, w, h), w) > 50,
                "The center-precision fixture must show structure, or a changed frame proves nothing.");
            Check(reference.CenterX == nudged.CenterX,
                "The nudge must be invisible to the decimal center fields — otherwise this proves nothing.");
            byte[] before = await RenderAsync(reference, true, w, h);
            byte[] after = await RenderAsync(nudged, true, w, h);
            Check(CountDiffering(before, after) > 0,
                "A tenth-of-a-pixel shift of the exact center must change the deep-zoom frame.");
        }

        // 6. Кэш опорной орбиты не путает состояния, различающиеся только формулой. Это тот
        //    самый класс ошибок, который у семейства Мандельброта однажды дал чёрный кадр:
        //    ключ кэша не нёс степень.
        {
            PhoenixState first = View(1e12, PhoenixVariant.Classic, PhoenixColoringMode.Smooth,
                primaryPower: 2, secondaryPower: 0);
            PhoenixState second = View(1e12, PhoenixVariant.Classic, PhoenixColoringMode.Smooth,
                primaryPower: 3, secondaryPower: 0);
            byte[] a = await RenderAsync(first, true, w, h);
            byte[] b = await RenderAsync(second, true, w, h);
            byte[] againA = await RenderAsync(first, true, w, h);
            Check(CountDiffering(a, b) > 0, "Different primary powers must not collide in the orbit cache.");
            Check(CountDiffering(a, againA) == 0, "Re-rendering the same Phoenix state must be deterministic.");

            PhoenixState julia = View(1e12, PhoenixVariant.Classic, PhoenixColoringMode.Smooth);
            PhoenixState parameter = View(1e12, PhoenixVariant.Classic, PhoenixColoringMode.Smooth,
                PhoenixPlaneMode.ParameterC1);
            Check(CountDiffering(await RenderAsync(julia, true, w, h),
                    await RenderAsync(parameter, true, w, h)) > 0,
                "Dynamic and parameter planes must not collide in the orbit cache.");
        }

        // 7. Фронт выхода — самый чувствительный кадр, какой удалось построить: центр в точке
        //    границы (бинарный поиск между заведомо внутренней и заведомо внешней точками), а
        //    число итераций подобрано так, что вся область вылетает за радиус в пределах одного
        //    шага. Прежде эта проверка считалась пределом движка и задавала потолок окна 1e24,
        //    но разбор показал иное: на шаге решения разброс |z|² по всему кадру меньше 2⁻⁵²
        //    относительно (на 1e28 — 3.43203826028866…, различие в 16-м знаке), и такой кадр не
        //    различает никакой рендер, ведущий z в double. Эталон различает его только потому,
        //    что итерирует z в BigFloat. Это свойство кадра, а не накопление ошибки δ: на кадрах
        //    со структурой расхождения с глубиной не растут (см. VerifyPhoenixExtremeZoomAsync).
        //    Проверка остаётся стражем точности до глубины, где кадр ещё различим.
        {
            const string borderX = "0.3605697876341732992373786585816072";
            const string borderY =
                "0.9044804668779509850030358017389001116892197535457371417753243249685989";
            foreach ((double zoom, int budget) in new[] { (1e20, 2), (1e22, 2), (1e24, 4) })
            {
                PhoenixState state = AtExactCenter(
                    View(zoom, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                    borderX, borderY);
                byte[] deep = await RenderAsync(state, true, w, h);
                byte[] exact = await Task.Run(() =>
                    PhoenixRenderer.RenderExactReferenceForTests(state, w, h, 256, CancellationToken.None));
                int differing = CountDiffering(deep, exact);
                Console.WriteLine($"[diag] phoenix escape-front {zoom:0.0e+0}: {differing}/{total} differ");
                Check(differing <= budget,
                    $"On the escape front at {zoom:0.0e+0} the engine must stay within {budget} pixels " +
                    $"of the exact reference: {differing}/{total} differ.");
            }
        }

        // 8. Ребазирование должно оставаться редким событием, а не срабатывать на каждом шаге.
        //    Условие «не хуже» в TryRebase существует именно для этого: у Феникса перенос в
        //    начало орбиты обычно увеличивает вторую компоненту пары (там z₋₁ = 0), и если
        //    условие сломается, δ начнёт переноситься туда, где оно только растёт. Порог взят
        //    с большим запасом от измеренного (около полутора процентов шагов).
        {
            PhoenixState state = AtExactCenter(
                View(1.21e24, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                "0.3605697876341732992373776850660347", "0.9162050700524670649253017882362955");
            PhoenixRenderer.RebaseCountForTests = 0;
            await RenderAsync(state, true, w, h);
            long rebases = Interlocked.Read(ref PhoenixRenderer.RebaseCountForTests);
            long steps = (long)total * state.Iterations;
            Console.WriteLine($"[diag] phoenix rebases {rebases} over {steps} steps " +
                $"({rebases * 100.0 / steps:F2}%)");
            Check(rebases * 10 <= steps,
                $"Phoenix rebasing must stay a rare event, not a per-step one: {rebases} rebases " +
                $"over {steps} steps.");
        }

        // 9. Круг «окно → сохранение → окно»: точный центр обязан пережить его без потерь.
        //    Именно здесь легко потерять глубину — decimal-поля состояния сохраняют лишь 28
        //    знаков, и если окно перестанет писать или читать строки, зум просто вернётся к
        //    прежнему потолку, а рендер останется формально исправным.
        {
            // Разметка окна тянет стили из App.xaml, а проверочный Application создаётся
            // пустым: без словаря конструктор падает на StaticResource.
            var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
            if (Application.Current.Resources.MergedDictionaries.All(d => d.Source != themeStyles))
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });

            var window = new PhoenixWindow();
            PhoenixState deep = AtExactCenter(
                View(1e20, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300),
                "0.3605697876341732992373776850660347", "0.9162050700524670649253017882362955");
            window.LoadState(deep);
            PhoenixState captured = window.CaptureState("round-trip");

            // Сравнение по значению, а не по тексту: BigFloat печатает столько цифр, сколько
            // несёт его мантисса, поэтому строка на выходе длиннее исходной, обозначая то же
            // число. Важно, что оно не изменилось, а не как оно записано.
            Check(captured.CenterXExact is { Length: > 0 } && captured.CenterYExact is { Length: > 0 },
                "A deep save must carry exact-center strings.");
            Check(BigFloat.Parse(captured.CenterXExact!) == BigFloat.Parse(deep.CenterXExact!) &&
                  BigFloat.Parse(captured.CenterYExact!) == BigFloat.Parse(deep.CenterYExact!),
                "The window must round-trip the exact center of a deep save without losing digits.");
            Check(captured.Zoom == deep.Zoom, "The window must round-trip a deep zoom unchanged.");

            // И обратно: обычное сохранение с мелким зумом не должно обзаводиться строками
            // точного центра — иначе они начнут расходиться с decimal-полями.
            // Сверхглубокий зум: центр длиннее тысячи знаков. Окно обязано разобрать его с
            // точностью, поднятой под зум (иначе BigFloat.Parse округлит до рабочих 384 бит), и
            // вернуть без потерь, а зум — вне диапазона double — сохранить как есть.
            string longX, longY;
            using (new BigFloat.PrecisionScope(2048))
            {
                longX = (BigFloat.FromInt(2) + (4.0 / FloatExp.Pow10(500) * 0.123456789).ToBigFloat()).ToInvariantString();
                longY = (4.0 / FloatExp.Pow10(500) * -0.0371).ToBigFloat().ToInvariantString();
            }
            PhoenixState extreme = AtExactCenter(
                View(1, PhoenixVariant.Classic, PhoenixColoringMode.Smooth, iterations: 300), longX, longY);
            extreme.Zoom = FloatExp.Pow10(500);
            window.LoadState(extreme);
            PhoenixState capturedExtreme = window.CaptureState("round-trip-extreme");
            Check(capturedExtreme.Zoom == extreme.Zoom, "The window must round-trip a 1e500 zoom unchanged.");
            // Окно разбирает центр с точностью под зум, а не с 2048 битами, поэтому сравнение
            // «бит в бит» здесь неверно; требование — потеря заведомо меньше пикселя.
            using (new BigFloat.PrecisionScope(2048))
            {
                FloatExp pixelFraction = 4.0 / extreme.Zoom * Math.ScaleB(1.0, -60);
                FloatExp driftX = FloatExp.Abs(FloatExp.FromBigFloat(BigFloat.Parse(capturedExtreme.CenterXExact!) - BigFloat.Parse(longX)));
                FloatExp driftY = FloatExp.Abs(FloatExp.FromBigFloat(BigFloat.Parse(capturedExtreme.CenterYExact!) - BigFloat.Parse(longY)));
                Check(driftX < pixelFraction && driftY < pixelFraction,
                    $"The window must round-trip a 1e500 exact center to far below a pixel: drift {driftX}, {driftY}.");
            }

            window.LoadState(View(700, PhoenixVariant.Classic, PhoenixColoringMode.Smooth));
            PhoenixState shallow = window.CaptureState("round-trip-shallow");
            Check(shallow.CenterXExact is null && shallow.CenterYExact is null,
                "A shallow save must not carry exact-center strings.");
            window.Close();
        }

        // 10. Параметрическая плоскость при b > 0. Опорная константа C1 в этой плоскости — центр
        //     кадра, и член C1·ΔG возмущения обязан её учитывать. Ядро долго подставляло сюда
        //     ноль: при b = 0 ΔG ≡ 0 и ошибка не видна, поэтому проверка 1 (b = 0) её пропускала,
        //     а на кадрах со структурой при b = 1 расходилось 2357 пикселей из 2816.
        foreach ((int secondary, double centerX, double centerY) in new[] { (1, -1.6, -0.2), (2, 0.2, 0.0) })
        {
            PhoenixState state = View(20, PhoenixVariant.Classic, PhoenixColoringMode.Smooth,
                PhoenixPlaneMode.ParameterC1, secondaryPower: secondary, centerX: centerX, centerY: centerY);
            byte[] shallow = await RenderAsync(state, false, w, h);
            byte[] deep = await RenderAsync(state, true, w, h);
            int differing = CountDiffering(shallow, deep);
            Check(CountEdges(shallow, w) > 150,
                $"The b={secondary} parameter-plane fixture must show structure, or the check proves nothing.");
            Check(differing * 100 <= total * 2,
                $"Phoenix parameter plane with b={secondary} must match the plain path: {differing}/{total} differ.");
        }

        // Гибридное ядро и линейный пропуск (PhoenixRenderer.Extended) — отдельной функцией:
        // её же запускает группа extreme.
        await VerifyPhoenixExtremeZoomAsync();

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Phoenix deep-zoom checks must leave the working precision restored.");
    }

    // Сверхглубокий зум Феникса: зум и сетка кадра в FloatExp, гибридное ядро (δ в FloatExp,
    // пока мало, дальше double) и линейный пропуск начала орбиты (PhoenixRenderer.Extended).
    //
    // Прежний потолок 1e24 держался на кадре «весь кадр вылетает за один шаг» (проверка 7 в
    // VerifyPhoenixDeepZoomAsync): там разброс |z|² по кадру на шаге решения меньше 2⁻⁵²
    // относительно, и такой кадр не различает никакой рендер, ведущий z в double. На кадрах со
    // структурой точность пертурбации относительная и с глубиной не падает — поэтому здесь
    // проверяется не «насколько глубоко можно», а три механизма, которыми снята стена
    // представления чисел, и полное совпадение с точным BigFloat-эталоном на 1e300/1e1000.
    //
    // Глубокие фикстуры со структурой на любой глубине строятся без поиска и без длинных
    // констант — это отталкивающие неподвижные точки, лежащие на границе:
    //   • β-точка «базилики» (C2 = 0, c1 = −1): z* = (1 + √5)/2, множитель 3.24;
    //   • седло Феникса с памятью (c1 = −1, C2 = −0.5): пара (2, 2) неподвижна, z₋₁ = 2 точно
    //     представим в decimal, собственные числа 2 ± √3.5. Это единственная фикстура, где
    //     на сверхглубине работает и член памяти C2·δₙ₋₁.
    private static bool _phoenixExtremeVerified;

    private static async Task VerifyPhoenixExtremeZoomAsync()
    {
        _phoenixExtremeVerified = true;
        static MandelbrotPalette Palette() => new()
        {
            Colors = [Colors.White, Colors.Red, Colors.Black],
            InteriorColor = Colors.Black,
            IsGradient = true
        };

        static PhoenixState State(FloatExp zoom, string centerX, string centerY, int iterations,
            PhoenixVariant variant = PhoenixVariant.Classic, PhoenixColoringMode coloring = PhoenixColoringMode.Smooth,
            PhoenixPlaneMode plane = PhoenixPlaneMode.Julia, decimal c1 = 0.56667m, decimal c2 = -0.5m,
            decimal initialPrevious = 0m)
        {
            var state = new PhoenixState
            {
                Zoom = zoom, Iterations = iterations, Threshold = 4m, C1Real = c1, C2Real = c2,
                InitialPreviousReal = initialPrevious, PlaneMode = plane, Variant = variant,
                PrimaryPower = 2, SecondaryPower = 0, ColoringMode = coloring,
                OrbitTrapMode = PhoenixOrbitTrapMode.Circle, OrbitTrapRadius = 0.5, OrbitTrapStrength = 1.5,
                StripeFrequency = 3, StripeStrength = 0.65, CycleTolerance = 1e-7, MaximumDetectedPeriod = 32,
                Palette = Palette(),
                CenterXExact = centerX, CenterYExact = centerY
            };
            using (new BigFloat.PrecisionScope(Math.Max(BigFloat.MinimumPrecisionBits, (int)zoom.Log2() + 128)))
            {
                state.CenterX = BigFloat.Parse(centerX).ToDecimalClamped();
                state.CenterY = BigFloat.Parse(centerY).ToDecimalClamped();
            }
            return state;
        }

        static async Task<byte[]> RenderAsync(PhoenixState state, int width, int height,
            bool? forceDeep = true, bool? forceExtended = null, bool? forceSkip = null)
        {
            byte[] pixels = new byte[width * height * 4];
            PhoenixRenderer.ForceDeepZoomForTests = forceDeep;
            PhoenixRenderer.ForceExtendedDeltaForTests = forceExtended;
            PhoenixRenderer.ForceLinearSkipForTests = forceSkip;
            try
            {
                await Task.Run(() => PhoenixRenderer.Render(state, pixels, width, height, width * 4,
                    Environment.ProcessorCount, CancellationToken.None));
            }
            finally
            {
                PhoenixRenderer.ForceDeepZoomForTests = null;
                PhoenixRenderer.ForceExtendedDeltaForTests = null;
                PhoenixRenderer.ForceLinearSkipForTests = null;
            }
            return pixels;
        }

        static int CountDiffering(byte[] a, byte[] b)
        {
            int differing = 0;
            for (int offset = 0; offset < a.Length; offset += 4)
                if (a[offset] != b[offset] || a[offset + 1] != b[offset + 1] || a[offset + 2] != b[offset + 2])
                    differing++;
            return differing;
        }

        static int CountColors(byte[] pixels) => Enumerable.Range(0, pixels.Length / 4)
            .Select(pixel => pixels[pixel * 4] | pixels[pixel * 4 + 1] << 8 | pixels[pixel * 4 + 2] << 16)
            .Distinct().Count();

        const int w = 64, h = 44, total = w * h;

        // 1. Полоса перекрытия: там, где верно и прежнее double-ядро, гибридное обязано совпасть
        //    с ним бит-в-бит без пропуска (в double-режиме это те же выражения, а FloatExp
        //    округляет мантиссу ровно как double) и почти бит-в-бит с пропуском. Кадры — со
        //    структурой (132–1178 рёбер в VerifyPhoenixDeepZoomAsync), все варианты свёрток и
        //    режимы окраски, читающие метрики по всей орбите: пропуск подменяет их префиксами
        //    опорной орбиты.
        (string X, string Y, double Zoom)[] overlap =
        [
            ("0.3605697876341732990634970411179978", "0.9162050700524670653999492733755535", 1.15e18),
            ("0.3605697876341732992373786585380622", "0.9162050700524670649253011924481502", 1.98e28),
        ];
        int worstPlain = 0, worstSkip = 0;
        foreach ((string centerX, string centerY, double zoom) in overlap)
        foreach (PhoenixVariant variant in Enum.GetValues<PhoenixVariant>())
        foreach (PhoenixColoringMode coloring in new[]
                 {
                     PhoenixColoringMode.Smooth, PhoenixColoringMode.OrbitTrap, PhoenixColoringMode.StripeAverage,
                     PhoenixColoringMode.TriangleInequalityAverage, PhoenixColoringMode.Period
                 })
        {
            PhoenixState state = State(zoom, centerX, centerY, 300, variant, coloring);
            byte[] reference = await RenderAsync(state, w, h, true, false);
            byte[] stepped = await RenderAsync(state, w, h, true, true, false);
            byte[] skipped = await RenderAsync(state, w, h, true, true, true);
            int plain = CountDiffering(reference, stepped), skip = CountDiffering(reference, skipped);
            worstPlain = Math.Max(worstPlain, plain);
            worstSkip = Math.Max(worstSkip, skip);
            Check(plain == 0,
                $"The hybrid kernel must match the double kernel bit-for-bit where both are valid " +
                $"({variant}, {coloring}, {zoom:0.0e+0}): {plain}/{total} differ.");
            Check(skip * 100 <= total,
                $"The linear skip must stay within 1% of the double kernel ({variant}, {coloring}, {zoom:0.0e+0}): " +
                $"{skip}/{total} differ.");
        }
        Console.WriteLine($"[diag] phoenix hybrid overlap worst: stepped {worstPlain}, skipped {worstSkip} of {total}");

        // 2. Сверхглубина против точного эталона. Эталон итерирует каждый пиксель напрямую в
        //    BigFloat и не делит с гибридным ядром ни пропуска, ни FloatExp-арифметики.
        string betaX;
        using (new BigFloat.PrecisionScope(4096))
            betaX = ((BigFloat.One + BigFloat.Sqrt(BigFloat.FromInt(5))) / 2L).ToInvariantString();
        foreach (int digits in new[] { 300, 1000 })
        foreach (bool memory in new[] { false, true })
        {
            FloatExp zoom = FloatExp.Pow10(digits);
            int iterations = digits * 2 + 400;
            PhoenixState state = memory
                ? State(zoom, "2", "0", iterations, c1: -1m, c2: -0.5m, initialPrevious: 2m)
                : State(zoom, betaX, "0", iterations, c1: -1m, c2: 0m);
            string label = $"{(memory ? "saddle" : "beta")} 1e{digits}";

            PhoenixRenderer.SkippedIterationsForTests = 0;
            byte[] frame = await RenderAsync(state, w, h, forceDeep: null);
            long skippedPerPixel = Interlocked.Read(ref PhoenixRenderer.SkippedIterationsForTests) / total;
            int colors = CountColors(frame);
            Console.WriteLine($"[diag] phoenix extreme {label}: {colors} colors, skip {skippedPerPixel}/{iterations} per pixel");
            Check(colors >= 5,
                $"The {label} Phoenix frame must show structure, or the comparison proves nothing: {colors} colors.");
            Check(skippedPerPixel > iterations / 4,
                $"The linear skip must engage at {label}: {skippedPerPixel} iterations per pixel.");
            Check(frame.Where((_, index) => index % 4 == 3).All(alpha => alpha == 255),
                $"The {label} Phoenix frame must fill every pixel.");

            const int sw = 32, sh = 22;
            byte[] deep = await RenderAsync(state, sw, sh, forceDeep: null);
            byte[] stepped = await RenderAsync(state, sw, sh, true, true, false);
            byte[] exact = await Task.Run(() =>
                PhoenixRenderer.RenderExactReferenceForTests(state, sw, sh, 64, CancellationToken.None));
            int vsExact = CountDiffering(deep, exact), vsStepped = CountDiffering(deep, stepped);
            Console.WriteLine($"[diag] phoenix extreme {label} 32x22: {vsExact} vs exact, {vsStepped} skip-vs-stepped");
            Check(CountColors(exact) >= 4, $"The {label} exact reference must show structure.");
            Check(vsExact <= 1,
                $"Phoenix at {label} must match the exact BigFloat reference: {vsExact}/{sw * sh} differ.");
            Check(vsStepped <= 1,
                $"The linear skip must match the stepped hybrid kernel at {label}: {vsStepped}/{sw * sh} differ.");
        }

        // 3. Тайл на сверхглубине совпадает с полным кадром: у тайла своя точка входа и раскладка,
        //    а таблица пропуска у них общая из кэша.
        {
            PhoenixState state = State(FloatExp.Pow10(1000), "2", "0", 2400, c1: -1m, c2: -0.5m, initialPrevious: 2m);
            byte[] full = await RenderAsync(state, w, h, forceDeep: null);
            byte[]? tile = await Task.Run(() => PhoenixRenderer.RenderTile(state, w, h,
                new MandelbrotRenderTile(16, 12, 24, 16, 1, 1), CancellationToken.None));
            Check(tile is not null, "The extreme-zoom Phoenix tile must render.");
            int tileDiffering = 0;
            for (int localY = 0; localY < 16; localY++)
            for (int localX = 0; localX < 24; localX++)
            {
                int tileOffset = (localY * 24 + localX) * 4;
                int fullOffset = ((12 + localY) * w + 16 + localX) * 4;
                if (tile![tileOffset] != full[fullOffset] || tile[tileOffset + 1] != full[fullOffset + 1] ||
                    tile[tileOffset + 2] != full[fullOffset + 2]) tileDiffering++;
            }
            Check(tileDiffering == 0,
                $"The extreme-zoom Phoenix tile must match the full frame exactly: {tileDiffering}/384 differ.");
        }

        // 4. Зум за пределами double переживает JSON-сохранение.
        {
            PhoenixState state = State(FloatExp.Pow10(777), "2", "0", 500, c1: -1m, c2: -0.5m, initialPrevious: 2m);
            string json = System.Text.Json.JsonSerializer.Serialize(new List<PhoenixState> { state }, JsonOptionsFactory.Create());
            PhoenixState restored = System.Text.Json.JsonSerializer.Deserialize<List<PhoenixState>>(json, JsonOptionsFactory.Create())![0];
            Check(restored.Zoom == state.Zoom, $"A 1e777 Phoenix zoom must survive JSON: {restored.Zoom}.");
            PhoenixState legacy = System.Text.Json.JsonSerializer.Deserialize<List<PhoenixState>>(
                "[{\"Zoom\": 1.5e20, \"Iterations\": 300}]", JsonOptionsFactory.Create())![0];
            Check(legacy.Zoom == FloatExp.FromDouble(1.5e20), "An old numeric Phoenix zoom must still load.");
        }

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Phoenix extreme-zoom checks must leave the working precision restored.");
    }
}
