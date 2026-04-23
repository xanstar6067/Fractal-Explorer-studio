using FractalDraving;
using FractalExplorer.Engines;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Globalization;
using System.Text.Json;

namespace FractalExplorer.Projects
{
    /// <summary>
    /// Представляет форму для отображения и взаимодействия с фракталом Симоноброт.
    /// Эта форма настраивает пользовательский интерфейс для параметров, специфичных для Simonobrot,
    /// таких как 'Степень' и 'Инверсия', и управляет сохранением/загрузкой состояния фрактала.
    /// Итерационная формула: z -> (z^p * |z|^p) + c.
    /// </summary>
    public partial class FractalSimonobrot : FractalMandelbrotFamilyForm
    {
        /// <summary>
        /// Поле для ввода числового значения степени 'p'.
        /// </summary>
        private NumericUpDown nudPower;

        /// <summary>
        /// Метка для поля ввода степени 'p'.
        /// </summary>
        private Label lblPower;

        /// <summary>
        /// Флажок для включения/отключения инверсии по горизонтали.
        /// </summary>
        private CheckBox chkInversion;

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="FractalSimonobrot"/>.
        /// </summary>
        public FractalSimonobrot()
        {
            Text = "Фрактал Симоноброт";
        }

        /// <inheritdoc />
        protected override FractalMandelbrotFamilyEngine CreateEngine()
        {
            return new SimonobrotEngine();
        }

