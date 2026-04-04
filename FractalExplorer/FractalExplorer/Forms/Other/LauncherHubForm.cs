using FractalExplorer.Forms;
using FractalExplorer.Forms.Fractals;
using FractalExplorer.Forms.Other;
using FractalExplorer.Projects;
using FractalExplorer.Resources;
using FractalExplorer.Properties;
using System.Reflection;

using FractalExplorer.Utilities.Theme;
namespace FractalExplorer
{
    /// <summary>
    /// Главная форма приложения, служащая хабом для запуска различных фрактальных форм.
    /// </summary>
    public partial class LauncherHubForm : Form
    {
        /// <summary>
        /// Вспомогательный класс для хранения информации о каждом фрактале, доступном в приложении.
        /// </summary>
        private class FractalInfo
        {
            /// <summary>
            /// Отображаемое имя фрактала.
            /// </summary>
            public string DisplayName { get; set; }
            /// <summary>
            /// Категория или семейство, к которому принадлежит фрактал.
            /// </summary>
            public string Family { get; set; }
            /// <summary>
            /// Тип формы (<see cref="Form"/>), которую нужно запустить для этого фрактала.
            /// </summary>
            public Type FormToLaunch { get; set; }
            /// <summary>
            /// Подробное описание фрактала, его особенностей и математической основы.
            /// </summary>
            public string Description { get; set; }
            /// <summary>
            /// Изображение для предпросмотра, загруженное из ресурсов проекта.
            /// </summary>
            public Image PreviewImage { get; set; }
        }

        /// <summary>
        /// Каталог всех доступных в приложении фракталов.
        /// </summary>
        private readonly List<FractalInfo> _fractalCatalog;

        /// <summary>
        /// Текущий фрактал, выбранный пользователем в дереве.
        /// </summary>
        private FractalInfo _selectedFractal;

        /// <summary>
        /// Узел, над которым находится курсор мыши.
        /// </summary>
        private TreeNode? _hoveredNode;

        /// <summary>
        /// Пункты селектора тем (реальные темы и действия управления).
        /// </summary>
        private readonly List<ThemeSelectorItem> _themeOptions = new();

        /// <summary>
        /// Модель элемента выпадающего списка тем.
        /// </summary>
        private sealed class ThemeSelectorItem
        {
            public bool IsManageAction { get; init; }
            public string? ThemeId { get; init; }
            public string DisplayText { get; init; } = string.Empty;

            public override string ToString() => DisplayText;
        }

        private const string ManageThemesItemText = "Управление темами...";

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="LauncherHubForm"/>.
        /// </summary>
        public LauncherHubForm()
        {
            InitializeComponent();
            ThemeManager.RegisterForm(this);
            _fractalCatalog = new List<FractalInfo>();

            InitializeFractalCatalog();
            PopulateTreeView();
            InitializeFractalTreeVisualStates();
            InitializeRenderPatternSelector();
            InitializeThemeSelector();
            ThemeManager.ThemesChanged += ThemeManager_ThemesChanged;
            ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
            Disposed += LauncherHubForm_Disposed;
            DisplayAppVersionInTitle();
        }

        private void InitializeFractalTreeVisualStates()
        {
            treeViewFractals.DrawMode = TreeViewDrawMode.OwnerDrawText;
            treeViewFractals.HideSelection = false;
            EnableDoubleBuffering(treeViewFractals);
            treeViewFractals.DrawNode += treeViewFractals_DrawNode;
            treeViewFractals.NodeMouseDoubleClick += treeViewFractals_NodeMouseDoubleClick;
            treeViewFractals.MouseMove += treeViewFractals_MouseMove;
            treeViewFractals.MouseLeave += treeViewFractals_MouseLeave;
        }

        private static void EnableDoubleBuffering(Control control)
        {
            PropertyInfo? doubleBufferedProperty = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            doubleBufferedProperty?.SetValue(control, true);
        }



