using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using FractalExplorer.Engines;
using FractalExplorer.Forms.Other;
using FractalExplorer.Resources;
using FractalExplorer.Utilities;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.Coloring;
using FractalExplorer.SelectorsForms;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Drawing.Drawing2D;
using FractalExplorer.Utilities.UI;

namespace FractalExplorer.Forms.Fractals
{
    public partial class FractalLyapunovForm : Form, ISaveLoadCapableFractal, IHighResRenderable
    {
        private const int PreviewTileSize = 16;
        private const string AutoThreadOptionText = "Авто";
        private const decimal DefaultAMin = 2.8m;
        private const decimal DefaultAMax = 4.0m;
        private const decimal DefaultBMin = 2.8m;
        private const decimal DefaultBMax = 4.0m;
        private const decimal MinADomain = 0m;
        private const decimal MaxADomain = 4m;
        private const decimal MinBDomain = -8m;
        private const decimal MaxBDomain = 8m;

        private readonly FractalLyapunovEngine _engine = new();
        private readonly LyapunovPaletteManager _paletteManager = new();
        private ColorConfigurationLyapunovForm? _lyapunovColorConfigForm;
        private readonly object _frameBufferLock = new();
        private readonly RenderVisualizerComponent _renderVisualizer = new(PreviewTileSize);
        private readonly System.Windows.Forms.Timer _renderRestartTimer = new() { Interval = 350 };
        private readonly HashSet<Rectangle> _currentRenderedTiles = new();
        private Bitmap? _previewFrameBuffer;
        private Bitmap? _currentRenderingFrameBuffer;
        private CancellationTokenSource? _previewRenderCts;
        private int _controlsOpenWidth = 231;
        private bool _isRenderingPreview;
        private bool _suppressViewportSync;
        private bool _isPanning;
        private bool _isUserResizingWindow;
        private bool _hasPendingCanvasResizeRender;
        private bool _isQueuingRenderRestart;
        private bool _isHighResRendering;
        private readonly FullscreenToggleController _fullscreenController = new();
        private Point _panStartPoint;
        private decimal _renderedAMin = DefaultAMin;
        private decimal _renderedAMax = DefaultAMax;
        private decimal _renderedBMin = DefaultBMin;
        private decimal _renderedBMax = DefaultBMax;
        private readonly string _baseTitle;

        public FractalLyapunovForm()
        {
            Text = "Фрактал Ляпунова";
            Width = 1280;
            Height = 780;
            StartPosition = FormStartPosition.CenterScreen;
            InitializeComponent();
            _baseTitle = Text;
            KeyPreview = true;
            ApplyDefaults();
            Shown += HandleFormShown;
            KeyDown += Form_KeyDown;
            FormClosing += (_, __) =>
            {
                ExitFullscreenSafely();
                CancelPreviewRender();
                _renderRestartTimer.Stop();
                _renderRestartTimer.Tick -= RenderRestartTimer_Tick;
                _renderRestartTimer.Dispose();
                lock (_frameBufferLock)
                {
                    _previewFrameBuffer?.Dispose();
                    _previewFrameBuffer = null;
                    _currentRenderingFrameBuffer?.Dispose();
                    _currentRenderingFrameBuffer = null;
                    _currentRenderedTiles.Clear();
                }
                _lyapunovColorConfigForm?.Dispose();
                _lyapunovColorConfigForm = null;
                _renderVisualizer.Dispose();
            };
            _renderVisualizer.NeedsRedraw += () => _canvas.Invalidate();
            _canvas.Paint += Canvas_Paint;
            _canvas.MouseWheel += Canvas_MouseWheel;
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseLeave += Canvas_MouseLeave;
            _canvas.MouseEnter += (_, _) => _canvas.Focus();
            ResizeBegin += (_, _) => _isUserResizingWindow = true;
            ResizeEnd += (_, _) =>
            {
                _isUserResizingWindow = false;
                if (_hasPendingCanvasResizeRender)
                {
                    _hasPendingCanvasResizeRender = false;
                    QueueRenderRestart(immediate: true);
                }
            };
            _renderRestartTimer.Tick += RenderRestartTimer_Tick;
            AttachAutoRenderControlTriggers();
        }

        private async void HandleFormShown(object? sender, EventArgs e)
        {
            Shown -= HandleFormShown;
            await RenderAsync();
            _canvas.SizeChanged += Canvas_SizeChanged;
        }

        private void ToggleFullscreenSafely()
        {
            _fullscreenController.Toggle(this);
            UpdateOverlayLayout();
            QueueRenderRestart(immediate: true);
        }

        private void ExitFullscreenSafely()
        {
            if (!_fullscreenController.IsFullscreen(this))
            {
                return;
            }

            _fullscreenController.ExitFullscreen(this);
            UpdateOverlayLayout();
            QueueRenderRestart(immediate: true);
        }

        private void Form_KeyDown(object? sender, KeyEventArgs e)
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