        /// <inheritdoc />
        protected override void OnPostInitialize()
        {
            lblRe.Visible = false;
            nudRe.Visible = false;
            lblIm.Visible = false;
            nudIm.Visible = false;
            mandelbrotPreviewPanel.Visible = false;

            pnlCustomControls.Visible = true;

            var innerTable = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 2,
                Dock = DockStyle.Fill,
            };
            innerTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            innerTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            innerTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 29F));
            innerTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 29F));

            lblPower = new Label { Text = "Степень (p)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            nudPower = new NumericUpDown
            {
                Minimum = -10,
                Maximum = 10,
                DecimalPlaces = 2,
                Increment = 0.1m,
                Value = 2.0m,
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 3, 3, 3)
            };
            nudPower.ValueChanged += ParamControl_Changed;

            chkInversion = new CheckBox
            {
                Text = "Инверсия",
                Dock = DockStyle.Fill,
                Checked = false
            };
            chkInversion.CheckedChanged += ParamControl_Changed;

            innerTable.Controls.Add(nudPower, 0, 0);
            innerTable.Controls.Add(lblPower, 1, 0);
            innerTable.Controls.Add(chkInversion, 0, 1);
            innerTable.SetColumnSpan(chkInversion, 2);

            pnlCustomControls.Height = 60;
            pnlCustomControls.Controls.Add(innerTable);
        }

        /// <inheritdoc />
        protected override void UpdateEngineSpecificParameters()
        {
            if (_fractalEngine is SimonobrotEngine engine)
            {
                engine.Power = nudPower.Value;
                engine.UseInversion = chkInversion.Checked;
            }
        }

        /// <inheritdoc />
        protected override string GetSaveFileNameDetails()
        {
            string powerString = nudPower.Value.ToString("F2", CultureInfo.InvariantCulture).Replace(".", "_");
            string inversionString = (_fractalEngine as SimonobrotEngine)?.UseInversion == true ? "_inv" : "";
            return $"simonobrot_p{powerString}{inversionString}";
        }

        #region ISaveLoadCapableFractal Implementation

        /// <inheritdoc />
        public override string FractalTypeIdentifier => "Simonobrot";

        /// <inheritdoc />
        public override Type ConcreteSaveStateType => typeof(GeneralizedMandelbrotSaveState);

        /// <summary>
        /// Содержит параметры, необходимые для рендеринга предпросмотра фрактала Симоноброт.
        /// </summary>
        public class SimonobrotPreviewParams : PreviewParams
        {
            /// <summary>
            /// Степень 'p' для предпросмотра.
            /// </summary>
            public decimal Power { get; set; }
            /// <summary>
            /// Флаг инверсии для предпросмотра.
            /// </summary>
            public bool UseInversion { get; set; }
        }

        /// <inheritdoc />
        public override FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            var state = new GeneralizedMandelbrotSaveState(this.FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                Threshold = nudThreshold.Value,
                Iterations = (int)nudIterations.Value,
                PaletteName = _paletteManager.ActivePalette?.Name ?? "Стандартный серый",
                PreviewEngineType = this.FractalTypeIdentifier,
                Power = nudPower.Value,
                UseInversion = chkInversion.Checked
            };

            var previewParams = new SimonobrotPreviewParams
            {
                CenterX = state.CenterX,
                CenterY = state.CenterY,
                Zoom = state.Zoom,
                Iterations = Math.Min(state.Iterations, 1000),
                PaletteName = state.PaletteName,
                Threshold = state.Threshold,
                PreviewEngineType = state.PreviewEngineType,
                Power = state.Power,
                UseInversion = state.UseInversion
            };
            state.PreviewParametersJson = JsonSerializer.Serialize(previewParams, new JsonSerializerOptions());
            return state;
        }

        /// <inheritdoc />
        public override void LoadState(FractalSaveStateBase stateBase)
        {
            base.LoadState(stateBase);
            if (stateBase is GeneralizedMandelbrotSaveState state)
            {
                nudPower.Value = Math.Max(nudPower.Minimum, Math.Min(nudPower.Maximum, state.Power));
                chkInversion.Checked = state.UseInversion;
            }
        }

        /// <inheritdoc />
        public override List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            var specificSaves = SaveFileManager.LoadSaves<GeneralizedMandelbrotSaveState>(this.FractalTypeIdentifier);
            return specificSaves.Cast<FractalSaveStateBase>().ToList();
        }

        /// <inheritdoc />
        public override void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            var specificSaves = saves.Cast<GeneralizedMandelbrotSaveState>().ToList();
            SaveFileManager.SaveSaves(this.FractalTypeIdentifier, specificSaves);
        }

        /// <inheritdoc />
        public override Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase stateBase, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(stateBase.PreviewParametersJson))
                    return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];

                SimonobrotPreviewParams previewParams;
                try
                {
                    previewParams = JsonSerializer.Deserialize<SimonobrotPreviewParams>(stateBase.PreviewParametersJson);
                }
                catch { return new byte[tile.Bounds.Width * tile.Bounds.Height * 4]; }

                var previewEngine = new SimonobrotEngine
                {
                    Power = previewParams.Power,
                    UseInversion = previewParams.UseInversion
                };

                previewEngine.MaxIterations = previewParams.Iterations;
                previewEngine.CenterX = previewParams.CenterX;
                previewEngine.CenterY = previewParams.CenterY;
                if (previewParams.Zoom == 0) previewParams.Zoom = 0.001m;
                previewEngine.Scale = BaseScale / previewParams.Zoom;
                previewEngine.ThresholdSquared = previewParams.Threshold * previewParams.Threshold;

                var paletteManager = new PaletteManager();
                var paletteForPreview = paletteManager.Palettes.FirstOrDefault(p => p.Name == previewParams.PaletteName) ?? paletteManager.Palettes.First();
                int effectiveMaxColorIterations = paletteForPreview.AlignWithRenderIterations ? previewEngine.MaxIterations : paletteForPreview.MaxColorIterations;

                previewEngine.UseSmoothColoring = false;
                previewEngine.MaxColorIterations = effectiveMaxColorIterations;
                previewEngine.Palette = GenerateDiscretePaletteFunction(paletteForPreview);
                previewEngine.SmoothPalette = GenerateSmoothPaletteFunction(paletteForPreview, effectiveMaxColorIterations);

                return previewEngine.RenderSingleTile(tile, totalWidth, totalHeight, out _);
            });
        }

        /// <inheritdoc />
        public override HighResRenderState GetRenderState()
        {
            var state = base.GetRenderState();
            state.Power = nudPower.Value;
            if (_fractalEngine is SimonobrotEngine engine)
            {
                state.UseInversion = engine.UseInversion;
            }
            return state;
        }

        #endregion
    }
}