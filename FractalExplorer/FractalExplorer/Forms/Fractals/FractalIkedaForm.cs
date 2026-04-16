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
    public partial class FractalIkedaForm : Form, ISaveLoadCapableFractal, IFullPreviewRenderCapableFractal, IHighResRenderable
    {
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
        private bool _suppressRangeValueChanged;
        private int _controlsOpenWidth = 231;
        private Point _panStart;

        private decimal _centerX = 0.0m;
        private decimal _centerY = 0.0m;
        private decimal _zoom = 1.0m;
        private decimal _renderCenterX = 0.0m;
        private decimal _renderCenterY = 0.0m;
        private decimal _renderZoom = 1.0m;

        private sealed class IkedaRenderSettings
        {
            public decimal U { get; init; }
            public decimal X0 { get; init; }
            public decimal Y0 { get; init; }
            public int Iterations { get; init; }
            public int DiscardIterations { get; init; }
            public decimal RangeXMin { get; init; }
            public decimal RangeXMax { get; init; }
            public decimal RangeYMin { get; init; }
            public decimal RangeYMax { get; init; }
            public int Threads { get; init; }
        }

        public FractalIkedaForm()
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
            ConfigureDecimal(_nudU, 6, 0.0001m, 0.0m, 1.2m, 0.918m);
            ConfigureDecimal(_nudX0, 6, 0.0001m, -10m, 10m, 0.1m);
            ConfigureDecimal(_nudY0, 6, 0.0001m, -10m, 10m, 0.1m);

            _nudIterations.Minimum = 1000;
            _nudIterations.Maximum = 10_000_000;
            _nudIterations.Value = 1_000_000;

            _nudDiscard.Minimum = 0;
            _nudDiscard.Maximum = 1_000_000;
            _nudDiscard.Value = 500;

            ConfigureDecimal(_nudRangeXMin, 6, 0.0001m, -20m, 20m, -2.0m);
            ConfigureDecimal(_nudRangeXMax, 6, 0.0001m, -20m, 20m, 2.0m);
            ConfigureDecimal(_nudRangeYMin, 6, 0.0001m, -20m, 20m, -2.0m);
            ConfigureDecimal(_nudRangeYMax, 6, 0.0001m, -20m, 20m, 2.0m);

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

            _nudU.ValueChanged += (_, _) => ScheduleRender();
            _nudX0.ValueChanged += (_, _) => ScheduleRender();
            _nudY0.ValueChanged += (_, _) => ScheduleRender();
            _nudIterations.ValueChanged += (_, _) => ScheduleRender();
            _nudDiscard.ValueChanged += (_, _) => ScheduleRender();
            _nudRangeXMin.ValueChanged += (_, _) => OnRangeChanged();
            _nudRangeXMax.ValueChanged += (_, _) => OnRangeChanged();
            _nudRangeYMin.ValueChanged += (_, _) => OnRangeChanged();
            _nudRangeYMax.ValueChanged += (_, _) => OnRangeChanged();
            _cbThreads.SelectedIndexChanged += (_, _) => ScheduleRender();

            ResetViewToCurrentRange();
        }

        private void OnRangeChanged()
        {
            if (_suppressRangeValueChanged) return;
            EnsureRangesValid();
            ResetViewToCurrentRange();
            ScheduleRender();
        }

        private void EnsureRangesValid()
        {
            _suppressRangeValueChanged = true;
            try
            {
                if (_nudRangeXMin.Value >= _nudRangeXMax.Value)
                {
                    _nudRangeXMax.Value = Math.Min(_nudRangeXMax.Maximum, _nudRangeXMin.Value + 0.0001m);
                }

                if (_nudRangeYMin.Value >= _nudRangeYMax.Value)
                {
                    _nudRangeYMax.Value = Math.Min(_nudRangeYMax.Maximum, _nudRangeYMin.Value + 0.0001m);
                }
            }
            finally
            {
                _suppressRangeValueChanged = false;
            }
        }

        private void ResetViewToCurrentRange()
        {
            _centerX = (_nudRangeXMin.Value + _nudRangeXMax.Value) * 0.5m;
            _centerY = (_nudRangeYMin.Value + _nudRangeYMax.Value) * 0.5m;
            _zoom = 1.0m;

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

        private decimal GetBaseSpanX() => Math.Max(0.0001m, _nudRangeXMax.Value - _nudRangeXMin.Value);
        private decimal GetBaseSpanY() => Math.Max(0.0001m, _nudRangeYMax.Value - _nudRangeYMin.Value);

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
            decimal spanBeforeX = GetBaseSpanX() / _zoom;
            decimal spanBeforeY = GetBaseSpanY() / _zoom;

            decimal mouseX = _centerX + (e.X - _canvas.Width / 2.0m) * spanBeforeX / _canvas.Width;
            decimal mouseY = _centerY - (e.Y - _canvas.Height / 2.0m) * spanBeforeY / _canvas.Height;

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

            decimal spanAfterX = GetBaseSpanX() / _zoom;
            decimal spanAfterY = GetBaseSpanY() / _zoom;
            _centerX = mouseX - (e.X - _canvas.Width / 2.0m) * spanAfterX / _canvas.Width;
            _centerY = mouseY + (e.Y - _canvas.Height / 2.0m) * spanAfterY / _canvas.Height;

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
            if (_isHighResRendering || !_isPanning || _canvas.Width <= 0 || _canvas.Height <= 0) return;

            decimal spanX = GetBaseSpanX() / _zoom;
            decimal spanY = GetBaseSpanY() / _zoom;
            decimal unitsPerPixelX = spanX / _canvas.Width;
            decimal unitsPerPixelY = spanY / _canvas.Height;

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

                decimal currentSpanX = GetBaseSpanX() / (_zoom == 0 ? 1m : _zoom);
                decimal currentSpanY = GetBaseSpanY() / (_zoom == 0 ? 1m : _zoom);
                decimal renderedSpanX = GetBaseSpanX() / (_renderZoom == 0 ? 1m : _renderZoom);
                decimal renderedSpanY = GetBaseSpanY() / (_renderZoom == 0 ? 1m : _renderZoom);

                decimal scaleX = renderedSpanX / currentSpanX;
                decimal scaleY = renderedSpanY / currentSpanY;
                decimal dx = (_renderCenterX - _centerX) / currentSpanX * _canvas.Width;
                decimal dy = (_centerY - _renderCenterY) / currentSpanY * _canvas.Height;

                float drawW = (float)(_previewBitmap.Width * scaleX);
                float drawH = (float)(_previewBitmap.Height * scaleY);
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

        private IkedaRenderSettings CaptureUiRenderSettings()
        {
            EnsureRangesValid();
            int threads = _cbThreads.SelectedItem is int value ? value : Environment.ProcessorCount;
            return new IkedaRenderSettings
            {
                U = _nudU.Value,
                X0 = _nudX0.Value,
                Y0 = _nudY0.Value,
                Iterations = Math.Max(1, (int)_nudIterations.Value),
                DiscardIterations = Math.Max(0, (int)_nudDiscard.Value),
                RangeXMin = _nudRangeXMin.Value,
                RangeXMax = _nudRangeXMax.Value,
                RangeYMin = _nudRangeYMin.Value,
                RangeYMax = _nudRangeYMax.Value,
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

            IkedaRenderSettings settings = CaptureUiRenderSettings();
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
                    RenderIkedaBuffer(width, height, centerX, centerY, zoom, settings, token), token);

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
                MessageBox.Show($"Ошибка рендера: {ex.Message}", "Отображение Икэды", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private static byte[] RenderIkedaBuffer(int width, int height, decimal centerX, decimal centerY, decimal zoom, IkedaRenderSettings settings, CancellationToken token, IProgress<int>? progress = null)
        {
            int[] hits = new int[width * height];
            int seeds = Math.Max(1, settings.Threads * 2);
            int pointsPerSeed = Math.Max(1000, settings.Iterations / seeds);
            int discardPerSeed = settings.DiscardIterations;

            double cx = (double)centerX;
            double cy = (double)centerY;
            double spanX = (double)((settings.RangeXMax - settings.RangeXMin) / (zoom == 0 ? 1m : zoom));
            double spanY = (double)((settings.RangeYMax - settings.RangeYMin) / (zoom == 0 ? 1m : zoom));
            spanX = Math.Max(1e-9, spanX);
            spanY = Math.Max(1e-9, spanY);

            double minX = cx - spanX * 0.5;
            double maxX = cx + spanX * 0.5;
            double minY = cy - spanY * 0.5;
            double maxY = cy + spanY * 0.5;

            double u = (double)settings.U;

            Parallel.For(0, seeds, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, settings.Threads),
                CancellationToken = token
            }, seedIndex =>
            {
                double x = (double)settings.X0 + seedIndex * 0.000127;
                double y = (double)settings.Y0 - seedIndex * 0.000093;
                int localSteps = seedIndex == seeds - 1 ? settings.Iterations - pointsPerSeed * (seeds - 1) : pointsPerSeed;
                localSteps = Math.Max(1, localSteps);

                for (int i = 0; i < discardPerSeed; i++)
                {
                    IterateIkeda(ref x, ref y, u);
                }

                for (int i = 0; i < localSteps; i++)
                {
                    token.ThrowIfCancellationRequested();

                    IterateIkeda(ref x, ref y, u);

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
                byte c = (byte)Math.Clamp((int)(Math.Pow(v, 0.62) * 255.0), 0, 255);
                int offset = i * 4;
                buffer[offset] = c;
                buffer[offset + 1] = c;
                buffer[offset + 2] = c;
                buffer[offset + 3] = 255;
            }

            progress?.Report(100);
            return buffer;
        }

        private static void IterateIkeda(ref double x, ref double y, double u)
        {
            double t = 0.4 - 6.0 / (1.0 + x * x + y * y);
            double cosT = Math.Cos(t);
            double sinT = Math.Sin(t);
            double nextX = 1.0 + u * (x * cosT - y * sinT);
            double nextY = u * (x * sinT + y * cosT);
            x = nextX;
            y = nextY;
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
            ResetViewToCurrentRange();
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

        public string FractalTypeIdentifier => "Ikeda";
        public Type ConcreteSaveStateType => typeof(IkedaSaveState);

        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new IkedaSaveState(FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                U = _nudU.Value,
                X0 = _nudX0.Value,
                Y0 = _nudY0.Value,
                Iterations = (int)_nudIterations.Value,
                DiscardIterations = (int)_nudDiscard.Value,
                RangeXMin = _nudRangeXMin.Value,
                RangeXMax = _nudRangeXMax.Value,
                RangeYMin = _nudRangeYMin.Value,
                RangeYMax = _nudRangeYMax.Value
            };

            state.PreviewParametersJson = JsonSerializer.Serialize(new
            {
                state.CenterX,
                state.CenterY,
                state.Zoom,
                state.U,
                state.X0,
                state.Y0,
                state.Iterations,
                state.DiscardIterations,
                state.RangeXMin,
                state.RangeXMax,
                state.RangeYMin,
                state.RangeYMax
            });

            return state;
        }

        public void LoadState(FractalSaveStateBase state)
        {
            if (state is not IkedaSaveState ikeda) return;

            _nudU.Value = Math.Max(_nudU.Minimum, Math.Min(_nudU.Maximum, ikeda.U));
            _nudX0.Value = Math.Max(_nudX0.Minimum, Math.Min(_nudX0.Maximum, ikeda.X0));
            _nudY0.Value = Math.Max(_nudY0.Minimum, Math.Min(_nudY0.Maximum, ikeda.Y0));
            _nudIterations.Value = Math.Max(_nudIterations.Minimum, Math.Min(_nudIterations.Maximum, ikeda.Iterations));
            _nudDiscard.Value = Math.Max(_nudDiscard.Minimum, Math.Min(_nudDiscard.Maximum, ikeda.DiscardIterations));
            _nudRangeXMin.Value = Math.Max(_nudRangeXMin.Minimum, Math.Min(_nudRangeXMin.Maximum, ikeda.RangeXMin));
            _nudRangeXMax.Value = Math.Max(_nudRangeXMax.Minimum, Math.Min(_nudRangeXMax.Maximum, ikeda.RangeXMax));
            _nudRangeYMin.Value = Math.Max(_nudRangeYMin.Minimum, Math.Min(_nudRangeYMin.Maximum, ikeda.RangeYMin));
            _nudRangeYMax.Value = Math.Max(_nudRangeYMax.Minimum, Math.Min(_nudRangeYMax.Maximum, ikeda.RangeYMax));

            EnsureRangesValid();
            _centerX = ikeda.CenterX;
            _centerY = ikeda.CenterY;
            _zoom = Math.Max(_nudZoom.Minimum, Math.Min(_nudZoom.Maximum, ikeda.Zoom));

            _suppressZoomValueChanged = true;
            try
            {
                _nudZoom.Value = _zoom;
            }
            finally
            {
                _suppressZoomValueChanged = false;
            }

            ScheduleRender();
        }

        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (state is not IkedaSaveState ikeda)
            {
                return new Bitmap(previewWidth, previewHeight);
            }

            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            decimal zoom = ikeda.Zoom == 0 ? 0.01m : ikeda.Zoom;
            var settings = new IkedaRenderSettings
            {
                U = ikeda.U,
                X0 = ikeda.X0,
                Y0 = ikeda.Y0,
                Iterations = Math.Max(1, ikeda.Iterations),
                DiscardIterations = Math.Max(0, ikeda.DiscardIterations),
                RangeXMin = ikeda.RangeXMin,
                RangeXMax = ikeda.RangeXMax,
                RangeYMin = ikeda.RangeYMin,
                RangeYMax = ikeda.RangeYMax,
                Threads = Environment.ProcessorCount
            };

            byte[] buffer = RenderIkedaBuffer(width, height, ikeda.CenterX, ikeda.CenterY, zoom, settings, CancellationToken.None);
            return CreateBitmapFromBuffer(width, height, buffer);
        }

        public async Task<byte[]> RenderPreviewAsync(FractalSaveStateBase state, int previewWidth, int previewHeight, CancellationToken cancellationToken, IProgress<int>? progress = null)
        {
            if (state is not IkedaSaveState ikeda)
            {
                return new byte[Math.Max(1, previewWidth * previewHeight * 4)];
            }

            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            decimal zoom = ikeda.Zoom == 0 ? 0.01m : ikeda.Zoom;
            var settings = new IkedaRenderSettings
            {
                U = ikeda.U,
                X0 = ikeda.X0,
                Y0 = ikeda.Y0,
                Iterations = Math.Max(1, ikeda.Iterations),
                DiscardIterations = Math.Max(0, ikeda.DiscardIterations),
                RangeXMin = ikeda.RangeXMin,
                RangeXMax = ikeda.RangeXMax,
                RangeYMin = ikeda.RangeYMin,
                RangeYMax = ikeda.RangeYMax,
                Threads = Environment.ProcessorCount
            };

            return await Task.Run(() =>
                RenderIkedaBuffer(width, height, ikeda.CenterX, ikeda.CenterY, zoom, settings, cancellationToken, progress), cancellationToken);
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
            return SaveFileManager.LoadSaves<IkedaSaveState>(FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();
        }

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            SaveFileManager.SaveSaves(FractalTypeIdentifier, saves.Cast<IkedaSaveState>().ToList());
        }

        public HighResRenderState GetRenderState()
        {
            return new HighResRenderState
            {
                EngineType = FractalTypeIdentifier,
                FileNameDetails = "ikeda_map",
                Iterations = (int)_nudIterations.Value,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                Threshold = 0,
                BaseScale = GetBaseSpanX(),
                ActivePaletteName = string.Empty,
                LyapunovAMin = _nudU.Value,
                LyapunovBMin = _nudX0.Value,
                LyapunovTransientIterations = (int)_nudDiscard.Value
            };
        }

        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            _isHighResRendering = true;
            try
            {
                IkedaRenderSettings settings = CaptureUiRenderSettings();
                decimal centerX = _centerX;
                decimal centerY = _centerY;
                decimal zoom = _zoom;

                return await Task.Run(() =>
                {
                    int w = Math.Max(1, width);
                    int h = Math.Max(1, height);
                    byte[] buffer = RenderIkedaBuffer(w, h, centerX, centerY, zoom, settings, cancellationToken);
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
            IkedaRenderSettings settings = CaptureUiRenderSettings();
            byte[] buffer = RenderIkedaBuffer(previewWidth, previewHeight, _centerX, _centerY, _zoom, settings, CancellationToken.None);
            return CreateBitmapFromBuffer(previewWidth, previewHeight, buffer);
        }
    }
}
