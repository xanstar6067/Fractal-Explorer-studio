using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models;

/// <summary>Трёхмерные фракталы, которые обслуживает <c>Views/Fractal3DWindow</c>.</summary>
public enum Fractal3DKind
{
    Mandelbulb,
    Juliabulb,
    Mandelbox,
    MengerSponge,
    SierpinskiTetrahedron,
    QuaternionJulia
}

/// <summary>
/// Источник цвета поверхности. Пока это заготовка под будущую систему окрасок и эффектов:
/// цвета задаются двумя опорными оттенками, а не палитрой с произвольным числом ключей.
/// </summary>
public enum Fractal3DColoringMode
{
    Material,
    Normal,
    OrbitTrap,
    Depth
}

public sealed record Fractal3DDefinition(
    string Title,
    string PanelTitle,
    string Description,
    string SaveCategory,
    string ExportPrefix);

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
    public double Bailout { get; set; } = 4;
    public double JuliaCX { get; set; }
    public double JuliaCY { get; set; }
    public double JuliaCZ { get; set; }
    public double JuliaCW { get; set; }
    public double QuaternionSlice { get; set; }
    public double BoxScale { get; set; } = 2;
    public double BoxMinRadius { get; set; } = 0.5;
    public double BoxFoldingLimit { get; set; } = 1;
    public double SierpinskiScale { get; set; } = 2;

    // ---- камера ----
    public double CameraYaw { get; set; } = 35;
    public double CameraPitch { get; set; } = 18;
    public double CameraDistance { get; set; } = 2.8;
    public double TargetX { get; set; }
    public double TargetY { get; set; }
    public double TargetZ { get; set; }
    public double FieldOfView { get; set; } = 55;

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
    public Color SurfaceColor { get; set; } = Color.FromRgb(228, 206, 180);
    public Color ColorA { get; set; } = Color.FromRgb(26, 58, 122);
    public Color ColorB { get; set; } = Color.FromRgb(255, 186, 92);
    public double ColorScale { get; set; } = 1;
    public double ColorOffset { get; set; }
    public Color BackgroundTop { get; set; } = Color.FromRgb(18, 22, 34);
    public Color BackgroundBottom { get; set; } = Color.FromRgb(6, 7, 11);

    public Fractal3DState Clone() => (Fractal3DState)MemberwiseClone();
}

public static class Fractal3DCatalog
{
    public const string LaunchPrefix = "Fractal3D:";

    public static string LaunchKey(Fractal3DKind kind) => LaunchPrefix + kind;

    public static bool TryParseLaunchKey(string? launchKey, out Fractal3DKind kind)
    {
        kind = default;
        return launchKey?.StartsWith(LaunchPrefix, StringComparison.Ordinal) == true &&
               Enum.TryParse(launchKey[LaunchPrefix.Length..], out kind);
    }

    public static bool UsesPower(Fractal3DKind kind) =>
        kind is Fractal3DKind.Mandelbulb or Fractal3DKind.Juliabulb;

    public static bool UsesJuliaConstant(Fractal3DKind kind) =>
        kind is Fractal3DKind.Juliabulb or Fractal3DKind.QuaternionJulia;

    public static Fractal3DDefinition GetDefinition(Fractal3DKind kind) => kind switch
    {
        Fractal3DKind.Juliabulb => new(
            "Жюлиабульб", "Жюлиабульб",
            "Julia-вариант Мандельбульба: вместо точки пространства в итерацию подставляется фиксированная константа C.",
            "Fractal3DJuliabulb", "juliabulb"),
        Fractal3DKind.Mandelbox => new(
            "Мандельбокс", "Мандельбокс",
            "Чередование сворачивания по кубу и по сфере с масштабированием даёт архитектурную структуру с галереями и арками.",
            "Fractal3DMandelbox", "mandelbox"),
        Fractal3DKind.MengerSponge => new(
            "Губка Менгера", "Губка Менгера",
            "Куб, из которого рекурсивно вырезаются крестообразные тоннели; классический самоподобный объект размерности log20/log3.",
            "Fractal3DMengerSponge", "menger-sponge"),
        Fractal3DKind.SierpinskiTetrahedron => new(
            "Тетраэдр Серпинского", "Тетраэдр Серпинского",
            "Трёхмерный аналог треугольника Серпинского: складывание пространства к ближайшей вершине тетраэдра.",
            "Fractal3DSierpinski", "sierpinski-tetrahedron"),
        Fractal3DKind.QuaternionJulia => new(
            "Кватернионное Жюлиа", "Кватернионное Жюлиа",
            "Множество Жюлиа в алгебре кватернионов: трёхмерный срез четырёхмерного множества с настраиваемой координатой среза.",
            "Fractal3DQuaternionJulia", "quaternion-julia"),
        _ => new(
            "Мандельбульб", "Мандельбульб",
            "Трёхмерное обобщение множества Мандельброта через сферические координаты: z → zⁿ + c с произвольной степенью n.",
            "Fractal3DMandelbulb", "mandelbulb")
    };

