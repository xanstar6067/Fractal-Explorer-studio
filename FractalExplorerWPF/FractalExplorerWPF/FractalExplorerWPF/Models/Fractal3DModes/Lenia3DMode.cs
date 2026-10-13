using System.Numerics;
using FractalExplorerWPF.Infrastructure;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class Lenia3DMode() : GpuSimulation3DMode(Fractal3DKind.Lenia3D, new(
    "Lenia 3D · искусственная жизнь", "Lenia · 3D",
    "Непрерывный клеточный автомат: сферическое ядро чувствует соседей, а правило роста рождает пульсации, вращение и движение. Пять трёхмерных видов, поиск форм, срезы и точное продолжение.",
    "Fractal3DLenia", "lenia3d"))
{
    public override string? ShapeHeader => "Lenia · правило жизни";

    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string> { [Fractal3DColoringMode.OrbitTrap] = "По плотности клеток" };

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.ShadingStyle = Fractal3DShadingStyle.Glow;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.Palette = Fractal3DPalettes.Get("Северное сияние");
        state.EffectStrength = 1.4; state.CameraDistance = 1.05;
        state.BackgroundTop = Color.FromRgb(35, 49, 71); state.BackgroundBottom = Color.FromRgb(7, 11, 20);
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Вращающийся куб · Hexahedrome", s => s.Lenia = Lenia3DSeedLibrary.Settings(Lenia3DSeed.RotatingCube)),
        Preset("Пульсирующая сфера · Sphaerome", s => { s.Lenia = Lenia3DSeedLibrary.Settings(Lenia3DSeed.PulsatingSphere); s.CameraDistance = 1.25; s.Palette = Fractal3DPalettes.Get("Аметист"); }),
        Preset("Прыгающая пара · Diguttome", s => { s.Lenia = Lenia3DSeedLibrary.Settings(Lenia3DSeed.JumpingPair); s.CameraDistance = .75; s.Palette = Fractal3DPalettes.Get("Огонь"); }),
        Preset("Живой тор · Ovome", s => { s.Lenia = Lenia3DSeedLibrary.Settings(Lenia3DSeed.Torus); s.CameraDistance = 1.6; s.Palette = Fractal3DPalettes.Get("Лёд"); }),
        Preset("Вращающийся октаэдр · Octahedrome", s => { s.Lenia = Lenia3DSeedLibrary.Settings(Lenia3DSeed.RotatingOctahedron); s.Palette = Fractal3DPalettes.Get("Неон"); })
    ];

    public override void Validate(Fractal3DState state) => state.Lenia.Validate();

    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        Lenia3DSettings s = state.Lenia;
        return new(new Vector3(s.Size, (float)s.Threshold, 0),
            new Vector4(s.CutAxis, (float)s.CutPosition, 0, 0), 0, BoxInversion(state));
    }
}
