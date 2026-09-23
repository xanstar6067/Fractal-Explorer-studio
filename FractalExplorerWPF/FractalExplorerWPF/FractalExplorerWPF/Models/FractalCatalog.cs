namespace FractalExplorerWPF.Models;

public static class FractalCatalog
{
    /// <summary>Запасная картинка для пунктов, превью которых рендерится на лету.</summary>
    private const string Placeholder3D = "temporary_preview_sq512.png";

    public static IReadOnlyList<FractalCatalogItem> Create() =>
    [
        Item(["Фракталы", "Комплексная динамика", "Семейство Мандельброта"], "Классический Мандельброт", "Классическое множество Z = Z² + C с глубоким масштабированием и настраиваемым окрашиванием.", "mandelbrot_preview_sq512.png"),
        Item(["Фракталы", "Комплексная динамика", "Семейство Мандельброта"], "Горящий Корабль", "Вариация Мандельброта с модулями компонент комплексного числа перед итерацией.", "burningship_preview_sq512.png"),
        Item(["Фракталы", "Комплексная динамика", "Семейство Мандельброта"], "Трикорн (Mandelbar)", "Антиголоморфная вариация с комплексным сопряжением.", "tricorn__preview_sq512.png"),
        Item(["Фракталы", "Комплексная динамика", "Семейство Мандельброта"], "Буффало", "Симметричная модификация семейства Мандельброта с модулем компонент.", "buffalo_preview_sq512.png"),
        Item(["Фракталы", "Комплексная динамика", "Семейство Мандельброта"], "Кельтский Мандельброт", "Вариант с модулем действительной части результата Z².", "celtic_mandelbrot_preview_sq512.png"),
        Item(["Фракталы", "Комплексная динамика", "Семейство Мандельброта"], "Симоноброт", "Фрактал с произвольной степенью и опциональной инверсией.", "simonobrot_preview_sq512.png"),
        Item(["Фракталы", "Комплексная динамика", "Семейство Мандельброта"], "Обобщенный Мандельброт", "Мультиброт Z = Zᵖ + C с настраиваемой степенью.", "general_mandelbrot_preview_sq512.png"),

        Item(["Фракталы", "Комплексная динамика", "Семейство Жюлиа"], "Классическое Жюлиа", "Семейство Жюлиа с фиксированной комплексной константой C.", "julia_preview_sq512.png"),
        Item(["Фракталы", "Комплексная динамика", "Семейство Жюлиа"], "Горящий Корабль (Жюлиа)", "Julia-вариант алгоритма «Горящий корабль».", "julia_burningship_preview_sq512.png"),
        Item(["Фракталы", "Комплексная динамика", "Семейство Жюлиа"], "Галерея констант C (Жюлиа)", "Пакетный просмотр множества Жюлиа для сетки значений C.", "julia_preview_sq512.png", "JuliaGallery"),
        Item(["Фракталы", "Комплексная динамика", "Семейство Жюлиа"], "Галерея констант C (Жюлиа горящий корабль)", "Сетка вариантов Julia Burning Ship.", "julia_burningship_preview_sq512.png", "JuliaBurningShipGallery"),

        Item(["Фракталы", "Комплексная динамика", "Бассейны притяжения"], "Бассейны Ньютона+", "Бассейны притяжения корней для методов Newton, Halley и Householder.", "newton_preview_sq512.png"),
        Item(["Фракталы", "Комплексная динамика", "Бассейны притяжения"], "Бассейны метода Мюллера", "Поиск корней по трём точкам с квадратичной интерполяцией: пиксель задаёт тройку последних приближений, а форма тройки и якоря заметно усложняют границы бассейнов.", "muller_basins_preview_sq512.png", BasinExplorerCatalog.LaunchKey(BasinExplorerKind.Muller)),
        Item(["Фракталы", "Комплексная динамика", "Бассейны притяжения"], "Бассейны метода Лагерра", "Кубически сходящийся метод для полиномов с настраиваемым n; режим расхождения показывает, где Лагерр и Ньютон приходят к разным корням.", "laguerre_basins_preview_sq512.png", BasinExplorerCatalog.LaunchKey(BasinExplorerKind.Laguerre)),
        Item(["Фракталы", "Комплексная динамика", "Бассейны притяжения"], "Бассейны метода секущих", "Двухшаговый метод без производной: фиксированная первая точка, пара z − h, z или двумерный срез четырёхмерного пространства состояний (x₀, x₁).", "secant_basins_preview_sq512.png", BasinExplorerCatalog.LaunchKey(BasinExplorerKind.Secant)),
        Item(["Фракталы", "Комплексная динамика", "Бассейны притяжения"], "Бассейны рациональных отображений", "Итерации R(z) = P(z)/Q(z): притягивающие циклы ищутся по орбитам критических точек, отмечаются на полотне, а точки раскрашиваются по конечному аттрактору, включая ∞.", "rational_map_basins_preview_sq512.png", BasinExplorerCatalog.LaunchKey(BasinExplorerKind.RationalMap)),
        Item(["Фракталы", "Комплексная динамика", "Бассейны притяжения"], "Бассейны периодических циклов", "Притягивающие циклы периодов 1…N произвольного отображения f(z) с параметром c: свой цвет каждого цикла, яркость по скорости сходимости, фаза и период, чёрный для убегающих и нераспознанных орбит.", "periodic_cycle_basins_preview_sq512.png", BasinExplorerCatalog.LaunchKey(BasinExplorerKind.PeriodicCycles)),
        Item(["Фракталы", "Комплексная динамика", "Бассейны притяжения"], "Комплексное логистическое отображение", "Бассейны z → λz(1−z) и карта периодов в комплексной плоскости λ. Переключение между начальными значениями и параметром, поиск циклов и просмотр орбит.", "complex_logistic_basins_preview_sq512.png", BasinExplorerCatalog.LaunchKey(BasinExplorerKind.ComplexLogistic)),
        Item(["Фракталы", "Комплексная динамика", "Бассейны притяжения"], "Магнитный маятник", "Маятник над несколькими магнитами: хаотические траектории, трение, возвратная сила и цвет по конечному магниту. Расстановка центров мышью и карта времени успокоения.", "magnetic_pendulum_basins_preview_sq512.png", BasinExplorerCatalog.LaunchKey(BasinExplorerKind.MagneticPendulum)),
        Item(["Фракталы", "Комплексная динамика", "Бассейны притяжения"], "Система притягивающих центров", "Движение частицы среди неподвижных центров: масса, радиус захвата, трение, начальная скорость, поглощение или захват после успокоения. Редактирование центров и просмотр траекторий.", "gravity_centers_basins_preview_sq512.png", BasinExplorerCatalog.LaunchKey(BasinExplorerKind.GravityCenters)),
        Item(["Фракталы", "Комплексная динамика", "Бассейны притяжения"], "Бассейны градиентного спуска", "Минимумы пользовательского потенциала V(x,y): сравнение обычного градиентного спуска, momentum, Nesterov и Adam, размеров шага и траекторий оптимизации.", "gradient_descent_basins_preview_sq512.png", BasinExplorerCatalog.LaunchKey(BasinExplorerKind.GradientDescent)),
        Item(["Фракталы", "Комплексная динамика", "Бассейны притяжения"], "Комплексный градиентный поток", "Непрерывный спуск по потенциалу ½|f(z)|²: адаптивное интегрирование z′ = −f(z)·conj(f′(z)), бассейны корней, время приближения и траектории.", "complex_gradient_flow_basins_preview_sq512.png", BasinExplorerCatalog.LaunchKey(BasinExplorerKind.ComplexGradientFlow)),
        Item(["Фракталы", "Комплексная динамика", "Бассейны притяжения"], "Полиномиальные векторные поля", "Пользовательская система x′ = f(x,y), y′ = g(x,y): бассейны устойчивых равновесий и предельных циклов, проверка устойчивости и адаптивный интегратор.", "polynomial_vector_field_basins_preview_sq512.png", BasinExplorerCatalog.LaunchKey(BasinExplorerKind.PolynomialVectorField)),

        Item(["Фракталы", "Комплексная динамика", "Другие итерационные семейства"], "Фрактал Феникс", "Динамические и параметрические плоскости обобщённого Phoenix с комплексной памятью, вариантами формулы и расширенным окрашиванием.", "phoenix_preview_sq512.png"),
        Item(["Фракталы", "Комплексная динамика", "Другие итерационные семейства"], "Фрактал Нова (Мандельброт)", "Модификация метода Ньютона с добавлением константы C.", "NovaMandelbrot_preview_sq512.png"),
        Item(["Фракталы", "Комплексная динамика", "Другие итерационные семейства"], "Фрактал Нова (Жюлиа)", "Julia-вариант семейства Nova.", "NovaJulia_preview_sq512.png"),
        Item(["Фракталы", "Комплексная динамика", "Другие итерационные семейства"], "Фрактал Коллатца", "Комплексное обобщение отображения Коллатца.", "collatz_preview_sq512.png"),
        Item(["Фракталы", "Комплексная динамика", "Другие итерационные семейства"], "Буддаброт / Анти-Буддаброт", "Накопительный рендер плотности посещения орбит.", "buddhabrot_f_preview_sq512.png"),

        Item(["Фракталы", "Трёхмерные фракталы"], "Мандельбульб", "Трёхмерное обобщение множества Мандельброта через сферические координаты: z → zⁿ + c с произвольной степенью. Трассировка лучей по дистанционной оценке на видеокарте.", Placeholder3D, Fractal3DCatalog.LaunchKey(Fractal3DKind.Mandelbulb)),
        Item(["Фракталы", "Трёхмерные фракталы"], "Жюлиабульб", "Julia-вариант Мандельбульба: вместо точки пространства в итерацию подставляется фиксированная константа C, задающая форму тела.", Placeholder3D, Fractal3DCatalog.LaunchKey(Fractal3DKind.Juliabulb)),
        Item(["Фракталы", "Трёхмерные фракталы"], "Мандельбокс", "Чередование свёртки по кубу и по сфере с масштабированием: архитектурная структура с галереями, арками и бесконечными коридорами.", Placeholder3D, Fractal3DCatalog.LaunchKey(Fractal3DKind.Mandelbox)),
        Item(["Фракталы", "Трёхмерные фракталы"], "Губка Менгера", "Куб с рекурсивно вырезанными крестообразными тоннелями — классический самоподобный объект размерности log20/log3.", Placeholder3D, Fractal3DCatalog.LaunchKey(Fractal3DKind.MengerSponge)),
        Item(["Фракталы", "Трёхмерные фракталы"], "Тетраэдр Серпинского", "Трёхмерный аналог треугольника Серпинского: пространство складывается к ближайшей вершине тетраэдра.", Placeholder3D, Fractal3DCatalog.LaunchKey(Fractal3DKind.SierpinskiTetrahedron)),
        Item(["Фракталы", "Трёхмерные фракталы"], "Кватернионное Жюлиа", "Множество Жюлиа в алгебре кватернионов: трёхмерный срез четырёхмерного множества с настраиваемой координатой среза.", Placeholder3D, Fractal3DCatalog.LaunchKey(Fractal3DKind.QuaternionJulia)),
        Item(["Фракталы", "Трёхмерные фракталы"], "Аполлонова упаковка сфер", "Рекурсивное заполнение промежутков между взаимно касающимися сферами. Число поколений меняет глубину упаковки, а камеру можно провести внутрь.", Placeholder3D, Fractal3DCatalog.LaunchKey(Fractal3DKind.ApollonianPacking)),

        Item(["Фракталы", "Итерируемые и самоподобные"], "IFS Барнсли / Хейуэя", "Стохастическая система аффинных преобразований.", "ifs_fractal_preview_sq512.png"),
        Item(["Фракталы", "Итерируемые и самоподобные"], "Конструктор объёмных IFS", "Редактируемые преобразования пространства порождают трёхмерный аттрактор. Пресеты, поворот, сохранение и экспорт.", Placeholder3D, "IFS3D"),
        Item(["Фракталы", "Итерируемые и самоподобные"], "Фрактальное пламя (стохастическое)", "Стохастический рендер с накоплением HDR-гистограммы.", "flame_fractal_preview_sq512.png"),
        Item(["Фракталы", "Итерируемые и самоподобные"], "Серпинский — игра хаоса", "Стохастическое построение треугольника Серпинского случайными переходами к вершинам.", "serpinski_preview_sq512.png", "SerpinskyChaos"),
        Item(["Фракталы", "Геометрические фракталы"], "Аполлонова прокладка", "Рекурсивная упаковка взаимно касающихся окружностей с раскраской по глубине, кривизне или родительской ветви.", "apollonian_preview_sq512.png", "ApollonianGasket"),
        Item(["Фракталы", "Стохастические фракталы"], "DLA — диффузионно-ограниченная агрегация", "Случайно блуждающие частицы прилипают к растущему кластеру, образуя молнии, кораллы и морозные узоры. Процесс отображается в реальном времени.", "dla_preview_sq512.png", "DLA"),

        Item(["Динамические системы и хаос", "Анализ динамики"], "Экспонента Ляпунова", "Карта экспонент Ляпунова логистического отображения.", "lyapunov_preview_sq512.png", "Lyapunov"),
        Item(["Динамические системы и хаос", "Анализ динамики"], "Логистическое отображение (орбиты)", "Орбитальный график логистического отображения.", "logistic_map_preview_sq512.png", "LogisticMap"),
        Item(["Динамические системы и хаос", "Анализ динамики"], "Диаграмма бифуркации", "Диаграмма бифуркаций логистического отображения.", "bifurcation_preview_sq512.png", "Bifurcation"),
        Item(["Динамические системы и хаос", "Аттракторы"], "Аттрактор Лоренца", "Визуализация классической хаотической системы Лоренца.", "sig_preview_sq512.png", "Lorenz"),
        Item(["Динамические системы и хаос", "Аттракторы"], "Аттрактор Рёсслера", "Визуализация хаотической системы Рёсслера.", "rossler_preview_sq512.png", "Rossler"),
        Item(["Динамические системы и хаос", "Аттракторы"], "Карта Хенона", "Классическое двумерное отображение Хенона.", "henon_preview_sq512.png", "Henon"),
        Item(["Динамические системы и хаос", "Аттракторы"], "Отображение Икэды", "Двумерное нелинейное отображение Икэды.", "ikeda_preview_sq512.png", "Ikeda"),
        Item(["Динамические системы и хаос", "Аттракторы"], "Странные аттракторы", "Облака плотности для аттракторов Клиффорда, Питера де Йонга, Tinkerbell и Gumowski–Mira.", "strange_attractors_preview_sq512.png", "Attractors2D"),
        Item(["Динамические системы и хаос", "Пространственные системы"], "Gray–Scott reaction–diffusion", "Двухкомпонентная реакционно‑диффузионная среда с живой эволюцией пятен, волн и лабиринтов, интерактивными затравками и собственными палитрами.", "strange_attractors_preview_sq512.png", "GrayScott"),

        Item(["Математические лаборатории", "Теория чисел и дискретные структуры"], "Арифметика по модулю", "Отображения x → ax+b mod N и функциональные графы на окружности: кардиоиды, нефроиды и плетёные конечные орбиты.", "inverse_collatz_tree_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.ModularArithmetic)),
        Item(["Математические лаборатории", "Теория чисел и дискретные структуры"], "Треугольник Паскаля по модулю N", "Цветовая карта C(n,k) mod m с режимом делимости и направляющими теоремы Люка.", "inverse_collatz_tree_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.PascalModulo)),
        Item(["Математические лаборатории", "Теория чисел и дискретные структуры"], "Лаборатория рациональных чисел", "Деревья Штерна—Броко и Калкина—Уилфа, последовательности Фарея, окружности Форда и цепные дроби.", "inverse_collatz_tree_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.RationalNumbers)),
        Item(["Математические лаборатории", "Теория чисел и дискретные структуры"], "Геометрия простых чисел", "Спирали Улама и Сакса, шестиугольная решётка, простые Гаусса и Эйзенштейна с раскраской по остаткам.", "inverse_collatz_tree_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.PrimeGeometry)),
        Item(["Математические лаборатории", "Теория чисел и дискретные структуры"], "Последовательность Рекамана", "Чередующиеся дуги, хордовая диаграмма и плоское блуждание для самоизбегающей целочисленной последовательности Рекамана.", "inverse_collatz_tree_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.RecamanSequence)),
        Item(["Математические лаборатории", "Теория чисел и дискретные структуры"], "Обратное дерево Коллатца", "Точное обратное дерево целочисленного отображения Коллатца с радиальной и древовидной раскладкой, фильтрами по остаткам и анимацией роста.", "inverse_collatz_tree_preview_sq512.png", "InverseCollatzTree"),

        Item(["Математические лаборатории", "Геометрия и преобразования"], "Филлотаксис и иррациональные вращения", "Золотой угол, рациональные приближения и настраиваемые вращения образуют парастихии и лучевые резонансы.", "inverse_collatz_tree_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.Phyllotaxis)),
        Item(["Математические лаборатории", "Геометрия и преобразования"], "Инверсия окружностей и преобразования Мёбиуса", "Интерактивная инверсия точек и окружностей, повторные преобразования и комплексные дробно-линейные отображения.", "inverse_collatz_tree_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.CircleInversion)),
        Item(["Математические лаборатории", "Геометрия и преобразования"], "Подстановочные и апериодические мозаики", "Инфляция мозаик Penrose, Ammann—Beenker, Chair, Pinwheel, сфинкса и треугольников Фибоначчи.", "inverse_collatz_tree_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.AperiodicTilings)),
        Item(["Математические лаборатории", "Геометрия и преобразования"], "Гиперболическая геометрия", "Мозаики {p,q}, геодезические и идеальная граница в модели диска Пуанкаре.", "inverse_collatz_tree_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.HyperbolicGeometry)),
        Item(["Математические лаборатории", "Геометрия и преобразования"], "Диаграммы Вороного и релаксация Ллойда", "Евклидовы, манхэттенские и взвешенные ячейки, двойственная сеть Делоне и интерактивная центроидальная релаксация.", "inverse_collatz_tree_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.VoronoiLloyd)),
        Item(["Математические лаборатории", "Геометрия и преобразования"], "Узлы: торические, Лиссажу и косы", "Псевдо‑трёхмерная студия параметрических узлов, многокомпонентных торических зацеплений и замкнутых кос.", "inverse_collatz_tree_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.KnotStudio)),

        Item(["Математические лаборатории", "Комплексный анализ"], "Domain Coloring", "Раскраска комплексных функций: аргумент f(z) задаёт оттенок, а модуль — яркость и контурные линии.", "domain_coloring_preview_sq512.png", "DomainColoring"),
        Item(["Математические лаборатории", "Комплексный анализ"], "Kleinian и Schottky groups", "Предельные множества классических и деформированных конфигураций Шоттки, двухпараболических орбит и инверсионной группы Аполлона.", "domain_coloring_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.KleinianSchottky)),
        Item(["Математические лаборатории", "Генеративная геометрия"], "L‑системы и черепашья графика", "Редактор аксиом и правил с анимацией: Кох, Гильберт, Леви, Dragon Curve, деревья, растения и Серпинский.", "lsystem_preview_sq512.png", "LSystem"),
        Item(["Математические лаборатории", "Стохастические процессы"], "Brownian motion / Lévy flights", "Воспроизводимые броуновские траектории, полёты Леви, мосты, ансамбли частиц и коррелированные случайные блуждания.", "dla_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.StochasticMotion)),
        Item(["Математические лаборатории", "Гармоники и волны"], "Fourier Epicycles", "Рисование замкнутого контура, дискретный ряд Фурье, спектр коэффициентов и анимированная цепочка эпициклов.", "inverse_collatz_tree_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.FourierEpicycles)),
        Item(["Математические лаборатории", "Гармоники и волны"], "Фигуры Хладни и интерференция волн", "Стоячие волны квадратных пластин и круглых мембран, узловые линии и интерференционные поля когерентных источников.", "inverse_collatz_tree_preview_sq512.png", MathematicalLaboratoryCatalog.LaunchKey(MathematicalLaboratoryKind.ChladniWaveInterference))
    ];

    private static FractalCatalogItem Item(
        string[] categoryPath,
        string name,
        string description,
        string previewFile,
        string? launchKey = null) =>
        new(categoryPath, name, description, $"Assets/Previews/{previewFile}", launchKey ?? GetLaunchKey(previewFile));

    private static string? GetLaunchKey(string previewFile) => previewFile switch
    {
        "mandelbrot_preview_sq512.png" => "Mandelbrot",
        "burningship_preview_sq512.png" => "BurningShip",
        "tricorn__preview_sq512.png" => "Tricorn",
        "buffalo_preview_sq512.png" => "Buffalo",
        "celtic_mandelbrot_preview_sq512.png" => "Celtic",
        "simonobrot_preview_sq512.png" => "Simonobrot",
        "general_mandelbrot_preview_sq512.png" => "Generalized",
        "julia_preview_sq512.png" => "Julia",
        "julia_burningship_preview_sq512.png" => "JuliaBurningShip",
        "newton_preview_sq512.png" => "NewtonPools",
        "phoenix_preview_sq512.png" => "Phoenix",
        "collatz_preview_sq512.png" => "Collatz",
        "NovaMandelbrot_preview_sq512.png" => "NovaMandelbrot",
        "NovaJulia_preview_sq512.png" => "NovaJulia",
        "buddhabrot_f_preview_sq512.png" => "Buddhabrot",
        "flame_fractal_preview_sq512.png" => "Flame",
        "ifs_fractal_preview_sq512.png" => "IFS",
        _ => null
    };
}
