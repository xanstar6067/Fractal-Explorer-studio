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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FlameTransformEditorForm));
            _split = new SplitContainer();
            _leftPanel = new Panel();
            _listContainer = new Panel();
            _listFooter = new Panel();
            _lblTotalWeight = new Label();
            _listHeader = new Panel();
            _btnAdd = new Button();
            _lblListTitle = new Label();
            _rightPanel = new Panel();
            _tabs = new TabControl();
            _tabMain = new TabPage();
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
            _lblA = new Label();
            _lblB = new Label();
            _lblC = new Label();
            _lblD = new Label();
            _lblE = new Label();
            _lblF = new Label();
            _nudA = new NumericUpDown();
            _nudB = new NumericUpDown();
            _nudC = new NumericUpDown();
            _nudD = new NumericUpDown();
            _nudE = new NumericUpDown();
            _nudF = new NumericUpDown();
            _pnlHint = new Panel();
            _lblHint = new Label();
            _tabMatrix = new TabPage();
            _lblMatrixInfo = new Label();
            _editorHeader = new Panel();
            _lblEditorTitle = new Label();
            _footerPanel = new Panel();
            _btnOk = new Button();
            _btnCancel = new Button();
            _btnDelete = new Button();
            ((System.ComponentModel.ISupportInitialize)_split).BeginInit();
            _split.Panel1.SuspendLayout();
            _split.Panel2.SuspendLayout();
            _split.SuspendLayout();
            _leftPanel.SuspendLayout();
            _listFooter.SuspendLayout();
            _listHeader.SuspendLayout();
            _rightPanel.SuspendLayout();
            _tabs.SuspendLayout();
            _tabMain.SuspendLayout();
            _tblMain.SuspendLayout();
            _tblVariationRow.SuspendLayout();
            _tblWeightRow.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_trkWeight).BeginInit();
            _tblAffineGrid.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_nudA).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudB).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudC).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudD).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudE).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudF).BeginInit();
            _pnlHint.SuspendLayout();
            _tabMatrix.SuspendLayout();
            _editorHeader.SuspendLayout();
            _footerPanel.SuspendLayout();
            SuspendLayout();
            // 
            // _split
            // 
            _split.Dock = DockStyle.Fill;
            _split.FixedPanel = FixedPanel.Panel1;
            _split.IsSplitterFixed = true;
            _split.Location = new Point(0, 0);
            _split.Name = "_split";
            // 
            // _split.Panel1
            // 
            _split.Panel1.Controls.Add(_leftPanel);
            // 
            // _split.Panel2
            // 
            _split.Panel2.Controls.Add(_rightPanel);
            _split.Size = new Size(780, 434);
            _split.SplitterDistance = 121;
            _split.TabIndex = 0;
            // 
            // _leftPanel
            // 
            _leftPanel.Controls.Add(_listContainer);
            _leftPanel.Controls.Add(_listFooter);
            _leftPanel.Controls.Add(_listHeader);
            _leftPanel.Dock = DockStyle.Fill;
            _leftPanel.Location = new Point(0, 0);
            _leftPanel.Name = "_leftPanel";
            _leftPanel.Size = new Size(121, 434);
            _leftPanel.TabIndex = 0;
            // 
            // _listContainer
            // 
            _listContainer.AutoScroll = true;
            _listContainer.Dock = DockStyle.Fill;
            _listContainer.Location = new Point(0, 40);
            _listContainer.Name = "_listContainer";
            _listContainer.Padding = new Padding(8, 6, 8, 0);
            _listContainer.Size = new Size(121, 366);
            _listContainer.TabIndex = 0;
            // 
            // _listFooter
            // 
            _listFooter.Controls.Add(_lblTotalWeight);
            _listFooter.Dock = DockStyle.Bottom;
            _listFooter.Location = new Point(0, 406);
            _listFooter.Name = "_listFooter";
            _listFooter.Padding = new Padding(10, 0, 10, 0);
            _listFooter.Size = new Size(121, 28);
            _listFooter.TabIndex = 1;
            // 
            // _lblTotalWeight
            // 
            _lblTotalWeight.Dock = DockStyle.Fill;
            _lblTotalWeight.Font = new Font("Segoe UI", 8F);
            _lblTotalWeight.Location = new Point(10, 0);
            _lblTotalWeight.Name = "_lblTotalWeight";
            _lblTotalWeight.Size = new Size(101, 28);
            _lblTotalWeight.TabIndex = 0;
            _lblTotalWeight.Text = "Суммарный вес: 0.00";
            _lblTotalWeight.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // _listHeader
            // 
            _listHeader.Controls.Add(_btnAdd);
            _listHeader.Controls.Add(_lblListTitle);
            _listHeader.Dock = DockStyle.Top;
            _listHeader.Location = new Point(0, 0);
            _listHeader.Name = "_listHeader";
            _listHeader.Padding = new Padding(10, 0, 6, 0);
            _listHeader.Size = new Size(121, 40);
            _listHeader.TabIndex = 2;
            // 
            // _btnAdd
            // 
            _btnAdd.AutoSize = true;
            _btnAdd.Dock = DockStyle.Right;
            _btnAdd.FlatStyle = FlatStyle.Flat;
            _btnAdd.Location = new Point(33, 0);
            _btnAdd.Name = "_btnAdd";
            _btnAdd.Size = new Size(82, 40);
            _btnAdd.TabIndex = 0;
            _btnAdd.Text = "+ Добавить";
            // 
            // _lblListTitle
            // 
            _lblListTitle.AutoEllipsis = true;
            _lblListTitle.Dock = DockStyle.Fill;
            _lblListTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _lblListTitle.Location = new Point(10, 0);
            _lblListTitle.Name = "_lblListTitle";
            _lblListTitle.Padding = new Padding(0, 0, 8, 0);
            _lblListTitle.Size = new Size(105, 40);
            _lblListTitle.TabIndex = 1;
            _lblListTitle.Text = "Трансформации";
            _lblListTitle.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _rightPanel
            // 
            _rightPanel.Controls.Add(_tabs);
            _rightPanel.Controls.Add(_editorHeader);
            _rightPanel.Dock = DockStyle.Fill;
            _rightPanel.Location = new Point(0, 0);
            _rightPanel.Name = "_rightPanel";
            _rightPanel.Size = new Size(655, 434);
            _rightPanel.TabIndex = 0;
            // 
            // _tabs
            // 
            _tabs.Controls.Add(_tabMain);
            _tabs.Controls.Add(_tabMatrix);
            _tabs.Dock = DockStyle.Fill;
            _tabs.Location = new Point(0, 36);
            _tabs.Name = "_tabs";
            _tabs.SelectedIndex = 0;
            _tabs.Size = new Size(655, 398);
            _tabs.TabIndex = 0;
            // 
            // _tabMain
            // 
            _tabMain.Controls.Add(_tblMain);
            _tabMain.Location = new Point(4, 24);
            _tabMain.Name = "_tabMain";
            _tabMain.Size = new Size(647, 370);
            _tabMain.TabIndex = 0;
            _tabMain.Text = "Основное";
            // 
            // _tblMain
            // 
            _tblMain.ColumnCount = 1;
            _tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _tblMain.Controls.Add(_lblVariationCaption, 0, 0);
            _tblMain.Controls.Add(_tblVariationRow, 0, 1);
            _tblMain.Controls.Add(_lblWeightCaption, 0, 2);
            _tblMain.Controls.Add(_tblWeightRow, 0, 3);
            _tblMain.Controls.Add(_divider, 0, 4);
            _tblMain.Controls.Add(_lblAffineCaption, 0, 5);
            _tblMain.Controls.Add(_lblAffineFormula, 0, 6);
            _tblMain.Controls.Add(_tblAffineGrid, 0, 7);
            _tblMain.Controls.Add(_pnlHint, 0, 8);
            _tblMain.Dock = DockStyle.Fill;
            _tblMain.Location = new Point(0, 0);
            _tblMain.Name = "_tblMain";
            _tblMain.Padding = new Padding(12, 8, 12, 8);
            _tblMain.RowCount = 9;
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F));
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 102F));
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tblMain.Size = new Size(647, 370);
            _tblMain.TabIndex = 0;
            // 
            // _lblVariationCaption
            // 
            _lblVariationCaption.Dock = DockStyle.Fill;
            _lblVariationCaption.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            _lblVariationCaption.Location = new Point(15, 8);
            _lblVariationCaption.Name = "_lblVariationCaption";
            _lblVariationCaption.Size = new Size(617, 22);
            _lblVariationCaption.TabIndex = 0;
            _lblVariationCaption.Text = "ВАРИАЦИЯ И ЦВЕТ";
            _lblVariationCaption.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _tblVariationRow
            // 
            _tblVariationRow.ColumnCount = 3;
            _tblVariationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _tblVariationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34F));
            _tblVariationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            _tblVariationRow.Controls.Add(_cmbVariation, 0, 0);
            _tblVariationRow.Controls.Add(_pnlColorPreview, 1, 0);
            _tblVariationRow.Controls.Add(_btnPickColor, 2, 0);
            _tblVariationRow.Dock = DockStyle.Fill;
            _tblVariationRow.Location = new Point(12, 30);
            _tblVariationRow.Margin = new Padding(0);
            _tblVariationRow.Name = "_tblVariationRow";
            _tblVariationRow.RowCount = 1;
            _tblVariationRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tblVariationRow.Size = new Size(623, 36);
            _tblVariationRow.TabIndex = 1;
            // 
            // _cmbVariation
            // 
            _cmbVariation.DataSource = new Engines.FlameVariation[]
    {
    Engines.FlameVariation.Linear,
    Engines.FlameVariation.Sinusoidal,
    Engines.FlameVariation.Spherical
    };
            _cmbVariation.Dock = DockStyle.Fill;
            _cmbVariation.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbVariation.Location = new Point(0, 0);
            _cmbVariation.Margin = new Padding(0, 0, 6, 0);
            _cmbVariation.Name = "_cmbVariation";
            _cmbVariation.Size = new Size(493, 23);
            _cmbVariation.TabIndex = 1;
            // 
            // _pnlColorPreview
            // 
            _pnlColorPreview.BorderStyle = BorderStyle.FixedSingle;
            _pnlColorPreview.Cursor = Cursors.Hand;
            _pnlColorPreview.Dock = DockStyle.Fill;
            _pnlColorPreview.Location = new Point(499, 0);
            _pnlColorPreview.Margin = new Padding(0, 0, 4, 0);
            _pnlColorPreview.Name = "_pnlColorPreview";
            _pnlColorPreview.Size = new Size(30, 36);
            _pnlColorPreview.TabIndex = 2;
            _pnlColorPreview.Tag = "preserve-backcolor";
            // 
            // _btnPickColor
            // 
            _btnPickColor.Dock = DockStyle.Fill;
            _btnPickColor.FlatStyle = FlatStyle.Flat;
            _btnPickColor.Location = new Point(533, 0);
            _btnPickColor.Margin = new Padding(0);
            _btnPickColor.Name = "_btnPickColor";
            _btnPickColor.Size = new Size(90, 36);
            _btnPickColor.TabIndex = 2;
            _btnPickColor.Text = "Цвет...";
            // 
            // _lblWeightCaption
            // 
            _lblWeightCaption.Dock = DockStyle.Fill;
            _lblWeightCaption.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            _lblWeightCaption.Location = new Point(15, 66);
            _lblWeightCaption.Name = "_lblWeightCaption";
            _lblWeightCaption.Size = new Size(617, 22);
            _lblWeightCaption.TabIndex = 2;
            _lblWeightCaption.Text = "ВЕС ТРАНСФОРМАЦИИ";
            _lblWeightCaption.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _tblWeightRow
            // 
            _tblWeightRow.ColumnCount = 3;
            _tblWeightRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _tblWeightRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
            _tblWeightRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            _tblWeightRow.Controls.Add(_trkWeight, 0, 0);
            _tblWeightRow.Controls.Add(_lblWeightValue, 1, 0);
            _tblWeightRow.Controls.Add(_lblWeightPercent, 2, 0);
            _tblWeightRow.Dock = DockStyle.Fill;
            _tblWeightRow.Location = new Point(12, 88);
            _tblWeightRow.Margin = new Padding(0);
            _tblWeightRow.Name = "_tblWeightRow";
            _tblWeightRow.RowCount = 1;
            _tblWeightRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tblWeightRow.Size = new Size(623, 38);
            _tblWeightRow.TabIndex = 3;
            // 
            // _trkWeight
            // 
            _trkWeight.AutoSize = false;
            _trkWeight.Dock = DockStyle.Fill;
            _trkWeight.Location = new Point(0, 4);
            _trkWeight.Margin = new Padding(0, 4, 4, 4);
            _trkWeight.Maximum = 100;
            _trkWeight.Minimum = 1;
            _trkWeight.Name = "_trkWeight";
            _trkWeight.Size = new Size(527, 30);
            _trkWeight.TabIndex = 3;
            _trkWeight.TickFrequency = 10;
            _trkWeight.Value = 10;
            // 
            // _lblWeightValue
            // 
            _lblWeightValue.Dock = DockStyle.Fill;
            _lblWeightValue.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _lblWeightValue.Location = new Point(534, 0);
            _lblWeightValue.Name = "_lblWeightValue";
            _lblWeightValue.Size = new Size(42, 38);
            _lblWeightValue.TabIndex = 4;
            _lblWeightValue.Text = "1.0";
            _lblWeightValue.TextAlign = ContentAlignment.MiddleRight;
            // 
            // _lblWeightPercent
            // 
            _lblWeightPercent.Dock = DockStyle.Fill;
            _lblWeightPercent.Font = new Font("Segoe UI", 8F);
            _lblWeightPercent.Location = new Point(582, 0);
            _lblWeightPercent.Name = "_lblWeightPercent";
            _lblWeightPercent.Size = new Size(38, 38);
            _lblWeightPercent.TabIndex = 5;
            _lblWeightPercent.Text = "100%";
            _lblWeightPercent.TextAlign = ContentAlignment.MiddleRight;
            // 
            // _divider
            // 
            _divider.Dock = DockStyle.Fill;
            _divider.Location = new Point(12, 130);
            _divider.Margin = new Padding(0, 4, 0, 4);
            _divider.Name = "_divider";
            _divider.Size = new Size(623, 2);
            _divider.TabIndex = 4;
            // 
            // _lblAffineCaption
            // 
            _lblAffineCaption.Dock = DockStyle.Fill;
            _lblAffineCaption.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            _lblAffineCaption.Location = new Point(15, 136);
            _lblAffineCaption.Name = "_lblAffineCaption";
            _lblAffineCaption.Size = new Size(617, 22);
            _lblAffineCaption.TabIndex = 5;
            _lblAffineCaption.Text = "АФФИННЫЕ КОЭФФИЦИЕНТЫ";
            _lblAffineCaption.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _lblAffineFormula
            // 
            _lblAffineFormula.Dock = DockStyle.Fill;
            _lblAffineFormula.Font = new Font("Segoe UI", 8F, FontStyle.Italic);
            _lblAffineFormula.Location = new Point(15, 158);
            _lblAffineFormula.Name = "_lblAffineFormula";
            _lblAffineFormula.Size = new Size(617, 18);
            _lblAffineFormula.TabIndex = 6;
            _lblAffineFormula.Text = "x' = a·x + b·y + c        y' = d·x + e·y + f";
            _lblAffineFormula.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _tblAffineGrid
            // 
            _tblAffineGrid.ColumnCount = 6;
            _tblAffineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66F));
            _tblAffineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66F));
            _tblAffineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66F));
            _tblAffineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66F));
            _tblAffineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66F));
            _tblAffineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66F));
            _tblAffineGrid.Controls.Add(_lblA, 0, 0);
            _tblAffineGrid.Controls.Add(_lblB, 1, 0);
            _tblAffineGrid.Controls.Add(_lblC, 2, 0);
            _tblAffineGrid.Controls.Add(_lblD, 3, 0);
            _tblAffineGrid.Controls.Add(_lblE, 4, 0);
            _tblAffineGrid.Controls.Add(_lblF, 5, 0);
            _tblAffineGrid.Controls.Add(_nudA, 0, 1);
            _tblAffineGrid.Controls.Add(_nudB, 1, 1);
            _tblAffineGrid.Controls.Add(_nudC, 2, 1);
            _tblAffineGrid.Controls.Add(_nudD, 3, 1);
            _tblAffineGrid.Controls.Add(_nudE, 4, 1);
            _tblAffineGrid.Controls.Add(_nudF, 5, 1);
            _tblAffineGrid.Dock = DockStyle.Fill;
            _tblAffineGrid.Location = new Point(12, 176);
            _tblAffineGrid.Margin = new Padding(0);
            _tblAffineGrid.Name = "_tblAffineGrid";
            _tblAffineGrid.RowCount = 2;
            _tblAffineGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            _tblAffineGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            _tblAffineGrid.Size = new Size(623, 102);
            _tblAffineGrid.TabIndex = 7;
            // 
            // _lblA
            // 
            _lblA.Location = new Point(3, 0);
            _lblA.Name = "_lblA";
            _lblA.Size = new Size(97, 18);
            _lblA.TabIndex = 0;
            // 
            // _lblB
            // 
            _lblB.Location = new Point(106, 0);
            _lblB.Name = "_lblB";
            _lblB.Size = new Size(97, 18);
            _lblB.TabIndex = 1;
            // 
            // _lblC
            // 
            _lblC.Location = new Point(209, 0);
            _lblC.Name = "_lblC";
            _lblC.Size = new Size(97, 18);
            _lblC.TabIndex = 2;
            // 
            // _lblD
            // 
            _lblD.Location = new Point(312, 0);
            _lblD.Name = "_lblD";
            _lblD.Size = new Size(97, 18);
            _lblD.TabIndex = 3;
            // 
            // _lblE
            // 
            _lblE.Location = new Point(415, 0);
            _lblE.Name = "_lblE";
            _lblE.Size = new Size(97, 18);
            _lblE.TabIndex = 4;
            // 
            // _lblF
            // 
            _lblF.Location = new Point(518, 0);
            _lblF.Name = "_lblF";
            _lblF.Size = new Size(100, 18);
            _lblF.TabIndex = 5;
            // 
            // _nudA
            // 
            _nudA.Location = new Point(3, 21);
            _nudA.Name = "_nudA";
            _nudA.Size = new Size(97, 23);
            _nudA.TabIndex = 6;
            // 
            // _nudB
            // 
            _nudB.Location = new Point(106, 21);
            _nudB.Name = "_nudB";
            _nudB.Size = new Size(97, 23);
            _nudB.TabIndex = 7;
            // 
            // _nudC
            // 
            _nudC.Location = new Point(209, 21);
            _nudC.Name = "_nudC";
            _nudC.Size = new Size(97, 23);
            _nudC.TabIndex = 8;
            // 
            // _nudD
            // 
            _nudD.Location = new Point(312, 21);
            _nudD.Name = "_nudD";
            _nudD.Size = new Size(97, 23);
            _nudD.TabIndex = 9;
            // 
            // _nudE
            // 
            _nudE.Location = new Point(415, 21);
            _nudE.Name = "_nudE";
            _nudE.Size = new Size(97, 23);
            _nudE.TabIndex = 10;
            // 
            // _nudF
            // 
            _nudF.Location = new Point(518, 21);
            _nudF.Name = "_nudF";
            _nudF.Size = new Size(102, 23);
            _nudF.TabIndex = 11;
            // 
            // _pnlHint
            // 
            _pnlHint.AutoSize = true;
            _pnlHint.Controls.Add(_lblHint);
            _pnlHint.Dock = DockStyle.Top;
            _pnlHint.Location = new Point(12, 284);
            _pnlHint.Margin = new Padding(0, 6, 0, 0);
            _pnlHint.Name = "_pnlHint";
            _pnlHint.Padding = new Padding(8, 6, 8, 6);
            _pnlHint.Size = new Size(623, 24);
            _pnlHint.TabIndex = 8;
            // 
            // _lblHint
            // 
            _lblHint.AutoSize = true;
            _lblHint.Dock = DockStyle.Fill;
            _lblHint.Font = new Font("Segoe UI", 7.5F, FontStyle.Italic);
            _lblHint.Location = new Point(8, 6);
            _lblHint.Name = "_lblHint";
            _lblHint.Size = new Size(435, 12);
            _lblHint.TabIndex = 0;
            _lblHint.Text = "Совет: a=e=0.5, b=d=0 — равномерное сжатие. Поворот: a=cos θ, b=−sin θ, d=sin θ, e=cos θ.";
            // 
            // _tabMatrix
            // 
            _tabMatrix.Controls.Add(_lblMatrixInfo);
            _tabMatrix.Location = new Point(4, 24);
            _tabMatrix.Name = "_tabMatrix";
            _tabMatrix.Padding = new Padding(12);
            _tabMatrix.Size = new Size(17, 36);
            _tabMatrix.TabIndex = 1;
            _tabMatrix.Text = "Матрица";
            // 
            // _lblMatrixInfo
            // 
            _lblMatrixInfo.AutoSize = true;
            _lblMatrixInfo.Dock = DockStyle.Top;
            _lblMatrixInfo.Font = new Font("Segoe UI", 8.5F, FontStyle.Italic);
            _lblMatrixInfo.Location = new Point(12, 12);
            _lblMatrixInfo.Name = "_lblMatrixInfo";
            _lblMatrixInfo.Size = new Size(413, 150);
            _lblMatrixInfo.TabIndex = 0;
            _lblMatrixInfo.Text = resources.GetString("_lblMatrixInfo.Text");
            // 
            // _editorHeader
            // 
            _editorHeader.Controls.Add(_lblEditorTitle);
            _editorHeader.Dock = DockStyle.Top;
            _editorHeader.Location = new Point(0, 0);
            _editorHeader.Name = "_editorHeader";
            _editorHeader.Padding = new Padding(12, 0, 12, 0);
            _editorHeader.Size = new Size(655, 36);
            _editorHeader.TabIndex = 1;
            // 
            // _lblEditorTitle
            // 
            _lblEditorTitle.Dock = DockStyle.Fill;
            _lblEditorTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _lblEditorTitle.Location = new Point(12, 0);
            _lblEditorTitle.Name = "_lblEditorTitle";
            _lblEditorTitle.Size = new Size(631, 36);
            _lblEditorTitle.TabIndex = 0;
            _lblEditorTitle.Text = "Редактор трансформации";
            _lblEditorTitle.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _footerPanel
            // 
            _footerPanel.Controls.Add(_btnOk);
            _footerPanel.Controls.Add(_btnCancel);
            _footerPanel.Controls.Add(_btnDelete);
            _footerPanel.Dock = DockStyle.Bottom;
            _footerPanel.Location = new Point(0, 434);
            _footerPanel.Name = "_footerPanel";
            _footerPanel.Padding = new Padding(10, 6, 10, 6);
            _footerPanel.Size = new Size(780, 46);
            _footerPanel.TabIndex = 1;
            // 
            // _btnOk
            // 
            _btnOk.DialogResult = DialogResult.OK;
            _btnOk.Dock = DockStyle.Right;
            _btnOk.FlatStyle = FlatStyle.Flat;
            _btnOk.Location = new Point(586, 6);
            _btnOk.Name = "_btnOk";
            _btnOk.Size = new Size(100, 34);
            _btnOk.TabIndex = 50;
            _btnOk.Text = "Применить";
            // 
            // _btnCancel
            // 
            _btnCancel.DialogResult = DialogResult.Cancel;
            _btnCancel.Dock = DockStyle.Right;
            _btnCancel.FlatStyle = FlatStyle.Flat;
            _btnCancel.Location = new Point(686, 6);
            _btnCancel.Margin = new Padding(0, 0, 4, 0);
            _btnCancel.Name = "_btnCancel";
            _btnCancel.Size = new Size(84, 34);
            _btnCancel.TabIndex = 51;
            _btnCancel.Text = "Отмена";
            // 
            // _btnDelete
            // 
            _btnDelete.Dock = DockStyle.Left;
            _btnDelete.Enabled = false;
            _btnDelete.FlatStyle = FlatStyle.Flat;
            _btnDelete.Location = new Point(10, 6);
            _btnDelete.Name = "_btnDelete";
            _btnDelete.Size = new Size(148, 34);
            _btnDelete.TabIndex = 52;
            _btnDelete.Text = "Удалить выбранную";
            // 
            // FlameTransformEditorForm
            // 
            AcceptButton = _btnOk;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = _btnCancel;
            ClientSize = new Size(780, 480);
            Controls.Add(_split);
            Controls.Add(_footerPanel);
            MinimumSize = new Size(680, 420);
            Name = "FlameTransformEditorForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Редактор трансформаций";
            _split.Panel1.ResumeLayout(false);
            _split.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_split).EndInit();
            _split.ResumeLayout(false);
            _leftPanel.ResumeLayout(false);
            _listFooter.ResumeLayout(false);
            _listHeader.ResumeLayout(false);
            _listHeader.PerformLayout();
            _rightPanel.ResumeLayout(false);
            _tabs.ResumeLayout(false);
            _tabMain.ResumeLayout(false);
            _tblMain.ResumeLayout(false);
            _tblMain.PerformLayout();
            _tblVariationRow.ResumeLayout(false);
            _tblWeightRow.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_trkWeight).EndInit();
            _tblAffineGrid.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_nudA).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudB).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudC).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudD).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudE).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudF).EndInit();
            _pnlHint.ResumeLayout(false);
            _pnlHint.PerformLayout();
            _tabMatrix.ResumeLayout(false);
            _tabMatrix.PerformLayout();
            _editorHeader.ResumeLayout(false);
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
