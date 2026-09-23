using System.Windows.Media;
using FractalExplorer.Engines;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>
/// WPF-представление точек интереса из PresetManager старой версии.
/// Возвращает состояния текущих WPF-моделей, готовые к загрузке и рендеру.
/// </summary>
public static class PresetManager
{
    // Встроенные палитры рассчитаны на период в сотни итераций, а снаружи множества у обзорных и
    // неглубоких кадров их единицы или десятки — без ускорения цикла (scale) такой кадр почти
    // целиком уходит в чёрное начало палитры. Mirror убирает шов на границе цикла, phase сдвигает
    // самые быстрые точки с чёрного. Каждую точку проверяет Verification (группа poi).
    public static IReadOnlyList<MandelbrotState> GetMandelbrotPresets(MandelbrotVariant variant) => variant switch
    {
        MandelbrotVariant.Mandelbrot =>
        [
            M("Долина Морских Коньков", variant, -0.743643887037151m, 0.13182590420533m, 11500m, 1000, "Лёд"),
            M("Шип Миниброта", variant, -1.7497m, 0m, 800m, 600, "Огонь", scale: 6, phase: 0.4),
            M("Лоза", variant, 0.3855604675494107229386479028m, -0.1050451711526294339131097223m, 150m, 800, "Огонь"),
            M("Спиральная Галактика", variant, -0.16070135m, 1.0375665m, 3000m, 600, "Ультрафиолет"),
            M("Долина Слонов", variant, 0.2869318688950451m, 0.014286693904085048m, 300m, 800, "Закат", scale: 10),
            M("Спираль Мисюревича", variant, -0.10109636384562m, 0.95628651080914m, 800m, 1000, "Золото", scale: 6),
            M("Четверная Спираль", variant, -0.7746806106269039m, -0.1374168856037867m, 5000m, 1500, "Космос"),
            M("Миниброт периода 3", variant, -1.7548776662466927m, 0m, 60m, 600, "Неон", scale: 6)
        ],
        MandelbrotVariant.Julia =>
        [
            M("Классическая Спираль", variant, 0m, 0m, 1m, 500, "Огонь", juliaReal: -0.8m, juliaImaginary: 0.156m),
            M("Дендрит", variant, 0m, 0m, 1m, 300, "Зеленый", juliaReal: 0m, juliaImaginary: 1m),
            M("Снежинка", variant, 0m, 0m, 1m, 400, "Лёд", juliaReal: -0.70176m, juliaImaginary: -0.3842m, scale: 12),
            M("Огненный Вихрь", variant, 0m, 0m, 1m, 350, "Огонь", juliaReal: 0.285m, juliaImaginary: 0.01m, scale: 8),
            M("Кролик Дуади", variant, 0m, 0m, 1m, 400, "Океан", juliaReal: -0.122561m, juliaImaginary: 0.744862m, scale: 10),
            M("Кружевная Спираль", variant, 0m, 0m, 1m, 400, "Аметист", juliaReal: -0.4m, juliaImaginary: 0.6m, scale: 10),
            M("Двойные Завитки", variant, 0m, 0m, 1m, 400, "Бирюза", juliaReal: 0.355m, juliaImaginary: 0.355m, scale: 10),
            M("Радужный Дракон", variant, 0m, 0m, 1m, 400, "Радуга", juliaReal: -0.835m, juliaImaginary: -0.2321m, scale: 6),
            M("Неоновое Облако", variant, 0m, 0m, 1m, 400, "Неон", juliaReal: -0.7269m, juliaImaginary: 0.1889m, scale: 6),
            M("Спираль крупным планом", variant, 0.3m, 0.1m, 6m, 400, "Огонь", juliaReal: -0.8m, juliaImaginary: 0.156m, scale: 6)
        ],
        MandelbrotVariant.BurningShip =>
        [
            M("Центральный Корабль", variant, -0.4m, 0.45m, 0.8m, 300, "Огонь", scale: 2, phase: 0.2),
            M("Глубоководный Корабль", variant, -1.7623214771385076201641266142m, 0.0200163188745603751465416114m, 40m, 700, "Ультрафиолет", scale: 3),
            M("Призрачные Паруса", variant, -1.7423683296426555512135816837m, 0.0648050817843091259643027922m, 76m, 1000, "Лёд", scale: 3),
            M("Армада", variant, -1.78m, 0.035m, 25m, 800, "Огонь", scale: 3, phase: 0.2),
            M("Золотой Мини-корабль", variant, -1.8621m, 0.001m, 640m, 1200, "Золото", scale: 3),
            M("Корабль у Кормы", variant, -1.9405m, 0.0018m, 1000m, 1500, "Океан", scale: 3),
            M("Медное Кружево", variant, -0.5m, 1m, 6m, 600, "Медь", scale: 3)
        ],
        MandelbrotVariant.Tricorn =>
        [
            M("Тройной Крест", variant, -0.2m, 0m, 0.8m, 500, "Ультрафиолет", scale: 15, phase: 0.2),
            M("Мини-трикорн", variant, 0.8725m, 1.51875m, 27m, 800, "Неон", scale: 3),
            M("Северная Ветвь", variant, 0.71667m, 1.25m, 6m, 800, "Лёд", scale: 4),
            M("Полосатый Берег", variant, 0.3m, 0.58333m, 6m, 800, "Космос", scale: 4),
            M("Золотая Антенна", variant, -1.2m, 0m, 6m, 800, "Золото", scale: 4),
            M("Ледяные Волокна", variant, 0.41m, -0.5525m, 93.75m, 800, "Океан", scale: 3)
        ],
        MandelbrotVariant.Celtic =>
        [
            M("Кельтский кардиоид", variant, -0.5m, 0m, 1.2m, 700, "Лёд", scale: 8, phase: 0.2),
            M("Мини-кельт", variant, -1.415m, 0.137m, 150m, 800, "Бирюза", scale: 1),
            M("Кельт на антенне", variant, -1.395m, 0m, 60m, 800, "Океан", scale: 1),
            M("Аметистовый Кельт", variant, -1.479m, 0m, 150m, 800, "Аметист", scale: 1),
            M("Антенна в тумане", variant, -1.5m, 0m, 6m, 800, "Лёд", scale: 1)
        ],
        MandelbrotVariant.JuliaBurningShip =>
        [
            M("Фиолетовый Пламень Жюлиа", variant, 0m, 0m, 1m, 500, "Ультрафиолет", juliaReal: 0.598214268684387m, juliaImaginary: 1.17851734161377m),
            M("Пульсарный Рубин", variant, 0m, 0m, 1m, 350, "Огонь", juliaReal: -0.0517381690442562m, juliaImaginary: -0.267557740211487m),
            M("Психонавт", variant, 0m, 0m, 1m, 500, "Психоделика", juliaReal: 0.736607134342194m, juliaImaginary: 1.09152793884277m),
            M("Кристальная Ось", variant, 0m, 0m, 1m, 400, "Неон", juliaReal: -1.7623m, juliaImaginary: 0.02m, scale: 6)
        ],
        MandelbrotVariant.Generalized =>
        [
            M("Трилистник (p=3.0)", variant, 0m, 0m, 0.8m, 500, "Ультрафиолет", power: 3m, scale: 15, phase: 0.2),
            M("Астероид (p=4.0)", variant, 0m, 0m, 0.8m, 500, "Огонь", power: 4m, scale: 15, phase: 0.2),
            M("Пятилистник (p=5.0)", variant, 0m, 0m, 0.8m, 500, "Неон", power: 5m, scale: 15, phase: 0.2),
            M("Долина Трилистника (p=3)", variant, 0.42375m, -0.61425m, 625m, 700, "Ультрафиолет", power: 3m, scale: 2),
            M("Мини-трилистник (p=3)", variant, 0.277125m, 0.73725m, 625m, 700, "Аметист", power: 3m, scale: 2),
            M("Огненная Спираль (p=4)", variant, -0.685875m, -0.313125m, 625m, 700, "Огонь", power: 4m, scale: 2),
            M("Закатные Ветви (p=4)", variant, 0.595875m, 0.672375m, 625m, 700, "Закат", power: 4m, scale: 2),
            M("Ледяная Спираль (p=5)", variant, 0.19425m, 0.69525m, 625m, 700, "Лёд", power: 5m, scale: 2)
        ],
        MandelbrotVariant.Buffalo =>
        [
            M("Классический Буффало", variant, -0.45m, -0.35m, 0.85m, 300, "Огонь", scale: 10, phase: 0.2),
            M("Мини-буффало в пыли", variant, -0.5737m, -0.8407m, 60m, 1000, "Медь", scale: 1),
            M("Силуэт Мини-буффало", variant, -0.5675m, -0.8317m, 150m, 800, "Золото", scale: 1),
            M("Кружевной Хвост", variant, 0.759m, -1.244m, 8m, 600, "Медь", scale: 2, phase: 0.2)
        ],
        MandelbrotVariant.Simonobrot =>
        [
            M("Кристальная пещера (p=5)", variant, 0.835m, -0.5725m, 93.75m, 700, "Океан", power: 5m, scale: 1),
            M("Звезда (p=-2)", variant, 0m, 0m, 0.5m, 500, "Ультрафиолет", power: -2m, scale: 12, phase: 0.2),
            M("Колючка (p=-3, инверсия)", variant, 0.875m, 0.75m, 3m, 500, "Психоделика", power: -3m, useInversion: true),
            M("Четырёхлистник (p=5)", variant, 0.3m, 0m, 1.2m, 500, "Лёд", power: 5m, scale: 12, phase: 0.2),
            M("Огненный Симоноброт (p=2)", variant, -0.3m, 0m, 0.75m, 300, "Огонь", power: 2m, scale: 15, phase: 0.2),
            M("Лиловые Фьорды (p=2)", variant, -0.928m, 0.3955m, 468.8m, 1000, "Космос", power: 2m, scale: 1),
            M("Бирюзовые Лезвия (p=3)", variant, 0.675m, 0.575m, 93.75m, 700, "Бирюза", power: 3m, scale: 1)
        ],
        _ => []
    };

