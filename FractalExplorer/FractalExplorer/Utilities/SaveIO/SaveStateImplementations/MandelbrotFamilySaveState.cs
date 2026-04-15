namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    /// <summary>
    /// Представляет состояние сохранения для фракталов семейства Мандельброта,
    /// включая общие параметры рендеринга, такие как центр, масштаб, итерации и выбранная палитра.
    /// </summary>
    public class MandelbrotFamilySaveState : FractalSaveStateBase
    {
        /// <summary>
        /// Получает или задает X-координату центра фрактала в комплексной плоскости.
        /// </summary>
        public decimal CenterX { get; set; }

        /// <summary>
        /// Получает или задает Y-координату центра фрактала в комплексной плоскости.
        /// </summary>
        public decimal CenterY { get; set; }

        /// <summary>
        /// Получает или задает коэффициент масштабирования (зум) фрактала.
        /// </summary>
        public decimal Zoom { get; set; }

        /// <summary>
        /// Получает или задает пороговое значение, используемое для определения того,
        /// находится ли точка внутри множества Мандельброта (или его аналогов).
        /// </summary>
        public decimal Threshold { get; set; }

        /// <summary>
        /// Получает или задает максимальное количество итераций,
        /// используемых при вычислении цвета каждой точки фрактала.
        /// </summary>
        public int Iterations { get; set; }

        /// <summary>
        /// Получает или задает имя цветовой палитры, примененной к фракталу.
        /// </summary>
        public string PaletteName { get; set; }

        /// <summary>
        /// Получает или задает тип движка рендеринга, использованный для создания предварительного просмотра
        /// (например, "Mandelbrot", "Julia" или "BurningShipMandelbrot"),
        /// что позволяет загрузчику выбрать правильный движок для отрисовки миниатюры.
        /// </summary>
        public string PreviewEngineType { get; set; }

        /// <summary>
        /// Получает или задает активный режим окрашивания.
        /// Значение nullable для обратной совместимости со старыми сохранениями.
        /// </summary>
        public int? ColoringMode { get; set; }

        /// <summary>
        /// Коэффициент A полинома режима Smooth Escape (Poly).
        /// </summary>
        public double? SmoothEscapePolyCoeffA { get; set; }
        /// <summary>
        /// Коэффициент B полинома режима Smooth Escape (Poly).
        /// </summary>
        public double? SmoothEscapePolyCoeffB { get; set; }
        /// <summary>
        /// Коэффициент C полинома режима Smooth Escape (Poly).
        /// </summary>
        public double? SmoothEscapePolyCoeffC { get; set; }
        /// <summary>
        /// Гамма-коррекция режима Smooth Escape (Poly).
        /// </summary>
        public double? SmoothEscapePolyGamma { get; set; }
        /// <summary>
        /// Параметр смешивания для режима Smooth Escape (Poly).
        /// </summary>
        public double? SmoothEscapePolyBlend { get; set; }
        /// <summary>
        /// Смещение результата для режима Smooth Escape (Poly).
        /// </summary>
        public double? SmoothEscapePolyBias { get; set; }

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="MandelbrotFamilySaveState"/>.
        /// Этот конструктор предназначен для использования десериализатором JSON.
        /// </summary>
        public MandelbrotFamilySaveState()
        {
            // Пустой конструктор необходим для корректной десериализации JSON.
        }

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="MandelbrotFamilySaveState"/>
        /// с указанным идентификатором типа фрактала.
        /// </summary>
        /// <param name="fractalType">Строковый идентификатор типа фрактала
        /// (например, "Mandelbrot", "BurningShipMandelbrot").</param>
        public MandelbrotFamilySaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }

    /// <summary>
    /// Представляет состояние сохранения для фракталов семейства Жюлиа,
    /// расширяя общие параметры Мандельброта дополнительными параметрами комплексной константы C,
    /// которая определяет форму конкретного множества Жюлиа.
    /// </summary>
    public class JuliaFamilySaveState : MandelbrotFamilySaveState
    {
        /// <summary>
        /// Получает или задает действительную часть комплексной константы C,
        /// используемой в итерационной формуле для фрактала Жюлиа.
        /// </summary>
        public decimal CRe { get; set; }

        /// <summary>
        /// Получает или задает мнимую часть комплексной константы C,
        /// используемой в итерационной формуле для фрактала Жюлиа.
        /// </summary>
        public decimal CIm { get; set; }

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="JuliaFamilySaveState"/>.
        /// Этот конструктор предназначен для использования десериализатором JSON.
        /// </summary>
        public JuliaFamilySaveState() : base()
        {
            // Пустой конструктор необходим для корректной десериализации JSON,
            // а также вызывает базовый конструктор для инициализации.
        }

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="JuliaFamilySaveState"/>
        /// с указанным идентификатором типа фрактала.
        /// </summary>
        /// <param name="fractalType">Строковый идентификатор типа фрактала (например, "Julia").</param>
        public JuliaFamilySaveState(string fractalType) : base(fractalType)
        {
            // Вызывает базовый конструктор для установки типа фрактала.
        }
    }
}
