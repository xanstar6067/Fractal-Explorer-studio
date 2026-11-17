using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Polytope4DMode() : Fractal3DMode(Fractal3DKind.Polytope4D, new(
    "Многогранники 4D и сечения", "Лаборатория многогранников · 4D → 3D",
    "Шесть правильных выпуклых 4D-многогранников: симплекс, тессеракт, ортоплекс, 24-, 120- и 600-ячейник. Каркасные проекции, трёхмерные сечения, шесть плоскостей вращения и анимация прохода среза.",
    "Fractal3DPolytope4D", "polytope4d"))
{
    public override bool UsesIterations => false;
    public override bool HasDedicatedShapeEditor => true;
    public override bool NeedsWidePanel => true;
    public override int? StripPixelBudget => 12_000;
    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По W каркаса / ячейкам сечения",
            [Fractal3DColoringMode.IterationIndex] = "По номеру ячейки / ребра"
        };

    protected override void ConfigureDefault(Fractal3DState s)
    {
        s.CameraDistance = 3.4; s.CameraPitch = 20; s.CameraYaw = 28;
        s.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        s.ColorRepeat = Fractal3DColorRepeat.Clamp;
        s.Palette = Fractal3DPalettes.Get("Спектр");
        s.SoftShadows = false; s.AmbientOcclusion = false;
        s.ShadingStyle = Fractal3DShadingStyle.Studio;
        s.MaxSteps = 256; s.Ssaa = 2;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Тессеракт · четвёртое измерение", _ => { }),
        Preset("Куб · центральное сечение тессеракта", s => s.Polytope4D = new() { View = Polytope4DView.Section, XW = 0, YW = 0 }),
        Preset("Тессеракт · наклонное сечение", s => s.Polytope4D = new() { View = Polytope4DView.Section, XW = 35, YW = 30, ZW = 25 }),
        Preset("Симплекс · сечение", s => s.Polytope4D = new() { Body = Polytope4DKind.Simplex, View = Polytope4DView.Section, XW = 0, YW = 0 }),
        Preset("16-ячейник · октаэдр в срезе", s => s.Polytope4D = new() { Body = Polytope4DKind.Orthoplex, View = Polytope4DView.Section, XW = 0, YW = 0 }),
        Preset("24-ячейник · самодвойственная геометрия", s => s.Polytope4D = new() { Body = Polytope4DKind.Cell24 }),
        Preset("120-ячейник · додекаэдрические ячейки", s => s.Polytope4D = new() { Body = Polytope4DKind.Cell120, Thickness = .006 }),
        Preset("120-ячейник · сечение", s => s.Polytope4D = new() { Body = Polytope4DKind.Cell120, View = Polytope4DView.Section, Offset = .15 }),
        Preset("600-ячейник · сечение", s => s.Polytope4D = new() { Body = Polytope4DKind.Cell600, View = Polytope4DView.Section, Offset = .1, Thickness = .006 })
    ];
    public override void Validate(Fractal3DState s) => s.Polytope4D.Validate();
    // Perspective of a unit sphere is bounded by d/sqrt(d²-1); d >= 1.25 => radius <= 5/3.
    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState s) =>
        new(new Vector3(1.8f, 0, 0), Vector4.Zero, 0, Vector4.Zero);
    public override string Describe(Fractal3DState s, string prefix) =>
        $"{prefix} · {Polytope4DSettings.Name(s.Polytope4D.Body)}\n" +
        $"{Polytope4DSettings.ViewName(s.Polytope4D.View)} · t = {s.Polytope4D.Offset:G5}\n" +
        $"XW {s.Polytope4D.XW:G4}° · YW {s.Polytope4D.YW:G4}° · ZW {s.Polytope4D.ZW:G4}°";
}
