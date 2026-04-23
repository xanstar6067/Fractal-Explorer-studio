using FractalExplorer.Utilities.Theme;
using System.ComponentModel;

namespace FractalExplorer.Controls
{
    /// <summary>
    /// Аккордеон-контрол для отображения каталога фракталов, сгруппированных по категориям.
    /// Заменяет TreeView в LauncherHubForm, поддерживает hover-эффекты и интеграцию с ThemeManager.
    /// </summary>
    public sealed class FractalAccordionPanel : UserControl
    {
        /// <summary>
        /// Срабатывает при выборе фрактала пользователем.
        /// <see cref="AccordionItemEventArgs.Tag"/> содержит тот же объект, что раньше лежал в TreeNode.Tag.
        /// </summary>
        public event EventHandler<AccordionItemEventArgs>? ItemSelected;

        /// <summary>
        /// Срабатывает при двойном клике на элемент (аналог NodeMouseDoubleClick).
        /// </summary>
        public event EventHandler<AccordionItemEventArgs>? ItemDoubleClicked;

        /// <summary>Данные одного фрактала, передаваемые в контрол.</summary>
        public sealed class AccordionEntry
        {
            public required string Category { get; init; }
            public required string DisplayName { get; init; }

            /// <summary>Произвольный объект (FractalInfo) — возвращается в событиях.</summary>
            public object? Tag { get; init; }
        }

        public sealed class AccordionItemEventArgs : EventArgs
        {
            public string DisplayName { get; }
            public object? Tag { get; }

            internal AccordionItemEventArgs(string displayName, object? tag)
            {
                DisplayName = displayName;
                Tag = tag;
            }
        }

        private readonly List<CategoryBlock> _blocks = new();
        private readonly HashSet<int> _openBlockIndices = new();
        private ItemRow? _hoveredItem;
        private ItemRow? _selectedItem;
        private ThemeColors _colors;

        public FractalAccordionPanel()
        {
            typeof(Control)
                .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(this, true);

            AutoScroll = true;
            Padding = new Padding(8, 6, 8, 6);
            BackColor = Color.Transparent;

            _colors = BuildColors();

            ThemeManager.ThemeChanged += OnThemeChanged;
            Disposed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
        }

        /// <summary>
        /// Заполняет контрол списком фракталов. Вызывать один раз после InitializeComponent.
        /// </summary>
        public void Populate(IEnumerable<AccordionEntry> entries)
        {
            _blocks.Clear();
            _hoveredItem = null;
            _selectedItem = null;
            _openBlockIndices.Clear();

            var grouped = new Dictionary<string, List<AccordionEntry>>();
            var order = new List<string>();

            foreach (AccordionEntry entry in entries)
            {
                if (!grouped.TryGetValue(entry.Category, out List<AccordionEntry>? items))
                {
                    items = new List<AccordionEntry>();
                    grouped[entry.Category] = items;
                    order.Add(entry.Category);
                }

                items.Add(entry);
            }

            foreach (string category in order)
            {
                _blocks.Add(new CategoryBlock(category, grouped[category]));
            }

            if (_blocks.Count > 0)
            {
                _openBlockIndices.Add(0);
            }

            RebuildLayout();
        }

        /// <summary>
        /// Выбирает первый доступный элемент списка (первый фрактал первой категории).
        /// </summary>
        public void SelectFirstItem(bool ensureVisible = false)
        {
            if (_blocks.Count == 0)
            {
                return;
            }

            if (!_openBlockIndices.Contains(0))
            {
                _openBlockIndices.Add(0);
            }

            RebuildLayout();

            ItemRow? firstItem = _blocks[0].ItemControls.FirstOrDefault();
            if (firstItem == null)
            {
                return;
            }

            OnItemClicked(firstItem);

            if (ensureVisible)
            {
                ScrollControlIntoView(firstItem);
            }
        }

