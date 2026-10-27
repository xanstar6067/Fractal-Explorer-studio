using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;
using Point = System.Windows.Point;

internal static partial class Program
{
    // ---------------------------------------------------------------- deep viewport
    // Вид окон с глубоким зумом (Мандельброт, Ньютон, Nova, Phoenix, Коллатц): центр в decimal
    // или double, точный центр в BigFloat за порогом глубокого режима, зум.
    //
    //   deep-viewport [--record файл] [--compare файл]
    //
    // Каждое окно проходит один и тот же сценарий без показа на экране: загрузка сохранений
    // (мелкое, глубокое с точным центром, глубокое без него, с испорченной строкой, за
    // пределами зума), ввод зума в поле через порог глубокого режима вверх и вниз, сброс
    // вида. После каждого шага снимается состояние окна; --record пишет его, --compare
    // сверяет побайтово. Так переделка ведения вида проверяется на старом коде.

    private const string DeepViewportExactX =
        "-0.743643887037158704752191506114774215390162013718891817393218294300293609123456789012345678901234567";
    private const string DeepViewportExactY =
        "0.131825904205311970493132056385139863810350530045481439620143954712000420987654321098765432109876543";

    private static async Task VerifyDeepViewportAsync(string[] args)
    {
        string? record = ReadOption(args, "--record");
        string? compare = ReadOption(args, "--compare");
        EnsureWindowThemeStyles();

        (string Name, Func<Window> Create)[] windows =
        [
            ("mandelbrot", () => new MandelbrotWindow(MandelbrotVariant.Mandelbrot)),
            ("newton", () => new NewtonPoolsWindow()),
            ("nova", () => new NovaWindow(NovaVariant.Mandelbrot)),
            ("phoenix", () => new PhoenixWindow()),
            ("collatz", () => new CollatzWindow()),
            // Окна без глубокого режима: тот же вид с центром в double, порог не достигается.
            ("basin-muller", () => new BasinExplorerWindow(BasinExplorerKind.Muller)),
            ("domain-coloring", () => new DomainColoringWindow()),
        ];

        var lines = new List<string>();
        foreach ((string name, Func<Window> create) in windows)
        {
            Window window = create();
            try
            {
                void Snap(string step) => lines.Add($"{name}/{step} {DescribeViewState(window)}");
                void Load(string zoom, string? exactX, string? exactY, double centerX = 0.25, double centerY = -0.1)
                {
                    object state = CaptureWindowState(window);
                    SetZoomFromText(state, zoom);
                    SetNumber(state, "CenterX", centerX);
                    SetNumber(state, "CenterY", centerY);
                    state.GetType().GetProperty("CenterXExact")?.SetValue(state, exactX);
                    state.GetType().GetProperty("CenterYExact")?.SetValue(state, exactY);
                    window.GetType().GetMethod("LoadState", BindingFlags.Instance | BindingFlags.Public, [state.GetType()])!
                        .Invoke(window, [state]);
                }
                void TypeZoom(string text) => ((TextBox)window.FindName("ZoomBox")!).Text = text;

                Snap("default");
                Load("1000", null, null);
                Snap("shallow");
                TypeZoom("1e27");
                Snap("typed-engage");
                TypeZoom("100");
                Snap("typed-release");
                Load("1e20", DeepViewportExactX, DeepViewportExactY, -0.7436438870371587, 0.13182590420531197);
                Snap("exact-1e20");
                TypeZoom("1e40");
                Snap("exact-typed-1e40");
                TypeZoom("1000");
                Snap("exact-typed-1000");
                Load("1e40", DeepViewportExactX, DeepViewportExactY, -0.7436438870371587, 0.13182590420531197);
                Snap("exact-1e40");
                TypeZoom("5e8");
                Snap("exact-typed-5e8");
                Load("1e12", DeepViewportExactX, DeepViewportExactY, -0.7436438870371587, 0.13182590420531197);
                Snap("exact-1e12");
                Load("1e30", null, null, 0.123456789012345678, -0.98765432109876543);
                Snap("deep-without-exact");
                Load("1e30", "x1", "0.5");
                Snap("corrupt-exact");
                Load("1e300", DeepViewportExactX, DeepViewportExactY, -0.7436438870371587, 0.13182590420531197);
                Snap("exact-1e300");
                TypeZoom("1e900");
                Snap("typed-1e900");
                Load("1e-30", null, null);
                Snap("below-min");
                if (window.GetType().GetMethod("ResetView", BindingFlags.Instance | BindingFlags.NonPublic) is { } reset)
                {
                    Load("1e40", DeepViewportExactX, DeepViewportExactY);
                    reset.Invoke(window, reset.GetParameters().Length == 0 ? [] : [false]);
                    Snap("reset");
                }
                else if (window.GetType().GetMethod("ResetViewButton_OnClick", BindingFlags.Instance | BindingFlags.NonPublic) is { } resetButton)
                {
                    Load("1e40", DeepViewportExactX, DeepViewportExactY);
                    resetButton.Invoke(window, [window, new RoutedEventArgs()]);
                    Snap("reset");
                }
            }
            finally
            {
                window.Close();
                await DrainAsync();
            }
        }

        foreach (string line in lines) Console.WriteLine("  " + line);
        if (record is not null)
        {
            File.WriteAllLines(record, lines);
            Console.WriteLine($"Записано {lines.Count} состояний вида в {record}.");
        }
        if (compare is not null)
        {
            string[] expected = File.ReadAllLines(compare).Where(line => line.Length > 0).ToArray();
            var mismatches = new List<string>();
            for (int i = 0; i < Math.Max(expected.Length, lines.Count); i++)
            {
                string? before = i < expected.Length ? expected[i] : null;
                string? after = i < lines.Count ? lines[i] : null;
                if (before != after) mismatches.Add($"было: {before ?? "—"}\n    стало: {after ?? "—"}");
            }
            Check(mismatches.Count == 0, "Состояние вида изменилось:\n  " + string.Join("\n  ", mismatches));
            Console.WriteLine($"Все {lines.Count} состояний вида совпадают с эталоном {compare}.");
        }
        int navigationCases = VerifyDeepViewportNavigation();
        Console.WriteLine($"PASS (deep-viewport): {lines.Count} состояний вида в {windows.Length} окнах, " +
                          $"{navigationCases} проверок колеса, перетаскивания и предпросмотра.");
    }

