using FractalExplorer.Engines;
using FractalExplorer.Forms.Other;
using FractalExplorer.Resources;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using FractalExplorer.Utilities.Theme;
using FractalExplorer.Utilities.UI;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Threading;

namespace FractalExplorer.Forms.Fractals
{
    public sealed partial class FractalFlameForm : Form, ISaveLoadCapableFractal, IFullPreviewRenderCapableFractal, IHighResRenderable
    {
        private const double MinScaleMagnitude = 1e-9;
        private readonly FractalFlameEngine _engine = new();
        private CancellationTokenSource? _cts;
        private bool _isPanning;
        private Point _panStartPoint;
        private double _panStartCenterX;
        private double _panStartCenterY;
        private readonly System.Windows.Forms.Timer _renderRestartTimer = new() { Interval = 350 };
        private readonly System.Windows.Forms.Timer _wheelDebounceTimer = new() { Interval = 500 };
        private bool _isQueuingRenderRestart;
        private bool _isUserResizingWindow;
        private bool _hasPendingCanvasResizeRender;
        private double _renderedCenterX;
        private double _renderedCenterY;
        private double _renderedScale = -1;
        private Bitmap? _previewBitmap;
        private Bitmap? _coverageOverlayBitmap;
        private readonly object _previewLock = new();
        private readonly string _baseTitle;
        private volatile bool _isRenderInProgress;
        private bool _hasIntermediateFrame;
        private double _activeRenderCenterX;
        private double _activeRenderCenterY;
        private double _activeRenderScale;
        private readonly FullscreenToggleController _fullscreenController = new();
        private const int ToggleButtonMargin = 12;
        private bool _controlsPanelVisible = true;
        private bool _suppressResizeRender;
        private bool _suppressAutoRenderQueue;
        private readonly SemaphoreSlim _renderExecutionLock = new(1, 1);
        private bool _isProcessingRenderQueue;
        private bool _isWheelZoomInProgress;

        public FractalFlameForm()
        {
            InitializeComponent();
            KeyPreview = true;
            ThemeManager.RegisterForm(this);
            _baseTitle = Text;
            InitializeDefaults();
            _btnRender.Click += async (_, _) => await RenderAsync();
            _btnEditTransforms.Click += BtnEditTransforms_Click;
            _btnSaveLoad.Click += (_, _) => { using var dlg = new SaveLoadDialogForm(this); dlg.ShowDialog(this); };
            _btnSaveImage.Click += (_, _) => { using var dlg = new SaveImageManagerForm(this); dlg.ShowDialog(this); };
            Load += async (_, _) => await RenderAsync();
            FormClosing += (_, _) =>
            {
                ExitFullscreenSafely();
                _cts?.Cancel();
                _renderRestartTimer.Stop();
                _wheelDebounceTimer.Stop();
                _renderRestartTimer.Tick -= RenderRestartTimer_Tick;
                _wheelDebounceTimer.Tick -= WheelDebounceTimer_Tick;
                lock (_previewLock)
                {
                    _previewBitmap?.Dispose();
                    _previewBitmap = null;
                    _coverageOverlayBitmap?.Dispose();
                    _coverageOverlayBitmap = null;
                }
            };
            _canvas.MouseWheel += Canvas_MouseWheel;
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseLeave += Canvas_MouseLeave;
            _canvas.MouseEnter += (_, _) => _canvas.Focus();
            _canvas.Paint += Canvas_Paint;
            _canvas.SizeChanged += Canvas_SizeChanged;
            _canvasHost.Resize += CanvasHost_Resize;
            _controlsPanel.SizeChanged += ControlsPanel_SizeChanged;
            _btnToggleControls.Click += BtnToggleControls_Click;
            KeyDown += Form_KeyDown;
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
            _wheelDebounceTimer.Tick += WheelDebounceTimer_Tick;
            AttachAutoRenderControlTriggers();
            UpdateToggleControlsPosition();
        }