        private void RebuildLayout()
        {
            Point previousScroll = AutoScrollPosition;
            SuspendLayout();
            Controls.Clear();
            AutoScrollMinSize = Size.Empty;

            int y = Padding.Top;
            const int blockGap = 4;

            for (int bi = 0; bi < _blocks.Count; bi++)
            {
                CategoryBlock block = _blocks[bi];
                bool isOpen = _openBlockIndices.Contains(bi);

                CategoryHeader header = new(block.Category, isOpen, _colors);
                int capturedIndex = bi;
                header.Click += (_, _) => OnCategoryHeaderClicked(capturedIndex);
                header.Location = new Point(0, y);
                header.Width = ClientSize.Width - Padding.Horizontal;
                header.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
                Controls.Add(header);
                block.HeaderControl = header;
                y += header.Height;

                block.ItemControls.Clear();
                if (isOpen)
                {
                    foreach (AccordionEntry entry in block.Entries)
                    {
                        ItemRow row = new(entry, _colors);
                        row.Location = new Point(0, y);
                        row.Width = ClientSize.Width - Padding.Horizontal;
                        row.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;

                        row.MouseEnter += (_, _) => OnItemMouseEnter(row);
                        row.MouseLeave += (_, _) => OnItemMouseLeave(row);
                        row.Click += (_, _) => OnItemClicked(row);
                        row.DoubleClick += (_, _) => OnItemDoubleClicked(row);

                        Controls.Add(row);
                        block.ItemControls.Add(row);
                        y += row.Height;
                    }

                    y += 4;
                }

                y += blockGap;
            }

            if (_selectedItem != null)
            {
                foreach (CategoryBlock b in _blocks)
                {
                    foreach (ItemRow r in b.ItemControls)
                    {
                        if (r.Entry == _selectedItem.Entry)
                        {
                            _selectedItem = r;
                            r.IsSelected = true;
                        }
                    }
                }
            }

            AutoScrollMinSize = new Size(0, y + Padding.Bottom);

            int previousScrollY = Math.Abs(previousScroll.Y);
            int maxScrollY = Math.Max(0, AutoScrollMinSize.Height - ClientSize.Height);
            int clampedScrollY = Math.Min(previousScrollY, maxScrollY);
            AutoScrollPosition = new Point(0, clampedScrollY);

            ResumeLayout(true);
        }

        private void OnCategoryHeaderClicked(int blockIndex)
        {
            if (!_openBlockIndices.Remove(blockIndex))
            {
                _openBlockIndices.Add(blockIndex);
            }

            RebuildLayout();
        }

        private void OnItemMouseEnter(ItemRow row)
        {
            if (_hoveredItem == row)
            {
                return;
            }

            _hoveredItem = row;
            row.IsHovered = true;
            row.Invalidate();
        }

        private void OnItemMouseLeave(ItemRow row)
        {
            if (_hoveredItem != row)
            {
                return;
            }

            _hoveredItem = null;
            row.IsHovered = false;
            row.Invalidate();
        }

        private void OnItemClicked(ItemRow row)
        {
            if (_selectedItem != null && _selectedItem != row)
            {
                _selectedItem.IsSelected = false;
                _selectedItem.Invalidate();
            }

            _selectedItem = row;
            row.IsSelected = true;
            row.Invalidate();

            ItemSelected?.Invoke(this, new AccordionItemEventArgs(row.Entry.DisplayName, row.Entry.Tag));
        }

        private void OnItemDoubleClicked(ItemRow row)
        {
            ItemDoubleClicked?.Invoke(this, new AccordionItemEventArgs(row.Entry.DisplayName, row.Entry.Tag));
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(OnThemeChanged, sender, e);
                return;
            }

            _colors = BuildColors();

            foreach (CategoryBlock b in _blocks)
            {
                b.HeaderControl?.ApplyColors(_colors);
                foreach (ItemRow r in b.ItemControls)
                {
                    r.ApplyColors(_colors);
                }
            }

            Invalidate(true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            foreach (Control c in Controls)
            {
                c.Width = ClientSize.Width - Padding.Horizontal;
            }
        }

        private static ThemeColors BuildColors()
        {
            ThemeDefinition t = ThemeManager.CurrentDefinition;
            return new ThemeColors(
                Accent: t.AccentPrimary,
                Hover: t.HoverBackground,
                SelectedText: t.PrimaryText,
                NormalText: t.SecondaryText,
                CategoryText: t.SecondaryText,
                CategoryTextOpen: t.PrimaryText,
                Separator: t.BorderColor,
                CategoryBg: t.ControlBackground,
                CategoryBgOpen: t.PanelBackground
            );
        }

        private sealed class CategoryBlock
        {
            public string Category { get; }
            public List<AccordionEntry> Entries { get; }
            public List<ItemRow> ItemControls { get; } = new();
            public CategoryHeader? HeaderControl { get; set; }

            public CategoryBlock(string category, List<AccordionEntry> entries)
            {
                Category = category;
                Entries = entries;
            }
        }

        private readonly record struct ThemeColors(
            Color Accent,
            Color Hover,
            Color SelectedText,
            Color NormalText,
            Color CategoryText,
            Color CategoryTextOpen,
            Color Separator,
            Color CategoryBg,
            Color CategoryBgOpen
        );

        /// <summary>Заголовок категории — кнопка-строка с индикатором открытия.</summary>
        private sealed class CategoryHeader : Control
        {
            private readonly bool _isOpen;
            private ThemeColors _colors;
            private bool _isHovered;

            private const int HeightPx = 34;
            private const int ArrowSize = 6;
            private const int HIndent = 10;

            public CategoryHeader(string text, bool isOpen, ThemeColors colors)
            {
                Text = text;
                _isOpen = isOpen;
                _colors = colors;

                Height = HeightPx;
                Cursor = Cursors.Hand;
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Regular);