    public static IReadOnlyList<PhoenixState> GetPhoenixPresets() =>
    [
        Phoenix("Классический Феникс", 0.56667m, 0m, -0.5m, 0m, 0m, 0m, 1, 300, "Психоделика"),
        Phoenix("Вихрь комплексной памяти", 0.35m, -0.01m, -0.62m, 0.005m, 0.1m, -0.2m, 1.2, 350, "Психоделика"),
        Phoenix("Хвост павлина", 0.56667m, 0.001m, -0.5m, 0.001m, 0m, 0m, 0.8, 400, "Лёд",
            coloring: PhoenixColoringMode.TriangleInequalityAverage),
        Phoenix("Кубическая корона", 0m, 1m, 0.1m, 0m, 0m, 0m, 1.3, 300, "Ультрафиолет",
            primaryPower: 3, colorPeriod: 30),
        Phoenix("Кубические спирали", 0.62m, 0.1m, -0.2m, 0m, 0m, 0m, 1.1, 300, "Ультрафиолет",
            primaryPower: 3, colorPeriod: 30),
        Phoenix("Параметрическая плоскость C1", 0.56667m, 0m, -0.5m, 0m, -0.25m, 0m, 0.9, 350, "Огонь",
            planeMode: PhoenixPlaneMode.ParameterC1, colorPeriod: 30),
        Phoenix("Трикорн Феникса", 0.24m, 0.08m, -0.46m, 0m, 0m, 0m, 0.9, 400, "Ультрафиолет",
            variant: PhoenixVariant.Tricorn, coloring: PhoenixColoringMode.StripeAverage),
        Phoenix("Горящий Феникс", -0.35m, -0.05m, -0.42m, 0.03m, 0m, 0m, 0.8, 400, "Огонь",
            variant: PhoenixVariant.BurningShip, coloring: PhoenixColoringMode.OrbitTrap)
    ];

