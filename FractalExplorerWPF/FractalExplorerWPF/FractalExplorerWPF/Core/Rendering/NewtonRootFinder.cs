using System.Numerics;
using FractalExplorerWPF.Core.NewtonMath;

namespace FractalExplorerWPF.Core.Rendering;

internal static class NewtonRootFinder
{
    private const int MaxPolynomialDegree = 128;
    private const double SolverTolerance = 1e-13;

    public static bool TryFindPolynomialRoots(
        ExpressionNode formula,
        double requestedTolerance,
        out IReadOnlyList<Complex> roots,
        out int degree)
    {
        roots = [];
        degree = 0;
        if (!TryGetPolynomialCoefficients(formula, out Complex[] coefficients)) return false;

        degree = coefficients.Length - 1;
        if (degree <= 0) return true;
        return TrySolvePolynomial(coefficients, requestedTolerance, out roots);
    }

    /// <summary>
    /// Коэффициенты полинома по возрастанию степени (старший ненулевой — последний), если выражение —
    /// полином от z с комплексными коэффициентами.
    /// </summary>
    internal static bool TryGetPolynomialCoefficients(ExpressionNode formula, out Complex[] coefficients)
    {
        if (!TryGetCoefficients(formula, out coefficients)) return false;
        coefficients = Trim(coefficients);
        return true;
    }

    /// <summary>
    /// Корни полинома степени ≥ 1 методом Аберта—Эрлиха с полировкой и слиянием кратных.
    /// Возвращает false, если старший коэффициент нулевой или нечисловой.
    /// </summary>
    internal static bool TrySolvePolynomial(Complex[] polynomial, double requestedTolerance, out IReadOnlyList<Complex> roots)
    {
        roots = [];
        Complex[] coefficients = Trim(polynomial);
        if (coefficients.Length <= 1) return coefficients.Length == 1;

        Complex leading = coefficients[^1];
        if (!IsFinite(leading) || leading == Complex.Zero) return false;
        for (int index = 0; index < coefficients.Length; index++) coefficients[index] /= leading;

        Complex[] candidates = SolveAberth(coefficients);
        var accepted = new List<Complex>(candidates.Length);
        double residualLimit = Math.Max(1e-10, requestedTolerance * 0.1);
        double mergeDistance = Math.Max(1e-9, Math.Min(1e-4, requestedTolerance * 2));

        foreach (Complex candidate in candidates)
        {
            Complex polished = PolishPolynomialRoot(coefficients, candidate);
            if (!IsFinite(polished)) continue;
            Complex residual = EvaluatePolynomial(coefficients, polished);
            double scale = PolynomialScale(coefficients, polished);
            if (!IsFinite(residual) || residual.Magnitude > residualLimit * Math.Max(1, scale)) continue;
            AddUnique(accepted, polished, mergeDistance);
        }

        roots = Sort(accepted);
        return true;
    }

    public static IReadOnlyList<Complex> FindAdaptiveRoots(
        Func<Complex, Complex> formula,
        Func<Complex, Complex> derivative,
        double centerX,
        double centerY,
        double radius,
        double requestedTolerance,
        int maxIterations = 250)
    {
        radius = Math.Clamp(radius, 0.01, 1e9);
        requestedTolerance = Math.Clamp(requestedTolerance, 1e-12, 0.1);
        double solverTolerance = Math.Min(requestedTolerance * 0.05, 1e-10);
        var roots = new List<Complex>();
        double mergeDistance = Math.Max(1e-8, Math.Min(1e-4, requestedTolerance * 2));
        double residualLimit = Math.Max(1e-12, solverTolerance);

        // A modest base mesh covers the whole requested area. Promising cells are then
        // recursively refined, so narrow basins do not depend on one lucky seed.
        const int gridSize = 17;
        var samples = new Complex[gridSize, gridSize];
        var values = new Complex[gridSize, gridSize];
        for (int y = 0; y < gridSize; y++)
        for (int x = 0; x < gridSize; x++)
        {
            Complex point = new(
                centerX - radius + 2 * radius * x / (gridSize - 1),
                centerY - radius + 2 * radius * y / (gridSize - 1));
            samples[x, y] = point;
            values[x, y] = Evaluate(formula, point);
            TrySeed(formula, derivative, point, solverTolerance, residualLimit, maxIterations, roots, mergeDistance);
        }

        for (int y = 0; y < gridSize - 1; y++)
        for (int x = 0; x < gridSize - 1; x++)
        {
            RefineCell(
                formula,
                derivative,
                samples[x, y],
                samples[x + 1, y + 1],
                values[x, y],
                values[x + 1, y],
                values[x + 1, y + 1],
                values[x, y + 1],
                depth: 0,
                solverTolerance,
                residualLimit,
                maxIterations,
                roots,
                mergeDistance);
        }

        // Concentric, phase-shifted rings help with remote roots near the boundary and
        // functions whose Newton basins align poorly with the Cartesian mesh.
        for (int ring = 1; ring <= 8; ring++)
        {
            double ringRadius = radius * ring / 8.0;
            int count = 24 + ring * 8;
            for (int index = 0; index < count; index++)
            {
                double angle = 2 * Math.PI * (index + ring * 0.3819660112501051) / count;
                Complex point = new(centerX + ringRadius * Math.Cos(angle), centerY + ringRadius * Math.Sin(angle));
                TrySeed(formula, derivative, point, solverTolerance, residualLimit, maxIterations, roots, mergeDistance);
            }
        }

        double boundaryAllowance = radius * 1e-9 + requestedTolerance;
        return Sort(roots.Where(root =>
            Math.Abs(root.Real - centerX) <= radius + boundaryAllowance &&
            Math.Abs(root.Imaginary - centerY) <= radius + boundaryAllowance));
    }

