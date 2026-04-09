namespace FractalExplorer.Forms.Fractals
{
    partial class FractalIFSForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            contentPanel = new Panel();
            canvasHost = new Panel();
            controlsHost = new Panel();
            panelControls = new Panel();
            pbRenderProgress = new ProgressBar();
            btnRender = new Button();
            btnResetView = new Button();
            btnSaveLoad = new Button();
            btnEditTransforms = new Button();
            btnBackgroundColor = new Button();
            btnFractalColor = new Button();
            nudScale = new NumericUpDown();
            label7 = new Label();
            nudCenterY = new NumericUpDown();
            label6 = new Label();
            nudCenterX = new NumericUpDown();
            label5 = new Label();
            nudIterations = new NumericUpDown();
            label3 = new Label();
            label1 = new Label();
            btnToggleControls = new Button();
            canvas = new PictureBox();
            contentPanel.SuspendLayout();
            canvasHost.SuspendLayout();
            controlsHost.SuspendLayout();
            panelControls.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudScale).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudCenterY).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudCenterX).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudIterations).BeginInit();
            ((System.ComponentModel.ISupportInitialize)canvas).BeginInit();
            SuspendLayout();
            // 
            // contentPanel
            // 
            contentPanel.Controls.Add(canvasHost);
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.Location = new Point(0, 0);
            contentPanel.Margin = new Padding(3, 2, 3, 2);
            contentPanel.Name = "contentPanel";
            contentPanel.Size = new Size(1180, 571);
            contentPanel.TabIndex = 0;
            // 
            // canvasHost
            // 
            canvasHost.Controls.Add(controlsHost);
            canvasHost.Controls.Add(btnToggleControls);
            canvasHost.Controls.Add(canvas);
            canvasHost.Dock = DockStyle.Fill;
            canvasHost.Location = new Point(0, 0);
            canvasHost.Margin = new Padding(3, 2, 3, 2);
            canvasHost.Name = "canvasHost";
            canvasHost.Size = new Size(1180, 571);
            canvasHost.TabIndex = 0;
            // 
            // controlsHost
            // 
            controlsHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            controlsHost.BackColor = SystemColors.Control;
            controlsHost.BorderStyle = BorderStyle.FixedSingle;
            controlsHost.Controls.Add(panelControls);
            controlsHost.Location = new Point(0, 0);
            controlsHost.Margin = new Padding(3, 2, 3, 2);
            controlsHost.Name = "controlsHost";
            controlsHost.Size = new Size(263, 571);
            controlsHost.TabIndex = 0;
            // 
            // panelControls
            // 
            panelControls.Controls.Add(pbRenderProgress);
            panelControls.Controls.Add(btnRender);
            panelControls.Controls.Add(btnResetView);
            panelControls.Controls.Add(btnSaveLoad);
            panelControls.Controls.Add(btnEditTransforms);
            panelControls.Controls.Add(btnBackgroundColor);
            panelControls.Controls.Add(btnFractalColor);
            panelControls.Controls.Add(nudScale);
            panelControls.Controls.Add(label7);
            panelControls.Controls.Add(nudCenterY);
            panelControls.Controls.Add(label6);
            panelControls.Controls.Add(nudCenterX);
            panelControls.Controls.Add(label5);
            panelControls.Controls.Add(nudIterations);
            panelControls.Controls.Add(label3);
            panelControls.Controls.Add(label1);
            panelControls.Dock = DockStyle.Fill;
            panelControls.Location = new Point(0, 0);
            panelControls.Margin = new Padding(3, 2, 3, 2);
            panelControls.Name = "panelControls";
            panelControls.Padding = new Padding(10, 9, 10, 9);
            panelControls.Size = new Size(261, 569);
            panelControls.TabIndex = 0;
            // 
            // pbRenderProgress
            // 
            pbRenderProgress.Location = new Point(13, 458);
            pbRenderProgress.Margin = new Padding(3, 2, 3, 2);
            pbRenderProgress.Name = "pbRenderProgress";
            pbRenderProgress.Size = new Size(235, 11);
            pbRenderProgress.TabIndex = 17;
            // 
            // btnRender
            // 
            btnRender.Location = new Point(13, 428);
            btnRender.Margin = new Padding(3, 2, 3, 2);
            btnRender.Name = "btnRender";
            btnRender.Size = new Size(235, 26);
            btnRender.TabIndex = 16;
            btnRender.Text = "Рендер";
            btnRender.UseVisualStyleBackColor = true;
            // 
            // btnResetView
            // 
            btnResetView.Location = new Point(13, 398);
            btnResetView.Margin = new Padding(3, 2, 3, 2);
            btnResetView.Name = "btnResetView";
            btnResetView.Size = new Size(235, 26);
            btnResetView.TabIndex = 15;
            btnResetView.Text = "Сброс вида";
            btnResetView.UseVisualStyleBackColor = true;
            // 
            // btnSaveLoad
            // 
            btnSaveLoad.Location = new Point(13, 368);
            btnSaveLoad.Margin = new Padding(3, 2, 3, 2);
            btnSaveLoad.Name = "btnSaveLoad";
            btnSaveLoad.Size = new Size(235, 26);
            btnSaveLoad.TabIndex = 14;
            btnSaveLoad.Text = "Сохранить / Загрузить";
            btnSaveLoad.UseVisualStyleBackColor = true;
            // 
            // btnEditTransforms
            // 
            btnEditTransforms.Location = new Point(13, 338);
            btnEditTransforms.Margin = new Padding(3, 2, 3, 2);
            btnEditTransforms.Name = "btnEditTransforms";
            btnEditTransforms.Size = new Size(235, 26);
            btnEditTransforms.TabIndex = 13;
            btnEditTransforms.Text = "Аффинные преобразования";
            btnEditTransforms.UseVisualStyleBackColor = true;
            // 
            // btnBackgroundColor
            // 
            btnBackgroundColor.Location = new Point(13, 308);
            btnBackgroundColor.Margin = new Padding(3, 2, 3, 2);
            btnBackgroundColor.Name = "btnBackgroundColor";
            btnBackgroundColor.Size = new Size(235, 26);
            btnBackgroundColor.TabIndex = 12;
            btnBackgroundColor.Text = "Цвет фона";
            btnBackgroundColor.UseVisualStyleBackColor = true;
            btnBackgroundColor.Click += btnBackgroundColor_Click;
            // 
            // btnFractalColor
            // 
            btnFractalColor.Location = new Point(13, 278);
            btnFractalColor.Margin = new Padding(3, 2, 3, 2);
            btnFractalColor.Name = "btnFractalColor";
            btnFractalColor.Size = new Size(235, 26);
            btnFractalColor.TabIndex = 11;
            btnFractalColor.Text = "Цвет фрактала";
            btnFractalColor.UseVisualStyleBackColor = true;
            btnFractalColor.Click += btnFractalColor_Click;
            // 
            // nudScale
            // 
            nudScale.DecimalPlaces = 4;
            nudScale.Increment = new decimal(new int[] { 1, 0, 0, 262144 });
            nudScale.Location = new Point(13, 186);
            nudScale.Margin = new Padding(3, 2, 3, 2);
            nudScale.Maximum = new decimal(new int[] { 40, 0, 0, 0 });
            nudScale.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            nudScale.Name = "nudScale";
            nudScale.Size = new Size(235, 23);
            nudScale.TabIndex = 10;
            nudScale.Value = new decimal(new int[] { 24, 0, 0, 65536 });
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(13, 169);
            label7.Name = "label7";
            label7.Size = new Size(59, 15);
            label7.TabIndex = 9;
            label7.Text = "Масштаб";
            // 
            // nudCenterY
            // 
            nudCenterY.DecimalPlaces = 6;
            nudCenterY.Increment = new decimal(new int[] { 1, 0, 0, 262144 });
            nudCenterY.Location = new Point(13, 141);
            nudCenterY.Margin = new Padding(3, 2, 3, 2);
            nudCenterY.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            nudCenterY.Minimum = new decimal(new int[] { 1000, 0, 0, int.MinValue });
            nudCenterY.Name = "nudCenterY";
            nudCenterY.Size = new Size(235, 23);
            nudCenterY.TabIndex = 8;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(13, 124);
            label6.Name = "label6";
            label6.Size = new Size(85, 15);
            label6.TabIndex = 7;
            label6.Text = "Центр Y (мир)";
            // 
            // nudCenterX
            // 
            nudCenterX.DecimalPlaces = 6;
            nudCenterX.Increment = new decimal(new int[] { 1, 0, 0, 262144 });
            nudCenterX.Location = new Point(13, 96);
            nudCenterX.Margin = new Padding(3, 2, 3, 2);
            nudCenterX.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            nudCenterX.Minimum = new decimal(new int[] { 1000, 0, 0, int.MinValue });
            nudCenterX.Name = "nudCenterX";
            nudCenterX.Size = new Size(235, 23);
            nudCenterX.TabIndex = 6;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(13, 79);
            label5.Name = "label5";
            label5.Size = new Size(85, 15);
            label5.TabIndex = 5;
            label5.Text = "Центр X (мир)";
            // 
            // nudIterations
            // 
            nudIterations.Increment = new decimal(new int[] { 10000, 0, 0, 0 });
            nudIterations.Location = new Point(13, 51);
            nudIterations.Margin = new Padding(3, 2, 3, 2);
            nudIterations.Maximum = new decimal(new int[] { 10000000, 0, 0, 0 });
            nudIterations.Minimum = new decimal(new int[] { 10000, 0, 0, 0 });
            nudIterations.Name = "nudIterations";
            nudIterations.Size = new Size(235, 23);
            nudIterations.TabIndex = 4;
            nudIterations.Value = new decimal(new int[] { 220000, 0, 0, 0 });
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(13, 34);
            label3.Name = "label3";
            label3.Size = new Size(61, 15);
            label3.TabIndex = 3;
            label3.Text = "Итерации";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(13, 9);
            label1.Name = "label1";
            label1.Size = new Size(181, 15);
            label1.TabIndex = 0;
            label1.Text = "IFS Explorer (zoom / pan / affine)";
            // 
            // btnToggleControls
            // 
            btnToggleControls.AutoSize = true;
            btnToggleControls.BackColor = Color.FromArgb(235, 32, 32, 32);
            btnToggleControls.FlatStyle = FlatStyle.Popup;
            btnToggleControls.ForeColor = Color.White;
            btnToggleControls.Location = new Point(273, 9);
            btnToggleControls.Margin = new Padding(3, 2, 3, 2);
            btnToggleControls.Name = "btnToggleControls";
            btnToggleControls.Size = new Size(38, 25);
            btnToggleControls.TabIndex = 2;
            btnToggleControls.Text = "✕";
            btnToggleControls.UseVisualStyleBackColor = true;
            // 
            // canvas
            // 
            canvas.BackColor = Color.Black;
            canvas.Dock = DockStyle.Fill;
            canvas.Location = new Point(0, 0);
            canvas.Margin = new Padding(3, 2, 3, 2);
            canvas.Name = "canvas";
            canvas.Size = new Size(1180, 571);
            canvas.TabIndex = 1;
            canvas.TabStop = false;
            // 
            // FractalIFSForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1180, 571);
            Controls.Add(contentPanel);
            KeyPreview = true;
            Margin = new Padding(3, 2, 3, 2);
            Name = "FractalIFSForm";
            Text = "IFS Explorer";
            FormClosing += FractalIFSForm_FormClosing;
            Load += FractalIFSForm_Load;
            contentPanel.ResumeLayout(false);
            canvasHost.ResumeLayout(false);
            canvasHost.PerformLayout();
            controlsHost.ResumeLayout(false);
            panelControls.ResumeLayout(false);
            panelControls.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudScale).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudCenterY).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudCenterX).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudIterations).EndInit();
            ((System.ComponentModel.ISupportInitialize)canvas).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private Panel contentPanel;
        private Panel canvasHost;
        private Panel controlsHost;
        private Panel panelControls;
        private PictureBox canvas;
        private Label label1;
        private NumericUpDown nudIterations;
        private Label label3;
        private Label label5;
        private NumericUpDown nudCenterX;
        private NumericUpDown nudCenterY;
        private Label label6;
        private NumericUpDown nudScale;
        private Label label7;
        private Button btnFractalColor;
        private Button btnBackgroundColor;
        private Button btnEditTransforms;
        private Button btnSaveLoad;
        private Button btnResetView;
        private Button btnRender;
        private ProgressBar pbRenderProgress;
        private Button btnToggleControls;
    }
}
