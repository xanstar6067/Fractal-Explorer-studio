using System.Numerics;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class LSystem3DMode() : Fractal3DMode(Fractal3DKind.LSystem3D, new(
    "Пространственные L-системы", "L-системы · 3D", "Правила превращаются в деревья, растения и трубчатые кривые. Повороты вокруг трёх локальных осей, ветвление и постепенное построение.",
    "Fractal3DLSystem", "lsystem3d"))
{
    public override bool UsesIterations => false;

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string> { [Fractal3DColoringMode.OrbitTrap] = "По L-системе" };

    public override int? StripPixelBudget => 12_000;

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.CameraDistance = 3.8;
        state.CameraPitch = 12;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.Palette = Fractal3DPalette.FromPair("Кора и побеги", Color.FromRgb(130, 77, 37), Color.FromRgb(107, 221, 85));
        state.SoftShadows = false;
        state.Ambient = .35;
        state.Ssaa = 2;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() => LSystem3DPresets.All.Select(p => Preset(p.Name, s =>
    {
        s.LSystem = p.Settings with { };
        if (p.Settings.ColorSource == LSystem3DColorSource.DrawingOrder)
            s.Palette = Fractal3DPalettes.Get("Спектр");
    })).ToArray();

    /// <summary>Число отрезков в ShapeA.z рендерер дописывает сам, когда построит геометрию.</summary>
    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) => new(
        new Vector3((float)state.LSystem.Growth, (float)state.LSystem.ColorSource, 0),
        JuliaConstant(state), 0, BoxInversion(state));
}
