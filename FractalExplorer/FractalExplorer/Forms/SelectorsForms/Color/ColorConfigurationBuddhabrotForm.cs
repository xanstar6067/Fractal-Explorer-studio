using FractalExplorer.Utilities.ColorPicking;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.Theme;
using System.Drawing.Drawing2D;

namespace FractalExplorer.SelectorsForms
{
    public partial class ColorConfigurationBuddhabrotForm : Form
    {
        private readonly BuddhabrotPaletteManager _paletteManager;
        private readonly ColorSelectionService _colorSelectionService = ColorSelectionService.Default;
        private readonly int _renderMaxIterations;
        private BuddhabrotColorPalette? _selectedPalette;
        private bool _isProgrammaticChange;
        private bool _hasUnsavedChanges;

        public event EventHandler? PaletteApplied;

        public ColorConfigurationBuddhabrotForm(BuddhabrotPaletteManager paletteManager, int renderMaxIterations)
        {
            _paletteManager = paletteManager;
            _renderMaxIterations = Math.Max(2, renderMaxIterations);
            InitializeComponent();
            InitializeData();
            InitializeEventHandlers();
            ThemeManager.RegisterForm(this);
            Load += (_, _) => PopulatePaletteList();
        }

        private void InitializeData()
        {
            _cbMode.Items.AddRange(Enum.GetNames(typeof(BuddhabrotColoringMode)));
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
            _cbMode.SelectedItem = _selectedPalette.ColoringMode.ToString();
            _checkIsGradient.Checked = _selectedPalette.IsGradient;
            _checkAlignSteps.Checked = _selectedPalette.AlignWithRenderIterations;
            _nudMaxSteps.Value = Math.Clamp(_selectedPalette.MaxColorIterations, (int)_nudMaxSteps.Minimum, (int)_nudMaxSteps.Maximum);
            _nudGamma.Value = (decimal)Math.Clamp(_selectedPalette.Gamma, (double)_nudGamma.Minimum, (double)_nudGamma.Maximum);
            RefreshColorsList();
            _isProgrammaticChange = false;

            ResetUnsaved();
            UpdateControlsState();
            _panelPreview.Invalidate();
        }

        private bool IsEditable() => _selectedPalette != null && !_selectedPalette.IsBuiltIn;

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
            _checkIsGradient.Enabled = editable;
            _checkAlignSteps.Enabled = editable;
            _nudMaxSteps.Enabled = editable && !_checkAlignSteps.Checked;
            _nudGamma.Enabled = editable;
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
            var palette = BuddhabrotPaletteManager.CreateDefaultBuiltInPalette().CloneAsCustom(name);
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
            if (!IsEditable() || _selectedPalette == null) return;
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
            var colors = _selectedPalette.Colors.Count == 0 ? new List<Color> { Color.Black, Color.White } : _selectedPalette.Colors.ToList();
            int cycleLength = _selectedPalette.AlignWithRenderIterations
                ? _renderMaxIterations
                : Math.Max(2, _selectedPalette.MaxColorIterations);

            using var bitmap = new Bitmap(w, 1);
            for (int x = 0; x < w; x++)
            {
                double normalized = w <= 1 ? 0.0 : x / (double)(w - 1);
                bitmap.SetPixel(x, 0, EvaluatePaletteColor(colors, _selectedPalette, cycleLength, normalized));
            }

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(bitmap, new Rectangle(0, 0, w, h));
        }

