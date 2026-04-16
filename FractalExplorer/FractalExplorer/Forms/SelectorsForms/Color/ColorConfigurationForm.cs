using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.ColorPicking;
using System.Drawing.Drawing2D;

using FractalExplorer.Utilities.Theme;
namespace FractalExplorer.Utilities
{
    /// <summary>
    /// Базовая форма для настройки цветовых палитр фракталов.
    /// Позволяет пользователю управлять встроенными и пользовательскими палитрами:
    /// выбирать, просматривать, создавать новые, редактировать цвета, копировать
    /// и применять выбранную палитру.
    /// </summary>
    public partial class ColorConfigurationForm : Form
    {
        #region Fields
        /// <summary>
        /// Менеджер палитр, управляющий доступными палитрами.
        /// </summary>
        private readonly PaletteManager _paletteManager;
        private readonly ColorSelectionService _colorSelectionService = ColorSelectionService.Default;
        /// <summary>
        /// Текущая выбранная палитра в списке.
        /// </summary>
        private Palette _selectedPalette;
        private bool _hasUnsavedChanges;
        #endregion

        #region Events
        /// <summary>
        /// Событие, которое возникает при применении палитры к главной форме.
        /// </summary>
        public event EventHandler PaletteApplied;
        #endregion

        #region Constructor
        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="ColorConfigurationForm"/>.
        /// </summary>
        /// <param name="paletteManager">Менеджер палитр для управления палитрами.</param>
        public ColorConfigurationForm(PaletteManager paletteManager)
            : this(paletteManager, null)
        {
        }

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="ColorConfigurationForm"/> с пользовательским заголовком.
        /// </summary>
        /// <param name="paletteManager">Менеджер палитр для управления палитрами.</param>
        /// <param name="title">Пользовательский заголовок формы (необязательно).</param>
        public ColorConfigurationForm(PaletteManager paletteManager, string? title)
        {
            InitializeComponent();
            ThemeManager.RegisterForm(this);
            _paletteManager = paletteManager;
            if (!string.IsNullOrWhiteSpace(title))
            {
                Text = title;
            }

            nudGamma.Minimum = 0.1m;
            nudGamma.Maximum = 5.0m;
            nudGamma.DecimalPlaces = 2;
            nudGamma.Increment = 0.1m;

            nudMaxColorIterations.Minimum = 2;
            nudMaxColorIterations.Maximum = 100000;
            nudMaxColorIterations.Increment = 10;

            // <<< ИЗМЕНЕНИЕ: Включаем возможность перетаскивания для списка цветов
            // и подписываемся на необходимые события.
            lbColorStops.AllowDrop = true;
            lbColorStops.MouseDown += new MouseEventHandler(lbColorStops_MouseDown);
            lbColorStops.DragOver += new DragEventHandler(lbColorStops_DragOver);
            lbColorStops.DragDrop += new DragEventHandler(lbColorStops_DragDrop);
        }
        #endregion

        #region UI Initialization
        /// <summary>
        /// Обработчик события загрузки формы.
        /// Заполняет список палитр и выбирает активную палитру.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void ColorConfigurationForm_Load(object sender, EventArgs e)
        {
            PopulatePaletteList();
            if (_paletteManager.ActivePalette != null)
            {
                string displayNameToFind = _paletteManager.ActivePalette.IsBuiltIn
                    ? $"{_paletteManager.ActivePalette.Name} [Встроенная]"
                    : _paletteManager.ActivePalette.Name;
                lbPalettes.SelectedItem = displayNameToFind;
            }
        }

        /// <summary>
        /// Заполняет ListBox доступными палитрами из менеджера палитр.
        /// </summary>
        private void PopulatePaletteList()
        {
            lbPalettes.Items.Clear();
            foreach (var palette in _paletteManager.Palettes)
            {
                string displayName = palette.IsBuiltIn ? $"{palette.Name} [Встроенная]" : palette.Name;
                lbPalettes.Items.Add(displayName);
            }
        }
        #endregion

