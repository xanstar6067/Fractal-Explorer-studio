namespace FractalExplorer.Forms.Fractals
{
    partial class IfsTransformEditorForm
    {
        private System.ComponentModel.IContainer? components = null;

        private SplitContainer _split;

        private Panel _leftPanel;
        private Panel _listHeader;
        private Label _lblListTitle;
        private Button _btnAdd;
        private Panel _listContainer;
        private Panel _listFooter;
        private Label _lblTotalProbability;

        private Panel _rightPanel;
        private Panel _editorHeader;
        private Label _lblEditorTitle;
        private TableLayoutPanel _tblMain;
        private Label _lblProbabilityCaption;
        private TableLayoutPanel _tblProbabilityRow;
        private TrackBar _trkProbability;
        private Label _lblProbabilityValue;
        private Label _lblProbabilityPercent;
        private Panel _divider;
        private Label _lblAffineCaption;
        private Label _lblAffineFormula;
        private TableLayoutPanel _tblAffineGrid;

        private Label _lblA;
        private Label _lblB;
        private Label _lblC;
        private Label _lblD;
        private Label _lblE;
        private Label _lblF;

        private NumericUpDown _nudA;
        private NumericUpDown _nudB;
        private NumericUpDown _nudC;
        private NumericUpDown _nudD;
        private NumericUpDown _nudE;
        private NumericUpDown _nudF;

        private Panel _footerPanel;
        private Button _btnUndo;
        private Button _btnNormalize;
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
            _split = new SplitContainer();
            _leftPanel = new Panel();
            _listContainer = new Panel();
            _listFooter = new Panel();
            _lblTotalProbability = new Label();
            _listHeader = new Panel();
            _btnAdd = new Button();
            _lblListTitle = new Label();
            _rightPanel = new Panel();
            _tblMain = new TableLayoutPanel();
            _lblProbabilityCaption = new Label();
            _tblProbabilityRow = new TableLayoutPanel();
            _trkProbability = new TrackBar();
            _lblProbabilityValue = new Label();
            _lblProbabilityPercent = new Label();
            _divider = new Panel();
            _lblAffineCaption = new Label();
            _lblAffineFormula = new Label();
            _tblAffineGrid = new TableLayoutPanel();
            _lblA = new Label();
            _nudA = new NumericUpDown();
            _lblB = new Label();
            _nudB = new NumericUpDown();
            _lblC = new Label();
            _nudC = new NumericUpDown();
            _lblD = new Label();
            _nudD = new NumericUpDown();
            _lblE = new Label();
            _nudE = new NumericUpDown();
            _lblF = new Label();
            _nudF = new NumericUpDown();
            _editorHeader = new Panel();
            _lblEditorTitle = new Label();
            _footerPanel = new Panel();
            _btnOk = new Button();
            _btnCancel = new Button();
            _btnNormalize = new Button();
            _btnUndo = new Button();
            ((System.ComponentModel.ISupportInitialize)_split).BeginInit();
            _split.Panel1.SuspendLayout();
            _split.Panel2.SuspendLayout();
            _split.SuspendLayout();
            _leftPanel.SuspendLayout();
            _listFooter.SuspendLayout();
            _listHeader.SuspendLayout();
            _rightPanel.SuspendLayout();
            _tblMain.SuspendLayout();
            _tblProbabilityRow.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_trkProbability).BeginInit();
            _tblAffineGrid.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_nudA).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudB).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudC).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudD).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudE).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudF).BeginInit();
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
            _split.Panel1MinSize = 250;
            // 
            // _split.Panel2
            // 
            _split.Panel2.Controls.Add(_rightPanel);
            _split.Size = new Size(920, 460);
            _split.SplitterDistance = 250;
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
            _leftPanel.Size = new Size(250, 460);
            _leftPanel.TabIndex = 0;
            // 
            // _listContainer
            // 
            _listContainer.AutoScroll = true;
            _listContainer.Dock = DockStyle.Fill;
            _listContainer.Location = new Point(0, 44);
            _listContainer.Name = "_listContainer";
            _listContainer.Padding = new Padding(8, 6, 6, 0);
            _listContainer.Size = new Size(250, 388);
            _listContainer.TabIndex = 0;
            // 
            // _listFooter
            // 
            _listFooter.Controls.Add(_lblTotalProbability);
            _listFooter.Dock = DockStyle.Bottom;
            _listFooter.Location = new Point(0, 432);
            _listFooter.Name = "_listFooter";
            _listFooter.Padding = new Padding(10, 0, 10, 0);
            _listFooter.Size = new Size(250, 28);
            _listFooter.TabIndex = 1;
            // 
            // _lblTotalProbability
            // 
            _lblTotalProbability.Dock = DockStyle.Fill;
            _lblTotalProbability.Font = new Font("Segoe UI", 8F);
            _lblTotalProbability.Location = new Point(10, 0);
            _lblTotalProbability.Name = "_lblTotalProbability";
            _lblTotalProbability.Size = new Size(230, 28);
            _lblTotalProbability.TabIndex = 0;
            _lblTotalProbability.Text = "Суммарная вероятность: 0.0000";
            _lblTotalProbability.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // _listHeader
            // 
            _listHeader.Controls.Add(_btnAdd);
            _listHeader.Controls.Add(_lblListTitle);
            _listHeader.Dock = DockStyle.Top;
            _listHeader.Location = new Point(0, 0);
            _listHeader.Name = "_listHeader";
            _listHeader.Padding = new Padding(10, 0, 10, 0);
            _listHeader.Size = new Size(250, 44);
            _listHeader.TabIndex = 2;
            // 
            // _btnAdd
            // 
            _btnAdd.Dock = DockStyle.Right;
            _btnAdd.FlatStyle = FlatStyle.Flat;
            _btnAdd.Location = new Point(148, 0);
            _btnAdd.Name = "_btnAdd";
            _btnAdd.Size = new Size(92, 44);
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
            _lblListTitle.Size = new Size(230, 44);
            _lblListTitle.TabIndex = 1;
            _lblListTitle.Text = "IFS-преобразования";
            _lblListTitle.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _rightPanel
            // 
            _rightPanel.Controls.Add(_tblMain);
            _rightPanel.Controls.Add(_editorHeader);
            _rightPanel.Dock = DockStyle.Fill;
            _rightPanel.Location = new Point(0, 0);
            _rightPanel.Name = "_rightPanel";
            _rightPanel.Size = new Size(666, 460);
            _rightPanel.TabIndex = 0;
            // 
            // _tblMain
            // 
            _tblMain.ColumnCount = 1;
            _tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _tblMain.Controls.Add(_lblProbabilityCaption, 0, 0);
            _tblMain.Controls.Add(_tblProbabilityRow, 0, 1);
            _tblMain.Controls.Add(_divider, 0, 2);
            _tblMain.Controls.Add(_lblAffineCaption, 0, 3);
            _tblMain.Controls.Add(_lblAffineFormula, 0, 4);
            _tblMain.Controls.Add(_tblAffineGrid, 0, 5);
            _tblMain.Dock = DockStyle.Fill;
            _tblMain.Location = new Point(0, 36);
            _tblMain.Name = "_tblMain";
            _tblMain.Padding = new Padding(12, 10, 12, 8);
            _tblMain.RowCount = 6;
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F));
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            _tblMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tblMain.Size = new Size(666, 424);
            _tblMain.TabIndex = 0;
            // 
            // _lblProbabilityCaption
            // 
            _lblProbabilityCaption.Dock = DockStyle.Fill;
            _lblProbabilityCaption.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            _lblProbabilityCaption.Location = new Point(15, 10);
            _lblProbabilityCaption.Name = "_lblProbabilityCaption";
            _lblProbabilityCaption.Size = new Size(636, 22);
            _lblProbabilityCaption.TabIndex = 0;
            _lblProbabilityCaption.Text = "ВЕРОЯТНОСТЬ";
            _lblProbabilityCaption.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _tblProbabilityRow
            // 
            _tblProbabilityRow.ColumnCount = 3;
            _tblProbabilityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _tblProbabilityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));
            _tblProbabilityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            _tblProbabilityRow.Controls.Add(_trkProbability, 0, 0);
            _tblProbabilityRow.Controls.Add(_lblProbabilityValue, 1, 0);
            _tblProbabilityRow.Controls.Add(_lblProbabilityPercent, 2, 0);
            _tblProbabilityRow.Dock = DockStyle.Fill;
            _tblProbabilityRow.Location = new Point(15, 35);
            _tblProbabilityRow.Margin = new Padding(0);
            _tblProbabilityRow.Name = "_tblProbabilityRow";
            _tblProbabilityRow.RowCount = 1;
            _tblProbabilityRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tblProbabilityRow.Size = new Size(636, 34);
            _tblProbabilityRow.TabIndex = 1;
            // 
            // _trkProbability
            // 
            _trkProbability.AutoSize = false;
            _trkProbability.Dock = DockStyle.Fill;
            _trkProbability.LargeChange = 50;
            _trkProbability.Location = new Point(0, 2);
            _trkProbability.Margin = new Padding(0, 2, 4, 2);
            _trkProbability.Maximum = 1000;
            _trkProbability.Name = "_trkProbability";
            _trkProbability.Size = new Size(528, 30);
            _trkProbability.SmallChange = 10;
            _trkProbability.TabIndex = 0;
            _trkProbability.TickFrequency = 100;
            _trkProbability.Value = 500;
            // 
            // _lblProbabilityValue
            // 
            _lblProbabilityValue.Dock = DockStyle.Fill;
            _lblProbabilityValue.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _lblProbabilityValue.Location = new Point(535, 0);
            _lblProbabilityValue.Name = "_lblProbabilityValue";
            _lblProbabilityValue.Size = new Size(54, 34);
            _lblProbabilityValue.TabIndex = 1;
            _lblProbabilityValue.Text = "0.500";
            _lblProbabilityValue.TextAlign = ContentAlignment.MiddleRight;
            // 
            // _lblProbabilityPercent
            // 
            _lblProbabilityPercent.Dock = DockStyle.Fill;
            _lblProbabilityPercent.Font = new Font("Segoe UI", 8F);
            _lblProbabilityPercent.Location = new Point(595, 0);
            _lblProbabilityPercent.Name = "_lblProbabilityPercent";
            _lblProbabilityPercent.Size = new Size(38, 34);
            _lblProbabilityPercent.TabIndex = 2;
            _lblProbabilityPercent.Text = "50%";
            _lblProbabilityPercent.TextAlign = ContentAlignment.MiddleRight;
            // 
            // _divider
            // 
            _divider.Dock = DockStyle.Fill;
            _divider.Location = new Point(15, 69);
            _divider.Name = "_divider";
            _divider.Size = new Size(636, 4);
            _divider.TabIndex = 2;
            // 
            // _lblAffineCaption
            // 
            _lblAffineCaption.Dock = DockStyle.Fill;
            _lblAffineCaption.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            _lblAffineCaption.Location = new Point(15, 76);
            _lblAffineCaption.Name = "_lblAffineCaption";
            _lblAffineCaption.Size = new Size(636, 22);
            _lblAffineCaption.TabIndex = 3;
            _lblAffineCaption.Text = "АФФИННОЕ ПРЕОБРАЗОВАНИЕ";
            _lblAffineCaption.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _lblAffineFormula
            // 
            _lblAffineFormula.AutoSize = true;
            _lblAffineFormula.Dock = DockStyle.Fill;
            _lblAffineFormula.Font = new Font("Consolas", 8F);
            _lblAffineFormula.Location = new Point(15, 98);
            _lblAffineFormula.Name = "_lblAffineFormula";
            _lblAffineFormula.Size = new Size(636, 20);
            _lblAffineFormula.TabIndex = 4;
            _lblAffineFormula.Text = "x' = A·x + B·y + E      y' = C·x + D·y + F";
            _lblAffineFormula.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _tblAffineGrid
            // 
            _tblAffineGrid.ColumnCount = 4;
            _tblAffineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22F));
            _tblAffineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _tblAffineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22F));
            _tblAffineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _tblAffineGrid.Controls.Add(_lblA, 0, 0);
            _tblAffineGrid.Controls.Add(_nudA, 1, 0);
            _tblAffineGrid.Controls.Add(_lblB, 2, 0);
            _tblAffineGrid.Controls.Add(_nudB, 3, 0);
            _tblAffineGrid.Controls.Add(_lblC, 0, 1);
            _tblAffineGrid.Controls.Add(_nudC, 1, 1);
            _tblAffineGrid.Controls.Add(_lblD, 2, 1);
            _tblAffineGrid.Controls.Add(_nudD, 3, 1);
            _tblAffineGrid.Controls.Add(_lblE, 0, 2);
            _tblAffineGrid.Controls.Add(_nudE, 1, 2);
            _tblAffineGrid.Controls.Add(_lblF, 2, 2);
            _tblAffineGrid.Controls.Add(_nudF, 3, 2);
            _tblAffineGrid.Dock = DockStyle.Top;
            _tblAffineGrid.Location = new Point(15, 121);
            _tblAffineGrid.Name = "_tblAffineGrid";
            _tblAffineGrid.RowCount = 3;
            _tblAffineGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _tblAffineGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _tblAffineGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _tblAffineGrid.Size = new Size(636, 110);
            _tblAffineGrid.TabIndex = 5;
            // 
            // _lblA
            // 
            _lblA.Dock = DockStyle.Fill;
            _lblA.Location = new Point(3, 0);
            _lblA.Name = "_lblA";
            _lblA.Size = new Size(16, 34);
            _lblA.TabIndex = 0;
            _lblA.Text = "A";
            _lblA.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudA
            // 
            _nudA.DecimalPlaces = 6;
            _nudA.Dock = DockStyle.Fill;
            _nudA.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            _nudA.Location = new Point(25, 3);
            _nudA.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            _nudA.Minimum = new decimal(new int[] { 1000, 0, 0, int.MinValue });
            _nudA.Name = "_nudA";
            _nudA.Size = new Size(290, 23);
            _nudA.TabIndex = 6;
            // 
            // _lblB
            // 
            _lblB.Dock = DockStyle.Fill;
            _lblB.Location = new Point(321, 0);
            _lblB.Name = "_lblB";
            _lblB.Size = new Size(16, 34);
            _lblB.TabIndex = 1;
            _lblB.Text = "B";
            _lblB.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudB
            // 
            _nudB.DecimalPlaces = 6;
            _nudB.Dock = DockStyle.Fill;
            _nudB.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            _nudB.Location = new Point(343, 3);
            _nudB.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            _nudB.Minimum = new decimal(new int[] { 1000, 0, 0, int.MinValue });
            _nudB.Name = "_nudB";
            _nudB.Size = new Size(290, 23);
            _nudB.TabIndex = 7;
            // 
            // _lblC
            // 
            _lblC.Dock = DockStyle.Fill;
            _lblC.Location = new Point(3, 34);
            _lblC.Name = "_lblC";
            _lblC.Size = new Size(16, 34);
            _lblC.TabIndex = 2;
            _lblC.Text = "C";
            _lblC.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudC
            // 
            _nudC.DecimalPlaces = 6;
            _nudC.Dock = DockStyle.Fill;
            _nudC.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            _nudC.Location = new Point(25, 37);
            _nudC.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            _nudC.Minimum = new decimal(new int[] { 1000, 0, 0, int.MinValue });
            _nudC.Name = "_nudC";
            _nudC.Size = new Size(290, 23);
            _nudC.TabIndex = 8;
            // 
            // _lblD
            // 
            _lblD.Dock = DockStyle.Fill;
            _lblD.Location = new Point(321, 34);
            _lblD.Name = "_lblD";
            _lblD.Size = new Size(16, 34);
            _lblD.TabIndex = 3;
            _lblD.Text = "D";
            _lblD.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudD
            // 
            _nudD.DecimalPlaces = 6;
            _nudD.Dock = DockStyle.Fill;
            _nudD.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            _nudD.Location = new Point(343, 37);
            _nudD.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            _nudD.Minimum = new decimal(new int[] { 1000, 0, 0, int.MinValue });
            _nudD.Name = "_nudD";
            _nudD.Size = new Size(290, 23);
            _nudD.TabIndex = 9;
            // 
            // _lblE
            // 
            _lblE.Dock = DockStyle.Fill;
            _lblE.Location = new Point(3, 68);
            _lblE.Name = "_lblE";
            _lblE.Size = new Size(16, 42);
            _lblE.TabIndex = 4;
            _lblE.Text = "E";
            _lblE.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudE
            // 
            _nudE.DecimalPlaces = 6;
            _nudE.Dock = DockStyle.Fill;
            _nudE.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            _nudE.Location = new Point(25, 71);
            _nudE.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            _nudE.Minimum = new decimal(new int[] { 1000, 0, 0, int.MinValue });
            _nudE.Name = "_nudE";
            _nudE.Size = new Size(290, 23);
            _nudE.TabIndex = 10;
            // 
            // _lblF
            // 
            _lblF.Dock = DockStyle.Fill;
            _lblF.Location = new Point(321, 68);
            _lblF.Name = "_lblF";
            _lblF.Size = new Size(16, 42);
            _lblF.TabIndex = 5;
            _lblF.Text = "F";
            _lblF.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudF
            // 
            _nudF.DecimalPlaces = 6;
            _nudF.Dock = DockStyle.Fill;
            _nudF.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            _nudF.Location = new Point(343, 71);
            _nudF.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            _nudF.Minimum = new decimal(new int[] { 1000, 0, 0, int.MinValue });
            _nudF.Name = "_nudF";
            _nudF.Size = new Size(290, 23);
            _nudF.TabIndex = 11;
            // 
            // _editorHeader
            // 
            _editorHeader.Controls.Add(_lblEditorTitle);
            _editorHeader.Dock = DockStyle.Top;
            _editorHeader.Location = new Point(0, 0);
            _editorHeader.Name = "_editorHeader";
            _editorHeader.Padding = new Padding(12, 0, 12, 0);
            _editorHeader.Size = new Size(666, 36);
            _editorHeader.TabIndex = 1;
            // 
            // _lblEditorTitle
            // 
            _lblEditorTitle.Dock = DockStyle.Fill;
            _lblEditorTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _lblEditorTitle.Location = new Point(12, 0);
            _lblEditorTitle.Name = "_lblEditorTitle";
            _lblEditorTitle.Size = new Size(642, 36);
            _lblEditorTitle.TabIndex = 0;
            _lblEditorTitle.Text = "Редактор преобразования";
            _lblEditorTitle.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _footerPanel
            // 
            _footerPanel.Controls.Add(_btnOk);
            _footerPanel.Controls.Add(_btnCancel);
            _footerPanel.Controls.Add(_btnNormalize);
            _footerPanel.Controls.Add(_btnUndo);
            _footerPanel.Dock = DockStyle.Bottom;
            _footerPanel.Location = new Point(0, 460);
            _footerPanel.Name = "_footerPanel";
            _footerPanel.Padding = new Padding(8);
            _footerPanel.Size = new Size(920, 52);
            _footerPanel.TabIndex = 1;
            // 
            // _btnOk
            // 
            _btnOk.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnOk.Location = new Point(800, 11);
            _btnOk.Name = "_btnOk";
            _btnOk.Size = new Size(108, 30);
            _btnOk.TabIndex = 3;
            _btnOk.Text = "Применить";
            _btnOk.UseVisualStyleBackColor = true;
            // 
            // _btnCancel
            // 
            _btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnCancel.DialogResult = DialogResult.Cancel;
            _btnCancel.Location = new Point(686, 11);
            _btnCancel.Name = "_btnCancel";
            _btnCancel.Size = new Size(108, 30);
            _btnCancel.TabIndex = 2;
            _btnCancel.Text = "Отмена";
            _btnCancel.UseVisualStyleBackColor = true;
            // 
            // _btnNormalize
            // 
            _btnNormalize.Location = new Point(124, 11);
            _btnNormalize.Name = "_btnNormalize";
            _btnNormalize.Size = new Size(170, 30);
            _btnNormalize.TabIndex = 1;
            _btnNormalize.Text = "Норм. вероятности";
            _btnNormalize.UseVisualStyleBackColor = true;
            // 
            // _btnUndo
            // 
            _btnUndo.Location = new Point(12, 11);
            _btnUndo.Name = "_btnUndo";
            _btnUndo.Size = new Size(106, 30);
            _btnUndo.TabIndex = 0;
            _btnUndo.Text = "↶ Отменить";
            _btnUndo.UseVisualStyleBackColor = true;
            // 
            // IfsTransformEditorForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(920, 512);
            Controls.Add(_split);
            Controls.Add(_footerPanel);
            MinimumSize = new Size(880, 500);
            Name = "IfsTransformEditorForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Редактор аффинных преобразований IFS";
            _split.Panel1.ResumeLayout(false);
            _split.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_split).EndInit();
            _split.ResumeLayout(false);
            _leftPanel.ResumeLayout(false);
            _listFooter.ResumeLayout(false);
            _listHeader.ResumeLayout(false);
            _rightPanel.ResumeLayout(false);
            _tblMain.ResumeLayout(false);
            _tblMain.PerformLayout();
            _tblProbabilityRow.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_trkProbability).EndInit();
            _tblAffineGrid.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_nudA).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudB).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudC).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudD).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudE).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudF).EndInit();
            _editorHeader.ResumeLayout(false);
            _footerPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
    }
}
