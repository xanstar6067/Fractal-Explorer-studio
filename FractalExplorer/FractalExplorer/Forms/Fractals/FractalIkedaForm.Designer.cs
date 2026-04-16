namespace FractalExplorer.Forms.Fractals
{
    partial class FractalIkedaForm
    {
        private System.ComponentModel.IContainer components = null;

        private Panel _canvasHost;
        private PictureBox _canvas;
        private Panel _controlsHost;
        private TableLayoutPanel _pnlControls;
        private Button _btnToggleControls;

        private NumericUpDown _nudU;
        private Label _lblU;
        private NumericUpDown _nudX0;
        private Label _lblX0;
        private NumericUpDown _nudY0;
        private Label _lblY0;
        private NumericUpDown _nudIterations;
        private Label _lblIterations;
        private NumericUpDown _nudDiscard;
        private Label _lblDiscard;
        private NumericUpDown _nudRangeXMin;
        private Label _lblRangeXMin;
        private NumericUpDown _nudRangeXMax;
        private Label _lblRangeXMax;
        private NumericUpDown _nudRangeYMin;
        private Label _lblRangeYMin;
        private NumericUpDown _nudRangeYMax;
        private Label _lblRangeYMax;
        private NumericUpDown _nudZoom;
        private Label _lblZoom;
        private ComboBox _cbThreads;
        private Label _lblThreads;
        private Button _btnSaveImage;
        private Button _btnRender;
        private Button _btnReset;
        private Button _btnState;
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
            _nudU = new NumericUpDown();
            _lblU = new Label();
            _nudX0 = new NumericUpDown();
            _lblX0 = new Label();
            _nudY0 = new NumericUpDown();
            _lblY0 = new Label();
            _nudIterations = new NumericUpDown();
            _lblIterations = new Label();
            _nudDiscard = new NumericUpDown();
            _lblDiscard = new Label();
            _nudRangeXMin = new NumericUpDown();
            _lblRangeXMin = new Label();
            _nudRangeXMax = new NumericUpDown();
            _lblRangeXMax = new Label();
            _nudRangeYMin = new NumericUpDown();
            _lblRangeYMin = new Label();
            _nudRangeYMax = new NumericUpDown();
            _lblRangeYMax = new Label();
            _nudZoom = new NumericUpDown();
            _lblZoom = new Label();
            _cbThreads = new ComboBox();
            _lblThreads = new Label();
            _btnSaveImage = new Button();
            _btnRender = new Button();
            _btnReset = new Button();
            _btnState = new Button();
            _lblProgress = new Label();
            _pbRenderProgress = new ProgressBar();
            _btnToggleControls = new Button();
            _canvas = new PictureBox();
            _canvasHost.SuspendLayout();
            _controlsHost.SuspendLayout();
            _pnlControls.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_nudU).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudX0).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudY0).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudIterations).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudDiscard).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudRangeXMin).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudRangeXMax).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudRangeYMin).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudRangeYMax).BeginInit();
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
            _controlsHost.Size = new Size(231, 1285);
            _controlsHost.TabIndex = 0;
            // 
            // _pnlControls
            // 
            _pnlControls.ColumnCount = 2;
            _pnlControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            _pnlControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            _pnlControls.Controls.Add(_nudU, 0, 0);
            _pnlControls.Controls.Add(_lblU, 1, 0);
            _pnlControls.Controls.Add(_nudX0, 0, 1);
            _pnlControls.Controls.Add(_lblX0, 1, 1);
            _pnlControls.Controls.Add(_nudY0, 0, 2);
            _pnlControls.Controls.Add(_lblY0, 1, 2);
            _pnlControls.Controls.Add(_nudIterations, 0, 3);
            _pnlControls.Controls.Add(_lblIterations, 1, 3);
            _pnlControls.Controls.Add(_nudDiscard, 0, 4);
            _pnlControls.Controls.Add(_lblDiscard, 1, 4);
            _pnlControls.Controls.Add(_nudRangeXMin, 0, 5);
            _pnlControls.Controls.Add(_lblRangeXMin, 1, 5);
            _pnlControls.Controls.Add(_nudRangeXMax, 0, 6);
            _pnlControls.Controls.Add(_lblRangeXMax, 1, 6);
            _pnlControls.Controls.Add(_nudRangeYMin, 0, 7);
            _pnlControls.Controls.Add(_lblRangeYMin, 1, 7);
            _pnlControls.Controls.Add(_nudRangeYMax, 0, 8);
            _pnlControls.Controls.Add(_lblRangeYMax, 1, 8);
            _pnlControls.Controls.Add(_nudZoom, 0, 9);
            _pnlControls.Controls.Add(_lblZoom, 1, 9);
            _pnlControls.Controls.Add(_cbThreads, 0, 10);
            _pnlControls.Controls.Add(_lblThreads, 1, 10);
            _pnlControls.Controls.Add(_btnSaveImage, 0, 11);
            _pnlControls.Controls.Add(_btnRender, 0, 12);
            _pnlControls.Controls.Add(_btnReset, 0, 13);
            _pnlControls.Controls.Add(_btnState, 0, 14);
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
            _pnlControls.RowStyles.Add(new RowStyle());
            _pnlControls.RowStyles.Add(new RowStyle());
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _pnlControls.Size = new Size(229, 1283);
            _pnlControls.TabIndex = 0;
            // 
            // _nudU
            // 
            _nudU.Dock = DockStyle.Fill;
            _nudU.Location = new Point(6, 6);
            _nudU.Margin = new Padding(6, 6, 3, 3);
            _nudU.Name = "_nudU";
            _nudU.Size = new Size(116, 23);
            _nudU.TabIndex = 0;
            // 
            // _lblU
            // 
            _lblU.AutoSize = true;
            _lblU.Dock = DockStyle.Fill;
            _lblU.Location = new Point(128, 0);
            _lblU.Name = "_lblU";
            _lblU.Size = new Size(98, 32);
            _lblU.TabIndex = 1;
            _lblU.Text = "Параметр u";
            _lblU.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudX0
            // 
            _nudX0.Dock = DockStyle.Fill;
            _nudX0.Location = new Point(6, 35);
            _nudX0.Margin = new Padding(6, 3, 3, 3);
            _nudX0.Name = "_nudX0";
            _nudX0.Size = new Size(116, 23);
            _nudX0.TabIndex = 2;
            // 
            // _lblX0
            // 
            _lblX0.AutoSize = true;
            _lblX0.Dock = DockStyle.Fill;
            _lblX0.Location = new Point(128, 32);
            _lblX0.Name = "_lblX0";
            _lblX0.Size = new Size(98, 29);
            _lblX0.TabIndex = 3;
            _lblX0.Text = "Нач. x0";
            _lblX0.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudY0
            // 
            _nudY0.Dock = DockStyle.Fill;
            _nudY0.Location = new Point(6, 64);
            _nudY0.Margin = new Padding(6, 3, 3, 3);
            _nudY0.Name = "_nudY0";
            _nudY0.Size = new Size(116, 23);
            _nudY0.TabIndex = 4;
            // 
            // _lblY0
            // 
            _lblY0.AutoSize = true;
            _lblY0.Dock = DockStyle.Fill;
            _lblY0.Location = new Point(128, 61);
            _lblY0.Name = "_lblY0";
            _lblY0.Size = new Size(98, 29);
            _lblY0.TabIndex = 5;
            _lblY0.Text = "Нач. y0";
            _lblY0.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudIterations
            // 
            _nudIterations.Dock = DockStyle.Fill;
            _nudIterations.Location = new Point(6, 93);
            _nudIterations.Margin = new Padding(6, 3, 3, 3);
            _nudIterations.Name = "_nudIterations";
            _nudIterations.Size = new Size(116, 23);
            _nudIterations.TabIndex = 6;
            // 
            // _lblIterations
            // 
            _lblIterations.AutoSize = true;
            _lblIterations.Dock = DockStyle.Fill;
            _lblIterations.Location = new Point(128, 90);
            _lblIterations.Name = "_lblIterations";
            _lblIterations.Size = new Size(98, 29);
            _lblIterations.TabIndex = 7;
            _lblIterations.Text = "Итерации";
            _lblIterations.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudDiscard
            // 
            _nudDiscard.Dock = DockStyle.Fill;
            _nudDiscard.Location = new Point(6, 122);
            _nudDiscard.Margin = new Padding(6, 3, 3, 3);
            _nudDiscard.Name = "_nudDiscard";
            _nudDiscard.Size = new Size(116, 23);
            _nudDiscard.TabIndex = 8;
            // 
            // _lblDiscard
            // 
            _lblDiscard.AutoSize = true;
            _lblDiscard.Dock = DockStyle.Fill;
            _lblDiscard.Location = new Point(128, 119);
            _lblDiscard.Name = "_lblDiscard";
            _lblDiscard.Size = new Size(98, 29);
            _lblDiscard.TabIndex = 9;
            _lblDiscard.Text = "Пропуск";
            _lblDiscard.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudRangeXMin
            // 
            _nudRangeXMin.Dock = DockStyle.Fill;
            _nudRangeXMin.Location = new Point(6, 151);
            _nudRangeXMin.Margin = new Padding(6, 3, 3, 3);
            _nudRangeXMin.Name = "_nudRangeXMin";
            _nudRangeXMin.Size = new Size(116, 23);
            _nudRangeXMin.TabIndex = 10;
            // 
            // _lblRangeXMin
            // 
            _lblRangeXMin.AutoSize = true;
            _lblRangeXMin.Dock = DockStyle.Fill;
            _lblRangeXMin.Location = new Point(128, 148);
            _lblRangeXMin.Name = "_lblRangeXMin";
            _lblRangeXMin.Size = new Size(98, 29);
            _lblRangeXMin.TabIndex = 11;
            _lblRangeXMin.Text = "X min";
            _lblRangeXMin.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudRangeXMax
            // 
            _nudRangeXMax.Dock = DockStyle.Fill;
            _nudRangeXMax.Location = new Point(6, 180);
            _nudRangeXMax.Margin = new Padding(6, 3, 3, 3);
            _nudRangeXMax.Name = "_nudRangeXMax";
            _nudRangeXMax.Size = new Size(116, 23);
            _nudRangeXMax.TabIndex = 12;
            // 
            // _lblRangeXMax
            // 
            _lblRangeXMax.AutoSize = true;
            _lblRangeXMax.Dock = DockStyle.Fill;
            _lblRangeXMax.Location = new Point(128, 177);
            _lblRangeXMax.Name = "_lblRangeXMax";
            _lblRangeXMax.Size = new Size(98, 29);
            _lblRangeXMax.TabIndex = 13;
            _lblRangeXMax.Text = "X max";
            _lblRangeXMax.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudRangeYMin
            // 
            _nudRangeYMin.Dock = DockStyle.Fill;
            _nudRangeYMin.Location = new Point(6, 209);
            _nudRangeYMin.Margin = new Padding(6, 3, 3, 3);
            _nudRangeYMin.Name = "_nudRangeYMin";
            _nudRangeYMin.Size = new Size(116, 23);
            _nudRangeYMin.TabIndex = 14;
            // 
            // _lblRangeYMin
            // 
            _lblRangeYMin.AutoSize = true;
            _lblRangeYMin.Dock = DockStyle.Fill;
            _lblRangeYMin.Location = new Point(128, 206);
            _lblRangeYMin.Name = "_lblRangeYMin";
            _lblRangeYMin.Size = new Size(98, 29);
            _lblRangeYMin.TabIndex = 15;
            _lblRangeYMin.Text = "Y min";
            _lblRangeYMin.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudRangeYMax
            // 
            _nudRangeYMax.Dock = DockStyle.Fill;
            _nudRangeYMax.Location = new Point(6, 238);
            _nudRangeYMax.Margin = new Padding(6, 3, 3, 3);
            _nudRangeYMax.Name = "_nudRangeYMax";
            _nudRangeYMax.Size = new Size(116, 23);
            _nudRangeYMax.TabIndex = 16;
            // 
            // _lblRangeYMax
            // 
            _lblRangeYMax.AutoSize = true;
            _lblRangeYMax.Dock = DockStyle.Fill;
            _lblRangeYMax.Location = new Point(128, 235);
            _lblRangeYMax.Name = "_lblRangeYMax";
            _lblRangeYMax.Size = new Size(98, 29);
            _lblRangeYMax.TabIndex = 17;
            _lblRangeYMax.Text = "Y max";
            _lblRangeYMax.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudZoom
            // 
            _nudZoom.Dock = DockStyle.Fill;
            _nudZoom.Location = new Point(6, 267);
            _nudZoom.Margin = new Padding(6, 3, 3, 3);
            _nudZoom.Name = "_nudZoom";
            _nudZoom.Size = new Size(116, 23);
            _nudZoom.TabIndex = 18;
            // 
            // _lblZoom
            // 
            _lblZoom.AutoSize = true;
            _lblZoom.Dock = DockStyle.Fill;
            _lblZoom.Location = new Point(128, 264);
            _lblZoom.Name = "_lblZoom";
            _lblZoom.Size = new Size(98, 29);
            _lblZoom.TabIndex = 19;
            _lblZoom.Text = "Zoom";
            _lblZoom.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _cbThreads
            // 
            _cbThreads.Dock = DockStyle.Fill;
            _cbThreads.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbThreads.FormattingEnabled = true;
            _cbThreads.Location = new Point(6, 296);
            _cbThreads.Margin = new Padding(6, 3, 3, 3);
            _cbThreads.Name = "_cbThreads";
            _cbThreads.Size = new Size(116, 23);
            _cbThreads.TabIndex = 20;
            // 
            // _lblThreads
            // 
            _lblThreads.AutoSize = true;
            _lblThreads.Dock = DockStyle.Fill;
            _lblThreads.Location = new Point(128, 293);
            _lblThreads.Name = "_lblThreads";
            _lblThreads.Size = new Size(98, 29);
            _lblThreads.TabIndex = 21;
            _lblThreads.Text = "Потоки";
            _lblThreads.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _btnSaveImage
            // 
            _btnSaveImage.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _pnlControls.SetColumnSpan(_btnSaveImage, 2);
            _btnSaveImage.Location = new Point(6, 332);
            _btnSaveImage.Margin = new Padding(6, 10, 6, 6);
            _btnSaveImage.Name = "_btnSaveImage";
            _btnSaveImage.Size = new Size(217, 32);
            _btnSaveImage.TabIndex = 22;
            _btnSaveImage.Text = "Сохранить изображение";
            _btnSaveImage.UseVisualStyleBackColor = true;
            _btnSaveImage.Click += btnSaveImage_Click;
            // 
            // _btnRender
            // 
            _btnRender.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _pnlControls.SetColumnSpan(_btnRender, 2);
            _btnRender.Location = new Point(6, 377);
            _btnRender.Margin = new Padding(6, 6, 6, 6);
            _btnRender.Name = "_btnRender";
            _btnRender.Size = new Size(217, 32);
            _btnRender.TabIndex = 23;
            _btnRender.Text = "Рендер";
            _btnRender.UseVisualStyleBackColor = true;
            _btnRender.Click += btnRender_Click;
            // 
            // _btnReset
            // 
            _btnReset.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _pnlControls.SetColumnSpan(_btnReset, 2);
            _btnReset.Location = new Point(6, 422);
            _btnReset.Margin = new Padding(6, 6, 6, 6);
            _btnReset.Name = "_btnReset";
            _btnReset.Size = new Size(217, 32);
            _btnReset.TabIndex = 24;
            _btnReset.Text = "Сбросить вид";
            _btnReset.UseVisualStyleBackColor = true;
            _btnReset.Click += btnReset_Click;
            // 
            // _btnState
            // 
            _btnState.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _pnlControls.SetColumnSpan(_btnState, 2);
            _btnState.Location = new Point(6, 467);
            _btnState.Margin = new Padding(6, 6, 6, 6);
            _btnState.Name = "_btnState";
            _btnState.Size = new Size(217, 32);
            _btnState.TabIndex = 25;
            _btnState.Text = "Сохранить / Загрузить";
            _btnState.UseVisualStyleBackColor = true;
            _btnState.Click += btnState_Click;
            // 
            // _lblProgress
            // 
            _lblProgress.AutoSize = true;
            _pnlControls.SetColumnSpan(_lblProgress, 2);
            _lblProgress.Dock = DockStyle.Fill;
            _lblProgress.Location = new Point(6, 505);
            _lblProgress.Margin = new Padding(6, 0, 3, 0);
            _lblProgress.Name = "_lblProgress";
            _lblProgress.Size = new Size(220, 20);
            _lblProgress.TabIndex = 26;
            _lblProgress.Text = "Готово";
            _lblProgress.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _pbRenderProgress
            // 
            _pnlControls.SetColumnSpan(_pbRenderProgress, 2);
            _pbRenderProgress.Dock = DockStyle.Fill;
            _pbRenderProgress.Location = new Point(6, 528);
            _pbRenderProgress.Margin = new Padding(6, 3, 6, 3);
            _pbRenderProgress.Name = "_pbRenderProgress";
            _pbRenderProgress.Size = new Size(217, 22);
            _pbRenderProgress.TabIndex = 27;
            _pbRenderProgress.Value = 100;
            // 
            // _btnToggleControls
            // 
            _btnToggleControls.FlatStyle = FlatStyle.Flat;
            _btnToggleControls.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 204);
            _btnToggleControls.Location = new Point(256, 12);
            _btnToggleControls.Name = "_btnToggleControls";
            _btnToggleControls.Size = new Size(40, 36);
            _btnToggleControls.TabIndex = 2;
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
            _canvas.Size = new Size(1086, 644);
            _canvas.TabIndex = 1;
            _canvas.TabStop = false;
            // 
            // FractalIkedaForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1086, 644);
            Controls.Add(_canvasHost);
            MinimumSize = new Size(980, 620);
            Name = "FractalIkedaForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Отображение Икэды";
            _canvasHost.ResumeLayout(false);
            _controlsHost.ResumeLayout(false);
            _pnlControls.ResumeLayout(false);
            _pnlControls.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_nudU).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudX0).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudY0).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudIterations).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudDiscard).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudRangeXMin).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudRangeXMax).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudRangeYMin).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudRangeYMax).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudZoom).EndInit();
            ((System.ComponentModel.ISupportInitialize)_canvas).EndInit();
            ResumeLayout(false);
        }
    }
}
