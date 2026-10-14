using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Фигуры Хладни и интерференция волн.</summary>
internal static class ChladniWaveInterferenceLaboratory
{
    public static void Render(
        LaboratoryCanvas canvas,
        MathematicalLaboratoryState state,
        CancellationToken token,
        IProgress<int>? progress)
    {
        int mode = Math.Clamp(state.Mode, 0, 5);
        int first = Math.Clamp(state.PrimaryValue, 0, 64);
        int second = Math.Clamp(state.SecondaryValue, 1, 128);
        int contourBands = Math.Clamp(state.TertiaryValue, 1, 360);
        double phase = Math.Tau * (state.Phase - Math.Floor(state.Phase));
        double threshold = Math.Clamp(state.Parameter, 0.005, 0.35);
        List<WaveSource> sources = mode >= 3
            ? CreateWaveSources(mode, first, state.TertiaryValue, state.Parameter)
            : [];
        double waveNumber = 5.2 + second * 1.55;
        int completedRows = 0;
        int reportStep = Math.Max(1, canvas.Height / 24);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        Parallel.For(0, canvas.Height, options, (y, loopState) =>
        {
            if (token.IsCancellationRequested)
            {
                loopState.Stop();
                return;
            }

            for (int x = 0; x < canvas.Width; x++)
            {
                if ((x & 63) == 0 && token.IsCancellationRequested)
                {
                    loopState.Stop();
                    return;
                }

                (double worldX, double worldY) = canvas.Unmap(x + 0.5, y + 0.5);
                bool inside;
                double value;
                double effectiveThreshold = threshold;
                if (mode <= 1)
                {
                    const double halfSize = 0.92;
                    inside = Math.Abs(worldX) <= halfSize && Math.Abs(worldY) <= halfSize;
                    if (!inside) continue;
                    double u = (worldX / halfSize + 1) * 0.5;
                    double v = (worldY / halfSize + 1) * 0.5;
                    int m = Math.Max(1, first);
                    int n = Math.Max(1, second);
                    double direct = Math.Sin(m * Math.PI * u) * Math.Sin(n * Math.PI * v);
                    double exchanged = Math.Sin(n * Math.PI * u) * Math.Sin(m * Math.PI * v);
                    double phaseAmplitude = 0.72 + 0.28 * Math.Cos(phase);
                    value = (mode == 0 ? direct + exchanged : direct - exchanged) *
                            phaseAmplitude * 0.72;
                }
                else if (mode == 2)
                {
                    double radius = Math.Sqrt(worldX * worldX + worldY * worldY) / 0.92;
                    inside = radius <= 1;
                    if (!inside) continue;
                    int angularOrder = first;
                    int radialOrder = Math.Max(1, second);
                    double alpha = (radialOrder + angularOrder * 0.5 - 0.25) * Math.PI;
                    double angle = Math.Atan2(worldY, worldX);
                    double phaseAmplitude = 0.72 + 0.28 * Math.Cos(phase);
                    value = BesselJ(angularOrder, alpha * radius) *
                            Math.Cos(angularOrder * angle) * phaseAmplitude * 2.35;
                }
                else
                {
                    inside = true;
                    double sum = 0;
                    foreach (WaveSource source in sources)
                    {
                        double dx = worldX - source.X;
                        double dy = worldY - source.Y;
                        double distance = Math.Sqrt(dx * dx + dy * dy);
                        sum += Math.Cos(waveNumber * distance - phase + source.Phase) /
                               Math.Sqrt(0.14 + distance);
                    }
                    value = sum / Math.Sqrt(Math.Max(1, sources.Count));
                    effectiveThreshold = Math.Clamp(0.028 + state.Parameter * 0.045, 0.015, 0.16);
                }

                Color color = WaveFieldColor(state, value, effectiveThreshold, contourBands);
                canvas.SetPixel(x, y, color);
            }

            int done = Interlocked.Increment(ref completedRows);
            if (!token.IsCancellationRequested && (done % reportStep == 0 || done == canvas.Height))
                progress?.Report(8 + done * 86 / canvas.Height);
        });

        if (token.IsCancellationRequested || !state.ShowGuides) return;
        Color guide = Mix(state.AccentColor, state.PrimaryColor, 0.35);
        if (mode <= 1)
        {
            LaboratoryPoint[] plate = [new(-0.92, -0.92), new(0.92, -0.92), new(0.92, 0.92), new(-0.92, 0.92)];
            canvas.Polygon(plate, guide, false, 1.4);
        }
        else if (mode == 2)
        {
            canvas.Circle(0, 0, 0.92, 1.5, guide, false);
        }
        else
        {
            foreach (WaveSource source in sources)
            {
                canvas.Circle(source.X, source.Y, 0.018, 1, state.AccentColor, true);
                canvas.Circle(source.X, source.Y, 0.034, 1, guide, false);
            }
            if (mode == 5)
                canvas.Line(-0.72, -0.92, -0.72, 0.92, 1, guide, 0.45);
        }
    }

