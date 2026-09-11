using FractalExplorerWPF.Core.NewtonMath;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// BLA для вариантов, у которых линейная часть возмущения — вещественная 2×2-карта, а не
/// комплексное умножение: пять отражённых/сопряжённых вариантов (Burning Ship, Julia Burning
/// Ship, Tricorn, Buffalo, Celtic) и Симоноброт целой степени.
///
/// Устройство то же, что у комплексной <see cref="BlaTable"/> (Zhuoran): пирамида отображений
/// δ_out ≈ A·δ_in + B·δc по отрезкам из 2^k итераций, слияние соседних пар вверх по уровням,
/// радиус применимости на каждом узле. Отличие ровно одно: A и B — вещественные матрицы 2×2,
/// поэтому композиция и нормы считаются матрично, а не как умножение комплексных чисел.
///
/// Комплексная таблица намеренно не переиспользуется и не изменяется: у матрицы вида
/// [[a,−b],[b,a]] оба сингулярных числа равны |a+bi|, поэтому её оценки уже точны, а любая
/// правка сдвинула бы проверенный путь Mandelbrot/Julia/Multibrot. Наборы вариантов строго
/// не пересекаются: <see cref="ReferenceOrbit.Bla"/> и <see cref="ReferenceOrbit.RealBla"/>
/// никогда не заполнены одновременно.
///
/// Линейная часть по вариантам (Z — точка опорной орбиты, s(x) — знак, M(u) = [[u_re, −u_im],
/// [u_im, u_re]] — матрица умножения на комплексное u):
/// <list type="bullet">
/// <item>Burning Ship: A = M(2W)·diag(s(Zr), −s(Zi)), W = (|Zr|, −|Zi|);</item>
/// <item>Buffalo:      A = M(2W)·diag(s(Zr),  s(Zi)), W = (|Zr|,  |Zi|);</item>
/// <item>Tricorn:      A = M(2W)·diag(1, −1),         W = (Zr, −Zi);</item>
/// <item>Celtic:       A = diag(s(U), 1)·M(2Z),       U = Zr²−Zi²;</item>
/// <item>Симоноброт: A = M(p·M^(p/2)·Zᵖ⁻¹) + p·M^(p/2−1)·(Zᵖ ⊗ Z), M = |Z|²; при нечётном p
/// обе степени модуля полуцелые и берут множителем √M.</item>
/// </list>
/// </summary>
public static partial class MandelbrotFamilyRenderer
{
    // Вещественная таблица хранит 9 массивов вместо 5 (A и B по четыре компоненты), поэтому
    // потолок длины орбиты вдвое ниже комплексного — верхняя оценка памяти сопоставима
    // (~144 МБ при 1M). При рабочих потолках зума этих вариантов недостижим.
    private const int RealBlaMaxOrbitLength = 1_000_000;

    // Тестовый шов: пока включён, ядра суммируют число итераций, пропущенных вещественной
    // таблицей. Без него проверка «BLA не портит картинку» может пройти вхолостую — на
    // недостаточно глубокой фикстуре δ больше радиуса применимости и BLA не срабатывает
    // ни разу. По умолчанию выключен, поэтому на рабочий рендер не влияет.
    internal static bool CountRealBlaSkipsForTests;
    internal static long RealBlaSkippedIterationsForTests;