        #region UI Display & Update
        /// <summary>
        /// Обработчик события изменения выбранного элемента в списке палитр.
        /// Обновляет отображаемые детали палитры и состояние элементов управления.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void lbPalettes_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lbPalettes.SelectedIndex == -1)
            {
                // Если ничего не выбрано, очищаем детали и выключаем кнопки
                _selectedPalette = null;
                UpdateControlsState();
                panelPreview.Invalidate();
                return;
            }
            string selectedName = lbPalettes.SelectedItem.ToString().Replace(" [Встроенная]", "");
            _selectedPalette = _paletteManager.Palettes.FirstOrDefault(p => p.Name == selectedName);
            if (_selectedPalette == null) return;

            ResetUnsavedChanges();
            DisplayPaletteDetails();
            UpdateControlsState();
        }

        /// <summary>
        /// Отображает детали выбранной палитры (имя, градиент, цвета и другие параметры) на форме.
        /// </summary>
        private void DisplayPaletteDetails()
        {
            // Временно отписываемся от событий, чтобы избежать рекурсивных вызовов при программном изменении значений
            txtName.TextChanged -= txtName_TextChanged;
            checkAlignSteps.CheckedChanged -= checkAlignSteps_CheckedChanged;
            nudMaxColorIterations.ValueChanged -= nudMaxColorIterations_ValueChanged;
            nudGamma.ValueChanged -= nudGamma_ValueChanged;

            txtName.Text = _selectedPalette.Name;
            checkIsGradient.Checked = _selectedPalette.IsGradient;
            checkAlignSteps.Checked = _selectedPalette.AlignWithRenderIterations;
            nudMaxColorIterations.Value = Math.Max(nudMaxColorIterations.Minimum, Math.Min(nudMaxColorIterations.Maximum, _selectedPalette.MaxColorIterations));
            nudGamma.Value = (decimal)Math.Max((double)nudGamma.Minimum, Math.Min((double)nudGamma.Maximum, _selectedPalette.Gamma));

            nudMaxColorIterations.Enabled = !_selectedPalette.AlignWithRenderIterations && !_selectedPalette.IsBuiltIn;

            lbColorStops.Items.Clear();
            foreach (var color in _selectedPalette.Colors)
            {
                // Для ListBox важно, чтобы элементы были уникальными. Имя цвета подходит.
                // Если могут быть дубликаты, лучше использовать более сложный объект.
                lbColorStops.Items.Add(color.Name);
            }
            panelPreview.Invalidate();

            // Подписываемся на события обратно
            txtName.TextChanged += txtName_TextChanged;
            checkAlignSteps.CheckedChanged += checkAlignSteps_CheckedChanged;
            nudMaxColorIterations.ValueChanged += nudMaxColorIterations_ValueChanged;
            nudGamma.ValueChanged += nudGamma_ValueChanged;
        }

        /// <summary>
        /// Обновляет состояние элементов управления (включено/отключено)
        /// в зависимости от того, является ли выбранная палитра пользовательской или встроенной.
        /// </summary>
        private void UpdateControlsState()
        {
            if (_selectedPalette == null)
            {
                // Если палитра не выбрана, отключаем большинство контролов
                bool isEnabled = false;
                txtName.Enabled = isEnabled;
                checkIsGradient.Enabled = isEnabled;
                checkAlignSteps.Enabled = isEnabled;
                lbColorStops.Enabled = isEnabled;
                btnAddColor.Enabled = isEnabled;
                btnEditColor.Enabled = isEnabled;
                btnRemoveColor.Enabled = isEnabled;
                btnDelete.Enabled = isEnabled;
                nudGamma.Enabled = isEnabled;
                nudMaxColorIterations.Enabled = isEnabled;
                btnCopy.Enabled = isEnabled;
                return;
            }

            bool isCustom = !_selectedPalette.IsBuiltIn;

            txtName.Enabled = isCustom;
            checkIsGradient.Enabled = isCustom;
            checkAlignSteps.Enabled = isCustom;
            lbColorStops.Enabled = isCustom;
            btnAddColor.Enabled = isCustom;
            btnEditColor.Enabled = isCustom;
            btnRemoveColor.Enabled = isCustom;
            btnDelete.Enabled = isCustom;
            nudGamma.Enabled = isCustom;

            nudMaxColorIterations.Enabled = isCustom && !_selectedPalette.AlignWithRenderIterations;

            btnCopy.Enabled = _selectedPalette != null;
            btnSave.Enabled = isCustom && _hasUnsavedChanges;
        }

