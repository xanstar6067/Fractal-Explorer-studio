using System.Numerics;
using System.Text.Json.Serialization;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models;

/// <summary>Полное состояние окна трёхмерного фрактала: форма, камера, качество, свет и окраска.</summary>
public sealed class Fractal3DState
{
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string FractalType { get; set; } = "Fractal3D";
    public Fractal3DKind Kind { get; set; }

    // ---- форма ----
    public int Iterations { get; set; } = 8;
    public double Power { get; set; } = 8;
    public BurningShip3DFormula BurningShipFormula { get; set; }
    public double Bailout { get; set; } = 4;
    public double JuliaCX { get; set; }
    public double JuliaCY { get; set; }
    public double JuliaCZ { get; set; }
    public double JuliaCW { get; set; }
    /// <summary>Вторая константа 3D Phoenix: множитель предыдущего кватерниона.</summary>
    public double PhoenixMemoryX { get; set; }
    public double PhoenixMemoryY { get; set; }
    public double PhoenixMemoryZ { get; set; }
    /// <summary>Степень при C₁: 0 даёт классическую формулу, 1 — линейный член.</summary>
    public int PhoenixSecondaryPower { get; set; }
    /// <summary>Временная минисфера редактора C; в сохранения фрактала не попадает.</summary>
    [JsonIgnore]
    public Vector4 PickerMarker { get; set; }
    public double QuaternionSlice { get; set; }
    public double BoxScale { get; set; } = 2;
    public double BoxMinRadius { get; set; } = 0.5;
    public double BoxFoldingLimit { get; set; } = 1;
    public BoxInversionShape BoxInversionShape { get; set; }
    public double BoxInversionStretch { get; set; } = 1;
    public double BoxInversionPower { get; set; } = 4;
    public double HybridMix { get; set; }
    public int HybridBulbSteps { get; set; } = 1;
    public int HybridBoxSteps { get; set; } = 1;
    public Hybrid3DOrder HybridOrder { get; set; }
    public double SierpinskiScale { get; set; } = 2;
    public double CubeThickness { get; set; } = 1;
    public List<Ifs3DTransform> IfsTransforms { get; set; } = [];
    public Attractor3DSettings Attractor { get; set; } = Attractor3DSystems.Default(Attractor3DSystem.Lorenz);

    public Flame3DSettings Flame { get; set; } = new();
    public Buddhabrot4DSettings Buddhabrot { get; set; } = new();
    public Kobayashi3DSettings Kobayashi { get; set; } = new();
    public Excitable3DSettings Excitable { get; set; } = new();
    public GrayScott3DSettings GrayScott { get; set; } = new();
    public Turing3DSettings Turing { get; set; } = new();
    public CahnHilliard3DSettings CahnHilliard { get; set; } = new();
    public Physarum3DSettings Physarum { get; set; } = new();
    public Lenia3DSettings Lenia { get; set; } = new();

    public Lyapunov3DSettings Lyapunov { get; set; } = new();

    public KifsSettings Kifs { get; set; } = new();

    public Dla3DSettings Dla { get; set; } = new();
    public Lichtenberg3DSettings Lichtenberg { get; set; } = new();

    public TerrainSettings Terrain { get; set; } = new();
    public LSystem3DSettings LSystem { get; set; } = new();
    public AbcFlowSettings AbcFlow { get; set; } = new();
    public HopfSettings Hopf { get; set; } = new();
    public PeriodicSurfaceSettings PeriodicSurface { get; set; } = new();
    public ImplicitSurfaceSettings ImplicitSurface { get; set; } = new();
    public HyperbolicHoneycombSettings HyperbolicHoneycomb { get; set; } = new();

    // ---- камера ----
    public double CameraYaw { get; set; } = 35;
    public double CameraPitch { get; set; } = 18;
    public double CameraDistance { get; set; } = 2.8;
    public double TargetX { get; set; }
    public double TargetY { get; set; }
    public double TargetZ { get; set; }
    public double FieldOfView { get; set; } = 55;

    /// <summary>
    /// Крен камеры вокруг оси взгляда, градусы; положительный поворачивает картинку по часовой
    /// стрелке. Сохранения, сделанные до появления крена, его не содержат и читаются с нулём —
    /// ровно так, как они и выглядели.
    /// </summary>
    public double CameraRoll { get; set; }

    // ---- навигация ----
    public Fractal3DNavigationMode NavigationMode { get; set; } = Fractal3DNavigationMode.Cad;
    public double GameMovementSpeed { get; set; } = 1;
    public double GameMouseSensitivity { get; set; } = 1;
    public Fractal3DMotionQuality MotionQuality { get; set; } = Fractal3DMotionQuality.NoShadows;
    public Fractal3DMotionResolution MotionResolution { get; set; } = Fractal3DMotionResolution.Adaptive;
    public bool RotationInertia { get; set; } = true;
    public bool AutoRotate { get; set; }
    public double AutoRotateSpeed { get; set; } = 18;

