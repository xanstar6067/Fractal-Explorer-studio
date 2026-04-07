using FractalExplorer.Engines;
using FractalExplorer.Resources;
using FractalExplorer.SelectorsForms;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Forms.Other;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

using FractalExplorer.Utilities.Theme;
using FractalExplorer.Utilities.UI;
namespace FractalExplorer
{
    /// <summary>
    /// Представляет основную форму для отображения и взаимодействия с фракталом Серпинского.
    /// Реализует интерфейс <see cref="ISaveLoadCapableFractal"/> для сохранения и загрузки состояний фрактала.
    /// </summary>
    public partial class FractalSerpinski : Form, ISaveLoadCapableFractal, IHighResRenderable
    {
        #region Fields
        /// <summary>
        /// Движок для рендеринга фрактала Серпинского.
        /// </summary>
        private readonly FractalSerpinskyEngine _engine;
        /// <summary>
        /// Bitmap для отображения на холсте.
        /// </summary>
        private Bitmap canvasBitmap;
        /// <summary>
        /// Флаг, указывающий, что в данный момент выполняется рендеринг предпросмотра.
        /// </summary>
        private volatile bool isRenderingPreview = false;
        /// <summary>
        /// Флаг, указывающий, что в данный момент выполняется рендеринг в высоком разрешении.
        /// </summary>
        private volatile bool isHighResRendering = false;
        /// <summary>
        /// Источник токена отмены для рендеринга предпросмотра.
        /// </summary>
        private CancellationTokenSource previewRenderCts;
        /// <summary>
        /// Источник токена отмены для рендеринга в высоком разрешении.
        /// </summary>
        private CancellationTokenSource highResRenderCts;
        /// <summary>
        /// Таймер для отложенного запуска рендеринга после взаимодействия с пользователем.
        /// </summary>
        private System.Windows.Forms.Timer renderTimer;
        /// <summary>
        /// Текущий уровень масштабирования.
        /// </summary>
        private double currentZoom = 1.0;
        /// <summary>
        /// Текущая координата X центра отображения.
        /// </summary>
        private double centerX = 0.0;
        /// <summary>
        /// Текущая координата Y центра отображения.
        /// </summary>
        private double centerY = 0.0;
        /// <summary>
        /// Уровень масштабирования на момент последнего рендеринга.
        /// </summary>
        private double renderedZoom = 1.0;
        /// <summary>
        /// Координата X центра на момент последнего рендеринга.
        /// </summary>
        private double renderedCenterX = 0.0;
        /// <summary>
        /// Координата Y центра на момент последнего рендеринга.
        /// </summary>
        private double renderedCenterY = 0.0;
        /// <summary>
        /// Начальная точка для панорамирования.
        /// </summary>
        private Point panStart;
        /// <summary>
        /// Флаг, указывающий, что выполняется панорамирование.
        /// </summary>
        private bool panning = false;
        /// <summary>
        /// Базовый заголовок окна.
        /// </summary>
        private string _baseTitle;
        /// <summary>
        /// Менеджер цветовых палитр для фрактала Серпинского.
        /// </summary>
        private SerpinskyPaletteManager _paletteManager;
        /// <summary>
        /// Форма конфигурации цветов.
        /// </summary>
        private ColorConfigurationSerpinskyForm _colorConfigForm;
        private const int ToggleButtonMargin = 12;
        private bool _suppressResizeRender = false;
        private readonly FullscreenToggleController _fullscreenController = new();
        private bool _controlsPanelVisible = true;
        private bool _pendingRenderRestart = false;
        #endregion

        #region Constructor
        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="FractalSerpinski"/>.
        /// Создает экземпляр движка фрактала и менеджера палитр.
        /// </summary>
        public FractalSerpinski()
        {
            InitializeComponent();
            KeyPreview = true;
            ThemeManager.RegisterForm(this);
            _engine = new FractalSerpinskyEngine();
            _paletteManager = new SerpinskyPaletteManager();
            InitializeCustomComponents();
        }
        #endregion

