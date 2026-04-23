using FractalDraving;
using FractalExplorer.Engines;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Globalization;
using System.Text.Json;

namespace FractalExplorer.Projects
{
    /// <summary>
    /// Представляет форму для отображения и взаимодействия с Обобщенным множеством Мандельброта (z -> z^p + c).
    /// </summary>
    public partial class FractalGeneralizedMandelbrot : FractalMandelbrotFamilyForm
    {
        /// <summary>
        /// Поле для ввода степени p.
        /// </summary>
        private NumericUpDown nudPower;

        /// <summary>
        /// Метка для поля ввода степени.
        /// </summary>
        private Label lblPower;

        #region Constructor

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="FractalGeneralizedMandelbrot"/>.
        /// </summary>
        public FractalGeneralizedMandelbrot()
        {
            Text = "Обобщённое множество Мандельброта (z^p + c)";
        }

        #endregion

        #region Fractal Engine Overrides

        /// <summary>
        /// Создает и возвращает экземпляр движка для рендеринга обобщенного множества Мандельброта.
        /// </summary>
        /// <returns>Экземпляр <see cref="GeneralizedMandelbrotEngine"/>.</returns>
        protected override FractalMandelbrotFamilyEngine CreateEngine()
        {
            return new GeneralizedMandelbrotEngine();
        }

