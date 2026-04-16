using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using FractalExplorer.Forms.Other;
using FractalExplorer.Resources;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using FractalExplorer.Utilities.UI;

namespace FractalExplorer.Forms.Fractals
{
    public partial class FractalHenonForm : Form, ISaveLoadCapableFractal, IFullPreviewRenderCapableFractal, IHighResRenderable
    {
        private const decimal BaseScale = 6.0m;
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

        private decimal _centerX = 0.0m;
        private decimal _centerY = 0.0m;
        private decimal _zoom = 1.0m;
        private decimal _renderCenterX = 0.0m;
        private decimal _renderCenterY = 0.0m;
        private decimal _renderZoom = 1.0m;

        private sealed class HenonRenderSettings
        {
            public decimal A { get; init; }
            public decimal B { get; init; }
            public decimal X0 { get; init; }
            public decimal Y0 { get; init; }
            public int Iterations { get; init; }
            public int DiscardIterations { get; init; }
            public int Threads { get; init; }
        }

        public FractalHenonForm()
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
            ConfigureDecimal(_nudA, 6, 0.0001m, -4m, 4m, 1.4m);
            ConfigureDecimal(_nudB, 6, 0.0001m, -2m, 2m, 0.3m);
            ConfigureDecimal(_nudX0, 6, 0.0001m, -4m, 4m, 0.1m);
            ConfigureDecimal(_nudY0, 6, 0.0001m, -4m, 4m, 0.0m);

            _nudIterations.Minimum = 1000;
            _nudIterations.Maximum = 5_000_000;
            _nudIterations.Value = 500_000;

            _nudDiscard.Minimum = 0;
            _nudDiscard.Maximum = 500_000;
            _nudDiscard.Value = 500;

            ConfigureDecimal(_nudZoom, 6, 0.05m, 0.01m, 1_000_000m, 1.0m);
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

            _nudA.ValueChanged += (_, _) => ScheduleRender();
            _nudB.ValueChanged += (_, _) => ScheduleRender();
            _nudX0.ValueChanged += (_, _) => ScheduleRender();
            _nudY0.ValueChanged += (_, _) => ScheduleRender();
            _nudIterations.ValueChanged += (_, _) => ScheduleRender();
            _nudDiscard.ValueChanged += (_, _) => ScheduleRender();
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
            decimal scaleBeforeZoomX = BaseScale / _zoom;
            decimal scaleBeforeZoomY = scaleBeforeZoomX * _canvas.Height / Math.Max(1m, _canvas.Width);

            decimal mouseX = _centerX + (e.X - _canvas.Width / 2.0m) * scaleBeforeZoomX / _canvas.Width;
            decimal mouseY = _centerY - (e.Y - _canvas.Height / 2.0m) * scaleBeforeZoomY / _canvas.Height;

            decimal newZoom;
            if (zoomFactor > 1.0m)
            {
                newZoom = _zoom > _nudZoom.Maximum / zoomFactor ? _nudZoom.Maximum : _zoom * zoomFactor;
            }
            else
            {
                newZoom = _zoom < _nudZoom.Minimum / zoomFactor ? _nudZoom.Minimum : _zoom * zoomFactor;
            }

            _zoom = Math.Max(_nudZoom.Minimum, Math.Min(_nudZoom.Maximum, newZoom));

            decimal scaleAfterZoomX = BaseScale / _zoom;
            decimal scaleAfterZoomY = scaleAfterZoomX * _canvas.Height / Math.Max(1m, _canvas.Width);
            _centerX = mouseX - (e.X - _canvas.Width / 2.0m) * scaleAfterZoomX / _canvas.Width;
            _centerY = mouseY + (e.Y - _canvas.Height / 2.0m) * scaleAfterZoomY / _canvas.Height;

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

            decimal scaleX = BaseScale / _zoom;
            decimal scaleY = scaleX * _canvas.Height / Math.Max(1m, _canvas.Width);
            decimal unitsPerPixelX = scaleX / _canvas.Width;
            decimal unitsPerPixelY = scaleY / Math.Max(1, _canvas.Height);

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
                if (_previewBitmap == null) return;

                e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

