using System.Numerics;

namespace FractalExplorerWPF.Core.RauzyMath;

/// <summary>
/// Prefix projection for a → a^k b, b → ac, c → a (k=1 is Tribonacci).
/// The complex left eigenvector (1, α−k, 1/α) annihilates the expanding direction.
/// </summary>
public static class RauzyFractal
{
    public readonly record struct Projection(double Beta, Complex Alpha, Complex A, Complex B, Complex C)
    {
        // Projection of the integer plane x+y+z=0: periods of the central tile.
        public Complex PeriodU => A - B;
        public Complex PeriodV => A - C;
    }

    public readonly record struct Sample(Complex Position, byte Letter);

    public static Projection CreateProjection(int coefficient)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(coefficient, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(coefficient, 4);
        double low = coefficient, high = coefficient + 2;
        for (int i = 0; i < 64; i++)
        {
            double x = (low + high) / 2;
            if (x * x * x - coefficient * x * x - x - 1 > 0) high = x;
            else low = x;
        }
        double beta = (low + high) / 2;
        double real = (coefficient - beta) / 2;
        var alpha = new Complex(real, -System.Math.Sqrt(1 / beta - real * real));
        return new(beta, alpha, Complex.One, alpha - coefficient, Complex.One / alpha);
    }

    /// <summary>The first count letters of the infinite fixed word, independently of count.</summary>
    public static byte[] GenerateWord(int count, int coefficient, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 2_000_000);
        _ = CreateProjection(coefficient);
        token.ThrowIfCancellationRequested();
        byte[] word = [0];
        while (word.Length < count)
        {
            var next = new byte[System.Math.Min(count, checked(word.Length * (coefficient + 1)))];
            int length = 0;
            for (int i = 0; i < word.Length && length < next.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                int zeros = word[i] == 0 ? coefficient : 1;
                while (zeros-- > 0 && length < next.Length) next[length++] = 0;
                if (word[i] < 2 && length < next.Length) next[length++] = (byte)(word[i] + 1);
            }
            if (length != next.Length) Array.Resize(ref next, length);
            word = next;
        }
        return word;
    }

    public static Sample[] Generate(int count, int coefficient, CancellationToken token = default)
    {
        byte[] word = GenerateWord(count, coefficient, token);
        Projection projection = CreateProjection(coefficient);
        Complex[] steps = [projection.A, projection.B, projection.C];
        var result = new Sample[count];
        // Compute from integer letter counts instead of accumulating floating point drift.
        int a = 0, b = 0, c = 0;
        for (int i = 0; i < count; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            result[i] = new(a * steps[0] + b * steps[1] + c * steps[2], word[i]);
            switch (word[i])
            {
                case 0: a++; break;
                case 1: b++; break;
                case 2: c++; break;
            }
        }
        return result;
    }
}