        private void ToggleControls()
        {
            bool hide = _controlsHost.Visible && _controlsHost.Width > 0;
            if (hide)
            {
                _controlsOpenWidth = _controlsHost.Width > 0 ? _controlsHost.Width : _controlsOpenWidth;
                _controlsHost.Visible = false;
                _controlsHost.Width = 0;
                _btnToggleControls.Text = "☰";
                _btnToggleControls.Location = new Point(12, 12);
            }
            else
            {
                _controlsHost.Width = _controlsOpenWidth > 0 ? _controlsOpenWidth : 231;
                _controlsHost.Visible = true;
                _btnToggleControls.Text = "✕";
                _btnToggleControls.Location = new Point(_controlsHost.Width + 25, 12);
            }
        }

        private void UpdateOverlayLayout()
        {
            _controlsHost.Height = ClientSize.Height;
            if (_controlsHost.Visible && _controlsHost.Width > 0)
            {
                _btnToggleControls.Location = new Point(_controlsHost.Width + 25, 12);
            }
        }

        private void ApplyDefaults()
        {
            ConfigureDecimal(_nudAMin, 2, 0.01m, MinADomain, MaxADomain, DefaultAMin);
            ConfigureDecimal(_nudAMax, 2, 0.01m, MinADomain, MaxADomain, DefaultAMax);
            ConfigureDecimal(_nudBMin, 2, 0.01m, MinBDomain, MaxBDomain, DefaultBMin);
            ConfigureDecimal(_nudBMax, 2, 0.01m, MinBDomain, MaxBDomain, DefaultBMax);

            _nudIterations.Minimum = 20;
            _nudIterations.Maximum = 5000;
            _nudIterations.Value = 300;

            _nudTransient.Minimum = 0;
            _nudTransient.Maximum = 3000;
            _nudTransient.Value = 100;

            int cores = Environment.ProcessorCount;
            _cbThreads.Items.Clear();
            for (int i = 1; i <= cores; i++)
            {
                _cbThreads.Items.Add(i);
            }
            _cbThreads.Items.Add(AutoThreadOptionText);
            _cbThreads.SelectedItem = AutoThreadOptionText;

            ConfigureDecimal(_nudZoom, 4, 0.001m, 0.001m, 2000000m, 1.0m);
            _cbSSAA.Items.AddRange(new object[] { "Выкл (1x)", "Низкое (2x)", "Высокое (4x)" });
            _cbSSAA.SelectedIndex = 0;

            _tbPattern.Text = "AB";
            _pbRenderProgress.Value = 0;
            _engine.ColorPalette = _paletteManager.ActivePalette;

            _nudZoom.ValueChanged += (_, _) => ApplyZoomFromControl();
            _nudAMin.ValueChanged += (_, _) => SyncViewportFromRangeControls();
            _nudAMax.ValueChanged += (_, _) => SyncViewportFromRangeControls();
            _nudBMin.ValueChanged += (_, _) => SyncViewportFromRangeControls();
            _nudBMax.ValueChanged += (_, _) => SyncViewportFromRangeControls();
        }

        private void AttachAutoRenderControlTriggers()
        {
            _nudAMin.ValueChanged += (_, _) => QueueRenderRestart();
            _nudAMax.ValueChanged += (_, _) => QueueRenderRestart();
            _nudBMin.ValueChanged += (_, _) => QueueRenderRestart();
            _nudBMax.ValueChanged += (_, _) => QueueRenderRestart();
            _nudIterations.ValueChanged += (_, _) => QueueRenderRestart();
            _nudTransient.ValueChanged += (_, _) => QueueRenderRestart();
            _cbThreads.SelectedIndexChanged += (_, _) => QueueRenderRestart();
            _nudZoom.ValueChanged += (_, _) => QueueRenderRestart();
            _cbSSAA.SelectedIndexChanged += (_, _) => QueueRenderRestart();
            _tbPattern.TextChanged += (_, _) => QueueRenderRestart();
            _tbPattern.Leave += (_, _) => QueueRenderRestart(immediate: true);
            _tbPattern.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    QueueRenderRestart(immediate: true);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
        }

        private void QueueRenderRestart(bool immediate = false)
        {
            if (IsDisposed)
            {
                return;
            }

            _isQueuingRenderRestart = true;
            if (immediate)
            {
                _renderRestartTimer.Stop();
                _ = TriggerQueuedRenderRestartAsync();
                return;
            }

            _renderRestartTimer.Stop();
            _renderRestartTimer.Start();
        }

        private async void RenderRestartTimer_Tick(object? sender, EventArgs e)
        {
            _renderRestartTimer.Stop();
            await TriggerQueuedRenderRestartAsync();
        }

        private async Task TriggerQueuedRenderRestartAsync()
        {
            if (!_isQueuingRenderRestart || _isPanning)
            {
                return;
            }

            _isQueuingRenderRestart = false;
            await RenderAsync();
        }

        private void Canvas_SizeChanged(object? sender, EventArgs e)
        {
            if (_canvas.Width <= 1 || _canvas.Height <= 1)
            {
                return;
            }

            if (_isUserResizingWindow)
            {
                _hasPendingCanvasResizeRender = true;
                return;
            }

            QueueRenderRestart(immediate: true);
        }

        private static void ConfigureDecimal(NumericUpDown nud, int decimals, decimal increment, decimal min, decimal max, decimal value)
        {
            nud.DecimalPlaces = decimals;
            nud.Increment = increment;
            nud.Minimum = min;
            nud.Maximum = max;
            nud.Value = value;
        }