        private void MarkUnsavedChanges()
        {
            if (_selectedPalette == null || _selectedPalette.IsBuiltIn)
            {
                return;
            }

            _hasUnsavedChanges = true;
            btnSave.Enabled = true;
        }

        private void ResetUnsavedChanges()
        {
            _hasUnsavedChanges = false;
            btnSave.Enabled = false;
        }

        /// <summary>
        /// Обработчик события отрисовки панели предпросмотра палитры.
        /// Отображает градиент или сплошной цвет выбранной палитры с учетом гамма-коррекции.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события рисования.</param>
        private void panelPreview_Paint(object sender, PaintEventArgs e)
        {
            if (_selectedPalette == null || _selectedPalette.Colors.Count == 0)
            {
                using (var linearGradientBrush = new LinearGradientBrush(panelPreview.ClientRectangle, Color.White, Color.Black, 0f))
                {
                    e.Graphics.FillRectangle(linearGradientBrush, panelPreview.ClientRectangle);
                }
                return;
            }

            if (_selectedPalette.Colors.Count == 1)
            {
                Color finalColor = ColorCorrection.ApplyGamma(_selectedPalette.Colors[0], _selectedPalette.Gamma);
                using (var brush = new SolidBrush(finalColor))
                {
                    e.Graphics.FillRectangle(brush, panelPreview.ClientRectangle);
                }
                return;
            }

            using (var linearGradientBrush = new LinearGradientBrush(panelPreview.ClientRectangle, Color.Black, Color.Black, 0f))
            {
                var colorBlend = new ColorBlend(_selectedPalette.Colors.Count);
                colorBlend.Colors = _selectedPalette.Colors.Select(c => ColorCorrection.ApplyGamma(c, _selectedPalette.Gamma)).ToArray();
                colorBlend.Positions = Enumerable.Range(0, _selectedPalette.Colors.Count)
                                                 .Select(i => (float)i / (_selectedPalette.Colors.Count - 1))
                                                 .ToArray();
                linearGradientBrush.InterpolationColors = colorBlend;
                e.Graphics.FillRectangle(linearGradientBrush, panelPreview.ClientRectangle);
            }
        }
        #endregion

        #region Palette Editing Actions
        /// <summary>
        /// Обработчик события изменения текста в поле имени палитры.
        /// Обновляет имя палитры в менеджере палитр и в списке.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void txtName_TextChanged(object sender, EventArgs e)
        {
            // Теперь этот метод просто обновляет имя в объекте палитры.
            // Он больше не трогает ListBox, поэтому фокус не теряется.
            if (_selectedPalette != null && !_selectedPalette.IsBuiltIn)
            {
                _selectedPalette.Name = txtName.Text;
                MarkUnsavedChanges();
            }
        }

        /// <summary>
        /// Обновляет отображаемое имя выбранной палитры в списке lbPalettes.
        /// </summary>
        private void UpdatePaletteListDisplayName()
        {
            if (_selectedPalette == null || _selectedPalette.IsBuiltIn) return;

            int selectedIndex = lbPalettes.SelectedIndex;
            if (selectedIndex != -1)
            {
                // Чтобы избежать любых побочных эффектов, на время обновления
                // отписываемся от события изменения индекса, а потом подписываемся обратно.
                // Это надежная практика.
                lbPalettes.SelectedIndexChanged -= lbPalettes_SelectedIndexChanged;
                lbPalettes.Items[selectedIndex] = _selectedPalette.Name;
                lbPalettes.SelectedIndexChanged += lbPalettes_SelectedIndexChanged;
            }
        }

