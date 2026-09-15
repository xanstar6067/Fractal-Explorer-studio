using System.Numerics;

namespace FractalExplorerWPF.Core.NewtonMath;

/// <summary>
/// Значение и первые две производные по z — «струя» второго порядка. Нужна линейному
/// пропуску глубокого зума бассейнов Ньютона: по ней считаются N′ и N″ шага итерации.
/// </summary>
internal readonly struct ComplexJet(Complex value, Complex first, Complex second)
{
    public readonly Complex Value = value;
    public readonly Complex First = first;
    public readonly Complex Second = second;

    public bool IsFinite =>
        double.IsFinite(Value.Real) && double.IsFinite(Value.Imaginary) &&
        double.IsFinite(First.Real) && double.IsFinite(First.Imaginary) &&
        double.IsFinite(Second.Real) && double.IsFinite(Second.Imaginary);

    public static ComplexJet operator +(ComplexJet left, ComplexJet right) =>
        new(left.Value + right.Value, left.First + right.First, left.Second + right.Second);

    public static ComplexJet operator -(ComplexJet left, ComplexJet right) =>
        new(left.Value - right.Value, left.First - right.First, left.Second - right.Second);

    public static ComplexJet operator -(ComplexJet value) => new(-value.Value, -value.First, -value.Second);

    public static ComplexJet operator *(ComplexJet left, ComplexJet right) => new(
        left.Value * right.Value,
        left.First * right.Value + left.Value * right.First,
        left.Second * right.Value + 2 * left.First * right.First + left.Value * right.Second);

    public static ComplexJet operator *(Complex factor, ComplexJet value) =>
        new(factor * value.Value, factor * value.First, factor * value.Second);

    public static ComplexJet operator /(ComplexJet left, ComplexJet right)
    {
        Complex value = left.Value / right.Value;
        Complex first = (left.First - value * right.First) / right.Value;
        Complex second = (left.Second - 2 * first * right.First - value * right.Second) / right.Value;
        return new ComplexJet(value, first, second);
    }
}

/// <summary>
/// Три дополнительных способа исполнить тот же байткод — для глубокого зума бассейнов Ньютона.
///
/// <para><b>Слоты.</b> Каждой инструкции отвечает слот со значением её результата в опорной
/// точке; тригонометрическим и гиперболическим — ещё и вспомогательный слот (косинус к синусу и
/// т. п.). Опорная орбита заполняет слоты из <see cref="EvaluateBig"/>, то есть это правильно
/// округлённые значения произвольной точности, а не пересчёт в double из округлённого z: у
/// знаменателя, близкого к нулю, только так сохраняется относительная точность.</para>
///
/// <para><b>Пертурбация.</b> <see cref="EvaluatePerturbed"/> по слотам и приращению δ аргумента
/// возвращает приращение результата <c>F(Z+δ) − F(Z)</c>. Для каждой операции приращение
/// выписано тождеством без вычитания близких величин: произведение раскрыто, частное сведено к
/// одному делению, у sin/cos/sh/ch разность косинусов записана через квадрат синуса половины
/// угла, у exp/log/степени — expm1/log1p с поправкой ветви.</para>
///
/// <para><b>Струи.</b> <see cref="EvaluateJet"/> даёт значение и две производные по z в опорной
/// точке — из них линейный пропуск строит матрицу переноса и оценку квадратичного члена.</para>
/// </summary>
internal sealed partial class CompiledComplexExpression
{
    private const double TwoPi = 2 * Math.PI;

    /// <summary>Порог Тейлоровского приращения обратных тригонометрических функций: |δ| ≤ 2⁻¹³·расстояние до особенности.</summary>
    private static readonly double InverseTrigTaylorRatio = Math.ScaleB(1.0, -13);

    private readonly int[] _left;
    private readonly int[] _right;
    private readonly int[] _auxiliary;
    private readonly int _slotCount;

    /// <summary>Число слотов на одну опорную точку: по одному на инструкцию плюс вспомогательные.</summary>
    public int SlotCount => _slotCount;

    /// <summary>Слот результата всего выражения — результат последней инструкции.</summary>
    public int ResultSlot => _instructions.Length - 1;

