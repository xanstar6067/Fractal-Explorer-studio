using System.Numerics;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class TerrainMode() : Fractal3DMode(Fractal3DKind.Terrain, new(
    "Фрактальные ландшафты", "Фрактальные ландшафты",
    "Горы, хребты и поверхности: fBm, гребневый и гибридный шум. Шероховатость, масштабы деталей и экспорт 16-битной карты высот.",
    "Fractal3DTerrain", "terrain"))
{
    public override bool UsesIterations => false;

    protected override bool HasOrbit => false;

    public override int? StripPixelBudget => 30_000;

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.CameraDistance = 9;
        state.CameraPitch = 35;
        state.TargetY = 0.6;
        state.MaxDistance = 30;
        state.ColoringMode = Fractal3DColoringMode.Height;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.Specular = 0.08;
        state.Palette = new Fractal3DPalette
        {
            Name = "Горный рельеф",
            Colors = [Color.FromRgb(30, 65, 47), Color.FromRgb(88, 116, 62),
                Color.FromRgb(142, 125, 87), Color.FromRgb(161, 156, 148), Color.FromRgb(246, 246, 239)]
        };
        state.BackgroundTop = Color.FromRgb(85, 130, 177);
        state.BackgroundBottom = Color.FromRgb(187, 208, 218);
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Горные хребты", _ => { }),
        Preset("Мягкие холмы — fBm", s => s.Terrain = s.Terrain with
            { Type = TerrainKind.Fbm, Roughness = 0.35, Height = 2 }),
        Preset("Альпийские вершины", s => s.Terrain = s.Terrain with
            { Seed = 137, Height = 3, Roughness = 0.65, Scale = 4 }),
        Preset("Гибридный рельеф", s => s.Terrain = s.Terrain with
            { Type = TerrainKind.Hybrid, Seed = 91, Roughness = 0.65, Height = 3 })
    ];

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state) => new(
        new Vector3((float)state.Terrain.Size, (float)state.Terrain.Height, state.Terrain.Resolution),
        JuliaConstant(state), 0, BoxInversion(state));

    public override string Describe(Fractal3DState state, string prefix)
    {
        TerrainSettings terrain = state.Terrain;
        string type = terrain.Type switch { TerrainKind.Fbm => "Холмы", TerrainKind.Hybrid => "Гибридный рельеф", _ => "Хребты" };
        return $"{prefix} · {type} · Начальное число: {terrain.Seed}\n" +
               $"Шероховатость: {terrain.Roughness:G4} · Уровни: {terrain.Octaves} · Отношение масштабов: {terrain.Lacunarity:G4}\n" +
               $"Масштаб деталей: {terrain.Scale:G4} · Участок: {terrain.Size:G4} × {terrain.Size:G4} · Высота: {terrain.Height:G4}\n" +
               $"Карта высот: {terrain.Resolution} × {terrain.Resolution} · Окраска: {ColoringModeName(state.ColoringMode)}";
    }
}
