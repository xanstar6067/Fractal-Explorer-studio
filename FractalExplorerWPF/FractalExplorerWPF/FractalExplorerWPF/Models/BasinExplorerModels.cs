using System.Globalization;
using System.Numerics;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models;

/// <summary>Пять исследователей из раздела «Бассейны притяжения», обслуживаемых одним окном.</summary>
public enum BasinExplorerKind
{
    Muller,
    Laguerre,
    Secant,
    RationalMap,
    PeriodicCycles
}

/// <summary>Как пиксель z превращается в начальную тройку приближений метода Мюллера.</summary>
public enum MullerSeedMode
{
    /// <summary>(z − h, z + h, z).</summary>
    Symmetric,

    /// <summary>(z − 2h, z − h, z).</summary>
    Trailing,

    /// <summary>(a, b, z) — две фиксированные точки и пиксель.</summary>
    FixedAnchors
}

public enum LaguerreComparisonMode
{
    Laguerre,
    Newton,
    Disagreement
}

public enum SecantPlaneMode
{
    /// <summary>x₀ = a фиксирована, пиксель — x₁.</summary>
    FixedFirstPoint,

    /// <summary>x₀ = z − h, x₁ = z: при h → 0 получается метод Ньютона.</summary>
    OffsetPair,

    /// <summary>Двумерный срез четырёхмерного пространства состояний (x₀, x₁).</summary>
    StateSlice
}

/// <summary>Вещественная координата пространства состояний метода секущих.</summary>
public enum SecantStateAxis
{
    ReX0,
    ImX0,
    ReX1,
    ImX1
}

public enum BasinInfinityHandling
{
    /// <summary>Рациональное отображение: ∞ — аттрактор, если он притягивает по степеням P и Q; иначе уход — чёрный.</summary>
    Auto,
    Attractor,
    Escape
}

public enum BasinColoringMode
{
    Basins,
    ConvergenceSpeed,
    CyclePhase,
    Period,
    OrbitOutcome,
    IterationCount
}

public enum BasinMarkerMode
{
    Hidden,
    Markers,
    MarkersWithLabels,
    MarkersWithCriticalPoints
}

public enum BasinOrbitOutcome
{
    Converged,
    Cycle,
    Degenerate,
    Escaped,
    NonFinite,
    IterationLimit
}

/// <summary>
/// Притягивающий цикл отображения (или ∞). Точки идут в порядке орбиты, первая — наименьшая
/// по вещественной, затем мнимой части, поэтому один и тот же цикл всегда записывается одинаково.
/// </summary>
public sealed class BasinAttractor
{
    public int Period { get; set; } = 1;
    public List<Complex> Points { get; set; } = [];
    public Complex Multiplier { get; set; }
    public bool IsInfinity { get; set; }

    public BasinAttractor Clone() => new()
    {
        Period = Period,
        Points = [.. Points],
        Multiplier = Multiplier,
        IsInfinity = IsInfinity
    };

    public string Describe(int index)
    {
        if (IsInfinity) return $"{index + 1}. ∞ (притягивающая бесконечность)";
        string point = Points.Count > 0 ? BasinExplorerFormatting.Complex(Points[0]) : "—";
        return $"{index + 1}. Период {Period} · |λ| = {Multiplier.Magnitude.ToString("0.####", CultureInfo.InvariantCulture)} · {point}";
    }

    public string ShortLabel(int index) => IsInfinity
        ? $"Аттрактор {index + 1}: ∞"
        : $"Цикл {index + 1}: период {Period}, {(Points.Count > 0 ? BasinExplorerFormatting.Complex(Points[0]) : "—")}";
}

public sealed class BasinExplorerState
{
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public BasinExplorerKind Kind { get; set; }

    /// <summary>f(z) для методов поиска корней и для режима периодических циклов.</summary>
    public string Formula { get; set; } = "z^3-1";

    /// <summary>Числитель P(z) рационального отображения.</summary>
    public string Numerator { get; set; } = "2*z^3-2";

    /// <summary>Знаменатель Q(z) рационального отображения.</summary>
    public string Denominator { get; set; } = "3*z^2-2";

    /// <summary>Значение параметра c, который можно использовать в формулах отображений.</summary>
    public Complex ParameterC { get; set; }

