using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using FractalExplorer.Engines;
using FractalExplorer.Forms.Common;
using FractalExplorer.Forms.Other;
using FractalExplorer.Utilities;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using FractalExplorer.Utilities.UI;

namespace FractalExplorer.Forms.Fractals
{
    public partial class FractalLogisticMapForm : Form, ISaveLoadCapableFractal, IFullPreviewRenderCapableFractal, IHighResRenderable
    {
        private enum LogisticVisualizationMode
        {
            Orbit,
            Bifurcation,
            Cobweb
        }

        private sealed class LogisticRenderSettings
        {
            public int Iterations { get; init; }
            public int TransientIterations { get; init; }
            public decimal R { get; init; }
            public decimal X0 { get; init; }
            public decimal BifurcationRMin { get; init; }
            public decimal BifurcationRMax { get; init; }
            public int BifurcationSamples { get; init; }
            public int BifurcationTransient { get; init; }
            public int BifurcationPlottedPoints { get; init; }
            public int CobwebSteps { get; init; }
            public List<Color> PaletteColors { get; init; } = new();
        }

        private const decimal BaseScale = 1.0m;
        private const string AutoThreadOptionText = "Авто";

        private readonly LogisticMapPaletteManager _paletteManager = new();
        private ColorConfigurationForm? _colorConfigForm;
        private readonly object _bitmapLock = new();
        private readonly FullscreenToggleController _fullscreenController = new();
        private readonly string _baseTitle;
        private readonly System.Windows.Forms.Timer _wheelDebounceTimer = new();
        private readonly System.Windows.Forms.Timer _resizeDebounceTimer = new();

        private Bitmap? _previewBitmap;
        private CancellationTokenSource? _renderCts;
        private int _renderGeneration;
        private bool _isHighResRendering;
        private bool _isPanning;
        private bool _isFormClosing;
        private bool _suppressZoomValueChanged;
        private int _controlsOpenWidth = 231;
        private Point _panStart;

        private Color _backgroundColor = Color.Black;

        private decimal _centerX = 0.5m;
        private decimal _centerY = 0.5m;
        private decimal _zoom = 1.0m;
        private decimal _renderCenterX = 0.5m;
        private decimal _renderCenterY = 0.5m;
        private decimal _renderZoom = 1.0m;
        private LogisticVisualizationMode _mode = LogisticVisualizationMode.Orbit;

        public FractalLogisticMapForm()
        {
            InitializeComponent();
            KeyPreview = true;
            _baseTitle = Text;

            ApplyDefaults();
            _wheelDebounceTimer.Interval = 140;
            _wheelDebounceTimer.Tick += (_, _) =>
            {
                _wheelDebounceTimer.Stop();
                ScheduleRender();
            };
            _resizeDebounceTimer.Interval = 260;
            _resizeDebounceTimer.Tick += (_, _) =>
            {
                _resizeDebounceTimer.Stop();
                ScheduleRender();
            };
            _canvas.Paint += Canvas_Paint;
            _canvas.MouseWheel += Canvas_MouseWheel;
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseLeave += Canvas_MouseUp;
            _canvas.MouseEnter += (_, _) => _canvas.Focus();
            _canvas.Resize += (_, _) => QueueRenderAfterResizeInteraction();
            KeyDown += Form_KeyDown;

            FormClosing += (_, _) =>
            {
                _isFormClosing = true;
                CancelRender();
                lock (_bitmapLock)
                {
                    _previewBitmap?.Dispose();
                    _previewBitmap = null;
                }
                _wheelDebounceTimer.Stop();
                _wheelDebounceTimer.Dispose();
                _resizeDebounceTimer.Stop();
                _resizeDebounceTimer.Dispose();
                _colorConfigForm?.Dispose();
                _colorConfigForm = null;
            };

            Shown += (_, _) =>
            {
                _canvas.Focus();
                ScheduleRender();
            };
        }

