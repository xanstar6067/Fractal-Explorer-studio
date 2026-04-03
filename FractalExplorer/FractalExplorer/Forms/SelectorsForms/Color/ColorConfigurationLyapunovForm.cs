using FractalExplorer.Utilities.ColorPicking;
using FractalExplorer.Utilities.Coloring;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.Theme;

namespace FractalExplorer.Forms.SelectorsForms.Color
{
    public class ColorConfigurationLyapunovForm : Form
    {
        private readonly LyapunovPaletteManager _paletteManager;
        private readonly ColorSelectionService _colorSelectionService = ColorSelectionService.Default;

        private LyapunovColorPalette? _selectedPalette;
        private bool _isProgrammaticChange;
        private bool _hasUnsavedChanges;

        private readonly ListBox _lbPalettes = new();
        private readonly ListBox _lbColors = new();
        private readonly TextBox _txtName = new();
        private readonly ComboBox _cbMode = new();
        private readonly NumericUpDown _nudRange = new();
        private readonly NumericUpDown _nudZeroBand = new();
        private readonly Panel _panelPreview = new();
        private readonly Button _btnSave = new();
        private readonly Button _btnApply = new();
        private readonly Button _btnClose = new();
        private readonly Button _btnNew = new();
        private readonly Button _btnCopy = new();
        private readonly Button _btnDelete = new();
        private readonly Button _btnAddColor = new();
        private readonly Button _btnEditColor = new();
        private readonly Button _btnRemoveColor = new();

        public event EventHandler? PaletteApplied;

        public ColorConfigurationLyapunovForm(LyapunovPaletteManager paletteManager)
        {
            _paletteManager = paletteManager;
            InitializeUi();
            ThemeManager.RegisterForm(this);
            Load += (_, _) => PopulatePaletteList();
        }

