using FractalExplorer.Engines;
using FractalExplorer.Forms.Common;
using FractalExplorer.Forms.Other;
using FractalExplorer.Utilities;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.UI;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FractalExplorer.Forms.Fractals
{
    public partial class FractalIFSForm : Form, ISaveLoadCapableFractal, IFullPreviewRenderCapableFractal, IHighResRenderable
    {
        private readonly FractalIFSGeometryEngine _engine = new();
        private readonly List<IfsPointOfInterest> _pointsOfInterest = PresetManager.GetIfsPointsOfInterest();
        private CancellationTokenSource? _renderCts;
        private readonly System.Windows.Forms.Timer _rerenderTimer = new() { Interval = 260 };
        private readonly System.Windows.Forms.Timer _wheelDebounceTimer = new() { Interval = 360 };
        private bool _isPanning;
        private Point _panStartPoint;
        private double _panStartCenterX;
        private double _panStartCenterY;
        private bool _suppressEvents;
        private readonly FullscreenToggleController _fullscreenController = new();
        private const int ToggleButtonMargin = 12;
        private bool _controlsPanelVisible = true;
        private Bitmap? _stableFrameBitmap;
        private double _renderedCenterX;
        private double _renderedCenterY;
        private double _renderedScale = 2.4;

        public FractalIFSForm()
        {
            InitializeComponent();
            canvas.Paint += Canvas_Paint;
            canvas.MouseWheel += Canvas_MouseWheel;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.MouseLeave += Canvas_MouseLeave;
            canvas.Resize += (_, _) => QueueRenderRestart(immediate: true);
            canvasHost.Resize += (_, _) => UpdateToggleControlsPosition();
            controlsHost.SizeChanged += (_, _) => UpdateToggleControlsPosition();
            btnToggleControls.Click += (_, _) => ToggleControlsPanel();
            KeyDown += FractalIFSForm_KeyDown;

            _rerenderTimer.Tick += (_, _) =>
            {
                _rerenderTimer.Stop();
                ScheduleRender();
            };

            _wheelDebounceTimer.Tick += (_, _) =>
            {
                _wheelDebounceTimer.Stop();
                ScheduleRender();
            };

            AttachControlTriggers();
        }

        private void AttachControlTriggers()
        {
            nudIterations.ValueChanged += ParamChanged;
            nudCenterX.ValueChanged += ParamChanged;
            nudCenterY.ValueChanged += ParamChanged;
            nudScale.ValueChanged += ParamChanged;
            btnRender.Click += (_, _) => ScheduleRender();
            btnEditTransforms.Click += BtnEditTransforms_Click;
            btnSaveLoad.Click += btnSaveLoad_Click;
            btnSaveImage.Click += (_, _) =>
            {
                using var saveManager = new SaveImageManagerForm(this);
                saveManager.ShowDialog(this);
            };
            btnResetView.Click += (_, _) =>
            {
                _suppressEvents = true;
                try
                {
                    nudCenterX.Value = 0;
                    nudCenterY.Value = 0;
                    nudScale.Value = 2.4m;
                }
                finally
                {
                    _suppressEvents = false;
                }
                QueueRenderRestart(immediate: true);
            };
        }

        private void FractalIFSForm_Load(object sender, EventArgs e)
        {
            IfsPointOfInterest? defaultPreset = _pointsOfInterest.FirstOrDefault(p => p.Id == "barnsley_overview")
                                             ?? _pointsOfInterest.FirstOrDefault();
            if (defaultPreset != null)
            {
                ApplyPointOfInterest(defaultPreset);
            }

            UpdateToggleControlsPosition();
            ScheduleRender();
        }

        private void FractalIFSForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ExitFullscreenSafely();
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _rerenderTimer.Stop();
            _wheelDebounceTimer.Stop();
            _stableFrameBitmap?.Dispose();
        }

        private void ApplyPointOfInterest(IfsPointOfInterest point)
        {
            _engine.ApplyPointOfInterest(point);
            _suppressEvents = true;
            try
            {
                nudIterations.Value = Math.Clamp(point.Iterations, (int)nudIterations.Minimum, (int)nudIterations.Maximum);
                nudCenterX.Value = (decimal)Math.Clamp(point.CenterX, (double)nudCenterX.Minimum, (double)nudCenterX.Maximum);
                nudCenterY.Value = (decimal)Math.Clamp(point.CenterY, (double)nudCenterY.Minimum, (double)nudCenterY.Maximum);
                nudScale.Value = (decimal)Math.Clamp(point.Scale, (double)nudScale.Minimum, (double)nudScale.Maximum);
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        private void BtnEditTransforms_Click(object? sender, EventArgs e)
        {
            using var editor = new IfsTransformEditorForm(_engine.Transforms);
            if (editor.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _engine.SetTransforms(editor.ResultTransforms);
            QueueRenderRestart(immediate: true);
        }

        private void ParamChanged(object? sender, EventArgs e)
        {
            if (_suppressEvents)
            {
                return;
            }

            _engine.Iterations = (int)nudIterations.Value;
            _engine.CenterX = (double)nudCenterX.Value;
            _engine.CenterY = (double)nudCenterY.Value;
            _engine.Scale = (double)nudScale.Value;
            QueueRenderRestart();
        }

        private void Canvas_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _isPanning = true;
            _panStartPoint = e.Location;
            _panStartCenterX = (double)nudCenterX.Value;
            _panStartCenterY = (double)nudCenterY.Value;
            canvas.Cursor = Cursors.SizeAll;
        }

        private void Canvas_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isPanning || canvas.Width <= 1 || canvas.Height <= 1)
            {
                return;
            }

            double scale = (double)nudScale.Value;
            double aspectScaleY = scale * canvas.Height / (double)canvas.Width;
            int dx = e.X - _panStartPoint.X;
            int dy = e.Y - _panStartPoint.Y;

            double newCenterX = _panStartCenterX - dx * (scale / canvas.Width);
            double newCenterY = _panStartCenterY + dy * (aspectScaleY / canvas.Height);

            _suppressEvents = true;
            try
            {
                nudCenterX.Value = (decimal)Math.Clamp(newCenterX, (double)nudCenterX.Minimum, (double)nudCenterX.Maximum);
                nudCenterY.Value = (decimal)Math.Clamp(newCenterY, (double)nudCenterY.Minimum, (double)nudCenterY.Maximum);
            }
            finally
            {
                _suppressEvents = false;
            }

            _engine.CenterX = (double)nudCenterX.Value;
            _engine.CenterY = (double)nudCenterY.Value;
            ShowFastPreviewFrame();
        }

        private void Canvas_MouseUp(object? sender, MouseEventArgs e)
        {
            if (!_isPanning)
            {
                return;
            }

            _isPanning = false;
            canvas.Cursor = Cursors.Default;
            ScheduleRender();
        }

        private void Canvas_MouseLeave(object? sender, EventArgs e)
        {
            if (!_isPanning)
            {
                return;
            }

            _isPanning = false;
            canvas.Cursor = Cursors.Default;
            ScheduleRender();
        }

        private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (canvas.Width <= 1 || canvas.Height <= 1)
            {
                return;
            }

            double zoomFactor = e.Delta > 0 ? 0.86 : 1.17;
            double oldScale = (double)nudScale.Value;
            double newScale = Math.Clamp(oldScale * zoomFactor, (double)nudScale.Minimum, (double)nudScale.Maximum);

            double worldXBefore = ScreenToWorldX(e.X, oldScale, (double)nudCenterX.Value);
            double worldYBefore = ScreenToWorldY(e.Y, oldScale, (double)nudCenterY.Value);
            double newCenterX = worldXBefore - (e.X / (double)canvas.Width - 0.5) * newScale;
            double newCenterY = worldYBefore + (e.Y / (double)canvas.Height - 0.5) * newScale * canvas.Height / (double)canvas.Width;

            _suppressEvents = true;
            try
            {
                nudScale.Value = (decimal)newScale;
                nudCenterX.Value = (decimal)Math.Clamp(newCenterX, (double)nudCenterX.Minimum, (double)nudCenterX.Maximum);
                nudCenterY.Value = (decimal)Math.Clamp(newCenterY, (double)nudCenterY.Minimum, (double)nudCenterY.Maximum);
            }
            finally
            {
                _suppressEvents = false;
            }

            _engine.Scale = (double)nudScale.Value;
            _engine.CenterX = (double)nudCenterX.Value;
            _engine.CenterY = (double)nudCenterY.Value;
            ShowFastPreviewFrame();

            _wheelDebounceTimer.Stop();
            _wheelDebounceTimer.Start();
        }

        private void FractalIFSForm_KeyDown(object? sender, KeyEventArgs e)
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

        private void ToggleFullscreenSafely()
        {
            _fullscreenController.Toggle(this);
            UpdateToggleControlsPosition();
        }

        private void ExitFullscreenSafely()
        {
            if (!_fullscreenController.IsFullscreen(this))
            {
                return;
            }

            _fullscreenController.ExitFullscreen(this);
            UpdateToggleControlsPosition();
        }

        private void ToggleControlsPanel()
        {
            _controlsPanelVisible = !_controlsPanelVisible;
            controlsHost.Visible = _controlsPanelVisible;
            btnToggleControls.Text = _controlsPanelVisible ? "✕" : "☰";
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

        private void ShowFastPreviewFrame()
        {
            if (_stableFrameBitmap is null || canvas.Width <= 1 || canvas.Height <= 1)
            {
                return;
            }

            canvas.Invalidate();
        }

        private void Canvas_Paint(object? sender, PaintEventArgs e)
        {
            e.Graphics.Clear(_engine.BackgroundColor);

            if (_stableFrameBitmap is null || canvas.Width <= 1 || canvas.Height <= 1)
            {
                return;
            }

            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(_stableFrameBitmap, CalculatePreviewDestinationRect());
        }

        private RectangleF CalculatePreviewDestinationRect()
        {
            int width = canvas.Width;
            int height = canvas.Height;
            double currentCenterX = (double)nudCenterX.Value;
            double currentCenterY = (double)nudCenterY.Value;
            double currentScale = (double)nudScale.Value;
            double oldScale = _renderedScale <= 0 ? currentScale : _renderedScale;
            double scaleRatio = oldScale / currentScale;
            double unitsPerPixelCurrentX = currentScale / width;
            double unitsPerPixelCurrentY = currentScale / width;
            double centerOffsetX = (_renderedCenterX - currentCenterX) / unitsPerPixelCurrentX;
            double centerOffsetY = (_renderedCenterY - currentCenterY) / unitsPerPixelCurrentY;
            float drawWidth = (float)(width * scaleRatio);
            float drawHeight = (float)(height * scaleRatio);
            float drawX = (float)(width / 2.0 + centerOffsetX - drawWidth / 2.0);
            float drawY = (float)(height / 2.0 - centerOffsetY - drawHeight / 2.0);
            return new RectangleF(drawX, drawY, drawWidth, drawHeight);
        }

        private double ScreenToWorldX(int x, double scale, double centerX)
            => centerX + (x / (double)canvas.Width - 0.5) * scale;

        private double ScreenToWorldY(int y, double scale, double centerY)
            => centerY - (y / (double)canvas.Height - 0.5) * scale * canvas.Height / (double)canvas.Width;

        private void QueueRenderRestart(bool immediate = false)
        {
            if (immediate)
            {
                _rerenderTimer.Stop();
                ScheduleRender();
                return;
            }

            _rerenderTimer.Stop();
            _rerenderTimer.Start();
        }

        private void ScheduleRender()
        {
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _renderCts = new CancellationTokenSource();
            _ = RenderAsync(_renderCts.Token);
        }

        private async Task RenderAsync(CancellationToken token)
        {
            if (canvas.Width < 2 || canvas.Height < 2)
            {
                return;
            }

            _engine.Iterations = (int)nudIterations.Value;
            _engine.Scale = (double)nudScale.Value;
            _engine.CenterX = (double)nudCenterX.Value;
            _engine.CenterY = (double)nudCenterY.Value;

            int width = canvas.Width;
            int height = canvas.Height;
            using Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
            Rectangle rect = new(0, 0, width, height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = data.Stride;
                int bufferLen = stride * height;
                byte[] buffer = new byte[bufferLen];

                Progress<int> progress = new(value =>
                {
                    int clamped = Math.Clamp(value, 0, 100);
                    if (!IsDisposed)
                    {
                        pbRenderProgress.Value = clamped;
                    }
                });

                await Task.Run(() => _engine.RenderToBuffer(buffer, width, height, stride, 4, token, p => ((IProgress<int>)progress).Report(p)), token);
                Marshal.Copy(buffer, 0, data.Scan0, bufferLen);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            Bitmap ready = (Bitmap)bitmap.Clone();
            BeginInvoke(new Action(() =>
            {
                Bitmap? previousStable = _stableFrameBitmap;
                previousStable?.Dispose();
                _stableFrameBitmap = ready;
                canvas.Invalidate();
                pbRenderProgress.Value = 100;
                _renderedCenterX = _engine.CenterX;
                _renderedCenterY = _engine.CenterY;
                _renderedScale = _engine.Scale;
            }));
        }

        private void btnFractalColor_Click(object sender, EventArgs e)
        {
            using ColorPickerPanelForm dialog = new(_engine.FractalColor);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _engine.FractalColor = dialog.SelectedColor;
                QueueRenderRestart(immediate: true);
            }
        }

        private void btnBackgroundColor_Click(object sender, EventArgs e)
        {
            using ColorPickerPanelForm dialog = new(_engine.BackgroundColor);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _engine.BackgroundColor = dialog.SelectedColor;
                QueueRenderRestart(immediate: true);
            }
        }

        private void btnSaveLoad_Click(object? sender, EventArgs e)
        {
            using SaveLoadDialogForm dlg = new(this);
            dlg.ShowDialog(this);
        }

        public string FractalTypeIdentifier => "IFS";
        public Type ConcreteSaveStateType => typeof(IFSSaveState);

        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            return new IFSSaveState(FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                Iterations = (int)nudIterations.Value,
                CenterX = (double)nudCenterX.Value,
                CenterY = (double)nudCenterY.Value,
                Scale = (double)nudScale.Value,
                Transforms = _engine.Transforms.Select(t => t.Clone()).ToList(),
                FractalColor = _engine.FractalColor,
                BackgroundColor = _engine.BackgroundColor
            };
        }

        public void LoadState(FractalSaveStateBase state)
        {
            if (state is not IFSSaveState s)
            {
                return;
            }

            _suppressEvents = true;
            try
            {
                nudIterations.Value = Math.Clamp(s.Iterations, (int)nudIterations.Minimum, (int)nudIterations.Maximum);
                nudCenterX.Value = (decimal)Math.Clamp(s.CenterX, (double)nudCenterX.Minimum, (double)nudCenterX.Maximum);
                nudCenterY.Value = (decimal)Math.Clamp(s.CenterY, (double)nudCenterY.Minimum, (double)nudCenterY.Maximum);
                nudScale.Value = (decimal)Math.Clamp(s.Scale <= 0 ? 2.4 : s.Scale, (double)nudScale.Minimum, (double)nudScale.Maximum);
            }
            finally
            {
                _suppressEvents = false;
            }

            if (s.Transforms.Count > 0)
            {
                _engine.SetTransforms(s.Transforms);
            }
            else if (!string.IsNullOrWhiteSpace(s.PointOfInterestId))
            {
                IfsPointOfInterest? pointById = _pointsOfInterest.FirstOrDefault(p => p.Id == s.PointOfInterestId);
                if (pointById != null)
                {
                    _engine.SetTransforms(pointById.Transforms);
                }
            }
            else if (_pointsOfInterest.Count > 0)
            {
                _engine.SetTransforms(_pointsOfInterest[0].Transforms);
            }

            _engine.FractalColor = s.FractalColor;
            _engine.BackgroundColor = s.BackgroundColor;
            QueueRenderRestart(immediate: true);
        }

        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (state is not IFSSaveState s)
            {
                return new Bitmap(previewWidth, previewHeight);
            }

            var previewEngine = new FractalIFSGeometryEngine
            {
                Iterations = s.Iterations,
                CenterX = s.CenterX,
                CenterY = s.CenterY,
                Scale = s.Scale <= 0 ? 2.4 : s.Scale,
                FractalColor = s.FractalColor,
                BackgroundColor = s.BackgroundColor
            };
            if (s.Transforms.Count > 0)
            {
                previewEngine.SetTransforms(s.Transforms);
            }
            else if (!string.IsNullOrWhiteSpace(s.PointOfInterestId))
            {
                IfsPointOfInterest? pointById = _pointsOfInterest.FirstOrDefault(p => p.Id == s.PointOfInterestId);
                if (pointById != null)
                {
                    previewEngine.SetTransforms(pointById.Transforms);
                }
            }

            Bitmap bmp = new(previewWidth, previewHeight, PixelFormat.Format32bppArgb);
            Rectangle rect = new(0, 0, previewWidth, previewHeight);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] buffer = new byte[stride * previewHeight];
                previewEngine.RenderToBuffer(buffer, previewWidth, previewHeight, stride, 4, CancellationToken.None);
                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            return bmp;
        }

        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            using Bitmap full = await Task.Run(() => RenderPreview(state, totalWidth, totalHeight));
            BitmapData data = full.LockBits(new Rectangle(0, 0, full.Width, full.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] bytes = new byte[Math.Abs(data.Stride) * full.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                full.UnlockBits(data);
            }
        }

        public async Task<byte[]> RenderPreviewAsync(
            FractalSaveStateBase state,
            int previewWidth,
            int previewHeight,
            CancellationToken cancellationToken,
            IProgress<int>? progress = null)
        {
            if (state is not IFSSaveState s)
            {
                return new byte[Math.Max(1, previewWidth * previewHeight * 4)];
            }

            var previewEngine = new FractalIFSGeometryEngine
            {
                Iterations = s.Iterations,
                CenterX = s.CenterX,
                CenterY = s.CenterY,
                Scale = s.Scale <= 0 ? 2.4 : s.Scale,
                FractalColor = s.FractalColor,
                BackgroundColor = s.BackgroundColor
            };

            if (s.Transforms.Count > 0)
            {
                previewEngine.SetTransforms(s.Transforms);
            }
            else if (!string.IsNullOrWhiteSpace(s.PointOfInterestId))
            {
                IfsPointOfInterest? pointById = _pointsOfInterest.FirstOrDefault(p => p.Id == s.PointOfInterestId);
                if (pointById != null)
                {
                    previewEngine.SetTransforms(pointById.Transforms);
                }
            }

            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            int stride = width * 4;
            byte[] buffer = new byte[stride * height];

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                previewEngine.RenderToBuffer(
                    buffer,
                    width,
                    height,
                    stride,
                    4,
                    cancellationToken,
                    p => progress?.Report(Math.Clamp(p, 0, 100)));
            }, cancellationToken);

            return buffer;
        }

        public List<FractalSaveStateBase> LoadAllSavesForThisType()
            => SaveFileManager.LoadSaves<IFSSaveState>(FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
            => SaveFileManager.SaveSaves(FractalTypeIdentifier, saves.Cast<IFSSaveState>().ToList());

        public HighResRenderState GetRenderState()
        {
            return new HighResRenderState
            {
                EngineType = FractalTypeIdentifier,
                CenterX = (decimal)_engine.CenterX,
                CenterY = (decimal)_engine.CenterY,
                Scale = (decimal)_engine.Scale,
                Iterations = _engine.Iterations,
                FileNameDetails = "ifs_fractal"
            };
        }

        public async Task<Bitmap> RenderHighResolutionAsync(
            HighResRenderState state,
            int width,
            int height,
            int ssaaFactor,
            IProgress<RenderProgress> progress,
            CancellationToken cancellationToken)
        {
            int renderWidth = Math.Max(1, width * Math.Max(1, ssaaFactor));
            int renderHeight = Math.Max(1, height * Math.Max(1, ssaaFactor));
            int stride = renderWidth * 4;
            byte[] buffer = new byte[stride * renderHeight];
            int baseIterations = state.Iterations > 0 ? state.Iterations : _engine.Iterations;
            int scaledIterations = ScaleIterationsForResolution(baseIterations, renderWidth, renderHeight);

            var renderEngine = new FractalIFSGeometryEngine
            {
                Iterations = scaledIterations,
                CenterX = (double)state.CenterX,
                CenterY = (double)state.CenterY,
                Scale = state.Scale > 0 ? (double)state.Scale : _engine.Scale,
                FractalColor = _engine.FractalColor,
                BackgroundColor = _engine.BackgroundColor
            };
            renderEngine.SetTransforms(_engine.Transforms);

            await Task.Run(() =>
            {
                renderEngine.RenderToBuffer(
                    buffer,
                    renderWidth,
                    renderHeight,
                    stride,
                    4,
                    cancellationToken,
                    p => progress.Report(new RenderProgress
                    {
                        Percentage = Math.Clamp(p, 0, 100),
                        Status = "Рендеринг..."
                    }));
            }, cancellationToken);

            Bitmap full = new(renderWidth, renderHeight, PixelFormat.Format32bppArgb);
            BitmapData fullData = full.LockBits(new Rectangle(0, 0, renderWidth, renderHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(buffer, 0, fullData.Scan0, buffer.Length);
            }
            finally
            {
                full.UnlockBits(fullData);
            }

            if (ssaaFactor <= 1)
            {
                return full;
            }

            Bitmap downscaled = new(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(downscaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(full, new Rectangle(0, 0, width, height));
            }

            full.Dispose();
            return downscaled;
        }

        private int ScaleIterationsForResolution(int baseIterations, int targetWidth, int targetHeight)
        {
            int safeBaseIterations = Math.Max(1_000, baseIterations);
            int sourceWidth = Math.Max(1, canvas.Width);
            int sourceHeight = Math.Max(1, canvas.Height);

            double sourcePixels = (double)sourceWidth * sourceHeight;
            double targetPixels = (double)Math.Max(1, targetWidth) * Math.Max(1, targetHeight);
            double scaleFactor = targetPixels / sourcePixels;
            double scaled = safeBaseIterations * Math.Max(1.0, scaleFactor);

            return scaled >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(scaled);
        }

        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            var renderEngine = new FractalIFSGeometryEngine
            {
                Iterations = state.Iterations > 0 ? state.Iterations : _engine.Iterations,
                CenterX = (double)state.CenterX,
                CenterY = (double)state.CenterY,
                Scale = state.Scale > 0 ? (double)state.Scale : _engine.Scale,
                FractalColor = _engine.FractalColor,
                BackgroundColor = _engine.BackgroundColor
            };
            renderEngine.SetTransforms(_engine.Transforms);

            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            Bitmap bmp = new(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int localStride = data.Stride;
                byte[] localBuffer = new byte[localStride * height];
                renderEngine.RenderToBuffer(localBuffer, width, height, localStride, 4, CancellationToken.None);
                Marshal.Copy(localBuffer, 0, data.Scan0, localBuffer.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            return bmp;
        }
    }
}
