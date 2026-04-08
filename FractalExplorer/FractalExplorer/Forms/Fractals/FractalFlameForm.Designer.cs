namespace FractalExplorer.Forms.Fractals
{
    partial class FractalFlameForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer? components = null;

        private Panel _controlsPanel;
        private Panel _contentPanel;
        private Panel _canvasHost;
        private TableLayoutPanel _settingsLayout;
        private Label _samplesLabel;
        private Label _iterationsLabel;
        private Label _warmupLabel;
        private Label _scaleLabel;
        private Label _centerXLabel;
        private Label _centerYLabel;
        private Label _exposureLabel;
        private Label _gammaLabel;
        private Label _threadsLabel;
        private PictureBox _canvas;
        private NumericUpDown _samples;
        private NumericUpDown _iterations;
        private NumericUpDown _warmup;
        private NumericUpDown _scale;
        private NumericUpDown _centerX;
        private NumericUpDown _centerY;
        private NumericUpDown _exposure;
        private NumericUpDown _gamma;
        private ComboBox _threads;
        private Button _btnRender;
        private Button _btnEditTransforms;
        private Button _btnSaveLoad;
        private Button _btnSaveImage;
        private ProgressBar _pbRenderProgress;
        private CheckBox _showCoverageMap;
        private Button _btnToggleControls;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                _cts?.Cancel();
                _cts?.Dispose();
                _canvas.Image?.Dispose();
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FractalFlameForm));
            _controlsPanel = new Panel();
            _settingsLayout = new TableLayoutPanel();
            _samples = new NumericUpDown();
            _samplesLabel = new Label();
            _iterations = new NumericUpDown();
            _iterationsLabel = new Label();
            _warmup = new NumericUpDown();
            _warmupLabel = new Label();
            _scale = new NumericUpDown();
            _scaleLabel = new Label();
            _centerX = new NumericUpDown();
            _centerXLabel = new Label();
            _centerY = new NumericUpDown();
            _centerYLabel = new Label();
            _exposure = new NumericUpDown();
            _exposureLabel = new Label();
            _gamma = new NumericUpDown();
            _gammaLabel = new Label();
            _threads = new ComboBox();
            _threadsLabel = new Label();
            _showCoverageMap = new CheckBox();
            _btnSaveImage = new Button();
            _btnSaveLoad = new Button();
            _btnEditTransforms = new Button();
            _btnRender = new Button();
            _pbRenderProgress = new ProgressBar();
            _contentPanel = new Panel();
            _canvasHost = new Panel();
            _btnToggleControls = new Button();
            _canvas = new PictureBox();
            _controlsPanel.SuspendLayout();
            _settingsLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_samples).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_iterations).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_warmup).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_scale).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_centerX).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_centerY).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_exposure).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_gamma).BeginInit();
            _contentPanel.SuspendLayout();
            _canvasHost.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_canvas).BeginInit();
            SuspendLayout();
            // 
            // _controlsPanel
            // 
            _controlsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            _controlsPanel.BorderStyle = BorderStyle.FixedSingle;
            _controlsPanel.Controls.Add(_settingsLayout);
            _controlsPanel.Location = new Point(0, 0);
            _controlsPanel.Name = "_controlsPanel";
            _controlsPanel.Padding = new Padding(8);
            _controlsPanel.Size = new Size(280, 781);
            _controlsPanel.TabIndex = 0;
            // 
            // _settingsLayout
            // 
            _settingsLayout.ColumnCount = 2;
            _settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            _settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            _settingsLayout.Controls.Add(_samples, 0, 0);
            _settingsLayout.Controls.Add(_samplesLabel, 1, 0);
            _settingsLayout.Controls.Add(_iterations, 0, 1);
            _settingsLayout.Controls.Add(_iterationsLabel, 1, 1);
            _settingsLayout.Controls.Add(_warmup, 0, 2);
            _settingsLayout.Controls.Add(_warmupLabel, 1, 2);
            _settingsLayout.Controls.Add(_scale, 0, 3);
            _settingsLayout.Controls.Add(_scaleLabel, 1, 3);
            _settingsLayout.Controls.Add(_centerX, 0, 4);
            _settingsLayout.Controls.Add(_centerXLabel, 1, 4);
            _settingsLayout.Controls.Add(_centerY, 0, 5);
            _settingsLayout.Controls.Add(_centerYLabel, 1, 5);
            _settingsLayout.Controls.Add(_exposure, 0, 6);
            _settingsLayout.Controls.Add(_exposureLabel, 1, 6);
            _settingsLayout.Controls.Add(_gamma, 0, 7);
            _settingsLayout.Controls.Add(_gammaLabel, 1, 7);
            _settingsLayout.Controls.Add(_threads, 0, 8);
            _settingsLayout.Controls.Add(_threadsLabel, 1, 8);
            _settingsLayout.Controls.Add(_showCoverageMap, 0, 9);
            _settingsLayout.Controls.Add(_btnSaveImage, 0, 10);
            _settingsLayout.Controls.Add(_btnSaveLoad, 0, 11);
            _settingsLayout.Controls.Add(_btnEditTransforms, 0, 12);
            _settingsLayout.Controls.Add(_btnRender, 0, 13);
            _settingsLayout.Controls.Add(_pbRenderProgress, 0, 14);
            _settingsLayout.Dock = DockStyle.Fill;
            _settingsLayout.Location = new Point(8, 8);
            _settingsLayout.Name = "_settingsLayout";
            _settingsLayout.RowCount = 16;
            _settingsLayout.RowStyles.Add(new RowStyle());
            _settingsLayout.RowStyles.Add(new RowStyle());
            _settingsLayout.RowStyles.Add(new RowStyle());
            _settingsLayout.RowStyles.Add(new RowStyle());
            _settingsLayout.RowStyles.Add(new RowStyle());
            _settingsLayout.RowStyles.Add(new RowStyle());
            _settingsLayout.RowStyles.Add(new RowStyle());
            _settingsLayout.RowStyles.Add(new RowStyle());
            _settingsLayout.RowStyles.Add(new RowStyle());
            _settingsLayout.RowStyles.Add(new RowStyle());
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _settingsLayout.Size = new Size(262, 763);
            _settingsLayout.TabIndex = 0;
            // 
            // _samples
            // 
            _samples.Dock = DockStyle.Fill;
            _samples.Increment = new decimal(new int[] { 1000, 0, 0, 0 });
            _samples.Location = new Point(6, 6);
            _samples.Margin = new Padding(6, 6, 3, 3);
            _samples.Maximum = new decimal(new int[] { 20000000, 0, 0, 0 });
            _samples.Minimum = new decimal(new int[] { 1000, 0, 0, 0 });
            _samples.Name = "_samples";
            _samples.Size = new Size(135, 23);
            _samples.TabIndex = 0;
            _samples.Value = new decimal(new int[] { 1000000, 0, 0, 0 });
            // 
            // _samplesLabel
            // 
            _samplesLabel.AutoSize = true;
            _samplesLabel.Dock = DockStyle.Fill;
            _samplesLabel.Location = new Point(147, 0);
            _samplesLabel.Name = "_samplesLabel";
            _samplesLabel.Size = new Size(112, 32);
            _samplesLabel.TabIndex = 1;
            _samplesLabel.Text = "Сэмплы";
            _samplesLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _iterations
            // 
            _iterations.Dock = DockStyle.Fill;
            _iterations.Location = new Point(6, 35);
            _iterations.Margin = new Padding(6, 3, 3, 3);
            _iterations.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
            _iterations.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            _iterations.Name = "_iterations";
            _iterations.Size = new Size(135, 23);
            _iterations.TabIndex = 2;
            _iterations.Value = new decimal(new int[] { 20, 0, 0, 0 });
            // 
            // _iterationsLabel
            // 
            _iterationsLabel.AutoSize = true;
            _iterationsLabel.Dock = DockStyle.Fill;
            _iterationsLabel.Location = new Point(147, 32);
            _iterationsLabel.Name = "_iterationsLabel";
            _iterationsLabel.Size = new Size(112, 29);
            _iterationsLabel.TabIndex = 3;
            _iterationsLabel.Text = "Итер./сэмпл";
            _iterationsLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _warmup
            // 
            _warmup.Dock = DockStyle.Fill;
            _warmup.Location = new Point(6, 64);
            _warmup.Margin = new Padding(6, 3, 3, 3);
            _warmup.Name = "_warmup";
            _warmup.Size = new Size(135, 23);
            _warmup.TabIndex = 4;
            _warmup.Value = new decimal(new int[] { 20, 0, 0, 0 });
            // 
            // _warmupLabel
            // 
            _warmupLabel.AutoSize = true;
            _warmupLabel.Dock = DockStyle.Fill;
            _warmupLabel.Location = new Point(147, 61);
            _warmupLabel.Name = "_warmupLabel";
            _warmupLabel.Size = new Size(112, 29);
            _warmupLabel.TabIndex = 5;
            _warmupLabel.Text = "Прогрев";
            _warmupLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _scale
            // 
            _scale.DecimalPlaces = 3;
            _scale.Dock = DockStyle.Fill;
            _scale.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            _scale.Location = new Point(6, 93);
            _scale.Margin = new Padding(6, 3, 3, 3);
            _scale.Maximum = new decimal(new int[] { 200000, 0, 0, 0 });
            _scale.Minimum = new decimal(new int[] { 20, 0, 0, int.MinValue });
            _scale.Name = "_scale";
            _scale.Size = new Size(135, 23);
            _scale.TabIndex = 6;
            _scale.Value = new decimal(new int[] { 4, 0, 0, 0 });
            // 
            // _scaleLabel
            // 
            _scaleLabel.AutoSize = true;
            _scaleLabel.Dock = DockStyle.Fill;
            _scaleLabel.Location = new Point(147, 90);
            _scaleLabel.Name = "_scaleLabel";
            _scaleLabel.Size = new Size(112, 29);
            _scaleLabel.TabIndex = 7;
            _scaleLabel.Text = "Масштаб";
            _scaleLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _centerX
            // 
            _centerX.DecimalPlaces = 4;
            _centerX.Dock = DockStyle.Fill;
            _centerX.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            _centerX.Location = new Point(6, 122);
            _centerX.Margin = new Padding(6, 3, 3, 3);
            _centerX.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            _centerX.Minimum = new decimal(new int[] { 100000, 0, 0, int.MinValue });
            _centerX.Name = "_centerX";
            _centerX.Size = new Size(135, 23);
            _centerX.TabIndex = 8;
            // 
            // _centerXLabel
            // 
            _centerXLabel.AutoSize = true;
            _centerXLabel.Dock = DockStyle.Fill;
            _centerXLabel.Location = new Point(147, 119);
            _centerXLabel.Name = "_centerXLabel";
            _centerXLabel.Size = new Size(112, 29);
            _centerXLabel.TabIndex = 9;
            _centerXLabel.Text = "Центр X";
            _centerXLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _centerY
            // 
            _centerY.DecimalPlaces = 4;
            _centerY.Dock = DockStyle.Fill;
            _centerY.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            _centerY.Location = new Point(6, 151);
            _centerY.Margin = new Padding(6, 3, 3, 3);
            _centerY.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            _centerY.Minimum = new decimal(new int[] { 100000, 0, 0, int.MinValue });
            _centerY.Name = "_centerY";
            _centerY.Size = new Size(135, 23);
            _centerY.TabIndex = 10;
            // 
            // _centerYLabel
            // 
            _centerYLabel.AutoSize = true;
            _centerYLabel.Dock = DockStyle.Fill;
            _centerYLabel.Location = new Point(147, 148);
            _centerYLabel.Name = "_centerYLabel";
            _centerYLabel.Size = new Size(112, 29);
            _centerYLabel.TabIndex = 11;
            _centerYLabel.Text = "Центр Y";
            _centerYLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _exposure
            // 
            _exposure.DecimalPlaces = 2;
            _exposure.Dock = DockStyle.Fill;
            _exposure.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            _exposure.Location = new Point(6, 180);
            _exposure.Margin = new Padding(6, 3, 3, 3);
            _exposure.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            _exposure.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            _exposure.Name = "_exposure";
            _exposure.Size = new Size(135, 23);
            _exposure.TabIndex = 12;
            _exposure.Value = new decimal(new int[] { 135, 0, 0, 131072 });
            // 
            // _exposureLabel
            // 
            _exposureLabel.AutoSize = true;
            _exposureLabel.Dock = DockStyle.Fill;
            _exposureLabel.Location = new Point(147, 177);
            _exposureLabel.Name = "_exposureLabel";
            _exposureLabel.Size = new Size(112, 29);
            _exposureLabel.TabIndex = 13;
            _exposureLabel.Text = "Экспозиция";
            _exposureLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _gamma
            // 
            _gamma.DecimalPlaces = 2;
            _gamma.Dock = DockStyle.Fill;
            _gamma.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            _gamma.Location = new Point(6, 209);
            _gamma.Margin = new Padding(6, 3, 3, 3);
            _gamma.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            _gamma.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            _gamma.Name = "_gamma";
            _gamma.Size = new Size(135, 23);
            _gamma.TabIndex = 14;
            _gamma.Value = new decimal(new int[] { 220, 0, 0, 131072 });
            // 
            // _gammaLabel
            // 
            _gammaLabel.AutoSize = true;
            _gammaLabel.Dock = DockStyle.Fill;
            _gammaLabel.Location = new Point(147, 206);
            _gammaLabel.Name = "_gammaLabel";
            _gammaLabel.Size = new Size(112, 29);
            _gammaLabel.TabIndex = 15;
            _gammaLabel.Text = "Гамма";
            _gammaLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _threads
            // 
            _threads.Dock = DockStyle.Fill;
            _threads.DropDownStyle = ComboBoxStyle.DropDownList;
            _threads.FormattingEnabled = true;
            _threads.Location = new Point(6, 238);
            _threads.Margin = new Padding(6, 3, 3, 3);
            _threads.Name = "_threads";
            _threads.Size = new Size(135, 23);
            _threads.TabIndex = 16;
            // 
            // _threadsLabel
            // 
            _threadsLabel.AutoSize = true;
            _threadsLabel.Dock = DockStyle.Fill;
            _threadsLabel.Location = new Point(147, 235);
            _threadsLabel.Name = "_threadsLabel";
            _threadsLabel.Size = new Size(112, 29);
            _threadsLabel.TabIndex = 17;
            _threadsLabel.Text = "Ядра CPU";
            _threadsLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _showCoverageMap
            // 
            _showCoverageMap.AutoSize = true;
            _showCoverageMap.Checked = true;
            _showCoverageMap.CheckState = CheckState.Checked;
            _settingsLayout.SetColumnSpan(_showCoverageMap, 2);
            _showCoverageMap.Dock = DockStyle.Fill;
            _showCoverageMap.Location = new Point(6, 264);
            _showCoverageMap.Margin = new Padding(6, 0, 3, 0);
            _showCoverageMap.Name = "_showCoverageMap";
            _showCoverageMap.Size = new Size(253, 19);
            _showCoverageMap.TabIndex = 18;
            _showCoverageMap.Text = "Показывать карту покрытия";
            _showCoverageMap.UseVisualStyleBackColor = true;
            // 
            // _btnSaveImage
            // 
            _settingsLayout.SetColumnSpan(_btnSaveImage, 2);
            _btnSaveImage.Dock = DockStyle.Fill;
            _btnSaveImage.Location = new Point(6, 286);
            _btnSaveImage.Margin = new Padding(6, 3, 6, 3);
            _btnSaveImage.Name = "_btnSaveImage";
            _btnSaveImage.Size = new Size(250, 36);
            _btnSaveImage.TabIndex = 19;
            _btnSaveImage.Text = "Сохранить изображение";
            _btnSaveImage.UseVisualStyleBackColor = true;
            // 
            // _btnSaveLoad
            // 
            _settingsLayout.SetColumnSpan(_btnSaveLoad, 2);
            _btnSaveLoad.Dock = DockStyle.Fill;
            _btnSaveLoad.Location = new Point(6, 328);
            _btnSaveLoad.Margin = new Padding(6, 3, 6, 3);
            _btnSaveLoad.Name = "_btnSaveLoad";
            _btnSaveLoad.Size = new Size(250, 36);
            _btnSaveLoad.TabIndex = 20;
            _btnSaveLoad.Text = "Менеджер сохранений";
            _btnSaveLoad.UseVisualStyleBackColor = true;
            // 
            // _btnEditTransforms
            // 
            _settingsLayout.SetColumnSpan(_btnEditTransforms, 2);
            _btnEditTransforms.Dock = DockStyle.Fill;
            _btnEditTransforms.Location = new Point(6, 370);
            _btnEditTransforms.Margin = new Padding(6, 3, 6, 3);
            _btnEditTransforms.Name = "_btnEditTransforms";
            _btnEditTransforms.Size = new Size(250, 36);
            _btnEditTransforms.TabIndex = 21;
            _btnEditTransforms.Text = "Трансформации";
            _btnEditTransforms.UseVisualStyleBackColor = true;
            // 
            // _btnRender
            // 
            _settingsLayout.SetColumnSpan(_btnRender, 2);
            _btnRender.Dock = DockStyle.Fill;
            _btnRender.Location = new Point(6, 412);
            _btnRender.Margin = new Padding(6, 3, 6, 3);
            _btnRender.Name = "_btnRender";
            _btnRender.Size = new Size(250, 36);
            _btnRender.TabIndex = 22;
            _btnRender.Text = "Запустить рендер";
            _btnRender.UseVisualStyleBackColor = true;
            // 
            // _pbRenderProgress
            // 
            _settingsLayout.SetColumnSpan(_pbRenderProgress, 2);
            _pbRenderProgress.Dock = DockStyle.Fill;
            _pbRenderProgress.Location = new Point(6, 454);
            _pbRenderProgress.Margin = new Padding(6, 3, 6, 3);
            _pbRenderProgress.Name = "_pbRenderProgress";
            _pbRenderProgress.Size = new Size(250, 20);
            _pbRenderProgress.Style = ProgressBarStyle.Continuous;
            _pbRenderProgress.TabIndex = 23;
            // 
            // _contentPanel
            // 
            _contentPanel.Controls.Add(_canvasHost);
            _contentPanel.Dock = DockStyle.Fill;
            _contentPanel.Location = new Point(0, 0);
            _contentPanel.Name = "_contentPanel";
            _contentPanel.Size = new Size(1264, 781);
            _contentPanel.TabIndex = 0;
            // 
            // _canvasHost
            // 
            _canvasHost.Controls.Add(_controlsPanel);
            _canvasHost.Controls.Add(_btnToggleControls);
            _canvasHost.Controls.Add(_canvas);
            _canvasHost.Dock = DockStyle.Fill;
            _canvasHost.Location = new Point(0, 0);
            _canvasHost.Name = "_canvasHost";
            _canvasHost.Size = new Size(1264, 781);
            _canvasHost.TabIndex = 0;
            // 
            // _btnToggleControls
            // 
            _btnToggleControls.AutoSize = true;
            _btnToggleControls.BackColor = Color.FromArgb(235, 32, 32, 32);
            _btnToggleControls.FlatStyle = FlatStyle.Popup;
            _btnToggleControls.ForeColor = Color.White;
            _btnToggleControls.Location = new Point(292, 12);
            _btnToggleControls.Name = "_btnToggleControls";
            _btnToggleControls.Size = new Size(44, 32);
            _btnToggleControls.TabIndex = 2;
            _btnToggleControls.Text = "✕";
            _btnToggleControls.UseVisualStyleBackColor = true;
            // 
            // _canvas
            // 
            _canvas.BackColor = Color.Black;
            _canvas.Dock = DockStyle.Fill;
            _canvas.Location = new Point(0, 0);
            _canvas.Name = "_canvas";
            _canvas.Size = new Size(1264, 781);
            _canvas.SizeMode = PictureBoxSizeMode.Zoom;
            _canvas.TabIndex = 1;
            _canvas.TabStop = false;
            // 
            // FractalFlameForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1264, 781);
            Controls.Add(_contentPanel);
            Icon = (Icon)resources.GetObject("$this.Icon");
            MinimumSize = new Size(1120, 680);
            Name = "FractalFlameForm";
            Text = "Fractal Flame";
            _controlsPanel.ResumeLayout(false);
            _settingsLayout.ResumeLayout(false);
            _settingsLayout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_samples).EndInit();
            ((System.ComponentModel.ISupportInitialize)_iterations).EndInit();
            ((System.ComponentModel.ISupportInitialize)_warmup).EndInit();
            ((System.ComponentModel.ISupportInitialize)_scale).EndInit();
            ((System.ComponentModel.ISupportInitialize)_centerX).EndInit();
            ((System.ComponentModel.ISupportInitialize)_centerY).EndInit();
            ((System.ComponentModel.ISupportInitialize)_exposure).EndInit();
            ((System.ComponentModel.ISupportInitialize)_gamma).EndInit();
            _contentPanel.ResumeLayout(false);
            _canvasHost.ResumeLayout(false);
            _canvasHost.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_canvas).EndInit();
            ResumeLayout(false);
        }

        #endregion
    }
}