    private static void RefineCell(
        Func<Complex, Complex> formula,
        Func<Complex, Complex> derivative,
        Complex min,
        Complex max,
        Complex bottomLeft,
        Complex bottomRight,
        Complex topRight,
        Complex topLeft,
        int depth,
        double tolerance,
        double residualLimit,
        int maxIterations,
        List<Complex> roots,
        double mergeDistance)
    {
        Complex center = (min + max) / 2;
        Complex centerValue = Evaluate(formula, center);
        TrySeed(formula, derivative, center, tolerance, residualLimit, maxIterations, roots, mergeDistance);

        if (depth >= 3) return;
        double winding = PhaseDelta(bottomLeft, bottomRight) + PhaseDelta(bottomRight, topRight) +
                         PhaseDelta(topRight, topLeft) + PhaseDelta(topLeft, bottomLeft);
        double minimum = MinMagnitude(bottomLeft, bottomRight, topRight, topLeft, centerValue);
        double maximum = MaxFiniteMagnitude(bottomLeft, bottomRight, topRight, topLeft, centerValue);
        bool phaseCandidate = Math.Abs(winding) > Math.PI * 0.75;
        bool residualCandidate = minimum < Math.Max(1, maximum) * 0.08;
        if (!phaseCandidate && !residualCandidate) return;

        Complex bottomMidPoint = new(center.Real, min.Imaginary);
        Complex rightMidPoint = new(max.Real, center.Imaginary);
        Complex topMidPoint = new(center.Real, max.Imaginary);
        Complex leftMidPoint = new(min.Real, center.Imaginary);
        Complex bottomMid = Evaluate(formula, bottomMidPoint);
        Complex rightMid = Evaluate(formula, rightMidPoint);
        Complex topMid = Evaluate(formula, topMidPoint);
        Complex leftMid = Evaluate(formula, leftMidPoint);

        RefineCell(formula, derivative, min, center, bottomLeft, bottomMid, centerValue, leftMid,
            depth + 1, tolerance, residualLimit, maxIterations, roots, mergeDistance);
        RefineCell(formula, derivative, bottomMidPoint, new Complex(max.Real, center.Imaginary), bottomMid, bottomRight,
            rightMid, centerValue, depth + 1, tolerance, residualLimit, maxIterations, roots, mergeDistance);
        RefineCell(formula, derivative, center, max, centerValue, rightMid, topRight, topMid,
            depth + 1, tolerance, residualLimit, maxIterations, roots, mergeDistance);
        RefineCell(formula, derivative, leftMidPoint, topMidPoint, leftMid, centerValue, topMid, topLeft,
            depth + 1, tolerance, residualLimit, maxIterations, roots, mergeDistance);
    }

