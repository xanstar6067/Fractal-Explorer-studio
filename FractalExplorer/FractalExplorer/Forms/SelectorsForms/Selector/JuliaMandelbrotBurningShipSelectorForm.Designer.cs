namespace FractalExplorer.Forms.SelectorsForms.Selector
{
    public partial class BurningShipCSelectorForm
    {
        private PictureBox displayPictureBox = null!;
        private Panel canvasBorderPanel = null!;
        private Panel canvasHoverBorderPanel = null!;
        private Label hintLabel = null!;
        private TableLayoutPanel mainLayout = null!;

        private void InitializeComponent()
        {
            mainLayout = new TableLayoutPanel();
            hintLabel = new Label();
            canvasBorderPanel = new Panel();
            canvasHoverBorderPanel = new Panel();
            displayPictureBox = new PictureBox();
            mainLayout.SuspendLayout();
            canvasBorderPanel.SuspendLayout();
            canvasHoverBorderPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)displayPictureBox).BeginInit();
            SuspendLayout();
            // 
            // mainLayout
            // 
            mainLayout.ColumnCount = 1;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.Controls.Add(hintLabel, 0, 0);
            mainLayout.Controls.Add(canvasBorderPanel, 0, 1);
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.Location = new Point(0, 0);
            mainLayout.Name = "mainLayout";
            mainLayout.Padding = new Padding(10);
            mainLayout.RowCount = 2;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.Size = new Size(784, 661);
            mainLayout.TabIndex = 0;
            // 
            // hintLabel
            // 
            hintLabel.AutoSize = true;
            hintLabel.Margin = new Padding(0, 0, 0, 8);
            hintLabel.Name = "hintLabel";
            hintLabel.Size = new Size(461, 15);
            hintLabel.TabIndex = 0;
            hintLabel.Text = "ЛКМ: выбор точки C, зажмите СКМ: панорамирование, колесо: масштаб";
            // 
            // canvasBorderPanel
            // 
            canvasBorderPanel.Controls.Add(canvasHoverBorderPanel);
            canvasBorderPanel.Dock = DockStyle.Fill;
            canvasBorderPanel.Margin = new Padding(0);
            canvasBorderPanel.Name = "canvasBorderPanel";
            canvasBorderPanel.Padding = new Padding(1);
            canvasBorderPanel.Size = new Size(764, 628);
            canvasBorderPanel.TabIndex = 1;
            // 
            // canvasHoverBorderPanel
            // 
            canvasHoverBorderPanel.Controls.Add(displayPictureBox);
            canvasHoverBorderPanel.Dock = DockStyle.Fill;
            canvasHoverBorderPanel.Margin = new Padding(0);
            canvasHoverBorderPanel.Name = "canvasHoverBorderPanel";
            canvasHoverBorderPanel.Padding = new Padding(1);
            canvasHoverBorderPanel.Size = new Size(762, 626);
            canvasHoverBorderPanel.TabIndex = 0;
            // 
            // displayPictureBox
            // 
            displayPictureBox.Cursor = Cursors.Cross;
            displayPictureBox.Dock = DockStyle.Fill;
            displayPictureBox.Location = new Point(1, 1);
            displayPictureBox.Name = "displayPictureBox";
            displayPictureBox.Size = new Size(760, 624);
            displayPictureBox.SizeMode = PictureBoxSizeMode.StretchImage;
            displayPictureBox.TabIndex = 0;
            displayPictureBox.TabStop = false;
            // 
            // BurningShipCSelectorForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(784, 661);
            Controls.Add(mainLayout);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Name = "BurningShipCSelectorForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Выбор точки C (Множество Горящий Корабль)";
            mainLayout.ResumeLayout(false);
            mainLayout.PerformLayout();
            canvasBorderPanel.ResumeLayout(false);
            canvasHoverBorderPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)displayPictureBox).EndInit();
            ResumeLayout(false);
        }
    }
}
