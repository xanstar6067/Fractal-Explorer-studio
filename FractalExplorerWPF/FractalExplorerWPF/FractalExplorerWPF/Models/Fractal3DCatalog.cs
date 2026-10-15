using FractalExplorerWPF.Models.Fractal3DModes;

namespace FractalExplorerWPF.Models;

/// <summary>
/// Каталог трёхмерных режимов: ключи запуска, реестр классов <see cref="Fractal3DMode"/> по пунктам
/// <see cref="Fractal3DKind"/>, подписи общих перечислений и короткие обращения к режиму по пункту.
/// </summary>
public static class Fractal3DCatalog
{
    public const string LaunchPrefix = "Fractal3D:";

    private static readonly Fractal3DMode[] ModesByKind = Register(
        new MandelbulbMode(), new JuliabulbMode(), new MandelboxMode(), new MengerSpongeMode(),
        new SierpinskiTetrahedronMode(), new QuaternionJuliaMode(), new ApollonianPackingMode(), new Ifs3DMode(),
        new VicsekMode(), new CantorDustMode(), new TerrainMode(), new BurningShipMode(), new BurningShipJuliaMode(),
        new BulbBoxHybridMode(), new Phoenix3DMode(), new StrangeAttractorMode(), new Flame3DMode(), new KifsMode(),
        new Dla3DMode(), new LSystem3DMode(), new Buddhabrot4DMode(), new GrayScott3DMode(), new Turing3DMode(),
        new CahnHilliard3DMode(), new HopfMode(), new Lichtenberg3DMode(), new Physarum3DMode(), new Lenia3DMode(),
        new Kobayashi3DMode(), new Lyapunov3DMode(), new Excitable3DMode(), new PeriodicSurfacesMode(),
        new ImplicitSurfaceMode(), new HyperbolicHoneycombMode(), new AbcFlowMode(), new GinzburgLandau3DMode(), new SwiftHohenberg3DMode(),
        new PhaseFieldCrystal3DMode(), new KleinianMode());

    /// <summary>Все режимы в порядке пунктов <see cref="Fractal3DKind"/>.</summary>
    public static IReadOnlyList<Fractal3DMode> Modes => ModesByKind;

    /// <summary>
    /// Режим пункта. Неизвестный пункт (файл из более новой версии) читается как Мандельбульб —
    /// так же вели себя ветви по умолчанию до разделения режимов на классы.
    /// </summary>
    public static Fractal3DMode Mode(Fractal3DKind kind) =>
        (uint)kind < (uint)ModesByKind.Length ? ModesByKind[(int)kind] : ModesByKind[(int)Fractal3DKind.Mandelbulb];

    private static Fractal3DMode[] Register(params Fractal3DMode[] modes)
    {
        Fractal3DKind[] kinds = Enum.GetValues<Fractal3DKind>();
        var byKind = new Fractal3DMode[kinds.Length];
        foreach (Fractal3DMode mode in modes)
        {
            if ((uint)mode.Kind >= (uint)byKind.Length || byKind[(int)mode.Kind] is not null)
                throw new InvalidOperationException($"Режим {mode.Kind} зарегистрирован дважды или вне перечисления.");
            byKind[(int)mode.Kind] = mode;
        }
        Fractal3DKind[] missing = kinds.Where(kind => byKind[(int)kind] is null).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Нет класса режима для {string.Join(", ", missing)}.");
        return byKind;
    }

    public static string LaunchKey(Fractal3DKind kind) => LaunchPrefix + kind;

    public static bool TryParseLaunchKey(string? launchKey, out Fractal3DKind kind)
    {
        kind = default;
        return launchKey?.StartsWith(LaunchPrefix, StringComparison.Ordinal) == true &&
               Enum.TryParse(launchKey[LaunchPrefix.Length..], out kind);
    }

    public static Fractal3DDefinition GetDefinition(Fractal3DKind kind) => Mode(kind).Definition;

    /// <summary>Состояние по умолчанию; оно же — превью пункта каталога.</summary>
    public static Fractal3DState CreateDefaultState(Fractal3DKind kind) => Mode(kind).CreateDefaultState();

    /// <summary>Готовые виды режима; они же — точки интереса менеджера сохранений.</summary>
    public static IReadOnlyList<Fractal3DState> GetPresets(Fractal3DKind kind) => Mode(kind).CreatePresets();

    public static bool UsesPower(Fractal3DKind kind) => Mode(kind).UsesPower;

    public static bool UsesJuliaConstant(Fractal3DKind kind) => Mode(kind).UsesJuliaConstant;

    public static bool IsBurningShip(Fractal3DKind kind) => Mode(kind) is BurningShipFamilyMode;

    /// <inheritdoc cref="Fractal3DMode.IsDensityVolume"/>
    public static bool IsDensityVolume(Fractal3DKind kind) => Mode(kind).IsDensityVolume;

    /// <inheritdoc cref="Fractal3DMode.IsGpuSimulation"/>
    public static bool IsGpuSimulation(Fractal3DKind kind) => Mode(kind).IsGpuSimulation;

    /// <inheritdoc cref="Fractal3DMode.UsesIterations"/>
    public static bool UsesIterations(Fractal3DKind kind) => Mode(kind).UsesIterations;

    /// <inheritdoc cref="Fractal3DMode.UsesBailout"/>
    public static bool UsesBailout(Fractal3DKind kind) => Mode(kind).UsesBailout;

    /// <inheritdoc cref="Fractal3DMode.HasColoringMode"/>
    public static bool HasColoringMode(Fractal3DColoringMode mode, Fractal3DKind kind) => Mode(kind).HasColoringMode(mode);

    public static string BurningShipFormulaName(BurningShip3DFormula formula) => formula switch
    {
        BurningShip3DFormula.QuaternionFullFold => "Кватернион · три отражения",
        BurningShip3DFormula.Spherical => "Сферическая степень · два отражения",
        BurningShip3DFormula.SphericalFullFold => "Сферическая степень · три отражения",
        _ => "Кватернион · два отражения"
    };

    public static string MotionQualityName(Fractal3DMotionQuality quality) => quality switch
    {
        Fractal3DMotionQuality.Full => "Полное",
        Fractal3DMotionQuality.Draft => "Черновое",
        _ => "Без мягких теней"
    };

    /// <summary>Подпись источника цвета; с пунктом режима — с учётом переназначенных им источников.</summary>
    public static string ColoringModeName(Fractal3DColoringMode mode, Fractal3DKind? kind = null)
    {
        if (kind is { } modeKind) return Mode(modeKind).ColoringModeName(mode);
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
        Fractal3DShadingStyle.Mirror => "Зеркало",
        _ => "Классический"
    };

    public static string ColorRepeatName(Fractal3DColorRepeat repeat) => repeat switch
    {
        Fractal3DColorRepeat.Clamp => "Зажать",
        Fractal3DColorRepeat.Cycle => "По кругу",
        _ => "Отражать"
    };

    public static string BoxInversionShapeName(BoxInversionShape shape) => shape switch
    {
        BoxInversionShape.Cube => "куб",
        BoxInversionShape.Ellipsoid => "вытянутая сфера",
        BoxInversionShape.RoundedCube => "скруглённый куб",
        _ => "сфера"
    };
}
