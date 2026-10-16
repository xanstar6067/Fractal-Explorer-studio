using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models;

public enum MathematicalLaboratoryKind
{
    ModularArithmetic,
    PascalModulo,
    RationalNumbers,
    PrimeGeometry,
    Phyllotaxis,
    CircleInversion,
    AperiodicTilings,
    HyperbolicGeometry,
    FourierEpicycles,
    ChladniWaveInterference,
    VoronoiLloyd,
    RecamanSequence,
    KnotStudio,
    StochasticMotion,
    KleinianSchottky,
    Rauzy
}

public readonly record struct LaboratoryPoint(double X, double Y);

public sealed class MathematicalLaboratoryState
{
    public string SaveName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public MathematicalLaboratoryKind Kind { get; set; }
    public int Mode { get; set; }
    public int PrimaryValue { get; set; }
    public int SecondaryValue { get; set; }
    public int TertiaryValue { get; set; }
    public double Parameter { get; set; }
    public double Phase { get; set; }
    public bool ShowGuides { get; set; } = true;
    public bool Filled { get; set; } = true;
    public bool Animate { get; set; }
    public double ViewCenterX { get; set; }
    public double ViewCenterY { get; set; }
    public double Zoom { get; set; } = 1;
    public double Rotation { get; set; }
    public double AnchorX { get; set; }
    public double AnchorY { get; set; }
    public Color BackgroundColor { get; set; } = Color.FromRgb(7, 13, 27);
    public Color PrimaryColor { get; set; } = Color.FromRgb(34, 211, 238);
    public Color SecondaryColor { get; set; } = Color.FromRgb(244, 63, 94);
    public Color AccentColor { get; set; } = Color.FromRgb(250, 204, 21);
    public List<LaboratoryPoint> InputPoints { get; set; } = [];

    public MathematicalLaboratoryState Clone(string? name = null) => new()
    {
        SaveName = name ?? SaveName,
        Timestamp = Timestamp,
        Kind = Kind,
        Mode = Mode,
        PrimaryValue = PrimaryValue,
        SecondaryValue = SecondaryValue,
        TertiaryValue = TertiaryValue,
        Parameter = Parameter,
        Phase = Phase,
        ShowGuides = ShowGuides,
        Filled = Filled,
        Animate = Animate,
        ViewCenterX = ViewCenterX,
        ViewCenterY = ViewCenterY,
        Zoom = Zoom,
        Rotation = Rotation,
        AnchorX = AnchorX,
        AnchorY = AnchorY,
        BackgroundColor = BackgroundColor,
        PrimaryColor = PrimaryColor,
        SecondaryColor = SecondaryColor,
        AccentColor = AccentColor,
        InputPoints = [.. InputPoints]
    };
}

public sealed record MathematicalLaboratoryDefinition(
    string Title,
    string Description,
    string[] Modes,
    string PrimaryLabel,
    string SecondaryLabel,
    string TertiaryLabel,
    string ParameterLabel,
    string InteractionHint,
    int PrimaryMinimum,
    int PrimaryMaximum,
    int SecondaryMinimum,
    int SecondaryMaximum,
    int TertiaryMinimum,
    int TertiaryMaximum,
    double ParameterMinimum,
    double ParameterMaximum);

public static class MathematicalLaboratoryCatalog
{
    public const string LaunchPrefix = "MathLab:";

    public static string LaunchKey(MathematicalLaboratoryKind kind) => LaunchPrefix + kind;

    public static bool TryParseLaunchKey(string? launchKey, out MathematicalLaboratoryKind kind)
    {
        kind = default;
        return launchKey?.StartsWith(LaunchPrefix, StringComparison.Ordinal) == true &&
               Enum.TryParse(launchKey[LaunchPrefix.Length..], out kind);
    }

