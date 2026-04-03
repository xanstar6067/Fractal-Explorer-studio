using FractalExplorer.Engines;
using FractalExplorer.Resources;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using FractalExplorer.Utilities.Theme;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FractalExplorer.Forms.Fractals
{
    public sealed partial class FractalBuddhabrotForm : Form, ISaveLoadCapableFractal, IFullPreviewRenderCapableFractal
    {
        private const decimal BaseScale = 4.0m;
        private const int ToggleButtonMargin = 12;

        private readonly FractalBuddhabrotEngine _engine = new();
        private readonly PaletteManager _paletteManager = new();
        private CancellationTokenSource? _renderCts;
        private readonly System.Windows.Forms.Timer _renderRestartTimer = new() { Interval = 350 };

        private decimal _centerX = 0;
        private decimal _centerY = 0;
        private bool _controlsPanelVisible = true;
        private bool _isPanning;
        private bool _suppressAutoRender;
        private bool _isQueuingRenderRestart;
        private Point _panStartPoint;

        public FractalBuddhabrotForm()
        {
            InitializeComponent();
            ThemeManager.RegisterForm(this);
            InitializeUiState();
        }

        private void InitializeUiState()
        {
            _modeCombo.SelectedIndex = 0;
            int cores = Environment.ProcessorCount;
            _threadsCombo.Items.Clear();
            _threadsCombo.Items.Add("Auto");
            for (int i = 1; i <= cores; i++)
            {
                _threadsCombo.Items.Add(i);
            }
            _threadsCombo.SelectedItem = "Auto";

            _paletteCombo.Items.Clear();
            foreach (var palette in _paletteManager.Palettes)
            {
                _paletteCombo.Items.Add(palette.Name);
            }

            _paletteCombo.SelectedItem = _paletteManager.ActivePalette?.Name;
            if (_paletteCombo.SelectedIndex < 0 && _paletteCombo.Items.Count > 0)
            {
                _paletteCombo.SelectedIndex = 0;
            }

            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseLeave += Canvas_MouseLeave;
            _canvas.MouseEnter += (_, _) => _canvas.Focus();
            _canvas.SizeChanged += Canvas_SizeChanged;
            _renderRestartTimer.Tick += RenderRestartTimer_Tick;
            AttachAutoRenderControlTriggers();
        }

        private async void FractalBuddhabrotForm_Load(object? sender, EventArgs e)
        {
            UpdateToggleControlsPosition();
            ApplyUiToEngine();
            await RenderAsync();
        }

        private void FractalBuddhabrotForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _renderCts?.Cancel();
            _renderRestartTimer.Stop();
            _renderRestartTimer.Tick -= RenderRestartTimer_Tick;
            _renderRestartTimer.Dispose();
        }

        private async void BtnRender_Click(object? sender, EventArgs e)
        {
            await RenderAsync();
        }

        private void BtnSaveLoad_Click(object? sender, EventArgs e)
        {
            using var dialog = new SaveLoadDialogForm(this);
            dialog.ShowDialog(this);
        }

        private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (_canvas.Width <= 0 || _canvas.Height <= 0)
            {
                return;
            }

            decimal scaleBeforeZoom = BaseScale / Math.Max(0.0000001m, _zoom.Value);
            decimal mouseReal = _centerX + (e.X - _canvas.Width / 2.0m) * scaleBeforeZoom / _canvas.Width;
            decimal mouseImaginary = _centerY - (e.Y - _canvas.Height / 2.0m) * scaleBeforeZoom / _canvas.Height;

            decimal nextZoom = Math.Clamp(_zoom.Value * (e.Delta > 0 ? 1.25m : 0.8m), _zoom.Minimum, _zoom.Maximum);
            if (nextZoom == _zoom.Value)
            {
                return;
            }

            decimal scaleAfterZoom = BaseScale / Math.Max(0.0000001m, nextZoom);
            _centerX = mouseReal - (e.X - _canvas.Width / 2.0m) * scaleAfterZoom / _canvas.Width;
            _centerY = mouseImaginary + (e.Y - _canvas.Height / 2.0m) * scaleAfterZoom / _canvas.Height;

            _suppressAutoRender = true;
            _zoom.Value = nextZoom;
            _suppressAutoRender = false;
            QueueRenderRestart();
        }

        private void Canvas_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _canvas.Width <= 0 || _canvas.Height <= 0)
            {
                return;
            }

            _isPanning = true;
            _panStartPoint = e.Location;
            _canvas.Cursor = Cursors.Hand;
        }

        private void Canvas_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isPanning || _canvas.Width <= 0)
            {
                return;
            }

            decimal unitsPerPixel = BaseScale / Math.Max(0.0000001m, _zoom.Value) / _canvas.Width;
            _centerX -= (e.X - _panStartPoint.X) * unitsPerPixel;
            _centerY += (e.Y - _panStartPoint.Y) * unitsPerPixel;
            _panStartPoint = e.Location;
        }

        private void Canvas_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

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

        private void Canvas_SizeChanged(object? sender, EventArgs e)
        {
            if (_canvas.Width <= 1 || _canvas.Height <= 1)
            {
                return;
            }

            QueueRenderRestart(immediate: true);
        }

        private void AttachAutoRenderControlTriggers()
        {
            _modeCombo.SelectedIndexChanged += (_, _) => QueueRenderRestart();
            _paletteCombo.SelectedIndexChanged += (_, _) => QueueRenderRestart();
            _threadsCombo.SelectedIndexChanged += (_, _) => QueueRenderRestart();
            _iterations.ValueChanged += (_, _) => QueueRenderRestart();
            _samples.ValueChanged += (_, _) => QueueRenderRestart();
            _zoom.ValueChanged += (_, _) =>
            {
                if (_suppressAutoRender)
                {
                    return;
                }

                QueueRenderRestart();
            };
            _sampleMinRe.ValueChanged += (_, _) => QueueRenderRestart();
            _sampleMaxRe.ValueChanged += (_, _) => QueueRenderRestart();
            _sampleMinIm.ValueChanged += (_, _) => QueueRenderRestart();
            _sampleMaxIm.ValueChanged += (_, _) => QueueRenderRestart();
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

        private void ApplyUiToEngine()
        {
            _engine.CenterX = _centerX;
            _engine.CenterY = _centerY;
            _engine.Scale = BaseScale / Math.Max(0.0000001m, _zoom.Value);
            _engine.MaxIterations = (int)_iterations.Value;
            _engine.SampleCount = (int)_samples.Value;
            _engine.ThreadCount = _threadsCombo.SelectedItem?.ToString() == "Auto"
                ? 0
                : Convert.ToInt32(_threadsCombo.SelectedItem);
            _engine.RenderMode = _modeCombo.SelectedIndex == 1 ? BuddhabrotRenderMode.AntiBuddhabrot : BuddhabrotRenderMode.Buddhabrot;

            _engine.SampleMinRe = _sampleMinRe.Value;
            _engine.SampleMaxRe = _sampleMaxRe.Value;
            _engine.SampleMinIm = _sampleMinIm.Value;
            _engine.SampleMaxIm = _sampleMaxIm.Value;

            string paletteName = _paletteCombo.SelectedItem?.ToString() ?? _paletteManager.ActivePalette?.Name ?? "Стандартный серый";
            _paletteManager.ActivePalette = _paletteManager.Palettes.FirstOrDefault(p => p.Name == paletteName) ?? _paletteManager.ActivePalette;
            _engine.DensityPalette = CreateDensityPalette(_paletteManager.ActivePalette);
        }

        private static Func<double, Color> CreateDensityPalette(Palette palette)
        {
            var colors = palette.Colors;
            if (colors == null || colors.Count == 0)
            {
                return t =>
                {
                    int g = (int)(Math.Clamp(t, 0, 1) * 255);
                    return Color.FromArgb(255, g, g, g);
                };
            }

            if (colors.Count == 1)
            {
                Color only = colors[0];
                return _ => only;
            }

            return t =>
            {
                t = Math.Clamp(t, 0, 1);
                double scaled = t * (colors.Count - 1);
                int i0 = (int)Math.Floor(scaled);
                int i1 = Math.Min(i0 + 1, colors.Count - 1);
                double f = scaled - i0;

                Color c0 = colors[i0];
                Color c1 = colors[i1];
                return Color.FromArgb(
                    255,
                    (int)(c0.R + (c1.R - c0.R) * f),
                    (int)(c0.G + (c1.G - c0.G) * f),
                    (int)(c0.B + (c1.B - c0.B) * f));
            };
        }

        private async Task RenderAsync()
        {
            if (_canvas.Width <= 0 || _canvas.Height <= 0) return;

            _renderCts?.Cancel();
            _renderCts = new CancellationTokenSource();
            var token = _renderCts.Token;

            ApplyUiToEngine();

            int width = _canvas.Width;
            int height = _canvas.Height;

            byte[] pixels = new byte[width * height * 4];
            try
            {
                SetRenderingState(isRendering: true);

                await Task.Run(() => _engine.RenderToBuffer(
                    pixels,
                    width,
                    height,
                    width * 4,
                    4,
                    token,
                    p => UpdateRenderProgressSafe(Math.Clamp(p, 0, 100))), token);
                token.ThrowIfCancellationRequested();

                using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }

                _canvas.Image?.Dispose();
                _canvas.Image = (Bitmap)bmp.Clone();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                SetRenderingState(isRendering: false);
            }
        }

        private void UpdateRenderProgressSafe(int value)
        {
            if (IsDisposed) return;

            void SetProgress()
            {
                int clamped = Math.Clamp(value, _renderProgress.Minimum, _renderProgress.Maximum);
                _renderProgress.Value = clamped;
                _progressLabel.Text = $"Обработка: {clamped}%";
            }

            if (InvokeRequired)
            {
                BeginInvoke((Action)SetProgress);
            }
            else
            {
                SetProgress();
            }
        }

        private void SetRenderingState(bool isRendering)
        {
            if (IsDisposed) return;

            void UpdateUi()
            {
                _btnRender.Enabled = !isRendering;
                if (!isRendering && _renderProgress.Value >= _renderProgress.Maximum)
                {
                    _progressLabel.Text = "Обработка: завершено";
                }
                else if (!isRendering)
                {
                    _progressLabel.Text = "Обработка: 0%";
                    _renderProgress.Value = 0;
                }
                else
                {
                    _progressLabel.Text = "Обработка: 0%";
                    _renderProgress.Value = 0;
                }
            }

            if (InvokeRequired)
            {
                BeginInvoke((Action)UpdateUi);
            }
            else
            {
                UpdateUi();
            }
        }

        private void BtnToggleControls_Click(object? sender, EventArgs e)
        {
            _controlsPanelVisible = !_controlsPanelVisible;
            _controlsHost.Visible = _controlsPanelVisible;
            _btnToggleControls.Text = _controlsPanelVisible ? "✕" : "☰";
            UpdateToggleControlsPosition();
        }

        private void CanvasHost_Resize(object? sender, EventArgs e)
        {
            UpdateToggleControlsPosition();
        }

        private void ControlsHost_SizeChanged(object? sender, EventArgs e)
        {
            UpdateToggleControlsPosition();
        }

        private void UpdateToggleControlsPosition()
        {
            int targetX = ToggleButtonMargin;
            if (_controlsPanelVisible)
            {
                targetX = _controlsHost.Right + ToggleButtonMargin;
            }

            int maxX = Math.Max(ToggleButtonMargin, _canvasHost.ClientSize.Width - _btnToggleControls.Width - ToggleButtonMargin);
            _btnToggleControls.Location = new Point(Math.Min(targetX, maxX), ToggleButtonMargin);
            _btnToggleControls.BringToFront();
        }

        public string FractalTypeIdentifier => "Buddhabrot";
        public Type ConcreteSaveStateType => typeof(BuddhabrotSaveState);

        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            ApplyUiToEngine();
            return new BuddhabrotSaveState(FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _engine.CenterX,
                CenterY = _engine.CenterY,
                Zoom = _zoom.Value,
                MaxIterations = _engine.MaxIterations,
                SampleCount = _engine.SampleCount,
                PaletteName = _paletteManager.ActivePalette?.Name ?? string.Empty,
                RenderMode = (int)_engine.RenderMode,
                SampleMinRe = _engine.SampleMinRe,
                SampleMaxRe = _engine.SampleMaxRe,
                SampleMinIm = _engine.SampleMinIm,
                SampleMaxIm = _engine.SampleMaxIm
            };
        }

        public void LoadState(FractalSaveStateBase state)
        {
            if (state is not BuddhabrotSaveState s) return;

            _centerX = s.CenterX;
            _centerY = s.CenterY;
            _zoom.Value = Math.Clamp(s.Zoom, _zoom.Minimum, _zoom.Maximum);
            _iterations.Value = Math.Clamp(s.MaxIterations, _iterations.Minimum, _iterations.Maximum);
            _samples.Value = Math.Clamp(s.SampleCount, _samples.Minimum, _samples.Maximum);
            _modeCombo.SelectedIndex = s.RenderMode == 1 ? 1 : 0;
            _sampleMinRe.Value = Math.Clamp(s.SampleMinRe, _sampleMinRe.Minimum, _sampleMinRe.Maximum);
            _sampleMaxRe.Value = Math.Clamp(s.SampleMaxRe, _sampleMaxRe.Minimum, _sampleMaxRe.Maximum);
            _sampleMinIm.Value = Math.Clamp(s.SampleMinIm, _sampleMinIm.Minimum, _sampleMinIm.Maximum);
            _sampleMaxIm.Value = Math.Clamp(s.SampleMaxIm, _sampleMaxIm.Minimum, _sampleMaxIm.Maximum);
            _paletteCombo.SelectedItem = _paletteManager.Palettes.FirstOrDefault(p => p.Name == s.PaletteName)?.Name ?? _paletteCombo.SelectedItem;

            _ = RenderAsync();
        }

        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (state is not BuddhabrotSaveState s)
                throw new InvalidOperationException("Ожидался BuddhabrotSaveState");

            var engine = BuildEngineFromState(s);
            byte[] pixels = new byte[previewWidth * previewHeight * 4];
            engine.RenderToBuffer(pixels, previewWidth, previewHeight, previewWidth * 4, 4, CancellationToken.None);

            var bmp = new Bitmap(previewWidth, previewHeight, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, previewWidth, previewHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            if (state is not BuddhabrotSaveState s)
                return Array.Empty<byte>();

            int w = Math.Max(1, tile.Bounds.Width);
            int h = Math.Max(1, tile.Bounds.Height);
            byte[] pixels = new byte[w * h * 4];

            await Task.Run(() =>
            {
                var engine = BuildEngineFromState(s);
                int fullWidth = Math.Max(1, totalWidth);
                int fullHeight = Math.Max(1, totalHeight);
                byte[] fullPixels = new byte[fullWidth * fullHeight * 4];
                engine.RenderToBuffer(fullPixels, fullWidth, fullHeight, fullWidth * 4, 4, CancellationToken.None);

                for (int y = 0; y < h; y++)
                {
                    int srcY = tile.Bounds.Y + y;
                    if ((uint)srcY >= (uint)fullHeight)
                    {
                        continue;
                    }

                    int srcX = Math.Max(0, tile.Bounds.X);
                    int copyWidth = Math.Min(w, fullWidth - srcX);
                    if (copyWidth <= 0)
                    {
                        continue;
                    }

                    Buffer.BlockCopy(
                        fullPixels,
                        (srcY * fullWidth + srcX) * 4,
                        pixels,
                        y * w * 4,
                        copyWidth * 4);
                }
            });

            return pixels;
        }

        public async Task<byte[]> RenderPreviewAsync(
            FractalSaveStateBase state,
            int previewWidth,
            int previewHeight,
            CancellationToken cancellationToken,
            IProgress<int>? progress = null)
        {
            if (state is not BuddhabrotSaveState s)
            {
                return Array.Empty<byte>();
            }

            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            byte[] pixels = new byte[width * height * 4];
            var engine = BuildEngineFromState(s);

            await Task.Run(() =>
            {
                engine.RenderToBuffer(
                    pixels,
                    width,
                    height,
                    width * 4,
                    4,
                    cancellationToken,
                    p => progress?.Report(Math.Clamp(p, 0, 100)));
            }, cancellationToken);

            return pixels;
        }

        public List<FractalSaveStateBase> LoadAllSavesForThisType() =>
            SaveFileManager.LoadSaves<BuddhabrotSaveState>(FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves) =>
            SaveFileManager.SaveSaves(FractalTypeIdentifier, saves.Cast<BuddhabrotSaveState>().ToList());

        private FractalBuddhabrotEngine BuildEngineFromState(BuddhabrotSaveState s)
        {
            Palette palette = _paletteManager.Palettes.FirstOrDefault(p => p.Name == s.PaletteName) ?? _paletteManager.ActivePalette;
            return new FractalBuddhabrotEngine
            {
                CenterX = s.CenterX,
                CenterY = s.CenterY,
                Scale = BaseScale / Math.Max(0.0000001m, s.Zoom),
                MaxIterations = s.MaxIterations,
                SampleCount = s.SampleCount,
                ThreadCount = _threadsCombo.SelectedItem?.ToString() == "Auto" ? 0 : Convert.ToInt32(_threadsCombo.SelectedItem),
                RenderMode = s.RenderMode == 1 ? BuddhabrotRenderMode.AntiBuddhabrot : BuddhabrotRenderMode.Buddhabrot,
                SampleMinRe = s.SampleMinRe,
                SampleMaxRe = s.SampleMaxRe,
                SampleMinIm = s.SampleMinIm,
                SampleMaxIm = s.SampleMaxIm,
                DensityPalette = CreateDensityPalette(palette)
            };
        }
    }
}
