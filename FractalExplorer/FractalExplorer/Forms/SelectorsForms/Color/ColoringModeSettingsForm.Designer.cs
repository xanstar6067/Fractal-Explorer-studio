namespace FractalExplorer.Utilities
{
    partial class ColoringModeSettingsForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer? components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            root = new TableLayoutPanel();
            modeGroup = new GroupBox();
            _pnlModeChips = new FlowLayoutPanel();
            _tabs = new TabControl();
            _tabParams = new TabPage();
            _modeParamsPanel = new Panel();
            _tabPalette = new TabPage();
            _tabInterior = new TabPage();
            buttonsRow = new FlowLayoutPanel();
            _btnApply = new Button();
            _btnCancel = new Button();
            _cbPalette = new ComboBox();
            _cbInteriorMode = new ComboBox();
            _btnPickInteriorColor = new Button();
            _pnlInteriorColorPreview = new Panel();
            root.SuspendLayout();
            modeGroup.SuspendLayout();
            _tabs.SuspendLayout();
            _tabParams.SuspendLayout();
            buttonsRow.SuspendLayout();
            SuspendLayout();
            // 
            // root
            // 
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            root.Controls.Add(modeGroup, 0, 0);
            root.Controls.Add(_tabs, 0, 1);
            root.Controls.Add(buttonsRow, 0, 2);
            root.Dock = DockStyle.Fill;
            root.Location = new Point(0, 0);
            root.Name = "root";
            root.Padding = new Padding(12);
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle());
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle());
            root.Size = new Size(776, 440);
            root.TabIndex = 0;
            // 
            // modeGroup
            // 
            modeGroup.AutoSize = true;
            modeGroup.Controls.Add(_pnlModeChips);
            modeGroup.Dock = DockStyle.Fill;
            modeGroup.Location = new Point(12, 12);
            modeGroup.Margin = new Padding(0, 0, 0, 8);
            modeGroup.Name = "modeGroup";
            modeGroup.Padding = new Padding(6, 4, 6, 6);
            modeGroup.Size = new Size(752, 28);
            modeGroup.TabIndex = 0;
            modeGroup.TabStop = false;
            modeGroup.Text = "Режим окраски";
            // 
            // _pnlModeChips
            // 
            _pnlModeChips.AutoScroll = false;
            _pnlModeChips.AutoSize = true;
            _pnlModeChips.Dock = DockStyle.Fill;
            _pnlModeChips.Location = new Point(6, 20);
            _pnlModeChips.Name = "_pnlModeChips";
            _pnlModeChips.Padding = new Padding(0, 2, 0, 0);
            _pnlModeChips.WrapContents = true;
            _pnlModeChips.Size = new Size(740, 2);
            _pnlModeChips.TabIndex = 0;
            // 
            // _tabs
            // 
            _tabs.Controls.Add(_tabParams);
            _tabs.Controls.Add(_tabPalette);
            _tabs.Controls.Add(_tabInterior);
            _tabs.Dock = DockStyle.Fill;
            _tabs.Location = new Point(12, 48);
            _tabs.Margin = new Padding(0);
            _tabs.Name = "_tabs";
            _tabs.Padding = new Point(10, 4);
            _tabs.SelectedIndex = 0;
            _tabs.Size = new Size(752, 338);
            _tabs.TabIndex = 1;
            // 
            // _tabParams
            // 
            _tabParams.Controls.Add(_modeParamsPanel);
            _tabParams.Location = new Point(4, 26);
            _tabParams.Name = "_tabParams";
            _tabParams.Padding = new Padding(4);
            _tabParams.Size = new Size(744, 308);
            _tabParams.TabIndex = 0;
            _tabParams.Text = "Параметры";
            // 
            // _modeParamsPanel
            // 
            _modeParamsPanel.AutoScroll = true;
            _modeParamsPanel.Dock = DockStyle.Fill;
            _modeParamsPanel.Location = new Point(4, 4);
            _modeParamsPanel.Name = "_modeParamsPanel";
            _modeParamsPanel.Padding = new Padding(8);
            _modeParamsPanel.Size = new Size(736, 300);
            _modeParamsPanel.TabIndex = 0;
            // 
            // _tabPalette
            // 
            _tabPalette.AutoScroll = true;
            _tabPalette.Location = new Point(4, 26);
            _tabPalette.Name = "_tabPalette";
            _tabPalette.Padding = new Padding(4);
            _tabPalette.Size = new Size(727, 308);
            _tabPalette.TabIndex = 1;
            _tabPalette.Text = "Палитра";
            // 
            // _tabInterior
            // 
            _tabInterior.AutoScroll = true;
            _tabInterior.Location = new Point(4, 26);
            _tabInterior.Name = "_tabInterior";
            _tabInterior.Padding = new Padding(4);
            _tabInterior.Size = new Size(727, 308);
            _tabInterior.TabIndex = 2;
            _tabInterior.Text = "Внутренность";
            // 
            // buttonsRow
            // 
            buttonsRow.AutoSize = true;
            buttonsRow.Controls.Add(_btnApply);
            buttonsRow.Controls.Add(_btnCancel);
            buttonsRow.Dock = DockStyle.Fill;
            buttonsRow.FlowDirection = FlowDirection.RightToLeft;
            buttonsRow.Location = new Point(12, 394);
            buttonsRow.Margin = new Padding(0, 8, 0, 0);
            buttonsRow.Name = "buttonsRow";
            buttonsRow.Size = new Size(752, 34);
            buttonsRow.TabIndex = 2;
            // 
            // _btnApply
            // 
            _btnApply.AutoSize = true;
            _btnApply.Location = new Point(659, 3);
            _btnApply.MinimumSize = new Size(90, 28);
            _btnApply.Name = "_btnApply";
            _btnApply.Size = new Size(90, 28);
            _btnApply.TabIndex = 0;
            _btnApply.Text = "Применить";
            _btnApply.Click += BtnApply_Click;
            // 
            // _btnCancel
            // 
            _btnCancel.AutoSize = true;
            _btnCancel.Location = new Point(573, 3);
            _btnCancel.MinimumSize = new Size(80, 28);
            _btnCancel.Name = "_btnCancel";
            _btnCancel.Size = new Size(80, 28);
            _btnCancel.TabIndex = 1;
            _btnCancel.Text = "Отмена";
            _btnCancel.Click += _btnCancel_Click;
            // 
            // _cbPalette
            // 
            _cbPalette.Location = new Point(0, 0);
            _cbPalette.Name = "_cbPalette";
            _cbPalette.Size = new Size(121, 23);
            _cbPalette.TabIndex = 0;
            // 
            // _cbInteriorMode
            // 
            _cbInteriorMode.Location = new Point(0, 0);
            _cbInteriorMode.Name = "_cbInteriorMode";
            _cbInteriorMode.Size = new Size(121, 23);
            _cbInteriorMode.TabIndex = 0;
            // 
            // _btnPickInteriorColor
            // 
            _btnPickInteriorColor.Location = new Point(0, 0);
            _btnPickInteriorColor.Name = "_btnPickInteriorColor";
            _btnPickInteriorColor.Size = new Size(75, 23);
            _btnPickInteriorColor.TabIndex = 0;
            // 
            // _pnlInteriorColorPreview
            // 
            _pnlInteriorColorPreview.Location = new Point(0, 0);
            _pnlInteriorColorPreview.Name = "_pnlInteriorColorPreview";
            _pnlInteriorColorPreview.Size = new Size(200, 100);
            _pnlInteriorColorPreview.TabIndex = 0;
            // 
            // ColoringModeSettingsForm
            // 
            ClientSize = new Size(776, 440);
            Controls.Add(root);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new Size(616, 479);
            Name = "ColoringModeSettingsForm";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Настройки окраски";
            root.ResumeLayout(false);
            root.PerformLayout();
            modeGroup.ResumeLayout(false);
            modeGroup.PerformLayout();
            _tabs.ResumeLayout(false);
            _tabParams.ResumeLayout(false);
            buttonsRow.ResumeLayout(false);
            buttonsRow.PerformLayout();
            ResumeLayout(false);
        }

        private TableLayoutPanel root;
        private GroupBox modeGroup;
        private FlowLayoutPanel buttonsRow;
    }
}
