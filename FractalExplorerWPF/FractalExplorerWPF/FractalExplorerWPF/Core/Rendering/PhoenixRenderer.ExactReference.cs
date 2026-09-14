using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Эталонный рендер для проверок: орбита каждого пикселя считается <b>напрямую</b> в
/// <see cref="BigFloat"/>, без пертурбации, ребазирования и опорной орбиты. Медленно (сотни
/// микросекунд на пиксель), поэтому только для маленьких кадров в проверочном проекте.
///
/// Чего этот эталон НЕ проверяет: он делит с опорной орбитой общую
/// <see cref="VariantPowerBig"/>, поэтому ошибку в переносе самой формулы в BigFloat он бы
/// повторил. Единственный по-настоящему независимый оракул здесь — плоская double-ступень
/// (<c>Iterate</c>), которая не делит с этим путём ни строки; она честна до ~1e11, и сравнение
/// с ней на мелком зуме и есть проверка транскрипции. Этот же эталон нужен там, где плоской
/// ступени уже нельзя верить: он проверяет пертурбацию поверх формулы.
/// </summary>
public static partial class PhoenixRenderer
{
    internal static byte[] RenderExactReferenceForTests(
        PhoenixState state, int width, int height, int extraBits, CancellationToken token)
    {
        int referenceBits = PlanReferenceBits(state) + extraBits;
        var pixels = new byte[checked(width * height * 4)];
        FloatExp viewWidth = DeepViewWidth(state);

        Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            y =>
            {
                using var precision = new BigFloat.PrecisionScope(referenceBits);
                // Геометрия дословно как в глубоком пути: смещение пикселя считается в FloatExp
                // и лишь потом прибавляется к точному центру — иначе сравнение проверяло бы
                // ещё и разницу раскладки, а не только орбиту.
                FloatExp deltaImaginary = (height / 2.0 - y) * viewWidth / width;
                for (int x = 0; x < width; x++)
                {
                    if (token.IsCancellationRequested) return;
                    FloatExp deltaReal = (x - width / 2.0) * viewWidth / width;
                    PixelMetrics metrics = IterateExact(state, deltaReal, deltaImaginary);
                    WritePixel(pixels, (y * width + x) * 4, ResolveColor(state, metrics));
                }
            });

