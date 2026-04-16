using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using FractalExplorer.Forms.Other;
using FractalExplorer.Resources;
using FractalExplorer.Utilities;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using FractalExplorer.Utilities.UI;

namespace FractalExplorer.Forms.Fractals
{
    public partial class FractalLogisticMapForm : Form, ISaveLoadCapableFractal, IHighResRenderable
    {
        private const decimal BaseScale = 1.0m;
        private const string AutoThreadOptionText = "Авто";

        private readonly PaletteManager _paletteManager = new();
        private ColorConfigurationForm? _colorConfigForm;
        private readonly object _bitmapLock = new();
        private readonly FullscreenToggleController _fullscreenController = new();
        private readonly string _baseTitle;
        private readonly System.Windows.Forms.Timer _wheelDebounceTimer = new();

        private Bitmap? _previewBitmap;
        private CancellationTokenSource? _renderCts;
        private bool _isRendering;
        private bool _renderQueued;
        private bool _isHighResRendering;
        private bool _isPanning;
        private bool _isFormClosing;
        private bool _suppressZoomValueChanged;
        private int _controlsOpenWidth = 231;
        private Point _panStart;

        private decimal _centerX = 0.5m;
        private decimal _centerY = 0.5m;
        private decimal _zoom = 1.0m;
        private decimal _renderCenterX = 0.5m;
        private decimal _renderCenterY = 0.5m;
        private decimal _renderZoom = 1.0m;

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
            _canvas.Paint += Canvas_Paint;
            _canvas.MouseWheel += Canvas_MouseWheel;
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseLeave += Canvas_MouseUp;
            _canvas.MouseEnter += (_, _) => _canvas.Focus();
            _canvas.Resize += (_, _) => ScheduleRender();
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
            ConfigureDecimal(_nudR, 6, 0.0001m, 0m, 4m, 3.8m);
            ConfigureDecimal(_nudX0, 6, 0.0001m, 0m, 1m, 0.2m);
            _nudIterations.Minimum = 32;
            _nudIterations.Maximum = 200000;
            _nudIterations.Value = 2500;

            _nudTransient.Minimum = 0;
            _nudTransient.Maximum = 100000;
            _nudTransient.Value = 500;

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
            _cbThreads.SelectedIndexChanged += (_, _) => ScheduleRender();
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
            e.Graphics.Clear(Color.Black);
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

        private void ScheduleRender()
        {
            if (_isFormClosing || _canvas.Width <= 1 || _canvas.Height <= 1 || IsDisposed) return;

            _renderQueued = true;
            if (_isRendering)
            {
                CancelRender();
                return;
            }

            _ = RenderAsync();
        }

        private async Task RenderAsync()
        {
            if (_isRendering) return;

            _isRendering = true;
            try
            {
                while (_renderQueued && !IsDisposed)
                {
                    _renderQueued = false;
                    if (!_isFormClosing && !IsDisposed && _pbRenderProgress.IsHandleCreated)
                    {
                        _pbRenderProgress.Style = ProgressBarStyle.Blocks;
                        _pbRenderProgress.Minimum = 0;
                        _pbRenderProgress.Maximum = 100;
                        _pbRenderProgress.Value = 0;
                    }

                    using CancellationTokenSource cts = StartNewRender();
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
                        byte[] buffer = await Task.Run(() => RenderOrbitBuffer(width, height, renderCenterX, renderCenterY, renderZoom, cts.Token, progress), cts.Token);

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
                }
            }
            finally
            {
                if (!_isFormClosing && !IsDisposed && _pbRenderProgress.IsHandleCreated)
                {
                    _pbRenderProgress.Value = 0;
                }
                _isRendering = false;
                if (!_isFormClosing && !IsDisposed) Text = _baseTitle;
            }
        }

        private byte[] RenderOrbitBuffer(int width, int height, CancellationToken ct, IProgress<int>? progress = null)
        {
            return RenderOrbitBuffer(width, height, _centerX, _centerY, _zoom, ct, progress);
        }

