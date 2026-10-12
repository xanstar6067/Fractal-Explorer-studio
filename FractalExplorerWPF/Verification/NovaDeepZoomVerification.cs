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

// Глубокий зум Nova.
internal static partial class Program
{
    // Phase 12: Nova gained a perturbation engine of its own. Two things make it unlike the
    // Mandelbrot and Phoenix ones, and both are what these checks are aimed at.
    //
    //   • The formula is not polynomial. The step is folded from
    //     z − m·(z^p − 1)/(p·z^(p−1)) + c into (1 − m/p)·z + (m/p)·z^(1−p) + c, and that
    //     folding is an algebraic claim about branches of the logarithm, not a rewrite the
    //     compiler could check. The exact reference here deliberately iterates the ORIGINAL
    //     form in BigFloat — two separate powers and a division — so a wrong folding shows up.
    //
    //   • The power need not be an integer. An integer one goes through a binomial expansion
    //     (exact, no transcendentals); a fractional or complex one through log1p/expm1 with an
    //     explicit branch correction. These are two independent kernels and both are covered.
    private static async Task VerifyNovaDeepZoomAsync()
    {
        static MandelbrotPalette Palette() => new()
        {
            Colors = [Colors.White, Colors.Black],
            InteriorColor = Colors.Black,
            IsGradient = true
        };

        static NovaState View(double zoom, NovaVariant variant, int iterations = 300,
            decimal pReal = 3m, decimal pImaginary = 0m, decimal m = 1m,
            decimal centerX = 0m, decimal centerY = 0m, decimal threshold = 10m) => new()
        {
            Variant = variant,
            CenterX = centerX,
            CenterY = centerY,
            Zoom = zoom,
            Threshold = threshold,
            Iterations = iterations,
            PReal = pReal,
            PImaginary = pImaginary,
            Z0Real = 1m,
            M = m,
            CReal = 0m,
            CImaginary = 1m,
            UseSmoothColoring = true,
            Palette = Palette()
        };

        static NovaState AtExactCenter(NovaState state, string centerX, string centerY)
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

        static async Task<byte[]> RenderAsync(NovaState state, bool? forceDeep, int width, int height,
            int? forceBits = null)
        {
            byte[] pixels = new byte[width * height * 4];
            NovaRenderer.ForceDeepZoomForTests = forceDeep;
            NovaRenderer.ForceReferenceBitsForTests = forceBits;
            try
            {
                await Task.Run(() => NovaRenderer.Render(state, pixels, width, height, width * 4, 4,
                    CancellationToken.None));
            }
            finally
            {
                NovaRenderer.ForceDeepZoomForTests = null;
                NovaRenderer.ForceReferenceBitsForTests = null;
            }
            return pixels;
        }

        const int w = 64, h = 44, total = w * h;

        // 1. Where the plain double stage is still exact, the perturbation engine must
        //    reproduce it. Zoom 1 is used on purpose: the whole Nova structure is in frame, so
        //    every combination below has real content to disagree about — at a deeper zoom most
        //    of them would render a uniform field and the comparison would pass vacuously.
        //    The powers cover both kernels (2, 3, 4, 5 and −2 integer; 2.5 fractional;
        //    3+0.4i complex) and both planes.
        //
        //    The escape radius is the smallest the window accepts (2) rather than the usual
        //    10: with a wide radius most of these combinations converge everywhere in frame —
        //    the Nova map is Newton's method, so a bounded orbit is the rule — and several
        //    frames would come out uniform. At radius 2 the least structured of the 28 still
        //    shows over a hundred edge pixels.
        int worstDiffering = 0;
        string worstLabel = "";
        foreach (NovaVariant variant in new[] { NovaVariant.Mandelbrot, NovaVariant.Julia })
        foreach ((decimal real, decimal imaginary) in new[]
                 { (3m, 0m), (2m, 0m), (4m, 0m), (5m, 0m), (-2m, 0m), (2.5m, 0m), (3m, 0.4m) })
        foreach (decimal relaxation in new[] { 1m, 0.6m })
        {
            // A Julia frame centred on zero would degenerate at once: |z₀| = 0 is a pole.
            NovaState state = View(1, variant, 200, real, imaginary, relaxation,
                variant == NovaVariant.Julia ? 0.5m : 0m, variant == NovaVariant.Julia ? 0.5m : 0m, 2m);
            byte[] shallow = await RenderAsync(state, false, w, h);
            byte[] deep = await RenderAsync(state, true, w, h);
            int differing = CountDiffering(shallow, deep);
            Check(CountEdges(shallow, w) > 20,
                $"Nova shallow-vs-deep frame must show structure ({variant}, p={real}+{imaginary}i, m={relaxation}).");
            if (differing > worstDiffering)
            {
                worstDiffering = differing;
                worstLabel = $"{variant}/p={real}+{imaginary}i/m={relaxation}";
            }
            Check(differing * 100 <= total * 2,
                $"Nova perturbation must match the plain double path at zoom 1 " +
                $"({variant}, p={real}+{imaginary}i, m={relaxation}): {differing}/{total} pixels differ.");
        }
        Console.WriteLine($"[diag] nova shallow-vs-deep worst {worstDiffering}/{total} ({worstLabel})");

        // 2. On depth the plain stage cannot be trusted — the reference is direct BigFloat
        //    iteration of the original (unfolded) formula. Centres found by descending on edge
        //    density, so every frame here has structure; without it the comparison would be
        //    empty.
        (string X, string Y, double Zoom)[] deepFixtures =
        [
            ("-0.509516618365150777259563561165083570374599345148385054482531586472759954631328582763671875",
                "0.12446526403814218906112533265557181617030335358912995769031795134651474654674530029296875", 2.5e12),
            ("-0.5095166183649699337614919710008681922163433830570441234531321318787684682138916514304582960903644561767578125",
                "0.12446526403779946010431759880120476125853088903040909626751026250277769140406558534550640615634620189666748046875", 1.4e18),
            ("-0.5095166183649699336509924041728346350204283378347156708235845009654971517262786448779730841263102547600283287465572357177734375",
                "0.124465264037799460791408720088841974283648683522681157925231238309941040027064462616712058008403007924869143607793375849723815918", 2.2e24),
            ("-0.5095166183649699336509921081529684513569533240729108569093398402627923908730704144762385388672474793227032234532725141207265551202",
                "0.1244652640377994607914085291439532442123344414316132502241762782568814053414408708136371817397475259513173195813351412652991712093", 1.4e28),
            ("-0.5095166183649699336509921081231778031725923878106713770956940720740811612144618601104968658748996699920397628167844305494688095237",
                "0.1244652640377994607914085291655502843850926810587318374913406744440192434264407152019680763176412423180732757135548904522948099327", 2.8e32),
        ];
        foreach ((string centerX, string centerY, double zoom) in deepFixtures)
        {
            NovaState state = AtExactCenter(View(zoom, NovaVariant.Mandelbrot), centerX, centerY);
            byte[] deep = await RenderAsync(state, true, w, h);
            byte[] exact = await Task.Run(() =>
                NovaRenderer.RenderExactReferenceForTests(state, w, h, 128, CancellationToken.None));
            int differing = CountDiffering(deep, exact);
            int edges = CountEdges(exact, w);
            Console.WriteLine($"[diag] nova deep {zoom:0.0e+0}: {differing}/{total} differ, {edges} edges");
            Check(edges >= 100, $"Nova deep fixture at {zoom:0.0e+0} lost its structure (edges {edges}).");
            Check(differing * 100 <= total * 3,
                $"Nova deep zoom must match the exact BigFloat reference at {zoom:0.0e+0}: " +
                $"{differing}/{total} pixels differ.");
        }

        // 3. Точность плана: тот же кадр с заведомо избыточной разрядностью опорной орбиты.
        //    Единственная проверка самого плана, не требующая внешнего эталона.
        foreach ((string centerX, string centerY, double zoom) in deepFixtures)
        {
            NovaState state = AtExactCenter(View(zoom, NovaVariant.Mandelbrot), centerX, centerY);
            byte[] planned = await RenderAsync(state, true, w, h);
            byte[] generous = await RenderAsync(state, true, w, h,
                NovaRenderer.PlanReferenceBits(state) + 256);
            int drift = CountDiffering(planned, generous);
            Check(drift == 0,
                $"Nova precision plan must not drift with 256 extra reference bits at {zoom:0.0e+0}: " +
                $"{drift}/{total} pixels differ.");
        }

        // 4. Дробная и комплексная степень на глубине — вторая ветвь приращения (log1p/expm1
        //    с поправкой ветви). Кадры меньше и итераций меньше: эталон для нецелой степени
        //    считает комплексный логарифм произвольной точности на каждом шаге.
        (decimal Real, decimal Imaginary, string X, string Y, double Zoom)[] fractionalFixtures =
        [
            (2.5m, 0m,
                "-0.54230858625529284624830722419350632704409780472480651081212954522925429046154022216796875",
                "0.10561361857564103568872547550552447522320710561827077599017510323164970031939446926116943359375",
                2.2e12),
            (3m, 0.4m,
                "-0.618079795182438829418512344834935446683807723718782488504797090200781894964165985584259033203125",
                "0.102843101909722159412254109837162628956617147567395749441221397546541993506252765655517578125",
                2.2e12),
        ];
        foreach ((decimal real, decimal imaginary, string centerX, string centerY, double zoom) in fractionalFixtures)
        {
            NovaState state = AtExactCenter(
                View(zoom, NovaVariant.Mandelbrot, 120, real, imaginary), centerX, centerY);
            byte[] deep = await RenderAsync(state, true, 32, 22);
            byte[] exact = await Task.Run(() =>
                NovaRenderer.RenderExactReferenceForTests(state, 32, 22, 128, CancellationToken.None));
            int differing = CountDiffering(deep, exact);
            Console.WriteLine($"[diag] nova deep p={real}+{imaginary}i {zoom:0.0e+0}: {differing}/704 differ, " +
                              $"{CountEdges(exact, 32)} edges");
            Check(differing * 100 <= 704 * 4,
                $"Nova deep zoom with power {real}+{imaginary}i must match the exact BigFloat reference: " +
                $"{differing}/704 pixels differ.");
        }

        // 5. Тайл прогрессивного предпросмотра обязан совпасть с полным кадром: у них разные
        //    точки входа в движок и своя раскладка пикселей.
        {
            NovaState state = AtExactCenter(View(deepFixtures[2].Zoom, NovaVariant.Mandelbrot),
                deepFixtures[2].X, deepFixtures[2].Y);
            byte[] full = await RenderAsync(state, true, w, h);
            NovaRenderer.ForceDeepZoomForTests = true;
            byte[]? tile;
            try
            {
                tile = await Task.Run(() => NovaRenderer.RenderTile(state, w, h,
                    new MandelbrotRenderTile(16, 12, 24, 16, 1, 1), CancellationToken.None));
            }
            finally { NovaRenderer.ForceDeepZoomForTests = null; }
            Check(tile is not null, "Nova deep-zoom tile must render.");
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
                $"Nova deep-zoom tile must match the full frame exactly: {tileDiffering}/384 differ.");
        }

