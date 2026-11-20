using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.WaveMath;

internal static class StandingWave
{
    // Scalar wave speed is one length unit per time unit.
    public static double Frequency(StandingWaveSettings settings, StandingWaveMode mode)
    {
        double x = mode.X / settings.LengthX, y = mode.Y / settings.LengthY, z = mode.Z / settings.LengthZ;
        return .5 * Math.Sqrt(x * x + y * y + z * z);
    }

    public static Vector4 Parameters(StandingWaveSettings settings, StandingWaveMode mode, double amplitudeSum)
    {
        double phase = Math.Tau * Frequency(settings, mode) * settings.Time + mode.Phase * Math.PI / 180;
        // Normalize by initial amplitudes, never by the instantaneous amplitude.
        double weight = mode.Amplitude * Math.Cos(phase) / Math.Max(1, amplitudeSum);
        return new(mode.X, mode.Y, mode.Z, (float)weight);
    }
}
