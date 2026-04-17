using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using FractalExplorer.Engines;
using FractalExplorer.Forms.Other;
using FractalExplorer.Resources;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using FractalExplorer.Utilities.UI;

namespace FractalExplorer.Forms.Fractals
{
    public partial class FractalRosslerForm : Form, ISaveLoadCapableFractal, IFullPreviewRenderCapableFractal, IHighResRenderable
    {
        private enum RosslerProjection
        {
            XY,
            XZ,
            YZ
        }

        private sealed class RosslerRenderSettings
        {
            public decimal A { get; init; }
            public decimal B { get; init; }
            public decimal C { get; init; }
            public decimal Dt { get; init; }
            public int Steps { get; init; }
            public decimal StartX { get; init; }
            public decimal StartY { get; init; }
            public decimal StartZ { get; init; }
            public RosslerProjection Projection { get; init; }
        }

        private const decimal BaseScale = 30m;
        private const string AutoThreadOptionText = "Авто";
        private const decimal MinRosslerDt = 0.000001m;
        private const decimal MaxRosslerDt = 0.2m;

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

        private decimal _centerX = 0m;
        private decimal _centerY = 0m;
        private decimal _zoom = 1.0m;
        private decimal _renderCenterX = 0m;
        private decimal _renderCenterY = 0m;
        private decimal _renderZoom = 1.0m;
        private RosslerProjection _projection = RosslerProjection.XY;

        public FractalRosslerForm()
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
            ConfigureDecimal(_nudA, 6, 0.001m, -5m, 5m, 0.2m);
            ConfigureDecimal(_nudB, 6, 0.001m, -5m, 5m, 0.2m);
            ConfigureDecimal(_nudC, 6, 0.001m, 0m, 20m, 5.7m);
            ConfigureDecimal(_nudDt, 6, 0.0001m, MinRosslerDt, MaxRosslerDt, 0.01m);

            _nudSteps.Minimum = 1000;
            _nudSteps.Maximum = 2000000;
            _nudSteps.Value = 150000;

            ConfigureDecimal(_nudStartX, 6, 0.001m, -100m, 100m, 0.1m);
            ConfigureDecimal(_nudStartY, 6, 0.001m, -100m, 100m, 0m);
            ConfigureDecimal(_nudStartZ, 6, 0.001m, -100m, 100m, 0m);

            _cbProjection.Items.Clear();
            _cbProjection.Items.AddRange(new object[] { "XY", "XZ", "YZ" });
            _cbProjection.SelectedIndex = 0;

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

            _nudA.ValueChanged += (_, _) => ScheduleRender();
            _nudB.ValueChanged += (_, _) => ScheduleRender();
            _nudC.ValueChanged += (_, _) => ScheduleRender();
            _nudDt.ValueChanged += (_, _) => ScheduleRender();
            _nudSteps.ValueChanged += (_, _) => ScheduleRender();
            _nudStartX.ValueChanged += (_, _) => ScheduleRender();
            _nudStartY.ValueChanged += (_, _) => ScheduleRender();
            _nudStartZ.ValueChanged += (_, _) => ScheduleRender();
            _cbProjection.SelectedIndexChanged += (_, _) =>
            {
                _projection = GetSelectedProjection();
                ScheduleRender();
            };
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

        private RosslerProjection GetSelectedProjection()
        {
            return _cbProjection.SelectedIndex switch
            {
                1 => RosslerProjection.XZ,
                2 => RosslerProjection.YZ,
                _ => RosslerProjection.XY
            };
        }

