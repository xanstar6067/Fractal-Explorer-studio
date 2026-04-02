using FractalDraving;
using FractalExplorer.Engines;
using FractalExplorer.Forms;
using FractalExplorer.Forms.Fractals;
using FractalExplorer.Projects;
using FractalExplorer.Utilities.JsonConverters;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Text.Json;

namespace FractalExplorer.Utilities
{
    /// <summary>
    /// Предоставляет коллекцию предустановленных состояний (пресетов) для различных типов фракталов.
    /// Эти пресеты могут быть загружены для быстрого отображения интересных точек или конфигураций фракталов.
    /// </summary>
    public static class PresetManager
    {
        #region Constants

        /// <summary>
        /// Ограничение на количество итераций для предварительного просмотра пресетов семейства Мандельброта.
        /// Эти значения выбраны, чтобы обеспечить разумную скорость рендера миниатюр
        /// в окне сохранения/загрузки, не нагружая систему слишком сильно.
        /// </summary>
        private const int PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY = 1000;

        /// <summary>
        /// Ограничение на количество итераций для предварительного просмотра пресетов фрактала Феникс.
        /// </summary>
        private const int PREVIEW_ITERATION_LIMIT_PHOENIX = 500;

        /// <summary>
        /// Ограничение на количество итераций для предварительного просмотра пресетов фрактала Бассейны Ньютона.
        /// </summary>
        private const int PREVIEW_ITERATION_LIMIT_NEWTON_CHAOS = 50;

        /// <summary>
        /// Ограничение на количество итераций для предварительного просмотра геометрического фрактала Серпинского.
        /// </summary>
        private const int PREVIEW_ITERATION_LIMIT_SERPINSKY_GEOMETRIC = 5;

        /// <summary>
        /// Ограничение на количество итераций для предварительного просмотра хаотического фрактала Серпинского.
        /// </summary>
        private const int PREVIEW_ITERATION_LIMIT_SERPINSKY_CHAOS = 20000;

        #endregion

        #region Public API / Dispatcher

        /// <summary>
        /// Возвращает список предустановленных точек интереса (пресетов) для указанного типа фрактала.
        /// </summary>
        /// <param name="fractalTypeIdentifier">Идентификатор типа фрактала (например, "Mandelbrot", "Julia", "Phoenix").</param>
        /// <returns>Список объектов <see cref="FractalSaveStateBase"/>, представляющих пресеты,
        /// или пустой список, если для данного типа пресетов нет.</returns>
        /// <summary>
        /// Возвращает список предустановленных точек интереса (пресетов) для указанного типа фрактала.
        /// </summary>
        /// <param name="fractalTypeIdentifier">Идентификатор типа фрактала (например, "Mandelbrot", "Julia", "Phoenix").</param>
        /// <returns>Список объектов <see cref="FractalSaveStateBase"/>, представляющих пресеты,
        /// или пустой список, если для данного типа пресетов нет.</returns>
        public static List<FractalSaveStateBase> GetPresetsFor(string fractalTypeIdentifier)
        {
            switch (fractalTypeIdentifier)
            {
                case "Mandelbrot":
                    return GetMandelbrotPresets();
                case "Julia":
                    return GetJuliaPresets();
                case "MandelbrotBurningShip":
                    return GetMandelbrotBurningShipPresets();
                case "Tricorn":
                    return GetTricornPresets();
                case "JuliaBurningShip":
                    return GetJuliaBurningShipPresets();
                case "Phoenix":
                    return GetPhoenixPresets();
                case "Serpinsky":
                    return GetSerpinskyPresets();
                case "NewtonPools":
                    return GetNewtonPoolsPresets();
                case "GeneralizedMandelbrot":
                    return GetGeneralizedMandelbrotPresets();
                case "Buffalo":
                    return GetBuffaloPresets();
                case "Simonobrot":
                    return GetSimonobrotPresets();
                case "Collatz": // <-- ДОБАВЛЕННЫЙ CASE
                    return GetCollatzPresets();
                default:
                    return new List<FractalSaveStateBase>();
            }
        }

        #endregion

        #region Mandelbrot-Julia Family Presets

        private static List<FractalSaveStateBase> GetGeneralizedMandelbrotPresets()
        {
            var presets = new List<FractalSaveStateBase>();
            var jsonOptions = new JsonSerializerOptions();

            // Пресет 1: "Трилистник" (p=3)
            var preset1 = new GeneralizedMandelbrotSaveState("GeneralizedMandelbrot")
            {
                SaveName = "Трилистник (p=3.0)",
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 0.8m,
                Iterations = 500,
                Threshold = 2.0m,
                PaletteName = "Ультрафиолет",
                Timestamp = DateTime.MinValue,
                Power = 3.0m
            };
            var previewParams1 = new FractalGeneralizedMandelbrot.GeneralizedMandelbrotPreviewParams
            {
                CenterX = preset1.CenterX,
                CenterY = preset1.CenterY,
                Zoom = preset1.Zoom,
                Iterations = Math.Min(preset1.Iterations, 500),
                PaletteName = preset1.PaletteName,
                Threshold = preset1.Threshold,
                PreviewEngineType = preset1.PreviewEngineType,
                Power = preset1.Power
            };
            preset1.PreviewParametersJson = JsonSerializer.Serialize(previewParams1, jsonOptions);
            presets.Add(preset1);

            // Пресет 2: "Астероид" (p=4)
            var preset2 = new GeneralizedMandelbrotSaveState("GeneralizedMandelbrot")
            {
                SaveName = "Астероид (p=4.0)",
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 0.8m,
                Iterations = 500,
                Threshold = 2.0m,
                PaletteName = "Огонь",
                Timestamp = DateTime.MinValue,
                Power = 4.0m
            };
            var previewParams2 = new FractalGeneralizedMandelbrot.GeneralizedMandelbrotPreviewParams
            {
                CenterX = preset2.CenterX,
                CenterY = preset2.CenterY,
                Zoom = preset2.Zoom,
                Iterations = Math.Min(preset2.Iterations, 500),
                PaletteName = preset2.PaletteName,
                Threshold = preset2.Threshold,
                PreviewEngineType = preset2.PreviewEngineType,
                Power = preset2.Power
            };
            preset2.PreviewParametersJson = JsonSerializer.Serialize(previewParams2, jsonOptions);
            presets.Add(preset2);

            return presets;
        }