        // 6. Точный центр действительно доходит до рендера: сдвиг на десятую пикселя на
        //    глубине, где decimal-поля состояния его уже не различают, обязан менять кадр.
        {
            (string centerX, string centerY, double zoom) = deepFixtures[4];
            NovaState state = AtExactCenter(View(zoom, NovaVariant.Mandelbrot), centerX, centerY);
            BigFloat shifted;
            using (new BigFloat.PrecisionScope(NovaRenderer.PlanReferenceBits(state)))
                shifted = BigFloat.Parse(centerX) + BigFloat.FromDouble(0.1 * 4.0 / zoom / w);
            NovaState moved = AtExactCenter(View(zoom, NovaVariant.Mandelbrot),
                shifted.ToInvariantString(), centerY);
            Check(moved.CenterX == state.CenterX,
                "The shift must be invisible to the decimal centre, otherwise the check proves nothing.");
            byte[] original = await RenderAsync(state, true, w, h);
            byte[] nudged = await RenderAsync(moved, true, w, h);
            Check(CountDiffering(original, nudged) > 0,
                "A sub-pixel shift of the exact centre must change the deep-zoom frame.");
        }

        // 7. Нулевая степень движку не по силам (знаменатель p·z^(p−1) тождественно ноль) и
        //    обязана молча уходить на плоскую ступень, а не падать.
        {
            NovaState degenerate = AtExactCenter(View(1e20, NovaVariant.Mandelbrot, 120, 0m),
                deepFixtures[0].X, deepFixtures[0].Y);
            byte[] pixels = await RenderAsync(degenerate, null, w, h);
            Check(pixels.Where((_, index) => index % 4 == 3).All(value => value == 255),
                "Nova with power 0 must still fill every pixel.");
        }

