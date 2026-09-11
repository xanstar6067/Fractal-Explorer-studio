using FractalExplorerWPF.Core.NewtonMath;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Ускорение глубокого зума методом BLA (Bivariate Linear Approximation, Zhuoran).
///
/// Пока отклонение δ мало по сравнению с опорной точкой Z, пертурбационная рекуррентность
/// δ' = 2·Z·δ + δ² + δc почти линейна. Отбросив δ², получаем линейное отображение
/// δ' ≈ A·δ + B·δc, которое можно композировать по отрезкам итераций. Таблица хранит
/// пирамиду таких отображений (уровень k — отрезки длины 2^k), и пиксельный цикл одним
/// шагом перепрыгивает целую пачку итераций там, где |δ| не превышает радиус применимости.
///
/// Строится один раз на кадр и кэшируется вместе с опорной орбитой. Раскраска и
/// <see cref="PixelMetrics"/> не участвуют — только рекуррентность δ.
/// </summary>
public static partial class MandelbrotFamilyRenderer
{
    // Тестовый шов: null — BLA включён (по умолчанию), false — принудительно выключен,
    // true — принудительно включён. Используется только из проверочного проекта.
    internal static bool? ForceBlaForTests { get; set; }

    private static bool BlaEnabled => ForceBlaForTests ?? true;

    // Допуск отбрасывания δ² в одном линейном шаге: r = tol·|A| = tol·2|Z|. 2^-52 ⇒
    // одиночный BLA-шаг совпадает с обычным double-шагом с точностью до округления;
    // на композитных BLA добавляется собственный порядок округления (единицы ULP —
    // того же рода расхождение, что переход decimal→пертурбация в Фазе 2).
    private const double BlaTolerance = 2.220446049250313e-16;

    // Верхняя граница длины орбиты, для которой строится таблица: пять double-массивов по
    // ~2·L элементов — около 160 МБ при L = 2M (плюс 32 МБ самой орбиты). Совпадает с
    // потолком итераций в окне (MaxIterations), поэтому BLA доступен на всём диапазоне
    // настроек и нигде не отключается молча.
    internal const int BlaMaxOrbitLength = 2_000_000;

    private sealed class BlaTable
    {
        // [уровень][элемент]. Уровень k, элемент j описывает отрезок итераций
        // [j·2^k, (j+1)·2^k): δ_out ≈ A·δ_in + B·δc при |δ_in|² ≤ R2.
        public required double[][] Ax;
        public required double[][] Ay;
        public required double[][] Bx;
        public required double[][] By;
        public required double[][] R2;
        public required int[] Count;
        public required int Levels;
        public required int MaxReferenceIndex;

