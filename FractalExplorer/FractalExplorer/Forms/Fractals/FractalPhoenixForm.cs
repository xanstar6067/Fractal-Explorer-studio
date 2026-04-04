using FractalExplorer.Utilities;
using FractalExplorer.Engines;
using FractalExplorer.Resources;
using FractalExplorer.SelectorsForms;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using System.Diagnostics;
using FractalExplorer.Forms.Other;
using FractalExplorer.Utilities.RenderUtilities;
using System.Text;
using System.Drawing.Drawing2D;

using FractalExplorer.Utilities.Theme;
using FractalExplorer.Utilities.UI;
namespace FractalExplorer.Forms
{
    /// <summary>
    /// Представляет основную форму для отображения и взаимодействия с фракталом Феникс.
    /// Реализует интерфейс <see cref="ISaveLoadCapableFractal"/> для сохранения и загрузки состояний фрактала.
    /// </summary>
    public partial class FractalPhoenixForm : Form, ISaveLoadCapableFractal, IHighResRenderable
    {
        #region Fields
        /// <summary>
        /// Движок для рендеринга фрактала Феникс.
        /// </summary>
        private PhoenixEngine _fractalEngine;
        /// <summary>
        /// Компонент для визуализации процесса рендеринга (например, отображения тайлов).
        /// </summary>
        private RenderVisualizerComponent _renderVisualizer;
        /// <summary>
        /// Менеджер цветовых палитр для фракталов.
        /// </summary>
        private PaletteManager _paletteManager;
        /// <summary>
        /// Кэш для цветов палитры с уже примененной гамма-коррекцией (для дискретного режима).
        /// </summary>
        private Color[] _gammaCorrectedPaletteCache;
        /// <summary>
        /// "Подпись" палитры, для которой был сгенерирован кэш. 
        /// </summary>
        private string _paletteCacheSignature;
        /// <summary>
        /// Форма для настройки параметров цветовой палитры.
        /// </summary>
        private ColorConfigurationForm _colorConfigForm;
        /// <summary>
        /// Форма для выбора C-параметров фрактала Феникс.
        /// </summary>
        private PhoenixCSelectorForm _phoenixCSelectorWindow;

        /// <summary>
        /// Размер тайла для рендеринга в пикселях.
        /// </summary>
        private const int TILE_SIZE = 16;
        private const int ToggleButtonMargin = 12;
        /// <summary>
        /// Объект для синхронизации доступа к битмапам из разных потоков.
        /// </summary>
        private readonly object _bitmapLock = new object();
        /// <summary>
        /// Битмап с отрендеренным предпросмотром фрактала.
        /// </summary>
        private Bitmap _previewBitmap;
        /// <summary>
        /// Битмап, на котором происходит текущий рендеринг.
        /// </summary>
        private Bitmap _currentRenderingBitmap;
        /// <summary>
        /// Размер полной буферной поверхности рендера для последнего завершенного кадра.
        /// </summary>
        private Size _renderedSurfaceSize = Size.Empty;
        /// <summary>
        /// Видимая область внутри полной буферной поверхности для последнего завершенного кадра.
        /// </summary>
        private Rectangle _renderedViewportRect = Rectangle.Empty;
        /// <summary>
        /// Источник токенов для отмены текущего рендеринга предпросмотра.
        /// </summary>
        private CancellationTokenSource _previewRenderCts;

        /// <summary>
        /// Флаг, указывающий, что в данный момент выполняется рендеринг в высоком разрешении.
        /// </summary>
        private volatile bool _isHighResRendering = false;
        /// <summary>
        /// Флаг, указывающий, что в данный момент выполняется рендеринг предпросмотра.
        /// </summary>
        private volatile bool _isRenderingPreview = false;

        /// <summary>
        /// Текущий уровень масштабирования.
        /// </summary>
        protected decimal _zoom = 1.0m;
        /// <summary>
        /// Координата X центра отображаемой области.
        /// </summary>
        protected decimal _centerX = 0.0m;
        /// <summary>
        /// Координата Y центра отображаемой области.
        /// </summary>
        protected decimal _centerY = 0.0m;

        /// <summary>
        /// Координата X центра на момент последнего завершенного рендеринга.
        /// </summary>
        private decimal _renderedCenterX;
        /// <summary>
        /// Координата Y центра на момент последнего завершенного рендеринга.
        /// </summary>
        private decimal _renderedCenterY;
        /// <summary>
        /// Уровень масштабирования на момент последнего завершенного рендеринга.
        /// </summary>
        private decimal _renderedZoom;

        /// <summary>
        /// Начальная точка для панорамирования.
        /// </summary>
        private Point _panStart;
        /// <summary>
        /// Флаг, указывающий, что выполняется панорамирование.
        /// </summary>
        private bool _panning = false;

        /// <summary>
        /// Таймер для отложенного запуска рендеринга после изменения параметров.
        /// </summary>
        private System.Windows.Forms.Timer _renderDebounceTimer;
        /// <summary>
        /// Флаг, отключающий запуск полного рендера при программном изменении layout.
        /// </summary>
        private bool _suppressResizeRender = false;
        private readonly FullscreenToggleController _fullscreenController = new();
        /// <summary>
        /// Признак видимости панели управления.
        /// </summary>
        private bool _controlsPanelVisible = true;
        /// <summary>
        /// Базовый заголовок окна.
        /// </summary>
        private string _baseTitle;
        /// <summary>
        /// Базовый масштаб для вычислений.
        /// </summary>
        private const decimal BASE_SCALE = 4.0m;
        #endregion

        #region Constructor
        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="FractalPhoenixForm"/>.
        /// </summary>
        public FractalPhoenixForm()
        {
            InitializeComponent();
            KeyPreview = true;
            ThemeManager.RegisterForm(this);
            Text = "Фрактал Феникс";
        }
        #endregion

        #region UI Initialization
        /// <summary>
        /// Инициализирует значения по умолчанию и ограничения для элементов управления UI.
        /// </summary>
        private void InitializeControls()
        {
            int cores = Environment.ProcessorCount;
            cbThreads.Items.Clear();
            for (int i = 1; i <= cores; i++) cbThreads.Items.Add(i);
            cbThreads.Items.Add("Auto");
            cbThreads.SelectedItem = "Auto";

            nudIterations.Minimum = 10;
            nudIterations.Maximum = 100000;
            nudIterations.Value = 100;

            nudThreshold.Minimum = 2m;
            nudThreshold.Maximum = 1000m;
            nudThreshold.DecimalPlaces = 1;
            nudThreshold.Increment = 0.1m;
            nudThreshold.Value = 4m;

            nudZoom.DecimalPlaces = 15;
            nudZoom.Increment = 0.1m;
            nudZoom.Minimum = 0.001m;
            nudZoom.Maximum = decimal.MaxValue;
            _zoom = BASE_SCALE / 4.0m;
            nudZoom.Value = _zoom;

            nudC1Re.Value = 0.56m; nudC1Im.Value = -0.5m;
            nudC2Re.Value = 0.0m; nudC2Im.Value = 0.0m;
            nudC1Re.DecimalPlaces = 15; nudC1Im.DecimalPlaces = 15;
            nudC2Re.DecimalPlaces = 15; nudC2Im.DecimalPlaces = 15;
            nudC1Re.Increment = 0.001m; nudC1Im.Increment = 0.001m;
            nudC2Re.Increment = 0.001m; nudC2Im.Increment = 0.001m;
            nudC1Re.Minimum = -2m; nudC1Re.Maximum = 2m;
            nudC1Im.Minimum = -2m; nudC1Im.Maximum = 2m;
            nudC2Re.Minimum = -2m; nudC2Re.Maximum = 2m;
            nudC2Im.Minimum = -2m; nudC2Im.Maximum = 2m;

            cbSSAA.Items.Add("Выкл (1x)");
            cbSSAA.Items.Add("Низкое (2x)");
            cbSSAA.Items.Add("Высокое (4x)");
            cbSSAA.SelectedItem = "Выкл (1x)";

            cbSmooth.Items.Clear();
            cbSmooth.Items.AddRange(new object[] { "Дискретно", "Плавно" });
            cbSmooth.SelectedIndex = 1;
        }

