using FractalDraving;
using FractalExplorer.Utilities.ColorPicking;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.Theme;

namespace FractalExplorer.Utilities
{
    /// <summary>
    /// Окно runtime-настроек режима окрашивания и быстрой смены палитры.
    /// Не управляет CRUD палитр, а только применяет выбранные параметры.
    /// </summary>
    public sealed class ColoringModeSettingsForm : Form
    {
        private readonly PaletteManager _paletteManager;
        private readonly FractalMandelbrotFamilyForm.ColoringRuntimeState _workingState;
        private readonly ColorSelectionService _colorSelectionService = ColorSelectionService.Default;

        // ── Выбор режима ────────────────────────────────────────────────────
        private readonly FlowLayoutPanel _pnlModeChips = new();
        private readonly List<Button> _modeChips = new();

        // ── Вкладки ─────────────────────────────────────────────────────────
        private readonly TabControl _tabs = new();
        private readonly TabPage _tabParams = new();
        private readonly TabPage _tabPalette = new();
        private readonly TabPage _tabInterior = new();

        // ── Вкладка «Параметры» ─────────────────────────────────────────────
        private readonly Panel _modeParamsPanel = new();

        // Поля режима Smooth
        private NumericUpDown? _nudBlendPower;
        private NumericUpDown? _nudIterationOffset;

        // Поля режима Histogram
        private CheckBox? _chkHistogramEqualization;
        private NumericUpDown? _nudHistogramContrast;
        private CheckBox? _chkHistogramInputUseSmooth;

        // Поля режима OrbitTrap
        private NumericUpDown? _nudOrbitTrapStrength;
        private NumericUpDown? _nudOrbitTrapBias;

        // Поля режима StripeAverage
        private NumericUpDown? _nudStripeFrequency;
        private NumericUpDown? _nudStripeStrength;
        private NumericUpDown? _nudStripeBias;

        // ── Вкладка «Палитра» ────────────────────────────────────────────────
        private readonly ComboBox _cbPalette = new();
        private NumericUpDown? _nudPalettePhaseOffset;
        private NumericUpDown? _nudPaletteScale;
        private ComboBox? _cbPaletteWrapMode;

        // ── Вкладка «Внутренность» ───────────────────────────────────────────
        private readonly ComboBox _cbInteriorMode = new();
        private readonly Button _btnPickInteriorColor = new();
        private readonly Panel _pnlInteriorColorPreview = new();

        // ── Кнопки ──────────────────────────────────────────────────────────
        private readonly Button _btnApply = new();
        private readonly Button _btnCancel = new();

        public event EventHandler<ColoringModeSettingsAppliedEventArgs>? SettingsApplied;