    private void InitializePerturbationLayout(out int[] left, out int[] right, out int[] auxiliary, out int slotCount)
    {
        int count = _instructions.Length;
        left = new int[count];
        right = new int[count];
        auxiliary = new int[count];
        var stack = new int[Math.Max(1, count)];
        int top = 0;
        int nextAuxiliary = count;
        for (int index = 0; index < count; index++)
        {
            left[index] = -1;
            right[index] = -1;
            auxiliary[index] = -1;
            switch (_instructions[index].OpCode)
            {
                case OpCode.PushConstant:
                case OpCode.PushZ:
                    break;
                case OpCode.Add:
                case OpCode.Subtract:
                case OpCode.Multiply:
                case OpCode.Divide:
                case OpCode.Power:
                    right[index] = stack[--top];
                    left[index] = stack[--top];
                    break;
                default:
                    left[index] = stack[--top];
                    break;
            }

            if (_instructions[index].OpCode is OpCode.Sin or OpCode.Cos or OpCode.Tan or
                OpCode.Sinh or OpCode.Cosh or OpCode.Tanh)
                auxiliary[index] = nextAuxiliary++;
            stack[top++] = index;
        }
        slotCount = nextAuxiliary;
    }

    // ------------------------------------------------------------------ arbitrary precision

    /// <summary>
    /// Значение выражения в произвольной точности (рабочей точности потока). <paramref name="values"/>
    /// — рабочий буфер не короче числа инструкций; <paramref name="slots"/> — если не пуст,
    /// получает double-копии слотов. Деление на ноль и логарифм нуля бросают исключения
    /// <see cref="BigFloat"/>: для опорной орбиты это конец данных.
    /// </summary>
    public ComplexBigFloat EvaluateBig(ComplexBigFloat z, ComplexBigFloat[] values, Span<Complex> slots)
    {
        bool record = !slots.IsEmpty;
        for (int index = 0; index < _instructions.Length; index++)
        {
            ref readonly Instruction instruction = ref _instructions[index];
            ComplexBigFloat result;
            ComplexBigFloat auxiliary = default;
            switch (instruction.OpCode)
            {
                case OpCode.PushConstant:
                    result = FromComplex(instruction.Operand);
                    break;
                case OpCode.PushZ:
                    result = z;
                    break;
                case OpCode.Negate:
                    result = -values[_left[index]];
                    break;
                case OpCode.Add:
                    result = values[_left[index]] + values[_right[index]];
                    break;
                case OpCode.Subtract:
                    result = values[_left[index]] - values[_right[index]];
                    break;
                case OpCode.Multiply:
                    result = values[_left[index]] * values[_right[index]];
                    break;
                case OpCode.Divide:
                    result = values[_left[index]] / values[_right[index]];
                    break;
                case OpCode.Power:
                    result = PowBig(values[_left[index]], values[_right[index]]);
                    break;
                case OpCode.PowerConstant:
                    result = PowConstantBig(values[_left[index]], instruction.Operand);
                    break;
                case OpCode.Sin:
                    ComplexBigFloat.SinCos(values[_left[index]], out result, out auxiliary);
                    break;
                case OpCode.Cos:
                    ComplexBigFloat.SinCos(values[_left[index]], out auxiliary, out result);
                    break;
                case OpCode.Tan:
                {
                    ComplexBigFloat.SinCos(values[_left[index]], out ComplexBigFloat sine, out auxiliary);
                    result = sine / auxiliary;
                    break;
                }
                case OpCode.Sinh:
                    ComplexBigFloat.SinhCosh(values[_left[index]], out result, out auxiliary);
                    break;
                case OpCode.Cosh:
                    ComplexBigFloat.SinhCosh(values[_left[index]], out auxiliary, out result);
                    break;
                case OpCode.Tanh:
                {
                    ComplexBigFloat.SinhCosh(values[_left[index]], out ComplexBigFloat sinh, out auxiliary);
                    result = sinh / auxiliary;
                    break;
                }
                case OpCode.Exp:
                    result = ComplexBigFloat.Exp(values[_left[index]]);
                    break;
                case OpCode.Log:
                    result = ComplexBigFloat.Log(values[_left[index]]);
                    break;
                case OpCode.Sqrt:
                    result = ComplexBigFloat.Sqrt(values[_left[index]]);
                    break;
                case OpCode.Asin:
                    result = ComplexBigFloat.Asin(values[_left[index]]);
                    break;
                case OpCode.Acos:
                    result = ComplexBigFloat.Acos(values[_left[index]]);
                    break;
                case OpCode.Atan:
                    result = ComplexBigFloat.Atan(values[_left[index]]);
                    break;
                default:
                    throw new InvalidOperationException($"Неизвестная инструкция {instruction.OpCode}.");
            }

            values[index] = result;
            if (!record) continue;
            slots[index] = result.ToComplex();
            if (_auxiliary[index] >= 0) slots[_auxiliary[index]] = auxiliary.ToComplex();
        }
        return values[_instructions.Length - 1];
    }

