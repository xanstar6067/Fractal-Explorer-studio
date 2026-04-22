using FractalExplorer.Properties;
using FractalExplorer.Utilities.ColorPicking;
using FractalExplorer.Utilities.Theme;
using System.Globalization;
using System.Reflection;

namespace FractalExplorer.Forms.Common
{
    public partial class ColorPickerPanelForm : Form
    {
        private const int CustomColorSlotsCount = 16;
        private const int PaletteColumnsCount = 12;
        private const int StandardPaletteRowsCount = 4;
        private const int CustomPaletteColumnsCount = 16;
        private const int CustomPaletteRowsCount = 1;
        private const int PaletteCellSize = 20;
        private const int PaletteCellMargin = 2;
        private const string CustomPaletteTooltipText = "ЛКМ - задать цвет, ПКМ - очистить";

        private readonly Color _originalColor;
        private readonly ScreenEyedropper _screenEyedropper = new();
        private readonly ToolTip _paletteToolTip = new();

        private readonly List<PaletteColorEntry> _standardColors = new()
        {
            new(Color.FromArgb(255, 128, 128), "Светло-красный"), new(Color.FromArgb(255, 255, 128), "Светло-жёлтый"), new(Color.FromArgb(128, 255, 128), "Светло-зелёный"), new(Color.FromArgb(0, 255, 128), "Аквамариновый"),
            new(Color.FromArgb(128, 255, 255), "Светло-бирюзовый"), new(Color.FromArgb(0, 128, 255), "Лазурный"), new(Color.FromArgb(255, 128, 192), "Светло-розовый"), new(Color.FromArgb(255, 128, 255), "Светло-пурпурный"),

            new(Color.Red, "Красный"), new(Color.Yellow, "Жёлтый"), new(Color.FromArgb(128, 255, 0), "Салатовый"), new(Color.FromArgb(0, 255, 64), "Изумрудный"),
            new(Color.Cyan, "Бирюзовый"), new(Color.FromArgb(0, 128, 192), "Сине-бирюзовый"), new(Color.FromArgb(128, 128, 192), "Серо-голубой"), new(Color.Magenta, "Пурпурный"),

            new(Color.FromArgb(128, 64, 64), "Коричнево-красный"), new(Color.FromArgb(255, 128, 64), "Светло-оранжевый"), new(Color.Lime, "Зелёный"), new(Color.Teal, "Тёмно-бирюзовый"),
            new(Color.FromArgb(0, 64, 128), "Тёмно-лазурный"), new(Color.FromArgb(128, 128, 255), "Светло-синий"), new(Color.FromArgb(128, 0, 64), "Тёмно-розовый"), new(Color.DeepPink, "Розовый"),

            new(Color.Maroon, "Бордовый"), new(Color.Orange, "Оранжевый"), new(Color.Green, "Тёмно-зелёный"), new(Color.FromArgb(0, 128, 64), "Хвойный"),
            new(Color.Blue, "Синий"), new(Color.FromArgb(0, 0, 160), "Индиго"), new(Color.Purple, "Фиолетовый"), new(Color.FromArgb(128, 0, 255), "Ярко-фиолетовый"),

            new(Color.FromArgb(64, 0, 0), "Очень тёмно-красный"), new(Color.FromArgb(128, 64, 0), "Тёмно-коричневый"), new(Color.DarkGreen, "Тёмно-зелёный"), new(Color.FromArgb(0, 64, 64), "Тёмный морской"),
            new(Color.Navy, "Тёмно-синий"), new(Color.FromArgb(0, 0, 64), "Ночной синий"), new(Color.FromArgb(64, 0, 64), "Тёмно-пурпурный"), new(Color.FromArgb(64, 0, 128), "Индиго-фиолетовый"),

            new(Color.Black, "Чёрный"), new(Color.FromArgb(64, 64, 64), "Тёмно-серый"), new(Color.Gray, "Серый"), new(Color.Silver, "Серебристый"),
            new(Color.White, "Белый"), new(Color.LightYellow, "Светло-жёлтый"), new(Color.Moccasin, "Кремовый"), new(Color.Gold, "Золотой")
        };