        // 8. Круг «окно → сохранение → окно»: точный центр обязан пережить его без потерь.
        //    Именно здесь легко потерять глубину — decimal-поля состояния сохраняют лишь 28
        //    знаков, и если окно перестанет писать или читать строки, зум просто вернётся к
        //    прежнему потолку, а рендер останется формально исправным.
        {
            // Разметка окна тянет стили из App.xaml, а проверочный Application создаётся
            // пустым: без словаря конструктор падает на StaticResource.
            var themeStyles = new Uri("pack://application:,,,/FractalExplorerWPF;component/Theming/ThemeStyles.xaml");
            if (Application.Current.Resources.MergedDictionaries.All(d => d.Source != themeStyles))
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = themeStyles });

            var window = new NovaWindow(NovaVariant.Mandelbrot);
            NovaState deep = AtExactCenter(View(1e20, NovaVariant.Mandelbrot),
                deepFixtures[2].X, deepFixtures[2].Y);
            window.LoadState(deep);
            NovaState captured = window.CaptureState("round-trip");

            // Сравнение по значению, а не по тексту: BigFloat печатает столько цифр, сколько
            // несёт его мантисса, поэтому строка на выходе длиннее исходной, обозначая то же
            // число. Важно, что оно не изменилось, а не как оно записано.
            Check(captured.CenterXExact is { Length: > 0 } && captured.CenterYExact is { Length: > 0 },
                "A deep Nova save must carry exact-center strings.");
            Check(BigFloat.Parse(captured.CenterXExact!) == BigFloat.Parse(deep.CenterXExact!) &&
                  BigFloat.Parse(captured.CenterYExact!) == BigFloat.Parse(deep.CenterYExact!),
                "The Nova window must round-trip the exact center of a deep save without losing digits.");
            Check(captured.Zoom == deep.Zoom, "The Nova window must round-trip a deep zoom unchanged.");

            // И обратно: обычное сохранение с мелким зумом не должно обзаводиться строками
            // точного центра — иначе они начнут расходиться с decimal-полями.
            window.LoadState(View(700, NovaVariant.Mandelbrot));
            NovaState shallow = window.CaptureState("round-trip-shallow");
            Check(shallow.CenterXExact is null && shallow.CenterYExact is null,
                "A shallow Nova save must not carry exact-center strings.");
            window.Close();
        }

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Deep Nova render must restore the calling thread's working precision.");
    }
}
