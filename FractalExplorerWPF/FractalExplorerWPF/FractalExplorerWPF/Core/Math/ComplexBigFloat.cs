using System.Numerics;

namespace FractalExplorerWPF.Core.NewtonMath;

/// <summary>
/// Комплексное число с компонентами <see cref="BigFloat"/>: сложение, вычитание, умножение
/// (на комплексное, на вещественное и на небольшое целое), деление, sin/cos от π·z, а также
/// экспонента, логарифм и степень.
///
/// Аналог <see cref="FractalExplorer.Utilities.ComplexDecimal"/> для ступени, где decimal
/// уже не хватает.
///
/// Набор рос по запросам движков: sin/cos от π·z появились ради прямой итерации Коллатца, а
/// деление, <see cref="Log"/> и <see cref="Pow(ComplexBigFloat, ComplexBigFloat)"/> — ради
/// опорной орбиты Nova, где формула содержит <c>z^(1−p)</c> с произвольной комплексной
/// степенью.
/// </summary>
public readonly struct ComplexBigFloat : IEquatable<ComplexBigFloat>
{
    public BigFloat Real { get; }
    public BigFloat Imaginary { get; }

    public ComplexBigFloat(BigFloat real, BigFloat imaginary)
    {
        Real = real;
        Imaginary = imaginary;
    }

    public static ComplexBigFloat Zero => default;

    /// <summary>Единица. Нужна нейтральным элементом бинарного возведения в степень.</summary>
    public static ComplexBigFloat One => new(BigFloat.One, BigFloat.Zero);

    /// <summary>Запас точности промежуточных величин — тот же, что в <see cref="BigFloatMath"/>.</summary>
    private const int GuardBits = 32;

    /// <summary>Сколько верных бит даёт начальное приближение из <see cref="Complex"/>.</summary>
    private const int SeedBits = 50;

    public static ComplexBigFloat FromDouble(double real, double imaginary) =>
        new(BigFloat.FromDouble(real), BigFloat.FromDouble(imaginary));

    public static ComplexBigFloat FromDecimal(decimal real, decimal imaginary) =>
        new(BigFloat.FromDecimal(real), BigFloat.FromDecimal(imaginary));

    public BigFloat MagnitudeSquared => Real * Real + Imaginary * Imaginary;

    public Complex ToComplex() => new(Real.ToDouble(), Imaginary.ToDouble());

    /// <summary>Умножение обеих компонент на 2^shift — точная операция.</summary>
    public static ComplexBigFloat ScaleByPowerOfTwo(ComplexBigFloat value, int shift) =>
        new(BigFloat.ScaleByPowerOfTwo(value.Real, shift),
            BigFloat.ScaleByPowerOfTwo(value.Imaginary, shift));

    public bool Equals(ComplexBigFloat other) =>
        Real.Equals(other.Real) && Imaginary.Equals(other.Imaginary);

    public override bool Equals(object? obj) => obj is ComplexBigFloat other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Real, Imaginary);

    public static ComplexBigFloat operator -(ComplexBigFloat value) =>
        new(-value.Real, -value.Imaginary);

    public static ComplexBigFloat operator +(ComplexBigFloat left, ComplexBigFloat right) =>
        new(left.Real + right.Real, left.Imaginary + right.Imaginary);

    public static ComplexBigFloat operator -(ComplexBigFloat left, ComplexBigFloat right) =>
        new(left.Real - right.Real, left.Imaginary - right.Imaginary);

    public static ComplexBigFloat operator *(ComplexBigFloat left, ComplexBigFloat right) =>
        new(left.Real * right.Real - left.Imaginary * right.Imaginary,
            left.Real * right.Imaginary + left.Imaginary * right.Real);

    public static ComplexBigFloat operator *(ComplexBigFloat left, BigFloat right) =>
        new(left.Real * right, left.Imaginary * right);

    public static ComplexBigFloat operator *(BigFloat left, ComplexBigFloat right) => right * left;

    public static ComplexBigFloat operator *(ComplexBigFloat left, long right) =>
        new(left.Real * right, left.Imaginary * right);

    public static ComplexBigFloat operator /(ComplexBigFloat left, long right) =>
        new(left.Real / right, left.Imaginary / right);

    public static ComplexBigFloat operator +(ComplexBigFloat left, long right) =>
        new(left.Real + BigFloat.FromInt(right), left.Imaginary);

    public static ComplexBigFloat operator -(ComplexBigFloat left, long right) =>
        new(left.Real - BigFloat.FromInt(right), left.Imaginary);

    public static ComplexBigFloat operator +(long left, ComplexBigFloat right) => right + left;

    public static ComplexBigFloat operator -(long left, ComplexBigFloat right) =>
        new(BigFloat.FromInt(left) - right.Real, -right.Imaginary);

    /// <summary>
    /// sin(πz) и cos(πz) за один проход: вещественная часть даёт sin/cos, мнимая — sh/ch,
    /// и обе пары переиспользуются обеими функциями. Формулы Коллатца просят то одну из них,
    /// то обе, а дорогая часть у них общая.
    /// </summary>
    public static void SinCosPi(ComplexBigFloat value, out ComplexBigFloat sin, out ComplexBigFloat cos)
    {
        BigFloatMath.SinCosPi(value.Real, out BigFloat sine, out BigFloat cosine);
        BigFloatMath.SinhCosh(BigFloatMath.Pi * value.Imaginary,
            out BigFloat hyperbolicSine, out BigFloat hyperbolicCosine);
        sin = new ComplexBigFloat(sine * hyperbolicCosine, cosine * hyperbolicSine);
        cos = new ComplexBigFloat(cosine * hyperbolicCosine, -(sine * hyperbolicSine));
    }

    /// <summary>
    /// cos(πz), когда синус вызывающему не нужен. Вещественные sin/cos и ch/sh всё равно
    /// нужны оба, а вот собирать из них вторую комплексную величину незачем.
    /// </summary>
    public static ComplexBigFloat CosPi(ComplexBigFloat value)
    {
        BigFloatMath.SinCosPi(value.Real, out BigFloat sine, out BigFloat cosine);
        BigFloatMath.SinhCosh(BigFloatMath.Pi * value.Imaginary,
            out BigFloat hyperbolicSine, out BigFloat hyperbolicCosine);
        return new ComplexBigFloat(cosine * hyperbolicCosine, -(sine * hyperbolicSine));
    }

    /// <summary>sin(πz), когда косинус вызывающему не нужен.</summary>
    public static ComplexBigFloat SinPi(ComplexBigFloat value)
    {
        BigFloatMath.SinCosPi(value.Real, out BigFloat sine, out BigFloat cosine);
        BigFloatMath.SinhCosh(BigFloatMath.Pi * value.Imaginary,
            out BigFloat hyperbolicSine, out BigFloat hyperbolicCosine);
        return new ComplexBigFloat(sine * hyperbolicCosine, cosine * hyperbolicSine);
    }

    /// <summary>
    /// Комплексное деление через сопряжённое. Знаменатель — вещественный |b|², поэтому обе
    /// компоненты делятся полноразрядным <see cref="BigFloat"/>-делением по одному разу.
    /// </summary>
    public static ComplexBigFloat operator /(ComplexBigFloat left, ComplexBigFloat right)
    {
        BigFloat denominator = right.MagnitudeSquared;
        if (denominator.IsZero) throw new DivideByZeroException("Деление ComplexBigFloat на ноль.");
        return new ComplexBigFloat(
            (left.Real * right.Real + left.Imaginary * right.Imaginary) / denominator,
            (left.Imaginary * right.Real - left.Real * right.Imaginary) / denominator);
    }

    /// <summary>Комплексная экспонента: e^z = e^(Re z)·(cos Im z + i·sin Im z).</summary>
    public static ComplexBigFloat Exp(ComplexBigFloat value)
    {
        BigFloat magnitude = BigFloatMath.Exp(value.Real);
        BigFloatMath.SinCos(value.Imaginary, out BigFloat sine, out BigFloat cosine);
        return new ComplexBigFloat(magnitude * cosine, magnitude * sine);
    }

    /// <summary>
    /// Главная ветвь комплексного логарифма — обращением <see cref="Exp"/> методом Ньютона:
    /// <c>w ← w + z·e^(−w) − 1</c>. Отдельные ln|z| и arg z не нужны, а значит не нужен и
    /// арктангенс произвольной точности: обе компоненты выходят из одной итерации.
    ///
    /// Ветвь задаётся начальным приближением. Оно берётся у <see cref="Complex.Log"/>, то есть
    /// главное, а квадратичная сходимость уводит от него не дальше чем на 1e-16 — попасть на
    /// соседнюю ветвь (они отстоят на 2πi) итерация не может.
    ///
    /// Аргумент предварительно делится на степень двойки так, чтобы |z| оказался около
    /// единицы: иначе ни double-приближение, ни сама итерация не работали бы за пределами
    /// диапазона double. Компенсация — слагаемое e·ln 2 в вещественной части.
    /// </summary>
    public static ComplexBigFloat Log(ComplexBigFloat value)
    {
        int exponent = System.Math.Max(value.Real.BinaryExponent, value.Imaginary.BinaryExponent);
        if (exponent == int.MinValue)
            throw new ArgumentOutOfRangeException(nameof(value), "Логарифм нуля.");

        int precision = BigFloat.WorkingPrecisionBits;
        int target = precision + GuardBits;
        BigFloat real, imaginary;
        using (var scope = new BigFloat.PrecisionScope(target))
        {
            ComplexBigFloat scaled = ScaleByPowerOfTwo(value, -exponent);
            Complex seed = Complex.Log(scaled.ToComplex());
            ComplexBigFloat result = FromDouble(seed.Real, seed.Imaginary);
            for (int bits = SeedBits; bits < target;)
            {
                bits = System.Math.Min(target, bits * 2);
                using var step = new BigFloat.PrecisionScope(bits + GuardBits);
                result += scaled * Exp(-result) - 1;
            }
            real = exponent == 0 ? result.Real : result.Real + BigFloatMath.LogTwo * exponent;
            imaginary = result.Imaginary;
        }
        return new ComplexBigFloat(
            BigFloat.FromScaled(real.Mantissa, real.Exponent),
            BigFloat.FromScaled(imaginary.Mantissa, imaginary.Exponent));
    }

    /// <summary>z^p = exp(p·ln z) на главной ветви — та же формула, что у
    /// <see cref="Complex.Pow"/> и у <see cref="FractalExplorer.Utilities.ComplexDecimal.Pow"/>,
    /// поэтому ступени точности дают одно и то же значение.</summary>
    public static ComplexBigFloat Pow(ComplexBigFloat value, ComplexBigFloat exponent) =>
        Exp(exponent * Log(value));

    /// <summary>
    /// Целая степень бинарным возведением: ни логарифма, ни экспоненты, ни выбора ветви —
    /// только умножения, и результат совпадает с <see cref="Pow(ComplexBigFloat,
    /// ComplexBigFloat)"/> с точностью до округлений. Отрицательный показатель берётся как
    /// обратное к положительному.
    /// </summary>
    public static ComplexBigFloat Pow(ComplexBigFloat value, int exponent)
    {
        if (exponent == 0) return One;

        ComplexBigFloat result = One;
        ComplexBigFloat factor = value;
        for (int remaining = System.Math.Abs(exponent); remaining > 0; remaining >>= 1)
        {
            if ((remaining & 1) != 0) result *= factor;
            if (remaining > 1) factor *= factor;
        }
        return exponent > 0 ? result : One / result;
    }

    public override string ToString() =>
        $"{Real.ToInvariantString(30)} + {Imaginary.ToInvariantString(30)}i";
}