        public ColoringModeSettingsForm(
            PaletteManager paletteManager,
            FractalMandelbrotFamilyForm.ColoringRuntimeState runtimeState,
            string? activePaletteName,
            Icon? ownerIcon)
        {
            _paletteManager = paletteManager ?? throw new ArgumentNullException(nameof(paletteManager));
            _workingState = runtimeState?.Clone() ?? throw new ArgumentNullException(nameof(runtimeState));
            ThemeManager.RegisterForm(this);

            InitializeUi();
            PopulateModeChips();
            PopulatePalettesTab(activePaletteName);
            PopulateInteriorTab();
            ApplyStateToUi();

            if (ownerIcon is not null)
                Icon = ownerIcon;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Построение UI
        // ────────────────────────────────────────────────────────────────────

        private void InitializeUi()
        {
            Text = "Настройки окраски";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = true;
            MinimumSize = new Size(480, 380);
            Size = new Size(520, 440);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // строка чипов режима
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // вкладки
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // кнопки

            // ── Блок выбора режима ───────────────────────────────────────────
            var modeGroup = new GroupBox
            {
                Text = "Режим окраски",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(6, 4, 6, 6),
                Margin = new Padding(0, 0, 0, 8)
            };

            _pnlModeChips.Dock = DockStyle.Fill;
            _pnlModeChips.FlowDirection = FlowDirection.LeftToRight;
            _pnlModeChips.WrapContents = true;
            _pnlModeChips.AutoSize = true;
            _pnlModeChips.Padding = new Padding(0, 2, 0, 0);
            modeGroup.Controls.Add(_pnlModeChips);

            // ── Вкладки ─────────────────────────────────────────────────────
            _tabs.Dock = DockStyle.Fill;
            _tabs.Padding = new Point(10, 4);
            _tabs.Margin = new Padding(0);

            _tabParams.Text = "Параметры";
            _tabPalette.Text = "Палитра";
            _tabInterior.Text = "Внутренность";

            _tabs.TabPages.Add(_tabParams);
            _tabs.TabPages.Add(_tabPalette);
            _tabs.TabPages.Add(_tabInterior);

            // Вкладка «Параметры»
            _modeParamsPanel.Dock = DockStyle.Fill;
            _modeParamsPanel.AutoScroll = true;
            _modeParamsPanel.Padding = new Padding(8);
            _tabParams.Controls.Add(_modeParamsPanel);
            _tabParams.Padding = new Padding(4);

            // Вкладка «Палитра» — заполним в PopulatePalettesTab()
            _tabPalette.Padding = new Padding(4);
            _tabPalette.AutoScroll = true;

            // Вкладка «Внутренность» — заполним в PopulateInteriorTab()
            _tabInterior.Padding = new Padding(4);
            _tabInterior.AutoScroll = true;

            // ── Кнопки ──────────────────────────────────────────────────────
            var buttonsRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };

            _btnApply.Text = "Применить";
            _btnApply.AutoSize = true;
            _btnApply.MinimumSize = new Size(90, 28);
            _btnApply.Click += BtnApply_Click;

            _btnCancel.Text = "Отмена";
            _btnCancel.AutoSize = true;
            _btnCancel.MinimumSize = new Size(80, 28);
            _btnCancel.Click += (_, _) => Close();

            buttonsRow.Controls.Add(_btnApply);
            buttonsRow.Controls.Add(_btnCancel);

            root.Controls.Add(modeGroup, 0, 0);
            root.Controls.Add(_tabs, 0, 1);
            root.Controls.Add(buttonsRow, 0, 2);
            Controls.Add(root);
        }

        // ── Чипы режима ──────────────────────────────────────────────────────

        private static readonly (FractalMandelbrotFamilyForm.ColoringModeType Mode, string Label)[] ModeDefinitions =
        {
            (FractalMandelbrotFamilyForm.ColoringModeType.Discrete,     "Дискретно"),
            (FractalMandelbrotFamilyForm.ColoringModeType.Smooth,       "Плавно"),
            (FractalMandelbrotFamilyForm.ColoringModeType.Histogram,    "Гистограмма"),
            (FractalMandelbrotFamilyForm.ColoringModeType.OrbitTrap,    "Орбитальная ловушка"),
            (FractalMandelbrotFamilyForm.ColoringModeType.StripeAverage,"Полосатое усреднение"),
        };

