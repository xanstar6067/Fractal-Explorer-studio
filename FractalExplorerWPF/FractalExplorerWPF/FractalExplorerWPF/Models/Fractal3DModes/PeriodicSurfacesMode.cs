using System.Numerics;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class PeriodicSurfacesMode() : Fractal3DMode(Fractal3DKind.PeriodicSurfaces, new(
    "Гироид и периодические поверхности", "Периодические поверхности · 3D",
    "Гироид, Schwarz P и Schwarz D и их смеси: переплетённые лабиринты, пористые стенки переменной толщины и каналы; период, уровень, растяжение по осям и срезы.",
    "Fractal3DPeriodicSurfaces", "periodic-surfaces"))
{
    public override bool UsesIterations => false;

    protected override bool HasOrbit => false;

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string> { [Fractal3DColoringMode.OrbitTrap] = "По лабиринту" };

    public override string? ShapeHeader => "Поверхность и лабиринты";

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.CameraDistance = 4.4;
        state.CameraYaw = 32; state.CameraPitch = 24;
        state.MaxSteps = 384;
        state.Detail = .35;
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.SurfaceColor = Color.FromRgb(93, 183, 189);
        state.SoftShadows = false;
        state.Ssaa = 2;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Гироид · переплетённые лабиринты", _ => { }),
        Preset("Schwarz P · кубические каналы", s => s.PeriodicSurface = new() { Family = PeriodicSurfaceFamily.SchwarzP, Thickness = .45 }),
        Preset("Schwarz D · алмазная сеть", s => s.PeriodicSurface = new() { Family = PeriodicSurfaceFamily.SchwarzD, Thickness = .2 }),
        Preset("Гироид · срез", s => s.PeriodicSurface = new() { CutAxis = 3, CutPosition = .1 }),
        Preset("Гироид · вытянутые тоннели", s => s.PeriodicSurface = new() { StretchY = 1.7, Period = 1.4, Thickness = .4 }),
        Preset("Schwarz P · поверхность без толщины", s => s.PeriodicSurface = new() { Family = PeriodicSurfaceFamily.SchwarzP, Region = PeriodicSurfaceRegion.Surface, Period = 2 }),
        Preset("Гироид · один лабиринт", s => s.PeriodicSurface = new() { Region = PeriodicSurfaceRegion.Positive, Level = .2, CutAxis = 3 }),
        Preset("Гироид · взгляд вдоль тоннеля", s => {
            s.PeriodicSurface = new() { Period = 2 };
            s.CameraDistance = 4.4; s.CameraYaw = 0; s.CameraPitch = 0;
            s.TargetX = .5; s.TargetY = .5; s.TargetZ = 0;
        }),
        Preset("Гироид → Schwarz P · смесь", s => s.PeriodicSurface = new() { Blend = .5, Thickness = .4, Period = 1.5 }),
        Preset("Schwarz P → D · смесь", s => s.PeriodicSurface = new() {
            Family = PeriodicSurfaceFamily.SchwarzP, BlendFamily = PeriodicSurfaceFamily.SchwarzD,
            Blend = .55, Thickness = .25, Period = 1.6, CutAxis = 3, CutPosition = .3 }),
        Preset("Гироид · градиент толщины", s => s.PeriodicSurface = new() {
            Thickness = .65, ThicknessAxis = 2, ThicknessGradient = .85, CutAxis = 3, CutPosition = .3 }),
        Preset("Два лабиринта · медь и патина", s => {
            s.PeriodicSurface = new() { Region = PeriodicSurfaceRegion.Both, Thickness = .7, Period = 1.5, CutAxis = 3, CutPosition = .15 };
            s.ColoringMode = Fractal3DColoringMode.OrbitTrap;
            s.Palette = Fractal3DPalettes.Get("Медь и патина");
            s.ColorRepeat = Fractal3DColorRepeat.Clamp;
        })
    ];

    public override void Validate(Fractal3DState state) => state.PeriodicSurface.Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        PeriodicSurfaceSettings s = state.PeriodicSurface;
        return new(
            // Exact integer packing: 2 bits per family, then the thickness axis.
            new Vector3((int)s.Family + 4 * (int)s.BlendFamily + 16 * s.ThicknessAxis, (float)s.Level, (float)s.Thickness),
            new Vector4((float)s.StretchX, (float)s.StretchY, (float)s.StretchZ, (float)s.Period),
            (float)s.Blend,
            new Vector4(s.CutAxis, (float)s.CutPosition, (float)s.Region, (float)s.ThicknessGradient));
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.PeriodicSurface;
        string family = s.Family switch { PeriodicSurfaceFamily.SchwarzP => "Schwarz P", PeriodicSurfaceFamily.SchwarzD => "Schwarz D", _ => "Гироид" };
        string region = s.Region switch { PeriodicSurfaceRegion.Surface => "поверхность", PeriodicSurfaceRegion.Positive => "лабиринт F ≥ уровня",
            PeriodicSurfaceRegion.Both => $"два лабиринта · зазор по F: {s.Thickness:G5}",
            PeriodicSurfaceRegion.Negative => "лабиринт F ≤ уровня", _ => $"стенки · толщина по F: {s.Thickness:G5}" };
        return $"{prefix} · {family} · {region}\n" +
            (s.Blend > 0 ? $"Смешивание с {s.BlendFamily}: {s.Blend:P1}\n" : "") +
            (s.ThicknessAxis > 0 && s.Region is PeriodicSurfaceRegion.Sheet or PeriodicSurfaceRegion.Both
                ? $"Градиент толщины {"XYZ"[s.ThicknessAxis - 1]}: {s.ThicknessGradient:G5}\n" : "") +
            $"Период: {s.Period:G5} · Уровень: {s.Level:G5} · Растяжение: {s.StretchX:G4}; {s.StretchY:G4}; {s.StretchZ:G4}\n" +
            (s.CutAxis > 0 ? $"Срез {"XYZ"[s.CutAxis - 1]} ≤ {s.CutPosition:G5}\n" : "") +
            $"Окраска: {ColoringModeName(state.ColoringMode)} · Палитра: {state.ResolvePalette().Name}";
    }
}
