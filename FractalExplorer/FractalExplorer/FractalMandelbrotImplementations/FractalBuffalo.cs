using FractalDraving;
using FractalExplorer.Engines;
using FractalExplorer.Utilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Text.Json;
using FractalExplorer.Utilities.RenderUtilities;

namespace FractalExplorer.Projects
{
    /// <summary>
    /// Форма для отображения и взаимодействия с фракталом Буффало.
    /// </summary>
    public partial class FractalBuffalo : FractalMandelbrotFamilyForm
    {
        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="FractalBuffalo"/>.
        /// </summary>
        public FractalBuffalo()
        {
            Text = "Фрактал Буффало";
        }

        /// <summary>
        /// Создает и возвращает экземпляр движка, специфичного для фрактала Буффало.
        /// </summary>
        /// <returns>Экземпляр <see cref="BuffaloEngine"/>.</returns>
        protected override FractalMandelbrotFamilyEngine CreateEngine()
        {
            return new BuffaloEngine();
        }

        /// <summary>
        /// Выполняет действия после основной инициализации формы.
        /// Скрывает элементы управления, нерелевантные для фрактала Буффало.
        /// </summary>
        protected override void OnPostInitialize()
        {
            lblRe.Visible = false;
            nudRe.Visible = false;
            lblIm.Visible = false;
            nudIm.Visible = false;
            mandelbrotPreviewPanel.Visible = false;
        }

        /// <summary>
        /// Возвращает строку, используемую для формирования имени файла сохранения.
        /// </summary>
        /// <returns>Строка "buffalo", идентифицирующая тип фрактала в имени файла.</returns>
        protected override string GetSaveFileNameDetails()
        {
            return "buffalo";
        }

        #region ISaveLoadCapableFractal Implementation

        /// <summary>
        /// Получает строковый идентификатор для типа фрактала "Буффало".
        /// </summary>
        public override string FractalTypeIdentifier => "Buffalo";

        /// <summary>
        /// Получает тип конкретного класса состояния сохранения для фракталов семейства Мандельброта.
        /// </summary>
        public override Type ConcreteSaveStateType => typeof(MandelbrotFamilySaveState);

        /// <summary>
        /// Собирает текущее состояние фрактала в объект для последующего сохранения.
        /// </summary>
        /// <param name="saveName">Имя, присваиваемое сохранению.</param>
        /// <returns>Объект <see cref="FractalSaveStateBase"/>, содержащий все параметры текущего вида фрактала.</returns>
        public override FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new MandelbrotFamilySaveState(this.FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                Threshold = nudThreshold.Value,
                Iterations = (int)nudIterations.Value,
                PaletteName = _paletteManager.ActivePalette?.Name ?? "Стандартный серый",
                PreviewEngineType = this.FractalTypeIdentifier
            };

            var previewParams = new PreviewParams
            {
                CenterX = state.CenterX,
                CenterY = state.CenterY,
                Zoom = state.Zoom,
                Iterations = state.Iterations,
                PaletteName = state.PaletteName,
                Threshold = state.Threshold,
                PreviewEngineType = state.PreviewEngineType
            };
            state.PreviewParametersJson = JsonSerializer.Serialize(previewParams, new JsonSerializerOptions());
            return state;
        }

        /// <summary>
        /// Загружает состояние фрактала из указанного объекта сохранения.
        /// </summary>
        /// <param name="stateBase">Состояние для загрузки.</param>
        public override void LoadState(FractalSaveStateBase stateBase)
        {
            base.LoadState(stateBase);
        }

