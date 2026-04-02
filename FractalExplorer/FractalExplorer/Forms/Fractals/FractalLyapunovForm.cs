using System.Text.Json;
using FractalExplorer.Engines;
using FractalExplorer.Resources;
using FractalExplorer.Utilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;

namespace FractalExplorer.Forms.Fractals
{
    public class FractalLyapunovForm : Form, ISaveLoadCapableFractal
    {
        private readonly FractalLyapunovEngine _engine = new();
        private readonly PictureBox _canvas = new() { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
        private readonly NumericUpDown _nudAMin = new();
        private readonly NumericUpDown _nudAMax = new();
        private readonly NumericUpDown _nudBMin = new();
        private readonly NumericUpDown _nudBMax = new();
        private readonly NumericUpDown _nudIterations = new();
        private readonly NumericUpDown _nudTransient = new();
        private readonly NumericUpDown _nudThreads = new();
        private readonly TextBox _tbPattern = new();
        private readonly Button _btnRender = new() { Text = "Рендер" };
        private readonly Button _btnPresets = new() { Text = "Пресеты" };
        private readonly Button _btnState = new() { Text = "Сохранения" };

        public FractalLyapunovForm()
        {
            Text = "Lyapunov Fractal";
            Width = 1280;
            Height = 780;
            StartPosition = FormStartPosition.CenterScreen;
            InitializeUi();
            ApplyDefaults();
            Shown += async (_, __) => await RenderAsync();
        }

        private void InitializeUi()
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel2,
                SplitterDistance = 970,
                IsSplitterFixed = false
            };

            split.Panel1.Controls.Add(_canvas);

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 11,
                Padding = new Padding(8)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
            for (int i = 0; i < panel.RowCount; i++) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

            AddLabeledControl(panel, 0, "A min", _nudAMin);
            AddLabeledControl(panel, 1, "A max", _nudAMax);
            AddLabeledControl(panel, 2, "B min", _nudBMin);
            AddLabeledControl(panel, 3, "B max", _nudBMax);
            AddLabeledControl(panel, 4, "Pattern (A/B)", _tbPattern);
            AddLabeledControl(panel, 5, "Iterations", _nudIterations);
            AddLabeledControl(panel, 6, "Transient", _nudTransient);
            AddLabeledControl(panel, 7, "Threads", _nudThreads);

            panel.Controls.Add(_btnRender, 0, 8);
            panel.SetColumnSpan(_btnRender, 2);
            panel.Controls.Add(_btnPresets, 0, 9);
            panel.SetColumnSpan(_btnPresets, 2);
            panel.Controls.Add(_btnState, 0, 10);
            panel.SetColumnSpan(_btnState, 2);

            split.Panel2.Controls.Add(panel);
            Controls.Add(split);

            _btnRender.Click += async (_, __) => await RenderAsync();
            _btnPresets.Click += (_, __) => ApplyPreset();
            _btnState.Click += (_, __) =>
            {
                using var dialog = new SaveLoadDialogForm(this);
                dialog.ShowDialog(this);
            };
        }

        private static void AddLabeledControl(TableLayoutPanel panel, int row, string label, Control control)
        {
            panel.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
            control.Dock = DockStyle.Fill;
            panel.Controls.Add(control, 1, row);
        }

        private void ApplyDefaults()
        {
            ConfigureDecimal(_nudAMin, 2, 0.01m, 0m, 4m, 2.8m);
            ConfigureDecimal(_nudAMax, 2, 0.01m, 0m, 4m, 4.0m);
            ConfigureDecimal(_nudBMin, 2, 0.01m, 0m, 4m, 2.8m);
            ConfigureDecimal(_nudBMax, 2, 0.01m, 0m, 4m, 4.0m);

            _nudIterations.Minimum = 20;
            _nudIterations.Maximum = 5000;
            _nudIterations.Value = 300;

            _nudTransient.Minimum = 0;
            _nudTransient.Maximum = 3000;
            _nudTransient.Value = 100;

            _nudThreads.Minimum = 1;
            _nudThreads.Maximum = Environment.ProcessorCount;
            _nudThreads.Value = Math.Max(1, Environment.ProcessorCount / 2);

            _tbPattern.Text = "AB";
        }

        private static void ConfigureDecimal(NumericUpDown nud, int decimals, decimal increment, decimal min, decimal max, decimal value)
        {
            nud.DecimalPlaces = decimals;
            nud.Increment = increment;
            nud.Minimum = min;
            nud.Maximum = max;
            nud.Value = value;
        }