        private static int GetRenderPatternIndex(TileSchedulingStrategy strategy)
        {
            return strategy switch
            {
                TileSchedulingStrategy.Classic => 0,
                TileSchedulingStrategy.Linear => 1,
                TileSchedulingStrategy.Spiral => 2,
                TileSchedulingStrategy.Randomized => 3,
                TileSchedulingStrategy.Checkerboard => 4,
                TileSchedulingStrategy.Diagonal => 5,
                TileSchedulingStrategy.EdgesInward => 6,
                TileSchedulingStrategy.MortonCurve => 7,
                _ => 0
            };
        }

        private static TileSchedulingStrategy GetRenderPatternStrategy(int selectedIndex)
        {
            return selectedIndex switch
            {
                0 => TileSchedulingStrategy.Classic,
                1 => TileSchedulingStrategy.Linear,
                2 => TileSchedulingStrategy.Spiral,
                3 => TileSchedulingStrategy.Randomized,
                4 => TileSchedulingStrategy.Checkerboard,
                5 => TileSchedulingStrategy.Diagonal,
                6 => TileSchedulingStrategy.EdgesInward,
                7 => TileSchedulingStrategy.MortonCurve,
                _ => TileSchedulingStrategy.Classic
            };
        }

        /// <summary>
        /// Инициализирует список шаблонов рендера и синхронизирует его с глобальными настройками.
        /// </summary>
        private void InitializeRenderPatternSelector()
        {
            // Отписываемся от события, чтобы избежать его срабатывания во время инициализации
            cbRenderPattern.SelectedIndexChanged -= cbRenderPattern_SelectedIndexChanged;

            cbRenderPattern.Items.Clear();

            // Старые элементы
            cbRenderPattern.Items.Add("Классический (от центра)");
            cbRenderPattern.Items.Add("Построчный"); // Бывший классический
            cbRenderPattern.Items.Add("Спиральный");
            cbRenderPattern.Items.Add("Случайный");

            // Новые зрелищные элементы
            cbRenderPattern.Items.Add("Шахматный");
            cbRenderPattern.Items.Add("Диагональный");
            cbRenderPattern.Items.Add("От краев к центру");
            cbRenderPattern.Items.Add("Z-кривая (Мортон)");

            TileSchedulingStrategy savedStrategy = GetRenderPatternStrategy(Settings.Default.RenderPatternIndex);
            RenderPatternSettings.SelectedPattern = savedStrategy;
            cbRenderPattern.SelectedIndex = GetRenderPatternIndex(savedStrategy);

            // Подписываемся на событие обратно
            cbRenderPattern.SelectedIndexChanged += cbRenderPattern_SelectedIndexChanged;
        }

        /// <summary>
        /// Применяет новый шаблон рендера из выпадающего списка.
        /// </summary>
        private void cbRenderPattern_SelectedIndexChanged(object sender, EventArgs e)
        {
            TileSchedulingStrategy selectedStrategy = GetRenderPatternStrategy(cbRenderPattern.SelectedIndex);
            RenderPatternSettings.SelectedPattern = selectedStrategy;
            Settings.Default.RenderPatternIndex = GetRenderPatternIndex(selectedStrategy);
            Settings.Default.Save();
        }

        /// <summary>
        /// Инициализирует селектор темы и синхронизирует его с активной темой приложения.
        /// </summary>
        private void InitializeThemeSelector()
        {
            cbTheme.SelectedIndexChanged -= cbTheme_SelectedIndexChanged;

            _themeOptions.Clear();
            _themeOptions.AddRange(ThemeManager.GetAllThemes().Select(theme => new ThemeSelectorItem
            {
                IsManageAction = false,
                ThemeId = theme.Id,
                DisplayText = theme.DisplayName
            }));
            _themeOptions.Add(new ThemeSelectorItem
            {
                IsManageAction = true,
                ThemeId = null,
                DisplayText = ManageThemesItemText
            });

            cbTheme.Items.Clear();
            cbTheme.Items.AddRange(_themeOptions.Cast<object>().ToArray());

            int selectedThemeIndex = _themeOptions.FindIndex(item =>
                !item.IsManageAction &&
                string.Equals(item.ThemeId, ThemeManager.CurrentThemeId, StringComparison.OrdinalIgnoreCase));

            cbTheme.SelectedIndex = selectedThemeIndex >= 0 ? selectedThemeIndex : 0;

            cbTheme.SelectedIndexChanged += cbTheme_SelectedIndexChanged;
        }