    public static IReadOnlyList<SerpinskySaveState> GetSerpinskyPresets() =>
    [
        new()
        {
            SaveName = "Классическая Геометрия", Timestamp = DateTime.MinValue,
            RenderMode = SerpinskyRenderMode.Geometric, Iterations = 8, Zoom = 1,
            CenterX = 0, CenterY = 0.1, FractalColor = Colors.Black, BackgroundColor = Colors.White
        },
        new()
        {
            SaveName = "Ночной Хаос", Timestamp = DateTime.MinValue,
            RenderMode = SerpinskyRenderMode.Chaos, Iterations = 100_000, Zoom = 1,
            CenterX = 0, CenterY = 0.1, FractalColor = Colors.OrangeRed,
            BackgroundColor = Color.FromRgb(10, 0, 20)
        }
    ];

    public static IReadOnlyList<NewtonState> GetNewtonPresets() =>
    [
        Newton("Ньютон: z^3 - 1 (Классика)", "z^3-1", 0, 0, 1, 100,
            Palette("NewtonPreset1_Classic", [Rgb(255,100,100), Rgb(100,255,100), Rgb(100,100,255)], Rgb(20,0,0), false)),
        Newton("Ньютон: z^4 - 1 (Градиент)", "z^4-1", 0, 0, 1.2, 80,
            Palette("NewtonPreset2_Gradient", [Colors.Cyan, Colors.Magenta, Colors.Yellow, Colors.Lime], Colors.Black, true)),
        Newton("Ньютон: z^5 - z^2 + 1", "z^5 - z^2 + 1", 0, 0, 1.5, 120,
            Palette("NewtonPreset3_Complex", [Colors.Orange, Colors.Purple, Colors.GreenYellow, Colors.SkyBlue, Colors.HotPink], Rgb(10,10,30), false)),
        Newton("Ньютон: z^3-2*z+2 (Сдвиг)", "z^3-2*z+2", 0.5, -0.3, 2, 150,
            Palette("NewtonPreset4_Shifted", [Colors.Teal, Colors.Gold, Colors.Crimson], Rgb(5,5,5), true)),
        Newton("Ньютон: z^3-2*z+2 (Цикл {0, 1})", "z^3-2*z+2", 0, 0, 8, 200,
            Palette("NewtonPreset5_Cycle", [Colors.Teal, Colors.Gold, Colors.Crimson], Colors.Black, true)),
        Newton("Ньютон: z^6 - 1 (Шестилепестковая роза)", "z^6-1", 0, 0, 1, 100,
            Palette("NewtonPreset6_Rose", [Colors.Orange, Colors.Purple, Colors.GreenYellow, Colors.SkyBlue, Colors.HotPink, Colors.Lime], Colors.Black, true)),
        Newton("Ньютон: z^8 + 15z^4 - 16", "z^8+15*z^4-16", 0, 0, 0.6, 120,
            Palette("NewtonPreset7_Octic", [Colors.Cyan, Colors.Magenta, Colors.Yellow, Colors.Lime, Colors.Orange, Colors.Purple, Colors.GreenYellow, Colors.SkyBlue], Colors.Black, true)),
        Newton("Галлей: z^3 - 1", "z^3-1", 0, 0, 1, 100,
            Palette("NewtonPreset8_Halley", [Rgb(255,100,100), Rgb(100,255,100), Rgb(100,100,255)], Colors.Black, true),
            NewtonIterationMethod.Halley),
        Newton("Релаксированный Ньютон: z^3 - 1, λ = 1.5", "z^3-1", 0, 0, 1, 150,
            Palette("NewtonPreset9_Relaxed", [Rgb(255,100,100), Rgb(100,255,100), Rgb(100,100,255)], Colors.Black, true),
            NewtonIterationMethod.RelaxedNewton, relaxation: new System.Numerics.Complex(1.5, 0)),
        Newton("Релаксированный Ньютон: z^3 - 1, λ = 1 + 0.5i", "z^3-1", 0, 0, 1, 150,
            Palette("NewtonPreset10_Twisted", [Colors.Orange, Colors.Purple, Colors.GreenYellow], Colors.Black, true),
            NewtonIterationMethod.RelaxedNewton, relaxation: new System.Numerics.Complex(1, 0.5)),
        Newton("Плоскость λ: z^3 - 1, z₀ = 0.5 + 0.5i", "z^3-1", 1, 0, 0.8, 150,
            Palette("NewtonPreset11_Lambda", [Rgb(255,100,100), Rgb(100,255,100), Rgb(100,100,255)], Colors.Black, true),
            NewtonIterationMethod.RelaxedNewton, lambdaPlane: true)
    ];

