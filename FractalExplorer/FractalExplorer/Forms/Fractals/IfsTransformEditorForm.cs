using FractalExplorer.Engines;
using FractalExplorer.Utilities.Theme;

namespace FractalExplorer.Forms.Fractals
{
    public sealed partial class IfsTransformEditorForm : Form
    {
        private readonly List<IfsAffineTransform> _transforms;
        private readonly Stack<EditorSnapshot> _undoStack = new();
        private readonly List<TransformCard> _cards = new();

        private int _selectedIndex = -1;
        private bool _suppressSync;

        public List<IfsAffineTransform> ResultTransforms { get; private set; }

        public IfsTransformEditorForm(IEnumerable<IfsAffineTransform> source)
        {
            _transforms = source.Select(t => t.Clone()).ToList();
            ResultTransforms = _transforms;

            InitializeComponent();

            ThemeManager.RegisterForm(this);
            ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
            Disposed += (_, _) => ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;

            WireEvents();
            RebuildList();

            if (_transforms.Count > 0)
                SelectTransform(0);
            else
                SetEditorEnabled(false);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
            UpdateUndoButtonState();
        }

        private void WireEvents()
        {
            _btnAdd.Click += BtnAdd_Click;
            _btnUndo.Click += BtnUndo_Click;
            _btnNormalize.Click += BtnNormalize_Click;
            _btnOk.Click += BtnOk_Click;

            _nudA.ValueChanged += EditorControl_Changed;
            _nudB.ValueChanged += EditorControl_Changed;
            _nudC.ValueChanged += EditorControl_Changed;
            _nudD.ValueChanged += EditorControl_Changed;
            _nudE.ValueChanged += EditorControl_Changed;
            _nudF.ValueChanged += EditorControl_Changed;
            _nudProbability.ValueChanged += EditorControl_Changed;
        }

        private void RebuildList()
        {
            _listContainer.SuspendLayout();
            _listContainer.Controls.Clear();
            _cards.Clear();

            for (int i = _transforms.Count - 1; i >= 0; i--)
            {
                int capturedIndex = i;
                var card = new TransformCard(_transforms[i], i + 1);
                card.Clicked += () => SelectTransform(capturedIndex);
                card.DeleteClicked += () => DeleteTransform(capturedIndex);
                _cards.Insert(0, card);
                _listContainer.Controls.Add(card);
            }

            _listContainer.ResumeLayout();
            ApplyThemeToCards();
            UpdateTotalProbabilityLabel();
        }

        private void RefreshCardAt(int index)
        {
            if (index < 0 || index >= _cards.Count) return;
            _cards[index].UpdateFrom(_transforms[index], index + 1);
            UpdateTotalProbabilityLabel();
        }

        private void SelectTransform(int index)
        {
            if (index < 0 || index >= _transforms.Count) return;

            _selectedIndex = index;
            UpdateCardSelection();
            LoadEditorFromTransform(_transforms[index]);
            SetEditorEnabled(true);
            _lblEditorTitle.Text = $"Преобразование {index + 1}";
        }

        private void LoadEditorFromTransform(IfsAffineTransform t)
        {
            _suppressSync = true;
            try
            {
                SetNud(_nudA, t.A);
                SetNud(_nudB, t.B);
                SetNud(_nudC, t.C);
                SetNud(_nudD, t.D);
                SetNud(_nudE, t.E);
                SetNud(_nudF, t.F);
                SetNud(_nudProbability, t.Probability);
            }
            finally
            {
                _suppressSync = false;
            }
        }

        private void SaveEditorToTransform(IfsAffineTransform t)
        {
            t.A = (double)_nudA.Value;
            t.B = (double)_nudB.Value;
            t.C = (double)_nudC.Value;
            t.D = (double)_nudD.Value;
            t.E = (double)_nudE.Value;
            t.F = (double)_nudF.Value;
            t.Probability = Math.Max(0, (double)_nudProbability.Value);
        }

        private void EditorControl_Changed(object? sender, EventArgs e)
        {
            if (_suppressSync || _selectedIndex < 0) return;
            PushUndoSnapshot();
            SaveEditorToTransform(_transforms[_selectedIndex]);
            RefreshCardAt(_selectedIndex);
        }

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            PushUndoSnapshot();
            _transforms.Add(new IfsAffineTransform
            {
                A = 0.5,
                D = 0.5,
                Probability = 0.5
            });
            RebuildList();
            SelectTransform(_transforms.Count - 1);
        }

