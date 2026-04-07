using FractalExplorer.Engines;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;

namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    /// <summary>
    /// Представляет состояние сохранения для фракталов Ньютона.
    /// Включает параметры, такие как формула, центр, масштаб, итерации и снимок цветовой палитры.
    /// </summary>
    public class NewtonSaveState : FractalSaveStateBase
    {
        /// <summary>
        /// Получает или задает математическую формулу, используемую для генерации фрактала Ньютона.
        /// </summary>
        public string Formula { get; set; }

        /// <summary>
        /// Получает или задает X-координату центра фрактала.
        /// Используется тип <see cref="decimal"/> для высокой точности, соответствующей другим формам.
        /// </summary>
        public decimal CenterX { get; set; }

        /// <summary>
        /// Получает или задает Y-координату центра фрактала.
        /// Используется тип <see cref="decimal"/> для высокой точности, соответствующей другим формам.
        /// </summary>
        public decimal CenterY { get; set; }

        /// <summary>
        /// Получает или задает коэффициент масштабирования (зум) фрактала.
        /// </summary>
        public decimal Zoom { get; set; }

        /// <summary>
        /// Получает или задает максимальное количество итераций для расчета каждой точки фрактала.
        /// </summary>
        public int Iterations { get; set; }

        /// <summary>
        /// Получает или задает снимок (копию) цветовой палитры, используемой для рендеринга фрактала.
        /// Это позволяет точно воспроизвести цвета, даже если исходная палитра изменится или будет удалена.
        /// </summary>
        public NewtonColorPalette PaletteSnapshot { get; set; }

        /// <summary>
        /// Выбранный метод итерации для построения бассейнов.
        /// </summary>
        public NewtonIterationMethod IterationMethod { get; set; } = NewtonIterationMethod.Newton;

        /// <summary>
        /// Порядок метода Householder (используется только для Householder-режима).
        /// </summary>
        public int HouseholderOrder { get; set; } = 3;

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="NewtonSaveState"/>.
        /// Этот конструктор предназначен для использования десериализатором JSON.
        /// </summary>
        public NewtonSaveState()
        {
            // Пустой конструктор необходим для корректной десериализации JSON.
        }

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="NewtonSaveState"/>
        /// с указанным идентификатором типа фрактала.
        /// </summary>
        /// <param name="fractalType">Строковый идентификатор типа фрактала (например, "Newton").</param>
        public NewtonSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
