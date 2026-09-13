using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FractalExplorerWPF.Core.NewtonMath;

/// <summary>
/// Инлайн-буфер лимбов мантиссы: физически лежит внутри <see cref="BigMantissa"/> (и вместе
/// с ней — внутри массивов вроде <c>ComplexBigFloat[]</c>), а не в отдельном объекте на куче.
/// </summary>
[InlineArray(BigMantissa.InlineCapacity)]
internal struct MantissaLimbBuffer
{
    private ulong _element0;
}

/// <summary>
/// Знаковая целая величина мантиссы <see cref="BigFloat"/> с оптимизацией малого буфера:
/// лимбы (64-битные слова, младший первым) хранятся ЛИБО в инлайн-буфере
/// <see cref="MantissaLimbBuffer"/> прямо внутри структуры (без единого выделения памяти,
/// пока длина не превышает <see cref="InlineCapacity"/>), ЛИБО, для более длинных значений —
/// в обычном массиве на куче.
///
/// Почему не один большой инлайн-буфер на все случаи (как было в первой версии). Планировщики
/// точности используют этот тип для двух принципиально разных ролей: опорная орбита
/// Мандельброта считается на глубине до нескольких тысяч бит, но один раз на КАДР, — а формула
/// Коллатца считается на сотнях бит, но один раз на каждую ИТЕРАЦИЮ КАЖДОГО ПИКСЕЛЯ. Инлайн-
/// буфер, вмещающий даже редкий тысячебитный случай Мандельброта (сотни лишних лимбов), делает
/// саму структуру огромной — и это огромное значение копируется по значению на каждом шаге
/// вложенного вычисления одного выражения формулы Коллатца (<c>ApplyFormula</c> → <c>CosPi</c>
/// → <c>SinCosPi</c> → ряд Тейлора), а таких шагов на одну итерацию — сотни. Измерено напрямую:
/// один и тот же кадр Коллатца отрисовывался в 2.4 раза МЕДЛЕННЕЕ прежнего
/// <see cref="System.Numerics.BigInteger"/>-движка при инлайн-ёмкости 96 лимбов и всё ещё вдвое
/// медленнее при 64 (минимум, покрывающий реальную потребность Мандельброта) — притом что сама
/// арифметика (сложение/умножение/деление по отдельности) не медленнее прежней. Малый инлайн-
/// буфер (<see cref="InlineCapacity"/> = 512 бит, с запасом перекрывает
/// <see cref="BigFloat.MinimumPrecisionBits"/> и типичные надбавки защитных бит
/// трансцендентных функций) держит структуру компактной для подавляющего большинства операций;
/// переполнение (глубокий зум Коллатца, опорная орбита Мандельброта на большой глубине) — по
/// определению редкий, не попиксельный путь, и там одно выделение массива на куче незаметно на
/// фоне самой длинной арифметики.
///
/// «Сырые» (ещё не округлённые) промежуточные величины сложения/умножения — заведомо шире
/// любого из этих двух представлений — никогда не заворачиваются в <see cref="BigMantissa"/> в
/// принципе: они считаются во временном <see langword="stackalloc"/>-буфере ровно нужного
/// размера внутри одного вызова (см. <c>*Rounded</c>-методы ниже) и сразу же округляются, так и
/// не покинув свой стек-фрейм в «широком» виде.
///
/// Редкие (не входящие в горячий путь пошаговой итерации орбиты) операции — разбор и вывод
/// десятичной строки, точное деление одного <see cref="BigFloat"/> на другой и квадратный
/// корень — по-прежнему считаются через <see cref="System.Numerics.BigInteger"/>
/// (см. <see cref="ToBigInteger"/>/<see cref="FromBigInteger"/>): они вызываются самое большее
/// один раз на строку кадра или на шаг опорной орбиты, а не на каждый пиксель каждой итерации.
/// </summary>
public readonly struct BigMantissa : IEquatable<BigMantissa>
{
    /// <summary>
    /// 8 лимбов = 512 бит. Покрывает <see cref="BigFloat.MinimumPrecisionBits"/> (384) плюс
    /// типичную надбавку защитных бит трансцендентных функций (обычно ≤128 сверху) — то есть
    /// подавляющее большинство операций движка на любой обычной глубине зума укладывается в
    /// инлайн-буфер без единого выделения памяти. Больше — редкий (не попиксельный) случай,
    /// см. <see cref="_overflow"/>.
    /// </summary>
    internal const int InlineCapacity = 8;

    /// <summary>
    /// Верхняя граница для запасного пути на куче — исключительно защита от программной
    /// ошибки (например, зацикленного роста точности), превращающая её в громкое исключение,
    /// а не в молчаливое повреждение данных или неограниченный рост памяти.
    /// </summary>
    private const int OverflowCapacity = 4096;

    private readonly MantissaLimbBuffer _inline;
    private readonly ulong[]? _overflow;
    private readonly int _length;
    private readonly int _sign;

    public static readonly BigMantissa Zero = default;
    public static readonly BigMantissa One = FromLong(1);

    /// <summary>Строит канонический (без старших нулевых лимбов) экземпляр и копирует лимбы.</summary>
    private BigMantissa(ReadOnlySpan<ulong> limbs, int sign)
    {
        int length = limbs.Length;
        while (length > 0 && limbs[length - 1] == 0) length--;
        if (length == 0 || sign == 0)
        {
            _inline = default;
            _overflow = null;
            _length = 0;
            _sign = 0;
            return;
        }

        if (length > OverflowCapacity)
            throw new OverflowException(
                $"Мантисса BigFloat превысила поддерживаемую ёмкость ({OverflowCapacity} лимбов, нужно {length}).");

        _length = length;
        _sign = sign;
        if (length <= InlineCapacity)
        {
            _overflow = null;
            // Хвост буфера за пределами length никогда не читается (AsSpan всегда возвращает
            // ровно [0, _length)), поэтому обнулять его ради нескольких значащих лимбов —
            // чистая трата: на каждую операцию движка приходится ровно одно такое построение.
            Unsafe.SkipInit(out MantissaLimbBuffer buffer);
            Span<ulong> span = buffer;
            limbs[..length].CopyTo(span);
            _inline = buffer;
        }
        else
        {
            _inline = default;
            _overflow = new ulong[length];
            limbs[..length].CopyTo(_overflow);
        }
    }

    public bool IsZero => _sign == 0;
    public int Sign => _sign;
    internal int Length => _length;

    [System.Diagnostics.CodeAnalysis.UnscopedRef]
    internal ReadOnlySpan<ulong> AsSpan()
    {
        if (_overflow is not null) return _overflow.AsSpan(0, _length);
        ReadOnlySpan<ulong> full = _inline;
        return full[.._length];
    }

    // ------------------------------------------------------------------ конструкторы

    public static BigMantissa FromLong(long value)
    {
        if (value == 0) return Zero;
        int sign = value < 0 ? -1 : 1;
        ulong magnitude = MagnitudeOf(value);
        Span<ulong> single = stackalloc ulong[1];
        single[0] = magnitude;
        return new BigMantissa(single, sign);
    }

    private static ulong MagnitudeOf(long value) =>
        value < 0 ? (value == long.MinValue ? 0x8000_0000_0000_0000UL : (ulong)(-value)) : (ulong)value;

    /// <summary>
    /// Редкий мост к <see cref="System.Numerics.BigInteger"/> — для разбора и вывода
    /// десятичной строки, точного деления и квадратного корня, где случайная аллокация не
    /// стоит переписывания уже проверенного алгоритма на лимбы.
    /// </summary>
    public static BigMantissa FromBigInteger(BigInteger value)
    {
        if (value.IsZero) return Zero;
        int sign = value.Sign;
        BigInteger magnitude = BigInteger.Abs(value);
        int byteCount = magnitude.GetByteCount(isUnsigned: true);
        int limbCount = (byteCount + 7) / 8;
        Span<byte> bytes = stackalloc byte[limbCount * 8];
        bytes.Clear();
        magnitude.TryWriteBytes(bytes, out _, isUnsigned: true, isBigEndian: false);
        ReadOnlySpan<ulong> limbs = MemoryMarshal.Cast<byte, ulong>(bytes);
        return new BigMantissa(limbs, sign);
    }

    public BigInteger ToBigInteger()
    {
        if (IsZero) return BigInteger.Zero;
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(AsSpan());
        var magnitude = new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
        return _sign < 0 ? -magnitude : magnitude;
    }

    /// <summary>
    /// Значение младших ≤64 бит как <see cref="double"/> — точно, если <see cref="GetBitLength"/>
    /// не превышает 53 (ровно так это используется в <see cref="BigFloat.ToDouble"/>, где
    /// мантисса заранее уменьшена до 53 бит).
    /// </summary>
    internal double ToDoubleMagnitude()
    {
        if (IsZero) return 0;
        ReadOnlySpan<ulong> span = AsSpan();
        ulong low = span[0];
        if (span.Length == 1) return low;
        double result = low;
        double scale = 18446744073709551616.0; // 2^64
        for (int i = 1; i < span.Length; i++)
        {
            result += span[i] * scale;
            scale *= 18446744073709551616.0;
        }
        return result;
    }

    // ------------------------------------------------------------------ базовые свойства

    public int GetBitLength() => IsZero ? 0 : BitLength(AsSpan());

    public static BigMantissa Abs(BigMantissa value) =>
        value._sign < 0 ? new BigMantissa(value.AsSpan(), 1) : value;

    public static BigMantissa operator -(BigMantissa value) =>
        value.IsZero ? Zero : new BigMantissa(value.AsSpan(), -value._sign);

    // ------------------------------------------------------------------ общее сложение (небольшие значения)

    /// <summary>
    /// Точная сумма без округления — для случаев с уже небольшими операндами (например,
    /// «магнитуда плюс половина младшего разряда» в <see cref="BigFloatMath.Round"/>). Горячий
    /// путь формулы орбиты использует не это, а
    /// <see cref="AddRounded"/>/<see cref="AddAlignedRounded"/> — см. описание типа.
    /// </summary>
    public static BigMantissa operator +(BigMantissa left, BigMantissa right)
    {
        if (left.IsZero) return right;
        if (right.IsZero) return left;

        Span<ulong> buffer = stackalloc ulong[Math.Max(left._length, right._length) + 1];
        if (left._sign == right._sign)
        {
            int length = AddMagnitude(left.AsSpan(), right.AsSpan(), buffer);
            return new BigMantissa(buffer[..length], left._sign);
        }

        int cmp = CompareMagnitude(left.AsSpan(), right.AsSpan());
        if (cmp == 0) return Zero;
        if (cmp > 0)
        {
            int length = SubtractMagnitude(left.AsSpan(), right.AsSpan(), buffer);
            return new BigMantissa(buffer[..length], left._sign);
        }
        else
        {
            int length = SubtractMagnitude(right.AsSpan(), left.AsSpan(), buffer);
            return new BigMantissa(buffer[..length], right._sign);
        }
    }

    public static BigMantissa operator -(BigMantissa left, BigMantissa right) => left + (-right);

    // ------------------------------------------------------------------ точные сдвиги (небольшие значения)

    public static BigMantissa operator <<(BigMantissa value, int shift)
    {
        if (value.IsZero || shift == 0) return value;
        if (shift < 0) throw new ArgumentOutOfRangeException(nameof(shift));
        Span<ulong> buffer = stackalloc ulong[value._length + shift / 64 + 2];
        int length = ShiftLeftInto(value.AsSpan(), shift, buffer);
        return new BigMantissa(buffer[..length], value._sign);
    }

    public static BigMantissa operator >>(BigMantissa value, int shift)
    {
        if (value.IsZero || shift == 0) return value;
        if (shift < 0) throw new ArgumentOutOfRangeException(nameof(shift));
        Span<ulong> buffer = stackalloc ulong[value._length];
        value.AsSpan().CopyTo(buffer);
        int length = ShiftRightInPlace(buffer, value._length, shift);
        return length == 0 ? Zero : new BigMantissa(buffer[..length], value._sign);
    }

    public static BigMantissa AddOne(BigMantissa value)
    {
        if (value.IsZero) return One;
        Span<ulong> buffer = stackalloc ulong[value._length + 1];
        value.AsSpan().CopyTo(buffer);
        int length = AddOneInPlace(buffer, value._length);
        return new BigMantissa(buffer[..length], value._sign);
    }

    // ------------------------------------------------------------------ округлённые (горячие) операции

    /// <summary>
    /// a + b при равных экспонентах, сразу округлённая до <paramref name="precisionBits"/>.
    /// «Сырая» сумма (может быть на 1 лимб шире операндов) считается в локальном
    /// стек-буфере и никогда не материализуется как отдельное значение <see cref="BigMantissa"/> —
    /// см. описание типа.
    /// </summary>
    internal static BigMantissa AddRounded(BigMantissa a, BigMantissa b, int precisionBits, ref int exponent)
    {
        if (a.IsZero) return RoundAndCanonicalizeCopy(b.AsSpan(), b._sign, precisionBits, ref exponent);
        if (b.IsZero) return RoundAndCanonicalizeCopy(a.AsSpan(), a._sign, precisionBits, ref exponent);

        Span<ulong> buffer = stackalloc ulong[Math.Max(a._length, b._length) + 2];
        if (a._sign == b._sign)
        {
            int length = AddMagnitude(a.AsSpan(), b.AsSpan(), buffer);
            return RoundAndCanonicalize(buffer, length, a._sign, precisionBits, ref exponent);
        }

        int cmp = CompareMagnitude(a.AsSpan(), b.AsSpan());
        if (cmp == 0) { exponent = 0; return Zero; }
        if (cmp > 0)
        {
            int length = SubtractMagnitude(a.AsSpan(), b.AsSpan(), buffer);
            return RoundAndCanonicalize(buffer, length, a._sign, precisionBits, ref exponent);
        }
        else
        {
            int length = SubtractMagnitude(b.AsSpan(), a.AsSpan(), buffer);
            return RoundAndCanonicalize(buffer, length, b._sign, precisionBits, ref exponent);
        }
    }

    /// <summary>
    /// Верхняя граница разницы экспонент двух слагаемых, при превышении которой меньшее по
    /// модулю слагаемое отбрасывается без выравнивания сдвигом (см. <see cref="AddAlignedRounded"/>).
    /// Обоснование точности — у <see cref="BigFloat"/>, рядом с местом, откуда вызывается этот
    /// путь.
    /// </summary>
    internal const int AlignShiftShortCircuitBits = 12000;

    /// <summary>
    /// (<paramref name="big"/> сдвинутый влево на <paramref name="shiftBits"/>) + <paramref name="small"/>,
    /// сразу округлённая до <paramref name="precisionBits"/> — то есть общий случай сложения
    /// <see cref="BigFloat"/> с разными экспонентами. И сдвинутое значение, и сумма — временные
    /// буферы на стеке текущего вызова; вызывающая сторона обязана убедиться, что
    /// <paramref name="shiftBits"/> не превышает <see cref="AlignShiftShortCircuitBits"/> (иначе
    /// вклад <paramref name="small"/> математически неотличим от нуля — см. обоснование у
    /// вызывающей стороны, — и звать этот метод незачем).
    /// </summary>
    internal static BigMantissa AddAlignedRounded(
        BigMantissa big, int shiftBits, BigMantissa small, int precisionBits, ref int exponent)
    {
        // Размер буфера — по фактической длине сдвигаемого операнда и фактической величине
        // сдвига (обе обычно на порядки меньше AlignShiftShortCircuitBits), а не по
        // теоретическому максимуму: резервирование и обнуление stackalloc-буфера стоит
        // пропорционально его размеру, и при типичном (небольшом) сдвиге буфер под
        // теоретический максимум был бы чистой тратой на каждый вызов.
        int alignBufferLimbs = big._length + shiftBits / 64 + 3;
        Span<ulong> shifted = stackalloc ulong[alignBufferLimbs];
        int shiftedLength = ShiftLeftInto(big.AsSpan(), shiftBits, shifted);
        // +2, не +1: RoundAndCanonicalize может дописать один лимб переноса при округлении
        // вверх «все единицы» (см. её описание), и это происходит ДО того, как усечение по
        // сдвигу успевает освободить место — на входе в неё буфер должен вмещать
        // length+1, где length — переданная длина СУММЫ, то есть Max(...)+1.
        Span<ulong> result = stackalloc ulong[Math.Max(shiftedLength, small._length) + 2];

        if (big._sign == small._sign)
        {
            int length = AddMagnitude(shifted[..shiftedLength], small.AsSpan(), result);
            return RoundAndCanonicalize(result, length, big._sign, precisionBits, ref exponent);
        }

        int cmp = CompareMagnitude(shifted[..shiftedLength], small.AsSpan());
        if (cmp == 0) { exponent = 0; return Zero; }
        if (cmp > 0)
        {
            int length = SubtractMagnitude(shifted[..shiftedLength], small.AsSpan(), result);
            return RoundAndCanonicalize(result, length, big._sign, precisionBits, ref exponent);
        }
        else
        {
            int length = SubtractMagnitude(small.AsSpan(), shifted[..shiftedLength], result);
            return RoundAndCanonicalize(result, length, small._sign, precisionBits, ref exponent);
        }
    }

    /// <summary>a·b, сразу округлённая до <paramref name="precisionBits"/>. «Сырое» произведение
    /// (до удвоенной разрядности операндов) считается в локальном стек-буфере — см. описание типа.</summary>
    internal static BigMantissa MultiplyRounded(BigMantissa a, BigMantissa b, int precisionBits, ref int exponent)
    {
        if (a.IsZero || b.IsZero) { exponent = 0; return Zero; }
        // +1 сверх точного размера произведения: см. обоснование запаса у RoundAndCanonicalize
        // (перенос при округлении вверх «все единицы» может дописать один лимб).
        Span<ulong> buffer = stackalloc ulong[a._length + b._length + 1];
        int length = MultiplyMagnitude(a.AsSpan(), b.AsSpan(), buffer);
        return RoundAndCanonicalize(buffer, length, a._sign * b._sign, precisionBits, ref exponent);
    }

    /// <summary>a·right, сразу округлённая до <paramref name="precisionBits"/>.</summary>
    internal static BigMantissa MultiplyLongRounded(BigMantissa a, long right, int precisionBits, ref int exponent)
    {
        if (a.IsZero || right == 0) { exponent = 0; return Zero; }
        ulong magnitude = MagnitudeOf(right);
        int sign = a._sign * (right < 0 ? -1 : 1);
        Span<ulong> single = stackalloc ulong[1];
        single[0] = magnitude;
        Span<ulong> buffer = stackalloc ulong[a._length + 2];
        int length = MultiplyMagnitude(a.AsSpan(), single, buffer);
        return RoundAndCanonicalize(buffer, length, sign, precisionBits, ref exponent);
    }

    // ------------------------------------------------------------------ деление на малое (без потери точности числителя)

    /// <summary>
    /// (|<paramref name="numerator"/>| сдвинутый влево на <paramref name="shift"/>) ÷
    /// <paramref name="divisor"/> нацело плюс признак округления по остатку — тот же приём,
    /// что <see cref="BigFloat"/> использует для общего деления (см. <c>FromRatio</c>), но
    /// однолимбовым делителем, поэтому без общего деления «длинное на длинное» и без обращения
    /// к <see cref="System.Numerics.BigInteger"/>. Возвращает модуль без знака — знак применяет
    /// вызывающий.
    /// </summary>
    public static BigMantissa DivideScaledBySmall(BigMantissa numerator, ulong divisor, int shift, out bool roundUp)
    {
        int bufferLimbs = numerator._length + shift / 64 + 2;
        Span<ulong> shiftedBuf = stackalloc ulong[bufferLimbs];
        int shiftedLength;
        if (shift > 0)
        {
            shiftedLength = ShiftLeftInto(numerator.AsSpan(), shift, shiftedBuf);
        }
        else
        {
            numerator.AsSpan().CopyTo(shiftedBuf);
            shiftedLength = numerator._length;
        }

        Span<ulong> quotientBuf = stackalloc ulong[shiftedLength];
        ulong remainder = 0;
        for (int i = shiftedLength - 1; i >= 0; i--)
        {
            UInt128 current = ((UInt128)remainder << 64) | shiftedBuf[i];
            quotientBuf[i] = (ulong)(current / divisor);
            remainder = (ulong)(current % divisor);
        }

        roundUp = (UInt128)remainder * 2 >= divisor;
        int length = shiftedLength;
        while (length > 0 && quotientBuf[length - 1] == 0) length--;
        return length == 0 ? Zero : new BigMantissa(quotientBuf[..length], 1);
    }

    // ------------------------------------------------------------------ округление к рабочей точности

    /// <summary>
    /// Копирует уже небольшое (уже хранимое как <see cref="BigMantissa"/>) значение во
    /// временный буфер и округляет его до <paramref name="precisionBits"/> — общий путь конструктора
    /// <see cref="BigFloat"/> для значений, которые не являются «сырым» результатом сложения
    /// или умножения (см. описание типа).
    /// </summary>
    internal static BigMantissa RoundAndCanonicalizeCopy(
        ReadOnlySpan<ulong> value, int sign, int precisionBits, ref int exponent)
    {
        Span<ulong> buffer = stackalloc ulong[value.Length + 1];
        value.CopyTo(buffer);
        return RoundAndCanonicalize(buffer, value.Length, sign, precisionBits, ref exponent);
    }

    /// <summary>
    /// Округляет «сырую» (ещё не приведённую к рабочей точности) величину, лежащую в
    /// <paramref name="rawMagnitude"/>[0..<paramref name="length"/>), до
    /// <paramref name="precisionBits"/> значащих бит по правилу «прибавить половину младшего
    /// разряда и отбросить хвост» и убирает младшие нулевые биты — тот же алгоритм, что раньше
    /// выполнял конструктор <see cref="BigFloat"/> над <see cref="System.Numerics.BigInteger"/>,
    /// перенесённый на лимбы. Работает НА МЕСТЕ в буфере вызывающего (который должен быть шире
    /// <paramref name="length"/> хотя бы на один лимб — для переноса при округлении вверх
    /// «все единицы» → степень двойки), поэтому сама «сырая» величина никогда не копируется в
    /// значение <see cref="BigMantissa"/> целиком — только уже уменьшенный результат.
    /// <paramref name="exponent"/> получает суммарный сдвиг.
    /// </summary>
    private static BigMantissa RoundAndCanonicalize(
        Span<ulong> rawMagnitude, int length, int sign, int precisionBits, ref int exponent)
    {
        if (sign == 0 || length <= 0) { exponent = 0; return Zero; }

        int bitLen = BitLength(rawMagnitude[..length]);
        if (bitLen == 0) { exponent = 0; return Zero; }

        if (bitLen > precisionBits)
        {
            int shift = bitLen - precisionBits;
            bool roundUp = GetBit(rawMagnitude[..length], shift - 1);
            length = ShiftRightInPlace(rawMagnitude, length, shift);
            exponent += shift;
            if (roundUp) length = AddOneInPlace(rawMagnitude, length);
        }

        if (length == 0) { exponent = 0; return Zero; }

        int trailing = TrailingZeroCount(rawMagnitude[..length]);
        if (trailing > 0)
        {
            length = ShiftRightInPlace(rawMagnitude, length, trailing);
            exponent += trailing;
        }

        return new BigMantissa(rawMagnitude[..length], sign);
    }

    // ------------------------------------------------------------------ равенство

    public bool Equals(BigMantissa other) =>
        _sign == other._sign && _length == other._length && AsSpan().SequenceEqual(other.AsSpan());

    public override bool Equals(object? obj) => obj is BigMantissa other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(_sign);
        foreach (ulong limb in AsSpan()) hash.Add(limb);
        return hash.ToHashCode();
    }

    // ------------------------------------------------------------------ лимбовые примитивы

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int CompareMagnitude(ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b)
    {
        if (a.Length != b.Length) return a.Length < b.Length ? -1 : 1;
        for (int i = a.Length - 1; i >= 0; i--)
        {
            if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
        }
        return 0;
    }

    // Перенос считается классической схемой на ulong (два сравнения вместо одного
    // UInt128-сложения со сдвигом на 64) — тот же результат, но без 128-битной арифметики в
    // самом частом (по числу вызовов на шаг орбиты) примитиве.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int AddMagnitude(ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b, Span<ulong> result)
    {
        int n = Math.Max(a.Length, b.Length);
        ulong carry = 0;
        for (int i = 0; i < n; i++)
        {
            ulong av = i < a.Length ? a[i] : 0;
            ulong bv = i < b.Length ? b[i] : 0;
            ulong sum = av + bv;
            ulong carryOut = sum < av ? 1UL : 0UL;
            ulong total = sum + carry;
            carryOut += total < sum ? 1UL : 0UL;
            result[i] = total;
            carry = carryOut;
        }
        if (carry != 0) { result[n] = carry; n++; }
        while (n > 0 && result[n - 1] == 0) n--;
        return n;
    }

    /// <summary>Требует |a| ≥ |b| (по значащим лимбам обоих операндов).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int SubtractMagnitude(ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b, Span<ulong> result)
    {
        ulong borrow = 0;
        for (int i = 0; i < a.Length; i++)
        {
            ulong av = a[i];
            ulong bv = i < b.Length ? b[i] : 0;
            ulong diff = av - bv;
            ulong borrowOut = av < bv ? 1UL : 0UL;
            ulong total = diff - borrow;
            borrowOut += diff < borrow ? 1UL : 0UL;
            result[i] = total;
            borrow = borrowOut;
        }
        int length = a.Length;
        while (length > 0 && result[length - 1] == 0) length--;
        return length;
    }

    private static int MultiplyMagnitude(ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b, Span<ulong> result)
    {
        result[..(a.Length + b.Length)].Clear();
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == 0) continue;
            UInt128 carry = 0;
            ulong ai = a[i];
            for (int j = 0; j < b.Length; j++)
            {
                UInt128 product = (UInt128)ai * b[j] + result[i + j] + carry;
                result[i + j] = (ulong)product;
                carry = product >> 64;
            }
            int k = i + b.Length;
            while (carry != 0)
            {
                UInt128 sum = (UInt128)result[k] + carry;
                result[k] = (ulong)sum;
                carry = sum >> 64;
                k++;
            }
        }
        int length = a.Length + b.Length;
        while (length > 0 && result[length - 1] == 0) length--;
        return length;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int ShiftLeftInto(ReadOnlySpan<ulong> a, int bits, Span<ulong> result)
    {
        int limbShift = bits / 64;
        int bitShift = bits % 64;
        int n = a.Length;
        result[..(n + limbShift + 1)].Clear();

        if (bitShift == 0)
        {
            a.CopyTo(result[limbShift..]);
            int length = n + limbShift;
            while (length > 0 && result[length - 1] == 0) length--;
            return length;
        }

        ulong carry = 0;
        for (int i = 0; i < n; i++)
        {
            ulong v = a[i];
            result[limbShift + i] = (v << bitShift) | carry;
            carry = v >> (64 - bitShift);
        }
        int total = n + limbShift;
        if (carry != 0) { result[total] = carry; total++; }
        while (total > 0 && result[total - 1] == 0) total--;
        return total;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int ShiftRightInPlace(Span<ulong> a, int length, int bits)
    {
        if (bits == 0) return length;
        int limbShift = bits / 64;
        int bitShift = bits % 64;
        if (limbShift >= length) return 0;

        int newLength = length - limbShift;
        if (bitShift == 0)
        {
            for (int i = 0; i < newLength; i++) a[i] = a[limbShift + i];
        }
        else
        {
            for (int i = 0; i < newLength; i++)
            {
                ulong lo = a[limbShift + i];
                ulong hi = limbShift + i + 1 < length ? a[limbShift + i + 1] : 0;
                a[i] = (lo >> bitShift) | (hi << (64 - bitShift));
            }
        }
        while (newLength > 0 && a[newLength - 1] == 0) newLength--;
        return newLength;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int AddOneInPlace(Span<ulong> a, int length)
    {
        for (int i = 0; i < length; i++)
        {
            if (++a[i] != 0) return length;
        }
        a[length] = 1;
        return length + 1;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int BitLength(ReadOnlySpan<ulong> a)
    {
        int top = a.Length - 1;
        while (top >= 0 && a[top] == 0) top--;
        if (top < 0) return 0;
        return top * 64 + (64 - BitOperations.LeadingZeroCount(a[top]));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int TrailingZeroCount(ReadOnlySpan<ulong> a)
    {
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != 0) return i * 64 + BitOperations.TrailingZeroCount(a[i]);
        }
        return 0;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool GetBit(ReadOnlySpan<ulong> a, int bitIndex)
    {
        if (bitIndex < 0) return false;
        int limb = bitIndex / 64;
        if (limb >= a.Length) return false;
        int bit = bitIndex % 64;
        return ((a[limb] >> bit) & 1UL) != 0;
    }
}
