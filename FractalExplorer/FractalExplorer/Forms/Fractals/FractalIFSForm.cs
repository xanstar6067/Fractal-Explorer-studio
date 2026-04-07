using FractalExplorer.Engines;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FractalExplorer.Forms.Fractals
{
    public partial class FractalIFSForm : Form, ISaveLoadCapableFractal
    {
        private readonly FractalIFSGeometryEngine _engine = new();
        private CancellationTokenSource? _renderCts;
        private Point _panStart;
        private bool _panning;
        private double _zoom = 1.0;
        private PointF _offset = new(0, 0);

        public FractalIFSForm()
        {
            InitializeComponent();
            cbPreset.DataSource = Enum.GetValues(typeof(IfsPreset));
            canvas.MouseWheel += Canvas_MouseWheel;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.Resize += (_, _) => ScheduleRender();
        }

        private void FractalIFSForm_Load(object sender, EventArgs e)
        {
            cbPreset.SelectedItem = IfsPreset.BarnsleyFern;
            ScheduleRender();
        }

        private void FractalIFSForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _renderCts?.Cancel();
            _renderCts?.Dispose();
        }

        private void ParamChanged(object? sender, EventArgs e)
        {
            _engine.Iterations = (int)nudIterations.Value;
            _engine.ApplyPreset((IfsPreset)cbPreset.SelectedItem!);
            ScheduleRender();
        }

        private void Canvas_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _panning = true;
            _panStart = e.Location;
        }

        private void Canvas_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_panning) return;
            _offset = new PointF(_offset.X + (e.X - _panStart.X), _offset.Y + (e.Y - _panStart.Y));
            _panStart = e.Location;
            if (canvas.Image != null)
            {
                canvas.Invalidate();
            }
        }

        private void Canvas_MouseUp(object? sender, MouseEventArgs e)
        {
            _panning = false;
        }

        private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
        {
            _zoom *= e.Delta > 0 ? 1.1 : 0.9;
            _zoom = Math.Clamp(_zoom, 0.2, 5.0);
            canvas.Invalidate();
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

                await Task.Run(() => _engine.RenderToBuffer(buffer, width, height, stride, 4, token), token);
                Marshal.Copy(buffer, 0, data.Scan0, bufferLen);
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
            }));
        }

        private void btnFractalColor_Click(object sender, EventArgs e)
        {
            using ColorDialog dialog = new() { Color = _engine.FractalColor };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _engine.FractalColor = dialog.Color;
                ScheduleRender();
            }
        }

        private void btnBackgroundColor_Click(object sender, EventArgs e)
        {
            using ColorDialog dialog = new() { Color = _engine.BackgroundColor };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _engine.BackgroundColor = dialog.Color;
                ScheduleRender();
            }
        }

        private void btnSaveLoad_Click(object sender, EventArgs e)
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
                Preset = (IfsPreset)cbPreset.SelectedItem!,
                Iterations = (int)nudIterations.Value,
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

            cbPreset.SelectedItem = s.Preset;
            nudIterations.Value = Math.Clamp(s.Iterations, nudIterations.Minimum, nudIterations.Maximum);
            _engine.FractalColor = s.FractalColor;
            _engine.BackgroundColor = s.BackgroundColor;
            ScheduleRender();
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
                FractalColor = s.FractalColor,
                BackgroundColor = s.BackgroundColor
            };
            previewEngine.ApplyPreset(s.Preset);

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
