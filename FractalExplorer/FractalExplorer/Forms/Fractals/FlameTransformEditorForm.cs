using FractalExplorer.Engines;
using FractalExplorer.Forms.Common;
using FractalExplorer.Utilities.Theme;

namespace FractalExplorer.Forms.Fractals
{
    /// <summary>
    /// Редактор трансформаций Flame-фрактала.
    /// Паттерн master/detail: слева список карточек, справа редактор выбранной трансформации.
    /// </summary>
    public sealed partial class FlameTransformEditorForm : Form
    {
        // ── данные ────────────────────────────────────────────────────────────
        private readonly List<FlameTransform> _transforms;
        private int _selectedIndex = -1;
        private bool _suppressSync;   // подавляет рекурсивные обновления
        private readonly Stack<EditorSnapshot> _undoStack = new();

        // ── карточки в списке ─────────────────────────────────────────────────
        private readonly List<TransformCard> _cards = new();

        public List<FlameTransform> ResultTransforms { get; private set; }
        public event Action<IReadOnlyList<FlameTransform>>? TransformsApplied;

        // ─────────────────────────────────────────────────────────────────────
        public FlameTransformEditorForm(IEnumerable<FlameTransform> transforms)
        {
            _transforms = transforms.Select(t => t.Clone()).ToList();
            ResultTransforms = _transforms; // обновится в CommitAndClose

            InitializeComponent();

            ThemeManager.RegisterForm(this);
            ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
            Disposed += (_, _) => ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;

            WireVariationCombo();
            WireEvents();
            RebuildList();

            if (_transforms.Count > 0)
                SelectTransform(0);
            else
                SetEditorEnabled(false);
        }