        private void Canvas_Paint(object? sender, PaintEventArgs e)
        {
            lock (_frameBufferLock)
            {
                bool drewStableFrame = false;
                if (_previewFrameBuffer != null)
                {
                    bool sameViewport =
                        _renderedAMin == _nudAMin.Value &&
                        _renderedAMax == _nudAMax.Value &&
                        _renderedBMin == _nudBMin.Value &&
                        _renderedBMax == _nudBMax.Value;

                    if (sameViewport)
                    {
                        e.Graphics.DrawImage(_previewFrameBuffer, _canvas.ClientRectangle);
                    }
                    else
                    {
                        DrawTransformedFrame(e.Graphics, _previewFrameBuffer, _canvas.ClientRectangle);
                    }

                    drewStableFrame = true;
                }

                bool hasWorkingTiles = _currentRenderingFrameBuffer != null && _currentRenderedTiles.Count > 0;
                if (hasWorkingTiles && _currentRenderingFrameBuffer != null)
                {
                    foreach (Rectangle tile in _currentRenderedTiles)
                    {
                        e.Graphics.DrawImage(_currentRenderingFrameBuffer, tile, tile, GraphicsUnit.Pixel);
                    }
                }

                if (!drewStableFrame && !hasWorkingTiles)
                {
                    e.Graphics.Clear(Color.Black);
                }
            }

            if (_isRenderingPreview)
            {
                _renderVisualizer.DrawVisualization(e.Graphics);
            }
        }

        private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (_canvas.Width <= 0 || _canvas.Height <= 0)
            {
                return;
            }

            CommitAndBakePreview();
            decimal factor = e.Delta > 0 ? 1.5m : 1.0m / 1.5m;
            decimal nextZoom = Math.Max(_nudZoom.Minimum, Math.Min(_nudZoom.Maximum, _nudZoom.Value * factor));
            ApplyZoom(nextZoom, e.X, e.Y);
            _canvas.Invalidate();
            QueueRenderRestart();
        }