    public static string ColoringModeName(Fractal3DColoringMode mode) => mode switch
    {
        Fractal3DColoringMode.Material => "Материал",
        Fractal3DColoringMode.Normal => "По нормали",
        Fractal3DColoringMode.Depth => "По глубине",
        _ => "Орбитальная ловушка"
    };

    /// <summary>Состояние по умолчанию; оно же — превью пункта каталога.</summary>
    public static Fractal3DState CreateDefaultState(Fractal3DKind kind)
    {
        var state = new Fractal3DState { Kind = kind, SaveName = GetDefinition(kind).Title };
        switch (kind)
        {
            case Fractal3DKind.Juliabulb:
                state.Power = 8;
                state.Iterations = 9;
                state.JuliaCX = -0.2;
                state.JuliaCY = 0.6;
                state.JuliaCZ = 0.2;
                state.CameraDistance = 2.9;
                state.ColorScale = 2.5;
                break;
            case Fractal3DKind.Mandelbox:
                state.Iterations = 12;
                state.BoxScale = 2;
                state.BoxMinRadius = 0.7;
                state.BoxFoldingLimit = 1;
                state.Bailout = 100;
                state.CameraDistance = 24;
                state.CameraPitch = 22;
                state.MaxDistance = 120;
                state.MaxSteps = 200;
                break;
            case Fractal3DKind.MengerSponge:
                state.Iterations = 5;
                state.CameraDistance = 3.4;
                state.CameraYaw = 28;
                state.CameraPitch = 24;
                state.ColoringMode = Fractal3DColoringMode.Depth;
                break;
            case Fractal3DKind.SierpinskiTetrahedron:
                state.Iterations = 13;
                state.SierpinskiScale = 2;
                state.CameraDistance = 3.2;
                state.CameraYaw = 30;
                state.CameraPitch = 16;
                state.MaxDistance = 30;
                break;
            case Fractal3DKind.QuaternionJulia:
                state.Iterations = 11;
                state.Bailout = 16;
                state.JuliaCX = -0.0909;
                state.JuliaCY = 0.2727;
                state.JuliaCZ = 0.6818;
                state.JuliaCW = -0.2727;
                state.CameraDistance = 3;
                break;
            default:
                state.Power = 8;
                state.Iterations = 9;
                state.CameraDistance = 2.8;
                break;
        }
        return state;
    }