        private readonly Color[] _customColors = new Color[CustomColorSlotsCount];
        private readonly List<Panel> _customColorCells = new();
        private readonly List<Panel> _standardColorCells = new();
        private readonly Dictionary<Panel, int> _customCellIndexes = new();
        private readonly Dictionary<Panel, PaletteColorEntry> _standardCellEntries = new();

        private Color _selectedColor;
        private bool _isUpdatingControls;
        private float _hue;
        private float _saturation;
        private float _brightness;
        private int _standardHoveredIndex = -1;
        private int _customHoveredIndex = -1;

        private Bitmap? _matrixBitmap;
        private Bitmap? _hueBitmap;

        public Color SelectedColor => _selectedColor;

        public ColorPickerPanelForm(Color initialColor)
        {
            InitializeComponent();

            ThemeManager.RegisterForm(this);
            ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
            Disposed += ColorPickerPanelForm_Disposed;
            Shown += ColorPickerPanelForm_Shown;

            EnableDoubleBuffering(pnlColorMatrix);
            EnableDoubleBuffering(pnlHueSlider);
            EnableDoubleBuffering(tableStandardColors);
            EnableDoubleBuffering(tableCustomColors);
            ConfigurePaletteGrid(tableStandardColors, PaletteColumnsCount, StandardPaletteRowsCount);
            ConfigurePaletteGrid(tableCustomColors, CustomPaletteColumnsCount, CustomPaletteRowsCount);
            WirePaletteInteractions();

            _originalColor = initialColor;

            LoadCustomColors();
            BuildStandardPalette();
            BuildCustomPalette();
            BuildHueBitmap();

            ApplySelectedColor(initialColor);
        }

        private void ColorPickerPanelForm_Disposed(object? sender, EventArgs e)
        {
            ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;
            Disposed -= ColorPickerPanelForm_Disposed;
            Shown -= ColorPickerPanelForm_Shown;
            _matrixBitmap?.Dispose();
            _hueBitmap?.Dispose();
            _paletteToolTip.Dispose();
        }

        private void ThemeManager_ThemeChanged(object? sender, EventArgs e)
        {
            BuildHueBitmap();
            RedrawMatrixBitmap();
            RefreshStandardPaletteColors();
            RefreshAllCustomColorCells();
            ApplySelectedColor(_selectedColor);
            InvalidatePaletteHoverVisuals();
            pnlHueSlider.Invalidate();
        }

        private void ColorPickerPanelForm_Shown(object? sender, EventArgs e)
        {
            BuildHueBitmap();
            RedrawMatrixBitmap();
            pnlHueSlider.Invalidate();
            pnlColorMatrix.Invalidate();
        }

        private void btnEyedropper_Click(object? sender, EventArgs e)
        {
            Hide();

            try
            {
                if (_screenEyedropper.TryPickColor(null, out Color pickedColor))
                {
                    ApplySelectedColor(Color.FromArgb(_selectedColor.A, pickedColor.R, pickedColor.G, pickedColor.B));
                }
            }
            finally
            {
                Show();
                Activate();
            }
        }

        private void trackColor_Scroll(object? sender, EventArgs e)
        {
            if (_isUpdatingControls)
            {
                return;
            }

            ApplySelectedColor(Color.FromArgb(_selectedColor.A, trkRed.Value, trkGreen.Value, trkBlue.Value));
        }

        private void pnlColorMatrix_Paint(object? sender, PaintEventArgs e)
        {
            if (_matrixBitmap != null)
            {
                e.Graphics.DrawImage(_matrixBitmap, 0, 0, pnlColorMatrix.Width, pnlColorMatrix.Height);
            }

            Point marker = GetMatrixMarkerPoint();
            Rectangle markerRect = new(marker.X - 4, marker.Y - 4, 8, 8);
            e.Graphics.DrawEllipse(Pens.Black, markerRect);
            e.Graphics.DrawEllipse(Pens.White, markerRect.X + 1, markerRect.Y + 1, markerRect.Width - 2, markerRect.Height - 2);
        }