        #region UI Initialization
        /// <summary>
        /// Инициализирует пользовательские компоненты и подписывается на события UI.
        /// Настраивает таймер рендеринга, элементы управления потоками ЦП и обработчики событий мыши/UI.
        /// </summary>
        private void InitializeCustomComponents()
        {
            renderTimer = new System.Windows.Forms.Timer { Interval = 350 };
            renderTimer.Tick += RenderTimer_Tick;

            int cores = Environment.ProcessorCount;
            cbCPUThreads.Items.Clear();
            for (int i = 1; i <= cores; i++) cbCPUThreads.Items.Add(i);
            cbCPUThreads.Items.Add("Auto");
            cbCPUThreads.SelectedItem = "Auto";

            Load += (s, e) =>
            {
                _baseTitle = this.Text;
                renderedCenterX = centerX;
                renderedCenterY = centerY;
                renderedZoom = currentZoom;
                ApplyActivePalette();
                UpdateToggleControlsPosition();
                ClearInitialControlSelection();
                ScheduleRender();
            };
            canvasSerpinsky.Paint += CanvasSerpinsky_Paint;
            canvasSerpinsky.MouseWheel += CanvasSerpinsky_MouseWheel;
            canvasSerpinsky.MouseDown += CanvasSerpinsky_MouseDown;
            canvasSerpinsky.MouseMove += CanvasSerpinsky_MouseMove;
            canvasSerpinsky.MouseUp += CanvasSerpinsky_MouseUp;
            canvasHost.Resize += CanvasHost_Resize;
            controlsHost.SizeChanged += ControlsHost_SizeChanged;
            KeyDown += Form_KeyDown;
            FormClosing += Form_FormClosing;
            Resize += (s, e) => { if (WindowState != FormWindowState.Minimized && !_suppressResizeRender) ScheduleRender(); };
            canvasSerpinsky.Resize += CanvasSerpinsky_Resize;

            nudZoom.ValueChanged += ParamControl_Changed;
            nudIterations.ValueChanged += ParamControl_Changed;
            cbCPUThreads.SelectedIndexChanged += ParamControl_Changed;
            FractalTypeIsGeometry.CheckedChanged += FractalType_CheckedChanged;
            FractalTypeIsChaos.CheckedChanged += FractalType_CheckedChanged;

            FractalTypeIsGeometry.Checked = true;
            UpdateAbortButtonState();
        }

        private void CanvasSerpinsky_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized) return;
            if (_suppressResizeRender)
            {
                canvasSerpinsky.Invalidate();
                return;
            }

