using System.Numerics;
using System.Text.Json.Serialization;
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
    QuaternionJulia,
    ApollonianPacking,
    Ifs3D,
    Vicsek,
    CantorDust,
    Terrain,
    BurningShip,
    BurningShipJulia,
    BulbBoxHybrid
}

public enum Hybrid3DOrder
{
    BulbFirst,
    BoxFirst
}

/// <summary>Способ продолжить отражённое комплексное возведение в степень до трёх координат.</summary>
public enum BurningShip3DFormula
{
    Quaternion,
    QuaternionFullFold,
    Spherical,
    SphericalFullFold
}

/// <summary>Чем жертвует черновой кадр, пока камера движется.</summary>
public enum Fractal3DMotionQuality
{
    /// <summary>Свет и тени считаются как заданы; меняется только разрешение чернового кадра.</summary>
    Full,

    /// <summary>Мягкие тени гасятся: самый дорогой эффект уходит первым.</summary>
    NoShadows,

    /// <summary>Ни теней, ни затенения складок, шаги луча урезаны: максимальная отзывчивость.</summary>
    Draft
}

/// <summary>
/// Разрешение живого кадра, пока камера движется. Значения сериализуются числами, поэтому новые
/// варианты только дописываются в конец.
/// </summary>
public enum Fractal3DMotionResolution
{
    /// <summary>Подбирается по времени предыдущего кадра, чтобы держать около 30 к/с.</summary>
    Adaptive,

    /// <summary>Всегда во весь холст: движение ничем не отличается от готового кадра.</summary>
    Full,

    /// <summary>Три четверти холста по каждой стороне.</summary>
    ThreeQuarters,

    /// <summary>Половина холста по каждой стороне.</summary>
    Half
}

/// <summary>Способ управления камерой трёхмерного фрактала.</summary>
public enum Fractal3DNavigationMode
{
    Cad,
    Game
}

/// <summary>
/// Что именно окрашивается палитрой: источник числа, которое шейдер превращает в позицию на
/// градиенте. Значения сериализуются числами, поэтому новые источники только дописываются в конец.
/// <see cref="Material"/> и <see cref="Normal"/> палитрой не пользуются: первый берёт один цвет
/// материала, второй показывает саму нормаль.
/// </summary>
public enum Fractal3DColoringMode
{
    Material = 0,
    Normal = 1,

    /// <summary>Минимальный радиус орбиты: классическая сферическая ловушка.</summary>
    OrbitTrap = 2,

    /// <summary>Пройденное лучом расстояние.</summary>
    Depth = 3,

    /// <summary>Ловушка по осям: минимум расстояния орбиты до координатных плоскостей.</summary>
    CrossTrap = 4,

    /// <summary>
    /// Номер последней итерации орбиты: у вылетающих форм — итерация вылета, то самое число,
    /// которое красит плоские фракталы; у губки и тетраэдра — итерация ближайшего подхода.
    /// </summary>
    IterationIndex = 5,

    /// <summary>Радиус орбиты на выходе — аналог скорости убегания плоских фракталов.</summary>
    Escape = 6,

    /// <summary>Высота точки поверхности вдоль оси Y.</summary>
    Height = 7,

    /// <summary>Затенение складок: окраска идёт по тому, насколько точка закрыта соседями.</summary>
    Occlusion = 8,

    /// <summary>Угол между нормалью и лучом: края фигуры красятся иначе, чем обращённое к нам.</summary>
    Fresnel = 9,

    /// <summary>Число шагов луча до попадания: карта «сложности» силуэта.</summary>
    Steps = 10
}

/// <summary>Что палитра делает со значениями вне отрезка [0; 1].</summary>
public enum Fractal3DColorRepeat
{
    /// <summary>Зажать: всё меньше нуля — первый цвет, всё больше единицы — последний.</summary>
    Clamp = 0,

    /// <summary>Повторять по кругу: последний цвет переходит в первый без шва.</summary>
    Cycle = 1,

    /// <summary>Отражать: градиент проходится туда и обратно (так выглядит окраска по умолчанию).</summary>
    Mirror = 2
}

