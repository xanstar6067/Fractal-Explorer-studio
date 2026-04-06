namespace FractalExplorer.Forms.Fractals
{
    partial class FlameTransformEditorForm
    {
        private System.ComponentModel.IContainer? components = null;

        // ── корневой SplitContainer ────────────────────────────────────────────
        private SplitContainer _split;

        // ── левая панель: список трансформаций ────────────────────────────────
        private Panel _leftPanel;
        private Panel _listHeader;
        private Label _lblListTitle;
        private Button _btnAdd;
        private Panel _listContainer;      // сюда динамически добавляются карточки
        private Panel _listFooter;
        private Label _lblTotalWeight;

        // ── правая панель: редактор выбранной трансформации ───────────────────
        private Panel _rightPanel;
        private Panel _editorHeader;
        private Label _lblEditorTitle;
        private TabControl _tabs;
        private TabPage _tabMain;
        private TabPage _tabMatrix;

        // -- вкладка «Основное» -----------------------------------------------
        private TableLayoutPanel _tblMain;

        private Label _lblVariationCaption;
        private TableLayoutPanel _tblVariationRow;
        private ComboBox _cmbVariation;
        private Panel _pnlColorPreview;
        private Button _btnPickColor;

        private Label _lblWeightCaption;
        private TableLayoutPanel _tblWeightRow;
        private TrackBar _trkWeight;
        private Label _lblWeightValue;
        private Label _lblWeightPercent;

        private Panel _divider;

        private Label _lblAffineCaption;
        private Label _lblAffineFormula;
        private TableLayoutPanel _tblAffineGrid;

        private Label _lblA; private NumericUpDown _nudA;
        private Label _lblB; private NumericUpDown _nudB;
        private Label _lblC; private NumericUpDown _nudC;
        private Label _lblD; private NumericUpDown _nudD;
        private Label _lblE; private NumericUpDown _nudE;
        private Label _lblF; private NumericUpDown _nudF;

        private Panel _pnlHint;
        private Label _lblHint;

        // -- вкладка «Матрица» -----------------------------------------------
        private Label _lblMatrixInfo;

        // ── нижняя полоска кнопок ─────────────────────────────────────────────
        private Panel _footerPanel;
        private Button _btnDelete;
        private Button _btnCancel;
        private Button _btnOk;

        protected override void Dispose(bool disposing)
        {
            if (disposing) components?.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            // ── корневые контейнеры ───────────────────────────────────────────
            _split = new SplitContainer();
            _leftPanel = new Panel();
            _listHeader = new Panel();
            _lblListTitle = new Label();
            _btnAdd = new Button();
            _listContainer = new Panel();
            _listFooter = new Panel();
            _lblTotalWeight = new Label();

            _rightPanel = new Panel();
            _editorHeader = new Panel();
            _lblEditorTitle = new Label();
            _tabs = new TabControl();
            _tabMain = new TabPage();
            _tabMatrix = new TabPage();

            // ── вкладка «Основное» ────────────────────────────────────────────
            _tblMain = new TableLayoutPanel();

            _lblVariationCaption = new Label();
            _tblVariationRow = new TableLayoutPanel();
            _cmbVariation = new ComboBox();
            _pnlColorPreview = new Panel();
            _btnPickColor = new Button();

            _lblWeightCaption = new Label();
            _tblWeightRow = new TableLayoutPanel();
            _trkWeight = new TrackBar();
            _lblWeightValue = new Label();
            _lblWeightPercent = new Label();

            _divider = new Panel();

            _lblAffineCaption = new Label();
            _lblAffineFormula = new Label();
            _tblAffineGrid = new TableLayoutPanel();

            _lblA = new Label(); _nudA = new NumericUpDown();
            _lblB = new Label(); _nudB = new NumericUpDown();
            _lblC = new Label(); _nudC = new NumericUpDown();
            _lblD = new Label(); _nudD = new NumericUpDown();
            _lblE = new Label(); _nudE = new NumericUpDown();
            _lblF = new Label(); _nudF = new NumericUpDown();

            _pnlHint = new Panel();
            _lblHint = new Label();

            // ── вкладка «Матрица» ─────────────────────────────────────────────
            _lblMatrixInfo = new Label();

            // ── подвал ────────────────────────────────────────────────────────
            _footerPanel = new Panel();
            _btnDelete = new Button();
            _btnCancel = new Button();
            _btnOk = new Button();

            // ──────────────────────────────────────────────────────────────────
            ((System.ComponentModel.ISupportInitialize)_split).BeginInit();
            _split.Panel1.SuspendLayout();
            _split.Panel2.SuspendLayout();
            _split.SuspendLayout();
            _leftPanel.SuspendLayout();
            _listHeader.SuspendLayout();
            _listFooter.SuspendLayout();
            _rightPanel.SuspendLayout();
            _editorHeader.SuspendLayout();
            _tabs.SuspendLayout();
            _tabMain.SuspendLayout();
            _tabMatrix.SuspendLayout();
            _tblMain.SuspendLayout();
            _tblVariationRow.SuspendLayout();
            _tblWeightRow.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_trkWeight).BeginInit();
            _tblAffineGrid.SuspendLayout();
            foreach (NumericUpDown n in new[] { _nudA, _nudB, _nudC, _nudD, _nudE, _nudF })
                ((System.ComponentModel.ISupportInitialize)n).BeginInit();
            _pnlHint.SuspendLayout();
            _footerPanel.SuspendLayout();
            SuspendLayout();

            // ══════════════════════════════════════════════════════════════════
            // _split
            // ══════════════════════════════════════════════════════════════════
            _split.Dock = DockStyle.Fill;
            _split.FixedPanel = FixedPanel.Panel1;
            _split.IsSplitterFixed = true;
            _split.SplitterDistance = 240;
            _split.SplitterWidth = 1;
            _split.Panel1.Controls.Add(_leftPanel);
            _split.Panel2.Controls.Add(_rightPanel);
            _split.Name = "_split";
            _split.TabIndex = 0;

            // ══════════════════════════════════════════════════════════════════
            // _leftPanel
            // ══════════════════════════════════════════════════════════════════
            _leftPanel.Dock = DockStyle.Fill;
            _leftPanel.Controls.Add(_listContainer);
            _leftPanel.Controls.Add(_listFooter);
            _leftPanel.Controls.Add(_listHeader);
            _leftPanel.Name = "_leftPanel";

            // _listHeader
            _listHeader.Dock = DockStyle.Top;
            _listHeader.Height = 40;
            _listHeader.Padding = new Padding(10, 0, 6, 0);
            _listHeader.Controls.Add(_lblListTitle);
            _listHeader.Controls.Add(_btnAdd);
            _listHeader.Name = "_listHeader";

            _lblListTitle.AutoSize = true;
            _lblListTitle.Text = "Трансформации";
            _lblListTitle.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
            _lblListTitle.Dock = DockStyle.Left;
            _lblListTitle.TextAlign = ContentAlignment.MiddleLeft;
            _lblListTitle.Name = "_lblListTitle";

            _btnAdd.Text = "+ Добавить";
            _btnAdd.Dock = DockStyle.Right;
            _btnAdd.AutoSize = true;
            _btnAdd.FlatStyle = FlatStyle.Flat;
            _btnAdd.Name = "_btnAdd";
            _btnAdd.TabIndex = 0;

            // _listContainer — прокручиваемый список карточек
            _listContainer.Dock = DockStyle.Fill;
            _listContainer.AutoScroll = true;
            _listContainer.Padding = new Padding(8, 6, 8, 0);
            _listContainer.Name = "_listContainer";

            // _listFooter
            _listFooter.Dock = DockStyle.Bottom;
            _listFooter.Height = 28;
            _listFooter.Padding = new Padding(10, 0, 10, 0);
            _listFooter.Controls.Add(_lblTotalWeight);
            _listFooter.Name = "_listFooter";

            _lblTotalWeight.Dock = DockStyle.Fill;
            _lblTotalWeight.TextAlign = ContentAlignment.MiddleCenter;
            _lblTotalWeight.Font = new Font(Font.FontFamily, 8f);
            _lblTotalWeight.Text = "Суммарный вес: 0.00";
            _lblTotalWeight.Name = "_lblTotalWeight";

            // ══════════════════════════════════════════════════════════════════
            // _rightPanel
            // ══════════════════════════════════════════════════════════════════
            _rightPanel.Dock = DockStyle.Fill;
            _rightPanel.Controls.Add(_tabs);
            _rightPanel.Controls.Add(_editorHeader);
            _rightPanel.Name = "_rightPanel";

            // _editorHeader
            _editorHeader.Dock = DockStyle.Top;
            _editorHeader.Height = 36;
            _editorHeader.Padding = new Padding(12, 0, 12, 0);
            _editorHeader.Controls.Add(_lblEditorTitle);
            _editorHeader.Name = "_editorHeader";

            _lblEditorTitle.Dock = DockStyle.Fill;
            _lblEditorTitle.TextAlign = ContentAlignment.MiddleLeft;
            _lblEditorTitle.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
            _lblEditorTitle.Text = "Редактор трансформации";
            _lblEditorTitle.Name = "_lblEditorTitle";

            // ── TabControl ────────────────────────────────────────────────────
            _tabs.Dock = DockStyle.Fill;
            _tabs.TabPages.Add(_tabMain);
            _tabs.TabPages.Add(_tabMatrix);
            _tabs.Name = "_tabs";
            _tabs.TabIndex = 0;

            // ── TabPage «Основное» ────────────────────────────────────────────
            _tabMain.Text = "Основное";
            _tabMain.Padding = new Padding(0);
            _tabMain.Controls.Add(_tblMain);
            _tabMain.Name = "_tabMain";

            // ── _tblMain: вертикальная компоновка редактора ───────────────────
            _tblMain.Dock = DockStyle.Fill;
            _tblMain.ColumnCount = 1;
            _tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _tblMain.RowCount = 8;
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));   // row 0 — подпись «Вариация»
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));   // row 1 — variation row
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));   // row 2 — подпись «Вес»
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));   // row 3 — weight row
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F));   // row 4 — divider
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));   // row 5 — подпись «Коэффициенты»
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));   // row 6 — формула
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 102F));  // row 7 — сетка 2×3
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // row 8 — hint + fill
            _tblMain.RowCount = 9;
            _tblMain.Padding = new Padding(12, 8, 12, 8);
            _tblMain.Name = "_tblMain";

            _tblMain.Controls.Add(_lblVariationCaption, 0, 0);
            _tblMain.Controls.Add(_tblVariationRow, 0, 1);
            _tblMain.Controls.Add(_lblWeightCaption, 0, 2);
            _tblMain.Controls.Add(_tblWeightRow, 0, 3);
            _tblMain.Controls.Add(_divider, 0, 4);
            _tblMain.Controls.Add(_lblAffineCaption, 0, 5);
            _tblMain.Controls.Add(_lblAffineFormula, 0, 6);
            _tblMain.Controls.Add(_tblAffineGrid, 0, 7);
            _tblMain.Controls.Add(_pnlHint, 0, 8);

            // -- подпись «Вариация и цвет» ------------------------------------
            _lblVariationCaption.Text = "ВАРИАЦИЯ И ЦВЕТ";
            _lblVariationCaption.Font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold);
            _lblVariationCaption.Dock = DockStyle.Fill;
            _lblVariationCaption.TextAlign = ContentAlignment.BottomLeft;
            _lblVariationCaption.Name = "_lblVariationCaption";

            // -- строка: ComboBox вариации + превью цвета + кнопка выбора цвета
            _tblVariationRow.Dock = DockStyle.Fill;
            _tblVariationRow.ColumnCount = 3;
            _tblVariationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _tblVariationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34F));
            _tblVariationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            _tblVariationRow.RowCount = 1;
            _tblVariationRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tblVariationRow.Controls.Add(_cmbVariation, 0, 0);
            _tblVariationRow.Controls.Add(_pnlColorPreview, 1, 0);
            _tblVariationRow.Controls.Add(_btnPickColor, 2, 0);
            _tblVariationRow.Name = "_tblVariationRow";
            _tblVariationRow.Margin = Padding.Empty;

            _cmbVariation.Dock = DockStyle.Fill;
            _cmbVariation.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbVariation.DataSource = Enum.GetValues(typeof(Engines.FlameVariation));
            _cmbVariation.Margin = new Padding(0, 0, 6, 0);
            _cmbVariation.Name = "_cmbVariation";
            _cmbVariation.TabIndex = 1;

            _pnlColorPreview.Dock = DockStyle.Fill;
            _pnlColorPreview.BorderStyle = BorderStyle.FixedSingle;
            _pnlColorPreview.Margin = new Padding(0, 0, 4, 0);
            _pnlColorPreview.Cursor = Cursors.Hand;
            _pnlColorPreview.Name = "_pnlColorPreview";
            _pnlColorPreview.Tag = "preserve-backcolor";

            _btnPickColor.Text = "Цвет...";
            _btnPickColor.Dock = DockStyle.Fill;
            _btnPickColor.FlatStyle = FlatStyle.Flat;
            _btnPickColor.Margin = Padding.Empty;
            _btnPickColor.Name = "_btnPickColor";
            _btnPickColor.TabIndex = 2;

            // -- подпись «Вес» ------------------------------------------------
            _lblWeightCaption.Text = "ВЕС ТРАНСФОРМАЦИИ";
            _lblWeightCaption.Font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold);
            _lblWeightCaption.Dock = DockStyle.Fill;
            _lblWeightCaption.TextAlign = ContentAlignment.BottomLeft;
            _lblWeightCaption.Name = "_lblWeightCaption";

            // -- строка: слайдер + значение + процент -------------------------
            _tblWeightRow.Dock = DockStyle.Fill;
            _tblWeightRow.ColumnCount = 3;
            _tblWeightRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _tblWeightRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
            _tblWeightRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            _tblWeightRow.RowCount = 1;
            _tblWeightRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tblWeightRow.Controls.Add(_trkWeight, 0, 0);
            _tblWeightRow.Controls.Add(_lblWeightValue, 1, 0);
            _tblWeightRow.Controls.Add(_lblWeightPercent, 2, 0);
            _tblWeightRow.Margin = Padding.Empty;
            _tblWeightRow.Name = "_tblWeightRow";

            _trkWeight.Dock = DockStyle.Fill;
            _trkWeight.Minimum = 1;
            _trkWeight.Maximum = 100;
            _trkWeight.Value = 10;
            _trkWeight.TickFrequency = 10;
            _trkWeight.AutoSize = false;
            _trkWeight.Margin = new Padding(0, 4, 4, 4);
            _trkWeight.Name = "_trkWeight";
            _trkWeight.TabIndex = 3;

            _lblWeightValue.Dock = DockStyle.Fill;
            _lblWeightValue.TextAlign = ContentAlignment.MiddleRight;
            _lblWeightValue.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
            _lblWeightValue.Text = "1.0";
            _lblWeightValue.Name = "_lblWeightValue";

            _lblWeightPercent.Dock = DockStyle.Fill;
            _lblWeightPercent.TextAlign = ContentAlignment.MiddleRight;
            _lblWeightPercent.Font = new Font(Font.FontFamily, 8f);
            _lblWeightPercent.Text = "100%";
            _lblWeightPercent.Name = "_lblWeightPercent";

            // -- разделитель --------------------------------------------------
            _divider.Dock = DockStyle.Fill;
            _divider.Height = 1;
            _divider.Margin = new Padding(0, 4, 0, 4);
            _divider.Name = "_divider";

            // -- подпись «Аффинные коэффициенты» ------------------------------
            _lblAffineCaption.Text = "АФФИННЫЕ КОЭФФИЦИЕНТЫ";
            _lblAffineCaption.Font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold);
            _lblAffineCaption.Dock = DockStyle.Fill;
            _lblAffineCaption.TextAlign = ContentAlignment.BottomLeft;
            _lblAffineCaption.Name = "_lblAffineCaption";

            _lblAffineFormula.Text = "x' = a·x + b·y + c        y' = d·x + e·y + f";
            _lblAffineFormula.Font = new Font(Font.FontFamily, 8f, FontStyle.Italic);
            _lblAffineFormula.Dock = DockStyle.Fill;
            _lblAffineFormula.TextAlign = ContentAlignment.MiddleLeft;
            _lblAffineFormula.Name = "_lblAffineFormula";

            // -- сетка 2×3 для нудов a–f -------------------------------------
            _tblAffineGrid.Dock = DockStyle.Fill;
            _tblAffineGrid.ColumnCount = 6;
            for (int i = 0; i < 6; i++)
                _tblAffineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66F));
            _tblAffineGrid.RowCount = 2;
            _tblAffineGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            _tblAffineGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            _tblAffineGrid.Margin = Padding.Empty;
            _tblAffineGrid.Name = "_tblAffineGrid";

            // labels
            ConfigureAffineLabel(_lblA, "a"); _tblAffineGrid.Controls.Add(_lblA, 0, 0);
            ConfigureAffineLabel(_lblB, "b"); _tblAffineGrid.Controls.Add(_lblB, 1, 0);
            ConfigureAffineLabel(_lblC, "c"); _tblAffineGrid.Controls.Add(_lblC, 2, 0);
            ConfigureAffineLabel(_lblD, "d"); _tblAffineGrid.Controls.Add(_lblD, 3, 0);
            ConfigureAffineLabel(_lblE, "e"); _tblAffineGrid.Controls.Add(_lblE, 4, 0);
            ConfigureAffineLabel(_lblF, "f"); _tblAffineGrid.Controls.Add(_lblF, 5, 0);

            // NUDs
            int nudTab = 10;
            ConfigureAffineNud(_nudA, nudTab++); _tblAffineGrid.Controls.Add(_nudA, 0, 1);
            ConfigureAffineNud(_nudB, nudTab++); _tblAffineGrid.Controls.Add(_nudB, 1, 1);
            ConfigureAffineNud(_nudC, nudTab++); _tblAffineGrid.Controls.Add(_nudC, 2, 1);
            ConfigureAffineNud(_nudD, nudTab++); _tblAffineGrid.Controls.Add(_nudD, 3, 1);
            ConfigureAffineNud(_nudE, nudTab++); _tblAffineGrid.Controls.Add(_nudE, 4, 1);
            ConfigureAffineNud(_nudF, nudTab++); _tblAffineGrid.Controls.Add(_nudF, 5, 1);

            // -- подсказка ---------------------------------------------------
            _pnlHint.Dock = DockStyle.Top;
            _pnlHint.AutoSize = true;
            _pnlHint.Padding = new Padding(8, 6, 8, 6);
            _pnlHint.Controls.Add(_lblHint);
            _pnlHint.Name = "_pnlHint";
            _pnlHint.Margin = new Padding(0, 6, 0, 0);

            _lblHint.Dock = DockStyle.Fill;
            _lblHint.AutoSize = true;
            _lblHint.Font = new Font(Font.FontFamily, 7.5f, FontStyle.Italic);
            _lblHint.Text = "Совет: a=e=0.5, b=d=0 — равномерное сжатие. Поворот: a=cos θ, b=−sin θ, d=sin θ, e=cos θ.";
            _lblHint.Name = "_lblHint";

            // ── TabPage «Матрица» ─────────────────────────────────────────────
            _tabMatrix.Text = "Матрица";
            _tabMatrix.Padding = new Padding(12);
            _tabMatrix.Controls.Add(_lblMatrixInfo);
            _tabMatrix.Name = "_tabMatrix";

            _lblMatrixInfo.Dock = DockStyle.Top;
            _lblMatrixInfo.AutoSize = true;
            _lblMatrixInfo.Font = new Font(Font.FontFamily, 8.5f, FontStyle.Italic);
            _lblMatrixInfo.Text =
                "Аффинное преобразование задаётся матрицей:\r\n\r\n" +
                "  | a  b |   | x |   | c |\r\n" +
                "  | d  e | × | y | + | f |\r\n\r\n" +
                "a, e — масштаб по осям X и Y\r\n" +
                "b, d — сдвиг (поворот/скос)\r\n" +
                "c, f — смещение (трансляция)\r\n\r\n" +
                "Нелинейная вариация применяется поверх аффинного преобразования.";
            _lblMatrixInfo.Name = "_lblMatrixInfo";

            // ══════════════════════════════════════════════════════════════════
            // _footerPanel
            // ══════════════════════════════════════════════════════════════════
            _footerPanel.Dock = DockStyle.Bottom;
            _footerPanel.Height = 46;
            _footerPanel.Padding = new Padding(10, 6, 10, 6);
            _footerPanel.Controls.Add(_btnOk);
            _footerPanel.Controls.Add(_btnCancel);
            _footerPanel.Controls.Add(_btnDelete);
            _footerPanel.Name = "_footerPanel";

            _btnOk.DialogResult = DialogResult.OK;
            _btnOk.Text = "Применить";
            _btnOk.Size = new Size(100, 30);
            _btnOk.Dock = DockStyle.Right;
            _btnOk.FlatStyle = FlatStyle.Flat;
            _btnOk.Name = "_btnOk";
            _btnOk.TabIndex = 50;

            _btnCancel.DialogResult = DialogResult.Cancel;
            _btnCancel.Text = "Отмена";
            _btnCancel.Size = new Size(84, 30);
            _btnCancel.Dock = DockStyle.Right;
            _btnCancel.FlatStyle = FlatStyle.Flat;
            _btnCancel.Margin = new Padding(0, 0, 4, 0);
            _btnCancel.Name = "_btnCancel";
            _btnCancel.TabIndex = 51;

            _btnDelete.Text = "Удалить выбранную";
            _btnDelete.Size = new Size(148, 30);
            _btnDelete.Dock = DockStyle.Left;
            _btnDelete.FlatStyle = FlatStyle.Flat;
            _btnDelete.Enabled = false;
            _btnDelete.Name = "_btnDelete";
            _btnDelete.TabIndex = 52;

            // ══════════════════════════════════════════════════════════════════
            // FlameTransformEditorForm
            // ══════════════════════════════════════════════════════════════════
            AcceptButton = _btnOk;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = _btnCancel;
            ClientSize = new Size(780, 480);
            MinimumSize = new Size(680, 420);
            Controls.Add(_split);
            Controls.Add(_footerPanel);
            Name = "FlameTransformEditorForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Редактор трансформаций";

            // ── ResumeLayout ──────────────────────────────────────────────────
            _pnlHint.ResumeLayout(false);
            _pnlHint.PerformLayout();
            foreach (NumericUpDown n in new[] { _nudA, _nudB, _nudC, _nudD, _nudE, _nudF })
                ((System.ComponentModel.ISupportInitialize)n).EndInit();
            _tblAffineGrid.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_trkWeight).EndInit();
            _tblWeightRow.ResumeLayout(false);
            _tblVariationRow.ResumeLayout(false);
            _tblMain.ResumeLayout(false);
            _tabMatrix.ResumeLayout(false);
            _tabMatrix.PerformLayout();
            _tabMain.ResumeLayout(false);
            _tabs.ResumeLayout(false);
            _editorHeader.ResumeLayout(false);
            _rightPanel.ResumeLayout(false);
            _listFooter.ResumeLayout(false);
            _listHeader.ResumeLayout(false);
            _leftPanel.ResumeLayout(false);
            _split.Panel1.ResumeLayout(false);
            _split.Panel2.ResumeLayout(false);
            _split.ResumeLayout(false);
            _footerPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        private static void ConfigureAffineLabel(Label lbl, string text)
        {
            lbl.Text = text;
            lbl.Dock = DockStyle.Fill;
            lbl.TextAlign = ContentAlignment.BottomCenter;
            lbl.Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f, FontStyle.Italic);
            lbl.Margin = new Padding(2, 0, 2, 0);
        }

        private static void ConfigureAffineNud(NumericUpDown nud, int tabIndex)
        {
            nud.Dock = DockStyle.Fill;
            nud.DecimalPlaces = 4;
            nud.Increment = 0.1M;
            nud.Minimum = -10M;
            nud.Maximum = 10M;
            nud.TextAlign = HorizontalAlignment.Right;
            nud.Margin = new Padding(2, 0, 2, 0);
            nud.TabIndex = tabIndex;
        }

        #endregion
    }
}
