namespace FractalExplorer.Forms.Fractals
{
    partial class FractalBifurcationForm
    {
        private System.ComponentModel.IContainer components = null;

        private Panel _canvasHost;
        private PictureBox _canvas;
        private Panel _controlsHost;
        private TableLayoutPanel _pnlControls;
        private Button _btnToggleControls;

        private NumericUpDown _nudRMin;
        private Label _lblRMin;
        private NumericUpDown _nudRMax;
        private Label _lblRMax;
        private NumericUpDown _nudXMin;
        private Label _lblXMin;
        private NumericUpDown _nudXMax;
        private Label _lblXMax;
        private NumericUpDown _nudTransient;
        private Label _lblTransient;
        private NumericUpDown _nudSamplesPerR;
        private Label _lblSamplesPerR;
        private NumericUpDown _nudIterations;
        private Label _lblIterations;
        private NumericUpDown _nudZoom;
        private Label _lblZoom;
        private ComboBox _cbThreads;
        private Label _lblThreads;

        private Button _btnSaveImage;
        private Button _btnRender;
        private Button _btnReset;
        private Button _btnState;
        private Button _btnBackgroundColor;
        private Button _btnPointColor;
        private Label _lblProgress;
        private ProgressBar _pbRenderProgress;

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
            _nudRMin = new NumericUpDown();
            _lblRMin = new Label();
            _nudRMax = new NumericUpDown();
            _lblRMax = new Label();
            _nudXMin = new NumericUpDown();
            _lblXMin = new Label();
            _nudXMax = new NumericUpDown();
            _lblXMax = new Label();
            _nudTransient = new NumericUpDown();
            _lblTransient = new Label();
            _nudSamplesPerR = new NumericUpDown();
            _lblSamplesPerR = new Label();
            _nudIterations = new NumericUpDown();
            _lblIterations = new Label();
            _nudZoom = new NumericUpDown();
            _lblZoom = new Label();
            _cbThreads = new ComboBox();
            _lblThreads = new Label();
            _btnSaveImage = new Button();
            _btnRender = new Button();
            _btnReset = new Button();
            _btnState = new Button();
            _btnBackgroundColor = new Button();
            _btnPointColor = new Button();
            _lblProgress = new Label();
            _pbRenderProgress = new ProgressBar();
            _btnToggleControls = new Button();
            _canvas = new PictureBox();
            _canvasHost.SuspendLayout();
            _controlsHost.SuspendLayout();
            _pnlControls.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_nudRMin).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudRMax).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudXMin).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudXMax).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudTransient).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudSamplesPerR).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudIterations).BeginInit();
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
            _canvasHost.Size = new Size(1086, 644);
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
            _controlsHost.Size = new Size(231, 644);
            _controlsHost.TabIndex = 0;
            // 
            // _pnlControls
            // 
            _pnlControls.ColumnCount = 2;
            _pnlControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            _pnlControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            _pnlControls.Controls.Add(_nudRMin, 0, 0);
            _pnlControls.Controls.Add(_lblRMin, 1, 0);
            _pnlControls.Controls.Add(_nudRMax, 0, 1);
            _pnlControls.Controls.Add(_lblRMax, 1, 1);
            _pnlControls.Controls.Add(_nudXMin, 0, 2);
            _pnlControls.Controls.Add(_lblXMin, 1, 2);
            _pnlControls.Controls.Add(_nudXMax, 0, 3);
            _pnlControls.Controls.Add(_lblXMax, 1, 3);
            _pnlControls.Controls.Add(_nudTransient, 0, 4);
            _pnlControls.Controls.Add(_lblTransient, 1, 4);
            _pnlControls.Controls.Add(_nudSamplesPerR, 0, 5);
            _pnlControls.Controls.Add(_lblSamplesPerR, 1, 5);
            _pnlControls.Controls.Add(_nudIterations, 0, 6);
            _pnlControls.Controls.Add(_lblIterations, 1, 6);
            _pnlControls.Controls.Add(_nudZoom, 0, 7);
            _pnlControls.Controls.Add(_lblZoom, 1, 7);
            _pnlControls.Controls.Add(_cbThreads, 0, 8);
            _pnlControls.Controls.Add(_lblThreads, 1, 8);
            _pnlControls.Controls.Add(_btnSaveImage, 0, 9);
            _pnlControls.Controls.Add(_btnRender, 0, 10);
            _pnlControls.Controls.Add(_btnReset, 0, 11);
            _pnlControls.Controls.Add(_btnState, 0, 12);
            _pnlControls.Controls.Add(_btnBackgroundColor, 0, 13);
            _pnlControls.Controls.Add(_btnPointColor, 0, 14);
            _pnlControls.Controls.Add(_lblProgress, 0, 15);
            _pnlControls.Controls.Add(_pbRenderProgress, 0, 16);
            _pnlControls.Dock = DockStyle.Fill;
            _pnlControls.Location = new Point(0, 0);
            _pnlControls.Name = "_pnlControls";
            _pnlControls.RowCount = 18;
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
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _pnlControls.Size = new Size(229, 642);
            _pnlControls.TabIndex = 0;
            // 
            // _nudRMin
            // 
            _nudRMin.Dock = DockStyle.Fill;
            _nudRMin.Location = new Point(6, 6);
            _nudRMin.Margin = new Padding(6, 6, 3, 3);
            _nudRMin.Name = "_nudRMin";
            _nudRMin.Size = new Size(116, 23);
            _nudRMin.TabIndex = 0;
            // 
            // _lblRMin
            // 
            _lblRMin.AutoSize = true;
            _lblRMin.Dock = DockStyle.Fill;
            _lblRMin.Location = new Point(128, 0);
            _lblRMin.Name = "_lblRMin";
            _lblRMin.Size = new Size(98, 32);
            _lblRMin.TabIndex = 1;
            _lblRMin.Text = "r min";
            _lblRMin.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudRMax
            // 
            _nudRMax.Dock = DockStyle.Fill;
            _nudRMax.Location = new Point(6, 35);
            _nudRMax.Margin = new Padding(6, 3, 3, 3);
            _nudRMax.Name = "_nudRMax";
            _nudRMax.Size = new Size(116, 23);
            _nudRMax.TabIndex = 2;
            // 
            // _lblRMax
            // 
            _lblRMax.AutoSize = true;
            _lblRMax.Dock = DockStyle.Fill;
            _lblRMax.Location = new Point(128, 32);
            _lblRMax.Name = "_lblRMax";
            _lblRMax.Size = new Size(98, 29);
            _lblRMax.TabIndex = 3;
            _lblRMax.Text = "r max";
            _lblRMax.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudXMin
            // 
            _nudXMin.Dock = DockStyle.Fill;
            _nudXMin.Location = new Point(6, 64);
            _nudXMin.Margin = new Padding(6, 3, 3, 3);
            _nudXMin.Name = "_nudXMin";
            _nudXMin.Size = new Size(116, 23);
            _nudXMin.TabIndex = 4;
            // 
            // _lblXMin
            // 
            _lblXMin.AutoSize = true;
            _lblXMin.Dock = DockStyle.Fill;
            _lblXMin.Location = new Point(128, 61);
            _lblXMin.Name = "_lblXMin";
            _lblXMin.Size = new Size(98, 29);
            _lblXMin.TabIndex = 5;
            _lblXMin.Text = "x min";
            _lblXMin.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudXMax
            // 
            _nudXMax.Dock = DockStyle.Fill;
            _nudXMax.Location = new Point(6, 93);
            _nudXMax.Margin = new Padding(6, 3, 3, 3);
            _nudXMax.Name = "_nudXMax";
            _nudXMax.Size = new Size(116, 23);
            _nudXMax.TabIndex = 6;
            // 
            // _lblXMax
            // 
            _lblXMax.AutoSize = true;
            _lblXMax.Dock = DockStyle.Fill;
            _lblXMax.Location = new Point(128, 90);
            _lblXMax.Name = "_lblXMax";
            _lblXMax.Size = new Size(98, 29);
            _lblXMax.TabIndex = 7;
            _lblXMax.Text = "x max";
            _lblXMax.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudTransient
            // 
            _nudTransient.Dock = DockStyle.Fill;
            _nudTransient.Location = new Point(6, 122);
            _nudTransient.Margin = new Padding(6, 3, 3, 3);
            _nudTransient.Name = "_nudTransient";
            _nudTransient.Size = new Size(116, 23);
            _nudTransient.TabIndex = 8;
            // 
            // _lblTransient
            // 
            _lblTransient.AutoSize = true;
            _lblTransient.Dock = DockStyle.Fill;
            _lblTransient.Location = new Point(128, 119);
            _lblTransient.Name = "_lblTransient";
            _lblTransient.Size = new Size(98, 29);
            _lblTransient.TabIndex = 9;
            _lblTransient.Text = "Transient";
            _lblTransient.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudSamplesPerR
            // 
            _nudSamplesPerR.Dock = DockStyle.Fill;
            _nudSamplesPerR.Location = new Point(6, 151);
            _nudSamplesPerR.Margin = new Padding(6, 3, 3, 3);
            _nudSamplesPerR.Name = "_nudSamplesPerR";
            _nudSamplesPerR.Size = new Size(116, 23);
            _nudSamplesPerR.TabIndex = 10;
            // 
            // _lblSamplesPerR
            // 
            _lblSamplesPerR.AutoSize = true;
            _lblSamplesPerR.Dock = DockStyle.Fill;
            _lblSamplesPerR.Location = new Point(128, 148);
            _lblSamplesPerR.Name = "_lblSamplesPerR";
            _lblSamplesPerR.Size = new Size(98, 29);
            _lblSamplesPerR.TabIndex = 11;
            _lblSamplesPerR.Text = "Samples / r";
            _lblSamplesPerR.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudIterations
            // 
            _nudIterations.Dock = DockStyle.Fill;
            _nudIterations.Location = new Point(6, 180);
            _nudIterations.Margin = new Padding(6, 3, 3, 3);
            _nudIterations.Name = "_nudIterations";
            _nudIterations.Size = new Size(116, 23);
            _nudIterations.TabIndex = 12;
            // 
            // _lblIterations
            // 
            _lblIterations.AutoSize = true;
            _lblIterations.Dock = DockStyle.Fill;
            _lblIterations.Location = new Point(128, 177);
            _lblIterations.Name = "_lblIterations";
            _lblIterations.Size = new Size(98, 29);
            _lblIterations.TabIndex = 13;
            _lblIterations.Text = "Итерации";
            _lblIterations.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudZoom
            // 
            _nudZoom.Dock = DockStyle.Fill;
            _nudZoom.Location = new Point(6, 209);
            _nudZoom.Margin = new Padding(6, 3, 3, 3);
            _nudZoom.Name = "_nudZoom";
            _nudZoom.Size = new Size(116, 23);
            _nudZoom.TabIndex = 14;
            // 
            // _lblZoom
            // 
            _lblZoom.AutoSize = true;
            _lblZoom.Dock = DockStyle.Fill;
            _lblZoom.Location = new Point(128, 206);
            _lblZoom.Name = "_lblZoom";
            _lblZoom.Size = new Size(98, 29);
            _lblZoom.TabIndex = 15;
            _lblZoom.Text = "Zoom";
            _lblZoom.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _cbThreads
            // 
            _cbThreads.Dock = DockStyle.Fill;
            _cbThreads.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbThreads.FormattingEnabled = true;
            _cbThreads.Location = new Point(6, 238);
            _cbThreads.Margin = new Padding(6, 3, 3, 3);
            _cbThreads.Name = "_cbThreads";
            _cbThreads.Size = new Size(116, 23);
            _cbThreads.TabIndex = 16;
            // 
            // _lblThreads
            // 
            _lblThreads.AutoSize = true;
            _lblThreads.Dock = DockStyle.Fill;
            _lblThreads.Location = new Point(128, 235);
            _lblThreads.Name = "_lblThreads";
            _lblThreads.Size = new Size(98, 29);
            _lblThreads.TabIndex = 17;
            _lblThreads.Text = "Потоки";
            _lblThreads.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _btnSaveImage
            // 
            _pnlControls.SetColumnSpan(_btnSaveImage, 2);
            _btnSaveImage.Dock = DockStyle.Fill;
            _btnSaveImage.Location = new Point(6, 267);
            _btnSaveImage.Margin = new Padding(6, 3, 6, 3);
            _btnSaveImage.Name = "_btnSaveImage";
            _btnSaveImage.Size = new Size(217, 39);
            _btnSaveImage.TabIndex = 18;
            _btnSaveImage.Text = "Сохранить изображение";
            _btnSaveImage.UseVisualStyleBackColor = true;
            _btnSaveImage.Click += btnSaveImage_Click;
            // 
            // _btnRender
            // 
            _pnlControls.SetColumnSpan(_btnRender, 2);
            _btnRender.Dock = DockStyle.Fill;
            _btnRender.Location = new Point(6, 312);
            _btnRender.Margin = new Padding(6, 3, 6, 3);
            _btnRender.Name = "_btnRender";
            _btnRender.Size = new Size(217, 39);
            _btnRender.TabIndex = 19;
            _btnRender.Text = "Рендер";
            _btnRender.UseVisualStyleBackColor = true;
            _btnRender.Click += btnRender_Click;
            // 
            // _btnReset
            // 
            _pnlControls.SetColumnSpan(_btnReset, 2);
            _btnReset.Dock = DockStyle.Fill;
            _btnReset.Location = new Point(6, 357);
            _btnReset.Margin = new Padding(6, 3, 6, 3);
            _btnReset.Name = "_btnReset";
            _btnReset.Size = new Size(217, 39);
            _btnReset.TabIndex = 20;
            _btnReset.Text = "Сброс вида";
            _btnReset.UseVisualStyleBackColor = true;
            _btnReset.Click += btnReset_Click;
            // 
            // _btnState
            // 
            _pnlControls.SetColumnSpan(_btnState, 2);
            _btnState.Dock = DockStyle.Fill;
            _btnState.Location = new Point(6, 402);
            _btnState.Margin = new Padding(6, 3, 6, 3);
            _btnState.Name = "_btnState";
            _btnState.Size = new Size(217, 39);
            _btnState.TabIndex = 21;
            _btnState.Text = "Менеджер сохранений";
            _btnState.UseVisualStyleBackColor = true;
            _btnState.Click += btnState_Click;
            // 
            // _btnBackgroundColor
            // 
            _pnlControls.SetColumnSpan(_btnBackgroundColor, 2);
            _btnBackgroundColor.Dock = DockStyle.Fill;
            _btnBackgroundColor.Location = new Point(6, 447);
            _btnBackgroundColor.Margin = new Padding(6, 3, 6, 3);
            _btnBackgroundColor.Name = "_btnBackgroundColor";
            _btnBackgroundColor.Size = new Size(217, 39);
            _btnBackgroundColor.TabIndex = 22;
            _btnBackgroundColor.Text = "Цвет фона";
            _btnBackgroundColor.UseVisualStyleBackColor = true;
            _btnBackgroundColor.Click += btnBackgroundColor_Click;
            // 
            // _btnPointColor
            // 
            _pnlControls.SetColumnSpan(_btnPointColor, 2);
            _btnPointColor.Dock = DockStyle.Fill;
            _btnPointColor.Location = new Point(6, 492);
            _btnPointColor.Margin = new Padding(6, 3, 6, 3);
            _btnPointColor.Name = "_btnPointColor";
            _btnPointColor.Size = new Size(217, 39);
            _btnPointColor.TabIndex = 23;
            _btnPointColor.Text = "Цвет фрактала";
            _btnPointColor.UseVisualStyleBackColor = true;
            _btnPointColor.Click += btnPointColor_Click;
            // 
            // _lblProgress
            // 
            _pnlControls.SetColumnSpan(_lblProgress, 2);
            _lblProgress.Dock = DockStyle.Fill;
            _lblProgress.Location = new Point(6, 534);
            _lblProgress.Margin = new Padding(6, 0, 6, 0);
            _lblProgress.Name = "_lblProgress";
            _lblProgress.Size = new Size(217, 20);
            _lblProgress.TabIndex = 24;
            _lblProgress.Text = "Статус рендера";
            _lblProgress.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _pbRenderProgress
            // 
            _pnlControls.SetColumnSpan(_pbRenderProgress, 2);
            _pbRenderProgress.Dock = DockStyle.Fill;
            _pbRenderProgress.Location = new Point(6, 557);
            _pbRenderProgress.Margin = new Padding(6, 3, 6, 3);
            _pbRenderProgress.Name = "_pbRenderProgress";
            _pbRenderProgress.Size = new Size(217, 24);
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
            _btnToggleControls.UseVisualStyleBackColor = false;
            _btnToggleControls.Click += btnToggleControls_Click;
            // 
            // _canvas
            // 
            _canvas.BackColor = Color.Transparent;
            _canvas.Dock = DockStyle.Fill;
            _canvas.Location = new Point(0, 0);
            _canvas.Name = "_canvas";
            _canvas.Size = new Size(1086, 644);
            _canvas.SizeMode = PictureBoxSizeMode.StretchImage;
            _canvas.TabIndex = 2;
            _canvas.TabStop = false;
            // 
            // FractalBifurcationForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1086, 644);
            Controls.Add(_canvasHost);
            KeyPreview = true;
            MinimumSize = new Size(900, 560);
            Name = "FractalBifurcationForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Диаграмма бифуркации";
            _canvasHost.ResumeLayout(false);
            _canvasHost.PerformLayout();
            _controlsHost.ResumeLayout(false);
            _pnlControls.ResumeLayout(false);
            _pnlControls.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_nudRMin).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudRMax).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudXMin).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudXMax).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudTransient).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudSamplesPerR).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudIterations).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudZoom).EndInit();
            ((System.ComponentModel.ISupportInitialize)_canvas).EndInit();
            ResumeLayout(false);
        }
    }
}
