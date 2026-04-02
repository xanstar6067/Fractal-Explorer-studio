namespace FractalDraving
{
    partial class FractalMandelbrotFamilyForm
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

        protected void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FractalMandelbrotFamilyForm));
            btnToggleControls = new Button();
            contentPanel = new Panel();
            canvasHost = new Panel();
            controlsHost = new Panel();
            pnlControls = new TableLayoutPanel();
            nudRe = new NumericUpDown();
            lblRe = new Label();
            nudIm = new NumericUpDown();
            lblIm = new Label();
            nudIterations = new NumericUpDown();
            lblIterations = new Label();
            nudThreshold = new NumericUpDown();
            lblThreshold = new Label();
            lblZoom = new Label();
            nudZoom = new NumericUpDown();
            cbThreads = new ComboBox();
            lblThreads = new Label();
            cbSSAA = new ComboBox();
            lbSSAA = new Label();
            cbQuickColoringMode = new ComboBox();
            btnSmoothSettings = new Button();
            pnlCustomControls = new Panel();
            btnSaveHighRes = new Button();
            color_configurations = new Button();
            btnRender = new Button();
            btnStateManager = new Button();
            lblProgress = new Label();
            pbRenderProgress = new ProgressBar();
            mandelbrotPreviewPanel = new Panel();
            mandelbrotPreviewCanvas = new PictureBox();
            canvas = new PictureBox();
            contentPanel.SuspendLayout();
            canvasHost.SuspendLayout();
            controlsHost.SuspendLayout();
            pnlControls.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudRe).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudIm).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudIterations).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudThreshold).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudZoom).BeginInit();
            mandelbrotPreviewPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)mandelbrotPreviewCanvas).BeginInit();
            ((System.ComponentModel.ISupportInitialize)canvas).BeginInit();
            SuspendLayout();
            // 
            // btnToggleControls
            // 
            btnToggleControls.AutoSize = true;
            btnToggleControls.BackColor = Color.FromArgb(235, 32, 32, 32);
            btnToggleControls.FlatStyle = FlatStyle.Popup;
            btnToggleControls.ForeColor = Color.White;
            btnToggleControls.Location = new Point(256, 12);
            btnToggleControls.Name = "btnToggleControls";
            btnToggleControls.Size = new Size(44, 32);
            btnToggleControls.TabIndex = 0;
            btnToggleControls.Text = "✕";
            btnToggleControls.UseVisualStyleBackColor = true;
            btnToggleControls.Click += btnToggleControls_Click;
            // 
            // contentPanel
            // 
            contentPanel.Controls.Add(canvasHost);
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.Location = new Point(0, 0);
            contentPanel.Name = "contentPanel";
            contentPanel.Size = new Size(1084, 662);
            contentPanel.TabIndex = 0;
            // 
            // canvasHost
            // 
            canvasHost.Controls.Add(controlsHost);
            canvasHost.Controls.Add(btnToggleControls);
            canvasHost.Controls.Add(canvas);
            canvasHost.Dock = DockStyle.Fill;
            canvasHost.Location = new Point(0, 0);
            canvasHost.Name = "canvasHost";
            canvasHost.Size = new Size(1084, 662);
            canvasHost.TabIndex = 0;
            // 
            // controlsHost
            // 
            controlsHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            controlsHost.BackColor = SystemColors.Control;
            controlsHost.BorderStyle = BorderStyle.FixedSingle;
            controlsHost.Controls.Add(pnlControls);
            controlsHost.Location = new Point(0, 0);
            controlsHost.Name = "controlsHost";
            controlsHost.Size = new Size(231, 662);
            controlsHost.TabIndex = 0;
            // 
            // pnlControls
            // 
            pnlControls.ColumnCount = 2;
            pnlControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            pnlControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            pnlControls.Controls.Add(nudRe, 0, 0);
            pnlControls.Controls.Add(lblRe, 1, 0);
            pnlControls.Controls.Add(nudIm, 0, 1);
            pnlControls.Controls.Add(lblIm, 1, 1);
            pnlControls.Controls.Add(nudIterations, 0, 2);
            pnlControls.Controls.Add(lblIterations, 1, 2);
            pnlControls.Controls.Add(nudThreshold, 0, 3);
            pnlControls.Controls.Add(lblThreshold, 1, 3);
            pnlControls.Controls.Add(lblZoom, 0, 4);
            pnlControls.Controls.Add(nudZoom, 0, 5);
            pnlControls.Controls.Add(cbThreads, 0, 6);
            pnlControls.Controls.Add(lblThreads, 1, 6);
            pnlControls.Controls.Add(cbSSAA, 0, 7);
            pnlControls.Controls.Add(lbSSAA, 1, 7);
            pnlControls.Controls.Add(cbQuickColoringMode, 0, 8);
            pnlControls.Controls.Add(btnSmoothSettings, 1, 8);
            pnlControls.Controls.Add(pnlCustomControls, 0, 9);
            pnlControls.Controls.Add(btnSaveHighRes, 0, 10);
            pnlControls.Controls.Add(color_configurations, 0, 11);
            pnlControls.Controls.Add(btnRender, 0, 12);
            pnlControls.Controls.Add(btnStateManager, 0, 13);
            pnlControls.Controls.Add(lblProgress, 0, 14);
            pnlControls.Controls.Add(pbRenderProgress, 0, 15);
            pnlControls.Controls.Add(mandelbrotPreviewPanel, 0, 16);
            pnlControls.Dock = DockStyle.Fill;
            pnlControls.Location = new Point(0, 0);
            pnlControls.Name = "pnlControls";
            pnlControls.RowCount = 18;
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 161F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            pnlControls.Size = new Size(229, 660);
            pnlControls.TabIndex = 0;
            // 
            // nudRe
            // 
            nudRe.DecimalPlaces = 15;
            nudRe.Dock = DockStyle.Fill;
            nudRe.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            nudRe.Location = new Point(6, 6);
            nudRe.Margin = new Padding(6, 6, 3, 3);
            nudRe.Maximum = new decimal(new int[] { 2, 0, 0, 0 });
            nudRe.Minimum = new decimal(new int[] { 2, 0, 0, int.MinValue });
            nudRe.Name = "nudRe";
            nudRe.Size = new Size(116, 23);
            nudRe.TabIndex = 0;
            // 
            // lblRe
            // 
            lblRe.AutoSize = true;
            lblRe.Dock = DockStyle.Fill;
            lblRe.Location = new Point(128, 0);
            lblRe.Name = "lblRe";
            lblRe.Size = new Size(98, 32);
            lblRe.TabIndex = 1;
            lblRe.Text = "Действительное";
            lblRe.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // nudIm
            // 
            nudIm.DecimalPlaces = 15;
            nudIm.Dock = DockStyle.Fill;
            nudIm.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            nudIm.Location = new Point(6, 35);
            nudIm.Margin = new Padding(6, 3, 3, 3);
            nudIm.Maximum = new decimal(new int[] { 2, 0, 0, 0 });
            nudIm.Minimum = new decimal(new int[] { 2, 0, 0, int.MinValue });
            nudIm.Name = "nudIm";
            nudIm.Size = new Size(116, 23);
            nudIm.TabIndex = 2;
            // 
            // lblIm
            // 
            lblIm.AutoSize = true;
            lblIm.Dock = DockStyle.Fill;
            lblIm.Location = new Point(128, 32);
            lblIm.Name = "lblIm";
            lblIm.Size = new Size(98, 29);
            lblIm.TabIndex = 3;
            lblIm.Text = "Мнимое";
            lblIm.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // nudIterations
            // 
            nudIterations.Dock = DockStyle.Fill;
            nudIterations.Location = new Point(6, 64);
            nudIterations.Margin = new Padding(6, 3, 3, 3);
            nudIterations.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            nudIterations.Minimum = new decimal(new int[] { 50, 0, 0, 0 });
            nudIterations.Name = "nudIterations";
            nudIterations.Size = new Size(116, 23);
            nudIterations.TabIndex = 4;
            nudIterations.Value = new decimal(new int[] { 500, 0, 0, 0 });
            // 
            // lblIterations
            // 
            lblIterations.AutoSize = true;
            lblIterations.Dock = DockStyle.Fill;
            lblIterations.Location = new Point(128, 61);
            lblIterations.Name = "lblIterations";
            lblIterations.Size = new Size(98, 29);
            lblIterations.TabIndex = 5;
            lblIterations.Text = "Итерации";
            lblIterations.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // nudThreshold
            // 
            nudThreshold.DecimalPlaces = 1;
            nudThreshold.Dock = DockStyle.Fill;
            nudThreshold.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            nudThreshold.Location = new Point(6, 93);
            nudThreshold.Margin = new Padding(6, 3, 3, 3);
            nudThreshold.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            nudThreshold.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
            nudThreshold.Name = "nudThreshold";
            nudThreshold.Size = new Size(116, 23);
            nudThreshold.TabIndex = 6;
            nudThreshold.Value = new decimal(new int[] { 2, 0, 0, 0 });
            // 
            // lblThreshold
            // 
            lblThreshold.AutoSize = true;
            lblThreshold.Dock = DockStyle.Fill;
            lblThreshold.Location = new Point(128, 90);
            lblThreshold.Name = "lblThreshold";
            lblThreshold.Size = new Size(98, 29);
            lblThreshold.TabIndex = 7;
            lblThreshold.Text = "Порог выхода";
            lblThreshold.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // lblZoom
            // 
            lblZoom.AutoSize = true;
            pnlControls.SetColumnSpan(lblZoom, 2);
            lblZoom.Dock = DockStyle.Fill;
            lblZoom.Location = new Point(3, 119);
            lblZoom.Name = "lblZoom";
            lblZoom.Size = new Size(223, 15);
            lblZoom.TabIndex = 8;
            lblZoom.Text = "Приближение";
            lblZoom.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // nudZoom
            // 
            pnlControls.SetColumnSpan(nudZoom, 2);
            nudZoom.DecimalPlaces = 4;
            nudZoom.Dock = DockStyle.Fill;
            nudZoom.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            nudZoom.Location = new Point(6, 137);
            nudZoom.Margin = new Padding(6, 3, 6, 3);
            nudZoom.Maximum = new decimal(new int[] { -1, -1, -1, 0 });
            nudZoom.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            nudZoom.Name = "nudZoom";
            nudZoom.Size = new Size(217, 23);
            nudZoom.TabIndex = 9;
            nudZoom.Value = new decimal(new int[] { 1, 0, 0, 65536 });
            // 
            // cbThreads
            // 
            cbThreads.Dock = DockStyle.Fill;
            cbThreads.DropDownStyle = ComboBoxStyle.DropDownList;
            cbThreads.FormattingEnabled = true;
            cbThreads.Location = new Point(6, 166);
            cbThreads.Margin = new Padding(6, 3, 3, 3);
            cbThreads.Name = "cbThreads";
            cbThreads.Size = new Size(116, 23);
            cbThreads.TabIndex = 10;
            // 
            // lblThreads
            // 
            lblThreads.AutoSize = true;
            lblThreads.Dock = DockStyle.Fill;
            lblThreads.Location = new Point(128, 163);
            lblThreads.Name = "lblThreads";
            lblThreads.Size = new Size(98, 29);
            lblThreads.TabIndex = 11;
            lblThreads.Text = "Потоки ЦП";
            lblThreads.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // cbSSAA
            // 
            cbSSAA.Dock = DockStyle.Fill;
            cbSSAA.DropDownStyle = ComboBoxStyle.DropDownList;
            cbSSAA.FormattingEnabled = true;
            cbSSAA.Location = new Point(6, 195);
            cbSSAA.Margin = new Padding(6, 3, 3, 3);
            cbSSAA.Name = "cbSSAA";
            cbSSAA.Size = new Size(116, 23);
            cbSSAA.TabIndex = 12;
            // 
            // lbSSAA
            // 
            lbSSAA.AutoSize = true;
            lbSSAA.Dock = DockStyle.Fill;
            lbSSAA.Location = new Point(128, 192);
            lbSSAA.Name = "lbSSAA";
            lbSSAA.Size = new Size(98, 29);
            lbSSAA.TabIndex = 13;
            lbSSAA.Text = "Сглаживание";
            lbSSAA.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // cbQuickColoringMode
            // 
            cbQuickColoringMode.Dock = DockStyle.Fill;
            cbQuickColoringMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cbQuickColoringMode.FormattingEnabled = true;
            cbQuickColoringMode.Location = new Point(6, 224);
            cbQuickColoringMode.Margin = new Padding(6, 3, 3, 3);
            cbQuickColoringMode.Name = "cbQuickColoringMode";
            cbQuickColoringMode.Size = new Size(116, 23);
            cbQuickColoringMode.TabIndex = 22;
            // 
            // btnSmoothSettings
            // 
            btnSmoothSettings.Dock = DockStyle.Fill;
            btnSmoothSettings.Location = new Point(128, 224);
            btnSmoothSettings.Margin = new Padding(3, 3, 6, 3);
            btnSmoothSettings.Name = "btnSmoothSettings";
            btnSmoothSettings.Size = new Size(95, 23);
            btnSmoothSettings.TabIndex = 21;
            btnSmoothSettings.Text = "⚙ Окраска";
            btnSmoothSettings.UseVisualStyleBackColor = true;
            // 
            // pnlCustomControls
            // 
            pnlControls.SetColumnSpan(pnlCustomControls, 2);
            pnlCustomControls.Dock = DockStyle.Fill;
            pnlCustomControls.Location = new Point(3, 253);
            pnlCustomControls.Name = "pnlCustomControls";
            pnlCustomControls.Size = new Size(223, 1);
            pnlCustomControls.TabIndex = 10;
            pnlCustomControls.Visible = false;
            // 
            // btnSaveHighRes
            // 
            pnlControls.SetColumnSpan(btnSaveHighRes, 2);
            btnSaveHighRes.Dock = DockStyle.Fill;
            btnSaveHighRes.Location = new Point(6, 259);
            btnSaveHighRes.Margin = new Padding(6, 3, 6, 3);
            btnSaveHighRes.Name = "btnSaveHighRes";
            btnSaveHighRes.Size = new Size(217, 39);
            btnSaveHighRes.TabIndex = 15;
            btnSaveHighRes.Text = "Сохранить изображение";
            btnSaveHighRes.UseVisualStyleBackColor = true;
            btnSaveHighRes.Click += btnOpenSaveManager_Click;
            // 
            // color_configurations
            // 
            pnlControls.SetColumnSpan(color_configurations, 2);
            color_configurations.Dock = DockStyle.Fill;
            color_configurations.Location = new Point(6, 304);
            color_configurations.Margin = new Padding(6, 3, 6, 3);
            color_configurations.Name = "color_configurations";
            color_configurations.Size = new Size(217, 39);
            color_configurations.TabIndex = 16;
            color_configurations.Text = "Настроить палитру";
            color_configurations.UseVisualStyleBackColor = true;
            // 
            // btnRender
            // 
            pnlControls.SetColumnSpan(btnRender, 2);
            btnRender.Dock = DockStyle.Fill;
            btnRender.Location = new Point(6, 349);
            btnRender.Margin = new Padding(6, 3, 6, 3);
            btnRender.Name = "btnRender";
            btnRender.Size = new Size(217, 39);
            btnRender.TabIndex = 17;
            btnRender.Text = "Запустить рендер";
            btnRender.UseVisualStyleBackColor = true;
            // 
            // btnStateManager
            // 
            pnlControls.SetColumnSpan(btnStateManager, 2);
            btnStateManager.Dock = DockStyle.Fill;
            btnStateManager.Location = new Point(6, 394);
            btnStateManager.Margin = new Padding(6, 3, 6, 3);
            btnStateManager.Name = "btnStateManager";
            btnStateManager.Size = new Size(217, 39);
            btnStateManager.TabIndex = 18;
            btnStateManager.Text = "Менеджер сохранений";
            btnStateManager.UseVisualStyleBackColor = true;
            btnStateManager.Click += btnStateManager_Click;
            // 
            // lblProgress
            // 
            lblProgress.AutoSize = true;
            pnlControls.SetColumnSpan(lblProgress, 2);
            lblProgress.Dock = DockStyle.Fill;
            lblProgress.Location = new Point(3, 436);
            lblProgress.Name = "lblProgress";
            lblProgress.Size = new Size(223, 20);
            lblProgress.TabIndex = 19;
            lblProgress.Text = "Обработка";
            lblProgress.TextAlign = ContentAlignment.BottomCenter;
            // 
            // pbRenderProgress
            // 
            pnlControls.SetColumnSpan(pbRenderProgress, 2);
            pbRenderProgress.Dock = DockStyle.Fill;
            pbRenderProgress.Location = new Point(6, 459);
            pbRenderProgress.Margin = new Padding(6, 3, 6, 3);
            pbRenderProgress.Name = "pbRenderProgress";
            pbRenderProgress.Size = new Size(217, 24);
            pbRenderProgress.TabIndex = 20;
            // 
            // mandelbrotPreviewPanel
            // 
            pnlControls.SetColumnSpan(mandelbrotPreviewPanel, 2);
            mandelbrotPreviewPanel.Controls.Add(mandelbrotPreviewCanvas);
            mandelbrotPreviewPanel.Dock = DockStyle.Fill;
            mandelbrotPreviewPanel.Location = new Point(3, 489);
            mandelbrotPreviewPanel.Name = "mandelbrotPreviewPanel";
            mandelbrotPreviewPanel.Size = new Size(223, 155);
            mandelbrotPreviewPanel.TabIndex = 21;
            // 
            // mandelbrotPreviewCanvas
            // 
            mandelbrotPreviewCanvas.BorderStyle = BorderStyle.FixedSingle;
            mandelbrotPreviewCanvas.Dock = DockStyle.Fill;
            mandelbrotPreviewCanvas.Location = new Point(0, 0);
            mandelbrotPreviewCanvas.Name = "mandelbrotPreviewCanvas";
            mandelbrotPreviewCanvas.Size = new Size(223, 155);
            mandelbrotPreviewCanvas.TabIndex = 0;
            mandelbrotPreviewCanvas.TabStop = false;
            // 
            // canvas
            // 
            canvas.Dock = DockStyle.Fill;
            canvas.Location = new Point(0, 0);
            canvas.Name = "canvas";
            canvas.Size = new Size(1084, 662);
            canvas.TabIndex = 1;
            canvas.TabStop = false;
            // 
            // FractalMandelbrotFamilyForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1084, 662);
            Controls.Add(contentPanel);
            Icon = (Icon)resources.GetObject("$this.Icon");
            MinimumSize = new Size(1100, 695);
            Name = "FractalMandelbrotFamilyForm";
            Text = "FractalFormBase";
            Load += FormBase_Load;
            contentPanel.ResumeLayout(false);
            canvasHost.ResumeLayout(false);
            canvasHost.PerformLayout();
            controlsHost.ResumeLayout(false);
            pnlControls.ResumeLayout(false);
            pnlControls.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudRe).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudIm).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudIterations).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudThreshold).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudZoom).EndInit();
            mandelbrotPreviewPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)mandelbrotPreviewCanvas).EndInit();
            ((System.ComponentModel.ISupportInitialize)canvas).EndInit();
            ResumeLayout(false);
        }
        #endregion

        private System.Windows.Forms.Button btnToggleControls;
        private System.Windows.Forms.Panel contentPanel;
        private System.Windows.Forms.Panel canvasHost;
        private System.Windows.Forms.Panel controlsHost;
        protected System.Windows.Forms.TableLayoutPanel pnlControls;
        protected System.Windows.Forms.PictureBox canvas;
        protected System.Windows.Forms.Label lblThreshold;
        protected System.Windows.Forms.Label lblIterations;
        protected System.Windows.Forms.NumericUpDown nudThreshold;
        protected System.Windows.Forms.NumericUpDown nudIterations;
        protected System.Windows.Forms.Button btnSaveHighRes;
        protected System.Windows.Forms.Label lblThreads;
        protected System.Windows.Forms.ComboBox cbThreads;
        protected System.Windows.Forms.ProgressBar pbRenderProgress;
        protected System.Windows.Forms.Button btnRender;
        protected System.Windows.Forms.Label lblZoom;
        protected System.Windows.Forms.NumericUpDown nudZoom;
        protected System.Windows.Forms.Label lblProgress;
        protected System.Windows.Forms.Panel mandelbrotPreviewPanel;
        protected System.Windows.Forms.PictureBox mandelbrotPreviewCanvas;
        protected System.Windows.Forms.Label lblIm;
        protected System.Windows.Forms.Label lblRe;
        protected System.Windows.Forms.NumericUpDown nudIm;
        protected System.Windows.Forms.NumericUpDown nudRe;
        protected System.Windows.Forms.Button color_configurations;
        private System.Windows.Forms.Button btnStateManager;
        private System.Windows.Forms.ComboBox cbSSAA;
        protected System.Windows.Forms.Label lbSSAA;
        private System.Windows.Forms.Button btnSmoothSettings;
        private System.Windows.Forms.ComboBox cbQuickColoringMode;
        protected System.Windows.Forms.Panel pnlCustomControls;
    }
}
