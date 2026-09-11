using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Эталонный рендер для проверок: орбита каждого пикселя считается <b>напрямую</b> в
/// <see cref="BigFloat"/>, без пертурбации, ребазирования и опорной орбиты. Медленно (сотни
/// микросекунд на пиксель), поэтому только для маленьких кадров в проверочном проекте.
///
/// В отличие от эталона Феникса этот проверяет и саму алгебру движка. Пертурбационное ядро
/// стоит на свёрнутой форме <c>z ← (1 − m/p)·z + (m/p)·z^(1−p) + c</c>, а здесь формула
/// выписана ровно так, как её считает плоская ступень, — с двумя отдельными степенями и
/// делением:
/// <code>
///   z ← z − m·(z^p − 1)/(p·z^(p−1)) + c
/// </code>
/// Поэтому расхождение свёртки (например, если бы <c>z^p·z^(1−p) = z</c> не выполнялось из-за
/// разных ветвей логарифма) здесь бы проявилось. Общего с глубоким путём у эталона остаётся
/// только арифметика <see cref="ComplexBigFloat"/>.
/// </summary>
public static partial class NovaRenderer
{
    internal static byte[] RenderExactReferenceForTests(
        NovaState state, int width, int height, int extraBits, CancellationToken token)
    {
        int referenceBits = PlanReferenceBits(state) + extraBits;
        var pixels = new byte[checked(width * height * 4)];
        double viewWidth = DeepViewWidth(state);

        Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            y =>
            {
                using var precision = new BigFloat.PrecisionScope(referenceBits);
                // Геометрия дословно как в глубоком пути: смещение пикселя считается в double
                // и лишь потом прибавляется к точному центру — иначе сравнение проверяло бы
                // ещё и разницу раскладки, а не только орбиту.
                double deltaImaginary = (height / 2.0 - y) * viewWidth / width;
                for (int x = 0; x < width; x++)
                {
                    if (token.IsCancellationRequested) return;
                    double deltaReal = (x - width / 2.0) * viewWidth / width;
                    (int iteration, double magnitudeSquared) = IterateExact(state, deltaReal, deltaImaginary);
                    Color color = ResolveColor(state, iteration, Smooth(iteration, state, magnitudeSquared));
                    int offset = (y * width + x) * 4;
                    pixels[offset] = color.B; pixels[offset + 1] = color.G;
                    pixels[offset + 2] = color.R; pixels[offset + 3] = color.A;
                }
            });

        return pixels;
    }

    private static (int Iteration, double MagnitudeSquared) IterateExact(
        NovaState state, double deltaReal, double deltaImaginary)
    {
        BigFloat centerX = state.CenterXExact is { Length: > 0 } exactX
            ? BigFloat.Parse(exactX)
            : BigFloat.FromDecimal(state.CenterX);
        BigFloat centerY = state.CenterYExact is { Length: > 0 } exactY
            ? BigFloat.Parse(exactY)
            : BigFloat.FromDecimal(state.CenterY);

        var pixel = new ComplexBigFloat(centerX + BigFloat.FromDouble(deltaReal),
            centerY + BigFloat.FromDouble(deltaImaginary));

        bool julia = state.Variant == NovaVariant.Julia;
        ComplexBigFloat current = julia ? pixel : ComplexBigFloat.FromDecimal(state.Z0Real, state.Z0Imaginary);
        ComplexBigFloat constant = julia ? ComplexBigFloat.FromDecimal(state.CReal, state.CImaginary) : pixel;

        ComplexBigFloat power = ComplexBigFloat.FromDecimal(state.PReal, state.PImaginary);
        ComplexBigFloat powerMinusOne = power - 1;
        BigFloat relaxation = BigFloat.FromDecimal(state.M);
        bool integerPower = state.PImaginary == 0 && decimal.Truncate(state.PReal) == state.PReal &&
                            state.PReal is >= -MaximumIntegerPower and <= MaximumIntegerPower;
        int integerValue = integerPower ? (int)state.PReal : 0;

        int maximum = state.Iterations;
        double thresholdSquared = (double)(state.Threshold * state.Threshold);
        int iteration = 0;
        double currentReal = current.Real.ToDouble();
        double currentImaginary = current.Imaginary.ToDouble();
        double magnitudeSquared = currentReal * currentReal + currentImaginary * currentImaginary;

        while (iteration < maximum && magnitudeSquared <= thresholdSquared)
        {
            if (magnitudeSquared < 1e-12) break;

            ComplexBigFloat numerator = (integerPower
                ? ComplexBigFloat.Pow(current, integerValue)
                : ComplexBigFloat.Pow(current, power)) - 1;
            ComplexBigFloat denominator = power * (integerPower
                ? ComplexBigFloat.Pow(current, integerValue - 1)
                : ComplexBigFloat.Pow(current, powerMinusOne));

            double denominatorMagnitudeSquared = denominator.MagnitudeSquared.ToDouble();
            if (!double.IsFinite(denominatorMagnitudeSquared) || denominatorMagnitudeSquared < 1e-24) break;

            current = current - relaxation * (numerator / denominator) + constant;
            currentReal = current.Real.ToDouble();
            currentImaginary = current.Imaginary.ToDouble();
            if (!double.IsFinite(currentReal) || !double.IsFinite(currentImaginary)) break;

            iteration++;
            magnitudeSquared = currentReal * currentReal + currentImaginary * currentImaginary;
        }

        return (iteration, magnitudeSquared);
    }
}