    public static MathematicalLaboratoryDefinition GetDefinition(MathematicalLaboratoryKind kind) => kind switch
    {
        MathematicalLaboratoryKind.ModularArithmetic => new(
            "Арифметика по модулю",
            "Отображения конечного кольца на окружности: таблицы умножения и функциональные графы превращаются в кардиоиды, нефроиды и плетёные орбиты.",
            ["x → ax mod N", "x → ax+b mod N", "x → x²+c mod N", "x → xᵏ mod N", "Collatz modulo N"],
            "Модуль N", "Коэффициент a", "b / c / степень k", "Толщина линий",
            "Колесо: масштаб. ЛКМ: перемещение. Анимация изменяет коэффициент a.",
            10, 2_000, -10_000, 10_000, -10_000, 10_000, 0.25, 8),
        MathematicalLaboratoryKind.PascalModulo => new(
            "Треугольник Паскаля по модулю N",
            "Остатки биномиальных коэффициентов образуют самоподобные структуры. Для mod 2 непосредственно проявляется треугольник Серпинского.",
            ["Цвет каждого остатка", "Делится / не делится", "Сетка теоремы Люка"],
            "Число строк", "Модуль", "Размер ячейки", "Контраст",
            "Колесо: масштаб. ЛКМ: перемещение. Для крупных треугольников используется растровое уплотнение.",
            8, 2_000, 2, 256, 1, 8, 0.2, 2.5),
        MathematicalLaboratoryKind.RationalNumbers => new(
            "Лаборатория рациональных чисел",
            "Деревья Штерна—Броко и Калкина—Уилфа, последовательности Фарея, окружности Форда и приближения цепными дробями в одном пространстве.",
            ["Дерево Штерна—Броко", "Дерево Калкина—Уилфа", "Последовательность Фарея", "Окружности Форда", "Цепная дробь"],
            "Глубина дерева", "Макс. знаменатель", "Лимит элементов", "Целевое число",
            "Колесо: масштаб. ЛКМ: перемещение. В режиме цепной дроби параметр задаёт приближаемое число.",
            1, 13, 2, 300, 20, 30_000, 0.000001, 1000),
        MathematicalLaboratoryKind.PrimeGeometry => new(
            "Геометрия простых чисел",
            "Простые числа на квадратных, полярных и алгебраических решётках. Цвет показывает класс вычетов, а направляющие помогают увидеть цепочки.",
            ["Спираль Улама", "Спираль Сакса", "Шестиугольная спираль", "Простые Гаусса", "Простые Эйзенштейна"],
            "Радиус / размер", "Модуль цвета", "Константа полинома", "Размер точки",
            "Колесо: масштаб. ЛКМ: перемещение. Жёлтым отмечаются значения n²+n+c.",
            8, 260, 2, 64, -200, 200, 0.5, 8),
        MathematicalLaboratoryKind.Phyllotaxis => new(
            "Филлотаксис и иррациональные вращения",
            "Точки с постоянным угловым шагом показывают парастихии золотого угла и распад структуры около рациональных вращений.",
            ["Золотой угол", "Угол π", "Угол √2", "Рациональное вращение", "Пользовательский угол"],
            "Число точек", "Числитель", "Знаменатель", "Угол, градусы",
            "Колесо: масштаб. ЛКМ: перемещение. Анимация медленно меняет угол вращения.",
            50, 50_000, 1, 10_000, 1, 10_000, -3600, 3600),
        MathematicalLaboratoryKind.CircleInversion => new(
            "Инверсия окружностей и преобразования Мёбиуса",
            "Инверсия переводит прямые и окружности друг в друга; комплексные дробно-линейные отображения продолжают ту же геометрию.",
            ["Инверсия точек", "Инверсия окружностей", "Повторные инверсии", "Преобразование Мёбиуса"],
            "Число объектов", "Число повторов", "Симметрия", "Радиус инверсии",
            "ЛКМ: добавить точку. ПКМ: перенести центр инверсии. Shift+ЛКМ: перемещение. Очистка возвращает исходный узор.",
            3, 2_000, 1, 12, 2, 24, 0.05, 2),
        MathematicalLaboratoryKind.AperiodicTilings => new(
            "Подстановочные и апериодические мозаики",
            "Инфляция и дефляция раскрывают иерархию мозаик Пенроуза, Амманна—Бинкера, Chair, Pinwheel, сфинкса и Фибоначчи.",
            ["Penrose P2/P3", "Ammann—Beenker", "Chair tiling", "Pinwheel tiling", "Мозаика сфинкса", "Треугольники Фибоначчи"],
            "Поколение", "Число секторов", "Вариант правила", "Толщина границ",
            "Колесо: масштаб. ЛКМ: перемещение. Переключатель «Заливка» оставляет только границы плиток.",
            1, 9, 3, 32, 0, 8, 0.25, 6),
        MathematicalLaboratoryKind.HyperbolicGeometry => new(
            "Гиперболическая геометрия",
            "Мозаики {p,q}, геодезические и идеальная граница в модели диска Пуанкаре. Евклидов размер плиток уменьшается к краю диска.",
            ["Мозаика {3,7}", "Мозаика {4,5}", "Мозаика {6,4}", "Пользовательская {p,q}", "Геодезические"],
            "Глубина", "Параметр p", "Параметр q", "Кривизна вида",
            "Колесо: масштаб. ЛКМ: перемещение. Для гиперболической мозаики требуется (p−2)(q−2) > 4.",
            1, 9, 3, 12, 3, 12, 0.15, 1.5),
        MathematicalLaboratoryKind.FourierEpicycles => new(
            "Fourier Epicycles",
            "Замкнутый контур раскладывается в дискретный ряд Фурье и восстанавливается цепочкой вращающихся окружностей.",
            ["Гармоники по частоте", "Гармоники по амплитуде", "Спектр коэффициентов"],
            "Число гармоник", "Число отсчётов", "Точек траектории", "Скорость анимации",
            "Зажмите ЛКМ и нарисуйте замкнутый контур. Shift+ЛКМ перемещает вид. Анимация ведёт эпициклы по траектории.",
            1, 250, 32, 2_048, 50, 4_000, 0.05, 4),
        MathematicalLaboratoryKind.ChladniWaveInterference => new(
            "Фигуры Хладни и интерференция волн",
            "Узловые линии стоячих волн на пластинах и мембранах, а также интерференция когерентных точечных источников и решёток щелей.",
            ["Квадратная пластина — симметричная мода", "Квадратная пластина — антисимметричная мода",
                "Круглая мембрана", "Два когерентных источника", "Кольцо источников", "Дифракционная решётка"],
            "Номер моды m", "Номер моды n", "Контурные полосы", "Порог узлов",
            "Колесо: масштаб. ЛКМ: перемещение. Анимация показывает фазу стоячей или бегущей волны.",
            0, 64, 1, 128, 1, 360, 0.005, 2),
        MathematicalLaboratoryKind.VoronoiLloyd => new(
            "Диаграммы Вороного и релаксация Ллойда",
            "Ячейки ближайшего центра для разных метрик, power diagram, двойственная сеть Делоне и пошаговое выравнивание центров методом Ллойда.",
            ["Вороной — евклидова метрика", "Релаксация Ллойда", "Power diagram — взвешенные центры",
                "Вороной — манхэттенская метрика", "Вороной + двойственная сеть Делоне"],
            "Число центров", "Итерации Ллойда", "Seed", "Толщина границ",
            "ЛКМ: добавить собственный центр (при наличии ручных центров случайные не используются). Shift+ЛКМ: перемещение. «Очистить» возвращает случайный набор.",
            3, 256, 0, 80, int.MinValue, int.MaxValue, 0.25, 8),
        MathematicalLaboratoryKind.RecamanSequence => new(
            "Последовательность Рекамана",
            "Самоизбегающие шаги a(n)=a(n−1)−n, если результат положителен и ещё не встречался; иначе выполняется шаг вперёд. Несколько раскладок показывают дуги и повторные масштабы.",
            ["Чередующиеся дуги", "Дуги над осью", "Хордовая диаграмма", "Плоское блуждание"],
            "Число членов", "Начальное значение", "Период цвета", "Толщина линий",
            "Колесо: масштаб. ЛКМ: перемещение. Анимация последовательно раскрывает члены последовательности.",
            10, 20_000, 0, 1_000_000, 1, 2_000, 0.25, 10),
        MathematicalLaboratoryKind.KnotStudio => new(
            "Лаборатория узлов и кос",
            "Параметрические торические, Лиссажу- и гармонические узлы, а также замкнутые косы с псевдо‑трёхмерной проекцией и сортировкой сегментов по глубине.",
            ["Торический узел T(p,q)", "Узел Лиссажу", "Замкнутая коса", "Гармонический узел"],
            "Параметр p / пряди", "Параметр q / обороты", "Число отсчётов", "Толщина нити",
            "Колесо: масштаб. ЛКМ: перемещение. Поворот меняет проекцию, анимация вращает узел в пространстве. Для торического узла НОД(p,q) задаёт число компонент.",
            1, 32, 1, 64, 200, 30_000, 0.4, 20),
        MathematicalLaboratoryKind.StochasticMotion => new(
            "Brownian motion и Lévy flights",
            "Отдельная лаборатория случайных траекторий: броуновское движение, полёты Леви, броуновские мосты, ансамбли частиц и коррелированные блуждания с воспроизводимым seed.",
            ["Броуновские траектории", "Полёты Леви", "Броуновский мост", "Ансамбль частиц", "Коррелированное блуждание"],
            "Число шагов", "Траектории / частицы", "Seed", "Диффузия / α Леви",
            "ПКМ задаёт начальную точку. Колесо: масштаб. ЛКМ: перемещение. Анимация раскрывает траектории во времени; seed позволяет повторить эксперимент.",
            20, 200_000, 1, 1_000, int.MinValue, int.MaxValue, 0.2, 2),
        MathematicalLaboratoryKind.KleinianSchottky => new(
            "Kleinian и Schottky groups",
            "Предельные множества групп дробно-линейных и круговых преобразований: классические конфигурации Шоттки, деформации, двухпараболические орбиты и инверсионная группа Аполлона.",
            ["Классическая группа Шоттки", "Спиральная деформация Шоттки", "Двухпараболическая орбита",
                "Инверсионная группа Аполлона", "Окружности генераторов и предел"],
            "Число точек", "Прогрев / глубина", "Seed", "Параметр деформации",
            "Колесо: масштаб. ЛКМ: перемещение. Направляющие показывают порождающие окружности. Seed выбирает воспроизводимую орбиту.",
            2_000, 2_000_000, 1, 200, int.MinValue, int.MaxValue, 0.05, 3),
        MathematicalLaboratoryKind.Rauzy => new(
            "Фрактал Раузи · плитки Трибоначчи",
            "Проекция слова a → aᵏb, b → ac, c → a на сжимающую плоскость. При k = 1 — классический фрактал Раузи: три под-плитки с самоподобной границей и периодическая мозаика.",
            ["Три под-плитки", "Под-плитка a", "Под-плитка b", "Под-плитка c", "Периодическая мозаика", "Порядок построения"],
            "Точек на кадр", "Подстановка k", "Радиус мозаики", "Радиус точки, px",
            "k = 1: Трибоначчи; k = 2…4: родственные подстановки. Радиус мозаики r даёт (2r+1)² плиток. Анимация раскрывает префиксы слова, цвет соответствует следующей букве. Колесо: масштаб. ЛКМ: перемещение.",
            100, 2_000_000, 1, 4, 1, 4, 0.3, 2.5),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static MathematicalLaboratoryState CreateDefaultState(MathematicalLaboratoryKind kind)
    {
        var state = new MathematicalLaboratoryState { Kind = kind };
        switch (kind)
        {
            case MathematicalLaboratoryKind.ModularArithmetic:
                state.PrimaryValue = 240; state.SecondaryValue = 2; state.TertiaryValue = 0; state.Parameter = 1.1;
                break;
            case MathematicalLaboratoryKind.PascalModulo:
                state.PrimaryValue = 360; state.SecondaryValue = 5; state.TertiaryValue = 1; state.Parameter = 1;
                break;
            case MathematicalLaboratoryKind.RationalNumbers:
                state.PrimaryValue = 8; state.SecondaryValue = 36; state.TertiaryValue = 8_000; state.Parameter = Math.Sqrt(2);
                break;
            case MathematicalLaboratoryKind.PrimeGeometry:
                state.PrimaryValue = 70; state.SecondaryValue = 6; state.TertiaryValue = 41; state.Parameter = 2.2;
                break;
            case MathematicalLaboratoryKind.Phyllotaxis:
                state.PrimaryValue = 5_000; state.SecondaryValue = 1; state.TertiaryValue = 7;
                state.Parameter = 137.50776405003785;
                break;
            case MathematicalLaboratoryKind.CircleInversion:
                state.PrimaryValue = 72; state.SecondaryValue = 5; state.TertiaryValue = 7; state.Parameter = 0.62;
                break;
            case MathematicalLaboratoryKind.AperiodicTilings:
                state.PrimaryValue = 5; state.SecondaryValue = 10; state.TertiaryValue = 0; state.Parameter = 1.1;
                break;
            case MathematicalLaboratoryKind.HyperbolicGeometry:
                state.PrimaryValue = 5; state.SecondaryValue = 3; state.TertiaryValue = 7; state.Parameter = 0.68;
                break;
            case MathematicalLaboratoryKind.FourierEpicycles:
                state.PrimaryValue = 45; state.SecondaryValue = 512; state.TertiaryValue = 900; state.Parameter = 0.7;
                break;
            case MathematicalLaboratoryKind.ChladniWaveInterference:
                state.PrimaryValue = 5; state.SecondaryValue = 3; state.TertiaryValue = 28; state.Parameter = 0.055;
                state.Filled = true;
                break;
            case MathematicalLaboratoryKind.VoronoiLloyd:
                state.PrimaryValue = 48; state.SecondaryValue = 8; state.TertiaryValue = 1729; state.Parameter = 1.2;
                state.Filled = true;
                break;
            case MathematicalLaboratoryKind.RecamanSequence:
                state.PrimaryValue = 420; state.SecondaryValue = 0; state.TertiaryValue = 24; state.Parameter = 1.35;
                break;
            case MathematicalLaboratoryKind.KnotStudio:
                state.PrimaryValue = 3; state.SecondaryValue = 7; state.TertiaryValue = 1_600; state.Parameter = 5.5;
                state.Rotation = -8;
                break;
            case MathematicalLaboratoryKind.StochasticMotion:
                state.PrimaryValue = 12_000; state.SecondaryValue = 12; state.TertiaryValue = 314159; state.Parameter = 1.25;
                break;
            case MathematicalLaboratoryKind.KleinianSchottky:
                state.PrimaryValue = 280_000; state.SecondaryValue = 24; state.TertiaryValue = 271828; state.Parameter = 1.25;
                state.Zoom = 0.82;
                state.Filled = true;
                break;
            case MathematicalLaboratoryKind.Rauzy:
                state.PrimaryValue = 450_000; state.SecondaryValue = 1; state.TertiaryValue = 1; state.Parameter = 0.85;
                state.ShowGuides = false;
                break;
        }
        return state;
    }
}
