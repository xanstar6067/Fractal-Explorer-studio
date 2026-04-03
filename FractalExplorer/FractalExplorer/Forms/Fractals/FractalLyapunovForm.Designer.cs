namespace FractalExplorer.Forms.Fractals
{
    partial class FractalLyapunovForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        private Panel _canvasHost;
        private PictureBox _canvas;
        private Panel _controlsHost;
        private TableLayoutPanel _pnlControls;
        private Button _btnToggleControls;
        private NumericUpDown _nudAMin;
        private Label _lblAMin;
        private NumericUpDown _nudAMax;
        private Label _lblAMax;
        private NumericUpDown _nudBMin;
        private Label _lblBMin;
        private NumericUpDown _nudBMax;
        private Label _lblBMax;
        private TextBox _tbPattern;
        private Label _lblPattern;
        private NumericUpDown _nudIterations;
        private Label _lblIterations;
        private NumericUpDown _nudTransient;
        private Label _lblTransient;
        private NumericUpDown _nudThreads;
        private Label _lblThreads;
        private Button _btnSaveImage;
        private Button _btnPalette;
        private Button _btnRender;
        private Button _btnPresets;
        private Button _btnState;
        private NumericUpDown _nudZoom;
        private Label _lblZoom;
        private ComboBox _cbSSAA;
        private Label _lblSSAA;
        private ProgressBar _pbRenderProgress;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            _canvasHost = new Panel();
            _controlsHost = new Panel();
            _pnlControls = new TableLayoutPanel();
            _nudAMin = new NumericUpDown();
            _lblAMin = new Label();
            _nudAMax = new NumericUpDown();
            _lblAMax = new Label();
            _nudBMin = new NumericUpDown();
            _lblBMin = new Label();
            _nudBMax = new NumericUpDown();
            _lblBMax = new Label();
            _tbPattern = new TextBox();
            _lblPattern = new Label();
            _nudIterations = new NumericUpDown();
            _lblIterations = new Label();
            _nudTransient = new NumericUpDown();
            _lblTransient = new Label();
            _nudThreads = new NumericUpDown();
            _lblThreads = new Label();
            _nudZoom = new NumericUpDown();
            _lblZoom = new Label();
            _cbSSAA = new ComboBox();
            _lblSSAA = new Label();
            _btnSaveImage = new Button();
            _btnPalette = new Button();
            _btnRender = new Button();
            _btnPresets = new Button();
            _btnState = new Button();
            _pbRenderProgress = new ProgressBar();
            _btnToggleControls = new Button();
            _canvas = new PictureBox();
            _canvasHost.SuspendLayout();
            _controlsHost.SuspendLayout();
            _pnlControls.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_nudAMin).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudAMax).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudBMin).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudBMax).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudIterations).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudTransient).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudThreads).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudZoom).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_canvas).BeginInit();
            SuspendLayout();
            // 
            // _canvasHost
            // 
            _canvasHost.Controls.Add(_controlsHost);
            _canvasHost.Controls.Add(_btnToggleControls);
            _canvasHost.Controls.Add(_canvas);
            _canvasHost.Dock = DockStyle.Fill;
            _canvasHost.Location = new Point(0, 0);
            _canvasHost.Name = "_canvasHost";
            _canvasHost.Size = new Size(1086, 591);
            _canvasHost.TabIndex = 0;
            // 
            // _controlsHost
            // 
            _controlsHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            _controlsHost.BackColor = SystemColors.Control;
            _controlsHost.BorderStyle = BorderStyle.FixedSingle;
            _controlsHost.Controls.Add(_pnlControls);
            _controlsHost.Location = new Point(0, 0);
            _controlsHost.Name = "_controlsHost";
            _controlsHost.Size = new Size(231, 1232);
            _controlsHost.TabIndex = 0;
            // 
            // _pnlControls
            // 
            _pnlControls.ColumnCount = 2;
            _pnlControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            _pnlControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            _pnlControls.Controls.Add(_nudAMin, 0, 0);
            _pnlControls.Controls.Add(_lblAMin, 1, 0);
            _pnlControls.Controls.Add(_nudAMax, 0, 1);
            _pnlControls.Controls.Add(_lblAMax, 1, 1);
            _pnlControls.Controls.Add(_nudBMin, 0, 2);
            _pnlControls.Controls.Add(_lblBMin, 1, 2);
            _pnlControls.Controls.Add(_nudBMax, 0, 3);
            _pnlControls.Controls.Add(_lblBMax, 1, 3);
            _pnlControls.Controls.Add(_tbPattern, 0, 4);
            _pnlControls.Controls.Add(_lblPattern, 1, 4);
            _pnlControls.Controls.Add(_nudIterations, 0, 5);
            _pnlControls.Controls.Add(_lblIterations, 1, 5);
            _pnlControls.Controls.Add(_nudTransient, 0, 6);
            _pnlControls.Controls.Add(_lblTransient, 1, 6);
            _pnlControls.Controls.Add(_nudThreads, 0, 7);
            _pnlControls.Controls.Add(_lblThreads, 1, 7);
            _pnlControls.Controls.Add(_nudZoom, 0, 8);
            _pnlControls.Controls.Add(_lblZoom, 1, 8);
            _pnlControls.Controls.Add(_cbSSAA, 0, 9);
            _pnlControls.Controls.Add(_lblSSAA, 1, 9);
            _pnlControls.Controls.Add(_btnSaveImage, 0, 10);
            _pnlControls.Controls.Add(_btnPalette, 0, 11);
            _pnlControls.Controls.Add(_btnRender, 0, 12);
            _pnlControls.Controls.Add(_btnPresets, 0, 13);
            _pnlControls.Controls.Add(_btnState, 0, 14);
            _pnlControls.Controls.Add(_pbRenderProgress, 0, 15);
            _pnlControls.Dock = DockStyle.Fill;
            _pnlControls.Location = new Point(0, 0);
            _pnlControls.Name = "_pnlControls";
            _pnlControls.RowCount = 17;
            _pnlControls.RowStyles.Add(new RowStyle());
            _pnlControls.RowStyles.Add(new RowStyle());
            _pnlControls.RowStyles.Add(new RowStyle());
            _pnlControls.RowStyles.Add(new RowStyle());
            _pnlControls.RowStyles.Add(new RowStyle());
            _pnlControls.RowStyles.Add(new RowStyle());
            _pnlControls.RowStyles.Add(new RowStyle());
            _pnlControls.RowStyles.Add(new RowStyle());
            _pnlControls.RowStyles.Add(new RowStyle());
            _pnlControls.RowStyles.Add(new RowStyle());
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _pnlControls.Size = new Size(229, 1230);
            _pnlControls.TabIndex = 0;
            // 
            // _nudAMin
            // 
            _nudAMin.Dock = DockStyle.Fill;
            _nudAMin.Location = new Point(6, 6);
            _nudAMin.Margin = new Padding(6, 6, 3, 3);
            _nudAMin.Name = "_nudAMin";
            _nudAMin.Size = new Size(116, 23);
            _nudAMin.TabIndex = 0;
            // 
            // _lblAMin
            // 
            _lblAMin.AutoSize = true;
            _lblAMin.Dock = DockStyle.Fill;
            _lblAMin.Location = new Point(128, 0);
            _lblAMin.Name = "_lblAMin";
            _lblAMin.Size = new Size(98, 32);
            _lblAMin.TabIndex = 1;
            _lblAMin.Text = "A min";
            _lblAMin.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudAMax
            // 
            _nudAMax.Dock = DockStyle.Fill;
            _nudAMax.Location = new Point(6, 35);
            _nudAMax.Margin = new Padding(6, 3, 3, 3);
            _nudAMax.Name = "_nudAMax";
            _nudAMax.Size = new Size(116, 23);
            _nudAMax.TabIndex = 2;
            // 
            // _lblAMax
            // 
            _lblAMax.AutoSize = true;
            _lblAMax.Dock = DockStyle.Fill;
            _lblAMax.Location = new Point(128, 32);
            _lblAMax.Name = "_lblAMax";
            _lblAMax.Size = new Size(98, 29);
            _lblAMax.TabIndex = 3;
            _lblAMax.Text = "A max";
            _lblAMax.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudBMin
            // 
            _nudBMin.Dock = DockStyle.Fill;
            _nudBMin.Location = new Point(6, 64);
            _nudBMin.Margin = new Padding(6, 3, 3, 3);
            _nudBMin.Name = "_nudBMin";
            _nudBMin.Size = new Size(116, 23);
            _nudBMin.TabIndex = 4;
            // 
            // _lblBMin
            // 
            _lblBMin.AutoSize = true;
            _lblBMin.Dock = DockStyle.Fill;
            _lblBMin.Location = new Point(128, 61);
            _lblBMin.Name = "_lblBMin";
            _lblBMin.Size = new Size(98, 29);
            _lblBMin.TabIndex = 5;
            _lblBMin.Text = "B min";
            _lblBMin.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudBMax
            // 
            _nudBMax.Dock = DockStyle.Fill;
            _nudBMax.Location = new Point(6, 93);
            _nudBMax.Margin = new Padding(6, 3, 3, 3);
            _nudBMax.Name = "_nudBMax";
            _nudBMax.Size = new Size(116, 23);
            _nudBMax.TabIndex = 6;
            // 
            // _lblBMax
            // 
            _lblBMax.AutoSize = true;
            _lblBMax.Dock = DockStyle.Fill;
            _lblBMax.Location = new Point(128, 90);
            _lblBMax.Name = "_lblBMax";
            _lblBMax.Size = new Size(98, 29);
            _lblBMax.TabIndex = 7;
            _lblBMax.Text = "B max";
            _lblBMax.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _tbPattern
            // 
            _tbPattern.Dock = DockStyle.Fill;
            _tbPattern.Location = new Point(6, 122);
            _tbPattern.Margin = new Padding(6, 3, 3, 3);
            _tbPattern.Name = "_tbPattern";
            _tbPattern.Size = new Size(116, 23);
            _tbPattern.TabIndex = 8;
            // 
            // _lblPattern
            // 
            _lblPattern.AutoSize = true;
            _lblPattern.Dock = DockStyle.Fill;
            _lblPattern.Location = new Point(128, 119);
            _lblPattern.Name = "_lblPattern";
            _lblPattern.Size = new Size(98, 29);
            _lblPattern.TabIndex = 9;
            _lblPattern.Text = "Pattern (A/B)";
            _lblPattern.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudIterations
            // 
            _nudIterations.Dock = DockStyle.Fill;
            _nudIterations.Location = new Point(6, 151);
            _nudIterations.Margin = new Padding(6, 3, 3, 3);
            _nudIterations.Name = "_nudIterations";
            _nudIterations.Size = new Size(116, 23);
            _nudIterations.TabIndex = 10;
            // 
            // _lblIterations
            // 
            _lblIterations.AutoSize = true;
            _lblIterations.Dock = DockStyle.Fill;
            _lblIterations.Location = new Point(128, 148);
            _lblIterations.Name = "_lblIterations";
            _lblIterations.Size = new Size(98, 29);
            _lblIterations.TabIndex = 11;
            _lblIterations.Text = "Iterations";
            _lblIterations.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudTransient
            // 
            _nudTransient.Dock = DockStyle.Fill;
            _nudTransient.Location = new Point(6, 180);
            _nudTransient.Margin = new Padding(6, 3, 3, 3);
            _nudTransient.Name = "_nudTransient";
            _nudTransient.Size = new Size(116, 23);
            _nudTransient.TabIndex = 12;
            // 
            // _lblTransient
            // 
            _lblTransient.AutoSize = true;
            _lblTransient.Dock = DockStyle.Fill;
            _lblTransient.Location = new Point(128, 177);
            _lblTransient.Name = "_lblTransient";
            _lblTransient.Size = new Size(98, 29);
            _lblTransient.TabIndex = 13;
            _lblTransient.Text = "Transient";
            _lblTransient.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudThreads
            // 
            _nudThreads.Dock = DockStyle.Fill;
            _nudThreads.Location = new Point(6, 209);
            _nudThreads.Margin = new Padding(6, 3, 3, 3);
            _nudThreads.Name = "_nudThreads";
            _nudThreads.Size = new Size(116, 23);
            _nudThreads.TabIndex = 14;
            // 
            // _lblThreads
            // 
            _lblThreads.AutoSize = true;
            _lblThreads.Dock = DockStyle.Fill;
            _lblThreads.Location = new Point(128, 206);
            _lblThreads.Name = "_lblThreads";
            _lblThreads.Size = new Size(98, 29);
            _lblThreads.TabIndex = 15;
            _lblThreads.Text = "Threads";
            _lblThreads.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudZoom
            // 
            _nudZoom.Dock = DockStyle.Fill;
            _nudZoom.Location = new Point(6, 238);
            _nudZoom.Margin = new Padding(6, 3, 3, 3);
            _nudZoom.Name = "_nudZoom";
            _nudZoom.Size = new Size(116, 23);
            _nudZoom.TabIndex = 16;
            // 
            // _lblZoom
            // 
            _lblZoom.AutoSize = true;
            _lblZoom.Dock = DockStyle.Fill;
            _lblZoom.Location = new Point(128, 235);
            _lblZoom.Name = "_lblZoom";
            _lblZoom.Size = new Size(98, 29);
            _lblZoom.TabIndex = 17;
            _lblZoom.Text = "Масштаб";
            _lblZoom.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _cbSSAA
            // 
            _cbSSAA.Dock = DockStyle.Fill;
            _cbSSAA.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbSSAA.FormattingEnabled = true;
            _cbSSAA.Location = new Point(6, 267);
            _cbSSAA.Margin = new Padding(6, 3, 3, 3);
            _cbSSAA.Name = "_cbSSAA";
            _cbSSAA.Size = new Size(116, 23);
            _cbSSAA.TabIndex = 18;
            // 
            // _lblSSAA
            // 
            _lblSSAA.AutoSize = true;
            _lblSSAA.Dock = DockStyle.Fill;
            _lblSSAA.Location = new Point(128, 264);
            _lblSSAA.Name = "_lblSSAA";
            _lblSSAA.Size = new Size(98, 29);
            _lblSSAA.TabIndex = 19;
            _lblSSAA.Text = "Сглаживание";
            _lblSSAA.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _btnSaveImage
            // 
            _pnlControls.SetColumnSpan(_btnSaveImage, 2);
            _btnSaveImage.Dock = DockStyle.Fill;
            _btnSaveImage.Location = new Point(6, 296);
            _btnSaveImage.Margin = new Padding(6, 3, 6, 3);
            _btnSaveImage.Name = "_btnSaveImage";
            _btnSaveImage.Size = new Size(217, 39);
            _btnSaveImage.TabIndex = 20;
            _btnSaveImage.Text = "Сохранить изображение";
            _btnSaveImage.UseVisualStyleBackColor = true;
            _btnSaveImage.Click += btnSaveImage_Click;
            // 
            // _btnPalette
            // 
            _pnlControls.SetColumnSpan(_btnPalette, 2);
            _btnPalette.Dock = DockStyle.Fill;
            _btnPalette.Location = new Point(6, 341);
            _btnPalette.Margin = new Padding(6, 3, 6, 3);
            _btnPalette.Name = "_btnPalette";
            _btnPalette.Size = new Size(217, 39);
            _btnPalette.TabIndex = 21;
            _btnPalette.Text = "Настроить палитру";
            _btnPalette.UseVisualStyleBackColor = true;
            _btnPalette.Click += btnPalette_Click;
            // 
            // _btnRender
            // 
            _pnlControls.SetColumnSpan(_btnRender, 2);
            _btnRender.Dock = DockStyle.Fill;
            _btnRender.Location = new Point(6, 386);
            _btnRender.Margin = new Padding(6, 3, 6, 3);
            _btnRender.Name = "_btnRender";
            _btnRender.Size = new Size(217, 39);
            _btnRender.TabIndex = 22;
            _btnRender.Text = "Запустить рендер";
            _btnRender.UseVisualStyleBackColor = true;
            _btnRender.Click += btnRender_Click;
            // 
            // _btnPresets
            // 
            _pnlControls.SetColumnSpan(_btnPresets, 2);
            _btnPresets.Dock = DockStyle.Fill;
            _btnPresets.Location = new Point(6, 431);
            _btnPresets.Margin = new Padding(6, 3, 6, 3);
            _btnPresets.Name = "_btnPresets";
            _btnPresets.Size = new Size(217, 39);
            _btnPresets.TabIndex = 23;
            _btnPresets.Text = "Пресеты";
            _btnPresets.UseVisualStyleBackColor = true;
            _btnPresets.Click += btnPresets_Click;
            // 
            // _btnState
            // 
            _pnlControls.SetColumnSpan(_btnState, 2);
            _btnState.Dock = DockStyle.Fill;
            _btnState.Location = new Point(6, 476);
            _btnState.Margin = new Padding(6, 3, 6, 3);
            _btnState.Name = "_btnState";
            _btnState.Size = new Size(217, 39);
            _btnState.TabIndex = 24;
            _btnState.Text = "Менеджер сохранений";
            _btnState.UseVisualStyleBackColor = true;
            _btnState.Click += btnState_Click;
            // 
            // _pbRenderProgress
            // 
            _pnlControls.SetColumnSpan(_pbRenderProgress, 2);
            _pbRenderProgress.Dock = DockStyle.Fill;
            _pbRenderProgress.Location = new Point(6, 521);
            _pbRenderProgress.Margin = new Padding(6, 3, 6, 3);
            _pbRenderProgress.Name = "_pbRenderProgress";
            _pbRenderProgress.Size = new Size(217, 22);
            _pbRenderProgress.Style = ProgressBarStyle.Continuous;
            _pbRenderProgress.TabIndex = 25;
            // 
            // _btnToggleControls
            // 
            _btnToggleControls.AutoSize = true;
            _btnToggleControls.BackColor = Color.FromArgb(235, 32, 32, 32);
            _btnToggleControls.FlatStyle = FlatStyle.Popup;
            _btnToggleControls.ForeColor = Color.White;
            _btnToggleControls.Location = new Point(256, 12);
            _btnToggleControls.Name = "_btnToggleControls";
            _btnToggleControls.Size = new Size(44, 32);
            _btnToggleControls.TabIndex = 1;
            _btnToggleControls.Text = "✕";
            _btnToggleControls.UseVisualStyleBackColor = true;
            _btnToggleControls.Click += btnToggleControls_Click;
            // 
            // _canvas
            // 
            _canvas.BackColor = Color.Black;
            _canvas.Dock = DockStyle.Fill;
            _canvas.Location = new Point(0, 0);
            _canvas.Name = "_canvas";
            _canvas.Size = new Size(1086, 591);
            _canvas.SizeMode = PictureBoxSizeMode.Zoom;
            _canvas.TabIndex = 2;
            _canvas.TabStop = false;
            // 
            // FractalLyapunovForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1086, 591);
            Controls.Add(_canvasHost);
            MinimumSize = new Size(1102, 630);
            Name = "FractalLyapunovForm";
            Resize += FractalLyapunovForm_Resize;
            _canvasHost.ResumeLayout(false);
            _canvasHost.PerformLayout();
            _controlsHost.ResumeLayout(false);
            _pnlControls.ResumeLayout(false);
            _pnlControls.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_nudAMin).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudAMax).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudBMin).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudBMax).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudIterations).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudTransient).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudThreads).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudZoom).EndInit();
            ((System.ComponentModel.ISupportInitialize)_canvas).EndInit();
            ResumeLayout(false);
        }

        private void btnToggleControls_Click(object sender, EventArgs e)
        {
            ToggleControls();
        }

        private void FractalLyapunovForm_Resize(object sender, EventArgs e)
        {
            UpdateOverlayLayout();
        }
    }
}
