using FractalDraving;
using FractalExplorer.Engines;
using FractalExplorer.Forms.SelectorsForms.Selector;
using FractalExplorer.Resources;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Globalization;

namespace FractalExplorer.Projects
{
    /// <summary>
    /// Форма для отображения и взаимодействия с фракталом "Пылающий корабль" Жюлиа.
    /// Позволяет настраивать параметры фрактала и выбирать константу 'C'
    /// с помощью предпросмотра множества "Пылающий корабль" Мандельброта.
    /// </summary>
    public partial class FractalJuliaBurningShip : FractalMandelbrotFamilyForm
    {
        #region Fields

        /// <summary>
        /// Минимальное значение действительной части для рендеринга предпросмотра.
        /// </summary>
        private const decimal BURNING_SHIP_MIN_REAL = -2.0m;

        /// <summary>
        /// Максимальное значение действительной части для рендеринга предпросмотра.
        /// </summary>
        private const decimal BURNING_SHIP_MAX_REAL = 1.5m;

        /// <summary>
        /// Минимальное значение мнимой части для рендеринга предпросмотра.
        /// </summary>
        private const decimal BURNING_SHIP_MIN_IMAGINARY = -1.0m;

        /// <summary>
        /// Максимальное значение мнимой части для рендеринга предпросмотра.
        /// </summary>
        private const decimal BURNING_SHIP_MAX_IMAGINARY = 1.5m;

        /// <summary>
        /// Количество итераций для рендеринга предпросмотра.
        /// </summary>
        private const int BURNING_SHIP_PREVIEW_ITERATIONS = 75;

        /// <summary>
        /// Окно выбора константы 'C' на основе множества "Пылающий корабль".
        /// </summary>
        private BurningShipCSelectorForm _burningShipCSelectorWindow;
        private bool _isPostInitialized;
        private bool _hasPendingJuliaConstant;
        private decimal _pendingJuliaReal;
        private decimal _pendingJuliaImaginary;

        #endregion

        #region Constructor

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="FractalJuliaBurningShip"/>.
        /// </summary>
        public FractalJuliaBurningShip()
        {
            Text = "Фрактал Горящий Корабль (Жюлиа)";
        }

        #endregion

        /// <summary>
        /// Применяет значения константы C для множества Жюлиа Burning Ship и обновляет интерфейс.
        /// </summary>
        public void ApplyJuliaConstant(decimal real, decimal imaginary)
        {
            _pendingJuliaReal = real;
            _pendingJuliaImaginary = imaginary;
            _hasPendingJuliaConstant = true;

            if (_isPostInitialized)
            {
                ApplyPendingJuliaConstantIfNeeded();
            }
        }

        private void ApplyPendingJuliaConstantIfNeeded()
        {
            if (!_hasPendingJuliaConstant)
            {
                return;
            }

            decimal clampedReal = Math.Max(nudRe.Minimum, Math.Min(nudRe.Maximum, _pendingJuliaReal));
            decimal clampedImaginary = Math.Max(nudIm.Minimum, Math.Min(nudIm.Maximum, _pendingJuliaImaginary));

            nudRe.Value = clampedReal;
            nudIm.Value = clampedImaginary;
            _hasPendingJuliaConstant = false;
        }

        #region Fractal Engine Overrides

        /// <summary>
        /// Создает и возвращает экземпляр движка для "Пылающего корабля" Жюлиа.
        /// </summary>
        /// <returns>Экземпляр <see cref="JuliaBurningShipEngine"/>.</returns>
        protected override FractalMandelbrotFamilyEngine CreateEngine()
        {
            return new JuliaBurningShipEngine();
        }

        /// <summary>
        /// Получает базовый масштаб для фрактала.
        /// </summary>
        protected override decimal BaseScale => 4.0m;

        /// <summary>
        /// Получает начальную координату X центра.
        /// </summary>
        protected override decimal InitialCenterX => 0.0m;

        /// <summary>
        /// Получает начальную координату Y центра.
        /// </summary>
        protected override decimal InitialCenterY => 0.0m;