        /// <summary>
        /// Выполняется после основной инициализации формы для настройки пользовательского интерфейса.
        /// Добавляет элементы управления для параметра "степень" (p).
        /// </summary>
        protected override void OnPostInitialize()
        {
            // Скрываем стандартные элементы управления для константы c (Re, Im), так как они не используются.
            lblRe.Visible = false;
            nudRe.Visible = false;
            lblIm.Visible = false;
            nudIm.Visible = false;
            mandelbrotPreviewPanel.Visible = false;

            // Отображаем панель для пользовательских элементов управления.
            pnlCustomControls.Visible = true;

            // Создаем внутреннюю таблицу для компоновки элементов управления.
            var innerTable = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill,
            };
            innerTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            innerTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));

            // Создаем и настраиваем элементы управления для параметра "степень".
            lblPower = new Label
            {
                Text = "Степень (p)",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            nudPower = new NumericUpDown
            {
                Minimum = 2,
                Maximum = 10,
                DecimalPlaces = 2,
                Increment = 0.1m,
                Value = 3.0m,
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 3, 3, 3)
            };
            nudPower.ValueChanged += ParamControl_Changed;

            // Добавляем созданные элементы управления на панель.
            innerTable.Controls.Add(nudPower, 0, 0);
            innerTable.Controls.Add(lblPower, 1, 0);
            pnlCustomControls.Controls.Add(innerTable);
        }

        /// <summary>
        /// Обновляет специфичные для этого фрактала параметры в движке рендеринга.
        /// </summary>
        protected override void UpdateEngineSpecificParameters()
        {
            if (_fractalEngine is GeneralizedMandelbrotEngine engine)
            {
                engine.Power = nudPower.Value;
            }
        }

        /// <summary>
        /// Формирует уникальную часть имени файла для сохранения, основанную на текущем значении степени.
        /// </summary>
        /// <returns>Строка с деталями для имени файла.</returns>
        protected override string GetSaveFileNameDetails()
        {
            string powerString = nudPower.Value.ToString("F2", CultureInfo.InvariantCulture).Replace(".", "_");
            return $"gen_mandelbrot_p{powerString}";
        }

        #endregion

        #region ISaveLoadCapableFractal Overrides

        /// <summary>
        /// Получает уникальный идентификатор типа фрактала для системы сохранения/загрузки.
        /// </summary>
        public override string FractalTypeIdentifier => "GeneralizedMandelbrot";

        /// <summary>
        /// Получает тип конкретного класса состояния сохранения для этого фрактала.
        /// </summary>
        public override Type ConcreteSaveStateType => typeof(GeneralizedMandelbrotSaveState);

        /// <summary>
        /// Содержит параметры, необходимые для рендеринга превью обобщенного множества Мандельброта.
        /// </summary>
        public class GeneralizedMandelbrotPreviewParams : PreviewParams
        {
            /// <summary>
            /// Степень (p) для формулы фрактала.
            /// </summary>
            public decimal Power { get; set; }
        }

        /// <summary>
        /// Собирает текущее состояние фрактала в объект для последующего сохранения.
        /// </summary>
        /// <param name="saveName">Имя сохранения.</param>
        /// <returns>Объект состояния <see cref="FractalSaveStateBase"/>, готовый к сохранению.</returns>
        public override FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new GeneralizedMandelbrotSaveState(FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                Threshold = nudThreshold.Value,
                Iterations = (int)nudIterations.Value,
                PaletteName = _paletteManager.ActivePalette?.Name ?? "Стандартный серый",
                PreviewEngineType = FractalTypeIdentifier,
                Power = nudPower.Value
            };

            var previewParams = new GeneralizedMandelbrotPreviewParams
            {
                CenterX = state.CenterX,
                CenterY = state.CenterY,
                Zoom = state.Zoom,
                Iterations = Math.Min(state.Iterations, 1000), // Ограничение для скорости превью
                PaletteName = state.PaletteName,
                Threshold = state.Threshold,
                PreviewEngineType = state.PreviewEngineType,
                Power = state.Power
            };

            state.PreviewParametersJson = JsonSerializer.Serialize(previewParams, new JsonSerializerOptions());
            return state;
        }

        /// <summary>
        /// Загружает состояние фрактала из предоставленного объекта сохранения.
        /// </summary>
        /// <param name="stateBase">Объект состояния для загрузки.</param>
        public override void LoadState(FractalSaveStateBase stateBase)
        {
            base.LoadState(stateBase);
            if (stateBase is GeneralizedMandelbrotSaveState state)
            {
                nudPower.Value = Math.Max(nudPower.Minimum, Math.Min(nudPower.Maximum, state.Power));
            }
        }

        /// <summary>
        /// Загружает все сохранения, относящиеся к данному типу фрактала.
        /// </summary>
        /// <returns>Список объектов состояний.</returns>
        public override List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            var specificSaves = SaveFileManager.LoadSaves<GeneralizedMandelbrotSaveState>(FractalTypeIdentifier);
            return specificSaves.Cast<FractalSaveStateBase>().ToList();
        }

        /// <summary>
        /// Сохраняет все состояния для данного типа фрактала.
        /// </summary>
        /// <param name="saves">Список состояний для сохранения.</param>
        public override void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            var specificSaves = saves.Cast<GeneralizedMandelbrotSaveState>().ToList();
            SaveFileManager.SaveSaves(FractalTypeIdentifier, specificSaves);
        }

        /// <summary>
        /// Асинхронно рендерит один тайл превью изображения на основе данных из состояния сохранения.
        /// </summary>
        /// <param name="stateBase">Состояние сохранения, содержащее параметры для рендеринга.</param>
        /// <param name="tile">Информация о тайле для рендеринга.</param>
        /// <param name="totalWidth">Общая ширина превью.</param>
        /// <param name="totalHeight">Общая высота превью.</param>
        /// <param name="tileSize">Размер тайла.</param>
        /// <returns>Массив байт (BGRA) с пиксельными данными тайла.</returns>
        public override Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase stateBase, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(stateBase.PreviewParametersJson))
                    return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];

                GeneralizedMandelbrotPreviewParams previewParams;
                try
                {
                    previewParams = JsonSerializer.Deserialize<GeneralizedMandelbrotPreviewParams>(stateBase.PreviewParametersJson);
                }
                catch
                {
                    return new byte[tile.Bounds.Width * tile.Bounds.Height * 4]; // Возврат пустого буфера в случае ошибки
                }

                var previewEngine = new GeneralizedMandelbrotEngine();

                // Установка специфичных для этого фрактала и общих параметров для движка
                previewEngine.Power = previewParams.Power;
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
        /// Рендерит полное превью-изображение для заданного состояния сохранения.
        /// </summary>
        /// <param name="stateBase">Состояние сохранения.</param>
        /// <param name="previewWidth">Ширина превью.</param>
        /// <param name="previewHeight">Высота превью.</param>
        /// <returns>Объект <see cref="Bitmap"/> с превью фрактала.</returns>
        public override Bitmap RenderPreview(FractalSaveStateBase stateBase, int previewWidth, int previewHeight)
        {
            var tile = new TileInfo(0, 0, previewWidth, previewHeight);
            byte[] buffer = RenderPreviewTileAsync(stateBase, tile, previewWidth, previewHeight, previewWidth).Result;

            var bmp = new Bitmap(previewWidth, previewHeight, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            var bmpData = bmp.LockBits(new Rectangle(0, 0, previewWidth, previewHeight), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);

            IntPtr ptr = bmpData.Scan0;
            byte[] rgbValues = new byte[bmpData.Stride * previewHeight];

            // Конвертация из 32-битного буфера (BGRA) в 24-битный (BGR) для Bitmap
            for (int i = 0; i < previewWidth * previewHeight; i++)
            {
                int srcIdx = i * 4;
                int destIdx = i * 3;
                rgbValues[destIdx] = buffer[srcIdx];         // Blue
                rgbValues[destIdx + 1] = buffer[srcIdx + 1]; // Green
                rgbValues[destIdx + 2] = buffer[srcIdx + 2]; // Red
            }

            System.Runtime.InteropServices.Marshal.Copy(rgbValues, 0, ptr, rgbValues.Length);
            bmp.UnlockBits(bmpData);

            return bmp;
        }

        /// <summary>
        /// Получает состояние для рендеринга в высоком разрешении, дополняя его специфичными параметрами.
        /// </summary>
        /// <returns>Объект <see cref="HighResRenderState"/> с параметрами рендеринга.</returns>
        public override HighResRenderState GetRenderState()
        {
            var state = base.GetRenderState();
            state.Power = nudPower.Value;
            return state;
        }

        #endregion
    }
}