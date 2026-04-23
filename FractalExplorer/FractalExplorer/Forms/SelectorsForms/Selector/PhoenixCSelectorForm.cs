using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FractalExplorer.Engines;
using FractalExplorer.Utilities;

using FractalExplorer.Utilities.Theme;
namespace FractalExplorer.SelectorsForms
{
    /// <summary>
    /// Представляет форму для выбора параметров C1 (P и Q) для фрактала Феникса.
    /// Форма отображает срезы пространства параметров, позволяя пользователю
    /// визуально выбирать значения P и Q, а также управляет рендерингом этих срезов.
    /// </summary>
    public partial class PhoenixCSelectorForm : Form
    {
        #region Fields
        private readonly FractalExplorer.Forms.FractalPhoenixForm _ownerForm;

        private Bitmap _slicePBitmap;
        private double _slicePMinRe = -2.0;
        private double _slicePMaxRe = 2.0;
        private double _slicePMinIm = -2.0;
        private double _slicePMaxIm = 2.0;
        private double _renderedSlicePMinRe, _renderedSlicePMaxRe, _renderedSlicePMinIm, _renderedSlicePMaxIm;
        private Point _panStartSliceP;
        private bool _panningSliceP = false;
        private CancellationTokenSource _ctsSliceP;
        private volatile bool _isRenderingSliceP = false;
        private System.Windows.Forms.Timer _renderDebounceTimerSliceP;

        private Bitmap _sliceQBitmap;
        private double _sliceQMinRe = -2.0;
        private double _sliceQMaxRe = 2.0;
        private double _sliceQMinIm = -2.0;
        private double _sliceQMaxIm = 2.0;
        private double _renderedSliceQMinRe, _renderedSliceQMaxRe, _renderedSliceQMinIm, _renderedSliceQMaxIm;
        private Point _panStartSliceQ;
        private bool _panningSliceQ = false;
        private CancellationTokenSource _ctsSliceQ;
        private volatile bool _isRenderingSliceQ = false;
        private System.Windows.Forms.Timer _renderDebounceTimerSliceQ;

        private ComplexDecimal _fixedC2;

        private const int SLICE_ITERATIONS = 275;
        private const int RENDER_DEBOUNCE_MILLISECONDS = 300;
        private readonly PhoenixEngine _sliceRenderEngine;
        private bool _isSlicePHovered;
        private bool _isSliceQHovered;
        private EventHandler? _themeChangedHandler;
        #endregion

        #region Events
        /// <summary>
        /// Происходит, когда пользователь выбирает новые параметры C1 и C2 (которые могут быть изменены только косвенно через C1).
        /// </summary>
        public event Action<ComplexDecimal, ComplexDecimal> ParametersSelected;
        #endregion

        #region Constructor
        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="PhoenixCSelectorForm"/>.
        /// </summary>
        /// <param name="owner">Родительская форма <see cref="FractalPhoenixForm"/>.</param>
        /// <param name="initialC1">Начальное значение параметра C1.</param>
        /// <param name="initialC2">Начальное значение параметра C2 (фиксированное для этой формы).</param>
        public PhoenixCSelectorForm(FractalExplorer.Forms.FractalPhoenixForm owner, ComplexDecimal initialC1, ComplexDecimal initialC2)
        {
            InitializeComponent();
            ThemeManager.RegisterForm(this);
            _ownerForm = owner;

            // Устанавливаем начальные значения для элементов управления из переданного C1.
            // nudPReal соответствует реальной части C1, nudQImaginary — мнимой части C1.
            // nudPImaginary и nudQReal используются для визуализации z0.Im на срезах.
            nudPReal.Value = initialC1.Real;
            nudPImaginary.Value = 0; // Значение z0.Im по умолчанию для среза P
            nudQReal.Value = 0; // Значение z0.Im по умолчанию для среза Q
            nudQImaginary.Value = initialC1.Imaginary;

            _fixedC2 = initialC2; // C2 является фиксированным для этой формы

            _sliceRenderEngine = new PhoenixEngine
            {
                MaxIterations = SLICE_ITERATIONS,
                ThresholdSquared = 4.0m,
                C2 = _fixedC2,
                Palette = GetSliceRenderPalette(), // Устанавливаем палитру для срезов.
                MaxColorIterations = SLICE_ITERATIONS // Для монохромной палитры это используется как максимальное количество итераций для нормализации цвета.
            };

            SetupSliceCanvasEvents(sliceCanvasP, true);
            SetupSliceCanvasEvents(sliceCanvasQ, false);

            _themeChangedHandler = (_, _) => ApplyInteractiveCanvasStyles();
            ThemeManager.ThemeChanged += _themeChangedHandler;
            ApplyInteractiveCanvasStyles();

            _renderDebounceTimerSliceP = new System.Windows.Forms.Timer { Interval = RENDER_DEBOUNCE_MILLISECONDS };
            _renderDebounceTimerSliceP.Tick += RenderDebounceTimerSliceP_Tick;
            _renderDebounceTimerSliceQ = new System.Windows.Forms.Timer { Interval = RENDER_DEBOUNCE_MILLISECONDS };
            _renderDebounceTimerSliceQ.Tick += RenderDebounceTimerSliceQ_Tick;

            this.Load += SelectorForm_Load;

            nudPReal.ValueChanged += NudValues_Changed;
            nudPImaginary.ValueChanged += NudValues_Changed;
            nudQReal.ValueChanged += NudValues_Changed;
            nudQImaginary.ValueChanged += NudValues_Changed;

            ConfigureTooltips();
            ApplyBasicMode(isAdvanced: false);
            UpdateFixedValueLabels();
        }

        /// <summary>
        /// Обрабатывает событие загрузки формы.
        /// Инициализирует отображаемые диапазоны срезов и запускает таймеры для первого рендера.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void SelectorForm_Load(object sender, EventArgs e)
        {
            _renderedSlicePMinRe = _slicePMinRe;
            _renderedSlicePMaxRe = _slicePMaxRe;
            _renderedSlicePMinIm = _slicePMinIm;
            _renderedSlicePMaxIm = _slicePMaxIm;
            _renderedSliceQMinRe = _sliceQMinRe;
            _renderedSliceQMaxRe = _sliceQMaxRe;
            _renderedSliceQMinIm = _sliceQMinIm;
            _renderedSliceQMaxIm = _sliceQMaxIm;

            // Запускаем таймеры, чтобы инициировать начальный рендер срезов после полной загрузки формы.
            _renderDebounceTimerSliceP.Start();
            _renderDebounceTimerSliceQ.Start();
        }

