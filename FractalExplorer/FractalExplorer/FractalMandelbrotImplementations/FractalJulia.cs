using FractalDraving;
using FractalExplorer.Engines;
using FractalExplorer.Forms.SelectorsForms.Selector;
using FractalExplorer.Resources;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Drawing.Imaging;
using System.Globalization;

namespace FractalExplorer.Projects
{
    /// <summary>
    /// Форма для отображения и взаимодействия с фракталом Жюлиа.
    /// Позволяет настраивать параметры фрактала и выбирать константу 'C'
    /// с помощью предпросмотра множества Мандельброта.
    /// </summary>
    public partial class FractalJulia : FractalMandelbrotFamilyForm
    {
        #region Fields

        /// <summary>
        /// Минимальное значение действительной части для рендеринга предпросмотра множества Мандельброта.
        /// </summary>
        private const decimal MANDELBROT_MIN_RE = -2.0m;

        /// <summary>
        /// Максимальное значение действительной части для рендеринга предпросмотра множества Мандельброта.
        /// </summary>
        private const decimal MANDELBROT_MAX_RE = 1.0m;

        /// <summary>
        /// Минимальное значение мнимой части для рендеринга предпросмотра множества Мандельброта.
        /// </summary>
        private const decimal MANDELBROT_MIN_IM = -1.2m;

        /// <summary>
        /// Максимальное значение мнимой части для рендеринга предпросмотра множества Мандельброта.
        /// </summary>
        private const decimal MANDELBROT_MAX_IM = 1.2m;

        /// <summary>
        /// Количество итераций для рендеринга предпросмотра множества Мандельброта.
        /// </summary>
        private const int MANDELBROT_PREVIEW_ITERATIONS = 75;

        /// <summary>
        /// Окно выбора константы 'C' на основе множества Мандельброта.
        /// </summary>
        private JuliaMandelbrotSelectorForm _mandelbrotCSelectorWindow;

        #endregion

        #region Constructor

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="FractalJulia"/>.
        /// </summary>
        public FractalJulia()
        {
            Text = "Фрактал Жюлиа";
        }

        #endregion


        /// <summary>
        /// Применяет значения константы C для множества Жюлиа и обновляет интерфейс.
        /// </summary>
        public void ApplyJuliaConstant(decimal real, decimal imaginary)
        {
            nudRe.Value = Math.Max(nudRe.Minimum, Math.Min(nudRe.Maximum, real));
            nudIm.Value = Math.Max(nudIm.Minimum, Math.Min(nudIm.Maximum, imaginary));
        }

        #region Fractal Engine Overrides

        /// <summary>
        /// Создает и возвращает экземпляр движка для рендеринга фрактала Жюлиа.
        /// </summary>
        /// <returns>Экземпляр <see cref="JuliaEngine"/>.</returns>
        protected override FractalMandelbrotFamilyEngine CreateEngine()
        {
            return new JuliaEngine();
        }

        /// <summary>
        /// Получает базовый масштаб для фрактала Жюлиа.
        /// </summary>
        protected override decimal BaseScale => 4.0m;

        /// <summary>
        /// Получает начальную координату X центра для фрактала Жюлиа.
        /// </summary>
        protected override decimal InitialCenterX => 0.0m;

        /// <summary>
        /// Получает начальную координату Y центра для фрактала Жюлиа.
        /// </summary>
        protected override decimal InitialCenterY => 0.0m;

        /// <summary>
        /// Вызывается после завершения инициализации формы.
        /// Отображает специфичные для Жюлиа элементы управления и запускает рендеринг предпросмотра Мандельброта.
        /// </summary>
        protected override void OnPostInitialize()
        {
            mandelbrotPreviewPanel.Visible = true;
            SetMandelbrotPreviewInteractive(true);
            lblRe.Visible = true;
            nudRe.Visible = true;
            lblIm.Visible = true;
            nudIm.Visible = true;

            var previewCanvas = Controls.Find("mandelbrotPreviewCanvas", true).FirstOrDefault() as PictureBox;
            if (previewCanvas != null)
            {
                previewCanvas.Click += mandelbrotCanvas_Click;
                previewCanvas.Paint += mandelbrotCanvas_Paint;
                // Запускаем рендеринг предпросмотра асинхронно, чтобы не блокировать UI.
                Task.Run(() => RenderAndDisplayMandelbrotSet());
            }
        }

        /// <summary>
        /// Обновляет специфичные для фрактала Жюлиа параметры движка.
        /// Устанавливает комплексную константу 'C' на основе значений из UI и вызывает перерисовку предпросмотра Мандельброта.
        /// </summary>
        protected override void UpdateEngineSpecificParameters()
        {
            _fractalEngine.C = new ComplexDecimal(nudRe.Value, nudIm.Value);

            var previewCanvas = Controls.Find("mandelbrotPreviewCanvas", true).FirstOrDefault();
            if (previewCanvas != null && previewCanvas.IsHandleCreated && !previewCanvas.IsDisposed)
            {
                // Запрашиваем перерисовку маркера на предпросмотре, так как значение 'C' изменилось.
                previewCanvas.Invalidate();
            }
        }