        /// <summary>
        /// Создает и возвращает список предустановленных состояний для фрактала Мандельброта.
        /// </summary>
        /// <returns>Список пресетов Мандельброта.</returns>
        private static List<FractalSaveStateBase> GetMandelbrotPresets()
        {
            var presets = new List<FractalSaveStateBase>();

            // Пресет 1: Долина Морских Коньков
            // Этот пресет демонстрирует известную область фрактала Мандельброта с высокой детализацией,
            // где часто можно увидеть характерные "морские коньки".
            var preset1 = new MandelbrotFamilySaveState("Mandelbrot")
            {
                SaveName = "Долина Морских Коньков",
                CenterX = -0.743643887037151m,
                CenterY = 0.13182590420533m,
                Zoom = 11500m,
                Iterations = 1000,
                Threshold = 2.0m,
                PaletteName = "Лёд", // Обновлено: теперь соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams1 = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset1.CenterX,
                CenterY = preset1.CenterY,
                Zoom = preset1.Zoom,
                // Количество итераций для превью ограничивается, чтобы рендер был быстрым.
                Iterations = Math.Min(preset1.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset1.PaletteName,
                Threshold = preset1.Threshold,
                PreviewEngineType = "Mandelbrot"
            };
            preset1.PreviewParametersJson = JsonSerializer.Serialize(previewParams1);
            presets.Add(preset1);

            // Пресет 2: Шип Миниброта
            // Этот пресет фокусируется на характерном шипе на левой стороне основного кардиоида фрактала Мандельброта,
            // где часто проявляются миниатюрные копии фрактала.
            var preset2 = new MandelbrotFamilySaveState("Mandelbrot")
            {
                SaveName = "Шип Миниброта",
                CenterX = -1.74995m,
                CenterY = 0.0m,
                Zoom = 4000m,
                Iterations = 500,
                Threshold = 2.0m,
                PaletteName = "Огонь", // Обновлено: теперь соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams2 = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset2.CenterX,
                CenterY = preset2.CenterY,
                Zoom = preset2.Zoom,
                Iterations = Math.Min(preset2.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset2.PaletteName,
                Threshold = preset2.Threshold,
                PreviewEngineType = "Mandelbrot"
            };
            preset2.PreviewParametersJson = JsonSerializer.Serialize(previewParams2);
            presets.Add(preset2);

            // Пресет 3: Электрическая Розетка
            // Уникальный узор, напоминающий электрическую розетку, найденный в сложной области фрактала Мандельброта.
            var preset3 = new MandelbrotFamilySaveState("Mandelbrot")
            {
                SaveName = "Лоза",
                CenterX = 0.3855604675494107229386479028m,
                CenterY = -0.1050451711526294339131097223m,
                Zoom = 150.0m,
                Iterations = 800,
                Threshold = 2.0m,
                PaletteName = "Огонь", // Обновлено: теперь соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams3 = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset3.CenterX,
                CenterY = preset3.CenterY,
                Zoom = preset3.Zoom,
                Iterations = Math.Min(preset3.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset3.PaletteName,
                Threshold = preset3.Threshold,
                PreviewEngineType = "Mandelbrot"
            };
            preset3.PreviewParametersJson = JsonSerializer.Serialize(previewParams3);
            presets.Add(preset3);

            // Пресет 4: Спиральная Галактика
            // Область фрактала, напоминающая галактическую спираль с множеством завитков.
            var preset4 = new MandelbrotFamilySaveState("Mandelbrot")
            {
                SaveName = "Спиральная Галактика",
                CenterX = -0.16070135m,
                CenterY = 1.0375665m,
                Zoom = 3000.0m,
                Iterations = 600,
                Threshold = 2.0m,
                PaletteName = "Ультрафиолет", // Обновлено: теперь соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams4 = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset4.CenterX,
                CenterY = preset4.CenterY,
                Zoom = preset4.Zoom,
                Iterations = Math.Min(preset4.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset4.PaletteName,
                Threshold = preset4.Threshold,
                PreviewEngineType = "Mandelbrot"
            };
            preset4.PreviewParametersJson = JsonSerializer.Serialize(previewParams4);
            presets.Add(preset4);

            return presets;
        }

        /// <summary>
        /// Создает и возвращает список предустановленных состояний для фрактала Жюлиа.
        /// </summary>
        /// <returns>Список пресетов Жюлиа.</returns>
        private static List<FractalSaveStateBase> GetJuliaPresets()
        {
            var presets = new List<FractalSaveStateBase>();

            // Пресет 1: Классическая Спираль
            // Широко известный фрактал Жюлиа с характерной спиральной структурой.
            var preset1 = new JuliaFamilySaveState("Julia")
            {
                SaveName = "Классическая Спираль",
                CRe = -0.8m,
                CIm = 0.156m,
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 1.0m,
                Iterations = 500,
                Threshold = 2.0m,
                PaletteName = "Огонь",
                Timestamp = DateTime.MinValue
            };
            var previewParams1J = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset1.CenterX,
                CenterY = preset1.CenterY,
                Zoom = preset1.Zoom,
                Iterations = Math.Min(preset1.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset1.PaletteName,
                Threshold = preset1.Threshold,
                CRe = preset1.CRe,
                CIm = preset1.CIm,
                PreviewEngineType = "Julia"
            };
            preset1.PreviewParametersJson = JsonSerializer.Serialize(previewParams1J);
            presets.Add(preset1);

            // Пресет 2: Дендрит
            // Разветвленная, древовидная структура фрактала Жюлиа, часто ассоциируемая с формой корней или нейронов.
            var preset2 = new JuliaFamilySaveState("Julia")
            {
                SaveName = "Дендрит",
                CRe = 0m,
                CIm = 1.0m,
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 1.0m,
                Iterations = 300,
                Threshold = 2.0m,
                PaletteName = "Зеленый", // Обновлено: соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams2J = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset2.CenterX,
                CenterY = preset2.CenterY,
                Zoom = preset2.Zoom,
                Iterations = Math.Min(preset2.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset2.PaletteName,
                Threshold = preset2.Threshold,
                CRe = preset2.CRe,
                CIm = preset2.CIm,
                PreviewEngineType = "Julia"
            };
            preset2.PreviewParametersJson = JsonSerializer.Serialize(previewParams2J);
            presets.Add(preset2);

            // Пресет 3: Снежинка
            // Фрактал Жюлиа, напоминающий узор снежинки или кристалла льда.
            var preset3 = new JuliaFamilySaveState("Julia")
            {
                SaveName = "Снежинка",
                CRe = -0.70176m,
                CIm = -0.3842m,
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 1.0m,
                Iterations = 400,
                Threshold = 2.0m,
                PaletteName = "Лёд", // Обновлено: соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams3J = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset3.CenterX,
                CenterY = preset3.CenterY,
                Zoom = preset3.Zoom,
                Iterations = Math.Min(preset3.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset3.PaletteName,
                Threshold = preset3.Threshold,
                CRe = preset3.CRe,
                CIm = preset3.CIm,
                PreviewEngineType = "Julia"
            };
            preset3.PreviewParametersJson = JsonSerializer.Serialize(previewParams3J);
            presets.Add(preset3);

            // Пресет 4: Огненный Вихрь
            // Фрактал Жюлиа с яркими, динамичными цветами и закрученной формой, создающей эффект вихря.
            var preset4 = new JuliaFamilySaveState("Julia")
            {
                SaveName = "Огненный Вихрь",
                CRe = 0.285m,
                CIm = 0.01m,
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 1.0m,
                Iterations = 350,
                Threshold = 2.0m,
                PaletteName = "Огонь", // Обновлено: соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams4J = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset4.CenterX,
                CenterY = preset4.CenterY,
                Zoom = preset4.Zoom,
                Iterations = Math.Min(preset4.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset4.PaletteName,
                Threshold = preset4.Threshold,
                CRe = preset4.CRe,
                CIm = preset4.CIm,
                PreviewEngineType = "Julia"
            };
            preset4.PreviewParametersJson = JsonSerializer.Serialize(previewParams4J);
            presets.Add(preset4);

            return presets;
        }

        /// <summary>
        /// Создает и возвращает список предустановленных состояний для фрактала "Горящий Корабль" (Мандельброта).
        /// </summary>
        /// <returns>Список пресетов "Горящий Корабль" (Мандельброта).</returns>
        private static List<FractalSaveStateBase> GetMandelbrotBurningShipPresets()
        {
            var presets = new List<FractalSaveStateBase>();

            // Пресет 1: Центральный Корабль
            // Основная, узнаваемая форма фрактала "Горящий Корабль", расположенная в центре изображения.
            var preset1 = new MandelbrotFamilySaveState("MandelbrotBurningShip")
            {
                SaveName = "Центральный Корабль",
                CenterX = 0.0m,
                CenterY = 0.0m,
                Zoom = 0.8m,
                Iterations = 300,
                Threshold = 2.0m,
                PaletteName = "Огонь", // Обновлено: соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams1BS = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset1.CenterX,
                CenterY = preset1.CenterY,
                Zoom = preset1.Zoom,
                Iterations = Math.Min(preset1.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset1.PaletteName,
                Threshold = preset1.Threshold,
                PreviewEngineType = "MandelbrotBurningShip"
            };
            preset1.PreviewParametersJson = JsonSerializer.Serialize(previewParams1BS);
            presets.Add(preset1);

            // Пресет 2: Глубоководный Корабль
            // Увеличено на область с тонкими деталями и сложными структурами, напоминающими подводные формы.
            var preset2 = new MandelbrotFamilySaveState("MandelbrotBurningShip")
            {
                SaveName = "Глубоководный Корабль",
                CenterX = -1.7623214771385076201641266142m,
                CenterY = 0.0200163188745603751465416114m,
                Zoom = 40.0m,
                Iterations = 700,
                Threshold = 2.0m,
                PaletteName = "Ультрафиолет",
                Timestamp = DateTime.MinValue
            };
            var previewParams2BS = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset2.CenterX,
                CenterY = preset2.CenterY,
                Zoom = preset2.Zoom,
                Iterations = Math.Min(preset2.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset2.PaletteName,
                Threshold = preset2.Threshold,
                PreviewEngineType = "MandelbrotBurningShip"
            };
            preset2.PreviewParametersJson = JsonSerializer.Serialize(previewParams2BS);
            presets.Add(preset2);

            // Пресет 3: Призрачные Паруса
            // Область, напоминающая развевающиеся на ветру паруса корабля-призрака, с бледными оттенками.
            var preset3 = new MandelbrotFamilySaveState("MandelbrotBurningShip")
            {
                SaveName = "Призрачные Паруса",
                CenterX = -1.7423683296426555512135816837m,
                CenterY = 0.0648050817843091259643027922m,
                Zoom = 76.0m,     // Умеренный зум, чтобы сфокусироваться на "парусах"
                Iterations = 1000,
                Threshold = 2.0m,
                PaletteName = "Лёд", // Обновлено: соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams3BS = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset3.CenterX,
                CenterY = preset3.CenterY,
                Zoom = preset3.Zoom,
                Iterations = Math.Min(preset3.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset3.PaletteName,
                Threshold = preset3.Threshold,
                PreviewEngineType = "MandelbrotBurningShip"
            };
            preset3.PreviewParametersJson = JsonSerializer.Serialize(previewParams3BS);
            presets.Add(preset3);

            return presets;
        }

        /// <summary>
        /// Создает и возвращает список предустановленных состояний для фрактала Tricorn.
        /// </summary>
        /// <returns>Список пресетов Tricorn.</returns>
        private static List<FractalSaveStateBase> GetTricornPresets()
        {
            var presets = new List<FractalSaveStateBase>();

            var preset1 = new MandelbrotFamilySaveState("Tricorn")
            {
                SaveName = "Тройной Крест",
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 0.85m,
                Iterations = 500,
                Threshold = 2.0m,
                PaletteName = "Ультрафиолет",
                Timestamp = DateTime.MinValue
            };
            var previewParams1 = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset1.CenterX,
                CenterY = preset1.CenterY,
                Zoom = preset1.Zoom,
                Iterations = Math.Min(preset1.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset1.PaletteName,
                Threshold = preset1.Threshold,
                PreviewEngineType = "Tricorn"
            };
            preset1.PreviewParametersJson = JsonSerializer.Serialize(previewParams1);
            presets.Add(preset1);

            var preset2 = new MandelbrotFamilySaveState("Tricorn")
            {
                SaveName = "Левый Вихрь",
                CenterX = -0.295m,
                CenterY = 0.018m,
                Zoom = 9.5m,
                Iterations = 800,
                Threshold = 2.0m,
                PaletteName = "Огонь",
                Timestamp = DateTime.MinValue
            };
            var previewParams2 = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset2.CenterX,
                CenterY = preset2.CenterY,
                Zoom = preset2.Zoom,
                Iterations = Math.Min(preset2.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset2.PaletteName,
                Threshold = preset2.Threshold,
                PreviewEngineType = "Tricorn"
            };
            preset2.PreviewParametersJson = JsonSerializer.Serialize(previewParams2);
            presets.Add(preset2);

            var preset3 = new MandelbrotFamilySaveState("Tricorn")
            {
                SaveName = "Северный Лепесток",
                CenterX = -0.1096m,
                CenterY = 0.8198m,
                Zoom = 28m,
                Iterations = 1100,
                Threshold = 2.0m,
                PaletteName = "Лёд",
                Timestamp = DateTime.MinValue
            };
            var previewParams3 = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset3.CenterX,
                CenterY = preset3.CenterY,
                Zoom = preset3.Zoom,
                Iterations = Math.Min(preset3.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset3.PaletteName,
                Threshold = preset3.Threshold,
                PreviewEngineType = "Tricorn"
            };
            preset3.PreviewParametersJson = JsonSerializer.Serialize(previewParams3);
            presets.Add(preset3);

            return presets;
        }

        /// <summary>
        /// Создает и возвращает список предустановленных состояний для фрактала "Горящий Корабль" (Жюлиа).
        /// </summary>
        /// <returns>Список пресетов "Горящий Корабль" (Жюлиа).</returns>
        private static List<FractalSaveStateBase> GetJuliaBurningShipPresets()
        {
            var presets = new List<FractalSaveStateBase>();

            // Пресет 1: Космический Цветок
            // Форма фрактала Жюлиа "Горящий Корабль", напоминающая цветок или сложную космическую структуру.
            var preset1 = new JuliaFamilySaveState("JuliaBurningShip")
            {
                SaveName = "Фиолетовый Пламень Жюлиа",
                CRe = 0.598214268684387m,
                CIm = 1.17851734161377m,
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 1.0m,
                Iterations = 500,
                Threshold = 2.0m,
                PaletteName = "Ультрафиолет", // Обновлено: соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams1JBS = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset1.CenterX,
                CenterY = preset1.CenterY,
                Zoom = preset1.Zoom,
                Iterations = Math.Min(preset1.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset1.PaletteName,
                Threshold = preset1.Threshold,
                CRe = preset1.CRe,
                CIm = preset1.CIm,
                PreviewEngineType = "JuliaBurningShip"
            };
            preset1.PreviewParametersJson = JsonSerializer.Serialize(previewParams1JBS);
            presets.Add(preset1);

            // Пресет 2: Огненный Дракон
            // Динамичная форма, напоминающая голову или тело дракона, пылающего яркими, огненными цветами.
            var preset2 = new JuliaFamilySaveState("JuliaBurningShip")
            {
                SaveName = "Пульсарный Рубин",
                CRe = -0.0517381690442562m,
                CIm = -0.267557740211487m,
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 1.0m,
                Iterations = 350,
                Threshold = 2.0m,
                PaletteName = "Огонь", // Обновлено: соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams2JBS = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset2.CenterX,
                CenterY = preset2.CenterY,
                Zoom = preset2.Zoom,
                Iterations = Math.Min(preset2.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset2.PaletteName,
                Threshold = preset2.Threshold,
                CRe = preset2.CRe,
                CIm = preset2.CIm,
                PreviewEngineType = "JuliaBurningShip"
            };
            preset2.PreviewParametersJson = JsonSerializer.Serialize(previewParams2JBS);
            presets.Add(preset2);

            // Пресет 3: Туманность Андромеды (стил.)
            // Абстрактная форма, напоминающая галактическую туманность с плавными переходами цветов.
            var preset3 = new JuliaFamilySaveState("JuliaBurningShip")
            {
                SaveName = "Психонавт",
                CRe = 0.736607134342194m,
                CIm = 1.09152793884277m,
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 1.0m,
                Iterations = 500,
                Threshold = 2.0m,
                PaletteName = "Психоделика", // Обновлено: соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams3JBS = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset3.CenterX,
                CenterY = preset3.CenterY,
                Zoom = preset3.Zoom,
                Iterations = Math.Min(preset3.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset3.PaletteName,
                Threshold = preset3.Threshold,
                CRe = preset3.CRe,
                CIm = preset3.CIm,
                PreviewEngineType = "JuliaBurningShip"
            };
            preset3.PreviewParametersJson = JsonSerializer.Serialize(previewParams3JBS);
            presets.Add(preset3);

            return presets;
        }

        private static List<FractalSaveStateBase> GetBuffaloPresets()
        {
            var presets = new List<FractalSaveStateBase>();

            var preset1 = new MandelbrotFamilySaveState("Buffalo")
            {
                SaveName = "Классический Буффало",
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 0.8m,
                Iterations = 500,
                Threshold = 2.0m,
                PaletteName = "Ультрафиолет",
                Timestamp = DateTime.MinValue
            };
            var previewParams1 = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset1.CenterX,
                CenterY = preset1.CenterY,
                Zoom = preset1.Zoom,
                Iterations = Math.Min(preset1.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset1.PaletteName,
                Threshold = preset1.Threshold,
                PreviewEngineType = "Buffalo" // Указываем правильный тип движка
            };
            preset1.PreviewParametersJson = JsonSerializer.Serialize(previewParams1);
            presets.Add(preset1);

            var preset2 = new MandelbrotFamilySaveState("Buffalo")
            {
                SaveName = "Глаз Жука",
                CenterX = -1.25066m,
                CenterY = 0.3837m,
                Zoom = 1500m,
                Iterations = 800,
                Threshold = 2.0m,
                PaletteName = "Огонь",
                Timestamp = DateTime.MinValue
            };
            var previewParams2 = new FractalMandelbrotFamilyForm.PreviewParams
            {
                CenterX = preset2.CenterX,
                CenterY = preset2.CenterY,
                Zoom = preset2.Zoom,
                Iterations = Math.Min(preset2.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset2.PaletteName,
                Threshold = preset2.Threshold,
                PreviewEngineType = "Buffalo" // Указываем правильный тип движка
            };
            preset2.PreviewParametersJson = JsonSerializer.Serialize(previewParams2);
            presets.Add(preset2);

            return presets;
        }

        private static List<FractalSaveStateBase> GetSimonobrotPresets()
        {
            var presets = new List<FractalSaveStateBase>();

            var preset1 = new GeneralizedMandelbrotSaveState("Simonobrot")
            {
                SaveName = "Кристальная пещера (p=5)",
                CenterX = 0.334m,
                CenterY = 0m,
                Zoom = 2.7m,
                Iterations = 500,
                Threshold = 2.0m,
                PaletteName = "Лёд",
                Timestamp = DateTime.MinValue,
                Power = 5.0m,
                UseInversion = false,
                PreviewEngineType = "Simonobrot" // Указываем правильный тип движка
            };
            var previewParams1 = new FractalSimonobrot.SimonobrotPreviewParams
            {
                CenterX = preset1.CenterX,
                CenterY = preset1.CenterY,
                Zoom = preset1.Zoom,
                Iterations = Math.Min(preset1.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset1.PaletteName,
                Threshold = preset1.Threshold,
                PreviewEngineType = preset1.PreviewEngineType,
                Power = preset1.Power,
                UseInversion = preset1.UseInversion
            };
            preset1.PreviewParametersJson = JsonSerializer.Serialize(previewParams1);
            presets.Add(preset1);

            var preset2 = new GeneralizedMandelbrotSaveState("Simonobrot")
            {
                SaveName = "Звезда (p=-2)",
                CenterX = -0.43m,
                CenterY = 0m,
                Zoom = 1.5m,
                Iterations = 500,
                Threshold = 2.0m,
                PaletteName = "Ультрафиолет",
                Timestamp = DateTime.MinValue,
                Power = -2.0m,
                UseInversion = false,
                PreviewEngineType = "Simonobrot" // Указываем правильный тип движка
            };
            var previewParams2 = new FractalSimonobrot.SimonobrotPreviewParams
            {
                CenterX = preset2.CenterX,
                CenterY = preset2.CenterY,
                Zoom = preset2.Zoom,
                Iterations = Math.Min(preset2.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset2.PaletteName,
                Threshold = preset2.Threshold,
                PreviewEngineType = preset2.PreviewEngineType,
                Power = preset2.Power,
                UseInversion = preset2.UseInversion
            };
            preset2.PreviewParametersJson = JsonSerializer.Serialize(previewParams2);
            presets.Add(preset2);

            var preset3 = new GeneralizedMandelbrotSaveState("Simonobrot")
            {
                SaveName = "Колючка (p=-3, инверсия)",
                CenterX = 0.413m,
                CenterY = 0m,
                Zoom = 23.0m,
                Iterations = 500,
                Threshold = 2.0m,
                PaletteName = "Психоделика",
                Timestamp = DateTime.MinValue,
                Power = -3.0m,
                UseInversion = true,
                PreviewEngineType = "Simonobrot" // Указываем правильный тип движка
            };
            var previewParams3 = new FractalSimonobrot.SimonobrotPreviewParams
            {
                CenterX = preset3.CenterX,
                CenterY = preset3.CenterY,
                Zoom = preset3.Zoom,
                Iterations = Math.Min(preset3.Iterations, PREVIEW_ITERATION_LIMIT_MANDELBROT_FAMILY),
                PaletteName = preset3.PaletteName,
                Threshold = preset3.Threshold,
                PreviewEngineType = preset3.PreviewEngineType,
                Power = preset3.Power,
                UseInversion = preset3.UseInversion
            };
            preset3.PreviewParametersJson = JsonSerializer.Serialize(previewParams3);
            presets.Add(preset3);

            return presets;
        }

        #endregion

        #region Phoenix Family Presets

        /// <summary>
        /// Создает и возвращает список предустановленных состояний для фрактала Феникса.
        /// </summary>
        /// <returns>Список пресетов Феникса.</returns>
        private static List<FractalSaveStateBase> GetPhoenixPresets()
        {
            var presets = new List<FractalSaveStateBase>();

            // Пресет 1: Стандартный Феникс
            // Классический вид фрактала Феникса с характерными "глазами" и симметричной структурой.
            var preset1 = new PhoenixSaveState("Phoenix")
            {
                SaveName = "Стандартный Феникс",
                C1Re = 0.56m,
                C1Im = -0.5m,
                C2Re = 0m,
                C2Im = 0m,
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 1m,
                Iterations = 200,
                Threshold = 4.0m,
                PaletteName = "Психоделика", // Обновлено: соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams1Ph = new FractalPhoenixForm.PhoenixPreviewParams
            {
                CenterX = preset1.CenterX,
                CenterY = preset1.CenterY,
                Zoom = preset1.Zoom,
                Iterations = Math.Min(preset1.Iterations, PREVIEW_ITERATION_LIMIT_PHOENIX),
                PaletteName = preset1.PaletteName,
                Threshold = preset1.Threshold,
                C1Re = preset1.C1Re,
                C1Im = preset1.C1Im,
                C2Re = preset1.C2Re,
                C2Im = preset1.C2Im
            };
            preset1.PreviewParametersJson = JsonSerializer.Serialize(previewParams1Ph);
            presets.Add(preset1);

            // Пресет 2: Вихрь Феникса
            // Более сложная структура с закрученными вихрями и динамичными цветовыми переходами.
            var preset2 = new PhoenixSaveState("Phoenix")
            {
                SaveName = "Вихрь Феникса",
                C1Re = 0.35m,
                C1Im = -0.62m,
                C2Re = -0.01m,
                C2Im = 0.005m,
                CenterX = 0.1m,
                CenterY = -0.2m,
                Zoom = 1.2m,
                Iterations = 250,
                Threshold = 4.0m,
                PaletteName = "Психоделика", // Обновлено: соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams2Ph = new FractalPhoenixForm.PhoenixPreviewParams
            {
                CenterX = preset2.CenterX,
                CenterY = preset2.CenterY,
                Zoom = preset2.Zoom,
                Iterations = Math.Min(preset2.Iterations, PREVIEW_ITERATION_LIMIT_PHOENIX),
                PaletteName = preset2.PaletteName,
                Threshold = preset2.Threshold,
                C1Re = preset2.C1Re,
                C1Im = preset2.C1Im,
                C2Re = preset2.C2Re,
                C2Im = preset2.C2Im
            };
            preset2.PreviewParametersJson = JsonSerializer.Serialize(previewParams2Ph);
            presets.Add(preset2);

            // Пресет 3: Хвост Павлина
            // Замысловатая форма, напоминающая распущенный хвост павлина, с множеством деталей и узоров.
            var preset3 = new PhoenixSaveState("Phoenix")
            {
                SaveName = "Хвост Павлина",
                C1Re = 0.56667m,
                C1Im = -0.5m,
                C2Re = 0.001m,
                C2Im = 0.001m,
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 0.8m,
                Iterations = 300,
                Threshold = 4.0m,
                PaletteName = "Лёд", // Обновлено: соответствует новой встроенной палитре
                Timestamp = DateTime.MinValue
            };
            var previewParams3Ph = new FractalPhoenixForm.PhoenixPreviewParams
            {
                CenterX = preset3.CenterX,
                CenterY = preset3.CenterY,
                Zoom = preset3.Zoom,
                Iterations = Math.Min(preset3.Iterations, PREVIEW_ITERATION_LIMIT_PHOENIX),
                PaletteName = preset3.PaletteName,
                Threshold = preset3.Threshold,
                C1Re = preset3.C1Re,
                C1Im = preset3.C1Im,
                C2Re = preset3.C2Re,
                C2Im = preset3.C2Im
            };
            preset3.PreviewParametersJson = JsonSerializer.Serialize(previewParams3Ph);
            presets.Add(preset3);

            return presets;
        }

        #endregion

        #region Serpinsky & Newton Presets

        /// <summary>
        /// Создает и возвращает список предустановленных состояний для фрактала Серпинского.
        /// Палитры здесь не используются, вместо них - прямые цвета.
        /// </summary>
        /// <returns>Список пресетов Серпинского.</returns>
        private static List<FractalSaveStateBase> GetSerpinskyPresets()
        {
            var presets = new List<FractalSaveStateBase>();
            // Используем пользовательский конвертер для корректной сериализации объектов Color в JSON.
            var jsonOptions = new JsonSerializerOptions();
            jsonOptions.Converters.Add(new JsonColorConverter());

            // Пресет 1: Классическая Геометрия
            // Треугольник Серпинского, построенный геометрическим методом, демонстрирующий его базовую структуру.
            var preset1 = new SerpinskySaveState("Serpinsky")
            {
                SaveName = "Классическая Геометрия",
                RenderMode = SerpinskyRenderMode.Geometric,
                Iterations = 8,
                Zoom = 1.0,
                CenterX = 0,
                CenterY = 0.1,
                FractalColor = Color.Black,
                BackgroundColor = Color.White,
                Timestamp = DateTime.MinValue
            };
            var previewParams1S = new FractalSerpinski.SerpinskyPreviewParams
            {
                RenderMode = preset1.RenderMode,
                // Количество итераций для превью ограничивается, чтобы рендер был быстрым.
                Iterations = Math.Min(preset1.Iterations, PREVIEW_ITERATION_LIMIT_SERPINSKY_GEOMETRIC),
                Zoom = preset1.Zoom,
                CenterX = preset1.CenterX,
                CenterY = preset1.CenterY,
                FractalColor = preset1.FractalColor,
                BackgroundColor = preset1.BackgroundColor
            };
            preset1.PreviewParametersJson = JsonSerializer.Serialize(previewParams1S, jsonOptions);
            presets.Add(preset1);

            // Пресет 2: Ночной Хаос
            // Треугольник Серпинского, построенный методом случайных итераций, с контрастными цветами,
            // создающими эффект ночного неба.
            var preset2 = new SerpinskySaveState("Serpinsky")
            {
                SaveName = "Ночной Хаос",
                RenderMode = SerpinskyRenderMode.Chaos,
                Iterations = 100000,
                Zoom = 1.0,
                CenterX = 0,
                CenterY = 0.1,
                FractalColor = Color.OrangeRed,
                BackgroundColor = Color.FromArgb(10, 0, 20),
                Timestamp = DateTime.MinValue
            };
            var previewParams2S = new FractalSerpinski.SerpinskyPreviewParams
            {
                RenderMode = preset2.RenderMode,
                // Количество итераций для превью ограничивается, чтобы рендер был быстрым.
                Iterations = Math.Min(preset2.Iterations, PREVIEW_ITERATION_LIMIT_SERPINSKY_CHAOS),
                Zoom = preset2.Zoom,
                CenterX = preset2.CenterX,
                CenterY = preset2.CenterY,
                FractalColor = preset2.FractalColor,
                BackgroundColor = preset2.BackgroundColor
            };
            preset2.PreviewParametersJson = JsonSerializer.Serialize(previewParams2S, jsonOptions);
            presets.Add(preset2);

            return presets;
        }

        /// <summary>
        /// Создает и возвращает список предустановленных состояний для фрактала "Бассейны Ньютона".
        /// </summary>
        /// <returns>Список пресетов "Бассейны Ньютона".</returns>
        private static List<FractalSaveStateBase> GetNewtonPoolsPresets()
        {
            var presets = new List<FractalSaveStateBase>();
            // Используем пользовательский конвертер для корректной сериализации объектов Color внутри PaletteSnapshot.
            var jsonOptions = new JsonSerializerOptions();
            jsonOptions.Converters.Add(new JsonColorConverter());

            // --- Пресет 1: Классический z^3 - 1 ---
            // Стандартное отображение бассейнов притяжения для функции z^3 - 1, демонстрирующее три области притяжения.
            var palette1 = new NewtonColorPalette
            {
                Name = "NewtonPreset1_Classic",
                RootColors = new List<Color> { Color.FromArgb(255, 100, 100), Color.FromArgb(100, 255, 100), Color.FromArgb(100, 100, 255) },
                BackgroundColor = Color.FromArgb(20, 0, 0),
                IsGradient = false
            };
            var preset1 = new NewtonSaveState("NewtonPools")
            {
                SaveName = "Ньютон: z^3 - 1 (Классика)",
                Formula = "z^3-1",
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 1.0m,
                Iterations = 100,
                PaletteSnapshot = palette1,
                Timestamp = DateTime.MinValue
            };
            var previewParams1N = new NewtonPools.NewtonPreviewParams
            {
                Formula = preset1.Formula,
                CenterX = preset1.CenterX,
                CenterY = preset1.CenterY,
                Zoom = preset1.Zoom,
                // Количество итераций для превью ограничивается, чтобы рендер был быстрым.
                Iterations = Math.Min(preset1.Iterations, PREVIEW_ITERATION_LIMIT_NEWTON_CHAOS),
                PaletteSnapshot = preset1.PaletteSnapshot
            };
            preset1.PreviewParametersJson = JsonSerializer.Serialize(previewParams1N, jsonOptions);
            presets.Add(preset1);

            // --- Пресет 2: z^4 - 1 с градиентом ---
            // Отображение бассейнов притяжения для функции z^4 - 1 с градиентным раскрашиванием,
            // подчеркивающим границы между областями.
            var palette2 = new NewtonColorPalette
            {
                Name = "NewtonPreset2_Gradient",
                RootColors = new List<Color> { Color.Cyan, Color.Magenta, Color.Yellow, Color.Lime },
                BackgroundColor = Color.Black,
                IsGradient = true
            };
            var preset2 = new NewtonSaveState("NewtonPools")
            {
                SaveName = "Ньютон: z^4 - 1 (Градиент)",
                Formula = "z^4-1",
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 1.2m,
                Iterations = 80,
                PaletteSnapshot = palette2,
                Timestamp = DateTime.MinValue
            };
            var previewParams2N = new NewtonPools.NewtonPreviewParams
            {
                Formula = preset2.Formula,
                CenterX = preset2.CenterX,
                CenterY = preset2.CenterY,
                Zoom = preset2.Zoom,
                Iterations = Math.Min(preset2.Iterations, PREVIEW_ITERATION_LIMIT_NEWTON_CHAOS),
                PaletteSnapshot = preset2.PaletteSnapshot
            };
            preset2.PreviewParametersJson = JsonSerializer.Serialize(previewParams2N, jsonOptions);
            presets.Add(preset2);

            // --- Пресет 3: Более сложная функция z^5 - z^2 + 1 ---
            // Демонстрация бассейнов притяжения для более комплексной полиномиальной функции,
            // приводящей к более сложным и интересным фрактальным границам.
            var palette3 = new NewtonColorPalette
            {
                Name = "NewtonPreset3_Complex",
                RootColors = new List<Color> { Color.Orange, Color.Purple, Color.GreenYellow, Color.SkyBlue, Color.HotPink },
                BackgroundColor = Color.FromArgb(10, 10, 30),
                IsGradient = false
            };
            var preset3 = new NewtonSaveState("NewtonPools")
            {
                SaveName = "Ньютон: z^5 - z^2 + 1",
                Formula = "z^5 - z^2 + 1",
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 1.5m,
                Iterations = 120,
                PaletteSnapshot = palette3,
                Timestamp = DateTime.MinValue
            };
            var previewParams3N = new NewtonPools.NewtonPreviewParams
            {
                Formula = preset3.Formula,
                CenterX = preset3.CenterX,
                CenterY = preset3.CenterY,
                Zoom = preset3.Zoom,
                Iterations = Math.Min(preset3.Iterations, PREVIEW_ITERATION_LIMIT_NEWTON_CHAOS),
                PaletteSnapshot = preset3.PaletteSnapshot
            };
            preset3.PreviewParametersJson = JsonSerializer.Serialize(previewParams3N, jsonOptions);
            presets.Add(preset3);

            // --- Пресет 4: z^3 - 2*z + 2 (из вашего списка) с градиентом и другим центром ---
            // Бассейны притяжения для функции z^3 - 2z + 2, с измененным центром и градиентной палитрой,
            // демонстрирующие смещенные структуры.
            var palette4 = new NewtonColorPalette
            {
                Name = "NewtonPreset4_Shifted",
                RootColors = new List<Color> { Color.Teal, Color.Gold, Color.Crimson },
                BackgroundColor = Color.FromArgb(5, 5, 5),
                IsGradient = true
            };
            var preset4 = new NewtonSaveState("NewtonPools")
            {
                SaveName = "Ньютон: z^3-2*z+2 (Сдвиг)",
                Formula = "z^3-2*z+2",
                CenterX = 0.5m,
                CenterY = -0.3m,
                Zoom = 2.0m,
                Iterations = 150,
                PaletteSnapshot = palette4,
                Timestamp = DateTime.MinValue
            };
            var previewParams4N = new NewtonPools.NewtonPreviewParams
            {
                Formula = preset4.Formula,
                CenterX = preset4.CenterX,
                CenterY = preset4.CenterY,
                Zoom = preset4.Zoom,
                Iterations = Math.Min(preset4.Iterations, PREVIEW_ITERATION_LIMIT_NEWTON_CHAOS),
                PaletteSnapshot = preset4.PaletteSnapshot
            };
            preset4.PreviewParametersJson = JsonSerializer.Serialize(previewParams4N, jsonOptions);
            presets.Add(preset4);

            return presets;
        }

        #endregion

        #region Collatz Presets

        /// <summary>
        /// Создает и возвращает список предустановленных состояний для фрактала Коллатца.
        /// </summary>
        /// <returns>Список пресетов Коллатца.</returns>
        private static List<FractalSaveStateBase> GetCollatzPresets()
        {
            var presets = new List<FractalSaveStateBase>();
            var jsonOptions = new JsonSerializerOptions();

            // Пресет 1: "Стандартный Коллатц"
            // Демонстрирует базовый вид фрактала с параметрами по умолчанию и стандартной серой палитрой.
            var preset1 = new CollatzSaveState("Collatz")
            {
                SaveName = "Стандартный Коллатц",
                CenterX = 0m,
                CenterY = 0m,
                Zoom = 1.0m,
                Iterations = 150,
                Threshold = 100.0m,
                PaletteName = "Стандартный серый",
                Timestamp = DateTime.MinValue,
                PreviewEngineType = "Collatz"
            };
            var previewParams1 = new FractalCollatzForm.CollatzPreviewParams
            {
                CenterX = preset1.CenterX,
                CenterY = preset1.CenterY,
                Zoom = preset1.Zoom,
                Iterations = Math.Min(preset1.Iterations, 150), // Ограничение для быстрого рендера превью
                PaletteName = preset1.PaletteName,
                Threshold = preset1.Threshold,
                UseSmoothColoring = false
            };
            preset1.PreviewParametersJson = JsonSerializer.Serialize(previewParams1, jsonOptions);
            presets.Add(preset1);

            return presets;
        }

        #endregion
    }
}
