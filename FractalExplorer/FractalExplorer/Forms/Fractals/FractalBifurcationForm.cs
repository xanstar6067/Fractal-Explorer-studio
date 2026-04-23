using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using FractalExplorer.Engines;
using FractalExplorer.Forms.Other;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using FractalExplorer.Utilities.UI;

namespace FractalExplorer.Forms.Fractals
{
    public partial class FractalBifurcationForm : Form, ISaveLoadCapableFractal, IFullPreviewRenderCapableFractal, IHighResRenderable
    {
        private const decimal BaseScale = 1.0m;
        private const string AutoThreadOptionText = "Авто";

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

        private decimal _centerX = 3.4m;
        private decimal _centerY = 0.5m;
        private decimal _zoom = 1.0m;
        private decimal _renderCenterX = 3.4m;
        private decimal _renderCenterY = 0.5m;
        private decimal _renderZoom = 1.0m;

        private sealed class BifurcationRenderSettings
        {
            public decimal RMin { get; init; }
            public decimal RMax { get; init; }
            public decimal XMin { get; init; }
            public decimal XMax { get; init; }
            public int TransientIterations { get; init; }
            public int SamplesPerR { get; init; }
            public int Iterations { get; init; }
        }

        public FractalBifurcationForm()
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
            };

            Shown += (_, _) =>
            {
                _canvas.Focus();
                ScheduleRender();
            };
        }

        private void ApplyDefaults()
        {
            ConfigureDecimal(_nudRMin, 6, 0.0001m, 0m, 4m, 2.8m);
            ConfigureDecimal(_nudRMax, 6, 0.0001m, 0m, 4m, 4.0m);
            ConfigureDecimal(_nudXMin, 6, 0.0001m, 0m, 1m, 0.0m);
            ConfigureDecimal(_nudXMax, 6, 0.0001m, 0m, 1m, 1.0m);

            _nudTransient.Minimum = 0;
            _nudTransient.Maximum = 200000;
            _nudTransient.Value = 500;

            _nudSamplesPerR.Minimum = 16;
            _nudSamplesPerR.Maximum = 5000;
            _nudSamplesPerR.Value = 240;

            _nudIterations.Minimum = 32;
            _nudIterations.Maximum = 100000;
            _nudIterations.Value = 1200;

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

            _nudRMin.ValueChanged += (_, _) => ScheduleRender();
            _nudRMax.ValueChanged += (_, _) => ScheduleRender();
            _nudXMin.ValueChanged += (_, _) => ScheduleRender();
            _nudXMax.ValueChanged += (_, _) => ScheduleRender();
            _nudTransient.ValueChanged += (_, _) => ScheduleRender();
            _nudSamplesPerR.ValueChanged += (_, _) => ScheduleRender();
            _nudIterations.ValueChanged += (_, _) => ScheduleRender();
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
            decimal mouseR = _centerX + (e.X - _canvas.Width / 2.0m) * scaleBeforeZoom / _canvas.Width;
            decimal mouseX = _centerY - (e.Y - _canvas.Height / 2.0m) * scaleBeforeZoom / _canvas.Height;

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
            _centerX = mouseR - (e.X - _canvas.Width / 2.0m) * scaleAfterZoom / _canvas.Width;
            _centerY = mouseX + (e.Y - _canvas.Height / 2.0m) * scaleAfterZoom / _canvas.Height;

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
            float scaledWidth = imageWidth * (float)zoomRatio;
            float scaledHeight = imageHeight * (float)zoomRatio;

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
                _lblProgress.Text = "Рендер...";
            }

            var progress = new Progress<int>(value =>
            {
                if (_isFormClosing || IsDisposed || !_pbRenderProgress.IsHandleCreated) return;
                int clamped = Math.Clamp(value, _pbRenderProgress.Minimum, _pbRenderProgress.Maximum);
                _pbRenderProgress.Value = clamped;
                _lblProgress.Text = $"Рендер: {clamped}%";
            });

            try
            {
                int width = Math.Max(1, _canvas.Width);
                int height = Math.Max(1, _canvas.Height);
                decimal renderCenterX = _centerX;
                decimal renderCenterY = _centerY;
                decimal renderZoom = _zoom;
                BifurcationRenderSettings settings = CaptureUiRenderSettings();

                byte[] buffer = await Task.Run(
                    () => RenderBifurcationBuffer(width, height, renderCenterX, renderCenterY, renderZoom, settings, cts.Token, progress, GetThreadCount()),
                    cts.Token);

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
                    _lblProgress.Text = "Рендер завершен";
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

        private BifurcationRenderSettings CaptureUiRenderSettings()
        {
            return new BifurcationRenderSettings
            {
                RMin = _nudRMin.Value,
                RMax = _nudRMax.Value,
                XMin = _nudXMin.Value,
                XMax = _nudXMax.Value,
                TransientIterations = (int)_nudTransient.Value,
                SamplesPerR = (int)_nudSamplesPerR.Value,
                Iterations = (int)_nudIterations.Value
            };
        }

        private static BifurcationRenderSettings BuildRenderSettingsFromSaveState(BifurcationSaveState save)
        {
            return new BifurcationRenderSettings
            {
                RMin = save.RMin,
                RMax = save.RMax,
                XMin = save.XMin,
                XMax = save.XMax,
                TransientIterations = Math.Max(0, save.TransientIterations),
                SamplesPerR = Math.Max(1, save.SamplesPerR),
                Iterations = Math.Max(1, save.Iterations)
            };
        }

        private byte[] RenderBifurcationBuffer(
            int width,
            int height,
            decimal centerX,
            decimal centerY,
            decimal zoom,
            BifurcationRenderSettings settings,
            CancellationToken ct,
            IProgress<int>? progress = null,
            int? maxDegreeOfParallelism = null)
        {
            return FractalBifurcationEngine.RenderBuffer(
                width,
                height,
                centerX,
                centerY,
                zoom,
                new FractalBifurcationEngine.RenderSettings
                {
                    RMin = settings.RMin,
                    RMax = settings.RMax,
                    XMin = settings.XMin,
                    XMax = settings.XMax,
                    TransientIterations = settings.TransientIterations,
                    SamplesPerR = settings.SamplesPerR,
                    Iterations = settings.Iterations
                },
                ct,
                progress,
                maxDegreeOfParallelism);
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

        private void btnRender_Click(object sender, EventArgs e) => ScheduleRender();

        private void btnReset_Click(object sender, EventArgs e)
        {
            _centerX = (_nudRMin.Value + _nudRMax.Value) / 2m;
            _centerY = (_nudXMin.Value + _nudXMax.Value) / 2m;
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

        public string FractalTypeIdentifier => "Bifurcation";
        public Type ConcreteSaveStateType => typeof(BifurcationSaveState);

        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new BifurcationSaveState(FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                RMin = _nudRMin.Value,
                RMax = _nudRMax.Value,
                XMin = _nudXMin.Value,
                XMax = _nudXMax.Value,
                TransientIterations = (int)_nudTransient.Value,
                SamplesPerR = (int)_nudSamplesPerR.Value,
                Iterations = (int)_nudIterations.Value
            };

            state.PreviewParametersJson = JsonSerializer.Serialize(new
            {
                state.CenterX,
                state.CenterY,
                state.Zoom,
                state.RMin,
                state.RMax,
                state.XMin,
                state.XMax,
                state.TransientIterations,
                state.SamplesPerR,
                state.Iterations
            });

            return state;
        }

        public void LoadState(FractalSaveStateBase state)
        {
            if (state is not BifurcationSaveState bifurcation) return;

            _centerX = bifurcation.CenterX;
            _centerY = bifurcation.CenterY;
            _zoom = Math.Max(_nudZoom.Minimum, Math.Min(_nudZoom.Maximum, bifurcation.Zoom));
            _nudZoom.Value = _zoom;

            _nudRMin.Value = Math.Max(_nudRMin.Minimum, Math.Min(_nudRMin.Maximum, bifurcation.RMin));
            _nudRMax.Value = Math.Max(_nudRMax.Minimum, Math.Min(_nudRMax.Maximum, bifurcation.RMax));
            _nudXMin.Value = Math.Max(_nudXMin.Minimum, Math.Min(_nudXMin.Maximum, bifurcation.XMin));
            _nudXMax.Value = Math.Max(_nudXMax.Minimum, Math.Min(_nudXMax.Maximum, bifurcation.XMax));
            _nudTransient.Value = Math.Max(_nudTransient.Minimum, Math.Min(_nudTransient.Maximum, bifurcation.TransientIterations));
            _nudSamplesPerR.Value = Math.Max(_nudSamplesPerR.Minimum, Math.Min(_nudSamplesPerR.Maximum, bifurcation.SamplesPerR));
            _nudIterations.Value = Math.Max(_nudIterations.Minimum, Math.Min(_nudIterations.Maximum, bifurcation.Iterations));

            ScheduleRender();
        }

        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (state is not BifurcationSaveState bifurcation)
            {
                return new Bitmap(previewWidth, previewHeight);
            }

            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            decimal zoom = bifurcation.Zoom == 0 ? 0.01m : bifurcation.Zoom;
            BifurcationRenderSettings settings = BuildRenderSettingsFromSaveState(bifurcation);

            byte[] buffer = RenderBifurcationBuffer(width, height, bifurcation.CenterX, bifurcation.CenterY, zoom, settings, CancellationToken.None);
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        public async Task<byte[]> RenderPreviewAsync(FractalSaveStateBase state, int previewWidth, int previewHeight, CancellationToken cancellationToken, IProgress<int>? progress = null)
        {
            if (state is not BifurcationSaveState bifurcation)
            {
                return new byte[Math.Max(1, previewWidth * previewHeight * 4)];
            }

            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            decimal zoom = bifurcation.Zoom == 0 ? 0.01m : bifurcation.Zoom;
            BifurcationRenderSettings settings = BuildRenderSettingsFromSaveState(bifurcation);

            return await Task.Run(
                () => RenderBifurcationBuffer(width, height, bifurcation.CenterX, bifurcation.CenterY, zoom, settings, cancellationToken, progress),
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
            return SaveFileManager.LoadSaves<BifurcationSaveState>(FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();
        }

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            SaveFileManager.SaveSaves(FractalTypeIdentifier, saves.Cast<BifurcationSaveState>().ToList());
        }

        public HighResRenderState GetRenderState()
        {
            return new HighResRenderState
            {
                EngineType = FractalTypeIdentifier,
                FileNameDetails = "bifurcation",
                Iterations = (int)_nudIterations.Value,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                Threshold = 0,
                BaseScale = BaseScale,
                ActivePaletteName = "Monochrome",
                LyapunovAMin = _nudRMin.Value,
                LyapunovAMax = _nudRMax.Value,
                LyapunovBMin = _nudXMin.Value,
                LyapunovBMax = _nudXMax.Value,
                LyapunovTransientIterations = (int)_nudTransient.Value
            };
        }

        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            _isHighResRendering = true;
            try
            {
                BifurcationRenderSettings settings = CaptureUiRenderSettings();
                decimal centerX = _centerX;
                decimal centerY = _centerY;
                decimal zoom = _zoom;
                int threads = GetThreadCount();

                return await Task.Run(() =>
                {
                    int w = Math.Max(1, width);
                    int h = Math.Max(1, height);
                    byte[] buffer = RenderBifurcationBuffer(w, h, centerX, centerY, zoom, settings, cancellationToken, null, threads);
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
            BifurcationRenderSettings settings = CaptureUiRenderSettings();
            byte[] buffer = RenderBifurcationBuffer(previewWidth, previewHeight, _centerX, _centerY, _zoom, settings, CancellationToken.None);
            var bmp = new Bitmap(previewWidth, previewHeight, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, previewWidth, previewHeight);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bmp.UnlockBits(data);
            return bmp;
        }
    }
}