                typeof(Control)
                    .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(this, true);

                MouseEnter += (_, _) => { _isHovered = true; Invalidate(); };
                MouseLeave += (_, _) => { _isHovered = false; Invalidate(); };
            }

            public void ApplyColors(ThemeColors colors)
            {
                _colors = colors;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Color bg = _isOpen
                    ? (_isHovered ? Blend(_colors.CategoryBgOpen, _colors.Hover, 0.35d) : _colors.CategoryBgOpen)
                    : (_isHovered ? _colors.Hover : _colors.CategoryBg);
                g.Clear(bg);

                if (_isOpen)
                {
                    using SolidBrush accent = new(_colors.Accent);
                    g.FillRectangle(accent, 0, 4, 3, Height - 8);
                }

                Color sepColor = _isOpen ? Color.FromArgb(80, _colors.Separator) : Color.FromArgb(45, _colors.Separator);
                using (Pen sep = new(sepColor, 1))
                {
                    g.DrawLine(sep, 0, Height - 1, Width, Height - 1);
                }

                Color textColor = _isOpen ? _colors.CategoryTextOpen : _colors.CategoryText;
                TextRenderer.DrawText(
                    g,
                    Text,
                    Font,
                    new Rectangle(HIndent + (_isOpen ? 6 : 0), 0, Width - HIndent * 2 - ArrowSize - 12, Height),
                    textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

                DrawArrow(g, _isOpen, textColor);
            }

            private static Color Blend(Color bottom, Color top, double topWeight)
            {
                topWeight = Math.Clamp(topWeight, 0d, 1d);
                double bottomWeight = 1d - topWeight;

                int a = (int)Math.Round(bottom.A * bottomWeight + top.A * topWeight);
                int r = (int)Math.Round(bottom.R * bottomWeight + top.R * topWeight);
                int g = (int)Math.Round(bottom.G * bottomWeight + top.G * topWeight);
                int b = (int)Math.Round(bottom.B * bottomWeight + top.B * topWeight);

                return Color.FromArgb(a, r, g, b);
            }

            private void DrawArrow(Graphics g, bool open, Color color)
            {
                int cx = Width - 16;
                int cy = Height / 2;

                using Pen pen = new(color, 1.5f)
                {
                    LineJoin = System.Drawing.Drawing2D.LineJoin.Round
                };

                if (open)
                {
                    g.DrawLine(pen, cx - ArrowSize / 2, cy - 2, cx, cy + 3);
                    g.DrawLine(pen, cx, cy + 3, cx + ArrowSize / 2, cy - 2);
                }
                else
                {
                    g.DrawLine(pen, cx - 2, cy - ArrowSize / 2, cx + 3, cy);
                    g.DrawLine(pen, cx + 3, cy, cx - 2, cy + ArrowSize / 2);
                }
            }
        }

        /// <summary>Строка одного фрактала внутри раскрытой категории.</summary>
        private sealed class ItemRow : Control
        {
            public AccordionEntry Entry { get; }
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public bool IsSelected { get; set; }
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public bool IsHovered { get; set; }

            private ThemeColors _colors;

            private const int HeightPx = 30;
            private const int TextIndent = 22;

            public ItemRow(AccordionEntry entry, ThemeColors colors)
            {
                Entry = entry;
                _colors = colors;

                Text = entry.DisplayName;
                Height = HeightPx;
                Cursor = Cursors.Hand;
                Font = new Font("Segoe UI", 10f);

                typeof(Control)
                    .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(this, true);
            }

            public void ApplyColors(ThemeColors colors)
            {
                _colors = colors;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Color bg;
                if (IsSelected)
                {
                    bg = _colors.Accent;
                }
                else if (IsHovered)
                {
                    bg = _colors.Hover;
                }
                else
                {
                    bg = Color.Transparent;
                }

                g.Clear(bg == Color.Transparent ? BackColor : bg);

                if (bg != Color.Transparent && bg != BackColor)
                {
                    using SolidBrush bgBrush = new(bg);
                    g.FillRectangle(bgBrush, 0, 0, Width, Height);
                }

                Color sepColor = Color.FromArgb(30, _colors.Separator);
                using (Pen sep = new(sepColor, 1))
                {
                    g.DrawLine(sep, TextIndent, Height - 1, Width - 8, Height - 1);
                }

                if (IsSelected)
                {
                    using SolidBrush dot = new(_colors.SelectedText);
                    int dotY = Height / 2 - 2;
                    g.FillEllipse(dot, TextIndent - 10, dotY, 4, 4);
                }

                Color textColor = IsSelected ? _colors.SelectedText : _colors.NormalText;
                TextRenderer.DrawText(
                    g,
                    Text,
                    Font,
                    new Rectangle(TextIndent, 0, Width - TextIndent - 8, Height),
                    textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