        private void ApplyDefaults()
        {
            _cbVisualizationMode.Items.Clear();
            _cbVisualizationMode.Items.AddRange(new object[] { "Орбиты", "Бифуркация", "Кобвеб" });
            _cbVisualizationMode.SelectedIndex = 0;

            ConfigureDecimal(_nudR, 6, 0.0001m, 0m, 4m, 3.8m);
            ConfigureDecimal(_nudX0, 6, 0.0001m, 0m, 1m, 0.2m);
            _nudIterations.Minimum = 32;
            _nudIterations.Maximum = 200000;
            _nudIterations.Value = 2500;

            _nudTransient.Minimum = 0;
            _nudTransient.Maximum = 100000;
            _nudTransient.Value = 500;

            ConfigureDecimal(_nudBifurcationRMin, 6, 0.0001m, 0m, 4m, 2.8m);
            ConfigureDecimal(_nudBifurcationRMax, 6, 0.0001m, 0m, 4m, 4.0m);
            _nudBifurcationSamples.Minimum = 32;
            _nudBifurcationSamples.Maximum = 20000;
            _nudBifurcationSamples.Value = 1600;
            _nudBifurcationTransient.Minimum = 0;
            _nudBifurcationTransient.Maximum = 50000;
            _nudBifurcationTransient.Value = 500;
            _nudBifurcationPlotted.Minimum = 1;
            _nudBifurcationPlotted.Maximum = 5000;
            _nudBifurcationPlotted.Value = 240;

            _nudCobwebSteps.Minimum = 1;
            _nudCobwebSteps.Maximum = 5000;
            _nudCobwebSteps.Value = 40;

            ConfigureDecimal(_nudZoom, 6, 0.05m, 0.01m, 1000000m, 1.0m);
            _nudZoom.ValueChanged += (_, _) =>
            {
                _zoom = _nudZoom.Value;
                if (_suppressZoomValueChanged) return;
                ScheduleRender();
            };

            int cores = Environment.ProcessorCount;
            _cbThreads.Items.Clear();
            for (int i = 1; i <= cores; i++) _cbThreads.Items.Add(i);
            _cbThreads.Items.Add(AutoThreadOptionText);
            _cbThreads.SelectedItem = AutoThreadOptionText;

            _nudR.ValueChanged += (_, _) => ScheduleRender();
            _nudX0.ValueChanged += (_, _) => ScheduleRender();
            _nudIterations.ValueChanged += (_, _) => ScheduleRender();
            _nudTransient.ValueChanged += (_, _) => ScheduleRender();
            _nudBifurcationRMin.ValueChanged += (_, _) => ScheduleRender();
            _nudBifurcationRMax.ValueChanged += (_, _) => ScheduleRender();
            _nudBifurcationSamples.ValueChanged += (_, _) => ScheduleRender();
            _nudBifurcationTransient.ValueChanged += (_, _) => ScheduleRender();
            _nudBifurcationPlotted.ValueChanged += (_, _) => ScheduleRender();
            _nudCobwebSteps.ValueChanged += (_, _) => ScheduleRender();
            _cbThreads.SelectedIndexChanged += (_, _) => ScheduleRender();
            _cbVisualizationMode.SelectedIndexChanged += (_, _) =>
            {
                _mode = GetSelectedMode();
                UpdateControlsForMode();
                ScheduleRender();
            };

            _mode = GetSelectedMode();
            UpdateControlsForMode();
        }

        private LogisticVisualizationMode GetSelectedMode()
        {
            return _cbVisualizationMode.SelectedIndex switch
            {
                1 => LogisticVisualizationMode.Bifurcation,
                2 => LogisticVisualizationMode.Cobweb,
                _ => LogisticVisualizationMode.Orbit
            };
        }

        private void SetSelectedMode(LogisticVisualizationMode mode)
        {
            _mode = mode;
            _cbVisualizationMode.SelectedIndex = mode switch
            {
                LogisticVisualizationMode.Bifurcation => 1,
                LogisticVisualizationMode.Cobweb => 2,
                _ => 0
            };
            UpdateControlsForMode();
        }

        private static LogisticVisualizationMode ParseMode(string? raw)
        {
            return Enum.TryParse(raw, true, out LogisticVisualizationMode parsed) ? parsed : LogisticVisualizationMode.Orbit;
        }

        private void UpdateControlsForMode()
        {
            bool bifurcation = _mode == LogisticVisualizationMode.Bifurcation;
            bool cobweb = _mode == LogisticVisualizationMode.Cobweb;

            _nudBifurcationRMin.Enabled = bifurcation;
            _nudBifurcationRMax.Enabled = bifurcation;
            _nudBifurcationSamples.Enabled = bifurcation;
            _nudBifurcationTransient.Enabled = bifurcation;
            _nudBifurcationPlotted.Enabled = bifurcation;

            _nudCobwebSteps.Enabled = cobweb;
        }

