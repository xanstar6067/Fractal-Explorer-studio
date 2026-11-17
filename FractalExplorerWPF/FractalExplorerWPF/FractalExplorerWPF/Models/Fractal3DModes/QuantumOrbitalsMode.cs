using System.Numerics;

namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class QuantumOrbitalsMode() : Fractal3DMode(Fractal3DKind.QuantumOrbitals, new(
    "Квантовые орбитали", "Квантовые орбитали · 3D",
    "Орбитали водорода: квантовые числа n/l/m, вещественный и комплексный базисы, фаза, плотность, внутренние узлы и суперпозиции двух состояний.",
    "Fractal3DQuantumOrbitals", "quantum-orbitals"))
{
    public override bool UsesIterations => false;
    public override bool IsDensityVolume => true;
    public override int VolumePreparationProgress => 60;
    public override string? ShapeHeader => "Волновая функция";
    public override int? StripPixelBudget => 12_000;
    protected override IReadOnlyDictionary<Fractal3DColoringMode, string> ReassignedColoring { get; } =
        new Dictionary<Fractal3DColoringMode, string>
        {
            [Fractal3DColoringMode.OrbitTrap] = "По фазе ψ",
            [Fractal3DColoringMode.CrossTrap] = "По плотности |ψ|²",
            [Fractal3DColoringMode.IterationIndex] = "По расстоянию от ядра"
        };
    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.CameraDistance = 2.6; state.CameraYaw = 32; state.CameraPitch = 55;
        state.ColoringMode = Fractal3DColoringMode.OrbitTrap;
        state.ColorRepeat = Fractal3DColorRepeat.Cycle;
        state.Palette = Fractal3DPalettes.Get("Спектр");
        state.ShadingStyle = Fractal3DShadingStyle.Studio;
        state.SoftShadows = false; state.Ssaa = 2;
    }
    public override IReadOnlyList<Fractal3DState> CreatePresets() => [
        Preset("3d z² · лепестки и кольцо", _ => { }),
        Preset("1s · облако вероятности", s => { s.QuantumOrbital = new() { N=1,L=0,M=0,Extent=4,Level=.03 }; s.ShadingStyle=Fractal3DShadingStyle.Density; }),
        Preset("2p x · два знака волны", s => s.QuantumOrbital = new() { N=2,L=1,M=1,Extent=12 }),
        Preset("3d xy · четыре лепестка", s => s.QuantumOrbital = new() { M=-2 }),
        Preset("4f · шесть лепестков", s => s.QuantumOrbital = new() { N=4,L=3,M=3,Extent=38 }),
        Preset("4s · радиальные узлы в срезе", s => s.QuantumOrbital = new() { N=4,L=0,M=0,Extent=36,Level=.0001,CutAxis=1 }),
        Preset("3d · комплексная фаза", s => s.QuantumOrbital = new() { M=2,Basis=QuantumOrbitalBasis.Complex }),
        Preset("2s + 2p · интерференция", s => s.QuantumOrbital = new() { N=2,L=0,M=0,N2=2,L2=1,M2=0,Superposition=true,Extent=10,Level=.005 }),
        Preset("3d · смесь в квадратуре", s => s.QuantumOrbital = new() { Superposition=true,Phase=90 })
    ];
    public override void Validate(Fractal3DState state) => state.QuantumOrbital.Validate();
    public override Fractal3DShapeConstants ShapeConstants(Fractal3DState state)
    {
        var s = state.QuantumOrbital;
        return new(new Vector3(s.Size, (float)s.Level, 0), new Vector4(s.CutAxis, (float)s.CutPosition, 0, 0), 0, Vector4.Zero);
    }
    public override string Describe(Fractal3DState state, string prefix)
    {
        var s = state.QuantumOrbital;
        return $"{prefix} · {QuantumOrbitalSettings.Label(s.N, s.L, s.M)} · {s.Basis}\n" +
            (s.Superposition ? $"Смесь с {QuantumOrbitalSettings.Label(s.N2, s.L2, s.M2)} · доля {s.Mix:G4} · фаза {s.Phase:G5}°\n" : "") +
            $"Область ±{s.Extent:G5} a₀ · {s.Size}³ · уровень {s.Level:G5}\nОкраска: {ColoringModeName(state.ColoringMode)}";
    }
}