    private static void TrySeed(
        Func<Complex, Complex> formula,
        Func<Complex, Complex> derivative,
        Complex seed,
        double tolerance,
        double residualLimit,
        int maxIterations,
        List<Complex> roots,
        double mergeDistance)
    {
        Complex z = seed;
        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            Complex f = Evaluate(formula, z);
            if (!IsFinite(f)) return;
            if (f == Complex.Zero) break;

            Complex f1 = Evaluate(derivative, z);
            if (!IsFinite(f1)) return;
            if (f1 == Complex.Zero)
            {
                if (f.Magnitude <= residualLimit) break;
                return;
            }
            Complex step = f / f1;
            if (!IsFinite(step)) return;

            // Backtracking prevents one large Newton jump from losing a useful seed.
            Complex next = z - step;
            Complex nextValue = Evaluate(formula, next);
            for (int damping = 0; damping < 8 &&
                 (!IsFinite(nextValue) || nextValue.Magnitude > f.Magnitude * 1.25); damping++)
            {
                step *= 0.5;
                next = z - step;
                nextValue = Evaluate(formula, next);
            }

            z = next;
            if (!IsFinite(z) || z.Magnitude > 1e12) return;
            if (step.Magnitude <= tolerance * Math.Max(1, z.Magnitude)) break;
        }