        private static void ConfigureDecimal(NumericUpDown control, int decimals, decimal increment, decimal min, decimal max, decimal value)
        {
            control.DecimalPlaces = decimals;
            control.Increment = increment;
            control.Minimum = min;
            control.Maximum = max;
            control.Value = value;
        }

        private void Form_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F11)
            {
                _fullscreenController.Toggle(this);
                ScheduleRender();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape && _fullscreenController.IsFullscreen(this))
            {
                _fullscreenController.ExitFullscreen(this);
                ScheduleRender();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (_isHighResRendering || _canvas.Width <= 0 || _canvas.Height <= 0) return;
            if (!_canvas.Focused) _canvas.Focus();

            decimal zoomFactor = e.Delta > 0 ? 1.5m : 1.0m / 1.5m;
            decimal scaleBeforeZoom = BaseScale / _zoom;
            decimal mouseReal = _centerX + (e.X - _canvas.Width / 2.0m) * scaleBeforeZoom / _canvas.Width;
            decimal mouseImaginary = _centerY - (e.Y - _canvas.Height / 2.0m) * scaleBeforeZoom / _canvas.Height;

            decimal newZoom;
            if (zoomFactor > 1.0m)
            {
                if (_zoom > _nudZoom.Maximum / zoomFactor) newZoom = _nudZoom.Maximum;
                else newZoom = _zoom * zoomFactor;
            }
            else
            {
                if (_zoom < _nudZoom.Minimum / zoomFactor) newZoom = _nudZoom.Minimum;
                else newZoom = _zoom * zoomFactor;
            }

            _zoom = Math.Max(_nudZoom.Minimum, Math.Min(_nudZoom.Maximum, newZoom));
            decimal scaleAfterZoom = BaseScale / _zoom;
            _centerX = mouseReal - (e.X - _canvas.Width / 2.0m) * scaleAfterZoom / _canvas.Width;
            _centerY = mouseImaginary + (e.Y - _canvas.Height / 2.0m) * scaleAfterZoom / _canvas.Height;

            _canvas.Invalidate();
            if (_nudZoom.Value != _zoom)
            {
                _suppressZoomValueChanged = true;
                try
                {
                    _nudZoom.Value = _zoom;
                }
                finally
                {
                    _suppressZoomValueChanged = false;
                }
            }
            QueueRenderAfterWheelInteraction();
        }

        private void Canvas_MouseDown(object? sender, MouseEventArgs e)
        {
            if (_isHighResRendering) return;
            if (e.Button == MouseButtons.Left)
            {
                if (!_canvas.Focused) _canvas.Focus();
                _isPanning = true;
                _panStart = e.Location;
                _canvas.Cursor = Cursors.SizeAll;
            }
        }

        private void Canvas_MouseMove(object? sender, MouseEventArgs e)
        {
            if (_isHighResRendering || !_isPanning || _canvas.Width <= 0) return;

            decimal scale = BaseScale / _zoom;
            decimal unitsPerPixelX = scale / _canvas.Width;
            decimal unitsPerPixelY = scale / Math.Max(1, _canvas.Height);
            _centerX -= (e.X - _panStart.X) * unitsPerPixelX;
            _centerY += (e.Y - _panStart.Y) * unitsPerPixelY;
            _panStart = e.Location;
            _canvas.Invalidate();
        }

        private void Canvas_MouseUp(object? sender, EventArgs e)
        {
            if (_isHighResRendering || !_isPanning) return;

            _isPanning = false;
            _canvas.Cursor = Cursors.Default;
            ScheduleRender();
        }

        private void Canvas_Paint(object? sender, PaintEventArgs e)
        {
            if (_backgroundColor.A == 0)
            {
                DrawTransparencyChecker(e.Graphics, _canvas.ClientRectangle);
            }
            else
            {
                e.Graphics.Clear(_backgroundColor);
            }

            lock (_bitmapLock)
            {
                if (_previewBitmap != null)
                {
                    e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    RectangleF destination = CalculateDestinationRectangle(_canvas.ClientSize, _previewBitmap.Size);
                    e.Graphics.DrawImage(_previewBitmap, destination);
                }
            }
        }