    // ---------------------------------------------------------------- навигация вида
    // Колесо и перетаскивание окна читают настоящий курсор, поэтому их математика проверяется
    // на самом DeepZoomViewport: те же вызовы, что делают обработчики мыши, на глубинах от 1 до
    // 1e990 и через порог глубокого режима в обе стороны. Погрешности меряются в пикселях холста.

    private const double NavigationWidth = 980, NavigationHeight = 680, NavigationTolerancePixels = 1e-3;

    private static int VerifyDeepViewportNavigation()
    {
        int cases = 0;
        string[] zooms = ["1", "1e6", "1e9", "1.4e9", "1e24", "1e30", "1e300", "1e990"];
        foreach (string zoom in zooms)
        {
            cases += CheckNavigation("decimal 4/1.5e9", () => new DecimalDeepZoomViewport(4, 1e-6, FloatExp.Pow10(1000), 1.5e9),
                -0.7436438870371587m, 0.1318259042053120m, zoom);
            cases += CheckNavigation("decimal 3/1e25", () => new DecimalDeepZoomViewport(3, 0.01, FloatExp.Pow10(1000), 1e25, 1.5),
                -0.7436438870371587m, 0.1318259042053120m, zoom);
            cases += CheckNavigation("double 3/1.5e9", () => new DoubleDeepZoomViewport(3, 0.001, FloatExp.Pow10(1000), 1.5e9),
                -0.7436438870371587, 0.131825904205312, zoom);
        }
        Console.WriteLine($"  навигация вида: {cases} проверок без расхождений больше {NavigationTolerancePixels} пикселя сверх шага типа центра.");
        return cases;
    }