/// <summary>
/// Встроенные шейдеры освещения. Меняют не форму, а то, как посчитанная поверхность превращается
/// в цвет; <see cref="Classic"/> — то, что рисовалось до появления набора.
/// </summary>
public enum Fractal3DShadingStyle
{
    /// <summary>Рассеянный свет, блик, мягкая тень, затенение складок и дымка вдаль.</summary>
    Classic = 0,

    /// <summary>Глина: мягкий обёрнутый свет без бликов и усиленное затенение складок.</summary>
    Clay = 1,

    /// <summary>Металл: отражение неба и жёсткий блик поверх приглушённого рассеянного света.</summary>
    Metal = 2,

    /// <summary>Свечение: близость луча к поверхности копится по дороге и светится ореолом.</summary>
    Glow = 3,

    /// <summary>Плотность: луч проходит фигуру насквозь, яркость набирается вдоль пути.</summary>
    Density = 4,

    /// <summary>Студийный свет: освещение берётся от нормали в осях камеры и не зависит от лампы.</summary>
    Studio = 5,

    /// <summary>Контурный: свет ступенями и тёмная обводка по силуэту.</summary>
    Toon = 6,

    /// <summary>Просвечивание: тонкие места подсвечиваются светом, пришедшим с изнанки.</summary>
    Translucent = 7
}

/// <summary>
/// Палитра трёхмерного фрактала: цвета опорных точек градиента и, по желанию, окружение —
/// фон, цвет лампы и цвет материала. Окружение применяется только при
/// <see cref="OverridesEnvironment"/>, поэтому встроенные палитры не трогают свет и фон.
/// </summary>
public sealed class Fractal3DPalette
{
    /// <summary>Сколько опорных цветов шейдер умеет принять за один кадр.</summary>
    public const int MaxColors = 16;

    public string Name { get; set; } = "Новая палитра";

    public List<Color> Colors { get; set; } = [Color.FromRgb(26, 58, 122), Color.FromRgb(255, 186, 92)];

    /// <summary>Плавный переход между опорными цветами или резкие полосы.</summary>
    public bool IsGradient { get; set; } = true;

    /// <summary>Степень, в которую возводится позиция на градиенте: смещает насыщенность полос.</summary>
    public double Gamma { get; set; } = 1;

    public bool IsBuiltIn { get; set; }

    /// <summary>Задаёт ли палитра заодно фон, свет и материал.</summary>
    public bool OverridesEnvironment { get; set; }

    public Color BackgroundTop { get; set; } = Fractal3DEnvironment.BackgroundTop;
    public Color BackgroundBottom { get; set; } = Fractal3DEnvironment.BackgroundBottom;
    public Color LightColor { get; set; } = Fractal3DEnvironment.LightColor;
    public Color SurfaceColor { get; set; } = Fractal3DEnvironment.SurfaceColor;

    public static Fractal3DPalette FromPair(string name, Color first, Color second) => new()
    {
        Name = name,
        Colors = [first, second]
    };

    public Fractal3DPalette Clone(string? name = null) => new()
    {
        Name = name ?? Name,
        Colors = [.. Colors],
        IsGradient = IsGradient,
        Gamma = Gamma,
        OverridesEnvironment = OverridesEnvironment,
        BackgroundTop = BackgroundTop,
        BackgroundBottom = BackgroundBottom,
        LightColor = LightColor,
        SurfaceColor = SurfaceColor
    };

    public override string ToString() => Name;
}

/// <summary>Свет и фон по умолчанию: их же несут встроенные палитры.</summary>
public static class Fractal3DEnvironment
{
    public static Color BackgroundTop => Color.FromRgb(18, 22, 34);
    public static Color BackgroundBottom => Color.FromRgb(6, 7, 11);
    public static Color LightColor => Color.FromRgb(255, 255, 255);
    public static Color SurfaceColor => Color.FromRgb(228, 206, 180);
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
    public BurningShip3DFormula BurningShipFormula { get; set; }
    public double Bailout { get; set; } = 4;
    public double JuliaCX { get; set; }
    public double JuliaCY { get; set; }
    public double JuliaCZ { get; set; }
    public double JuliaCW { get; set; }
    /// <summary>Временная минисфера редактора C; в сохранения фрактала не попадает.</summary>
    [JsonIgnore]
    public Vector4 PickerMarker { get; set; }
    public double QuaternionSlice { get; set; }
    public double BoxScale { get; set; } = 2;
    public double BoxMinRadius { get; set; } = 0.5;
    public double BoxFoldingLimit { get; set; } = 1;
    public double HybridMix { get; set; }
    public int HybridBulbSteps { get; set; } = 1;
    public int HybridBoxSteps { get; set; } = 1;
    public Hybrid3DOrder HybridOrder { get; set; }
    public double SierpinskiScale { get; set; } = 2;
    public double CubeThickness { get; set; } = 1;
    public List<Ifs3DTransform> IfsTransforms { get; set; } = [];