        private static void DrawTransparencyChecker(Graphics graphics, Rectangle bounds)
        {
            graphics.Clear(Color.White);
            const int checkerSize = 12;
            using Brush checkerBrush = new SolidBrush(Color.FromArgb(232, 232, 232));
            for (int y = 0; y < bounds.Height; y += checkerSize)
            {
                int row = y / checkerSize;
                for (int x = (row % 2 == 0 ? 0 : checkerSize); x < bounds.Width; x += checkerSize * 2)
                {
                    graphics.FillRectangle(checkerBrush, x, y, checkerSize, checkerSize);
                }
            }
        }

        private RectangleF CalculateDestinationRectangle(Size canvasSize, Size imageSize)
        {
            float canvasWidth = Math.Max(1, canvasSize.Width);
            float canvasHeight = Math.Max(1, canvasSize.Height);
            float imageWidth = Math.Max(1, imageSize.Width);
            float imageHeight = Math.Max(1, imageSize.Height);

            decimal zoomRatio = _zoom / _renderZoom;
            float scaledWidth = (float)(imageWidth * (float)zoomRatio);
            float scaledHeight = (float)(imageHeight * (float)zoomRatio);

            float offsetX = (canvasWidth - scaledWidth) / 2f
                            + (float)((_renderCenterX - _centerX) * _zoom * (decimal)canvasWidth / BaseScale);
            float offsetY = (canvasHeight - scaledHeight) / 2f
                            + (float)((_centerY - _renderCenterY) * _zoom * (decimal)canvasHeight / BaseScale);

            return new RectangleF(offsetX, offsetY, scaledWidth, scaledHeight);
        }

        private void QueueRenderAfterWheelInteraction()
        {
            if (_isHighResRendering || IsDisposed) return;
            _wheelDebounceTimer.Stop();
            _wheelDebounceTimer.Start();
        }

        private void QueueRenderAfterResizeInteraction()
        {
            if (_isHighResRendering || IsDisposed) return;
            _resizeDebounceTimer.Stop();
            _resizeDebounceTimer.Start();
        }

        private void ScheduleRender()
        {
            if (_isFormClosing || _canvas.Width <= 1 || _canvas.Height <= 1 || IsDisposed) return;

            CancellationTokenSource cts = StartNewRender();
            int renderGeneration = Interlocked.Increment(ref _renderGeneration);
            _ = RenderAsync(cts, renderGeneration);
        }

