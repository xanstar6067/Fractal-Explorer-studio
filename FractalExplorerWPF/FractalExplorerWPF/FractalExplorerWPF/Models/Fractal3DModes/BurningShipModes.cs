using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

/// <summary>Трёхмерный «Горящий корабль» и его Жюлиа: целая степень и выбор формулы отражения.</summary>
internal abstract class BurningShipFamilyMode(Fractal3DKind kind, Fractal3DDefinition definition) : Fractal3DMode(kind, definition)
{
    public override bool UsesPower => true;

    public override bool UsesBailout => true;

    public override Fractal3DField PowerField { get; } = new("Степень n (целая, 2–16)", "Степень", 2, 16, IsInteger: true);

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Power = 2;
        state.Iterations = 16;
        state.Bailout = 4;
        state.CameraDistance = 4.2;
        state.CameraYaw = 30;
        state.CameraPitch = 24;
        state.MaxSteps = 220;
        state.Palette = Fractal3DPalettes.Get("Раскалённый металл");
        state.ColorScale = 1.4;
    }

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) =>
        new(new Vector3((float)state.Power, (float)state.BurningShipFormula, 0f), JuliaConstant(state), 0, BoxInversion(state));
}

internal sealed class BurningShipMode() : BurningShipFamilyMode(Fractal3DKind.BurningShip, new(
    "Горящий корабль 3D", "Горящий корабль 3D",
    "Трёхмерный «Горящий корабль»: степень, кватернионная или сферическая формула и отражение двух либо трёх координат меняют форму. Кватернионная степень 2 на срезе z = 0 совпадает с плоским фракталом.",
    "Fractal3DBurningShip", "burning-ship-3d"))
{
    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Горящий корабль — общий вид", _ => { }),
        Preset("Кватернион — степень 3", s =>
        {
            s.Power = 3;
            s.Iterations = 20;
        }),
        Preset("Три отражения", s => s.BurningShipFormula = BurningShip3DFormula.QuaternionFullFold),
        Preset("Сферический корабль", s =>
        {
            s.BurningShipFormula = BurningShip3DFormula.Spherical;
            s.Power = 4;
            s.Iterations = 12;
        }),
        Preset("Сферические ледяные гребни", s =>
        {
            s.BurningShipFormula = BurningShip3DFormula.SphericalFullFold;
            s.CameraYaw = 105;
            s.CameraPitch = 32;
            s.Power = 5;
            s.Iterations = 12;
            s.Palette = Fractal3DPalettes.Get("Лёд");
            s.ColoringMode = Fractal3DColoringMode.IterationIndex;
            s.ColorScale = 0.8;
        })
    ];

    protected override string ShapeSummary(Fractal3DState state) =>
        $"Степень: {state.Power:G6} · {Fractal3DCatalog.BurningShipFormulaName(state.BurningShipFormula)}";
}

internal sealed class BurningShipJuliaMode() : BurningShipFamilyMode(Fractal3DKind.BurningShipJulia, new(
    "Горящий корабль 3D — Жюлиа", "Горящий корабль 3D — Жюлиа",
    "Julia-вариант трёхмерного «Горящего корабля»: та же выбранная формула и степень, но постоянная C фиксирована. Её можно выбрать на соответствующей 3D-карте.",
    "Fractal3DBurningShipJulia", "burning-ship-julia-3d"))
{
    public override bool UsesJuliaConstant => true;

    protected override void ConfigureDefault(Fractal3DState state)
    {
        base.ConfigureDefault(state);
        state.JuliaCX = -0.35;
        state.JuliaCY = -0.05;
        state.JuliaCZ = 0.15;
        state.CameraDistance = 3.3;
        state.Iterations = 20;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Жюлиа горящего корабля", _ => { }),
        Preset("Три отражения", s => s.BurningShipFormula = BurningShip3DFormula.QuaternionFullFold),
        Preset("Константа из 2D-галереи", s =>
        {
            s.JuliaCX = -1.7551867961883;
            s.JuliaCY = 0.01068;
            s.JuliaCZ = 0;
            s.CameraDistance = 4.2;
        }),
        Preset("Тёмные грани", s =>
        {
            s.JuliaCX = 0.25;
            s.JuliaCY = -0.45;
            s.JuliaCZ = 0.15;
            s.CameraYaw = 55;
        }),
        Preset("Сферическая Жюлиа", s =>
        {
            s.BurningShipFormula = BurningShip3DFormula.Spherical;
            s.Power = 3;
            s.JuliaCX = -0.3;
            s.JuliaCY = 0.25;
            s.JuliaCZ = 0.15;
        }),
        Preset("Сферическая Жюлиа — три отражения", s =>
        {
            s.BurningShipFormula = BurningShip3DFormula.SphericalFullFold;
            s.Power = 4;
            s.JuliaCX = -0.3;
            s.JuliaCY = 0.25;
            s.JuliaCZ = 0.15;
            s.Palette = Fractal3DPalettes.Get("Лёд");
        })
    ];

    protected override string ShapeSummary(Fractal3DState state) =>
        $"Степень: {state.Power:G6} · {Fractal3DCatalog.BurningShipFormulaName(state.BurningShipFormula)} · " +
        $"C: {state.JuliaCX:G5}; {state.JuliaCY:G5}; {state.JuliaCZ:G5}";
}