    private static ComplexBigFloat FromComplex(Complex value) => ComplexBigFloat.FromDouble(value.Real, value.Imaginary);

    private static bool IsZero(ComplexBigFloat value) => value.Real.IsZero && value.Imaginary.IsZero;

    /// <summary>Та же развилка, что у double-<see cref="Pow"/>: целая степень умножениями, иначе через логарифм.</summary>
    private static ComplexBigFloat PowConstantBig(ComplexBigFloat value, Complex exponent)
    {
        if (TryIntegerExponent(exponent, out int integer))
            return integer == 0 ? ComplexBigFloat.One : ComplexBigFloat.Pow(value, integer);
        return PowBig(value, FromComplex(exponent));
    }

    /// <summary>Дословно правила <see cref="Complex.Pow(Complex, Complex)"/> на нулевых операндах.</summary>
    private static ComplexBigFloat PowBig(ComplexBigFloat value, ComplexBigFloat exponent)
    {
        if (IsZero(exponent)) return ComplexBigFloat.One;
        if (IsZero(value)) return ComplexBigFloat.Zero;
        return ComplexBigFloat.Pow(value, exponent);
    }

    private static bool TryIntegerExponent(Complex exponent, out int integer)
    {
        integer = 0;
        if (exponent.Imaginary != 0) return false;
        int rounded = (int)Math.Round(exponent.Real);
        if (Math.Abs(exponent.Real - rounded) > 1e-12 || rounded is < -64 or > 64) return false;
        integer = rounded;
        return true;
    }

    // ------------------------------------------------------------------ perturbation