    /// <summary>
    /// Сингулярные числа вещественной 2×2-матрицы в закрытой форме: σ² — собственные числа
    /// AᵀA, у которой след равен квадрату нормы Фробениуса, а определитель — det(A)².
    /// Матрица предварительно нормируется на наибольший по модулю элемент, поэтому
    /// промежуточные величины не переполняются даже у композитных узлов верхних уровней.
    /// </summary>
    private static (double Smallest, double Largest) SingularValues2x2(
        double a11, double a12, double a21, double a22)
    {
        double scale = System.Math.Max(
            System.Math.Max(System.Math.Abs(a11), System.Math.Abs(a12)),
            System.Math.Max(System.Math.Abs(a21), System.Math.Abs(a22)));
        if (scale == 0.0) return (0.0, 0.0);
        if (!double.IsFinite(scale)) return (double.PositiveInfinity, double.PositiveInfinity);

        double b11 = a11 / scale, b12 = a12 / scale, b21 = a21 / scale, b22 = a22 / scale;
        double frobeniusSquared = b11 * b11 + b12 * b12 + b21 * b21 + b22 * b22;   // ≤ 4
        double determinant = b11 * b22 - b12 * b21;                                 // |·| ≤ 2
        double discriminant = frobeniusSquared * frobeniusSquared - 4.0 * determinant * determinant;
        if (discriminant < 0.0) discriminant = 0.0;   // отрицательным бывает только от округления

        double largest = System.Math.Sqrt(0.5 * (frobeniusSquared + System.Math.Sqrt(discriminant)));
        double smallest = largest > 0.0 ? System.Math.Abs(determinant) / largest : 0.0;
        return (scale * smallest, scale * largest);
    }

    private static double SpectralNorm2x2(double a11, double a12, double a21, double a22) =>
        SingularValues2x2(a11, a12, a21, a22).Largest;

    private sealed class RealBlaTable
    {
        // [уровень][элемент] — как в BlaTable, но A и B по четыре компоненты.
        public required double[][] A11;
        public required double[][] A12;
        public required double[][] A21;
        public required double[][] A22;
        public required double[][] B11;
        public required double[][] B12;
        public required double[][] B21;
        public required double[][] B22;
        public required double[][] R2;
        public required int[] Count;
        public required int Levels;
        public required int MaxReferenceIndex;

        /// <summary>
        /// Узлы первого уровня (пропуск ровно двух итераций) и их число — плоской
        /// ссылкой, чтобы горячая проверка <see cref="CanSkip"/> шла в одно чтение, а не через
        /// двухуровневый массив. Тот же массив, что <c>R2[1]</c>, без копирования.
        /// </summary>
        public required double[] Level1RadiusSquared;
        public required int Level1Count;

        /// <summary>
        /// Наибольший радиус среди узлов первого уровня. Даёт ядру отсечку в одно
        /// сравнение с полем, без обращения к памяти: при |δ|² ≥ этого значения пропуск
        /// невозможен ни в одной точке орбиты.
        /// </summary>
        public required double MaxLevel1RadiusSquared;

