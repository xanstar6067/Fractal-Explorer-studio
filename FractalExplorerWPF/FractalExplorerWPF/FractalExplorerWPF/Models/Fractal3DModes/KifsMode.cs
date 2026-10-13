using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class KifsMode() : Fractal3DMode(Fractal3DKind.Kifs, new(
    "Калейдоскопические фракталы — KIFS", "Калейдоскопические фракталы",
    "Зеркальные плоскости складывают пространство в кристаллы, звёзды и рекурсивные соборы. Симметрия, поворот и смещение на каждой итерации меняют архитектуру; карта смещения и рандомайзер работают вживую.",
    "Fractal3DKifs", "kifs"))
{
    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Iterations = 10;
        state.CameraDistance = 4.5;
        state.MaxSteps = 240;
        state.Palette = Fractal3DPalettes.Get("Лёд");
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Рекурсивный кристалл · тетраэдр", _ => { }),
        Preset("Гранёный собор · октаэдр", s => { s.Kifs.Symmetry = KifsSymmetry.Octahedral; s.Kifs.OffsetY = .65; s.Kifs.OffsetZ = .35; s.Kifs.RotationZ = 12; s.Kifs.Scale = 2.4; }),
        Preset("Зеркальный лабиринт · куб", s => { s.Kifs.Symmetry = KifsSymmetry.Cubic; s.Kifs.Seed = KifsSeed.Cube; s.Kifs.Scale = 2.6; s.Kifs.RotationY = 18; s.Kifs.Radius = .8; }),
        Preset("Шестилучевая звезда", s => { s.Kifs.Symmetry = KifsSymmetry.Dihedral; s.Kifs.OffsetY = 0; s.Kifs.OffsetZ = .35; s.Kifs.Scale = 2.2; s.Kifs.RotationZ = 8; }),
        Preset("Спиральный кристалл", s => { s.Kifs.Symmetry = KifsSymmetry.Octahedral; s.Kifs.OffsetY = .65; s.Kifs.OffsetZ = .35; s.Kifs.RotationX = 8; s.Kifs.RotationY = 15; s.Kifs.RotationZ = 5; s.Kifs.Scale = 2.2; }),
        Preset("Восьмилучевая корона", s => { s.Kifs.Symmetry = KifsSymmetry.Dihedral; s.Kifs.Sectors = 8; s.Kifs.OffsetY = 0; s.Kifs.OffsetZ = .5; s.Kifs.RotationX = 8; s.Kifs.Scale = 2.5; })
    ];

    /// <summary>Повороты зеркал уходят в слот инверсии Мандельбокса в радианах, смещение — в слот константы C.</summary>
    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        KifsSettings kifs = (state.Kifs ?? new()).Normalized();
        return new(
            new Vector3((float)kifs.Scale, (float)kifs.Symmetry, kifs.Sectors),
            new Vector4((float)kifs.OffsetX, (float)kifs.OffsetY, (float)kifs.OffsetZ, (float)kifs.Radius),
            0,
            new Vector4((float)(kifs.RotationX * Math.PI / 180), (float)(kifs.RotationY * Math.PI / 180),
                (float)(kifs.RotationZ * Math.PI / 180), (float)kifs.Seed));
    }

    protected override string ShapeSummary(Fractal3DState state) =>
        $"{KifsSettings.SymmetryName(state.Kifs.Symmetry)} · Масштаб: {state.Kifs.Scale:G4} · Поворот: {state.Kifs.RotationX:G4}; {state.Kifs.RotationY:G4}; {state.Kifs.RotationZ:G4}° · Смещение: {state.Kifs.OffsetX:G4}; {state.Kifs.OffsetY:G4}; {state.Kifs.OffsetZ:G4}";
}