    /// <summary>
    /// Приращение результата <c>F(Z+δ) − F(Z)</c> по слотам опорной точки. <paramref name="deltas"/>
    /// — рабочий буфер не короче числа инструкций. Значение самого выражения в пикселе —
    /// <c>slots[ResultSlot] + результат</c>.
    /// </summary>
    public Complex EvaluatePerturbed(ReadOnlySpan<Complex> slots, Complex delta, Span<Complex> deltas)
    {
        Instruction[] instructions = _instructions;
        for (int index = 0; index < instructions.Length; index++)
        {
            Complex result;
            switch (instructions[index].OpCode)
            {
                case OpCode.PushConstant:
                    result = Complex.Zero;
                    break;
                case OpCode.PushZ:
                    result = delta;
                    break;
                case OpCode.Negate:
                    result = -deltas[_left[index]];
                    break;
                case OpCode.Add:
                    result = deltas[_left[index]] + deltas[_right[index]];
                    break;
                case OpCode.Subtract:
                    result = deltas[_left[index]] - deltas[_right[index]];
                    break;
                case OpCode.Multiply:
                {
                    int left = _left[index], right = _right[index];
                    result = slots[left] * deltas[right] + deltas[left] * (slots[right] + deltas[right]);
                    break;
                }
                case OpCode.Divide:
                {
                    // (A+a)/(B+b) − A/B = (a − (A/B)·b)/(B+b): одно деление, без разности частных.
                    int right = _right[index];
                    result = (deltas[_left[index]] - slots[index] * deltas[right]) / (slots[right] + deltas[right]);
                    break;
                }
                case OpCode.PowerConstant:
                    result = PerturbPowerConstant(slots[_left[index]], deltas[_left[index]], slots[index],
                        instructions[index].Operand);
                    break;
                case OpCode.Power:
                {
                    int left = _left[index], right = _right[index];
                    result = deltas[left] == Complex.Zero && deltas[right] == Complex.Zero
                        ? Complex.Zero
                        : Complex.Pow(slots[left] + deltas[left], slots[right] + deltas[right]) - slots[index];
                    break;
                }
                case OpCode.Sin:
                {
                    // sin(A+a) − sin A = sin A·(cos a − 1) + cos A·sin a,  cos a − 1 = −2·sin²(a/2).
                    Complex argument = deltas[_left[index]];
                    Complex half = Complex.Sin(0.5 * argument);
                    result = slots[index] * (-2 * half * half) + slots[_auxiliary[index]] * Complex.Sin(argument);
                    break;
                }
                case OpCode.Cos:
                {
                    Complex argument = deltas[_left[index]];
                    Complex half = Complex.Sin(0.5 * argument);
                    result = slots[index] * (-2 * half * half) - slots[_auxiliary[index]] * Complex.Sin(argument);
                    break;
                }
                case OpCode.Tan:
                {
                    // tg(A+a) − tg A = sin a / (cos A · cos(A+a)),  cos(A+a) = cos A·(cos a − tg A·sin a).
                    Complex argument = deltas[_left[index]];
                    Complex cosine = slots[_auxiliary[index]];
                    Complex sine = Complex.Sin(argument);
                    result = sine / (cosine * cosine * (Complex.Cos(argument) - slots[index] * sine));
                    break;
                }
                case OpCode.Sinh:
                {
                    // sh(A+a) − sh A = sh A·(ch a − 1) + ch A·sh a,  ch a − 1 = 2·sh²(a/2).
                    Complex argument = deltas[_left[index]];
                    Complex half = Complex.Sinh(0.5 * argument);
                    result = slots[index] * (2 * half * half) + slots[_auxiliary[index]] * Complex.Sinh(argument);
                    break;
                }
                case OpCode.Cosh:
                {
                    Complex argument = deltas[_left[index]];
                    Complex half = Complex.Sinh(0.5 * argument);
                    result = slots[index] * (2 * half * half) + slots[_auxiliary[index]] * Complex.Sinh(argument);
                    break;
                }
                case OpCode.Tanh:
                {
                    // th(A+a) − th A = sh a / (ch A · ch(A+a)),  ch(A+a) = ch A·(ch a + th A·sh a).
                    Complex argument = deltas[_left[index]];
                    Complex cosh = slots[_auxiliary[index]];
                    Complex sinh = Complex.Sinh(argument);
                    result = sinh / (cosh * cosh * (Complex.Cosh(argument) + slots[index] * sinh));
                    break;
                }
                case OpCode.Exp:
                    result = slots[index] * ExpMinusOne(deltas[_left[index]]);
                    break;
                case OpCode.Log:
                    result = LogRatio(slots[_left[index]], deltas[_left[index]]);
                    break;
                case OpCode.Sqrt:
                    result = PerturbSqrt(slots[_left[index]], deltas[_left[index]], slots[index]);
                    break;
                case OpCode.Asin:
                case OpCode.Acos:
                case OpCode.Atan:
                    result = PerturbInverseTrig(instructions[index].OpCode, slots[_left[index]],
                        deltas[_left[index]], slots[index]);
                    break;
                default:
                    throw new InvalidOperationException($"Неизвестная инструкция {instructions[index].OpCode}.");
            }
            deltas[index] = result;
        }
        return deltas[instructions.Length - 1];
    }

    /// <summary>
    /// Приращение степени с постоянным показателем. Целая степень — бинарное возведение пар
    /// (значение, приращение): квадрат даёт <c>b·(2B + b)</c>, произведение — раскрытую сумму,
    /// разностей нет. Отрицательная — через <c>−Δ/(P·(P+Δ))</c>. Дробная и комплексная —
    /// <c>R·expm1(c·L)</c>, где <c>L = Log(A+a) − Log A</c> с явной поправкой ветви.
    /// </summary>
    private static Complex PerturbPowerConstant(Complex reference, Complex delta, Complex value, Complex exponent)
    {
        if (delta == Complex.Zero) return Complex.Zero;
        if (TryIntegerExponent(exponent, out int integer))
        {
            if (integer == 0) return Complex.Zero;
            int remaining = Math.Abs(integer);
            Complex baseValue = reference, baseDelta = delta;
            Complex accumulatedValue = Complex.One, accumulatedDelta = Complex.Zero;
            bool empty = true;
            while (true)
            {
                if ((remaining & 1) != 0)
                {
                    if (empty)
                    {
                        accumulatedValue = baseValue;
                        accumulatedDelta = baseDelta;
                        empty = false;
                    }
                    else
                    {
                        accumulatedDelta = accumulatedValue * baseDelta + accumulatedDelta * (baseValue + baseDelta);
                        accumulatedValue *= baseValue;
                    }
                }
                remaining >>= 1;
                if (remaining == 0) break;
                baseDelta *= 2 * baseValue + baseDelta;
                baseValue *= baseValue;
            }

            return integer > 0
                ? accumulatedDelta
                : -accumulatedDelta / (accumulatedValue * (accumulatedValue + accumulatedDelta));
        }

        if (reference == Complex.Zero) return Complex.Pow(reference + delta, exponent) - value;
        return value * ExpMinusOne(exponent * LogRatio(reference, delta));
    }