        /// <summary>
        /// Строит таблицу по опорной орбите. Ровно один из <paramref name="reflect"/> и
        /// <paramref name="simonobrotPower"/> должен быть задан; иначе возвращается null
        /// (вариант обслуживает комплексная <see cref="BlaTable"/>).
        /// </summary>
        public static RealBlaTable? Build(
            double[] re, double[] im, int length, bool isJulia, double escapeSquared, FloatExp deltaCMax,
            ReflectKind? reflect, int simonobrotPower)
        {
            if (length < 4 || length > RealBlaMaxOrbitLength) return null;
            if (reflect is null && simonobrotPower < 2) return null;

            int level0Count = length - 1;
            // length >= 4 ⇒ level0Count >= 3 ⇒ цикл доводит levels минимум до 2, так что
            // уровень 1 (отсечка CanSkip) всегда существует.
            int levels = 1;
            while ((1 << levels) < level0Count && levels < 30) levels++;
            if (levels < 2) levels = 2;

            var a11 = new double[levels][];
            var a12 = new double[levels][];
            var a21 = new double[levels][];
            var a22 = new double[levels][];
            var b11 = new double[levels][];
            var b12 = new double[levels][];
            var b21 = new double[levels][];
            var b22 = new double[levels][];
            var r2 = new double[levels][];
            var count = new int[levels];

            count[0] = level0Count;
            a11[0] = new double[level0Count];
            a12[0] = new double[level0Count];
            a21[0] = new double[level0Count];
            a22[0] = new double[level0Count];
            b11[0] = new double[level0Count];
            b12[0] = new double[level0Count];
            b21[0] = new double[level0Count];
            b22[0] = new double[level0Count];
            r2[0] = new double[level0Count];

            // δc входит в шаг слагаемым, поэтому B₀ — единичная матрица (у Жюлиа δc нет).
            double bSeed = isJulia ? 0.0 : 1.0;
            int halfPower = simonobrotPower / 2;

            for (int n = 0; n < level0Count; n++)
            {
                double zr = re[n], zi = im[n];
                double zMagnitudeSquared = zr * zr + zi * zi;
                double nextMagnitudeSquared = re[n + 1] * re[n + 1] + im[n + 1] * im[n + 1];

                // Через выход опорной орбиты за радиус (и через её нуль) перепрыгивать нельзя.
                bool blocked = zMagnitudeSquared > escapeSquared
                            || nextMagnitudeSquared > escapeSquared
                            || zMagnitudeSquared <= 0.0
                            || !double.IsFinite(zMagnitudeSquared);

                double n11, n12, n21, n22;
                // Граница применимости свёртки знака: линеаризация |Zc+δc|−|Zc| = s(Zc)·δc точна,
                // пока δ не переворачивает знак свёрнутой величины. Бесконечность — ограничения нет.
                double foldLimit = double.PositiveInfinity;
                // Верхняя оценка коэффициента при |δ|² в разложении шага: C(d,2)·|Z|^(d−2),
                // где d — степень однородности формулы по вещественным компонентам z.
                double secondOrderCoefficient;

                if (simonobrotPower >= 2)
                {
                    // z ← zᵖ·M^(p/2) + c. Линейная часть: M^(p/2)·p·Zᵖ⁻¹·δ (комплексная) плюс
                    // Zᵖ·p·M^(p/2−1)·(Zr·δr + Zi·δi) (вещественная, ранга 1). d = 2p.
                    double powerMinusOneReal = 1.0, powerMinusOneImaginary = 0.0;   // Z^(p−1)
                    for (int e = 0; e < simonobrotPower - 1; e++)
                    {
                        double nr = powerMinusOneReal * zr - powerMinusOneImaginary * zi;
                        powerMinusOneImaginary = powerMinusOneReal * zi + powerMinusOneImaginary * zr;
                        powerMinusOneReal = nr;
                    }
                    double wReal = powerMinusOneReal * zr - powerMinusOneImaginary * zi;     // Zᵖ
                    double wImaginary = powerMinusOneReal * zi + powerMinusOneImaginary * zr;

                    double magnitudePowerHalf = 1.0;                                          // M^(p/2)
                    for (int e = 0; e < halfPower; e++) magnitudePowerHalf *= zMagnitudeSquared;
                    double magnitudePowerHalfMinusOne = 1.0;                                  // M^(p/2−1)
                    for (int e = 0; e < halfPower - 1; e++) magnitudePowerHalfMinusOne *= zMagnitudeSquared;
                    if ((simonobrotPower & 1) != 0)
                    {
                        // Нечётное p = 2q+1: обе степени модуля полуцелые — M^(p/2) = Mᵠ·√M,
                        // M^(p/2−1) = M^(q−1)·√M (q ≥ 1, так как нечётное p здесь не меньше 3).
                        double root = System.Math.Sqrt(zMagnitudeSquared);
                        magnitudePowerHalf *= root;
                        magnitudePowerHalfMinusOne *= root;
                    }

                    double linearReal = simonobrotPower * magnitudePowerHalf * powerMinusOneReal;
                    double linearImaginary = simonobrotPower * magnitudePowerHalf * powerMinusOneImaginary;
                    double rankOneScale = simonobrotPower * magnitudePowerHalfMinusOne;

                    n11 = linearReal + rankOneScale * wReal * zr;
                    n12 = -linearImaginary + rankOneScale * wReal * zi;
                    n21 = linearImaginary + rankOneScale * wImaginary * zr;
                    n22 = linearReal + rankOneScale * wImaginary * zi;

                    // C(2p,2)·|Z|^(2p−2) = p·(2p−1)·M^(p−1).
                    double magnitudePowerPowerMinusOne = 1.0;
                    for (int e = 0; e < simonobrotPower - 1; e++)
                        magnitudePowerPowerMinusOne *= zMagnitudeSquared;
                    secondOrderCoefficient =
                        simonobrotPower * (2.0 * simonobrotPower - 1.0) * magnitudePowerPowerMinusOne;
                }
                else if (reflect == ReflectKind.Celtic)
                {
                    // Re' = |Zr²−Zi²| + cr, Im' = 2·Zr·Zi + ci ⇒ A = diag(s(U),1)·M(2Z).
                    double u = zr * zr - zi * zi;
                    double signU = u > 0.0 ? 1.0 : u < 0.0 ? -1.0 : 0.0;
                    if (signU == 0.0) blocked = true;

                    n11 = signU * 2.0 * zr;
                    n12 = -signU * 2.0 * zi;
                    n21 = 2.0 * zi;
                    n22 = 2.0 * zr;

                    // |δu| ≤ 2|Z|·r + r² ≤ 3|Z|·r (r ≪ |Z|), а нужно |δu| ≤ |U|.
                    double magnitude = System.Math.Sqrt(zMagnitudeSquared);
                    foldLimit = magnitude > 0.0 ? System.Math.Abs(u) / (3.0 * magnitude) : 0.0;
                    secondOrderCoefficient = 1.0;
                }
                else
                {
                    // z ← w² + c, w — покомпонентная свёртка знака. Свёртка — ортогональная
                    // диагональ diag(p,q), поэтому A = M(2W)·diag(p,q) и |A·δ| = 2|Z|·|δ| точно.
                    double signReal = zr > 0.0 ? 1.0 : zr < 0.0 ? -1.0 : 0.0;
                    double signImaginary = zi > 0.0 ? 1.0 : zi < 0.0 ? -1.0 : 0.0;
                    double foldedReferenceReal, foldedReferenceImaginary;
                    double diagonalReal, diagonalImaginary;

                    switch (reflect)
                    {
                        case ReflectKind.BurningShip:
                            foldedReferenceReal = System.Math.Abs(zr);
                            foldedReferenceImaginary = -System.Math.Abs(zi);
                            diagonalReal = signReal;
                            diagonalImaginary = -signImaginary;
                            if (signReal == 0.0 || signImaginary == 0.0) blocked = true;
                            foldLimit = System.Math.Min(System.Math.Abs(zr), System.Math.Abs(zi));
                            break;
                        case ReflectKind.Buffalo:
                            foldedReferenceReal = System.Math.Abs(zr);
                            foldedReferenceImaginary = System.Math.Abs(zi);
                            diagonalReal = signReal;
                            diagonalImaginary = signImaginary;
                            if (signReal == 0.0 || signImaginary == 0.0) blocked = true;
                            foldLimit = System.Math.Min(System.Math.Abs(zr), System.Math.Abs(zi));
                            break;
                        default: // Tricorn — сопряжение, знак определён всегда, свёртки нет
                            foldedReferenceReal = zr;
                            foldedReferenceImaginary = -zi;
                            diagonalReal = 1.0;
                            diagonalImaginary = -1.0;
                            break;
                    }

                    n11 = 2.0 * foldedReferenceReal * diagonalReal;
                    n12 = -2.0 * foldedReferenceImaginary * diagonalImaginary;
                    n21 = 2.0 * foldedReferenceImaginary * diagonalReal;
                    n22 = 2.0 * foldedReferenceReal * diagonalImaginary;
                    secondOrderCoefficient = 1.0;
                }

                a11[0][n] = n11;
                a12[0][n] = n12;
                a21[0][n] = n21;
                a22[0][n] = n22;
                b11[0][n] = bSeed;
                b12[0][n] = 0.0;
                b21[0][n] = 0.0;
                b22[0][n] = bSeed;

                if (blocked)
                {
                    r2[0][n] = 0.0;
                    continue;
                }

                // Отброшенный член ≤ secondOrder·|δ|², сохранённый ≥ σmin(A)·|δ| ⇒ их отношение
                // не хуже 1/tol при |δ| ≤ tol·σmin/secondOrder. Для отражённых это ровно
                // tol·2|Z| — та же величина, что даёт комплексная таблица при p=2.
                (double smallestSingular, _) = SingularValues2x2(n11, n12, n21, n22);
                double radius = secondOrderCoefficient > 0.0
                    ? BlaTolerance * smallestSingular / secondOrderCoefficient
                    : 0.0;
                radius = System.Math.Min(radius, foldLimit);
                r2[0][n] = double.IsFinite(radius) && radius > 0.0 ? radius * radius : 0.0;
            }

            // --- уровни выше: слияние соседних пар (x применяется первым, затем y) ---
            for (int k = 1; k < levels; k++)
            {
                int previous = count[k - 1];
                int current = (previous + 1) >> 1;
                count[k] = current;
                a11[k] = new double[current];
                a12[k] = new double[current];
                a21[k] = new double[current];
                a22[k] = new double[current];
                b11[k] = new double[current];
                b12[k] = new double[current];
                b21[k] = new double[current];
                b22[k] = new double[current];
                r2[k] = new double[current];

                for (int j = 0; j < current; j++)
                {
                    int left = 2 * j;
                    int right = 2 * j + 1;
                    if (right >= previous)
                    {
                        // Непарный хвост — переносим левый элемент как есть.
                        a11[k][j] = a11[k - 1][left];
                        a12[k][j] = a12[k - 1][left];
                        a21[k][j] = a21[k - 1][left];
                        a22[k][j] = a22[k - 1][left];
                        b11[k][j] = b11[k - 1][left];
                        b12[k][j] = b12[k - 1][left];
                        b21[k][j] = b21[k - 1][left];
                        b22[k][j] = b22[k - 1][left];
                        r2[k][j] = r2[k - 1][left];
                        continue;
                    }

                    double xA11 = a11[k - 1][left], xA12 = a12[k - 1][left];
                    double xA21 = a21[k - 1][left], xA22 = a22[k - 1][left];
                    double xB11 = b11[k - 1][left], xB12 = b12[k - 1][left];
                    double xB21 = b21[k - 1][left], xB22 = b22[k - 1][left];
                    double xR2 = r2[k - 1][left];

                    double yA11 = a11[k - 1][right], yA12 = a12[k - 1][right];
                    double yA21 = a21[k - 1][right], yA22 = a22[k - 1][right];
                    double yB11 = b11[k - 1][right], yB12 = b12[k - 1][right];
                    double yB21 = b21[k - 1][right], yB22 = b22[k - 1][right];
                    double yR2 = r2[k - 1][right];

                    // A_z = A_y·A_x ; B_z = A_y·B_x + B_y   (матрично)
                    double zA11 = yA11 * xA11 + yA12 * xA21;
                    double zA12 = yA11 * xA12 + yA12 * xA22;
                    double zA21 = yA21 * xA11 + yA22 * xA21;
                    double zA22 = yA21 * xA12 + yA22 * xA22;
                    double zB11 = yA11 * xB11 + yA12 * xB21 + yB11;
                    double zB12 = yA11 * xB12 + yA12 * xB22 + yB12;
                    double zB21 = yA21 * xB11 + yA22 * xB21 + yB21;
                    double zB22 = yA21 * xB12 + yA22 * xB22 + yB22;

                    double zR2;
                    if (xR2 <= 0.0 || yR2 <= 0.0 ||
                        !double.IsFinite(zA11) || !double.IsFinite(zA12) ||
                        !double.IsFinite(zA21) || !double.IsFinite(zA22) ||
                        !double.IsFinite(zB11) || !double.IsFinite(zB12) ||
                        !double.IsFinite(zB21) || !double.IsFinite(zB22))
                    {
                        zR2 = 0.0;
                    }
                    else
                    {
                        // |δ_in| ≤ r_x  и  |A_x·δ_in + B_x·δc| ≤ r_y ⇒
                        // r_z = min( r_x , max(0, (r_y − ‖B_x‖·δcmax) / ‖A_x‖) ), ‖·‖ — σmax.
                        double xANorm = SpectralNorm2x2(xA11, xA12, xA21, xA22);
                        double xBNorm = SpectralNorm2x2(xB11, xB12, xB21, xB22);
                        double rx = System.Math.Sqrt(xR2);
                        double ry = System.Math.Sqrt(yR2);
                        // См. BlaTable.Build: вклад δc считается в расширенном диапазоне.
                        double bCorrection = (FloatExp.FromDouble(xBNorm) * deltaCMax).ToDouble();
                        double bound = xANorm > 0.0 ? (ry - bCorrection) / xANorm : 0.0;
                        double rz = System.Math.Min(rx, System.Math.Max(0.0, bound));
                        zR2 = double.IsFinite(rz) ? rz * rz : 0.0;
                    }

                    a11[k][j] = zA11;
                    a12[k][j] = zA12;
                    a21[k][j] = zA21;
                    a22[k][j] = zA22;
                    b11[k][j] = zB11;
                    b12[k][j] = zB12;
                    b21[k][j] = zB21;
                    b22[k][j] = zB22;
                    r2[k][j] = zR2;
                }
            }

            double maxLevel1RadiusSquared = 0.0;
            foreach (double value in r2[1])
                if (value > maxLevel1RadiusSquared) maxLevel1RadiusSquared = value;

            return new RealBlaTable
            {
                A11 = a11,
                A12 = a12,
                A21 = a21,
                A22 = a22,
                B11 = b11,
                B12 = b12,
                B21 = b21,
                B22 = b22,
                R2 = r2,
                Count = count,
                Levels = levels,
                MaxReferenceIndex = length - 1,
                Level1RadiusSquared = r2[1],
                Level1Count = count[1],
                MaxLevel1RadiusSquared = maxLevel1RadiusSquared,
            };
        }