    public static IReadOnlyList<CollatzState> GetCollatzPresets() =>
    [
        new()
        {
            SaveName = "Стандартный Коллатц", Timestamp = DateTime.MinValue,
            CenterX = 0, CenterY = 0, Zoom = 1, Iterations = 150, Threshold = 100,
            Variation = CollatzVariation.Standard, UseSmoothColoring = false,
            Palette = MandelbrotPalette("Стандартный серый")
        },
        Collatz("Огненные Ловушки", 0m, 0m, 1, "Огонь", coloring: CollatzColoringMode.IntegerTrap),
        Collatz("Солнце над Двойкой", 2m, 0m, 4, "Лава", coloring: CollatzColoringMode.IntegerTrap),
        Collatz("Ледяная Ось", 0m, 0m, 1, "Лёд", coloring: CollatzColoringMode.RealAxisTrap),
        Collatz("Фазовый Узор", 0m, 0m, 1, "Океан", coloring: CollatzColoringMode.FinalArgument),
        Collatz("Серые Купола", 0.3m, 0m, 40, "Стандартный серый"),
        Collatz("Бассейны Циклов", -0.1m, 0m, 3, "Радуга", coloring: CollatzColoringMode.CycleBasins),
        Collatz("Синусная Цепь", 0m, 0m, 1, "Стандартный серый", variation: CollatzVariation.SineVariation),
        Collatz("Неоновая Ось Синуса", 0m, 0m, 1, "Неон", variation: CollatzVariation.SineVariation,
            coloring: CollatzColoringMode.RealAxisTrap),
        Collatz("Золотые Ловушки C(p, q)", 0m, 0m, 1, "Золото", variation: CollatzVariation.GeneralizedPQ,
            coloring: CollatzColoringMode.IntegerTrap, q: (0m, 0.5m))
    ];

    private static CollatzState Collatz(string name, decimal centerX, decimal centerY, double zoom, string paletteName,
        CollatzVariation variation = CollatzVariation.Standard, CollatzColoringMode coloring = CollatzColoringMode.EscapeTime,
        (decimal Real, decimal Imaginary)? q = null)
    {
        var manager = new CollatzPaletteManager();
        MandelbrotPalette template = manager.Palettes.FirstOrDefault(palette =>
            palette.Name.Equals(paletteName, StringComparison.OrdinalIgnoreCase)) ?? manager.Palettes[0];
        return new CollatzState
        {
            SaveName = name, Timestamp = DateTime.MinValue, CenterX = centerX, CenterY = centerY, Zoom = zoom,
            Iterations = 150, Threshold = 100, Variation = variation, ColoringMode = coloring, UseSmoothColoring = true,
            QRealParameter = q?.Real ?? 0m, QImaginaryParameter = q?.Imaginary ?? 0m, Palette = template.Clone(template.Name)
        };
    }

