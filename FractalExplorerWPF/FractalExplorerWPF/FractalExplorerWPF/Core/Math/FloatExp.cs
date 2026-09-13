using System.Globalization;
using System.Numerics;
using System.Text;

namespace FractalExplorerWPF.Core.NewtonMath;

/// <summary>
/// Число с плавающей запятой и расширенным диапазоном экспоненты:
/// значение = <see cref="Mantissa"/> · 2^<see cref="Exponent"/>, где мантисса —
/// обычный <see cref="double"/>, нормализованный в диапазон [1; 2) (со знаком), а
/// экспонента — 32-битное целое. Даёт ту же ~52-битную относительную точность, что и
/// <see cref="double"/>, но без потолка ±1e308 и без потери значимости в денормалах.
///
/// Обслуживает две роли «второго двигателя» глубокого зума:
/// 1. Отклонение δ на пиксель: на зуме за ~1e72 δ (и особенно δ²) перестаёт помещаться в
///    обычный <see cref="double"/> — δ² уходит в денормалы и в ноль.
/// 2. Сам коэффициент зума и пиксельная сетка кадра: за 1.8e308 множитель зума не
///    представим в double вовсе, а ширина вида 3/zoom обращается в ноль.
///
/// Опорная орбита при этом остаётся в <see cref="double"/>: её значения ограничены
/// радиусом бейлаута и в расширенном диапазоне не нуждаются. Позиция центра требует не
/// диапазона, а разрядности мантиссы, и ведётся в <see cref="BigFloat"/>.
/// </summary>
public readonly struct FloatExp : IComparable<FloatExp>, IEquatable<FloatExp>, IFormattable
{
    /// <summary>Нормализованная мантисса: 0 либо |Mantissa| ∈ [1; 2).</summary>
    public readonly double Mantissa;

    /// <summary>Двоичная экспонента. Для нулевого значения — 0.</summary>
    public readonly int Exponent;

    // Разность экспонент, за которой меньшее слагаемое уже не влияет на 52-битную
    // мантиссу большего. 120 — с запасом больше 53 и не заходит в денормалы double.
    private const int NegligibleShift = 120;

    // Граница насыщения экспоненты: за ней значение считается бесконечным (а обратное —
    // нулём). Запас до int.MaxValue нужен, чтобы сложение двух экспонент в умножении
    // гарантированно не переполняло сам int — поэтому арифметика идёт в long, а
    // насыщение проверяется по этой границе.
    private const int ExponentLimit = 1 << 30;

    // Предел десятичного показателя для разбора строк и <see cref="Pow10"/>. Нужен не для
    // точности, а чтобы «1e99999999» не уходило в BigInteger.Pow(10, …): там показатель задаёт
    // размер целого числа напрямую, и такая строка съела бы память. Предел с запасом
    // перекрывает и диапазон самого типа (двоичная экспонента ±2^30), и диапазон BigFloat
    // (±2^20 бит ≈ ±1e315653), за которыми значение всё равно насыщается.
    private const int MaximumDecimalExponent = 400_000;

    private FloatExp(double mantissa, int exponent)
    {
        Mantissa = mantissa;
        Exponent = exponent;
    }

    public static FloatExp Zero => default;

    /// <summary>Единица.</summary>
    public static FloatExp One { get; } = new(1.0, 0);

    public bool IsZero => Mantissa == 0.0;

    /// <summary>Значение конечно (не ±∞ и не NaN).</summary>
    public bool IsFinite => double.IsFinite(Mantissa);

    /// <summary>Знак значения: −1, 0 или +1.</summary>
    public int Sign => Mantissa > 0.0 ? 1 : Mantissa < 0.0 ? -1 : 0;

    /// <summary>Нормализует произвольные mantissa·2^exponent в канонический вид.</summary>
    private static FloatExp Normalize(double mantissa, long exponent)
    {
        if (mantissa == 0.0) return default;
        if (!double.IsFinite(mantissa)) return new FloatExp(mantissa, 0);
        int shift = System.Math.ILogB(mantissa);
        long result = exponent + shift;
        if (result > ExponentLimit)
            return new FloatExp(double.IsNegative(mantissa) ? double.NegativeInfinity : double.PositiveInfinity, 0);
        if (result < -ExponentLimit) return default;
        return new FloatExp(System.Math.ScaleB(mantissa, -shift), (int)result);
    }

    public static FloatExp FromDouble(double value)
    {
        if (value == 0.0 || !double.IsFinite(value)) return value == 0.0 ? default : new FloatExp(value, 0);
        int shift = System.Math.ILogB(value);
        return new FloatExp(System.Math.ScaleB(value, -shift), shift);
    }

    /// <summary>
    /// Значение <see cref="BigFloat"/> с округлением до 53-битной мантиссы. Диапазон
    /// экспоненты у обоих типов двоичный, поэтому теряется только разрядность.
    /// </summary>
    public static FloatExp FromBigFloat(BigFloat value)
    {
        if (value.Mantissa.IsZero) return default;
        BigMantissa mantissa = value.Mantissa;
        int bits = mantissa.GetBitLength();
        long exponent = value.Exponent;
        if (bits > 53)
        {
            int drop = bits - 53;
            int sign = mantissa.Sign;
            BigMantissa magnitude = BigMantissa.Abs(mantissa);
            magnitude = (magnitude + (BigMantissa.One << (drop - 1))) >> drop;
            mantissa = sign < 0 ? -magnitude : magnitude;
            exponent += drop;
        }

        double signedMagnitude = mantissa.Sign < 0 ? -mantissa.ToDoubleMagnitude() : mantissa.ToDoubleMagnitude();
        return Normalize(signedMagnitude, exponent);
    }

    /// <summary>Точное (мантисса переносится целиком) значение как <see cref="BigFloat"/>.</summary>
    public BigFloat ToBigFloat()
    {
        if (IsZero || !IsFinite) return BigFloat.Zero;
        // Mantissa ∈ [1;2) ⇒ Mantissa·2^52 — целое в [2^52; 2^53).
        long scaled = (long)System.Math.ScaleB(Mantissa, 52);
        return BigFloat.FromScaled(scaled, Exponent - 52);
    }

    /// <summary>
    /// Ближайший <see cref="double"/>. Слишком малое по модулю значение обращается в 0,
    /// слишком большое — в ±∞; и то и другое корректно для сравнений в пиксельном цикле
    /// (пренебрежимо мало / убежало за радиус).
    /// </summary>
    public double ToDouble() => IsZero ? 0.0 : System.Math.ScaleB(Mantissa, Exponent);

    /// <summary>Двоичный логарифм |значения|; для нуля — <see cref="double.NegativeInfinity"/>.</summary>
    public double Log2() => IsZero
        ? double.NegativeInfinity
        : Exponent + System.Math.Log2(System.Math.Abs(Mantissa));

    /// <summary>Десятичный логарифм |значения|; для нуля — <see cref="double.NegativeInfinity"/>.</summary>
    public double Log10() => Log2() * 0.30102999566398120;

    /// <summary>
    /// 10 в целой степени — правильно округлённая до 53 бит мантиссы. Степень раскрывается
    /// точно в <see cref="BigInteger"/> и округляется один раз: возведение в квадрат самого
    /// <see cref="FloatExp"/> копило бы ошибку по одному ulp на умножение, и тогда
    /// <c>Pow10(1000)</c> не совпадал бы с <c>Parse("1e1000")</c>.
    /// </summary>
    public static FloatExp Pow10(int power)
    {
        if (System.Math.Abs(power) > MaximumDecimalExponent)
            return power > 0 ? new FloatExp(double.PositiveInfinity, 0) : default;

        using var precision = new BigFloat.PrecisionScope(BigFloat.MinimumPrecisionBits);
        BigFloat magnitude = BigFloat.FromScaled(BigInteger.Pow(10, System.Math.Abs(power)), 0);
        return FromBigFloat(power >= 0 ? magnitude : BigFloat.One / magnitude);
    }

    public static FloatExp Abs(FloatExp value) =>
        value.Mantissa < 0.0 ? new FloatExp(-value.Mantissa, value.Exponent) : value;

    public static FloatExp Max(FloatExp left, FloatExp right) => left >= right ? left : right;

    public static FloatExp Clamp(FloatExp value, FloatExp minimum, FloatExp maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;

    public static FloatExp Sqrt(FloatExp value)
    {
        if (value.IsZero) return default;
        if (value.Mantissa < 0.0) return new FloatExp(double.NaN, 0);
        // Экспонента должна быть чётной, чтобы делиться пополам без остатка.
        double mantissa = value.Mantissa;
        int exponent = value.Exponent;
        if ((exponent & 1) != 0)
        {
            mantissa *= 2.0;
            exponent--;
        }

        return Normalize(System.Math.Sqrt(mantissa), exponent / 2);
    }

    public static FloatExp operator -(FloatExp value) =>
        value.IsZero ? default : new FloatExp(-value.Mantissa, value.Exponent);

    public static FloatExp operator +(FloatExp left, FloatExp right)
    {
        if (left.IsZero) return right;
        if (right.IsZero) return left;

        int difference = left.Exponent - right.Exponent;
        if (difference > NegligibleShift) return left;
        if (difference < -NegligibleShift) return right;

        return difference >= 0
            ? Normalize(left.Mantissa + System.Math.ScaleB(right.Mantissa, -difference), left.Exponent)
            : Normalize(System.Math.ScaleB(left.Mantissa, difference) + right.Mantissa, right.Exponent);
    }

    public static FloatExp operator -(FloatExp left, FloatExp right) => left + (-right);

    public static FloatExp operator *(FloatExp left, FloatExp right)
    {
        if (left.IsZero || right.IsZero) return default;
        // Мантиссы в [1; 2) ⇒ произведение в [1; 4): одной нормализации достаточно.
        return Normalize(left.Mantissa * right.Mantissa, (long)left.Exponent + right.Exponent);
    }

    public static FloatExp operator *(FloatExp left, double right) => left * FromDouble(right);
    public static FloatExp operator *(double left, FloatExp right) => FromDouble(left) * right;

    public static FloatExp operator /(FloatExp left, FloatExp right)
    {
        if (right.IsZero)
            return left.IsZero
                ? new FloatExp(double.NaN, 0)
                : new FloatExp(left.Mantissa < 0.0 ? double.NegativeInfinity : double.PositiveInfinity, 0);
        if (left.IsZero) return default;
        // Мантиссы в [1; 2) ⇒ частное в (0.5; 2): одной нормализации достаточно.
        return Normalize(left.Mantissa / right.Mantissa, (long)left.Exponent - right.Exponent);
    }

    public static FloatExp operator /(FloatExp left, double right) => left / FromDouble(right);
    public static FloatExp operator /(double left, FloatExp right) => FromDouble(left) / right;

    /// <summary>
    /// Неявное расширение <see cref="double"/>: числовые литералы и существующие
    /// double-вычисления остаются читаемыми там, где тип поля стал расширенным.
    /// Обратного неявного сужения намеренно нет — оно теряло бы диапазон молча.
    /// </summary>
    public static implicit operator FloatExp(double value) => FromDouble(value);

    public int CompareTo(FloatExp other)
    {
        int sign = Sign;
        int otherSign = other.Sign;
        if (sign != otherSign) return sign.CompareTo(otherSign);
        if (sign == 0) return 0;
        if (!IsFinite || !other.IsFinite) return ToDouble().CompareTo(other.ToDouble());
        if (Exponent != other.Exponent)
            return sign > 0 ? Exponent.CompareTo(other.Exponent) : other.Exponent.CompareTo(Exponent);
        return Mantissa.CompareTo(other.Mantissa);
    }

    public static bool operator <(FloatExp left, FloatExp right) => left.CompareTo(right) < 0;
    public static bool operator >(FloatExp left, FloatExp right) => left.CompareTo(right) > 0;
    public static bool operator <=(FloatExp left, FloatExp right) => left.CompareTo(right) <= 0;
    public static bool operator >=(FloatExp left, FloatExp right) => left.CompareTo(right) >= 0;
    public static bool operator ==(FloatExp left, FloatExp right) => left.Equals(right);
    public static bool operator !=(FloatExp left, FloatExp right) => !left.Equals(right);

    public bool Equals(FloatExp other) => Mantissa.Equals(other.Mantissa) && Exponent == other.Exponent;
    public override bool Equals(object? obj) => obj is FloatExp other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Mantissa, Exponent);

    /// <summary>|re|² + |im|² как <see cref="FloatExp"/> — без промежуточного переполнения диапазона.</summary>
    public static FloatExp MagnitudeSquared(FloatExp re, FloatExp im) => re * re + im * im;

    /// <summary>Число значащих десятичных цифр в <see cref="ToInvariantString"/>.</summary>
    private const int SignificantDigits = 17;

    /// <summary>
    /// Round-trip строка инвариантной культуры. В пределах диапазона double — обычный
    /// формат <c>"R"</c> (старые сохранения и привычный вид), вне него — научная нотация с
    /// точной (через <see cref="BigInteger"/>) десятичной мантиссой на
    /// <see cref="SignificantDigits"/> цифр. <see cref="Parse"/> восстанавливает значение с
    /// погрешностью ниже одного ulp double.
    /// </summary>
    public string ToInvariantString()
    {
        if (IsZero) return "0";
        if (!IsFinite) return double.IsNegative(Mantissa) ? "-Infinity" : "Infinity";

        double asDouble = ToDouble();
        if (asDouble != 0.0 && double.IsFinite(asDouble))
            return asDouble.ToString("R", CultureInfo.InvariantCulture);

        // Значение = m·2^e, где m — целое в [2^52; 2^53).
        long integerMantissa = (long)System.Math.ScaleB(System.Math.Abs(Mantissa), 52);
        int binaryExponent = Exponent - 52;

        int decimalExponent = (int)System.Math.Floor(Log10());
        string digits = SignificantDecimalDigits(
            integerMantissa, binaryExponent, decimalExponent, out decimalExponent);

        var builder = new StringBuilder();
        if (Mantissa < 0.0) builder.Append('-');
        builder.Append(digits[0]);
        if (digits.Length > 1) builder.Append('.').Append(digits[1..].TrimEnd('0'));
        if (builder[^1] == '.') builder.Length--;
        builder.Append('e').Append(decimalExponent.ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    /// <summary>
    /// Округляет |mantissa·2^binaryExponent| до <see cref="SignificantDigits"/> десятичных
    /// цифр и возвращает их строкой, уточняя показатель степени (оценка
    /// <paramref name="decimalExponent"/> может промахнуться на единицу, а округление
    /// 99…9 → 10…0 сдвигает её ещё на единицу).
    /// </summary>
    private static string SignificantDecimalDigits(
        long mantissa, int binaryExponent, int decimalExponent, out int adjustedExponent)
    {
        for (int attempt = 0; ; attempt++)
        {
            BigInteger numerator = mantissa;
            BigInteger denominator = BigInteger.One;
            if (binaryExponent >= 0) numerator <<= binaryExponent;
            else denominator <<= -binaryExponent;

            int scale = decimalExponent - (SignificantDigits - 1);
            if (scale >= 0) denominator *= BigInteger.Pow(10, scale);
            else numerator *= BigInteger.Pow(10, -scale);

            BigInteger rounded = (numerator + (denominator >> 1)) / denominator;
            string digits = rounded.ToString(CultureInfo.InvariantCulture);
            if (digits.Length == SignificantDigits || attempt >= 2)
            {
                adjustedExponent = decimalExponent + (digits.Length - SignificantDigits);
                return digits.Length > SignificantDigits ? digits[..SignificantDigits] : digits;
            }

            decimalExponent += digits.Length - SignificantDigits;
        }
    }

    /// <summary>
    /// Разбор десятичной строки (инвариантная культура), в том числе в научной нотации и
    /// за пределами диапазона double. Точность разбора — 53 бита мантиссы: десятичная
    /// степень раскрывается точно через <see cref="BigFloat"/>, поэтому накопления
    /// погрешности от возведения 10 в степень здесь нет.
    /// </summary>
    public static FloatExp Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        text = text.Trim();

        if (text.Equals("Infinity", StringComparison.OrdinalIgnoreCase))
            return new FloatExp(double.PositiveInfinity, 0);
        if (text.Equals("-Infinity", StringComparison.OrdinalIgnoreCase))
            return new FloatExp(double.NegativeInfinity, 0);

        // Обычные значения разбирает double — это сохраняет поведение прежних сохранений
        // бит-в-бит. Выход за диапазон (0 или ±∞ при непустой мантиссе) уводит на точный
        // путь через BigFloat, которому произвольная десятичная степень по силам.
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double direct) &&
            direct != 0.0 && double.IsFinite(direct))
            return FromDouble(direct);

        // Абсурдный показатель насыщаем до разбора — см. MaximumDecimalExponent.
        int exponentMark = text.IndexOfAny(['e', 'E']);
        if (exponentMark >= 0 &&
            int.TryParse(text[(exponentMark + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int decimalExponent) &&
            System.Math.Abs(decimalExponent) > MaximumDecimalExponent)
            return decimalExponent < 0
                ? default
                : new FloatExp(text[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity, 0);

        // BigFloat.Parse работает с мантиссой рабочей точности потока; 96 бит (абсолютный
        // минимум типа) с запасом хватает для 53-битного результата и не зависит от того,
        // какую точность выставил вызывающий поток.
        using var precision = new BigFloat.PrecisionScope(BigFloat.AbsoluteMinimumPrecisionBits);
        return FromBigFloat(BigFloat.Parse(text));
    }

    public static bool TryParse(string? text, out FloatExp value)
    {
        try
        {
            value = Parse(text ?? string.Empty);
            return value.IsFinite;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            value = default;
            return false;
        }
    }

    public override string ToString() => ToInvariantString();

    /// <summary>
    /// Форматирование как у <see cref="double"/>, пока значение в его диапазоне: строки вида
    /// <c>$"{zoom:G6}"</c> в существующем коде продолжают работать как раньше. За диапазоном
    /// спецификатор неприменим, и возвращается научная нотация
    /// (<see cref="ToInvariantString"/>).
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        double asDouble = ToDouble();
        return double.IsFinite(asDouble) && (asDouble != 0.0 || IsZero)
            ? asDouble.ToString(format, formatProvider)
            : ToInvariantString();
    }
}