    private static int CheckNavigation<T>(string name, Func<DeepZoomViewport<T>> create, T centerX, T centerY, string zoomText)
        where T : struct
    {
        FloatExp.TryParse(zoomText, out FloatExp zoom);
        string label = $"{name}, зум {zoomText}";
        int cases = 0;
        Point[] points = [new(700, 200), new(13, 655), new(490, 340)];
        double[] factors = [1.2, 1 / 1.2, 10, 0.1, 1.05];

        // Колесо: мировая точка под курсором остаётся под ним, отрисованный кадр сдвигается так,
        // что та же точка кадра оказывается под курсором, а масштаб равен множителю зума.
        foreach (Point point in points)
        foreach (double factor in factors)
        {
            DeepZoomViewport<T> viewport = CreateAt(create, centerX, centerY, zoom);
            viewport.MarkRendered();
            bool deepBefore = viewport.IsDeep;
            (BigFloat beforeX, BigFloat beforeY) = WorldUnder(viewport, point);
            FloatExp beforeZoom = viewport.Zoom;
            viewport.ZoomAt(point, NavigationWidth, NavigationHeight, factor);
            (BigFloat afterX, BigFloat afterY) = WorldUnder(viewport, point);
            double tolerance = NavigationTolerance(viewport, shallow: !(deepBefore && viewport.IsDeep));
            CheckPixels(viewport, beforeX, afterX, tolerance, $"{label}: колесо ×{factor:G3} в {point} сдвинуло точку под курсором по X");
            CheckPixels(viewport, beforeY, afterY, tolerance, $"{label}: колесо ×{factor:G3} в {point} сдвинуло точку под курсором по Y");
            Check(viewport.IsDeep == viewport.Zoom >= viewport.DeepZoomThreshold, $"{label}: глубокий режим не совпал с порогом.");

            Check(viewport.TryGetPreviewTransform(NavigationWidth, out double scale, out double translationX, out double translationY),
                $"{label}: нет сдвига предпросмотра после колеса.");
            double expectedScale = (viewport.Zoom / beforeZoom).ToDouble();
            Check(Math.Abs(scale / expectedScale - 1) < 1e-12, $"{label}: масштаб предпросмотра {scale} вместо {expectedScale}.");
            double screenX = (point.X - NavigationWidth / 2) * scale + translationX;
            double screenY = (point.Y - NavigationHeight / 2) * scale + translationY;
            Check(Math.Abs(screenX - (point.X - NavigationWidth / 2)) < tolerance &&
                  Math.Abs(screenY - (point.Y - NavigationHeight / 2)) < tolerance,
                $"{label}: после колеса ×{factor:G3} кадр под курсором {point} уехал на ({screenX - point.X + NavigationWidth / 2}, {screenY - point.Y + NavigationHeight / 2}) px.");

            (T worldX, T worldY) = viewport.ScreenToWorld(point, NavigationWidth, NavigationHeight);
            CheckScreenToWorld(viewport, worldX, worldY, afterX, afterY, $"{label}: ScreenToWorld в {point}");
            cases += 4;
        }

        // Перетаскивание: кадр едет ровно за мышью, обратный путь возвращает центр.
        {
            DeepZoomViewport<T> viewport = CreateAt(create, centerX, centerY, zoom);
            viewport.MarkRendered();
            BigFloat startX = viewport.ExactCenterX, startY = viewport.ExactCenterY;
            Point[] path = [new(500, 300), new(531.5, 287), new(612, 410.25), new(380, 120), new(500.5, 299)];
            viewport.BeginPan(path[0]);
            foreach (Point point in path.Skip(1)) Check(viewport.PanTo(point, NavigationWidth), $"{label}: перетаскивание не идёт.");
            Check(viewport.TryGetPreviewTransform(NavigationWidth, out double scale, out double translationX, out double translationY),
                $"{label}: нет сдвига предпросмотра после перетаскивания.");
            Point last = path[^1];
            double tolerance = NavigationTolerance(viewport, shallow: !viewport.IsDeep);
            Check(Math.Abs(scale - 1) < 1e-15 && Math.Abs(translationX - (last.X - path[0].X)) < tolerance &&
                  Math.Abs(translationY - (last.Y - path[0].Y)) < tolerance,
                $"{label}: предпросмотр сдвинулся на ({translationX}, {translationY}) вместо ({last.X - path[0].X}, {last.Y - path[0].Y}).");
            foreach (Point point in path.Reverse().Skip(1)) viewport.PanTo(point, NavigationWidth);
            Check(viewport.EndPan() && !viewport.EndPan(), $"{label}: перетаскивание не закончилось.");
            CheckPixels(viewport, startX, viewport.ExactCenterX, tolerance, $"{label}: перетаскивание туда и обратно не вернуло центр по X");
            CheckPixels(viewport, startY, viewport.ExactCenterY, tolerance, $"{label}: перетаскивание туда и обратно не вернуло центр по Y");
            cases += 3;
        }

        // Один пиксель пана на любой глубине меняет точный центр ровно на ширину пикселя.
        {
            DeepZoomViewport<T> viewport = CreateAt(create, centerX, centerY, zoom);
            BigFloat startX = viewport.ExactCenterX;
            viewport.BeginPan(new Point(100, 100));
            viewport.PanTo(new Point(99, 100), NavigationWidth);
            using var precision = DeepZoomViewport<T>.PrecisionScope(viewport.Zoom);
            double moved = (FloatExp.FromBigFloat(viewport.ExactCenterX - startX) / (viewport.ViewWidth / NavigationWidth)).ToDouble();
            Check(Math.Abs(moved - 1) < NavigationTolerance(viewport, shallow: !viewport.IsDeep),
                $"{label}: сдвиг на пиксель дал {moved} px.");
            cases++;
        }

        // Сохранение и загрузка центра без потерь.
        {
            DeepZoomViewport<T> viewport = CreateAt(create, centerX, centerY, zoom);
            viewport.ApplyShift(viewport.ViewWidth * 0.123456789, viewport.ViewWidth * -0.0987654321);
            (T x, T y, string? exactX, string? exactY) = viewport.CaptureCenter();
            DeepZoomViewport<T> restored = create();
            restored.Restore(x, y, exactX, exactY, viewport.Zoom);
            Check(restored.CaptureCenter().Equals((x, y, exactX, exactY)) && restored.Zoom == viewport.Zoom &&
                  restored.IsDeep == viewport.IsDeep, $"{label}: центр не пережил сохранение и загрузку.");
            cases++;
        }
        return cases;
    }