    // У Nova сходящиеся точки закрашены цветом внутренности, и при пороге по умолчанию (10) кадр
    // почти целиком чёрный с редкими пузырями. Порог 2–3 раздувает убегающие области — пузыри и
    // кольца вокруг множества становятся видны.
    public static IReadOnlyList<NovaState> GetNovaPresets(NovaVariant variant) => variant switch
    {
        NovaVariant.Mandelbrot =>
        [
            Nova("Классическая Нова", variant, 0m, 0m, 1, 200, "Огонь", threshold: 3m),
            Nova("Огненный Веер", variant, -0.4m, 0m, 3, 200, "Огонь", threshold: 3m),
            Nova("Закатная Нова (p=5)", variant, 0m, 0m, 1, 200, "Закат", power: 5m, threshold: 3m),
            Nova("Кислотная Нова (p=3+0.5i)", variant, 0m, 0m, 1, 200, "Неон", powerImaginary: 0.5m, threshold: 3m),
            Nova("Океанская Нова (m=0.6)", variant, 0m, 0m, 1.2, 200, "Океан", relaxation: 0.6m, threshold: 3m),
            Nova("Ледяные Пузыри (p=4)", variant, -0.28125m, -0.328125m, 16, 200, "Лёд", power: 4m, threshold: 3m),
            Nova("Бирюзовая Пена (p=4)", variant, -0.453125m, -0.109375m, 16, 200, "Бирюза", power: 4m, threshold: 3m),
            Nova("Космические Пузыри (p=4)", variant, -0.25m, 0.2695312m, 64, 260, "Космос", power: 4m, threshold: 3m)
        ],
        _ =>
        [
            Nova("Огненные Кольца", variant, 0m, 0m, 1, 200, "Огонь", c: (-0.5m, 0m), threshold: 2m),
            Nova("Золотой Трезубец", variant, 0m, 0m, 1, 200, "Золото", c: (0.1m, 0m), threshold: 2m),
            Nova("Закатный Хоровод", variant, 0m, 0m, 1, 200, "Закат", c: (0.5m, 0.1m), threshold: 3m),
            Nova("Лавовые Пузыри", variant, 0m, 0m, 1.5, 200, "Лава", c: (0.3m, 0.3m), threshold: 3m),
            Nova("Синяя Пена (p=4)", variant, 0m, 0m, 1, 200, "Лёд", power: 4m, c: (-0.3m, 0.1m), threshold: 3m),
            Nova("Неоновые Завитки (p=3+0.5i)", variant, 0m, 0m, 1, 200, "Неон", powerImaginary: 0.5m, c: (0.2m, 0m), threshold: 3m),
            Nova("Океанская Пена (p=5)", variant, 0m, 0m, 1, 200, "Океан", power: 5m, c: (0.2m, 0.2m), threshold: 3m)
        ]
    };

    // Мнимая ось Буддаброта горизонтальна, вещественная — вертикальна (фигура «сидит»). Палитры с
    // логарифмическим режимом поверх логарифмической плотности выжигают фон обычного Буддаброта,
    // поэтому для него взяты линейные и корневые палитры.
    public static IReadOnlyList<BuddhabrotState> GetBuddhabrotPresets() =>
    [
        Buddhabrot("Медный Будда", -0.4m, 0m, 1.2m, 500, 1_000_000, "Медь"),
        Buddhabrot("Огненный Анти-Буддаброт", -0.4m, 0m, 1.2m, 500, 1_000_000, "Огонь", BuddhabrotRenderMode.AntiBuddhabrot),
        Buddhabrot("Неоновый Анти-Буддаброт", -0.4m, 0m, 1.2m, 500, 1_000_000, "Неон", BuddhabrotRenderMode.AntiBuddhabrot),
        Buddhabrot("Серебряная Симметрия", -0.4m, 0m, 1.2m, 500, 1_000_000, "Стандартный серый", BuddhabrotRenderMode.SymmetricBuddhabrot),
        Buddhabrot("Призрак (20 итераций)", -0.4m, 0m, 1.2m, 20, 1_000_000, "Сепия"),
        Buddhabrot("Глубокие Орбиты (5000 итераций)", -0.4m, 0m, 1.2m, 5000, 1_500_000, "Лава"),
        Buddhabrot("Голова Будды", -1.2m, 0m, 3m, 1000, 2_000_000, "Медь"),
        Buddhabrot("Орбиты верхнего края кардиоиды", -0.4m, 0m, 1.2m, 1000, 1_000_000, "Радуга",
            sampleArea: (-0.8m, 0.4m, 0.3m, 1m)),
        Buddhabrot("Орбиты антенны", -0.4m, 0m, 1.2m, 2000, 1_500_000, "Лава",
            sampleArea: (-2m, -1.4m, -0.2m, 0.2m))
    ];