        return pixels;
    }

    private static PixelMetrics IterateExact(PhoenixState state, FloatExp deltaReal, FloatExp deltaImaginary)
    {
        BigFloat centerX = state.CenterXExact is { Length: > 0 } exactX
            ? BigFloat.Parse(exactX)
            : BigFloat.FromDecimal(state.CenterX);
        BigFloat centerY = state.CenterYExact is { Length: > 0 } exactY
            ? BigFloat.Parse(exactY)
            : BigFloat.FromDecimal(state.CenterY);

        // ToBigFloat переносит мантиссу смещения целиком — и за пределами диапазона double.
        BigFloat pixelReal = centerX + deltaReal.ToBigFloat();
        BigFloat pixelImaginary = centerY + deltaImaginary.ToBigFloat();

        bool parameterPlane = state.PlaneMode == PhoenixPlaneMode.ParameterC1;
        bool automaticStart = state.SecondaryPower > 0 &&
                              state.InitialZReal == 0 && state.InitialZImaginary == 0 &&
                              state.InitialPreviousReal == 0 && state.InitialPreviousImaginary == 0;

        BigFloat currentReal, currentImaginary, c1Real, c1Imaginary;
        if (parameterPlane)
        {
            currentReal = automaticStart ? BigFloat.One : BigFloat.FromDecimal(state.InitialZReal);
            currentImaginary = automaticStart ? BigFloat.Zero : BigFloat.FromDecimal(state.InitialZImaginary);
            c1Real = pixelReal;
            c1Imaginary = pixelImaginary;
        }
        else
        {
            currentReal = pixelReal;
            currentImaginary = pixelImaginary;
            c1Real = BigFloat.FromDecimal(state.C1Real);
            c1Imaginary = BigFloat.FromDecimal(state.C1Imaginary);
        }

        BigFloat previousReal = BigFloat.FromDecimal(state.InitialPreviousReal);
        BigFloat previousImaginary = BigFloat.FromDecimal(state.InitialPreviousImaginary);
        BigFloat c2Real = BigFloat.FromDecimal(state.C2Real);
        BigFloat c2Imaginary = BigFloat.FromDecimal(state.C2Imaginary);

        int maximum = state.Iterations;
        double thresholdSquared = (double)(state.Threshold * state.Threshold);
        bool detectPeriods = state.ColoringMode == PhoenixColoringMode.Period;
        bool trackTrap = state.ColoringMode == PhoenixColoringMode.OrbitTrap;
        bool trackStripe = state.ColoringMode == PhoenixColoringMode.StripeAverage;
        bool trackTriangle = state.ColoringMode == PhoenixColoringMode.TriangleInequalityAverage;
        int maximumPeriod = Math.Clamp(state.MaximumDetectedPeriod, 1, MaximumSupportedPeriod);
        int historyCapacity = maximumPeriod + 1;

        // Орбита ведётся в BigFloat, а метрики окраски — в double от неё: точность нужна
        // самой орбите, производные величины (угол, полоса, ловушка) значимы в 2–3 цифрах.
        var currentHistory = detectPeriods ? new ComplexValue[MaximumSupportedPeriod + 1] : [];
        var previousHistory = detectPeriods ? new ComplexValue[MaximumSupportedPeriod + 1] : [];

        double minimumTrap = double.MaxValue;
        double stripeSum = 0, triangleSum = 0;
        int iteration = 0, detectedPeriod = 0;
        var current = new ComplexValue(currentReal.ToDouble(), currentImaginary.ToDouble());

        while (iteration < maximum && current.MagnitudeSquared <= thresholdSquared)
        {
            if (trackTrap) minimumTrap = Math.Min(minimumTrap, OrbitTrapDistance(state, current));
            if (trackStripe)
                stripeSum += 0.5 + 0.5 * Math.Sin(state.StripeFrequency * Math.Atan2(current.Imaginary, current.Real));

            if (detectPeriods)
            {
                int historyIndex = iteration % historyCapacity;
                currentHistory[historyIndex] = current;
                previousHistory[historyIndex] =
                    new ComplexValue(previousReal.ToDouble(), previousImaginary.ToDouble());
            }

            (BigFloat primaryReal, BigFloat primaryImaginary) =
                VariantPowerBig(currentReal, currentImaginary, state.PrimaryPower, state.Variant);
            (BigFloat secondaryReal, BigFloat secondaryImaginary) =
                VariantPowerBig(currentReal, currentImaginary, state.SecondaryPower, state.Variant);

            BigFloat nextReal = primaryReal
                + (c1Real * secondaryReal - c1Imaginary * secondaryImaginary)
                + (c2Real * previousReal - c2Imaginary * previousImaginary);
            BigFloat nextImaginary = primaryImaginary
                + (c1Real * secondaryImaginary + c1Imaginary * secondaryReal)
                + (c2Real * previousImaginary + c2Imaginary * previousReal);

            var next = new ComplexValue(nextReal.ToDouble(), nextImaginary.ToDouble());
            if (trackTriangle)
            {
                double edgeLength = Distance(next, current);
                if (double.IsFinite(edgeLength) && edgeLength > 1e-300)
                {
                    double triangleRatio = (next.Magnitude - current.Magnitude) / edgeLength;
                    triangleSum += 0.5 + 0.5 * Math.Clamp(triangleRatio, -1, 1);
                }
            }

            previousReal = currentReal;
            previousImaginary = currentImaginary;
            currentReal = nextReal;
            currentImaginary = nextImaginary;
            current = next;
            iteration++;

            if (detectPeriods && iteration >= 4)
            {
                var previousValue = new ComplexValue(previousReal.ToDouble(), previousImaginary.ToDouble());
                int available = Math.Min(maximumPeriod, iteration - 1);
                double toleranceSquared = Math.Pow(Math.Max(1e-14, state.CycleTolerance), 2);
                for (int period = 1; period <= available; period++)
                {
                    if (iteration < period * 2) continue;
                    int pastIndex = (iteration - period) % historyCapacity;
                    if (!Close(current, currentHistory[pastIndex], toleranceSquared) ||
                        !Close(previousValue, previousHistory[pastIndex], toleranceSquared)) continue;
                    detectedPeriod = period;
                    break;
                }
                if (detectedPeriod > 0) break;
            }
        }

        bool isInterior = detectedPeriod > 0 || iteration >= maximum;
        double smooth = Smooth(iteration, maximum, current.MagnitudeSquared,
            Math.Max(state.PrimaryPower, state.SecondaryPower));
        double argument = double.IsFinite(current.Real) && double.IsFinite(current.Imaginary)
            ? PositiveModulo(Math.Atan2(current.Imaginary, current.Real) / (2 * Math.PI) + 0.5, 1)
            : 0;
        return new PixelMetrics(iteration, smooth,
            minimumTrap == double.MaxValue ? 0 : minimumTrap,
            iteration == 0 ? 0 : stripeSum / iteration,
            iteration == 0 ? 0 : triangleSum / iteration,
            argument, detectedPeriod, isInterior);
    }
}
