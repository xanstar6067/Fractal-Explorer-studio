using FractalExplorer.Utilities.ColorPicking;
using FractalExplorer.Utilities.Coloring;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.Theme;

namespace FractalExplorer.SelectorsForms
{
    public partial class ColorConfigurationLyapunovForm : Form
    {
        private static readonly (LyapunovColoringMode Mode, string DisplayName)[] ModeUiItems =
        {
            (LyapunovColoringMode.LegacyBuiltIn, "Классический (встроенный)"),
            (LyapunovColoringMode.Diverging, "Дивергентный"),
            (LyapunovColoringMode.Absolute, "Абсолютный"),
            (LyapunovColoringMode.ZeroBandHighlight, "Подсветка нулевой зоны"),
            (LyapunovColoringMode.HistogramEqualized, "Гистограммное выравнивание")
        };

        private readonly LyapunovPaletteManager _paletteManager;
        private readonly ColorSelectionService _colorSelectionService = ColorSelectionService.Default;

        private LyapunovColorPalette? _selectedPalette;
        private bool _isProgrammaticChange;
        private bool _hasUnsavedChanges;

        public event EventHandler? PaletteApplied;

        public ColorConfigurationLyapunovForm(LyapunovPaletteManager paletteManager)
        {
            _paletteManager = paletteManager;
            InitializeComponent();
            InitializeData();
            InitializeEventHandlers();
            ThemeManager.RegisterForm(this);
            Load += (_, _) => PopulatePaletteList();
        }

        private void InitializeData()
        {
            _cbMode.Items.AddRange(ModeUiItems.Select(item => item.DisplayName).ToArray());
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
            _cbMode.SelectedIndex = Array.FindIndex(ModeUiItems, item => item.Mode == _selectedPalette.Mode);
            if (_cbMode.SelectedIndex < 0)
            {
                _cbMode.SelectedIndex = 0;
            }
            _nudRange.Value = (decimal)Math.Clamp(_selectedPalette.ExponentRange, (double)_nudRange.Minimum, (double)_nudRange.Maximum);
            _nudZeroBand.Value = (decimal)Math.Clamp(_selectedPalette.ZeroBandWidth, (double)_nudZeroBand.Minimum, (double)_nudZeroBand.Maximum);
            RefreshColorsList();
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
            _lbColors.AllowDrop = editable;
            _btnAddColor.Enabled = editable;
            _btnEditColor.Enabled = editable && _lbColors.SelectedIndex >= 0;
            _btnRemoveColor.Enabled = editable && _lbColors.SelectedIndex >= 0 && _selectedPalette!.Colors.Count > 1;
            _btnDelete.Enabled = editable;
            _btnCopy.Enabled = has;
            _btnApply.Enabled = has;
            _btnSave.Enabled = editable && _hasUnsavedChanges;
        }

        private void RefreshColorsList()
        {
            _lbColors.Items.Clear();
            if (_selectedPalette == null)
            {
                return;
            }

            foreach (Color c in _selectedPalette.Colors)
            {
                _lbColors.Items.Add($"#{c.R:X2}{c.G:X2}{c.B:X2}");
            }
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
            palette.Mode = LyapunovColoringMode.Diverging;
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
                Mode = GetSelectedModeOrDefault(),
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

        private void ColorsListMouseDown(object? sender, MouseEventArgs e)
        {
            if (!IsEditable() || e.Button != MouseButtons.Left)
            {
                return;
            }

            int sourceIndex = _lbColors.IndexFromPoint(e.Location);
            if (sourceIndex == ListBox.NoMatches)
            {
                return;
            }

            _lbColors.SelectedIndex = sourceIndex;
            _lbColors.DoDragDrop(sourceIndex, DragDropEffects.Move);
        }

        private void ColorsListDragOver(object? sender, DragEventArgs e)
        {
            e.Effect = e.Data?.GetDataPresent(typeof(int)) == true && IsEditable()
                ? DragDropEffects.Move
                : DragDropEffects.None;
        }

        private void ColorsListDragDrop(object? sender, DragEventArgs e)
        {
            if (!IsEditable() || _selectedPalette == null || e.Data?.GetDataPresent(typeof(int)) != true)
            {
                return;
            }

            int sourceIndex = (int)e.Data.GetData(typeof(int))!;
            Point point = _lbColors.PointToClient(new Point(e.X, e.Y));
            int destinationIndex = _lbColors.IndexFromPoint(point);
            if (destinationIndex == ListBox.NoMatches)
            {
                destinationIndex = _lbColors.Items.Count - 1;
            }

            if (sourceIndex < 0 || sourceIndex >= _selectedPalette.Colors.Count || sourceIndex == destinationIndex)
            {
                return;
            }

            Color moved = _selectedPalette.Colors[sourceIndex];
            _selectedPalette.Colors.RemoveAt(sourceIndex);
            if (sourceIndex < destinationIndex)
            {
                destinationIndex--;
            }
            _selectedPalette.Colors.Insert(destinationIndex, moved);

            MarkUnsaved();
            RefreshColorsList();
            _lbColors.SelectedIndex = destinationIndex;
            _panelPreview.Invalidate();
        }

        private void InitializeEventHandlers()
        {
            _lbPalettes.SelectedIndexChanged += (_, _) => OnPaletteSelected();
            _lbColors.SelectedIndexChanged += (_, _) => UpdateControlsState();
            _lbColors.MouseDown += ColorsListMouseDown;
            _lbColors.DragOver += ColorsListDragOver;
            _lbColors.DragDrop += ColorsListDragDrop;

            _btnNew.Click += (_, _) => CreateNew();
            _btnCopy.Click += (_, _) => CopySelected();
            _btnDelete.Click += (_, _) => DeleteSelected();

            _txtName.TextChanged += (_, _) =>
            {
                if (_isProgrammaticChange || !IsEditable())
                {
                    return;
                }

                _selectedPalette!.Name = _txtName.Text;
                MarkUnsaved();
            };

            _cbMode.SelectedIndexChanged += (_, _) =>
            {
                if (_isProgrammaticChange || _selectedPalette == null || !IsEditable()) return;
                _selectedPalette.Mode = GetSelectedModeOrDefault();
                MarkUnsaved();
                _panelPreview.Invalidate();
            };

            _nudRange.ValueChanged += (_, _) =>
            {
                if (_isProgrammaticChange || !IsEditable())
                {
                    return;
                }

                _selectedPalette!.ExponentRange = (double)_nudRange.Value;
                MarkUnsaved();
                _panelPreview.Invalidate();
            };

            _nudZeroBand.ValueChanged += (_, _) =>
            {
                if (_isProgrammaticChange || !IsEditable())
                {
                    return;
                }

                _selectedPalette!.ZeroBandWidth = (double)_nudZeroBand.Value;
                MarkUnsaved();
                _panelPreview.Invalidate();
            };

            _panelPreview.Paint += (_, e) => DrawPreview(e.Graphics);

            _btnAddColor.Click += (_, _) => AddColor();
            _btnEditColor.Click += (_, _) => EditColor();
            _btnRemoveColor.Click += (_, _) => RemoveColor();
            _btnSave.Click += (_, _) => SaveSelected();
            _btnApply.Click += (_, _) => ApplySelected();
            _btnClose.Click += (_, _) => Close();
        }

        private LyapunovColoringMode GetSelectedModeOrDefault()
        {
            int index = _cbMode.SelectedIndex;
            return index >= 0 && index < ModeUiItems.Length
                ? ModeUiItems[index].Mode
                : LyapunovColoringMode.LegacyBuiltIn;
        }
    }
}
