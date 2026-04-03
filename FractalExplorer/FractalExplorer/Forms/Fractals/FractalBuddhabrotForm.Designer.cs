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
            _rootSplitContainer = new SplitContainer();
            _canvas = new PictureBox();
            _controlsPanel = new FlowLayoutPanel();
            _modeLabel = new Label();
            _modeCombo = new ComboBox();
            _paletteLabel = new Label();
            _paletteCombo = new ComboBox();
            _samplesLabel = new Label();
            _samples = new NumericUpDown();
            _iterationsLabel = new Label();
            _iterations = new NumericUpDown();
            _zoomLabel = new Label();
            _zoom = new NumericUpDown();
            _sampleMinReLabel = new Label();
            _sampleMinRe = new NumericUpDown();
            _sampleMaxReLabel = new Label();
            _sampleMaxRe = new NumericUpDown();
            _sampleMinImLabel = new Label();
            _sampleMinIm = new NumericUpDown();
            _sampleMaxImLabel = new Label();
            _sampleMaxIm = new NumericUpDown();
            _btnRender = new Button();
            _btnSaveLoad = new Button();
            ((System.ComponentModel.ISupportInitialize)_rootSplitContainer).BeginInit();
            _rootSplitContainer.Panel1.SuspendLayout();
            _rootSplitContainer.Panel2.SuspendLayout();
            _rootSplitContainer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_canvas).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_samples).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_iterations).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_zoom).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMinRe).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMaxRe).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMinIm).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMaxIm).BeginInit();
            SuspendLayout();
            // 
            // _rootSplitContainer
            // 
            _rootSplitContainer.Dock = DockStyle.Fill;
            _rootSplitContainer.Location = new Point(0, 0);
            _rootSplitContainer.Name = "_rootSplitContainer";
            // 
            // _rootSplitContainer.Panel1
            // 
            _rootSplitContainer.Panel1.Controls.Add(_canvas);
            // 
            // _rootSplitContainer.Panel2
            // 
            _rootSplitContainer.Panel2.Controls.Add(_controlsPanel);
            _rootSplitContainer.Size = new Size(1304, 821);
            _rootSplitContainer.SplitterDistance = 1020;
            _rootSplitContainer.TabIndex = 0;
            // 
            // _canvas
            // 
            _canvas.BackColor = Color.Black;
            _canvas.Dock = DockStyle.Fill;
            _canvas.Location = new Point(0, 0);
            _canvas.Name = "_canvas";
            _canvas.Size = new Size(1020, 821);
            _canvas.SizeMode = PictureBoxSizeMode.Zoom;
            _canvas.TabIndex = 0;
            _canvas.TabStop = false;
            _canvas.MouseWheel += Canvas_MouseWheel;
            // 
            // _controlsPanel
            // 
            _controlsPanel.AutoScroll = true;
            _controlsPanel.Dock = DockStyle.Fill;
            _controlsPanel.FlowDirection = FlowDirection.TopDown;
            _controlsPanel.WrapContents = false;
            _controlsPanel.Padding = new Padding(8);
            _controlsPanel.Controls.Add(_modeLabel);
            _controlsPanel.Controls.Add(_modeCombo);
            _controlsPanel.Controls.Add(_paletteLabel);
            _controlsPanel.Controls.Add(_paletteCombo);
            _controlsPanel.Controls.Add(_samplesLabel);
            _controlsPanel.Controls.Add(_samples);
            _controlsPanel.Controls.Add(_iterationsLabel);
            _controlsPanel.Controls.Add(_iterations);
            _controlsPanel.Controls.Add(_zoomLabel);
            _controlsPanel.Controls.Add(_zoom);
            _controlsPanel.Controls.Add(_sampleMinReLabel);
            _controlsPanel.Controls.Add(_sampleMinRe);
            _controlsPanel.Controls.Add(_sampleMaxReLabel);
            _controlsPanel.Controls.Add(_sampleMaxRe);
            _controlsPanel.Controls.Add(_sampleMinImLabel);
            _controlsPanel.Controls.Add(_sampleMinIm);
            _controlsPanel.Controls.Add(_sampleMaxImLabel);
            _controlsPanel.Controls.Add(_sampleMaxIm);
            _controlsPanel.Controls.Add(_btnRender);
            _controlsPanel.Controls.Add(_btnSaveLoad);
            _controlsPanel.Location = new Point(0, 0);
            _controlsPanel.Name = "_controlsPanel";
            _controlsPanel.Size = new Size(280, 821);
            _controlsPanel.TabIndex = 0;
            // 
            // _modeLabel
            // 
            _modeLabel.Location = new Point(11, 8);
            _modeLabel.Name = "_modeLabel";
            _modeLabel.Size = new Size(220, 24);
            _modeLabel.TabIndex = 0;
            _modeLabel.Text = "Режим";
            _modeLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _modeCombo
            // 
            _modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _modeCombo.FormattingEnabled = true;
            _modeCombo.Items.AddRange(new object[] { "Buddhabrot", "Anti-Buddhabrot" });
            _modeCombo.Location = new Point(11, 35);
            _modeCombo.Name = "_modeCombo";
            _modeCombo.Size = new Size(220, 23);
            _modeCombo.TabIndex = 1;
            // 
            // _paletteLabel
            // 
            _paletteLabel.Location = new Point(11, 61);
            _paletteLabel.Name = "_paletteLabel";
            _paletteLabel.Size = new Size(220, 24);
            _paletteLabel.TabIndex = 2;
            _paletteLabel.Text = "Палитра";
            _paletteLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _paletteCombo
            // 
            _paletteCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _paletteCombo.FormattingEnabled = true;
            _paletteCombo.Location = new Point(11, 88);
            _paletteCombo.Name = "_paletteCombo";
            _paletteCombo.Size = new Size(220, 23);
            _paletteCombo.TabIndex = 3;
            // 
            // _samplesLabel
            // 
            _samplesLabel.Location = new Point(11, 114);
            _samplesLabel.Name = "_samplesLabel";
            _samplesLabel.Size = new Size(220, 24);
            _samplesLabel.TabIndex = 4;
            _samplesLabel.Text = "Samples";
            _samplesLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _samples
            // 
            _samples.Location = new Point(11, 141);
            _samples.Maximum = new decimal(new int[] { 5000000, 0, 0, 0 });
            _samples.Minimum = new decimal(new int[] { 1000, 0, 0, 0 });
            _samples.Name = "_samples";
            _samples.Size = new Size(220, 23);
            _samples.TabIndex = 5;
            _samples.Value = new decimal(new int[] { 250000, 0, 0, 0 });
            // 
            // _iterationsLabel
            // 
            _iterationsLabel.Location = new Point(11, 167);
            _iterationsLabel.Name = "_iterationsLabel";
            _iterationsLabel.Size = new Size(220, 24);
            _iterationsLabel.TabIndex = 6;
            _iterationsLabel.Text = "Max Iterations";
            _iterationsLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _iterations
            // 
            _iterations.Location = new Point(11, 194);
            _iterations.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            _iterations.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            _iterations.Name = "_iterations";
            _iterations.Size = new Size(220, 23);
            _iterations.TabIndex = 7;
            _iterations.Value = new decimal(new int[] { 500, 0, 0, 0 });
            // 
            // _zoomLabel
            // 
            _zoomLabel.Location = new Point(11, 220);
            _zoomLabel.Name = "_zoomLabel";
            _zoomLabel.Size = new Size(220, 24);
            _zoomLabel.TabIndex = 8;
            _zoomLabel.Text = "Zoom";
            _zoomLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _zoom
            // 
            _zoom.DecimalPlaces = 6;
            _zoom.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            _zoom.Location = new Point(11, 247);
            _zoom.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            _zoom.Minimum = 0.0000001m;
            _zoom.Name = "_zoom";
            _zoom.Size = new Size(220, 23);
            _zoom.TabIndex = 9;
            _zoom.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // _sampleMinReLabel
            // 
            _sampleMinReLabel.Location = new Point(11, 273);
            _sampleMinReLabel.Name = "_sampleMinReLabel";
            _sampleMinReLabel.Size = new Size(220, 24);
            _sampleMinReLabel.TabIndex = 10;
            _sampleMinReLabel.Text = "Sample Min Re";
            _sampleMinReLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _sampleMinRe
            // 
            _sampleMinRe.DecimalPlaces = 4;
            _sampleMinRe.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            _sampleMinRe.Location = new Point(11, 300);
            _sampleMinRe.Minimum = new decimal(new int[] { 4, 0, 0, int.MinValue });
            _sampleMinRe.Name = "_sampleMinRe";
            _sampleMinRe.Size = new Size(220, 23);
            _sampleMinRe.TabIndex = 11;
            _sampleMinRe.Value = new decimal(new int[] { 2, 0, 0, int.MinValue });
            // 
            // _sampleMaxReLabel
            // 
            _sampleMaxReLabel.Location = new Point(11, 326);
            _sampleMaxReLabel.Name = "_sampleMaxReLabel";
            _sampleMaxReLabel.Size = new Size(220, 24);
            _sampleMaxReLabel.TabIndex = 12;
            _sampleMaxReLabel.Text = "Sample Max Re";
            _sampleMaxReLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _sampleMaxRe
            // 
            _sampleMaxRe.DecimalPlaces = 4;
            _sampleMaxRe.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            _sampleMaxRe.Location = new Point(11, 353);
            _sampleMaxRe.Maximum = new decimal(new int[] { 4, 0, 0, 0 });
            _sampleMaxRe.Minimum = new decimal(new int[] { 4, 0, 0, int.MinValue });
            _sampleMaxRe.Name = "_sampleMaxRe";
            _sampleMaxRe.Size = new Size(220, 23);
            _sampleMaxRe.TabIndex = 13;
            _sampleMaxRe.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // _sampleMinImLabel
            // 
            _sampleMinImLabel.Location = new Point(11, 379);
            _sampleMinImLabel.Name = "_sampleMinImLabel";
            _sampleMinImLabel.Size = new Size(220, 24);
            _sampleMinImLabel.TabIndex = 14;
            _sampleMinImLabel.Text = "Sample Min Im";
            _sampleMinImLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _sampleMinIm
            // 
            _sampleMinIm.DecimalPlaces = 4;
            _sampleMinIm.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            _sampleMinIm.Location = new Point(11, 406);
            _sampleMinIm.Minimum = new decimal(new int[] { 4, 0, 0, int.MinValue });
            _sampleMinIm.Name = "_sampleMinIm";
            _sampleMinIm.Size = new Size(220, 23);
            _sampleMinIm.TabIndex = 15;
            _sampleMinIm.Value = -1.5m;
            // 
            // _sampleMaxImLabel
            // 
            _sampleMaxImLabel.Location = new Point(11, 432);
            _sampleMaxImLabel.Name = "_sampleMaxImLabel";
            _sampleMaxImLabel.Size = new Size(220, 24);
            _sampleMaxImLabel.TabIndex = 16;
            _sampleMaxImLabel.Text = "Sample Max Im";
            _sampleMaxImLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _sampleMaxIm
            // 
            _sampleMaxIm.DecimalPlaces = 4;
            _sampleMaxIm.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            _sampleMaxIm.Location = new Point(11, 459);
            _sampleMaxIm.Maximum = new decimal(new int[] { 4, 0, 0, 0 });
            _sampleMaxIm.Minimum = new decimal(new int[] { 4, 0, 0, int.MinValue });
            _sampleMaxIm.Name = "_sampleMaxIm";
            _sampleMaxIm.Size = new Size(220, 23);
            _sampleMaxIm.TabIndex = 17;
            _sampleMaxIm.Value = 1.5m;
            // 
            // _btnRender
            // 
            _btnRender.Location = new Point(11, 488);
            _btnRender.Name = "_btnRender";
            _btnRender.Size = new Size(220, 36);
            _btnRender.TabIndex = 18;
            _btnRender.Text = "Рендер";
            _btnRender.UseVisualStyleBackColor = true;
            _btnRender.Click += BtnRender_Click;
            // 
            // _btnSaveLoad
            // 
            _btnSaveLoad.Location = new Point(11, 530);
            _btnSaveLoad.Name = "_btnSaveLoad";
            _btnSaveLoad.Size = new Size(220, 36);
            _btnSaveLoad.TabIndex = 19;
            _btnSaveLoad.Text = "Сохранить / Загрузить";
            _btnSaveLoad.UseVisualStyleBackColor = true;
            _btnSaveLoad.Click += BtnSaveLoad_Click;
            // 
            // FractalBuddhabrotForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1304, 821);
            Controls.Add(_rootSplitContainer);
            KeyPreview = true;
            Name = "FractalBuddhabrotForm";
            Text = "Фрактал Buddhabrot";
            Load += FractalBuddhabrotForm_Load;
            FormClosing += FractalBuddhabrotForm_FormClosing;
            _rootSplitContainer.Panel1.ResumeLayout(false);
            _rootSplitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_rootSplitContainer).EndInit();
            _rootSplitContainer.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_canvas).EndInit();
            ((System.ComponentModel.ISupportInitialize)_samples).EndInit();
            ((System.ComponentModel.ISupportInitialize)_iterations).EndInit();
            ((System.ComponentModel.ISupportInitialize)_zoom).EndInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMinRe).EndInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMaxRe).EndInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMinIm).EndInit();
            ((System.ComponentModel.ISupportInitialize)_sampleMaxIm).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private SplitContainer _rootSplitContainer;
        private PictureBox _canvas;
        private FlowLayoutPanel _controlsPanel;
        private Label _modeLabel;
        private ComboBox _modeCombo;
        private Label _paletteLabel;
        private ComboBox _paletteCombo;
        private Label _samplesLabel;
        private NumericUpDown _samples;
        private Label _iterationsLabel;
        private NumericUpDown _iterations;
        private Label _zoomLabel;
        private NumericUpDown _zoom;
        private Label _sampleMinReLabel;
        private NumericUpDown _sampleMinRe;
        private Label _sampleMaxReLabel;
        private NumericUpDown _sampleMaxRe;
        private Label _sampleMinImLabel;
        private NumericUpDown _sampleMinIm;
        private Label _sampleMaxImLabel;
        private NumericUpDown _sampleMaxIm;
        private Button _btnRender;
        private Button _btnSaveLoad;
    }
}