        /// <summary>
        /// Инициализирует обработчики событий для различных элементов управления UI.
        /// </summary>
        private void InitializeEventHandlers()
        {
            nudC1Re.ValueChanged += ParamControl_Changed;
            nudC1Im.ValueChanged += ParamControl_Changed;
            nudC2Re.ValueChanged += ParamControl_Changed;
            nudC2Im.ValueChanged += ParamControl_Changed;
            nudIterations.ValueChanged += ParamControl_Changed;
            nudThreshold.ValueChanged += ParamControl_Changed;
            cbThreads.SelectedIndexChanged += ParamControl_Changed;
            nudZoom.ValueChanged += ParamControl_Changed;
            cbSmooth.SelectedIndexChanged += ParamControl_Changed;
            cbSSAA.SelectedIndexChanged += ParamControl_Changed;

            canvas.MouseWheel += Canvas_MouseWheel;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.Paint += Canvas_Paint;
            canvas.Resize += Canvas_Resize;
            canvasHost.Resize += CanvasHost_Resize;
            controlsHost.SizeChanged += ControlsHost_SizeChanged;
            KeyDown += Form_KeyDown;
            FormClosing += Form_FormClosing;
        }
        #endregion

        /// <summary>
        /// Снимает стартовый фокус с комбобоксов, чтобы они не выглядели выделенными сразу после открытия формы.
        /// </summary>
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

        #region UI Event Handlers
        /// <summary>
        /// Обрабатывает изменение значения в элементах управления параметрами фрактала.
        /// </summary>
        private void ParamControl_Changed(object sender, EventArgs e)
        {
            if (_isHighResRendering) return;
            if (sender == nudZoom && nudZoom.Value != _zoom) _zoom = nudZoom.Value;
            ScheduleRender();
        }

        /// <summary>
        /// Обрабатывает нажатие на кнопку "Рендер", запуская рендеринг предпросмотра.
        /// </summary>
        private async void btnRender_Click(object sender, EventArgs e)
        {
            _renderDebounceTimer.Stop();
            _previewRenderCts?.Cancel();
            if (_isHighResRendering || _isRenderingPreview) return;

            int ssaaFactor = GetSelectedSsaaFactor();
            this.Text = $"{_baseTitle} - Качество: {ssaaFactor}x";
            if (ssaaFactor > 1)
            {
                await StartPreviewRenderSSAA(ssaaFactor);
            }
            else
            {
                await StartPreviewRender();
            }
        }

        /// <summary>
        /// Обрабатывает нажатие на кнопку конфигурации цветов, открывая соответствующее окно.
        /// </summary>
        private void color_configurations_Click(object sender, EventArgs e)
        {
            if (_colorConfigForm == null || _colorConfigForm.IsDisposed)
            {
                _colorConfigForm = new ColorConfigurationForm(_paletteManager);
                _colorConfigForm.PaletteApplied += OnPaletteApplied;
                _colorConfigForm.FormClosed += (s, args) => _colorConfigForm = null;
                _colorConfigForm.Show(this);
            }
            else
            {
                _colorConfigForm.Activate();
            }
        }

        /// <summary>
        /// Открывает окно для выбора параметров C для фрактала Феникс.
        /// </summary>
        private void btnSelectPhoenixParameters_Click(object sender, EventArgs e)
        {
            ComplexDecimal currentC1 = new ComplexDecimal(nudC1Re.Value, nudC1Im.Value);
            ComplexDecimal currentC2 = new ComplexDecimal(nudC2Re.Value, nudC2Im.Value);
            if (_phoenixCSelectorWindow == null || _phoenixCSelectorWindow.IsDisposed)
            {
                _phoenixCSelectorWindow = new PhoenixCSelectorForm(this, currentC1, currentC2);
                _phoenixCSelectorWindow.ParametersSelected += (selectedC1, selectedC2) =>
                {
                    if (nudC1Re.Value != selectedC1.Real) nudC1Re.Value = selectedC1.Real;
                    if (nudC1Im.Value != selectedC1.Imaginary) nudC1Im.Value = selectedC1.Imaginary;
                };
                _phoenixCSelectorWindow.FormClosed += (s, args) => { _phoenixCSelectorWindow = null; };
                _phoenixCSelectorWindow.Show(this);
            }
            else
            {
                _phoenixCSelectorWindow.SetSelectedParameters(currentC1);
                _phoenixCSelectorWindow.Activate();
            }
        }

        /// <summary>
        /// Открывает менеджер сохранения изображений.
        /// </summary>
        private void btnOpenSaveManager_Click(object sender, EventArgs e)
        {
            if (_isHighResRendering)
            {
                MessageBox.Show("Процесс рендеринга уже запущен.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var saveManager = new SaveImageManagerForm(this))
            {
                saveManager.ShowDialog(this);
            }
        }

        #endregion

        #region Canvas Interaction
        /// <summary>
        /// Обрабатывает событие прокрутки колеса мыши для масштабирования.
        /// </summary>
        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering || canvas.Width <= 0 || canvas.Height <= 0) return;
            CommitAndBakePreview();
            decimal zoomFactor = e.Delta > 0 ? 1.5m : 1.0m / 1.5m;
            decimal scaleBeforeZoom = BASE_SCALE / _zoom;
            decimal mouseReal = _centerX + (e.X - canvas.Width / 2.0m) * scaleBeforeZoom / canvas.Width;
            decimal mouseImaginary = _centerY - (e.Y - canvas.Height / 2.0m) * scaleBeforeZoom / canvas.Height;
            _zoom = Math.Max(nudZoom.Minimum, Math.Min(nudZoom.Maximum, _zoom * zoomFactor));
            decimal scaleAfterZoom = BASE_SCALE / _zoom;
            _centerX = mouseReal - (e.X - canvas.Width / 2.0m) * scaleAfterZoom / canvas.Width;
            _centerY = mouseImaginary + (e.Y - canvas.Height / 2.0m) * scaleAfterZoom / canvas.Height;
            canvas.Invalidate();
            if (nudZoom.Value != _zoom) nudZoom.Value = _zoom;
            else ScheduleRender();
        }

