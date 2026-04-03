using FractalExplorer.Engines;
using FractalExplorer.Forms.Other;
using FractalExplorer.Forms.SelectorsForms.Selector;
using FractalExplorer.Resources;
using FractalExplorer.Utilities;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

using FractalExplorer.Utilities.Theme;
using FractalExplorer.Utilities.UI;
namespace FractalExplorer.Forms
{
    public partial class FractalNovaJuliaForm : Form, IFractalForm, IHighResRenderable, ISaveLoadCapableFractal
    {
        #region Fields
        private FractalNovaFamilyEngine _fractalEngine;
        private RenderVisualizerComponent _renderVisualizer;
        private PaletteManager _paletteManager;
        private ColorConfigurationForm _colorConfigForm;
        private NovaMandelbrotSelectorForm _selectorForm;
        private System.Windows.Forms.Timer _renderDebounceTimer;

        private Color[] _gammaCorrectedPaletteCache;
        private string _paletteCacheSignature;

        private const int TILE_SIZE = 16;
        private readonly object _bitmapLock = new object();
        private Bitmap _previewBitmap;
        private Bitmap _currentRenderingBitmap;
        private CancellationTokenSource _previewRenderCts;

        private volatile bool _isHighResRendering = false;
        private volatile bool _isRenderingPreview = false;

        protected decimal _zoom = 1.0m;
        protected decimal _centerX = 0.0m;
        protected decimal _centerY = 0.0m;

        private decimal _renderedCenterX;
        private decimal _renderedCenterY;
        private decimal _renderedZoom;

        private Point _panStart;
        private bool _panning = false;

        private string _baseTitle;
        private const int ToggleButtonMargin = 12;
        private bool _suppressResizeRender = false;
        private readonly FullscreenToggleController _fullscreenController = new();
        private bool _controlsPanelVisible = true;
        private const decimal BASE_SCALE = 4.0m;

        private const int MAP_PREVIEW_ITERATIONS = 100;
        private const double MAP_MIN_RE = -2.0;
        private const double MAP_MAX_RE = 2.0;
        private const double MAP_MIN_IM = -2.0;
        private const double MAP_MAX_IM = 2.0;

        private Label _mapPreviewHintLabel;
        private Panel _mapPreviewBorderPanel;
        private Panel _mapPreviewHoverBorderPanel;
        private bool _isMapPreviewHovered;
        private EventHandler? _mapPreviewThemeChangedHandler;
        #endregion

        public FractalNovaJuliaForm()
        {
            InitializeComponent();
            KeyPreview = true;
            ThemeManager.RegisterForm(this);
            InitializeMandelbrotPreviewInteractionUi();
            this.Load += FractalNovaJuliaForm_Load;
            this.FormClosed += FractalNovaJuliaForm_FormClosed;
        }


        private void InitializeMandelbrotPreviewInteractionUi()
        {
            _mapPreviewHintLabel = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 20,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Кликните по изображению для выбора точки",
                Padding = new Padding(0, 0, 0, 4)
            };

