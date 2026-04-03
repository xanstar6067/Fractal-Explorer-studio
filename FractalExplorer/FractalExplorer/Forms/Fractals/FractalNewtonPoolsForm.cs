using FractalExplorer.Engines;
using FractalExplorer.Forms.Other;
using FractalExplorer.Resources;
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
namespace FractalExplorer
{
    /// <summary>
    /// Форма для отображения и взаимодействия с фракталами Ньютона.
    /// Позволяет задавать комплексные функции, находить их корни и визуализировать
    /// бассейны притяжения этих корней с различными настройками палитры.
    /// </summary>
    public partial class NewtonPools : Form, ISaveLoadCapableFractal, IHighResRenderable
    {
        #region Fields

        /// <summary>
        /// Экземпляр движка для рендеринга фрактала Ньютона.
        /// </summary>
        private readonly FractalNewtonEngine _engine;

        /// <summary>
        /// Таймер для отложенного запуска рендеринга, чтобы избежать частых перерисовок.
        /// </summary>
        private readonly System.Windows.Forms.Timer _renderDebounceTimer;

        /// <summary>
        /// Компонент для визуализации процесса рендеринга плиток.
        /// </summary>
        private RenderVisualizerComponent _renderVisualizer;
        private EventHandler? _themeChangedHandler;
        private bool _isFormulaInputHovered;

        /// <summary>
        /// Менеджер палитр, специфичный для фракталов Ньютона.
        /// </summary>
        private NewtonPaletteManager _paletteManager;

        /// <summary>
        /// Форма для настройки цветовой палитры фрактала Ньютона.
        /// </summary>
        private ColorConfigurationNewtonPoolsForm _colorSettingsForm;

        /// <summary>
        /// Базовый масштаб для преобразования мировых координат в экранные.
        /// </summary>
        private const double BASE_SCALE = 3.0;

        /// <summary>
        /// Размер одной плитки (тайла) для пошагового рендеринга.
        /// </summary>
        private const int TILE_SIZE = 16;

        /// <summary>
        /// Объект для блокировки доступа к битмапам во время рендеринга.
        /// </summary>
        private readonly object _bitmapLock = new object();

        /// <summary>
        /// Битмап, содержащий отрисованное изображение для предпросмотра.
        /// </summary>
        private Bitmap _previewBitmap;

        /// <summary>
        /// Битмап, в который текущий момент происходит рендеринг плиток.
        /// </summary>
        private Bitmap _currentRenderingBitmap;

        /// <summary>
        /// Токен отмены для операций рендеринга предпросмотра.
        /// </summary>
        private CancellationTokenSource _previewRenderCts;

        /// <summary>
        /// Флаг, указывающий, выполняется ли сейчас рендеринг в высоком разрешении.
        /// </summary>
        private volatile bool _isHighResRendering = false;

        /// <summary>
        /// Флаг, указывающий, выполняется ли сейчас рендеринг предпросмотра.
        /// </summary>
        private volatile bool _isRenderingPreview = false;

        /// <summary>
        /// Текущий коэффициент масштабирования фрактала.
        /// </summary>
        private double _zoom = 1.0;

        /// <summary>
        /// Текущая координата X центра видимой области фрактала.
        /// </summary>
        private double _centerX = 0.0;

        /// <summary>
        /// Текущая координата Y центра видимой области фрактала.
        /// </summary>
        private double _centerY = 0.0;

        /// <summary>
        /// Координата X центра, по которой был отрисован _previewBitmap.
        /// Используется для интерполяции при панорамировании/масштабировании.
        /// </summary>
        private double _renderedCenterX;

        /// <summary>
        /// Координата Y центра, по которой был отрисован _previewBitmap.
        /// Используется для интерполяции при панорамировании/масштабировании.
        /// </summary>
        private double _renderedCenterY;

        /// <summary>
        /// Коэффициент масштабирования, по которому был отрисован _previewBitmap.
        /// Используется для интерполяции при панорамировании/масштабировании.
        /// </summary>
        private double _renderedZoom;

        /// <summary>
        /// Начальная позиция курсора мыши при панорамировании.
        /// </summary>
        private Point _panStart;

        /// <summary>
        /// Флаг, указывающий, находится ли пользователь в режиме панорамирования.
        /// </summary>
        private bool _panning = false;

        /// <summary>
        /// Базовая часть заголовка окна.
        /// </summary>
        private string _baseTitle;
        private const int ToggleButtonMargin = 12;
        private bool _suppressResizeRender = false;
        private readonly FullscreenToggleController _fullscreenController = new();
        private bool _controlsPanelVisible = true;