        private static Color EvaluatePaletteColor(IReadOnlyList<Color> colors, BuddhabrotColorPalette palette, int cycleLength, double normalized)
        {
            if (colors.Count == 1)
            {
                return ApplyGamma(colors[0], palette.Gamma);
            }

            double mapped = MapNormalizedByMode(normalized, palette.ColoringMode);
            if (palette.IsGradient)
            {
                double t = Math.Clamp(mapped, 0.0, 1.0);
                double scaled = t * (colors.Count - 1);
                int i0 = (int)Math.Floor(scaled);
                int i1 = Math.Min(i0 + 1, colors.Count - 1);
                double f = scaled - i0;
                Color c0 = ApplyGamma(colors[i0], palette.Gamma);
                Color c1 = ApplyGamma(colors[i1], palette.Gamma);
                return Color.FromArgb(
                    255,
                    (int)(c0.R + (c1.R - c0.R) * f),
                    (int)(c0.G + (c1.G - c0.G) * f),
                    (int)(c0.B + (c1.B - c0.B) * f));
            }

            double mappedClamped = Math.Clamp(mapped, 0.0, 1.0);
            double quant = mappedClamped >= 1.0
                ? 1.0
                : Math.Floor(mappedClamped * cycleLength) / (cycleLength - 1.0);
            double tQuant = Math.Clamp(quant, 0.0, 1.0);
            double scaledQuant = tQuant * (colors.Count - 1);
            int q0 = Math.Min((int)Math.Floor(scaledQuant), colors.Count - 1);
            int q1 = Math.Min(q0 + 1, colors.Count - 1);
            double qf = scaledQuant - q0;
            Color qc0 = ApplyGamma(colors[q0], palette.Gamma);
            Color qc1 = ApplyGamma(colors[q1], palette.Gamma);
            return Color.FromArgb(
                255,
                (int)(qc0.R + (qc1.R - qc0.R) * qf),
                (int)(qc0.G + (qc1.G - qc0.G) * qf),
                (int)(qc0.B + (qc1.B - qc0.B) * qf));
        }

        private static double MapNormalizedByMode(double normalized, BuddhabrotColoringMode mode)
        {
            normalized = Math.Clamp(normalized, 0.0, 1.0);
            return mode switch
            {
                BuddhabrotColoringMode.Linear => normalized,
                BuddhabrotColoringMode.Sqrt => Math.Sqrt(normalized),
                _ => Math.Log(1 + normalized * 15.0) / Math.Log(16.0)
            };
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

        private static Color ApplyGamma(Color c, double gamma)
        {
            gamma = Math.Clamp(gamma, 0.1, 5.0);
            static int Convert(int channel, double g)
            {
                double normalized = channel / 255.0;
                double corrected = Math.Pow(normalized, 1.0 / g);
                return (int)Math.Round(Math.Clamp(corrected, 0.0, 1.0) * 255.0);
            }

            return Color.FromArgb(c.A, Convert(c.R, gamma), Convert(c.G, gamma), Convert(c.B, gamma));
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
                if (_isProgrammaticChange || !IsEditable()) return;
                _selectedPalette!.ColoringMode = GetSelectedMode();
                MarkUnsaved();
                _panelPreview.Invalidate();
            };

            _checkIsGradient.CheckedChanged += (_, _) =>
            {
                if (_isProgrammaticChange || !IsEditable()) return;
                _selectedPalette!.IsGradient = _checkIsGradient.Checked;
                MarkUnsaved();
                _panelPreview.Invalidate();
            };

            _checkAlignSteps.CheckedChanged += (_, _) =>
            {
                if (_isProgrammaticChange || !IsEditable()) return;
                _selectedPalette!.AlignWithRenderIterations = _checkAlignSteps.Checked;
                _nudMaxSteps.Enabled = !_checkAlignSteps.Checked;
                MarkUnsaved();
                _panelPreview.Invalidate();
            };

            _nudMaxSteps.ValueChanged += (_, _) =>
            {
                if (_isProgrammaticChange || !IsEditable()) return;
                _selectedPalette!.MaxColorIterations = (int)_nudMaxSteps.Value;
                MarkUnsaved();
                _panelPreview.Invalidate();
            };

            _nudGamma.ValueChanged += (_, _) =>
            {
                if (_isProgrammaticChange || !IsEditable()) return;
                _selectedPalette!.Gamma = (double)_nudGamma.Value;
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

        private BuddhabrotColoringMode GetSelectedMode()
        {
            if (_cbMode.SelectedItem is string modeName && Enum.TryParse(modeName, out BuddhabrotColoringMode mode))
            {
                return mode;
            }

            return BuddhabrotColoringMode.Logarithmic;
        }
    }
}