        private void InitializeUi()
        {
            Text = "Настройка палитры (Lyapunov)";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(669, 501);
            MinimumSize = new Size(685, 540);
            MaximumSize = new Size(685, 540);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            var grpPalettes = new GroupBox { Text = "Список палитр", Location = new Point(12, 12), Size = new Size(238, 437) };
            _lbPalettes.Location = new Point(7, 22);
            _lbPalettes.Size = new Size(224, 364);
            _lbPalettes.SelectedIndexChanged += (_, _) => OnPaletteSelected();

            _btnNew.Text = "Новая";
            _btnNew.Location = new Point(7, 400);
            _btnNew.Size = new Size(61, 28);
            _btnNew.Click += (_, _) => CreateNew();

            _btnCopy.Text = "Копировать";
            _btnCopy.Location = new Point(74, 400);
            _btnCopy.Size = new Size(88, 28);
            _btnCopy.Click += (_, _) => CopySelected();

            _btnDelete.Text = "Удалить";
            _btnDelete.Location = new Point(168, 400);
            _btnDelete.Size = new Size(63, 28);
            _btnDelete.Click += (_, _) => DeleteSelected();

            grpPalettes.Controls.AddRange(new Control[] { _lbPalettes, _btnNew, _btnCopy, _btnDelete });

            var grpEditor = new GroupBox { Text = "Редактор палитры Lyapunov", Location = new Point(272, 12), Size = new Size(385, 437) };
            var lblName = new Label { Text = "Название:", Location = new Point(9, 22), AutoSize = true };
            _txtName.Location = new Point(9, 40);
            _txtName.Size = new Size(369, 23);
            _txtName.TextChanged += (_, _) => { if (!_isProgrammaticChange && IsEditable()) { _selectedPalette!.Name = _txtName.Text; MarkUnsaved(); } };

            var lblMode = new Label { Text = "Режим:", Location = new Point(9, 71), AutoSize = true };
            _cbMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbMode.Location = new Point(9, 89);
            _cbMode.Size = new Size(369, 23);
            _cbMode.Items.AddRange(Enum.GetNames(typeof(LyapunovColoringMode)));
            _cbMode.SelectedIndexChanged += (_, _) =>
            {
                if (_isProgrammaticChange || _selectedPalette == null || !IsEditable()) return;
                _selectedPalette.Mode = (LyapunovColoringMode)_cbMode.SelectedIndex;
                MarkUnsaved();
                _panelPreview.Invalidate();
            };

            var lblRange = new Label { Text = "Диапазон |λ|:", Location = new Point(9, 120), AutoSize = true };
            _nudRange.Location = new Point(9, 138);
            _nudRange.Size = new Size(178, 23);
            _nudRange.DecimalPlaces = 3;
            _nudRange.Minimum = 0.001m;
            _nudRange.Maximum = 20;
            _nudRange.Increment = 0.01m;
            _nudRange.ValueChanged += (_, _) => { if (!_isProgrammaticChange && IsEditable()) { _selectedPalette!.ExponentRange = (double)_nudRange.Value; MarkUnsaved(); _panelPreview.Invalidate(); } };

            var lblZero = new Label { Text = "Zero-band:", Location = new Point(200, 120), AutoSize = true };
            _nudZeroBand.Location = new Point(200, 138);
            _nudZeroBand.Size = new Size(178, 23);
            _nudZeroBand.DecimalPlaces = 4;
            _nudZeroBand.Minimum = 0.0001m;
            _nudZeroBand.Maximum = 2;
            _nudZeroBand.Increment = 0.001m;
            _nudZeroBand.ValueChanged += (_, _) => { if (!_isProgrammaticChange && IsEditable()) { _selectedPalette!.ZeroBandWidth = (double)_nudZeroBand.Value; MarkUnsaved(); _panelPreview.Invalidate(); } };

            var lblPreview = new Label { Text = "Превью:", Location = new Point(9, 170), AutoSize = true };
            _panelPreview.Location = new Point(9, 188);
            _panelPreview.Size = new Size(369, 38);
            _panelPreview.BorderStyle = BorderStyle.FixedSingle;
            _panelPreview.Paint += (_, e) => DrawPreview(e.Graphics);

            var lblColors = new Label { Text = "Ключевые цвета:", Location = new Point(9, 235), AutoSize = true };
            _lbColors.Location = new Point(9, 253);
            _lbColors.Size = new Size(256, 139);
            _lbColors.SelectedIndexChanged += (_, _) => UpdateControlsState();

            _btnAddColor.Text = "Добавить...";
            _btnAddColor.Location = new Point(271, 253);
            _btnAddColor.Size = new Size(107, 28);
            _btnAddColor.Click += (_, _) => AddColor();

            _btnEditColor.Text = "Изменить...";
            _btnEditColor.Location = new Point(271, 287);
            _btnEditColor.Size = new Size(107, 28);
            _btnEditColor.Click += (_, _) => EditColor();

            _btnRemoveColor.Text = "Удалить";
            _btnRemoveColor.Location = new Point(271, 321);
            _btnRemoveColor.Size = new Size(107, 28);
            _btnRemoveColor.Click += (_, _) => RemoveColor();

            grpEditor.Controls.AddRange(new Control[] { lblName, _txtName, lblMode, _cbMode, lblRange, _nudRange, lblZero, _nudZeroBand, lblPreview, _panelPreview, lblColors, _lbColors, _btnAddColor, _btnEditColor, _btnRemoveColor });

            _btnSave.Text = "Сохранить изменения";
            _btnSave.Location = new Point(19, 461);
            _btnSave.Size = new Size(149, 28);
            _btnSave.Click += (_, _) => SaveSelected();

            _btnApply.Text = "Применить";
            _btnApply.Location = new Point(432, 461);
            _btnApply.Size = new Size(107, 28);
            _btnApply.Click += (_, _) => ApplySelected();

            _btnClose.Text = "Закрыть";
            _btnClose.Location = new Point(545, 461);
            _btnClose.Size = new Size(107, 28);
            _btnClose.Click += (_, _) => Close();

            Controls.AddRange(new Control[] { grpPalettes, grpEditor, _btnSave, _btnApply, _btnClose });
        }