            ScheduleRender();
        }

        private void btnToggleControls_Click(object sender, EventArgs e)
        {
            ToggleControlsPanel();
        }

        private void CanvasHost_Resize(object sender, EventArgs e)
        {
            UpdateToggleControlsPosition();
        }

        private void ControlsHost_SizeChanged(object sender, EventArgs e)
        {
            UpdateToggleControlsPosition();
        }

        private void UpdateToggleControlsPosition()
        {
            int targetX = ToggleButtonMargin;
            if (_controlsPanelVisible)
            {
                targetX = controlsHost.Right + ToggleButtonMargin;
            }

            int maxX = Math.Max(ToggleButtonMargin, canvasHost.ClientSize.Width - btnToggleControls.Width - ToggleButtonMargin);
            btnToggleControls.Location = new Point(Math.Min(targetX, maxX), ToggleButtonMargin);
            btnToggleControls.BringToFront();
        }

        private void ToggleControlsPanel()
        {
            _controlsPanelVisible = !_controlsPanelVisible;
            btnToggleControls.Text = _controlsPanelVisible ? "✕" : "☰";
            _suppressResizeRender = true;
            try
            {
                controlsHost.Visible = _controlsPanelVisible;
                UpdateToggleControlsPosition();
                canvasSerpinsky.Invalidate();
            }
            finally
            {
                _suppressResizeRender = false;
            }
        }

        private void ClearInitialControlSelection()
        {
            BeginInvoke(new Action(() => ActiveControl = null));
        }

        private void ToggleFullscreenSafely()
        {
            _suppressResizeRender = true;
            try
            {
                _fullscreenController.Toggle(this);
                UpdateToggleControlsPosition();
                ScheduleRender();
            }
            finally
            {
                _suppressResizeRender = false;
            }
        }

        private void ExitFullscreenSafely()
        {
            if (!_fullscreenController.IsFullscreen(this))
            {
                return;
            }

            _suppressResizeRender = true;
            try
            {
                _fullscreenController.ExitFullscreen(this);
                UpdateToggleControlsPosition();
                ScheduleRender();
            }
            finally
            {
                _suppressResizeRender = false;
            }
        }

        private void Form_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F11)
            {
                ToggleFullscreenSafely();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape && _fullscreenController.IsFullscreen(this))
            {
                ExitFullscreenSafely();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            ExitFullscreenSafely();
        }

        #endregion

        #region UI Event Handlers

        /// <summary>
        /// Обрабатывает изменение типа фрактала (геометрический или хаос).
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void FractalType_CheckedChanged(object sender, EventArgs e)
        {
            CheckBox activeCheckBox = sender as CheckBox;
            if (activeCheckBox == null || !activeCheckBox.Checked) return;

            if (activeCheckBox == FractalTypeIsGeometry)
            {
                FractalTypeIsChaos.Checked = false;
                nudIterations.Maximum = 20;
                nudIterations.Minimum = 0;
                if (nudIterations.Value >= 20 || nudIterations.Value < 0) nudIterations.Value = 8;
            }
            else
            {
                FractalTypeIsGeometry.Checked = false;
                nudIterations.Maximum = int.MaxValue;
                nudIterations.Minimum = 1000;
                if (nudIterations.Value < 1000) nudIterations.Value = 50000;
            }
            ScheduleRender();
        }

        /// <summary>
        /// Открывает форму конфигурации цветов.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void color_configurations_Click(object sender, EventArgs e)
        {
            if (_colorConfigForm == null || _colorConfigForm.IsDisposed)
            {
                _colorConfigForm = new ColorConfigurationSerpinskyForm(_paletteManager);
                _colorConfigForm.PaletteApplied += OnPaletteApplied;
                _colorConfigForm.FormClosed += (s, args) => _colorConfigForm = null;
                _colorConfigForm.Show(this);
            }
            else if (!_colorConfigForm.Visible)
            {
                _colorConfigForm.Show(this);
            }
            else
            {
                _colorConfigForm.Activate();
            }
        }

        /// <summary>
        /// Срабатывает при применении новой палитры в форме конфигурации.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void OnPaletteApplied(object sender, EventArgs e)
        {
            ApplyActivePalette();
            ScheduleRender();
        }

        /// <summary>
        /// Запускает принудительный рендеринг немедленно.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void btnRender_Click(object sender, EventArgs e)
        {
            previewRenderCts?.Cancel();
            renderTimer.Stop();
            RenderTimer_Tick(sender, e);
        }

        /// <summary>
        /// Открывает диалог менеджера состояний для сохранения/загрузки.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void btnStateManager_Click(object sender, EventArgs e)
        {
            using (var dialog = new Forms.SaveLoadDialogForm(this))
            {
                dialog.ShowDialog(this);
            }
        }

        /// <summary>
        /// Отменяет текущий процесс рендеринга.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void abortRender_Click(object sender, EventArgs e)
        {
            if (isRenderingPreview) previewRenderCts?.Cancel();
            if (isHighResRendering) highResRenderCts?.Cancel();
        }

        /// <summary>
        /// Открывает менеджер сохранения изображений.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void btnOpenSaveManager_Click(object sender, EventArgs e)
        {
            if (isRenderingPreview) previewRenderCts?.Cancel();

            if (isHighResRendering)
            {
                MessageBox.Show("Процесс сохранения уже запущен.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var saveManager = new SaveImageManagerForm(this))
            {
                saveManager.ShowDialog(this);
            }
        }

        #endregion

        #region Rendering Logic

        /// <summary>
        /// Обрабатывает изменение параметров рендеринга и планирует новый рендеринг.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void ParamControl_Changed(object sender, EventArgs e)
        {
            if (isHighResRendering) return;
            if (sender == nudZoom) currentZoom = (double)nudZoom.Value;
            ScheduleRender();
        }

        /// <summary>
        /// Планирует отложенный рендеринг предпросмотра.
        /// </summary>
        private void ScheduleRender()
        {
            if (isHighResRendering || WindowState == FormWindowState.Minimized) return;
            _pendingRenderRestart = true;
            previewRenderCts?.Cancel();
            renderTimer.Stop();
            renderTimer.Start();
        }

        /// <summary>
        /// Выполняет асинхронный рендеринг предпросмотра фрактала на холст.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private async void RenderTimer_Tick(object sender, EventArgs e)
        {
            renderTimer.Stop();
            if (isHighResRendering || WindowState == FormWindowState.Minimized)
            {
                return;
            }

            if (isRenderingPreview)
            {
                _pendingRenderRestart = true;
                renderTimer.Start();
                return;
            }

            _pendingRenderRestart = false;

            var stopwatch = Stopwatch.StartNew();

            isRenderingPreview = true;
            SetMainControlsEnabled(false);
            UpdateAbortButtonState();

            previewRenderCts?.Dispose();
            previewRenderCts = new CancellationTokenSource();
            CancellationToken token = previewRenderCts.Token;

            UpdateEngineParameters();
            int numThreads = GetThreadCount();
            int renderWidth = canvasSerpinsky.Width;
            int renderHeight = canvasSerpinsky.Height;

            if (renderWidth <= 0 || renderHeight <= 0)
            {
                isRenderingPreview = false;
                SetMainControlsEnabled(true);
                UpdateAbortButtonState();
                return;
            }

            try
            {
                var bitmap = new Bitmap(renderWidth, renderHeight, PixelFormat.Format32bppArgb);
                var bitmapData = bitmap.LockBits(new Rectangle(0, 0, renderWidth, renderHeight), ImageLockMode.WriteOnly, bitmap.PixelFormat);
                var pixelBuffer = new byte[bitmapData.Stride * renderHeight];

                await Task.Run(() => _engine.RenderToBuffer(
                    pixelBuffer, renderWidth, renderHeight, bitmapData.Stride, 4,
                    numThreads, token, (progress) => UpdateProgressBar(progressBarSerpinsky, progress)), token);

                token.ThrowIfCancellationRequested();
                Marshal.Copy(pixelBuffer, 0, bitmapData.Scan0, pixelBuffer.Length);
                bitmap.UnlockBits(bitmapData);

                Bitmap oldImage = canvasBitmap;
                canvasBitmap = bitmap;
                renderedZoom = currentZoom;
                renderedCenterX = centerX;
                renderedCenterY = centerY;
                canvasSerpinsky.Invalidate();
                oldImage?.Dispose();

                stopwatch.Stop();
                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                this.Text = $"{_baseTitle} - Время последнего рендера: {elapsedSeconds:F3} сек.";
            }
            catch (OperationCanceledException)
            {
                // Игнорируем отмену операции
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка рендеринга: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                isRenderingPreview = false;
                if (!isHighResRendering) SetMainControlsEnabled(true);
                UpdateAbortButtonState();
                UpdateProgressBar(progressBarSerpinsky, 0);

                if (_pendingRenderRestart && !isHighResRendering && WindowState != FormWindowState.Minimized)
                {
                    _pendingRenderRestart = false;
                    renderTimer.Stop();
                    renderTimer.Start();
                }
            }
        }
        #endregion

        #region Canvas Interaction

        /// <summary>
        /// Отрисовывает холст, применяя трансформации для панорамирования и масштабирования.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события.</param>
        private void CanvasSerpinsky_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(_engine.BackgroundColor);
            if (canvasBitmap == null || canvasSerpinsky.Width <= 0 || canvasSerpinsky.Height <= 0) return;

            double renderedAspectRatio = (double)canvasBitmap.Width / canvasBitmap.Height;
            double renderedViewHeightWorld = 1.0 / renderedZoom;
            double renderedViewWidthWorld = renderedViewHeightWorld * renderedAspectRatio;
            double renderedMinReal = renderedCenterX - renderedViewWidthWorld / 2.0;
            double renderedMaxImaginary = renderedCenterY + renderedViewHeightWorld / 2.0;

            double currentAspectRatio = (double)canvasSerpinsky.Width / canvasSerpinsky.Height;
            double currentViewHeightWorld = 1.0 / currentZoom;
            double currentViewWidthWorld = currentViewHeightWorld * currentAspectRatio;
            double currentMinReal = centerX - currentViewWidthWorld / 2.0;
            double currentMaxImaginary = centerY + currentViewHeightWorld / 2.0;

            float offsetX = (float)(((renderedMinReal - currentMinReal) / currentViewWidthWorld) * canvasSerpinsky.Width);
            float offsetY = (float)(((currentMaxImaginary - renderedMaxImaginary) / currentViewHeightWorld) * canvasSerpinsky.Height);
            float scaleWidth = (float)((renderedViewWidthWorld / currentViewWidthWorld) * canvasSerpinsky.Width);
            float scaleHeight = (float)((renderedViewHeightWorld / currentViewHeightWorld) * canvasSerpinsky.Height);

            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            e.Graphics.DrawImage(canvasBitmap, new RectangleF(offsetX, offsetY, scaleWidth, scaleHeight));
        }

        /// <summary>
        /// Обрабатывает масштабирование с помощью колеса мыши.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события мыши.</param>
        private void CanvasSerpinsky_MouseWheel(object sender, MouseEventArgs e)
        {
            if (isHighResRendering || canvasSerpinsky.Width <= 0) return;

            double zoomFactor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
            PointF worldPosition = ScreenToWorld(e.Location, canvasSerpinsky.Width, canvasSerpinsky.Height, currentZoom, centerX, centerY);
            currentZoom = Math.Max((double)nudZoom.Minimum, Math.Min((double)nudZoom.Maximum, currentZoom * zoomFactor));
            PointF newWorldPosition = ScreenToWorld(e.Location, canvasSerpinsky.Width, canvasSerpinsky.Height, currentZoom, centerX, centerY);

            centerX += worldPosition.X - newWorldPosition.X;
            centerY += worldPosition.Y - newWorldPosition.Y;
            canvasSerpinsky.Invalidate();

            if (nudZoom.Value != (decimal)currentZoom) nudZoom.Value = (decimal)currentZoom;
            else ScheduleRender();
        }

        /// <summary>
        /// Начинает операцию панорамирования при нажатии левой кнопки мыши.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события мыши.</param>
        private void CanvasSerpinsky_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                panning = true;
                panStart = e.Location;
                canvasSerpinsky.Cursor = Cursors.SizeAll;
            }
        }

        /// <summary>
        /// Выполняет панорамирование при перемещении мыши с зажатой левой кнопкой.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события мыши.</param>
        private void CanvasSerpinsky_MouseMove(object sender, MouseEventArgs e)
        {
            if (!panning) return;

            PointF worldBefore = ScreenToWorld(panStart, canvasSerpinsky.Width, canvasSerpinsky.Height, currentZoom, centerX, centerY);
            PointF worldAfter = ScreenToWorld(e.Location, canvasSerpinsky.Width, canvasSerpinsky.Height, currentZoom, centerX, centerY);
            centerX += worldBefore.X - worldAfter.X;
            centerY += worldBefore.Y - worldAfter.Y;
            panStart = e.Location;
            canvasSerpinsky.Invalidate();
        }

        /// <summary>
        /// Завершает операцию панорамирования при отпускании левой кнопки мыши.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Данные события мыши.</param>
        private void CanvasSerpinsky_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                panning = false;
                canvasSerpinsky.Cursor = Cursors.Default;
                ScheduleRender();
            }
        }

        /// <summary>
        /// Преобразует экранные координаты в мировые координаты фрактала.
        /// </summary>
        /// <param name="screenPoint">Точка на экране.</param>
        /// <param name="screenWidth">Ширина экрана.</param>
        /// <param name="screenHeight">Высота экрана.</param>
        /// <param name="zoom">Текущий зум.</param>
        /// <param name="centerX">Координата X центра.</param>
        /// <param name="centerY">Координата Y центра.</param>
        /// <returns>Точка в мировых координатах.</returns>
        private PointF ScreenToWorld(Point screenPoint, int screenWidth, int screenHeight, double zoom, double centerX, double centerY)
        {
            double aspectRatio = (double)screenWidth / screenHeight;
            double viewHeightWorld = 1.0 / zoom;
            double viewWidthWorld = viewHeightWorld * aspectRatio;
            double minReal = centerX - viewWidthWorld / 2.0;
            double maxImaginary = centerY + viewHeightWorld / 2.0;
            float worldX = (float)(minReal + (screenPoint.X / (double)screenWidth) * viewWidthWorld);
            float worldY = (float)(maxImaginary - (screenPoint.Y / (double)screenHeight) * viewHeightWorld);
            return new PointF(worldX, worldY);
        }
        #endregion

        #region Utility Methods

        /// <summary>
        /// Обновляет параметры движка фрактала на основе текущих значений элементов управления.
        /// </summary>
        private void UpdateEngineParameters()
        {
            _engine.RenderMode = FractalTypeIsGeometry.Checked ? SerpinskyRenderMode.Geometric : SerpinskyRenderMode.Chaos;
            _engine.Iterations = (int)nudIterations.Value;
            _engine.Zoom = currentZoom;
            _engine.CenterX = centerX;
            _engine.CenterY = centerY;
            ApplyActivePalette();
        }

        /// <summary>
        /// Применяет активную цветовую палитру к движку рендеринга.
        /// </summary>
        private void ApplyActivePalette()
        {
            if (_engine == null || _paletteManager?.ActivePalette == null) return;

            var activePalette = _paletteManager.ActivePalette;
            _engine.ColorMode = SerpinskyColorMode.CustomColor;
            _engine.FractalColor = activePalette.FractalColor;
            _engine.BackgroundColor = activePalette.BackgroundColor;
        }

        /// <summary>
        /// Определяет количество потоков для рендеринга на основе выбора пользователя.
        /// </summary>
        /// <returns>Количество потоков.</returns>
        private int GetThreadCount()
        {
            return cbCPUThreads.SelectedItem?.ToString() == "Auto" ? Environment.ProcessorCount : Convert.ToInt32(cbCPUThreads.SelectedItem);
        }

        /// <summary>
        /// Включает или отключает основные элементы управления на панели.
        /// </summary>
        /// <param name="enabled">True, чтобы включить; false, чтобы отключить.</param>
        private void SetMainControlsEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                Invoke((Action)(() => SetMainControlsEnabled(enabled)));
                return;
            }

            // Перебираем все контролы ВЕРХНЕГО УРОВНЯ на главной панели
            foreach (Control ctrl in pnlControls.Controls)
            {
                // Мы НЕ трогаем контейнер, в котором лежат кнопки рендера,
                // так как его нужно обработать отдельно.
                if (ctrl != pnlRenderButtons)
                {
                    ctrl.Enabled = enabled;
                }
            }

            // Теперь обрабатываем кнопки рендера отдельно.
            // Кнопка "Запустить рендер" должна быть выключена во время рендеринга.
            btnRender.Enabled = enabled;

            // А состояние кнопки "Отмена" определяется тем, запущен ли рендер.
            // Вызов этой функции гарантирует, что она будет включена, когда нужно.
            UpdateAbortButtonState();
        }

        /// <summary>
        /// Обновляет состояние кнопки отмены рендеринга.
        /// </summary>
        private void UpdateAbortButtonState()
        {
            if (IsHandleCreated)
            {
                Invoke((Action)(() => abortRender.Enabled = isRenderingPreview || isHighResRendering));
            }
        }

        /// <summary>
        /// Потокобезопасно обновляет значение ProgressBar.
        /// </summary>
        /// <param name="progressBar">Прогресс-бар для обновления.</param>
        /// <param name="percentage">Процент выполнения (0-100).</param>
        private void UpdateProgressBar(ProgressBar progressBar, int percentage)
        {
            if (progressBar.IsHandleCreated)
            {
                progressBar.Invoke((Action)(() => progressBar.Value = Math.Min(100, Math.Max(0, percentage))));
            }
        }

        /// <summary>
        /// Освобождает ресурсы при закрытии формы.
        /// </summary>
        /// <param name="e">Данные события.</param>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            previewRenderCts?.Cancel();
            previewRenderCts?.Dispose();
            highResRenderCts?.Cancel();
            highResRenderCts?.Dispose();
            renderTimer?.Stop();
            renderTimer?.Dispose();
            canvasBitmap?.Dispose();
            base.OnFormClosed(e);
        }
        #endregion

        #region ISaveLoadCapableFractal Implementation

        /// <inheritdoc />
        public string FractalTypeIdentifier => "Serpinsky";

        /// <inheritdoc />
        public Type ConcreteSaveStateType => typeof(SerpinskySaveState);

        /// <summary>
        /// Содержит параметры для рендеринга предпросмотра состояния фрактала.
        /// </summary>
        public class SerpinskyPreviewParams
        {
            /// <summary>
            /// Режим рендеринга.
            /// </summary>
            public SerpinskyRenderMode RenderMode { get; set; }
            /// <summary>
            /// Количество итераций.
            /// </summary>
            public int Iterations { get; set; }
            /// <summary>
            /// Уровень масштабирования.
            /// </summary>
            public double Zoom { get; set; }
            /// <summary>
            /// Координата X центра.
            /// </summary>
            public double CenterX { get; set; }
            /// <summary>
            /// Координата Y центра.
            /// </summary>
            public double CenterY { get; set; }
            /// <summary>
            /// Цвет фрактала.
            /// </summary>
            public Color FractalColor { get; set; }
            /// <summary>
            /// Цвет фона.
            /// </summary>
            public Color BackgroundColor { get; set; }
        }

        /// <inheritdoc />
        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new SerpinskySaveState(this.FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                RenderMode = FractalTypeIsGeometry.Checked ? SerpinskyRenderMode.Geometric : SerpinskyRenderMode.Chaos,
                Iterations = (int)nudIterations.Value,
                Zoom = this.currentZoom,
                CenterX = this.centerX,
                CenterY = this.centerY,
                FractalColor = _engine.FractalColor,
                BackgroundColor = _engine.BackgroundColor
            };

            var previewParams = new SerpinskyPreviewParams
            {
                RenderMode = state.RenderMode,
                Zoom = state.Zoom,
                CenterX = state.CenterX,
                CenterY = state.CenterY,
                FractalColor = state.FractalColor,
                BackgroundColor = state.BackgroundColor,
                Iterations = (state.RenderMode == SerpinskyRenderMode.Geometric) ? Math.Min(state.Iterations, 5) : Math.Min(state.Iterations, 20000)
            };

            var jsonOptions = new JsonSerializerOptions();
            jsonOptions.Converters.Add(new Utilities.JsonConverters.JsonColorConverter());
            state.PreviewParametersJson = JsonSerializer.Serialize(previewParams, jsonOptions);

            return state;
        }

        /// <inheritdoc />
        public void LoadState(FractalSaveStateBase stateBase)
        {
            if (stateBase is SerpinskySaveState state)
            {
                previewRenderCts?.Cancel();
                renderTimer.Stop();

                var loadedPalette = new SerpinskyColorPalette
                {
                    Name = $"Загружено: {state.SaveName}",
                    FractalColor = state.FractalColor,
                    BackgroundColor = state.BackgroundColor,
                    IsBuiltIn = false
                };

                _paletteManager.ActivePalette = loadedPalette;

                if (state.RenderMode == SerpinskyRenderMode.Geometric) FractalTypeIsGeometry.Checked = true;
                else FractalTypeIsChaos.Checked = true;

                decimal safeIterations = Math.Max(nudIterations.Minimum, Math.Min(nudIterations.Maximum, state.Iterations));
                nudIterations.Value = safeIterations;

                this.centerX = state.CenterX;
                this.centerY = state.CenterY;
                this.currentZoom = state.Zoom;
                decimal safeZoom = Math.Max(nudZoom.Minimum, Math.Min(nudZoom.Maximum, (decimal)state.Zoom));
                nudZoom.Value = safeZoom;

                UpdateEngineParameters();
                ScheduleRender();
            }
            else
            {
                MessageBox.Show("Несовместимый тип состояния для загрузки.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Кэшированное изображение предпросмотра.
        /// </summary>
        private static Bitmap _cachedPreviewBitmap;
        /// <summary>
        /// Идентификатор состояния для кэшированного предпросмотра.
        /// </summary>
        private static string _cachedPreviewStateIdentifier;
        /// <summary>
        /// Объект блокировки для доступа к кэшу предпросмотра.
        /// </summary>
        private static readonly object _previewCacheLock = new object();

        /// <inheritdoc />
        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            string currentStateIdentifier = $"{state.SaveName}_{state.Timestamp}";

            lock (_previewCacheLock)
            {
                if (_cachedPreviewBitmap == null || _cachedPreviewStateIdentifier != currentStateIdentifier)
                {
                    _cachedPreviewBitmap?.Dispose();
                    _cachedPreviewBitmap = null;
                }
            }

            if (_cachedPreviewBitmap == null)
            {
                var newBitmap = await Task.Run(() => RenderPreview(state, totalWidth, totalHeight));
                lock (_previewCacheLock)
                {
                    if (_cachedPreviewBitmap == null)
                    {
                        _cachedPreviewBitmap = newBitmap;
                        _cachedPreviewStateIdentifier = currentStateIdentifier;
                    }
                    else
                    {
                        newBitmap?.Dispose();
                    }
                }
            }

            var tileBuffer = new byte[tile.Bounds.Width * tile.Bounds.Height * 4];
            using (var tileBmp = new Bitmap(tile.Bounds.Width, tile.Bounds.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(tileBmp))
                {
                    lock (_previewCacheLock)
                    {
                        if (_cachedPreviewBitmap != null)
                        {
                            g.DrawImage(_cachedPreviewBitmap, new Rectangle(0, 0, tile.Bounds.Width, tile.Bounds.Height), tile.Bounds, GraphicsUnit.Pixel);
                        }
                    }
                }

                var bmpData = tileBmp.LockBits(new Rectangle(0, 0, tileBmp.Width, tileBmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(bmpData.Scan0, tileBuffer, 0, tileBuffer.Length);
                tileBmp.UnlockBits(bmpData);
            }

            return tileBuffer;
        }

        /// <inheritdoc />
        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (string.IsNullOrEmpty(state.PreviewParametersJson))
            {
                var bmpError = new Bitmap(previewWidth, previewHeight);
                using (var g = Graphics.FromImage(bmpError)) { g.Clear(Color.DarkGray); TextRenderer.DrawText(g, "Нет данных", Font, new Rectangle(0, 0, previewWidth, previewHeight), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
                return bmpError;
            }

            SerpinskyPreviewParams previewParams;
            try
            {
                var jsonOptions = new JsonSerializerOptions();
                jsonOptions.Converters.Add(new Utilities.JsonConverters.JsonColorConverter());
                previewParams = JsonSerializer.Deserialize<SerpinskyPreviewParams>(state.PreviewParametersJson, jsonOptions);
            }
            catch (Exception)
            {
                var bmpError = new Bitmap(previewWidth, previewHeight);
                using (var g = Graphics.FromImage(bmpError)) { g.Clear(Color.DarkRed); TextRenderer.DrawText(g, "Ошибка параметров", Font, new Rectangle(0, 0, previewWidth, previewHeight), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
                return bmpError;
            }

            var previewEngine = new FractalSerpinskyEngine
            {
                RenderMode = previewParams.RenderMode,
                Iterations = previewParams.Iterations,
                Zoom = previewParams.Zoom,
                CenterX = previewParams.CenterX,
                CenterY = previewParams.CenterY,
                FractalColor = previewParams.FractalColor,
                BackgroundColor = previewParams.BackgroundColor,
                ColorMode = SerpinskyColorMode.CustomColor
            };

            Bitmap bmp = new Bitmap(previewWidth, previewHeight, PixelFormat.Format32bppArgb);
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, previewWidth, previewHeight), ImageLockMode.WriteOnly, bmp.PixelFormat);
            byte[] buffer = new byte[bmpData.Stride * previewHeight];

            previewEngine.RenderToBuffer(buffer, previewWidth, previewHeight, bmpData.Stride, 4, 1, CancellationToken.None, progress => { });

            Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
            bmp.UnlockBits(bmpData);

            return bmp;
        }

        /// <inheritdoc />
        public List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            var specificSaves = SaveFileManager.LoadSaves<SerpinskySaveState>(this.FractalTypeIdentifier);
            return specificSaves.Cast<FractalSaveStateBase>().ToList();
        }

        /// <inheritdoc />
        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            var specificSaves = saves.Cast<SerpinskySaveState>().ToList();
            SaveFileManager.SaveSaves(this.FractalTypeIdentifier, specificSaves);
        }
        #endregion

        #region IHighResRenderable Implementation
        /// <inheritdoc />
        public HighResRenderState GetRenderState()
        {
            var state = new HighResRenderState
            {
                EngineType = this.FractalTypeIdentifier,
                CenterX = (decimal)centerX,
                CenterY = (decimal)centerY,
                Zoom = (decimal)currentZoom,
                BaseScale = 1.0M, // Для Серпинского базовый масштаб всегда 1.0
                Iterations = (int)nudIterations.Value,
                FileNameDetails = "serpinski_fractal"
            };
            return state;
        }

        /// <summary>
        /// Создает и настраивает экземпляр движка <see cref="FractalSerpinskyEngine"/> на основе состояния рендеринга.
        /// </summary>
        /// <param name="state">Состояние рендеринга.</param>
        /// <param name="forPreview">Если true, используются пониженные настройки для быстрого предпросмотра.</param>
        /// <returns>Настроенный экземпляр движка.</returns>
        private FractalSerpinskyEngine CreateEngineFromState(HighResRenderState state, bool forPreview)
        {
            var engine = new FractalSerpinskyEngine();

            var currentPalette = _paletteManager.ActivePalette;
            engine.FractalColor = currentPalette.FractalColor;
            engine.BackgroundColor = currentPalette.BackgroundColor;
            engine.ColorMode = SerpinskyColorMode.CustomColor;

            engine.RenderMode = FractalTypeIsGeometry.Checked ? SerpinskyRenderMode.Geometric : SerpinskyRenderMode.Chaos;
            engine.CenterX = (double)state.CenterX;
            engine.CenterY = (double)state.CenterY;
            engine.Zoom = (double)state.Zoom;

            if (forPreview)
            {
                engine.Iterations = (engine.RenderMode == SerpinskyRenderMode.Geometric)
                    ? Math.Min(state.Iterations, 5)
                    : Math.Min(state.Iterations, 20000);
            }
            else
            {
                engine.Iterations = state.Iterations;
            }

            return engine;
        }

        /// <inheritdoc />
        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            isHighResRendering = true;
            SetMainControlsEnabled(false);
            highResRenderCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var linkedToken = highResRenderCts.Token;

            try
            {
                var renderEngine = CreateEngineFromState(state, forPreview: false);
                int numThreads = GetThreadCount();
                Action<int> progressCallback = p => progress.Report(new RenderProgress { Percentage = p, Status = "Рендеринг..." });

                if (renderEngine.RenderMode == SerpinskyRenderMode.Geometric)
                {
                    // Стратегия SSAA для геометрического режима
                    int highResWidth = width * ssaaFactor;
                    int highResHeight = height * ssaaFactor;

                    var highResBmp = new Bitmap(highResWidth, highResHeight, PixelFormat.Format32bppArgb);
                    var bmpData = highResBmp.LockBits(new Rectangle(0, 0, highResWidth, highResHeight), ImageLockMode.WriteOnly, highResBmp.PixelFormat);
                    var buffer = new byte[bmpData.Stride * highResHeight];

                    await Task.Run(() => renderEngine.RenderToBuffer(buffer, highResWidth, highResHeight, bmpData.Stride, 4, numThreads, linkedToken, progressCallback), linkedToken);
                    linkedToken.ThrowIfCancellationRequested();

                    Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
                    highResBmp.UnlockBits(bmpData);

                    var finalBmp = new Bitmap(width, height);
                    using (var g = Graphics.FromImage(finalBmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(highResBmp, 0, 0, width, height);
                    }
                    highResBmp.Dispose();
                    return finalBmp;
                }
                else // Chaos Mode
                {
                    // Стратегия увеличения итераций для режима хаоса
                    double basePixels = 1000 * 1000;
                    double targetPixels = width * height;
                    double qualityFactor = Math.Max(1.0, targetPixels / basePixels);
                    renderEngine.Iterations = (int)(state.Iterations * qualityFactor);

                    var finalBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    var bmpData = finalBmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, finalBmp.PixelFormat);
                    var buffer = new byte[bmpData.Stride * height];

                    await Task.Run(() => renderEngine.RenderToBuffer(buffer, width, height, bmpData.Stride, 4, numThreads, linkedToken, progressCallback), linkedToken);
                    linkedToken.ThrowIfCancellationRequested();

                    Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
                    finalBmp.UnlockBits(bmpData);
                    return finalBmp;
                }
            }
            finally
            {
                isHighResRendering = false;
                SetMainControlsEnabled(true);
                highResRenderCts?.Dispose();
                highResRenderCts = null;
            }
        }

        /// <inheritdoc />
        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            var engine = CreateEngineFromState(state, forPreview: true);
            var bmp = new Bitmap(previewWidth, previewHeight, PixelFormat.Format32bppArgb);
            var bmpData = bmp.LockBits(new Rectangle(0, 0, previewWidth, previewHeight), ImageLockMode.WriteOnly, bmp.PixelFormat);
            var buffer = new byte[bmpData.Stride * previewHeight];

            engine.RenderToBuffer(buffer, previewWidth, previewHeight, bmpData.Stride, 4, 1, CancellationToken.None, _ => { });

            Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
            bmp.UnlockBits(bmpData);
            return bmp;
        }

        #endregion
    }
}
