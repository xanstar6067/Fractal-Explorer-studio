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
        private readonly Panel _canvasHost = new() { Dock = DockStyle.Fill };
        private readonly Panel _controlsHost = new()
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.FixedSingle,
            Width = 231
        };
        private readonly TableLayoutPanel _pnlControls = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 14
        };
        private readonly Button _btnToggleControls = new()
        {
            AutoSize = true,
            BackColor = Color.FromArgb(235, 32, 32, 32),
            FlatStyle = FlatStyle.Popup,
            ForeColor = Color.White,
            Size = new Size(44, 32),
            Text = "✕"
        };
        private readonly NumericUpDown _nudAMin = new();
        private readonly NumericUpDown _nudAMax = new();
        private readonly NumericUpDown _nudBMin = new();
        private readonly NumericUpDown _nudBMax = new();
        private readonly NumericUpDown _nudIterations = new();
        private readonly NumericUpDown _nudTransient = new();
        private readonly NumericUpDown _nudThreads = new();
        private readonly TextBox _tbPattern = new();
        private readonly Button _btnSaveImage = new() { Text = "Сохранить изображение" };
        private readonly Button _btnPalette = new() { Text = "Настроить палитру" };
        private readonly Button _btnRender = new() { Text = "Запустить рендер" };
        private readonly Button _btnPresets = new() { Text = "Пресеты" };
        private readonly Button _btnState = new() { Text = "Менеджер сохранений" };
        private int _controlsOpenWidth = 231;

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
            _canvasHost.Controls.Add(_controlsHost);
            _canvasHost.Controls.Add(_btnToggleControls);
            _canvasHost.Controls.Add(_canvas);
            Controls.Add(_canvasHost);

            _controlsHost.Controls.Add(_pnlControls);

            _pnlControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            _pnlControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            _pnlControls.RowStyles.Add(new RowStyle()); // A min
            _pnlControls.RowStyles.Add(new RowStyle()); // A max
            _pnlControls.RowStyles.Add(new RowStyle()); // B min
            _pnlControls.RowStyles.Add(new RowStyle()); // B max
            _pnlControls.RowStyles.Add(new RowStyle()); // Pattern
            _pnlControls.RowStyles.Add(new RowStyle()); // Iterations
            _pnlControls.RowStyles.Add(new RowStyle()); // Transient
            _pnlControls.RowStyles.Add(new RowStyle()); // Threads
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f)); // Save image
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f)); // Palette
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f)); // Render
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f)); // Presets
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f)); // State
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            AddLabeledControl(_pnlControls, 0, "A min", _nudAMin);
            AddLabeledControl(_pnlControls, 1, "A max", _nudAMax);
            AddLabeledControl(_pnlControls, 2, "B min", _nudBMin);
            AddLabeledControl(_pnlControls, 3, "B max", _nudBMax);
            AddLabeledControl(_pnlControls, 4, "Pattern (A/B)", _tbPattern);
            AddLabeledControl(_pnlControls, 5, "Iterations", _nudIterations);
            AddLabeledControl(_pnlControls, 6, "Transient", _nudTransient);
            AddLabeledControl(_pnlControls, 7, "Threads", _nudThreads);

            AddMainButton(_btnSaveImage, 8);
            AddMainButton(_btnPalette, 9);
            AddMainButton(_btnRender, 10);
            AddMainButton(_btnPresets, 11);
            AddMainButton(_btnState, 12);

            _controlsHost.Location = new Point(0, 0);
            _controlsHost.Height = ClientSize.Height;
            _btnToggleControls.Location = new Point(_controlsHost.Width + 25, 12);
            _btnToggleControls.Click += (_, __) => ToggleControls();
            Resize += (_, __) => UpdateOverlayLayout();

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
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(6, 3, 3, 3);
            panel.Controls.Add(control, 0, row);
            panel.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, AutoSize = true }, 1, row);
        }

        private void AddMainButton(Button button, int row)
        {
            _pnlControls.SetColumnSpan(button, 2);
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(6, 3, 6, 3);
            _pnlControls.Controls.Add(button, 0, row);
        }

        private void ToggleControls()
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

        private void UpdateOverlayLayout()
        {
            _controlsHost.Height = ClientSize.Height;
            if (_controlsHost.Visible && _controlsHost.Width > 0)
            {
                _btnToggleControls.Location = new Point(_controlsHost.Width + 25, 12);
            }
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
