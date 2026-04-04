using FractalExplorer.Engines;
using FractalExplorer.Forms;
using FractalExplorer.Forms.Other;
using FractalExplorer.Resources;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using FractalExplorer.Utilities.Theme;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FractalExplorer.Forms.Fractals
{
    public sealed class FractalFlameForm : Form, ISaveLoadCapableFractal, IFullPreviewRenderCapableFractal, IHighResRenderable
    {
        private readonly FractalFlameEngine _engine = new();
        private readonly PictureBox _canvas = new() { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
        private readonly NumericUpDown _samples = new() { Minimum = 1000, Maximum = 20_000_000, Increment = 1000, Value = 1_000_000 };
        private readonly NumericUpDown _iterations = new() { Minimum = 1, Maximum = 300, Value = 20 };
        private readonly NumericUpDown _warmup = new() { Minimum = 0, Maximum = 100, Value = 20 };
        private readonly NumericUpDown _scale = new() { Minimum = 0.1m, Maximum = 20m, DecimalPlaces = 3, Increment = 0.05m, Value = 4m };
        private readonly NumericUpDown _centerX = new() { Minimum = -10m, Maximum = 10m, DecimalPlaces = 4, Increment = 0.01m, Value = 0m };
        private readonly NumericUpDown _centerY = new() { Minimum = -10m, Maximum = 10m, DecimalPlaces = 4, Increment = 0.01m, Value = 0m };
        private readonly NumericUpDown _exposure = new() { Minimum = 0.1m, Maximum = 10m, DecimalPlaces = 2, Increment = 0.05m, Value = 1.35m };
        private readonly NumericUpDown _gamma = new() { Minimum = 0.1m, Maximum = 5m, DecimalPlaces = 2, Increment = 0.05m, Value = 2.20m };
        private readonly ComboBox _threads = new() { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button _btnRender = new() { Text = "Render" };
        private readonly Button _btnEditTransforms = new() { Text = "Трансформации" };
        private readonly Label _status = new() { AutoSize = true, Text = "Готово" };
        private CancellationTokenSource? _cts;

        public FractalFlameForm()
        {
            Text = "Fractal Flame";
            Width = 1280;
            Height = 820;
            ThemeManager.RegisterForm(this);
            BuildUi();
            InitializeDefaults();
        }

        private void BuildUi()
        {
            var panel = new Panel { Dock = DockStyle.Left, Width = 280, Padding = new Padding(8) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

            AddLabeled(layout, "Samples", _samples);
            AddLabeled(layout, "Iter/Sample", _iterations);
            AddLabeled(layout, "Warmup", _warmup);
            AddLabeled(layout, "Scale", _scale);
            AddLabeled(layout, "CenterX", _centerX);
            AddLabeled(layout, "CenterY", _centerY);
            AddLabeled(layout, "Exposure", _exposure);
            AddLabeled(layout, "Gamma", _gamma);
            AddLabeled(layout, "Threads", _threads);

            _btnRender.Dock = DockStyle.Top;
            _btnEditTransforms.Dock = DockStyle.Top;
            _btnRender.Click += async (_, _) => await RenderAsync();
            _btnEditTransforms.Click += BtnEditTransforms_Click;

            var btnSaveLoad = new Button { Text = "Save/Load", Dock = DockStyle.Top };
            btnSaveLoad.Click += (_, _) => { using var dlg = new SaveLoadDialogForm(this); dlg.ShowDialog(this); };
            var btnSaveImage = new Button { Text = "Сохранить PNG", Dock = DockStyle.Top };
            btnSaveImage.Click += (_, _) => { using var dlg = new SaveImageManagerForm(this); dlg.ShowDialog(this); };

            panel.Controls.Add(_status);
            panel.Controls.Add(btnSaveImage);
            panel.Controls.Add(btnSaveLoad);
            panel.Controls.Add(_btnEditTransforms);
            panel.Controls.Add(_btnRender);
            panel.Controls.Add(layout);

            Controls.Add(_canvas);
            Controls.Add(panel);
            Load += async (_, _) => await RenderAsync();
            FormClosing += (_, _) => _cts?.Cancel();
        }

        private void AddLabeled(TableLayoutPanel layout, string label, Control control)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Top;
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        private void InitializeDefaults()
        {
            _threads.Items.Add("Auto");
            for (int i = 1; i <= Environment.ProcessorCount; i++) _threads.Items.Add(i);
            _threads.SelectedIndex = 0;

            _engine.Transforms.Clear();
            _engine.Transforms.AddRange(new[]
            {
                new FlameTransform { Weight = 1.0, A = 0.5, B = 0, C = -0.3, D = 0, E = 0.5, F = 0.0, Variation = FlameVariation.Linear, Color = Color.Orange },
                new FlameTransform { Weight = 1.0, A = 0.5, B = 0, C = 0.3, D = 0, E = 0.5, F = 0.0, Variation = FlameVariation.Sinusoidal, Color = Color.DeepSkyBlue },
                new FlameTransform { Weight = 0.6, A = 0.5, B = 0, C = 0.0, D = 0, E = 0.5, F = 0.4, Variation = FlameVariation.Spherical, Color = Color.Lime }
            });
        }

        private void BtnEditTransforms_Click(object? sender, EventArgs e)
        {
            using var editor = new FlameTransformEditorForm(_engine.Transforms);
            if (editor.ShowDialog(this) == DialogResult.OK)
            {
                _engine.Transforms.Clear();
                _engine.Transforms.AddRange(editor.ResultTransforms);
            }
        }

        private void ApplyUiToEngine()
        {
            _engine.Samples = (int)_samples.Value;
            _engine.IterationsPerSample = (int)_iterations.Value;
            _engine.WarmupIterations = (int)_warmup.Value;
            _engine.Scale = (double)_scale.Value;
            _engine.CenterX = (double)_centerX.Value;
            _engine.CenterY = (double)_centerY.Value;
            _engine.Exposure = (double)_exposure.Value;
            _engine.Gamma = (double)_gamma.Value;
            _engine.ThreadCount = _threads.SelectedItem is int i ? i : 0;
        }

        private async Task RenderAsync()
        {
            if (_canvas.Width <= 1 || _canvas.Height <= 1)
            {
                return;
            }

            ApplyUiToEngine();
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            _btnRender.Enabled = false;
            _status.Text = "Рендер...";
            var bmp = new Bitmap(_canvas.Width, _canvas.Height, PixelFormat.Format32bppArgb);
            Rectangle rect = new(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
            byte[] buffer = new byte[Math.Abs(data.Stride) * data.Height];
            var progress = new Progress<int>(p => _status.Text = $"Рендер: {Math.Clamp(p, 0, 100)}%");

            try
            {
                await Task.Run(() => _engine.RenderToBuffer(buffer, bmp.Width, bmp.Height, data.Stride, 4, token, p => ((IProgress<int>)progress).Report(p)), token);
                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
                _status.Text = "Готово";
            }
            catch (OperationCanceledException)
            {
                _status.Text = "Отменено";
            }
            finally
            {
                bmp.UnlockBits(data);
                if (!token.IsCancellationRequested)
                {
                    Bitmap? old = _canvas.Image as Bitmap;
                    _canvas.Image = bmp;
                    old?.Dispose();
                }
                else
                {
                    bmp.Dispose();
                }
                _btnRender.Enabled = true;
            }
        }

        public string FractalTypeIdentifier => "Flame";
        public Type ConcreteSaveStateType => typeof(FlameFractalSaveState);

        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            ApplyUiToEngine();
            return new FlameFractalSaveState(FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _engine.CenterX,
                CenterY = _engine.CenterY,
                Scale = _engine.Scale,
                Samples = _engine.Samples,
                IterationsPerSample = _engine.IterationsPerSample,
                WarmupIterations = _engine.WarmupIterations,
                Exposure = _engine.Exposure,
                Gamma = _engine.Gamma,
                Transforms = _engine.Transforms.Select(t => t.Clone()).ToList()
            };
        }

        public void LoadState(FractalSaveStateBase state)
        {
            if (state is not FlameFractalSaveState s)
            {
                return;
            }

            _centerX.Value = (decimal)s.CenterX;
            _centerY.Value = (decimal)s.CenterY;
            _scale.Value = (decimal)Math.Clamp(s.Scale, (double)_scale.Minimum, (double)_scale.Maximum);
            _samples.Value = Math.Clamp(s.Samples, (int)_samples.Minimum, (int)_samples.Maximum);
            _iterations.Value = Math.Clamp(s.IterationsPerSample, (int)_iterations.Minimum, (int)_iterations.Maximum);
            _warmup.Value = Math.Clamp(s.WarmupIterations, (int)_warmup.Minimum, (int)_warmup.Maximum);
            _exposure.Value = (decimal)Math.Clamp(s.Exposure, (double)_exposure.Minimum, (double)_exposure.Maximum);
            _gamma.Value = (decimal)Math.Clamp(s.Gamma, (double)_gamma.Minimum, (double)_gamma.Maximum);
            _engine.Transforms.Clear();
            _engine.Transforms.AddRange((s.Transforms ?? new List<FlameTransform>()).Select(t => t.Clone()));
            _ = RenderAsync();
        }

        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            using CancellationTokenSource cts = new();
            return RenderPreviewCore(state, previewWidth, previewHeight, cts.Token, null);
        }

        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            if (state is not FlameFractalSaveState)
            {
                return Array.Empty<byte>();
            }

            using CancellationTokenSource cts = new();
            using Bitmap bmp = RenderPreviewCore(state, totalWidth, totalHeight, cts.Token, null);
            Rectangle tileRect = tile.Bounds;
            BitmapData data = bmp.LockBits(tileRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] bytes = new byte[Math.Abs(data.Stride) * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            bmp.UnlockBits(data);
            await Task.CompletedTask;
            return bytes;
        }

        public async Task<byte[]> RenderPreviewAsync(
            FractalSaveStateBase state,
            int previewWidth,
            int previewHeight,
            CancellationToken cancellationToken,
            IProgress<int>? progress = null)
        {
            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            using Bitmap bmp = await Task.Run(() => RenderPreviewCore(state, width, height, cancellationToken, progress), cancellationToken);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] bytes = new byte[Math.Abs(data.Stride) * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            bmp.UnlockBits(data);
            return bytes;
        }

        private Bitmap RenderPreviewCore(FractalSaveStateBase state, int width, int height, CancellationToken token, IProgress<int>? progress)
        {
            FlameFractalSaveState? save = state as FlameFractalSaveState;
            var engine = new FractalFlameEngine
            {
                CenterX = save?.CenterX ?? _engine.CenterX,
                CenterY = save?.CenterY ?? _engine.CenterY,
                Scale = save?.Scale ?? _engine.Scale,
                Samples = Math.Max(50_000, (save?.Samples ?? _engine.Samples) / 10),
                IterationsPerSample = save?.IterationsPerSample ?? _engine.IterationsPerSample,
                WarmupIterations = save?.WarmupIterations ?? _engine.WarmupIterations,
                Exposure = save?.Exposure ?? _engine.Exposure,
                Gamma = save?.Gamma ?? _engine.Gamma,
                ThreadCount = 0
            };

            engine.Transforms.AddRange((save?.Transforms ?? _engine.Transforms).Select(t => t.Clone()));
            Bitmap bmp = new(width, height, PixelFormat.Format32bppArgb);
            Rectangle rect = new(0, 0, width, height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
            byte[] buffer = new byte[Math.Abs(data.Stride) * data.Height];
            engine.RenderToBuffer(buffer, width, height, data.Stride, 4, token, p => progress?.Report(p));
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        public HighResRenderState GetRenderState()
        {
            return new HighResRenderState
            {
                EngineType = FractalTypeIdentifier,
                CenterX = (decimal)_engine.CenterX,
                CenterY = (decimal)_engine.CenterY,
                Scale = (decimal)_engine.Scale,
                Iterations = _engine.IterationsPerSample,
                BuddhabrotSampleCount = _engine.Samples,
                OrbitTrapStrength = _engine.Exposure,
                OrbitTrapBias = _engine.Gamma,
                FileNameDetails = "flame_fractal"
            };
        }

        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            int renderWidth = Math.Max(1, width * Math.Max(1, ssaaFactor));
            int renderHeight = Math.Max(1, height * Math.Max(1, ssaaFactor));

            var save = new FlameFractalSaveState(FractalTypeIdentifier)
            {
                CenterX = (double)state.CenterX,
                CenterY = (double)state.CenterY,
                Scale = state.Scale == 0 ? _engine.Scale : (double)state.Scale,
                IterationsPerSample = state.Iterations > 0 ? state.Iterations : _engine.IterationsPerSample,
                Samples = state.BuddhabrotSampleCount ?? _engine.Samples,
                Exposure = state.OrbitTrapStrength > 0 ? state.OrbitTrapStrength : _engine.Exposure,
                Gamma = state.OrbitTrapBias > 0 ? state.OrbitTrapBias : _engine.Gamma,
                WarmupIterations = _engine.WarmupIterations,
                Transforms = _engine.Transforms.Select(t => t.Clone()).ToList()
            };

            Bitmap full = await Task.Run(() => RenderPreviewCore(save, renderWidth, renderHeight, cancellationToken, new Progress<int>(p =>
            {
                progress.Report(new RenderProgress { Percentage = p, Status = "Рендеринг..." });
            })), cancellationToken);

            if (ssaaFactor <= 1)
            {
                return full;
            }

            Bitmap downscaled = new(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(downscaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(full, new Rectangle(0, 0, width, height));
            }
            full.Dispose();
            return downscaled;
        }

        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            using var cts = new CancellationTokenSource();
            var save = new FlameFractalSaveState(FractalTypeIdentifier)
            {
                CenterX = (double)state.CenterX,
                CenterY = (double)state.CenterY,
                Scale = state.Scale == 0 ? _engine.Scale : (double)state.Scale,
                IterationsPerSample = state.Iterations > 0 ? state.Iterations : _engine.IterationsPerSample,
                Samples = Math.Max(20_000, (state.BuddhabrotSampleCount ?? _engine.Samples) / 8),
                Exposure = state.OrbitTrapStrength > 0 ? state.OrbitTrapStrength : _engine.Exposure,
                Gamma = state.OrbitTrapBias > 0 ? state.OrbitTrapBias : _engine.Gamma,
                WarmupIterations = _engine.WarmupIterations,
                Transforms = _engine.Transforms.Select(t => t.Clone()).ToList()
            };
            return RenderPreviewCore(save, previewWidth, previewHeight, cts.Token, null);
        }

        public List<FractalSaveStateBase> LoadAllSavesForThisType() =>
            SaveFileManager.LoadSaves<FlameFractalSaveState>(FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves) =>
            SaveFileManager.SaveSaves(FractalTypeIdentifier, saves.Cast<FlameFractalSaveState>().ToList());
    }
}