        /// <summary>
        /// Настраивает обработчики событий для канваса среза (PictureBox).
        /// </summary>
        /// <param name="canvas">Контрол PictureBox, представляющий канвас среза.</param>
        /// <param name="isPSliceTarget">Истина, если это канвас для среза P; ложь для среза Q.</param>
        private void SetupSliceCanvasEvents(PictureBox canvas, bool isPSliceTarget)
        {
            canvas.Cursor = Cursors.Cross;
            canvas.Paint += (s, e) => SliceCanvas_Paint(s, e, isPSliceTarget);
            canvas.MouseClick += (s, e) => SliceCanvas_MouseClick(s, e, isPSliceTarget);
            canvas.MouseWheel += (s, e) => SliceCanvas_MouseWheel(s, e, isPSliceTarget);
            canvas.MouseDown += (s, e) => SliceCanvas_MouseDown(s, e, isPSliceTarget);
            canvas.MouseMove += (s, e) => SliceCanvas_MouseMove(s, e, isPSliceTarget);
            canvas.MouseUp += (s, e) => SliceCanvas_MouseUp(s, e, isPSliceTarget);
            canvas.MouseEnter += (s, e) => SliceCanvas_MouseEnter(s, e, isPSliceTarget);
            canvas.MouseLeave += (s, e) => SliceCanvas_MouseLeave(s, e, isPSliceTarget);
            canvas.Resize += (s, e) =>
            {
                // Запускаем рендер при изменении размера канваса,
                // чтобы изображение перерисовывалось под новые размеры.
                if (canvas.Width > 0 && canvas.Height > 0)
                {
                    var timer = isPSliceTarget ? _renderDebounceTimerSliceP : _renderDebounceTimerSliceQ;
                    timer.Stop();
                    timer.Start();
                }
            };
            var pb = isPSliceTarget ? progressBarSliceP : progressBarSliceQ;
            pb.Visible = false; // Скрываем прогресс-бар по умолчанию.
        }
        /// <summary>
        /// Настраивает всплывающие подсказки для обозначений P/Q и взаимодействия с канвасом.
        /// </summary>
        private void ConfigureTooltips()
        {
            toolTipSelector.SetToolTip(lblSliceP, "Ось P: по горизонтали — Re(C1), по вертикали — служебная ось визуализации Im(z0).");
            toolTipSelector.SetToolTip(lblSliceQ, "Ось Q: по горизонтали — служебная ось визуализации Re(z0), по вертикали — Im(C1).");
            toolTipSelector.SetToolTip(lblPImaginary, "Служебная координата для среза P (мнимая часть z0), не является отдельным параметром C2.");
            toolTipSelector.SetToolTip(lblQReal, "Служебная координата для среза Q (действительная часть z0), не является отдельным параметром C2.");
            toolTipSelector.SetToolTip(sliceCanvasP, "ЛКМ — выбрать точку C1 на срезе P. Колесо — масштаб, перетаскивание СКМ — панорамирование.");
            toolTipSelector.SetToolTip(sliceCanvasQ, "ЛКМ — выбрать точку C1 на срезе Q. Колесо — масштаб, перетаскивание СКМ — панорамирование.");
        }

        /// <summary>
        /// Переключает видимость элементов расширенного режима.
        /// </summary>
        private void ApplyBasicMode(bool isAdvanced)
        {
            nudPImaginary.Visible = isAdvanced;
            nudQReal.Visible = isAdvanced;
            lblPImaginary.Visible = isAdvanced;
            lblQReal.Visible = isAdvanced;
        }

        private void chkAdvancedMode_CheckedChanged(object sender, EventArgs e)
        {
            ApplyBasicMode(chkAdvancedMode.Checked);
        }

        #endregion

        #region UI Update and Value Handling
        /// <summary>
        /// Обрабатывает изменение значений в NumericUpDown контролах.
        /// Обновляет метки фиксированных значений и инициирует перерисовку срезов.
        /// </summary>
        /// <param name="sender">Источник события (NumericUpDown).</param>
        /// <param name="e">Данные события.</param>
        private void NudValues_Changed(object sender, EventArgs e)
        {
            UpdateFixedValueLabels();
            sliceCanvasP.Invalidate(); // Перерисовываем для обновления маркера.
            sliceCanvasQ.Invalidate(); // Перерисовываем для обновления маркера.

            // Запускаем рендер соответствующего среза, если изменение параметра влияет на него.
            // Например, изменение nudPReal влияет на срез Q, т.к. nudPReal является фиксированной координатой для среза Q.
            if (sender == nudPReal)
            {
                _renderDebounceTimerSliceQ.Stop();
                _renderDebounceTimerSliceQ.Start();
            }
            else if (sender == nudQImaginary)
            {
                _renderDebounceTimerSliceP.Stop();
                _renderDebounceTimerSliceP.Start();
            }
            // nudPImaginary влияет на маркер на срезе P, поэтому требует его перерисовки.
            else if (sender == nudPImaginary)
            {
                _renderDebounceTimerSliceP.Stop();
                _renderDebounceTimerSliceP.Start();
            }
            // nudQReal влияет на маркер на срезе Q, поэтому требует его перерисовки.
            else if (sender == nudQReal)
            {
                _renderDebounceTimerSliceQ.Stop();
                _renderDebounceTimerSliceQ.Start();
            }
        }

        /// <summary>
        /// Обновляет текстовые метки, отображающие фиксированные значения для срезов.
        /// </summary>
        private void UpdateFixedValueLabels()
        {
            lblFixedQForPSlice.Text = $"(Q фикс. = {nudQImaginary.Value:F4})";
            lblFixedPForQSlice.Text = $"(P фикс. = {nudPReal.Value:F4})";
        }

