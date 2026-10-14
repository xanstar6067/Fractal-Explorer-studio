using System.Runtime.CompilerServices;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Пертурбационные ядра семейства Мандельброта написаны один раз и обобщены по паре
/// «δ и производная для Distance Estimation»: <see cref="DoubleDelta"/> с <see cref="Jacobian2"/>
/// до порога FloatExpDeltaZoomBits (основной и самый быстрый путь) и <see cref="FloatExp"/> с
/// <see cref="Jacobian2Exp"/> за ним. Арифметику δ даёт <see cref="IDeltaNumber{TSelf}"/>, а
/// всё, где два пути считают по-разному, — тип производной через
/// <see cref="IDeepDerivative{TSelf,TDelta}"/>: он однозначно соответствует представлению δ.
///
/// Каждый путь выполняет ровно те же операции в том же порядке, что и прежние написанные
/// вручную ядра, поэтому кадры совпадают с ними побитно.
/// </summary>
public static partial class MandelbrotFamilyRenderer
{
    private interface IDeepDerivative<TSelf, TDelta>
        where TSelf : struct, IDeepDerivative<TSelf, TDelta>
        where TDelta : struct, IDeltaNumber<TDelta>
    {
        static abstract TSelf Identity { get; }
        static abstract TSelf Zero { get; }

        /// <summary><c>D ← J(z)·D + ∂f/∂c</c> (см. <see cref="AdvanceDerivative"/>).</summary>
        static abstract TSelf Advance(MandelbrotState state, TSelf derivative, Jacobian2 parameterDerivative,
            double currentReal, double currentImaginary);

        /// <summary><c>D ← step·D + ∂f/∂c</c> с готовым якобианом шага (кубические свёртки).</summary>
        static abstract TSelf Chain(Jacobian2 step, TSelf derivative, Jacobian2 parameterDerivative);

        /// <summary>Хвост ядра: сглаживание и расстояние (см. <see cref="FinishDeepZoomPixel"/>).</summary>
        static abstract PixelMetrics Finish(int iteration, double magnitudeSquared, double minTrap, double stripe,
            bool estimateDistance, double escapeReal, double escapeImaginary, TSelf derivative,
            TDelta distanceScale, double smoothingPower);

        /// <summary>
        /// Пропуск комплексной BLA: δ ← A·δ + B·δc. Свой у каждого пути, потому что два прежних
        /// ядра складывали мнимую часть в разном порядке, а порядок сложения меняет последний бит.
        /// </summary>
        static abstract void ComplexBlaStep(ref TDelta real, ref TDelta imaginary, TDelta addReal, TDelta addImaginary,
            double ax, double ay, double bx, double by);

        /// <summary>
        /// Шаг кубической свёртки. Расширенный путь берёт опорную точку из точных
        /// <see cref="ReferenceOrbit.CubicRe"/>/<see cref="ReferenceOrbit.CubicIm"/>: Im(Z³) там
        /// гасится сокращением ниже точности double.
        /// </summary>
        static abstract (TDelta Real, TDelta Imaginary, Jacobian2 Jacobian) FoldedCubicStep(ReflectKind kind,
            ReferenceOrbit orbit, int index, TDelta deltaReal, TDelta deltaImaginary, bool estimateDistance);

        /// <summary>
        /// Ребазирование кубической свёртки к Z₀. Расширенный путь ведёт сумму по точной опорной
        /// точке, double — по уже собранному <c>z</c>, как и все остальные варианты.
        /// </summary>
        static abstract (TDelta Real, TDelta Imaginary) RebaseCubic(ReferenceOrbit orbit, int index,
            TDelta deltaReal, TDelta deltaImaginary, double fullReal, double fullImaginary);
    }