            _mapPreviewBorderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1)
            };

            _mapPreviewHoverBorderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                Margin = new Padding(0)
            };

            pbMandelbrotPreview.BorderStyle = BorderStyle.None;
            pbMandelbrotPreview.Cursor = Cursors.Cross;
            pbMandelbrotPreview.MouseEnter += PbMandelbrotPreview_MouseEnter;
            pbMandelbrotPreview.MouseLeave += PbMandelbrotPreview_MouseLeave;

            pnlMapPreview.Controls.Remove(pbMandelbrotPreview);
            _mapPreviewHoverBorderPanel.Controls.Add(pbMandelbrotPreview);
            _mapPreviewBorderPanel.Controls.Add(_mapPreviewHoverBorderPanel);
            pnlMapPreview.Controls.Add(_mapPreviewBorderPanel);
            pnlMapPreview.Controls.Add(_mapPreviewHintLabel);

            _mapPreviewThemeChangedHandler = (_, _) => ApplyMandelbrotPreviewInteractiveStyles();
            ThemeManager.ThemeChanged += _mapPreviewThemeChangedHandler;
            ApplyMandelbrotPreviewInteractiveStyles();
        }

        private void PbMandelbrotPreview_MouseEnter(object? sender, EventArgs e)
        {
            _isMapPreviewHovered = true;
            ApplyMandelbrotPreviewInteractiveStyles();
        }

        private void PbMandelbrotPreview_MouseLeave(object? sender, EventArgs e)
        {
            _isMapPreviewHovered = false;
            ApplyMandelbrotPreviewInteractiveStyles();
        }

        private void ApplyMandelbrotPreviewInteractiveStyles()
        {
            if (_mapPreviewHintLabel == null || _mapPreviewBorderPanel == null || _mapPreviewHoverBorderPanel == null)
            {
                return;
            }

            ThemeDefinition theme = ThemeManager.CurrentDefinition;
            _mapPreviewHintLabel.ForeColor = theme.SecondaryText;
            _mapPreviewHintLabel.BackColor = Color.Transparent;
            Color previewBorderBackground = _mapPreviewBorderPanel.Parent?.BackColor ?? theme.PanelBackground;
            Color borderColor = ThemeManager.GetInteractiveStateColor(theme, previewBorderBackground, _isMapPreviewHovered);
            _mapPreviewBorderPanel.BackColor = borderColor;
            _mapPreviewHoverBorderPanel.BackColor = _isMapPreviewHovered ? borderColor : previewBorderBackground;
            pbMandelbrotPreview.BackColor = theme.ControlBackground;
        }

        #region Initialization
        private void InitializeControls()
        {
            int cores = Environment.ProcessorCount;
            cbThreads.Items.Clear();
            for (int i = 1; i <= cores; i++)
            {
                cbThreads.Items.Add(i);
            }
            cbThreads.Items.Add("Auto");
            cbThreads.SelectedItem = "Auto";

            cbSSAA.Items.AddRange(new object[] { "Выкл (1x)", "Низкое (2x)", "Высокое (4x)" });
            cbSSAA.SelectedItem = "Выкл (1x)";

            nudP_Re.Value = 3.0m;
            nudP_Im.Value = 0.0m;
            nudZ0_Re.Value = 1.0m;
            nudZ0_Im.Value = 0.0m;
            nudM.Value = 1.0m;
            nudC_Re.Value = 0.0m;
            nudC_Im.Value = 1.0m;

            nudIterations.Value = 100;
            nudThreshold.Value = 10m;
            nudZoom.Value = 1.0m;

            cbSmooth.Items.Clear();
            cbSmooth.Items.AddRange(new object[] { "Дискретно", "Плавно" });
            cbSmooth.SelectedIndex = 1;
        }

        private void InitializeEventHandlers()
        {
            // Обработчики, созданные в дизайнере, здесь не дублируются.
            btnRender.Click += (s, e) => ScheduleRender(true);

            nudP_Re.ValueChanged += ParamControl_Changed_WithMapUpdate;
            nudP_Im.ValueChanged += ParamControl_Changed_WithMapUpdate;
            nudZ0_Re.ValueChanged += ParamControl_Changed_WithMapUpdate;
            nudZ0_Im.ValueChanged += ParamControl_Changed_WithMapUpdate;
            nudM.ValueChanged += ParamControl_Changed_WithMapUpdate;

            nudC_Re.ValueChanged += ParamControl_Changed;
            nudC_Im.ValueChanged += ParamControl_Changed;

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

            pbMandelbrotPreview.Paint += PbMandelbrotPreview_Paint;
            pbMandelbrotPreview.Click += PbMandelbrotPreview_Click;

            _renderDebounceTimer.Tick += RenderDebounceTimer_Tick;
            _renderVisualizer.NeedsRedraw += OnVisualizerNeedsRedraw;
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
        private void ParamControl_Changed(object sender, EventArgs e)
        {
            if (_isHighResRendering)
            {
                return;
            }

            if (sender == nudZoom && nudZoom.Value != _zoom)
            {
                _zoom = nudZoom.Value;
            }

            if (sender == nudC_Re || sender == nudC_Im)
            {
                pbMandelbrotPreview.Invalidate();
            }

            ScheduleRender();
        }

        private void ParamControl_Changed_WithMapUpdate(object sender, EventArgs e)
        {
            if (_isHighResRendering)
            {
                return;
            }

            RenderMandelbrotMapAsync();
            ScheduleRender();
        }

        // Обработчики кнопок, созданные через дизайнер, остаются здесь.
        private void btnSaveHighRes_Click(object sender, EventArgs e)
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

        private void btnConfigurePalette_Click(object sender, EventArgs e)
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

        private void btnStateManager_Click(object sender, EventArgs e)
        {
            using (var dialog = new SaveLoadDialogForm(this))
            {
                dialog.ShowDialog(this);
            }
        }
        #endregion

        #region Map Preview Logic
        private async void RenderMandelbrotMapAsync()
        {
            if (pbMandelbrotPreview.Width <= 0 || pbMandelbrotPreview.Height <= 0)
            {
                return;
            }

            int w = pbMandelbrotPreview.Width;
            int h = pbMandelbrotPreview.Height;

            var mapEngine = new NovaMandelbrotEngine
            {
                P = new ComplexDecimal(nudP_Re.Value, nudP_Im.Value),
                M = nudM.Value,
                Z0 = new ComplexDecimal(nudZ0_Re.Value, nudZ0_Im.Value),
                MaxIterations = MAP_PREVIEW_ITERATIONS,
                ThresholdSquared = 100.0m,
                Scale = (decimal)(MAP_MAX_RE - MAP_MIN_RE),
                Palette = (iter, max, maxColor) =>
                {
                    if (iter == max)
                    {
                        return Color.Black;
                    }
                    double t = (double)(iter % 20) / 20;
                    int r = (int)(Math.Min(255, t * 3 * 255));
                    int g = (int)(Math.Min(255, Math.Max(0, (t - 0.33) * 3 * 255)));
                    int b = (int)(Math.Min(255, Math.Max(0, (t - 0.66) * 3 * 255)));
                    return Color.FromArgb(r, g, b);
                }
            };

            try
            {
                Bitmap mapBmp = await Task.Run(() => mapEngine.RenderToBitmap(w, h, 1, _ => { }, CancellationToken.None));

                if (pbMandelbrotPreview.IsHandleCreated && !pbMandelbrotPreview.IsDisposed)
                {
                    pbMandelbrotPreview.Invoke((Action)(() =>
                    {
                        var old = pbMandelbrotPreview.Image;
                        pbMandelbrotPreview.Image = mapBmp;
                        old?.Dispose();
                    }));
                }
                else
                {
                    mapBmp.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to render Mandelbrot map: {ex.Message}");
            }
        }

        private void PbMandelbrotPreview_Paint(object sender, PaintEventArgs e)
        {
            if (pbMandelbrotPreview.Image == null)
            {
                return;
            }

            double cRe = (double)nudC_Re.Value;
            double cIm = (double)nudC_Im.Value;
            double reRange = MAP_MAX_RE - MAP_MIN_RE;
            double imRange = MAP_MAX_IM - MAP_MIN_IM;
            if (reRange <= 0 || imRange <= 0)
            {
                return;
            }

            int x = (int)((cRe - MAP_MIN_RE) / reRange * pbMandelbrotPreview.Width);
            int y = (int)((MAP_MAX_IM - cIm) / imRange * pbMandelbrotPreview.Height);

            using (var pen = new Pen(Color.Lime, 2))
            {
                e.Graphics.DrawLine(pen, x - 6, y, x + 6, y);
                e.Graphics.DrawLine(pen, x, y - 6, x, y + 6);
            }
        }

        private void PbMandelbrotPreview_Click(object sender, EventArgs e)
        {
            if (_selectorForm == null || _selectorForm.IsDisposed)
            {
                _selectorForm = new NovaMandelbrotSelectorForm(this,
                    (double)nudC_Re.Value, (double)nudC_Im.Value,
                    MAP_MIN_RE, MAP_MAX_RE, MAP_MIN_IM, MAP_MAX_IM,
                    new Complex((double)nudP_Re.Value, (double)nudP_Im.Value),
                    (double)nudM.Value,
                    new Complex((double)nudZ0_Re.Value, (double)nudZ0_Im.Value)
                );

                _selectorForm.CoordinatesSelected += (re, im) => { nudC_Re.Value = (decimal)re; nudC_Im.Value = (decimal)im; };
                _selectorForm.FormClosed += (s, args) => _selectorForm = null;
                _selectorForm.Show(this);
            }
            else
            {
                _selectorForm.Activate();
                _selectorForm.SetSelectedCoordinates((double)nudC_Re.Value, (double)nudC_Im.Value, true);
            }
        }
        #endregion

        #region Canvas Interaction
        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering || canvas.Width <= 0 || canvas.Height <= 0)
            {
                return;
            }

            CommitAndBakePreview();
            decimal zoomFactor = e.Delta > 0 ? 1.5m : 1.0m / 1.5m;
            decimal scaleBeforeZoom = BASE_SCALE / _zoom;
            decimal mouseReal = _centerX + (e.X - canvas.Width / 2.0m) * scaleBeforeZoom / canvas.Width;
            decimal mouseImaginary = _centerY - (e.Y - canvas.Height / 2.0m) * scaleBeforeZoom / canvas.Height;
            _zoom = Math.Max((decimal)nudZoom.Minimum, Math.Min((decimal)nudZoom.Maximum, _zoom * zoomFactor));
            decimal scaleAfterZoom = BASE_SCALE / _zoom;
            _centerX = mouseReal - (e.X - canvas.Width / 2.0m) * scaleAfterZoom / canvas.Width;
            _centerY = mouseImaginary + (e.Y - canvas.Height / 2.0m) * scaleAfterZoom / canvas.Height;
            canvas.Invalidate();

            if (nudZoom.Value != _zoom)
            {
                nudZoom.Value = _zoom;
            }
            else
            {
                ScheduleRender();
            }
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering)
            {
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                _panning = true;
                _panStart = e.Location;
                canvas.Cursor = Cursors.Hand;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering || !_panning || canvas.Width <= 0)
            {
                return;
            }

            CommitAndBakePreview();
            decimal unitsPerPixel = BASE_SCALE / _zoom / canvas.Width;
            _centerX -= (decimal)(e.X - _panStart.X) * unitsPerPixel;
            _centerY += (decimal)(e.Y - _panStart.Y) * unitsPerPixel;
            _panStart = e.Location;
            canvas.Invalidate();
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering)
            {
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                _panning = false;
                canvas.Cursor = Cursors.Default;
                ScheduleRender();
            }
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            if (canvas.Width <= 0 || canvas.Height <= 0)
            {
                e.Graphics.Clear(Color.Black);
                return;
            }

            e.Graphics.Clear(Color.Black);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            lock (_bitmapLock)
            {
                DrawTransformedBitmap(e.Graphics, _previewBitmap, _renderedCenterX, _renderedCenterY, _renderedZoom, _centerX, _centerY, _zoom);
                if (_currentRenderingBitmap != null)
                {
                    e.Graphics.DrawImageUnscaled(_currentRenderingBitmap, Point.Empty);
                }
            }

            if (_renderVisualizer != null && _isRenderingPreview)
            {
                _renderVisualizer.DrawVisualization(e.Graphics);
            }
        }
        #endregion

        #region Rendering Logic
        private void ScheduleRender(bool force = false)
        {
            if (_isHighResRendering || WindowState == FormWindowState.Minimized)
            {
                return;
            }

            _previewRenderCts?.Cancel();
            _renderDebounceTimer.Stop();

            if (force)
            {
                RenderDebounceTimer_Tick(null, EventArgs.Empty);
            }
            else
            {
                _renderDebounceTimer.Start();
            }
        }

        private async void RenderDebounceTimer_Tick(object sender, EventArgs e)
        {
            _renderDebounceTimer.Stop();
            if (_isHighResRendering || _isRenderingPreview)
            {
                ScheduleRender(); // Reschedule if busy
                return;
            }

            try
            {
                int ssaaFactor = GetSelectedSsaaFactor();
                await StartPreviewRenderInternal(ssaaFactor);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Render failed: {ex}");
                MessageBox.Show($"Произошла ошибка рендеринга: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _isRenderingPreview = false;
                if (IsHandleCreated && !IsDisposed)
                {
                    this.Text = _baseTitle + " - Ошибка";
                }
            }
        }

        private async Task StartPreviewRenderInternal(int ssaaFactor)
        {
            if (canvas.Width <= 0 || canvas.Height <= 0)
            {
                return;
            }

            _isRenderingPreview = true;
            _previewRenderCts = new CancellationTokenSource();
            var token = _previewRenderCts.Token;
            _renderVisualizer?.NotifyRenderSessionStart();

            var stopwatch = Stopwatch.StartNew();
            string statusText = ssaaFactor > 1 ? $"Рендер (SSAA {ssaaFactor}x)..." : "Рендер...";
            if (IsHandleCreated)
            {
                this.Text = $"{_baseTitle} - {statusText}";
            }

            var newRenderingBitmap = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppArgb);
            lock (_bitmapLock)
            {
                _currentRenderingBitmap = newRenderingBitmap;
            }

            UpdateEngineParameters();
            var renderEngineCopy = new NovaJuliaEngine();
            renderEngineCopy.CopyParametersFrom(_fractalEngine);

            var threadCount = GetThreadCount();
            int tileConcurrency = threadCount;
            int inTileConcurrency = 1;
            var tiles = GenerateTiles(canvas.Width, canvas.Height);
            var dispatcher = new TileRenderDispatcher(tiles, tileConcurrency, RenderPatternSettings.SelectedPattern);

            if (pbRenderProgress.IsHandleCreated)
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

                    byte[] tileBuffer;
                    if (ssaaFactor > 1)
                    {
                        // Не допускаем вложенный oversubscription: параллелим по плиткам, внутри плитки — один поток.
                        tileBuffer = renderEngineCopy.RenderSingleTileSSAA(tile, canvas.Width, canvas.Height, ssaaFactor, inTileConcurrency, out _);
                    }
                    else
                    {
                        tileBuffer = renderEngineCopy.RenderSingleTile(tile, canvas.Width, canvas.Height, out _);
                    }

                    ct.ThrowIfCancellationRequested();

                    lock (_bitmapLock)
                    {
                        if (ct.IsCancellationRequested || _currentRenderingBitmap != newRenderingBitmap)
                        {
                            return;
                        }

                        var bmpData = _currentRenderingBitmap.LockBits(tile.Bounds, ImageLockMode.WriteOnly, _currentRenderingBitmap.PixelFormat);
                        int bytesPerPixel = Image.GetPixelFormatSize(_currentRenderingBitmap.PixelFormat) / 8;
                        int tileWidthInBytes = tile.Bounds.Width * bytesPerPixel;
                        for (int y = 0; y < tile.Bounds.Height; y++)
                        {
                            IntPtr destPtr = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                            int srcOffset = y * tileWidthInBytes;
                            Marshal.Copy(tileBuffer, srcOffset, destPtr, tileWidthInBytes);
                        }
                        _currentRenderingBitmap.UnlockBits(bmpData);
                    }

                    _renderVisualizer?.NotifyTileRenderComplete(tile.Bounds);
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        if (canvas.IsHandleCreated)
                        {
                            canvas.Invoke((Action)(() =>
                            {
                                if (!ct.IsCancellationRequested && pbRenderProgress.IsHandleCreated)
                                {
                                    pbRenderProgress.Value = Math.Min(pbRenderProgress.Maximum, Interlocked.Increment(ref progress));
                                }
                            }));
                        }
                    }
                    catch (ObjectDisposedException) { }

                    await Task.Yield();
                }, token);

                token.ThrowIfCancellationRequested();
                stopwatch.Stop();
                if (IsHandleCreated)
                {
                    this.Text = $"{_baseTitle} - Готово за {stopwatch.Elapsed.TotalSeconds:F3} сек.";
                }

                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _previewBitmap?.Dispose();
                        _previewBitmap = _currentRenderingBitmap;
                        _currentRenderingBitmap = null;
                        _renderedCenterX = _centerX;
                        _renderedCenterY = _centerY;
                        _renderedZoom = _zoom;
                    }
                    else
                    {
                        newRenderingBitmap?.Dispose();
                    }
                }
                if (canvas.IsHandleCreated)
                {
                    canvas.Invalidate();
                }
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
            catch (Exception)
            {
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _currentRenderingBitmap = null;
                    }
                }
                newRenderingBitmap?.Dispose();
                throw;
            }
            finally
            {
                _isRenderingPreview = false;
                _renderVisualizer?.NotifyRenderSessionComplete();
                try
                {
                    if (pbRenderProgress.IsHandleCreated)
                    {
                        pbRenderProgress.Invoke((Action)(() => pbRenderProgress.Value = 0));
                    }
                }
                catch (ObjectDisposedException) { }
            }
        }

        private void OnVisualizerNeedsRedraw()
        {
            if (canvas.IsHandleCreated && !canvas.IsDisposed)
            {
                canvas.BeginInvoke((Action)(() => canvas.Invalidate()));
            }
        }
        #endregion


        private void Canvas_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                return;
            }

            if (_suppressResizeRender)
            {
                canvas.Invalidate();
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

        #region Utility Methods
        private void CommitAndBakePreview()
        {
            _previewRenderCts?.Cancel();

            lock (_bitmapLock)
            {
                if (!_isRenderingPreview || _currentRenderingBitmap == null)
                {
                    return;
                }

                var bakedBitmap = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(bakedBitmap))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    if (_previewBitmap != null)
                    {
                        DrawTransformedBitmap(g, _previewBitmap, _renderedCenterX, _renderedCenterY, _renderedZoom, _centerX, _centerY, _zoom);
                    }
                    g.DrawImageUnscaled(_currentRenderingBitmap, Point.Empty);
                }

                _previewBitmap?.Dispose();
                _previewBitmap = bakedBitmap;
                _currentRenderingBitmap = null; // Detach, don't dispose. Let the background threads finish gracefully.

                _renderedCenterX = _centerX;
                _renderedCenterY = _centerY;
                _renderedZoom = _zoom;
            }
        }

        private void DrawTransformedBitmap(Graphics g, Bitmap bmp, decimal srcCenterX, decimal srcCenterY, decimal srcZoom, decimal destCenterX, decimal destCenterY, decimal destZoom)
        {
            if (bmp == null || g == null || srcZoom <= 0 || destZoom <= 0 || canvas.Width <= 0)
            {
                return;
            }

            decimal renderedScale = BASE_SCALE / srcZoom;
            decimal currentScale = BASE_SCALE / destZoom;
            float drawScaleRatio = (float)(renderedScale / currentScale);

            float newWidth = canvas.Width * drawScaleRatio;
            float newHeight = canvas.Height * drawScaleRatio;

            float offsetX = (float)((srcCenterX - destCenterX) / currentScale * canvas.Width);
            float offsetY = (float)(-(srcCenterY - destCenterY) / currentScale * canvas.Width);

            float drawX = (canvas.Width - newWidth) / 2.0f + offsetX;
            float drawY = (canvas.Height - newHeight) / 2.0f + offsetY;

            g.DrawImage(bmp, drawX, drawY, newWidth, newHeight);
        }

        private void UpdateEngineParameters()
        {
            _fractalEngine.MaxIterations = (int)nudIterations.Value;
            _fractalEngine.ThresholdSquared = nudThreshold.Value * nudThreshold.Value;
            _fractalEngine.CenterX = _centerX;
            _fractalEngine.CenterY = _centerY;
            _fractalEngine.Scale = BASE_SCALE / _zoom;
            _fractalEngine.UseSmoothColoring = IsSmoothColoringEnabled();
            _fractalEngine.P = new ComplexDecimal(nudP_Re.Value, nudP_Im.Value);
            _fractalEngine.Z0 = new ComplexDecimal(nudZ0_Re.Value, nudZ0_Im.Value);
            _fractalEngine.M = nudM.Value;
            _fractalEngine.JuliaC = new ComplexDecimal(nudC_Re.Value, nudC_Im.Value);
            ApplyActivePalette();
        }

        private List<TileInfo> GenerateTiles(int width, int height)
        {
            var tiles = new List<TileInfo>();
            Point center = new Point(width / 2, height / 2);
            for (int y = 0; y < height; y += TILE_SIZE)
            {
                for (int x = 0; x < width; x += TILE_SIZE)
                {
                    tiles.Add(new TileInfo(x, y, Math.Min(TILE_SIZE, width - x), Math.Min(TILE_SIZE, height - y)));
                }
            }
            return tiles.OrderBy(t => Math.Pow(t.Center.X - center.X, 2) + Math.Pow(t.Center.Y - center.Y, 2)).ToList();
        }

        private int GetThreadCount()
        {
            if (cbThreads.SelectedItem?.ToString() == "Auto")
            {
                return Environment.ProcessorCount;
            }
            return Convert.ToInt32(cbThreads.SelectedItem);
        }

        private bool IsSmoothColoringEnabled()
        {
            return cbSmooth.SelectedIndex == 1;
        }

        private int GetSelectedSsaaFactor()
        {
            switch (cbSSAA.SelectedIndex)
            {
                case 1:
                    return 2;
                case 2:
                    return 4;
                default:
                    return 1;
            }
        }
        #endregion

        #region Palette Management
        private Func<double, Color> GenerateSmoothPaletteFunction(Palette palette, int effectiveMaxColorIterations)
        {
            double gamma = palette.Gamma;
            var colors = new List<Color>(palette.Colors);
            int colorCount = colors.Count;

            if (palette.Name == "Стандартный серый")
            {
                return (smoothIter) =>
                {
                    if (smoothIter >= _fractalEngine.MaxIterations)
                    {
                        return Color.White;
                    }

                    if (smoothIter < 0)
                    {
                        smoothIter = 0;
                    }

                    double logMax = Math.Log(_fractalEngine.MaxIterations + 1);
                    if (logMax <= 0)
                    {
                        return Color.Black;
                    }
                    double tLog = Math.Log(smoothIter + 1) / logMax;
                    int grayValue = (int)Math.Max(0, Math.Min(255, 255.0 * tLog));
                    return ColorCorrection.ApplyGamma(Color.FromArgb(grayValue, grayValue, grayValue), gamma);
                };
            }

            if (effectiveMaxColorIterations <= 0 || colorCount == 0)
            {
                return _ => Color.Black;
            }

            if (colorCount == 1)
            {
                return _ => (_ >= _fractalEngine.MaxIterations) ? Color.Black : ColorCorrection.ApplyGamma(colors[0], gamma);
            }

            return (smoothIter) =>
            {
                if (smoothIter >= _fractalEngine.MaxIterations)
                {
                    return Color.Black;
                }

                if (smoothIter < 0)
                {
                    smoothIter = 0;
                }

                double t = (smoothIter % effectiveMaxColorIterations) / (double)effectiveMaxColorIterations;
                double scaledT = t * (colorCount - 1);
                int index1 = (int)Math.Floor(scaledT);
                int index2 = Math.Min(index1 + 1, colorCount - 1);
                double localT = scaledT - index1;
                Color baseColor = LerpColor(colors[index1], colors[index2], localT);
                return ColorCorrection.ApplyGamma(baseColor, gamma);
            };
        }

        private string GeneratePaletteSignature(Palette palette, int maxIterationsForAlignment)
        {
            var sb = new StringBuilder();
            sb.Append(palette.Name).Append(':');
            foreach (var color in palette.Colors)
            {
                sb.Append(color.ToArgb().ToString("X8"));
            }
            int effectiveMaxColorIterations = palette.AlignWithRenderIterations ? maxIterationsForAlignment : palette.MaxColorIterations;
            sb.Append(':').Append(palette.IsGradient)
              .Append(':').Append(palette.Gamma.ToString("F2", CultureInfo.InvariantCulture))
              .Append(':').Append(effectiveMaxColorIterations);
            return sb.ToString();
        }

        private void ApplyActivePalette()
        {
            if (_fractalEngine == null || _paletteManager.ActivePalette == null)
            {
                return;
            }

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
                if (activePalette.Name == "Стандартный серый" && iter == maxIter)
                {
                    return Color.White;
                }

                if (iter == maxIter)
                {
                    return Color.Black;
                }

                int index = Math.Min(iter, _gammaCorrectedPaletteCache.Length - 1);
                return _gammaCorrectedPaletteCache[index];
            };
        }

        private Func<int, int, int, Color> GenerateDiscretePaletteFunction(Palette palette)
        {
            double gamma = palette.Gamma;
            var colors = new List<Color>(palette.Colors);
            int colorCount = colors.Count;

            if (palette.Name == "Стандартный серый")
            {
                return (iter, maxIter, maxColorIter) =>
                {
                    if (iter == maxIter)
                    {
                        return Color.White;
                    }

                    double logMax = Math.Log(maxColorIter + 1);
                    if (logMax <= 0)
                    {
                        return Color.Black;
                    }

                    double tLog = Math.Log(Math.Min(iter, maxColorIter) + 1) / logMax;
                    int cVal = (int)Math.Max(0, Math.Min(255, 255.0 * tLog));
                    return ColorCorrection.ApplyGamma(Color.FromArgb(cVal, cVal, cVal), gamma);
                };
            }

            if (colorCount == 0)
            {
                return (_, _, _) => Color.Black;
            }

            if (colorCount == 1)
            {
                return (iter, max, _) => ColorCorrection.ApplyGamma((iter == max) ? Color.Black : colors[0], gamma);
            }

            return (iter, maxIter, maxColorIter) =>
            {
                if (iter == maxIter)
                {
                    return Color.Black;
                }

                double normalizedIter = maxColorIter > 0 ? (double)Math.Min(iter, maxColorIter) / maxColorIter : 0;
                Color baseColor;
                if (palette.IsGradient)
                {
                    double scaledT = normalizedIter * (colorCount - 1);
                    int index1 = (int)Math.Floor(scaledT);
                    int index2 = Math.Min(index1 + 1, colorCount - 1);
                    double localT = scaledT - index1;
                    baseColor = LerpColor(colors[index1], colors[index2], localT);
                }
                else
                {
                    int colorIndex = Math.Min((int)(normalizedIter * colorCount), colorCount - 1);
                    baseColor = colors[colorIndex];
                }
                return ColorCorrection.ApplyGamma(baseColor, gamma);
            };
        }

        private static Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private void OnPaletteApplied(object sender, EventArgs e)
        {
            UpdateEngineParameters();
            ScheduleRender();
        }
        #endregion

        #region Form Lifecycle
        private void FractalNovaJuliaForm_Load(object sender, EventArgs e)
        {
            _baseTitle = this.Text;
            _fractalEngine = new NovaJuliaEngine();
            _paletteManager = new PaletteManager();
            _renderDebounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _renderVisualizer = new RenderVisualizerComponent(TILE_SIZE);

            InitializeControls();
            InitializeEventHandlers();

            _renderedCenterX = _centerX;
            _renderedCenterY = _centerY;
            _renderedZoom = _zoom;
            UpdateEngineParameters();
            UpdateToggleControlsPosition();
            ClearInitialControlSelection();
            RenderMandelbrotMapAsync();
            ScheduleRender();
        }

        private void FractalNovaJuliaForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            _renderDebounceTimer?.Stop();
            _renderDebounceTimer?.Dispose();
            _previewRenderCts?.Cancel();
            _previewRenderCts?.Dispose();

            lock (_bitmapLock)
            {
                _previewBitmap?.Dispose();
                _currentRenderingBitmap?.Dispose();
                pbMandelbrotPreview.Image?.Dispose();
            }

            if (_renderVisualizer != null)
            {
                _renderVisualizer.NeedsRedraw -= OnVisualizerNeedsRedraw;
                _renderVisualizer.Dispose();
            }
            _colorConfigForm?.Close();

            if (_mapPreviewThemeChangedHandler is not null)
            {
                ThemeManager.ThemeChanged -= _mapPreviewThemeChangedHandler;
                _mapPreviewThemeChangedHandler = null;
            }
        }
        #endregion

        #region Interface Implementations (IHighResRenderable, ISaveLoadCapableFractal, IFractalForm)
        public HighResRenderState GetRenderState()
        {
            string pReStr = nudP_Re.Value.ToString("F4", CultureInfo.InvariantCulture).Replace(".", "_");
            string pImStr = nudP_Im.Value.ToString("F4", CultureInfo.InvariantCulture).Replace(".", "_");
            string mStr = nudM.Value.ToString("F3", CultureInfo.InvariantCulture).Replace(".", "_");
            string cReStr = nudC_Re.Value.ToString("F4", CultureInfo.InvariantCulture).Replace(".", "_");
            string cImStr = nudC_Im.Value.ToString("F4", CultureInfo.InvariantCulture).Replace(".", "_");
            string fileNameDetails = $"NovaJulia_p{pReStr}_{pImStr}_c{cReStr}_{cImStr}";

            return new HighResRenderState
            {
                EngineType = "NovaJulia",
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                BaseScale = BASE_SCALE,
                Iterations = (int)nudIterations.Value,
                Threshold = nudThreshold.Value,
                ActivePaletteName = _paletteManager.ActivePalette?.Name ?? "Стандартный серый",
                FileNameDetails = fileNameDetails,
                UseSmoothColoring = IsSmoothColoringEnabled(),
                NovaP = new ComplexDecimal(nudP_Re.Value, nudP_Im.Value),
                NovaZ0 = new ComplexDecimal(nudZ0_Re.Value, nudZ0_Im.Value),
                NovaM = nudM.Value,
                JuliaC = new ComplexDecimal(nudC_Re.Value, nudC_Im.Value)
            };
        }

        private FractalNovaFamilyEngine CreateEngineFromState(HighResRenderState state, bool forPreview)
        {
            var engine = new NovaJuliaEngine
            {
                MaxIterations = forPreview ? Math.Min(state.Iterations, 150) : state.Iterations,
                ThresholdSquared = state.Threshold * state.Threshold,
                CenterX = state.CenterX,
                CenterY = state.CenterY,
                Scale = state.BaseScale / state.Zoom,
                UseSmoothColoring = state.UseSmoothColoring,
                P = state.NovaP ?? new ComplexDecimal(3, 0),
                Z0 = state.NovaZ0 ?? new ComplexDecimal(1, 0),
                M = state.NovaM ?? 1.0m,
                JuliaC = state.JuliaC ?? new ComplexDecimal(0, 1)
            };

            var paletteForRender = _paletteManager.Palettes.FirstOrDefault(p => p.Name == state.ActivePaletteName) ?? _paletteManager.Palettes.First();
            int effectiveMaxColorIterations = paletteForRender.AlignWithRenderIterations ? engine.MaxIterations : paletteForRender.MaxColorIterations;
            engine.MaxColorIterations = effectiveMaxColorIterations;
            engine.SmoothPalette = GenerateSmoothPaletteFunction(paletteForRender, effectiveMaxColorIterations);
            engine.Palette = GenerateDiscretePaletteFunction(paletteForRender);
            return engine;
        }

        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            _isHighResRendering = true;
            try
            {
                FractalNovaFamilyEngine renderEngine = CreateEngineFromState(state, forPreview: false);
                Action<int> progressCallback = p => progress.Report(new RenderProgress { Percentage = p, Status = "Рендеринг..." });
                return await renderEngine.RenderToBitmapSSAA(width, height, GetThreadCount(), progressCallback, ssaaFactor, cancellationToken);
            }
            finally
            {
                _isHighResRendering = false;
            }
        }

        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            var engine = CreateEngineFromState(state, forPreview: true);
            return engine.RenderToBitmap(previewWidth, previewHeight, 1, _ => { }, CancellationToken.None);
        }

        public string FractalTypeIdentifier => "NovaJulia";
        public Type ConcreteSaveStateType => typeof(NovaJuliaSaveState);

        public class NovaJuliaPreviewParams
        {
            public decimal CenterX { get; set; }
            public decimal CenterY { get; set; }
            public decimal Zoom { get; set; }
            public int Iterations { get; set; }
            public string PaletteName { get; set; }
            public decimal Threshold { get; set; }
            public decimal P_Re { get; set; }
            public decimal P_Im { get; set; }
            public decimal Z0_Re { get; set; }
            public decimal Z0_Im { get; set; }
            public decimal M { get; set; }
            public decimal C_Re { get; set; }
            public decimal C_Im { get; set; }
            public bool UseSmoothColoring { get; set; }
        }

        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new NovaJuliaSaveState(this.FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                Threshold = nudThreshold.Value,
                Iterations = (int)nudIterations.Value,
                PaletteName = _paletteManager.ActivePalette?.Name ?? "Стандартный серый",
                P_Re = nudP_Re.Value,
                P_Im = nudP_Im.Value,
                Z0_Re = nudZ0_Re.Value,
                Z0_Im = nudZ0_Im.Value,
                M = nudM.Value,
                C_Re = nudC_Re.Value,
                C_Im = nudC_Im.Value
            };
            var previewParams = new NovaJuliaPreviewParams
            {
                CenterX = state.CenterX,
                CenterY = state.CenterY,
                Zoom = state.Zoom,
                Iterations = state.Iterations,
                PaletteName = state.PaletteName,
                Threshold = state.Threshold,
                P_Re = state.P_Re,
                P_Im = state.P_Im,
                Z0_Re = state.Z0_Re,
                Z0_Im = state.Z0_Im,
                M = state.M,
                C_Re = state.C_Re,
                C_Im = state.C_Im,
                UseSmoothColoring = IsSmoothColoringEnabled()
            };
            state.PreviewParametersJson = JsonSerializer.Serialize(previewParams);
            return state;
        }

        public void LoadState(FractalSaveStateBase stateBase)
        {
            if (stateBase is NovaJuliaSaveState state)
            {
                _isRenderingPreview = false;
                _previewRenderCts?.Cancel();
                _renderDebounceTimer.Stop();

                _centerX = state.CenterX;
                _centerY = state.CenterY;
                _zoom = state.Zoom;

                nudZoom.Value = state.Zoom;
                nudThreshold.Value = state.Threshold;
                nudIterations.Value = state.Iterations;
                nudP_Re.Value = state.P_Re;
                nudP_Im.Value = state.P_Im;
                nudZ0_Re.Value = state.Z0_Re;
                nudZ0_Im.Value = state.Z0_Im;
                nudM.Value = state.M;
                nudC_Re.Value = state.C_Re;
                nudC_Im.Value = state.C_Im;

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
                RenderMandelbrotMapAsync();
                ScheduleRender(true);
            }
            else
            {
                MessageBox.Show("Несовместимый тип состояния для загрузки.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(state.PreviewParametersJson))
                {
                    return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];
                }

                NovaJuliaPreviewParams p;
                try
                {
                    p = JsonSerializer.Deserialize<NovaJuliaPreviewParams>(state.PreviewParametersJson);
                }
                catch
                {
                    return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];
                }

                var previewEngine = new NovaJuliaEngine
                {
                    CenterX = p.CenterX,
                    CenterY = p.CenterY,
                    Scale = BASE_SCALE / (p.Zoom > 0 ? p.Zoom : 0.001m),
                    MaxIterations = p.Iterations,
                    ThresholdSquared = p.Threshold * p.Threshold,
                    P = new ComplexDecimal(p.P_Re, p.P_Im),
                    Z0 = new ComplexDecimal(p.Z0_Re, p.Z0_Im),
                    M = p.M,
                    JuliaC = new ComplexDecimal(p.C_Re, p.C_Im),
                    UseSmoothColoring = p.UseSmoothColoring
                };
                var palette = _paletteManager.Palettes.FirstOrDefault(pal => pal.Name == p.PaletteName) ?? _paletteManager.Palettes.First();
                int maxColorIter = palette.AlignWithRenderIterations ? previewEngine.MaxIterations : palette.MaxColorIterations;
                previewEngine.MaxColorIterations = maxColorIter;

                if (previewEngine.UseSmoothColoring)
                {
                    previewEngine.SmoothPalette = GenerateSmoothPaletteFunction(palette, maxColorIter);
                }
                else
                {
                    previewEngine.Palette = GenerateDiscretePaletteFunction(palette);
                }

                return previewEngine.RenderSingleTile(tile, totalWidth, totalHeight, out _);
            });
        }

        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (string.IsNullOrEmpty(state.PreviewParametersJson))
            {
                var bmp = new Bitmap(previewWidth, previewHeight);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.DarkGray);
                }
                return bmp;
            }

            NovaJuliaPreviewParams p;
            try
            {
                p = JsonSerializer.Deserialize<NovaJuliaPreviewParams>(state.PreviewParametersJson);
            }
            catch
            {
                var bmp = new Bitmap(previewWidth, previewHeight);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.DarkRed);
                }
                return bmp;
            }

            var previewEngine = new NovaJuliaEngine
            {
                CenterX = p.CenterX,
                CenterY = p.CenterY,
                Scale = BASE_SCALE / (p.Zoom > 0 ? p.Zoom : 0.001m),
                MaxIterations = p.Iterations,
                ThresholdSquared = p.Threshold * p.Threshold,
                P = new ComplexDecimal(p.P_Re, p.P_Im),
                Z0 = new ComplexDecimal(p.Z0_Re, p.Z0_Im),
                M = p.M,
                JuliaC = new ComplexDecimal(p.C_Re, p.C_Im),
                UseSmoothColoring = p.UseSmoothColoring
            };
            var palette = _paletteManager.Palettes.FirstOrDefault(pal => pal.Name == p.PaletteName) ?? _paletteManager.Palettes.First();
            int maxColorIter = palette.AlignWithRenderIterations ? previewEngine.MaxIterations : palette.MaxColorIterations;
            previewEngine.MaxColorIterations = maxColorIter;

            if (previewEngine.UseSmoothColoring)
            {
                previewEngine.SmoothPalette = GenerateSmoothPaletteFunction(palette, maxColorIter);
            }
            else
            {
                previewEngine.Palette = GenerateDiscretePaletteFunction(palette);
            }

            return previewEngine.RenderToBitmap(previewWidth, previewHeight, 1, _ => { }, CancellationToken.None);
        }

        public List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            return SaveFileManager.LoadSaves<NovaJuliaSaveState>(this.FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();
        }

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            SaveFileManager.SaveSaves(this.FractalTypeIdentifier, saves.Cast<NovaJuliaSaveState>().ToList());
        }

        public double LoupeZoom => 4.0;
        public event EventHandler LoupeZoomChanged;
        #endregion
    }
}
