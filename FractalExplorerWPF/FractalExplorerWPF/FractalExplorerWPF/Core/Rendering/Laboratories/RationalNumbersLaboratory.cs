using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Рациональные числа: деревья Штерна—Броко и Калкина—Уилфа, Фарей, окружности Форда, цепные дроби.</summary>
internal static class RationalNumbersLaboratory
{
    public static void Render(LaboratoryCanvas canvas, MathematicalLaboratoryState state, CancellationToken token)
    {
        int depth = Math.Clamp(state.PrimaryValue, 1, 13);
        int denominator = Math.Clamp(state.SecondaryValue, 2, 300);
        int limit = Math.Clamp(state.TertiaryValue, 20, 30_000);
        switch (state.Mode)
        {
            case 0:
                RenderRationalTree(canvas, depth, limit, sternBrocot: true, state, token);
                break;
            case 1:
                RenderRationalTree(canvas, depth, limit, sternBrocot: false, state, token);
                break;
            case 2:
            case 3:
                RenderFarey(canvas, denominator, limit, state.Mode == 3, state, token);
                break;
            default:
                RenderContinuedFraction(canvas, state.Parameter, denominator, state);
                break;
        }
    }

    private static void RenderRationalTree(
        LaboratoryCanvas canvas, int maxDepth, int limit, bool sternBrocot,
        MathematicalLaboratoryState state, CancellationToken token)
    {
        var nodes = new List<RationalNode>();
        if (sternBrocot)
        {
            var queue = new Queue<(long lp, long lq, long rp, long rq, int depth, int position, int parent)>();
            queue.Enqueue((0, 1, 1, 0, 0, 0, -1));
            while (queue.Count > 0 && nodes.Count < limit)
            {
                var item = queue.Dequeue();
                long p = item.lp + item.rp;
                long q = item.lq + item.rq;
                int index = nodes.Count;
                double slots = Math.Pow(2, item.depth);
                double x = -0.94 + 1.88 * (item.position + 0.5) / slots;
                double y = -0.85 + 1.7 * item.depth / Math.Max(1, maxDepth);
                nodes.Add(new RationalNode(p, q, x, y, item.parent, item.depth));
                if (item.depth >= maxDepth) continue;
                queue.Enqueue((item.lp, item.lq, p, q, item.depth + 1, item.position * 2, index));
                queue.Enqueue((p, q, item.rp, item.rq, item.depth + 1, item.position * 2 + 1, index));
            }
        }
        else
        {
            var queue = new Queue<(long p, long q, int depth, int position, int parent)>();
            queue.Enqueue((1, 1, 0, 0, -1));
            while (queue.Count > 0 && nodes.Count < limit)
            {
                var item = queue.Dequeue();
                int index = nodes.Count;
                double slots = Math.Pow(2, item.depth);
                double x = -0.94 + 1.88 * (item.position + 0.5) / slots;
                double y = -0.85 + 1.7 * item.depth / Math.Max(1, maxDepth);
                nodes.Add(new RationalNode(item.p, item.q, x, y, item.parent, item.depth));
                if (item.depth >= maxDepth) continue;
                queue.Enqueue((item.p, item.p + item.q, item.depth + 1, item.position * 2, index));
                queue.Enqueue((item.p + item.q, item.q, item.depth + 1, item.position * 2 + 1, index));
            }
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
            RationalNode node = nodes[i];
            if (node.Parent >= 0)
            {
                RationalNode parent = nodes[node.Parent];
                canvas.Line(parent.X, parent.Y, node.X, node.Y, 1,
                    Palette(state, (double)node.Depth / Math.Max(1, maxDepth)), 0.6);
            }
        }
        foreach (RationalNode node in nodes)
        {
            double radius = Math.Clamp(0.026 / Math.Sqrt(node.Depth + 1), 0.0035, 0.02);
            canvas.Circle(node.X, node.Y, radius, 1,
                Palette(state, (double)node.Depth / Math.Max(1, maxDepth)), state.Filled);
        }
    }