    private readonly partial record struct Jacobian2 : IDeepDerivative<Jacobian2, DoubleDelta>
    {
        public static Jacobian2 Advance(MandelbrotState state, Jacobian2 derivative, Jacobian2 parameterDerivative,
            double currentReal, double currentImaginary) =>
            AdvanceDerivative(state, derivative, parameterDerivative, currentReal, currentImaginary);

        public static Jacobian2 Chain(Jacobian2 step, Jacobian2 derivative, Jacobian2 parameterDerivative) =>
            Multiply(step, derivative) + parameterDerivative;

        public static PixelMetrics Finish(int iteration, double magnitudeSquared, double minTrap, double stripe,
            bool estimateDistance, double escapeReal, double escapeImaginary, Jacobian2 derivative,
            DoubleDelta distanceScale, double smoothingPower) =>
            FinishDeepZoomPixel(iteration, magnitudeSquared, minTrap, stripe, estimateDistance,
                escapeReal, escapeImaginary, derivative, distanceScale.Value, smoothingPower);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ComplexBlaStep(ref DoubleDelta real, ref DoubleDelta imaginary,
            DoubleDelta addReal, DoubleDelta addImaginary, double ax, double ay, double bx, double by)
        {
            double skippedReal = ax * real.Value - ay * imaginary.Value
                               + bx * addReal.Value - by * addImaginary.Value;
            double skippedImaginary = ax * imaginary.Value + ay * real.Value
                                    + bx * addImaginary.Value + by * addReal.Value;
            real = new(skippedReal);
            imaginary = new(skippedImaginary);
        }

        public static (DoubleDelta Real, DoubleDelta Imaginary, Jacobian2 Jacobian) FoldedCubicStep(ReflectKind kind,
            ReferenceOrbit orbit, int index, DoubleDelta deltaReal, DoubleDelta deltaImaginary, bool estimateDistance)
        {
            var delta = FoldedCubicDelta(kind, orbit.Re[index], orbit.Im[index], orbit.CubicV![index].ToDouble(),
                deltaReal.Value, deltaImaginary.Value, estimateDistance);
            return (new(delta.Real), new(delta.Imaginary), delta.Jacobian);
        }

        public static (DoubleDelta Real, DoubleDelta Imaginary) RebaseCubic(ReferenceOrbit orbit, int index,
            DoubleDelta deltaReal, DoubleDelta deltaImaginary, double fullReal, double fullImaginary) =>
            (new(fullReal - orbit.Re[0]), new(fullImaginary - orbit.Im[0]));
    }

    private readonly partial record struct Jacobian2Exp : IDeepDerivative<Jacobian2Exp, FloatExp>
    {
        public static Jacobian2Exp Advance(MandelbrotState state, Jacobian2Exp derivative, Jacobian2 parameterDerivative,
            double currentReal, double currentImaginary) =>
            AdvanceDerivativeExp(state, derivative, parameterDerivative, currentReal, currentImaginary);

        public static Jacobian2Exp Chain(Jacobian2 step, Jacobian2Exp derivative, Jacobian2 parameterDerivative) =>
            Multiply(step, derivative) + parameterDerivative;

        public static PixelMetrics Finish(int iteration, double magnitudeSquared, double minTrap, double stripe,
            bool estimateDistance, double escapeReal, double escapeImaginary, Jacobian2Exp derivative,
            FloatExp distanceScale, double smoothingPower) =>
            FinishDeepZoomPixelExp(iteration, magnitudeSquared, minTrap, stripe, estimateDistance,
                escapeReal, escapeImaginary, derivative, distanceScale, smoothingPower);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ComplexBlaStep(ref FloatExp real, ref FloatExp imaginary,
            FloatExp addReal, FloatExp addImaginary, double ax, double ay, double bx, double by)
        {
            FloatExp skippedReal = real * ax - imaginary * ay + addReal * bx - addImaginary * by;
            FloatExp skippedImaginary = real * ay + imaginary * ax + addReal * by + addImaginary * bx;
            real = skippedReal;
            imaginary = skippedImaginary;
        }

        public static (FloatExp Real, FloatExp Imaginary, Jacobian2 Jacobian) FoldedCubicStep(ReflectKind kind,
            ReferenceOrbit orbit, int index, FloatExp deltaReal, FloatExp deltaImaginary, bool estimateDistance) =>
            FoldedCubicDeltaExp(kind, orbit.CubicRe![index], orbit.CubicIm![index], orbit.CubicV![index],
                deltaReal, deltaImaginary, estimateDistance);

        public static (FloatExp Real, FloatExp Imaginary) RebaseCubic(ReferenceOrbit orbit, int index,
            FloatExp deltaReal, FloatExp deltaImaginary, double fullReal, double fullImaginary) =>
            (orbit.CubicRe![index] + deltaReal - orbit.CubicRe[0],
             orbit.CubicIm![index] + deltaImaginary - orbit.CubicIm[0]);
    }
}
