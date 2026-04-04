namespace FractalExplorer.Forms.Fractals
{
    partial class FractalBuddhabrotForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer? components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                _canvas.Image?.Dispose();
                _renderCts?.Cancel();
                _renderCts?.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            _contentPanel = new Panel();
            _canvasHost = new Panel();
            _controlsHost = new Panel();
            _controlsPanel = new TableLayoutPanel();
            _modeCombo = new ComboBox();
            _modeLabel = new Label();
            _samples = new NumericUpDown();
            _samplesLabel = new Label();
            _threadsCombo = new ComboBox();
            _threadsLabel = new Label();
            _iterations = new NumericUpDown();
            _iterationsLabel = new Label();
            _zoomLabel = new Label();
            _zoom = new NumericUpDown();
            _sampleMinRe = new NumericUpDown();
            _sampleMinReLabel = new Label();
            _sampleMaxRe = new NumericUpDown();
            _sampleMaxReLabel = new Label();
            _sampleMinIm = new NumericUpDown();
            _sampleMinImLabel = new Label();
            _sampleMaxIm = new NumericUpDown();
            _sampleMaxImLabel = new Label();
            _btnSaveImage = new Button();
            _btnPalette = new Button();
            _btnRender = new Button();
            _btnSaveLoad = new Button();
            _progressLabel = new Label();
            _renderProgress = new ProgressBar();
            _btnToggleControls = new Button();
            _canvas = new PictureBox();
            _contentPanel.SuspendLayout();
            _canvasHost.SuspendLayout();
            _controlsHost.SuspendLayout();
            _controlsPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_samples).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_iterations).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_zoom).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMinRe).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMaxRe).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMinIm).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMaxIm).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_canvas).BeginInit();
            SuspendLayout();
            // 
            // _contentPanel
            // 
            _contentPanel.Controls.Add(_canvasHost);
            _contentPanel.Dock = DockStyle.Fill;
            _contentPanel.Location = new Point(0, 0);
            _contentPanel.Name = "_contentPanel";
            _contentPanel.Size = new Size(1029, 561);
            _contentPanel.TabIndex = 0;
            // 
            // _canvasHost
            // 
            _canvasHost.Controls.Add(_controlsHost);
            _canvasHost.Controls.Add(_btnToggleControls);
            _canvasHost.Controls.Add(_canvas);
            _canvasHost.Dock = DockStyle.Fill;
            _canvasHost.Location = new Point(0, 0);
            _canvasHost.Name = "_canvasHost";
            _canvasHost.Size = new Size(1029, 561);
            _canvasHost.TabIndex = 0;
            _canvasHost.Resize += CanvasHost_Resize;
            // 
            // _controlsHost
            // 
            _controlsHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            _controlsHost.BackColor = SystemColors.Control;
            _controlsHost.BorderStyle = BorderStyle.FixedSingle;
            _controlsHost.Controls.Add(_controlsPanel);
            _controlsHost.Location = new Point(0, 0);
            _controlsHost.Name = "_controlsHost";
            _controlsHost.Size = new Size(244, 561);
            _controlsHost.TabIndex = 0;
            _controlsHost.SizeChanged += ControlsHost_SizeChanged;
            // 
            // _controlsPanel
            // 
            _controlsPanel.ColumnCount = 2;
            _controlsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            _controlsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            _controlsPanel.Controls.Add(_modeCombo, 0, 0);
            _controlsPanel.Controls.Add(_modeLabel, 1, 0);
            _controlsPanel.Controls.Add(_samples, 0, 2);
            _controlsPanel.Controls.Add(_samplesLabel, 1, 2);
            _controlsPanel.Controls.Add(_threadsCombo, 0, 3);
            _controlsPanel.Controls.Add(_threadsLabel, 1, 3);
            _controlsPanel.Controls.Add(_iterations, 0, 4);
            _controlsPanel.Controls.Add(_iterationsLabel, 1, 4);
            _controlsPanel.Controls.Add(_zoomLabel, 0, 5);
            _controlsPanel.Controls.Add(_zoom, 0, 6);
            _controlsPanel.Controls.Add(_sampleMinRe, 0, 7);
            _controlsPanel.Controls.Add(_sampleMinReLabel, 1, 7);
            _controlsPanel.Controls.Add(_sampleMaxRe, 0, 8);
            _controlsPanel.Controls.Add(_sampleMaxReLabel, 1, 8);
            _controlsPanel.Controls.Add(_sampleMinIm, 0, 9);
            _controlsPanel.Controls.Add(_sampleMinImLabel, 1, 9);
            _controlsPanel.Controls.Add(_sampleMaxIm, 0, 10);
            _controlsPanel.Controls.Add(_sampleMaxImLabel, 1, 10);
            _controlsPanel.Controls.Add(_btnSaveImage, 0, 11);
            _controlsPanel.Controls.Add(_btnPalette, 0, 12);
            _controlsPanel.Controls.Add(_btnRender, 0, 13);
            _controlsPanel.Controls.Add(_btnSaveLoad, 0, 14);
            _controlsPanel.Controls.Add(_progressLabel, 0, 15);
            _controlsPanel.Controls.Add(_renderProgress, 0, 16);
            _controlsPanel.Dock = DockStyle.Fill;
            _controlsPanel.Location = new Point(0, 0);
            _controlsPanel.Name = "_controlsPanel";
            _controlsPanel.RowCount = 18;
            _controlsPanel.RowStyles.Add(new RowStyle());
            _controlsPanel.RowStyles.Add(new RowStyle());
            _controlsPanel.RowStyles.Add(new RowStyle());
            _controlsPanel.RowStyles.Add(new RowStyle());
            _controlsPanel.RowStyles.Add(new RowStyle());
            _controlsPanel.RowStyles.Add(new RowStyle());
            _controlsPanel.RowStyles.Add(new RowStyle());
            _controlsPanel.RowStyles.Add(new RowStyle());
            _controlsPanel.RowStyles.Add(new RowStyle());
            _controlsPanel.RowStyles.Add(new RowStyle());
            _controlsPanel.RowStyles.Add(new RowStyle());
            _controlsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _controlsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _controlsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _controlsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _controlsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            _controlsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            _controlsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _controlsPanel.Size = new Size(242, 559);
            _controlsPanel.TabIndex = 0;
            // 
            // _modeCombo
            // 
            _modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _modeCombo.FormattingEnabled = true;
            _modeCombo.Items.AddRange(new object[] { "Buddhabrot", "Anti-Buddhabrot" });
            _modeCombo.Location = new Point(6, 6);
            _modeCombo.Margin = new Padding(6, 6, 3, 3);
            _modeCombo.Name = "_modeCombo";
            _modeCombo.Size = new Size(124, 23);
            _modeCombo.TabIndex = 0;
            // 
            // _modeLabel
            // 
            _modeLabel.AutoSize = true;
            _modeLabel.Dock = DockStyle.Fill;
            _modeLabel.Location = new Point(136, 0);
            _modeLabel.Name = "_modeLabel";
            _modeLabel.Size = new Size(103, 32);
            _modeLabel.TabIndex = 1;
            _modeLabel.Text = "Режим";
            _modeLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _samples
            // 
            _samples.Dock = DockStyle.Fill;
            _samples.Location = new Point(6, 64);
            _samples.Margin = new Padding(6, 3, 3, 3);
            int max_samples = int.MaxValue;
            _samples.Maximum = new decimal(new int[] { max_samples, 0, 0, 0 });
            _samples.Minimum = new decimal(new int[] { 1000, 0, 0, 0 });
            _samples.Name = "_samples";
            _samples.Size = new Size(124, 23);
            _samples.TabIndex = 4;
            _samples.Value = new decimal(new int[] { 250000, 0, 0, 0 });
            // 
            // _samplesLabel
            // 
            _samplesLabel.AutoSize = true;
            _samplesLabel.Dock = DockStyle.Fill;
            _samplesLabel.Location = new Point(136, 61);
            _samplesLabel.Name = "_samplesLabel";
            _samplesLabel.Size = new Size(103, 29);
            _samplesLabel.TabIndex = 5;
            _samplesLabel.Text = "Samples";
            _samplesLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _threadsCombo
            // 
            _threadsCombo.Dock = DockStyle.Fill;
            _threadsCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _threadsCombo.FormattingEnabled = true;
            _threadsCombo.Location = new Point(6, 93);
            _threadsCombo.Margin = new Padding(6, 3, 3, 3);
            _threadsCombo.Name = "_threadsCombo";
            _threadsCombo.Size = new Size(124, 23);
            _threadsCombo.TabIndex = 6;
            // 
            // _threadsLabel
            // 
            _threadsLabel.AutoSize = true;
            _threadsLabel.Dock = DockStyle.Fill;
            _threadsLabel.Location = new Point(136, 90);
            _threadsLabel.Name = "_threadsLabel";
            _threadsLabel.Size = new Size(103, 29);
            _threadsLabel.TabIndex = 7;
            _threadsLabel.Text = "CPU Threads";
            _threadsLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _iterations
            // 
            _iterations.Dock = DockStyle.Fill;
            _iterations.Location = new Point(6, 122);
            _iterations.Margin = new Padding(6, 3, 3, 3);
            _iterations.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            _iterations.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            _iterations.Name = "_iterations";
            _iterations.Size = new Size(124, 23);
            _iterations.TabIndex = 8;
            _iterations.Value = new decimal(new int[] { 500, 0, 0, 0 });
            // 
            // _iterationsLabel
            // 
            _iterationsLabel.AutoSize = true;
            _iterationsLabel.Dock = DockStyle.Fill;
            _iterationsLabel.Location = new Point(136, 119);
            _iterationsLabel.Name = "_iterationsLabel";
            _iterationsLabel.Size = new Size(103, 29);
            _iterationsLabel.TabIndex = 9;
            _iterationsLabel.Text = "Max Iterations";
            _iterationsLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _zoomLabel
            // 
            _zoomLabel.AutoSize = true;
            _zoomLabel.Dock = DockStyle.Fill;
            _zoomLabel.Location = new Point(3, 148);
            _zoomLabel.Name = "_zoomLabel";
            _zoomLabel.Size = new Size(127, 15);
            _zoomLabel.TabIndex = 8;
            _zoomLabel.Text = "Zoom";
            _zoomLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _zoom
            // 
            _zoom.DecimalPlaces = 6;
            _zoom.Dock = DockStyle.Fill;
            _zoom.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            _zoom.Location = new Point(6, 166);
            _zoom.Margin = new Padding(6, 3, 3, 3);
            _zoom.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            _zoom.Minimum = new decimal(new int[] { 1, 0, 0, 458752 });
            _zoom.Name = "_zoom";
            _zoom.Size = new Size(124, 23);
            _zoom.TabIndex = 10;
            _zoom.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // _sampleMinRe
            // 
            _sampleMinRe.DecimalPlaces = 4;
            _sampleMinRe.Dock = DockStyle.Fill;
            _sampleMinRe.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            _sampleMinRe.Location = new Point(6, 195);
            _sampleMinRe.Margin = new Padding(6, 3, 3, 3);
            _sampleMinRe.Minimum = new decimal(new int[] { 4, 0, 0, int.MinValue });
            _sampleMinRe.Name = "_sampleMinRe";
            _sampleMinRe.Size = new Size(124, 23);
            _sampleMinRe.TabIndex = 11;
            _sampleMinRe.Value = new decimal(new int[] { 2, 0, 0, int.MinValue });
            // 
            // _sampleMinReLabel
            // 
            _sampleMinReLabel.AutoSize = true;
            _sampleMinReLabel.Dock = DockStyle.Fill;
            _sampleMinReLabel.Location = new Point(136, 192);
            _sampleMinReLabel.Name = "_sampleMinReLabel";
            _sampleMinReLabel.Size = new Size(103, 29);
            _sampleMinReLabel.TabIndex = 12;
            _sampleMinReLabel.Text = "Sample Min Re";
            _sampleMinReLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _sampleMaxRe
            // 
            _sampleMaxRe.DecimalPlaces = 4;
            _sampleMaxRe.Dock = DockStyle.Fill;
            _sampleMaxRe.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            _sampleMaxRe.Location = new Point(6, 224);
            _sampleMaxRe.Margin = new Padding(6, 3, 3, 3);
            _sampleMaxRe.Maximum = new decimal(new int[] { 4, 0, 0, 0 });
            _sampleMaxRe.Minimum = new decimal(new int[] { 4, 0, 0, int.MinValue });
            _sampleMaxRe.Name = "_sampleMaxRe";
            _sampleMaxRe.Size = new Size(124, 23);
            _sampleMaxRe.TabIndex = 13;
            _sampleMaxRe.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // _sampleMaxReLabel
            // 
            _sampleMaxReLabel.AutoSize = true;
            _sampleMaxReLabel.Dock = DockStyle.Fill;
            _sampleMaxReLabel.Location = new Point(136, 221);
            _sampleMaxReLabel.Name = "_sampleMaxReLabel";
            _sampleMaxReLabel.Size = new Size(103, 29);
            _sampleMaxReLabel.TabIndex = 14;
            _sampleMaxReLabel.Text = "Sample Max Re";
            _sampleMaxReLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _sampleMinIm
            // 
            _sampleMinIm.DecimalPlaces = 4;
            _sampleMinIm.Dock = DockStyle.Fill;
            _sampleMinIm.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            _sampleMinIm.Location = new Point(6, 253);
            _sampleMinIm.Margin = new Padding(6, 3, 3, 3);
            _sampleMinIm.Minimum = new decimal(new int[] { 4, 0, 0, int.MinValue });
            _sampleMinIm.Name = "_sampleMinIm";
            _sampleMinIm.Size = new Size(124, 23);
            _sampleMinIm.TabIndex = 15;
            _sampleMinIm.Value = new decimal(new int[] { 15, 0, 0, -2147418112 });
            // 
            // _sampleMinImLabel
            // 
            _sampleMinImLabel.AutoSize = true;
            _sampleMinImLabel.Dock = DockStyle.Fill;
            _sampleMinImLabel.Location = new Point(136, 250);
            _sampleMinImLabel.Name = "_sampleMinImLabel";
            _sampleMinImLabel.Size = new Size(103, 29);
            _sampleMinImLabel.TabIndex = 16;
            _sampleMinImLabel.Text = "Sample Min Im";
            _sampleMinImLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _sampleMaxIm
            // 
            _sampleMaxIm.DecimalPlaces = 4;
            _sampleMaxIm.Dock = DockStyle.Fill;
            _sampleMaxIm.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            _sampleMaxIm.Location = new Point(6, 282);
            _sampleMaxIm.Margin = new Padding(6, 3, 3, 3);
            _sampleMaxIm.Maximum = new decimal(new int[] { 4, 0, 0, 0 });
            _sampleMaxIm.Minimum = new decimal(new int[] { 4, 0, 0, int.MinValue });
            _sampleMaxIm.Name = "_sampleMaxIm";
            _sampleMaxIm.Size = new Size(124, 23);
            _sampleMaxIm.TabIndex = 17;
            _sampleMaxIm.Value = new decimal(new int[] { 15, 0, 0, 65536 });
            // 
            // _sampleMaxImLabel
            // 
            _sampleMaxImLabel.AutoSize = true;
            _sampleMaxImLabel.Dock = DockStyle.Fill;
            _sampleMaxImLabel.Location = new Point(136, 279);
            _sampleMaxImLabel.Name = "_sampleMaxImLabel";
            _sampleMaxImLabel.Size = new Size(103, 29);
            _sampleMaxImLabel.TabIndex = 18;
            _sampleMaxImLabel.Text = "Sample Max Im";
            _sampleMaxImLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _btnSaveImage
            // 
            _controlsPanel.SetColumnSpan(_btnSaveImage, 2);
            _btnSaveImage.Dock = DockStyle.Fill;
            _btnSaveImage.Location = new Point(6, 311);
            _btnSaveImage.Margin = new Padding(6, 3, 6, 3);
            _btnSaveImage.Name = "_btnSaveImage";
            _btnSaveImage.Size = new Size(230, 39);
            _btnSaveImage.TabIndex = 19;
            _btnSaveImage.Text = "Сохранить изображение";
            _btnSaveImage.UseVisualStyleBackColor = true;
            // 
            // _btnPalette
            // 
            _controlsPanel.SetColumnSpan(_btnPalette, 2);
            _btnPalette.Dock = DockStyle.Fill;
            _btnPalette.Location = new Point(6, 356);
            _btnPalette.Margin = new Padding(6, 3, 6, 3);
            _btnPalette.Name = "_btnPalette";
            _btnPalette.Size = new Size(230, 39);
            _btnPalette.TabIndex = 20;
            _btnPalette.Text = "Настроить палитру";
            _btnPalette.UseVisualStyleBackColor = true;
            _btnPalette.Click += BtnPalette_Click;
            // 
            // _btnRender
            // 
            _controlsPanel.SetColumnSpan(_btnRender, 2);
            _btnRender.Dock = DockStyle.Fill;
            _btnRender.Location = new Point(6, 401);
            _btnRender.Margin = new Padding(6, 3, 6, 3);
            _btnRender.Name = "_btnRender";
            _btnRender.Size = new Size(230, 39);
            _btnRender.TabIndex = 21;
            _btnRender.Text = "Рендер";
            _btnRender.UseVisualStyleBackColor = true;
            _btnRender.Click += BtnRender_Click;
            // 
            // _btnSaveLoad
            // 
            _controlsPanel.SetColumnSpan(_btnSaveLoad, 2);
            _btnSaveLoad.Dock = DockStyle.Fill;
            _btnSaveLoad.Location = new Point(6, 446);
            _btnSaveLoad.Margin = new Padding(6, 3, 6, 3);
            _btnSaveLoad.Name = "_btnSaveLoad";
            _btnSaveLoad.Size = new Size(230, 39);
            _btnSaveLoad.TabIndex = 22;
            _btnSaveLoad.Text = "Менеджер сохранений";
            _btnSaveLoad.UseVisualStyleBackColor = true;
            _btnSaveLoad.Click += BtnSaveLoad_Click;
            // 
            // _progressLabel
            // 
            _progressLabel.AutoSize = true;
            _controlsPanel.SetColumnSpan(_progressLabel, 2);
            _progressLabel.Dock = DockStyle.Fill;
            _progressLabel.Location = new Point(3, 488);
            _progressLabel.Name = "_progressLabel";
            _progressLabel.Size = new Size(236, 20);
            _progressLabel.TabIndex = 23;
            _progressLabel.Text = "Обработка: 0%";
            _progressLabel.TextAlign = ContentAlignment.BottomCenter;
            // 
            // _renderProgress
            // 
            _controlsPanel.SetColumnSpan(_renderProgress, 2);
            _renderProgress.Dock = DockStyle.Fill;
            _renderProgress.Location = new Point(6, 511);
            _renderProgress.Margin = new Padding(6, 3, 6, 3);
            _renderProgress.Name = "_renderProgress";
            _renderProgress.Size = new Size(230, 24);
            _renderProgress.TabIndex = 24;
            // 
            // _btnToggleControls
            // 
            _btnToggleControls.AutoSize = true;
            _btnToggleControls.BackColor = Color.FromArgb(235, 32, 32, 32);
            _btnToggleControls.FlatStyle = FlatStyle.Popup;
            _btnToggleControls.ForeColor = Color.White;
            _btnToggleControls.Location = new Point(269, 12);
            _btnToggleControls.Name = "_btnToggleControls";
            _btnToggleControls.Size = new Size(44, 32);
            _btnToggleControls.TabIndex = 1;
            _btnToggleControls.Text = "✕";
            _btnToggleControls.UseVisualStyleBackColor = false;
            _btnToggleControls.Click += BtnToggleControls_Click;
            // 
            // _canvas
            // 
            _canvas.BackColor = Color.Black;
            _canvas.Dock = DockStyle.Fill;
            _canvas.Location = new Point(0, 0);
            _canvas.Name = "_canvas";
            _canvas.Size = new Size(1029, 561);
            _canvas.SizeMode = PictureBoxSizeMode.Zoom;
            _canvas.TabIndex = 2;
            _canvas.TabStop = false;
            _canvas.MouseWheel += Canvas_MouseWheel;
            // 
            // FractalBuddhabrotForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1029, 561);
            Controls.Add(_contentPanel);
            KeyPreview = true;
            MinimumSize = new Size(1045, 600);
            Name = "FractalBuddhabrotForm";
            Text = "Фрактал Buddhabrot";
            FormClosing += FractalBuddhabrotForm_FormClosing;
            Load += FractalBuddhabrotForm_Load;
            _contentPanel.ResumeLayout(false);
            _canvasHost.ResumeLayout(false);
            _canvasHost.PerformLayout();
            _controlsHost.ResumeLayout(false);
            _controlsPanel.ResumeLayout(false);
            _controlsPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_samples).EndInit();
            ((System.ComponentModel.ISupportInitialize)_iterations).EndInit();
            ((System.ComponentModel.ISupportInitialize)_zoom).EndInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMinRe).EndInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMaxRe).EndInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMinIm).EndInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMaxIm).EndInit();
            ((System.ComponentModel.ISupportInitialize)_canvas).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private Panel _contentPanel;
        private Panel _canvasHost;
        private Panel _controlsHost;
        private TableLayoutPanel _controlsPanel;
        private PictureBox _canvas;
        private Button _btnToggleControls;
        private ComboBox _modeCombo;
        private Label _modeLabel;
        private NumericUpDown _samples;
        private Label _samplesLabel;
        private ComboBox _threadsCombo;
        private Label _threadsLabel;
        private NumericUpDown _iterations;
        private Label _iterationsLabel;
        private Label _zoomLabel;
        private NumericUpDown _zoom;
        private NumericUpDown _sampleMinRe;
        private Label _sampleMinReLabel;
        private NumericUpDown _sampleMaxRe;
        private Label _sampleMaxReLabel;
        private NumericUpDown _sampleMinIm;
        private Label _sampleMinImLabel;
        private NumericUpDown _sampleMaxIm;
        private Label _sampleMaxImLabel;
        private Button _btnSaveImage;
        private Button _btnPalette;
        private Button _btnRender;
        private Button _btnSaveLoad;
        private Label _progressLabel;
        private ProgressBar _renderProgress;
    }
}