        private void Canvas_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _canvas.Width <= 0 || _canvas.Height <= 0)
            {
                return;
            }

            CommitAndBakePreview();
            _isPanning = true;
            _panStartPoint = e.Location;
            _canvas.Cursor = Cursors.Hand;
        }

        private void Canvas_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isPanning || _canvas.Width <= 0 || _canvas.Height <= 0)
            {
                return;
            }

            decimal deltaX = e.X - _panStartPoint.X;
            decimal deltaY = e.Y - _panStartPoint.Y;
            _panStartPoint = e.Location;

            decimal aRange = _nudAMax.Value - _nudAMin.Value;
            decimal bRange = _nudBMax.Value - _nudBMin.Value;
            if (aRange <= 0 || bRange <= 0)
            {
                return;
            }

            decimal shiftA = (deltaX / Math.Max(1, _canvas.Width)) * aRange;
            decimal shiftB = (deltaY / Math.Max(1, _canvas.Height)) * bRange;

            decimal newAMin = _nudAMin.Value - shiftA;
            decimal newAMax = _nudAMax.Value - shiftA;
            decimal newBMin = _nudBMin.Value + shiftB;
            decimal newBMax = _nudBMax.Value + shiftB;
            ClampRange(ref newAMin, ref newAMax, MinADomain, MaxADomain);
            ClampRange(ref newBMin, ref newBMax, MinBDomain, MaxBDomain);

            _suppressViewportSync = true;
            _nudAMin.Value = Math.Max(_nudAMin.Minimum, Math.Min(_nudAMin.Maximum, newAMin));
            _nudAMax.Value = Math.Max(_nudAMax.Minimum, Math.Min(_nudAMax.Maximum, newAMax));
            _nudBMin.Value = Math.Max(_nudBMin.Minimum, Math.Min(_nudBMin.Maximum, newBMin));
            _nudBMax.Value = Math.Max(_nudBMax.Minimum, Math.Min(_nudBMax.Maximum, newBMax));
            _suppressViewportSync = false;

            _canvas.Invalidate();
        }

        private void Canvas_MouseUp(object? sender, MouseEventArgs e)
        {
            bool wasPanning = _isPanning;
            _isPanning = false;
            _canvas.Cursor = Cursors.Default;
            if (wasPanning)
            {
                QueueRenderRestart(immediate: true);
            }
        }

        private void Canvas_MouseLeave(object? sender, EventArgs e)
        {
            bool wasPanning = _isPanning;
            _isPanning = false;
            _canvas.Cursor = Cursors.Default;
            if (wasPanning)
            {
                QueueRenderRestart(immediate: true);
            }
        }

        private void SyncViewportFromRangeControls()
        {
            if (_suppressViewportSync)
            {
                return;
            }

            decimal currentRange = _nudAMax.Value - _nudAMin.Value;
            if (currentRange <= 0)
            {
                return;
            }

            decimal baseRange = DefaultAMax - DefaultAMin;
            decimal inferredZoom = Math.Max(_nudZoom.Minimum, Math.Min(_nudZoom.Maximum, baseRange / currentRange));
            _suppressViewportSync = true;
            _nudZoom.Value = inferredZoom;
            _suppressViewportSync = false;
        }

        private void ApplyZoomFromControl()
        {
            if (_suppressViewportSync || _canvas.Width <= 0 || _canvas.Height <= 0)
            {
                return;
            }

            ApplyZoom(_nudZoom.Value, _canvas.Width / 2, _canvas.Height / 2);
        }

        private void ApplyZoom(decimal nextZoom, int anchorX, int anchorY)
        {
            decimal width = Math.Max(1, _canvas.Width);
            decimal height = Math.Max(1, _canvas.Height);
            decimal aRange = _nudAMax.Value - _nudAMin.Value;
            decimal bRange = _nudBMax.Value - _nudBMin.Value;

            if (aRange <= 0 || bRange <= 0)
            {
                return;
            }

            decimal mouseA = _nudAMin.Value + (anchorX / width) * aRange;
            decimal mouseB = _nudBMax.Value - (anchorY / height) * bRange;

            decimal newRangeA = (DefaultAMax - DefaultAMin) / nextZoom;
            decimal newRangeB = (DefaultBMax - DefaultBMin) / nextZoom;

            decimal newAMin = mouseA - (anchorX / width) * newRangeA;
            decimal newBMax = mouseB + (anchorY / height) * newRangeB;
            decimal newAMax = newAMin + newRangeA;
            decimal newBMin = newBMax - newRangeB;

            ClampRange(ref newAMin, ref newAMax, MinADomain, MaxADomain);
            ClampRange(ref newBMin, ref newBMax, MinBDomain, MaxBDomain);

            _suppressViewportSync = true;
            _nudAMin.Value = Math.Max(_nudAMin.Minimum, Math.Min(_nudAMin.Maximum, newAMin));
            _nudAMax.Value = Math.Max(_nudAMax.Minimum, Math.Min(_nudAMax.Maximum, newAMax));
            _nudBMin.Value = Math.Max(_nudBMin.Minimum, Math.Min(_nudBMin.Maximum, newBMin));
            _nudBMax.Value = Math.Max(_nudBMax.Minimum, Math.Min(_nudBMax.Maximum, newBMax));
            _nudZoom.Value = Math.Max(_nudZoom.Minimum, Math.Min(_nudZoom.Maximum, nextZoom));
            _suppressViewportSync = false;
        }

        private static void ClampRange(ref decimal min, ref decimal max, decimal domainMin, decimal domainMax)
        {
            decimal width = max - min;
            if (width <= 0)
            {
                return;
            }

            if (min < domainMin)
            {
                max += domainMin - min;
                min = domainMin;
            }

            if (max > domainMax)
            {
                min -= max - domainMax;
                max = domainMax;
            }

            min = Math.Max(domainMin, min);
            max = Math.Min(domainMax, max);
        }

        private void SyncEngine()
        {
            _engine.AMin = _nudAMin.Value;
            _engine.AMax = _nudAMax.Value;
            _engine.BMin = _nudBMin.Value;
            _engine.BMax = _nudBMax.Value;
            _engine.Pattern = _tbPattern.Text;
            _engine.Iterations = (int)_nudIterations.Value;
            _engine.TransientIterations = (int)_nudTransient.Value;
            _engine.ColorPalette = _paletteManager.ActivePalette;
        }

        private void CommitAndBakePreview()
        {
            CancelPreviewRender();
            lock (_frameBufferLock)
            {
                Bitmap? bakedFrame = CreateBakedFrameUnsafe();
                if (bakedFrame == null)
                {
                    return;
                }

                _previewFrameBuffer?.Dispose();
                _previewFrameBuffer = bakedFrame;
                _currentRenderingFrameBuffer?.Dispose();
                _currentRenderingFrameBuffer = null;
                _currentRenderedTiles.Clear();
                _renderedAMin = _nudAMin.Value;
                _renderedAMax = _nudAMax.Value;
                _renderedBMin = _nudBMin.Value;
                _renderedBMax = _nudBMax.Value;
            }

            _isRenderingPreview = false;
            _renderVisualizer.NotifyRenderSessionComplete();
            _canvas.Invalidate();
        }

        private Bitmap? CreateBakedFrameUnsafe()
        {
            if (_canvas.Width <= 0 || _canvas.Height <= 0)
            {
                return null;
            }

            bool hasStableFrame = _previewFrameBuffer != null;
            bool hasWorkingTiles = _currentRenderingFrameBuffer != null && _currentRenderedTiles.Count > 0;
            if (!hasStableFrame && !hasWorkingTiles)
            {
                return null;
            }

            Bitmap bakedFrame = new Bitmap(_canvas.Width, _canvas.Height, PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(bakedFrame);

            if (_previewFrameBuffer != null)
            {
                bool sameViewport =
                    _renderedAMin == _nudAMin.Value &&
                    _renderedAMax == _nudAMax.Value &&
                    _renderedBMin == _nudBMin.Value &&
                    _renderedBMax == _nudBMax.Value;

                if (sameViewport)
                {
                    g.DrawImage(_previewFrameBuffer, _canvas.ClientRectangle);
                }
                else
                {
                    DrawTransformedFrame(g, _previewFrameBuffer, _canvas.ClientRectangle);
                }
            }
            else
            {
                g.Clear(Color.Black);
            }

            if (_currentRenderingFrameBuffer != null)
            {
                foreach (Rectangle tile in _currentRenderedTiles)
                {
                    g.DrawImage(_currentRenderingFrameBuffer, tile, tile, GraphicsUnit.Pixel);
                }
            }

            return bakedFrame;
        }

        private void DrawTransformedFrame(Graphics g, Bitmap bitmap, Rectangle destinationRect)
        {
            decimal renderedARange = _renderedAMax - _renderedAMin;
            decimal renderedBRange = _renderedBMax - _renderedBMin;
            decimal currentARange = _nudAMax.Value - _nudAMin.Value;
            decimal currentBRange = _nudBMax.Value - _nudBMin.Value;
            if (renderedARange <= 0 || renderedBRange <= 0 || currentARange <= 0 || currentBRange <= 0)
            {
                g.DrawImage(bitmap, destinationRect);
                return;
            }

            decimal offsetX = (_renderedAMin - _nudAMin.Value) / currentARange * destinationRect.Width;
            decimal offsetY = (_nudBMax.Value - _renderedBMax) / currentBRange * destinationRect.Height;
            decimal newWidth = renderedARange / currentARange * destinationRect.Width;
            decimal newHeight = renderedBRange / currentBRange * destinationRect.Height;

            g.InterpolationMode = InterpolationMode.Bilinear;
            PointF p1 = new(destinationRect.X + (float)offsetX, destinationRect.Y + (float)offsetY);
            PointF p2 = new(destinationRect.X + (float)(offsetX + newWidth), destinationRect.Y + (float)offsetY);
            PointF p3 = new(destinationRect.X + (float)offsetX, destinationRect.Y + (float)(offsetY + newHeight));
            g.DrawImage(bitmap, new[] { p1, p2, p3 });
        }

        private async Task RenderAsync()
        {
            if (_canvas.Width <= 1 || _canvas.Height <= 1)
            {
                return;
            }
            var renderStopwatch = System.Diagnostics.Stopwatch.StartNew();

            SyncEngine();
            CancellationTokenSource renderSession = StartNewPreviewRender();
            CancellationToken token = renderSession.Token;

            int width = _canvas.Width;
            int height = _canvas.Height;
            int threads = GetThreadCount();
            int ssaaFactor = GetSelectedSsaaFactor();
            var tiles = GenerateTiles(width, height, PreviewTileSize);
            decimal renderTargetAMin = _nudAMin.Value;
            decimal renderTargetAMax = _nudAMax.Value;
            decimal renderTargetBMin = _nudBMin.Value;
            decimal renderTargetBMax = _nudBMax.Value;

            var engine = new FractalLyapunovEngine
            {
                AMin = _engine.AMin,
                AMax = _engine.AMax,
                BMin = _engine.BMin,
                BMax = _engine.BMax,
                Pattern = _engine.Pattern,
                Iterations = _engine.Iterations,
                TransientIterations = _engine.TransientIterations,
                ColorPalette = _engine.ColorPalette
            };

            LyapunovColoringContext? coloringContext = await Task.Run(() => engine.PrepareColoringContext(width, height, token), token);

            Bitmap frame = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            lock (_frameBufferLock)
            {
                _currentRenderingFrameBuffer?.Dispose();
                _currentRenderingFrameBuffer = frame;
                _currentRenderedTiles.Clear();
            }
            _canvas.Invalidate();

            if (_pbRenderProgress.IsHandleCreated)
            {
                _pbRenderProgress.Maximum = Math.Max(1, tiles.Count);
                _pbRenderProgress.Value = 0;
            }

            int completedTiles = 0;
            var dispatcher = new TileRenderDispatcher(tiles, threads, RenderPatternSettings.SelectedPattern);
            _renderVisualizer.NotifyRenderSessionStart();
            _isRenderingPreview = true;

            try
            {
                await dispatcher.RenderAsync(async (tile, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    _renderVisualizer.NotifyTileRenderStart(tile.Bounds);

                    byte[] tileBuffer = RenderTileWithSsaa(engine, tile, width, height, ssaaFactor, out int bytesPerPixel, coloringContext);
                    ct.ThrowIfCancellationRequested();

                    lock (_frameBufferLock)
                    {
                        if (ct.IsCancellationRequested || _currentRenderingFrameBuffer != frame)
                        {
                            return;
                        }

                        WriteTileToBitmap(frame, tile, tileBuffer, bytesPerPixel);
                        _currentRenderedTiles.Add(tile.Bounds);
                    }

                    int done = Interlocked.Increment(ref completedTiles);
                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke((Action)(() =>
                        {
                            if (_pbRenderProgress.IsHandleCreated)
                            {
                                _pbRenderProgress.Value = Math.Min(_pbRenderProgress.Maximum, done);
                            }
                            _canvas.Invalidate(tile.Bounds);
                        }));
                    }

                    _renderVisualizer.NotifyTileRenderComplete(tile.Bounds);
                    await Task.Yield();
                }, token);

                token.ThrowIfCancellationRequested();

                lock (_frameBufferLock)
                {
                    if (_currentRenderingFrameBuffer == frame)
                    {
                        Bitmap? previousStable = _previewFrameBuffer;
                        _previewFrameBuffer = frame;
                        _currentRenderingFrameBuffer = null;
                        _currentRenderedTiles.Clear();
                        _renderedAMin = renderTargetAMin;
                        _renderedAMax = renderTargetAMax;
                        _renderedBMin = renderTargetBMin;
                        _renderedBMax = renderTargetBMax;
                        Text = $"{_baseTitle} - Время последнего рендера: {renderStopwatch.Elapsed.TotalSeconds:F3} сек.";
                        previousStable?.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                lock (_frameBufferLock)
                {
                    if (_currentRenderingFrameBuffer == frame)
                    {
                        _currentRenderingFrameBuffer = null;
                        _currentRenderedTiles.Clear();
                    }
                }
                frame.Dispose();
            }
            finally
            {
                if (ReferenceEquals(_previewRenderCts, renderSession))
                {
                    _isRenderingPreview = false;
                    _renderVisualizer.NotifyRenderSessionComplete();
                    ClearPreviewRenderToken();
                    _canvas.Invalidate();
                }
            }
        }

        private int GetSelectedSsaaFactor()
        {
            return _cbSSAA.SelectedIndex switch
            {
                1 => 2,
                2 => 4,
                _ => 1
            };
        }

        private static byte[] RenderTileWithSsaa(FractalLyapunovEngine engine, TileInfo tile, int width, int height, int ssaaFactor, out int bytesPerPixel, LyapunovColoringContext? coloringContext = null)
        {
            if (ssaaFactor <= 1)
            {
                return engine.RenderSingleTile(tile, width, height, out bytesPerPixel, coloringContext);
            }

            int hiWidth = width * ssaaFactor;
            int hiHeight = height * ssaaFactor;
            var hiTile = new TileInfo(tile.Bounds.X * ssaaFactor, tile.Bounds.Y * ssaaFactor, tile.Bounds.Width * ssaaFactor, tile.Bounds.Height * ssaaFactor);
            byte[] hiBuffer = engine.RenderSingleTile(hiTile, hiWidth, hiHeight, out int hiBpp, coloringContext);
            bytesPerPixel = hiBpp;
            return DownsampleTile(hiBuffer, tile.Bounds.Width, tile.Bounds.Height, ssaaFactor, bytesPerPixel);
        }

        private static byte[] DownsampleTile(byte[] source, int width, int height, int factor, int bytesPerPixel)
        {
            byte[] output = new byte[width * height * bytesPerPixel];
            int hiWidth = width * factor;
            int kernel = factor * factor;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int destIndex = (y * width + x) * bytesPerPixel;
                    int sumB = 0;
                    int sumG = 0;
                    int sumR = 0;
                    int sumA = 0;

                    for (int sy = 0; sy < factor; sy++)
                    {
                        int row = (y * factor + sy) * hiWidth;
                        for (int sx = 0; sx < factor; sx++)
                        {
                            int srcIndex = (row + (x * factor + sx)) * bytesPerPixel;
                            sumB += source[srcIndex + 0];
                            sumG += source[srcIndex + 1];
                            sumR += source[srcIndex + 2];
                            sumA += source[srcIndex + 3];
                        }
                    }

                    output[destIndex + 0] = (byte)(sumB / kernel);
                    output[destIndex + 1] = (byte)(sumG / kernel);
                    output[destIndex + 2] = (byte)(sumR / kernel);
                    output[destIndex + 3] = (byte)(sumA / kernel);
                }
            }

            return output;
        }

        private static void WriteTileToBitmap(Bitmap bitmap, TileInfo tile, byte[] tileBuffer, int bytesPerPixel)
        {
            BitmapData bmpData = bitmap.LockBits(tile.Bounds, ImageLockMode.WriteOnly, bitmap.PixelFormat);
            try
            {
                int tileWidthInBytes = tile.Bounds.Width * bytesPerPixel;
                for (int y = 0; y < tile.Bounds.Height; y++)
                {
                    IntPtr destPtr = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                    int srcOffset = y * tileWidthInBytes;
                    Marshal.Copy(tileBuffer, srcOffset, destPtr, tileWidthInBytes);
                }
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }

        private static List<TileInfo> GenerateTiles(int width, int height, int tileSize)
        {
            var tiles = new List<TileInfo>();
            for (int y = 0; y < height; y += tileSize)
            {
                int h = Math.Min(tileSize, height - y);
                for (int x = 0; x < width; x += tileSize)
                {
                    int w = Math.Min(tileSize, width - x);
                    tiles.Add(new TileInfo(x, y, w, h));
                }
            }

            return tiles;
        }

        private CancellationTokenSource StartNewPreviewRender()
        {
            var next = new CancellationTokenSource();
            CancellationTokenSource? previous = Interlocked.Exchange(ref _previewRenderCts, next);
            if (previous != null)
            {
                previous.Cancel();
                previous.Dispose();
            }

            return next;
        }

        private void ClearPreviewRenderToken()
        {
            CancellationTokenSource? currentToken = Interlocked.Exchange(ref _previewRenderCts, null);
            currentToken?.Dispose();
        }

        private void CancelPreviewRender()
        {
            CancellationTokenSource? currentToken = Interlocked.Exchange(ref _previewRenderCts, null);
            if (currentToken != null)
            {
                currentToken.Cancel();
                currentToken.Dispose();
            }
        }

        private void ApplyPreset()
        {
            List<FractalSaveStateBase> presets = PresetManager.GetPresetsFor(FractalTypeIdentifier);
            if (presets.Count == 0)
            {
                MessageBox.Show("Пресеты не найдены.");
                return;
            }

            if (presets[0] is LyapunovSaveState state)
            {
                LoadState(state);
            }
        }

        private async void btnRender_Click(object sender, EventArgs e)
        {
            await RenderAsync();
        }

        private void btnPresets_Click(object sender, EventArgs e)
        {
            ApplyPreset();
        }

        private void btnState_Click(object sender, EventArgs e)
        {
            using var dialog = new SaveLoadDialogForm(this);
            dialog.ShowDialog(this);
        }

        private void btnSaveImage_Click(object sender, EventArgs e)
        {
            if (_isHighResRendering)
            {
                MessageBox.Show("Процесс рендеринга уже запущен.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var saveManager = new SaveImageManagerForm(this);
            saveManager.ShowDialog(this);
        }


        private void btnPalette_Click(object sender, EventArgs e)
        {
            if (_lyapunovColorConfigForm == null || _lyapunovColorConfigForm.IsDisposed)
            {
                _lyapunovColorConfigForm = new ColorConfigurationLyapunovForm(_paletteManager);
                _lyapunovColorConfigForm.PaletteApplied += (_, _) => QueueRenderRestart(immediate: true);
            }

            _lyapunovColorConfigForm.Show(this);
            _lyapunovColorConfigForm.BringToFront();
        }

        public HighResRenderState GetRenderState()
        {
            string pattern = string.IsNullOrWhiteSpace(_tbPattern.Text) ? "AB" : _tbPattern.Text.Trim().ToUpperInvariant();
            return new HighResRenderState
            {
                EngineType = FractalTypeIdentifier,
                Iterations = (int)_nudIterations.Value,
                FileNameDetails = $"lyapunov_{pattern}",
                Threshold = 0,
                BaseScale = 1,
                Zoom = _nudZoom.Value,
                LyapunovAMin = _nudAMin.Value,
                LyapunovAMax = _nudAMax.Value,
                LyapunovBMin = _nudBMin.Value,
                LyapunovBMax = _nudBMax.Value,
                LyapunovTransientIterations = (int)_nudTransient.Value,
                LyapunovPattern = pattern
            };
        }

        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            _isHighResRendering = true;
            try
            {
                var engine = new FractalLyapunovEngine
                {
                    AMin = state.LyapunovAMin ?? _nudAMin.Value,
                    AMax = state.LyapunovAMax ?? _nudAMax.Value,
                    BMin = state.LyapunovBMin ?? _nudBMin.Value,
                    BMax = state.LyapunovBMax ?? _nudBMax.Value,
                    Pattern = string.IsNullOrWhiteSpace(state.LyapunovPattern) ? _tbPattern.Text : state.LyapunovPattern,
                    Iterations = state.Iterations > 0 ? state.Iterations : (int)_nudIterations.Value,
                    TransientIterations = state.LyapunovTransientIterations ?? (int)_nudTransient.Value,
                    ColorPalette = _paletteManager.ActivePalette
                };

                return await Task.Run(() =>
                {
                    int safeWidth = Math.Max(1, width);
                    int safeHeight = Math.Max(1, height);
                    int effectiveSsaa = Math.Max(1, ssaaFactor);
                    int totalRows = safeHeight;
                    LyapunovColoringContext? coloringContext = engine.PrepareColoringContext(safeWidth, safeHeight, cancellationToken);
                    var result = new Bitmap(safeWidth, safeHeight, PixelFormat.Format32bppArgb);
                    int threadCount = GetThreadCount();

                    var po = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = threadCount,
                        CancellationToken = cancellationToken
                    };

                    int completedRows = 0;
                    Parallel.For(0, totalRows, po, y =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var tile = new TileInfo(0, y, safeWidth, 1);
                        byte[] rowBuffer = RenderTileWithSsaa(engine, tile, safeWidth, safeHeight, effectiveSsaa, out int bytesPerPixel, coloringContext);

                        lock (result)
                        {
                            WriteTileToBitmap(result, tile, rowBuffer, bytesPerPixel);
                        }

                        int done = Interlocked.Increment(ref completedRows);
                        int percentage = (int)Math.Round(done * 100.0 / totalRows);
                        progress.Report(new RenderProgress { Percentage = percentage, Status = "Рендеринг..." });
                    });

                    return result;
                }, cancellationToken);
            }
            finally
            {
                _isHighResRendering = false;
            }
        }

        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            var engine = new FractalLyapunovEngine
            {
                AMin = state.LyapunovAMin ?? _nudAMin.Value,
                AMax = state.LyapunovAMax ?? _nudAMax.Value,
                BMin = state.LyapunovBMin ?? _nudBMin.Value,
                BMax = state.LyapunovBMax ?? _nudBMax.Value,
                Pattern = string.IsNullOrWhiteSpace(state.LyapunovPattern) ? _tbPattern.Text : state.LyapunovPattern,
                Iterations = state.Iterations > 0 ? state.Iterations : (int)_nudIterations.Value,
                TransientIterations = state.LyapunovTransientIterations ?? (int)_nudTransient.Value,
                ColorPalette = _paletteManager.ActivePalette
            };

            LyapunovColoringContext? coloringContext = engine.PrepareColoringContext(previewWidth, previewHeight);
            return engine.RenderToBitmap(previewWidth, previewHeight, GetThreadCount(), coloringContext: coloringContext);
        }

        private int GetThreadCount()
        {
            if (_cbThreads.SelectedItem?.ToString() == AutoThreadOptionText)
            {
                return Environment.ProcessorCount;
            }

            if (_cbThreads.SelectedItem is int selectedThreads)
            {
                return Math.Max(1, selectedThreads);
            }

            return Environment.ProcessorCount;
        }

        public string FractalTypeIdentifier => "Lyapunov";
        public Type ConcreteSaveStateType => typeof(LyapunovSaveState);

        public class LyapunovPreviewParams
        {
            public decimal AMin { get; set; }
            public decimal AMax { get; set; }
            public decimal BMin { get; set; }
            public decimal BMax { get; set; }
            public string Pattern { get; set; } = "AB";
            public int Iterations { get; set; }
            public int TransientIterations { get; set; }
        }

        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new LyapunovSaveState(FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                AMin = _nudAMin.Value,
                AMax = _nudAMax.Value,
                BMin = _nudBMin.Value,
                BMax = _nudBMax.Value,
                Pattern = _tbPattern.Text,
                Iterations = (int)_nudIterations.Value,
                TransientIterations = (int)_nudTransient.Value,
                PaletteName = _paletteManager.ActivePalette.Name
            };

            state.PreviewParametersJson = JsonSerializer.Serialize(new LyapunovPreviewParams
            {
                AMin = state.AMin,
                AMax = state.AMax,
                BMin = state.BMin,
                BMax = state.BMax,
                Pattern = state.Pattern,
                Iterations = state.Iterations,
                TransientIterations = state.TransientIterations
            });

            return state;
        }

        public void LoadState(FractalSaveStateBase state)
        {
            if (state is not LyapunovSaveState lyapunov)
            {
                return;
            }

            _nudAMin.Value = lyapunov.AMin;
            _nudAMax.Value = lyapunov.AMax;
            _nudBMin.Value = lyapunov.BMin;
            _nudBMax.Value = lyapunov.BMax;
            _tbPattern.Text = lyapunov.Pattern;
            _nudIterations.Value = Math.Max(_nudIterations.Minimum, Math.Min(_nudIterations.Maximum, lyapunov.Iterations));
            _nudTransient.Value = Math.Max(_nudTransient.Minimum, Math.Min(_nudTransient.Maximum, lyapunov.TransientIterations));
            if (!string.IsNullOrWhiteSpace(lyapunov.PaletteName))
            {
                _paletteManager.ActivePalette = _paletteManager.Palettes.FirstOrDefault(p => p.Name == lyapunov.PaletteName) ?? _paletteManager.ActivePalette;
            }
            _ = RenderAsync();
        }

        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (state is not LyapunovSaveState lyapunov)
            {
                return new Bitmap(previewWidth, previewHeight);
            }

            var engine = new FractalLyapunovEngine
            {
                AMin = lyapunov.AMin,
                AMax = lyapunov.AMax,
                BMin = lyapunov.BMin,
                BMax = lyapunov.BMax,
                Pattern = lyapunov.Pattern,
                Iterations = lyapunov.Iterations,
                TransientIterations = lyapunov.TransientIterations,
                ColorPalette = _paletteManager.Palettes.FirstOrDefault(p => p.Name == lyapunov.PaletteName) ?? _paletteManager.ActivePalette
            };

            LyapunovColoringContext? coloringContext = engine.PrepareColoringContext(previewWidth, previewHeight);
            return engine.RenderToBitmap(previewWidth, previewHeight, 1, coloringContext: coloringContext);
        }

        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            return await Task.Run(() =>
            {
                if (state is not LyapunovSaveState lyapunov)
                {
                    return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];
                }

                var engine = new FractalLyapunovEngine
                {
                    AMin = lyapunov.AMin,
                    AMax = lyapunov.AMax,
                    BMin = lyapunov.BMin,
                    BMax = lyapunov.BMax,
                    Pattern = lyapunov.Pattern,
                    Iterations = lyapunov.Iterations,
                    TransientIterations = lyapunov.TransientIterations,
                    ColorPalette = _paletteManager.Palettes.FirstOrDefault(p => p.Name == lyapunov.PaletteName) ?? _paletteManager.ActivePalette
                };
                LyapunovColoringContext? coloringContext = engine.PrepareColoringContext(totalWidth, totalHeight);
                return engine.RenderSingleTile(tile, totalWidth, totalHeight, out _, coloringContext);
            });
        }

        public List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            return SaveFileManager.LoadSaves<LyapunovSaveState>(FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();
        }

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            SaveFileManager.SaveSaves(FractalTypeIdentifier, saves.Cast<LyapunovSaveState>().ToList());
        }
    }
}