        private void SyncEngine()
        {
            _engine.AMin = _nudAMin.Value;
            _engine.AMax = _nudAMax.Value;
            _engine.BMin = _nudBMin.Value;
            _engine.BMax = _nudBMax.Value;
            _engine.Pattern = _tbPattern.Text;
            _engine.Iterations = (int)_nudIterations.Value;
            _engine.TransientIterations = (int)_nudTransient.Value;
        }

        private async Task RenderAsync()
        {
            if (_canvas.Width <= 1 || _canvas.Height <= 1)
            {
                return;
            }

            SyncEngine();
            Cursor = Cursors.WaitCursor;
            try
            {
                int threads = (int)_nudThreads.Value;
                Bitmap bmp = await Task.Run(() => _engine.RenderToBitmap(_canvas.Width, _canvas.Height, threads));
                var prev = _canvas.Image;
                _canvas.Image = bmp;
                prev?.Dispose();
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void ApplyPreset()
        {
            List<FractalSaveStateBase> presets = PresetManager.GetPresetsFor(FractalTypeIdentifier);
            if (presets.Count == 0)
            {
                MessageBox.Show("Пресеты не найдены.");
                return;
            }

            if (presets[0] is LyapunovSaveState state)
            {
                LoadState(state);
            }
        }

        public string FractalTypeIdentifier => "Lyapunov";
        public Type ConcreteSaveStateType => typeof(LyapunovSaveState);

        public class LyapunovPreviewParams
        {
            public decimal AMin { get; set; }
            public decimal AMax { get; set; }
            public decimal BMin { get; set; }
            public decimal BMax { get; set; }
            public string Pattern { get; set; } = "AB";
            public int Iterations { get; set; }
            public int TransientIterations { get; set; }
        }

        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new LyapunovSaveState(FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                AMin = _nudAMin.Value,
                AMax = _nudAMax.Value,
                BMin = _nudBMin.Value,
                BMax = _nudBMax.Value,
                Pattern = _tbPattern.Text,
                Iterations = (int)_nudIterations.Value,
                TransientIterations = (int)_nudTransient.Value
            };

            state.PreviewParametersJson = JsonSerializer.Serialize(new LyapunovPreviewParams
            {
                AMin = state.AMin,
                AMax = state.AMax,
                BMin = state.BMin,
                BMax = state.BMax,
                Pattern = state.Pattern,
                Iterations = state.Iterations,
                TransientIterations = state.TransientIterations
            });

            return state;
        }

        public void LoadState(FractalSaveStateBase state)
        {
            if (state is not LyapunovSaveState lyapunov)
            {
                return;
            }

            _nudAMin.Value = lyapunov.AMin;
            _nudAMax.Value = lyapunov.AMax;
            _nudBMin.Value = lyapunov.BMin;
            _nudBMax.Value = lyapunov.BMax;
            _tbPattern.Text = lyapunov.Pattern;
            _nudIterations.Value = Math.Max(_nudIterations.Minimum, Math.Min(_nudIterations.Maximum, lyapunov.Iterations));
            _nudTransient.Value = Math.Max(_nudTransient.Minimum, Math.Min(_nudTransient.Maximum, lyapunov.TransientIterations));
            _ = RenderAsync();
        }

        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (state is not LyapunovSaveState lyapunov)
            {
                return new Bitmap(previewWidth, previewHeight);
            }

            var engine = new FractalLyapunovEngine
            {
                AMin = lyapunov.AMin,
                AMax = lyapunov.AMax,
                BMin = lyapunov.BMin,
                BMax = lyapunov.BMax,
                Pattern = lyapunov.Pattern,
                Iterations = lyapunov.Iterations,
                TransientIterations = lyapunov.TransientIterations
            };

            return engine.RenderToBitmap(previewWidth, previewHeight, 1);
        }

        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            return await Task.Run(() =>
            {
                if (state is not LyapunovSaveState lyapunov)
                {
                    return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];
                }

                var engine = new FractalLyapunovEngine
                {
                    AMin = lyapunov.AMin,
                    AMax = lyapunov.AMax,
                    BMin = lyapunov.BMin,
                    BMax = lyapunov.BMax,
                    Pattern = lyapunov.Pattern,
                    Iterations = lyapunov.Iterations,
                    TransientIterations = lyapunov.TransientIterations
                };
                return engine.RenderSingleTile(tile, totalWidth, totalHeight, out _);
            });
        }

        public List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            return SaveFileManager.LoadSaves<LyapunovSaveState>(FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();
        }

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            SaveFileManager.SaveSaves(FractalTypeIdentifier, saves.Cast<LyapunovSaveState>().ToList());
        }
    }
}