    private static List<WaveSource> CreateWaveSources(
        int mode, int primary, int phaseStepDegrees, double parameter)
    {
        var sources = new List<WaveSource>();
        double phaseStep = phaseStepDegrees * Math.PI / 180;
        if (mode == 3)
        {
            double spacing = Math.Clamp(0.24 + parameter * 0.5, 0.2, 0.82);
            sources.Add(new WaveSource(-spacing / 2, 0, 0));
            sources.Add(new WaveSource(spacing / 2, 0, phaseStep));
            return sources;
        }

        if (mode == 4)
        {
            int count = Math.Clamp(primary, 2, 64);
            double radius = Math.Clamp(0.3 + parameter * 0.32, 0.22, 0.72);
            for (int i = 0; i < count; i++)
            {
                double angle = Math.Tau * i / count;
                sources.Add(new WaveSource(radius * Math.Cos(angle), radius * Math.Sin(angle), i * phaseStep));
            }
            return sources;
        }

        int slitCount = Math.Clamp(primary, 2, 32);
        double separation = Math.Clamp(0.075 + parameter * 0.22, 0.06, 0.24);
        double offset = (slitCount - 1) * separation / 2;
        for (int i = 0; i < slitCount; i++)
            sources.Add(new WaveSource(-0.72, i * separation - offset, i * phaseStep));
        return sources;
    }

    private static Color WaveFieldColor(
        MathematicalLaboratoryState state, double value, double threshold, int contourBands)
    {
        double absolute = Math.Abs(value);
        double signed = 0.5 + 0.5 * Math.Tanh(value * 1.35);
        double nodeStrength = Math.Exp(-Math.Pow(absolute / Math.Max(1e-6, threshold), 2));
        if (!state.Filled)
            return Mix(state.BackgroundColor, state.AccentColor, nodeStrength * 0.96);

        double amplitude = Math.Clamp(absolute, 0, 1);
        double contour = 0.82 + 0.18 * Math.Cos(Math.Tau * contourBands * absolute);
        Color wave = Palette(state, signed);
        Color color = Mix(state.BackgroundColor, wave, (0.25 + amplitude * 0.7) * contour);
        return Mix(color, state.AccentColor, nodeStrength * 0.88);
    }

    private static double BesselJ(int order, double value)
    {
        value = Math.Abs(value);
        if (value < 1e-12) return order == 0 ? 1 : 0;
        if (value > 20)
            return Math.Sqrt(2 / (Math.PI * value)) *
                   Math.Cos(value - order * Math.PI / 2 - Math.PI / 4);

        double half = value / 2;
        double term = 1;
        for (int i = 1; i <= order; i++) term *= half / i;
        double sum = term;
        for (int index = 1; index < 80; index++)
        {
            term *= -half * half / (index * (index + order));
            sum += term;
            if (Math.Abs(term) < Math.Abs(sum) * 1e-14 + 1e-16) break;
        }
        return sum;
    }

    private readonly record struct WaveSource(double X, double Y, double Phase);
}