        private void PopulatePaletteList()
        {
            _isProgrammaticChange = true;
            _lbPalettes.Items.Clear();
            foreach (var palette in _paletteManager.Palettes)
            {
                _lbPalettes.Items.Add(palette.IsBuiltIn ? $"{palette.Name} [Встроенная]" : palette.Name);
            }

            string activeName = _paletteManager.ActivePalette.IsBuiltIn
                ? $"{_paletteManager.ActivePalette.Name} [Встроенная]"
                : _paletteManager.ActivePalette.Name;
            _lbPalettes.SelectedItem = activeName;
            _isProgrammaticChange = false;
            OnPaletteSelected();
        }

        private void OnPaletteSelected()
        {
            if (_isProgrammaticChange || _lbPalettes.SelectedItem is null)
            {
                return;
            }

            string selectedName = _lbPalettes.SelectedItem.ToString()!.Replace(" [Встроенная]", string.Empty);
            _selectedPalette = _paletteManager.Palettes.First(p => p.Name == selectedName);

            _isProgrammaticChange = true;
            _txtName.Text = _selectedPalette.Name;
            _cbMode.SelectedIndex = (int)_selectedPalette.Mode;
            _nudRange.Value = (decimal)Math.Clamp(_selectedPalette.ExponentRange, (double)_nudRange.Minimum, (double)_nudRange.Maximum);
            _nudZeroBand.Value = (decimal)Math.Clamp(_selectedPalette.ZeroBandWidth, (double)_nudZeroBand.Minimum, (double)_nudZeroBand.Maximum);
            _lbColors.Items.Clear();
            foreach (Color c in _selectedPalette.Colors)
            {
                _lbColors.Items.Add($"#{c.R:X2}{c.G:X2}{c.B:X2}");
            }
            _isProgrammaticChange = false;

            ResetUnsaved();
            UpdateControlsState();
            _panelPreview.Invalidate();
        }

        private bool IsEditable()
        {
            if (_selectedPalette == null || _selectedPalette.IsBuiltIn)
            {
                return false;
            }
            return true;
        }

        private void MarkUnsaved()
        {
            if (!IsEditable()) return;
            _hasUnsavedChanges = true;
            _btnSave.Enabled = true;
        }

        private void ResetUnsaved()
        {
            _hasUnsavedChanges = false;
            _btnSave.Enabled = false;
        }

        private void UpdateControlsState()
        {
            bool has = _selectedPalette != null;
            bool editable = has && !_selectedPalette!.IsBuiltIn;
            _txtName.Enabled = editable;
            _cbMode.Enabled = editable;
            _nudRange.Enabled = editable;
            _nudZeroBand.Enabled = editable;
            _lbColors.Enabled = editable;
            _btnAddColor.Enabled = editable;
            _btnEditColor.Enabled = editable && _lbColors.SelectedIndex >= 0;
            _btnRemoveColor.Enabled = editable && _lbColors.SelectedIndex >= 0 && _selectedPalette!.Colors.Count > 1;
            _btnDelete.Enabled = editable;
            _btnCopy.Enabled = has;
            _btnApply.Enabled = has;
            _btnSave.Enabled = editable && _hasUnsavedChanges;
        }

        private string GenerateUniqueName(string baseName)
        {
            string seed = string.IsNullOrWhiteSpace(baseName) ? "Новая палитра" : baseName.Trim();
            string name = seed;
            int i = 1;
            while (_paletteManager.Palettes.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{seed} {i++}";
            }
            return name;
        }

        private void CreateNew()
        {
            string name = GenerateUniqueName("Новая палитра");
            var palette = LyapunovPaletteManager.CreateDefaultBuiltInPalette().CloneAsCustom(name);
            _paletteManager.Palettes.Add(palette);
            _paletteManager.SavePalettes();
            PopulatePaletteList();
            _lbPalettes.SelectedItem = palette.Name;
        }

