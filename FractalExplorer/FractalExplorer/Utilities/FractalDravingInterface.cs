namespace FractalExplorer.Utilities
{
    /// <summary>
    /// Определяет общий контракт для форм фракталов,
    /// позволяя другим компонентам (например, селекторам) взаимодействовать с ними.
    /// </summary>
    public interface IFractalForm
    {
        #region Interface Definition

        /// <summary>
        /// Получает текущий коэффициент масштабирования, используемый для "лупы" или увеличения.
        /// </summary>
        double LoupeZoom { get; }

        /// <summary>
        /// Событие, которое возникает при изменении значения масштаба "лупы".
        /// Другие компоненты могут подписываться на это событие, чтобы реагировать на изменения масштаба.
        /// </summary>
        event EventHandler LoupeZoomChanged;

        #endregion
    }
}