        private void InitializeDefaults()
        {
            _threads.Items.Add("Авто");
            for (int i = 1; i <= Environment.ProcessorCount; i++) _threads.Items.Add(i);
            _threads.SelectedIndex = 0;
            _pbRenderProgress.Minimum = 0;
            _pbRenderProgress.Maximum = 100;
            _pbRenderProgress.Value = 0;

            _engine.Transforms.Clear();
            _engine.Transforms.AddRange(new[]
            {
                new FlameTransform { Weight = 1.0, A = 0.5, B = 0, C = -0.3, D = 0, E = 0.5, F = 0.0, Variation = FlameVariation.Linear, Color = Color.Orange },
                new FlameTransform { Weight = 1.0, A = 0.5, B = 0, C = 0.3, D = 0, E = 0.5, F = 0.0, Variation = FlameVariation.Sinusoidal, Color = Color.DeepSkyBlue },
                new FlameTransform { Weight = 0.6, A = 0.5, B = 0, C = 0.0, D = 0, E = 0.5, F = 0.4, Variation = FlameVariation.Spherical, Color = Color.Lime }
            });
        }

        private void AttachAutoRenderControlTriggers()
        {
            _samples.ValueChanged += (_, _) => QueueRenderRestartIfAllowed();
            _iterations.ValueChanged += (_, _) => QueueRenderRestartIfAllowed();
            _warmup.ValueChanged += (_, _) => QueueRenderRestartIfAllowed();
            _scale.ValueChanged += (_, _) => QueueRenderRestartIfAllowed();
            _centerX.ValueChanged += (_, _) => QueueRenderRestartIfAllowed();
            _centerY.ValueChanged += (_, _) => QueueRenderRestartIfAllowed();
            _exposure.ValueChanged += (_, _) => QueueRenderRestartIfAllowed();
            _gamma.ValueChanged += (_, _) => QueueRenderRestartIfAllowed();
            _threads.SelectedIndexChanged += (_, _) => QueueRenderRestartIfAllowed();
            _showCoverageMap.CheckedChanged += (_, _) => _canvas.Invalidate();
        }

        private void QueueRenderRestartIfAllowed()
        {
            if (_suppressAutoRenderQueue)
            {
                return;
            }

            QueueRenderRestart();
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
            if (_isPanning || _isWheelZoomInProgress || _isProcessingRenderQueue)
            {
                return;
            }

            _isProcessingRenderQueue = true;
            try
            {
                while (_isQueuingRenderRestart && !_isPanning)
                {
                    _isQueuingRenderRestart = false;
                    await RenderAsync();
                }
            }
            finally
            {
                _isProcessingRenderQueue = false;
                if (_isQueuingRenderRestart && !_isPanning)
                {
                    _ = TriggerQueuedRenderRestartAsync();
                }
            }
        }

        private void Canvas_SizeChanged(object? sender, EventArgs e)
        {
            if (_canvas.Width <= 1 || _canvas.Height <= 1)
            {
                return;
            }

            if (_suppressResizeRender)
            {
                _canvas.Invalidate();
                return;
            }

            if (_isUserResizingWindow)
            {
                _hasPendingCanvasResizeRender = true;
                return;
            }

            QueueRenderRestart(immediate: true);
        }

        private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (_canvas.Width <= 1 || _canvas.Height <= 1)
            {
                return;
            }

            double zoomFactor = e.Delta > 0 ? 0.85 : 1.18;
            double oldScale = NormalizeScale((double)_scale.Value);
            double newScale = Math.Clamp(oldScale * zoomFactor, (double)_scale.Minimum, (double)_scale.Maximum);
            newScale = NormalizeScale(newScale);

            double worldXBefore = ScreenToWorldX(e.X, oldScale, (double)_centerX.Value);
            double worldYBefore = ScreenToWorldY(e.Y, oldScale, (double)_centerY.Value);

            double newCenterX = worldXBefore - (e.X / (double)_canvas.Width - 0.5) * newScale;
            double newCenterY = worldYBefore + (e.Y / (double)_canvas.Height - 0.5) * newScale * _canvas.Height / (double)_canvas.Width;

            _suppressAutoRenderQueue = true;
            try
            {
                _scale.Value = (decimal)newScale;
                _centerX.Value = (decimal)Math.Clamp(newCenterX, (double)_centerX.Minimum, (double)_centerX.Maximum);
                _centerY.Value = (decimal)Math.Clamp(newCenterY, (double)_centerY.Minimum, (double)_centerY.Maximum);
            }
            finally
            {
                _suppressAutoRenderQueue = false;
            }
            _canvas.Invalidate();
            _isWheelZoomInProgress = true;
            _wheelDebounceTimer.Stop();
            _wheelDebounceTimer.Start();
        }