    public int MaxIterations { get; set; } = 200;
    public double Zoom { get; set; } = 1;
    public double CenterX { get; set; }
    public double CenterY { get; set; }

    public NewtonRootSearchMode RootSearchMode { get; set; }
    public double RootTolerance { get; set; } = 1e-6;
    public double RootSearchRadius { get; set; } = 8;
    public List<Complex> Roots { get; set; } = [];

    public MullerSeedMode MullerSeedMode { get; set; }
    public Complex MullerOffset { get; set; } = new(0.25, 0);
    public Complex MullerAnchorA { get; set; } = new(-1, 0);
    public Complex MullerAnchorB { get; set; } = new(1, 0);

    public bool LaguerreAutoDegree { get; set; } = true;
    public double LaguerreDegree { get; set; } = 3;
    public LaguerreComparisonMode LaguerreComparison { get; set; }

    public SecantPlaneMode SecantPlaneMode { get; set; }
    public Complex SecantFirstPoint { get; set; } = new(1, 1);
    public Complex SecantOffset { get; set; } = new(0.25, 0);
    public SecantStateAxis SecantHorizontalAxis { get; set; } = SecantStateAxis.ReX0;
    public SecantStateAxis SecantVerticalAxis { get; set; } = SecantStateAxis.ReX1;
    public Complex SecantBaseX0 { get; set; }
    public Complex SecantBaseX1 { get; set; }

    public int MaxPeriod { get; set; } = 8;
    public double CycleTolerance { get; set; } = 1e-6;
    public double AttractorSearchRadius { get; set; } = 3;
    public double EscapeRadius { get; set; } = 1e6;
    public BasinInfinityHandling InfinityHandling { get; set; }

    /// <summary>0 — все периоды, иначе раскрашиваются только бассейны циклов этого периода.</summary>
    public int PeriodFilter { get; set; }

    public List<BasinAttractor> Attractors { get; set; } = [];

    /// <summary>
    /// Список <see cref="Attractors"/> снят с окна и используется как есть (даже пустой). У готовых
    /// примеров false — циклы ищутся при загрузке.
    /// </summary>
    public bool UseSavedAttractors { get; set; }

    public BasinColoringMode ColoringMode { get; set; } = BasinColoringMode.ConvergenceSpeed;
    public double ShadingScale { get; set; } = 3;
    public BasinMarkerMode MarkerMode { get; set; }
    public NewtonColorPalette Palette { get; set; } = new();

    public BasinExplorerState Clone()
    {
        var copy = (BasinExplorerState)MemberwiseClone();
        copy.Roots = [.. Roots];
        copy.Attractors = Attractors.Select(attractor => attractor.Clone()).ToList();
        copy.Palette = Palette.Clone(Palette.Name);
        copy.Palette.IsBuiltIn = false;
        return copy;
    }
}

public readonly record struct BasinOrbitResult(
    BasinOrbitOutcome Outcome,
    int Iterations,
    double SmoothIterations,
    Complex FinalPoint,
    int TargetIndex = -1,
    int Phase = 0,
    int CyclePeriod = 0);

public sealed record BasinExplorerDefinition(
    string Title,
    string PanelTitle,
    string SaveFilePrefix,
    string ExportFilePrefix,
    bool UsesRoots,
    string Hint);

public static class BasinExplorerCatalog
{
    public const string LaunchPrefix = "Basins:";

    public static string LaunchKey(BasinExplorerKind kind) => LaunchPrefix + kind;

    public static bool TryParseLaunchKey(string? launchKey, out BasinExplorerKind kind)
    {
        kind = default;
        return launchKey?.StartsWith(LaunchPrefix, StringComparison.Ordinal) == true &&
               Enum.TryParse(launchKey[LaunchPrefix.Length..], out kind);
    }

    public static bool UsesRoots(BasinExplorerKind kind) =>
        kind is BasinExplorerKind.Muller or BasinExplorerKind.Laguerre or BasinExplorerKind.Secant;