        /// <summary>
        /// Массив предустановленных полиномиальных формул для выбора.
        /// </summary>
        private readonly string[] presetPolynomials =
        {
            "z^3-1",
            "z^4-1",
            "z^5-1",
            "z^6-1",
            "z^10 - 1",
            "z^3-2*z+2",
            "z^5 - z^2 + 1",
            "z^6 + 3*z^3 - 2",
            "z^4 - 4*z^2 + 4",
            "z^7 + z^4 - z + 1",
            "z^8 + 15*z^4 - 16",
            "z^4 + z^3 + z^2 + z + 1",
            "z^2 - i", "(z^2-1)*(z-2*i)",
            "(1+2*i)*z^2+z-1",
            "0.5*z^3 - 1.25*z + 2",
            "(2+i)*z^3 - (1-2*i)*z + 1",
            "i*z^4 + z - 1",
            "(1+0.5*i)*z^2 - z + (2-3*i)",
            "(0.3+1.7*i)*z^3 + (1-i)",
            "(2-i)*z^5 + (3+2*i)*z^2 - 1",
            "-2*z^3 + 0.75*z^2 - 1",
            "z^6 - 1.5*z^3 + 0.25",
            "-0.1*z^4 + z - 2",
            "(1/2)*z^3 + (3/4)*z - 1",
            "(2+3*i)*(z^2) - (1-i)*z + 4",
            "(z^2-1)/(z^2+1)",
            "(z^3-1)/(z^3+1)",
            "z^2 / (z-1)^2",
            "(z^4-1)/(z*z-2*z+1)",
            "(z-1)*(z+1)*(z-0.2)*(z+0.2)",
            "(z-1)^2*(z+1)",
            "z^7 - 0.5*z^3 + 1",
            "z^5 + (0.3-0.8*i)*z^2 - (1.2+0.1*i)",
            "(z-1)*(z+1)*(z-0.1)*(z+0.1)*(z-0.01)",
            "(z-1)*(z-0.3)*(z+0.7)*(z-0.05)",
            "(z-1)^2*(z+1)*(z-0.2)"
        };

        #endregion

        #region Constructor

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="NewtonPools"/>.
        /// </summary>
        public NewtonPools()
        {
            InitializeComponent();
            KeyPreview = true;
            ThemeManager.RegisterForm(this);
            _engine = new FractalNewtonEngine();
            _renderDebounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _paletteManager = new NewtonPaletteManager();
            InitializeForm();
        }

        #endregion

        #region UI Initialization

        /// <summary>
        /// Выполняет инициализацию элементов управления формы и подписку на события.
        /// </summary>
        private void InitializeForm()
        {
            _baseTitle = this.Text;
            _renderDebounceTimer.Tick += RenderDebounceTimer_Tick;

            _renderVisualizer = new RenderVisualizerComponent(TILE_SIZE);
            _renderVisualizer.NeedsRedraw += OnVisualizerNeedsRedraw;

            cbSelector.Items.AddRange(presetPolynomials);
            cbSelector.SelectedIndex = 0;
            richTextInput.Text = cbSelector.SelectedItem.ToString();

            int cores = Environment.ProcessorCount;
            for (int i = 1; i <= cores; i++) cbThreads.Items.Add(i);
            cbThreads.Items.Add("Auto");
            cbThreads.SelectedItem = "Auto";

            nudZoom.Minimum = 0.001M;
            nudZoom.Maximum = 1_000_000_000_000M;
            nudZoom.DecimalPlaces = 4;
            nudZoom.Value = (decimal)_zoom;

            btnConfigurePalette.Click += btnConfigurePalette_Click;

            nudIterations.ValueChanged += (s, e) => ScheduleRender();
            cbThreads.SelectedIndexChanged += (s, e) => ScheduleRender();
            nudZoom.ValueChanged += (s, e) => { _zoom = (double)nudZoom.Value; ScheduleRender(); };
            cbSelector.SelectedIndexChanged += cbSelector_SelectedIndexChanged;
            richTextInput.TextChanged += (s, e) => UpdateFormulaAccentState();
            richTextInput.MouseEnter += FormulaInput_MouseEnter;
            richTextInput.MouseLeave += FormulaInput_MouseLeave;
            pnlFormulaInput.MouseEnter += FormulaInput_MouseEnter;
            pnlFormulaInput.MouseLeave += FormulaInput_MouseLeave;
            canvasHost.Resize += CanvasHost_Resize;
            controlsHost.SizeChanged += ControlsHost_SizeChanged;
            KeyDown += Form_KeyDown;
            FormClosing += Form_FormClosing;
            pnlFormulaGlow.MouseEnter += FormulaInput_MouseEnter;
            pnlFormulaGlow.MouseLeave += FormulaInput_MouseLeave;
            lblFormulaExample.MouseEnter += FormulaInput_MouseEnter;
            lblFormulaExample.MouseLeave += FormulaInput_MouseLeave;
            btnApplyFormula.Click += btnApplyFormula_Click;
            btnRender.Click += (s, e) => ScheduleRender();

            toolTipFormula.SetToolTip(richTextInput,
                "Синтаксис: используйте z, числа, i, +, -, *, /, ^ и скобки.\n" +
                "Пример: (z^3-1)/(z^2+1)");
            toolTipFormula.SetToolTip(lblFormulaExample, "Поддерживаются комплексные коэффициенты, например: (2+3*i)*z^2 - 1");

            _themeChangedHandler = (_, _) => ApplyFormulaInputTheme();
            ThemeManager.ThemeChanged += _themeChangedHandler;
            ApplyFormulaInputTheme();

            fractal_bitmap.MouseWheel += Canvas_MouseWheel;
            fractal_bitmap.MouseDown += Canvas_MouseDown;
            fractal_bitmap.MouseMove += Canvas_MouseMove;
            fractal_bitmap.MouseUp += Canvas_MouseUp;
            fractal_bitmap.Paint += Canvas_Paint;
            fractal_bitmap.Resize += FractalBitmap_Resize;

            _renderedCenterX = _centerX;
            _renderedCenterY = _centerY;
            _renderedZoom = _zoom;

            ApplyActivePalette();
            UpdateToggleControlsPosition();
            ClearInitialControlSelection();
            ScheduleRender();
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

        private void btnApplyFormula_Click(object? sender, EventArgs e)
        {
            if (!_engine.SetFormula(richTextInput.Text, out string debugInfo))
            {
                richTextDebugOutput.Text = debugInfo;
                MessageBox.Show("Проверьте формулу: есть синтаксическая ошибка.", "Ошибка формулы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateFormulaAccentState();
                return;
            }

            richTextDebugOutput.Text = debugInfo;
            RefreshColorSettingsRootCount();
            ScheduleRender();
            UpdateFormulaAccentState();
        }

        private void ApplyFormulaInputTheme()
        {
            ThemeDefinition theme = ThemeManager.CurrentDefinition;

            pnlFormulaInput.BackColor = theme.InputBorderColor;
            pnlFormulaGlow.BackColor = theme.ControlBackground;
            richTextInput.BackColor = theme.ControlBackground;
            richTextInput.ForeColor = theme.PrimaryText;
            lblFormulaExample.ForeColor = theme.SecondaryText;
            lblFormula.ForeColor = theme.AccentPrimary;

            UpdateFormulaAccentState();
        }

        private void UpdateFormulaAccentState()
        {
            ThemeDefinition theme = ThemeManager.CurrentDefinition;
            bool hasText = !string.IsNullOrWhiteSpace(richTextInput.Text);
            lblFormulaExample.Visible = !hasText;
            pnlFormulaInput.BackColor = hasText ? theme.InputBorderColor : theme.AccentSecondary;
            Color glowBackground = pnlFormulaGlow.Parent?.BackColor ?? theme.PanelBackground;
            pnlFormulaGlow.BackColor = ThemeManager.GetInteractiveStateColor(theme, glowBackground, _isFormulaInputHovered);
        }

        private void FormulaInput_MouseEnter(object? sender, EventArgs e)
        {
            _isFormulaInputHovered = true;
            UpdateFormulaAccentState();
        }

        private void FormulaInput_MouseLeave(object? sender, EventArgs e)
        {
            Point mouseScreen = Control.MousePosition;
            bool isStillInside = pnlFormulaGlow.RectangleToScreen(pnlFormulaGlow.ClientRectangle).Contains(mouseScreen);
            if (isStillInside)
            {
                return;
            }

            _isFormulaInputHovered = false;
            UpdateFormulaAccentState();
        }


        private void FractalBitmap_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                return;
            }

            if (_suppressResizeRender)
            {
                fractal_bitmap.Invalidate();
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
                fractal_bitmap.Invalidate();
            }
            finally
            {
                _suppressResizeRender = false;
            }
        }