    private static BuddhabrotState Buddhabrot(string name, decimal centerX, decimal centerY, decimal zoom, int iterations,
        int samples, string paletteName, BuddhabrotRenderMode mode = BuddhabrotRenderMode.Buddhabrot,
        (decimal MinRe, decimal MaxRe, decimal MinIm, decimal MaxIm)? sampleArea = null)
    {
        var manager = new BuddhabrotPaletteManager();
        BuddhabrotColorPalette template = manager.Palettes.FirstOrDefault(palette => palette.Name == paletteName) ?? manager.Palettes[0];
        var area = sampleArea ?? (-2m, 1m, -1.5m, 1.5m);
        return new BuddhabrotState
        {
            SaveName = name, Timestamp = DateTime.MinValue, CenterX = centerX, CenterY = centerY, Zoom = zoom,
            MaxIterations = iterations, SampleCount = samples, RenderMode = mode,
            SampleMinRe = area.MinRe, SampleMaxRe = area.MaxRe, SampleMinIm = area.MinIm, SampleMaxIm = area.MaxIm,
            Palette = template.Clone(template.Name, template.IsBuiltIn)
        };
    }

    public static IReadOnlyList<IfsState> GetIfsPresets() => IfsPresets.All.Select(preset => new IfsState
    {
        SaveName = preset.Name, Timestamp = DateTime.MinValue, PointOfInterestId = preset.Id,
        Iterations = preset.Iterations, CenterX = preset.CenterX, CenterY = preset.CenterY, Scale = preset.Scale,
        Transforms = preset.Transforms.Select(transform => transform.Clone()).ToList(),
        FractalColor = Colors.Lime, BackgroundColor = Colors.Black
    }).ToList();

    public static IReadOnlyList<Ifs3DState> GetIfs3DPresets() => Ifs3DPresets.All.Select(preset =>
    {
        Ifs3DState state = preset.State.Clone(preset.Name);
        state.Timestamp = DateTime.MinValue;
        state.PointOfInterestId = preset.Id;
        return state;
    }).ToList();

