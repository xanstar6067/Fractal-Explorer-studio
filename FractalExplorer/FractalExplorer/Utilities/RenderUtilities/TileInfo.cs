namespace FractalExplorer.Utilities.RenderUtilities
{
    /// <summary>
    /// Представляет одну плитку (тайл) для мозаичного рендеринга.
    /// Эта структура является неизменяемой (immutable).
    /// </summary>
    public readonly struct TileInfo
    {
        #region Fields

        /// <summary>
        /// Границы плитки в пиксельных координатах.
        /// </summary>
        public readonly Rectangle Bounds;

        /// <summary>
        /// Центральная точка плитки, используемая для сортировки (например, рендеринг от центра к краям).
        /// </summary>
        public readonly Point Center;

        #endregion

        #region Constructor

        /// <summary>
        /// Инициализирует новый экземпляр структуры <see cref="TileInfo"/>
        /// с указанными координатами и размерами.
        /// </summary>
        /// <param name="x">X-координата плитки.</param>
        /// <param name="y">Y-координата плитки.</param>
        /// <param name="width">Ширина плитки.</param>
        /// <param name="height">Высота плитки.</param>
        public TileInfo(int x, int y, int width, int height)
        {
            Bounds = new Rectangle(x, y, width, height);
            Center = new Point(x + width / 2, y + height / 2); // Вычисляем центр плитки
        }

        #endregion
    }
}