    /// <summary>
    /// <c>Log(A+a) − Log A</c> на главных ветвях: <c>log1p(a/A)</c> плюс 2πi·k, где k находится
    /// сравнением аргументов — разность главных логарифмов отличается от log1p ровно тогда,
    /// когда A и A+a лежат по разные стороны разреза.
    /// </summary>
    private static Complex LogRatio(Complex reference, Complex delta)
    {
        if (delta == Complex.Zero) return Complex.Zero;
        Complex ratio = delta / reference;
        double real = 0.5 * LogOnePlus(2 * ratio.Real + ratio.Real * ratio.Real + ratio.Imaginary * ratio.Imaginary);
        double imaginary = Math.Atan2(ratio.Imaginary, 1 + ratio.Real);
        Complex shifted = reference + delta;
        double turns = Math.Round((Math.Atan2(shifted.Imaginary, shifted.Real) -
                                   Math.Atan2(reference.Imaginary, reference.Real) - imaginary) / TwoPi);
        if (turns != 0) imaginary += TwoPi * turns;
        return new Complex(real, imaginary);
    }

    /// <summary>
    /// √(A+a) − √A = a/(√(A+a) + √A) — тождество для любых двух корней с нужными квадратами. Если
    /// корни на разных сторонах разреза (сумма меньше разности), разность берётся напрямую: она
    /// тогда порядка самих корней и сокращения нет.
    /// </summary>
    private static Complex PerturbSqrt(Complex reference, Complex delta, Complex value)
    {
        if (delta == Complex.Zero) return Complex.Zero;
        Complex shifted = Complex.Sqrt(reference + delta);
        Complex sum = shifted + value;
        Complex difference = shifted - value;
        return MagnitudeSquared(sum) >= MagnitudeSquared(difference) ? delta / sum : difference;
    }

    /// <summary>
    /// Приращение arcsin/arccos/arctg. У малого приращения — ряд Тейлора третьего порядка (его
    /// остаток ~(|a|/расстояние до особенности)⁴ ≤ 2⁻⁵²); у большого — прямая разность, которой
    /// при таком |a| значащих цифр уже хватает. Разрезы у прямой разности свои, как у плоской ступени.
    /// </summary>
    private static Complex PerturbInverseTrig(OpCode opCode, Complex reference, Complex delta, Complex value)
    {
        if (delta == Complex.Zero) return Complex.Zero;
        double singularityDistance = opCode == OpCode.Atan
            ? Math.Min((reference - Complex.ImaginaryOne).Magnitude, (reference + Complex.ImaginaryOne).Magnitude)
            : Math.Min((reference - Complex.One).Magnitude, (reference + Complex.One).Magnitude);

        if (delta.Magnitude <= InverseTrigTaylorRatio * singularityDistance)
        {
            Complex first, second, third;
            if (opCode == OpCode.Atan)
            {
                Complex denominator = 1 + reference * reference;
                first = 1 / denominator;
                second = -2 * reference * first * first;
                third = (6 * reference * reference - 2) * first * first * first;
            }
            else
            {
                Complex remainder = 1 - reference * reference;
                Complex root = Complex.Sqrt(remainder);
                first = 1 / root;
                second = reference / (remainder * root);
                third = (1 + 2 * reference * reference) / (remainder * remainder * root);
                if (opCode == OpCode.Acos)
                {
                    first = -first;
                    second = -second;
                    third = -third;
                }
            }
            return delta * (first + delta * (0.5 * second + delta * (third / 6)));
        }

        Complex shifted = reference + delta;
        Complex direct = opCode switch
        {
            OpCode.Asin => Complex.Asin(shifted),
            OpCode.Acos => Complex.Acos(shifted),
            _ => Complex.Atan(shifted)
        };
        return direct - value;
    }