        /// <summary>
        /// Устанавливает выбранные параметры C1 из внешней формы и обновляет соответствующие NumericUpDown контролы.
        /// Инициирует перерисовку срезов, если значения изменились.
        /// </summary>
        /// <param name="c1FromOwner">Значение C1, полученное от родительской формы.</param>
        public void SetSelectedParameters(ComplexDecimal c1FromOwner)
        {
            bool triggerRenderP = false;
            bool triggerRenderQ = false;

            // Обновляем NumericUpDown и флаги для рендера, только если значения действительно изменились,
            // чтобы избежать лишних операций.
            if (nudPReal.Value != c1FromOwner.Real)
            {
                nudPReal.Value = c1FromOwner.Real;
                triggerRenderQ = true; // Изменение P (Real C1) требует перерисовки среза Q.
            }
            if (nudQImaginary.Value != c1FromOwner.Imaginary)
            {
                nudQImaginary.Value = c1FromOwner.Imaginary;
                triggerRenderP = true; // Изменение Q (Imaginary C1) требует перерисовки среза P.
            }

            UpdateFixedValueLabels();
            sliceCanvasP.Invalidate(); // Перерисовываем для обновления маркера.
            sliceCanvasQ.Invalidate(); // Перерисовываем для обновления маркера.

            // Запускаем рендер срезов, если их параметры изменились.
            if (triggerRenderP)
            {
                _renderDebounceTimerSliceP.Stop();
                _renderDebounceTimerSliceP.Start();
            }
            if (triggerRenderQ)
            {
                _renderDebounceTimerSliceQ.Stop();
                _renderDebounceTimerSliceQ.Start();
            }
        }
        #endregion

        #region Rendering Slices
        /// <summary>
        /// Создает функцию палитры для рендеринга срезов.
        /// Возвращает черно-серую палитру: черный для точек, входящих в множество,
        /// и оттенки серого в зависимости от количества итераций для точек, выходящих за порог.
        /// </summary>
        /// <returns>Функция, принимающая количество итераций, максимальное количество итераций и максимальное количество итераций для цвета,
        /// и возвращающая соответствующий цвет.</returns>
        private Func<int, int, int, Color> GetSliceRenderPalette()
        {
            return (iter, maxIter, maxClrIter) =>
            {
                if (iter == maxIter)
                {
                    return Color.Black; // Точки, входящие в множество (не достигли порога)
                }
                if (maxClrIter <= 0)
                {
                    return Color.Gray; // Запасной вариант, если макс. итераций для цвета некорректен
                }

                // Логарифмическое масштабирование для более плавного перехода цветов в зависимости от итераций.
                double tLog = Math.Log(Math.Min(iter, maxClrIter) + 1) / Math.Log(maxClrIter + 1);
                int cValRaw = (int)(255.0 * (1 - tLog)); // Инвертируем, чтобы меньшие итерации были светлее.
                int cVal = ClampColorComponent(cValRaw);
                return Color.FromArgb(cVal, cVal, cVal); // Оттенки серого.
            };
        }