    public static BasinExplorerDefinition GetDefinition(BasinExplorerKind kind) => kind switch
    {
        BasinExplorerKind.Muller => new(
            "Бассейны метода Мюллера", "Метод Мюллера", "BasinsMuller", "muller_basins", true,
            "Колесо мыши: масштаб (Ctrl — ×10, Shift — точно). Левая кнопка: перемещение. Правая кнопка: орбита точки. F11: полноэкранный режим."),
        BasinExplorerKind.Laguerre => new(
            "Бассейны метода Лагерра", "Метод Лагерра", "BasinsLaguerre", "laguerre_basins", true,
            "Колесо мыши: масштаб (Ctrl — ×10, Shift — точно). Левая кнопка: перемещение. Правая кнопка: орбита точки. F11: полноэкранный режим."),
        BasinExplorerKind.Secant => new(
            "Бассейны метода секущих", "Метод секущих", "BasinsSecant", "secant_basins", true,
            "Колесо мыши: масштаб (Ctrl — ×10, Shift — точно). Левая кнопка: перемещение. Правая кнопка: орбита точки (кроме среза пространства состояний). F11: полноэкранный режим."),
        BasinExplorerKind.RationalMap => new(
            "Бассейны рациональных отображений", "Рациональное отображение", "BasinsRationalMap", "rational_map_basins", false,
            "Колесо мыши: масштаб (Ctrl — ×10, Shift — точно). Левая кнопка: перемещение. Правая кнопка: орбита точки и затравка для поиска цикла. F11: полноэкранный режим."),
        _ => new(
            "Бассейны периодических циклов", "Периодические циклы", "BasinsPeriodicCycles", "periodic_cycle_basins", false,
            "Колесо мыши: масштаб (Ctrl — ×10, Shift — точно). Левая кнопка: перемещение. Правая кнопка: орбита точки и затравка для поиска цикла. F11: полноэкранный режим.")
    };