        private async Task RenderAsync(CancellationTokenSource cts, int renderGeneration)
        {
            if (!_isFormClosing && !IsDisposed && _pbRenderProgress.IsHandleCreated)
            {
                _pbRenderProgress.Style = ProgressBarStyle.Blocks;
                _pbRenderProgress.Minimum = 0;
                _pbRenderProgress.Maximum = 100;
                _pbRenderProgress.Value = 0;
            }

            var progress = new Progress<int>(value =>
            {
                if (_isFormClosing || IsDisposed || !_pbRenderProgress.IsHandleCreated) return;
                _pbRenderProgress.Value = Math.Clamp(value, _pbRenderProgress.Minimum, _pbRenderProgress.Maximum);
            });

            try
            {
                int width = Math.Max(1, _canvas.Width);
                int height = Math.Max(1, _canvas.Height);
                decimal renderCenterX = _centerX;
                decimal renderCenterY = _centerY;
                decimal renderZoom = _zoom;
                LogisticVisualizationMode mode = _mode;
                LogisticRenderSettings renderSettings = CaptureUiRenderSettings();
                byte[] buffer = await Task.Run(() => RenderCurrentModeBuffer(mode, width, height, renderCenterX, renderCenterY, renderZoom, renderSettings, cts.Token, progress), cts.Token);

                if (cts.IsCancellationRequested || renderGeneration != Volatile.Read(ref _renderGeneration) || _isFormClosing || IsDisposed)
                {
                    return;
                }

                var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var rect = new Rectangle(0, 0, width, height);
                BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
                bmp.UnlockBits(data);

                lock (_bitmapLock)
                {
                    _previewBitmap?.Dispose();
                    _previewBitmap = bmp;
                    _renderCenterX = renderCenterX;
                    _renderCenterY = renderCenterY;
                    _renderZoom = renderZoom;
                }

                _canvas.Invalidate();
                if (!_isFormClosing && !IsDisposed && _pbRenderProgress.IsHandleCreated)
                {
                    _pbRenderProgress.Value = 100;
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                CancellationTokenSource? current = Interlocked.CompareExchange(ref _renderCts, null, cts);
                if (ReferenceEquals(current, cts))
                {
                    cts.Dispose();
                }

                if (!_isFormClosing && !IsDisposed && _pbRenderProgress.IsHandleCreated && renderGeneration == Volatile.Read(ref _renderGeneration))
                {
                    _pbRenderProgress.Value = 0;
                }

                if (!_isFormClosing && !IsDisposed && renderGeneration == Volatile.Read(ref _renderGeneration))
                {
                    Text = _baseTitle;
                }
            }
        }

        private byte[] RenderOrbitBuffer(int width, int height, CancellationToken ct, IProgress<int>? progress = null)
        {
            return RenderOrbitBuffer(width, height, _centerX, _centerY, _zoom, CaptureUiRenderSettings(), ct, progress, GetThreadCount());
        }

        private byte[] RenderCurrentModeBuffer(
            LogisticVisualizationMode mode,
            int width,
            int height,
            decimal centerX,
            decimal centerY,
            decimal zoom,
            CancellationToken ct,
            IProgress<int>? progress = null,
            int? maxDegreeOfParallelism = null,
            bool drawAxes = true,
            Color backgroundColor = default)
        {
            return RenderCurrentModeBuffer(mode, width, height, centerX, centerY, zoom, CaptureUiRenderSettings(), ct, progress, maxDegreeOfParallelism, drawAxes, backgroundColor);
        }

        private byte[] RenderCurrentModeBuffer(
            LogisticVisualizationMode mode,
            int width,
            int height,
            decimal centerX,
            decimal centerY,
            decimal zoom,
            LogisticRenderSettings settings,
            CancellationToken ct,
            IProgress<int>? progress = null,
            int? maxDegreeOfParallelism = null,
            bool drawAxes = true,
            Color backgroundColor = default)
        {
            return mode switch
            {
                LogisticVisualizationMode.Bifurcation => RenderBifurcationBuffer(width, height, centerX, centerY, zoom, settings, ct, progress, maxDegreeOfParallelism, drawAxes, backgroundColor),
                LogisticVisualizationMode.Cobweb => RenderCobwebBuffer(width, height, centerX, centerY, zoom, settings, ct, progress, backgroundColor),
                _ => RenderOrbitBuffer(width, height, centerX, centerY, zoom, settings, ct, progress, maxDegreeOfParallelism, drawAxes, backgroundColor)
            };
        }

        private LogisticRenderSettings CaptureUiRenderSettings()
        {
            return new LogisticRenderSettings
            {
                Iterations = (int)_nudIterations.Value,
                TransientIterations = (int)_nudTransient.Value,
                R = _nudR.Value,
                X0 = _nudX0.Value,
                BifurcationRMin = _nudBifurcationRMin.Value,
                BifurcationRMax = _nudBifurcationRMax.Value,
                BifurcationSamples = (int)_nudBifurcationSamples.Value,
                BifurcationTransient = (int)_nudBifurcationTransient.Value,
                BifurcationPlottedPoints = (int)_nudBifurcationPlotted.Value,
                CobwebSteps = (int)_nudCobwebSteps.Value,
                PaletteColors = _paletteManager.ActivePalette.Colors.ToList()
            };
        }

        private static LogisticRenderSettings BuildRenderSettingsFromSaveState(LogisticMapSaveState logistic, LogisticMapPaletteManager paletteManager)
        {
            List<Color> paletteColors = paletteManager.ActivePalette.Colors.ToList();
            if (!string.IsNullOrWhiteSpace(logistic.PaletteName))
            {
                var palette = paletteManager.Palettes.FirstOrDefault(p => p.Name == logistic.PaletteName);
                if (palette != null)
                {
                    paletteColors = palette.Colors.ToList();
                }
            }

            return new LogisticRenderSettings
            {
                Iterations = Math.Max(1, logistic.Iterations),
                TransientIterations = Math.Max(0, logistic.TransientIterations),
                R = logistic.R,
                X0 = logistic.X0,
                BifurcationRMin = logistic.BifurcationRMin,
                BifurcationRMax = logistic.BifurcationRMax,
                BifurcationSamples = Math.Max(1, logistic.BifurcationSamples),
                BifurcationTransient = Math.Max(0, logistic.BifurcationTransient),
                BifurcationPlottedPoints = Math.Max(1, logistic.BifurcationPlottedPoints),
                CobwebSteps = Math.Max(1, logistic.CobwebSteps),
                PaletteColors = paletteColors
            };
        }

        private byte[] RenderOrbitBuffer(
            int width,
            int height,
            decimal centerX,
            decimal centerY,
            decimal zoom,
            LogisticRenderSettings settings,
            CancellationToken ct,
            IProgress<int>? progress = null,
            int? maxDegreeOfParallelism = null,
            bool drawAxes = true,
            Color backgroundColor = default)
        {
            return FractalLogisticMapEngine.RenderOrbitBuffer(width, height, centerX, centerY, zoom, ToEngineSettings(settings), ct, progress, maxDegreeOfParallelism, drawAxes, backgroundColor);
        }

        private byte[] RenderBifurcationBuffer(
            int width,
            int height,
            decimal centerX,
            decimal centerY,
            decimal zoom,
            LogisticRenderSettings settings,
            CancellationToken ct,
            IProgress<int>? progress = null,
            int? maxDegreeOfParallelism = null,
            bool drawAxes = true,
            Color backgroundColor = default)
        {
            return FractalLogisticMapEngine.RenderBifurcationBuffer(width, height, centerX, centerY, zoom, ToEngineSettings(settings), ct, progress, maxDegreeOfParallelism, drawAxes, backgroundColor);
        }

        private byte[] RenderCobwebBuffer(
            int width,
            int height,
            decimal centerX,
            decimal centerY,
            decimal zoom,
            LogisticRenderSettings settings,
            CancellationToken ct,
            IProgress<int>? progress = null,
            Color backgroundColor = default)
        {
            return FractalLogisticMapEngine.RenderCobwebBuffer(width, height, ToEngineSettings(settings), ct, progress, backgroundColor);
        }

        private static FractalLogisticMapEngine.RenderSettings ToEngineSettings(LogisticRenderSettings settings)
        {
            return new FractalLogisticMapEngine.RenderSettings
            {
                Iterations = settings.Iterations,
                TransientIterations = settings.TransientIterations,
                R = settings.R,
                X0 = settings.X0,
                BifurcationRMin = settings.BifurcationRMin,
                BifurcationRMax = settings.BifurcationRMax,
                BifurcationSamples = settings.BifurcationSamples,
                BifurcationTransient = settings.BifurcationTransient,
                BifurcationPlottedPoints = settings.BifurcationPlottedPoints,
                CobwebSteps = settings.CobwebSteps,
                PaletteColors = settings.PaletteColors.ToList()
            };
        }

        private CancellationTokenSource StartNewRender()
        {
            var next = new CancellationTokenSource();
            CancellationTokenSource? prev = Interlocked.Exchange(ref _renderCts, next);
            prev?.Cancel();
            prev?.Dispose();
            return next;
        }

        private void CancelRender()
        {
            CancellationTokenSource? current = Interlocked.Exchange(ref _renderCts, null);
            current?.Cancel();
            current?.Dispose();
        }

        private int GetThreadCount()
        {
            if (InvokeRequired)
            {
                return (int)Invoke(new Func<int>(GetThreadCount));
            }

            if (_cbThreads.SelectedItem?.ToString() == AutoThreadOptionText) return Environment.ProcessorCount;
            if (_cbThreads.SelectedItem is int selected) return Math.Max(1, selected);
            return Environment.ProcessorCount;
        }

        private void btnToggleControls_Click(object sender, EventArgs e)
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

        private void btnBackgroundColor_Click(object sender, EventArgs e)
        {
            using var dialog = new ColorPickerPanelForm(_backgroundColor);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _backgroundColor = dialog.SelectedColor;
                _canvas.Invalidate();
                ScheduleRender();
            }
        }

