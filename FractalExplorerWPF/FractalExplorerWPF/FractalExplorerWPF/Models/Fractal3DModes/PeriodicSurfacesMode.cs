using System.Numerics;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class PeriodicSurfacesMode() : Fractal3DMode(Fractal3DKind.PeriodicSurfaces, new(
    "Гироид и периодические поверхности", "Периодические поверхности · 3D",
    "Гироид, Schwarz P и Schwarz D: тригонометрические приближения минимальных поверхностей. Переплетённые лабиринты, пористые стенки и каналы; период, уровень, растяжение по осям и срезы.",
    "Fractal3DPeriodicSurfaces", "periodic-surfaces"))
{
    public override bool UsesIterations => false;

    protected override bool HasOrbit => false;

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
        })
    ];

    public override void Validate(Fractal3DState state) => state.PeriodicSurface.Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        PeriodicSurfaceSettings s = state.PeriodicSurface;
        return new(
            new Vector3((float)s.Family, (float)s.Level, (float)s.Thickness),
            new Vector4((float)s.StretchX, (float)s.StretchY, (float)s.StretchZ, (float)s.Period),
            0,
            new Vector4(s.CutAxis, (float)s.CutPosition, (float)s.Region, 0));
    }

    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.PeriodicSurface;
        string family = s.Family switch { PeriodicSurfaceFamily.SchwarzP => "Schwarz P", PeriodicSurfaceFamily.SchwarzD => "Schwarz D", _ => "Гироид" };
        string region = s.Region switch { PeriodicSurfaceRegion.Surface => "поверхность", PeriodicSurfaceRegion.Positive => "лабиринт F ≥ уровня",
            PeriodicSurfaceRegion.Negative => "лабиринт F ≤ уровня", _ => $"стенки · толщина по F: {s.Thickness:G5}" };
        return $"{prefix} · {family} · {region}\n" +
            $"Период: {s.Period:G5} · Уровень: {s.Level:G5} · Растяжение: {s.StretchX:G4}; {s.StretchY:G4}; {s.StretchZ:G4}\n" +
            (s.CutAxis > 0 ? $"Срез {"XYZ"[s.CutAxis - 1]} ≤ {s.CutPosition:G5}\n" : "") +
            $"Окраска: {ColoringModeName(state.ColoringMode)} · Палитра: {state.ResolvePalette().Name}";
    }
}
