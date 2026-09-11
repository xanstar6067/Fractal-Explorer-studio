using System.Globalization;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Эталонный рендер прямой итерацией в <see cref="BigFloat"/> без пертурбации, BLA и
/// FloatExp — «золотой стандарт» для оценки потерь качества основного глубокого движка.
/// Медленный (BigFloat на каждый пиксель), поэтому только для проверочного проекта.
/// Поддерживает Smooth-окраску вариантов Mandelbrot/Julia, отражённых и Multibrot целой
/// степени — тех же, что и <see cref="ShouldUseDeepZoom"/>.
/// </summary>
public static partial class MandelbrotFamilyRenderer
{
    internal static byte[] RenderExactReferenceForTests(
        MandelbrotState state, int width, int height, CancellationToken token)
    {
        var buffer = new byte[checked(width * height * 4)];

        // Заметно выше рабочей точности движка — чтобы эталон был практически точным.
        int bits = PlanDeepZoom(state).ReferenceBits + 128;

        string centerXRaw = state.CenterXExact is { Length: > 0 } exactX
            ? exactX
            : state.CenterX.ToString(CultureInfo.InvariantCulture);
        string centerYRaw = state.CenterYExact is { Length: > 0 } exactY
            ? exactY
            : state.CenterY.ToString(CultureInfo.InvariantCulture);

        bool estimateDistance = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        double escapeSquared = estimateDistance
            ? DistanceEstimationEscapeSquared(state)
            : (double)(state.Threshold * state.Threshold);
        // Сетка кадра — в расширенном диапазоне, как и у основного движка: смещение пикселя
        // на зуме за 1.8e308 в double обращается в ноль, и эталон рисовал бы один цвет.
        FloatExp viewWidth = 3.0 / state.Zoom;
        FloatExp viewHeight = viewWidth * height / width;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = System.Math.Clamp(
                state.Threads <= 0 ? Environment.ProcessorCount : state.Threads, 1, Environment.ProcessorCount),
        };

        if (estimateDistance)
        {
            RenderExactReferenceDistanceEstimation(state, buffer, width, height, bits,
                centerXRaw, centerYRaw, escapeSquared, viewWidth, viewHeight, options, token);
            return buffer;
        }

        Parallel.For(0, height, options, (y, loopState) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return; }
            using var precision = new BigFloat.PrecisionScope(bits);

