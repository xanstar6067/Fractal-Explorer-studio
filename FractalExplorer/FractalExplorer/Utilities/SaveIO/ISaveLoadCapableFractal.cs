using FractalExplorer.Utilities;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using FractalExplorer.Utilities.RenderUtilities;

namespace FractalExplorer.Utilities.SaveIO
{
    /// <summary>
    /// Определяет контракт для фрактальных форм, способных сохранять и загружать свое состояние.
    /// Предоставляет методы для управления сохранениями, включая получение текущего состояния,
    /// загрузку состояния, рендеринг предварительного просмотра и управление коллекциями сохранений.
    /// </summary>
    public interface ISaveLoadCapableFractal
    {
        /// <summary>
        /// Получает строковый идентификатор типа фрактала, используемый для различения сохранений.
        /// </summary>
        string FractalTypeIdentifier { get; }

        /// <summary>
        /// Получает конкретный тип объекта состояния сохранения, используемый этим фракталом.
        /// Это необходимо для корректной десериализации сохраненных данных.
        /// </summary>
        Type ConcreteSaveStateType { get; }

        /// <summary>
        /// Получает текущее состояние фрактала для сохранения.
        /// </summary>
        /// <param name="saveName">Имя, присваиваемое этому сохранению пользователем.</param>
        /// <returns>Базовый объект состояния фрактала, содержащий все необходимые параметры.</returns>
        FractalSaveStateBase GetCurrentStateForSave(string saveName);

        /// <summary>
        /// Загружает состояние фрактала из предоставленного объекта состояния.
        /// Применяет параметры из объекта состояния к текущей форме фрактала.
        /// </summary>
        /// <param name="state">Базовый объект состояния фрактала для загрузки.</param>
        void LoadState(FractalSaveStateBase state);

        /// <summary>
        /// Рендерит изображение предварительного просмотра фрактала на основе заданного состояния.
        /// Используется для отображения миниатюр в пользовательском интерфейсе сохранения/загрузки.
        /// </summary>
        /// <param name="state">Состояние фрактала, параметры которого будут использоваться для рендеринга.</param>
        /// <param name="previewWidth">Желаемая ширина изображения предварительного просмотра в пикселях.</param>
        /// <param name="previewHeight">Желаемая высота изображения предварительного просмотра в пикселях.</param>
        /// <returns>Объект <see cref="Bitmap"/>, содержащий отрендеренное изображение предварительного просмотра.</returns>
        Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight);

        /// <summary>
        /// Асинхронно рендерит отдельную плитку (фрагмент) предварительного просмотра для заданного состояния фрактала.
        /// Этот метод позволяет эффективно отображать большие предварительные просмотры по частям.
        /// </summary>
        /// <param name="state">Состояние фрактала, параметры которого будут использоваться для рендеринга.</param>
        /// <param name="tile">Объект <see cref="TileInfo"/>, описывающий границы и положение плитки.</param>
        /// <param name="totalWidth">Общая ширина всего изображения предварительного просмотра, частью которого является эта плитка.</param>
        /// <param name="totalHeight">Общая высота всего изображения предварительного просмотра, частью которого является эта плитка.</param>
        /// <param name="tileSize">Размер стороны квадратной плитки в пикселях (может не использоваться, если рендерится весь битмап).</param>
        /// <returns>Массив байтов, представляющий данные пикселей отрендеренной плитки.</returns>
        Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize);

        /// <summary>
        /// Загружает все сохраненные состояния для данного типа фрактала.
        /// </summary>
        /// <returns>Список базовых объектов состояний фрактала, относящихся к текущему типу.</returns>
        List<FractalSaveStateBase> LoadAllSavesForThisType();

        /// <summary>
        /// Сохраняет предоставленный список состояний фрактала для данного типа.
        /// Этот метод используется для сохранения всех текущих сохранений фрактала после изменений (например, добавления/удаления).
        /// </summary>
        /// <param name="saves">Список базовых объектов состояний фрактала для сохранения.</param>
        void SaveAllSavesForThisType(List<FractalSaveStateBase> saves);
    }
}
