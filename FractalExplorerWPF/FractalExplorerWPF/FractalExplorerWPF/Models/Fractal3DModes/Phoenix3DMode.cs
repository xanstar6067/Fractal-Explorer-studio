using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Phoenix3DMode() : Fractal3DMode(Fractal3DKind.Phoenix, new(
    "Феникс 3D", "Феникс 3D",
    "Кватернионное продолжение Phoenix: qₙ₊₁ = qₙᵖ + C₁qₙˢ + C₂qₙ₋₁. Предыдущая итерация хранится отдельно; срез z = 0 при вещественных C₁ и C₂ совпадает с классическим 2D Phoenix.",
    "Fractal3DPhoenix", "phoenix-3d"))
{
    public override bool UsesPower => true;

    public override bool UsesJuliaConstant => true;

    public override bool UsesBailout => true;

    public override Fractal3DField PowerField { get; } = new("Основная степень p (целая, 2–6)", "Степень", 2, 6, IsInteger: true);

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Power = 2;
        state.Iterations = 18;
        state.Bailout = 4;
        state.JuliaCX = 0.56667;
        state.PhoenixMemoryX = -0.5;
        state.CameraDistance = 3.2;
        state.CameraYaw = 32;
        state.CameraPitch = 22;
        state.MaxSteps = 240;
        state.Palette = Fractal3DPalettes.Get("Раскалённый металл");
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Классический Феникс · C₂ = −0,5", _ => { }),
        Preset("Косая память", s =>
        {
            s.JuliaCX = 0.35;
            s.JuliaCY = -0.01;
            s.PhoenixMemoryX = -0.62;
            s.PhoenixMemoryZ = 0.12;
        }),
        Preset("Линейный член C₁q", s =>
        {
            s.JuliaCX = 0.24;
            s.JuliaCY = 0.08;
            s.PhoenixMemoryX = -0.46;
            s.PhoenixSecondaryPower = 1;
        })
    ];

    /// <summary>Вторая константа C₂ занимает слот инверсии Мандельбокса: у Феникса её нет.</summary>
    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) => new(
        new Vector3((float)state.Power, state.PhoenixSecondaryPower, 0f),
        JuliaConstant(state), 0,
        new Vector4((float)state.PhoenixMemoryX, (float)state.PhoenixMemoryY, (float)state.PhoenixMemoryZ, 0));

    protected override string ShapeSummary(Fractal3DState state) =>
        $"p={state.Power:G4}, s={state.PhoenixSecondaryPower} · C₁: {state.JuliaCX:G5}; {state.JuliaCY:G5}; {state.JuliaCZ:G5} · " +
        $"C₂: {state.PhoenixMemoryX:G5}; {state.PhoenixMemoryY:G5}; {state.PhoenixMemoryZ:G5}";
}
