using System.Numerics;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class StandingWavesMode() : Fractal3DMode(Fractal3DKind.StandingWaves, new(
    "Объёмные стоячие волны", "Стоячие волны · 3D",
    "Смешивайте моды прямоугольного резонатора и наблюдайте биения, узлы и сечения волнового поля.",
    "Fractal3DStandingWaves", "standing-waves"))
{
    public override bool UsesIterations => false;
    public override bool IsDensityVolume => true;
    public override bool NeedsWidePanel => true;
    public override string? ShapeHeader => "Резонатор и моды";
    public override int? GetStripPixelBudget(Fractal3DState state) =>
        24_000 * 64 / (state.StandingWave?.Resolution ?? 64);

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По знаку поля",
            [Fractal3DColoringMode.CrossTrap] = "По амплитуде |u|"
        };

    internal static StandingWaveSettings Settings(Fractal3DState state) => state.StandingWave ?? new();

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.StandingWave = new();
        state.CameraDistance = 5.65;
        state.CameraYaw = 32;
        state.CameraPitch = 19;
        state.FieldOfView = 35;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.Palette = new()
        {
            Name = "Знаки волны",
            Colors = [Color.FromRgb(62, 160, 255), Color.FromRgb(235, 225, 205), Color.FromRgb(255, 119, 57)]
        };
        state.BackgroundTop = Color.FromRgb(24, 35, 52);
        state.BackgroundBottom = Color.FromRgb(6, 12, 21);
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.SoftShadows = false;
        state.Ssaa = 1;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Куб · 12 пучностей", _ => { }),
        Preset("Куб · неподвижные узлы", s => s.StandingWave = new() { View = StandingWaveView.Nodes }),
        Preset("Близкие частоты · биения", s => s.StandingWave = new()
        {
            LengthX = 2.4, Modes = [new(2, 1, 2), new(1, 2, 2)], PlaybackSpeed = 1, Level = .21
        }),
        Preset("Суперпозиция · облако", s => s.StandingWave = new()
        {
            Modes = [new(3, 2, 1), new(1, 3, 2, .7, 35), new(2, 1, 3, .55, -30)],
            View = StandingWaveView.Cloud, Level = .1
        }),
        Preset("Сечение · интерференция", s => s.StandingWave = new()
        {
            Modes = [new(4, 2, 1), new(2, 4, 1, .85, 25)], View = StandingWaveView.Slice
        })
    ];

    public override void Validate(Fractal3DState state) => state.StandingWave = Settings(state).Validated();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        var s = Settings(state);
        Vector3 half = s.HalfExtents;
        int axis = (int)s.Axis;
        int cut = s.Cutaway || s.View == StandingWaveView.Slice ? axis + 1 : 0;
        return new(new Vector3(s.Resolution, (float)s.Level, (float)s.NodeWidth),
            new Vector4(cut, (float)(2 * s.CutPosition - 1) * half[axis], (int)s.View, 0), 0, new Vector4(half, 0));
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = Settings(state).Validated();
        string modes = string.Join(" + ", s.Modes.Select(m => $"({m.X}, {m.Y}, {m.Z})"));
        return $"{prefix} · {modes}\nРезонатор {s.LengthX:G4} × {s.LengthY:G4} × {s.LengthZ:G4} · t = {s.Time:G5}";
    }
}