        /// <summary>
        /// Строит таблицу по опорной орбите (<paramref name="re"/>/<paramref name="im"/>,
        /// первые <paramref name="length"/> точек). <paramref name="deltaCMax"/> — верхняя
        /// оценка |δc| по кадру (берётся ширина вида, консервативно). <paramref name="power"/> —
        /// целая степень формулы zᵖ+c (2 для Mandelbrot/Julia): линейный член A = p·Zᵖ⁻¹,
        /// радиус r = tol·(2/(p−1))·|Z|. Возвращает null, если таблица не нужна или орбита
        /// слишком длинная.
        /// </summary>
        public static BlaTable? Build(
            double[] re, double[] im, int length, bool isJulia, double escapeSquared, FloatExp deltaCMax,
            int power = 2)
        {
            if (length < 4 || length > BlaMaxOrbitLength) return null;

            // r = tol·|A|/|следующий член| = tol·(p/C(p,2))·|Z| = tol·(2/(p−1))·|Z|.
            double radiusFactor = 2.0 / (power - 1);

            int level0Count = length - 1;
            int levels = 1;
            while ((1 << levels) < level0Count && levels < 30) levels++;

            var ax = new double[levels][];
            var ay = new double[levels][];
            var bx = new double[levels][];
            var by = new double[levels][];
            var r2 = new double[levels][];
            var count = new int[levels];

            // --- уровень 0: одиночные шаги n → n+1, Z = orbit[n] ---
            count[0] = level0Count;
            ax[0] = new double[level0Count];
            ay[0] = new double[level0Count];
            bx[0] = new double[level0Count];
            by[0] = new double[level0Count];
            r2[0] = new double[level0Count];

            double bSeed = isJulia ? 0.0 : 1.0;
            for (int n = 0; n < level0Count; n++)
            {
                double zr = re[n], zi = im[n];

                // A = p·Zᵖ⁻¹ (для p=2 — ровно 2Z, как раньше).
                double powerReal = 1.0, powerImaginary = 0.0;
                for (int e = 0; e < power - 1; e++)
                {
                    double nr = powerReal * zr - powerImaginary * zi;
                    powerImaginary = powerReal * zi + powerImaginary * zr;
                    powerReal = nr;
                }
                ax[0][n] = power * powerReal;
                ay[0][n] = power * powerImaginary;
                bx[0][n] = bSeed;
                by[0][n] = 0.0;

                double zMagnitudeSquared = zr * zr + zi * zi;
                double nextMagnitudeSquared = re[n + 1] * re[n + 1] + im[n + 1] * im[n + 1];
                // Через выход пикселя за радиус перепрыгивать нельзя — там нужен обычный шаг.
                if (zMagnitudeSquared > escapeSquared || nextMagnitudeSquared > escapeSquared)
                {
                    r2[0][n] = 0.0;
                }
                else
                {
                    double r = BlaTolerance * radiusFactor * System.Math.Sqrt(zMagnitudeSquared);
                    r2[0][n] = r * r;
                }
            }

            // --- уровни выше: слияние соседних пар (x применяется первым, затем y) ---
            for (int k = 1; k < levels; k++)
            {
                int previous = count[k - 1];
                int current = (previous + 1) >> 1;
                count[k] = current;
                ax[k] = new double[current];
                ay[k] = new double[current];
                bx[k] = new double[current];
                by[k] = new double[current];
                r2[k] = new double[current];

                for (int j = 0; j < current; j++)
                {
                    int left = 2 * j;
                    int right = 2 * j + 1;
                    if (right >= previous)
                    {
                        // Непарный хвост — переносим левый элемент как есть.
                        ax[k][j] = ax[k - 1][left];
                        ay[k][j] = ay[k - 1][left];
                        bx[k][j] = bx[k - 1][left];
                        by[k][j] = by[k - 1][left];
                        r2[k][j] = r2[k - 1][left];
                        continue;
                    }

                    double xAx = ax[k - 1][left], xAy = ay[k - 1][left];
                    double xBx = bx[k - 1][left], xBy = by[k - 1][left];
                    double xR2 = r2[k - 1][left];
                    double yAx = ax[k - 1][right], yAy = ay[k - 1][right];
                    double yBx = bx[k - 1][right], yBy = by[k - 1][right];
                    double yR2 = r2[k - 1][right];

                    // A_z = A_y · A_x ; B_z = A_y · B_x + B_y   (комплексно)
                    double zAx = yAx * xAx - yAy * xAy;
                    double zAy = yAx * xAy + yAy * xAx;
                    double zBx = yAx * xBx - yAy * xBy + yBx;
                    double zBy = yAx * xBy + yAy * xBx + yBy;

                    double zR2;
                    if (xR2 <= 0.0 || yR2 <= 0.0 ||
                        !double.IsFinite(zAx) || !double.IsFinite(zAy) ||
                        !double.IsFinite(zBx) || !double.IsFinite(zBy))
                    {
                        zR2 = 0.0;
                    }
                    else
                    {
                        double xAabs = System.Math.Sqrt(xAx * xAx + xAy * xAy);
                        double xBabs = System.Math.Sqrt(xBx * xBx + xBy * xBy);
                        double rx = System.Math.Sqrt(xR2);
                        double ry = System.Math.Sqrt(yR2);
                        // |δ_in| ≤ r_x  и  |A_x·δ_in + B_x·δc| ≤ r_y ⇒
                        // r_z = min( r_x , max(0, (r_y − |B_x|·δcmax) / |A_x|) )
                        // Произведение |B|·δcmax считается в расширенном диапазоне: |B| доходит
                        // до ~1e308, а δcmax на глубоком зуме уже вне double — в double
                        // произведение давало бы 0/NaN вместо настоящей оценки. Сам радиус
                        // остаётся double: он ограничен tol·|Z|, то есть всегда в диапазоне.
                        double bCorrection = (FloatExp.FromDouble(xBabs) * deltaCMax).ToDouble();
                        double bound = xAabs > 0.0 ? (ry - bCorrection) / xAabs : 0.0;
                        double rz = System.Math.Min(rx, System.Math.Max(0.0, bound));
                        zR2 = rz * rz;
                    }

                    ax[k][j] = zAx;
                    ay[k][j] = zAy;
                    bx[k][j] = zBx;
                    by[k][j] = zBy;
                    r2[k][j] = zR2;
                }
            }

            return new BlaTable
            {
                Ax = ax,
                Ay = ay,
                Bx = bx,
                By = by,
                R2 = r2,
                Count = count,
                Levels = levels,
                MaxReferenceIndex = length - 1,
            };
        }

        /// <summary>
        /// Наибольший применимый BLA из <paramref name="referenceIndex"/> при текущем |δ|²
        /// и оставшемся бюджете итераций. false — нужен обычный шаг (в т.ч. если подходит
        /// только отрезок длины 1: одиночный BLA-шаг заменять на обычный смысла нет).
        /// </summary>
        public bool TryLookup(
            int referenceIndex, double deltaMagnitudeSquared, int iterationBudget,
            out double aX, out double aY, out double bX, out double bY, out int steps)
        {
            aX = 1.0;
            aY = 0.0;
            bX = 0.0;
            bY = 0.0;
            steps = 0;

            for (int k = 0; k < Levels; k++)
            {
                int span = 1 << k;
                if ((referenceIndex & (span - 1)) != 0) break;   // не выровнен под 2^k
                int j = referenceIndex >> k;
                if (j >= Count[k]) break;
                double radiusSquared = R2[k][j];
                if (radiusSquared <= 0.0 || deltaMagnitudeSquared >= radiusSquared) break;
                if (span > iterationBudget) break;
                if (referenceIndex + span > MaxReferenceIndex) break;

                aX = Ax[k][j];
                aY = Ay[k][j];
                bX = Bx[k][j];
                bY = By[k][j];
                steps = span;
            }

            return steps >= 2;
        }
    }
}
