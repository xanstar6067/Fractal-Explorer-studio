using FractalExplorer.Engines;
using FractalExplorer.Forms.Other;
using FractalExplorer.Resources;
using FractalExplorer.Utilities;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

using FractalExplorer.Utilities.Theme;
using FractalExplorer.Utilities.UI;
namespace FractalExplorer.Forms.Fractals
{
    /// <summary>
    /// Форма для отображения и взаимодействия с фракталом, основанным на гипотезе Коллатца.
    /// Реализует интерфейс <see cref="IHighResRenderable"/> для сохранения изображений в высоком разрешении.
    /// Реализует интерфейс <see cref="ISaveLoadCapableFractal"/> для сохранения и загрузки состояний фрактала.
    /// </summary>
    public partial class FractalCollatzForm : Form, IHighResRenderable, ISaveLoadCapableFractal
    {
        #region Fields

        /// <summary>
        /// Движок для рендеринга фрактала Коллатца.
        /// </summary>
        private FractalCollatzEngine _fractalEngine;

        /// <summary>
        /// Компонент для визуализации процесса рендеринга (например, отображения тайлов).
        /// </summary>
        private RenderVisualizerComponent _renderVisualizer;
        /// <summary>
        /// Менеджер цветовых палитр для фракталов.
        /// </summary>
        private PaletteManager _paletteManager;
        /// <summary>
        /// Кэш для цветов палитры с уже примененной гамма-коррекцией.
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
        /// Размер тайла для рендеринга в пикселях.
        /// </summary>
        private const int TILE_SIZE = 16;
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
        /// Размер отступа кнопки переключения панели.
        /// </summary>
        private const int ToggleButtonMargin = 12;
        /// <summary>
        /// Базовый масштаб для вычислений.
        /// </summary>
        private const decimal BASE_SCALE = 4.0m;
        #endregion

        #region Constructor
        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="FractalCollatzForm"/>.
        /// </summary>
        public FractalCollatzForm()
        {
            InitializeComponent();
            KeyPreview = true;
            ThemeManager.RegisterForm(this);
            Text = "Фрактал Коллатца";
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
            nudIterations.Value = 150;

            nudThreshold.Minimum = 2m;
            nudThreshold.Maximum = 10000m;
            nudThreshold.DecimalPlaces = 1;
            nudThreshold.Increment = 1m;
            nudThreshold.Value = 100m;

            nudZoom.DecimalPlaces = 15;
            nudZoom.Increment = 0.1m;
            nudZoom.Minimum = 0.000000000000001m;

            //nudZoom.Maximum = decimal.MaxValue;
            nudZoom.Maximum = 1434648375m;
            _zoom = BASE_SCALE / 4.0m;
            nudZoom.Value = _zoom;

            cbSSAA.Items.Add("Выкл (1x)");
            cbSSAA.Items.Add("Низкое (2x)");
            cbSSAA.Items.Add("Высокое (4x)");
            cbSSAA.SelectedItem = "Выкл (1x)";

            cbSmooth.Items.Clear();
            cbSmooth.Items.AddRange(new object[] { "Дискретно", "Плавно" });
            cbSmooth.SelectedIndex = 1;

            // Инициализация новых элементов управления для вариаций
            cbVariation.DataSource = Enum.GetValues(typeof(CollatzVariation));
            cbVariation.SelectedItem = CollatzVariation.Standard;

            nudPParameter.Value = 3.0m;
            nudPParameter.Minimum = -100m;
            nudPParameter.Maximum = 100m;
            nudPParameter.DecimalPlaces = 2;
            nudPParameter.Increment = 0.01m;

            // Скрываем поле для параметра P, так как оно не нужно для стандартной вариации
            TogglePParameterControl(false);
        }