    /// <summary>Вид с центром рядом с заданным; на глубине точный центр несёт лишние знаки.</summary>
    private static DeepZoomViewport<T> CreateAt<T>(Func<DeepZoomViewport<T>> create, T centerX, T centerY, FloatExp zoom)
        where T : struct
    {
        DeepZoomViewport<T> viewport = create();
        viewport.Restore(centerX, centerY, DeepViewportExactX, DeepViewportExactY, zoom);
        return viewport;
    }

    /// <summary>Мировая точка под экранной — независимо от вида, в BigFloat с запасом точности.</summary>
    private static (BigFloat X, BigFloat Y) WorldUnder<T>(DeepZoomViewport<T> viewport, Point point) where T : struct
    {
        using var precision = new BigFloat.PrecisionScope(DeepZoomViewport<T>.CenterPrecisionBits(viewport.Zoom) + 128);
        FloatExp pixel = viewport.ViewWidth / NavigationWidth;
        return (viewport.ExactCenterX + ((point.X - NavigationWidth / 2) * pixel).ToBigFloat(),
            viewport.ExactCenterY + ((NavigationHeight / 2 - point.Y) * pixel).ToBigFloat());
    }

    /// <summary>
    /// Допуск в пикселях. Ниже порога глубокого режима центр — обычный decimal или double, и
    /// каждая операция с ним округляется до шага этого типа: у Мандельброта на 1e24 это уже
    /// сотые доли пикселя. Так было и до общего вида, поэтому допуск включает шаг типа.
    /// </summary>
    private static double NavigationTolerance<T>(DeepZoomViewport<T> viewport, bool shallow) where T : struct
    {
        if (!shallow) return NavigationTolerancePixels;
        double typeStep = default(T) is decimal ? 1e-28 : 2.3e-16;
        double pixel = (viewport.ViewWidth / NavigationWidth).ToDouble();
        return NavigationTolerancePixels + 16 * typeStep / pixel;
    }