        /// <summary>
        /// Загружает все сохранения, относящиеся к типу фрактала "Буффало".
        /// </summary>
        /// <returns>Список объектов состояний сохранения.</returns>
        public override List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            var specificSaves = SaveFileManager.LoadSaves<MandelbrotFamilySaveState>(this.FractalTypeIdentifier);
            return specificSaves.Cast<FractalSaveStateBase>().ToList();
        }

        /// <summary>
        /// Сохраняет список состояний для фрактала "Буффало".
        /// </summary>
        /// <param name="saves">Список состояний для сохранения.</param>
        public override void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            var specificSaves = saves.Cast<MandelbrotFamilySaveState>().ToList();
            SaveFileManager.SaveSaves(this.FractalTypeIdentifier, specificSaves);
        }

        /// <summary>
        /// Асинхронно рендерит плитку (часть) изображения для предварительного просмотра.
        /// </summary>
        /// <param name="stateBase">Состояние фрактала для рендеринга.</param>
        /// <param name="tile">Информация о плитке (координаты и размер).</param>
        /// <param name="totalWidth">Общая ширина изображения предварительного просмотра.</param>
        /// <param name="totalHeight">Общая высота изображения предварительного просмотра.</param>
        /// <param name="tileSize">Размер плитки (не используется в данной реализации, но является частью интерфейса).</param>
        /// <returns>Задача, возвращающая массив байтов (пиксели в формате BGRA) для указанной плитки.</returns>
        public override Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase stateBase, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(stateBase.PreviewParametersJson))
                    return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];

                PreviewParams previewParams;
                try
                {
                    previewParams = JsonSerializer.Deserialize<PreviewParams>(stateBase.PreviewParametersJson);
                }
                catch { return new byte[tile.Bounds.Width * tile.Bounds.Height * 4]; }

                var previewEngine = new BuffaloEngine();

                previewEngine.MaxIterations = previewParams.Iterations;
                previewEngine.CenterX = previewParams.CenterX;
                previewEngine.CenterY = previewParams.CenterY;
                if (previewParams.Zoom == 0) previewParams.Zoom = 0.001m;
                previewEngine.Scale = BaseScale / previewParams.Zoom;
                previewEngine.ThresholdSquared = previewParams.Threshold * previewParams.Threshold;

                var paletteManager = new PaletteManager();
                var paletteForPreview = paletteManager.Palettes.FirstOrDefault(p => p.Name == previewParams.PaletteName) ?? paletteManager.Palettes.First();
                int effectiveMaxColorIterations = paletteForPreview.AlignWithRenderIterations ? previewEngine.MaxIterations : paletteForPreview.MaxColorIterations;

                previewEngine.UseSmoothColoring = false;
                previewEngine.MaxColorIterations = effectiveMaxColorIterations;
                previewEngine.Palette = GenerateDiscretePaletteFunction(paletteForPreview);
                previewEngine.SmoothPalette = GenerateSmoothPaletteFunction(paletteForPreview, effectiveMaxColorIterations);

                return previewEngine.RenderSingleTile(tile, totalWidth, totalHeight, out _);
            });
        }

        /// <summary>
        /// Рендерит полное изображение для предварительного просмотра на основе состояния сохранения.
        /// </summary>
        /// <param name="stateBase">Состояние фрактала для рендеринга.</param>
        /// <param name="previewWidth">Ширина изображения предварительного просмотра.</param>
        /// <param name="previewHeight">Высота изображения предварительного просмотра.</param>
        /// <returns>Объект <see cref="Bitmap"/> с полным изображением предварительного просмотра.</returns>
        public override Bitmap RenderPreview(FractalSaveStateBase stateBase, int previewWidth, int previewHeight)
        {
            var tile = new TileInfo(0, 0, previewWidth, previewHeight);
            byte[] buffer = RenderPreviewTileAsync(stateBase, tile, previewWidth, previewHeight, previewWidth).Result;

            var bmp = new Bitmap(previewWidth, previewHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bmpData = bmp.LockBits(new Rectangle(0, 0, previewWidth, previewHeight), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);

            System.Runtime.InteropServices.Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
            bmp.UnlockBits(bmpData);

            Bitmap finalBmp = new Bitmap(bmp);
            bmp.Dispose();

            return finalBmp;
        }

        #endregion
    }
}