    public TerrainSettings Terrain { get; set; } = new();

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

    /// <summary>Сила выбранного шейдера: ореол свечения, плотность, число ступеней контура.</summary>
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
        clone.IfsTransforms = IfsTransforms.Select(transform => transform.Clone()).ToList();
        return clone;
    }
}

/// <summary>
/// Встроенные палитры трёхмерных фракталов. Окружение у них — то же, что было до появления
/// палитр, и <see cref="Fractal3DPalette.OverridesEnvironment"/> выключен: встроенная палитра
/// меняет цвет фигуры, но не трогает свет и фон.
/// </summary>
public static class Fractal3DPalettes
{
    /// <summary>Палитра по умолчанию: те же два цвета, что рисовались до появления палитр.</summary>
    public const string ClassicName = "Классическая";

    public static IReadOnlyList<Fractal3DPalette> All { get; } =
    [
        BuiltIn(ClassicName, [Rgb(26, 58, 122), Rgb(255, 186, 92)]),
        BuiltIn("Раскалённый металл",
            [Rgb(8, 4, 2), Rgb(92, 18, 10), Rgb(214, 74, 18), Rgb(255, 176, 48), Rgb(255, 246, 214)]),
        BuiltIn("Виридис",
            [Rgb(68, 1, 84), Rgb(59, 82, 139), Rgb(33, 145, 140), Rgb(94, 201, 98), Rgb(253, 231, 37)]),
        BuiltIn("Северное сияние",
            [Rgb(3, 7, 26), Rgb(10, 58, 80), Rgb(24, 158, 140), Rgb(126, 232, 151), Rgb(236, 255, 214)]),
        BuiltIn("Аметист",
            [Rgb(12, 4, 24), Rgb(70, 16, 110), Rgb(160, 40, 180), Rgb(236, 110, 200), Rgb(255, 225, 250)]),
        BuiltIn("Медь и патина",
            [Rgb(16, 28, 26), Rgb(22, 92, 86), Rgb(86, 190, 168), Rgb(224, 158, 86), Rgb(120, 44, 18)]),
        BuiltIn("Спектр",
            [Rgb(255, 64, 64), Rgb(255, 208, 48), Rgb(72, 220, 92), Rgb(52, 176, 255), Rgb(128, 88, 255)]),
        BuiltIn("Лёд",
            [Rgb(6, 16, 38), Rgb(26, 86, 150), Rgb(120, 200, 238), Rgb(214, 244, 255), Rgb(255, 255, 255)]),
        BuiltIn("Сепия",
            [Rgb(24, 16, 10), Rgb(86, 58, 34), Rgb(168, 128, 82), Rgb(228, 200, 158), Rgb(255, 246, 228)]),
        BuiltIn("Неон",
            [Rgb(4, 2, 10), Rgb(236, 32, 180), Rgb(60, 244, 236), Rgb(255, 255, 255)]),
        BuiltIn("Мрамор",
            [Rgb(22, 22, 26), Rgb(96, 98, 104), Rgb(186, 188, 194), Rgb(238, 238, 242), Rgb(255, 255, 255)]),
        BuiltIn("Закат",
            [Rgb(18, 10, 40), Rgb(104, 28, 92), Rgb(220, 74, 88), Rgb(255, 158, 74), Rgb(255, 232, 166)])
    ];