        private byte[] RenderOrbitBuffer(int width, int height, decimal centerX, decimal centerY, decimal zoom, CancellationToken ct, IProgress<int>? progress = null)
        {
            byte[] buffer = new byte[width * height * 4];
            int iterations = (int)_nudIterations.Value;
            int transient = Math.Min((int)_nudTransient.Value, Math.Max(0, iterations - 1));
            double r = (double)_nudR.Value;
            double x = (double)_nudX0.Value;

            decimal scale = BaseScale / zoom;
            decimal minX = centerX - scale / 2m;
            decimal maxX = centerX + scale / 2m;
            decimal minY = centerY - scale / 2m;
            decimal maxY = centerY + scale / 2m;

            int plotted = 0;
            var paletteColors = _paletteManager.ActivePalette.Colors;
            Color fallback = Color.Lime;

            int progressInterval = Math.Max(1, iterations / 100);

            for (int i = 0; i < iterations; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i % progressInterval == 0 || i == iterations - 1)
                {
                    int percent = iterations > 0 ? (int)((i + 1) * 100L / iterations) : 100;
                    progress?.Report(percent);
                }
                x = r * x * (1.0 - x);
                if (i < transient) continue;

                decimal graphX = iterations > 1 ? (decimal)i / (iterations - 1) : 0m;
                decimal graphY = (decimal)x;
                if (graphX < minX || graphX > maxX || graphY < minY || graphY > maxY) continue;

                int px = (int)Math.Round((graphX - minX) / (maxX - minX) * (width - 1));
                int py = (int)Math.Round((maxY - graphY) / (maxY - minY) * (height - 1));
                if (px < 0 || px >= width || py < 0 || py >= height) continue;

                Color c = paletteColors.Count > 0
                    ? paletteColors[(i - transient) % paletteColors.Count]
                    : fallback;

                int idx = (py * width + px) * 4;
                buffer[idx] = c.B;
                buffer[idx + 1] = c.G;
                buffer[idx + 2] = c.R;
                buffer[idx + 3] = 255;
                plotted++;
            }

            if (plotted == 0)
            {
                DrawFallbackAxes(buffer, width, height, minX, maxX, minY, maxY);
            }

            progress?.Report(100);
            return buffer;
        }

        private static void DrawFallbackAxes(byte[] buffer, int width, int height, decimal minX, decimal maxX, decimal minY, decimal maxY)
        {
            void SetPixel(int px, int py, Color color)
            {
                if (px < 0 || py < 0 || px >= width || py >= height) return;
                int idx = (py * width + px) * 4;
                buffer[idx] = color.B;
                buffer[idx + 1] = color.G;
                buffer[idx + 2] = color.R;
                buffer[idx + 3] = color.A;
            }

            if (minX <= 0m && maxX >= 0m)
            {
                int x0 = (int)Math.Round((-minX) / (maxX - minX) * (width - 1));
                for (int y = 0; y < height; y++) SetPixel(x0, y, Color.FromArgb(128, 80, 80, 80));
            }

            if (minY <= 0m && maxY >= 0m)
            {
                int y0 = (int)Math.Round((maxY - 0m) / (maxY - minY) * (height - 1));
                for (int x = 0; x < width; x++) SetPixel(x, y0, Color.FromArgb(128, 80, 80, 80));
            }
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
                state.TransientIterations
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

            decimal oldCenterX = _centerX;
            decimal oldCenterY = _centerY;
            decimal oldZoom = _zoom;
            decimal oldR = _nudR.Value;
            decimal oldX0 = _nudX0.Value;
            decimal oldIterations = _nudIterations.Value;
            decimal oldTransient = _nudTransient.Value;

            try
            {
                _centerX = logistic.CenterX;
                _centerY = logistic.CenterY;
                _zoom = logistic.Zoom;
                _nudR.Value = Math.Max(_nudR.Minimum, Math.Min(_nudR.Maximum, logistic.R));
                _nudX0.Value = Math.Max(_nudX0.Minimum, Math.Min(_nudX0.Maximum, logistic.X0));
                _nudIterations.Value = Math.Max(_nudIterations.Minimum, Math.Min(_nudIterations.Maximum, logistic.Iterations));
                _nudTransient.Value = Math.Max(_nudTransient.Minimum, Math.Min(_nudTransient.Maximum, logistic.TransientIterations));

                byte[] buffer = RenderOrbitBuffer(previewWidth, previewHeight, CancellationToken.None);
                var bmp = new Bitmap(previewWidth, previewHeight, PixelFormat.Format32bppArgb);
                var rect = new Rectangle(0, 0, previewWidth, previewHeight);
                BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
                bmp.UnlockBits(data);
                return bmp;
            }
            finally
            {
                _centerX = oldCenterX;
                _centerY = oldCenterY;
                _zoom = oldZoom;
                _nudR.Value = oldR;
                _nudX0.Value = oldX0;
                _nudIterations.Value = oldIterations;
                _nudTransient.Value = oldTransient;
            }
        }

        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            return await Task.Run(() =>
            {
                Bitmap preview = RenderPreview(state, totalWidth, totalHeight);
                var rect = tile.Bounds;
                var tileBytes = new byte[rect.Width * rect.Height * 4];
                BitmapData data = preview.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(data.Scan0, tileBytes, 0, tileBytes.Length);
                preview.UnlockBits(data);
                preview.Dispose();
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
                return await Task.Run(() =>
                {
                    int w = Math.Max(1, width);
                    int h = Math.Max(1, height);
                    byte[] buffer = RenderOrbitBuffer(w, h, cancellationToken);
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
            byte[] buffer = RenderOrbitBuffer(previewWidth, previewHeight, CancellationToken.None);
            var bmp = new Bitmap(previewWidth, previewHeight, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, previewWidth, previewHeight);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bmp.UnlockBits(data);
            return bmp;
        }
    }
}
