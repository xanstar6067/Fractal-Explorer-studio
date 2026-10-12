using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

// Трансцендентные функции, логарифм и корень BigFloat.
internal static partial class Program
{
    // Phase 10: BigFloat.Sqrt — the one operation odd-power Simonobrot needs that the type
    // did not have (|z|ᵖ = M^(p/2) = Mᵠ·√M). Checked three ways: against the published
    // decimal expansion of √2, by round-tripping (√x)² back to x at several magnitudes, and
    // by demanding exactness on perfect squares (where the integer Newton iteration must
    // land on the root itself, not one ULP below it).
    // Phase 11: transcendental functions over BigFloat (π, exp, sin/cos, sh/ch), built for
    // the Collatz deep-zoom stage. Nothing in the Mandelbrot engine calls them, so this is
    // the only place that pins them down. Three independent kinds of oracle:
    //   • published digits — catches a wrong algorithm outright;
    //   • identities (sin²+cos²=1, ch²−sh²=1, doubling formulas) — hold at every precision
    //     and catch guard-bit shortfalls the digit checks would miss at a single precision;
    //   • agreement with the double library on ordinary arguments — catches a wrong branch
    //     in the argument reduction, which the identities alone would not (they survive a
    //     consistent shift of both sin and cos).
    private static void VerifyBigFloatTranscendental()
    {
        // First 100 digits of each constant (truncated, not rounded — the checks compare
        // a prefix of the produced digit string).
        const string PiDigits =
            "3.141592653589793238462643383279502884197169399375105820974944592307816406286208998628034825342117067";
        const string EDigits =
            "2.718281828459045235360287471352662497757247093699959574966967627724076630353547594571382178525166427";
        const string Sin1Digits =
            "0.841470984807896506652502321630298999622563060798371065672751709991910404391239668948639743543052695";
        const string Cos1Digits =
            "0.540302305868139717400936607442976603732310420617922227670097255381100394774471764517951856087183089";
        const string Sinh1Digits =
            "1.175201193643801456882381850595600815155717981334095870229565413013307567304323895607117452089623391";
        const string Cosh1Digits =
            "1.543080634815243778477905620757061682601529112365863704737402214710769063049223698964264726435543035";
        const string SinPiTenthDigits =
            "0.309016994374947424102293417182819058860154589902881431067724311352630231409451224853603602094695568";
        const string CosPiTenthDigits =
            "0.951056516295153572116439333379382143405698634125750222447305644430153170085193501718792810970811381";
        const string ExpMinusFiveDigits =
            "0.006737946999085467096636048423148424248849585027355085430305531572683522515604062281449138844208361";

        static void CheckDigits(string label, BigFloat value, string expected)
        {
            string produced = value.ToInvariantString(expected.Length + 20);
            int common = 0;
            while (common < produced.Length && common < expected.Length && produced[common] == expected[common])
                common++;
            Check(common >= expected.Length,
                $"{label} matches only {common} of {expected.Length} published characters: {produced}");
        }

        // 384 bits ≈ 115 decimal digits, so all 100 published ones must come out right.
        using (new BigFloat.PrecisionScope(BigFloat.MinimumPrecisionBits))
        {
            CheckDigits("π", BigFloatMath.Pi, PiDigits);
            CheckDigits("exp(1)", BigFloatMath.Exp(BigFloat.One), EDigits);
            CheckDigits("exp(-5)", BigFloatMath.Exp(BigFloat.FromInt(-5)), ExpMinusFiveDigits);
            Check(BigFloatMath.Exp(BigFloat.Zero).Equals(BigFloat.One), "exp(0) must be exactly 1.");

            BigFloatMath.SinCos(BigFloat.One, out BigFloat sine, out BigFloat cosine);
            CheckDigits("sin(1)", sine, Sin1Digits);
            CheckDigits("cos(1)", cosine, Cos1Digits);

            BigFloatMath.SinCosPi(BigFloat.One / 10, out BigFloat sinePi, out BigFloat cosinePi);
            CheckDigits("sin(π/10)", sinePi, SinPiTenthDigits);
            CheckDigits("cos(π/10)", cosinePi, CosPiTenthDigits);

            BigFloatMath.SinhCosh(BigFloat.One, out BigFloat hyperbolicSine, out BigFloat hyperbolicCosine);
            CheckDigits("sh(1)", hyperbolicSine, Sinh1Digits);
            CheckDigits("ch(1)", hyperbolicCosine, Cosh1Digits);
        }

        // π at a precision far above and far below the 384-bit default: the Machin series
        // must be recomputed per precision, not reused from a cache keyed by nothing.
        using (new BigFloat.PrecisionScope(1024)) CheckDigits("π at 1024 bits", BigFloatMath.Pi, PiDigits);
        using (new BigFloat.PrecisionScope(128))
        {
            string produced = BigFloatMath.Pi.ToInvariantString(60);
            int common = 0;
            while (common < produced.Length && produced[common] == PiDigits[common]) common++;
            // 128 bits ≈ 38 decimal digits; ask for 34 to stay clear of the rounding digit.
            Check(common >= 36, $"π at 128 bits matches only {common} characters: {produced}");
        }

        // Identities at several precisions, over arguments that exercise every branch of
        // the reduction (both signs, every quadrant, several periods away from zero).
        foreach (int bits in new[] { 128, BigFloat.MinimumPrecisionBits, 512 })
        {
            using var precision = new BigFloat.PrecisionScope(bits);
            BigFloat tolerance = BigFloat.FromDouble(System.Math.ScaleB(1.0, -(bits - 12)));
            for (int index = -260; index <= 260; index += 7)
            {
                BigFloat turns = BigFloat.FromInt(index) / 37;
                BigFloatMath.SinCosPi(turns, out BigFloat sine, out BigFloat cosine);
                BigFloat residual = BigFloat.Abs(sine * sine + cosine * cosine - BigFloat.One);
                Check(residual.CompareTo(tolerance) <= 0,
                    $"sin²+cos² deviates from 1 by {residual.ToInvariantString(20)} at {index}/37 turns, {bits} bits.");

                // sin(2πx) = 2 sin(πx) cos(πx) ties the reduced branches to each other:
                // a quadrant mix-up survives sin²+cos²=1 but not this.
                BigFloatMath.SinCosPi(BigFloat.ScaleByPowerOfTwo(turns, 1), out BigFloat doubleSine, out _);
                BigFloat doublingError = BigFloat.Abs(
                    doubleSine - BigFloat.ScaleByPowerOfTwo(sine * cosine, 1));
                Check(doublingError.CompareTo(tolerance) <= 0,
                    $"sin(2πx) ≠ 2·sin(πx)·cos(πx) by {doublingError.ToInvariantString(20)} at {index}/37 turns.");

                BigFloatMath.SinhCosh(turns, out BigFloat hyperbolicSine, out BigFloat hyperbolicCosine);
                BigFloat hyperbolicResidual = BigFloat.Abs(
                    hyperbolicCosine * hyperbolicCosine - hyperbolicSine * hyperbolicSine - BigFloat.One);
                // ch grows like e^|x|, so the absolute residual is allowed to grow with it.
                BigFloat hyperbolicTolerance = tolerance * hyperbolicCosine * hyperbolicCosine;
                Check(hyperbolicResidual.CompareTo(hyperbolicTolerance) <= 0,
                    $"ch²−sh² deviates from 1 by {hyperbolicResidual.ToInvariantString(20)} at {index}/37, {bits} bits.");
            }
        }

        // Reduction by period is exact because it is done on the argument of sin(πx), not by
        // dividing by an approximate 2π. Far from zero the double library visibly loses this
        // (Math.PI * 12345.75 is already rounded), so the reference here is the exact value:
        // 12345.75 mod 2 = 1.75, hence sin = −√2/2 and cos = +√2/2.
        using (new BigFloat.PrecisionScope(BigFloat.MinimumPrecisionBits))
        {
            BigFloat half = BigFloat.ScaleByPowerOfTwo(BigFloat.Sqrt(BigFloat.FromInt(2)), -1);
            BigFloat tolerance = BigFloat.FromDouble(System.Math.ScaleB(1.0, -360));
            // Every argument here is an exact multiple of 1/4 turn, so |sin| = |cos| = √2/2
            // to the last bit; the signs come from the (small, exactly representable)
            // reduced argument, where the double library is still reliable.
            foreach (double turns in new[] { 12345.75, -87.25, 1e6 + 1.75, 0.75 })
            {
                BigFloatMath.SinCosPi(BigFloat.FromDouble(turns), out BigFloat sine, out BigFloat cosine);
                double reduced = turns - 2 * System.Math.Round(turns / 2);
                BigFloat expectedSine = System.Math.Sin(System.Math.PI * reduced) < 0 ? -half : half;
                BigFloat expectedCosine = System.Math.Cos(System.Math.PI * reduced) < 0 ? -half : half;
                Check(BigFloat.Abs(sine - expectedSine).CompareTo(tolerance) <= 0 &&
                      BigFloat.Abs(cosine - expectedCosine).CompareTo(tolerance) <= 0,
                    $"sin/cos(π·{turns}) lost the exact ±√2/2: {sine.ToInvariantString(25)}, {cosine.ToInvariantString(25)}");
            }
        }

        // Agreement with the double library on ordinary arguments — an oracle that shares
        // no code with BigFloat at all.
        using (new BigFloat.PrecisionScope(256))
        {
            var random = new Random(20260908);
            double worstTrig = 0, worstExp = 0, worstHyperbolic = 0;
            for (int index = 0; index < 3000; index++)
            {
                double turns = (random.NextDouble() - 0.5) * 8;
                BigFloatMath.SinCosPi(BigFloat.FromDouble(turns), out BigFloat sine, out BigFloat cosine);
                worstTrig = System.Math.Max(worstTrig,
                    System.Math.Abs(sine.ToDouble() - System.Math.Sin(System.Math.PI * turns)));
                worstTrig = System.Math.Max(worstTrig,
                    System.Math.Abs(cosine.ToDouble() - System.Math.Cos(System.Math.PI * turns)));

                double argument = (random.NextDouble() - 0.5) * 60;
                double exponential = System.Math.Exp(argument);
                worstExp = System.Math.Max(worstExp,
                    System.Math.Abs(BigFloatMath.Exp(BigFloat.FromDouble(argument)).ToDouble() - exponential) / exponential);

                BigFloatMath.SinhCosh(BigFloat.FromDouble(argument),
                    out BigFloat hyperbolicSine, out BigFloat hyperbolicCosine);
                worstHyperbolic = System.Math.Max(worstHyperbolic,
                    System.Math.Abs(hyperbolicSine.ToDouble() - System.Math.Sinh(argument)) /
                    System.Math.Abs(System.Math.Sinh(argument)));
                worstHyperbolic = System.Math.Max(worstHyperbolic,
                    System.Math.Abs(hyperbolicCosine.ToDouble() - System.Math.Cosh(argument)) /
                    System.Math.Cosh(argument));
            }
            Console.WriteLine($"[diag] BigFloat vs double: trig {worstTrig:E2} abs, exp {worstExp:E2} rel, " +
                              $"hyperbolic {worstHyperbolic:E2} rel");
            Check(worstTrig < 1e-13 && worstExp < 1e-13 && worstHyperbolic < 1e-13,
                "BigFloat transcendentals disagree with the double library beyond double's own rounding.");
        }

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Transcendental checks must leave the working precision restored.");
    }