        /// <summary>
        /// Обработчик события клика по кнопке "Add Color".
        /// Открывает диалог выбора цвета и добавляет выбранный цвет в палитру.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void btnAddColor_Click(object sender, EventArgs e)
        {
            if (_colorSelectionService.TrySelectColor(this, Color.White, out Color selectedColor))
            {
                _selectedPalette.Colors.Add(selectedColor);
                DisplayPaletteDetails();
                MarkUnsavedChanges();
            }
        }

        /// <summary>
        /// Обработчик события клика по кнопке "Edit Color".
        /// Открывает диалог выбора цвета для редактирования выбранного цвета в списке.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void btnEditColor_Click(object sender, EventArgs e)
        {
            if (lbColorStops.SelectedIndex == -1) return;
            Color initialColor = _selectedPalette.Colors[lbColorStops.SelectedIndex];
            if (_colorSelectionService.TrySelectColor(this, initialColor, out Color selectedColor))
            {
                _selectedPalette.Colors[lbColorStops.SelectedIndex] = selectedColor;
                DisplayPaletteDetails();
                MarkUnsavedChanges();
            }
        }

        /// <summary>
        /// Обработчик события клика по кнопке "Remove Color".
        /// Удаляет выбранный цвет из палитры (если их больше одного).
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void btnRemoveColor_Click(object sender, EventArgs e)
        {
            if (lbColorStops.SelectedIndex != -1 && _selectedPalette.Colors.Count > 1)
            {
                _selectedPalette.Colors.RemoveAt(lbColorStops.SelectedIndex);
                DisplayPaletteDetails();
                MarkUnsavedChanges();
            }
        }

        /// <summary>
        /// Обработчик события клика по кнопке "New Palette".
        /// Создает новую палитру с именем по умолчанию и добавляет ее в список.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void btnNew_Click(object sender, EventArgs e)
        {
            string newName = "Новая палитра";
            int counter = 1;
            while (_paletteManager.Palettes.Any(p => p.Name == newName))
            {
                newName = $"Новая палитра {++counter}";
            }
            var newPalette = new Palette(newName, new List<Color> { Color.Black, Color.White }, true, false, 500, 1.0, false);

            // ИЗМЕНЕНО: Используем безопасный метод менеджера для добавления палитры
            _paletteManager.AddPalette(newPalette);
            _paletteManager.SaveCustomPalettes();

            PopulatePaletteList();
            lbPalettes.SelectedItem = newPalette.Name;
        }

        /// <summary>
        /// Обработчик события клика по кнопке "Копировать".
        /// Создает редактируемую копию выбранной палитры.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void btnCopy_Click(object sender, EventArgs e)
        {
            if (_selectedPalette == null)
            {
                return;
            }

            string baseName = $"{_selectedPalette.Name} (копия)";
            string newName = baseName;
            int counter = 2;
            while (_paletteManager.Palettes.Any(p => p.Name == newName))
            {
                newName = $"{baseName} {counter++}";
            }

            var newPalette = new Palette(
                name: newName,
                colors: new List<Color>(_selectedPalette.Colors),
                isGradient: _selectedPalette.IsGradient,
                isBuiltIn: false,
                maxColorIterations: _selectedPalette.MaxColorIterations,
                gamma: _selectedPalette.Gamma,
                alignWithRenderIterations: _selectedPalette.AlignWithRenderIterations
            );

            // ИЗМЕНЕНО: Используем безопасный метод менеджера для добавления палитры
            _paletteManager.AddPalette(newPalette);
            _paletteManager.SaveCustomPalettes();

            PopulatePaletteList();
            lbPalettes.SelectedItem = newPalette.Name;
        }