        private void ClearInitialControlSelection()
        {
            if (IsDisposed)
            {
                return;
            }

            if (!IsHandleCreated)
            {
                EventHandler? onHandleCreated = null;
                onHandleCreated = (sender, args) =>
                {
                    HandleCreated -= onHandleCreated;
                    if (!IsDisposed)
                    {
                        BeginInvoke(new Action(() => ActiveControl = null));
                    }
                };

                HandleCreated += onHandleCreated;
                return;
            }

            BeginInvoke(new Action(() => ActiveControl = null));
        }

        #endregion

        #region Palette Management

        /// <summary>
        /// Обрабатывает нажатие на кнопку конфигурации палитры.
        /// </summary>
        private void btnConfigurePalette_Click(object sender, EventArgs e)
        {
            if (!_engine.SetFormula(richTextInput.Text, out string _))
            {
                MessageBox.Show("Сначала введите корректную формулу, чтобы определить количество корней.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_colorSettingsForm == null || _colorSettingsForm.IsDisposed)
            {
                _colorSettingsForm = new ColorConfigurationNewtonPoolsForm(_paletteManager);
                _colorSettingsForm.PaletteChanged += (s, palette) =>
                {
                    _paletteManager.ActivePalette = palette;
                    ApplyActivePalette();
                    ScheduleRender();
                };
            }
            _colorSettingsForm.ShowWithRootCount(_engine.Roots.Count);
        }

        /// <summary>
        /// Применяет активную палитру к движку рендеринга.
        /// </summary>
        private void ApplyActivePalette()
        {
            var palette = _paletteManager.ActivePalette;
            if (palette == null) return;

            if (palette.RootColors == null || palette.RootColors.Count == 0)
            {
                _engine.RootColors = ColorConfigurationNewtonPoolsForm.GenerateHarmonicColors(_engine.Roots.Count).ToArray();
            }
            else
            {
                _engine.RootColors = palette.RootColors.ToArray();
            }

            _engine.BackgroundColor = palette.BackgroundColor;
            _engine.UseGradient = palette.IsGradient;
        }

        private void RefreshColorSettingsRootCount()
        {
            if (_colorSettingsForm == null || _colorSettingsForm.IsDisposed || !_colorSettingsForm.Visible)
            {
                return;
            }

            _colorSettingsForm.UpdateRootCount(_engine.Roots.Count);
        }

        #endregion

        #region Rendering Logic

        /// <summary>
        /// Вызывает перерисовку элемента управления, когда визуализатор рендеринга этого требует.
        /// </summary>
        private void OnVisualizerNeedsRedraw()
        {
            if (fractal_bitmap.IsHandleCreated && !fractal_bitmap.IsDisposed)
            {
                fractal_bitmap.BeginInvoke((Action)(() => fractal_bitmap.Invalidate()));
            }
        }

        /// <summary>
        /// Планирует отложенный рендеринг фрактала.
        /// </summary>
        private void ScheduleRender()
        {
            if (_isHighResRendering || WindowState == FormWindowState.Minimized) return;
            if (_isRenderingPreview) _previewRenderCts?.Cancel();
            _renderDebounceTimer.Stop();
            _renderDebounceTimer.Start();
        }

        /// <summary>
        /// Обрабатывает событие тика таймера для отложенного рендеринга.
        /// </summary>
        private async void RenderDebounceTimer_Tick(object sender, EventArgs e)
        {
            _renderDebounceTimer.Stop();
            if (_isHighResRendering || _isRenderingPreview)
            {
                ScheduleRender();
                return;
            }
            await StartPreviewRender();
        }

        /// <summary>
        /// Запускает асинхронный процесс рендеринга предпросмотра.
        /// </summary>
        private async Task StartPreviewRender()
        {
            if (fractal_bitmap.Width <= 0 || fractal_bitmap.Height <= 0) return;

            var stopwatch = Stopwatch.StartNew();
            _isRenderingPreview = true;
            _previewRenderCts?.Cancel();
            _previewRenderCts = new CancellationTokenSource();
            var token = _previewRenderCts.Token;

            _renderVisualizer?.NotifyRenderSessionStart();

            if (!_engine.SetFormula(richTextInput.Text, out string debugInfo))
            {
                richTextDebugOutput.Text = debugInfo;
                lock (_bitmapLock)
                {
                    _previewBitmap?.Dispose(); _previewBitmap = null;
                    _currentRenderingBitmap?.Dispose(); _currentRenderingBitmap = null;
                }
                if (fractal_bitmap.IsHandleCreated && !fractal_bitmap.IsDisposed) fractal_bitmap.Invalidate();
                _isRenderingPreview = false;
                _renderVisualizer?.NotifyRenderSessionComplete();
                return;
            }
            richTextDebugOutput.Text = debugInfo;
            RefreshColorSettingsRootCount();

            var newRenderingBitmap = new Bitmap(fractal_bitmap.Width, fractal_bitmap.Height, PixelFormat.Format32bppArgb);
            lock (_bitmapLock)
            {
                _currentRenderingBitmap = newRenderingBitmap;
            }

            UpdateEngineParameters();

            double currentRenderedCenterX = _centerX;
            double currentRenderedCenterY = _centerY;
            double currentRenderedZoom = _zoom;

            var tiles = GenerateTiles(fractal_bitmap.Width, fractal_bitmap.Height);
            var dispatcher = new TileRenderDispatcher(tiles, GetThreadCount(), RenderPatternSettings.SelectedPattern);

            if (progressBar.IsHandleCreated && !progressBar.IsDisposed)
            {
                progressBar.Invoke((Action)(() => { progressBar.Value = 0; progressBar.Maximum = tiles.Count; }));
            }
            int progress = 0;

            try
            {
                await dispatcher.RenderAsync(async (tile, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    _renderVisualizer?.NotifyTileRenderStart(tile.Bounds);
                    var tileBuffer = _engine.RenderSingleTile(tile, fractal_bitmap.Width, fractal_bitmap.Height, out int bytesPerPixel);
                    ct.ThrowIfCancellationRequested();

                    lock (_bitmapLock)
                    {
                        if (ct.IsCancellationRequested || _currentRenderingBitmap != newRenderingBitmap) return;
                        BitmapData bitmapData = _currentRenderingBitmap.LockBits(tile.Bounds, ImageLockMode.WriteOnly, _currentRenderingBitmap.PixelFormat);
                        int tileWidthInBytes = tile.Bounds.Width * bytesPerPixel;
                        for (int y = 0; y < tile.Bounds.Height; y++)
                        {
                            IntPtr destinationPointer = IntPtr.Add(bitmapData.Scan0, y * bitmapData.Stride);
                            int sourceOffset = y * tileWidthInBytes;
                            Marshal.Copy(tileBuffer, sourceOffset, destinationPointer, tileWidthInBytes);
                        }
                        _currentRenderingBitmap.UnlockBits(bitmapData);
                    }

                    _renderVisualizer?.NotifyTileRenderComplete(tile.Bounds);
                    if (ct.IsCancellationRequested || !fractal_bitmap.IsHandleCreated || fractal_bitmap.IsDisposed) return;

                    fractal_bitmap.Invoke((Action)(() => { if (!ct.IsCancellationRequested && progressBar.IsHandleCreated && !progressBar.IsDisposed) progressBar.Value = Math.Min(progressBar.Maximum, Interlocked.Increment(ref progress)); }));
                    await Task.Yield();
                }, token);

                token.ThrowIfCancellationRequested();

                stopwatch.Stop();
                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                this.Text = $"{_baseTitle} - Время последнего рендера: {elapsedSeconds:F3} сек.";

                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _previewBitmap?.Dispose();
                        _previewBitmap = new Bitmap(_currentRenderingBitmap);
                        _currentRenderingBitmap.Dispose();
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
                if (fractal_bitmap.IsHandleCreated && !fractal_bitmap.IsDisposed) fractal_bitmap.Invalidate();
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
                newRenderingBitmap?.Dispose();
                if (IsHandleCreated && !IsDisposed) MessageBox.Show($"Ошибка рендеринга: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isRenderingPreview = false;
                _renderVisualizer?.NotifyRenderSessionComplete();
                if (progressBar.IsHandleCreated && !progressBar.IsDisposed) progressBar.Invoke((Action)(() => progressBar.Value = 0));
            }
        }

        /// <summary>
        /// Генерирует список плиток (тайлов) для рендеринга изображения в порядке обычного обхода сетки.
        /// Итоговая стратегия распределения задается в <see cref="TileRenderDispatcher"/>.
        /// </summary>
        /// <param name="width">Ширина области рендеринга.</param>
        /// <param name="height">Высота области рендеринга.</param>
        /// <returns>Список плиток для рендеринга.</returns>
        private List<TileInfo> GenerateTiles(int width, int height)
        {
            var tiles = new List<TileInfo>();

            for (int y = 0; y < height; y += TILE_SIZE)
            {
                for (int x = 0; x < width; x += TILE_SIZE)
                {
                    int tileWidth = Math.Min(TILE_SIZE, width - x);
                    int tileHeight = Math.Min(TILE_SIZE, height - y);
                    tiles.Add(new TileInfo(x, y, tileWidth, tileHeight));
                }
            }
            return tiles;
        }

        #endregion

        #region Canvas Interaction

        /// <summary>
        /// Обрабатывает событие перерисовки холста.
        /// </summary>
        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Black);
            e.Graphics.InterpolationMode = InterpolationMode.Bilinear;

            lock (_bitmapLock)
            {
                if (_previewBitmap != null && fractal_bitmap.Width > 0 && fractal_bitmap.Height > 0)
                {
                    try
                    {
                        double renderedScale = BASE_SCALE / _renderedZoom;
                        double currentScale = BASE_SCALE / _zoom;
                        float drawScaleRatio = (float)(renderedScale / currentScale);

                        float newWidth = fractal_bitmap.Width * drawScaleRatio;
                        float newHeight = fractal_bitmap.Height * drawScaleRatio;

                        double deltaReal = _renderedCenterX - _centerX;
                        double deltaImaginary = _renderedCenterY - _centerY;

                        float offsetX = (float)(deltaReal / currentScale * fractal_bitmap.Width);
                        float offsetY = (float)(deltaImaginary / currentScale * fractal_bitmap.Width);

                        float drawX = (fractal_bitmap.Width - newWidth) / 2.0f + offsetX;
                        float drawY = (fractal_bitmap.Height - newHeight) / 2.0f + offsetY;

                        var destinationRectangle = new RectangleF(drawX, drawY, newWidth, newHeight);
                        e.Graphics.DrawImage(_previewBitmap, destinationRectangle);
                    }
                    catch (Exception) { /* Игнорируем ошибки интерполяции */}
                }

                if (_currentRenderingBitmap != null) e.Graphics.DrawImageUnscaled(_currentRenderingBitmap, Point.Empty);

                if (_renderVisualizer != null && _isRenderingPreview) _renderVisualizer.DrawVisualization(e.Graphics);
            }
        }

        /// <summary>
        /// Обрабатывает прокрутку колеса мыши для масштабирования.
        /// </summary>
        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering) return;