    /// <summary>Готовые примеры окна; они же — точки интереса менеджера сохранений.</summary>
    public static IReadOnlyList<BasinExplorerState> GetPresets(BasinExplorerKind kind) => kind switch
    {
        BasinExplorerKind.Muller =>
        [
            Root(kind, "z³ − 1 · симметричная тройка", "z^3-1", s => { s.MullerOffset = new Complex(0.25, 0); }),
            Root(kind, "z³ − 1 · отстающая тройка, h = 0.6i", "z^3-1", s => { s.MullerSeedMode = MullerSeedMode.Trailing; s.MullerOffset = new Complex(0, 0.6); }),
            Root(kind, "z⁵ − 1 · якоря a = −1, b = 1", "z^5-1", s => { s.MullerSeedMode = MullerSeedMode.FixedAnchors; s.Zoom = 0.8; }),
            Root(kind, "z⁴ − 1 · якоря a = 0, b = i", "z^4-1", s => { s.MullerSeedMode = MullerSeedMode.FixedAnchors; s.MullerAnchorA = Complex.Zero; s.MullerAnchorB = Complex.ImaginaryOne; }),
            Root(kind, "z⁵ − 1 · близкие якоря a = 0.2 + 0.2i, b = −0.2 + 0.2i", "z^5-1", s =>
            {
                s.MullerSeedMode = MullerSeedMode.FixedAnchors;
                s.MullerAnchorA = new Complex(0.2, 0.2);
                s.MullerAnchorB = new Complex(-0.2, 0.2);
                s.Zoom = 1.3;
            }),
            Root(kind, "z³ − 2z + 2 · большой шаг h = 1", "z^3-2*z+2", s => { s.MullerOffset = Complex.One; }),
            Root(kind, "sin(z) − 0.5", "sin(z)-0.5", s => { s.Zoom = 0.4; s.RootSearchRadius = 10; s.MullerOffset = new Complex(0.5, 0); }),
            Root(kind, "z⁶ + 3z³ − 2 · h = 0.1 + 0.1i", "z^6+3*z^3-2", s => { s.MullerOffset = new Complex(0.1, 0.1); s.Zoom = 0.9; })
        ],
        BasinExplorerKind.Laguerre =>
        [
            Root(kind, "z³ − 1 · Лагерр", "z^3-1", _ => { }),
            Root(kind, "z³ − 1 · расхождение с Ньютоном", "z^3-1", s => { s.LaguerreComparison = LaguerreComparisonMode.Disagreement; }),
            Root(kind, "z⁸ + 15z⁴ − 16 · Лагерр", "z^8+15*z^4-16", s => { s.Zoom = 0.6; }),
            Root(kind, "z⁸ + 15z⁴ − 16 · n = 2", "z^8+15*z^4-16", s => { s.Zoom = 0.6; s.LaguerreAutoDegree = false; s.LaguerreDegree = 2; }),
            Root(kind, "z³ − 2z + 2 · расхождение с Ньютоном", "z^3-2*z+2", s => { s.LaguerreComparison = LaguerreComparisonMode.Disagreement; }),
            Root(kind, "(z − 1)²(z + 1) · кратный корень", "(z-1)^2*(z+1)", _ => { }),
            Root(kind, "z⁵ − z² + 1 · n = 12", "z^5-z^2+1", s => { s.LaguerreAutoDegree = false; s.LaguerreDegree = 12; })
        ],
        BasinExplorerKind.Secant =>
        [
            Root(kind, "z³ − 1 · x₀ = 1 + i", "z^3-1", _ => { }),
            Root(kind, "z³ − 1 · пара z − h, z", "z^3-1", s => { s.SecantPlaneMode = SecantPlaneMode.OffsetPair; s.SecantOffset = new Complex(0.4, 0.2); }),
            Root(kind, "z³ − 1 · срез (Re x₀, Re x₁), Im x₀ = 0.5, Im x₁ = −0.5", "z^3-1", s =>
            {
                s.SecantPlaneMode = SecantPlaneMode.StateSlice;
                s.SecantBaseX0 = new Complex(0, 0.5);
                s.SecantBaseX1 = new Complex(0, -0.5);
                s.Zoom = 0.6;
            }),
            Root(kind, "z³ − 2z + 2 · вещественный срез (Re x₀, Re x₁)", "z^3-2*z+2", s => { s.SecantPlaneMode = SecantPlaneMode.StateSlice; s.Zoom = 0.5; }),
            Root(kind, "z⁴ − 1 · срез (Re x₀, Im x₀), x₁ = 1.2 − 0.6i", "z^4-1", s =>
            {
                s.SecantPlaneMode = SecantPlaneMode.StateSlice;
                s.SecantHorizontalAxis = SecantStateAxis.ReX0;
                s.SecantVerticalAxis = SecantStateAxis.ImX0;
                s.SecantBaseX1 = new Complex(1.2, -0.6);
                s.Zoom = 0.5;
            }),
            Root(kind, "z⁵ − 1 · x₀ = 1.5", "z^5-1", s => { s.SecantFirstPoint = new Complex(1.5, 0); s.Zoom = 0.9; })
        ],
        BasinExplorerKind.RationalMap =>
        [
            Rational("Ньютон для z³ − 2z + 2: цикл {0, 1}", "2*z^3-2", "3*z^2-2", Complex.Zero, s => { s.CenterX = 0; s.Zoom = 1; }),
            Rational("Ньютон для z³ − 1", "2*z^3+1", "3*z^2", Complex.Zero, _ => { }),
            Rational("Кролик Дуади: z² + c", "z^2+c", "1", new Complex(-0.122561, 0.744862), s => { s.Zoom = 0.9; }),
            Rational("Цикл периода 4: z² + c", "z^2+c", "1", new Complex(-1.3107, 0), s => { s.Zoom = 0.9; }),
            Rational("Магнитная модель I: ((z² + c − 1)/(2z + c − 2))²", "(z^2+c-1)^2", "(2*z+c-2)^2", new Complex(1.5, 0), s => { s.Zoom = 0.45; s.CenterX = 1; }),
            Rational("Мак-Маллен z³ + c/z³: канторово множество окружностей", "z^6+c", "z^3", new Complex(0.01, 0), s => { s.ShadingScale = 1.5; }),
            Rational("z² + c/z² при c = −1/16: ковёр Серпинского", "z^4+c", "z^2", new Complex(-0.0625, 0), s => { s.ShadingScale = 1.5; }),
            Rational("z² − 1: сверхпритягивающий цикл {−1, 0}", "z^2-1", "1", Complex.Zero, s => { s.Zoom = 0.9; }),
            Rational("Галлей для z³ − 1", "z^4+2*z", "2*z^3+1", Complex.Zero, s => { s.Zoom = 0.9; })
        ],
        _ =>
        [
            Cycles("z² + c · кролик, период 3", "z^2+c", new Complex(-0.122561, 0.744862), s => { s.Zoom = 0.9; }),
            Cycles("z² + c · аэроплан, период 3", "z^2+c", new Complex(-1.754878, 0), s => { s.Zoom = 0.9; }),
            Cycles("z² + c · период 4", "z^2+c", new Complex(-1.3107, 0), s => { s.Zoom = 0.9; }),
            Cycles("z² + c · период 5", "z^2+c", new Complex(-0.504340, 0.562765), s => { s.Zoom = 0.9; }),
            Cycles("z³ + c·z + 0.25 · период 8", "z^3+c*z+0.25", Complex.ImaginaryOne, s => { s.Zoom = 0.9; s.MaxIterations = 600; }),
            Cycles("z⁴ − 0.5z + c · периоды 1 и 2, окраска по периоду", "z^4-0.5*z+c", new Complex(-0.75, 0), s => { s.Zoom = 1.1; s.ColoringMode = BasinColoringMode.Period; }),
            Cycles("z³ + c·z · два цикла периода 3", "z^3+c*z", new Complex(1.75, 0.5), s => { s.Zoom = 0.8; }),
            Cycles("c·exp(z) · неподвижная точка", "c*exp(z)", new Complex(0.3, 0), s => { s.Zoom = 0.25; s.CenterX = 1; s.EscapeRadius = 1e4; }),
            Cycles("c·sin(z) · цикл периода 4", "c*sin(z)", new Complex(0, 1.166667), s => { s.Zoom = 0.3; s.EscapeRadius = 1e4; s.AttractorSearchRadius = 6; }),
            Cycles("c·cos(z) · цикл периода 5", "c*cos(z)", new Complex(1.166667, -0.583333), s => { s.Zoom = 0.3; s.EscapeRadius = 1e4; s.AttractorSearchRadius = 6; }),
            Cycles("z² + c · кролик, окраска по фазе", "z^2+c", new Complex(-0.122561, 0.744862), s => { s.Zoom = 0.9; s.ColoringMode = BasinColoringMode.CyclePhase; })
        ]
    };