            BigFloat centerX = BigFloat.Parse(centerXRaw);
            BigFloat centerY = BigFloat.Parse(centerYRaw);
            BigFloat imaginary = centerY + ((0.5 - (double)y / height) * viewHeight).ToBigFloat();
            int row = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return; }
                BigFloat real = centerX + (((double)x / width - 0.5) * viewWidth).ToBigFloat();
                PixelMetrics metrics = ExactIterate(state, real, imaginary, escapeSquared,
                    FloatExp.One, token);
                Color color = ResolveColor(state, metrics, 0);
                int offset = row + x * 4;
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = 255;
            }
        });

        return buffer;
    }

    // Эталон для Distance Estimation. Схема — как у глубокого движка
    // (<see cref="RenderDeepZoomDistanceEstimation"/>): те же нормированные на пиксель
    // расстояния и тот же второй проход, поэтому картинки сравнимы побайтово. Отличие ровно
    // одно и именно оно и измеряется: <c>z</c> для якобиана берётся из точной BigFloat-орбиты,
    // а не из суммы <c>Z + δ</c>.
    private static void RenderExactReferenceDistanceEstimation(
        MandelbrotState state,
        byte[] buffer,
        int width,
        int height,
        int bits,
        string centerXRaw,
        string centerYRaw,
        double escapeSquared,
        FloatExp viewWidth,
        FloatExp viewHeight,
        ParallelOptions options,
        CancellationToken token)
    {
        int stride = checked(width * 4);
        int sampleWidth = checked(width + 2);
        int sampleHeight = checked(height + 2);
        var distances = new float[checked(sampleWidth * sampleHeight)];
        FloatExp distanceScale = PixelDistanceScale(viewWidth, width);

        Parallel.For(0, sampleHeight, options, (sampleY, loopState) =>
        {
            if (token.IsCancellationRequested) { loopState.Stop(); return; }
            using var precision = new BigFloat.PrecisionScope(bits);

            BigFloat centerX = BigFloat.Parse(centerXRaw);
            BigFloat centerY = BigFloat.Parse(centerYRaw);
            int y = sampleY - 1;
            BigFloat imaginary = centerY + ((0.5 - (double)y / height) * viewHeight).ToBigFloat();
            int distanceRow = sampleY * sampleWidth;

            for (int sampleX = 0; sampleX < sampleWidth; sampleX++)
            {
                if ((sampleX & 63) == 0 && token.IsCancellationRequested) { loopState.Stop(); return; }
                int x = sampleX - 1;
                BigFloat real = centerX + (((double)x / width - 0.5) * viewWidth).ToBigFloat();
                PixelMetrics metrics = ExactIterate(state, real, imaginary, escapeSquared,
                    distanceScale, token);
                distances[distanceRow + sampleX] = StoreDistance(metrics.Distance);
                if (sampleX is > 0 && sampleX <= width && sampleY is > 0 && sampleY <= height)
                {
                    Color baseColor = ResolveDistanceBaseColor(state, metrics);
                    WriteColor(buffer, (sampleY - 1) * stride + (sampleX - 1) * 4, baseColor);
                }
            }
        });

        if (token.IsCancellationRequested) return;

        ShadeDistanceField(state, buffer, width, height, stride, distances, 1.0, options, token, null);
    }

    private static PixelMetrics ExactIterate(
        MandelbrotState state, BigFloat startReal, BigFloat startImaginary, double escapeSquared,
        FloatExp distanceScale, CancellationToken token)
    {
        bool isJulia = IsJuliaVariant(state.Variant);
        ReflectKind? reflect = ReflectKindOf(state.Variant);
        int multibrotPower = MultibrotPowerOrZero(state);
        int simonobrotPower = SimonobrotPowerOrZero(state);
        bool invertReal = state.Variant == MandelbrotVariant.Simonobrot && state.UseInversion;

        BigFloat constantReal = isJulia
            ? BigFloat.FromDecimal(state.JuliaCReal)
            : invertReal ? -startReal : startReal;
        BigFloat constantImaginary = isJulia ? BigFloat.FromDecimal(state.JuliaCImaginary) : startImaginary;
        BigFloat zReal = isJulia ? startReal : BigFloat.Zero;
        BigFloat zImaginary = isJulia ? startImaginary : BigFloat.Zero;
        BigFloat two = BigFloat.FromInt(2);

        int maxIterations = state.Iterations;
        int iteration = 0;
        double magnitudeSquared = 0;
        double escapeReal = 0;
        double escapeImaginary = 0;
        bool escaped = false;

        bool estimateDistance = state.ColoringMode == MandelbrotColoringMode.DistanceEstimation;
        // Производная — в расширенном диапазоне, как и у глубокого движка: иначе эталон
        // переполнялся бы раньше проверяемого кода и перестал быть эталоном.
        Jacobian2Exp derivative = isJulia ? Jacobian2Exp.Identity : Jacobian2Exp.Zero;
        Jacobian2 parameterDerivative = ParameterDerivativeOf(state, isJulia);

        while (iteration < maxIterations)
        {
            if ((iteration & 4095) == 0 && token.IsCancellationRequested) return default;

            if (estimateDistance)
                derivative = AdvanceDerivativeExp(state, derivative, parameterDerivative,
                    zReal.ToDouble(), zImaginary.ToDouble());

            if (reflect is { } kind)
            {
                (zReal, zImaginary) = StepReflectedReference(
                    kind, zReal, zImaginary, constantReal, constantImaginary, two);
            }
            else if (simonobrotPower >= 2)
            {
                (zReal, zImaginary) = StepSimonobrotReference(
                    simonobrotPower, zReal, zImaginary, constantReal, constantImaginary);
            }
            else if (multibrotPower >= 3)
            {
                BigFloat powerReal = zReal, powerImaginary = zImaginary;
                for (int e = 1; e < multibrotPower; e++)
                {
                    BigFloat nr = powerReal * zReal - powerImaginary * zImaginary;
                    powerImaginary = powerReal * zImaginary + powerImaginary * zReal;
                    powerReal = nr;
                }
                zReal = powerReal + constantReal;
                zImaginary = powerImaginary + constantImaginary;
            }
            else
            {
                BigFloat nextReal = zReal * zReal - zImaginary * zImaginary + constantReal;
                BigFloat nextImaginary = two * zReal * zImaginary + constantImaginary;
                zReal = nextReal;
                zImaginary = nextImaginary;
            }

            iteration++;

            double realDouble = zReal.ToDouble();
            double imaginaryDouble = zImaginary.ToDouble();
            magnitudeSquared = realDouble * realDouble + imaginaryDouble * imaginaryDouble;
            if (!double.IsFinite(magnitudeSquared) || magnitudeSquared > escapeSquared)
            {
                escapeReal = realDouble;
                escapeImaginary = imaginaryDouble;
                escaped = true;
                break;
            }
        }

        if (!escaped)
            return new PixelMetrics(maxIterations, maxIterations, 0, 0);

        return FinishDeepZoomPixelExp(iteration, magnitudeSquared, double.MaxValue, 0,
            estimateDistance, escapeReal, escapeImaginary, derivative, distanceScale);
    }
}