        private void ThemeManager_ThemesChanged(object? sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => InitializeThemeSelector()));
                return;
            }

            InitializeThemeSelector();
        }

        private void ThemeManager_ThemeChanged(object? sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => treeViewFractals.Invalidate()));
                return;
            }

            treeViewFractals.Invalidate();
        }

        private void LauncherHubForm_Disposed(object? sender, EventArgs e)
        {
            ThemeManager.ThemesChanged -= ThemeManager_ThemesChanged;
            ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;
            Disposed -= LauncherHubForm_Disposed;
        }

        /// <summary>
        /// Применяет выбранную пользователем тему ко всем открытым формам и текущему окну.
        /// </summary>
        private void cbTheme_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbTheme.SelectedIndex < 0 || cbTheme.SelectedIndex >= _themeOptions.Count)
            {
                return;
            }

            ThemeSelectorItem selectedItem = _themeOptions[cbTheme.SelectedIndex];

            if (selectedItem.IsManageAction)
            {
                using ThemeEditorForm themeEditorForm = new();
                themeEditorForm.ShowDialog(this);

                InitializeThemeSelector();

                int activeThemeIndex = _themeOptions.FindIndex(item =>
                    !item.IsManageAction &&
                    string.Equals(item.ThemeId, ThemeManager.CurrentThemeId, StringComparison.OrdinalIgnoreCase));
                cbTheme.SelectedIndex = activeThemeIndex >= 0 ? activeThemeIndex : 0;
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedItem.ThemeId))
            {
                return;
            }

            ThemeManager.SetTheme(selectedItem.ThemeId);

            Settings.Default.UiTheme = selectedItem.ThemeId;
            Settings.Default.Save();
        }

        /// <summary>
        /// Инициализирует каталог фракталов, добавляя информацию о каждом из них.
        /// <br/><b>Чтобы добавить новый фрактал, необходимо добавить новую запись только в этот метод.</b>
        /// </summary>
        private void InitializeFractalCatalog()
        {
            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Множество Мандельброта",
                DisplayName = "Классический Мандельброт",
                FormToLaunch = typeof(FractalMondelbrot),
                Description = "Иконический фрактал, определяемый простой формулой Z = Z² + C.\n\n" +
                              "Он представляет собой множество всех комплексных чисел C, для которых итерация не уходит в бесконечность. Является картой всех связанных множеств Жюлиа и славится бесконечной сложностью своей границы.\n\n" +
                              "Особенности: Исследуйте фрактал с огромным приближением, настраивайте градиентные палитры с помощью плавного окрашивания и сохраняйте изображения в высоком разрешении.",
                PreviewImage = Properties.Resources.mandelbrot_preview_sq512
            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Множество Мандельброта",
                DisplayName = "Горящий Корабль",
                FormToLaunch = typeof(FractalMondelbrotBurningShip),
                Description = "Поразительная модификация алгоритма Мандельброта, где перед возведением в квадрат берутся абсолютные значения компонент: Z = (|Re(Z)| + i|Im(Z)|)² + C.\n\n" +
                              "Это нарушение симметрии создает совершенно иные, более хаотичные структуры, напоминающие горящий корабль и клубы дыма.\n\n" +
                              "Особенности: Откройте для себя уникальные детали в 'палубах' и 'мачтах' корабля. Полный контроль над цветами и возможность сохранения позволят создать настоящий шедевр.",
                PreviewImage = Properties.Resources.burningship_preview_sq512
            });


            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Множество Мандельброта",
                DisplayName = "Трикорн (Mandelbar)",
                FormToLaunch = typeof(FractalTricorn),
                Description = "Антиголоморфная вариация множества Мандельброта с комплексным сопряжением перед возведением в квадрат: Z = conj(Z)² + C.\n\n" +
                              "Из-за сопряжения фрактал получает характерную трёхлепестковую структуру и симметрию, отличную от классического Мандельброта.\n\n" +
                              "Особенности: Исследуйте иную динамику итераций и необычные области самоподобия, используя те же инструменты окрашивания и масштабирования.",

                PreviewImage = Properties.Resources.tricorn__preview_sq512
            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Множество Мандельброта",
                DisplayName = "Буффало",
                FormToLaunch = typeof(FractalBuffalo),
                Description = "Вариация множества Мандельброта, которая использует абсолютные значения компонент Z перед возведением в квадрат: Z = (|Re(Z)| - i|Im(Z)|)² + C.\n\n" +
                             "Результатом является симметричный фрактал, напоминающий жука или быка, с уникальными и менее 'шумными' структурами по сравнению с 'Горящим Кораблем'.\n\n" +
                             "Особенности: Исследуйте гладкие, органические формы этого фрактала. Все возможности по настройке палитры и сохранению также доступны.",
                PreviewImage = Properties.Resources.buffalo_preview_sq512
            });


            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Множество Мандельброта",
                DisplayName = "Кельтский Мандельброт",
                FormToLaunch = typeof(FractalCelticMandelbrot),
                Description = "Модификация классического алгоритма, где после возведения Z в квадрат берётся модуль действительной части: Z = (|Re(Z²)| + i·Im(Z²)) + C.\n\n" +
                              "Такая трансформация заметно меняет топологию множества и формирует характерные 'кельтские' узоры с выраженной осевой структурой.\n\n" +
                              "Особенности: Используйте те же инструменты масштабирования, центрирования и палитры, что и для других вариантов семейства Мандельброта.",
                PreviewImage = Properties.Resources.celtic_mandelbrot_preview_sq512
            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Множество Мандельброта",
                DisplayName = "Симоноброт",
                FormToLaunch = typeof(FractalSimonobrot),
                Description = "Необычный фрактал, определяемый формулой Z = |Z|^p + C, где |Z| - это модуль комплексного числа (действительное число).\n\n" +
                              "Это приводит к совершенно другим структурам, часто с радиальной симметрией и интересными 'лучами'.\n\n" +
                              "Особенности: Экспериментируйте со степенью 'p' (включая отрицательные значения) и используйте опцию инверсии для получения зеркального отражения фрактала.",
                PreviewImage = Properties.Resources.simonobrot_preview_sq512
            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Множество Мандельброта",
                DisplayName = "Обобщенный Мандельброт",
                FormToLaunch = typeof(FractalGeneralizedMandelbrot),
                Description = "Вариация множества Мандельброта, где используется произвольная степень 'p', а не только квадрат: Z = Z^p + C.\n\n" +
                              "Изменение степени кардинально меняет форму фрактала, создавая так называемые 'мультиброты'.\n\n" +
                              "Особенности: Экспериментируйте с различными степенями, чтобы исследовать бесконечное разнообразие форм. Все возможности по настройке палитры и сохранению также доступны.",
                PreviewImage = Properties.Resources.general_mandelbrot_preview_sq512
            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Множество Жюлиа",
                DisplayName = "Классическое Жюлиа",
                FormToLaunch = typeof(FractalJulia),
                Description = "Множества, тесно связанные с фракталом Мандельброта. Здесь константа C остается неизменной для всего изображения, а итерируется начальная точка Z₀.\n\n" +
                              "Для каждой точки C из множества Мандельброта существует своё уникальное и красивое множество Жюлиа.\n\n" +
                              "Особенности: Интерактивно задавайте константу C, чтобы исследовать различные множества. Настраивайте цветовую палитру и сохраняйте свои находки.",
                PreviewImage = Properties.Resources.julia_preview_sq512
            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Множество Жюлиа",
                DisplayName = "Горящий Корабль (Жюлиа)",
                FormToLaunch = typeof(FractalJuliaBurningShip),
                Description = "Соответствующее множество Жюлиа для фрактала 'Горящий корабль'. Также использует итерацию с модулями компонент и постоянной константой C.\n\n" +
                              "Генерирует уникальные асимметричные узоры, сохраняя хаотичный характер своего 'родителя'.\n\n" +
                              "Особенности: Выбирайте константу C и погружайтесь в исследование удивительных и непредсказуемых паттернов. Сохраняйте лучшие результаты в высоком качестве.",
                PreviewImage = Properties.Resources.julia_burningship_preview_sq512
            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Итерируемые функции",
                DisplayName = "Бассейны Ньютона",
                FormToLaunch = typeof(NewtonPools),
                Description = "Фрактал, визуализирующий работу метода Ньютона для нахождения корней комплексного многочлена. Формула итерации: Z_n+1 = Z_n - f(Z_n) / f'(Z_n).\n\n" +
                              "Разные цвета показывают 'бассейны притяжения' — области, точки из которых сходятся к одному и тому же корню. Границы между этими бассейнами и являются фракталом.\n\n" +
                              "Особенности: Вводите собственные полиномы, настраивайте цвета для каждого корня и исследуйте сложную динамику на комплексной плоскости.",
                PreviewImage = Properties.Resources.newton_preview_sq512
            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Итерируемые функции",
                DisplayName = "Фрактал Феникс",
                FormToLaunch = typeof(FractalPhoenixForm),
                Description = "Обобщение множества Жюлиа, формула которого включает не только текущее, но и предыдущее значение Z: Z_n+1 = Z_n² + C₁ + C₂*Z_n-1.\n\n" +
                              "Наличие двух констант и 'памяти' о предыдущем шаге создает невероятно сложные и красивые вихревые структуры, похожие на перья мифической птицы.\n\n" +
                              "Особенности: Исследуйте огромное пространство параметров C₁ и C₂, чтобы найти уникальные вариации. Настраивайте палитры и сохраняйте изображения.",
                PreviewImage = Properties.Resources.phoenix_preview_sq512
            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Итерируемые функции",
                DisplayName = "Фрактал Коллатца",
                FormToLaunch = typeof(FractalCollatzForm),
                Description = "Представляет собой обобщение знаменитой гипотезы Коллатца (проблемы 3n+1) на комплексную плоскость. Итерационная формула: Z_next = 0.25 * (2 + 7*Z - (2 + 5*Z) * cos(πZ)).\n\n" +
                              "Гипотеза утверждает, что последовательность 3n+1 для любого целого числа в итоге придет к циклу 4-2-1. Этот фрактал визуализирует хаотичное и непредсказуемое поведение этой, казалось бы, простой идеи.\n\n" +
                              "В результате получается уникальная, бесконечно детализированная паутинообразная структура, не похожая ни на один другой фрактал.\n\n" +
                              "Особенности: Исследуйте сложную структуру фрактала с помощью глубокого масштабирования, настраивайте цветовые схемы и сохраняйте полученные изображения в высоком разрешении.",
                PreviewImage = Properties.Resources.collatz_preview_sq512
            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Итерируемые функции",
                DisplayName = "Фрактал Нова (Мандельброт)",
                FormToLaunch = typeof(FractalNovaMandelbrotForm),
                Description = "Мощное обобщение фрактала Ньютона, которое добавляет в итерационную формулу константу C по аналогии с множеством Мандельброта. Формула: Z_n+1 = Z_n - m * (Z_n^p - 1) / (p*Z_n^(p-1)) + C.\n\n" +
                              "Этот фрактал знаменит своей гибкостью. Варьируя степень 'p' (в том числе делая ее комплексной), начальное значение Z₀ и коэффициент релаксации 'm', можно получить бесконечное разнообразие форм — от симметричных звезд до невероятно сложных спиральных галактик.\n\n" +
                              "Особенности: Полный контроль над уникальными параметрами Nova. Исследуйте, как мнимая часть степени 'p' закручивает фрактал в потрясающие спирали. Все возможности по настройке палитры и сохранению также доступны.",
                PreviewImage = Properties.Resources.NovaMandelbrot_preview_sq512


            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Геометрические",
                DisplayName = "Треугольник Серпинского",
                FormToLaunch = typeof(FractalSerpinski),
                Description = "Классический геометрический фрактал с простой, но элегантной структурой. Обладает строгим свойством самоподобия.\n\n" +
                              "Может быть построен двумя методами: рекурсивным удалением центральных треугольников (геометрический) или стохастическим методом 'игры в хаос'.\n\n" +
                              "Особенности: Переключайтесь между двумя методами построения, настраивайте цвета фрактала и фона, сохраняйте результат.",
                PreviewImage = Properties.Resources.serpinski_preview_sq512
            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Итерируемые функции",
                DisplayName = "Фрактал Нова (Жюлиа)",
                FormToLaunch = typeof(FractalNovaJuliaForm), // Используем нашу новую форму
                Description = "Двойственная версия фрактала Нова, аналогичная множеству Жюлиа для классического Мандельброта. Здесь константа C фиксирована, а итерируется начальная точка Z.\n\n" +
                      "Каждой точке на карте Нова-Мандельброта соответствует свое уникальное множество Нова-Жюлиа. Это позволяет исследовать глубокую связь между двумя этими множествами.\n\n" +
                      "Особенности: Используйте встроенную карту для выбора константы C и находите удивительные формы — от дендритов до замкнутых островов. Доступны все настройки параметров P, M и Z₀.",
                PreviewImage = Properties.Resources.NovaJulia_preview_sq512
            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Итерируемые функции",
                DisplayName = "Buddhabrot / Anti-Buddhabrot",
                FormToLaunch = typeof(FractalBuddhabrotForm),
                Description = "Вместо прямого окрашивания точки по escape-time этот рендер накапливает плотность посещения орбит в отдельном буфере.\n\n" +
                              "Режим Buddhabrot учитывает только орбиты вышедших точек, а Anti-Buddhabrot — орбиты оставшихся ограниченными в заданном лимите итераций.\n\n" +
                              "Особенности: настройка числа случайных стартовых точек, максимума итераций и ограничений области выборки.",
                PreviewImage = Properties.Resources.buddhabrot_f_preview_sq512
            });

            _fractalCatalog.Add(new FractalInfo
            {
                Family = "Динамические системы",
                DisplayName = "Экспонента Ляпунова",
                FormToLaunch = typeof(FractalLyapunovForm),
                Description = "Карта экспонент Ляпунова для логистического отображения xₙ₊₁ = r·xₙ·(1-xₙ) при чередовании параметров A/B по строковому паттерну (например ABAB...).\n\n" +
                              "Отрицательная экспонента соответствует устойчивому режиму, положительная — хаотическому, а граница между ними формирует характерную фрактальную структуру.\n\n" +
                              "Особенности: задайте диапазоны A и B, строку паттерна и число transient/основных итераций.",
                PreviewImage = Properties.Resources.lyapunov_preview_sq512
            });
        }

        /// <summary>
        /// Заполняет элемент управления <see cref="TreeView"/> на основе каталога фракталов, группируя их по семействам.
        /// </summary>
        private void PopulateTreeView()
        {
            treeViewFractals.BeginUpdate();
            treeViewFractals.Nodes.Clear();

            var familyNodes = new Dictionary<string, TreeNode>();

            foreach (var fractal in _fractalCatalog)
            {
                if (!familyNodes.ContainsKey(fractal.Family))
                {
                    var newFamilyNode = new TreeNode(fractal.Family);
                    familyNodes[fractal.Family] = newFamilyNode;
                    treeViewFractals.Nodes.Add(newFamilyNode);
                }

                var fractalNode = new TreeNode(fractal.DisplayName)
                {
                    // Сохраняем всю информацию о фрактале в теге узла. Это ключ к успеху!
                    Tag = fractal
                };

                familyNodes[fractal.Family].Nodes.Add(fractalNode);
            }

            treeViewFractals.ExpandAll();
            treeViewFractals.EndUpdate();
        }

        /// <summary>
        /// Обрабатывает событие выбора узла в дереве фракталов и обновляет панель информации.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void treeViewFractals_AfterSelect(object sender, TreeViewEventArgs e)
        {
            // Если выбран узел фрактала (а не семейства), его Tag будет содержать объект FractalInfo.
            if (e.Node.Tag is FractalInfo selected)
            {
                _selectedFractal = selected;
                lblFractalName.Text = selected.DisplayName;
                richTextBoxDescription.Text = selected.Description;
                pictureBoxPreview.Image = selected.PreviewImage;
                btnLaunchSelected.Visible = true;
            }
            else // Выбран узел категории.
            {
                _selectedFractal = null;
                lblFractalName.Text = "Выберите фрактал";
                richTextBoxDescription.Text = "Выберите конкретный фрактал из списка слева, чтобы увидеть его описание и запустить.";
                pictureBoxPreview.Image = Properties.Resources.base_img_CHAT_GPT_01;
                btnLaunchSelected.Visible = false;
            }
        }

        private void treeViewFractals_DrawNode(object? sender, DrawTreeNodeEventArgs e)
        {
            ThemeDefinition theme = ThemeManager.CurrentDefinition;
            bool isSelected = (e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected;
            bool isHovered = e.Node == _hoveredNode;

            Color background = treeViewFractals.BackColor;
            if (isSelected)
            {
                background = theme.AccentPrimary;
            }
            else if (isHovered)
            {
                background = theme.HoverBackground;
            }

            Color textColor = isSelected ? theme.PrimaryText : treeViewFractals.ForeColor;

            using SolidBrush backgroundBrush = new(background);

            e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Node.Text, treeViewFractals.Font, e.Bounds, textColor, TextFormatFlags.VerticalCenter);

            if ((e.State & TreeNodeStates.Focused) == TreeNodeStates.Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, textColor, background);
            }
        }

        private void treeViewFractals_MouseMove(object? sender, MouseEventArgs e)
        {
            UpdateHoveredNode(treeViewFractals.GetNodeAt(e.Location));
        }

        private void treeViewFractals_MouseLeave(object? sender, EventArgs e)
        {
            UpdateHoveredNode(null);
        }

        private void treeViewFractals_NodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node.Tag is not FractalInfo)
            {
                return;
            }

            LaunchFractal(_selectedFractal);
        }

        private void UpdateHoveredNode(TreeNode? node)
        {
            if (_hoveredNode == node)
            {
                return;
            }

            TreeNode? previousNode = _hoveredNode;
            _hoveredNode = node;

            InvalidateNode(previousNode);
            InvalidateNode(_hoveredNode);
        }

        private void InvalidateNode(TreeNode? node)
        {
            if (node is null)
            {
                return;
            }

            treeViewFractals.Invalidate(node.Bounds);
        }

        /// <summary>
        /// Обрабатывает нажатие на кнопку "Запустить" и открывает форму для выбранного фрактала.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void btnLaunchSelected_Click(object sender, EventArgs e)
        {
            LaunchFractal(_selectedFractal);
        }

        private void btnAboutInfo_Click(object sender, EventArgs e)
        {
            using AboutForm aboutForm = new();
            aboutForm.ShowDialog(this);
        }

        private void LaunchFractal(FractalInfo? fractalToLaunch)
        {
            if (fractalToLaunch?.FormToLaunch == null)
            {
                return;
            }

            // Используем Activator для создания экземпляра формы по её типу.
            if (Activator.CreateInstance(fractalToLaunch.FormToLaunch) is Form form)
            {
                ThemeManager.ApplyTheme(form);
                form.Show();
            }
        }

        #region Version Display

        /// <summary>
        /// Отображает версию приложения в заголовке главной формы.
        /// </summary>
        private void DisplayAppVersionInTitle()
        {
            string appVersion = GetAppVersion();
            this.Text = $"{this.Text} - Версия: {appVersion}";
        }

        /// <summary>
        /// Получает версию приложения из атрибутов сборки.
        /// </summary>
        /// <returns>Строка с версией приложения или "неизвестно", если версия не найдена.</returns>
        private string GetAppVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            // Предпочтительно использовать InformationalVersion, так как он может содержать семантическую версию (например, "1.2.3-beta").
            var informationalVersionAttribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (informationalVersionAttribute != null && !string.IsNullOrWhiteSpace(informationalVersionAttribute.InformationalVersion))
            {
                return informationalVersionAttribute.InformationalVersion;
            }

            // В качестве запасного варианта используется FileVersion.
            var fileVersionAttribute = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            if (fileVersionAttribute != null && !string.IsNullOrWhiteSpace(fileVersionAttribute.Version))
            {
                return fileVersionAttribute.Version;
            }

            // Самый крайний случай - версия сборки.
            Version version = assembly.GetName().Version;
            return version?.ToString() ?? "неизвестно";
        }
        #endregion
    }
}