        private void btnRender_Click(object sender, EventArgs e) => ScheduleRender();

        private void btnReset_Click(object sender, EventArgs e)
        {
            _centerX = 0.5m;
            _centerY = 0.5m;
            _zoom = 1.0m;
            _nudZoom.Value = _zoom;
            ScheduleRender();
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
            if (_colorConfigForm == null || _colorConfigForm.IsDisposed)
            {
                _colorConfigForm = new ColorConfigurationForm(_paletteManager, "Палитры (Логистическое отображение)");
                _colorConfigForm.PaletteApplied += (_, _) => ScheduleRender();
            }

            _colorConfigForm.Show(this);
            _colorConfigForm.BringToFront();
        }

        public string FractalTypeIdentifier => "LogisticMap";
        public Type ConcreteSaveStateType => typeof(LogisticMapSaveState);

        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new LogisticMapSaveState(FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                R = _nudR.Value,
                X0 = _nudX0.Value,
                Iterations = (int)_nudIterations.Value,
                TransientIterations = (int)_nudTransient.Value,
                VisualizationMode = _mode.ToString(),
                BifurcationRMin = _nudBifurcationRMin.Value,
                BifurcationRMax = _nudBifurcationRMax.Value,
                BifurcationSamples = (int)_nudBifurcationSamples.Value,
                BifurcationTransient = (int)_nudBifurcationTransient.Value,
                BifurcationPlottedPoints = (int)_nudBifurcationPlotted.Value,
                CobwebSteps = (int)_nudCobwebSteps.Value,
                PaletteName = _paletteManager.ActivePalette.Name
            };