        Complex residual = Evaluate(formula, z);
        if (!IsFinite(residual) || residual.Magnitude > residualLimit * 10) return;
        AddUnique(roots, z, mergeDistance * Math.Max(1, z.Magnitude));
    }

    private static Complex[] SolveAberth(Complex[] coefficients)
    {
        int degree = coefficients.Length - 1;
        if (degree == 1) return [-coefficients[0]];

        double bound = 1 + coefficients.Take(degree).Max(value => value.Magnitude);
        var roots = new Complex[degree];
        for (int index = 0; index < degree; index++)
        {
            double angle = 2 * Math.PI * (index + 0.5) / degree;
            double radius = bound * (0.82 + 0.18 * (index + 1.0) / degree);
            roots[index] = Complex.FromPolarCoordinates(radius, angle);
        }

        for (int iteration = 0; iteration < 2500; iteration++)
        {
            double largestCorrection = 0;
            for (int index = 0; index < degree; index++)
            {
                Complex z = roots[index];
                (Complex value, Complex derivative) = EvaluatePolynomialAndDerivative(coefficients, z);
                if (!IsFinite(value) || !IsFinite(derivative)) continue;
                if (value.Magnitude <= SolverTolerance * PolynomialScale(coefficients, z)) continue;

                if (derivative.Magnitude < 1e-18)
                {
                    double jitterAngle = 2 * Math.PI * (iteration + index * 0.6180339887498949) / degree;
                    roots[index] += Complex.FromPolarCoordinates(1e-8 * Math.Max(1, z.Magnitude), jitterAngle);
                    continue;
                }

                Complex newton = value / derivative;
                Complex repulsion = Complex.Zero;
                for (int other = 0; other < degree; other++)
                {
                    if (other == index) continue;
                    Complex difference = z - roots[other];
                    if (difference.Magnitude > 1e-18) repulsion += Complex.One / difference;
                }

                Complex denominator = Complex.One - newton * repulsion;
                Complex correction = denominator.Magnitude > 1e-18 ? newton / denominator : newton;
                if (!IsFinite(correction)) continue;
                roots[index] -= correction;
                largestCorrection = Math.Max(largestCorrection, correction.Magnitude / Math.Max(1, z.Magnitude));
            }

            if (largestCorrection <= SolverTolerance) break;
        }

        return roots;
    }

    private static Complex PolishPolynomialRoot(Complex[] coefficients, Complex root)
    {
        Complex z = root;
        int detectedMultiplicity = 1;
        for (int iteration = 0; iteration < 80; iteration++)
        {
            (Complex value, Complex derivative, Complex secondDerivative) = EvaluatePolynomialDerivatives(coefficients, z);
            if (!IsFinite(value) || !IsFinite(derivative) || !IsFinite(secondDerivative) || value == Complex.Zero || derivative == Complex.Zero) break;

            int multiplicity = 1;
            Complex derivativeSquared = derivative * derivative;
            if (derivativeSquared != Complex.Zero && IsFinite(derivativeSquared))
            {
                Complex estimate = Complex.One / (Complex.One - value * secondDerivative / derivativeSquared);
                if (IsFinite(estimate))
                {
                    int rounded = (int)Math.Round(estimate.Real);
                    if (Math.Abs(estimate.Imaginary) < 0.2 && Math.Abs(estimate.Real - rounded) < 0.2 &&
                        rounded is >= 2 && rounded < coefficients.Length)
                    {
                        multiplicity = rounded;
                        detectedMultiplicity = Math.Max(detectedMultiplicity, rounded);
                    }
                }
            }

            Complex step = multiplicity * value / derivative;
            if (!IsFinite(step)) break;
            z -= step;
            if (step.Magnitude <= SolverTolerance * Math.Max(1, z.Magnitude)) break;
        }

        if (detectedMultiplicity > 1)
        {
            Complex[] derivative = coefficients;
            for (int order = 1; order < detectedMultiplicity; order++) derivative = DifferentiatePolynomial(derivative);
            z = PolishSimplePolynomialRoot(derivative, z);
        }
        return z;
    }

    private static Complex PolishSimplePolynomialRoot(Complex[] coefficients, Complex root)
    {
        Complex z = root;
        for (int iteration = 0; iteration < 40; iteration++)
        {
            (Complex value, Complex derivative) = EvaluatePolynomialAndDerivative(coefficients, z);
            if (!IsFinite(value) || !IsFinite(derivative) || value == Complex.Zero || derivative == Complex.Zero) break;
            Complex step = value / derivative;
            if (!IsFinite(step)) break;
            z -= step;
            if (step.Magnitude <= SolverTolerance * Math.Max(1, z.Magnitude)) break;
        }
        return z;
    }

    private static Complex[] DifferentiatePolynomial(Complex[] coefficients)
    {
        if (coefficients.Length <= 1) return [Complex.Zero];
        var derivative = new Complex[coefficients.Length - 1];
        for (int index = 1; index < coefficients.Length; index++) derivative[index - 1] = index * coefficients[index];
        return Trim(derivative);
    }

    private static (Complex Value, Complex Derivative) EvaluatePolynomialAndDerivative(Complex[] coefficients, Complex z)
    {
        Complex value = coefficients[^1];
        Complex derivative = Complex.Zero;
        for (int index = coefficients.Length - 2; index >= 0; index--)
        {
            derivative = derivative * z + value;
            value = value * z + coefficients[index];
        }
        return (value, derivative);
    }

    private static (Complex Value, Complex Derivative, Complex SecondDerivative) EvaluatePolynomialDerivatives(
        Complex[] coefficients,
        Complex z)
    {
        Complex value = coefficients[^1];
        Complex derivative = Complex.Zero;
        Complex secondDerivative = Complex.Zero;
        for (int index = coefficients.Length - 2; index >= 0; index--)
        {
            secondDerivative = secondDerivative * z + 2 * derivative;
            derivative = derivative * z + value;
            value = value * z + coefficients[index];
        }
        return (value, derivative, secondDerivative);
    }

    private static Complex EvaluatePolynomial(Complex[] coefficients, Complex z)
    {
        Complex value = coefficients[^1];
        for (int index = coefficients.Length - 2; index >= 0; index--) value = value * z + coefficients[index];
        return value;
    }

    private static double PolynomialScale(Complex[] coefficients, Complex z)
    {
        double scale = coefficients[^1].Magnitude;
        double magnitude = z.Magnitude;
        for (int index = coefficients.Length - 2; index >= 0; index--)
            scale = scale * magnitude + coefficients[index].Magnitude;
        return scale;
    }

    private static bool TryGetCoefficients(ExpressionNode node, out Complex[] coefficients)
    {
        switch (node)
        {
            case NumberNode number:
                coefficients = [number.Value];
                return IsFinite(number.Value);
            case VariableNode variable when variable.Name == "z":
                coefficients = [Complex.Zero, Complex.One];
                return true;
            case VariableNode variable when variable.Name == "i":
                coefficients = [Complex.ImaginaryOne];
                return true;
            case UnaryOpNode unary when TryGetCoefficients(unary.Operand, out Complex[] operand):
                coefficients = unary.Operator == "-" ? operand.Select(value => -value).ToArray() : operand;
                return unary.Operator is "+" or "-";
            case BinaryOpNode binary:
                return TryGetBinaryCoefficients(binary, out coefficients);
            default:
                coefficients = [];
                return false;
        }
    }

    private static bool TryGetBinaryCoefficients(BinaryOpNode binary, out Complex[] coefficients)
    {
        coefficients = [];
        if (!TryGetCoefficients(binary.Left, out Complex[] left) ||
            !TryGetCoefficients(binary.Right, out Complex[] right)) return false;

        switch (binary.Operator)
        {
            case "+":
                coefficients = Add(left, right, 1);
                return true;
            case "-":
                coefficients = Add(left, right, -1);
                return true;
            case "*":
                coefficients = Multiply(left, right);
                return coefficients.Length <= MaxPolynomialDegree + 1;
            case "/" when right.Length == 1 && right[0] != Complex.Zero:
                coefficients = left.Select(value => value / right[0]).ToArray();
                return coefficients.All(IsFinite);
            case "^" when right.Length == 1 && right[0].Imaginary == 0:
                double exponentValue = right[0].Real;
                int exponent = (int)Math.Round(exponentValue);
                if (Math.Abs(exponentValue - exponent) > 1e-12 || exponent is < 0 or > MaxPolynomialDegree) return false;
                coefficients = Pow(left, exponent);
                return coefficients.Length <= MaxPolynomialDegree + 1;
            default:
                return false;
        }
    }

    private static Complex[] Add(Complex[] left, Complex[] right, int rightSign)
    {
        var result = new Complex[Math.Max(left.Length, right.Length)];
        for (int index = 0; index < left.Length; index++) result[index] += left[index];
        for (int index = 0; index < right.Length; index++) result[index] += rightSign * right[index];
        return Trim(result);
    }

    private static Complex[] Multiply(Complex[] left, Complex[] right)
    {
        if (left.Length + right.Length - 1 > MaxPolynomialDegree + 1) return new Complex[MaxPolynomialDegree + 2];
        var result = new Complex[left.Length + right.Length - 1];
        for (int leftIndex = 0; leftIndex < left.Length; leftIndex++)
        for (int rightIndex = 0; rightIndex < right.Length; rightIndex++)
            result[leftIndex + rightIndex] += left[leftIndex] * right[rightIndex];
        return Trim(result);
    }

    private static Complex[] Pow(Complex[] value, int exponent)
    {
        Complex[] result = [Complex.One];
        Complex[] factor = value;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0) result = Multiply(result, factor);
            exponent >>= 1;
            if (exponent > 0) factor = Multiply(factor, factor);
            if (result.Length > MaxPolynomialDegree + 1 || factor.Length > MaxPolynomialDegree + 1)
                return new Complex[MaxPolynomialDegree + 2];
        }
        return Trim(result);
    }

    internal static Complex[] Trim(Complex[] coefficients)
    {
        int last = coefficients.Length - 1;
        while (last > 0 && coefficients[last] == Complex.Zero) last--;
        return coefficients.Take(last + 1).ToArray();
    }

    private static Complex Evaluate(Func<Complex, Complex> expression, Complex z)
    {
        try { return expression(z); }
        catch { return new Complex(double.NaN, double.NaN); }
    }

    private static double PhaseDelta(Complex first, Complex second)
    {
        if (!IsFinite(first) || !IsFinite(second) || first == Complex.Zero || second == Complex.Zero) return 0;
        double delta = Math.Atan2(second.Imaginary, second.Real) - Math.Atan2(first.Imaginary, first.Real);
        while (delta > Math.PI) delta -= 2 * Math.PI;
        while (delta < -Math.PI) delta += 2 * Math.PI;
        return delta;
    }

    private static double MinMagnitude(params Complex[] values) => values.Where(IsFinite).Select(value => value.Magnitude).DefaultIfEmpty(double.PositiveInfinity).Min();
    private static double MaxFiniteMagnitude(params Complex[] values) => values.Where(IsFinite).Select(value => value.Magnitude).DefaultIfEmpty(0).Max();

    private static void AddUnique(List<Complex> roots, Complex candidate, double distance)
    {
        int existing = roots.FindIndex(root => (root - candidate).Magnitude <= distance);
        if (existing < 0) roots.Add(candidate);
        else roots[existing] = (roots[existing] + candidate) / 2;
    }

    private static IReadOnlyList<Complex> Sort(IEnumerable<Complex> roots) => roots
        .OrderBy(root => root.Real)
        .ThenBy(root => root.Imaginary)
        .ToArray();

    private static bool IsFinite(Complex value) => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);
}