        private void WireVariationCombo()
        {
            _cmbVariation.BeginUpdate();
            try
            {
                _cmbVariation.DataSource = null;
                _cmbVariation.Items.Clear();
                _cmbVariation.Items.AddRange(new object[]
                {
                    FlameVariation.Linear,
                    FlameVariation.Sinusoidal,
                    FlameVariation.Spherical
                });
            }
            finally
            {
                _cmbVariation.EndUpdate();
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Подписка на события
        // ══════════════════════════════════════════════════════════════════════
        private void WireEvents()
        {
            _btnAdd.Click += BtnAdd_Click;
            _btnUndo.Click += BtnUndo_Click;
            _btnOk.Click += (_, _) => CommitChanges();
            _btnClose.Click += (_, _) => CommitAndClose();

            _cmbVariation.SelectedIndexChanged += EditorControl_Changed;
            _btnPickColor.Click += BtnPickColor_Click;
            _pnlColorPreview.Click += BtnPickColor_Click;

            _trkWeight.Scroll += TrkWeight_Scroll;

            _nudA.ValueChanged += EditorControl_Changed;
            _nudB.ValueChanged += EditorControl_Changed;
            _nudC.ValueChanged += EditorControl_Changed;
            _nudD.ValueChanged += EditorControl_Changed;
            _nudE.ValueChanged += EditorControl_Changed;
            _nudF.ValueChanged += EditorControl_Changed;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Построение списка карточек
        // ══════════════════════════════════════════════════════════════════════
        private void RebuildList()
        {
            _listContainer.SuspendLayout();
            _listContainer.Controls.Clear();
            _cards.Clear();

            // Карточки добавляются снизу вверх из-за DockStyle.Top + обратный порядок
            for (int i = _transforms.Count - 1; i >= 0; i--)
            {
                int capturedIndex = i;
                var card = new TransformCard(_transforms[i], i + 1);
                card.Clicked += () => SelectTransform(capturedIndex);
                card.DeleteClicked += () => DeleteTransform(capturedIndex);
                _cards.Insert(0, card);
                _listContainer.Controls.Add(card);
            }

            ApplyThemeToCards();
            UpdateTotalWeightLabel();
            _listContainer.ResumeLayout();
        }

        private void RefreshCardAt(int index)
        {
            if (index < 0 || index >= _cards.Count) return;
            _cards[index].UpdateFrom(_transforms[index]);
            UpdateTotalWeightLabel();
        }

        private void UpdateTotalWeightLabel()
        {
            double total = _transforms.Sum(t => t.Weight);
            _lblTotalWeight.Text = $"Суммарный вес: {total:F2}";

            // Подсвечиваем если сумма сильно отличается от 1.0 (движок нормализует, но пусть знает)
            bool ok = Math.Abs(total - 1.0) < 0.001 || _transforms.Count == 0;
            _lblTotalWeight.ForeColor = ok
                ? ThemeManager.CurrentDefinition.SecondaryText
                : Color.OrangeRed;
        }

        private void UpdateCardSelection()
        {
            for (int i = 0; i < _cards.Count; i++)
                _cards[i].SetSelected(i == _selectedIndex);
        }

        // ══════════════════════════════════════════════════════════════════════
        // Выбор трансформации
        // ══════════════════════════════════════════════════════════════════════
        private void SelectTransform(int index)
        {
            if (index < 0 || index >= _transforms.Count) return;

            _selectedIndex = index;
            UpdateCardSelection();
            LoadEditorFromTransform(_transforms[index]);
            SetEditorEnabled(true);
            _lblEditorTitle.Text = $"Трансформация {index + 1}";
        }

        // ══════════════════════════════════════════════════════════════════════
        // Загрузка / сохранение значений в редакторе
        // ══════════════════════════════════════════════════════════════════════
        private void LoadEditorFromTransform(FlameTransform t)
        {
            _suppressSync = true;
            try
            {
                _cmbVariation.SelectedItem = t.Variation;

                _pnlColorPreview.BackColor = t.Color;

                int sliderVal = (int)Math.Round(Math.Clamp(t.Weight * 10.0, 1.0, 100.0));
                _trkWeight.Value = sliderVal;
                UpdateWeightLabels(sliderVal);

                SetNud(_nudA, t.A);
                SetNud(_nudB, t.B);
                SetNud(_nudC, t.C);
                SetNud(_nudD, t.D);
                SetNud(_nudE, t.E);
                SetNud(_nudF, t.F);
            }
            finally
            {
                _suppressSync = false;
            }
        }

        private void SaveEditorToTransform(FlameTransform t)
        {
            t.Variation = _cmbVariation.SelectedItem is FlameVariation v ? v : FlameVariation.Linear;
            t.Color = _pnlColorPreview.BackColor;
            t.Weight = _trkWeight.Value / 10.0;
            t.A = (double)_nudA.Value;
            t.B = (double)_nudB.Value;
            t.C = (double)_nudC.Value;
            t.D = (double)_nudD.Value;
            t.E = (double)_nudE.Value;
            t.F = (double)_nudF.Value;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Обработчики событий редактора
        // ══════════════════════════════════════════════════════════════════════
        private void EditorControl_Changed(object? sender, EventArgs e)
        {
            if (_suppressSync || _selectedIndex < 0) return;
            PushUndoSnapshot();
            SaveEditorToTransform(_transforms[_selectedIndex]);
            RefreshCardAt(_selectedIndex);
        }

        private void TrkWeight_Scroll(object? sender, EventArgs e)
        {
            UpdateWeightLabels(_trkWeight.Value);
            EditorControl_Changed(sender, e);
        }

        private void UpdateWeightLabels(int sliderValue)
        {
            double w = sliderValue / 10.0;
            _lblWeightValue.Text = w.ToString("F1");

            double total = _transforms.Count > 0 ? _transforms.Sum(t => t.Weight) : 1.0;
            // Если выбрана трансформация — считаем долю относительно текущей суммы
            if (_selectedIndex >= 0 && total > 0)
            {
                double others = total - _transforms[_selectedIndex].Weight;
                double projected = others + w;
                int pct = projected > 0 ? (int)Math.Round(w / projected * 100) : 0;
                _lblWeightPercent.Text = $"{pct}%";
            }
            else
            {
                _lblWeightPercent.Text = "—";
            }
        }

        private void BtnPickColor_Click(object? sender, EventArgs e)
        {
            if (_selectedIndex < 0) return;

            using var dlg = new ColorPickerPanelForm(_pnlColorPreview.BackColor);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _pnlColorPreview.BackColor = dlg.SelectedColor;
                EditorControl_Changed(sender, e);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Добавление / удаление
        // ══════════════════════════════════════════════════════════════════════
        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            PushUndoSnapshot();
            var t = new FlameTransform
            {
                Weight = 1.0,
                A = 0.5,
                B = 0.0,
                C = 0.0,
                D = 0.0,
                E = 0.5,
                F = 0.0,
                Variation = FlameVariation.Linear,
                Color = Color.White
            };
            _transforms.Add(t);
            RebuildList();
            SelectTransform(_transforms.Count - 1);
        }

        private void DeleteTransform(int index)
        {
            if (index < 0 || index >= _transforms.Count) return;

            PushUndoSnapshot();
            _transforms.RemoveAt(index);

            int newIndex = _transforms.Count == 0
                ? -1
                : Math.Min(index, _transforms.Count - 1);

            RebuildList();

            if (newIndex >= 0)
                SelectTransform(newIndex);
            else
            {
                _selectedIndex = -1;
                SetEditorEnabled(false);
                _lblEditorTitle.Text = "Редактор трансформации";
            }
        }

        private void BtnUndo_Click(object? sender, EventArgs e) => UndoLastAction();

        // ══════════════════════════════════════════════════════════════════════
        // Включение / выключение редактора
        // ══════════════════════════════════════════════════════════════════════
        private void SetEditorEnabled(bool enabled)
        {
            _cmbVariation.Enabled = enabled;
            _btnPickColor.Enabled = enabled;
            _pnlColorPreview.Enabled = enabled;
            _trkWeight.Enabled = enabled;
            _nudA.Enabled = enabled;
            _nudB.Enabled = enabled;
            _nudC.Enabled = enabled;
            _nudD.Enabled = enabled;
            _nudE.Enabled = enabled;
            _nudF.Enabled = enabled;

            if (!enabled)
            {
                _lblEditorTitle.Text = "Нет трансформаций — нажмите «+ Добавить»";
                _pnlColorPreview.BackColor = ThemeManager.CurrentDefinition.PanelBackground;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Применение / закрытие
        // ══════════════════════════════════════════════════════════════════════
        private void CommitChanges()
        {
            // Сохраняем текущий редактор если есть выбранная трансформация
            if (_selectedIndex >= 0 && _selectedIndex < _transforms.Count)
                SaveEditorToTransform(_transforms[_selectedIndex]);

            ResultTransforms = _transforms
                .Where(t => t.Weight > 0)
                .Select(t => t.Clone())
                .ToList();

            TransformsApplied?.Invoke(ResultTransforms);
        }

        private void CommitAndClose()
        {
            CommitChanges();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void PushUndoSnapshot()
        {
            var snapshot = new EditorSnapshot(_transforms, _selectedIndex);
            if (_undoStack.Count > 0 && _undoStack.Peek().HasSameState(_transforms, _selectedIndex))
                return;
            _undoStack.Push(snapshot);
            UpdateUndoButtonState();
        }

        private void UndoLastAction()
        {
            if (_undoStack.Count == 0) return;

            var snapshot = _undoStack.Pop();
            _transforms.Clear();
            _transforms.AddRange(snapshot.Transforms.Select(t => t.Clone()));

            RebuildList();
            if (_transforms.Count == 0)
            {
                _selectedIndex = -1;
                SetEditorEnabled(false);
                _lblEditorTitle.Text = "Редактор трансформации";
            }
            else
            {
                int indexToSelect = Math.Clamp(snapshot.SelectedIndex, 0, _transforms.Count - 1);
                SelectTransform(indexToSelect);
            }

            UpdateUndoButtonState();
        }

        private void UpdateUndoButtonState()
        {
            _btnUndo.Enabled = _undoStack.Count > 0;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Тема
        // ══════════════════════════════════════════════════════════════════════
        private void ThemeManager_ThemeChanged(object? sender, EventArgs e)
        {
            ApplyThemeToCards();
            UpdateTotalWeightLabel();

            // Разделитель — цвет границы из темы
            _divider.BackColor = ThemeManager.CurrentDefinition.BorderColor;
            _pnlHint.BackColor = ThemeManager.CurrentDefinition.ControlBackground;
            _lblHint.ForeColor = ThemeManager.CurrentDefinition.SecondaryText;
            _lblAffineCaption.ForeColor = ThemeManager.CurrentDefinition.SecondaryText;
            _lblVariationCaption.ForeColor = ThemeManager.CurrentDefinition.SecondaryText;
            _lblWeightCaption.ForeColor = ThemeManager.CurrentDefinition.SecondaryText;
            _lblAffineFormula.ForeColor = ThemeManager.CurrentDefinition.SecondaryText;

            // Сплиттер — цвет рамки
            _split.BackColor = ThemeManager.CurrentDefinition.BorderColor;
        }

        private sealed class EditorSnapshot
        {
            public IReadOnlyList<FlameTransform> Transforms { get; }
            public int SelectedIndex { get; }

            public EditorSnapshot(IEnumerable<FlameTransform> transforms, int selectedIndex)
            {
                Transforms = transforms.Select(t => t.Clone()).ToList();
                SelectedIndex = selectedIndex;
            }

            public bool HasSameState(IReadOnlyList<FlameTransform> transforms, int selectedIndex)
            {
                if (selectedIndex != SelectedIndex || transforms.Count != Transforms.Count) return false;

                for (int i = 0; i < transforms.Count; i++)
                {
                    var left = transforms[i];
                    var right = Transforms[i];
                    if (left.Variation != right.Variation ||
                        left.Color != right.Color ||
                        left.Weight != right.Weight ||
                        left.A != right.A ||
                        left.B != right.B ||
                        left.C != right.C ||
                        left.D != right.D ||
                        left.E != right.E ||
                        left.F != right.F)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private void ApplyThemeToCards()
        {
            var theme = ThemeManager.CurrentDefinition;
            foreach (var card in _cards)
                card.ApplyTheme(theme);
            UpdateCardSelection();
        }

        // ══════════════════════════════════════════════════════════════════════
        // Вспомогательные
        // ══════════════════════════════════════════════════════════════════════
        private static void SetNud(NumericUpDown nud, double value)
        {
            decimal clamped = (decimal)Math.Clamp(value, (double)nud.Minimum, (double)nud.Maximum);
            nud.Value = clamped;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Вложенный класс: карточка трансформации в списке
        // ══════════════════════════════════════════════════════════════════════
        private sealed class TransformCard : Panel
        {
            private readonly Panel _colorDot;
            private readonly Label _lblName;
            private readonly Label _lblMeta;
            private readonly Panel _weightBar;
            private readonly Panel _weightFill;
            private readonly Button _btnDel;

            public event Action? Clicked;
            public event Action? DeleteClicked;

            private bool _isSelected;

            public TransformCard(FlameTransform t, int number)
            {
                Dock = DockStyle.Top;
                Height = 58;
                Margin = new Padding(0, 0, 0, 4);
                Cursor = Cursors.Hand;
                Padding = new Padding(8, 6, 8, 6);

                // цветная точка
                _colorDot = new Panel
                {
                    Size = new Size(12, 12),
                    Location = new Point(8, 10),
                    Tag = "preserve-backcolor",
                    BackColor = t.Color
                };
                // скруглить через Paint
                _colorDot.Paint += (_, e) =>
                {
                    using var path = RoundedRect(_colorDot.ClientRectangle, 6);
                    using var brush = new SolidBrush(_colorDot.BackColor);
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.FillPath(brush, path);
                };
                _colorDot.Region = null; // не нужно, Paint перекрывает

                // название
                _lblName = new Label
                {
                    Text = $"Трансформация {number}",
                    Font = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f, FontStyle.Bold),
                    AutoSize = false,
                    Bounds = new Rectangle(26, 6, 160, 16),
                    TextAlign = ContentAlignment.MiddleLeft
                };

                // мета (вариация + вес)
                _lblMeta = new Label
                {
                    Text = MetaText(t),
                    Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
                    AutoSize = false,
                    Bounds = new Rectangle(26, 23, 160, 14),
                    TextAlign = ContentAlignment.MiddleLeft
                };

                // полоса веса
                _weightBar = new Panel
                {
                    Bounds = new Rectangle(26, 40, 160, 3),
                    BackColor = Color.FromArgb(50, 128, 128, 128)
                };

                _weightFill = new Panel
                {
                    Bounds = new Rectangle(0, 0, WeightBarWidth(t.Weight), 3),
                    Tag = "preserve-backcolor",
                    BackColor = Color.FromArgb(130, 180, 120)
                };
                _weightBar.Controls.Add(_weightFill);

                // кнопка удаления
                _btnDel = new Button
                {
                    Text = "🗑",
                    Size = new Size(18, 36),
                    Location = new Point(Width - 26, 2),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font(SystemFonts.DefaultFont.FontFamily, 9f),
                    Cursor = Cursors.Default,
                    TabStop = false
                };
                _btnDel.FlatAppearance.BorderSize = 0;
                _btnDel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                _btnDel.Click += (_, _) => DeleteClicked?.Invoke();

                Controls.AddRange(new Control[] { _colorDot, _lblName, _lblMeta, _weightBar, _btnDel });

                Click += (_, _) => Clicked?.Invoke();
                _lblName.Click += (_, _) => Clicked?.Invoke();
                _lblMeta.Click += (_, _) => Clicked?.Invoke();
                _weightBar.Click += (_, _) => Clicked?.Invoke();
                _colorDot.Click += (_, _) => Clicked?.Invoke();
            }

            public void UpdateFrom(FlameTransform t)
            {
                _colorDot.BackColor = t.Color;
                _colorDot.Invalidate();
                _lblMeta.Text = MetaText(t);
                _weightFill.Width = WeightBarWidth(t.Weight);
            }

            public void SetSelected(bool selected)
            {
                _isSelected = selected;
                Invalidate();
            }

            public void ApplyTheme(Utilities.Theme.ThemeDefinition theme)
            {
                BackColor = _isSelected ? theme.HoverBackground : theme.PanelBackground;
                _lblName.BackColor = Color.Transparent;
                _lblMeta.BackColor = Color.Transparent;
                _lblName.ForeColor = _isSelected ? theme.PrimaryText : theme.PrimaryText;
                _lblMeta.ForeColor = theme.SecondaryText;
                _weightBar.BackColor = theme.BorderColor;
                _weightFill.BackColor = theme.AccentPrimary;
                _btnDel.BackColor = Color.Transparent;
                _btnDel.ForeColor = theme.SecondaryText;
                _btnDel.FlatAppearance.MouseOverBackColor = theme.HoverBackground;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                if (_isSelected)
                {
                    using var pen = new Pen(ThemeManager.CurrentDefinition.AccentPrimary, 2f);
                    e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
                }
            }

            private static string MetaText(FlameTransform t) =>
                $"{t.Variation}  ·  вес {t.Weight:F2}";

            private static int WeightBarWidth(double weight) =>
                (int)Math.Round(Math.Clamp(weight / 10.0, 0.0, 1.0) * 160);

            private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle r, int radius)
            {
                var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddArc(r.X, r.Y, radius * 2, radius * 2, 180, 90);
                path.AddArc(r.Right - radius * 2, r.Y, radius * 2, radius * 2, 270, 90);
                path.AddArc(r.Right - radius * 2, r.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
                path.AddArc(r.X, r.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
                path.CloseFigure();
                return path;
            }
        }
    }
}