        /// <summary>
        /// Возвращает строку с деталями для имени файла сохранения,
        /// включая значения действительной и мнимой частей константы 'C'.
        /// </summary>
        /// <returns>Строка, содержащая информацию о фрактале для имени файла.</returns>
        protected override string GetSaveFileNameDetails()
        {
            // Форматируем значения Re и Im для включения в имя файла,
            // заменяя десятичные разделители на подчеркивания, чтобы избежать проблем с именами файлов.
            string reString = nudRe.Value.ToString("F15", CultureInfo.InvariantCulture).Replace(".", "_");
            string imString = nudIm.Value.ToString("F15", CultureInfo.InvariantCulture).Replace(".", "_");
            return $"julia_re{reString}_im{imString}";
        }

        #endregion

        #region Mandelbrot Preview / C-Parameter Selection

        /// <summary>
        /// Рендерит множество Мандельброта в фоновом потоке и отображает его на канвасе предпросмотра.
        /// </summary>
        private void RenderAndDisplayMandelbrotSet()
        {
            var previewCanvas = Controls.Find("mandelbrotPreviewCanvas", true).FirstOrDefault() as PictureBox;

            // КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ: Проверка на нулевые размеры перед рендерингом.
            if (previewCanvas == null || previewCanvas.Width <= 0 || previewCanvas.Height <= 0)
            {
                return; // Прерываем рендеринг, если холст не готов.
            }

            // Рендерим изображение Мандельброта.
            Bitmap mandelbrotImage = RenderMandelbrotSetInternal(previewCanvas.Width, previewCanvas.Height, MANDELBROT_PREVIEW_ITERATIONS);

            // Обновляем изображение на UI-потоке, используя Invoke, чтобы избежать ошибок кросс-поточной операции.
            if (previewCanvas.IsHandleCreated && !previewCanvas.IsDisposed)
            {
                previewCanvas.Invoke(() =>
                {
                    // Освобождаем старое изображение, чтобы избежать утечек памяти.
                    previewCanvas.Image?.Dispose();
                    previewCanvas.Image = mandelbrotImage;
                });
            }
            else
            {
                // Если канвас уже недействителен (например, форма закрывается), просто освобождаем битмап.
                mandelbrotImage?.Dispose();
            }
        }

        /// <summary>
        /// Внутренний метод для рендеринга множества Мандельброта в Bitmap.
        /// </summary>
        /// <param name="canvasWidth">Ширина канваса для рендеринга.</param>
        /// <param name="canvasHeight">Высота канваса для рендеринга.</param>
        /// <param name="iterationsLimit">Максимальное количество итераций.</param>
        /// <returns>Объект <see cref="Bitmap"/> с отрисованным множеством Мандельброта.</returns>
        private Bitmap RenderMandelbrotSetInternal(int canvasWidth, int canvasHeight, int iterationsLimit)
        {
            Bitmap bitmap = new Bitmap(canvasWidth, canvasHeight, PixelFormat.Format24bppRgb);

            // Создаем временный движок Мандельброта с фиксированными параметрами для предпросмотра.
            var engine = new MandelbrotEngine
            {
                MaxIterations = iterationsLimit,
                ThresholdSquared = 4m,
                Palette = GetPaletteMandelbrotClassicColor,
                Scale = MANDELBROT_MAX_RE - MANDELBROT_MIN_RE,
                CenterX = (MANDELBROT_MAX_RE + MANDELBROT_MIN_RE) / 2,
                CenterY = (MANDELBROT_MAX_IM + MANDELBROT_MIN_IM) / 2
            };

            // Используем существующий метод RenderToBitmap для простоты и надежности.
            // Он уже содержит всю логику параллельного рендеринга.
            return engine.RenderToBitmap(canvasWidth, canvasHeight, Environment.ProcessorCount, _ => { });
        }

