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

// Глубокий и сверхглубокий зум бассейнов Ньютона.
internal static partial class Program
{
    // Бассейны Ньютона: пертурбационный движок по произвольной формуле (Newton, Halley,
    // Householder, Relaxed в плоскостях z и λ, обычная и диагностическая раскраска), зум до 1e1000.
    private static async Task VerifyNewtonDeepZoomAsync()
    {
        static NewtonPoolsEngine Engine(string formula, NewtonIterationMethod method, FloatExp zoom,
            string centerX, string centerY, int iterations = 300,
            NewtonDiagnosticColoringMode diagnostics = NewtonDiagnosticColoringMode.Disabled,
            NewtonRelaxedPlaneMode plane = NewtonRelaxedPlaneMode.ZPlane, bool gradient = true)
        {
            var engine = new NewtonPoolsEngine
            {
                MaxIterations = iterations,
                CenterX = BigFloat.Parse(centerX).ToDouble(),
                CenterY = BigFloat.Parse(centerY).ToDouble(),
                CenterXExact = centerX,
                CenterYExact = centerY,
                Zoom = zoom,
                IterationMethod = method,
                HouseholderOrder = 3,
                RelaxedPlaneMode = plane,
                Relaxation = new Complex(0.8, 0.3),
                FixedInitialZ = new Complex(0.5, 0.5),
                RootTolerance = 1e-6,
                DiagnosticColoringMode = diagnostics,
                BackgroundColor = Colors.Black,
                UseGradient = gradient
            };
            Check(engine.SetFormula(formula, out string debug), $"Newton fixture formula {formula} must parse: {debug}");
            engine.RootColors = [Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow, Colors.Cyan, Colors.Magenta, Colors.Orange, Colors.White];
            return engine;
        }

        static async Task<byte[]> RenderAsync(NewtonPoolsEngine engine, bool? forceDeep, int width, int height,
            int? forceBits = null, double? handoff = null, bool? skip = null)
        {
            byte[] pixels = new byte[width * height * 4];
            NewtonPoolsEngine.ForceDeepZoomForTests = forceDeep;
            NewtonPoolsEngine.ForceReferenceBitsForTests = forceBits;
            NewtonPoolsEngine.ForceHandoffRatioSquaredForTests = handoff;
            NewtonPoolsEngine.ForceLinearSkipForTests = skip;
            try
            {
                await Task.Run(() => engine.RenderToBuffer(pixels, width, height, width * 4, Environment.ProcessorCount,
                    CancellationToken.None));
            }
            finally
            {
                NewtonPoolsEngine.ForceDeepZoomForTests = null;
                NewtonPoolsEngine.ForceReferenceBitsForTests = null;
                NewtonPoolsEngine.ForceHandoffRatioSquaredForTests = null;
                NewtonPoolsEngine.ForceLinearSkipForTests = null;
            }
            return pixels;
        }

        static int CountDiffering(byte[] a, byte[] b)
        {
            int differing = 0;
            for (int offset = 0; offset < a.Length; offset += 4)
                if (a[offset] != b[offset] || a[offset + 1] != b[offset + 1] || a[offset + 2] != b[offset + 2]) differing++;
            return differing;
        }

        static int CountEdges(byte[] pixels, int width)
        {
            int edges = 0;
            for (int offset = 4; offset < pixels.Length; offset += 4)
            {
                if (offset / 4 % width == 0) continue;
                if (pixels[offset] != pixels[offset - 4] || pixels[offset + 1] != pixels[offset - 3] ||
                    pixels[offset + 2] != pixels[offset - 2]) edges++;
            }
            return edges;
        }

        // Отталкивающий 2-цикл отображения Ньютона N(z) = z − f/f′: N(N(z*)) = z*. Множество Жюлиа
        // вокруг него самоподобно, поэтому кадр со структурой есть на любой глубине. Точка
        // уточняется методом Ньютона для N∘N − z прямо в BigFloat от double-приближения.
        static (string X, string Y) RepellingTwoCycle(string formula, Complex seed, int bits)
        {
            ExpressionNode node = new Parser(new Tokenizer(formula).Tokenize()).Parse().Simplify();
            ExpressionNode first = node.Differentiate("z").Simplify();
            CompiledComplexExpression f = CompiledComplexExpression.Compile(node);
            CompiledComplexExpression g = CompiledComplexExpression.Compile(first);
            CompiledComplexExpression h = CompiledComplexExpression.Compile(first.Differentiate("z").Simplify());
            using var precision = new BigFloat.PrecisionScope(bits);
            var fValues = new ComplexBigFloat[f.InstructionCount];
            var gValues = new ComplexBigFloat[g.InstructionCount];
            var hValues = new ComplexBigFloat[h.InstructionCount];
            ComplexBigFloat Map(ComplexBigFloat z) => z - f.EvaluateBig(z, fValues, default) / g.EvaluateBig(z, gValues, default);
            ComplexBigFloat Slope(ComplexBigFloat z)
            {
                ComplexBigFloat derivative = g.EvaluateBig(z, gValues, default);
                return f.EvaluateBig(z, fValues, default) * h.EvaluateBig(z, hValues, default) / (derivative * derivative);
            }

            ComplexBigFloat point = ComplexBigFloat.FromDouble(seed.Real, seed.Imaginary);
            for (int step = 0; step < 40; step++)
            {
                ComplexBigFloat image = Map(point);
                ComplexBigFloat correction = (Map(image) - point) / (Slope(image) * Slope(point) - 1);
                point -= correction;
                if (Math.Max(correction.Real.BinaryExponent, correction.Imaginary.BinaryExponent) < -(bits - 32)) break;
            }
            ComplexBigFloat residual = Map(Map(point)) - point;
            Check(Math.Max(residual.Real.BinaryExponent, residual.Imaginary.BinaryExponent) < -(bits - 64),
                $"The {formula} two-cycle must converge in BigFloat.");
            Check((Map(point) - point).ToComplex().Magnitude > 1e-3, $"The {formula} two-cycle must not be a fixed point.");
            return (point.Real.ToInvariantString(), point.Imaginary.ToInvariantString());
        }

        // 1. Где double ещё точен, глубокий движок обязан воспроизвести плоскую ступень. Порог
        //    передачи в double поднят до 2⁻⁸, поэтому пертурбация ведёт почти всю орбиту, а δ
        //    при этом не вырастает до порядка самих значений (там сокращение больших слагаемых
        //    теряло бы разряды у обеих ступеней по-разному). У двух путей нет общего кода, кроме
        //    проверок и окраски, — это и есть проверка переноса всех операций и методов.
        var shallow = new (string Formula, NewtonIterationMethod Method, NewtonRelaxedPlaneMode Plane,
            NewtonDiagnosticColoringMode Diagnostics, double X, double Y, double Zoom)[]
        {
            ("z^3-1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, -0.2, 0.3, 8),
            ("z^3-1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.OrbitOutcome, -0.2, 0.3, 8),
            ("z^3-1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Residual, -0.2, 0.3, 8),
            ("z^6+3*z^3-2", NewtonIterationMethod.Halley, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.1, 0.2, 5),
            ("z^6+3*z^3-2", NewtonIterationMethod.Halley, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.OrbitOutcome, 0.1, 0.2, 5),
            ("(z^2-1)/(z^2+1)", NewtonIterationMethod.Householder, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.3, 0.1, 3),
            ("z^4-1", NewtonIterationMethod.Householder, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.OrbitOutcome, 0.3, 0.1, 3),
            ("z^3-1", NewtonIterationMethod.RelaxedNewton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.1, 0.1, 4),
            ("z^3-1", NewtonIterationMethod.RelaxedNewton, NewtonRelaxedPlaneMode.LambdaPlane, NewtonDiagnosticColoringMode.Disabled, 1.0, 0.2, 6),
            ("z^3-1", NewtonIterationMethod.RelaxedNewton, NewtonRelaxedPlaneMode.LambdaPlane, NewtonDiagnosticColoringMode.OrbitOutcome, 1.0, 0.2, 6),
            ("sin(z)-0.5", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 1.0, 0.5, 2),
            ("z-exp(-z)", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, -1.0, 2.0, 1),
            ("tan(z)-z", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 2.0, 0.5, 1),
            ("log(z)-1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, -1.0, 0.3, 1),
            ("sqrt(z)-z^3", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.0, 0.5, 1),
            ("atan(z^3)-0.5", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.5, 0.5, 1),
            ("asin(z^4)-0.5", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.5, 0.5, 1),
            ("sinh(z)-1", NewtonIterationMethod.Halley, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.5, 1.5, 1),
            ("tanh(z)-0.5", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.5, 1.5, 1),
            ("z^0.5*z^2.5-1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.2, 0.3, 2),
            ("z^(-2)+z-1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled, 0.2, 0.3, 2),
        };
        const int sw = 64, sh = 44;
        foreach (var fixture in shallow)
        {
            string x = fixture.X.ToString("R", CultureInfo.InvariantCulture);
            string y = fixture.Y.ToString("R", CultureInfo.InvariantCulture);
            NewtonPoolsEngine Make() => Engine(fixture.Formula, fixture.Method, fixture.Zoom, x, y, 500,
                fixture.Diagnostics, fixture.Plane);
            byte[] plain = await RenderAsync(Make(), false, sw, sh);
            byte[] deep = await RenderAsync(Make(), true, sw, sh, handoff: Math.ScaleB(1.0, -16));
            int differing = CountDiffering(plain, deep);
            Check(differing * 200 <= sw * sh,
                $"Newton perturbation must match the plain double path ({fixture.Formula}, {fixture.Method}, " +
                $"{fixture.Plane}, {fixture.Diagnostics}): {differing}/{sw * sh} pixels differ.");
        }
        Console.WriteLine($"[diag] newton shallow-vs-deep: {shallow.Length} configurations match");

        const int w = 32, h = 24, total = w * h;

        // 2. На глубине плоской ступени верить нельзя — сравнение с прямой итерацией каждого
        //    пикселя в BigFloat. z³ − 1 у отталкивающего 2-цикла (множитель ровно 6). Центральный
        //    пиксель лежит на самом цикле, и его судьбу решает округление — отсюда допуск в пиксель.
        //    Эталон с комплексным делением в BigFloat дорог, поэтому на 1e1000 кадр крошечный, а
        //    весь кадр там проверяется самоподобием (пункт 5).
        (string cycleX, string cycleY) = RepellingTwoCycle("z^3-1", new Complex(0.538608672507971, 0.417204483749251), 3648);
        foreach ((string zoomText, int iterations, int width, int height) in new[] { ("1e20", 300, w, h), ("1e300", 1200, w, h), ("1e1000", 3500, 6, 4) })
        {
            FloatExp zoom = FloatExp.Parse(zoomText);
            NewtonPoolsEngine.SkippedIterationsForTests = 0;
            NewtonPoolsEngine.CountSkippedIterationsForTests = true;
            byte[] deep;
            try { deep = await RenderAsync(Engine("z^3-1", NewtonIterationMethod.Newton, zoom, cycleX, cycleY, iterations), true, width, height); }
            finally { NewtonPoolsEngine.CountSkippedIterationsForTests = false; }
            long skipped = Interlocked.Read(ref NewtonPoolsEngine.SkippedIterationsForTests);
            NewtonPoolsEngine exactEngine = Engine("z^3-1", NewtonIterationMethod.Newton, zoom, cycleX, cycleY, iterations);
            byte[] exact = await Task.Run(() => exactEngine.RenderExactReferenceForTests(width, height, 64, CancellationToken.None));
            int differing = CountDiffering(deep, exact);
            int edges = CountEdges(exact, width);
            Console.WriteLine($"[diag] newton z^3-1 at {zoomText}: {differing}/{width * height} differ, {edges} edges, {skipped} skipped iterations");
            Check(skipped > 0, $"The linear skip must engage at {zoomText}.");
            Check(differing <= 1, $"Newton deep zoom must match the exact reference at {zoomText}: {differing}/{width * height} differ.");
            if (width == w) Check(edges > 60, $"The z^3-1 two-cycle frame at {zoomText} must show structure: {edges} edges.");
            if (zoomText == "1e300")
            {
                byte[] unskipped = await RenderAsync(Engine("z^3-1", NewtonIterationMethod.Newton, zoom, cycleX, cycleY, iterations),
                    true, w, h, skip: false);
                Check(CountDiffering(unskipped, exact) <= 1,
                    "Without the linear skip the step-by-step kernel must match the reference while δ still fits double.");
            }
        }

        // 3. Трансцендентная формула: опорная орбита требует cos/sin произвольной точности.
        (string cosX, string cosY) = RepellingTwoCycle("cos(z)-z", new Complex(-0.570836212742015, -0.968967177259400), 512);
        foreach ((string zoomText, int iterations) in new[] { ("1e40", 300) })
        {
            FloatExp zoom = FloatExp.Parse(zoomText);
            byte[] deep = await RenderAsync(Engine("cos(z)-z", NewtonIterationMethod.Newton, zoom, cosX, cosY, iterations), true, w, h);
            NewtonPoolsEngine exactEngine = Engine("cos(z)-z", NewtonIterationMethod.Newton, zoom, cosX, cosY, iterations);
            byte[] exact = await Task.Run(() => exactEngine.RenderExactReferenceForTests(w, h, 64, CancellationToken.None));
            int differing = CountDiffering(deep, exact);
            Console.WriteLine($"[diag] newton cos(z)-z at {zoomText}: {differing}/{total} differ, {CountEdges(exact, w)} edges");
            Check(CountEdges(exact, w) > 60, $"The cos(z)-z frame at {zoomText} must show structure.");
            Check(differing * 100 <= total * 2, $"Transcendental Newton deep zoom must match the exact reference at {zoomText}: {differing}/{total} differ.");
        }

        // 4. Остальные методы и режимы на 1e30. Центры найдены спуском по границе бассейнов
        //    (шаг ×10, всякий раз к ближайшей к центру паре соседних пикселей разных корней).
        var methods = new (string Label, string Formula, NewtonIterationMethod Method, NewtonRelaxedPlaneMode Plane,
            NewtonDiagnosticColoringMode Diagnostics, string X, string Y)[]
        {
            ("Halley", "z^4-1", NewtonIterationMethod.Halley, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled,
                "0.37990808172817811191204123330806241141344351327125", "0.43494150585041415009061837942431348347333451525957"),
            ("Householder", "z^3-1", NewtonIterationMethod.Householder, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.Disabled,
                "-0.6008994764551517061752977045861167985988191002439", "0.00515030394938313118897024704315396473356942575383"),
            ("Relaxed λ-plane", "z^3-1", NewtonIterationMethod.RelaxedNewton, NewtonRelaxedPlaneMode.LambdaPlane, NewtonDiagnosticColoringMode.Disabled,
                "0.98190717272726090889187616725550228304191009628161", "0.19514139504866868699532215875719665538544613181438"),
            ("Relaxed λ-plane outcome", "z^3-1", NewtonIterationMethod.RelaxedNewton, NewtonRelaxedPlaneMode.LambdaPlane, NewtonDiagnosticColoringMode.OrbitOutcome,
                "0.98190717272726090889187616725550228304191009628161", "0.19514139504866868699532215875719665538544613181438"),
            ("orbit outcome", "z^5-z^2+1", NewtonIterationMethod.Newton, NewtonRelaxedPlaneMode.ZPlane, NewtonDiagnosticColoringMode.OrbitOutcome,
                "0.17959818181999800397470557686812243250064308499655", "0.36777685959594957991683114856036181685021462969821"),
        };
        foreach (var fixture in methods)
        {
            FloatExp zoom = FloatExp.Parse("1e30");
            NewtonPoolsEngine Make() => Engine(fixture.Formula, fixture.Method, zoom, fixture.X, fixture.Y, 900,
                fixture.Diagnostics, fixture.Plane);
            byte[] deep = await RenderAsync(Make(), true, w, h);
            NewtonPoolsEngine exactEngine = Make();
            byte[] exact = await Task.Run(() => exactEngine.RenderExactReferenceForTests(w, h, 64, CancellationToken.None));
            int differing = CountDiffering(deep, exact);
            int edges = CountEdges(exact, w);
            Console.WriteLine($"[diag] newton {fixture.Label} at 1e30: {differing}/{total} differ, {edges} edges");
            Check(edges > 40, $"The {fixture.Label} fixture must show structure at 1e30: {edges} edges.");
            Check(differing * 100 <= total, $"Newton {fixture.Label} deep zoom must match the exact reference: {differing}/{total} differ.");
        }

        // 5. Самоподобие на 1e1000: множитель 2-цикла — ровно 6, поэтому два шага итерации
        //    переводят сетку кадра на зуме 6·Z в сетку кадра на Z пиксель в пиксель. При сплошных
        //    цветах корней (номер итерации не участвует) кадры обязаны совпасть — независимая от
        //    эталона проверка всей сверхглубины: пропуск и точность ведутся на разных зумах по-разному.
        {
            FloatExp deeper = FloatExp.Pow10(1000);
            byte[] near = await RenderAsync(Engine("z^3-1", NewtonIterationMethod.Newton, deeper, cycleX, cycleY, 3600, gradient: false), true, w, h);
            byte[] far = await RenderAsync(Engine("z^3-1", NewtonIterationMethod.Newton, deeper / 6.0, cycleX, cycleY, 3600, gradient: false), true, w, h);
            int differing = CountDiffering(near, far);
            int edges = CountEdges(near, w);
            Console.WriteLine($"[diag] newton self-similarity at 1e1000: {differing}/{total} differ, {edges} edges");
            Check(edges > 60, "The 1e1000 self-similarity frame must show structure.");
            Check(differing <= 1, $"Frames at 1e1000 and 1e1000/6 around the two-cycle must coincide: {differing}/{total} differ.");
        }

        // 6. План точности: избыточная разрядность опорной орбиты не меняет кадр. Центр сдвинут на
        //    треть пикселя с цикла, чтобы ни один пиксель не лежал на нём ровно — судьба такой
        //    точки зависит от округления при любой разрядности.
        {
            FloatExp zoom = FloatExp.Parse("1e150");
            string offsetX;
            using (new BigFloat.PrecisionScope(1024))
                offsetX = (BigFloat.Parse(cycleX) + (3.0 / zoom / w / 3).ToBigFloat()).ToInvariantString();
            NewtonPoolsEngine planned = Engine("z^3-1", NewtonIterationMethod.Newton, zoom, offsetX, cycleY, 600);
            byte[] plannedPixels = await RenderAsync(planned, true, w, h);
            byte[] generous = await RenderAsync(Engine("z^3-1", NewtonIterationMethod.Newton, zoom, offsetX, cycleY, 600), true, w, h,
                forceBits: planned.PlanReferenceBits() + 256);
            Check(CountEdges(plannedPixels, w) > 60, "The precision-plan frame must show structure.");
            Check(CountDiffering(plannedPixels, generous) == 0,
                "The Newton precision plan must not drift with 256 extra reference bits.");
        }

        // 7. Тайл прогрессивного предпросмотра совпадает с полным кадром.
        {
            FloatExp zoom = FloatExp.Parse("1e60");
            byte[] full = await RenderAsync(Engine("z^3-1", NewtonIterationMethod.Newton, zoom, cycleX, cycleY, 400), true, w, h);
            NewtonPoolsEngine tileEngine = Engine("z^3-1", NewtonIterationMethod.Newton, zoom, cycleX, cycleY, 400);
            NewtonPoolsEngine.ForceDeepZoomForTests = true;
            byte[]? tile;
            try
            {
                tile = await Task.Run(() => tileEngine.RenderTile(new MandelbrotRenderTile(8, 6, 16, 12, 1, 1), w, h, CancellationToken.None));
            }
            finally { NewtonPoolsEngine.ForceDeepZoomForTests = null; }
            int tileDiffering = 0;
            for (int localY = 0; localY < 12; localY++)
            for (int localX = 0; localX < 16; localX++)
            {
                int tileOffset = (localY * 16 + localX) * 4;
                int fullOffset = ((6 + localY) * w + 8 + localX) * 4;
                if (tile![tileOffset] != full[fullOffset] || tile[tileOffset + 1] != full[fullOffset + 1] ||
                    tile[tileOffset + 2] != full[fullOffset + 2]) tileDiffering++;
            }
            Check(tileDiffering == 0, $"A Newton deep-zoom tile must match the full frame exactly: {tileDiffering}/192 differ.");
        }

        // 8. Зум за пределами double переживает JSON, старое числовое поле читается.
        {
            var state = new NewtonState { Formula = "z^3-1", Zoom = FloatExp.Pow10(777), CenterXExact = cycleX, CenterYExact = cycleY };
            string json = System.Text.Json.JsonSerializer.Serialize(new List<NewtonState> { state }, JsonOptionsFactory.Create());
            NewtonState restored = System.Text.Json.JsonSerializer.Deserialize<List<NewtonState>>(json, JsonOptionsFactory.Create())![0];
            Check(restored.Zoom == state.Zoom && restored.CenterXExact == cycleX && restored.CenterYExact == cycleY,
                "A 1e777 Newton zoom and its exact center must survive JSON.");
            NewtonState legacy = System.Text.Json.JsonSerializer.Deserialize<List<NewtonState>>(
                "[{\"Formula\": \"z^3-1\", \"Zoom\": 45073244110.32, \"CenterX\": 0.1}]", JsonOptionsFactory.Create())![0];
            Check(legacy.Zoom == FloatExp.FromDouble(45073244110.32) && legacy.CenterXExact is null,
                "An old numeric Newton zoom must still load.");
        }

        // 9. Круг «окно → сохранение → окно» на 1e500: центр длиннее тысячи знаков разбирается
        //    с точностью под зум и возвращается без потерь, мелкий зум строк точного центра не заводит.
        {
            var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
            if (Application.Current.Resources.MergedDictionaries.All(d => d.Source != themeStyles))
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });

            var window = new NewtonPoolsWindow();
            NewtonState extreme = window.CaptureState("template");
            extreme.Zoom = FloatExp.Pow10(500);
            extreme.CenterXExact = cycleX;
            extreme.CenterYExact = cycleY;
            extreme.CenterX = BigFloat.Parse(cycleX).ToDouble();
            extreme.CenterY = BigFloat.Parse(cycleY).ToDouble();
            window.LoadState(extreme);
            NewtonState captured = window.CaptureState("round-trip");
            Check(captured.Zoom == extreme.Zoom, "The Newton window must round-trip a 1e500 zoom unchanged.");
            using (new BigFloat.PrecisionScope(3648))
            {
                FloatExp pixelFraction = 3.0 / extreme.Zoom * Math.ScaleB(1.0, -60);
                FloatExp driftX = FloatExp.Abs(FloatExp.FromBigFloat(BigFloat.Parse(captured.CenterXExact!) - BigFloat.Parse(cycleX)));
                FloatExp driftY = FloatExp.Abs(FloatExp.FromBigFloat(BigFloat.Parse(captured.CenterYExact!) - BigFloat.Parse(cycleY)));
                Check(driftX < pixelFraction && driftY < pixelFraction,
                    $"The Newton window must round-trip a 1e500 exact center to far below a pixel: drift {driftX}, {driftY}.");
            }

            NewtonState shallowState = window.CaptureState("template");
            shallowState.Zoom = FloatExp.FromDouble(700);
            shallowState.CenterXExact = null;
            shallowState.CenterYExact = null;
            window.LoadState(shallowState);
            NewtonState capturedShallow = window.CaptureState("round-trip-shallow");
            Check(capturedShallow.CenterXExact is null && capturedShallow.CenterYExact is null,
                "A shallow Newton save must not carry exact-center strings.");
            window.Close();
        }

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Newton deep-zoom checks must leave the working precision restored.");
    }
}