    private static void RenderFarey(
        LaboratoryCanvas canvas, int maxDenominator, int limit, bool fordCircles,
        MathematicalLaboratoryState state, CancellationToken token)
    {
        var fractions = new List<(int p, int q, double value)> { (0, 1, 0), (1, 1, 1) };
        for (int q = 2; q <= maxDenominator && fractions.Count < limit; q++)
        {
            for (int p = 1; p < q && fractions.Count < limit; p++)
                if (GreatestCommonDivisor(p, q) == 1) fractions.Add((p, q, (double)p / q));
            if ((q & 15) == 0) token.ThrowIfCancellationRequested();
        }
        fractions.Sort((left, right) => left.value.CompareTo(right.value));
        canvas.Line(-0.94, 0.72, 0.94, 0.72, 1.4, state.PrimaryColor, 0.7);

        foreach ((int p, int q, double value) fraction in fractions)
        {
            double x = -0.94 + 1.88 * fraction.value;
            if (fordCircles)
            {
                double radius = Math.Min(0.34, 0.7 / (fraction.q * fraction.q));
                canvas.Circle(x, 0.72 - radius, radius, 1,
                    Palette(state, (double)(fraction.q % 17) / 17), state.Filled);
            }
            else
            {
                double height = 0.7 / fraction.q;
                canvas.Line(x, 0.72, x, 0.72 - height, 1.2,
                    Palette(state, (double)(fraction.q % 13) / 13), 0.85);
                canvas.Circle(x, 0.72 - height, 0.004 + 0.012 / Math.Sqrt(fraction.q),
                    1, state.AccentColor, true);
            }
        }
    }

    private static void RenderContinuedFraction(
        LaboratoryCanvas canvas, double target, int maxTerms, MathematicalLaboratoryState state)
    {
        target = double.IsFinite(target) ? target : Math.Sqrt(2);
        maxTerms = Math.Clamp(maxTerms, 2, 32);
        var convergents = new List<(long p, long q, double error)>();
        double x = target;
        long p0 = 0, p1 = 1, q0 = 1, q1 = 0;
        for (int i = 0; i < maxTerms; i++)
        {
            long a = (long)Math.Floor(x);
            long p;
            long q;
            try
            {
                p = checked(a * p1 + p0);
                q = checked(a * q1 + q0);
            }
            catch (OverflowException)
            {
                break;
            }
            convergents.Add((p, q, Math.Abs(target - (double)p / q)));
            p0 = p1; p1 = p; q0 = q1; q1 = q;
            double remainder = x - a;
            if (remainder < 1e-13 || q > 10_000_000) break;
            x = 1 / remainder;
        }
        if (convergents.Count == 0) return;
        double maxError = Math.Max(1e-12, convergents[0].error);
        for (int i = 0; i < convergents.Count; i++)
        {
            double px = -0.88 + 1.76 * i / Math.Max(1, convergents.Count - 1d);
            double py = 0.78 - 1.56 * Math.Clamp(-Math.Log10(Math.Max(1e-15, convergents[i].error)) / 15, 0, 1);
            if (i > 0)
            {
                double previousX = -0.88 + 1.76 * (i - 1) / Math.Max(1, convergents.Count - 1d);
                double previousY = 0.78 - 1.56 * Math.Clamp(
                    -Math.Log10(Math.Max(1e-15, convergents[i - 1].error)) / 15, 0, 1);
                canvas.Line(previousX, previousY, px, py, 2, Palette(state, (double)i / convergents.Count), 0.9);
            }
            canvas.Circle(px, py, 0.018, 1, Palette(state, (double)i / convergents.Count), true);
        }
        if (state.ShowGuides)
            for (int i = 0; i <= 5; i++)
            {
                double y = 0.78 - 1.56 * i / 5d;
                canvas.Line(-0.9, y, 0.9, y, 1, state.PrimaryColor, 0.16);
            }
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return Math.Abs(a);
    }

    private readonly record struct RationalNode(long P, long Q, double X, double Y, int Parent, int Depth);
}
