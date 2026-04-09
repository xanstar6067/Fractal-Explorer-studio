using FractalExplorer.Engines;
using FractalExplorer.Forms.Other;
using FractalExplorer.Resources;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FractalExplorer.Forms.Fractals
{
    public partial class FractalIFSForm : Form, ISaveLoadCapableFractal
    {
        private readonly FractalIFSGeometryEngine _engine = new();
        private readonly List<IfsPointOfInterest> _pointsOfInterest = FractalIFSGeometryEngine.CreateDefaultPointsOfInterest();
        private CancellationTokenSource? _renderCts;
        private readonly System.Windows.Forms.Timer _rerenderTimer = new() { Interval = 260 };
        private readonly System.Windows.Forms.Timer _wheelDebounceTimer = new() { Interval = 360 };
        private bool _isPanning;
        private Point _panStartPoint;
        private double _panStartCenterX;
        private double _panStartCenterY;
        private bool _suppressEvents;

        public FractalIFSForm()
        {
            InitializeComponent();
            canvas.MouseWheel += Canvas_MouseWheel;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.MouseLeave += Canvas_MouseLeave;
            canvas.Resize += (_, _) => QueueRenderRestart(immediate: true);

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

            cbPointOfInterest.DisplayMember = nameof(IfsPointOfInterest.Name);
            cbPointOfInterest.ValueMember = nameof(IfsPointOfInterest.Id);
            cbPointOfInterest.DataSource = _pointsOfInterest;

            AttachControlTriggers();
        }

        private void AttachControlTriggers()
        {
            nudIterations.ValueChanged += ParamChanged;
            nudCenterX.ValueChanged += ParamChanged;
            nudCenterY.ValueChanged += ParamChanged;
            nudScale.ValueChanged += ParamChanged;
            cbPointOfInterest.SelectedIndexChanged += CbPointOfInterest_SelectedIndexChanged;

            btnRender.Click += (_, _) => ScheduleRender();
            btnEditTransforms.Click += BtnEditTransforms_Click;
            btnSaveLoad.Click += btnSaveLoad_Click;
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
            if (_pointsOfInterest.Count > 0)
            {
                cbPointOfInterest.SelectedIndex = 0;
                ApplyPointOfInterest(_pointsOfInterest[0]);
            }

            ScheduleRender();
        }

        private void FractalIFSForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _rerenderTimer.Stop();
            _wheelDebounceTimer.Stop();
        }

        private void CbPointOfInterest_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_suppressEvents || cbPointOfInterest.SelectedItem is not IfsPointOfInterest selected)
            {
                return;
            }

            ApplyPointOfInterest(selected);
            QueueRenderRestart(immediate: true);
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

            _wheelDebounceTimer.Stop();
            _wheelDebounceTimer.Start();
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
                Image? old = canvas.Image;
                canvas.Image = ready;
                old?.Dispose();
                canvas.Refresh();
                pbRenderProgress.Value = 100;
            }));
        }

        private void btnFractalColor_Click(object sender, EventArgs e)
        {
            using ColorDialog dialog = new() { Color = _engine.FractalColor };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _engine.FractalColor = dialog.Color;
                QueueRenderRestart(immediate: true);
            }
        }

        private void btnBackgroundColor_Click(object sender, EventArgs e)
        {
            using ColorDialog dialog = new() { Color = _engine.BackgroundColor };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _engine.BackgroundColor = dialog.Color;
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
                PointOfInterestId = cbPointOfInterest.SelectedValue?.ToString(),
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
                if (!string.IsNullOrWhiteSpace(s.PointOfInterestId))
                {
                    int idx = _pointsOfInterest.FindIndex(p => p.Id == s.PointOfInterestId);
                    if (idx >= 0)
                    {
                        cbPointOfInterest.SelectedIndex = idx;
                    }
                }

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
            else if (cbPointOfInterest.SelectedItem is IfsPointOfInterest point)
            {
                _engine.SetTransforms(point.Transforms);
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

        public List<FractalSaveStateBase> LoadAllSavesForThisType()
            => SaveFileManager.LoadSaves<IFSSaveState>(FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
            => SaveFileManager.SaveSaves(FractalTypeIdentifier, saves.Cast<IFSSaveState>().ToList());
    }
}
