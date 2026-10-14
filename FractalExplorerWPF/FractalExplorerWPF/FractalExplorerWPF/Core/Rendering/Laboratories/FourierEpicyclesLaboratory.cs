using System.Numerics;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Эпициклы Фурье: контур, его ряд и восстановление вращающимися окружностями.</summary>
internal static class FourierEpicyclesLaboratory
{
    public static void Render(
        LaboratoryCanvas canvas,
        MathematicalLaboratoryState state,
        CancellationToken token,
        IProgress<int>? progress)
    {
        int samples = Math.Clamp(state.SecondaryValue, 32, 2_048);
        Complex[] source = ResampleContour(state.InputPoints, samples);
        List<FourierCoefficient> coefficients = CalculateFourier(source, token, progress);
        if (state.Mode == 2)
        {
            List<FourierCoefficient> spectrum = coefficients
                .OrderBy(coefficient => Math.Abs(coefficient.Frequency))
                .Take(Math.Min(state.PrimaryValue * 2 + 1, coefficients.Count)).ToList();
            double maximum = Math.Max(1e-9, spectrum.Max(item => item.Value.Magnitude));
            for (int i = 0; i < spectrum.Count; i++)
            {
                double x = -0.92 + 1.84 * (i + 0.5) / spectrum.Count;
                double barHeight = 1.55 * Math.Sqrt(spectrum[i].Value.Magnitude / maximum);
                canvas.Line(x, 0.78, x, 0.78 - barHeight, Math.Max(1, canvas.Width / (double)spectrum.Count * 0.55),
                    Palette(state, (double)i / spectrum.Count), 0.88);
            }
            return;
        }

        int harmonicCount = Math.Clamp(state.PrimaryValue, 1, Math.Min(250, coefficients.Count));
        List<FourierCoefficient> selected = state.Mode == 1
            ? coefficients.OrderByDescending(item => item.Value.Magnitude).Take(harmonicCount).ToList()
            : coefficients.OrderBy(item => Math.Abs(item.Frequency) * 2 + (item.Frequency < 0 ? 1 : 0))
                .Take(harmonicCount).ToList();
        int tracePoints = Math.Clamp(state.TertiaryValue, 50, 4_000);
        LaboratoryPoint? previous = null;
        for (int i = 0; i <= tracePoints; i++)
        {
            if ((i & 127) == 0) token.ThrowIfCancellationRequested();
            double t = (double)i / tracePoints;
            Complex value = Reconstruct(selected, t);
            var point = new LaboratoryPoint(value.Real, value.Imaginary);
            if (previous is { } last)
                canvas.Line(last.X, last.Y, point.X, point.Y, 1.4,
                    Palette(state, t), 0.88);
            previous = point;
        }

        double phase = state.Phase - Math.Floor(state.Phase);
        Complex center = Complex.Zero;
        for (int i = 0; i < selected.Count; i++)
        {
            FourierCoefficient coefficient = selected[i];
            Complex next = center + coefficient.Value * Complex.Exp(Complex.ImaginaryOne * Math.Tau * coefficient.Frequency * phase);
            double radius = coefficient.Value.Magnitude;
            if (state.ShowGuides && radius > 0.002)
                canvas.Circle(center.Real, center.Imaginary, radius, 0.8,
                    Palette(state, (double)i / selected.Count), false);
            canvas.Line(center.Real, center.Imaginary, next.Real, next.Imaginary, 1,
                Palette(state, (double)i / selected.Count), 0.85);
            center = next;
        }
        canvas.Circle(center.Real, center.Imaginary, 0.014, 1, state.AccentColor, true);
    }

    private static Complex[] ResampleContour(IReadOnlyList<LaboratoryPoint> points, int samples)
    {
        LaboratoryPoint[] contour = points.Count >= 4
            ? points.ToArray()
            : Enumerable.Range(0, 360).Select(index =>
            {
                double t = Math.Tau * index / 360;
                double radius = 0.48 + 0.16 * Math.Cos(5 * t) + 0.08 * Math.Sin(3 * t);
                return new LaboratoryPoint(radius * Math.Cos(t), radius * Math.Sin(t));
            }).ToArray();
        var result = new Complex[samples];
        for (int i = 0; i < samples; i++)
        {
            double position = (double)i / samples * contour.Length;
            int left = (int)Math.Floor(position) % contour.Length;
            int right = (left + 1) % contour.Length;
            double amount = position - Math.Floor(position);
            LaboratoryPoint point = Lerp(contour[left], contour[right], amount);
            result[i] = new Complex(point.X, point.Y);
        }
        return result;
    }

    private static List<FourierCoefficient> CalculateFourier(
        Complex[] samples, CancellationToken token, IProgress<int>? progress)
    {
        int count = samples.Length;
        var result = new List<FourierCoefficient>(count);
        for (int frequency = -count / 2; frequency < (count + 1) / 2; frequency++)
        {
            Complex sum = Complex.Zero;
            for (int n = 0; n < count; n++)
                sum += samples[n] * Complex.Exp(-Complex.ImaginaryOne * Math.Tau * frequency * n / count);
            result.Add(new FourierCoefficient(frequency, sum / count));
            if ((frequency & 15) == 0)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(12 + (frequency + count / 2) * 54 / count);
            }
        }
        return result;
    }

    private static Complex Reconstruct(IEnumerable<FourierCoefficient> coefficients, double time)
    {
        Complex result = Complex.Zero;
        foreach (FourierCoefficient coefficient in coefficients)
            result += coefficient.Value * Complex.Exp(Complex.ImaginaryOne * Math.Tau * coefficient.Frequency * time);
        return result;
    }

    private readonly record struct FourierCoefficient(int Frequency, Complex Value);
}
