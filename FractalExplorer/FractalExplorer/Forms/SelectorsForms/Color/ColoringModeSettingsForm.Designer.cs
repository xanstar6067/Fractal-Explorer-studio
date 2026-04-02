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
            var root = new TableLayoutPanel();
            var modeGroup = new GroupBox();
            var buttonsRow = new FlowLayoutPanel();

            _pnlModeChips = new FlowLayoutPanel();
            _tabs = new TabControl();
            _tabParams = new TabPage();
            _tabPalette = new TabPage();
            _tabInterior = new TabPage();
            _modeParamsPanel = new Panel();
            _cbPalette = new ComboBox();
            _cbInteriorMode = new ComboBox();
            _btnPickInteriorColor = new Button();
            _pnlInteriorColorPreview = new Panel();
            _btnApply = new Button();
            _btnCancel = new Button();

            SuspendLayout();

            Text = "Настройки окраски";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = true;
            ClientSize = new Size(600, 440);
            MinimumSize = Size;
            MaximumSize = Size;

            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.Padding = new Padding(12);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            modeGroup.Text = "Режим окраски";
            modeGroup.Dock = DockStyle.Fill;
            modeGroup.AutoSize = true;
            modeGroup.Padding = new Padding(6, 4, 6, 6);
            modeGroup.Margin = new Padding(0, 0, 0, 8);

            _pnlModeChips.Dock = DockStyle.Fill;
            _pnlModeChips.FlowDirection = FlowDirection.LeftToRight;
            _pnlModeChips.WrapContents = true;
            _pnlModeChips.AutoSize = true;
            _pnlModeChips.Padding = new Padding(0, 2, 0, 0);
            modeGroup.Controls.Add(_pnlModeChips);

            _tabs.Dock = DockStyle.Fill;
            _tabs.Padding = new Point(10, 4);
            _tabs.Margin = new Padding(0);

            _tabParams.Text = "Параметры";
            _tabPalette.Text = "Палитра";
            _tabInterior.Text = "Внутренность";

            _tabs.TabPages.Add(_tabParams);
            _tabs.TabPages.Add(_tabPalette);
            _tabs.TabPages.Add(_tabInterior);

            _modeParamsPanel.Dock = DockStyle.Fill;
            _modeParamsPanel.AutoScroll = true;
            _modeParamsPanel.Padding = new Padding(8);
            _tabParams.Controls.Add(_modeParamsPanel);
            _tabParams.Padding = new Padding(4);

            _tabPalette.Padding = new Padding(4);
            _tabPalette.AutoScroll = true;

            _tabInterior.Padding = new Padding(4);
            _tabInterior.AutoScroll = true;

            buttonsRow.FlowDirection = FlowDirection.RightToLeft;
            buttonsRow.Dock = DockStyle.Fill;
            buttonsRow.AutoSize = true;
            buttonsRow.Margin = new Padding(0, 8, 0, 0);

            _btnApply.Text = "Применить";
            _btnApply.AutoSize = true;
            _btnApply.MinimumSize = new Size(90, 28);
            _btnApply.Click += BtnApply_Click;

            _btnCancel.Text = "Отмена";
            _btnCancel.AutoSize = true;
            _btnCancel.MinimumSize = new Size(80, 28);
            _btnCancel.Click += (_, _) => Close();

            buttonsRow.Controls.Add(_btnApply);
            buttonsRow.Controls.Add(_btnCancel);

            root.Controls.Add(modeGroup, 0, 0);
            root.Controls.Add(_tabs, 0, 1);
            root.Controls.Add(buttonsRow, 0, 2);

            Controls.Add(root);

            ResumeLayout(false);
        }
    }
}