    /// <summary>ln(1+x) без потери малого x — компенсация Кэхэна, как у ступени Nova.</summary>
    private static double LogOnePlus(double value)
    {
        double shifted = 1 + value;
        if (shifted == 1) return value;
        return Math.Log(shifted) * value / (shifted - 1);
    }

    /// <summary>e^x − 1 без потери малого x.</summary>
    private static double ExpMinusOneReal(double value)
    {
        double exponential = Math.Exp(value);
        if (exponential == 1) return value;
        double shifted = exponential - 1;
        if (shifted == -1) return -1;
        return shifted * value / Math.Log(exponential);
    }

    /// <summary>Комплексный e^w − 1: вещественная часть — expm1(a)·cos b − 2·sin²(b/2).</summary>
    private static Complex ExpMinusOne(Complex value)
    {
        if (value == Complex.Zero) return Complex.Zero;
        double growth = ExpMinusOneReal(value.Real);
        double halfSine = Math.Sin(0.5 * value.Imaginary);
        return new Complex(
            growth * Math.Cos(value.Imaginary) - 2 * halfSine * halfSine,
            (growth + 1) * Math.Sin(value.Imaginary));
    }

    private static double MagnitudeSquared(Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;

    // ------------------------------------------------------------------ jets

    /// <summary>
    /// Значение и первые две производные выражения по z в опорной точке. Значения берутся из
    /// слотов, производные распространяются по инструкциям. <paramref name="jets"/> — рабочий
    /// буфер не короче числа инструкций.
    /// </summary>
    public ComplexJet EvaluateJet(ReadOnlySpan<Complex> slots, Span<ComplexJet> jets)
    {
        Instruction[] instructions = _instructions;
        for (int index = 0; index < instructions.Length; index++)
        {
            Complex value = slots[index];
            Complex first, second;
            int left = _left[index], right = _right[index];
            switch (instructions[index].OpCode)
            {
                case OpCode.PushConstant:
                    first = Complex.Zero;
                    second = Complex.Zero;
                    break;
                case OpCode.PushZ:
                    first = Complex.One;
                    second = Complex.Zero;
                    break;
                case OpCode.Negate:
                    first = -jets[left].First;
                    second = -jets[left].Second;
                    break;
                case OpCode.Add:
                    first = jets[left].First + jets[right].First;
                    second = jets[left].Second + jets[right].Second;
                    break;
                case OpCode.Subtract:
                    first = jets[left].First - jets[right].First;
                    second = jets[left].Second - jets[right].Second;
                    break;
                case OpCode.Multiply:
                {
                    ComplexJet product = jets[left] * jets[right];
                    first = product.First;
                    second = product.Second;
                    break;
                }
                case OpCode.Divide:
                {
                    Complex denominator = slots[right];
                    first = (jets[left].First - value * jets[right].First) / denominator;
                    second = (jets[left].Second - 2 * first * jets[right].First - value * jets[right].Second) / denominator;
                    break;
                }
                case OpCode.PowerConstant:
                {
                    Complex exponent = instructions[index].Operand;
                    Complex argument = slots[left];
                    Complex a1 = jets[left].First, a2 = jets[left].Second;
                    Complex lower, lowest;
                    if (TryIntegerExponent(exponent, out int integer))
                    {
                        if (integer == 0)
                        {
                            first = Complex.Zero;
                            second = Complex.Zero;
                            break;
                        }
                        lower = IntegerPower(argument, integer - 1);
                        lowest = IntegerPower(argument, integer - 2);
                    }
                    else if (argument == Complex.Zero)
                    {
                        first = new Complex(double.NaN, double.NaN);
                        second = first;
                        break;
                    }
                    else
                    {
                        lower = Complex.Pow(argument, exponent - 1);
                        lowest = Complex.Pow(argument, exponent - 2);
                    }
                    first = exponent * lower * a1;
                    second = exponent * (exponent - 1) * lowest * a1 * a1 + exponent * lower * a2;
                    break;
                }
                case OpCode.Power:
                {
                    Complex baseValue = slots[left], exponentValue = slots[right];
                    Complex logarithm = Complex.Log(baseValue);
                    Complex logFirst = jets[left].First / baseValue;
                    Complex logSecond = jets[left].Second / baseValue - logFirst * logFirst;
                    Complex exponentFirst = jets[right].First * logarithm + exponentValue * logFirst;
                    Complex exponentSecond = jets[right].Second * logarithm + 2 * jets[right].First * logFirst +
                                             exponentValue * logSecond;
                    first = value * exponentFirst;
                    second = value * (exponentFirst * exponentFirst + exponentSecond);
                    break;
                }
                case OpCode.Sin:
                {
                    Complex cosine = slots[_auxiliary[index]];
                    Complex a1 = jets[left].First;
                    first = cosine * a1;
                    second = -value * a1 * a1 + cosine * jets[left].Second;
                    break;
                }
                case OpCode.Cos:
                {
                    Complex sine = slots[_auxiliary[index]];
                    Complex a1 = jets[left].First;
                    first = -sine * a1;
                    second = -value * a1 * a1 - sine * jets[left].Second;
                    break;
                }
                case OpCode.Tan:
                {
                    Complex growth = 1 + value * value;
                    Complex a1 = jets[left].First;
                    first = growth * a1;
                    second = growth * (jets[left].Second + 2 * value * a1 * a1);
                    break;
                }
                case OpCode.Sinh:
                {
                    Complex cosh = slots[_auxiliary[index]];
                    Complex a1 = jets[left].First;
                    first = cosh * a1;
                    second = value * a1 * a1 + cosh * jets[left].Second;
                    break;
                }
                case OpCode.Cosh:
                {
                    Complex sinh = slots[_auxiliary[index]];
                    Complex a1 = jets[left].First;
                    first = sinh * a1;
                    second = value * a1 * a1 + sinh * jets[left].Second;
                    break;
                }
                case OpCode.Tanh:
                {
                    Complex growth = 1 - value * value;
                    Complex a1 = jets[left].First;
                    first = growth * a1;
                    second = growth * (jets[left].Second - 2 * value * a1 * a1);
                    break;
                }
                case OpCode.Exp:
                {
                    Complex a1 = jets[left].First;
                    first = value * a1;
                    second = value * (jets[left].Second + a1 * a1);
                    break;
                }
                case OpCode.Log:
                {
                    Complex argument = slots[left];
                    first = jets[left].First / argument;
                    second = jets[left].Second / argument - first * first;
                    break;
                }
                case OpCode.Sqrt:
                    first = jets[left].First / (2 * value);
                    second = (jets[left].Second - 2 * first * first) / (2 * value);
                    break;
                case OpCode.Asin:
                case OpCode.Acos:
                {
                    Complex argument = slots[left];
                    Complex remainder = 1 - argument * argument;
                    Complex root = Complex.Sqrt(remainder);
                    Complex a1 = jets[left].First;
                    first = a1 / root;
                    second = (jets[left].Second + argument * a1 * a1 / remainder) / root;
                    if (instructions[index].OpCode == OpCode.Acos)
                    {
                        first = -first;
                        second = -second;
                    }
                    break;
                }
                case OpCode.Atan:
                {
                    Complex argument = slots[left];
                    Complex denominator = 1 + argument * argument;
                    Complex a1 = jets[left].First;
                    first = a1 / denominator;
                    second = (jets[left].Second - 2 * argument * a1 * first) / denominator;
                    break;
                }
                default:
                    throw new InvalidOperationException($"Неизвестная инструкция {instructions[index].OpCode}.");
            }
            jets[index] = new ComplexJet(value, first, second);
        }
        return jets[instructions.Length - 1];
    }

    private static Complex IntegerPower(Complex value, int exponent)
    {
        if (exponent == 0) return Complex.One;
        Complex result = Complex.One;
        Complex factor = value;
        for (int remaining = Math.Abs(exponent); remaining > 0; remaining >>= 1)
        {
            if ((remaining & 1) != 0) result *= factor;
            if (remaining > 1) factor *= factor;
        }
        return exponent > 0 ? result : Complex.One / result;
    }
}
