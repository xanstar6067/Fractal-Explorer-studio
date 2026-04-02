namespace FractalExplorer.Forms.SelectorsForms.Selector
{
    public partial class JuliaMandelbrotSelectorForm
    {
        private PictureBox mandelbrotDisplay = null!;
        private Panel mandelbrotCanvasBorder = null!;
        private Panel mandelbrotCanvasHoverBorder = null!;
        private Label mandelbrotHintLabel = null!;
        private TableLayoutPanel mainLayout = null!;

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(JuliaMandelbrotSelectorForm));
            mainLayout = new TableLayoutPanel();
            mandelbrotHintLabel = new Label();
            mandelbrotCanvasBorder = new Panel();
            mandelbrotCanvasHoverBorder = new Panel();
            mandelbrotDisplay = new PictureBox();
            mainLayout.SuspendLayout();
            mandelbrotCanvasBorder.SuspendLayout();
            mandelbrotCanvasHoverBorder.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)mandelbrotDisplay).BeginInit();
            SuspendLayout();
            // 
            // mainLayout
            // 
            mainLayout.ColumnCount = 1;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.Controls.Add(mandelbrotHintLabel, 0, 0);
            mainLayout.Controls.Add(mandelbrotCanvasBorder, 0, 1);
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.Location = new Point(0, 0);
            mainLayout.Name = "mainLayout";
            mainLayout.Padding = new Padding(10);
            mainLayout.RowCount = 2;
            mainLayout.RowStyles.Add(new RowStyle());
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.Size = new Size(784, 661);
            mainLayout.TabIndex = 0;
            // 
            // mandelbrotHintLabel
            // 
            mandelbrotHintLabel.AutoSize = true;
            mandelbrotHintLabel.Location = new Point(10, 10);
            mandelbrotHintLabel.Margin = new Padding(0, 0, 0, 8);
            mandelbrotHintLabel.Name = "mandelbrotHintLabel";
            mandelbrotHintLabel.Size = new Size(412, 15);
            mandelbrotHintLabel.TabIndex = 0;
            mandelbrotHintLabel.Text = "ЛКМ: выбор точки C, зажмите СКМ: панорамирование, колесо: масштаб";
            // 
            // mandelbrotCanvasBorder
            // 
            mandelbrotCanvasBorder.Controls.Add(mandelbrotCanvasHoverBorder);
            mandelbrotCanvasBorder.Dock = DockStyle.Fill;
            mandelbrotCanvasBorder.Location = new Point(10, 33);
            mandelbrotCanvasBorder.Margin = new Padding(0);
            mandelbrotCanvasBorder.Name = "mandelbrotCanvasBorder";
            mandelbrotCanvasBorder.Padding = new Padding(1);
            mandelbrotCanvasBorder.Size = new Size(764, 618);
            mandelbrotCanvasBorder.TabIndex = 1;
            // 
            // mandelbrotCanvasHoverBorder
            // 
            mandelbrotCanvasHoverBorder.Controls.Add(mandelbrotDisplay);
            mandelbrotCanvasHoverBorder.Dock = DockStyle.Fill;
            mandelbrotCanvasHoverBorder.Location = new Point(1, 1);
            mandelbrotCanvasHoverBorder.Margin = new Padding(0);
            mandelbrotCanvasHoverBorder.Name = "mandelbrotCanvasHoverBorder";
            mandelbrotCanvasHoverBorder.Padding = new Padding(1);
            mandelbrotCanvasHoverBorder.Size = new Size(762, 616);
            mandelbrotCanvasHoverBorder.TabIndex = 0;
            // 
            // mandelbrotDisplay
            // 
            mandelbrotDisplay.Cursor = Cursors.Cross;
            mandelbrotDisplay.Dock = DockStyle.Fill;
            mandelbrotDisplay.Location = new Point(1, 1);
            mandelbrotDisplay.Name = "mandelbrotDisplay";
            mandelbrotDisplay.Size = new Size(760, 614);
            mandelbrotDisplay.SizeMode = PictureBoxSizeMode.StretchImage;
            mandelbrotDisplay.TabIndex = 0;
            mandelbrotDisplay.TabStop = false;
            // 
            // JuliaMandelbrotSelectorForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(784, 661);
            Controls.Add(mainLayout);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            Name = "JuliaMandelbrotSelectorForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Выбор точки C (Множество Мандельброта)";
            mainLayout.ResumeLayout(false);
            mainLayout.PerformLayout();
            mandelbrotCanvasBorder.ResumeLayout(false);
            mandelbrotCanvasHoverBorder.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)mandelbrotDisplay).EndInit();
            ResumeLayout(false);
        }
    }
}
