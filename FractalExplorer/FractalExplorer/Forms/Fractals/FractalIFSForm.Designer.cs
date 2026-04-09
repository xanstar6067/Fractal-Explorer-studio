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
            settingsLayout = new TableLayoutPanel();
            nudIterations = new NumericUpDown();
            label3 = new Label();
            nudCenterX = new NumericUpDown();
            label5 = new Label();
            nudCenterY = new NumericUpDown();
            label6 = new Label();
            nudScale = new NumericUpDown();
            label7 = new Label();
            btnFractalColor = new Button();
            btnBackgroundColor = new Button();
            btnSaveImage = new Button();
            btnSaveLoad = new Button();
            btnEditTransforms = new Button();
            btnResetView = new Button();
            btnRender = new Button();
            pbRenderProgress = new ProgressBar();
            btnToggleControls = new Button();
            canvas = new PictureBox();
            contentPanel.SuspendLayout();
            canvasHost.SuspendLayout();
            controlsHost.SuspendLayout();
            panelControls.SuspendLayout();
            settingsLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudIterations).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudCenterX).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudCenterY).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudScale).BeginInit();
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
            contentPanel.Size = new Size(1180, 781);
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
            canvasHost.Size = new Size(1180, 781);
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
            controlsHost.Size = new Size(280, 781);
            controlsHost.TabIndex = 0;
            // 
            // panelControls
            // 
            panelControls.Controls.Add(settingsLayout);
            panelControls.Dock = DockStyle.Fill;
            panelControls.Location = new Point(0, 0);
            panelControls.Margin = new Padding(3, 2, 3, 2);
            panelControls.Name = "panelControls";
            panelControls.Padding = new Padding(8);
            panelControls.Size = new Size(278, 779);
            panelControls.TabIndex = 0;
            // 
            // settingsLayout
            // 
            settingsLayout.ColumnCount = 2;
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            settingsLayout.Controls.Add(nudIterations, 0, 0);
            settingsLayout.Controls.Add(label3, 1, 0);
            settingsLayout.Controls.Add(nudCenterX, 0, 1);
            settingsLayout.Controls.Add(label5, 1, 1);
            settingsLayout.Controls.Add(nudCenterY, 0, 2);
            settingsLayout.Controls.Add(label6, 1, 2);
            settingsLayout.Controls.Add(nudScale, 0, 3);
            settingsLayout.Controls.Add(label7, 1, 3);
            settingsLayout.Controls.Add(btnFractalColor, 0, 4);
            settingsLayout.Controls.Add(btnBackgroundColor, 0, 5);
            settingsLayout.Controls.Add(btnSaveImage, 0, 6);
            settingsLayout.Controls.Add(btnSaveLoad, 0, 7);
            settingsLayout.Controls.Add(btnEditTransforms, 0, 8);
            settingsLayout.Controls.Add(btnResetView, 0, 9);
            settingsLayout.Controls.Add(btnRender, 0, 10);
            settingsLayout.Controls.Add(pbRenderProgress, 0, 11);
            settingsLayout.Dock = DockStyle.Fill;
            settingsLayout.Location = new Point(8, 8);
            settingsLayout.Margin = new Padding(3, 2, 3, 2);
            settingsLayout.Name = "settingsLayout";
            settingsLayout.RowCount = 13;
            settingsLayout.RowStyles.Add(new RowStyle());
            settingsLayout.RowStyles.Add(new RowStyle());
            settingsLayout.RowStyles.Add(new RowStyle());
            settingsLayout.RowStyles.Add(new RowStyle());
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            settingsLayout.Size = new Size(262, 763);
            settingsLayout.TabIndex = 0;
            // 
            // nudIterations
            // 
            nudIterations.Dock = DockStyle.Fill;
            nudIterations.Increment = new decimal(new int[] { 10000, 0, 0, 0 });
            nudIterations.Location = new Point(6, 6);
            nudIterations.Margin = new Padding(6, 6, 3, 3);
            nudIterations.Maximum = new decimal(new int[] { 10000000, 0, 0, 0 });
            nudIterations.Minimum = new decimal(new int[] { 10000, 0, 0, 0 });
            nudIterations.Name = "nudIterations";
            nudIterations.Size = new Size(135, 23);
            nudIterations.TabIndex = 0;
            nudIterations.Value = new decimal(new int[] { 220000, 0, 0, 0 });
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Dock = DockStyle.Fill;
            label3.Location = new Point(147, 0);
            label3.Name = "label3";
            label3.Size = new Size(112, 32);
            label3.TabIndex = 1;
            label3.Text = "Итерации";
            label3.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // nudCenterX
            // 
            nudCenterX.DecimalPlaces = 6;
            nudCenterX.Dock = DockStyle.Fill;
            nudCenterX.Increment = new decimal(new int[] { 1, 0, 0, 262144 });
            nudCenterX.Location = new Point(6, 35);
            nudCenterX.Margin = new Padding(6, 3, 3, 3);
            nudCenterX.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            nudCenterX.Minimum = new decimal(new int[] { 1000, 0, 0, int.MinValue });
            nudCenterX.Name = "nudCenterX";
            nudCenterX.Size = new Size(135, 23);
            nudCenterX.TabIndex = 2;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Dock = DockStyle.Fill;
            label5.Location = new Point(147, 32);
            label5.Name = "label5";
            label5.Size = new Size(112, 29);
            label5.TabIndex = 3;
            label5.Text = "Центр X (мир)";
            label5.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // nudCenterY
            // 
            nudCenterY.DecimalPlaces = 6;
            nudCenterY.Dock = DockStyle.Fill;
            nudCenterY.Increment = new decimal(new int[] { 1, 0, 0, 262144 });
            nudCenterY.Location = new Point(6, 64);
            nudCenterY.Margin = new Padding(6, 3, 3, 3);
            nudCenterY.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            nudCenterY.Minimum = new decimal(new int[] { 1000, 0, 0, int.MinValue });
            nudCenterY.Name = "nudCenterY";
            nudCenterY.Size = new Size(135, 23);
            nudCenterY.TabIndex = 4;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Dock = DockStyle.Fill;
            label6.Location = new Point(147, 61);
            label6.Name = "label6";
            label6.Size = new Size(112, 29);
            label6.TabIndex = 5;
            label6.Text = "Центр Y (мир)";
            label6.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // nudScale
            // 
            nudScale.DecimalPlaces = 4;
            nudScale.Dock = DockStyle.Fill;
            nudScale.Increment = new decimal(new int[] { 1, 0, 0, 262144 });
            nudScale.Location = new Point(6, 93);
            nudScale.Margin = new Padding(6, 3, 3, 3);
            nudScale.Maximum = new decimal(new int[] { 40, 0, 0, 0 });
            nudScale.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            nudScale.Name = "nudScale";
            nudScale.Size = new Size(135, 23);
            nudScale.TabIndex = 6;
            nudScale.Value = new decimal(new int[] { 24, 0, 0, 65536 });
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Dock = DockStyle.Fill;
            label7.Location = new Point(147, 90);
            label7.Name = "label7";
            label7.Size = new Size(112, 29);
            label7.TabIndex = 7;
            label7.Text = "Масштаб";
            label7.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // btnFractalColor
            // 
            settingsLayout.SetColumnSpan(btnFractalColor, 2);
            btnFractalColor.Dock = DockStyle.Fill;
            btnFractalColor.Location = new Point(6, 122);
            btnFractalColor.Margin = new Padding(6, 3, 6, 3);
            btnFractalColor.Name = "btnFractalColor";
            btnFractalColor.Size = new Size(250, 36);
            btnFractalColor.TabIndex = 8;
            btnFractalColor.Text = "Цвет фрактала";
            btnFractalColor.UseVisualStyleBackColor = true;
            btnFractalColor.Click += btnFractalColor_Click;
            // 
            // btnBackgroundColor
            // 
            settingsLayout.SetColumnSpan(btnBackgroundColor, 2);
            btnBackgroundColor.Dock = DockStyle.Fill;
            btnBackgroundColor.Location = new Point(6, 164);
            btnBackgroundColor.Margin = new Padding(6, 3, 6, 3);
            btnBackgroundColor.Name = "btnBackgroundColor";
            btnBackgroundColor.Size = new Size(250, 36);
            btnBackgroundColor.TabIndex = 9;
            btnBackgroundColor.Text = "Цвет фона";
            btnBackgroundColor.UseVisualStyleBackColor = true;
            btnBackgroundColor.Click += btnBackgroundColor_Click;
            // 
            // btnSaveImage
            // 
            settingsLayout.SetColumnSpan(btnSaveImage, 2);
            btnSaveImage.Dock = DockStyle.Fill;
            btnSaveImage.Location = new Point(6, 206);
            btnSaveImage.Margin = new Padding(6, 3, 6, 3);
            btnSaveImage.Name = "btnSaveImage";
            btnSaveImage.Size = new Size(250, 36);
            btnSaveImage.TabIndex = 10;
            btnSaveImage.Text = "Сохранить изображение";
            btnSaveImage.UseVisualStyleBackColor = true;
            // 
            // btnSaveLoad
            // 
            settingsLayout.SetColumnSpan(btnSaveLoad, 2);
            btnSaveLoad.Dock = DockStyle.Fill;
            btnSaveLoad.Location = new Point(6, 248);
            btnSaveLoad.Margin = new Padding(6, 3, 6, 3);
            btnSaveLoad.Name = "btnSaveLoad";
            btnSaveLoad.Size = new Size(250, 36);
            btnSaveLoad.TabIndex = 11;
            btnSaveLoad.Text = "Менеджер сохранений";
            btnSaveLoad.UseVisualStyleBackColor = true;
            // 
            // btnEditTransforms
            // 
            settingsLayout.SetColumnSpan(btnEditTransforms, 2);
            btnEditTransforms.Dock = DockStyle.Fill;
            btnEditTransforms.Location = new Point(6, 290);
            btnEditTransforms.Margin = new Padding(6, 3, 6, 3);
            btnEditTransforms.Name = "btnEditTransforms";
            btnEditTransforms.Size = new Size(250, 36);
            btnEditTransforms.TabIndex = 12;
            btnEditTransforms.Text = "Трансформации";
            btnEditTransforms.UseVisualStyleBackColor = true;
            // 
            // btnResetView
            // 
            settingsLayout.SetColumnSpan(btnResetView, 2);
            btnResetView.Dock = DockStyle.Fill;
            btnResetView.Location = new Point(6, 332);
            btnResetView.Margin = new Padding(6, 3, 6, 3);
            btnResetView.Name = "btnResetView";
            btnResetView.Size = new Size(250, 36);
            btnResetView.TabIndex = 13;
            btnResetView.Text = "Сброс вида";
            btnResetView.UseVisualStyleBackColor = true;
            // 
            // btnRender
            // 
            settingsLayout.SetColumnSpan(btnRender, 2);
            btnRender.Dock = DockStyle.Fill;
            btnRender.Location = new Point(6, 374);
            btnRender.Margin = new Padding(6, 3, 6, 3);
            btnRender.Name = "btnRender";
            btnRender.Size = new Size(250, 36);
            btnRender.TabIndex = 14;
            btnRender.Text = "Запустить рендер";
            btnRender.UseVisualStyleBackColor = true;
            // 
            // pbRenderProgress
            // 
            settingsLayout.SetColumnSpan(pbRenderProgress, 2);
            pbRenderProgress.Dock = DockStyle.Fill;
            pbRenderProgress.Location = new Point(6, 416);
            pbRenderProgress.Margin = new Padding(6, 3, 6, 3);
            pbRenderProgress.Name = "pbRenderProgress";
            pbRenderProgress.Size = new Size(250, 20);
            pbRenderProgress.TabIndex = 15;
            // 
            // btnToggleControls
            // 
            btnToggleControls.AutoSize = true;
            btnToggleControls.BackColor = Color.FromArgb(235, 32, 32, 32);
            btnToggleControls.FlatStyle = FlatStyle.Popup;
            btnToggleControls.ForeColor = Color.White;
            btnToggleControls.Location = new Point(290, 9);
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
            canvas.Size = new Size(1180, 781);
            canvas.TabIndex = 1;
            canvas.TabStop = false;
            // 
            // FractalIFSForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1180, 781);
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
            settingsLayout.ResumeLayout(false);
            settingsLayout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudIterations).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudCenterX).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudCenterY).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudScale).EndInit();
            ((System.ComponentModel.ISupportInitialize)canvas).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private Panel contentPanel;
        private Panel canvasHost;
        private Panel controlsHost;
        private Panel panelControls;
        private TableLayoutPanel settingsLayout;
        private PictureBox canvas;
        private NumericUpDown nudIterations;
        private Label label3;
        private NumericUpDown nudCenterX;
        private Label label5;
        private NumericUpDown nudCenterY;
        private Label label6;
        private NumericUpDown nudScale;
        private Label label7;
        private Button btnFractalColor;
        private Button btnBackgroundColor;
        private Button btnSaveImage;
        private Button btnSaveLoad;
        private Button btnEditTransforms;
        private Button btnResetView;
        private Button btnRender;
        private ProgressBar pbRenderProgress;
        private Button btnToggleControls;
    }
}