        /// <summary>
        /// Дешёвая проверка «есть ли смысл искать». Пропуск длиной хотя бы в две итерации
        /// требует узла первого уровня в текущей точке, а радиусы по уровням не растут
        /// (при слиянии r_z = min(r_x, …) ≤ r_x, а r_x — радиус левого потомка в той же точке).
        /// Поэтому отказ здесь равносилен отказу <see cref="TryLookup"/> и только избавляет
        /// от его вызова в подавляющем большинстве итераций, где δ уже выросло.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public bool CanSkip(int referenceIndex, double deltaMagnitudeSquared)
        {
            if (deltaMagnitudeSquared >= MaxLevel1RadiusSquared || (referenceIndex & 1) != 0) return false;
            int j = referenceIndex >> 1;
            return j < Level1Count && deltaMagnitudeSquared < Level1RadiusSquared[j];
        }

        /// <summary>
        /// Наибольший применимый BLA из <paramref name="referenceIndex"/> — правила выбора
        /// те же, что в <see cref="BlaTable.TryLookup"/>.
        /// </summary>
        public bool TryLookup(
            int referenceIndex, double deltaMagnitudeSquared, int iterationBudget,
            out double aa11, out double aa12, out double aa21, out double aa22,
            out double bb11, out double bb12, out double bb21, out double bb22, out int steps)
        {
            aa11 = 1.0;
            aa12 = 0.0;
            aa21 = 0.0;
            aa22 = 1.0;
            bb11 = 0.0;
            bb12 = 0.0;
            bb21 = 0.0;
            bb22 = 0.0;
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

                aa11 = A11[k][j];
                aa12 = A12[k][j];
                aa21 = A21[k][j];
                aa22 = A22[k][j];
                bb11 = B11[k][j];
                bb12 = B12[k][j];
                bb21 = B21[k][j];
                bb22 = B22[k][j];
                steps = span;
            }

            return steps >= 2;
        }
    }
}