        /// <summary>
        /// Вызывается после инициализации. Настраивает UI для Жюлиа и запускает рендер предпросмотра.
        /// </summary>
        protected override void OnPostInitialize()
        {
            _isPostInitialized = true;
            mandelbrotPreviewPanel.Visible = true;
            SetMandelbrotPreviewInteractive(true);
            lblRe.Visible = true;
            nudRe.Visible = true;
            lblIm.Visible = true;
            nudIm.Visible = true;

            nudRe.Value = -1.7551867961883m;
            nudIm.Value = 0.01068m;
            ApplyPendingJuliaConstantIfNeeded();

            var previewCanvas = Controls.Find("mandelbrotPreviewCanvas", true).FirstOrDefault() as PictureBox;
            if (previewCanvas != null)
            {
                previewCanvas.Click += mandelbrotCanvas_Click;
                previewCanvas.Paint += mandelbrotCanvas_Paint;
                Task.Run(() => RenderAndDisplayBurningShipSet());
            }
        }

        /// <summary>
        /// Обновляет специфичные параметры движка (константу 'C').
        /// </summary>
        protected override void UpdateEngineSpecificParameters()
        {
            _fractalEngine.C = new ComplexDecimal(nudRe.Value, nudIm.Value);
            var previewCanvas = Controls.Find("mandelbrotPreviewCanvas", true).FirstOrDefault();
            if (previewCanvas != null && previewCanvas.IsHandleCreated && !previewCanvas.IsDisposed)
            {
                previewCanvas.Invalidate();
            }
        }

        /// <summary>
        /// Возвращает детали для имени файла сохранения, включая константу 'C'.
        /// </summary>
        /// <returns>Строка для имени файла.</returns>
        protected override string GetSaveFileNameDetails()
        {
            string reString = nudRe.Value.ToString("F15", CultureInfo.InvariantCulture).Replace(".", "_");
            string imString = nudIm.Value.ToString("F15", CultureInfo.InvariantCulture).Replace(".", "_");
            return $"burningship_julia_re{reString}_im{imString}";
        }

        #endregion

        #region Burning Ship Preview / C-Parameter Selection

        /// <summary>
        /// Рендерит множество "Пылающий корабль" и отображает его на панели предпросмотра.
        /// </summary>
        private void RenderAndDisplayBurningShipSet()
        {
            var previewCanvas = Controls.Find("mandelbrotPreviewCanvas", true).FirstOrDefault() as PictureBox;

            // КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ: Проверка на нулевые размеры перед рендерингом.
            if (previewCanvas == null || previewCanvas.Width <= 0 || previewCanvas.Height <= 0)
            {
                return; // Прерываем рендеринг, если холст не готов.
            }

            Bitmap burningShipImage = RenderBurningShipSetInternal(previewCanvas.Width, previewCanvas.Height, BURNING_SHIP_PREVIEW_ITERATIONS);

            if (previewCanvas.IsHandleCreated && !previewCanvas.IsDisposed)
            {
                previewCanvas.Invoke(() =>
                {
                    previewCanvas.Image?.Dispose();
                    previewCanvas.Image = burningShipImage;
                });
            }
            else
            {
                burningShipImage?.Dispose();
            }
        }

        /// <summary>
        /// Внутренний метод для рендеринга множества "Пылающий корабль" Мандельброта.
        /// </summary>
        /// <returns>Объект <see cref="Bitmap"/> с отрисованным множеством.</returns>
        private Bitmap RenderBurningShipSetInternal(int canvasWidth, int canvasHeight, int iterationsLimit)
        {
            // Создаем временный движок для рендеринга предпросмотра.
            var engine = new MandelbrotBurningShipEngine
            {
                MaxIterations = iterationsLimit,
                ThresholdSquared = 4m,
                Palette = GetPaletteMandelbrotClassicColor,
                Scale = BURNING_SHIP_MAX_REAL - BURNING_SHIP_MIN_REAL,
                CenterX = (BURNING_SHIP_MAX_REAL + BURNING_SHIP_MIN_REAL) / 2,
                CenterY = (BURNING_SHIP_MAX_IMAGINARY + BURNING_SHIP_MIN_IMAGINARY) / 2
            };

            return engine.RenderToBitmap(canvasWidth, canvasHeight, Environment.ProcessorCount, _ => { });
        }