        private void PopulateModeChips()
        {
            _pnlModeChips.Controls.Clear();
            _modeChips.Clear();

            foreach (var (mode, label) in ModeDefinitions)
            {
                var btn = new Button
                {
                    Text = label,
                    AutoSize = true,
                    MinimumSize = new Size(0, 28),
                    Margin = new Padding(0, 0, 6, 4),
                    Tag = mode,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 1;
                btn.Click += ModeChip_Click;
                _modeChips.Add(btn);
                _pnlModeChips.Controls.Add(btn);
            }
        }

        private void ModeChip_Click(object? sender, EventArgs e)
        {
            if (sender is not Button btn) return;
            SelectModeChip((FractalMandelbrotFamilyForm.ColoringModeType)btn.Tag!);
            RebuildModeParamsPanel();
        }

        private void SelectModeChip(FractalMandelbrotFamilyForm.ColoringModeType mode)
        {
            foreach (var chip in _modeChips)
            {
                bool active = (FractalMandelbrotFamilyForm.ColoringModeType)chip.Tag! == mode;
                // Визуальный акцент: системные цвета, работают и со светлой и с тёмной темой
                chip.FlatAppearance.BorderColor = active ? SystemColors.Highlight : SystemColors.ControlDark;
                chip.Font = new Font(chip.Font, active ? FontStyle.Bold : FontStyle.Regular);
            }
        }

        private FractalMandelbrotFamilyForm.ColoringModeType SelectedMode()
        {
            foreach (var chip in _modeChips)
                if (chip.Font.Bold)
                    return (FractalMandelbrotFamilyForm.ColoringModeType)chip.Tag!;
            return FractalMandelbrotFamilyForm.ColoringModeType.Smooth;
        }

        // ── Вкладка «Параметры» (динамические поля) ──────────────────────────

        private void RebuildModeParamsPanel()
        {
            _modeParamsPanel.Controls.Clear();
            _nudBlendPower = null;
            _nudIterationOffset = null;
            _chkHistogramEqualization = null;
            _nudHistogramContrast = null;
            _chkHistogramInputUseSmooth = null;
            _nudOrbitTrapStrength = null;
            _nudOrbitTrapBias = null;
            _nudStripeFrequency = null;
            _nudStripeStrength = null;
            _nudStripeBias = null;

            switch (SelectedMode())
            {
                case FractalMandelbrotFamilyForm.ColoringModeType.Smooth:
                    BuildSmoothParams();
                    break;
                case FractalMandelbrotFamilyForm.ColoringModeType.Histogram:
                    BuildHistogramParams();
                    break;
                case FractalMandelbrotFamilyForm.ColoringModeType.OrbitTrap:
                    BuildOrbitTrapParams();
                    break;
                case FractalMandelbrotFamilyForm.ColoringModeType.StripeAverage:
                    BuildStripeAverageParams();
                    break;
                default:
                    _modeParamsPanel.Controls.Add(new Label
                    {
                        Dock = DockStyle.Top,
                        AutoSize = true,
                        Text = "У выбранного режима нет дополнительных параметров.",
                        Margin = new Padding(0, 8, 0, 0)
                    });
                    break;
            }
        }

        private void BuildSmoothParams()
        {
            var layout = MakeTwoColumnLayout();

            AddRow(layout, "Плавность (степень):", 0);
            _nudBlendPower = MakeNud(0.10m, 5.00m, 0.05m, 2, _workingState.SmoothBlendPower, 0.10, 5.00);
            layout.Controls.Add(_nudBlendPower, 1, 0);

            AddRow(layout, "Смещение итерации:", 1);
            _nudIterationOffset = MakeNud(-100m, 100m, 0.10m, 2, _workingState.SmoothIterationOffset, -100.0, 100.0);
            layout.Controls.Add(_nudIterationOffset, 1, 1);

            _modeParamsPanel.Controls.Add(layout);
        }

        private void BuildHistogramParams()
        {
            var layout = MakeTwoColumnLayout();

            _chkHistogramEqualization = new CheckBox
            {
                Text = "Выравнивание (CDF)",
                AutoSize = true,
                Checked = _workingState.HistogramSettings.EnabledEqualization,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 0, 6)
            };
            layout.Controls.Add(_chkHistogramEqualization, 0, 0);
            layout.SetColumnSpan(_chkHistogramEqualization, 2);

            AddRow(layout, "Контраст:", 1);
            _nudHistogramContrast = MakeNud(0.10m, 4.00m, 0.05m, 2, _workingState.HistogramSettings.Contrast, 0.10, 4.00);
            layout.Controls.Add(_nudHistogramContrast, 1, 1);

            _chkHistogramInputUseSmooth = new CheckBox
            {
                Text = "Плавные итерации как вход",
                AutoSize = true,
                Checked = _workingState.HistogramSettings.InputUseSmooth,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 0, 0)
            };
            layout.Controls.Add(_chkHistogramInputUseSmooth, 0, 2);
            layout.SetColumnSpan(_chkHistogramInputUseSmooth, 2);

            _modeParamsPanel.Controls.Add(layout);
        }

        private void BuildOrbitTrapParams()
        {
            var layout = MakeTwoColumnLayout();

            AddRow(layout, "Сила ловушки:", 0);
            _nudOrbitTrapStrength = MakeNud(0m, 5m, 0.05m, 2, _workingState.OrbitTrapSettings.Strength, 0.0, 5.0);
            layout.Controls.Add(_nudOrbitTrapStrength, 1, 0);

            AddRow(layout, "Смещение:", 1);
            _nudOrbitTrapBias = MakeNud(-1m, 1m, 0.01m, 2, _workingState.OrbitTrapSettings.Bias, -1.0, 1.0);
            layout.Controls.Add(_nudOrbitTrapBias, 1, 1);

            _modeParamsPanel.Controls.Add(layout);
        }