    private static void CheckPixels<T>(DeepZoomViewport<T> viewport, BigFloat expected, BigFloat actual, double tolerance,
        string message) where T : struct
    {
        using var precision = new BigFloat.PrecisionScope(DeepZoomViewport<T>.CenterPrecisionBits(viewport.Zoom) + 128);
        double pixels = Math.Abs((FloatExp.FromBigFloat(actual - expected) / (viewport.ViewWidth / NavigationWidth)).ToDouble());
        Check(pixels < tolerance, $"{message} на {pixels:G3} px (допуск {tolerance:G3}).");
    }

    /// <summary>
    /// ScreenToWorld отдаёт обычный тип центра: сверяется с точной точкой с точностью этого типа,
    /// а на мелком зуме — до пикселя.
    /// </summary>
    private static void CheckScreenToWorld<T>(DeepZoomViewport<T> viewport, T worldX, T worldY, BigFloat expectedX,
        BigFloat expectedY, string message) where T : struct
    {
        BigFloat actualX = worldX is decimal dx ? BigFloat.FromDecimal(dx) : BigFloat.FromDouble((double)(object)worldX);
        BigFloat actualY = worldY is decimal dy ? BigFloat.FromDecimal(dy) : BigFloat.FromDouble((double)(object)worldY);
        double typeResolution = worldX is decimal ? 1e-27 : 2e-16;
        using var precision = new BigFloat.PrecisionScope(DeepZoomViewport<T>.CenterPrecisionBits(viewport.Zoom) + 128);
        double pixel = Math.Max((viewport.ViewWidth / NavigationWidth).ToDouble(), 0);
        double error = Math.Max(Math.Abs(FloatExp.FromBigFloat(actualX - expectedX).ToDouble()),
            Math.Abs(FloatExp.FromBigFloat(actualY - expectedY).ToDouble()));
        Check(error <= Math.Max(typeResolution, pixel * NavigationTolerancePixels), $"{message} ошибся на {error:G3}.");
    }

    private static object CaptureWindowState(Window window)
    {
        PrepareWindowForCapture(window);
        return
        window.GetType().GetMethod("CaptureState", BindingFlags.Instance | BindingFlags.Public, [typeof(string)])!
            .Invoke(window, ["deep-viewport"])!;
    }

    private static void SetZoomFromText(object state, string text)
    {
        PropertyInfo zoom = state.GetType().GetProperty("Zoom")!;
        object value = zoom.PropertyType == typeof(FloatExp)
            ? FloatExp.TryParse(text, out FloatExp parsed) ? parsed : throw new FormatException(text)
            : (object)double.Parse(text, CultureInfo.InvariantCulture);
        zoom.SetValue(state, value);
    }

    /// <summary>Центр, точный центр и зум состояния окна — без округления.</summary>
    private static string DescribeViewState(Window window)
    {
        object state = CaptureWindowState(window);
        string Value(string property) => state.GetType().GetProperty(property)?.GetValue(state) switch
        {
            null => "null",
            double value => value.ToString("R", CultureInfo.InvariantCulture),
            decimal value => value.ToString(CultureInfo.InvariantCulture),
            FloatExp value => value.IsZero ? "0" : string.Create(CultureInfo.InvariantCulture, $"{value.Mantissa:R}p{value.Exponent}"),
            object value => value.ToString()!
        };
        return $"x={Value("CenterX")} y={Value("CenterY")} ex={Value("CenterXExact")} ey={Value("CenterYExact")} zoom={Value("Zoom")}";
    }
}