        /// <summary>
        /// Асинхронно рендерит срез P (изменение P_real и z0.Im) фрактала Феникса.
        /// </summary>
        /// <returns>Задача, представляющая асинхронную операцию рендеринга.</returns>
        private async Task RenderSlicePAsync()
        {
            // Проверяем, не идет ли уже рендер, и корректны ли размеры канваса.
            if (_isRenderingSliceP || sliceCanvasP.Width <= 0 || sliceCanvasP.Height <= 0)
            {
                return;
            }
            _isRenderingSliceP = true;
            _ctsSliceP?.Cancel(); // Отменяем предыдущий рендер, если он еще активен.
            _ctsSliceP = new CancellationTokenSource();
            var token = _ctsSliceP.Token;

            var pb = progressBarSliceP;
            // Обновляем прогресс-бар, используя Invoke, чтобы обеспечить безопасность потоков.
            if (pb.IsHandleCreated && !pb.IsDisposed)
            {
                pb.Invoke((Action)(() => { pb.Value = 0; pb.Visible = true; }));
            }

            int w = sliceCanvasP.Width;
            int h = sliceCanvasP.Height;
            double minR_axis = _slicePMinRe;
            double maxR_axis = _slicePMaxRe;
            double minI_axis = _slicePMinIm;
            double maxI_axis = _slicePMaxIm;

            decimal fixedQ_scalar = nudQImaginary.Value; // Q-координата фиксирована для среза P.

            // Устанавливаем фиксированный параметр C2 для движка один раз перед циклом
            _sliceRenderEngine.C2 = _fixedC2;

            Bitmap newBitmap = null;
            try
            {
                newBitmap = await Task.Run(() =>
                {
                    Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                    BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, bmp.PixelFormat);
                    byte[] buffer = new byte[Math.Abs(bmpData.Stride) * h];
                    long renderedLines = 0;

                    // Используем Parallel.For для эффективного рендеринга среза,
                    // распределяя работу по нескольким ядрам ЦП.
                    Parallel.For(0, h, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Environment.ProcessorCount }, y_pixel =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }
                        for (int x_pixel = 0; x_pixel < w; x_pixel++)
                        {
                            // Преобразуем пиксельные координаты в координаты фрактала.
                            decimal p_scalar_for_engine = (decimal)(minR_axis + x_pixel * (maxR_axis - minR_axis) / w);
                            decimal z0_im_for_slice_visual = (decimal)(maxI_axis - y_pixel * (maxI_axis - minI_axis) / h); // Ось Y инвертирована.

                            // Создаем параметры для этого конкретного пикселя
                            ComplexDecimal c1_engine_param = new ComplexDecimal(p_scalar_for_engine, fixedQ_scalar);
                            ComplexDecimal z_current = new ComplexDecimal(0, z0_im_for_slice_visual);

                            // Вызываем потокобезопасную перегрузку метода, передавая C1 как аргумент
                            int iter = _sliceRenderEngine.CalculateIterations(ref z_current, ComplexDecimal.Zero, c1_engine_param);

                            Color c = _sliceRenderEngine.Palette(iter, SLICE_ITERATIONS, SLICE_ITERATIONS); // Используем палитру из движка.
                            int idx = y_pixel * bmpData.Stride + x_pixel * 3;
                            buffer[idx] = c.B;
                            buffer[idx + 1] = c.G;
                            buffer[idx + 2] = c.R;
                        }
                        long currentProgress = Interlocked.Increment(ref renderedLines);
                        // Обновляем прогресс-бар, используя Invoke для безопасности потоков.
                        if (pb.IsHandleCreated && !pb.IsDisposed)
                        {
                            pb.Invoke((Action)(() => pb.Value = (int)(100.0 * currentProgress / h)));
                        }
                    });
                    token.ThrowIfCancellationRequested(); // Проверяем отмену после завершения рендера.
                    Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
                    bmp.UnlockBits(bmpData);
                    return bmp;
                }, token);

                // Обновляем битмап на UI потоке.
                if (this.IsHandleCreated && !this.IsDisposed && sliceCanvasP.IsHandleCreated && !sliceCanvasP.IsDisposed)
                {
                    sliceCanvasP.Invoke((Action)(() =>
                    {
                        _slicePBitmap?.Dispose(); // Освобождаем старый битмап.
                        _slicePBitmap = newBitmap;
                        // Сохраняем параметры, по которым был отрендерен битмап.
                        _renderedSlicePMinRe = minR_axis;
                        _renderedSlicePMaxRe = maxR_axis;
                        _renderedSlicePMinIm = minI_axis;
                        _renderedSlicePMaxIm = maxI_axis;
                        sliceCanvasP.Invalidate(); // Запрашиваем перерисовку канваса.
                    }));
                }
                else
                {
                    newBitmap?.Dispose(); // Если форма уже закрыта, освобождаем ресурсы.
                }
            }
            catch (OperationCanceledException)
            {
                newBitmap?.Dispose(); // Освобождаем ресурсы, если рендер был отменен.
            }
            catch (Exception ex)
            {
                newBitmap?.Dispose(); // Освобождаем ресурсы при ошибке.
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    MessageBox.Show($"Ошибка рендера среза P: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                _isRenderingSliceP = false;
                // Скрываем прогресс-бар и сбрасываем его значение.
                if (pb.IsHandleCreated && !pb.IsDisposed)
                {
                    pb.Invoke((Action)(() => { pb.Visible = false; pb.Value = 0; }));
                }
            }
        }

        /// <summary>
        /// Асинхронно рендерит срез Q (изменение Q_imaginary и z0.Im) фрактала Феникса.
        /// </summary>
        /// <returns>Задача, представляющая асинхронную операцию рендеринга.</returns>
        private async Task RenderSliceQAsync()
        {
            if (_isRenderingSliceQ || sliceCanvasQ.Width <= 0 || sliceCanvasQ.Height <= 0)
            {
                return;
            }
            _isRenderingSliceQ = true;
            _ctsSliceQ?.Cancel();
            _ctsSliceQ = new CancellationTokenSource();
            var token = _ctsSliceQ.Token;

            var pb = progressBarSliceQ;
            if (pb.IsHandleCreated && !pb.IsDisposed)
            {
                pb.Invoke((Action)(() => { pb.Value = 0; pb.Visible = true; }));
            }

            int w = sliceCanvasQ.Width;
            int h = sliceCanvasQ.Height;
            double minR_axis = _sliceQMinRe;
            double maxR_axis = _sliceQMaxRe;
            double minI_axis = _sliceQMinIm;
            double maxI_axis = _sliceQMaxIm;

            decimal fixedP_scalar = nudPReal.Value; // P-координата фиксирована для среза Q.

            // Устанавливаем фиксированный параметр C2 для движка один раз перед циклом
            _sliceRenderEngine.C2 = _fixedC2;

            Bitmap newBitmap = null;
            try
            {
                newBitmap = await Task.Run(() =>
                {
                    Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                    BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, bmp.PixelFormat);
                    byte[] buffer = new byte[Math.Abs(bmpData.Stride) * h];
                    long renderedLines = 0;

                    Parallel.For(0, h, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Environment.ProcessorCount }, y_pixel =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }
                        for (int x_pixel = 0; x_pixel < w; x_pixel++)
                        {
                            // Преобразуем пиксельные координаты в координаты фрактала.
                            decimal q_scalar_for_engine = (decimal)(minR_axis + x_pixel * (maxR_axis - minR_axis) / w);
                            decimal z0_im_for_slice_visual = (decimal)(maxI_axis - y_pixel * (maxI_axis - minI_axis) / h); // Ось Y инвертирована.

                            // Создаем параметры для этого конкретного пикселя
                            ComplexDecimal c1_engine_param = new ComplexDecimal(fixedP_scalar, q_scalar_for_engine);
                            ComplexDecimal z_current = new ComplexDecimal(0, z0_im_for_slice_visual);

                            // Вызываем потокобезопасную перегрузку метода, передавая C1 как аргумент
                            int iter = _sliceRenderEngine.CalculateIterations(ref z_current, ComplexDecimal.Zero, c1_engine_param);

                            Color c = _sliceRenderEngine.Palette(iter, SLICE_ITERATIONS, SLICE_ITERATIONS); // Используем палитру из движка.
                            int idx = y_pixel * bmpData.Stride + x_pixel * 3;
                            buffer[idx] = c.B;
                            buffer[idx + 1] = c.G;
                            buffer[idx + 2] = c.R;
                        }
                        long currentProgress = Interlocked.Increment(ref renderedLines);
                        // Обновляем прогресс-бар, используя Invoke для безопасности потоков.
                        if (pb.IsHandleCreated && !pb.IsDisposed)
                        {
                            pb.Invoke((Action)(() => pb.Value = (int)(100.0 * currentProgress / h)));
                        }
                    });
                    token.ThrowIfCancellationRequested();
                    Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
                    bmp.UnlockBits(bmpData);
                    return bmp;
                }, token);

                // Обновляем битмап на UI потоке.
                if (this.IsHandleCreated && !this.IsDisposed && sliceCanvasQ.IsHandleCreated && !sliceCanvasQ.IsDisposed)
                {
                    sliceCanvasQ.Invoke((Action)(() =>
                    {
                        _sliceQBitmap?.Dispose();
                        _sliceQBitmap = newBitmap;
                        // Сохраняем параметры, по которым был отрендерен битмап.
                        _renderedSliceQMinRe = minR_axis;
                        _renderedSliceQMaxRe = maxR_axis;
                        _renderedSliceQMinIm = minI_axis;
                        _renderedSliceQMaxIm = maxI_axis;
                        sliceCanvasQ.Invalidate(); // Запрашиваем перерисовку канваса.
                    }));
                }
                else
                {
                    newBitmap?.Dispose(); // Если форма уже закрыта, освобождаем ресурсы.
                }
            }
            catch (OperationCanceledException)
            {
                newBitmap?.Dispose(); // Освобождаем ресурсы, если рендер был отменен.
            }
            catch (Exception ex)
            {
                newBitmap?.Dispose(); // Освобождаем ресурсы при ошибке.
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    MessageBox.Show($"Ошибка рендера среза Q: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                _isRenderingSliceQ = false;
                // Скрываем прогресс-бар и сбрасываем его значение.
                if (pb.IsHandleCreated && !pb.IsDisposed)
                {
                    pb.Invoke((Action)(() => { pb.Visible = false; pb.Value = 0; }));
                }
            }
        }
        #endregion

        #region Canvas Interaction
        /// <summary>
        /// Обрабатывает событие рисования на канвасе среза.
        /// Отображает отрендеренное изображение среза, масштабируя его при необходимости, и рисует маркер текущих значений.
        /// </summary>
        /// <param name="sender">Источник события (PictureBox).</param>
        /// <param name="e">Данные события рисования.</param>
        /// <param name="isPSliceTarget">Истина, если это канвас для среза P; ложь для среза Q.</param>
        private void SliceCanvas_Paint(object sender, PaintEventArgs e, bool isPSliceTarget)
        {
            PictureBox canvas = sender as PictureBox;
            Bitmap bmpToDraw = isPSliceTarget ? _slicePBitmap : _sliceQBitmap;

            // Получаем параметры отрисованного фрагмента и текущего окна просмотра.
            double renderedMinRe = isPSliceTarget ? _renderedSlicePMinRe : _renderedSliceQMinRe;
            double renderedMaxRe = isPSliceTarget ? _renderedSlicePMaxRe : _renderedSliceQMaxRe;
            double renderedMinIm = isPSliceTarget ? _renderedSlicePMinIm : _renderedSliceQMinIm;
            double renderedMaxIm = isPSliceTarget ? _renderedSlicePMaxIm : _renderedSliceQMaxIm;

            double currentMinRe = isPSliceTarget ? _slicePMinRe : _sliceQMinRe;
            double currentMaxRe = isPSliceTarget ? _slicePMaxRe : _sliceQMaxRe;
            double currentMinIm = isPSliceTarget ? _slicePMinIm : _sliceQMinIm;
            double currentMaxIm = isPSliceTarget ? _slicePMaxIm : _sliceQMaxIm;

            e.Graphics.Clear(Color.DimGray); // Очищаем фон канваса.
            if (bmpToDraw == null || canvas.Width <= 0 || canvas.Height <= 0)
            {
                DrawMarker(e.Graphics, canvas, isPSliceTarget); // Всегда рисуем маркер, даже если нет изображения.
                return;
            }

            double renderedComplexWidth = renderedMaxRe - renderedMinRe;
            double renderedComplexHeight = renderedMaxIm - renderedMinIm;
            double currentComplexWidth = currentMaxRe - currentMinRe;
            double currentComplexHeight = currentMaxIm - currentMinIm;

            if (renderedComplexWidth <= 0 || renderedComplexHeight <= 0 || currentComplexWidth <= 0 || currentComplexHeight <= 0)
            {
                if (bmpToDraw != null)
                {
                    e.Graphics.DrawImageUnscaled(bmpToDraw, Point.Empty);
                }
                DrawMarker(e.Graphics, canvas, isPSliceTarget);
                return;
            }

            // Вычисляем смещение и масштаб для отрисовки кешированного битмапа.
            float offsetX = (float)((renderedMinRe - currentMinRe) / currentComplexWidth * canvas.Width);
            float offsetY = (float)((currentMaxIm - renderedMaxIm) / currentComplexHeight * canvas.Height); // Y инвертирована.
            float destWidthPixels = (float)(renderedComplexWidth / currentComplexWidth * canvas.Width);
            float destHeightPixels = (float)(renderedComplexHeight / currentComplexHeight * canvas.Height);

            if (destWidthPixels > 0 && destHeightPixels > 0)
            {
                // Используем NearestNeighbor для быстрого и четкого масштабирования срезов.
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                e.Graphics.DrawImage(bmpToDraw, new RectangleF(offsetX, offsetY, destWidthPixels, destHeightPixels));
            }
            else
            {
                if (bmpToDraw != null)
                {
                    e.Graphics.DrawImageUnscaled(bmpToDraw, Point.Empty);
                }
            }
            DrawMarker(e.Graphics, canvas, isPSliceTarget);
            DrawInteractiveBorder(e.Graphics, canvas, isPSliceTarget);
        }

        private void DrawInteractiveBorder(Graphics graphics, PictureBox canvas, bool isPSliceTarget)
        {
            bool isHovered = isPSliceTarget ? _isSlicePHovered : _isSliceQHovered;
            Color borderColor = ThemeManager.GetInteractiveStateColor(ThemeManager.CurrentDefinition, canvas.BackColor, isHovered);
            float borderWidth = isHovered ? 2f : 1f;

            using Pen borderPen = new Pen(borderColor, borderWidth);
            int inset = (int)Math.Floor(borderWidth / 2f);
            int width = Math.Max(1, canvas.Width - 1 - inset * 2);
            int height = Math.Max(1, canvas.Height - 1 - inset * 2);
            graphics.DrawRectangle(borderPen, inset, inset, width, height);
        }

        private void SliceCanvas_MouseEnter(object? sender, EventArgs e, bool isPSliceTarget)
        {
            if (isPSliceTarget)
            {
                _isSlicePHovered = true;
            }
            else
            {
                _isSliceQHovered = true;
            }

            (sender as PictureBox)?.Invalidate();
        }

        private void SliceCanvas_MouseLeave(object? sender, EventArgs e, bool isPSliceTarget)
        {
            if (isPSliceTarget)
            {
                _isSlicePHovered = false;
            }
            else
            {
                _isSliceQHovered = false;
            }

            (sender as PictureBox)?.Invalidate();
        }

        private void ApplyInteractiveCanvasStyles()
        {
            ThemeDefinition theme = ThemeManager.CurrentDefinition;

            lblSlicePHint.ForeColor = theme.SecondaryText;
            lblSlicePHint.BackColor = Color.Transparent;

            sliceCanvasP.BackColor = theme.ControlBackground;
            sliceCanvasQ.BackColor = theme.ControlBackground;

            sliceCanvasP.Invalidate();
            sliceCanvasQ.Invalidate();
        }

        /// <summary>
        /// Рисует маркер на канвасе среза, указывающий на текущие значения P и Q.
        /// </summary>
        /// <param name="g">Объект Graphics для рисования.</param>
        /// <param name="canvas">PictureBox, на котором рисуется маркер.</param>
        /// <param name="isPSliceTarget">Истина, если это канвас для среза P; ложь для среза Q.</param>
        private void DrawMarker(Graphics g, PictureBox canvas, bool isPSliceTarget)
        {
            decimal valX_axis; // Значение по оси X (P_Real для среза P, Q_Imag для среза Q)
            decimal valY_axis_z0_Im; // Значение по оси Y (Z0.Im для обоих срезов)

            double minX_map, maxX_map;
            double minY_map, maxY_map;

            // Определяем, какие значения NumericUpDown соответствуют осям текущего среза.
            if (isPSliceTarget)
            {
                valX_axis = nudPReal.Value;
                valY_axis_z0_Im = nudPImaginary.Value;
                minX_map = _slicePMinRe;
                maxX_map = _slicePMaxRe;
                minY_map = _slicePMinIm;
                maxY_map = _slicePMaxIm;
            }
            else
            {
                valX_axis = nudQImaginary.Value;
                valY_axis_z0_Im = nudQReal.Value; // Для среза Q, nudQReal используется для Z0.Im.
                minX_map = _sliceQMinRe;
                maxX_map = _sliceQMaxRe;
                minY_map = _sliceQMinIm;
                maxY_map = _sliceQMaxIm;
            }

            double xRange_map = maxX_map - minX_map;
            double yRange_map = maxY_map - minY_map;

            if (xRange_map > 0 && yRange_map > 0 && canvas.Width > 0 && canvas.Height > 0)
            {
                // Преобразуем значения фрактала в пиксельные координаты для отрисовки маркера.
                int markerX_pixel = (int)(((double)valX_axis - minX_map) / xRange_map * canvas.Width);
                int markerY_pixel = (int)((maxY_map - (double)valY_axis_z0_Im) / yRange_map * canvas.Height); // Инвертируем Y.

                int size = 7;
                using (Pen p = new Pen(Color.LimeGreen, 2))
                {
                    g.DrawLine(p, markerX_pixel - size, markerY_pixel, markerX_pixel + size, markerY_pixel);
                    g.DrawLine(p, markerX_pixel, markerY_pixel - size, markerX_pixel, markerY_pixel + size);
                }
            }
        }

        /// <summary>
        /// Обрабатывает событие клика мыши по канвасу среза.
        /// Устанавливает значения NumericUpDown контролов в соответствии с точкой клика.
        /// </summary>
        /// <param name="sender">Источник события (PictureBox).</param>
        /// <param name="e">Данные события мыши.</param>
        /// <param name="isPSliceTarget">Истина, если это канвас для среза P; ложь для среза Q.</param>
        private void SliceCanvas_MouseClick(object sender, MouseEventArgs e, bool isPSliceTarget)
        {
            PictureBox canvas = sender as PictureBox;
            if (e.Button != MouseButtons.Left || canvas.Width <= 0 || canvas.Height <= 0)
            {
                return;
            }

            // Получаем текущие диапазоны отображения для преобразования пиксельных координат в координаты фрактала.
            double currentMinRe_map = isPSliceTarget ? _slicePMinRe : _sliceQMinRe;
            double currentMaxRe_map = isPSliceTarget ? _slicePMaxRe : _sliceQMaxRe;
            double currentMinIm_map = isPSliceTarget ? _slicePMinIm : _sliceQMinIm;
            double currentMaxIm_map = isPSliceTarget ? _slicePMaxIm : _sliceQMaxIm;

            double xRange_map = currentMaxRe_map - currentMinRe_map;
            double yRange_map = currentMaxIm_map - currentMinIm_map;

            if (xRange_map <= 0 || yRange_map <= 0)
            {
                return;
            }

            // Вычисляем значения фрактала, соответствующие точке клика.
            decimal selectedValueOnXAxis = ClampDecimal((decimal)(currentMinRe_map + e.X / (double)canvas.Width * xRange_map),
                                                       isPSliceTarget ? nudPReal.Minimum : nudQImaginary.Minimum,
                                                       isPSliceTarget ? nudPReal.Maximum : nudQImaginary.Maximum);

            decimal selectedValueOnYAxis_for_z0Im = ClampDecimal((decimal)(currentMaxIm_map - e.Y / (double)canvas.Height * yRange_map), // Ось Y инвертирована.
                                                                isPSliceTarget ? nudPImaginary.Minimum : nudQReal.Minimum,
                                                                isPSliceTarget ? nudPImaginary.Maximum : nudQReal.Maximum);

            // Обновляем соответствующие NumericUpDown контролы.
            if (isPSliceTarget)
            {
                nudPReal.Value = selectedValueOnXAxis;
                nudPImaginary.Value = selectedValueOnYAxis_for_z0Im;
            }
            else
            {
                nudQImaginary.Value = selectedValueOnXAxis;
                nudQReal.Value = selectedValueOnYAxis_for_z0Im;
            }
        }

        /// <summary>
        /// Обрабатывает событие прокрутки колесика мыши по канвасу среза.
        /// Выполняет масштабирование (зум) среза относительно положения курсора.
        /// </summary>
        /// <param name="sender">Источник события (PictureBox).</param>
        /// <param name="e">Данные события мыши.</param>
        /// <param name="isPSliceTarget">Истина, если это канвас для среза P; ложь для среза Q.</param>
        private void SliceCanvas_MouseWheel(object sender, MouseEventArgs e, bool isPSliceTarget)
        {
            PictureBox canvas = sender as PictureBox;
            if (canvas.Width <= 0 || canvas.Height <= 0)
            {
                return;
            }

            double zoomFactor = e.Delta > 0 ? 1.25 : 1.0 / 1.25; // Определяем коэффициент зума.

            // Используем ref для прямого изменения полей, содержащих диапазоны срезов.
            ref double minRe_map = ref (isPSliceTarget ? ref _slicePMinRe : ref _sliceQMinRe);
            ref double maxRe_map = ref (isPSliceTarget ? ref _slicePMaxRe : ref _sliceQMaxRe);
            ref double minIm_map = ref (isPSliceTarget ? ref _slicePMinIm : ref _sliceQMinIm);
            ref double maxIm_map = ref (isPSliceTarget ? ref _slicePMaxIm : ref _sliceQMaxIm);

            double oldReRange_map = maxRe_map - minRe_map;
            double oldImRange_map = maxIm_map - minIm_map;
            if (oldReRange_map <= 0 || oldImRange_map <= 0)
            {
                return;
            }

            double mouseX_canvas = e.X;
            double mouseY_canvas = e.Y;

            // Вычисляем координату фрактала под курсором до масштабирования.
            double mouseRe_complex_map = minRe_map + (mouseX_canvas / canvas.Width) * oldReRange_map;
            double mouseIm_complex_map = maxIm_map - (mouseY_canvas / canvas.Height) * oldImRange_map; // Y инвертирована.

            double newReRange_map = oldReRange_map / zoomFactor;
            double newImRange_map = oldImRange_map / zoomFactor;

            const double MIN_ALLOWED_RANGE = 1e-12; // Минимальный допустимый диапазон, чтобы избежать деления на ноль и слишком сильного зума.
            const double MAX_ALLOWED_RANGE = 1e4; // Максимальный допустимый диапазон.

            if (newReRange_map < MIN_ALLOWED_RANGE || newImRange_map < MIN_ALLOWED_RANGE ||
                newReRange_map > MAX_ALLOWED_RANGE || newImRange_map > MAX_ALLOWED_RANGE)
            {
                return; // Предотвращаем слишком сильное или слишком слабое масштабирование.
            }

            // Пересчитываем новые границы диапазона, чтобы точка под курсором оставалась на месте.
            minRe_map = mouseRe_complex_map - (mouseX_canvas / canvas.Width) * newReRange_map;
            maxRe_map = minRe_map + newReRange_map;

            minIm_map = mouseIm_complex_map - (1.0 - (mouseY_canvas / canvas.Height)) * newImRange_map; // Y инвертирована.
            maxIm_map = minIm_map + newImRange_map;

            canvas.Invalidate(); // Запрашиваем перерисовку для визуального отклика.

            // Запускаем таймер для отложенного рендера, чтобы избежать частых перерисовок при прокрутке.
            var timer = isPSliceTarget ? _renderDebounceTimerSliceP : _renderDebounceTimerSliceQ;
            timer.Stop();
            timer.Start();
        }

        /// <summary>
        /// Обрабатывает событие нажатия кнопки мыши на канвасе среза.
        /// Инициирует режим панорамирования, если нажата левая кнопка мыши.
        /// </summary>
        /// <param name="sender">Источник события (PictureBox).</param>
        /// <param name="e">Данные события мыши.</param>
        /// <param name="isPSliceTarget">Истина, если это канвас для среза P; ложь для среза Q.</param>
        private void SliceCanvas_MouseDown(object sender, MouseEventArgs e, bool isPSliceTarget)
        {
            if (e.Button == MouseButtons.Middle)
            {
                if (isPSliceTarget)
                {
                    _panningSliceP = true;
                    _panStartSliceP = e.Location;
                }
                else
                {
                    _panningSliceQ = true;
                    _panStartSliceQ = e.Location;
                }
                if (sender is PictureBox canvas)
                {
                    canvas.Cursor = Cursors.Hand; // Меняем курсор на руку, чтобы показать режим панорамирования.
                }
            }
        }

        /// <summary>
        /// Обрабатывает событие перемещения мыши по канвасу среза.
        /// Выполняет панорамирование (перемещение) среза, если активен режим панорамирования.
        /// </summary>
        /// <param name="sender">Источник события (PictureBox).</param>
        /// <param name="e">Данные события мыши.</param>
        /// <param name="isPSliceTarget">Истина, если это канвас для среза P; ложь для среза Q.</param>
        private void SliceCanvas_MouseMove(object sender, MouseEventArgs e, bool isPSliceTarget)
        {
            PictureBox canvas = sender as PictureBox;
            bool isPanning = isPSliceTarget ? _panningSliceP : _panningSliceQ;
            if (!isPanning || canvas.Width <= 0 || canvas.Height <= 0)
            {
                return;
            }

            // Получаем ссылки на поля, чтобы напрямую изменять диапазоны.
            ref Point panStart = ref (isPSliceTarget ? ref _panStartSliceP : ref _panStartSliceQ);
            ref double minRe_map = ref (isPSliceTarget ? ref _slicePMinRe : ref _sliceQMinRe);
            ref double maxRe_map = ref (isPSliceTarget ? ref _slicePMaxRe : ref _sliceQMaxRe);
            ref double minIm_map = ref (isPSliceTarget ? ref _slicePMinIm : ref _sliceQMinIm);
            ref double maxIm_map = ref (isPSliceTarget ? ref _slicePMaxIm : ref _sliceQMaxIm);

            double rRange_map = maxRe_map - minRe_map;
            double iRange_map = maxIm_map - minIm_map;
            if (rRange_map <= 0 || iRange_map <= 0)
            {
                return;
            }

            // Вычисляем смещение в пикселях и преобразуем его в координаты фрактала.
            double deltaXPixels = e.X - panStart.X;
            double deltaYPixels = e.Y - panStart.Y;

            double deltaRe_map = deltaXPixels * (rRange_map / canvas.Width);
            double deltaIm_map = deltaYPixels * (iRange_map / canvas.Height);

            // Обновляем диапазоны, чтобы сдвинуть видимую область.
            minRe_map -= deltaRe_map;
            maxRe_map -= deltaRe_map;
            minIm_map += deltaIm_map; // Ось Y инвертирована, поэтому добавляем для сдвига вверх.
            maxIm_map += deltaIm_map;

            panStart = e.Location; // Обновляем начальную точку для следующего перемещения.
            canvas.Invalidate(); // Запрашиваем перерисовку для визуального отклика.
        }

        /// <summary>
        /// Обрабатывает событие отпускания кнопки мыши на канвасе среза.
        /// Завершает режим панорамирования и запускает отложенный рендер, если было панорамирование.
        /// </summary>
        /// <param name="sender">Источник события (PictureBox).</param>
        /// <param name="e">Данные события мыши.</param>
        /// <param name="isPSliceTarget">Истина, если это канвас для среза P; ложь для среза Q.</param>
        private void SliceCanvas_MouseUp(object sender, MouseEventArgs e, bool isPSliceTarget)
        {
            if (e.Button != MouseButtons.Middle)
            {
                return;
            }

            bool wasPanning;
            var timerToStart = isPSliceTarget ? _renderDebounceTimerSliceP : _renderDebounceTimerSliceQ;

            if (isPSliceTarget)
            {
                wasPanning = _panningSliceP;
                _panningSliceP = false;
            }
            else
            {
                wasPanning = _panningSliceQ;
                _panningSliceQ = false;
            }

            if (!(sender is PictureBox canvas))
            {
                return;
            }

            canvas.Cursor = Cursors.Cross; // Возвращаем курсор выбора.

            if (wasPanning)
            {
                // Если было панорамирование, запускаем отложенный рендер для обновления изображения.
                timerToStart.Stop();
                timerToStart.Start();
            }
        }

        #endregion

        #region Timer Tick Handlers
        /// <summary>
        /// Обрабатывает событие срабатывания таймера задержки рендера для среза P.
        /// Инициирует асинхронный рендер среза P.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private async void RenderDebounceTimerSliceP_Tick(object sender, EventArgs e)
        {
            _renderDebounceTimerSliceP.Stop(); // Останавливаем таймер, чтобы предотвратить повторные вызовы.
            // Проверяем состояние формы, чтобы избежать ошибок при попытке рендеринга на закрытой форме.
            if (this.IsHandleCreated && !this.IsDisposed && !this.Disposing)
            {
                await RenderSlicePAsync();
            }
        }

        /// <summary>
        /// Обрабатывает событие срабатывания таймера задержки рендера для среза Q.
        /// Инициирует асинхронный рендер среза Q.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private async void RenderDebounceTimerSliceQ_Tick(object sender, EventArgs e)
        {
            _renderDebounceTimerSliceQ.Stop(); // Останавливаем таймер.
            // Проверяем состояние формы.
            if (this.IsHandleCreated && !this.IsDisposed && !this.Disposing)
            {
                await RenderSliceQAsync();
            }
        }
        #endregion

        #region Form Actions
        /// <summary>
        /// Обрабатывает нажатие кнопки "Применить".
        /// Вызывает событие <see cref="ParametersSelected"/> с текущими выбранными параметрами C1 и C2.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void btnApply_Click(object sender, EventArgs e)
        {
            ComplexDecimal c1Result = new ComplexDecimal(nudPReal.Value, nudQImaginary.Value);
            ParametersSelected?.Invoke(c1Result, _fixedC2); // Вызываем событие, передавая выбранные параметры.
        }

        /// <summary>
        /// Обрабатывает нажатие кнопки "Отмена".
        /// Закрывает текущую форму.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }
        #endregion

        #region Form Closing
        /// <summary>
        /// Вызывается при закрытии формы.
        /// Отменяет текущие операции рендеринга и освобождает связанные ресурсы,
        /// такие как токены отмены, таймеры и битмапы.
        /// </summary>
        /// <param name="e">Данные события, которые предоставляют информацию о том, как закрывается форма.</param>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _ctsSliceP?.Cancel();
            _ctsSliceP?.Dispose();
            _ctsSliceP = null;
            _ctsSliceQ?.Cancel();
            _ctsSliceQ?.Dispose();
            _ctsSliceQ = null;

            _renderDebounceTimerSliceP?.Stop();
            _renderDebounceTimerSliceP?.Dispose();
            _renderDebounceTimerSliceP = null;
            _renderDebounceTimerSliceQ?.Stop();
            _renderDebounceTimerSliceQ?.Dispose();
            _renderDebounceTimerSliceQ = null;

            _slicePBitmap?.Dispose();
            _slicePBitmap = null;
            _sliceQBitmap?.Dispose();
            _sliceQBitmap = null;

            if (_themeChangedHandler is not null)
            {
                ThemeManager.ThemeChanged -= _themeChangedHandler;
                _themeChangedHandler = null;
            }

            base.OnFormClosed(e);
        }
        #endregion

        /// <summary>
        /// Ограничивает значение компонента цвета в диапазоне от 0 до 255.
        /// </summary>
        /// <param name="component">Значение компонента цвета.</param>
        /// <returns>Ограниченное значение компонента цвета.</returns>
        private static int ClampColorComponent(int component)
        {
            if (component < 0)
            {
                return 0;
            }
            if (component > 255)
            {
                return 255;
            }
            return component;
        }

        /// <summary>
        /// Ограничивает десятичное значение в заданном диапазоне (min, max).
        /// </summary>
        /// <param name="value">Десятичное значение для ограничения.</param>
        /// <param name="min">Минимальное допустимое значение.</param>
        /// <param name="max">Максимальное допустимое значение.</param>
        /// <returns>Ограниченное десятичное значение.</returns>
        private decimal ClampDecimal(decimal value, decimal min, decimal max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }
    }
}
