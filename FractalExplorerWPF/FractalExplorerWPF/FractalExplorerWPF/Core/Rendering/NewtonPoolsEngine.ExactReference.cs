using System.Numerics;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Эталон для проверок: орбита каждого пикселя считается напрямую в <see cref="BigFloat"/>, без
/// пертурбации, пропуска и опорной орбиты; все решения — по double-копиям тех же величин, что у
/// плоской ступени. Медленно, только для маленьких кадров проверочного проекта.
///
/// Эталон делит с опорной орбитой <see cref="CompiledComplexExpression.EvaluateBig"/>, поэтому
/// ошибку переноса самих функций в BigFloat повторил бы. Её ловит сравнение глубокого движка с
/// плоской double-ступенью на мелком зуме — у них общего кода нет.
/// </summary>
public sealed partial class NewtonPoolsEngine
{
    internal byte[] RenderExactReferenceForTests(int width, int height, int extraBits, CancellationToken token)
    {
        CompiledComplexExpression[] expressions = DeepExpressions()
            ?? throw new InvalidOperationException("Формула не задана или метод не поддерживается.");
        int bits = PlanReferenceBits() + extraBits;
        bool diagnostics = DiagnosticColoringMode != NewtonDiagnosticColoringMode.Disabled;
        FloatExp viewWidth = FloatExp.FromDouble(BaseViewWidth) / Zoom;
        var pixels = new byte[checked(width * height * 4)];

        Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, y =>
        {
            using var precision = new BigFloat.PrecisionScope(bits);
            var center = new ComplexBigFloat(BigFloat.Parse(CenterXRaw), BigFloat.Parse(CenterYRaw));
            var values = new ComplexBigFloat[expressions.Length][];
            for (int expression = 0; expression < expressions.Length; expression++)
                values[expression] = new ComplexBigFloat[expressions[expression].InstructionCount];
            FloatExp pixelImaginary = (height / 2.0 - y) * viewWidth / width;
            for (int x = 0; x < width; x++)
            {
                if (token.IsCancellationRequested) return;
                FloatExp pixelReal = (x - width / 2.0) * viewWidth / width;
                var pixel = new ComplexBigFloat(center.Real + pixelReal.ToBigFloat(), center.Imaginary + pixelImaginary.ToBigFloat());
                bool lambdaPlane = UsesLambdaParameterPlane;
                ComplexBigFloat z = lambdaPlane ? ComplexBigFloat.FromDouble(FixedInitialZ.Real, FixedInitialZ.Imaginary) : pixel;
                ComplexBigFloat lambda = lambdaPlane ? pixel : ComplexBigFloat.FromDouble(Relaxation.Real, Relaxation.Imaginary);
                System.Windows.Media.Color color;
                if (diagnostics)
                {
                    color = GetDiagnosticColor(ExactDiagnose(expressions, values, z, lambda));
                }
                else
                {
                    int iteration = ExactNormal(expressions, values, ref z, lambda);
                    color = GetPixelColor(z.ToComplex(), iteration);
                }
                WriteColor(pixels, (y * width + x) * 4, color);
            }
        });
        return pixels;
    }

    /// <summary>Шаг в BigFloat и double-копии величин, по которым плоская ступень принимает решения.</summary>
    private DeepStepStatus ExactStep(CompiledComplexExpression[] expressions, ComplexBigFloat[][] values,
        ComplexBigFloat z, ComplexBigFloat f, ComplexBigFloat lambda, bool diagnostics, out ComplexBigFloat step)
    {
        step = default;
        Complex fDouble = f.ToComplex();
        try
        {
            ComplexBigFloat first = expressions[1].EvaluateBig(z, values[1], default);
            Complex g = first.ToComplex();
            switch (IterationMethod)
            {
                case NewtonIterationMethod.Halley:
                {
                    if (!IsFinite(g)) return DeepStepStatus.NonFinite;
                    if (diagnostics ? IsEffectivelyZero(g) : g == Complex.Zero) return DeepStepStatus.ZeroDerivative;
                    ComplexBigFloat second = expressions[2].EvaluateBig(z, values[2], default);
                    Complex h = second.ToComplex();
                    if (diagnostics && !IsFinite(h)) return DeepStepStatus.NonFinite;
                    ComplexBigFloat denominatorBig = first * first * 2 - f * second;
                    Complex denominator = denominatorBig.ToComplex();
                    if (!IsFinite(denominator)) return DeepStepStatus.NonFinite;
                    if (diagnostics ? IsEffectivelyZero(denominator) : denominator == Complex.Zero)
                        return DeepStepStatus.ZeroDerivative;
                    step = -(f * first * 2 / denominatorBig);
                    break;
                }
                case NewtonIterationMethod.Householder:
                {
                    ComplexBigFloat current = expressions[2].EvaluateBig(z, values[2], default);
                    Complex q = current.ToComplex();
                    if (!IsFinite(g) || !IsFinite(q)) return DeepStepStatus.NonFinite;
                    if (diagnostics ? IsEffectivelyZero(q) : q == Complex.Zero) return DeepStepStatus.ZeroDerivative;
                    step = first * HouseholderOrder / current;
                    break;
                }
                default:
                {
                    if (!IsFinite(g)) return DeepStepStatus.NonFinite;
                    if (diagnostics ? IsEffectivelyZero(g) : g == Complex.Zero) return DeepStepStatus.ZeroDerivative;
                    step = IterationMethod == NewtonIterationMethod.RelaxedNewton
                        ? -(lambda * f / first)
                        : -(f / first);
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is ArithmeticException or ArgumentException)
        {
            return DeepStepStatus.NonFinite;
        }

        Complex stepDouble = step.ToComplex();
        if (!IsFinite(stepDouble) || !IsFinite(fDouble)) return DeepStepStatus.NonFinite;
        if (!diagnostics && stepDouble == Complex.Zero) return DeepStepStatus.ZeroDerivative;
        return DeepStepStatus.Success;
    }

    private int ExactNormal(CompiledComplexExpression[] expressions, ComplexBigFloat[][] values,
        ref ComplexBigFloat z, ComplexBigFloat lambda)
    {
        double toleranceSquared = RootTolerance * RootTolerance;
        int iteration = 0;
        while (iteration < MaxIterations)
        {
            ComplexBigFloat f;
            try { f = expressions[0].EvaluateBig(z, values[0], default); }
            catch (Exception exception) when (exception is ArithmeticException or ArgumentException) { break; }
            Complex fDouble = f.ToComplex();
            if (!IsFinite(fDouble) || fDouble == Complex.Zero) break;
            if ((MagnitudeSquared(fDouble) <= toleranceSquared || (iteration & 7) == 7) && IsNearKnownRoot(z.ToComplex())) break;
            if (ExactStep(expressions, values, z, f, lambda, false, out ComplexBigFloat step) != DeepStepStatus.Success) break;
            z += step;
            iteration++;
        }
        return iteration;
    }

    private NewtonOrbitResult ExactDiagnose(CompiledComplexExpression[] expressions, ComplexBigFloat[][] values,
        ComplexBigFloat z, ComplexBigFloat lambda)
    {
        double toleranceSquared = RootTolerance * RootTolerance;
        Span<Complex> history = stackalloc Complex[HistoryCapacity];
        int historyCount = 0, historyNext = 0;
        AddHistory(history, ref historyCount, ref historyNext, z.ToComplex());
        Complex lastValue = new(double.NaN, double.NaN);

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            Complex zDouble = z.ToComplex();
            if (!IsFinite(zDouble)) return CreateOrbitResult(NewtonOrbitOutcome.NonFinite, iteration, zDouble, lastValue);
            if (IsEscaped(zDouble)) return CreateOrbitResult(NewtonOrbitOutcome.Escaped, iteration, zDouble, lastValue);

            ComplexBigFloat f;
            try { f = expressions[0].EvaluateBig(z, values[0], default); }
            catch (Exception exception) when (exception is ArithmeticException or ArgumentException)
            {
                return CreateOrbitResult(NewtonOrbitOutcome.NonFinite, iteration, zDouble, lastValue);
            }
            Complex fDouble = f.ToComplex();
            lastValue = fDouble;
            if (!IsFinite(fDouble)) return CreateOrbitResult(NewtonOrbitOutcome.NonFinite, iteration, zDouble, fDouble);

            int rootIndex = FindKnownRootIndex(zDouble);
            if (fDouble == Complex.Zero || MagnitudeSquared(fDouble) <= toleranceSquared || rootIndex >= 0)
                return CreateOrbitResult(NewtonOrbitOutcome.ConvergedToRoot, iteration, zDouble, fDouble, rootIndex);

            int cyclePeriod = DetectCycle(history, historyCount, historyNext);
            if (cyclePeriod is >= 2 and <= 8)
                return CreateOrbitResult(NewtonOrbitOutcome.Cycle, iteration, zDouble, fDouble, cyclePeriod: cyclePeriod);

            switch (ExactStep(expressions, values, z, f, lambda, true, out ComplexBigFloat step))
            {
                case DeepStepStatus.NonFinite:
                    return CreateOrbitResult(NewtonOrbitOutcome.NonFinite, iteration, zDouble, fDouble);
                case DeepStepStatus.ZeroDerivative:
                    return CreateOrbitResult(NewtonOrbitOutcome.ZeroDerivative, iteration, zDouble, fDouble);
            }

            z += step;
            AddHistory(history, ref historyCount, ref historyNext, z.ToComplex());
        }

        Complex finalZ = z.ToComplex();
        return FinishDiagnostic(finalZ, history, historyCount, historyNext, lastValue);
    }
}