        private void DeleteTransform(int index)
        {
            if (index < 0 || index >= _transforms.Count) return;

            PushUndoSnapshot();
            _transforms.RemoveAt(index);

            int newIndex = _transforms.Count == 0 ? -1 : Math.Min(index, _transforms.Count - 1);
            RebuildList();

            if (newIndex >= 0)
                SelectTransform(newIndex);
            else
            {
                _selectedIndex = -1;
                SetEditorEnabled(false);
                _lblEditorTitle.Text = "Редактор преобразования";
            }
        }

        private void BtnNormalize_Click(object? sender, EventArgs e)
        {
            double total = _transforms.Sum(t => Math.Max(0, t.Probability));
            if (total <= 0)
            {
                return;
            }

            PushUndoSnapshot();
            foreach (IfsAffineTransform transform in _transforms)
            {
                transform.Probability = Math.Max(0, transform.Probability) / total;
            }

            RebuildList();
            if (_selectedIndex >= 0 && _selectedIndex < _transforms.Count)
            {
                LoadEditorFromTransform(_transforms[_selectedIndex]);
                UpdateCardSelection();
            }
        }

        private void BtnOk_Click(object? sender, EventArgs e)
        {
            if (_selectedIndex >= 0 && _selectedIndex < _transforms.Count)
                SaveEditorToTransform(_transforms[_selectedIndex]);

            if (_transforms.Count == 0)
            {
                MessageBox.Show(this, "Добавьте хотя бы одно преобразование.", "IFS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            ResultTransforms = _transforms.Select(t => t.Clone()).ToList();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void BtnUndo_Click(object? sender, EventArgs e) => UndoLastAction();

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
                _lblEditorTitle.Text = "Редактор преобразования";
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

        private void UpdateTotalProbabilityLabel()
        {
            double total = _transforms.Sum(t => Math.Max(0, t.Probability));
            _lblTotalProbability.Text = $"Суммарная вероятность: {total:F4}";
            _lblTotalProbability.ForeColor = Math.Abs(total - 1.0) < 0.0001 || _transforms.Count == 0
                ? ThemeManager.CurrentDefinition.SecondaryText
                : Color.OrangeRed;
        }

        private void SetEditorEnabled(bool enabled)
        {
            _nudA.Enabled = enabled;
            _nudB.Enabled = enabled;
            _nudC.Enabled = enabled;
            _nudD.Enabled = enabled;
            _nudE.Enabled = enabled;
            _nudF.Enabled = enabled;
            _nudProbability.Enabled = enabled;

            if (!enabled)
                _lblEditorTitle.Text = "Нет преобразований — нажмите «+ Добавить»";
        }

        private void UpdateCardSelection()
        {
            for (int i = 0; i < _cards.Count; i++)
                _cards[i].SetSelected(i == _selectedIndex);
        }

        private static void SetNud(NumericUpDown nud, double value)
        {
            decimal clamped = (decimal)Math.Clamp(value, (double)nud.Minimum, (double)nud.Maximum);
            nud.Value = clamped;
        }

        private void ThemeManager_ThemeChanged(object? sender, EventArgs e)
        {
            ApplyThemeToCards();
            UpdateTotalProbabilityLabel();
            _split.BackColor = ThemeManager.CurrentDefinition.BorderColor;
            _divider.BackColor = ThemeManager.CurrentDefinition.BorderColor;
            _lblAffineCaption.ForeColor = ThemeManager.CurrentDefinition.SecondaryText;
            _lblProbabilityCaption.ForeColor = ThemeManager.CurrentDefinition.SecondaryText;
            _lblAffineFormula.ForeColor = ThemeManager.CurrentDefinition.SecondaryText;
        }

        private void ApplyThemeToCards()
        {
            var theme = ThemeManager.CurrentDefinition;
            foreach (var card in _cards)
                card.ApplyTheme(theme);
            UpdateCardSelection();
        }

        private sealed class EditorSnapshot
        {
            public IReadOnlyList<IfsAffineTransform> Transforms { get; }
            public int SelectedIndex { get; }

            public EditorSnapshot(IEnumerable<IfsAffineTransform> transforms, int selectedIndex)
            {
                Transforms = transforms.Select(t => t.Clone()).ToList();
                SelectedIndex = selectedIndex;
            }

            public bool HasSameState(IReadOnlyList<IfsAffineTransform> transforms, int selectedIndex)
            {
                if (selectedIndex != SelectedIndex || transforms.Count != Transforms.Count)
                    return false;

                for (int i = 0; i < transforms.Count; i++)
                {
                    var left = transforms[i];
                    var right = Transforms[i];
                    if (left.A != right.A ||
                        left.B != right.B ||
                        left.C != right.C ||
                        left.D != right.D ||
                        left.E != right.E ||
                        left.F != right.F ||
                        left.Probability != right.Probability)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private sealed class TransformCard : Panel
        {
            private readonly Label _lblName;
            private readonly Label _lblMeta;
            private readonly Panel _probabilityBar;
            private readonly Panel _probabilityFill;
            private readonly Button _btnDelete;
            private bool _selected;

            public event Action? Clicked;
            public event Action? DeleteClicked;

            public TransformCard(IfsAffineTransform transform, int number)
            {
                Dock = DockStyle.Top;
                Height = 58;
                Margin = new Padding(0, 0, 0, 4);
                Cursor = Cursors.Hand;
                Padding = new Padding(8, 6, 8, 6);

                _lblName = new Label
                {
                    Text = $"Преобразование {number}",
                    Font = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f, FontStyle.Bold),
                    AutoSize = false,
                    Bounds = new Rectangle(8, 6, 160, 16),
                    TextAlign = ContentAlignment.MiddleLeft
                };

                _lblMeta = new Label
                {
                    Text = MetaText(transform),
                    Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
                    AutoSize = false,
                    Bounds = new Rectangle(8, 23, 170, 14),
                    TextAlign = ContentAlignment.MiddleLeft
                };

                _probabilityBar = new Panel
                {
                    Bounds = new Rectangle(8, 40, 170, 3),
                    BackColor = Color.FromArgb(50, 128, 128, 128)
                };

                _probabilityFill = new Panel
                {
                    Height = 3,
                    Width = 1,
                    BackColor = Color.FromArgb(90, 170, 255),
                    Location = new Point(0, 0)
                };
                _probabilityBar.Controls.Add(_probabilityFill);

                _btnDelete = new Button
                {
                    Text = "✕",
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    Size = new Size(24, 24),
                    Location = new Point(190, 17),
                    FlatStyle = FlatStyle.Flat,
                    TabStop = false
                };
                _btnDelete.FlatAppearance.BorderSize = 1;
                _btnDelete.Click += (_, _) => DeleteClicked?.Invoke();

                Controls.Add(_lblName);
                Controls.Add(_lblMeta);
                Controls.Add(_probabilityBar);
                Controls.Add(_btnDelete);

                Click += (_, _) => Clicked?.Invoke();
                foreach (Control c in Controls)
                    c.Click += (_, _) => Clicked?.Invoke();

                UpdateFrom(transform, number);
            }

            public void UpdateFrom(IfsAffineTransform transform, int number)
            {
                _lblName.Text = $"Преобразование {number}";
                _lblMeta.Text = MetaText(transform);

                double p = Math.Clamp(transform.Probability, 0.0, 1.0);
                int w = (int)Math.Round(_probabilityBar.Width * p);
                _probabilityFill.Width = Math.Max(1, w);
            }

            public void ApplyTheme(ThemeDefinition theme)
            {
                BackColor = _selected ? theme.ControlBackground : theme.PanelBackground;
                ForeColor = theme.PrimaryText;
                _lblName.ForeColor = theme.PrimaryText;
                _lblMeta.ForeColor = theme.SecondaryText;
                _btnDelete.BackColor = theme.ControlBackground;
                _btnDelete.ForeColor = theme.PrimaryText;
                _btnDelete.FlatAppearance.BorderColor = theme.BorderColor;
            }

            public void SetSelected(bool selected)
            {
                _selected = selected;
                ApplyTheme(ThemeManager.CurrentDefinition);
            }

            private static string MetaText(IfsAffineTransform transform)
            {
                return $"P={transform.Probability:F4}  |  [{transform.A:F2} {transform.B:F2}; {transform.C:F2} {transform.D:F2}]";
            }
        }
    }
}