            CommitAndBakePreview();

            double zoomFactor = e.Delta > 0 ? 1.5 : 1.0 / 1.5;
            double scaleBefore = BASE_SCALE / _zoom / fractal_bitmap.Width;

            double mouseReal = _centerX + (e.X - fractal_bitmap.Width / 2.0) * scaleBefore;
            double mouseImaginary = _centerY + (e.Y - fractal_bitmap.Height / 2.0) * scaleBefore;

            _zoom = Math.Max((double)nudZoom.Minimum, Math.Min((double)nudZoom.Maximum, _zoom * zoomFactor));

            double scaleAfter = BASE_SCALE / _zoom / fractal_bitmap.Width;
            _centerX = mouseReal - (e.X - fractal_bitmap.Width / 2.0) * scaleAfter;
            _centerY = mouseImaginary - (e.Y - fractal_bitmap.Height / 2.0) * scaleAfter;

            fractal_bitmap.Invalidate();

            if (nudZoom.Value != (decimal)_zoom) nudZoom.Value = (decimal)_zoom;
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
            }
        }

        /// <summary>
        /// Обрабатывает движение мыши для панорамирования.
        /// </summary>
        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering || !_panning) return;

            CommitAndBakePreview();

            double scale = BASE_SCALE / _zoom / fractal_bitmap.Width;
            _centerX -= (e.X - _panStart.X) * scale;
            _centerY -= (e.Y - _panStart.Y) * scale;
            _panStart = e.Location;

            fractal_bitmap.Invalidate();
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
                ScheduleRender();
            }
        }

        /// <summary>
        /// Обрабатывает изменение выбранного элемента в списке предустановленных формул.
        /// </summary>
        private void cbSelector_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbSelector.SelectedIndex >= 0)
            {
                richTextInput.Text = cbSelector.SelectedItem.ToString();
                btnApplyFormula_Click(this, EventArgs.Empty);
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// "Запекает" текущее состояние рендеринга в основной битмап предпросмотра.
        /// Вызывается перед началом интерактивного действия (панорамирование, масштабирование).
        /// </summary>
        private void CommitAndBakePreview()
        {
            lock (_bitmapLock) { if (!_isRenderingPreview || _currentRenderingBitmap == null) return; }

            _previewRenderCts?.Cancel();

            lock (_bitmapLock)
            {
                if (_currentRenderingBitmap == null) return;

                var bakedBitmap = new Bitmap(fractal_bitmap.Width, fractal_bitmap.Height, PixelFormat.Format24bppRgb);
                using (var graphics = Graphics.FromImage(bakedBitmap))
                {
                    graphics.Clear(Color.Black);
                    graphics.InterpolationMode = InterpolationMode.Bilinear;

                    if (_previewBitmap != null)
                    {
                        try
                        {
                            double renderedScale = BASE_SCALE / _renderedZoom;
                            double currentScale = BASE_SCALE / _zoom;
                            float drawScaleRatio = (float)(renderedScale / currentScale);
                            float newWidth = fractal_bitmap.Width * drawScaleRatio;
                            float newHeight = fractal_bitmap.Height * drawScaleRatio;
                            double deltaReal = _renderedCenterX - _centerX;
                            double deltaImaginary = _renderedCenterY - _centerY;
                            float offsetX = (float)(deltaReal / currentScale * fractal_bitmap.Width);
                            float offsetY = (float)(deltaImaginary / currentScale * fractal_bitmap.Width);
                            float drawX = (fractal_bitmap.Width - newWidth) / 2.0f + offsetX;
                            float drawY = (fractal_bitmap.Height - newHeight) / 2.0f + offsetY;
                            var destinationRectangle = new RectangleF(drawX, drawY, newWidth, newHeight);
                            graphics.DrawImage(_previewBitmap, destinationRectangle);
                        }
                        catch (Exception) { /* Игнорируем ошибки */}
                    }

                    if (_currentRenderingBitmap != null) graphics.DrawImageUnscaled(_currentRenderingBitmap, Point.Empty);
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
        /// Обновляет параметры движка рендеринга значениями из элементов управления.
        /// </summary>
        private void UpdateEngineParameters()
        {
            _engine.MaxIterations = (int)nudIterations.Value;
            _engine.CenterX = _centerX;
            _engine.CenterY = _centerY;
            _engine.Scale = BASE_SCALE / _zoom;
            ApplyActivePalette();
        }

        /// <summary>
        /// Возвращает количество потоков, выбранное пользователем.
        /// </summary>
        /// <returns>Количество потоков для рендеринга.</returns>
        private int GetThreadCount()
        {
            return cbThreads.SelectedItem?.ToString() == "Auto" ? Environment.ProcessorCount : Convert.ToInt32(cbThreads.SelectedItem);
        }

        /// <summary>
        /// Открывает менеджер сохранения изображений.
        /// </summary>
        /// <remarks>
        /// </remarks>
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

        #region Form Lifecycle

        /// <summary>
        /// Выполняет очистку ресурсов при закрытии формы.
        /// </summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _renderDebounceTimer?.Stop();
            _previewRenderCts?.Cancel();
            _previewRenderCts?.Dispose();
            _renderDebounceTimer?.Dispose();
            lock (_bitmapLock)
            {
                _previewBitmap?.Dispose();
                _previewBitmap = null;
                _currentRenderingBitmap?.Dispose();
                _currentRenderingBitmap = null;
            }
            _colorSettingsForm?.Close();
            _colorSettingsForm?.Dispose();
            if (_themeChangedHandler is not null)
            {
                ThemeManager.ThemeChanged -= _themeChangedHandler;
                _themeChangedHandler = null;
            }
            if (_renderVisualizer != null)
            {
                _renderVisualizer.NeedsRedraw -= OnVisualizerNeedsRedraw;
                _renderVisualizer.Dispose();
            }
        }

        #endregion

        #region ISaveLoadCapableFractal Implementation

        /// <inheritdoc />
        public string FractalTypeIdentifier => "NewtonPools";

        /// <inheritdoc />
        public Type ConcreteSaveStateType => typeof(NewtonSaveState);

        /// <summary>
        /// Содержит параметры для генерации превью фрактала.
        /// </summary>
        public class NewtonPreviewParams
        {
            /// <summary>
            /// Формула фрактала.
            /// </summary>
            public string Formula { get; set; }
            /// <summary>
            /// Координата X центра.
            /// </summary>
            public decimal CenterX { get; set; }
            /// <summary>
            /// Координата Y центра.
            /// </summary>
            public decimal CenterY { get; set; }
            /// <summary>
            /// Коэффициент масштабирования.
            /// </summary>
            public decimal Zoom { get; set; }
            /// <summary>
            /// Максимальное количество итераций.
            /// </summary>
            public int Iterations { get; set; }
            /// <summary>
            /// Снимок палитры.
            /// </summary>
            public NewtonColorPalette PaletteSnapshot { get; set; }
        }

        /// <inheritdoc />
        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            _paletteManager.ActivePalette.BackgroundColor = _engine.BackgroundColor;
            _paletteManager.ActivePalette.IsGradient = _engine.UseGradient;
            _paletteManager.ActivePalette.RootColors = new List<Color>(_engine.RootColors);

            var state = new NewtonSaveState(this.FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                Formula = richTextInput.Text,
                CenterX = (decimal)_centerX,
                CenterY = (decimal)_centerY,
                Zoom = (decimal)_zoom,
                Iterations = (int)nudIterations.Value,
                PaletteSnapshot = _paletteManager.ActivePalette
            };

            var previewParams = new NewtonPreviewParams
            {
                Formula = state.Formula,
                CenterX = state.CenterX,
                CenterY = state.CenterY,
                Zoom = state.Zoom,
                Iterations = Math.Min(state.Iterations, 50),
                PaletteSnapshot = state.PaletteSnapshot
            };

            var jsonOptions = new JsonSerializerOptions();
            jsonOptions.Converters.Add(new Utilities.JsonConverters.JsonColorConverter());
            state.PreviewParametersJson = JsonSerializer.Serialize(previewParams, jsonOptions);

            return state;
        }

        /// <inheritdoc />
        public void LoadState(FractalSaveStateBase stateBase)
        {
            if (stateBase is NewtonSaveState state)
            {
                _isRenderingPreview = false;
                _previewRenderCts?.Cancel();
                _renderDebounceTimer.Stop();

                _centerX = (double)state.CenterX;
                _centerY = (double)state.CenterY;
                _zoom = (double)state.Zoom;

                richTextInput.Text = state.Formula;
                nudZoom.Value = (decimal)_zoom;
                nudIterations.Value = state.Iterations;

                _paletteManager.ActivePalette = state.PaletteSnapshot;

                UpdateEngineParameters();

                lock (_bitmapLock)
                {
                    _previewBitmap?.Dispose(); _previewBitmap = null;
                    _currentRenderingBitmap?.Dispose(); _currentRenderingBitmap = null;
                }

                _renderedCenterX = _centerX;
                _renderedCenterY = _centerY;
                _renderedZoom = _zoom;

                ScheduleRender();
            }
            else
            {
                MessageBox.Show("Несовместимый тип состояния для загрузки.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <inheritdoc />
        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(state.PreviewParametersJson)) return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];

                NewtonPreviewParams previewParams;
                try
                {
                    var jsonOptions = new JsonSerializerOptions();
                    jsonOptions.Converters.Add(new Utilities.JsonConverters.JsonColorConverter());
                    previewParams = JsonSerializer.Deserialize<NewtonPreviewParams>(state.PreviewParametersJson, jsonOptions);
                }
                catch { return new byte[tile.Bounds.Width * tile.Bounds.Height * 4]; }

                var previewEngine = new FractalNewtonEngine();
                if (!previewEngine.SetFormula(previewParams.Formula, out _)) return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];

                previewEngine.CenterX = (double)previewParams.CenterX;
                previewEngine.CenterY = (double)previewParams.CenterY;
                previewEngine.Scale = 3.0 / (double)previewParams.Zoom;
                previewEngine.MaxIterations = 150;

                var palette = previewParams.PaletteSnapshot;
                previewEngine.BackgroundColor = palette.BackgroundColor;
                previewEngine.UseGradient = palette.IsGradient;
                previewEngine.RootColors = (palette.RootColors != null && palette.RootColors.Any()) ? palette.RootColors.ToArray() : ColorConfigurationNewtonPoolsForm.GenerateHarmonicColors(previewEngine.Roots.Count).ToArray();

                return previewEngine.RenderSingleTile(tile, totalWidth, totalHeight, out _);
            });
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

            NewtonPreviewParams previewParams;
            try
            {
                var jsonOptions = new JsonSerializerOptions();
                jsonOptions.Converters.Add(new Utilities.JsonConverters.JsonColorConverter());
                previewParams = JsonSerializer.Deserialize<NewtonPreviewParams>(state.PreviewParametersJson, jsonOptions);
            }
            catch (Exception)
            {
                var bmpError = new Bitmap(previewWidth, previewHeight);
                using (var g = Graphics.FromImage(bmpError)) { g.Clear(Color.DarkRed); TextRenderer.DrawText(g, "Ошибка параметров", Font, new Rectangle(0, 0, previewWidth, previewHeight), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
                return bmpError;
            }

            var previewEngine = new FractalNewtonEngine();
            if (!previewEngine.SetFormula(previewParams.Formula, out _))
            {
                var bmpError = new Bitmap(previewWidth, previewHeight);
                using (var g = Graphics.FromImage(bmpError)) { g.Clear(Color.DarkRed); TextRenderer.DrawText(g, "Ошибка формулы", Font, new Rectangle(0, 0, previewWidth, previewHeight), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
                return bmpError;
            }

            previewEngine.CenterX = (double)previewParams.CenterX;
            previewEngine.CenterY = (double)previewParams.CenterY;
            if (previewParams.Zoom == 0) previewParams.Zoom = 0.001m;
            previewEngine.Scale = 3.0 / (double)previewParams.Zoom;
            previewEngine.MaxIterations = previewParams.Iterations;

            var palette = previewParams.PaletteSnapshot;
            previewEngine.BackgroundColor = palette.BackgroundColor;
            previewEngine.UseGradient = palette.IsGradient;
            if (palette.RootColors != null && palette.RootColors.Count > 0)
            {
                previewEngine.RootColors = palette.RootColors.ToArray();
            }
            else
            {
                previewEngine.RootColors = ColorConfigurationNewtonPoolsForm.GenerateHarmonicColors(previewEngine.Roots.Count).ToArray();
            }

            return previewEngine.RenderToBitmap(previewWidth, previewHeight, 1, progress => { }, CancellationToken.None);
        }

        /// <inheritdoc />
        public List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            var specificSaves = SaveFileManager.LoadSaves<NewtonSaveState>(this.FractalTypeIdentifier);
            return specificSaves.Cast<FractalSaveStateBase>().ToList();
        }

        /// <inheritdoc />
        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            var specificSaves = saves.Cast<NewtonSaveState>().ToList();
            SaveFileManager.SaveSaves(this.FractalTypeIdentifier, specificSaves);
        }

        /// <summary>
        /// Открывает диалоговое окно менеджера сохранений.
        /// </summary>
        private void btnStateManager_Click(object sender, EventArgs e)
        {
            using (var dialog = new Forms.SaveLoadDialogForm(this))
            {
                dialog.ShowDialog(this);
            }
        }

        #endregion

        #region IHighResRenderable Implementation

        /// <inheritdoc />
        public HighResRenderState GetRenderState()
        {
            var state = new HighResRenderState
            {
                EngineType = this.FractalTypeIdentifier,
                CenterX = (decimal)_centerX,
                CenterY = (decimal)_centerY,
                Zoom = (decimal)_zoom,
                BaseScale = (decimal)BASE_SCALE,
                Iterations = (int)nudIterations.Value,
                FileNameDetails = "newton_fractal",
            };
            return state;
        }

        /// <summary>
        /// Создает и настраивает экземпляр движка на основе состояния рендеринга.
        /// </summary>
        /// <param name="state">Состояние для рендеринга.</param>
        /// <param name="forPreview">Указывает, создается ли движок для предпросмотра (с меньшим числом итераций).</param>
        /// <returns>Настроенный экземпляр <see cref="FractalNewtonEngine"/>.</returns>
        /// <exception cref="InvalidOperationException">Вызывается, если формула некорректна.</exception>
        private FractalNewtonEngine CreateEngineFromState(HighResRenderState state, bool forPreview)
        {
            var engine = new FractalNewtonEngine();

            if (!engine.SetFormula(richTextInput.Text, out _))
            {
                throw new InvalidOperationException("Не удалось обработать формулу для рендеринга.");
            }

            engine.CenterX = (double)state.CenterX;
            engine.CenterY = (double)state.CenterY;
            engine.Scale = (double)state.BaseScale / (double)state.Zoom;

            if (forPreview)
            {
                engine.MaxIterations = Math.Min(state.Iterations, 150);
            }
            else
            {
                engine.MaxIterations = state.Iterations;
            }

            var currentPalette = _paletteManager.ActivePalette;
            engine.BackgroundColor = currentPalette.BackgroundColor;
            engine.UseGradient = currentPalette.IsGradient;
            engine.RootColors = (currentPalette.RootColors != null && currentPalette.RootColors.Any())
                ? currentPalette.RootColors.ToArray()
                : ColorConfigurationNewtonPoolsForm.GenerateHarmonicColors(engine.Roots.Count).ToArray();

            return engine;
        }

        /// <inheritdoc />
        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            _isHighResRendering = true;
            try
            {
                FractalNewtonEngine renderEngine = CreateEngineFromState(state, forPreview: false);
                int threadCount = GetThreadCount();

                Action<int> progressCallback = p => progress.Report(new RenderProgress { Percentage = p, Status = "Рендеринг..." });

                Bitmap highResBitmap = await Task.Run(() => renderEngine.RenderToBitmapSSAA(
                    width, height, threadCount, progressCallback, ssaaFactor, cancellationToken), cancellationToken);

                return highResBitmap;
            }
            finally
            {
                _isHighResRendering = false;
            }
        }

        /// <inheritdoc />
        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            var engine = CreateEngineFromState(state, forPreview: true);
            return engine.RenderToBitmap(previewWidth, previewHeight, 1, _ => { }, CancellationToken.None);
        }

        #endregion
    }
}