        private void WheelDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _wheelDebounceTimer.Stop();
            _isWheelZoomInProgress = false;
            QueueRenderRestart(immediate: true);
        }

        private void Canvas_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _cts?.Cancel();
            _renderRestartTimer.Stop();
            _isPanning = true;
            _panStartPoint = e.Location;
            _panStartCenterX = (double)_centerX.Value;
            _panStartCenterY = (double)_centerY.Value;
            _canvas.Cursor = Cursors.SizeAll;
        }

        private void Canvas_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isPanning || _canvas.Width <= 1 || _canvas.Height <= 1)
            {
                return;
            }

            double scale = (double)_scale.Value;
            double aspectScaleY = scale * _canvas.Height / (double)_canvas.Width;

            int dx = e.X - _panStartPoint.X;
            int dy = e.Y - _panStartPoint.Y;
            double newCenterX = _panStartCenterX - dx * (scale / _canvas.Width);
            double newCenterY = _panStartCenterY + dy * (aspectScaleY / _canvas.Height);

            _suppressAutoRenderQueue = true;
            try
            {
                _centerX.Value = (decimal)Math.Clamp(newCenterX, (double)_centerX.Minimum, (double)_centerX.Maximum);
                _centerY.Value = (decimal)Math.Clamp(newCenterY, (double)_centerY.Minimum, (double)_centerY.Maximum);
            }
            finally
            {
                _suppressAutoRenderQueue = false;
            }
            _canvas.Invalidate();
        }

        private void Canvas_MouseUp(object? sender, MouseEventArgs e)
        {
            if (!_isPanning)
            {
                return;
            }

            _isPanning = false;
            _canvas.Cursor = Cursors.Default;
            QueueRenderRestart(immediate: true);
        }

        private void Canvas_MouseLeave(object? sender, EventArgs e)
        {
            if (!_isPanning)
            {
                return;
            }

            _isPanning = false;
            _canvas.Cursor = Cursors.Default;
            QueueRenderRestart(immediate: true);
        }

        private void BtnToggleControls_Click(object? sender, EventArgs e)
        {
            ToggleControlsPanel();
        }

        private void CanvasHost_Resize(object? sender, EventArgs e)
        {
            UpdateToggleControlsPosition();
        }

        private void ControlsPanel_SizeChanged(object? sender, EventArgs e)
        {
            UpdateToggleControlsPosition();
        }

        private void UpdateToggleControlsPosition()
        {
            int targetX = ToggleButtonMargin;
            if (_controlsPanelVisible)
            {
                targetX = _controlsPanel.Right + ToggleButtonMargin;
            }

            int maxX = Math.Max(ToggleButtonMargin, _canvasHost.ClientSize.Width - _btnToggleControls.Width - ToggleButtonMargin);
            _btnToggleControls.Location = new Point(Math.Min(targetX, maxX), ToggleButtonMargin);
            _btnToggleControls.BringToFront();
        }

        private void ToggleControlsPanel()
        {
            _controlsPanelVisible = !_controlsPanelVisible;
            _btnToggleControls.Text = _controlsPanelVisible ? "✕" : "☰";
            _suppressResizeRender = true;
            try
            {
                _controlsPanel.Visible = _controlsPanelVisible;
                UpdateToggleControlsPosition();
                _canvas.Invalidate();
            }
            finally
            {
                _suppressResizeRender = false;
            }
        }

        private void ToggleFullscreenSafely()
        {
            _suppressResizeRender = true;
            try
            {
                _fullscreenController.Toggle(this);
                UpdateToggleControlsPosition();
                QueueRenderRestart(immediate: true);
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
                QueueRenderRestart(immediate: true);
            }
            finally
            {
                _suppressResizeRender = false;
            }
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

        private void Canvas_Paint(object? sender, PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Black);
            Bitmap? preview;
            Bitmap? coverageOverlay;
            double renderedCenterX;
            double renderedCenterY;
            double renderedScale;
            double activeRenderCenterX;
            double activeRenderCenterY;
            double activeRenderScale;
            lock (_previewLock)
            {
                preview = _previewBitmap;
                coverageOverlay = _coverageOverlayBitmap;
                renderedCenterX = _renderedCenterX;
                renderedCenterY = _renderedCenterY;
                renderedScale = _renderedScale;
                activeRenderCenterX = _activeRenderCenterX;
                activeRenderCenterY = _activeRenderCenterY;
                activeRenderScale = _activeRenderScale;
            }

            if (preview == null || Math.Abs(renderedScale) < MinScaleMagnitude)
            {
                return;
            }

            double currentCenterX = (double)_centerX.Value;
            double currentCenterY = (double)_centerY.Value;
            double currentScale = NormalizeScale((double)_scale.Value);
            bool sameViewport =
                Math.Abs(renderedCenterX - currentCenterX) < 1e-12 &&
                Math.Abs(renderedCenterY - currentCenterY) < 1e-12 &&
                Math.Abs(renderedScale - currentScale) < 1e-12;
            e.Graphics.InterpolationMode = sameViewport ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

            if (!TryComputeDestinationRectForViewport(
                renderedCenterX,
                renderedCenterY,
                renderedScale,
                preview.Width,
                preview.Height,
                out RectangleF previewDestRect))
            {
                return;
            }

            e.Graphics.DrawImage(preview, previewDestRect);
            DrawCoverageOverlay(e.Graphics, coverageOverlay, activeRenderCenterX, activeRenderCenterY, activeRenderScale);
        }

        private void DrawCoverageOverlay(
            Graphics graphics,
            Bitmap? coverageOverlay,
            double activeRenderCenterX,
            double activeRenderCenterY,
            double activeRenderScale)
        {
            if (!_isRenderInProgress || !_showCoverageMap.Checked || coverageOverlay == null)
            {
                return;
            }

            if (!TryComputeDestinationRectForViewport(
                activeRenderCenterX,
                activeRenderCenterY,
                activeRenderScale,
                coverageOverlay.Width,
                coverageOverlay.Height,
                out RectangleF coverageDestRect))
            {
                return;
            }

            using ImageAttributes attributes = new();
            var matrix = new ColorMatrix
            {
                Matrix33 = 0.42f
            };
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            graphics.DrawImage(
                coverageOverlay,
                Rectangle.Round(coverageDestRect),
                0,
                0,
                coverageOverlay.Width,
                coverageOverlay.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        private bool TryComputeDestinationRectForViewport(double viewportCenterX, double viewportCenterY, double viewportScale, int bitmapWidth, int bitmapHeight, out RectangleF destinationRect)
        {
            destinationRect = RectangleF.Empty;
            if (_canvas.Width <= 1 || _canvas.Height <= 1 || Math.Abs(viewportScale) < MinScaleMagnitude)
            {
                return false;
            }

            double currentCenterX = (double)_centerX.Value;
            double currentCenterY = (double)_centerY.Value;
            double currentScale = NormalizeScale((double)_scale.Value);
            double currentWidthWorld = Math.Abs(currentScale);
            double currentHeightWorld = currentWidthWorld * _canvas.Height / (double)_canvas.Width;
            double currentLeft = currentCenterX - currentWidthWorld * 0.5;
            double currentTop = currentCenterY + currentHeightWorld * 0.5;

            double renderedWidthWorld = Math.Abs(viewportScale);
            double renderedHeightWorld = renderedWidthWorld * bitmapHeight / (double)bitmapWidth;
            double renderedLeft = viewportCenterX - renderedWidthWorld * 0.5;
            double renderedRight = viewportCenterX + renderedWidthWorld * 0.5;
            double renderedTop = viewportCenterY + renderedHeightWorld * 0.5;
            double renderedBottom = viewportCenterY - renderedHeightWorld * 0.5;

            float destLeft = (float)((renderedLeft - currentLeft) / currentWidthWorld * _canvas.Width);
            float destRight = (float)((renderedRight - currentLeft) / currentWidthWorld * _canvas.Width);
            float destTop = (float)((currentTop - renderedTop) / currentHeightWorld * _canvas.Height);
            float destBottom = (float)((currentTop - renderedBottom) / currentHeightWorld * _canvas.Height);

            destinationRect = RectangleF.FromLTRB(destLeft, destTop, destRight, destBottom);
            return destinationRect.Width > 0 && destinationRect.Height > 0;
        }

        private double ScreenToWorldX(int x, double scale, double centerX)
        {
            return centerX + (x / (double)_canvas.Width - 0.5) * scale;
        }

        private double ScreenToWorldY(int y, double scale, double centerY)
        {
            return centerY - (y / (double)_canvas.Height - 0.5) * scale * _canvas.Height / (double)_canvas.Width;
        }

        private void BtnEditTransforms_Click(object? sender, EventArgs e)
        {
            using var editor = new FlameTransformEditorForm(_engine.Transforms);
            if (editor.ShowDialog(this) == DialogResult.OK)
            {
                _engine.Transforms.Clear();
                _engine.Transforms.AddRange(editor.ResultTransforms);
                QueueRenderRestart(immediate: true);
            }
        }

        private void ApplyUiToEngine()
        {
            _engine.Samples = (int)_samples.Value;
            _engine.IterationsPerSample = (int)_iterations.Value;
            _engine.WarmupIterations = (int)_warmup.Value;
            _engine.Scale = NormalizeScale((double)_scale.Value);
            _engine.CenterX = (double)_centerX.Value;
            _engine.CenterY = (double)_centerY.Value;
            _engine.Exposure = (double)_exposure.Value;
            _engine.Gamma = (double)_gamma.Value;
            _engine.ThreadCount = _threads.SelectedItem is int i ? i : 0;
        }

        private async Task RenderAsync()
        {
            await _renderExecutionLock.WaitAsync();
            try
            {
            if (_canvas.Width <= 1 || _canvas.Height <= 1)
            {
                return;
            }

            ApplyUiToEngine();
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            _btnRender.Enabled = false;
            _isRenderInProgress = true;
            _status.Text = "Рендер...";
            _pbRenderProgress.Value = 0;
            lock (_previewLock)
            {
                _activeRenderCenterX = (double)_centerX.Value;
                _activeRenderCenterY = (double)_centerY.Value;
                _activeRenderScale = NormalizeScale((double)_scale.Value);
                _hasIntermediateFrame = false;
            }
            var renderStopwatch = Stopwatch.StartNew();
            var bmp = new Bitmap(_canvas.Width, _canvas.Height, PixelFormat.Format32bppArgb);
            Rectangle rect = new(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
            byte[] buffer = new byte[Math.Abs(data.Stride) * data.Height];
            var progress = new Progress<int>(p =>
            {
                int clamped = Math.Clamp(p, 0, 100);
                _status.Text = $"Рендер: {clamped}%";
                _pbRenderProgress.Value = clamped;
            });
            Action<byte[]>? coverageCallback = _showCoverageMap.Checked
                ? heatmap => UpdateCoverageOverlay(heatmap, bmp.Width, bmp.Height, data.Stride)
                : null;

            try
            {
                await Task.Run(() => _engine.RenderToBuffer(
                    buffer,
                    bmp.Width,
                    bmp.Height,
                    data.Stride,
                    4,
                    token,
                    p => ((IProgress<int>)progress).Report(p),
                    reportCoverageHeatmap: coverageCallback,
                    coverageUpdateIntervalMs: 200), token);
                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
                renderStopwatch.Stop();
                _status.Text = "Готово";
                _pbRenderProgress.Value = 100;
                _lastRenderTime.Text = $"Последний рендер: {DateTime.Now:HH:mm:ss} ({renderStopwatch.Elapsed.TotalSeconds:F2} сек.)";
                Text = $"{_baseTitle} - Время последнего рендера: {renderStopwatch.Elapsed.TotalSeconds:F3} сек.";
            }
            catch (OperationCanceledException)
            {
                _status.Text = "Отменено";
                _pbRenderProgress.Value = 0;
            }
            finally
            {
                bmp.UnlockBits(data);
                if (!token.IsCancellationRequested)
                {
                    lock (_previewLock)
                    {
                        Bitmap? old = _previewBitmap;
                        _previewBitmap = bmp;
                        _renderedCenterX = (double)_centerX.Value;
                        _renderedCenterY = (double)_centerY.Value;
                        _renderedScale = NormalizeScale((double)_scale.Value);
                        old?.Dispose();
                    }
                    _canvas.Invalidate();
                }
                else
                {
                    bmp.Dispose();
                }
                _btnRender.Enabled = true;
                _isRenderInProgress = false;
                ClearCoverageOverlay();
            }
            }
            finally
            {
                _renderExecutionLock.Release();
            }
        }

        private void UpdateCoverageOverlay(byte[] heatmapBuffer, int width, int height, int stride)
        {
            if (!_showCoverageMap.Checked || tokenSafeIsDisposed())
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => UpdateCoverageOverlay(heatmapBuffer, width, height, stride)));
                return;
            }

            Bitmap overlay = new(width, height, PixelFormat.Format32bppArgb);
            BitmapData overlayData = overlay.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, overlay.PixelFormat);
            Marshal.Copy(heatmapBuffer, 0, overlayData.Scan0, heatmapBuffer.Length);
            overlay.UnlockBits(overlayData);

            lock (_previewLock)
            {
                Bitmap? old = _coverageOverlayBitmap;
                _coverageOverlayBitmap = overlay;
                old?.Dispose();

                if (!_hasIntermediateFrame)
                {
                    Bitmap? previousPreview = _previewBitmap;
                    _previewBitmap = (Bitmap)overlay.Clone();
                    _renderedCenterX = _activeRenderCenterX;
                    _renderedCenterY = _activeRenderCenterY;
                    _renderedScale = _activeRenderScale;
                    _hasIntermediateFrame = true;
                    previousPreview?.Dispose();
                }
            }

            _canvas.Invalidate();
        }

        private void ClearCoverageOverlay()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ClearCoverageOverlay));
                return;
            }

            lock (_previewLock)
            {
                Bitmap? old = _coverageOverlayBitmap;
                _coverageOverlayBitmap = null;
                old?.Dispose();
            }

            _canvas.Invalidate();
        }

        private bool tokenSafeIsDisposed() => IsDisposed || !IsHandleCreated;

        public string FractalTypeIdentifier => "Flame";
        public Type ConcreteSaveStateType => typeof(FlameFractalSaveState);

        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            ApplyUiToEngine();
            return new FlameFractalSaveState(FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _engine.CenterX,
                CenterY = _engine.CenterY,
                Scale = _engine.Scale,
                Samples = _engine.Samples,
                IterationsPerSample = _engine.IterationsPerSample,
                WarmupIterations = _engine.WarmupIterations,
                Exposure = _engine.Exposure,
                Gamma = _engine.Gamma,
                Transforms = _engine.Transforms.Select(t => t.Clone()).ToList()
            };
        }

        public void LoadState(FractalSaveStateBase state)
        {
            if (state is not FlameFractalSaveState s)
            {
                return;
            }

            _centerX.Value = (decimal)s.CenterX;
            _centerY.Value = (decimal)s.CenterY;
            _scale.Value = (decimal)NormalizeScale(Math.Clamp(s.Scale, (double)_scale.Minimum, (double)_scale.Maximum));
            _samples.Value = Math.Clamp(s.Samples, (int)_samples.Minimum, (int)_samples.Maximum);
            _iterations.Value = Math.Clamp(s.IterationsPerSample, (int)_iterations.Minimum, (int)_iterations.Maximum);
            _warmup.Value = Math.Clamp(s.WarmupIterations, (int)_warmup.Minimum, (int)_warmup.Maximum);
            _exposure.Value = (decimal)Math.Clamp(s.Exposure, (double)_exposure.Minimum, (double)_exposure.Maximum);
            _gamma.Value = (decimal)Math.Clamp(s.Gamma, (double)_gamma.Minimum, (double)_gamma.Maximum);
            _engine.Transforms.Clear();
            _engine.Transforms.AddRange((s.Transforms ?? new List<FlameTransform>()).Select(t => t.Clone()));
            _canvas.Invalidate();
            QueueRenderRestart(immediate: true);
        }

        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            using CancellationTokenSource cts = new();
            return RenderPreviewCore(state, previewWidth, previewHeight, cts.Token, null);
        }

        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            if (state is not FlameFractalSaveState)
            {
                return Array.Empty<byte>();
            }

            Rectangle fullRect = new(0, 0, Math.Max(1, totalWidth), Math.Max(1, totalHeight));
            Rectangle tileRect = Rectangle.Intersect(fullRect, tile.Bounds);
            if (tileRect.Width <= 0 || tileRect.Height <= 0)
            {
                return Array.Empty<byte>();
            }

            if (tileSize > 0)
            {
                int maxTileDimension = Math.Max(tileRect.Width, tileRect.Height);
                if (maxTileDimension > tileSize)
                {
                    tileRect = new Rectangle(tileRect.X, tileRect.Y, Math.Min(tileRect.Width, tileSize), Math.Min(tileRect.Height, tileSize));
                }
            }

            using CancellationTokenSource cts = new();
            using Bitmap bmp = RenderPreviewTileCore(state, tileRect, fullRect.Width, fullRect.Height, cts.Token, null);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] bytes = new byte[Math.Abs(data.Stride) * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            bmp.UnlockBits(data);
            await Task.CompletedTask;
            return bytes;
        }

        public async Task<byte[]> RenderPreviewAsync(
            FractalSaveStateBase state,
            int previewWidth,
            int previewHeight,
            CancellationToken cancellationToken,
            IProgress<int>? progress = null)
        {
            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            using Bitmap bmp = await Task.Run(() => RenderPreviewCore(state, width, height, cancellationToken, progress), cancellationToken);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] bytes = new byte[Math.Abs(data.Stride) * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            bmp.UnlockBits(data);
            return bytes;
        }


        private Bitmap RenderPreviewTileCore(FractalSaveStateBase state, Rectangle tileRect, int totalWidth, int totalHeight, CancellationToken token, IProgress<int>? progress)
        {
            FlameFractalSaveState? save = state as FlameFractalSaveState;
            var engine = new FractalFlameEngine
            {
                CenterX = save?.CenterX ?? _engine.CenterX,
                CenterY = save?.CenterY ?? _engine.CenterY,
                Scale = NormalizeScale(save?.Scale ?? _engine.Scale),
                Samples = Math.Max(50_000, (save?.Samples ?? _engine.Samples) / 10),
                IterationsPerSample = save?.IterationsPerSample ?? _engine.IterationsPerSample,
                WarmupIterations = save?.WarmupIterations ?? _engine.WarmupIterations,
                Exposure = save?.Exposure ?? _engine.Exposure,
                Gamma = save?.Gamma ?? _engine.Gamma,
                ThreadCount = 0
            };

            engine.Transforms.AddRange((save?.Transforms ?? _engine.Transforms).Select(t => t.Clone()));
            Bitmap bmp = new(tileRect.Width, tileRect.Height, PixelFormat.Format32bppArgb);
            Rectangle rect = new(0, 0, tileRect.Width, tileRect.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
            byte[] buffer = new byte[Math.Abs(data.Stride) * data.Height];
            engine.RenderToBuffer(
                buffer,
                tileRect.Width,
                tileRect.Height,
                data.Stride,
                4,
                token,
                p => progress?.Report(p),
                tileRect.X,
                tileRect.Y,
                totalWidth,
                totalHeight);
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        private Bitmap RenderPreviewCore(FractalSaveStateBase state, int width, int height, CancellationToken token, IProgress<int>? progress)
        {
            FlameFractalSaveState? save = state as FlameFractalSaveState;
            var engine = new FractalFlameEngine
            {
                CenterX = save?.CenterX ?? _engine.CenterX,
                CenterY = save?.CenterY ?? _engine.CenterY,
                Scale = NormalizeScale(save?.Scale ?? _engine.Scale),
                Samples = Math.Max(50_000, (save?.Samples ?? _engine.Samples) / 10),
                IterationsPerSample = save?.IterationsPerSample ?? _engine.IterationsPerSample,
                WarmupIterations = save?.WarmupIterations ?? _engine.WarmupIterations,
                Exposure = save?.Exposure ?? _engine.Exposure,
                Gamma = save?.Gamma ?? _engine.Gamma,
                ThreadCount = 0
            };

            engine.Transforms.AddRange((save?.Transforms ?? _engine.Transforms).Select(t => t.Clone()));
            Bitmap bmp = new(width, height, PixelFormat.Format32bppArgb);
            Rectangle rect = new(0, 0, width, height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
            byte[] buffer = new byte[Math.Abs(data.Stride) * data.Height];
            engine.RenderToBuffer(buffer, width, height, data.Stride, 4, token, p => progress?.Report(p));
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        public HighResRenderState GetRenderState()
        {
            return new HighResRenderState
            {
                EngineType = FractalTypeIdentifier,
                CenterX = (decimal)_engine.CenterX,
                CenterY = (decimal)_engine.CenterY,
                Scale = (decimal)_engine.Scale,
                Iterations = _engine.IterationsPerSample,
                BuddhabrotSampleCount = _engine.Samples,
                OrbitTrapStrength = _engine.Exposure,
                OrbitTrapBias = _engine.Gamma,
                FileNameDetails = "flame_fractal"
            };
        }

        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            int renderWidth = Math.Max(1, width * Math.Max(1, ssaaFactor));
            int renderHeight = Math.Max(1, height * Math.Max(1, ssaaFactor));

            var save = new FlameFractalSaveState(FractalTypeIdentifier)
            {
                CenterX = (double)state.CenterX,
                CenterY = (double)state.CenterY,
                Scale = NormalizeScale(state.Scale == 0 ? _engine.Scale : (double)state.Scale),
                IterationsPerSample = state.Iterations > 0 ? state.Iterations : _engine.IterationsPerSample,
                Samples = state.BuddhabrotSampleCount ?? _engine.Samples,
                Exposure = state.OrbitTrapStrength > 0 ? state.OrbitTrapStrength : _engine.Exposure,
                Gamma = state.OrbitTrapBias > 0 ? state.OrbitTrapBias : _engine.Gamma,
                WarmupIterations = _engine.WarmupIterations,
                Transforms = _engine.Transforms.Select(t => t.Clone()).ToList()
            };

            Bitmap full = await Task.Run(() => RenderPreviewCore(save, renderWidth, renderHeight, cancellationToken, new Progress<int>(p =>
            {
                progress.Report(new RenderProgress { Percentage = p, Status = "Рендеринг..." });
            })), cancellationToken);

            if (ssaaFactor <= 1)
            {
                return full;
            }

            Bitmap downscaled = new(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(downscaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(full, new Rectangle(0, 0, width, height));
            }
            full.Dispose();
            return downscaled;
        }

        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            using var cts = new CancellationTokenSource();
            var save = new FlameFractalSaveState(FractalTypeIdentifier)
            {
                CenterX = (double)state.CenterX,
                CenterY = (double)state.CenterY,
                Scale = NormalizeScale(state.Scale == 0 ? _engine.Scale : (double)state.Scale),
                IterationsPerSample = state.Iterations > 0 ? state.Iterations : _engine.IterationsPerSample,
                Samples = Math.Max(20_000, (state.BuddhabrotSampleCount ?? _engine.Samples) / 8),
                Exposure = state.OrbitTrapStrength > 0 ? state.OrbitTrapStrength : _engine.Exposure,
                Gamma = state.OrbitTrapBias > 0 ? state.OrbitTrapBias : _engine.Gamma,
                WarmupIterations = _engine.WarmupIterations,
                Transforms = _engine.Transforms.Select(t => t.Clone()).ToList()
            };
            return RenderPreviewCore(save, previewWidth, previewHeight, cts.Token, null);
        }

        public List<FractalSaveStateBase> LoadAllSavesForThisType() =>
            SaveFileManager.LoadSaves<FlameFractalSaveState>(FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves) =>
            SaveFileManager.SaveSaves(FractalTypeIdentifier, saves.Cast<FlameFractalSaveState>().ToList());

        private static double NormalizeScale(double scale)
        {
            if (Math.Abs(scale) >= MinScaleMagnitude)
            {
                return scale;
            }

            return scale < 0 ? -MinScaleMagnitude : MinScaleMagnitude;
        }
    }
}
