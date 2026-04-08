namespace FractalExplorer.Forms.Fractals
{
    partial class FractalJuliaGridForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            _canvasBitmap?.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            splitContainerMain = new SplitContainer();
            panelControls = new TableLayoutPanel();
            lblReRange = new Label();
            nudReMin = new NumericUpDown();
            nudReMax = new NumericUpDown();
            lblImRange = new Label();
            nudImMin = new NumericUpDown();
            nudImMax = new NumericUpDown();
            lblGridSize = new Label();
            nudCols = new NumericUpDown();
            nudRows = new NumericUpDown();
            lblTileSize = new Label();
            nudTileSize = new NumericUpDown();
            chkOpenOnClick = new CheckBox();
            btnRender = new Button();
            btnExportCanvas = new Button();
            progressBar = new ProgressBar();
            lblStatus = new Label();
            pictureBoxGrid = new PictureBox();
            ((System.ComponentModel.ISupportInitialize)splitContainerMain).BeginInit();
            splitContainerMain.Panel1.SuspendLayout();
            splitContainerMain.Panel2.SuspendLayout();
            splitContainerMain.SuspendLayout();
            panelControls.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudReMin).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudReMax).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudImMin).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudImMax).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudCols).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudRows).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudTileSize).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBoxGrid).BeginInit();
            SuspendLayout();
            // 
            // splitContainerMain
            // 
            splitContainerMain.Dock = DockStyle.Fill;
            splitContainerMain.FixedPanel = FixedPanel.Panel1;
            splitContainerMain.Location = new Point(0, 0);
            splitContainerMain.Name = "splitContainerMain";
            // 
            // splitContainerMain.Panel1
            // 
            splitContainerMain.Panel1.Controls.Add(panelControls);
            // 
            // splitContainerMain.Panel2
            // 
            splitContainerMain.Panel2.Controls.Add(pictureBoxGrid);
            splitContainerMain.Size = new Size(1400, 900);
            splitContainerMain.SplitterDistance = 330;
            splitContainerMain.TabIndex = 0;
            // 
            // panelControls
            // 
            panelControls.ColumnCount = 2;
            panelControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            panelControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            panelControls.Controls.Add(lblReRange, 0, 0);
            panelControls.Controls.Add(nudReMin, 0, 1);
            panelControls.Controls.Add(nudReMax, 1, 1);
            panelControls.Controls.Add(lblImRange, 0, 2);
            panelControls.Controls.Add(nudImMin, 0, 3);
            panelControls.Controls.Add(nudImMax, 1, 3);
            panelControls.Controls.Add(lblGridSize, 0, 4);
            panelControls.Controls.Add(nudCols, 0, 5);
            panelControls.Controls.Add(nudRows, 1, 5);
            panelControls.Controls.Add(lblTileSize, 0, 6);
            panelControls.Controls.Add(nudTileSize, 0, 7);
            panelControls.Controls.Add(chkOpenOnClick, 0, 8);
            panelControls.Controls.Add(btnRender, 0, 9);
            panelControls.Controls.Add(btnExportCanvas, 1, 9);
            panelControls.Controls.Add(progressBar, 0, 10);
            panelControls.Controls.Add(lblStatus, 0, 11);
            panelControls.Dock = DockStyle.Fill;
            panelControls.Location = new Point(0, 0);
            panelControls.Name = "panelControls";
            panelControls.Padding = new Padding(10);
            panelControls.RowCount = 12;
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panelControls.Size = new Size(330, 900);
            panelControls.TabIndex = 0;
            // 
            // lblReRange
            // 
            lblReRange.AutoSize = true;
            panelControls.SetColumnSpan(lblReRange, 2);
            lblReRange.Dock = DockStyle.Fill;
            lblReRange.Location = new Point(13, 10);
            lblReRange.Name = "lblReRange";
            lblReRange.Size = new Size(304, 26);
            lblReRange.TabIndex = 0;
            lblReRange.Text = "Диапазон Re(C) [min / max]";
            lblReRange.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // nudReMin
            // 
            nudReMin.DecimalPlaces = 4;
            nudReMin.Dock = DockStyle.Fill;
            nudReMin.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            nudReMin.Location = new Point(13, 39);
            nudReMin.Maximum = new decimal(new int[] { 2, 0, 0, 0 });
            nudReMin.Minimum = new decimal(new int[] { 2, 0, 0, int.MinValue });
            nudReMin.Name = "nudReMin";
            nudReMin.Size = new Size(149, 23);
            nudReMin.TabIndex = 1;
            nudReMin.Value = new decimal(new int[] { 2, 0, 0, int.MinValue });
            // 
            // nudReMax
            // 
            nudReMax.DecimalPlaces = 4;
            nudReMax.Dock = DockStyle.Fill;
            nudReMax.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            nudReMax.Location = new Point(168, 39);
            nudReMax.Maximum = new decimal(new int[] { 2, 0, 0, 0 });
            nudReMax.Minimum = new decimal(new int[] { 2, 0, 0, int.MinValue });
            nudReMax.Name = "nudReMax";
            nudReMax.Size = new Size(149, 23);
            nudReMax.TabIndex = 2;
            nudReMax.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // lblImRange
            // 
            lblImRange.AutoSize = true;
            panelControls.SetColumnSpan(lblImRange, 2);
            lblImRange.Dock = DockStyle.Fill;
            lblImRange.Location = new Point(13, 70);
            lblImRange.Name = "lblImRange";
            lblImRange.Size = new Size(304, 26);
            lblImRange.TabIndex = 3;
            lblImRange.Text = "Диапазон Im(C) [min / max]";
            lblImRange.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // nudImMin
            // 
            nudImMin.DecimalPlaces = 4;
            nudImMin.Dock = DockStyle.Fill;
            nudImMin.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            nudImMin.Location = new Point(13, 99);
            nudImMin.Maximum = new decimal(new int[] { 2, 0, 0, 0 });
            nudImMin.Minimum = new decimal(new int[] { 2, 0, 0, int.MinValue });
            nudImMin.Name = "nudImMin";
            nudImMin.Size = new Size(149, 23);
            nudImMin.TabIndex = 4;
            nudImMin.Value = new decimal(new int[] { 12, 0, 0, int.MinValue });
            // 
            // nudImMax
            // 
            nudImMax.DecimalPlaces = 4;
            nudImMax.Dock = DockStyle.Fill;
            nudImMax.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            nudImMax.Location = new Point(168, 99);
            nudImMax.Maximum = new decimal(new int[] { 2, 0, 0, 0 });
            nudImMax.Minimum = new decimal(new int[] { 2, 0, 0, int.MinValue });
            nudImMax.Name = "nudImMax";
            nudImMax.Size = new Size(149, 23);
            nudImMax.TabIndex = 5;
            nudImMax.Value = new decimal(new int[] { 12, 0, 0, 65536 });
            // 
            // lblGridSize
            // 
            lblGridSize.AutoSize = true;
            panelControls.SetColumnSpan(lblGridSize, 2);
            lblGridSize.Dock = DockStyle.Fill;
            lblGridSize.Location = new Point(13, 130);
            lblGridSize.Name = "lblGridSize";
            lblGridSize.Size = new Size(304, 26);
            lblGridSize.TabIndex = 6;
            lblGridSize.Text = "Сетка [колонки / строки]";
            lblGridSize.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // nudCols
            // 
            nudCols.Dock = DockStyle.Fill;
            nudCols.Location = new Point(13, 159);
            nudCols.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            nudCols.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
            nudCols.Name = "nudCols";
            nudCols.Size = new Size(149, 23);
            nudCols.TabIndex = 7;
            nudCols.Value = new decimal(new int[] { 5, 0, 0, 0 });
            // 
            // nudRows
            // 
            nudRows.Dock = DockStyle.Fill;
            nudRows.Location = new Point(168, 159);
            nudRows.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            nudRows.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
            nudRows.Name = "nudRows";
            nudRows.Size = new Size(149, 23);
            nudRows.TabIndex = 8;
            nudRows.Value = new decimal(new int[] { 4, 0, 0, 0 });
            // 
            // lblTileSize
            // 
            lblTileSize.AutoSize = true;
            panelControls.SetColumnSpan(lblTileSize, 2);
            lblTileSize.Dock = DockStyle.Fill;
            lblTileSize.Location = new Point(13, 190);
            lblTileSize.Name = "lblTileSize";
            lblTileSize.Size = new Size(304, 26);
            lblTileSize.TabIndex = 9;
            lblTileSize.Text = "Размер миниатюры (px)";
            lblTileSize.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // nudTileSize
            // 
            panelControls.SetColumnSpan(nudTileSize, 2);
            nudTileSize.Dock = DockStyle.Fill;
            nudTileSize.Location = new Point(13, 219);
            nudTileSize.Maximum = new decimal(new int[] { 512, 0, 0, 0 });
            nudTileSize.Minimum = new decimal(new int[] { 64, 0, 0, 0 });
            nudTileSize.Name = "nudTileSize";
            nudTileSize.Size = new Size(304, 23);
            nudTileSize.TabIndex = 10;
            nudTileSize.Value = new decimal(new int[] { 180, 0, 0, 0 });
            // 
            // chkOpenOnClick
            // 
            chkOpenOnClick.AutoSize = true;
            panelControls.SetColumnSpan(chkOpenOnClick, 2);
            chkOpenOnClick.Dock = DockStyle.Fill;
            chkOpenOnClick.Location = new Point(13, 253);
            chkOpenOnClick.Name = "chkOpenOnClick";
            chkOpenOnClick.Size = new Size(304, 28);
            chkOpenOnClick.TabIndex = 11;
            chkOpenOnClick.Text = "Открывать FractalJulia по клику на клетку";
            chkOpenOnClick.UseVisualStyleBackColor = true;
            // 
            // btnRender
            // 
            btnRender.Dock = DockStyle.Fill;
            btnRender.Location = new Point(13, 287);
            btnRender.Name = "btnRender";
            btnRender.Size = new Size(149, 34);
            btnRender.TabIndex = 12;
            btnRender.Text = "Построить сетку";
            btnRender.UseVisualStyleBackColor = true;
            btnRender.Click += btnRender_Click;
            // 
            // btnExportCanvas
            // 
            btnExportCanvas.Dock = DockStyle.Fill;
            btnExportCanvas.Location = new Point(168, 287);
            btnExportCanvas.Name = "btnExportCanvas";
            btnExportCanvas.Size = new Size(149, 34);
            btnExportCanvas.TabIndex = 13;
            btnExportCanvas.Text = "Экспорт полотна";
            btnExportCanvas.UseVisualStyleBackColor = true;
            btnExportCanvas.Click += btnExportCanvas_Click;
            // 
            // progressBar
            // 
            panelControls.SetColumnSpan(progressBar, 2);
            progressBar.Dock = DockStyle.Fill;
            progressBar.Location = new Point(13, 327);
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(304, 24);
            progressBar.TabIndex = 14;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            panelControls.SetColumnSpan(lblStatus, 2);
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.Location = new Point(13, 354);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(304, 536);
            lblStatus.TabIndex = 15;
            lblStatus.Text = "Готово к рендеру.";
            // 
            // pictureBoxGrid
            // 
            pictureBoxGrid.BackColor = Color.Black;
            pictureBoxGrid.Cursor = Cursors.Cross;
            pictureBoxGrid.Dock = DockStyle.Fill;
            pictureBoxGrid.Location = new Point(0, 0);
            pictureBoxGrid.Name = "pictureBoxGrid";
            pictureBoxGrid.Size = new Size(1066, 900);
            pictureBoxGrid.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBoxGrid.TabIndex = 0;
            pictureBoxGrid.TabStop = false;
            pictureBoxGrid.MouseClick += pictureBoxGrid_MouseClick;
            // 
            // FractalJuliaGridForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1400, 900);
            Controls.Add(splitContainerMain);
            Name = "FractalJuliaGridForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Исследователь сетки Жюлиа";
            splitContainerMain.Panel1.ResumeLayout(false);
            splitContainerMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainerMain).EndInit();
            splitContainerMain.ResumeLayout(false);
            panelControls.ResumeLayout(false);
            panelControls.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudReMin).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudReMax).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudImMin).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudImMax).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudCols).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudRows).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudTileSize).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBoxGrid).EndInit();
            ResumeLayout(false);
        }

        private SplitContainer splitContainerMain;
        private TableLayoutPanel panelControls;
        private Label lblReRange;
        private NumericUpDown nudReMin;
        private NumericUpDown nudReMax;
        private Label lblImRange;
        private NumericUpDown nudImMin;
        private NumericUpDown nudImMax;
        private Label lblGridSize;
        private NumericUpDown nudCols;
        private NumericUpDown nudRows;
        private Label lblTileSize;
        private NumericUpDown nudTileSize;
        private CheckBox chkOpenOnClick;
        private Button btnRender;
        private Button btnExportCanvas;
        private ProgressBar progressBar;
        private Label lblStatus;
        private PictureBox pictureBoxGrid;
    }
}