    // Phase 12: logarithm over BigFloat and the complex exponential/logarithm/power built on
    // it, added for the Nova deep-zoom stage — its reference orbit contains z^(1−p) with an
    // arbitrary complex power, and such a power exists only through a logarithm. The same
    // three kinds of oracle as the other transcendentals:
    //   • published digits of ln 2 — catches a wrong algorithm outright;
    //   • identities (exp∘log = id, ln(xy) = ln x + ln y, integer power against exp(k·ln z))
    //     — hold at every precision and catch guard-bit shortfalls;
    //   • agreement with the double library — catches a wrong branch, which the identities
    //     survive (a consistent 2πi shift satisfies exp∘log = id).
    private static void VerifyBigFloatLogarithm()
    {
        const string LogTwoDigits =
            "0.693147180559945309417232121458176568075500134360255254120680009493393621969694715605863326996418687";

        static void CheckDigits(string label, BigFloat value, string expected)
        {
            string produced = value.ToInvariantString(expected.Length + 20);
            int common = 0;
            while (common < produced.Length && common < expected.Length && produced[common] == expected[common])
                common++;
            Check(common >= expected.Length,
                $"{label} matches only {common} of {expected.Length} published characters: {produced}");
        }

        using (new BigFloat.PrecisionScope(BigFloat.MinimumPrecisionBits))
        {
            CheckDigits("ln 2", BigFloatMath.Log(BigFloat.FromInt(2)), LogTwoDigits);
            CheckDigits("LogTwo", BigFloatMath.LogTwo, LogTwoDigits);
            Check(BigFloatMath.Log(BigFloat.One).IsZero, "ln 1 must be exactly 0.");
        }

        // ln 2 at a precision far above and far below the default: the series must be
        // recomputed per precision, not served from a cache keyed by nothing.
        using (new BigFloat.PrecisionScope(1024)) CheckDigits("ln 2 at 1024 bits", BigFloatMath.LogTwo, LogTwoDigits);
        using (new BigFloat.PrecisionScope(128))
        {
            string produced = BigFloatMath.LogTwo.ToInvariantString(60);
            int common = 0;
            while (common < produced.Length && produced[common] == LogTwoDigits[common]) common++;
            // 128 bits ≈ 38 decimal digits; ask for 36 to stay clear of the rounding digit.
            Check(common >= 36, $"ln 2 at 128 bits matches only {common} characters: {produced}");
        }

        // Identities at several precisions. The round trip is the strongest single check the
        // logarithm has: it ties Log to the already-published Exp, and its residual is
        // relative, so it fails the moment the guard bits stop covering the Newton step.
        foreach (int bits in new[] { 128, BigFloat.MinimumPrecisionBits, 768 })
        {
            using var precision = new BigFloat.PrecisionScope(bits);
            BigFloat tolerance = BigFloat.FromDouble(System.Math.ScaleB(1.0, -(bits - 12)));
            foreach (string text in new[]
                     { "1e-90", "0.0009765625", "0.5", "0.9999999", "1.0000001", "3", "123456.789", "1e75" })
            {
                BigFloat value = BigFloat.Parse(text);
                BigFloat roundTrip = BigFloatMath.Exp(BigFloatMath.Log(value));
                BigFloat residual = BigFloat.Abs((roundTrip - value) / value);
                Check(residual.CompareTo(tolerance) <= 0,
                    $"exp(ln {text}) deviates by {residual.ToInvariantString(20)} at {bits} bits.");
            }

            // ln(x·y) = ln x + ln y over arguments whose exponents differ wildly: the
            // decomposition x = m·2^e must contribute exactly e·ln 2 and nothing else.
            BigFloat left = BigFloat.Parse("7.25e-40");
            BigFloat right = BigFloat.Parse("3.5e31");
            BigFloat additive = BigFloat.Abs(
                BigFloatMath.Log(left * right) - BigFloatMath.Log(left) - BigFloatMath.Log(right));
            Check(additive.CompareTo(tolerance) <= 0,
                $"ln(xy) ≠ ln x + ln y by {additive.ToInvariantString(20)} at {bits} bits.");
        }

        // Complex exponential, logarithm and power. The integer power is computed by binary
        // exponentiation and shares no line with the logarithm, so agreeing with
        // exp(k·ln z) pins both down at once — including the branch, which a wrong 2πi
        // offset in Log would break for a non-integer k but not for an integer one.
        using (new BigFloat.PrecisionScope(512))
        {
            BigFloat tolerance = BigFloat.FromDouble(System.Math.ScaleB(1.0, -460));
            var samples = new[]
            {
                new Complex(0.7, 0.3), new Complex(-0.8, 0.05), new Complex(-0.8, -0.05),
                new Complex(1, 0), new Complex(0, 2), new Complex(3e10, -1e-4),
                new Complex(1e-12, 5e-13), new Complex(-2.5, 0)
            };
            foreach (Complex sample in samples)
            {
                ComplexBigFloat value = ComplexBigFloat.FromDouble(sample.Real, sample.Imaginary);
                ComplexBigFloat roundTrip = ComplexBigFloat.Exp(ComplexBigFloat.Log(value));
                ComplexBigFloat residual = (roundTrip - value) / value;
                Check(BigFloat.Abs(residual.Real).CompareTo(tolerance) <= 0 &&
                      BigFloat.Abs(residual.Imaginary).CompareTo(tolerance) <= 0,
                    $"exp(ln z) ≠ z for {sample}: {residual}");

                foreach (int power in new[] { 1, 2, 3, 7, 11, -1, -2, -9 })
                {
                    ComplexBigFloat viaInteger = ComplexBigFloat.Pow(value, power);
                    ComplexBigFloat viaLogarithm =
                        ComplexBigFloat.Pow(value, ComplexBigFloat.FromDouble(power, 0));
                    ComplexBigFloat drift = (viaInteger - viaLogarithm) / viaInteger;
                    Check(BigFloat.Abs(drift.Real).CompareTo(tolerance) <= 0 &&
                          BigFloat.Abs(drift.Imaginary).CompareTo(tolerance) <= 0,
                        $"z^{power} by binary exponentiation ≠ exp({power}·ln z) for {sample}: {drift}");
                }
            }
        }

        // Agreement with the double library — an oracle that shares no code with BigFloat.
        // The complex samples deliberately straddle the negative real axis, where the
        // principal branch jumps: a Newton iteration seeded off-branch would show up here.
        using (new BigFloat.PrecisionScope(256))
        {
            var random = new Random(20260911);
            double worstReal = 0, worstComplex = 0, worstPower = 0;
            for (int index = 0; index < 2000; index++)
            {
                double argument = System.Math.Exp((random.NextDouble() - 0.5) * 120);
                worstReal = System.Math.Max(worstReal,
                    System.Math.Abs(BigFloatMath.Log(BigFloat.FromDouble(argument)).ToDouble() -
                                    System.Math.Log(argument)) / System.Math.Abs(System.Math.Log(argument)));

                var sample = new Complex((random.NextDouble() - 0.5) * 6, (random.NextDouble() - 0.5) * 0.02);
                if (Complex.Abs(sample) < 1e-6) continue;
                Complex expectedLogarithm = Complex.Log(sample);
                Complex actualLogarithm =
                    ComplexBigFloat.Log(ComplexBigFloat.FromDouble(sample.Real, sample.Imaginary)).ToComplex();
                worstComplex = System.Math.Max(worstComplex,
                    Complex.Abs(actualLogarithm - expectedLogarithm) /
                    System.Math.Max(1e-3, Complex.Abs(expectedLogarithm)));

                var exponent = new Complex((random.NextDouble() - 0.5) * 8, (random.NextDouble() - 0.5) * 2);
                Complex expectedPower = Complex.Pow(sample, exponent);
                if (!double.IsFinite(expectedPower.Real) || !double.IsFinite(expectedPower.Imaginary) ||
                    Complex.Abs(expectedPower) < 1e-250) continue;
                Complex actualPower = ComplexBigFloat.Pow(
                    ComplexBigFloat.FromDouble(sample.Real, sample.Imaginary),
                    ComplexBigFloat.FromDouble(exponent.Real, exponent.Imaginary)).ToComplex();
                worstPower = System.Math.Max(worstPower,
                    Complex.Abs(actualPower - expectedPower) / Complex.Abs(expectedPower));
            }
            Console.WriteLine($"[diag] BigFloat log vs double: real {worstReal:E2} rel, " +
                              $"complex {worstComplex:E2} rel, complex pow {worstPower:E2} rel");
            Check(worstReal < 1e-13 && worstComplex < 1e-13 && worstPower < 1e-11,
                "BigFloat logarithm disagrees with the double library beyond double's own rounding.");
        }

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "Logarithm checks must leave the working precision restored.");
    }

    private static void VerifyBigFloatSqrt()
    {
        // First 100 digits of √2.
        const string Root2 =
            "1.414213562373095048801688724209698078569671875376948073176679737990732478462107038850387534327641572";

        using (new BigFloat.PrecisionScope(BigFloat.MinimumPrecisionBits))
        {
            string produced = BigFloat.Sqrt(BigFloat.FromInt(2)).ToInvariantString(120);
            int common = 0;
            while (common < produced.Length && common < Root2.Length && produced[common] == Root2[common]) common++;
            // 384 bits ≈ 115 decimal digits, so all 100 published ones must come out right.
            Check(common >= Root2.Length,
                $"BigFloat.Sqrt(2) matches only {common} of {Root2.Length} published characters: {produced}");
        }

        foreach (int bits in new[] { BigFloat.MinimumPrecisionBits, 512, 1024 })
        {
            using var precision = new BigFloat.PrecisionScope(bits);
            foreach (string text in new[] { "2", "3", "0.9999999999999", "1e-120", "1e40", "1e-300" })
            {
                BigFloat value = BigFloat.Parse(text);
                BigFloat root = BigFloat.Sqrt(value);
                BigFloat error = root * root - value;
                if (error.Sign < 0) error = -error;
                // Two roundings (the root and the squaring) at `bits` significant bits. The
                // bound is scaled in BigFloat, not in double: at 1e-300 a relative ratio
                // taken through ToDouble would underflow to zero and pass vacuously.
                BigFloat tolerance = value * BigFloat.FromDouble(System.Math.ScaleB(1.0, -(bits - 4)));
                Check(error.CompareTo(tolerance) <= 0,
                    $"BigFloat.Sqrt({text}) at {bits} bits: (√x)² is off by {error.ToInvariantString(20)}.");
            }

            foreach (string text in new[] { "4", "0.25", "1", "1e-100", "1e100", "12345678901234567890" })
            {
                BigFloat value = BigFloat.Parse(text);
                Check((BigFloat.Sqrt(value * value) - value).IsZero,
                    $"BigFloat.Sqrt of the perfect square ({text})² must return exactly {text} at {bits} bits.");
            }

            Check(BigFloat.Sqrt(BigFloat.Zero).IsZero, "BigFloat.Sqrt(0) must be zero.");
        }

        Check(BigFloat.WorkingPrecisionBits == BigFloat.MinimumPrecisionBits,
            "BigFloat.Sqrt checks must leave the working precision restored.");
    }
}