    // ---- качество ----
    public int MaxSteps { get; set; } = 160;
    public double Detail { get; set; } = 1;
    public double MaxDistance { get; set; } = 24;
    public int Ssaa { get; set; } = 1;

    // ---- свет ----
    public double LightYaw { get; set; } = 40;
    public double LightPitch { get; set; } = 45;
    public double Ambient { get; set; } = 0.2;
    public double Specular { get; set; } = 0.3;
    public bool SoftShadows { get; set; } = true;
    public double ShadowSharpness { get; set; } = 16;
    public bool AmbientOcclusion { get; set; } = true;
    public double AoStrength { get; set; } = 0.7;

    // ---- окраска ----
    public Fractal3DColoringMode ColoringMode { get; set; } = Fractal3DColoringMode.OrbitTrap;
    public Fractal3DShadingStyle ShadingStyle { get; set; } = Fractal3DShadingStyle.Classic;

    /// <summary>Сила выбранного шейдера: ореол, плотность, ступени контура или доля отражения.</summary>
    public double EffectStrength { get; set; } = 1;

    public Color SurfaceColor { get; set; } = Fractal3DEnvironment.SurfaceColor;

    /// <summary>
    /// Палитра вида. У сохранений, сделанных до появления палитр, её нет: там цвета лежат в
    /// <see cref="ColorA"/> и <see cref="ColorB"/>, и <see cref="ResolvePalette"/> собирает
    /// палитру из них, чтобы старый файл выглядел ровно так же.
    /// </summary>
    public Fractal3DPalette? Palette { get; set; }

    /// <summary>Устаревшее поле: первый цвет градиента до появления палитр.</summary>
    public Color ColorA { get; set; } = Color.FromRgb(26, 58, 122);

    /// <summary>Устаревшее поле: второй цвет градиента до появления палитр.</summary>
    public Color ColorB { get; set; } = Color.FromRgb(255, 186, 92);

    public double ColorScale { get; set; } = 1;
    public double ColorOffset { get; set; }
    public Fractal3DColorRepeat ColorRepeat { get; set; } = Fractal3DColorRepeat.Mirror;

    // ---- окружение ----
    public Color BackgroundTop { get; set; } = Fractal3DEnvironment.BackgroundTop;
    public Color BackgroundBottom { get; set; } = Fractal3DEnvironment.BackgroundBottom;
    public Color LightColor { get; set; } = Fractal3DEnvironment.LightColor;

    /// <summary>Насколько цвет неба подмешивается в фоновый свет: 0 — лампа и фон независимы.</summary>
    public double SkyLightMix { get; set; }

    /// <summary>Палитра вида или собранная из устаревших цветов, если файл старше палитр.</summary>
    public Fractal3DPalette ResolvePalette() =>
        Palette ?? Fractal3DPalette.FromPair("Из сохранения", ColorA, ColorB);

    public Fractal3DState Clone()
    {
        var clone = (Fractal3DState)MemberwiseClone();
        clone.Palette = Palette?.Clone();
        clone.Kifs = Kifs?.Clone() ?? new();
        clone.Dla = Dla?.Normalized() ?? new();
        clone.Lichtenberg = Lichtenberg is null ? new() : Lichtenberg with { };
        clone.LSystem = LSystem is null ? new() : LSystem with { };
        clone.AbcFlow = AbcFlow is null ? new() : AbcFlow with { };
        clone.Hopf = Hopf?.Copy() ?? new();
        clone.PeriodicSurface = PeriodicSurface is null ? new() : PeriodicSurface with { };
        clone.ImplicitSurface = ImplicitSurface is null ? new() : ImplicitSurface with { };
        clone.HyperbolicHoneycomb = HyperbolicHoneycomb is null ? new() : HyperbolicHoneycomb with { };
        clone.Flame = Flame?.Clone() ?? new();
        clone.Buddhabrot = Buddhabrot is null ? new() : Buddhabrot with { };
        clone.Kobayashi = Kobayashi is null ? new() : Kobayashi with { };
        clone.Excitable = Excitable is null ? new() : Excitable with { };
        clone.GrayScott = GrayScott is null ? new() : GrayScott with { };
        clone.Turing = Turing is null ? new() : Turing with { };
        clone.Lenia = Lenia is null ? new() : Lenia with { };
        clone.Lyapunov = Lyapunov is null ? new() : Lyapunov with { };
        clone.CahnHilliard = CahnHilliard is null ? new() : CahnHilliard with { };
        clone.IfsTransforms = IfsTransforms.Select(transform => transform.Clone()).ToList();
        clone.Attractor = Attractor?.Clone() ?? Attractor3DSystems.Default(Attractor3DSystem.Lorenz);
        return clone;
    }
}
