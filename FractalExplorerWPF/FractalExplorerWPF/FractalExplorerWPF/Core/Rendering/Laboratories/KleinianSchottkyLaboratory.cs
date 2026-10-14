using System.Numerics;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Предельные множества клейновых групп и групп Шоттки.</summary>
internal static class KleinianSchottkyLaboratory
{
    public static void Render(
        LaboratoryCanvas canvas,
        MathematicalLaboratoryState state,
        CancellationToken token,
        IProgress<int>? progress)
    {
        int points = Math.Clamp(state.PrimaryValue, 2_000, 2_000_000);
        int burnIn = Math.Clamp(state.SecondaryValue, 1, 200);
        double deformation = Math.Clamp(state.Parameter, 0.05, 3);
        int mode = Math.Clamp(state.Mode, 0, 4);
        var random = new Random(state.TertiaryValue);
        Complex rotation = Complex.FromPolarCoordinates(1, state.Phase * Math.Tau);

        if (mode == 2)
            RenderTwoParabolicOrbit(canvas, state, points, burnIn, deformation, random, rotation, token, progress);
        else
            RenderCircleGroupOrbit(canvas, state, points, burnIn, deformation, mode, random, rotation, token, progress);
        progress?.Report(95);
    }

    private static void RenderCircleGroupOrbit(
        LaboratoryCanvas canvas,
        MathematicalLaboratoryState state,
        int points,
        int burnIn,
        double deformation,
        int mode,
        Random random,
        Complex rotation,
        CancellationToken token,
        IProgress<int>? progress)
    {
        CircleGenerator[] generators;
        if (mode == 3)
        {
            double smallRadius = 0.34;
            generators =
            [
                new CircleGenerator(new Complex(0, 0), 0.92, new Complex(0, 0), 0.92, 0),
                new CircleGenerator(Complex.FromPolarCoordinates(0.43, -Math.PI / 2), smallRadius,
                    Complex.FromPolarCoordinates(0.43, -Math.PI / 2), smallRadius, 0),
                new CircleGenerator(Complex.FromPolarCoordinates(0.43, Math.PI / 6), smallRadius,
                    Complex.FromPolarCoordinates(0.43, Math.PI / 6), smallRadius, 0),
                new CircleGenerator(Complex.FromPolarCoordinates(0.43, 5 * Math.PI / 6), smallRadius,
                    Complex.FromPolarCoordinates(0.43, 5 * Math.PI / 6), smallRadius, 0)
            ];
        }
        else
        {
            double radius = Math.Clamp(0.255 + deformation * 0.115, 0.22, 0.42);
            double distance = Math.Clamp(0.72 - deformation * 0.055, 0.48, 0.7);
            double twist = mode == 1 ? deformation * 0.42 : 0;
            Complex left = new(-distance, 0), right = new(distance, 0);
            Complex bottom = new(0, -distance), top = new(0, distance);
            generators =
            [
                new CircleGenerator(right, radius, left, radius, twist),
                new CircleGenerator(left, radius, right, radius, -twist),
                new CircleGenerator(top, radius, bottom, radius, twist),
                new CircleGenerator(bottom, radius, top, radius, -twist)
            ];
        }

        Complex z = mode == 3 ? new Complex(0.1, 0.13) : Complex.Zero;
        int previous = -1;
        int total = checked(points + burnIn);
        for (int iteration = 0; iteration < total; iteration++)
        {
            if ((iteration & 8191) == 0)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(8 + iteration * 80 / Math.Max(1, total));
            }
            int branch;
            do branch = random.Next(generators.Length);
            while (mode != 3 && previous >= 0 && branch == (previous ^ 1));

            CircleGenerator generator = generators[branch];
            z = mode == 3 ? generator.Invert(z) : generator.Contract(z, branch);
            previous = branch;
            if (!IsFinite(z) || z.Magnitude > 1e8)
            {
                z = new Complex(random.NextDouble() * 0.1, random.NextDouble() * 0.1);
                previous = -1;
                continue;
            }
            if (iteration < burnIn) continue;
            Complex plotted = z * rotation;
            Color color = Palette(state, (branch + iteration * 0.000013) / generators.Length);
            canvas.Plot(plotted.Real, plotted.Imaginary, color,
                state.Filled ? 0.075 : 0.3, state.Filled ? 1.35 : 0.7);
        }

        if (state.ShowGuides || mode == 4)
        {
            foreach (CircleGenerator generator in generators)
            {
                Complex center = generator.Target * rotation;
                canvas.Circle(center.Real, center.Imaginary, generator.TargetRadius, 1.2,
                    state.AccentColor, false, 0.72);
            }
        }
    }

    private static void RenderTwoParabolicOrbit(
        LaboratoryCanvas canvas,
        MathematicalLaboratoryState state,
        int points,
        int burnIn,
        double deformation,
        Random random,
        Complex rotation,
        CancellationToken token,
        IProgress<int>? progress)
    {
        Complex mu = new(-0.18 + deformation * 0.08, 0.45 + deformation * 0.82);
        Complex z = new(0.031, 0.071);
        int previous = -1;
        int total = points + burnIn;
        for (int iteration = 0; iteration < total; iteration++)
        {
            if ((iteration & 8191) == 0)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(8 + iteration * 80 / Math.Max(1, total));
            }
            int branch;
            do branch = random.Next(4);
            while (previous >= 0 && branch == (previous ^ 1));
            z = branch switch
            {
                0 => z + Complex.One,
                1 => z - Complex.One,
                2 => z / (mu * z + Complex.One),
                _ => z / (-mu * z + Complex.One)
            };
            previous = branch;
            if (!IsFinite(z) || z.Magnitude > 1e7)
            {
                z = new Complex(0.031, 0.071);
                previous = -1;
                continue;
            }
            if (iteration < burnIn) continue;
            Complex normalized = 0.56 * z / (1 + 0.16 * z.Magnitude) * rotation;
            canvas.Plot(normalized.Real, normalized.Imaginary,
                Palette(state, branch / 4d + iteration * 0.000019),
                state.Filled ? 0.07 : 0.3, state.Filled ? 1.4 : 0.7);
        }

        if (state.ShowGuides)
        {
            canvas.Line(-0.9, 0, 0.9, 0, 1, Mix(state.PrimaryColor, state.BackgroundColor, 0.62), 0.7);
            canvas.Circle(0, 0, 0.56 / Math.Max(0.2, mu.Magnitude), 1,
                state.AccentColor, false, 0.5);
        }
    }

    private static bool IsFinite(Complex value) =>
        double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);

    private readonly record struct CircleGenerator(
        Complex Target,
        double TargetRadius,
        Complex Source,
        double SourceRadius,
        double Twist)
    {
        public Complex Contract(Complex value, int branch)
        {
            Complex normalized = value / 1.15;
            Complex bend = Complex.FromPolarCoordinates(0.29, branch * Math.Tau / 4 + Twist * 0.35);
            Complex denominator = Complex.One + Complex.Conjugate(bend) * normalized;
            if (denominator.Magnitude < 1e-12) denominator = new Complex(1e-12, 0);
            Complex diskAutomorphism = (normalized + bend) / denominator;
            return Target + TargetRadius * 0.94 * Complex.FromPolarCoordinates(1, Twist) * diskAutomorphism;
        }

        public Complex Invert(Complex value)
        {
            Complex denominator = Complex.Conjugate(value - Target);
            if (denominator.Magnitude < 1e-12) denominator = new Complex(1e-12, 0);
            return Target + TargetRadius * TargetRadius / denominator;
        }
    }
}