        /// <summary>
        /// Обработчик события клика по кнопке "Delete Palette".
        /// Удаляет текущую выбранную палитру (если она не встроенная).
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void btnDelete_Click(object sender, EventArgs e)
        {
            if (_selectedPalette != null && !_selectedPalette.IsBuiltIn)
            {
                DialogResult confirmResult = MessageBox.Show($"Вы уверены, что хотите удалить палитру '{_selectedPalette.Name}'?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirmResult == DialogResult.Yes)
                {
                    // ИЗМЕНЕНО: Используем безопасный метод менеджера для удаления
                    bool removed = _paletteManager.RemovePalette(_selectedPalette);

                    if (removed)
                    {
                        _paletteManager.SaveCustomPalettes();
                        PopulatePaletteList();
                        // Устанавливаем выбор на последний элемент в списке или
                        // оставляем без выбора, если список пуст.
                        if (lbPalettes.Items.Count > 0)
                        {
                            lbPalettes.SelectedIndex = lbPalettes.Items.Count - 1;
                        }
                        else
                        {
                            // Если список стал пустым, нужно явно сбросить выбор.
                            // Событие SelectedIndexChanged позаботится об остальном.
                            lbPalettes.SelectedIndex = -1;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Обработчик события изменения состояния чекбокса "Is Gradient".
        /// Обновляет свойство IsGradient выбранной палитры.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void checkIsGradient_CheckedChanged(object sender, EventArgs e)
        {
            if (_selectedPalette != null && !_selectedPalette.IsBuiltIn)
            {
                _selectedPalette.IsGradient = checkIsGradient.Checked;
                panelPreview.Invalidate();
                MarkUnsavedChanges();
            }
        }

        /// <summary>
        /// Обработчик события изменения состояния чекбокса "Align Steps".
        /// Обновляет свойство AlignWithRenderIterations и блокирует/разблокирует поле длины цикла.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void checkAlignSteps_CheckedChanged(object sender, EventArgs e)
        {
            if (_selectedPalette != null && !_selectedPalette.IsBuiltIn)
            {
                _selectedPalette.AlignWithRenderIterations = checkAlignSteps.Checked;
                nudMaxColorIterations.Enabled = !checkAlignSteps.Checked;
                MarkUnsavedChanges();
            }
        }

        /// <summary>
        /// Обработчик события изменения значения "Длина цикла цвета".
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void nudMaxColorIterations_ValueChanged(object sender, EventArgs e)
        {
            if (_selectedPalette != null && !_selectedPalette.IsBuiltIn)
            {
                _selectedPalette.MaxColorIterations = (int)nudMaxColorIterations.Value;
                MarkUnsavedChanges();
            }
        }

        /// <summary>
        /// Обработчик события изменения значения "Гамма-коррекция".
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void nudGamma_ValueChanged(object sender, EventArgs e)
        {
            if (_selectedPalette != null && !_selectedPalette.IsBuiltIn)
            {
                _selectedPalette.Gamma = (double)nudGamma.Value;
                panelPreview.Invalidate();
                MarkUnsavedChanges();
            }
        }

        /// <summary>
        /// Обработчик события клика по кнопке "Save".
        /// Сохраняет пользовательские палитры в файл.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void btnSave_Click(object sender, EventArgs e)
        {
            _paletteManager.SaveCustomPalettes();
            ResetUnsavedChanges();
            MessageBox.Show("Изменения палитры сохранены.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        #endregion

        #region Main Form Actions

        private void txtName_LostFocus(object sender, EventArgs e)
        {
            // Когда пользователь уводит фокус с поля ввода, обновляем имя в списке.
            UpdatePaletteListDisplayName();
        }

        private void txtName_KeyDown(object sender, KeyEventArgs e)
        {
            // Когда пользователь нажимает Enter, также обновляем имя в списке
            // и "съедаем" нажатие, чтобы не было системного звука "бип".
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true; // Подавляем системный звук
                UpdatePaletteListDisplayName();
                // Опционально: можно перевести фокус на следующий элемент, например, на кнопку "Применить"
                // btnApply.Focus();
            }
        }

        /// <summary>
        /// Обработчик события клика по кнопке "Apply".
        /// Устанавливает выбранную палитру как активную в менеджере палитр
        /// и вызывает событие <see cref="PaletteApplied"/> для уведомления главной формы.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void btnApply_Click(object sender, EventArgs e)
        {
            if (_selectedPalette != null)
            {
                _paletteManager.ActivePalette = _selectedPalette;
                PaletteApplied?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Обработчик события клика по кнопке "Close".
        /// Закрывает форму настроек палитры.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }
        #endregion

        #region Drag and Drop for Color List

        /// <summary>
        /// Обработчик события нажатия кнопки мыши на списке цветов.
        /// Начинает операцию перетаскивания (Drag and Drop).
        /// </summary>
        private void lbColorStops_MouseDown(object sender, MouseEventArgs e)
        {
            // Перетаскивание доступно только для пользовательских палитр.
            if (_selectedPalette == null || _selectedPalette.IsBuiltIn)
            {
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                int index = lbColorStops.IndexFromPoint(e.Location);
                if (index != ListBox.NoMatches)
                {
                    // Начинаем операцию, передавая текст выбранного элемента как данные.
                    lbColorStops.DoDragDrop(lbColorStops.Items[index], DragDropEffects.Move);
                }
            }
        }

        /// <summary>
        /// Обработчик события, когда перетаскиваемый элемент находится над списком.
        /// Устанавливает эффект "перемещение", чтобы показать пользователю, что сюда можно "бросить" элемент.
        /// </summary>
        private void lbColorStops_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        /// <summary>
        /// Обработчик события, когда пользователь "бросает" элемент на список.
        /// Выполняет фактическую перестановку цвета в источнике данных.
        /// </summary>
        private void lbColorStops_DragDrop(object sender, DragEventArgs e)
        {
            // Перетаскивание доступно только для пользовательских палитр.
            if (_selectedPalette == null || _selectedPalette.IsBuiltIn)
            {
                return;
            }

            // Получаем точку, где был отпущен элемент.
            Point point = lbColorStops.PointToClient(new Point(e.X, e.Y));
            // Находим индекс элемента, на который "бросили" (целевой индекс).
            int destinationIndex = lbColorStops.IndexFromPoint(point);

            // Если "бросили" на пустое место внизу, считаем, что это последний элемент.
            if (destinationIndex == ListBox.NoMatches)
            {
                destinationIndex = lbColorStops.Items.Count - 1;
            }

            // Получаем данные перетаскиваемого элемента (это текст, который мы передали в MouseDown).
            string draggedItemText = (string)e.Data.GetData(typeof(string));
            // Находим его исходный индекс в списке.
            int sourceIndex = lbColorStops.Items.IndexOf(draggedItemText);

            // Если исходный и целевой индексы совпадают, ничего не делаем.
            if (sourceIndex == destinationIndex)
            {
                return;
            }

            // Теперь работаем с реальным источником данных - списком `_selectedPalette.Colors`.
            // Получаем объект Color, который мы перемещаем.
            Color draggedColor = _selectedPalette.Colors[sourceIndex];

            // Удаляем его со старого места и вставляем в новое.
            _selectedPalette.Colors.RemoveAt(sourceIndex);
            _selectedPalette.Colors.Insert(destinationIndex, draggedColor);
            MarkUnsavedChanges();

            // Полностью обновляем все детали палитры на форме.
            // Это самый надежный способ, так как он перерисовывает список
            // и панель предпросмотра на основе актуальных данных.
            DisplayPaletteDetails();

            // После обновления снова выделяем перемещенный элемент для удобства.
            lbColorStops.SelectedIndex = destinationIndex;
        }

        #endregion 
    }
}
