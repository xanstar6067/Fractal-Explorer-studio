using System.Numerics;
using System.Text.Json.Serialization;

namespace FractalExplorerWPF.Models;

/// <summary>Ортонормированный 3D-срез q = R(x,y,z,w), порядок поворотов XW → YW → ZW.</summary>
public sealed record QuaternionJuliaSettings
{
    public double XW { get; init; }
    public double YW { get; init; }
    public double ZW { get; init; }
    public double SweepMinimum { get; init; } = -.8;
    public double SweepMaximum { get; init; } = .8;
    public double SweepSpeed { get; init; } = .25;
    [JsonIgnore]
    public bool HasRotation => XW != 0 || YW != 0 || ZW != 0;

    public void Validate()
    {
        if (!Finite(XW, -180, 180) || !Finite(YW, -180, 180) || !Finite(ZW, -180, 180) ||
            !Finite(SweepMinimum, -4, 4) || !Finite(SweepMaximum, -4, 4) || SweepMinimum >= SweepMaximum ||
            !Finite(SweepSpeed, .01, 4))
            throw new ArgumentException("Углы среза: −180…180°; проход: −4…4 с возрастающими границами; скорость: 0,01…4.");
    }

    public Vector4 SlicePoint(Vector3 point, double offset)
    {
        double[] q = [point.X, point.Y, point.Z, offset];
        double[] angles = [XW, YW, ZW];
        for (int i = 0; i < 3; i++)
        {
            double angle = angles[i] * Math.PI / 180, co = Math.Cos(angle), si = Math.Sin(angle);
            (q[i], q[3]) = (co * q[i] - si * q[3], si * q[i] + co * q[3]);
        }
        return new((float)q[0], (float)q[1], (float)q[2], (float)q[3]);
    }

    /// <summary>Проход туда-обратно без скачка на границе.</summary>
    public double Advance(double current, double seconds, ref int direction)
    {
        double span = SweepMaximum - SweepMinimum;
        double position = Math.Clamp(current, SweepMinimum, SweepMaximum) - SweepMinimum;
        double phase = direction >= 0 ? position : 2 * span - position;
        phase = (phase + Math.Max(0, seconds) * SweepSpeed) % (2 * span);
        direction = phase < span ? 1 : -1;
        return SweepMinimum + (phase <= span ? phase : 2 * span - phase);
    }

    private static bool Finite(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;
}
