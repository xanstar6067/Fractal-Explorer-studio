using System.Text.Json.Serialization;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Models;

public enum Physarum3DSeed { Cloud, Shell, Torus, TwinStars }

/// <summary>Lossless immutable checkpoint: trail and all positions/headings, measured in grid cells.</summary>
[JsonConverter(typeof(Physarum3DFieldConverter))]
public sealed class Physarum3DField
{
    public int Size { get; }
    public long Step { get; }
    public int AgentCount => Agents.Length / 8;
    internal float[] Values { get; }
    internal float[] Agents { get; }
    public ReadOnlySpan<float> Trail => Values;
    public ReadOnlySpan<float> AgentState => Agents;

    public Physarum3DField(int size, long step, ReadOnlySpan<float> trail, ReadOnlySpan<float> agents)
    {
        if (!Physarum3DSettings.IsSupportedSize(size) || step < 0 || trail.Length != size * size * size ||
            agents.Length % 8 != 0 || agents.Length / 8 is < 1024 or > 262144)
            throw new ArgumentException("Некорректная контрольная точка Physarum 3D.");
        foreach (float v in trail) if (!float.IsFinite(v) || v < 0 || v > 1000)
            throw new ArgumentException("Повреждён след Physarum 3D.");
        for (int i = 0; i < agents.Length; i += 8)
        {
            for (int k = 0; k < 8; k++) if (!float.IsFinite(agents[i+k]))
                throw new ArgumentException("Повреждены агенты Physarum 3D.");
            for (int k = 0; k < 3; k++) if (agents[i+k] < 0 || agents[i+k] >= size)
                throw new ArgumentException("Агент за пределами области.");
            double norm = agents[i+4]*agents[i+4] + agents[i+5]*agents[i+5] + agents[i+6]*agents[i+6];
            if (Math.Abs(norm-1) > .002) throw new ArgumentException("Некорректное направление агента.");
        }
        Size = size; Step = step; Values = trail.ToArray(); Agents = agents.ToArray();
    }
}

public sealed record Physarum3DSettings
{
    public int Size { get; init; } = 128;
    public int AgentCount { get; init; } = 16384;
    public Physarum3DSeed SeedShape { get; init; }
    public int Seed { get; init; } = 42;
    public double SensorDistance { get; init; } = 2;
    public double SensorAngle { get; init; } = 40;
    public double TurnAngle { get; init; } = 22;
    public double Speed { get; init; } = .8;
    public double Deposit { get; init; } = 2;
    public double Diffusion { get; init; } = .12;
    public double Decay { get; init; } = .025;
    public int WarmupSteps { get; init; } = 80;
    public int StepsPerFrame { get; init; } = 4;
    public double Threshold { get; init; } = .35;
    public double Exposure { get; init; } = .12;
    public int CutAxis { get; init; }
    public double CutPosition { get; init; } = .5;
    public Physarum3DField? Field { get; init; }
    [JsonIgnore] public Physarum3DVolume? Live { get; init; }

    public static bool IsSupportedSize(int n) => n is 48 or 64 or 96 or 128;
    public void Validate()
    {
        static bool In(double x, double a, double b) => double.IsFinite(x) && x >= a && x <= b;
        if (!IsSupportedSize(Size) || AgentCount is < 1024 or > 262144 || !Enum.IsDefined(SeedShape) ||
            !In(SensorDistance, 1, 16) || !In(SensorAngle, 5, 85) || !In(TurnAngle, 1, 70) ||
            !In(Speed, .1, 2) || !In(Deposit, .1, 8) || !In(Diffusion, 0, 1) || !In(Decay, .001, .2) ||
            !In(Threshold, .01, .9) || !In(Exposure, .05, 4) || WarmupSteps is < 0 or > 2000 ||
            StepsPerFrame is < 1 or > 32 || CutAxis is < 0 or > 3 || !In(CutPosition, -1, 1) ||
            Field is not null && (Field.Size != Size || Field.AgentCount != AgentCount) || Live is not null && Live.Size != Size)
            throw new ArgumentException("Проверьте параметры Physarum 3D: сетка 48–128, от 1024 до 262144 агентов.");
    }
    public bool SameEvolution(Physarum3DSettings s) => Size == s.Size && AgentCount == s.AgentCount &&
        SeedShape == s.SeedShape && Seed == s.Seed && SensorDistance == s.SensorDistance &&
        SensorAngle == s.SensorAngle && TurnAngle == s.TurnAngle && Speed == s.Speed && Deposit == s.Deposit &&
        Diffusion == s.Diffusion && Decay == s.Decay && WarmupSteps == s.WarmupSteps && ReferenceEquals(Field, s.Field);
}