    public static IReadOnlyList<FlameState> GetFlamePresets() =>
    [
        F("Огненный лист",0,.1,4.2,1_500_000,22,24,1.42,2.15,
            T(1,.53,.03,-.34,-.02,.55,-.03,FlameVariation.Linear,Colors.OrangeRed),
            T(.94,.51,-.03,.33,.02,.50,0,FlameVariation.Sinusoidal,Colors.Gold),
            T(.66,.47,0,.01,0,.44,.39,FlameVariation.Spherical,Colors.DeepSkyBlue)),
        F("Ледяная бабочка",-.05,0,3.7,1_800_000,24,26,1.35,2.30,
            T(1,.62,-.08,-.36,.08,.62,-.03,FlameVariation.Sinusoidal,Colors.Cyan),
            T(1,.62,.08,.36,-.08,.62,-.03,FlameVariation.Sinusoidal,Colors.MediumPurple),
            T(.55,.45,0,0,0,.45,.45,FlameVariation.Spherical,Colors.WhiteSmoke)),
        F("Галактический вихрь",0,-.05,5,2_200_000,26,28,1.28,2.25,
            T(.92,.78,-.18,-.05,.18,.78,.04,FlameVariation.Linear,Colors.MediumOrchid),
            T(.92,.78,.18,.05,-.18,.78,.04,FlameVariation.Sinusoidal,Colors.DeepPink),
            T(.40,.31,0,0,0,.31,-.62,FlameVariation.Spherical,Colors.LightSkyBlue)),
        F("Световые лепестки",.02,.08,3.6,1_700_000,23,24,1.45,2.10,
            T(.95,.58,-.22,-.21,.22,.58,-.02,FlameVariation.Linear,Colors.HotPink),
            T(.95,.58,.22,.21,-.22,.58,-.02,FlameVariation.Sinusoidal,Colors.Orange),
            T(.58,.39,0,0,0,.39,.51,FlameVariation.Spherical,Colors.Aqua)),
        F("Симметричный кристалл",0,0,4.6,2_200_000,24,26,1.34,2.18,
            T(1,.64,-.14,-.32,.14,.64,0,FlameVariation.Linear,Colors.AliceBlue),
            T(1,.64,.14,.32,-.14,.64,0,FlameVariation.Linear,Colors.SkyBlue),
            T(.62,.44,0,0,0,.44,.50,FlameVariation.Sinusoidal,Colors.Plum)),
        F("Туманность Андромеды",.03,-.08,5.3,2_800_000,27,30,1.18,2.34,
            T(.86,.81,-.24,-.07,.24,.81,.03,FlameVariation.Sinusoidal,Colors.MediumPurple),
            T(.86,.81,.24,.07,-.24,.81,.03,FlameVariation.Sinusoidal,Colors.DeepPink),
            T(.40,.28,0,.02,0,.28,-.66,FlameVariation.Spherical,Colors.LightCyan)),
        F("Папоротник рассвета",-.01,-.22,3.4,2_100_000,23,26,1.50,2.08,
            T(1.12,.83,.04,0,-.04,.86,.18,FlameVariation.Linear,Colors.ForestGreen),
            T(.74,.32,-.30,-.21,.26,.30,.24,FlameVariation.Sinusoidal,Colors.LawnGreen),
            T(.66,.32,.30,.21,-.26,.30,.24,FlameVariation.Sinusoidal,Colors.Gold),
            T(.24,.14,0,0,0,.16,-.56,FlameVariation.Spherical,Colors.LightSkyBlue)),
        F("Контрастный неон",0,0,4,2_500_000,25,28,1.65,1.95,
            T(1,.60,-.26,-.28,.26,.60,-.01,FlameVariation.Linear,Colors.Fuchsia),
            T(1,.60,.26,.28,-.26,.60,-.01,FlameVariation.Sinusoidal,Colors.Cyan),
            T(.54,.40,0,0,0,.40,.56,FlameVariation.Spherical,Colors.Yellow)),
        F("Храмовая мандала",0,.03,4.8,2_400_000,25,27,1.30,2.20,
            T(.96,.70,-.12,-.24,.12,.70,0,FlameVariation.Linear,Colors.Goldenrod),
            T(.96,.70,.12,.24,-.12,.70,0,FlameVariation.Linear,Colors.OrangeRed),
            T(.52,.36,0,0,0,.36,.52,FlameVariation.Sinusoidal,Colors.LightGoldenrodYellow),
            T(.30,.28,0,0,0,.28,-.64,FlameVariation.Spherical,Colors.MediumPurple)),
        F("Полярное сияние",-.02,-.04,4.4,2_300_000,24,27,1.38,2.16,
            T(.92,.66,-.21,-.22,.18,.69,-.04,FlameVariation.Sinusoidal,Colors.Aquamarine),
            T(.92,.66,.21,.22,-.18,.69,-.04,FlameVariation.Sinusoidal,Colors.SpringGreen),
            T(.44,.34,0,0,0,.34,.62,FlameVariation.Spherical,Colors.DeepSkyBlue)),
        F("Ртутный вихрь",.01,-.01,5.1,2_700_000,27,30,1.22,2.28,
            T(.90,.79,-.19,-.04,.19,.79,.05,FlameVariation.Linear,Colors.Silver),
            T(.90,.79,.19,.04,-.19,.79,.05,FlameVariation.Sinusoidal,Colors.LightSteelBlue),
            T(.38,.30,0,0,0,.30,-.68,FlameVariation.Spherical,Colors.WhiteSmoke)),
        F("Пламя",0,-.16,3.25,2_900_000,27,31,1.74,1.92,
            T(1.26,.84,0,0,0,.46,.34,FlameVariation.Linear,Colors.OrangeRed),
            T(.95,.57,-.27,-.20,.23,.56,.07,FlameVariation.Sinusoidal,Colors.Gold),
            T(.95,.57,.27,.20,-.23,.56,.07,FlameVariation.Sinusoidal,Colors.Orange),
            T(.33,.28,0,0,0,.30,-.73,FlameVariation.Spherical,Colors.DodgerBlue))
    ];

    /// <param name="scale">Ускорение цикла палитры; если задано, палитра отражается (Mirror) и сдвигается на <paramref name="phase"/>.</param>
    private static MandelbrotState M(string name, MandelbrotVariant variant, decimal centerX, decimal centerY,
        decimal zoom, int iterations, string paletteName, decimal power = 2m, bool useInversion = false,
        decimal juliaReal = 0m, decimal juliaImaginary = 0m, double? scale = null, double phase = 0.15)
    {
        MandelbrotPalette palette = MandelbrotPalette(paletteName);
        return new MandelbrotState
        {
            SaveName = name, Timestamp = DateTime.MinValue, Variant = variant, CenterX = centerX, CenterY = centerY,
            Zoom = (double)zoom, Iterations = iterations, Threshold = 2m, ColoringMode = MandelbrotColoringMode.Smooth,
            PaletteName = paletteName, Palette = palette, Power = power, UseInversion = useInversion,
            JuliaCReal = juliaReal, JuliaCImaginary = juliaImaginary, InteriorColor = palette.InteriorColor,
            PaletteScale = scale ?? 1, PalettePhaseOffset = scale is null ? 0 : phase,
            PaletteWrapMode = scale is null ? MandelbrotPaletteWrapMode.Repeat : MandelbrotPaletteWrapMode.Mirror
        };
    }

