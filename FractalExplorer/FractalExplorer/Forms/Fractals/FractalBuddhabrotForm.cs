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
    public sealed class FractalBuddhabrotForm : Form, ISaveLoadCapableFractal
    {
        private const decimal BaseScale = 4.0m;

        private readonly FractalBuddhabrotEngine _engine = new();
        private readonly PaletteManager _paletteManager = new();
        private CancellationTokenSource? _renderCts;

        private readonly PictureBox _canvas = new();
        private readonly ComboBox _modeCombo = new();
        private readonly ComboBox _paletteCombo = new();
        private readonly NumericUpDown _iterations = new();
        private readonly NumericUpDown _samples = new();
        private readonly NumericUpDown _zoom = new();

        private readonly NumericUpDown _sampleMinRe = new();
        private readonly NumericUpDown _sampleMaxRe = new();
        private readonly NumericUpDown _sampleMinIm = new();
        private readonly NumericUpDown _sampleMaxIm = new();

        private decimal _centerX = 0;
        private decimal _centerY = 0;

        public FractalBuddhabrotForm()
        {
            Text = "Фрактал Buddhabrot";
            Width = 1320;
            Height = 860;
            KeyPreview = true;
            ThemeManager.RegisterForm(this);

            BuildLayout();
            Load += (_, __) =>
            {
                ApplyUiToEngine();
                _ = RenderAsync();
            };
            FormClosing += (_, __) => _renderCts?.Cancel();
        }

        private void BuildLayout()
        {
            var root = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 1020 };
            Controls.Add(root);

            _canvas.Dock = DockStyle.Fill;
            _canvas.BackColor = Color.Black;
            _canvas.SizeMode = PictureBoxSizeMode.Zoom;
            _canvas.MouseWheel += Canvas_MouseWheel;
            root.Panel1.Controls.Add(_canvas);

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoScroll = true,
                WrapContents = false,
                Padding = new Padding(8)
            };
            root.Panel2.Controls.Add(panel);

            panel.Controls.Add(MakeLabel("Режим"));
            _modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _modeCombo.Items.AddRange(new object[] { "Buddhabrot", "Anti-Buddhabrot" });
            _modeCombo.SelectedIndex = 0;
            panel.Controls.Add(_modeCombo);

            panel.Controls.Add(MakeLabel("Палитра"));
            _paletteCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var p in _paletteManager.Palettes) _paletteCombo.Items.Add(p.Name);
            _paletteCombo.SelectedItem = _paletteManager.ActivePalette?.Name;
            panel.Controls.Add(_paletteCombo);

            panel.Controls.Add(MakeLabel("Samples"));
            ConfigureDecimal(_samples, 1_000, 5_000_000, 250_000, 0);
            panel.Controls.Add(_samples);

            panel.Controls.Add(MakeLabel("Max Iterations"));
            ConfigureDecimal(_iterations, 10, 10_000, 500, 0);
            panel.Controls.Add(_iterations);

            panel.Controls.Add(MakeLabel("Zoom"));
            ConfigureDecimal(_zoom, 0.0000001m, 100000m, 1m, 6);
            panel.Controls.Add(_zoom);

            panel.Controls.Add(MakeLabel("Sample Min Re"));
            ConfigureDecimal(_sampleMinRe, -4, 4, -2m, 4);
            panel.Controls.Add(_sampleMinRe);
            panel.Controls.Add(MakeLabel("Sample Max Re"));
            ConfigureDecimal(_sampleMaxRe, -4, 4, 1m, 4);
            panel.Controls.Add(_sampleMaxRe);
            panel.Controls.Add(MakeLabel("Sample Min Im"));
            ConfigureDecimal(_sampleMinIm, -4, 4, -1.5m, 4);
            panel.Controls.Add(_sampleMinIm);
            panel.Controls.Add(MakeLabel("Sample Max Im"));
            ConfigureDecimal(_sampleMaxIm, -4, 4, 1.5m, 4);
            panel.Controls.Add(_sampleMaxIm);

            var btnRender = new Button { Text = "Рендер", Width = 220, Height = 36 };
            btnRender.Click += async (_, __) => await RenderAsync();
            panel.Controls.Add(btnRender);

            var btnSaveLoad = new Button { Text = "Сохранить / Загрузить", Width = 220, Height = 36 };
            btnSaveLoad.Click += (_, __) => new SaveLoadDialogForm(this).ShowDialog(this);
            panel.Controls.Add(btnSaveLoad);
        }

        private static Label MakeLabel(string text) => new() { Text = text, Width = 220, Height = 24, TextAlign = ContentAlignment.BottomLeft };

        private static void ConfigureDecimal(NumericUpDown nud, decimal min, decimal max, decimal value, int decimalPlaces)
        {
            nud.Minimum = min;
            nud.Maximum = max;
            nud.Value = Math.Clamp(value, min, max);
            nud.DecimalPlaces = decimalPlaces;
            nud.Increment = decimalPlaces == 0 ? 1 : 0.01m;
            nud.Width = 220;
        }

        private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
        {
            _zoom.Value = Math.Clamp(_zoom.Value * (e.Delta > 0 ? 0.8m : 1.25m), _zoom.Minimum, _zoom.Maximum);
            _ = RenderAsync();
        }

        private void ApplyUiToEngine()
        {
            _engine.CenterX = _centerX;
            _engine.CenterY = _centerY;
            _engine.Scale = BaseScale / Math.Max(0.0000001m, _zoom.Value);
            _engine.MaxIterations = (int)_iterations.Value;
            _engine.SampleCount = (int)_samples.Value;
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
                await Task.Run(() => _engine.RenderToBuffer(pixels, width, height, width * 4, 4, token), token);
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
                decimal fullScale = BaseScale / Math.Max(0.0000001m, s.Zoom);
                decimal shiftX = ((tile.Bounds.X + tile.Bounds.Width / 2m) - totalWidth / 2m) / totalWidth;
                decimal shiftY = ((tile.Bounds.Y + tile.Bounds.Height / 2m) - totalHeight / 2m) / totalWidth;

                engine.CenterX = s.CenterX + shiftX * fullScale;
                engine.CenterY = s.CenterY - shiftY * fullScale;
                engine.Scale = fullScale * ((decimal)w / Math.Max(1, totalWidth));

                engine.RenderToBuffer(pixels, w, h, w * 4, 4, CancellationToken.None);
            });

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