        /// <summary>
        /// Обрабатывает нажатие кнопки мыши для начала панорамирования.
        /// </summary>
        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering) return;
            if (e.Button == MouseButtons.Left)
            {
                _panning = true;
                _panStart = e.Location;
                canvas.Cursor = Cursors.Hand;
            }
        }
        /// <summary>
        /// Обрабатывает движение мыши для выполнения панорамирования.
        /// </summary>
        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering || !_panning || canvas.Width <= 0) return;
            CommitAndBakePreview();
            decimal unitsPerPixel = BASE_SCALE / _zoom / canvas.Width;
            _centerX -= (decimal)(e.X - _panStart.X) * unitsPerPixel;
            _centerY += (decimal)(e.Y - _panStart.Y) * unitsPerPixel;
            _panStart = e.Location;
            canvas.Invalidate();
        }

        /// <summary>
        /// Обрабатывает отпускание кнопки мыши для завершения панорамирования.
        /// </summary>
        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering) return;
            if (e.Button == MouseButtons.Left)
            {
                _panning = false;
                canvas.Cursor = Cursors.Default;
                ScheduleRender();
            }
        }
        /// <summary>
        /// Обрабатывает событие перерисовки холста, отображая фрактал.
        /// </summary>
        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            if (canvas.Width <= 0 || canvas.Height <= 0) { e.Graphics.Clear(Color.Black); return; }
            e.Graphics.Clear(Color.Black);
            Rectangle currentViewportRect = GetCurrentViewportRect();
            lock (_bitmapLock)
            {
                if (_previewBitmap != null)
                {
                    if (_renderedCenterX == _centerX && _renderedCenterY == _centerY && _renderedZoom == _zoom)
                    {
                        Rectangle sourceViewport = GetSafeViewportRect(_previewBitmap.Size, currentViewportRect);
                        DrawPreviewBitmap(e.Graphics, _previewBitmap, sourceViewport, canvas.ClientRectangle);
                    }
                    else
                    {
                        DrawTransformedPreview(e.Graphics, _previewBitmap, canvas.ClientRectangle);
                    }
                }
                if (_currentRenderingBitmap != null) DrawVisibleBitmap(e.Graphics, _currentRenderingBitmap, currentViewportRect, canvas.ClientRectangle);
            }
            if (_renderVisualizer != null && _isRenderingPreview) _renderVisualizer.DrawVisualization(e.Graphics);
        }
        #endregion

        #region Rendering Logic
        /// <summary>
        /// Планирует отложенный запуск рендеринга.
        /// </summary>
        private void ScheduleRender(bool cancelCurrentRender = true)
        {
            if (_isHighResRendering || WindowState == FormWindowState.Minimized) return;
            if (cancelCurrentRender && _isRenderingPreview) _previewRenderCts?.Cancel();
            _renderDebounceTimer.Stop();
            _renderDebounceTimer.Start();
        }

        /// <summary>
        /// Асинхронно запускает рендеринг предпросмотра с использованием суперсэмплинга (SSAA).
        /// </summary>
        private async Task StartPreviewRenderSSAA(int ssaaFactor)
        {
            Size renderSurfaceSize = GetRenderSurfaceSize();
            Rectangle viewportRect = GetCurrentViewportRect();
            if (renderSurfaceSize.Width <= 0 || renderSurfaceSize.Height <= 0 || viewportRect.Width <= 0 || viewportRect.Height <= 0) return;

            int currentWidth = renderSurfaceSize.Width;
            int currentHeight = renderSurfaceSize.Height;

            var stopwatch = Stopwatch.StartNew();
            _isRenderingPreview = true;
            _previewRenderCts?.Cancel();
            _previewRenderCts = new CancellationTokenSource();
            var token = _previewRenderCts.Token;
            _renderVisualizer?.NotifyRenderSessionStart();

            var newRenderingBitmap = new Bitmap(currentWidth, currentHeight, PixelFormat.Format32bppArgb);
            lock (_bitmapLock)
            {
                _currentRenderingBitmap = newRenderingBitmap;
            }
            UpdateEngineParameters();
            var currentRenderedCenterX = _centerX;
            var currentRenderedCenterY = _centerY;
            var currentRenderedZoom = _zoom;
            var renderEngineCopy = new PhoenixEngine
            {
                MaxIterations = _fractalEngine.MaxIterations,
                ThresholdSquared = _fractalEngine.ThresholdSquared,
                CenterX = GetRenderSurfaceCenterX(renderSurfaceSize, viewportRect),
                CenterY = _fractalEngine.CenterY,
                Scale = GetRenderSurfaceScale(renderSurfaceSize, viewportRect),
                C1 = _fractalEngine.C1,
                C2 = _fractalEngine.C2,
                UseSmoothColoring = _fractalEngine.UseSmoothColoring,
                Palette = _fractalEngine.Palette,
                SmoothPalette = _fractalEngine.SmoothPalette,
                MaxColorIterations = _fractalEngine.MaxColorIterations
            };

            var threadCount = GetThreadCount();
            int tileConcurrency = threadCount;
            int inTileConcurrency = 1;
            var tiles = GenerateTiles(currentWidth, currentHeight);
            var dispatcher = new TileRenderDispatcher(tiles, tileConcurrency, RenderPatternSettings.SelectedPattern);

            if (pbRenderProgress.IsHandleCreated && !pbRenderProgress.IsDisposed)
            {
                pbRenderProgress.Invoke((Action)(() => { pbRenderProgress.Value = 0; pbRenderProgress.Maximum = tiles.Count; }));
            }
            int progress = 0;
            try
            {
                await dispatcher.RenderAsync(async (tile, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    _renderVisualizer?.NotifyTileRenderStart(tile.Bounds);

                    // Не допускаем вложенный oversubscription: параллелим по плиткам, внутри плитки — один поток.
                    var tileBuffer = renderEngineCopy.RenderSingleTileSSAA(tile, currentWidth, currentHeight, ssaaFactor, inTileConcurrency, out int bytesPerPixel);

                    ct.ThrowIfCancellationRequested();
                    lock (_bitmapLock)
                    {
                        if (ct.IsCancellationRequested || _currentRenderingBitmap != newRenderingBitmap) return;
                        var tileRect = tile.Bounds;
                        var bitmapRect = new Rectangle(0, 0, _currentRenderingBitmap.Width, _currentRenderingBitmap.Height);
                        tileRect.Intersect(bitmapRect);
                        if (tileRect.Width == 0 || tileRect.Height == 0) return;

                        BitmapData bmpData = _currentRenderingBitmap.LockBits(tileRect, ImageLockMode.WriteOnly, _currentRenderingBitmap.PixelFormat);
                        int tileWidthInBytes = tile.Bounds.Width * bytesPerPixel;
                        for (int y = 0; y < tileRect.Height; y++)
                        {
                            IntPtr destPtr = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                            int srcOffset = y * tileWidthInBytes;
                            Marshal.Copy(tileBuffer, srcOffset, destPtr, tileRect.Width * bytesPerPixel);
                        }
                        _currentRenderingBitmap.UnlockBits(bmpData);
                    }
                    _renderVisualizer?.NotifyTileRenderComplete(tile.Bounds);
                    if (ct.IsCancellationRequested || !canvas.IsHandleCreated || canvas.IsDisposed) return;
                    canvas.Invoke((Action)(() =>
                    {
                        if (!ct.IsCancellationRequested && pbRenderProgress.IsHandleCreated && !pbRenderProgress.IsDisposed)
                        {
                            pbRenderProgress.Value = Math.Min(pbRenderProgress.Maximum, Interlocked.Increment(ref progress));
                        }
                    }));
                    await Task.Yield();
                }, token);

                token.ThrowIfCancellationRequested();
                stopwatch.Stop();
                this.Text = $"{_baseTitle} - Время рендера (SSAA {ssaaFactor}x): {stopwatch.Elapsed.TotalSeconds:F3} сек.";
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _previewBitmap?.Dispose();
                        _previewBitmap = _currentRenderingBitmap;
                        _currentRenderingBitmap = null;
                        _renderedCenterX = currentRenderedCenterX;
                        _renderedCenterY = currentRenderedCenterY;
                        _renderedZoom = currentRenderedZoom;
                        _renderedSurfaceSize = renderSurfaceSize;
                        _renderedViewportRect = viewportRect;
                    }
                    else
                    {
                        newRenderingBitmap?.Dispose();
                    }
                }
                if (canvas.IsHandleCreated && !canvas.IsDisposed) canvas.Invalidate();
            }
            catch (OperationCanceledException)
            {
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _currentRenderingBitmap = null;
                    }
                }
                newRenderingBitmap.Dispose();
            }
            catch (Exception ex)
            {
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _currentRenderingBitmap = null;
                    }
                }
                newRenderingBitmap?.Dispose();
                if (IsHandleCreated && !IsDisposed) MessageBox.Show($"Ошибка рендеринга SSAA: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isRenderingPreview = false;
                _renderVisualizer?.NotifyRenderSessionComplete();
                if (pbRenderProgress.IsHandleCreated && !pbRenderProgress.IsDisposed) pbRenderProgress.Invoke((Action)(() => pbRenderProgress.Value = 0));
            }
        }


        /// <summary>
        /// Запускает асинхронный процесс рендеринга предпросмотра фрактала.
        /// </summary>
        private async Task StartPreviewRender()
        {
            Size renderSurfaceSize = GetRenderSurfaceSize();
            Rectangle viewportRect = GetCurrentViewportRect();
            if (renderSurfaceSize.Width <= 0 || renderSurfaceSize.Height <= 0 || viewportRect.Width <= 0 || viewportRect.Height <= 0) return;

            int currentWidth = renderSurfaceSize.Width;
            int currentHeight = renderSurfaceSize.Height;

            var stopwatch = Stopwatch.StartNew();
            _isRenderingPreview = true;
            _previewRenderCts?.Cancel();
            _previewRenderCts = new CancellationTokenSource();
            var token = _previewRenderCts.Token;
            _renderVisualizer?.NotifyRenderSessionStart();

            var newRenderingBitmap = new Bitmap(currentWidth, currentHeight, PixelFormat.Format32bppArgb);
            lock (_bitmapLock)
            {
                _currentRenderingBitmap = newRenderingBitmap;
            }
            UpdateEngineParameters();
            var currentRenderedCenterX = _centerX;
            var currentRenderedCenterY = _centerY;
            var currentRenderedZoom = _zoom;
            var renderEngineCopy = new PhoenixEngine
            {
                MaxIterations = _fractalEngine.MaxIterations,
                ThresholdSquared = _fractalEngine.ThresholdSquared,
                CenterX = GetRenderSurfaceCenterX(renderSurfaceSize, viewportRect),
                CenterY = _fractalEngine.CenterY,
                Scale = GetRenderSurfaceScale(renderSurfaceSize, viewportRect),
                C1 = _fractalEngine.C1,
                C2 = _fractalEngine.C2,
                UseSmoothColoring = _fractalEngine.UseSmoothColoring,
                Palette = _fractalEngine.Palette,
                SmoothPalette = _fractalEngine.SmoothPalette,
                MaxColorIterations = _fractalEngine.MaxColorIterations
            };

            var threadCount = GetThreadCount();
            int tileConcurrency = threadCount;
            var tiles = GenerateTiles(currentWidth, currentHeight);
            var dispatcher = new TileRenderDispatcher(tiles, tileConcurrency, RenderPatternSettings.SelectedPattern);

            if (pbRenderProgress.IsHandleCreated && !pbRenderProgress.IsDisposed)
            {
                pbRenderProgress.Invoke((Action)(() => { pbRenderProgress.Value = 0; pbRenderProgress.Maximum = tiles.Count; }));
            }
            int progress = 0;
            try
            {
                await dispatcher.RenderAsync(async (tile, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    _renderVisualizer?.NotifyTileRenderStart(tile.Bounds);

                    var tileBuffer = renderEngineCopy.RenderSingleTile(tile, currentWidth, currentHeight, out int bytesPerPixel);

                    ct.ThrowIfCancellationRequested();
                    lock (_bitmapLock)
                    {
                        if (ct.IsCancellationRequested || _currentRenderingBitmap != newRenderingBitmap) return;
                        var tileRect = tile.Bounds;
                        var bitmapRect = new Rectangle(0, 0, _currentRenderingBitmap.Width, _currentRenderingBitmap.Height);
                        tileRect.Intersect(bitmapRect);
                        if (tileRect.Width == 0 || tileRect.Height == 0) return;
                        BitmapData bmpData = _currentRenderingBitmap.LockBits(tileRect, ImageLockMode.WriteOnly, _currentRenderingBitmap.PixelFormat);
                        int originalTileWidthInBytes = tile.Bounds.Width * bytesPerPixel;
                        for (int y = 0; y < tileRect.Height; y++)
                        {
                            IntPtr destPtr = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                            int srcOffset = ((y + tileRect.Y) - tile.Bounds.Y) * originalTileWidthInBytes + ((tileRect.X - tile.Bounds.X) * bytesPerPixel);
                            Marshal.Copy(tileBuffer, srcOffset, destPtr, tileRect.Width * bytesPerPixel);
                        }
                        _currentRenderingBitmap.UnlockBits(bmpData);
                    }
                    _renderVisualizer?.NotifyTileRenderComplete(tile.Bounds);
                    if (ct.IsCancellationRequested || !canvas.IsHandleCreated || canvas.IsDisposed) return;
                    canvas.Invoke((Action)(() =>
                    {
                        if (!ct.IsCancellationRequested && pbRenderProgress.IsHandleCreated && !pbRenderProgress.IsDisposed)
                        {
                            pbRenderProgress.Value = Math.Min(pbRenderProgress.Maximum, Interlocked.Increment(ref progress));
                        }
                    }));
                    await Task.Yield();
                }, token);

                token.ThrowIfCancellationRequested();
                stopwatch.Stop();
                this.Text = $"{_baseTitle} - Время последнего рендера: {stopwatch.Elapsed.TotalSeconds:F3} сек.";
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _previewBitmap?.Dispose();
                        _previewBitmap = _currentRenderingBitmap;
                        _currentRenderingBitmap = null;
                        _renderedCenterX = currentRenderedCenterX;
                        _renderedCenterY = currentRenderedCenterY;
                        _renderedZoom = currentRenderedZoom;
                        _renderedSurfaceSize = renderSurfaceSize;
                        _renderedViewportRect = viewportRect;
                    }
                    else
                    {
                        newRenderingBitmap?.Dispose();
                    }
                }
                if (canvas.IsHandleCreated && !canvas.IsDisposed) canvas.Invalidate();
            }
            catch (OperationCanceledException)
            {
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _currentRenderingBitmap = null;
                    }
                }
                newRenderingBitmap.Dispose();
            }
            catch (Exception ex)
            {
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _currentRenderingBitmap = null;
                    }
                }
                newRenderingBitmap?.Dispose();
                if (IsHandleCreated && !IsDisposed) MessageBox.Show($"Ошибка рендеринга: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isRenderingPreview = false;
                _renderVisualizer?.NotifyRenderSessionComplete();
                if (pbRenderProgress.IsHandleCreated && !pbRenderProgress.IsDisposed) pbRenderProgress.Invoke((Action)(() => pbRenderProgress.Value = 0));
            }
        }

        /// <summary>
        /// Обрабатывает тик таймера отложенного рендеринга.
        /// </summary>
        private async void RenderDebounceTimer_Tick(object sender, EventArgs e)
        {
            _renderDebounceTimer.Stop();
            if (_isHighResRendering || _isRenderingPreview)
            {
                ScheduleRender();
                return;
            }

            int ssaaFactor = GetSelectedSsaaFactor();
            this.Text = $"{_baseTitle} - Качество: {ssaaFactor}x";
            if (ssaaFactor > 1)
            {
                await StartPreviewRenderSSAA(ssaaFactor);
            }
            else
            {
                await StartPreviewRender();
            }
        }

        /// <summary>
        /// Вызывается визуализатором рендеринга, когда требуется перерисовка.
        /// </summary>
        private void OnVisualizerNeedsRedraw()
        {
            if (canvas.IsHandleCreated && !canvas.IsDisposed)
            {
                canvas.BeginInvoke((Action)(() => canvas.Invalidate()));
            }
        }
        #endregion

        #region Utility Methods
        /// <summary>
        /// Завершает текущий рендеринг и "запекает" его результат в основной битмап предпросмотра.
        /// </summary>
        private void CommitAndBakePreview()
        {
            lock (_bitmapLock) { if (!_isRenderingPreview || _currentRenderingBitmap == null) return; }
            _previewRenderCts?.Cancel();
            lock (_bitmapLock)
            {
                Size renderSurfaceSize = GetRenderSurfaceSize();
                Rectangle viewportRect = GetCurrentViewportRect();
                if (_currentRenderingBitmap == null || renderSurfaceSize.Width <= 0 || renderSurfaceSize.Height <= 0 || viewportRect.Width <= 0 || viewportRect.Height <= 0) return;
                var bakedBitmap = new Bitmap(renderSurfaceSize.Width, renderSurfaceSize.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(bakedBitmap))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    if (_previewBitmap != null)
                    {
                        try
                        {
                            decimal renderedComplexWidth = BASE_SCALE / _renderedZoom;
                            decimal currentComplexWidth = BASE_SCALE / _zoom;
                            if (!(_renderedZoom <= 0 || _zoom <= 0 || renderedComplexWidth <= 0 || currentComplexWidth <= 0))
                            {
                                Rectangle sourceViewport = GetSafeViewportRect(_previewBitmap.Size, _renderedViewportRect);
                                decimal unitsPerPixelRendered = renderedComplexWidth / sourceViewport.Width;
                                decimal unitsPerPixelCurrent = currentComplexWidth / viewportRect.Width;
                                decimal renderedReMin = _renderedCenterX - (renderedComplexWidth / 2.0m);
                                decimal renderedImMax = _renderedCenterY + (sourceViewport.Height * unitsPerPixelRendered / 2.0m);
                                decimal currentReMin = _centerX - (currentComplexWidth / 2.0m);
                                decimal currentImMax = _centerY + (viewportRect.Height * unitsPerPixelCurrent / 2.0m);
                                decimal offsetXPixels = (renderedReMin - currentReMin) / unitsPerPixelCurrent;
                                decimal offsetYPixels = (currentImMax - renderedImMax) / unitsPerPixelCurrent;
                                decimal newWidthPixels = sourceViewport.Width * (unitsPerPixelRendered / unitsPerPixelCurrent);
                                decimal newHeightPixels = sourceViewport.Height * (unitsPerPixelRendered / unitsPerPixelCurrent);
                                PointF destPoint1 = new PointF(viewportRect.X + (float)offsetXPixels, viewportRect.Y + (float)offsetYPixels);
                                PointF destPoint2 = new PointF(viewportRect.X + (float)(offsetXPixels + newWidthPixels), viewportRect.Y + (float)offsetYPixels);
                                PointF destPoint3 = new PointF(viewportRect.X + (float)offsetXPixels, viewportRect.Y + (float)(offsetYPixels + newHeightPixels));
                                g.DrawImage(_previewBitmap, new PointF[] { destPoint1, destPoint2, destPoint3 }, sourceViewport, GraphicsUnit.Pixel);
                            }
                        }
                        catch (Exception) { }
                    }
                    g.DrawImageUnscaled(_currentRenderingBitmap, Point.Empty);
                }
                _previewBitmap?.Dispose();
                _previewBitmap = bakedBitmap;
                // ВАЖНО: здесь только detach. Фоновые worker'ы могут еще завершаться и
                // писать в старый bitmap под _bitmapLock, поэтому немедленный Dispose опасен.
                // Реальный Dispose выполняется в безопасной точке жизненного цикла формы (cleanup/закрытие).
                _currentRenderingBitmap = null;
                _renderedCenterX = _centerX;
                _renderedCenterY = _centerY;
                _renderedZoom = _zoom;
                _renderedSurfaceSize = renderSurfaceSize;
                _renderedViewportRect = viewportRect;
            }
        }

        private void Canvas_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized) return;
            if (_suppressResizeRender)
            {
                canvas.Invalidate();
                return;
            }
            ScheduleRender(cancelCurrentRender: false);
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
                canvas.Invalidate();
            }
            finally
            {
                _suppressResizeRender = false;
            }
        }

        private Size GetRenderSurfaceSize()
        {
            return canvas.ClientSize;
        }

        private Rectangle GetCurrentViewportRect()
        {
            return new Rectangle(0, 0, Math.Max(0, canvas.Width), Math.Max(0, canvas.Height));
        }

        private decimal GetRenderSurfaceCenterX(Size renderSurfaceSize, Rectangle viewportRect)
        {
            if (renderSurfaceSize.Width <= 0 || viewportRect.Width <= 0) return _centerX;
            decimal unitsPerPixel = (BASE_SCALE / _zoom) / viewportRect.Width;
            decimal viewportCenterOffset = (viewportRect.X + (viewportRect.Width / 2.0m)) - (renderSurfaceSize.Width / 2.0m);
            return _centerX - (viewportCenterOffset * unitsPerPixel);
        }

        private decimal GetRenderSurfaceScale(Size renderSurfaceSize, Rectangle viewportRect)
        {
            if (renderSurfaceSize.Width <= 0 || viewportRect.Width <= 0) return BASE_SCALE / _zoom;
            return (BASE_SCALE / _zoom) * renderSurfaceSize.Width / viewportRect.Width;
        }

        private Rectangle GetSafeViewportRect(Size bitmapSize, Rectangle viewportRect)
        {
            var safeRect = viewportRect;
            if (safeRect.Width <= 0 || safeRect.Height <= 0)
            {
                safeRect = new Rectangle(Point.Empty, bitmapSize);
            }
            safeRect.Intersect(new Rectangle(Point.Empty, bitmapSize));
            if (safeRect.Width <= 0 || safeRect.Height <= 0)
            {
                safeRect = new Rectangle(Point.Empty, bitmapSize);
            }
            return safeRect;
        }

        private void DrawVisibleBitmap(Graphics graphics, Bitmap bitmap, Rectangle sourceViewport, Rectangle destRect)
        {
            Rectangle safeSourceRect = GetSafeViewportRect(bitmap.Size, sourceViewport);
            DrawPreviewBitmap(graphics, bitmap, safeSourceRect, destRect);
        }

        private void DrawPreviewBitmap(Graphics graphics, Bitmap bitmap, Rectangle sourceViewport, Rectangle destRect)
        {
            if (destRect.Width <= 0 || destRect.Height <= 0 || sourceViewport.Width <= 0 || sourceViewport.Height <= 0) return;
            graphics.DrawImage(bitmap, destRect, sourceViewport, GraphicsUnit.Pixel);
        }

        private void DrawTransformedPreview(Graphics graphics, Bitmap bitmap, Rectangle destRect)
        {
            try
            {
                decimal renderedComplexWidth = BASE_SCALE / _renderedZoom;
                decimal currentComplexWidth = BASE_SCALE / _zoom;
                if (_renderedZoom <= 0 || _zoom <= 0 || renderedComplexWidth <= 0 || currentComplexWidth <= 0 || destRect.Width <= 0 || destRect.Height <= 0) return;

                Rectangle sourceViewport = GetSafeViewportRect(bitmap.Size, _renderedViewportRect);
                decimal unitsPerPixelRendered = renderedComplexWidth / sourceViewport.Width;
                decimal unitsPerPixelCurrent = currentComplexWidth / destRect.Width;
                decimal renderedReMin = _renderedCenterX - (renderedComplexWidth / 2.0m);
                decimal renderedImMax = _renderedCenterY + (sourceViewport.Height * unitsPerPixelRendered / 2.0m);
                decimal currentReMin = _centerX - (currentComplexWidth / 2.0m);
                decimal currentImMax = _centerY + (destRect.Height * unitsPerPixelCurrent / 2.0m);
                decimal offsetXPixels = (renderedReMin - currentReMin) / unitsPerPixelCurrent;
                decimal offsetYPixels = (currentImMax - renderedImMax) / unitsPerPixelCurrent;
                decimal newWidthPixels = sourceViewport.Width * (unitsPerPixelRendered / unitsPerPixelCurrent);
                decimal newHeightPixels = sourceViewport.Height * (unitsPerPixelRendered / unitsPerPixelCurrent);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                PointF destPoint1 = new PointF(destRect.X + (float)offsetXPixels, destRect.Y + (float)offsetYPixels);
                PointF destPoint2 = new PointF(destRect.X + (float)(offsetXPixels + newWidthPixels), destRect.Y + (float)offsetYPixels);
                PointF destPoint3 = new PointF(destRect.X + (float)offsetXPixels, destRect.Y + (float)(offsetYPixels + newHeightPixels));
                graphics.DrawImage(bitmap, new PointF[] { destPoint1, destPoint2, destPoint3 }, sourceViewport, GraphicsUnit.Pixel);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Обновляет параметры движка рендеринга на основе значений из элементов управления UI.
        /// </summary>
        private void UpdateEngineParameters()
        {
            _fractalEngine.MaxIterations = (int)nudIterations.Value;
            _fractalEngine.ThresholdSquared = nudThreshold.Value * nudThreshold.Value;
            _fractalEngine.CenterX = _centerX;
            _fractalEngine.CenterY = _centerY;
            _fractalEngine.Scale = BASE_SCALE / _zoom;
            _fractalEngine.C1 = new ComplexDecimal(nudC1Re.Value, nudC1Im.Value);
            _fractalEngine.C2 = new ComplexDecimal(nudC2Re.Value, nudC2Im.Value);
            _fractalEngine.UseSmoothColoring = IsSmoothColoringEnabled();
            ApplyActivePalette();
        }

        /// <summary>
        /// Генерирует список тайлов для рендеринга, отсортированных от центра к краям.
        /// </summary>
        private List<TileInfo> GenerateTiles(int width, int height)
        {
            var tiles = new List<TileInfo>();
            Point center = new Point(width / 2, height / 2);
            for (int y = 0; y < height; y += TILE_SIZE)
            {
                for (int x = 0; x < width; x += TILE_SIZE)
                {
                    tiles.Add(new TileInfo(x, y, TILE_SIZE, TILE_SIZE));
                }
            }
            return tiles.OrderBy(t => Math.Pow(t.Center.X - center.X, 2) + Math.Pow(t.Center.Y - center.Y, 2)).ToList();
        }

        /// <summary>
        /// Определяет количество потоков для рендеринга на основе выбора пользователя.
        /// </summary>
        private int GetThreadCount()
        {
            return cbThreads.SelectedItem?.ToString() == "Auto" ? Environment.ProcessorCount : Convert.ToInt32(cbThreads.SelectedItem);
        }

        private bool IsSmoothColoringEnabled()
        {
            return cbSmooth.SelectedIndex == 1;
        }

        /// <summary>
        /// Получает выбранный пользователем фактор суперсэмплинга (SSAA) из выпадающего списка.
        /// </summary>
        private int GetSelectedSsaaFactor()
        {
            if (cbSSAA.InvokeRequired)
            {
                return (int)cbSSAA.Invoke(new Func<int>(GetSelectedSsaaFactor));
            }
            if (cbSSAA.SelectedItem == null) return 1;
            switch (cbSSAA.SelectedItem.ToString())
            {
                case "Низкое (2x)": return 2;
                case "Высокое (4x)": return 4;
                default: return 1;
            }
        }
        #endregion

        #region Palette Management

        /// <summary>
        /// Генерирует функцию сглаженного окрашивания на основе заданной палитры.
        /// </summary>
        private Func<double, Color> GenerateSmoothPaletteFunction(Palette palette, int effectiveMaxColorIterations)
        {
            double gamma = palette.Gamma;
            var colors = new List<Color>(palette.Colors);
            int colorCount = colors.Count;

            // Специальный случай для алгоритмической серой палитры.
            if (palette.Name == "Стандартный серый")
            {
                return (smoothIter) =>
                {
                    if (smoothIter >= _fractalEngine.MaxIterations) return Color.Black;
                    if (smoothIter < 0) smoothIter = 0;

                    double logMax = Math.Log(_fractalEngine.MaxIterations + 1);
                    if (logMax <= 0) return Color.Black;

                    double tLog = Math.Log(smoothIter + 1) / logMax;

                    int gray_level = (int)(255.0 * (1.0 - tLog));
                    gray_level = Math.Max(0, Math.Min(255, gray_level));

                    Color baseColor = Color.FromArgb(gray_level, gray_level, gray_level);
                    return ColorCorrection.ApplyGamma(baseColor, gamma);
                };
            }

            // Общий случай для всех остальных палитр.
            if (effectiveMaxColorIterations <= 0)
            {
                return (smoothIter) => Color.Black;
            }

            if (colorCount == 0) return (smoothIter) => Color.Black;
            if (colorCount == 1) return (smoothIter) => (smoothIter >= _fractalEngine.MaxIterations) ? Color.Black : ColorCorrection.ApplyGamma(colors[0], gamma);

            return (smoothIter) =>
            {
                if (smoothIter >= _fractalEngine.MaxIterations) return Color.Black;
                if (smoothIter < 0) smoothIter = 0;

                double cyclicIter = smoothIter % effectiveMaxColorIterations;

                double t = cyclicIter / (double)effectiveMaxColorIterations;
                t = Math.Max(0.0, Math.Min(1.0, t));

                double scaledT = t * (colorCount - 1);
                int index1 = (int)Math.Floor(scaledT);
                int index2 = Math.Min(index1 + 1, colorCount - 1);
                double localT = scaledT - index1;

                Color baseColor = LerpColor(colors[index1], colors[index2], localT);
                return ColorCorrection.ApplyGamma(baseColor, gamma);
            };
        }

        /// <summary>
        /// Генерирует уникальную "подпись" для палитры на основе ее параметров.
        /// </summary>
        private string GeneratePaletteSignature(Palette palette, int maxIterationsForAlignment)
        {
            var sb = new StringBuilder();
            sb.Append(palette.Name);
            sb.Append(':');
            foreach (var color in palette.Colors) sb.Append(color.ToArgb().ToString("X8"));
            sb.Append(':');
            sb.Append(palette.IsGradient);
            sb.Append(':');
            sb.Append(palette.Gamma.ToString("F2"));
            sb.Append(':');
            int effectiveMaxColorIterations = palette.AlignWithRenderIterations ? maxIterationsForAlignment : palette.MaxColorIterations;
            sb.Append(effectiveMaxColorIterations);
            return sb.ToString();
        }

        /// <summary>
        /// Применяет активную цветовую палитру к движку рендеринга.
        /// </summary>
        private void ApplyActivePalette()
        {
            if (_fractalEngine == null || _paletteManager.ActivePalette == null) return;

            var activePalette = _paletteManager.ActivePalette;
            int effectiveMaxColorIterations = activePalette.AlignWithRenderIterations ? _fractalEngine.MaxIterations : activePalette.MaxColorIterations;

            _fractalEngine.MaxColorIterations = effectiveMaxColorIterations;

            _fractalEngine.SmoothPalette = GenerateSmoothPaletteFunction(activePalette, effectiveMaxColorIterations);

            string newSignature = GeneratePaletteSignature(activePalette, _fractalEngine.MaxIterations);
            if (_gammaCorrectedPaletteCache == null || newSignature != _paletteCacheSignature)
            {
                _paletteCacheSignature = newSignature;
                var paletteGeneratorFunc = GenerateDiscretePaletteFunction(activePalette);
                _gammaCorrectedPaletteCache = new Color[effectiveMaxColorIterations + 1];
                for (int i = 0; i <= effectiveMaxColorIterations; i++)
                {
                    _gammaCorrectedPaletteCache[i] = paletteGeneratorFunc(i, _fractalEngine.MaxIterations, effectiveMaxColorIterations);
                }
            }

            _fractalEngine.Palette = (iter, maxIter, maxColorIter) =>
            {
                if (iter == maxIter) return Color.Black;
                int index = Math.Min(iter, _gammaCorrectedPaletteCache.Length - 1);
                return _gammaCorrectedPaletteCache[index];
            };
        }

        /// <summary>
        /// Генерирует функцию окрашивания на основе заданной палитры.
        /// </summary>
        private Func<int, int, int, Color> GenerateDiscretePaletteFunction(Palette palette)
        {
            double gamma = palette.Gamma;
            var colors = new List<Color>(palette.Colors);
            bool isGradient = palette.IsGradient;
            int colorCount = colors.Count;

            if (palette.Name == "Стандартный серый")
            {
                return (iter, maxIter, maxColorIter) =>
                {
                    if (iter == maxIter) return Color.Black;
                    double logMax = Math.Log(maxColorIter + 1);
                    if (logMax == 0) return Color.Black;
                    double tLog = Math.Log(Math.Min(iter, maxColorIter) + 1) / logMax;
                    int cVal = (int)(255.0 * (1 - tLog));
                    return ColorCorrection.ApplyGamma(Color.FromArgb(cVal, cVal, cVal), gamma);
                };
            }
            if (colorCount == 0) return (i, m, mc) => Color.Black;
            if (colorCount == 1) return (iter, max, clrMax) => ColorCorrection.ApplyGamma((iter == max) ? Color.Black : colors[0], gamma);

            return (iter, maxIter, maxColorIter) =>
            {
                if (iter == maxIter) return Color.Black;
                double normalizedIter = maxColorIter > 0 ? (double)Math.Min(iter, maxColorIter) / maxColorIter : 0;
                Color baseColor;
                if (isGradient)
                {
                    double scaledT = normalizedIter * (colorCount - 1);
                    int index1 = (int)Math.Floor(scaledT);
                    int index2 = Math.Min(index1 + 1, colorCount - 1);
                    double localT = scaledT - index1;
                    baseColor = LerpColor(colors[index1], colors[index2], localT);
                }
                else
                {
                    int colorIndex = (int)(normalizedIter * colorCount);
                    if (colorIndex >= colorCount) colorIndex = colorCount - 1;
                    baseColor = colors[colorIndex];
                }
                return ColorCorrection.ApplyGamma(baseColor, gamma);
            };
        }

        /// <summary>
        /// Выполняет линейную интерполяцию между двумя цветами.
        /// </summary>
        private Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// Обрабатывает событие применения новой палитры из формы конфигурации.
        /// </summary>
        private void OnPaletteApplied(object sender, EventArgs e)
        {
            UpdateEngineParameters();
            ScheduleRender();
        }
        #endregion

        #region Form Lifecycle
        /// <summary>
        /// Обрабатывает событие загрузки формы.
        /// </summary>
        private void FractalPhoenixForm_Load(object sender, EventArgs e)
        {
            _baseTitle = this.Text;
            _paletteManager = new PaletteManager();
            _fractalEngine = new PhoenixEngine();
            _renderDebounceTimer = new System.Windows.Forms.Timer { Interval = 350 };
            _renderDebounceTimer.Tick += RenderDebounceTimer_Tick;
            _renderVisualizer = new RenderVisualizerComponent(TILE_SIZE);
            _renderVisualizer.NeedsRedraw += OnVisualizerNeedsRedraw;

            InitializeControls();
            InitializeEventHandlers();
            btnToggleControls.Text = "✕";
            UpdateToggleControlsPosition();

            _centerX = 0.0m; _centerY = 0.0m;
            _renderedCenterX = _centerX; _renderedCenterY = _centerY;
            _renderedZoom = _zoom;

            UpdateEngineParameters();
            ScheduleRender();
            ClearInitialControlSelection();
        }

        /// <summary>
        /// Обрабатывает событие закрытия формы, освобождая ресурсы.
        /// </summary>
        private void FractalPhoenixForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            _renderDebounceTimer?.Stop();
            _renderDebounceTimer?.Dispose();
            _previewRenderCts?.Cancel();
            _previewRenderCts?.Dispose();
            lock (_bitmapLock)
            {
                _previewBitmap?.Dispose(); _previewBitmap = null;
                _currentRenderingBitmap?.Dispose(); _currentRenderingBitmap = null;
            }
            if (_renderVisualizer != null)
            {
                _renderVisualizer.NeedsRedraw -= OnVisualizerNeedsRedraw;
                _renderVisualizer.Dispose();
            }
            _colorConfigForm?.Close();
            _colorConfigForm?.Dispose();
        }
        #endregion

        #region ISaveLoadCapableFractal Implementation

        /// <summary>
        /// Обрабатывает нажатие кнопки, открывая диалог сохранения/загрузки состояний.
        /// </summary>
        private void btnStateManager_Click(object sender, EventArgs e)
        {
            using (var dialog = new SaveLoadDialogForm(this))
            {
                dialog.ShowDialog(this);
            }
        }
        /// <summary>
        /// Уникальный идентификатор типа фрактала.
        /// </summary>
        public string FractalTypeIdentifier => "Phoenix";
        /// <summary>
        /// Тип конкретного класса состояния сохранения для этого фрактала.
        /// </summary>
        public Type ConcreteSaveStateType => typeof(PhoenixSaveState);

        /// <summary>
        /// Класс для хранения параметров предпросмотра фрактала Феникс.
        /// </summary>
        public class PhoenixPreviewParams
        {
            public decimal CenterX { get; set; }
            public decimal CenterY { get; set; }
            public decimal Zoom { get; set; }
            public int Iterations { get; set; }
            public string PaletteName { get; set; }
            public decimal Threshold { get; set; }
            public decimal C1Re { get; set; }
            public decimal C1Im { get; set; }
            public decimal C2Re { get; set; }
            public decimal C2Im { get; set; }
            public bool UseSmoothColoring { get; set; }
        }

        /// <summary>
        /// Собирает текущее состояние фрактала для сохранения.
        /// </summary>
        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new PhoenixSaveState(this.FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                Threshold = nudThreshold.Value,
                Iterations = (int)nudIterations.Value,
                PaletteName = _paletteManager.ActivePalette?.Name ?? "Стандартный серый",
                C1Re = nudC1Re.Value,
                C1Im = nudC1Im.Value,
                C2Re = nudC2Re.Value,
                C2Im = nudC2Im.Value
            };
            var previewParams = new PhoenixPreviewParams
            {
                CenterX = state.CenterX,
                CenterY = state.CenterY,
                Zoom = state.Zoom,
                Iterations = state.Iterations,
                PaletteName = state.PaletteName,
                Threshold = state.Threshold,
                C1Re = state.C1Re,
                C1Im = state.C1Im,
                C2Re = state.C2Re,
                C2Im = state.C2Im,
                UseSmoothColoring = IsSmoothColoringEnabled()
            };
            state.PreviewParametersJson = JsonSerializer.Serialize(previewParams);
            return state;
        }

        /// <summary>
        /// Загружает состояние фрактала из объекта сохранения.
        /// </summary>
        public void LoadState(FractalSaveStateBase stateBase)
        {
            if (stateBase is PhoenixSaveState state)
            {
                _isRenderingPreview = false;
                _previewRenderCts?.Cancel();
                _renderDebounceTimer.Stop();

                _centerX = state.CenterX; _centerY = state.CenterY; _zoom = state.Zoom;
                nudZoom.Value = state.Zoom; nudThreshold.Value = state.Threshold;
                nudIterations.Value = state.Iterations; nudC1Re.Value = state.C1Re;
                nudC1Im.Value = state.C1Im; nudC2Re.Value = state.C2Re; nudC2Im.Value = state.C2Im;

                var paletteToLoad = _paletteManager.Palettes.FirstOrDefault(p => p.Name == state.PaletteName);
                if (paletteToLoad != null) _paletteManager.ActivePalette = paletteToLoad;

                lock (_bitmapLock)
                {
                    _previewBitmap?.Dispose(); _previewBitmap = null;
                    _currentRenderingBitmap?.Dispose(); _currentRenderingBitmap = null;
                }
                _renderedCenterX = _centerX; _renderedCenterY = _centerY; _renderedZoom = _zoom;
                UpdateEngineParameters();
                ScheduleRender();
            }
            else
            {
                MessageBox.Show("Несовместимый тип состояния для загрузки.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Асинхронно рендерит тайл предпросмотра для заданного состояния.
        /// </summary>
        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(state.PreviewParametersJson)) return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];
                PhoenixPreviewParams previewParams;
                try { previewParams = JsonSerializer.Deserialize<PhoenixPreviewParams>(state.PreviewParametersJson); }
                catch { return new byte[tile.Bounds.Width * tile.Bounds.Height * 4]; }

                var previewEngine = new PhoenixEngine();
                previewEngine.CenterX = previewParams.CenterX;
                previewEngine.CenterY = previewParams.CenterY;
                if (previewParams.Zoom == 0) previewParams.Zoom = 0.001m;
                previewEngine.Scale = BASE_SCALE / previewParams.Zoom;
                previewEngine.MaxIterations = previewParams.Iterations;
                previewEngine.ThresholdSquared = previewParams.Threshold * previewParams.Threshold;
                previewEngine.C1 = new ComplexDecimal(previewParams.C1Re, previewParams.C1Im);
                previewEngine.C2 = new ComplexDecimal(previewParams.C2Re, previewParams.C2Im);
                var paletteForPreview = _paletteManager.Palettes.FirstOrDefault(p => p.Name == previewParams.PaletteName) ?? _paletteManager.Palettes.First();
                int effectiveMaxColorIterations = paletteForPreview.AlignWithRenderIterations ? previewEngine.MaxIterations : paletteForPreview.MaxColorIterations;
                previewEngine.MaxColorIterations = effectiveMaxColorIterations;

                previewEngine.UseSmoothColoring = previewParams.UseSmoothColoring;

                if (previewEngine.UseSmoothColoring)
                {
                    previewEngine.SmoothPalette = GenerateSmoothPaletteFunction(paletteForPreview, effectiveMaxColorIterations);
                }
                else
                {
                    previewEngine.Palette = GenerateDiscretePaletteFunction(paletteForPreview);
                }
                return previewEngine.RenderSingleTile(tile, totalWidth, totalHeight, out _);
            });
        }

        /// <summary>
        /// Рендерит полное изображение предпросмотра для заданного состояния.
        /// </summary>
        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (string.IsNullOrEmpty(state.PreviewParametersJson))
            {
                var bmpError = new Bitmap(previewWidth, previewHeight);
                using (var g = Graphics.FromImage(bmpError)) { g.Clear(Color.DarkGray); TextRenderer.DrawText(g, "Нет данных", Font, new Rectangle(0, 0, previewWidth, previewHeight), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
                return bmpError;
            }
            PhoenixPreviewParams previewParams;
            try { previewParams = JsonSerializer.Deserialize<PhoenixPreviewParams>(state.PreviewParametersJson); }
            catch (Exception)
            {
                var bmpError = new Bitmap(previewWidth, previewHeight);
                using (var g = Graphics.FromImage(bmpError)) { g.Clear(Color.DarkRed); TextRenderer.DrawText(g, "Ошибка параметров", Font, new Rectangle(0, 0, previewWidth, previewHeight), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
                return bmpError;
            }
            var previewEngine = new PhoenixEngine();
            previewEngine.CenterX = previewParams.CenterX;
            previewEngine.CenterY = previewParams.CenterY;
            if (previewParams.Zoom == 0) previewParams.Zoom = 0.001m;
            previewEngine.Scale = BASE_SCALE / previewParams.Zoom;
            previewEngine.MaxIterations = previewParams.Iterations;
            previewEngine.ThresholdSquared = previewParams.Threshold * previewParams.Threshold;
            previewEngine.C1 = new ComplexDecimal(previewParams.C1Re, previewParams.C1Im);
            previewEngine.C2 = new ComplexDecimal(previewParams.C2Re, previewParams.C2Im);
            var paletteForPreview = _paletteManager.Palettes.FirstOrDefault(p => p.Name == previewParams.PaletteName) ?? _paletteManager.Palettes.First();
            int effectiveMaxColorIterations = paletteForPreview.AlignWithRenderIterations ? previewEngine.MaxIterations : paletteForPreview.MaxColorIterations;
            previewEngine.MaxColorIterations = effectiveMaxColorIterations;

            previewEngine.UseSmoothColoring = previewParams.UseSmoothColoring;

            if (previewEngine.UseSmoothColoring)
            {
                previewEngine.SmoothPalette = GenerateSmoothPaletteFunction(paletteForPreview, effectiveMaxColorIterations);
            }
            else
            {
                previewEngine.Palette = GenerateDiscretePaletteFunction(paletteForPreview);
            }
            return previewEngine.RenderToBitmap(previewWidth, previewHeight, 1, progress => { });
        }

        /// <summary>
        /// Загружает все сохранения для данного типа фрактала.
        /// </summary>
        public List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            var specificSaves = SaveFileManager.LoadSaves<PhoenixSaveState>(this.FractalTypeIdentifier);
            return specificSaves.Cast<FractalSaveStateBase>().ToList();
        }

        /// <summary>
        /// Сохраняет все состояния для данного типа фрактала.
        /// </summary>
        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            var specificSaves = saves.Cast<PhoenixSaveState>().ToList();
            SaveFileManager.SaveSaves(this.FractalTypeIdentifier, specificSaves);
        }
        #endregion

        #region IHighResRenderable Implementation

        /// <summary>
        /// Получает текущее состояние для рендеринга в высоком разрешении.
        /// </summary>
        public HighResRenderState GetRenderState()
        {
            string c1ReStr = nudC1Re.Value.ToString("F6", CultureInfo.InvariantCulture).Replace(".", "_");
            string c1ImStr = nudC1Im.Value.ToString("F6", CultureInfo.InvariantCulture).Replace(".", "_");
            string fileNameDetails = $"phoenix_P{c1ReStr}_Q{c1ImStr}";

            var state = new HighResRenderState
            {
                EngineType = this.FractalTypeIdentifier,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                BaseScale = BASE_SCALE,
                Iterations = (int)nudIterations.Value,
                Threshold = nudThreshold.Value,
                ActivePaletteName = _paletteManager.ActivePalette?.Name ?? "Стандартный серый",
                FileNameDetails = fileNameDetails,
                JuliaC = new ComplexDecimal(nudC1Re.Value, nudC1Im.Value),
                UseSmoothColoring = IsSmoothColoringEnabled()
            };

            return state;
        }

        /// <summary>
        /// Создает и настраивает экземпляр движка Phoenix на основе состояния рендеринга.
        /// </summary>
        private PhoenixEngine CreateEngineFromState(HighResRenderState state, bool forPreview)
        {
            var engine = new PhoenixEngine();

            engine.MaxIterations = forPreview ? Math.Min(state.Iterations, 150) : state.Iterations;
            engine.ThresholdSquared = state.Threshold * state.Threshold;
            engine.CenterX = state.CenterX;
            engine.CenterY = state.CenterY;
            engine.Scale = state.BaseScale / state.Zoom;
            engine.C1 = state.JuliaC.Value;
            engine.C2 = ComplexDecimal.Zero;
            engine.UseSmoothColoring = state.UseSmoothColoring;

            var paletteForRender = _paletteManager.Palettes.FirstOrDefault(p => p.Name == state.ActivePaletteName) ?? _paletteManager.Palettes.First();
            int effectiveMaxColorIterations = paletteForRender.AlignWithRenderIterations ? engine.MaxIterations : paletteForRender.MaxColorIterations;
            engine.MaxColorIterations = effectiveMaxColorIterations;

            engine.SmoothPalette = GenerateSmoothPaletteFunction(paletteForRender, effectiveMaxColorIterations);
            engine.Palette = GenerateDiscretePaletteFunction(paletteForRender);

            return engine;
        }

        /// <summary>
        /// Асинхронно рендерит изображение в высоком разрешении.
        /// </summary>
        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            _isHighResRendering = true;
            try
            {
                PhoenixEngine renderEngine = CreateEngineFromState(state, forPreview: false);
                int threadCount = GetThreadCount();

                Action<int> progressCallback = p => progress.Report(new RenderProgress { Percentage = p, Status = "Рендеринг..." });

                Bitmap highResBitmap = await renderEngine.RenderToBitmapSSAA(
                    width, height, threadCount, progressCallback, ssaaFactor, cancellationToken);

                return highResBitmap;
            }
            finally
            {
                _isHighResRendering = false;
            }
        }

        /// <summary>
        /// Рендерит предпросмотр для окна рендеринга в высоком разрешении.
        /// </summary>
        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            var engine = CreateEngineFromState(state, forPreview: true);
            return engine.RenderToBitmap(previewWidth, previewHeight, 1, _ => { }, CancellationToken.None);
        }
        #endregion
    }
}
