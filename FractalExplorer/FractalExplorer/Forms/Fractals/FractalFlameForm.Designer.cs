namespace FractalExplorer.Forms.Fractals
{
    partial class FractalFlameForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer? components = null;

        private Panel _controlsPanel;
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
        private Label _status;

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
            _controlsPanel = new Panel();
            _settingsLayout = new TableLayoutPanel();
            _samplesLabel = new Label();
            _iterationsLabel = new Label();
            _warmupLabel = new Label();
            _scaleLabel = new Label();
            _centerXLabel = new Label();
            _centerYLabel = new Label();
            _exposureLabel = new Label();
            _gammaLabel = new Label();
            _threadsLabel = new Label();
            _samples = new NumericUpDown();
            _iterations = new NumericUpDown();
            _warmup = new NumericUpDown();
            _scale = new NumericUpDown();
            _centerX = new NumericUpDown();
            _centerY = new NumericUpDown();
            _exposure = new NumericUpDown();
            _gamma = new NumericUpDown();
            _threads = new ComboBox();
            _btnRender = new Button();
            _btnEditTransforms = new Button();
            _btnSaveLoad = new Button();
            _btnSaveImage = new Button();
            _status = new Label();
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
            ((System.ComponentModel.ISupportInitialize)_canvas).BeginInit();
            SuspendLayout();
            // 
            // _controlsPanel
            // 
            _controlsPanel.Controls.Add(_status);
            _controlsPanel.Controls.Add(_btnSaveImage);
            _controlsPanel.Controls.Add(_btnSaveLoad);
            _controlsPanel.Controls.Add(_btnEditTransforms);
            _controlsPanel.Controls.Add(_btnRender);
            _controlsPanel.Controls.Add(_settingsLayout);
            _controlsPanel.Dock = DockStyle.Left;
            _controlsPanel.Location = new Point(0, 0);
            _controlsPanel.Name = "_controlsPanel";
            _controlsPanel.Padding = new Padding(8);
            _controlsPanel.Size = new Size(280, 781);
            _controlsPanel.TabIndex = 0;
            // 
            // _settingsLayout
            // 
            _settingsLayout.AutoScroll = true;
            _settingsLayout.ColumnCount = 2;
            _settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            _settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            _settingsLayout.Controls.Add(_samplesLabel, 0, 0);
            _settingsLayout.Controls.Add(_samples, 1, 0);
            _settingsLayout.Controls.Add(_iterationsLabel, 0, 1);
            _settingsLayout.Controls.Add(_iterations, 1, 1);
            _settingsLayout.Controls.Add(_warmupLabel, 0, 2);
            _settingsLayout.Controls.Add(_warmup, 1, 2);
            _settingsLayout.Controls.Add(_scaleLabel, 0, 3);
            _settingsLayout.Controls.Add(_scale, 1, 3);
            _settingsLayout.Controls.Add(_centerXLabel, 0, 4);
            _settingsLayout.Controls.Add(_centerX, 1, 4);
            _settingsLayout.Controls.Add(_centerYLabel, 0, 5);
            _settingsLayout.Controls.Add(_centerY, 1, 5);
            _settingsLayout.Controls.Add(_exposureLabel, 0, 6);
            _settingsLayout.Controls.Add(_exposure, 1, 6);
            _settingsLayout.Controls.Add(_gammaLabel, 0, 7);
            _settingsLayout.Controls.Add(_gamma, 1, 7);
            _settingsLayout.Controls.Add(_threadsLabel, 0, 8);
            _settingsLayout.Controls.Add(_threads, 1, 8);
            _settingsLayout.Dock = DockStyle.Fill;
            _settingsLayout.Location = new Point(8, 8);
            _settingsLayout.Name = "_settingsLayout";
            _settingsLayout.RowCount = 10;
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _settingsLayout.Size = new Size(264, 584);
            _settingsLayout.TabIndex = 0;
            // 
            // _samplesLabel
            // 
            _samplesLabel.AutoSize = true;
            _samplesLabel.Margin = new Padding(3, 8, 3, 3);
            _samplesLabel.Name = "_samplesLabel";
            _samplesLabel.Size = new Size(52, 15);
            _samplesLabel.TabIndex = 0;
            _samplesLabel.Text = "Samples";
            // 
            // _iterationsLabel
            // 
            _iterationsLabel.AutoSize = true;
            _iterationsLabel.Margin = new Padding(3, 8, 3, 3);
            _iterationsLabel.Name = "_iterationsLabel";
            _iterationsLabel.Size = new Size(65, 15);
            _iterationsLabel.TabIndex = 2;
            _iterationsLabel.Text = "Iter/Sample";
            // 
            // _warmupLabel
            // 
            _warmupLabel.AutoSize = true;
            _warmupLabel.Margin = new Padding(3, 8, 3, 3);
            _warmupLabel.Name = "_warmupLabel";
            _warmupLabel.Size = new Size(52, 15);
            _warmupLabel.TabIndex = 4;
            _warmupLabel.Text = "Warmup";
            // 
            // _scaleLabel
            // 
            _scaleLabel.AutoSize = true;
            _scaleLabel.Margin = new Padding(3, 8, 3, 3);
            _scaleLabel.Name = "_scaleLabel";
            _scaleLabel.Size = new Size(35, 15);
            _scaleLabel.TabIndex = 6;
            _scaleLabel.Text = "Scale";
            // 
            // _centerXLabel
            // 
            _centerXLabel.AutoSize = true;
            _centerXLabel.Margin = new Padding(3, 8, 3, 3);
            _centerXLabel.Name = "_centerXLabel";
            _centerXLabel.Size = new Size(50, 15);
            _centerXLabel.TabIndex = 8;
            _centerXLabel.Text = "CenterX";
            // 
            // _centerYLabel
            // 
            _centerYLabel.AutoSize = true;
            _centerYLabel.Margin = new Padding(3, 8, 3, 3);
            _centerYLabel.Name = "_centerYLabel";
            _centerYLabel.Size = new Size(50, 15);
            _centerYLabel.TabIndex = 10;
            _centerYLabel.Text = "CenterY";
            // 
            // _exposureLabel
            // 
            _exposureLabel.AutoSize = true;
            _exposureLabel.Margin = new Padding(3, 8, 3, 3);
            _exposureLabel.Name = "_exposureLabel";
            _exposureLabel.Size = new Size(53, 15);
            _exposureLabel.TabIndex = 12;
            _exposureLabel.Text = "Exposure";
            // 
            // _gammaLabel
            // 
            _gammaLabel.AutoSize = true;
            _gammaLabel.Margin = new Padding(3, 8, 3, 3);
            _gammaLabel.Name = "_gammaLabel";
            _gammaLabel.Size = new Size(46, 15);
            _gammaLabel.TabIndex = 14;
            _gammaLabel.Text = "Gamma";
            // 
            // _threadsLabel
            // 
            _threadsLabel.AutoSize = true;
            _threadsLabel.Margin = new Padding(3, 8, 3, 3);
            _threadsLabel.Name = "_threadsLabel";
            _threadsLabel.Size = new Size(48, 15);
            _threadsLabel.TabIndex = 16;
            _threadsLabel.Text = "Threads";
            // 
            // _samples
            // 
            _samples.DecimalPlaces = 0;
            _samples.Dock = DockStyle.Top;
            _samples.Increment = new decimal(new int[] { 1000, 0, 0, 0 });
            _samples.Location = new Point(148, 3);
            _samples.Maximum = new decimal(new int[] { 20000000, 0, 0, 0 });
            _samples.Minimum = new decimal(new int[] { 1000, 0, 0, 0 });
            _samples.Name = "_samples";
            _samples.Size = new Size(113, 23);
            _samples.TabIndex = 1;
            _samples.Value = new decimal(new int[] { 1000000, 0, 0, 0 });
            // 
            // _iterations
            // 
            _iterations.Dock = DockStyle.Top;
            _iterations.Location = new Point(148, 32);
            _iterations.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
            _iterations.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            _iterations.Name = "_iterations";
            _iterations.Size = new Size(113, 23);
            _iterations.TabIndex = 3;
            _iterations.Value = new decimal(new int[] { 20, 0, 0, 0 });
            // 
            // _warmup
            // 
            _warmup.Dock = DockStyle.Top;
            _warmup.Location = new Point(148, 61);
            _warmup.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            _warmup.Name = "_warmup";
            _warmup.Size = new Size(113, 23);
            _warmup.TabIndex = 5;
            _warmup.Value = new decimal(new int[] { 20, 0, 0, 0 });
            // 
            // _scale
            // 
            _scale.DecimalPlaces = 3;
            _scale.Dock = DockStyle.Top;
            _scale.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            _scale.Location = new Point(148, 90);
            _scale.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            _scale.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            _scale.Name = "_scale";
            _scale.Size = new Size(113, 23);
            _scale.TabIndex = 7;
            _scale.Value = new decimal(new int[] { 4, 0, 0, 0 });
            // 
            // _centerX
            // 
            _centerX.DecimalPlaces = 4;
            _centerX.Dock = DockStyle.Top;
            _centerX.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            _centerX.Location = new Point(148, 119);
            _centerX.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            _centerX.Minimum = new decimal(new int[] { 10, 0, 0, int.MinValue });
            _centerX.Name = "_centerX";
            _centerX.Size = new Size(113, 23);
            _centerX.TabIndex = 9;
            // 
            // _centerY
            // 
            _centerY.DecimalPlaces = 4;
            _centerY.Dock = DockStyle.Top;
            _centerY.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            _centerY.Location = new Point(148, 148);
            _centerY.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            _centerY.Minimum = new decimal(new int[] { 10, 0, 0, int.MinValue });
            _centerY.Name = "_centerY";
            _centerY.Size = new Size(113, 23);
            _centerY.TabIndex = 11;
            // 
            // _exposure
            // 
            _exposure.DecimalPlaces = 2;
            _exposure.Dock = DockStyle.Top;
            _exposure.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            _exposure.Location = new Point(148, 177);
            _exposure.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            _exposure.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            _exposure.Name = "_exposure";
            _exposure.Size = new Size(113, 23);
            _exposure.TabIndex = 13;
            _exposure.Value = new decimal(new int[] { 135, 0, 0, 131072 });
            // 
            // _gamma
            // 
            _gamma.DecimalPlaces = 2;
            _gamma.Dock = DockStyle.Top;
            _gamma.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            _gamma.Location = new Point(148, 206);
            _gamma.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            _gamma.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            _gamma.Name = "_gamma";
            _gamma.Size = new Size(113, 23);
            _gamma.TabIndex = 15;
            _gamma.Value = new decimal(new int[] { 220, 0, 0, 131072 });
            // 
            // _threads
            // 
            _threads.Dock = DockStyle.Top;
            _threads.DropDownStyle = ComboBoxStyle.DropDownList;
            _threads.FormattingEnabled = true;
            _threads.Location = new Point(148, 235);
            _threads.Name = "_threads";
            _threads.Size = new Size(113, 23);
            _threads.TabIndex = 17;
            // 
            // _btnRender
            // 
            _btnRender.Dock = DockStyle.Bottom;
            _btnRender.Location = new Point(8, 592);
            _btnRender.Name = "_btnRender";
            _btnRender.Size = new Size(264, 40);
            _btnRender.TabIndex = 1;
            _btnRender.Text = "Render";
            _btnRender.UseVisualStyleBackColor = true;
            // 
            // _btnEditTransforms
            // 
            _btnEditTransforms.Dock = DockStyle.Bottom;
            _btnEditTransforms.Location = new Point(8, 632);
            _btnEditTransforms.Name = "_btnEditTransforms";
            _btnEditTransforms.Size = new Size(264, 40);
            _btnEditTransforms.TabIndex = 2;
            _btnEditTransforms.Text = "Трансформации";
            _btnEditTransforms.UseVisualStyleBackColor = true;
            // 
            // _btnSaveLoad
            // 
            _btnSaveLoad.Dock = DockStyle.Bottom;
            _btnSaveLoad.Location = new Point(8, 672);
            _btnSaveLoad.Name = "_btnSaveLoad";
            _btnSaveLoad.Size = new Size(264, 40);
            _btnSaveLoad.TabIndex = 3;
            _btnSaveLoad.Text = "Save/Load";
            _btnSaveLoad.UseVisualStyleBackColor = true;
            // 
            // _btnSaveImage
            // 
            _btnSaveImage.Dock = DockStyle.Bottom;
            _btnSaveImage.Location = new Point(8, 712);
            _btnSaveImage.Name = "_btnSaveImage";
            _btnSaveImage.Size = new Size(264, 40);
            _btnSaveImage.TabIndex = 4;
            _btnSaveImage.Text = "Сохранить PNG";
            _btnSaveImage.UseVisualStyleBackColor = true;
            // 
            // _status
            // 
            _status.AutoSize = true;
            _status.Dock = DockStyle.Bottom;
            _status.Location = new Point(8, 752);
            _status.Name = "_status";
            _status.Size = new Size(45, 15);
            _status.TabIndex = 5;
            _status.Text = "Готово";
            // 
            // _canvas
            // 
            _canvas.BackColor = Color.Black;
            _canvas.Dock = DockStyle.Fill;
            _canvas.Location = new Point(280, 0);
            _canvas.Name = "_canvas";
            _canvas.Size = new Size(984, 781);
            _canvas.SizeMode = PictureBoxSizeMode.Zoom;
            _canvas.TabIndex = 1;
            _canvas.TabStop = false;
            // 
            // FractalFlameForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1264, 781);
            Controls.Add(_canvas);
            Controls.Add(_controlsPanel);
            Name = "FractalFlameForm";
            Text = "Fractal Flame";
            _controlsPanel.ResumeLayout(false);
            _controlsPanel.PerformLayout();
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
            ((System.ComponentModel.ISupportInitialize)_canvas).EndInit();
            ResumeLayout(false);
        }

        #endregion
    }
}