        private static RosslerProjection ParseProjection(string? value)
        {
            return Enum.TryParse(value, true, out RosslerProjection parsed) ? parsed : RosslerProjection.XY;
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
            decimal mouseU = _centerX + (e.X - _canvas.Width / 2.0m) * scaleBeforeZoom / _canvas.Width;
            decimal mouseV = _centerY - (e.Y - _canvas.Height / 2.0m) * scaleBeforeZoom / _canvas.Height;

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
            decimal scaleAfterZoom = BaseScale / _zoom;
            _centerX = mouseU - (e.X - _canvas.Width / 2.0m) * scaleAfterZoom / _canvas.Width;
            _centerY = mouseV + (e.Y - _canvas.Height / 2.0m) * scaleAfterZoom / _canvas.Height;

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
                if (_previewBitmap == null)
                {
                    return;
                }

                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                RectangleF destination = CalculateDestinationRectangle(_canvas.ClientSize, _previewBitmap.Size);
                e.Graphics.DrawImage(_previewBitmap, destination);
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
                RosslerRenderSettings settings = CaptureUiRenderSettings();

                byte[] buffer = await Task.Run(
                    () => RenderRosslerBuffer(width, height, renderCenterX, renderCenterY, renderZoom, settings, cts.Token, progress, GetThreadCount()),
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
            catch (Exception ex)
            {
                if (!_isFormClosing && !IsDisposed)
                {
                    _lblProgress.Text = "Ошибка рендера";
                    MessageBox.Show(
                        $"Не удалось отрисовать аттрактор Рёсслера.\n{ex.Message}",
                        "Ошибка рендера",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
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

        private RosslerRenderSettings CaptureUiRenderSettings()
        {
            return new RosslerRenderSettings
            {
                A = _nudA.Value,
                B = _nudB.Value,
                C = _nudC.Value,
                Dt = NormalizeDt(_nudDt.Value),
                Steps = (int)_nudSteps.Value,
                StartX = _nudStartX.Value,
                StartY = _nudStartY.Value,
                StartZ = _nudStartZ.Value,
                Projection = GetSelectedProjection()
            };
        }

        private static RosslerRenderSettings BuildRenderSettingsFromState(RosslerSaveState state)
        {
            return new RosslerRenderSettings
            {
                A = state.A,
                B = state.B,
                C = state.C,
                Dt = NormalizeDt(state.Dt),
                Steps = Math.Max(100, state.Steps),
                StartX = state.StartX,
                StartY = state.StartY,
                StartZ = state.StartZ,
                Projection = ParseProjection(state.ProjectionMode)
            };
        }

        private static byte[] RenderRosslerBuffer(
            int width,
            int height,
            decimal centerX,
            decimal centerY,
            decimal zoom,
            RosslerRenderSettings settings,
            CancellationToken ct,
            IProgress<int>? progress = null,
            int? _ = null,
            bool drawAxes = true)
        {
            return FractalRosslerEngine.RenderBuffer(
                width,
                height,
                centerX,
                centerY,
                zoom,
                new FractalRosslerEngine.RenderSettings
                {
                    A = settings.A,
                    B = settings.B,
                    C = settings.C,
                    Dt = settings.Dt,
                    Steps = settings.Steps,
                    StartX = settings.StartX,
                    StartY = settings.StartY,
                    StartZ = settings.StartZ,
                    Projection = settings.Projection switch
                    {
                        RosslerProjection.XZ => FractalRosslerEngine.ProjectionMode.XZ,
                        RosslerProjection.YZ => FractalRosslerEngine.ProjectionMode.YZ,
                        _ => FractalRosslerEngine.ProjectionMode.XY
                    }
                },
                ct,
                progress,
                drawAxes);
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
            _centerX = 0m;
            _centerY = 0m;
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

        public string FractalTypeIdentifier => "Rossler";
        public Type ConcreteSaveStateType => typeof(RosslerSaveState);

        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new RosslerSaveState(FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                A = _nudA.Value,
                B = _nudB.Value,
                C = _nudC.Value,
                Dt = NormalizeDt(_nudDt.Value),
                Steps = (int)_nudSteps.Value,
                StartX = _nudStartX.Value,
                StartY = _nudStartY.Value,
                StartZ = _nudStartZ.Value,
                ProjectionMode = GetSelectedProjection().ToString()
            };

            state.PreviewParametersJson = JsonSerializer.Serialize(new
            {
                state.CenterX,
                state.CenterY,
                state.Zoom,
                state.A,
                state.B,
                state.C,
                state.Dt,
                state.Steps,
                state.StartX,
                state.StartY,
                state.StartZ,
                state.ProjectionMode
            });

            return state;
        }

        public void LoadState(FractalSaveStateBase state)
        {
            if (state is not RosslerSaveState rossler) return;

            _centerX = rossler.CenterX;
            _centerY = rossler.CenterY;
            _zoom = Math.Max(_nudZoom.Minimum, Math.Min(_nudZoom.Maximum, rossler.Zoom));
            _nudZoom.Value = _zoom;

            _nudA.Value = Math.Max(_nudA.Minimum, Math.Min(_nudA.Maximum, rossler.A));
            _nudB.Value = Math.Max(_nudB.Minimum, Math.Min(_nudB.Maximum, rossler.B));
            _nudC.Value = Math.Max(_nudC.Minimum, Math.Min(_nudC.Maximum, rossler.C));
            _nudDt.Value = Math.Max(_nudDt.Minimum, Math.Min(_nudDt.Maximum, NormalizeDt(rossler.Dt)));
            _nudSteps.Value = Math.Max(_nudSteps.Minimum, Math.Min(_nudSteps.Maximum, rossler.Steps));
            _nudStartX.Value = Math.Max(_nudStartX.Minimum, Math.Min(_nudStartX.Maximum, rossler.StartX));
            _nudStartY.Value = Math.Max(_nudStartY.Minimum, Math.Min(_nudStartY.Maximum, rossler.StartY));
            _nudStartZ.Value = Math.Max(_nudStartZ.Minimum, Math.Min(_nudStartZ.Maximum, rossler.StartZ));

            RosslerProjection projection = ParseProjection(rossler.ProjectionMode);
            _cbProjection.SelectedIndex = projection switch
            {
                RosslerProjection.XZ => 1,
                RosslerProjection.YZ => 2,
                _ => 0
            };
            _projection = projection;

            ScheduleRender();
        }

        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (state is not RosslerSaveState rossler)
            {
                return new Bitmap(previewWidth, previewHeight);
            }

            RosslerRenderSettings settings = BuildRenderSettingsFromState(rossler);
            byte[] buffer = RenderRosslerBuffer(previewWidth, previewHeight, rossler.CenterX, rossler.CenterY, rossler.Zoom, settings, CancellationToken.None);
            return BufferToBitmap(buffer, previewWidth, previewHeight);
        }

        public async Task<byte[]> RenderPreviewAsync(FractalSaveStateBase state, int previewWidth, int previewHeight, CancellationToken cancellationToken, IProgress<int>? progress = null)
        {
            if (state is not RosslerSaveState rossler)
            {
                return new byte[previewWidth * previewHeight * 4];
            }

            RosslerRenderSettings settings = BuildRenderSettingsFromState(rossler);
            return await Task.Run(() => RenderRosslerBuffer(previewWidth, previewHeight, rossler.CenterX, rossler.CenterY, rossler.Zoom, settings, cancellationToken, progress), cancellationToken);
        }

        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            return await Task.Run(() =>
            {
                using Bitmap preview = RenderPreview(state, totalWidth, totalHeight);
                Rectangle sourceRect = tile.Bounds;
                using Bitmap tileBitmap = preview.Clone(sourceRect, PixelFormat.Format32bppArgb);
                BitmapData data = tileBitmap.LockBits(new Rectangle(0, 0, sourceRect.Width, sourceRect.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] result = new byte[sourceRect.Width * sourceRect.Height * 4];
                Marshal.Copy(data.Scan0, result, 0, result.Length);
                tileBitmap.UnlockBits(data);
                return result;
            });
        }

        public List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            return SaveFileManager.LoadSaves<RosslerSaveState>(FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();
        }

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            SaveFileManager.SaveSaves(FractalTypeIdentifier, saves.Cast<RosslerSaveState>().ToList());
        }

        public HighResRenderState GetRenderState()
        {
            return new HighResRenderState
            {
                EngineType = FractalTypeIdentifier,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                BaseScale = BaseScale,
                Iterations = (int)_nudSteps.Value,
                Threshold = NormalizeDt(_nudDt.Value),
                FileNameDetails =
                    $"a{_nudA.Value:F3}_b{_nudB.Value:F3}_c{_nudC.Value:F3}_dt{NormalizeDt(_nudDt.Value):F4}_{GetSelectedProjection()}"
            };
        }

        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            _isHighResRendering = true;
            try
            {
                int renderWidth = Math.Max(1, width * Math.Max(1, ssaaFactor));
                int renderHeight = Math.Max(1, height * Math.Max(1, ssaaFactor));
                RosslerRenderSettings settings = CaptureUiRenderSettings();

                var innerProgress = new Progress<int>(p =>
                {
                    progress.Report(new RenderProgress { Percentage = p, Status = $"Рендер Рёсслера: {p}%" });
                });

                byte[] buffer = await Task.Run(() => RenderRosslerBuffer(renderWidth, renderHeight, state.CenterX, state.CenterY, state.Zoom, settings, cancellationToken, innerProgress, drawAxes: false), cancellationToken);

                using Bitmap full = BufferToBitmap(buffer, renderWidth, renderHeight);
                if (ssaaFactor <= 1)
                {
                    return (Bitmap)full.Clone();
                }

                var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using Graphics g = Graphics.FromImage(result);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(full, new Rectangle(0, 0, width, height));
                return result;
            }
            finally
            {
                _isHighResRendering = false;
            }
        }

        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            RosslerRenderSettings settings = CaptureUiRenderSettings();
            byte[] buffer = RenderRosslerBuffer(previewWidth, previewHeight, state.CenterX, state.CenterY, state.Zoom, settings, CancellationToken.None);
            return BufferToBitmap(buffer, previewWidth, previewHeight);
        }

        private static Bitmap BufferToBitmap(byte[] buffer, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bitmap.UnlockBits(data);
            return bitmap;
        }

        private static decimal NormalizeDt(decimal dt)
        {
            if (dt <= 0m) return 0.01m;
            return Math.Clamp(dt, MinRosslerDt, MaxRosslerDt);
        }
    }
}