        /// <summary>
        /// Отрисовывает маркер на предпросмотре.
        /// </summary>
        private void mandelbrotCanvas_Paint(object sender, PaintEventArgs e)
        {
            var previewCanvas = sender as PictureBox;
            if (previewCanvas?.Image == null) return;

            decimal realRange = BURNING_SHIP_MAX_REAL - BURNING_SHIP_MIN_REAL;
            decimal imaginaryRange = BURNING_SHIP_MAX_IMAGINARY - BURNING_SHIP_MIN_IMAGINARY;

            if (realRange <= 0 || imaginaryRange <= 0) return;

            decimal currentCReal = nudRe.Value;
            decimal currentCImaginary = nudIm.Value;

            if (currentCReal >= BURNING_SHIP_MIN_REAL && currentCReal <= BURNING_SHIP_MAX_REAL &&
                currentCImaginary >= BURNING_SHIP_MIN_IMAGINARY && currentCImaginary <= BURNING_SHIP_MAX_IMAGINARY)
            {
                int markerX = (int)((currentCReal - BURNING_SHIP_MIN_REAL) / realRange * previewCanvas.Width);
                int markerY = (int)((BURNING_SHIP_MAX_IMAGINARY - currentCImaginary) / imaginaryRange * previewCanvas.Height);

                using (Pen markerPen = new Pen(Color.FromArgb(200, Color.LimeGreen), 1.5f))
                {
                    e.Graphics.DrawLine(markerPen, 0, markerY, previewCanvas.Width, markerY);
                    e.Graphics.DrawLine(markerPen, markerX, 0, markerX, previewCanvas.Height);
                }
            }
        }

        /// <summary>
        /// Открывает окно выбора константы 'C'.
        /// </summary>
        private void mandelbrotCanvas_Click(object sender, EventArgs e)
        {
            double initialReal = (double)nudRe.Value;
            double initialImaginary = (double)nudIm.Value;

            if (_burningShipCSelectorWindow == null || _burningShipCSelectorWindow.IsDisposed)
            {
                // Передаем границы допустимой области в конструктор
                _burningShipCSelectorWindow = new BurningShipCSelectorForm(this, initialReal, initialImaginary,
                    (double)BURNING_SHIP_MIN_REAL, (double)BURNING_SHIP_MAX_REAL,
                    (double)BURNING_SHIP_MIN_IMAGINARY, (double)BURNING_SHIP_MAX_IMAGINARY);

                _burningShipCSelectorWindow.CoordinatesSelected += (re, im) =>
                {
                    nudRe.Value = (decimal)re;
                    nudIm.Value = (decimal)im;
                };
                _burningShipCSelectorWindow.FormClosed += (s, args) => { _burningShipCSelectorWindow = null; };
                _burningShipCSelectorWindow.Show(this);
            }
            else
            {
                _burningShipCSelectorWindow.Activate();
                _burningShipCSelectorWindow.SetSelectedCoordinates(initialReal, initialImaginary, true);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Палитра для рендеринга предпросмотра.
        /// </summary>
        private Color GetPaletteMandelbrotClassicColor(int iter, int maxIter, int maxColorIterations)
        {
            if (iter == maxIter) return Color.Black;

            double tClassic = (double)iter / maxIter;
            byte r, g, b;

            if (tClassic < 0.5)
            {
                double t = tClassic * 2;
                r = (byte)(t * 200); g = (byte)(t * 50); b = (byte)(t * 30);
            }
            else
            {
                double t = (tClassic - 0.5) * 2;
                r = (byte)(200 + t * 55); g = (byte)(50 + t * 205); b = (byte)(30 + t * 225);
            }
            return Color.FromArgb(r, g, b);
        }

        #endregion

        #region ISaveLoadCapableFractal Overrides

        /// <summary>
        /// Получает строковый идентификатор типа фрактала.
        /// </summary>
        public override string FractalTypeIdentifier => "JuliaBurningShip";

        /// <summary>
        /// Получает конкретный тип состояния сохранения.
        /// </summary>
        public override Type ConcreteSaveStateType => typeof(JuliaFamilySaveState);

        /// <summary>
        /// Загружает все сохраненные состояния для данного типа.
        /// </summary>
        /// <returns>Список состояний.</returns>
        public override List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            var specificSaves = SaveFileManager.LoadSaves<JuliaFamilySaveState>(this.FractalTypeIdentifier);
            return specificSaves.Cast<FractalSaveStateBase>().ToList();
        }

        /// <summary>
        /// Сохраняет список состояний для данного типа.
        /// </summary>
        /// <param name="saves">Список состояний для сохранения.</param>
        public override void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            var specificSaves = saves.Cast<JuliaFamilySaveState>().ToList();
            SaveFileManager.SaveSaves(this.FractalTypeIdentifier, specificSaves);
        }

        #endregion
    }
}