        /// <summary>
        /// Обработчик события отрисовки канваса предпросмотра Мандельброта.
        /// Рисует маркер, указывающий текущие значения Re/Im константы 'C'.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события рисования.</param>
        private void mandelbrotCanvas_Paint(object sender, PaintEventArgs e)
        {
            var previewCanvas = sender as PictureBox;
            if (previewCanvas?.Image == null) return;

            decimal reRange = MANDELBROT_MAX_RE - MANDELBROT_MIN_RE;
            decimal imRange = MANDELBROT_MAX_IM - MANDELBROT_MIN_IM;

            if (reRange <= 0 || imRange <= 0) return; // Защита от деления на ноль

            decimal currentCReal = nudRe.Value;
            decimal currentCImaginary = nudIm.Value;

            if (currentCReal >= MANDELBROT_MIN_RE && currentCReal <= MANDELBROT_MAX_RE &&
                currentCImaginary >= MANDELBROT_MIN_IM && currentCImaginary <= MANDELBROT_MAX_IM)
            {
                int markerX = (int)((currentCReal - MANDELBROT_MIN_RE) / reRange * previewCanvas.Width);
                int markerY = (int)((MANDELBROT_MAX_IM - currentCImaginary) / imRange * previewCanvas.Height);

                using (Pen markerPen = new Pen(Color.FromArgb(200, Color.LimeGreen), 1.5f))
                {
                    e.Graphics.DrawLine(markerPen, 0, markerY, previewCanvas.Width, markerY);
                    e.Graphics.DrawLine(markerPen, markerX, 0, markerX, previewCanvas.Height);
                }
            }
        }

        /// <summary>
        /// Обработчик события клика по канвасу предпросмотра Мандельброта.
        /// Открывает окно выбора константы 'C' для интерактивного выбора.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void mandelbrotCanvas_Click(object sender, EventArgs e)
        {
            double initialReal = (double)nudRe.Value;
            double initialImaginary = (double)nudIm.Value;

            if (_mandelbrotCSelectorWindow == null || _mandelbrotCSelectorWindow.IsDisposed)
            {
                // Передаем границы допустимой области в конструктор
                _mandelbrotCSelectorWindow = new JuliaMandelbrotSelectorForm(this, initialReal, initialImaginary,
                    (double)MANDELBROT_MIN_RE, (double)MANDELBROT_MAX_RE,
                    (double)MANDELBROT_MIN_IM, (double)MANDELBROT_MAX_IM);

                _mandelbrotCSelectorWindow.CoordinatesSelected += (re, im) =>
                {
                    nudRe.Value = (decimal)re;
                    nudIm.Value = (decimal)im;
                };
                _mandelbrotCSelectorWindow.FormClosed += (s, args) => { _mandelbrotCSelectorWindow = null; };
                _mandelbrotCSelectorWindow.Show(this);
            }
            else
            {
                _mandelbrotCSelectorWindow.Activate();
                _mandelbrotCSelectorWindow.SetSelectedCoordinates(initialReal, initialImaginary, true);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Локальная функция палитры для рендеринга предпросмотра Мандельброта.
        /// </summary>
        /// <param name="iter">Количество итераций до выхода за порог.</param>
        /// <param name="maxIter">Максимально допустимое количество итераций.</param>
        /// <param name="maxColorIterations">Максимальное количество итераций для нормализации цвета.</param>
        /// <returns>Цвет пикселя.</returns>
        private Color GetPaletteMandelbrotClassicColor(int iter, int maxIter, int maxColorIterations)
        {
            if (iter == maxIter) return Color.Black;

            double tClassic = (double)iter / maxIter;
            byte r, g, b;

            if (tClassic < 0.5)
            {
                double t = tClassic * 2;
                r = (byte)(t * 200);
                g = (byte)(t * 50);
                b = (byte)(t * 30);
            }
            else
            {
                double t = (tClassic - 0.5) * 2;
                r = (byte)(200 + t * 55);
                g = (byte)(50 + t * 205);
                b = (byte)(30 + t * 225);
            }
            return Color.FromArgb(r, g, b);
        }

        #endregion

        #region ISaveLoadCapableFractal Overrides

        /// <summary>
        /// Получает строковый идентификатор типа фрактала.
        /// </summary>
        public override string FractalTypeIdentifier => "Julia";

        /// <summary>
        /// Получает конкретный тип состояния сохранения, используемый для фрактала Жюлиа.
        /// </summary>
        public override Type ConcreteSaveStateType => typeof(JuliaFamilySaveState);

        /// <summary>
        /// Загружает все сохраненные состояния фрактала для данного типа.
        /// </summary>
        /// <returns>Список базовых объектов состояний фрактала.</returns>
        public override List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            var specificSaves = SaveFileManager.LoadSaves<JuliaFamilySaveState>(this.FractalTypeIdentifier);
            return specificSaves.Cast<FractalSaveStateBase>().ToList();
        }

        /// <summary>
        /// Сохраняет предоставленный список состояний фрактала для данного типа.
        /// </summary>
        /// <param name="saves">Список базовых объектов состояний фрактала для сохранения.</param>
        public override void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            var specificSaves = saves.Cast<JuliaFamilySaveState>().ToList();
            SaveFileManager.SaveSaves(this.FractalTypeIdentifier, specificSaves);
        }
        #endregion
    }
}