        private void pnlColorMatrix_MouseDown(object? sender, MouseEventArgs e)
        {
            UpdateColorFromMatrix(e.Location);
        }

        private void pnlColorMatrix_MouseMove(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                UpdateColorFromMatrix(e.Location);
            }
        }

        private void pnlHueSlider_Paint(object? sender, PaintEventArgs e)
        {
            if (_hueBitmap == null || _hueBitmap.Width != Math.Max(1, pnlHueSlider.Width) || _hueBitmap.Height != Math.Max(1, pnlHueSlider.Height))
            {
                BuildHueBitmap();
            }

            if (_hueBitmap != null)
            {
                e.Graphics.DrawImage(_hueBitmap, 0, 0, pnlHueSlider.Width, pnlHueSlider.Height);
            }

            int markerY = GetHueMarkerPosition();
            Rectangle markerRect = new(0, markerY - 3, pnlHueSlider.Width - 1, 6);
            e.Graphics.DrawRectangle(Pens.Black, markerRect);
        }

        private void pnlHueSlider_MouseDown(object? sender, MouseEventArgs e)
        {
            UpdateHueFromPoint(e.Y);
        }

        private void pnlHueSlider_MouseMove(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                UpdateHueFromPoint(e.Y);
            }
        }

        private void btnAddToCustomColors_Click(object? sender, EventArgs e)
        {
            for (int i = 0; i < _customColors.Length; i++)
            {
                if (_customColors[i] == Color.Empty)
                {
                    _customColors[i] = _selectedColor;
                    RefreshCustomColorCell(i);
                    SaveCustomColors();
                    return;
                }
            }

            _customColors[0] = _selectedColor;
            RefreshCustomColorCell(0);
            SaveCustomColors();
        }

        private void btnApplyHex_Click(object? sender, EventArgs e)
        {
            ApplyColorFromHexInput();
        }

        private void txtHexInput_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            ApplyColorFromHexInput();
        }

        private void btnCopyCurrentHex_Click(object? sender, EventArgs e)
        {
            string currentHex = ToHexString(_originalColor);
            Clipboard.SetText(currentHex);
        }

        private void CustomPaletteCell_Click(object? sender, EventArgs e)
        {
            if (sender is not Panel panel || !_customCellIndexes.TryGetValue(panel, out int index))
            {
                return;
            }

            if (_customColors[index] != Color.Empty)
            {
                ApplySelectedColor(_customColors[index]);
            }
            else
            {
                _customColors[index] = _selectedColor;
                RefreshCustomColorCell(index);
                SaveCustomColors();
            }
        }

        private void CustomPaletteCell_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || sender is not Panel panel || !_customCellIndexes.TryGetValue(panel, out int index))
            {
                return;
            }

            _customColors[index] = Color.Empty;
            RefreshCustomColorCell(index);
            SaveCustomColors();
        }

        private void StandardPaletteCell_Click(object? sender, EventArgs e)
        {
            if (sender is Panel panel && _standardCellEntries.TryGetValue(panel, out PaletteColorEntry? entry))
            {
                ApplySelectedColor(entry.Color);
            }
        }

        private void ApplySelectedColor(Color color)
        {
            _selectedColor = color;
            _hue = color.GetHue();
            _saturation = color.GetSaturation();
            _brightness = color.GetBrightness();

            _isUpdatingControls = true;
            try
            {
                trkRed.Value = color.R;
                trkGreen.Value = color.G;
                trkBlue.Value = color.B;
            }
            finally
            {
                _isUpdatingControls = false;
            }

            lblRedValue.Text = color.R.ToString(CultureInfo.InvariantCulture);
            lblGreenValue.Text = color.G.ToString(CultureInfo.InvariantCulture);
            lblBlueValue.Text = color.B.ToString(CultureInfo.InvariantCulture);

            pnlCurrentColor.BackColor = _originalColor;
            pnlNewColor.BackColor = _selectedColor;

            lblCurrentHexValue.Text = ToHexString(_originalColor);
            lblNewHexValue.Text = ToHexString(_selectedColor);
            txtHexInput.Text = ToHexString(_selectedColor);

            RedrawMatrixBitmap();
            pnlColorMatrix.Invalidate();
            pnlHueSlider.Invalidate();
        }

        private void ApplyColorFromHexInput()
        {
            if (TryParseHexColor(txtHexInput.Text, out Color parsedColor))
            {
                ApplySelectedColor(parsedColor);
                txtHexInput.ForeColor = SystemColors.WindowText;
                return;
            }

            txtHexInput.ForeColor = Color.Firebrick;
        }

        private void UpdateColorFromMatrix(Point location)
        {
            if (pnlColorMatrix.Width <= 1 || pnlColorMatrix.Height <= 1)
            {
                return;
            }

            int x = Math.Max(0, Math.Min(pnlColorMatrix.Width - 1, location.X));
            int y = Math.Max(0, Math.Min(pnlColorMatrix.Height - 1, location.Y));

            _saturation = x / (float)(pnlColorMatrix.Width - 1);
            _brightness = 1f - (y / (float)(pnlColorMatrix.Height - 1));

            ApplySelectedColor(FromAhsb(_selectedColor.A, _hue, _saturation, _brightness));
        }

        private void UpdateHueFromPoint(int y)
        {
            if (pnlHueSlider.Height <= 1)
            {
                return;
            }

            int clampedY = Math.Max(0, Math.Min(pnlHueSlider.Height - 1, y));
            _hue = 360f - (clampedY / (float)(pnlHueSlider.Height - 1)) * 360f;
            if (_hue >= 360f)
            {
                _hue = 0f;
            }

            ApplySelectedColor(FromAhsb(_selectedColor.A, _hue, _saturation, _brightness));
        }

        private Point GetMatrixMarkerPoint()
        {
            int x = (int)Math.Round(_saturation * Math.Max(1, pnlColorMatrix.Width - 1));
            int y = (int)Math.Round((1f - _brightness) * Math.Max(1, pnlColorMatrix.Height - 1));
            return new Point(x, y);
        }

        private int GetHueMarkerPosition()
        {
            float normalized = 1f - (_hue / 360f);
            return (int)Math.Round(normalized * Math.Max(1, pnlHueSlider.Height - 1));
        }

        private void BuildStandardPalette()
        {
            tableStandardColors.SuspendLayout();
            tableStandardColors.Controls.Clear();
            _standardColorCells.Clear();
            _standardCellEntries.Clear();

            for (int i = 0; i < _standardColors.Count; i++)
            {
                Panel cell = CreateColorCell(_standardColors[i], StandardPaletteCell_Click);
                int capturedIndex = i;
                cell.MouseEnter += (_, _) => SetStandardHoveredIndex(capturedIndex);
                cell.MouseLeave += (_, _) => SetStandardHoveredIndex(-1);
                cell.MouseMove += (_, _) => SetStandardHoveredIndex(capturedIndex);
                cell.Paint += StandardColorCell_Paint;
                _standardColorCells.Add(cell);
                _standardCellEntries[cell] = _standardColors[i];
                _paletteToolTip.SetToolTip(cell, _standardColors[i].Name ?? string.Empty);
                tableStandardColors.Controls.Add(cell, i % PaletteColumnsCount, i / PaletteColumnsCount);
            }

            tableStandardColors.ResumeLayout();
        }

        private void BuildCustomPalette()
        {
            tableCustomColors.SuspendLayout();
            tableCustomColors.Controls.Clear();
            _customColorCells.Clear();
            _customCellIndexes.Clear();

            for (int i = 0; i < _customColors.Length; i++)
            {
                Panel cell = CreateColorCell(_customColors[i], i, CustomPaletteCell_Click);
                int capturedIndex = i;
                cell.MouseEnter += (_, _) => SetCustomHoveredIndex(capturedIndex);
                cell.MouseLeave += (_, _) => SetCustomHoveredIndex(-1);
                cell.MouseMove += (_, _) => SetCustomHoveredIndex(capturedIndex);
                cell.MouseUp += CustomPaletteCell_MouseUp;
                cell.Paint += CustomColorCell_Paint;
                _customColorCells.Add(cell);
                _customCellIndexes[cell] = i;
                _paletteToolTip.SetToolTip(cell, CustomPaletteTooltipText);
                tableCustomColors.Controls.Add(cell, i % CustomPaletteColumnsCount, i / CustomPaletteColumnsCount);
            }

            tableCustomColors.ResumeLayout();
        }

        private static Panel CreateColorCell(PaletteColorEntry entry, EventHandler clickHandler)
        {
            Panel cell = new()
            {
                BackColor = entry.Color == Color.Empty ? Color.White : entry.Color,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                Margin = new Padding(PaletteCellMargin),
                Cursor = Cursors.Hand,
                Tag = "preserve-backcolor",
                MinimumSize = new Size(PaletteCellSize, PaletteCellSize)
            };

            EnableDoubleBuffering(cell);
            cell.Click += clickHandler;
            return cell;
        }

        private static Panel CreateColorCell(Color color, int index, EventHandler clickHandler)
        {
            Panel cell = new()
            {
                BackColor = color == Color.Empty ? Color.White : color,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                Margin = new Padding(PaletteCellMargin),
                Cursor = Cursors.Hand,
                Tag = "preserve-backcolor",
                MinimumSize = new Size(PaletteCellSize, PaletteCellSize)
            };

            EnableDoubleBuffering(cell);
            cell.Click += clickHandler;
            return cell;
        }

        private void RefreshCustomColorCell(int index)
        {
            if (index < 0 || index >= _customColorCells.Count)
            {
                return;
            }

            _customColorCells[index].BackColor = _customColors[index] == Color.Empty ? Color.White : _customColors[index];
        }

        private void RefreshStandardPaletteColors()
        {
            for (int i = 0; i < _standardColorCells.Count && i < _standardColors.Count; i++)
            {
                _standardColorCells[i].BackColor = _standardColors[i].Color;
            }
        }

        private void RefreshAllCustomColorCells()
        {
            for (int i = 0; i < _customColorCells.Count; i++)
            {
                RefreshCustomColorCell(i);
            }
        }

        private void RedrawMatrixBitmap()
        {
            _matrixBitmap?.Dispose();

            int width = Math.Max(1, pnlColorMatrix.Width);
            int height = Math.Max(1, pnlColorMatrix.Height);
            _matrixBitmap = new Bitmap(width, height);

            for (int y = 0; y < height; y++)
            {
                float b = 1f - (y / (float)Math.Max(1, height - 1));
                for (int x = 0; x < width; x++)
                {
                    float s = x / (float)Math.Max(1, width - 1);
                    _matrixBitmap.SetPixel(x, y, FromAhsb(255, _hue, s, b));
                }
            }
        }

        private void BuildHueBitmap()
        {
            _hueBitmap?.Dispose();

            int width = Math.Max(1, pnlHueSlider.Width);
            int height = Math.Max(1, pnlHueSlider.Height);
            _hueBitmap = new Bitmap(width, height);

            for (int y = 0; y < height; y++)
            {
                float h = 360f - (y / (float)Math.Max(1, height - 1)) * 360f;
                if (h >= 360f)
                {
                    h = 0f;
                }

                // FromAhsb реализует HSL-подобную модель (где "brightness" = lightness).
                // Для lightness=1f любой hue становится белым, поэтому для hue-полосы
                // используем среднюю lightness, чтобы отображалась полноценная радуга.
                Color color = FromAhsb(255, h, 1f, 0.5f);
                for (int x = 0; x < width; x++)
                {
                    _hueBitmap.SetPixel(x, y, color);
                }
            }
        }

        private void ColorPickerPanelForm_Resize(object? sender, EventArgs e)
        {
            BuildHueBitmap();
            RedrawMatrixBitmap();
            pnlColorMatrix.Invalidate();
            pnlHueSlider.Invalidate();
        }

        private static void EnableDoubleBuffering(Control control)
        {
            PropertyInfo? property = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            property?.SetValue(control, true);
        }

        private static void ConfigurePaletteGrid(TableLayoutPanel table, int columns, int rows)
        {
            table.SuspendLayout();
            table.ColumnCount = columns;
            table.RowCount = rows;
            table.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            table.Margin = Padding.Empty;
            table.Padding = Padding.Empty;

            table.ColumnStyles.Clear();
            for (int i = 0; i < columns; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, PaletteCellSize + (PaletteCellMargin * 2)));
            }

            table.RowStyles.Clear();
            for (int i = 0; i < rows; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, PaletteCellSize + (PaletteCellMargin * 2)));
            }

            table.Width = columns * (PaletteCellSize + (PaletteCellMargin * 2));
            table.Height = rows * (PaletteCellSize + (PaletteCellMargin * 2));
            table.Anchor = AnchorStyles.None;
            table.ResumeLayout();
        }

        private void WirePaletteInteractions()
        {
            tableStandardColors.MouseClick += tableStandardColors_MouseClick;
            tableStandardColors.MouseMove += tableStandardColors_MouseMove;
            tableStandardColors.MouseLeave += tableStandardColors_MouseLeave;
            tableStandardColors.Paint += tableStandardColors_Paint;

            tableCustomColors.MouseMove += tableCustomColors_MouseMove;
            tableCustomColors.MouseLeave += tableCustomColors_MouseLeave;
            tableCustomColors.Paint += tableCustomColors_Paint;
        }

        private void tableStandardColors_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            int index = GetPaletteIndexAtPoint(tableStandardColors, e.Location, _standardColors.Count);
            if (index >= 0)
            {
                ApplySelectedColor(_standardColors[index].Color);
            }
        }

        private void tableStandardColors_MouseMove(object? sender, MouseEventArgs e)
        {
            int hoveredIndex = GetPaletteIndexAtPoint(tableStandardColors, e.Location, _standardColors.Count);
            SetStandardHoveredIndex(hoveredIndex);
        }

        private void tableStandardColors_MouseLeave(object? sender, EventArgs e)
        {
            if (_standardHoveredIndex < 0)
            {
                return;
            }

            SetStandardHoveredIndex(-1);
        }

        private void tableCustomColors_MouseMove(object? sender, MouseEventArgs e)
        {
            int hoveredIndex = GetPaletteIndexAtPoint(tableCustomColors, e.Location, _customColorCells.Count);
            SetCustomHoveredIndex(hoveredIndex);
        }

        private void tableCustomColors_MouseLeave(object? sender, EventArgs e)
        {
            if (_customHoveredIndex < 0)
            {
                return;
            }

            SetCustomHoveredIndex(-1);
        }

        private void tableStandardColors_Paint(object? sender, PaintEventArgs e)
        {
            if (_standardHoveredIndex >= 0 && _standardHoveredIndex < _standardColorCells.Count)
            {
                _standardColorCells[_standardHoveredIndex].Invalidate();
            }
        }

        private void tableCustomColors_Paint(object? sender, PaintEventArgs e)
        {
            if (_customHoveredIndex >= 0 && _customHoveredIndex < _customColorCells.Count)
            {
                _customColorCells[_customHoveredIndex].Invalidate();
            }
        }

        private void StandardColorCell_Paint(object? sender, PaintEventArgs e)
        {
            if (sender is not Panel panel || !_standardCellEntries.TryGetValue(panel, out _))
            {
                return;
            }

            if (!_standardColorCells.Contains(panel))
            {
                return;
            }

            int index = _standardColorCells.IndexOf(panel);
            if (index != _standardHoveredIndex)
            {
                return;
            }

            DrawHoverFrame(e.Graphics, panel.ClientRectangle);
        }

        private void CustomColorCell_Paint(object? sender, PaintEventArgs e)
        {
            if (sender is not Panel panel || !_customCellIndexes.TryGetValue(panel, out int index))
            {
                return;
            }

            if (index != _customHoveredIndex)
            {
                return;
            }

            DrawHoverFrame(e.Graphics, panel.ClientRectangle);
        }

        private static void DrawHoverFrame(Graphics graphics, Rectangle bounds)
        {
            using Pen hoverPen = new(ThemeManager.GetInteractiveBorderColor(ThemeManager.CurrentDefinition, ThemeManager.CurrentDefinition.PanelBackground, hovered: true), 2f);
            Rectangle hoverRect = Rectangle.Inflate(bounds, -1, -1);
            hoverRect.Width = Math.Max(1, hoverRect.Width - 1);
            hoverRect.Height = Math.Max(1, hoverRect.Height - 1);
            graphics.DrawRectangle(hoverPen, hoverRect);
        }

        private static int GetPaletteIndexAtPoint(TableLayoutPanel table, Point location, int itemCount)
        {
            int cellPitch = PaletteCellSize + (PaletteCellMargin * 2);
            if (location.X < 0 || location.Y < 0)
            {
                return -1;
            }

            int column = location.X / cellPitch;
            int row = location.Y / cellPitch;
            if (column < 0 || column >= table.ColumnCount || row < 0 || row >= table.RowCount)
            {
                return -1;
            }

            int index = row * table.ColumnCount + column;
            return index >= 0 && index < itemCount ? index : -1;
        }

        private static Rectangle GetPaletteCellBounds(TableLayoutPanel table, int index)
        {
            if (index < 0)
            {
                return Rectangle.Empty;
            }

            int col = index % table.ColumnCount;
            int row = index / table.ColumnCount;
            if (row >= table.RowCount)
            {
                return Rectangle.Empty;
            }

            return GetCellPositionBounds(table, col, row);
        }

        private static Rectangle GetCellPositionBounds(TableLayoutPanel table, int column, int row)
        {
            int x = (PaletteCellSize + (PaletteCellMargin * 2)) * column;
            int y = (PaletteCellSize + (PaletteCellMargin * 2)) * row;
            return new Rectangle(x + PaletteCellMargin, y + PaletteCellMargin, PaletteCellSize, PaletteCellSize);
        }

        private void InvalidatePaletteHoverVisuals()
        {
            InvalidatePaletteCell(tableStandardColors, _standardHoveredIndex);
            InvalidatePaletteCell(tableCustomColors, _customHoveredIndex);
        }

        private static void InvalidatePaletteCell(TableLayoutPanel table, int index)
        {
            if (index < 0)
            {
                return;
            }

            Rectangle bounds = GetPaletteCellBounds(table, index);
            if (!bounds.IsEmpty)
            {
                table.Invalidate(Rectangle.Inflate(bounds, 3, 3));
            }
        }

        private void SetStandardHoveredIndex(int hoveredIndex)
        {
            if (_standardHoveredIndex == hoveredIndex)
            {
                return;
            }

            int previous = _standardHoveredIndex;
            _standardHoveredIndex = hoveredIndex;
            InvalidatePaletteCell(tableStandardColors, previous);
            InvalidatePaletteCell(tableStandardColors, _standardHoveredIndex);
            if (previous >= 0 && previous < _standardColorCells.Count)
            {
                _standardColorCells[previous].Invalidate();
            }

            if (_standardHoveredIndex >= 0 && _standardHoveredIndex < _standardColorCells.Count)
            {
                _standardColorCells[_standardHoveredIndex].Invalidate();
            }
        }

        private void SetCustomHoveredIndex(int hoveredIndex)
        {
            if (_customHoveredIndex == hoveredIndex)
            {
                return;
            }

            int previous = _customHoveredIndex;
            _customHoveredIndex = hoveredIndex;
            InvalidatePaletteCell(tableCustomColors, previous);
            InvalidatePaletteCell(tableCustomColors, _customHoveredIndex);
            if (previous >= 0 && previous < _customColorCells.Count)
            {
                _customColorCells[previous].Invalidate();
            }

            if (_customHoveredIndex >= 0 && _customHoveredIndex < _customColorCells.Count)
            {
                _customColorCells[_customHoveredIndex].Invalidate();
            }
        }

        private void LoadCustomColors()
        {
            string serialized = Settings.Default.ColorPicker_CustomColors;
            if (string.IsNullOrWhiteSpace(serialized))
            {
                Array.Fill(_customColors, Color.Empty);
                return;
            }

            string[] chunks = serialized.Split(';');
            for (int i = 0; i < _customColors.Length; i++)
            {
                if (i < chunks.Length && int.TryParse(chunks[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int argb))
                {
                    _customColors[i] = argb == int.MinValue ? Color.Empty : Color.FromArgb(argb);
                }
                else
                {
                    _customColors[i] = Color.Empty;
                }
            }
        }

        private void SaveCustomColors()
        {
            string serialized = string.Join(";", _customColors.Select(c => (c == Color.Empty ? int.MinValue : c.ToArgb()).ToString(CultureInfo.InvariantCulture)));
            Settings.Default.ColorPicker_CustomColors = serialized;
            Settings.Default.Save();
        }

        private static string ToHexString(Color color)
        {
            return color.A == byte.MaxValue
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private static bool TryParseHexColor(string? text, out Color color)
        {
            color = Color.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string normalized = text.Trim();
            if (normalized.StartsWith("#", StringComparison.Ordinal))
            {
                normalized = normalized[1..];
            }

            if (normalized.Length == 6
                && byte.TryParse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r6)
                && byte.TryParse(normalized.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g6)
                && byte.TryParse(normalized.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b6))
            {
                color = Color.FromArgb(r6, g6, b6);
                return true;
            }

            if (normalized.Length == 8
                && byte.TryParse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte a8)
                && byte.TryParse(normalized.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r8)
                && byte.TryParse(normalized.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g8)
                && byte.TryParse(normalized.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b8))
            {
                color = Color.FromArgb(a8, r8, g8, b8);
                return true;
            }

            return false;
        }

        private void btnSetTransparent_Click(object? sender, EventArgs e)
        {
            ApplySelectedColor(Color.FromArgb(0, _selectedColor.R, _selectedColor.G, _selectedColor.B));
        }

        private static Color FromAhsb(int alpha, float hue, float saturation, float brightness)
        {
            if (0 > alpha || 255 < alpha)
            {
                throw new ArgumentOutOfRangeException(nameof(alpha));
            }

            if (0f > hue || 360f < hue)
            {
                throw new ArgumentOutOfRangeException(nameof(hue));
            }

            if (0f > saturation || 1f < saturation)
            {
                throw new ArgumentOutOfRangeException(nameof(saturation));
            }

            if (0f > brightness || 1f < brightness)
            {
                throw new ArgumentOutOfRangeException(nameof(brightness));
            }

            if (0 == saturation)
            {
                int l = Convert.ToInt32(brightness * 255);
                return Color.FromArgb(alpha, l, l, l);
            }

            float fMax;
            float fMid;
            float fMin;

            int sextant;
            if (0.5 < brightness)
            {
                fMax = brightness - (brightness * saturation) + saturation;
                fMin = brightness + (brightness * saturation) - saturation;
            }
            else
            {
                fMax = brightness + (brightness * saturation);
                fMin = brightness - (brightness * saturation);
            }

            sextant = (int)Math.Floor(hue / 60f);
            if (300f <= hue)
            {
                hue -= 360f;
            }

            hue /= 60f;
            hue -= 2f * (float)Math.Floor(((sextant + 1f) % 6f) / 2f);
            if (0 == sextant % 2)
            {
                fMid = hue * (fMax - fMin) + fMin;
            }
            else
            {
                fMid = fMin - hue * (fMax - fMin);
            }

            int iMax = Convert.ToInt32(fMax * 255);
            int iMid = Convert.ToInt32(fMid * 255);
            int iMin = Convert.ToInt32(fMin * 255);

            return sextant switch
            {
                1 => Color.FromArgb(alpha, iMid, iMax, iMin),
                2 => Color.FromArgb(alpha, iMin, iMax, iMid),
                3 => Color.FromArgb(alpha, iMin, iMid, iMax),
                4 => Color.FromArgb(alpha, iMid, iMin, iMax),
                5 => Color.FromArgb(alpha, iMax, iMin, iMid),
                _ => Color.FromArgb(alpha, iMax, iMid, iMin)
            };
        }

        private sealed record PaletteColorEntry(Color Color, string? Name);
    }
}