    /// <summary>Копия встроенной палитры: состояние владеет своей палитрой и правит её свободно.</summary>
    public static Fractal3DPalette Get(string name) =>
        (All.FirstOrDefault(palette => palette.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
         ?? All[0]).Clone();

    public static Fractal3DPalette Classic() => Get(ClassicName);

    private static Fractal3DPalette BuiltIn(string name, List<Color> colors) => new()
    {
        Name = name,
        Colors = colors,
        IsBuiltIn = true
    };

    private static Color Rgb(byte red, byte green, byte blue) => Color.FromRgb(red, green, blue);
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
        kind is Fractal3DKind.Mandelbulb or Fractal3DKind.Juliabulb or
            Fractal3DKind.BurningShip or Fractal3DKind.BurningShipJulia or Fractal3DKind.BulbBoxHybrid;

    public static bool UsesJuliaConstant(Fractal3DKind kind) =>
        kind is Fractal3DKind.Juliabulb or Fractal3DKind.BurningShipJulia or Fractal3DKind.QuaternionJulia;

    public static bool IsBurningShip(Fractal3DKind kind) =>
        kind is Fractal3DKind.BurningShip or Fractal3DKind.BurningShipJulia;

    public static string BurningShipFormulaName(BurningShip3DFormula formula) => formula switch
    {
        BurningShip3DFormula.QuaternionFullFold => "Кватернион · три отражения",
        BurningShip3DFormula.Spherical => "Сферическая степень · два отражения",
        BurningShip3DFormula.SphericalFullFold => "Сферическая степень · три отражения",
        _ => "Кватернион · два отражения"
    };

    public static Fractal3DDefinition GetDefinition(Fractal3DKind kind) => kind switch
    {
        Fractal3DKind.BulbBoxHybrid => new(
            "Гибрид Мандельбульб × Мандельбокс", "Гибрид Мандельбульб × Мандельбокс",
            "Бульб и бокс чередуются заданными сериями итераций. Доля смешивания подмешивает вторую операцию к каждой серии; порядок, длины серий и параметры обеих форм меняют геометрию.",
            "Fractal3DBulbBoxHybrid", "bulb-box-hybrid"),
        Fractal3DKind.BurningShip => new(
            "Горящий корабль 3D", "Горящий корабль 3D",
            "Трёхмерный «Горящий корабль»: степень, кватернионная или сферическая формула и отражение двух либо трёх координат меняют форму. Кватернионная степень 2 на срезе z = 0 совпадает с плоским фракталом.",
            "Fractal3DBurningShip", "burning-ship-3d"),
        Fractal3DKind.BurningShipJulia => new(
            "Горящий корабль 3D — Жюлиа", "Горящий корабль 3D — Жюлиа",
            "Julia-вариант трёхмерного «Горящего корабля»: та же выбранная формула и степень, но постоянная C фиксирована. Её можно выбрать на соответствующей 3D-карте.",
            "Fractal3DBurningShipJulia", "burning-ship-julia-3d"),
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
        Fractal3DKind.Vicsek => new(
            "Объёмный фрактал Вицека", "Объёмный фрактал Вицека",
            "На каждом уровне куб заменяется центральным кубиком и шестью кубиками на гранях.",
            "Fractal3DVicsek", "vicsek-3d"),
        Fractal3DKind.CantorDust => new(
            "Кубическая пыль Кантора", "Кубическая пыль Кантора",
            "На каждом уровне остаются восемь угловых кубиков из 27; структура повторяется в каждом из них.",
            "Fractal3DCantorDust", "cantor-dust-3d"),
        Fractal3DKind.SierpinskiTetrahedron => new(
            "Тетраэдр Серпинского", "Тетраэдр Серпинского",
            "Трёхмерный аналог треугольника Серпинского: складывание пространства к ближайшей вершине тетраэдра.",
            "Fractal3DSierpinski", "sierpinski-tetrahedron"),
        Fractal3DKind.QuaternionJulia => new(
            "Кватернионное Жюлиа", "Кватернионное Жюлиа",
            "Множество Жюлиа в алгебре кватернионов: трёхмерный срез четырёхмерного множества с настраиваемой координатой среза.",
            "Fractal3DQuaternionJulia", "quaternion-julia"),
        Fractal3DKind.ApollonianPacking => new(
            "Аполлонова упаковка сфер", "Аполлонова упаковка сфер",
            "Рекурсивная упаковка взаимно касающихся сфер внутри внешней сферы. Число поколений задаёт глубину заполнения промежутков.",
            "Fractal3DApollonian", "apollonian-spheres"),
        Fractal3DKind.Terrain => new(
            "Фрактальные ландшафты", "Фрактальные ландшафты",
            "Горы, хребты и поверхности: fBm, гребневый и гибридный шум. Шероховатость, масштабы деталей и экспорт 16-битной карты высот.",
            "Fractal3DTerrain", "terrain"),
        Fractal3DKind.Ifs3D => new(
            "Конструктор объёмных IFS", "Конструктор объёмных IFS",
            "Редактируемые аффинные преобразования порождают трёхмерный аттрактор. Кадр строится на Direct3D 11; камера и навигация общие с другими трёхмерными фракталами.",
            "IFS3D", "ifs3d"),
        _ => new(
            "Мандельбульб", "Мандельбульб",
            "Трёхмерное обобщение множества Мандельброта через сферические координаты: z → zⁿ + c с произвольной степенью n.",
            "Fractal3DMandelbulb", "mandelbulb")
    };

    public static string MotionQualityName(Fractal3DMotionQuality quality) => quality switch
    {
        Fractal3DMotionQuality.Full => "Полное",
        Fractal3DMotionQuality.Draft => "Черновое",
        _ => "Без мягких теней"
    };

    public static string ColoringModeName(Fractal3DColoringMode mode, Fractal3DKind? kind = null)
    {
        if (kind == Fractal3DKind.ApollonianPacking)
        {
            return mode switch
            {
                Fractal3DColoringMode.OrbitTrap => "По диаметру сферы",
                Fractal3DColoringMode.CrossTrap => "По положению сферы",
                Fractal3DColoringMode.IterationIndex => "По масштабу сферы",
                Fractal3DColoringMode.Escape => "По радиусу сферы",
                _ => ColoringModeName(mode)
            };
        }
        return mode switch
        {
            Fractal3DColoringMode.Material => "Материал",
            Fractal3DColoringMode.Normal => "По нормали",
            Fractal3DColoringMode.Depth => "По глубине",
            Fractal3DColoringMode.CrossTrap => "Ловушка по осям",
            Fractal3DColoringMode.IterationIndex => "Номер итерации",
            Fractal3DColoringMode.Escape => "Скорость убегания",
            Fractal3DColoringMode.Height => "По высоте",
            Fractal3DColoringMode.Occlusion => "По затенению складок",
            Fractal3DColoringMode.Fresnel => "По углу взгляда",
            Fractal3DColoringMode.Steps => "По числу шагов луча",
            _ => "Орбитальная ловушка"
        };
    }

    /// <summary>Пользуется ли источник цвета палитрой; материал и нормаль обходятся без неё.</summary>
    public static bool UsesPalette(Fractal3DColoringMode mode) =>
        mode is not (Fractal3DColoringMode.Material or Fractal3DColoringMode.Normal);

    public static string ShadingStyleName(Fractal3DShadingStyle style) => style switch
    {
        Fractal3DShadingStyle.Clay => "Глина",
        Fractal3DShadingStyle.Metal => "Металл",
        Fractal3DShadingStyle.Glow => "Свечение",
        Fractal3DShadingStyle.Density => "Плотность",
        Fractal3DShadingStyle.Studio => "Студийный свет",
        Fractal3DShadingStyle.Toon => "Контурный",
        Fractal3DShadingStyle.Translucent => "Просвечивание",
        _ => "Классический"
    };

    public static string ColorRepeatName(Fractal3DColorRepeat repeat) => repeat switch
    {
        Fractal3DColorRepeat.Clamp => "Зажать",
        Fractal3DColorRepeat.Cycle => "По кругу",
        _ => "Отражать"
    };

    /// <summary>Состояние по умолчанию; оно же — превью пункта каталога.</summary>
    public static Fractal3DState CreateDefaultState(Fractal3DKind kind)
    {
        var state = new Fractal3DState
        {
            Kind = kind,
            SaveName = GetDefinition(kind).Title,
            Palette = Fractal3DPalettes.Classic()
        };
        switch (kind)
        {
            case Fractal3DKind.BulbBoxHybrid:
                state.Power = 3;
                state.BoxScale = -1.8;
                state.BoxMinRadius = 0.5;
                state.BoxFoldingLimit = 1;
                state.HybridMix = 0;
                state.Iterations = 12;
                state.Bailout = 32;
                state.CameraDistance = 5.5;
                state.MaxDistance = 60;
                state.MaxSteps = 360;
                state.Palette = Fractal3DPalettes.Get("Медь и патина");
                break;
            case Fractal3DKind.BurningShip:
            case Fractal3DKind.BurningShipJulia:
                state.Power = 2;
                state.Iterations = 16;
                state.Bailout = 4;
                state.CameraDistance = 4.2;
                state.CameraYaw = 30;
                state.CameraPitch = 24;
                state.MaxSteps = 220;
                state.Palette = Fractal3DPalettes.Get("Раскалённый металл");
                state.ColorScale = 1.4;
                if (kind == Fractal3DKind.BurningShipJulia)
                {
                    state.JuliaCX = -0.35;
                    state.JuliaCY = -0.05;
                    state.JuliaCZ = 0.15;
                    state.CameraDistance = 3.3;
                    state.Iterations = 20;
                }
                break;
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
                // С этого расстояния куб целиком помещается в квадратную карточку каталога.
                state.CameraDistance = 4.2;
                state.CameraYaw = 28;
                state.CameraPitch = 24;
                state.ColoringMode = Fractal3DColoringMode.Depth;
                // Глубина — не циклическая величина: градиент проходится один раз от ближнего края.
                state.ColorRepeat = Fractal3DColorRepeat.Clamp;
                break;
            case Fractal3DKind.Vicsek:
            case Fractal3DKind.CantorDust:
                state.Iterations = 4;
                state.CameraDistance = 4.2;
                state.CameraYaw = 28;
                state.CameraPitch = 24;
                state.MaxSteps = 220;
                state.ColoringMode = Fractal3DColoringMode.Depth;
                state.ColorRepeat = Fractal3DColorRepeat.Clamp;
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
            case Fractal3DKind.ApollonianPacking:
                state.Iterations = 4;
                state.CameraDistance = 3.7;
                state.CameraYaw = 32;
                state.CameraPitch = 20;
                state.MaxDistance = 30;
                state.MaxSteps = 192;
                state.ColoringMode = Fractal3DColoringMode.IterationIndex;
                state.ColorRepeat = Fractal3DColorRepeat.Clamp;
                state.Palette = Fractal3DPalettes.Get("Медь и патина");
                break;
            case Fractal3DKind.Terrain:
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
                break;
            case Fractal3DKind.Ifs3D:
                ApplyIfsPreset(state, Ifs3DPresets.All[0]);
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
        Fractal3DKind.BulbBoxHybrid =>
        [
            Preset(kind, "Чередование 1 : 1", _ => { }),
            Preset(kind, "Сначала бокс · 2 : 1", s =>
            {
                s.HybridOrder = Hybrid3DOrder.BoxFirst;
                s.HybridBoxSteps = 2;
                s.HybridMix = 0.1;
                s.CameraDistance = 15;
            }),
            Preset(kind, "Преобладание бульба · 3 : 1", s =>
            {
                s.HybridBulbSteps = 3;
                s.HybridMix = 0.25;
                s.Power = 4;
            }),
            Preset(kind, "Сильное смешивание", s =>
            {
                s.HybridMix = 0.45;
                s.BoxScale = 2;
                s.Power = 5;
                s.Palette = Fractal3DPalettes.Get("Аметист");
            }),
            Preset(kind, "Ледяные складки", s =>
            {
                s.HybridOrder = Hybrid3DOrder.BoxFirst;
                s.HybridBulbSteps = 2;
                s.HybridBoxSteps = 2;
                s.HybridMix = 0.15;
                s.BoxScale = -2;
                s.Power = 6;
                s.Palette = Fractal3DPalettes.Get("Лёд");
                s.ColoringMode = Fractal3DColoringMode.CrossTrap;
                s.ColorScale = 0.6;
            })
        ],
        Fractal3DKind.BurningShip =>
        [
            Preset(kind, "Горящий корабль — общий вид", _ => { }),
            Preset(kind, "Кватернион — степень 3", s =>
            {
                s.Power = 3;
                s.Iterations = 20;
            }),
            Preset(kind, "Три отражения", s => s.BurningShipFormula = BurningShip3DFormula.QuaternionFullFold),
            Preset(kind, "Сферический корабль", s =>
            {
                s.BurningShipFormula = BurningShip3DFormula.Spherical;
                s.Power = 4;
                s.Iterations = 12;
            }),
            Preset(kind, "Сферические ледяные гребни", s =>
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
        ],
        Fractal3DKind.BurningShipJulia =>
        [
            Preset(kind, "Жюлиа горящего корабля", _ => { }),
            Preset(kind, "Три отражения", s => s.BurningShipFormula = BurningShip3DFormula.QuaternionFullFold),
            Preset(kind, "Константа из 2D-галереи", s =>
            {
                s.JuliaCX = -1.7551867961883;
                s.JuliaCY = 0.01068;
                s.JuliaCZ = 0;
                s.CameraDistance = 4.2;
            }),
            Preset(kind, "Тёмные грани", s =>
            {
                s.JuliaCX = 0.25;
                s.JuliaCY = -0.45;
                s.JuliaCZ = 0.15;
                s.CameraYaw = 55;
            }),
            Preset(kind, "Сферическая Жюлиа", s =>
            {
                s.BurningShipFormula = BurningShip3DFormula.Spherical;
                s.Power = 3;
                s.JuliaCX = -0.3;
                s.JuliaCY = 0.25;
                s.JuliaCZ = 0.15;
            }),
            Preset(kind, "Сферическая Жюлиа — три отражения", s =>
            {
                s.BurningShipFormula = BurningShip3DFormula.SphericalFullFold;
                s.Power = 4;
                s.JuliaCX = -0.3;
                s.JuliaCY = 0.25;
                s.JuliaCZ = 0.15;
                s.Palette = Fractal3DPalettes.Get("Лёд");
            })
        ],
        Fractal3DKind.Ifs3D => Ifs3DPresets.All.Select(preset =>
        {
            Fractal3DState state = CreateDefaultState(kind);
            ApplyIfsPreset(state, preset);
            state.SaveName = preset.Name;
            return state;
        }).ToList(),
        Fractal3DKind.Terrain =>
        [
            Preset(kind, "Горные хребты", _ => { }),
            Preset(kind, "Мягкие холмы — fBm", s => s.Terrain = s.Terrain with
                { Type = TerrainKind.Fbm, Roughness = 0.35, Height = 2 }),
            Preset(kind, "Альпийские вершины", s => s.Terrain = s.Terrain with
                { Seed = 137, Height = 3, Roughness = 0.65, Scale = 4 }),
            Preset(kind, "Гибридный рельеф", s => s.Terrain = s.Terrain with
                { Type = TerrainKind.Hybrid, Seed = 91, Roughness = 0.65, Height = 3 })
        ],
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
                s.Palette = Fractal3DPalettes.Get("Лёд");
            }),
            Preset(kind, "Светящиеся складки", s =>
            {
                s.Iterations = 10;
                s.ShadingStyle = Fractal3DShadingStyle.Glow;
                s.ColoringMode = Fractal3DColoringMode.IterationIndex;
                s.Palette = Fractal3DPalettes.Get("Неон");
                s.ColorScale = 1.4;
                s.EffectStrength = 1.3;
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
            }),
            Preset(kind, "Плотность без поверхности", s =>
            {
                // Ветвистая константа: у сплошного шара просвет одинаков везде и смотреть не на что.
                s.JuliaCX = -0.5;
                s.JuliaCY = 0.2;
                s.JuliaCZ = 0.1;
                s.Iterations = 11;
                s.CameraDistance = 3.1;
                s.ShadingStyle = Fractal3DShadingStyle.Density;
                s.Palette = Fractal3DPalettes.Get("Северное сияние");
                s.ColorRepeat = Fractal3DColorRepeat.Clamp;
                s.EffectStrength = 0.5;
                s.MaxSteps = 220;
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
                s.Palette = Fractal3DPalettes.Get("Медь и патина");
            }),
            Preset(kind, "Полированный металл", s =>
            {
                s.ShadingStyle = Fractal3DShadingStyle.Metal;
                s.ColoringMode = Fractal3DColoringMode.CrossTrap;
                s.Palette = Fractal3DPalettes.Get("Мрамор");
                s.BackgroundTop = Color.FromRgb(72, 96, 140);
                s.BackgroundBottom = Color.FromRgb(12, 14, 20);
                s.Ambient = 0.35;
                s.Specular = 0.9;
                s.SkyLightMix = 0.35;
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
            }),
            Preset(kind, "Студийная глина", s =>
            {
                s.ShadingStyle = Fractal3DShadingStyle.Studio;
                s.ColoringMode = Fractal3DColoringMode.Occlusion;
                s.Palette = Fractal3DPalettes.Get("Сепия");
                s.ColorRepeat = Fractal3DColorRepeat.Clamp;
            })
        ],
        Fractal3DKind.Vicsek or Fractal3DKind.CantorDust =>
        [
            Preset(kind, "Классический вид", _ => { }),
            Preset(kind, "Крупные детали", s => s.Iterations = 2),
            Preset(kind, "Тонкие элементы", s => s.CubeThickness = 0.7),
            Preset(kind, "Массивные элементы", s => s.CubeThickness = 1.4)
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
            }),
            Preset(kind, "Контурный чертёж", s =>
            {
                s.ShadingStyle = Fractal3DShadingStyle.Toon;
                s.ColoringMode = Fractal3DColoringMode.Height;
                s.Palette = Fractal3DPalettes.Get("Закат");
                s.ColorScale = 0.4;
                s.ColorOffset = 0.5;
                s.ColorRepeat = Fractal3DColorRepeat.Clamp;
                s.EffectStrength = 1.2;
            })
        ],
        Fractal3DKind.ApollonianPacking =>
        [
            Preset(kind, "Четыре поколения — общий вид", _ => { }),
            Preset(kind, "Два поколения — крупные сферы", s =>
            {
                s.Iterations = 2;
                s.CameraYaw = 25;
            }),
            Preset(kind, "Пять поколений — сеть касаний", s =>
            {
                s.Iterations = 5;
                s.CameraDistance = 3.2;
                s.MaxSteps = 240;
                s.Detail = 0.6;
            }),
            Preset(kind, "Центральный просвет", s =>
            {
                s.Iterations = 5;
                s.CameraDistance = 1.7;
                s.CameraYaw = 44;
                s.CameraPitch = 8;
                s.TargetX = 0.08;
                s.TargetY = -0.05;
                s.Detail = 0.5;
                s.MaxSteps = 280;
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
                s.Palette = Fractal3DPalettes.Get("Аметист");
            }),
            Preset(kind, "Просвечивающее стекло", s =>
            {
                s.ShadingStyle = Fractal3DShadingStyle.Translucent;
                s.ColoringMode = Fractal3DColoringMode.Fresnel;
                s.Palette = Fractal3DPalettes.Get("Лёд");
                s.EffectStrength = 1.5;
                s.Specular = 0.8;
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

    private static void ApplyIfsPreset(Fractal3DState target, Ifs3DPreset preset)
    {
        Ifs3DState source = preset.State;
        target.Iterations = source.Iterations;
        target.IfsTransforms = source.Transforms.Select(transform => transform.Clone()).ToList();
        target.CameraYaw = source.Yaw;
        target.CameraPitch = source.Pitch;
        target.CameraDistance = 3.5;
        target.MaxSteps = 320;
        target.SurfaceColor = source.PointColor;
        target.BackgroundTop = source.BackgroundColor;
        target.BackgroundBottom = source.BackgroundColor;
        target.ColoringMode = Fractal3DColoringMode.Material;
        target.SoftShadows = false;
        target.AmbientOcclusion = false;
        target.Palette = Fractal3DPalette.FromPair(
            $"IFS: {preset.Name}", source.PointColor, Color.FromRgb(235, 255, 255));
    }
}