        private void CopySelected()
        {
            if (_selectedPalette == null) return;
            string name = GenerateUniqueName($"{_selectedPalette.Name} (копия)");
            var copy = _selectedPalette.CloneAsCustom(name);
            _paletteManager.Palettes.Add(copy);
            _paletteManager.SavePalettes();
            PopulatePaletteList();
            _lbPalettes.SelectedItem = copy.Name;
        }

        private void DeleteSelected()
        {
            if (!IsEditable()) return;
            if (_selectedPalette == null) return;
            _paletteManager.Palettes.Remove(_selectedPalette);
            _paletteManager.ActivePalette = _paletteManager.Palettes.First();
            _paletteManager.SavePalettes();
            PopulatePaletteList();
        }

        private void SaveSelected()
        {
            if (!IsEditable() || _selectedPalette == null)
            {
                return;
            }

            if (_selectedPalette.Colors.Count == 0)
            {
                _selectedPalette.Colors.Add(Color.White);
            }

            _paletteManager.SavePalettes();
            ResetUnsaved();
            PopulatePaletteList();
            _lbPalettes.SelectedItem = _selectedPalette.Name;
        }

        private void ApplySelected()
        {
            if (_selectedPalette == null)
            {
                return;
            }

            _paletteManager.ActivePalette = _selectedPalette;
            PaletteApplied?.Invoke(this, EventArgs.Empty);
        }

        private void AddColor()
        {
            if (!IsEditable()) return;
            if (_colorSelectionService.TrySelectColor(this, Color.White, out Color selected))
            {
                _selectedPalette!.Colors.Add(selected);
                _lbColors.Items.Add($"#{selected.R:X2}{selected.G:X2}{selected.B:X2}");
                MarkUnsaved();
                _panelPreview.Invalidate();
            }
        }

        private void EditColor()
        {
            if (!IsEditable() || _selectedPalette == null || _lbColors.SelectedIndex < 0)
            {
                return;
            }

            int idx = _lbColors.SelectedIndex;
            if (_colorSelectionService.TrySelectColor(this, _selectedPalette.Colors[idx], out Color selected))
            {
                _selectedPalette.Colors[idx] = selected;
                _lbColors.Items[idx] = $"#{selected.R:X2}{selected.G:X2}{selected.B:X2}";
                MarkUnsaved();
                _panelPreview.Invalidate();
            }
        }

        private void RemoveColor()
        {
            if (!IsEditable() || _selectedPalette == null || _lbColors.SelectedIndex < 0 || _selectedPalette.Colors.Count <= 1)
            {
                return;
            }

            int idx = _lbColors.SelectedIndex;
            _selectedPalette.Colors.RemoveAt(idx);
            _lbColors.Items.RemoveAt(idx);
            MarkUnsaved();
            _panelPreview.Invalidate();
        }

        private void DrawPreview(Graphics g)
        {
            if (_selectedPalette == null)
            {
                return;
            }

            int w = Math.Max(1, _panelPreview.ClientSize.Width);
            int h = Math.Max(1, _panelPreview.ClientSize.Height);
            var local = new LyapunovColorPalette
            {
                Mode = (LyapunovColoringMode)Math.Max(0, _cbMode.SelectedIndex),
                Colors = _selectedPalette.Colors.ToList(),
                ExponentRange = (double)_nudRange.Value,
                ZeroBandWidth = (double)_nudZeroBand.Value
            };

            for (int x = 0; x < w; x++)
            {
                double exponent = ((x / (double)(w - 1)) * 2.0 - 1.0) * local.ExponentRange;
                using var pen = new Pen(LyapunovColoring.MapExponent(exponent, local));
                g.DrawLine(pen, x, 0, x, h);
            }
        }
    }
}
