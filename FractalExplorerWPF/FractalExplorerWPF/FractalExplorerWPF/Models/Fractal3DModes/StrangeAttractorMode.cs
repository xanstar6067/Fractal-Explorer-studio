namespace FractalExplorerWPF.Models.Fractal3DModes;

internal sealed class StrangeAttractorMode() : Fractal3DMode(Fractal3DKind.StrangeAttractor, new(
    "Объёмные странные аттракторы", "Объёмные странные аттракторы",
    "Лоренц, Рёсслер, Томас, Халворсен, Айзава и Дадрас. Траектории интегрируются отдельно, их плотность показывается общей 3D-камерой и объёмным шейдером.",
    "Fractal3DAttractors", "strange-attractor-3d"))
{
    public override bool IsDensityVolume => true;

    protected override bool HasOrbit => false;

    public override Fractal3DField IterationsField { get; } =
        new("Точки траектории (50 000–5 000 000)", "Итерации", 50_000, 5_000_000, IsInteger: true);

    protected override void ConfigureDefault(Fractal3DState state)
    {
        state.Attractor = Attractor3DSystems.Default(Attractor3DSystem.Lorenz);
        state.Iterations = 1_000_000;
        state.CameraDistance = 2.8;
        state.CameraYaw = 0;
        state.CameraPitch = 12;
        state.MaxDistance = 12;
        state.ColoringMode = Fractal3DColoringMode.Height;
        state.ColorRepeat = Fractal3DColorRepeat.Clamp;
        state.ShadingStyle = Fractal3DShadingStyle.Glow;
        state.EffectStrength = 1.3;
        state.Palette = Fractal3DPalettes.Get("Лёд");
    }

    public override IReadOnlyList<Fractal3DState> CreatePresets() =>
    [
        Preset("Лоренц · двойное крыло", _ => { }),
        Preset("Лоренц · высокий нагрев", s => { s.Attractor.B = 35; s.CameraYaw = 14; s.Palette = Fractal3DPalettes.Get("Раскалённый металл"); }),
        Preset("Рёсслер · складка", s => { s.Attractor = Attractor3DSystems.Default(Attractor3DSystem.Rossler); s.CameraPitch = 50; s.Palette = Fractal3DPalettes.Get("Медь и патина"); }),
        Preset("Томас · тройной узел", s => { s.Attractor = Attractor3DSystems.Default(Attractor3DSystem.Thomas); s.CameraYaw = 45; s.CameraPitch = 28; s.Palette = Fractal3DPalettes.Get("Неон"); }),
        Preset("Халворсен · три рукава", s => { s.Attractor = Attractor3DSystems.Default(Attractor3DSystem.Halvorsen); s.CameraYaw = 35; s.Palette = Fractal3DPalettes.Get("Аметист"); }),
        Preset("Айзава · воронка", s => { s.Attractor = Attractor3DSystems.Default(Attractor3DSystem.Aizawa); s.CameraPitch = 38; s.Palette = Fractal3DPalettes.Get("Северное сияние"); }),
        Preset("Дадрас · раскрытые крылья", s => { s.Attractor = Attractor3DSystems.Default(Attractor3DSystem.Dadras); s.CameraYaw = 22; s.Palette = Fractal3DPalettes.Get("Закат"); }),
        Preset("Чуа · двойная спираль", s => { s.Attractor = Attractor3DSystems.Default(Attractor3DSystem.Chua); s.CameraYaw = 0; s.CameraPitch = 0; s.CameraDistance = 2.2; s.Palette = Fractal3DPalettes.Get("Северное сияние"); }),
        Preset("Чуа · одиночная спираль", s => { s.Attractor = Attractor3DSystems.Default(Attractor3DSystem.Chua); s.Attractor.B = 32; s.Attractor.StartX = 1.5; s.CameraYaw = 0; s.CameraPitch = 45; s.CameraDistance = 2.2; s.Palette = Fractal3DPalettes.Get("Аметист"); })
    ];

    public override string Describe(Fractal3DState state, string prefix) =>
        $"{prefix} · {Attractor3DSystems.Name(state.Attractor.System)} · " +
        $"{state.Iterations:N0} точек · шаг {state.Attractor.TimeStep:G5}\n" +
        $"Камера: азимут {state.CameraYaw:F1}°, наклон {state.CameraPitch:F1}°, " +
        $"крен {state.CameraRoll:F1}°, расстояние {state.CameraDistance:G6}";
}
