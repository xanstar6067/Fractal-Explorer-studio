using System.Numerics;

namespace FractalExplorerWPF.Models;

public enum StandingWaveView { Surface, Cloud, Nodes, Slice }
public enum StandingWaveAxis { X, Y, Z }

public readonly record struct StandingWaveMode(int X, int Y, int Z, double Amplitude = 1, double Phase = 0)
{
    public const int MaxOrder = 12;

    internal StandingWaveMode Validated() => new(
        Math.Clamp(X, 1, MaxOrder), Math.Clamp(Y, 1, MaxOrder), Math.Clamp(Z, 1, MaxOrder),
        StandingWaveSettings.Bound(Amplitude, 0, 2, 1), StandingWaveSettings.Bound(Phase, -180, 180, 0));
}

public sealed record StandingWaveSettings
{
    public const int MaxModes = 4;
    public const double MaxTime = 10_000;
    internal static IReadOnlyList<int> Resolutions { get; } = Array.AsReadOnly(new[] { 64, 96, 128 });

    public StandingWaveMode[] Modes { get; init; } = [new(3, 2, 2)];
    public double LengthX { get; init; } = 2;
    public double LengthY { get; init; } = 2;
    public double LengthZ { get; init; } = 2;
    public double Time { get; init; }
    public double PlaybackSpeed { get; init; } = .25;
    public StandingWaveView View { get; init; }
    public double Level { get; init; } = .34;
    public double NodeWidth { get; init; } = .04;
    public int Resolution { get; init; } = 64;
    public bool Cutaway { get; init; }
    public StandingWaveAxis Axis { get; init; } = StandingWaveAxis.Z;
    public double CutPosition { get; init; } = .5;

    public StandingWaveSettings Validated() => this with
    {
        Modes = Modes is { Length: > 0 }
            ? Modes.Take(MaxModes).Select(mode => mode.Validated()).ToArray() : [new(3, 2, 2)],
        LengthX = Bound(LengthX, 1, 4, 2),
        LengthY = Bound(LengthY, 1, 4, 2),
        LengthZ = Bound(LengthZ, 1, 4, 2),
        Time = Bound(Time, 0, MaxTime, 0),
        PlaybackSpeed = Bound(PlaybackSpeed, .01, 4, .25),
        View = Enum.IsDefined(View) ? View : StandingWaveView.Surface,
        Level = Bound(Level, .02, 1, .34),
        NodeWidth = Bound(NodeWidth, .02, .15, .04),
        Resolution = Resolutions.Contains(Resolution) ? Resolution : 64,
        Axis = Enum.IsDefined(Axis) ? Axis : StandingWaveAxis.Z,
        CutPosition = Bound(CutPosition, .01, .99, .5)
    };

    internal Vector3 HalfExtents
    {
        get
        {
            double longest = Math.Max(LengthX, Math.Max(LengthY, LengthZ));
            return new((float)(LengthX / longest), (float)(LengthY / longest), (float)(LengthZ / longest));
        }
    }

    internal static double Bound(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
