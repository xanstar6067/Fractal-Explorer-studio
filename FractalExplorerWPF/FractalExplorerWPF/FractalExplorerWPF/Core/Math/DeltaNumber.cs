using System.Runtime.CompilerServices;

namespace FractalExplorerWPF.Core.NewtonMath;

/// <summary>
/// Число, в котором пертурбационное ядро ведёт отклонение δ от опорной орбиты. Ядро пишется
/// один раз, обобщённо по такому числу: <see cref="DoubleDelta"/> — обычный double (основной и
/// самый быстрый путь), <see cref="FloatExp"/> — расширенная экспонента для зума, на котором δ
/// и особенно δ² уже не помещаются в double. Обе реализации — структуры, поэтому JIT строит под
/// каждую отдельный код ядра без вызовов через интерфейс.
/// </summary>
internal interface IDeltaNumber<TSelf> where TSelf : struct, IDeltaNumber<TSelf>
{
    static abstract TSelf Zero { get; }
    static abstract TSelf FromDouble(double value);
    double ToDouble();

    static abstract TSelf operator +(TSelf left, TSelf right);
    static abstract TSelf operator +(TSelf left, double right);
    static abstract TSelf operator -(TSelf left, TSelf right);
    static abstract TSelf operator -(TSelf value);
    static abstract TSelf operator *(TSelf left, TSelf right);
    static abstract TSelf operator *(TSelf left, double right);
    static abstract TSelf operator /(TSelf left, TSelf right);
    static abstract bool operator >(TSelf left, double right);
    static abstract bool operator <(TSelf left, double right);
    static abstract TSelf Abs(TSelf value);
    static abstract TSelf Sqrt(TSelf value);

    /// <summary>|δ|² как double: слишком малое δ обращается в 0, а не в денормал.</summary>
    static abstract double MagnitudeSquared(TSelf real, TSelf imaginary);
}

/// <summary>δ в обычном double: каждая операция — ровно та же double-операция.</summary>
internal readonly struct DoubleDelta(double value) : IDeltaNumber<DoubleDelta>
{
    public readonly double Value = value;

    public static DoubleDelta Zero => new(0.0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDelta FromDouble(double value) => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToDouble() => Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDelta operator +(DoubleDelta left, DoubleDelta right) => new(left.Value + right.Value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDelta operator +(DoubleDelta left, double right) => new(left.Value + right);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDelta operator -(DoubleDelta left, DoubleDelta right) => new(left.Value - right.Value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDelta operator -(DoubleDelta value) => new(-value.Value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDelta operator *(DoubleDelta left, DoubleDelta right) => new(left.Value * right.Value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDelta operator *(DoubleDelta left, double right) => new(left.Value * right);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDelta operator /(DoubleDelta left, DoubleDelta right) => new(left.Value / right.Value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(DoubleDelta left, double right) => left.Value > right;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(DoubleDelta left, double right) => left.Value < right;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDelta Abs(DoubleDelta value) => new(System.Math.Abs(value.Value));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDelta Sqrt(DoubleDelta value) => new(System.Math.Sqrt(value.Value));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double MagnitudeSquared(DoubleDelta real, DoubleDelta imaginary) =>
        real.Value * real.Value + imaginary.Value * imaginary.Value;
}