    /// <summary>Готовые виды режима; они же — точки интереса менеджера сохранений.</summary>
    public static IReadOnlyList<Fractal3DState> GetPresets(Fractal3DKind kind) => kind switch
    {
        Fractal3DKind.Mandelbulb =>
        [
            Preset(kind, "Степень 8 — классический вид", _ => { }),
            Preset(kind, "Степень 8 — северный полюс", s =>
            {
                s.CameraPitch = 68;
                s.CameraYaw = 12;
                s.CameraDistance = 2.4;
            }),
            Preset(kind, "Степень 3 — гладкие лепестки", s =>
            {
                s.Power = 3;
                s.Iterations = 12;
                s.CameraDistance = 3;
            }),
            Preset(kind, "Степень 16 — игольчатая корона", s =>
            {
                s.Power = 16;
                s.Iterations = 8;
                s.ColorA = Color.FromRgb(28, 74, 128);
                s.ColorB = Color.FromRgb(150, 235, 255);
            }),
            Preset(kind, "Крупный план складки", s =>
            {
                s.CameraDistance = 1.35;
                s.CameraYaw = 118;
                s.CameraPitch = 6;
                s.TargetX = 0.32;
                s.TargetY = 0.12;
                s.Detail = 0.5;
                s.MaxSteps = 256;
                s.Iterations = 12;
            })
        ],
        Fractal3DKind.Juliabulb =>
        [
            Preset(kind, "C = (−0.2; 0.6; 0.2)", _ => { }),
            Preset(kind, "C = (0.4; 0.0; 0.0) — симметричный", s =>
            {
                s.JuliaCX = 0.4;
                s.JuliaCY = 0;
                s.JuliaCZ = 0;
            }),
            Preset(kind, "C = (−0.5; 0.2; 0.1) — ветвистый", s =>
            {
                s.JuliaCX = -0.5;
                s.JuliaCY = 0.2;
                s.JuliaCZ = 0.1;
                s.Iterations = 11;
                s.CameraDistance = 3.1;
            }),
            Preset(kind, "Степень 4, C = (0.3; 0.3; 0.3)", s =>
            {
                s.Power = 4;
                s.JuliaCX = 0.3;
                s.JuliaCY = 0.3;
                s.JuliaCZ = 0.3;
                s.Iterations = 12;
            })
        ],
        Fractal3DKind.Mandelbox =>
        [
            Preset(kind, "Масштаб 2 — классическая коробка", _ => { }),
            Preset(kind, "Масштаб 3 — плотная решётка", s =>
            {
                s.BoxScale = 3;
                s.CameraDistance = 14;
                s.Iterations = 14;
            }),
            Preset(kind, "Масштаб −1.5 — вывернутый", s =>
            {
                s.BoxScale = -1.5;
                s.CameraDistance = 10;
                s.Iterations = 14;
                s.ColorA = Color.FromRgb(20, 50, 40);
                s.ColorB = Color.FromRgb(240, 220, 140);
            }),
            Preset(kind, "Внутри галереи", s =>
            {
                s.CameraDistance = 6;
                s.CameraYaw = 62;
                s.CameraPitch = 4;
                s.TargetX = 1.6;
                s.TargetZ = 0.8;
                s.MaxSteps = 256;
                s.Detail = 0.6;
            })
        ],
        Fractal3DKind.MengerSponge =>
        [
            Preset(kind, "5 уровней — общий вид", _ => { }),
            Preset(kind, "3 уровня — крупные тоннели", s =>
            {
                s.Iterations = 3;
                s.CameraDistance = 3.6;
            }),
            Preset(kind, "Взгляд вдоль тоннеля", s =>
            {
                s.CameraYaw = 0;
                s.CameraPitch = 0;
                s.CameraDistance = 2.2;
                s.MaxSteps = 256;
            }),
            Preset(kind, "Угловой крупный план", s =>
            {
                s.CameraDistance = 1.5;
                s.CameraYaw = 45;
                s.CameraPitch = 35;
                s.TargetX = 0.55;
                s.TargetY = 0.55;
                s.TargetZ = 0.55;
                s.Iterations = 6;
                s.Detail = 0.6;
            })
        ],
        Fractal3DKind.SierpinskiTetrahedron =>
        [
            Preset(kind, "Классический тетраэдр", _ => { }),
            Preset(kind, "Масштаб 1.8 — сросшийся", s =>
            {
                s.SierpinskiScale = 1.8;
                s.CameraDistance = 4.4;
            }),
            Preset(kind, "Вид вдоль ребра", s =>
            {
                s.CameraYaw = 45;
                s.CameraPitch = 0;
                s.CameraDistance = 3.4;
            }),
            Preset(kind, "Крупный план вершины", s =>
            {
                s.CameraDistance = 1.4;
                s.TargetX = 0.7;
                s.TargetY = 0.7;
                s.TargetZ = 0.7;
                s.Detail = 0.6;
                s.MaxSteps = 220;
            })
        ],
        _ =>
        [
            Preset(kind, "C = (−0.09; 0.27; 0.68; −0.27)", _ => { }),
            Preset(kind, "C = (−1; 0.2; 0; 0)", s =>
            {
                s.JuliaCX = -1;
                s.JuliaCY = 0.2;
                s.JuliaCZ = 0;
                s.JuliaCW = 0;
                s.CameraDistance = 3;
            }),
            Preset(kind, "Срез w = 0.35", s =>
            {
                s.QuaternionSlice = 0.35;
                s.CameraDistance = 2.8;
            }),
            Preset(kind, "C = (−0.2; 0.6; 0.2; 0) — ветвистый", s =>
            {
                s.JuliaCX = -0.2;
                s.JuliaCY = 0.6;
                s.JuliaCZ = 0.2;
                s.JuliaCW = 0;
                s.Iterations = 12;
                s.CameraDistance = 3.2;
                s.ColorA = Color.FromRgb(40, 16, 60);
                s.ColorB = Color.FromRgb(255, 160, 210);
            })
        ]
    };

    private static Fractal3DState Preset(Fractal3DKind kind, string name, Action<Fractal3DState> configure)
    {
        Fractal3DState state = CreateDefaultState(kind);
        state.SaveName = name;
        configure(state);
        return state;
    }
}
