using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;

namespace FractalExplorer.Utilities.SaveIO
{
    /// <summary>
    /// Опциональный контракт для фракталов, которым требуется полноразмерный
    /// (неплиточный) рендер превью с прогрессом.
    /// </summary>
    public interface IFullPreviewRenderCapableFractal
    {
        /// <summary>
        /// Асинхронно рендерит превью целиком и возвращает готовый буфер пикселей.
        /// </summary>
        /// <param name="state">Состояние фрактала для рендера.</param>
        /// <param name="previewWidth">Ширина превью.</param>
        /// <param name="previewHeight">Высота превью.</param>
        /// <param name="cancellationToken">Токен отмены операции.</param>
        /// <param name="progress">Прогресс рендера в процентах (0..100).</param>
        /// <returns>Массив пикселей в формате 32bpp ARGB.</returns>
        Task<byte[]> RenderPreviewAsync(
            FractalSaveStateBase state,
            int previewWidth,
            int previewHeight,
            CancellationToken cancellationToken,
            IProgress<int>? progress = null);
    }
}