        private void BuildStripeAverageParams()
        {
            var layout = MakeTwoColumnLayout();

            AddRow(layout, "Частота:", 0);
            _nudStripeFrequency = MakeNud(0.10m, 20m, 0.10m, 2, _workingState.StripeAverageSettings.Frequency, 0.10, 20.0);
            layout.Controls.Add(_nudStripeFrequency, 1, 0);

            AddRow(layout, "Сила:", 1);
            _nudStripeStrength = MakeNud(0m, 1m, 0.01m, 2, _workingState.StripeAverageSettings.Strength, 0.0, 1.0);
            layout.Controls.Add(_nudStripeStrength, 1, 1);

            AddRow(layout, "Смещение:", 2);
            _nudStripeBias = MakeNud(-1m, 1m, 0.01m, 2, _workingState.StripeAverageSettings.Bias, -1.0, 1.0);
            layout.Controls.Add(_nudStripeBias, 1, 2);

            _modeParamsPanel.Controls.Add(layout);
        }

        // ── Вкладка «Палитра» ────────────────────────────────────────────────

        private void PopulatePalettesTab(string? activePaletteName)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), AutoScroll = true };
            var layout = MakeTwoColumnLayout(rowCount: 5);

            // Палитра
            AddRow(layout, "Палитра:", 0);
            _cbPalette.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbPalette.Dock = DockStyle.Fill;
            foreach (var palette in _paletteManager.Palettes)
            {
                string label = palette.IsBuiltIn
                    ? $"{palette.Name} [встроенная]"
                    : $"{palette.Name} [пользовательская]";
                _cbPalette.Items.Add(new PaletteItem(palette.Name, label));
            }
            if (_cbPalette.Items.Count > 0)
            {
                PaletteItem? selected = null;
                if (!string.IsNullOrWhiteSpace(activePaletteName))
                    selected = _cbPalette.Items.OfType<PaletteItem>().FirstOrDefault(p => p.PaletteName == activePaletteName);
                _cbPalette.SelectedItem = selected ?? _cbPalette.Items[0];
            }
            layout.Controls.Add(_cbPalette, 1, 0);

            // Разделитель
            var sep = new Label
            {
                Text = "Преобразование палитры",
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 12, 0, 4)
            };
            layout.Controls.Add(sep, 0, 1);
            layout.SetColumnSpan(sep, 2);

            // Phase offset
            AddRow(layout, "Сдвиг фазы:", 2);
            _nudPalettePhaseOffset = MakeNud(-2m, 2m, 0.010m, 3, _workingState.PaletteTransform.PhaseOffset, -2.0, 2.0);
            layout.Controls.Add(_nudPalettePhaseOffset, 1, 2);

            // Scale
            AddRow(layout, "Масштаб:", 3);
            _nudPaletteScale = MakeNud(-5m, 5m, 0.010m, 3, _workingState.PaletteTransform.Scale, -5.0, 5.0);
            layout.Controls.Add(_nudPaletteScale, 1, 3);

            // Wrap mode
            AddRow(layout, "Режим повтора:", 4);
            _cbPaletteWrapMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cbPaletteWrapMode.Items.Add(new PaletteWrapModeItem(FractalMandelbrotFamilyForm.PaletteTransformWrapMode.Repeat, "Повтор"));
            _cbPaletteWrapMode.Items.Add(new PaletteWrapModeItem(FractalMandelbrotFamilyForm.PaletteTransformWrapMode.Clamp, "Ограничение"));
            _cbPaletteWrapMode.Items.Add(new PaletteWrapModeItem(FractalMandelbrotFamilyForm.PaletteTransformWrapMode.Mirror, "Зеркало"));
            _cbPaletteWrapMode.SelectedItem = _cbPaletteWrapMode.Items
                .OfType<PaletteWrapModeItem>()
                .FirstOrDefault(x => x.Mode == _workingState.PaletteTransform.WrapMode)
                ?? _cbPaletteWrapMode.Items[0];
            layout.Controls.Add(_cbPaletteWrapMode, 1, 4);

            panel.Controls.Add(layout);
            _tabPalette.Controls.Add(panel);
        }

        // ── Вкладка «Внутренность» ────────────────────────────────────────────

        private void PopulateInteriorTab()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), AutoScroll = true };
            var layout = MakeTwoColumnLayout(rowCount: 2);

            // Режим
            AddRow(layout, "Режим:", 0);
            _cbInteriorMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbInteriorMode.Dock = DockStyle.Fill;
            _cbInteriorMode.Items.Add(new InteriorModeItem(FractalMandelbrotFamilyForm.InteriorMode.Black, "Чёрный (совместимый)"));
            _cbInteriorMode.Items.Add(new InteriorModeItem(FractalMandelbrotFamilyForm.InteriorMode.Custom, "Свой цвет"));
            _cbInteriorMode.SelectedIndexChanged += (_, _) => UpdateInteriorControlsState();
            layout.Controls.Add(_cbInteriorMode, 1, 0);

            // Цвет
            AddRow(layout, "Цвет:", 1);
            _btnPickInteriorColor.Text = "Выбрать…";
            _btnPickInteriorColor.AutoSize = true;
            _btnPickInteriorColor.Click += BtnPickInteriorColor_Click;

            _pnlInteriorColorPreview.Width = 40;
            _pnlInteriorColorPreview.Height = 24;
            _pnlInteriorColorPreview.BorderStyle = BorderStyle.FixedSingle;
            _pnlInteriorColorPreview.Margin = new Padding(6, 0, 0, 0);

            var colorRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            colorRow.Controls.Add(_btnPickInteriorColor);
            colorRow.Controls.Add(_pnlInteriorColorPreview);
            layout.Controls.Add(colorRow, 1, 1);

            panel.Controls.Add(layout);
            _tabInterior.Controls.Add(panel);
        }

        // ── Применение состояния к UI ─────────────────────────────────────────

        private void ApplyStateToUi()
        {
            // Выбрать нужный чип режима
            SelectModeChip(_workingState.ActiveMode.ModeType);

            // Interior-вкладка
            var interiorItem = _cbInteriorMode.Items
                .OfType<InteriorModeItem>()
                .FirstOrDefault(m => m.Mode == _workingState.InteriorMode);
            _cbInteriorMode.SelectedItem = interiorItem ?? _cbInteriorMode.Items.OfType<InteriorModeItem>().FirstOrDefault();
            _pnlInteriorColorPreview.BackColor = _workingState.InteriorColor;
            UpdateInteriorControlsState();

            // Построить параметры выбранного режима
            RebuildModeParamsPanel();
        }

        // ── Обработчики ───────────────────────────────────────────────────────

        private void BtnApply_Click(object? sender, EventArgs e)
        {
            var mode = SelectedMode();
            _workingState.ActiveMode = FractalMandelbrotFamilyForm.ColoringModeRuntime.FromType(mode);
            _workingState.InteriorMode = (_cbInteriorMode.SelectedItem as InteriorModeItem)?.Mode
                ?? FractalMandelbrotFamilyForm.InteriorMode.Black;
            _workingState.InteriorColor = _pnlInteriorColorPreview.BackColor;

            if (_nudBlendPower is not null)
                _workingState.SmoothBlendPower = (double)_nudBlendPower.Value;
            if (_nudIterationOffset is not null)
                _workingState.SmoothIterationOffset = (double)_nudIterationOffset.Value;
            if (_chkHistogramEqualization is not null)
                _workingState.HistogramSettings.EnabledEqualization = _chkHistogramEqualization.Checked;
            if (_nudHistogramContrast is not null)
                _workingState.HistogramSettings.Contrast = (double)_nudHistogramContrast.Value;
            if (_chkHistogramInputUseSmooth is not null)
                _workingState.HistogramSettings.InputUseSmooth = _chkHistogramInputUseSmooth.Checked;
            if (_nudOrbitTrapStrength is not null)
                _workingState.OrbitTrapSettings.Strength = (double)_nudOrbitTrapStrength.Value;
            if (_nudOrbitTrapBias is not null)
                _workingState.OrbitTrapSettings.Bias = (double)_nudOrbitTrapBias.Value;
            if (_nudStripeFrequency is not null)
                _workingState.StripeAverageSettings.Frequency = (double)_nudStripeFrequency.Value;
            if (_nudStripeStrength is not null)
                _workingState.StripeAverageSettings.Strength = (double)_nudStripeStrength.Value;
            if (_nudStripeBias is not null)
                _workingState.StripeAverageSettings.Bias = (double)_nudStripeBias.Value;
            if (_nudPalettePhaseOffset is not null)
                _workingState.PaletteTransform.PhaseOffset = (double)_nudPalettePhaseOffset.Value;
            if (_nudPaletteScale is not null)
                _workingState.PaletteTransform.Scale = (double)_nudPaletteScale.Value;
            if (_cbPaletteWrapMode is not null)
                _workingState.PaletteTransform.WrapMode = (_cbPaletteWrapMode.SelectedItem as PaletteWrapModeItem)?.Mode
                    ?? FractalMandelbrotFamilyForm.PaletteTransformWrapMode.Repeat;

            var selectedPaletteName = (_cbPalette.SelectedItem as PaletteItem)?.PaletteName;
            SettingsApplied?.Invoke(this, new ColoringModeSettingsAppliedEventArgs(
                _workingState.Clone(),
                selectedPaletteName));
        }

        private void UpdateInteriorControlsState()
        {
            bool isCustom = (_cbInteriorMode.SelectedItem as InteriorModeItem)?.Mode
                == FractalMandelbrotFamilyForm.InteriorMode.Custom;
            _btnPickInteriorColor.Enabled = isCustom;
            _pnlInteriorColorPreview.BackColor = isCustom ? _workingState.InteriorColor : Color.Black;
        }

        private void BtnPickInteriorColor_Click(object? sender, EventArgs e)
        {
            if (!_colorSelectionService.TrySelectColor(this, _workingState.InteriorColor, out var selectedColor))
                return;
            _workingState.InteriorColor = selectedColor;
            if ((_cbInteriorMode.SelectedItem as InteriorModeItem)?.Mode == FractalMandelbrotFamilyForm.InteriorMode.Custom)
                _pnlInteriorColorPreview.BackColor = selectedColor;
        }

        // ── Вспомогательные фабричные методы ─────────────────────────────────

        /// <summary>Создаёт TableLayoutPanel с двумя колонками: метка 140px + растяжение.</summary>
        private static TableLayoutPanel MakeTwoColumnLayout(int rowCount = 10)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < rowCount; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return layout;
        }

        /// <summary>Добавляет Label в указанную строку первой колонки.</summary>
        private static void AddRow(TableLayoutPanel layout, string text, int row)
        {
            layout.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, 8, 8, 0)
            }, 0, row);
        }

        /// <summary>Создаёт NumericUpDown с ограничением значения по диапазону double.</summary>
        private static NumericUpDown MakeNud(
            decimal min, decimal max, decimal increment, int decimals,
            double rawValue, double clampMin, double clampMax)
        {
            return new NumericUpDown
            {
                DecimalPlaces = decimals,
                Minimum = min,
                Maximum = max,
                Increment = increment,
                Value = (decimal)Math.Max(clampMin, Math.Min(clampMax, rawValue)),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 0)
            };
        }

        // ── Вспомогательные record-типы ───────────────────────────────────────

        private sealed record PaletteItem(string PaletteName, string DisplayName)
        {
            public override string ToString() => DisplayName;
        }

        private sealed record InteriorModeItem(FractalMandelbrotFamilyForm.InteriorMode Mode, string DisplayName)
        {
            public override string ToString() => DisplayName;
        }

        private sealed record PaletteWrapModeItem(FractalMandelbrotFamilyForm.PaletteTransformWrapMode Mode, string DisplayName)
        {
            public override string ToString() => DisplayName;
        }
    }

    public sealed class ColoringModeSettingsAppliedEventArgs : EventArgs
    {
        public ColoringModeSettingsAppliedEventArgs(
            FractalMandelbrotFamilyForm.ColoringRuntimeState runtimeState,
            string? selectedPaletteName)
        {
            RuntimeState = runtimeState;
            SelectedPaletteName = selectedPaletteName;
        }

        public FractalMandelbrotFamilyForm.ColoringRuntimeState RuntimeState { get; }
        public string? SelectedPaletteName { get; }
    }
}