    private static BasinExplorerState Root(BasinExplorerKind kind, string name, string formula, Action<BasinExplorerState> configure)
    {
        var state = new BasinExplorerState
        {
            SaveName = name,
            Timestamp = DateTime.MinValue,
            Kind = kind,
            Formula = formula,
            MaxIterations = 200,
            ShadingScale = kind == BasinExplorerKind.Laguerre ? 2 : 3,
            Palette = ClassicPalette()
        };
        configure(state);
        return state;
    }

    private static BasinExplorerState Rational(string name, string numerator, string denominator, Complex c,
        Action<BasinExplorerState> configure)
    {
        var state = new BasinExplorerState
        {
            SaveName = name,
            Timestamp = DateTime.MinValue,
            Kind = BasinExplorerKind.RationalMap,
            Numerator = numerator,
            Denominator = denominator,
            ParameterC = c,
            MaxIterations = 300,
            MaxPeriod = 12,
            ShadingScale = 20,
            MarkerMode = BasinMarkerMode.Markers,
            Palette = ClassicPalette()
        };
        configure(state);
        return state;
    }

    private static BasinExplorerState Cycles(string name, string formula, Complex c, Action<BasinExplorerState> configure)
    {
        var state = new BasinExplorerState
        {
            SaveName = name,
            Timestamp = DateTime.MinValue,
            Kind = BasinExplorerKind.PeriodicCycles,
            Formula = formula,
            ParameterC = c,
            MaxIterations = 400,
            MaxPeriod = 8,
            EscapeRadius = 1e6,
            ShadingScale = 30,
            MarkerMode = BasinMarkerMode.Markers,
            Palette = ClassicPalette()
        };
        configure(state);
        return state;
    }

    /// <summary>Встроенная палитра «Классика» — гармонические оттенки по числу бассейнов.</summary>
    public static NewtonColorPalette ClassicPalette() => new()
    {
        Name = "Классика",
        RootColors = [],
        BackgroundColor = Colors.Black,
        IsGradient = false,
        ExpansionMode = NewtonPaletteExpansionMode.Harmonic
    };
}

public static class BasinExplorerFormatting
{
    public static string Complex(Complex value)
    {
        string real = value.Real.ToString("0.######", CultureInfo.InvariantCulture);
        string imaginary = Math.Abs(value.Imaginary).ToString("0.######", CultureInfo.InvariantCulture);
        return $"{real} {(value.Imaginary < 0 ? '−' : '+')} {imaginary}i";
    }

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}