            state.PreviewParametersJson = JsonSerializer.Serialize(new
            {
                state.CenterX,
                state.CenterY,
                state.Zoom,
                state.R,
                state.X0,
                state.Iterations,
                state.TransientIterations,
                state.VisualizationMode
            });

            return state;
        }

        public void LoadState(FractalSaveStateBase state)
        {
            if (state is not LogisticMapSaveState logistic) return;

            _centerX = logistic.CenterX;
            _centerY = logistic.CenterY;
            _zoom = Math.Max(_nudZoom.Minimum, Math.Min(_nudZoom.Maximum, logistic.Zoom));
            _nudZoom.Value = _zoom;

            _nudR.Value = Math.Max(_nudR.Minimum, Math.Min(_nudR.Maximum, logistic.R));
            _nudX0.Value = Math.Max(_nudX0.Minimum, Math.Min(_nudX0.Maximum, logistic.X0));
            _nudIterations.Value = Math.Max(_nudIterations.Minimum, Math.Min(_nudIterations.Maximum, logistic.Iterations));
            _nudTransient.Value = Math.Max(_nudTransient.Minimum, Math.Min(_nudTransient.Maximum, logistic.TransientIterations));
            _nudBifurcationRMin.Value = Math.Max(_nudBifurcationRMin.Minimum, Math.Min(_nudBifurcationRMin.Maximum, logistic.BifurcationRMin));
            _nudBifurcationRMax.Value = Math.Max(_nudBifurcationRMax.Minimum, Math.Min(_nudBifurcationRMax.Maximum, logistic.BifurcationRMax));
            _nudBifurcationSamples.Value = Math.Max(_nudBifurcationSamples.Minimum, Math.Min(_nudBifurcationSamples.Maximum, logistic.BifurcationSamples));
            _nudBifurcationTransient.Value = Math.Max(_nudBifurcationTransient.Minimum, Math.Min(_nudBifurcationTransient.Maximum, logistic.BifurcationTransient));
            _nudBifurcationPlotted.Value = Math.Max(_nudBifurcationPlotted.Minimum, Math.Min(_nudBifurcationPlotted.Maximum, logistic.BifurcationPlottedPoints));
            _nudCobwebSteps.Value = Math.Max(_nudCobwebSteps.Minimum, Math.Min(_nudCobwebSteps.Maximum, logistic.CobwebSteps));
            SetSelectedMode(ParseMode(logistic.VisualizationMode));

            if (!string.IsNullOrWhiteSpace(logistic.PaletteName))
            {
                _paletteManager.ActivePalette = _paletteManager.Palettes.FirstOrDefault(p => p.Name == logistic.PaletteName) ?? _paletteManager.ActivePalette;
            }

            ScheduleRender();
        }

        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (state is not LogisticMapSaveState logistic)
            {
                return new Bitmap(previewWidth, previewHeight);
            }

            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            LogisticVisualizationMode mode = ParseMode(logistic.VisualizationMode);
            decimal zoom = logistic.Zoom == 0 ? 0.01m : logistic.Zoom;
            LogisticRenderSettings settings = BuildRenderSettingsFromSaveState(logistic, _paletteManager);

            byte[] buffer = RenderCurrentModeBuffer(mode, width, height, logistic.CenterX, logistic.CenterY, zoom, settings, CancellationToken.None);
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        public async Task<byte[]> RenderPreviewAsync(FractalSaveStateBase state, int previewWidth, int previewHeight, CancellationToken cancellationToken, IProgress<int>? progress = null)
        {
            if (state is not LogisticMapSaveState logistic)
            {
                return new byte[Math.Max(1, previewWidth * previewHeight * 4)];
            }

            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            LogisticVisualizationMode mode = ParseMode(logistic.VisualizationMode);
            decimal zoom = logistic.Zoom == 0 ? 0.01m : logistic.Zoom;
            LogisticRenderSettings settings = BuildRenderSettingsFromSaveState(logistic, _paletteManager);

            return await Task.Run(
                () => RenderCurrentModeBuffer(mode, width, height, logistic.CenterX, logistic.CenterY, zoom, settings, cancellationToken, progress),
                cancellationToken);
        }

        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            return await Task.Run(() =>
            {
                using Bitmap preview = RenderPreview(state, totalWidth, totalHeight);
                Rectangle rect = tile.Bounds;
                var tileBytes = new byte[rect.Width * rect.Height * 4];
                BitmapData data = preview.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int rowBytes = rect.Width * 4;
                    for (int y = 0; y < rect.Height; y++)
                    {
                        IntPtr rowPtr = IntPtr.Add(data.Scan0, y * data.Stride);
                        Marshal.Copy(rowPtr, tileBytes, y * rowBytes, rowBytes);
                    }
                }
                finally
                {
                    preview.UnlockBits(data);
                }
                return tileBytes;
            });
        }

        public List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            return SaveFileManager.LoadSaves<LogisticMapSaveState>(FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();
        }

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            SaveFileManager.SaveSaves(FractalTypeIdentifier, saves.Cast<LogisticMapSaveState>().ToList());
        }

        public HighResRenderState GetRenderState()
        {
            return new HighResRenderState
            {
                EngineType = FractalTypeIdentifier,
                FileNameDetails = "logistic_map",
                Iterations = (int)_nudIterations.Value,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                Threshold = 0,
                BaseScale = BaseScale,
                ActivePaletteName = _paletteManager.ActivePalette.Name,
                LyapunovAMin = _nudR.Value,
                LyapunovBMin = _nudX0.Value,
                LyapunovTransientIterations = (int)_nudTransient.Value
            };
        }

        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            _isHighResRendering = true;
            try
            {
                LogisticRenderSettings settings = CaptureUiRenderSettings();
                LogisticVisualizationMode mode = _mode;
                decimal centerX = _centerX;
                decimal centerY = _centerY;
                decimal zoom = _zoom;
                return await Task.Run(() =>
                {
                    int w = Math.Max(1, width);
                    int h = Math.Max(1, height);
                    byte[] buffer = RenderCurrentModeBuffer(
                        mode,
                        w,
                        h,
                        centerX,
                        centerY,
                        zoom,
                        settings,
                        cancellationToken,
                        null,
                        null,
                        drawAxes: false,
                        backgroundColor: _backgroundColor);
                    var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    var rect = new Rectangle(0, 0, w, h);
                    BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
                    Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
                    bmp.UnlockBits(data);
                    progress.Report(new RenderProgress { Percentage = 100, Status = "Готово" });
                    return bmp;
                }, cancellationToken);
            }
            finally
            {
                _isHighResRendering = false;
            }
        }

        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            LogisticRenderSettings settings = CaptureUiRenderSettings();
            byte[] buffer = RenderCurrentModeBuffer(
                _mode,
                previewWidth,
                previewHeight,
                _centerX,
                _centerY,
                _zoom,
                settings,
                CancellationToken.None,
                null,
                null,
                drawAxes: false,
                backgroundColor: _backgroundColor);
            var bmp = new Bitmap(previewWidth, previewHeight, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, previewWidth, previewHeight);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bmp.UnlockBits(data);
            return bmp;
        }
    }
}