        /// <summary>
        /// Инициализирует обработчики событий для различных элементов управления UI.
        /// </summary>
        private void InitializeEventHandlers()
        {
            nudIterations.ValueChanged += ParamControl_Changed;
            nudThreshold.ValueChanged += ParamControl_Changed;
            cbThreads.SelectedIndexChanged += ParamControl_Changed;
            nudZoom.ValueChanged += ParamControl_Changed;
            cbSmooth.SelectedIndexChanged += ParamControl_Changed;
            cbSSAA.SelectedIndexChanged += ParamControl_Changed;

            // Добавляем обработчики для новых элементов
            cbVariation.SelectedIndexChanged += Variation_Changed;
            nudPParameter.ValueChanged += ParamControl_Changed;

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
        /// Обрабатывает изменение значения в элементах управления параметрами, планируя перерисовку.
        /// </summary>
        private void ParamControl_Changed(object sender, EventArgs e)
        {
            if (_isHighResRendering) return;
            if (sender == nudZoom && nudZoom.Value != _zoom)
            {
                _zoom = Math.Max(nudZoom.Minimum, Math.Min(nudZoom.Maximum, nudZoom.Value));
            }
            ScheduleRender();
        }

        /// <summary>
        /// Обрабатывает изменение выбранной вариации фрактала.
        /// </summary>
        private void Variation_Changed(object sender, EventArgs e)
        {
            if (cbVariation.SelectedItem is CollatzVariation selectedVariation)
            {
                // Показываем или скрываем поле для параметра 'P' в зависимости от выбора
                TogglePParameterControl(selectedVariation == CollatzVariation.GeneralizedP);
            }
            ScheduleRender();
        }

        /// <summary>
        /// Запускает рендеринг предпросмотра по нажатию кнопки.
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
        /// Открывает окно для настройки цветовых палитр.
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

        private void ClearInitialControlSelection()
        {
            BeginInvoke(new Action(() => ActiveControl = null));
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
                canvas.Cursor = Cursors.SizeAll;
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

            lock (_bitmapLock)
            {
                if (_previewBitmap != null)
                {
                    // Если позиция и зум не менялись, просто отрисовываем готовый битмап
                    if (_renderedCenterX == _centerX && _renderedCenterY == _centerY && _renderedZoom == _zoom)
                    {
                        e.Graphics.DrawImageUnscaled(_previewBitmap, Point.Empty);
                    }
                    else // Иначе, нужно трансформировать старый битмап для создания иллюзии плавности
                    {
                        // Проверяем, что значения зума корректны
                        if (_renderedZoom > 0 && _zoom > 0 && _previewBitmap.Width > 0 && _previewBitmap.Height > 0)
                        {
                            // Эта логика численно устойчива и не приводит к сбоям при экстремальном зуме.
                            // Она вычисляет трансформацию относительно стабильной системы координат последнего
                            // завершенного рендера (_renderedZoom), а не текущей (_zoom), что предотвращает
                            // деление на исчезающе малые числа.

                            // 1. Коэффициент масштабирования: во сколько раз изменился зум
                            decimal scaleRatio = _zoom / _renderedZoom;

                            // 2. "Единиц на пиксель" для ПОСЛЕДНЕГО завершенного рендера. Эта величина стабильна.
                            decimal unitsPerPixelRendered = (BASE_SCALE / _renderedZoom) / _previewBitmap.Width;

                            // 3. Находим, где на старом битмапе (_previewBitmap) теперь должен быть центр нового вида.
                            //    Вычисляем смещение нового центра (_centerX) относительно старого (_renderedCenterX)
                            //    и переводим его в пиксели старого битмапа.
                            decimal centerPxOnOldBitmap = (_previewBitmap.Width / 2.0m) + (_centerX - _renderedCenterX) / unitsPerPixelRendered;
                            decimal centerPyOnOldBitmap = (_previewBitmap.Height / 2.0m) - (_centerY - _renderedCenterY) / unitsPerPixelRendered; // Y инвертирован

                            // 4. Вычисляем новые размеры отмасштабированного битмапа
                            decimal newWidthPixels = _previewBitmap.Width * scaleRatio;
                            decimal newHeightPixels = _previewBitmap.Height * scaleRatio;

                            // 5. Вычисляем координаты (смещение) верхнего левого угла отмасштабированного битмапа.
                            //    Цель — чтобы точка (centerPxOnOldBitmap, centerPyOnOldBitmap) оказалась в центре холста.
                            decimal offsetXPixels = (canvas.Width / 2.0m) - (centerPxOnOldBitmap * scaleRatio);
                            decimal offsetYPixels = (canvas.Height / 2.0m) - (centerPyOnOldBitmap * scaleRatio);

                            // 6. Отрисовываем оттрансформированное изображение
                            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            PointF destPoint1 = new PointF((float)offsetXPixels, (float)offsetYPixels);
                            PointF destPoint2 = new PointF((float)(offsetXPixels + newWidthPixels), (float)offsetYPixels);
                            PointF destPoint3 = new PointF((float)offsetXPixels, (float)(offsetYPixels + newHeightPixels));

                            try
                            {
                                // Оставляем try-catch на случай, если даже после всех расчетов
                                // итоговые float-значения окажутся невалидными (что маловероятно с этой логикой).
                                e.Graphics.DrawImage(_previewBitmap, new PointF[] { destPoint1, destPoint2, destPoint3 });
                            }
                            catch (Exception) { /* Игнорировать редкие сбои отрисовки GDI+ */ }
                        }
                    }
                }

                // Отрисовываем поверх всего текущий рендеринг (если он идет)
                if (_currentRenderingBitmap != null) e.Graphics.DrawImageUnscaled(_currentRenderingBitmap, Point.Empty);
            }

            // Отрисовываем визуализацию тайлов
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
        /// Создает копию движка рендеринга с текущими параметрами.
        /// </summary>
        /// <returns>Новый экземпляр <see cref="FractalCollatzEngine"/> с скопированными параметрами.</returns>
        private FractalCollatzEngine CreateEngineCopy()
        {
            var engineCopy = new FractalCollatzEngine
            {
                MaxIterations = _fractalEngine.MaxIterations,
                ThresholdSquared = _fractalEngine.ThresholdSquared,
                CenterX = _fractalEngine.CenterX,
                CenterY = _fractalEngine.CenterY,
                Scale = _fractalEngine.Scale,
                UseSmoothColoring = _fractalEngine.UseSmoothColoring,
                Palette = _fractalEngine.Palette,
                SmoothPalette = _fractalEngine.SmoothPalette,
                MaxColorIterations = _fractalEngine.MaxColorIterations,
                // Копируем новые свойства вариаций
                Variation = _fractalEngine.Variation,
                P_Parameter = _fractalEngine.P_Parameter
            };
            return engineCopy;
        }

        /// <summary>
        /// Асинхронно запускает рендеринг предпросмотра с использованием суперсэмплинга (SSAA).
        /// </summary>
        /// <param name="ssaaFactor">Фактор суперсэмплинга (2, 4 и т.д.).</param>
        private async Task StartPreviewRenderSSAA(int ssaaFactor)
        {
            if (canvas.Width <= 0 || canvas.Height <= 0) return;

            int currentWidth = canvas.Width;
            int currentHeight = canvas.Height;

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

            var renderEngineCopy = CreateEngineCopy();

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
        /// Запускает асинхронный процесс рендеринга предпросмотра фрактала (без SSAA).
        /// </summary>
        private async Task StartPreviewRender()
        {
            if (canvas.Width <= 0 || canvas.Height <= 0) return;

            int currentWidth = canvas.Width;
            int currentHeight = canvas.Height;

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

            var renderEngineCopy = CreateEngineCopy();

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
        /// Переключает видимость элементов управления для параметра 'P'.
        /// </summary>
        /// <param name="isVisible">True, чтобы сделать элементы видимыми, иначе False.</param>
        private void TogglePParameterControl(bool isVisible)
        {
            if (lblPParameter.InvokeRequired)
            {
                lblPParameter.Invoke((Action)(() => TogglePParameterControl(isVisible)));
            }
            else
            {
                lblPParameter.Visible = isVisible;
                nudPParameter.Visible = isVisible;
            }
        }

        /// <summary>
        /// Завершает текущий рендеринг и "запекает" его результат в основной битмап предпросмотра.
        /// </summary>
        private void CommitAndBakePreview()
        {
            lock (_bitmapLock) { if (!_isRenderingPreview || _currentRenderingBitmap == null) return; }
            _previewRenderCts?.Cancel();

            lock (_bitmapLock)
            {
                if (_currentRenderingBitmap == null || canvas.Width <= 0 || canvas.Height <= 0) return;
                var bakedBitmap = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format24bppRgb);

                using (var g = Graphics.FromImage(bakedBitmap))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = InterpolationMode.Bilinear;

                    if (_previewBitmap != null)
                    {
                        if (_renderedZoom > 0 && _zoom > 0 && _previewBitmap.Width > 0 && _previewBitmap.Height > 0)
                        {
                            // Используется та же самая численно устойчивая логика, что и в Canvas_Paint

                            decimal scaleRatio = _zoom / _renderedZoom;
                            decimal unitsPerPixelRendered = (BASE_SCALE / _renderedZoom) / _previewBitmap.Width;

                            decimal centerPxOnOldBitmap = (_previewBitmap.Width / 2.0m) + (_centerX - _renderedCenterX) / unitsPerPixelRendered;
                            decimal centerPyOnOldBitmap = (_previewBitmap.Height / 2.0m) - (_centerY - _renderedCenterY) / unitsPerPixelRendered;

                            decimal newWidthPixels = _previewBitmap.Width * scaleRatio;
                            decimal newHeightPixels = _previewBitmap.Height * scaleRatio;

                            decimal offsetXPixels = (canvas.Width / 2.0m) - (centerPxOnOldBitmap * scaleRatio);
                            decimal offsetYPixels = (canvas.Height / 2.0m) - (centerPyOnOldBitmap * scaleRatio);

                            PointF destPoint1 = new PointF((float)offsetXPixels, (float)offsetYPixels);
                            PointF destPoint2 = new PointF((float)(offsetXPixels + newWidthPixels), (float)offsetYPixels);
                            PointF destPoint3 = new PointF((float)offsetXPixels, (float)(offsetYPixels + newHeightPixels));

                            try
                            {
                                g.DrawImage(_previewBitmap, new PointF[] { destPoint1, destPoint2, destPoint3 });
                            }
                            catch (Exception) { /* Игнорировать */ }
                        }
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
            }
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
            _fractalEngine.UseSmoothColoring = IsSmoothColoringEnabled();

            // Обновляем движок параметрами вариаций
            if (cbVariation.InvokeRequired)
            {
                cbVariation.Invoke((Action)(() => _fractalEngine.Variation = (CollatzVariation)cbVariation.SelectedItem));
            }
            else
            {
                _fractalEngine.Variation = (CollatzVariation)cbVariation.SelectedItem;
            }

            if (nudPParameter.InvokeRequired)
            {
                nudPParameter.Invoke((Action)(() => _fractalEngine.P_Parameter = nudPParameter.Value));
            }
            else
            {
                _fractalEngine.P_Parameter = nudPParameter.Value;
            }

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

            if (effectiveMaxColorIterations <= 0) return (smoothIter) => Color.Black;
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
            var sb = new System.Text.StringBuilder();
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
        /// Генерирует функцию дискретного окрашивания на основе заданной палитры.
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
        private void FractalCollatzForm_Load(object sender, EventArgs e)
        {
            _baseTitle = this.Text;
            _paletteManager = new PaletteManager();
            _fractalEngine = new FractalCollatzEngine();
            _renderDebounceTimer = new System.Windows.Forms.Timer { Interval = 350 };
            _renderDebounceTimer.Tick += RenderDebounceTimer_Tick;
            _renderVisualizer = new RenderVisualizerComponent(TILE_SIZE);
            _renderVisualizer.NeedsRedraw += OnVisualizerNeedsRedraw;

            InitializeControls();
            InitializeEventHandlers();

            _centerX = 0.0m;
            _centerY = 0.0m;
            _renderedCenterX = _centerX;
            _renderedCenterY = _centerY;
            _renderedZoom = _zoom;

            UpdateEngineParameters();
            UpdateToggleControlsPosition();
            ClearInitialControlSelection();
            ScheduleRender();
        }

        /// <summary>
        /// Обрабатывает событие закрытия формы, освобождая ресурсы.
        /// </summary>
        private void FractalCollatzForm_FormClosed(object sender, FormClosedEventArgs e)
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

        #region IHighResRenderable Implementation

        /// <summary>
        /// Получает текущее состояние фрактала для рендеринга в высоком разрешении.
        /// </summary>
        /// <returns>Объект <see cref="HighResRenderState"/> с текущими параметрами.</returns>
        public HighResRenderState GetRenderState()
        {
            var state = new HighResRenderState
            {
                EngineType = "Collatz",
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                BaseScale = BASE_SCALE,
                Scale = BASE_SCALE / _zoom,
                Iterations = (int)nudIterations.Value,
                Threshold = nudThreshold.Value,
                ActivePaletteName = _paletteManager.ActivePalette?.Name ?? "Стандартный серый",
                FileNameDetails = "collatz",
                UseSmoothColoring = IsSmoothColoringEnabled(),

                // Добавляем специфичные для Коллатца параметры
                Variation = (CollatzVariation)cbVariation.SelectedItem,
                P_Parameter = nudPParameter.Value
            };

            return state;
        }

        /// <summary>
        /// Создает и настраивает экземпляр движка Collatz на основе состояния рендеринга.
        /// </summary>
        /// <param name="state">Состояние, из которого создается движок.</param>
        /// <param name="forPreview">Если true, могут применяться упрощенные параметры для ускорения рендеринга превью.</param>
        /// <returns>Настроенный экземпляр <see cref="FractalCollatzEngine"/>.</returns>
        private FractalCollatzEngine CreateEngineFromState(HighResRenderState state, bool forPreview)
        {
            var engine = new FractalCollatzEngine
            {
                MaxIterations = forPreview ? Math.Min(state.Iterations, 150) : state.Iterations,
                ThresholdSquared = state.Threshold * state.Threshold,
                CenterX = state.CenterX,
                CenterY = state.CenterY,
                Scale = state.Scale,
                UseSmoothColoring = state.UseSmoothColoring
            };

            // Извлекаем параметры вариаций из состояния
            if (state.Variation.HasValue)
            {
                engine.Variation = state.Variation.Value;
            }
            if (state.P_Parameter.HasValue)
            {
                engine.P_Parameter = state.P_Parameter.Value;
            }

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
        /// <param name="state">Состояние для рендеринга.</param>
        /// <param name="width">Ширина изображения.</param>
        /// <param name="height">Высота изображения.</param>
        /// <param name="ssaaFactor">Фактор суперсэмплинга.</param>
        /// <param name="progress">Объект для отчета о прогрессе.</param>
        /// <param name="cancellationToken">Токен для отмены операции.</param>
        /// <returns>Отрендеренное изображение в виде <see cref="Bitmap"/>.</returns>
        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            _isHighResRendering = true;
            try
            {
                FractalCollatzEngine renderEngine = CreateEngineFromState(state, forPreview: false);
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
        /// <param name="state">Состояние для рендеринга.</param>
        /// <param name="previewWidth">Ширина превью.</param>
        /// <param name="previewHeight">Высота превью.</param>
        /// <returns>Изображение превью в виде <see cref="Bitmap"/>.</returns>
        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            var engine = CreateEngineFromState(state, forPreview: true);
            return engine.RenderToBitmap(previewWidth, previewHeight, 1, _ => { }, CancellationToken.None);
        }
        #endregion

        #region ISaveLoadCapableFractal Implementation
        /// <summary>
        /// Уникальный идентификатор типа фрактала.
        /// </summary>
        public string FractalTypeIdentifier => "Collatz";

        /// <summary>
        /// Тип конкретного класса состояния сохранения для этого фрактала.
        /// </summary>
        public Type ConcreteSaveStateType => typeof(CollatzSaveState);

        /// <summary>
        /// Класс для хранения параметров предпросмотра фрактала Коллатца.
        /// Расширен для поддержки вариаций.
        /// </summary>
        public class CollatzPreviewParams
        {
            public decimal CenterX { get; set; }
            public decimal CenterY { get; set; }
            public decimal Zoom { get; set; }
            public int Iterations { get; set; }
            public string PaletteName { get; set; }
            public decimal Threshold { get; set; }
            public bool UseSmoothColoring { get; set; }
            /// <summary>
            /// Сохраненная вариация формулы.
            /// </summary>
            public CollatzVariation Variation { get; set; }
            /// <summary>
            /// Сохраненный параметр 'P'.
            /// </summary>
            public decimal P_Parameter { get; set; }
        }

        /// <summary>
        /// Собирает текущее состояние фрактала для сохранения.
        /// </summary>
        /// <param name="saveName">Имя, присвоенное сохранению.</param>
        /// <returns>Объект состояния фрактала, готовый к сериализации.</returns>
        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new CollatzSaveState(this.FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                Threshold = nudThreshold.Value,
                Iterations = (int)nudIterations.Value,
                PaletteName = _paletteManager.ActivePalette?.Name ?? "Стандартный серый",
                PreviewEngineType = this.FractalTypeIdentifier,
                // Сохраняем параметры вариаций
                Variation = (CollatzVariation)cbVariation.SelectedItem,
                P_Parameter = nudPParameter.Value
            };

            var previewParams = new CollatzPreviewParams
            {
                CenterX = state.CenterX,
                CenterY = state.CenterY,
                Zoom = state.Zoom,
                Iterations = state.Iterations,
                PaletteName = state.PaletteName,
                Threshold = state.Threshold,
                UseSmoothColoring = IsSmoothColoringEnabled(),
                // Добавляем параметры вариаций в JSON для предпросмотра
                Variation = state.Variation,
                P_Parameter = state.P_Parameter
            };
            state.PreviewParametersJson = JsonSerializer.Serialize(previewParams);
            return state;
        }

        /// <summary>
        /// Загружает состояние фрактала из объекта сохранения.
        /// </summary>
        /// <param name="stateBase">Базовый объект состояния для загрузки.</param>
        public void LoadState(FractalSaveStateBase stateBase)
        {
            if (stateBase is CollatzSaveState state)
            {
                _isRenderingPreview = false;
                _previewRenderCts?.Cancel();
                _renderDebounceTimer.Stop();

                _centerX = state.CenterX;
                _centerY = state.CenterY;
                _zoom = state.Zoom;
                if (nudZoom.Value != state.Zoom) nudZoom.Value = state.Zoom;
                if (nudThreshold.Value != state.Threshold) nudThreshold.Value = state.Threshold;
                if (nudIterations.Value != state.Iterations) nudIterations.Value = state.Iterations;

                // Загружаем и применяем параметры вариаций
                cbVariation.SelectedItem = state.Variation;
                nudPParameter.Value = state.P_Parameter;
                TogglePParameterControl(state.Variation == CollatzVariation.GeneralizedP);


                var paletteToLoad = _paletteManager.Palettes.FirstOrDefault(p => p.Name == state.PaletteName);
                if (paletteToLoad != null)
                {
                    _paletteManager.ActivePalette = paletteToLoad;
                }

                lock (_bitmapLock)
                {
                    _previewBitmap?.Dispose();
                    _previewBitmap = null;
                    _currentRenderingBitmap?.Dispose();
                    _currentRenderingBitmap = null;
                }
                _renderedCenterX = _centerX;
                _renderedCenterY = _centerY;
                _renderedZoom = _zoom;
                UpdateEngineParameters();
                ScheduleRender();
            }
            else
            {
                MessageBox.Show("Несовместимый тип состояния для загрузки.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Создает и настраивает экземпляр движка Collatz на основе параметров для превью.
        /// </summary>
        /// <param name="previewParams">Параметры для создания движка превью.</param>
        /// <returns>Настроенный экземпляр <see cref="FractalCollatzEngine"/>.</returns>
        private FractalCollatzEngine CreateEngineFromPreviewParams(CollatzPreviewParams previewParams)
        {
            var engine = new FractalCollatzEngine
            {
                MaxIterations = previewParams.Iterations,
                ThresholdSquared = previewParams.Threshold * previewParams.Threshold,
                CenterX = previewParams.CenterX,
                CenterY = previewParams.CenterY,
                UseSmoothColoring = previewParams.UseSmoothColoring,
                // Устанавливаем параметры вариаций для движка предпросмотра
                Variation = previewParams.Variation,
                P_Parameter = previewParams.P_Parameter
            };
            if (previewParams.Zoom == 0) previewParams.Zoom = 0.001m;
            engine.Scale = BASE_SCALE / previewParams.Zoom;

            var paletteForPreview = _paletteManager.Palettes.FirstOrDefault(p => p.Name == previewParams.PaletteName) ?? _paletteManager.Palettes.First();
            int effectiveMaxColorIterations = paletteForPreview.AlignWithRenderIterations ? engine.MaxIterations : paletteForPreview.MaxColorIterations;
            engine.MaxColorIterations = effectiveMaxColorIterations;

            if (engine.UseSmoothColoring)
            {
                engine.SmoothPalette = GenerateSmoothPaletteFunction(paletteForPreview, effectiveMaxColorIterations);
            }
            else
            {
                engine.Palette = GenerateDiscretePaletteFunction(paletteForPreview);
            }
            return engine;
        }

        /// <summary>
        /// Асинхронно рендерит тайл предпросмотра для заданного состояния.
        /// </summary>
        /// <param name="state">Состояние фрактала.</param>
        /// <param name="tile">Информация о тайле.</param>
        /// <param name="totalWidth">Общая ширина превью.</param>
        /// <param name="totalHeight">Общая высота превью.</param>
        /// <param name="tileSize">Размер тайла.</param>
        /// <returns>Массив байтов с данными тайла.</returns>
        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(state.PreviewParametersJson)) return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];
                CollatzPreviewParams previewParams;
                try
                {
                    previewParams = JsonSerializer.Deserialize<CollatzPreviewParams>(state.PreviewParametersJson);
                }
                catch
                {
                    return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];
                }

                var previewEngine = CreateEngineFromPreviewParams(previewParams);
                var paletteForPreview = _paletteManager.Palettes.FirstOrDefault(p => p.Name == previewParams.PaletteName) ?? _paletteManager.Palettes.First();
                previewEngine.Palette = GenerateDiscretePaletteFunction(paletteForPreview);

                return previewEngine.RenderSingleTile(tile, totalWidth, totalHeight, out _);
            });
        }

        /// <summary>
        /// Рендерит полное изображение предпросмотра для заданного состояния.
        /// </summary>
        /// <param name="state">Состояние фрактала.</param>
        /// <param name="previewWidth">Ширина превью.</param>
        /// <param name="previewHeight">Высота превью.</param>
        /// <returns>Bitmap с изображением превью.</returns>
        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (string.IsNullOrEmpty(state.PreviewParametersJson))
            {
                var bmpError = new Bitmap(previewWidth, previewHeight);
                using (var g = Graphics.FromImage(bmpError)) { g.Clear(Color.DarkGray); TextRenderer.DrawText(g, "Нет данных", Font, new Rectangle(0, 0, previewWidth, previewHeight), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
                return bmpError;
            }
            CollatzPreviewParams previewParams;
            try
            {
                previewParams = JsonSerializer.Deserialize<CollatzPreviewParams>(state.PreviewParametersJson);
            }
            catch (Exception)
            {
                var bmpError = new Bitmap(previewWidth, previewHeight);
                using (var g = Graphics.FromImage(bmpError)) { g.Clear(Color.DarkRed); TextRenderer.DrawText(g, "Ошибка параметров", Font, new Rectangle(0, 0, previewWidth, previewHeight), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
                return bmpError;
            }

            var previewEngine = CreateEngineFromPreviewParams(previewParams);
            return previewEngine.RenderToBitmap(previewWidth, previewHeight, 1, progress => { });
        }

        /// <summary>
        /// Загружает все сохранения для данного типа фрактала.
        /// </summary>
        /// <returns>Список загруженных состояний.</returns>
        public List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            var specificSaves = SaveFileManager.LoadSaves<CollatzSaveState>(this.FractalTypeIdentifier);
            return specificSaves.Cast<FractalSaveStateBase>().ToList();
        }

        /// <summary>
        /// Сохраняет все состояния для данного типа фрактала.
        /// </summary>
        /// <param name="saves">Список состояний для сохранения.</param>
        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            var specificSaves = saves.Cast<CollatzSaveState>().ToList();
            SaveFileManager.SaveSaves(this.FractalTypeIdentifier, specificSaves);
        }
        #endregion
    }
}