    private static PhoenixState Phoenix(string name, decimal c1Real, decimal c1Imaginary, decimal c2Real,
        decimal c2Imaginary, decimal centerX, decimal centerY, double zoom, int iterations, string paletteName,
        PhoenixPlaneMode planeMode = PhoenixPlaneMode.Julia, PhoenixVariant variant = PhoenixVariant.Classic,
        int primaryPower = 2, int secondaryPower = 0,
        PhoenixColoringMode coloring = PhoenixColoringMode.Smooth, int? colorPeriod = null) => new()
    {
        SaveName = name, Timestamp = DateTime.MinValue, C1Real = c1Real, C1Imaginary = c1Imaginary,
        C2Real = c2Real, C2Imaginary = c2Imaginary, CenterX = centerX, CenterY = centerY,
        Zoom = zoom, Iterations = iterations, Threshold = 4m, PlaneMode = planeMode, Variant = variant,
        PrimaryPower = primaryPower, SecondaryPower = secondaryPower, ColoringMode = coloring,
        // Окно берёт палитру сохранения целиком, поэтому укороченный период (у встроенных — сотни
        // итераций) доезжает до рендера и не даёт кадру уйти в чёрное начало палитры.
        Palette = MandelbrotPalette(paletteName, colorPeriod)
    };

    private static NovaState Nova(string name, NovaVariant variant, decimal centerX, decimal centerY, double zoom,
        int iterations, string paletteName, decimal power = 3m, decimal powerImaginary = 0m, decimal relaxation = 1m,
        (decimal Real, decimal Imaginary)? c = null, decimal threshold = 10m)
    {
        var manager = new NovaPaletteManager();
        MandelbrotPalette template = manager.Palettes.FirstOrDefault(palette =>
            palette.Name.Equals(paletteName, StringComparison.OrdinalIgnoreCase)) ?? manager.Palettes[0];
        return new NovaState
        {
            SaveName = name, Timestamp = DateTime.MinValue, Variant = variant,
            FractalType = variant == NovaVariant.Julia ? "NovaJulia" : "NovaMandelbrot",
            CenterX = centerX, CenterY = centerY, Zoom = zoom, Iterations = iterations, Threshold = threshold,
            PReal = power, PImaginary = powerImaginary, Z0Real = 1m, Z0Imaginary = 0m, M = relaxation,
            CReal = c?.Real ?? 0m, CImaginary = c?.Imaginary ?? 1m, UseSmoothColoring = true,
            Palette = template.Clone(template.Name)
        };
    }

    private static NewtonState Newton(string name, string formula, double centerX, double centerY,
        double zoom, int iterations, NewtonColorPalette palette,
        NewtonIterationMethod method = NewtonIterationMethod.Newton, System.Numerics.Complex? relaxation = null,
        bool lambdaPlane = false) => new()
    {
        SaveName = name, Timestamp = DateTime.MinValue, Formula = formula, CenterX = centerX, CenterY = centerY,
        Zoom = zoom, MaxIterations = iterations, IterationMethod = method,
        HouseholderOrder = 3, Palette = palette, Relaxation = relaxation ?? System.Numerics.Complex.One,
        RelaxedPlaneMode = lambdaPlane ? NewtonRelaxedPlaneMode.LambdaPlane : NewtonRelaxedPlaneMode.ZPlane
    };

    private static NewtonColorPalette Palette(string name, List<Color> colors, Color background, bool gradient) => new()
    {
        Name = name, RootColors = colors, BackgroundColor = background, IsGradient = gradient
    };

    private static FlameState F(string name, double centerX, double centerY, double scale, int samples,
        int iterations, int warmup, double exposure, double gamma, params FlameTransform[] transforms) => new()
    {
        SaveName = name, Timestamp = DateTime.MinValue, CenterX = centerX, CenterY = centerY, Scale = scale,
        Samples = samples, IterationsPerSample = iterations, WarmupIterations = warmup,
        Exposure = exposure, Gamma = gamma, Transforms = transforms.ToList()
    };

    private static FlameTransform T(double weight, double a, double b, double c, double d, double e,
        double f, FlameVariation variation, Color color) => new()
    {
        Weight = weight, A = a, B = b, C = c, D = d, E = e, F = f, Variation = variation, Color = color
    };

    private static MandelbrotPalette MandelbrotPalette(string name, int? colorPeriod = null)
    {
        var manager = new MandelbrotPaletteManager();
        MandelbrotPalette template = manager.Palettes.FirstOrDefault(palette =>
            palette.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? manager.Palettes[0];
        MandelbrotPalette palette = template.Clone(template.Name);
        if (colorPeriod is int period) palette.ColorPeriod = period;
        return palette;
    }

    private static Color Rgb(byte red, byte green, byte blue) => Color.FromRgb(red, green, blue);
}