                decimal scaleX = (_renderZoom == 0 ? 1m : _renderZoom) / (_zoom == 0 ? 1m : _zoom);
                decimal dx = (_renderCenterX - _centerX) * _zoom / BaseScale * _canvas.Width;
                decimal dy = (_centerY - _renderCenterY) * _zoom / BaseScale * _canvas.Width;

                float drawW = (float)(_previewBitmap.Width * scaleX);
                float drawH = (float)(_previewBitmap.Height * scaleX);
                float drawX = (_canvas.Width - drawW) / 2f + (float)dx;
                float drawY = (_canvas.Height - drawH) / 2f + (float)dy;

                e.Graphics.DrawImage(_previewBitmap, drawX, drawY, drawW, drawH);
            }
        }

        private void QueueRenderAfterWheelInteraction()
        {
            _wheelDebounceTimer.Stop();
            _wheelDebounceTimer.Start();
        }

        private void QueueRenderAfterResizeInteraction()
        {
            _resizeDebounceTimer.Stop();
            _resizeDebounceTimer.Start();
        }

        private void CancelRender()
        {
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _renderCts = null;
        }

        private HenonRenderSettings CaptureUiRenderSettings()
        {
            int threads = _cbThreads.SelectedItem is int value ? value : Environment.ProcessorCount;
            return new HenonRenderSettings
            {
                A = _nudA.Value,
                B = _nudB.Value,
                X0 = _nudX0.Value,
                Y0 = _nudY0.Value,
                Iterations = Math.Max(1, (int)_nudIterations.Value),
                DiscardIterations = Math.Max(0, (int)_nudDiscard.Value),
                Threads = Math.Max(1, threads)
            };
        }

        private async void ScheduleRender()
        {
            if (_isFormClosing || _isHighResRendering) return;
            if (_canvas.Width <= 0 || _canvas.Height <= 0) return;

            CancelRender();
            var cts = new CancellationTokenSource();
            _renderCts = cts;
            CancellationToken token = cts.Token;
            int generation = ++_renderGeneration;

            HenonRenderSettings settings = CaptureUiRenderSettings();
            int width = Math.Max(1, _canvas.Width);
            int height = Math.Max(1, _canvas.Height);
            decimal centerX = _centerX;
            decimal centerY = _centerY;
            decimal zoom = _zoom;

            _pbRenderProgress.Style = ProgressBarStyle.Marquee;
            _lblProgress.Text = "Рендер...";
            Text = $"{_baseTitle} — рендер";

            try
            {
                byte[] buffer = await Task.Run(() =>
                    RenderHenonBuffer(width, height, centerX, centerY, zoom, settings, token), token);

                if (token.IsCancellationRequested || generation != _renderGeneration || IsDisposed) return;

                Bitmap bmp = CreateBitmapFromBuffer(width, height, buffer);
                lock (_bitmapLock)
                {
                    _previewBitmap?.Dispose();
                    _previewBitmap = bmp;
                    _renderCenterX = centerX;
                    _renderCenterY = centerY;
                    _renderZoom = zoom;
                }

                _canvas.Invalidate();
                _pbRenderProgress.Style = ProgressBarStyle.Continuous;
                _pbRenderProgress.Value = 100;
                _lblProgress.Text = "Готово";
                Text = _baseTitle;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _lblProgress.Text = "Ошибка рендера";
                Text = _baseTitle;
                MessageBox.Show($"Ошибка рендера: {ex.Message}", "Карта Хенона", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_renderCts == cts)
                {
                    _renderCts.Dispose();
                    _renderCts = null;
                }
                if (!IsDisposed)
                {
                    _pbRenderProgress.Style = ProgressBarStyle.Continuous;
                }
            }
        }

        private static Bitmap CreateBitmapFromBuffer(int width, int height, byte[] buffer)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            Rectangle rect = new Rectangle(0, 0, width, height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        private static byte[] RenderHenonBuffer(int width, int height, decimal centerX, decimal centerY, decimal zoom, HenonRenderSettings settings, CancellationToken token, IProgress<int>? progress = null)
        {
            int[] hits = new int[width * height];
            int seeds = Math.Max(1, settings.Threads * 2);
            int pointsPerSeed = Math.Max(1000, settings.Iterations / seeds);
            int discardPerSeed = settings.DiscardIterations;

            double cx = (double)centerX;
            double cy = (double)centerY;
            double scaleX = (double)(BaseScale / (zoom == 0 ? 1m : zoom));
            double scaleY = scaleX * Math.Max(1, height) / Math.Max(1, width);
            double minX = cx - scaleX * 0.5;
            double maxX = cx + scaleX * 0.5;
            double minY = cy - scaleY * 0.5;
            double maxY = cy + scaleY * 0.5;

            double a = (double)settings.A;
            double b = (double)settings.B;

            Parallel.For(0, seeds, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, settings.Threads),
                CancellationToken = token
            }, seedIndex =>
            {
                double x = (double)settings.X0 + seedIndex * 0.000137;
                double y = (double)settings.Y0 - seedIndex * 0.000073;
                int localSteps = seedIndex == seeds - 1 ? settings.Iterations - pointsPerSeed * (seeds - 1) : pointsPerSeed;
                localSteps = Math.Max(1, localSteps);

                for (int i = 0; i < discardPerSeed; i++)
                {
                    double xNext = 1.0 - a * x * x + y;
                    double yNext = b * x;
                    x = xNext;
                    y = yNext;
                }

                for (int i = 0; i < localSteps; i++)
                {
                    token.ThrowIfCancellationRequested();

                    double xNext = 1.0 - a * x * x + y;
                    double yNext = b * x;
                    x = xNext;
                    y = yNext;

                    if (x < minX || x > maxX || y < minY || y > maxY) continue;

                    int px = (int)((x - minX) / (maxX - minX) * (width - 1));
                    int py = (int)((maxY - y) / (maxY - minY) * (height - 1));

                    if ((uint)px >= (uint)width || (uint)py >= (uint)height) continue;
                    Interlocked.Increment(ref hits[py * width + px]);
                }

                progress?.Report(Math.Min(99, (int)((seedIndex + 1) * 100.0 / seeds)));
            });

            int maxHit = hits.Max();
            if (maxHit <= 0)
            {
                return new byte[width * height * 4];
            }

            byte[] buffer = new byte[width * height * 4];
            double norm = Math.Log(1.0 + maxHit);
            for (int i = 0; i < hits.Length; i++)
            {
                int hit = hits[i];
                if (hit <= 0) continue;

                double v = Math.Log(1.0 + hit) / norm;
                byte c = (byte)Math.Clamp((int)(Math.Pow(v, 0.65) * 255.0), 0, 255);
                int offset = i * 4;
                buffer[offset] = c;
                buffer[offset + 1] = c;
                buffer[offset + 2] = c;
                buffer[offset + 3] = 255;
            }

            progress?.Report(100);
            return buffer;
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
            _centerX = 0.0m;
            _centerY = 0.0m;
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

        public string FractalTypeIdentifier => "Henon";
        public Type ConcreteSaveStateType => typeof(HenonSaveState);

        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new HenonSaveState(FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                A = _nudA.Value,
                B = _nudB.Value,
                X0 = _nudX0.Value,
                Y0 = _nudY0.Value,
                Iterations = (int)_nudIterations.Value,
                DiscardIterations = (int)_nudDiscard.Value
            };

            state.PreviewParametersJson = JsonSerializer.Serialize(new
            {
                state.CenterX,
                state.CenterY,
                state.Zoom,
                state.A,
                state.B,
                state.X0,
                state.Y0,
                state.Iterations,
                state.DiscardIterations
            });

            return state;
        }

        public void LoadState(FractalSaveStateBase state)
        {
            if (state is not HenonSaveState henon) return;

            _centerX = henon.CenterX;
            _centerY = henon.CenterY;
            _zoom = Math.Max(_nudZoom.Minimum, Math.Min(_nudZoom.Maximum, henon.Zoom));
            _nudZoom.Value = _zoom;

            _nudA.Value = Math.Max(_nudA.Minimum, Math.Min(_nudA.Maximum, henon.A));
            _nudB.Value = Math.Max(_nudB.Minimum, Math.Min(_nudB.Maximum, henon.B));
            _nudX0.Value = Math.Max(_nudX0.Minimum, Math.Min(_nudX0.Maximum, henon.X0));
            _nudY0.Value = Math.Max(_nudY0.Minimum, Math.Min(_nudY0.Maximum, henon.Y0));
            _nudIterations.Value = Math.Max(_nudIterations.Minimum, Math.Min(_nudIterations.Maximum, henon.Iterations));
            _nudDiscard.Value = Math.Max(_nudDiscard.Minimum, Math.Min(_nudDiscard.Maximum, henon.DiscardIterations));

            ScheduleRender();
        }

        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (state is not HenonSaveState henon)
            {
                return new Bitmap(previewWidth, previewHeight);
            }

            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            decimal zoom = henon.Zoom == 0 ? 0.01m : henon.Zoom;
            var settings = new HenonRenderSettings
            {
                A = henon.A,
                B = henon.B,
                X0 = henon.X0,
                Y0 = henon.Y0,
                Iterations = Math.Max(1, henon.Iterations),
                DiscardIterations = Math.Max(0, henon.DiscardIterations),
                Threads = Environment.ProcessorCount
            };

            byte[] buffer = RenderHenonBuffer(width, height, henon.CenterX, henon.CenterY, zoom, settings, CancellationToken.None);
            return CreateBitmapFromBuffer(width, height, buffer);
        }

        public async Task<byte[]> RenderPreviewAsync(FractalSaveStateBase state, int previewWidth, int previewHeight, CancellationToken cancellationToken, IProgress<int>? progress = null)
        {
            if (state is not HenonSaveState henon)
            {
                return new byte[Math.Max(1, previewWidth * previewHeight * 4)];
            }

            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            decimal zoom = henon.Zoom == 0 ? 0.01m : henon.Zoom;
            var settings = new HenonRenderSettings
            {
                A = henon.A,
                B = henon.B,
                X0 = henon.X0,
                Y0 = henon.Y0,
                Iterations = Math.Max(1, henon.Iterations),
                DiscardIterations = Math.Max(0, henon.DiscardIterations),
                Threads = Environment.ProcessorCount
            };

            return await Task.Run(() =>
                RenderHenonBuffer(width, height, henon.CenterX, henon.CenterY, zoom, settings, cancellationToken, progress), cancellationToken);
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
            return SaveFileManager.LoadSaves<HenonSaveState>(FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();
        }

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            SaveFileManager.SaveSaves(FractalTypeIdentifier, saves.Cast<HenonSaveState>().ToList());
        }

        public HighResRenderState GetRenderState()
        {
            return new HighResRenderState
            {
                EngineType = FractalTypeIdentifier,
                FileNameDetails = "henon_map",
                Iterations = (int)_nudIterations.Value,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                Threshold = 0,
                BaseScale = BaseScale,
                ActivePaletteName = string.Empty,
                LyapunovAMin = _nudA.Value,
                LyapunovBMin = _nudB.Value,
                LyapunovTransientIterations = (int)_nudDiscard.Value
            };
        }

        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            _isHighResRendering = true;
            try
            {
                HenonRenderSettings settings = CaptureUiRenderSettings();
                decimal centerX = _centerX;
                decimal centerY = _centerY;
                decimal zoom = _zoom;

                return await Task.Run(() =>
                {
                    int w = Math.Max(1, width);
                    int h = Math.Max(1, height);
                    byte[] buffer = RenderHenonBuffer(w, h, centerX, centerY, zoom, settings, cancellationToken);
                    progress.Report(new RenderProgress { Percentage = 100, Status = "Готово" });
                    return CreateBitmapFromBuffer(w, h, buffer);
                }, cancellationToken);
            }
            finally
            {
                _isHighResRendering = false;
            }
        }

        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            HenonRenderSettings settings = CaptureUiRenderSettings();
            byte[] buffer = RenderHenonBuffer(previewWidth, previewHeight, _centerX, _centerY, _zoom, settings, CancellationToken.None);
            return CreateBitmapFromBuffer(previewWidth, previewHeight, buffer);
        }
    }
}
