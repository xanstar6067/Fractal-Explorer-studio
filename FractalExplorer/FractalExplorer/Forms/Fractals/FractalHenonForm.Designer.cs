namespace FractalExplorer.Forms.Fractals
{
    partial class FractalHenonForm
    {
        private System.ComponentModel.IContainer components = null;

        private Panel _canvasHost;
        private PictureBox _canvas;
        private Panel _controlsHost;
        private TableLayoutPanel _pnlControls;
        private Button _btnToggleControls;
        private NumericUpDown _nudA;
        private Label _lblA;
        private NumericUpDown _nudB;
        private Label _lblB;
        private NumericUpDown _nudX0;
        private Label _lblX0;
        private NumericUpDown _nudY0;
        private Label _lblY0;
        private NumericUpDown _nudIterations;
        private Label _lblIterations;
        private NumericUpDown _nudDiscard;
        private Label _lblDiscard;
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
            _nudA = new NumericUpDown();
            _lblA = new Label();
            _nudB = new NumericUpDown();
            _lblB = new Label();
            _nudX0 = new NumericUpDown();
            _lblX0 = new Label();
            _nudY0 = new NumericUpDown();
            _lblY0 = new Label();
            _nudIterations = new NumericUpDown();
            _lblIterations = new Label();
            _nudDiscard = new NumericUpDown();
            _lblDiscard = new Label();
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
            ((System.ComponentModel.ISupportInitialize)_nudA).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudB).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudX0).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudY0).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudIterations).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudDiscard).BeginInit();
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
            _pnlControls.Controls.Add(_nudA, 0, 0);
            _pnlControls.Controls.Add(_lblA, 1, 0);
            _pnlControls.Controls.Add(_nudB, 0, 1);
            _pnlControls.Controls.Add(_lblB, 1, 1);
            _pnlControls.Controls.Add(_nudX0, 0, 2);
            _pnlControls.Controls.Add(_lblX0, 1, 2);
            _pnlControls.Controls.Add(_nudY0, 0, 3);
            _pnlControls.Controls.Add(_lblY0, 1, 3);
            _pnlControls.Controls.Add(_nudIterations, 0, 4);
            _pnlControls.Controls.Add(_lblIterations, 1, 4);
            _pnlControls.Controls.Add(_nudDiscard, 0, 5);
            _pnlControls.Controls.Add(_lblDiscard, 1, 5);
            _pnlControls.Controls.Add(_nudZoom, 0, 6);
            _pnlControls.Controls.Add(_lblZoom, 1, 6);
            _pnlControls.Controls.Add(_cbThreads, 0, 7);
            _pnlControls.Controls.Add(_lblThreads, 1, 7);
            _pnlControls.Controls.Add(_btnSaveImage, 0, 8);
            _pnlControls.Controls.Add(_btnRender, 0, 9);
            _pnlControls.Controls.Add(_btnReset, 0, 10);
            _pnlControls.Controls.Add(_btnState, 0, 11);
            _pnlControls.Controls.Add(_lblProgress, 0, 12);
            _pnlControls.Controls.Add(_pbRenderProgress, 0, 13);
            _pnlControls.Dock = DockStyle.Fill;
            _pnlControls.Location = new Point(0, 0);
            _pnlControls.Name = "_pnlControls";
            _pnlControls.RowCount = 15;
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
            // _nudA
            // 
            _nudA.Dock = DockStyle.Fill;
            _nudA.Location = new Point(6, 6);
            _nudA.Margin = new Padding(6, 6, 3, 3);
            _nudA.Name = "_nudA";
            _nudA.Size = new Size(116, 23);
            _nudA.TabIndex = 0;
            // 
            // _lblA
            // 
            _lblA.AutoSize = true;
            _lblA.Dock = DockStyle.Fill;
            _lblA.Location = new Point(128, 0);
            _lblA.Name = "_lblA";
            _lblA.Size = new Size(98, 32);
            _lblA.TabIndex = 1;
            _lblA.Text = "Параметр a";
            _lblA.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudB
            // 
            _nudB.Dock = DockStyle.Fill;
            _nudB.Location = new Point(6, 35);
            _nudB.Margin = new Padding(6, 3, 3, 3);
            _nudB.Name = "_nudB";
            _nudB.Size = new Size(116, 23);
            _nudB.TabIndex = 2;
            // 
            // _lblB
            // 
            _lblB.AutoSize = true;
            _lblB.Dock = DockStyle.Fill;
            _lblB.Location = new Point(128, 32);
            _lblB.Name = "_lblB";
            _lblB.Size = new Size(98, 29);
            _lblB.TabIndex = 3;
            _lblB.Text = "Параметр b";
            _lblB.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudX0
            // 
            _nudX0.Dock = DockStyle.Fill;
            _nudX0.Location = new Point(6, 64);
            _nudX0.Margin = new Padding(6, 3, 3, 3);
            _nudX0.Name = "_nudX0";
            _nudX0.Size = new Size(116, 23);
            _nudX0.TabIndex = 4;
            // 
            // _lblX0
            // 
            _lblX0.AutoSize = true;
            _lblX0.Dock = DockStyle.Fill;
            _lblX0.Location = new Point(128, 61);
            _lblX0.Name = "_lblX0";
            _lblX0.Size = new Size(98, 29);
            _lblX0.TabIndex = 5;
            _lblX0.Text = "Нач. x0";
            _lblX0.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudY0
            // 
            _nudY0.Dock = DockStyle.Fill;
            _nudY0.Location = new Point(6, 93);
            _nudY0.Margin = new Padding(6, 3, 3, 3);
            _nudY0.Name = "_nudY0";
            _nudY0.Size = new Size(116, 23);
            _nudY0.TabIndex = 6;
            // 
            // _lblY0
            // 
            _lblY0.AutoSize = true;
            _lblY0.Dock = DockStyle.Fill;
            _lblY0.Location = new Point(128, 90);
            _lblY0.Name = "_lblY0";
            _lblY0.Size = new Size(98, 29);
            _lblY0.TabIndex = 7;
            _lblY0.Text = "Нач. y0";
            _lblY0.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudIterations
            // 
            _nudIterations.Dock = DockStyle.Fill;
            _nudIterations.Location = new Point(6, 122);
            _nudIterations.Margin = new Padding(6, 3, 3, 3);
            _nudIterations.Name = "_nudIterations";
            _nudIterations.Size = new Size(116, 23);
            _nudIterations.TabIndex = 8;
            // 
            // _lblIterations
            // 
            _lblIterations.AutoSize = true;
            _lblIterations.Dock = DockStyle.Fill;
            _lblIterations.Location = new Point(128, 119);
            _lblIterations.Name = "_lblIterations";
            _lblIterations.Size = new Size(98, 29);
            _lblIterations.TabIndex = 9;
            _lblIterations.Text = "Итерации";
            _lblIterations.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudDiscard
            // 
            _nudDiscard.Dock = DockStyle.Fill;
            _nudDiscard.Location = new Point(6, 151);
            _nudDiscard.Margin = new Padding(6, 3, 3, 3);
            _nudDiscard.Name = "_nudDiscard";
            _nudDiscard.Size = new Size(116, 23);
            _nudDiscard.TabIndex = 10;
            // 
            // _lblDiscard
            // 
            _lblDiscard.AutoSize = true;
            _lblDiscard.Dock = DockStyle.Fill;
            _lblDiscard.Location = new Point(128, 148);
            _lblDiscard.Name = "_lblDiscard";
            _lblDiscard.Size = new Size(98, 29);
            _lblDiscard.TabIndex = 11;
            _lblDiscard.Text = "Пропуск";
            _lblDiscard.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudZoom
            // 
            _nudZoom.Dock = DockStyle.Fill;
            _nudZoom.Location = new Point(6, 180);
            _nudZoom.Margin = new Padding(6, 3, 3, 3);
            _nudZoom.Name = "_nudZoom";
            _nudZoom.Size = new Size(116, 23);
            _nudZoom.TabIndex = 12;
            // 
            // _lblZoom
            // 
            _lblZoom.AutoSize = true;
            _lblZoom.Dock = DockStyle.Fill;
            _lblZoom.Location = new Point(128, 177);
            _lblZoom.Name = "_lblZoom";
            _lblZoom.Size = new Size(98, 29);
            _lblZoom.TabIndex = 13;
            _lblZoom.Text = "Zoom";
            _lblZoom.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _cbThreads
            // 
            _cbThreads.Dock = DockStyle.Fill;
            _cbThreads.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbThreads.FormattingEnabled = true;
            _cbThreads.Location = new Point(6, 209);
            _cbThreads.Margin = new Padding(6, 3, 3, 3);
            _cbThreads.Name = "_cbThreads";
            _cbThreads.Size = new Size(116, 23);
            _cbThreads.TabIndex = 14;
            // 
            // _lblThreads
            // 
            _lblThreads.AutoSize = true;
            _lblThreads.Dock = DockStyle.Fill;
            _lblThreads.Location = new Point(128, 206);
            _lblThreads.Name = "_lblThreads";
            _lblThreads.Size = new Size(98, 29);
            _lblThreads.TabIndex = 15;
            _lblThreads.Text = "Потоки";
            _lblThreads.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _btnSaveImage
            // 
            _pnlControls.SetColumnSpan(_btnSaveImage, 2);
            _btnSaveImage.Dock = DockStyle.Fill;
            _btnSaveImage.Location = new Point(6, 241);
            _btnSaveImage.Margin = new Padding(6);
            _btnSaveImage.Name = "_btnSaveImage";
            _btnSaveImage.Size = new Size(217, 33);
            _btnSaveImage.TabIndex = 16;
            _btnSaveImage.Text = "Сохранить изображение";
            _btnSaveImage.UseVisualStyleBackColor = true;
            _btnSaveImage.Click += btnSaveImage_Click;
            // 
            // _btnRender
            // 
            _pnlControls.SetColumnSpan(_btnRender, 2);
            _btnRender.Dock = DockStyle.Fill;
            _btnRender.Location = new Point(6, 286);
            _btnRender.Margin = new Padding(6);
            _btnRender.Name = "_btnRender";
            _btnRender.Size = new Size(217, 33);
            _btnRender.TabIndex = 17;
            _btnRender.Text = "Рендер";
            _btnRender.UseVisualStyleBackColor = true;
            _btnRender.Click += btnRender_Click;
            // 
            // _btnReset
            // 
            _pnlControls.SetColumnSpan(_btnReset, 2);
            _btnReset.Dock = DockStyle.Fill;
            _btnReset.Location = new Point(6, 331);
            _btnReset.Margin = new Padding(6);
            _btnReset.Name = "_btnReset";
            _btnReset.Size = new Size(217, 33);
            _btnReset.TabIndex = 18;
            _btnReset.Text = "Сброс вида";
            _btnReset.UseVisualStyleBackColor = true;
            _btnReset.Click += btnReset_Click;
            // 
            // _btnState
            // 
            _pnlControls.SetColumnSpan(_btnState, 2);
            _btnState.Dock = DockStyle.Fill;
            _btnState.Location = new Point(6, 376);
            _btnState.Margin = new Padding(6);
            _btnState.Name = "_btnState";
            _btnState.Size = new Size(217, 33);
            _btnState.TabIndex = 19;
            _btnState.Text = "Менеджер сохранений";
            _btnState.UseVisualStyleBackColor = true;
            _btnState.Click += btnState_Click;
            // 
            // _lblProgress
            // 
            _lblProgress.AutoSize = true;
            _pnlControls.SetColumnSpan(_lblProgress, 2);
            _lblProgress.Dock = DockStyle.Fill;
            _lblProgress.Location = new Point(6, 415);
            _lblProgress.Margin = new Padding(6, 0, 6, 0);
            _lblProgress.Name = "_lblProgress";
            _lblProgress.Size = new Size(217, 20);
            _lblProgress.TabIndex = 20;
            _lblProgress.Text = "Статус рендера";
            _lblProgress.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _pbRenderProgress
            // 
            _pnlControls.SetColumnSpan(_pbRenderProgress, 2);
            _pbRenderProgress.Dock = DockStyle.Fill;
            _pbRenderProgress.Location = new Point(6, 438);
            _pbRenderProgress.Margin = new Padding(6, 3, 6, 3);
            _pbRenderProgress.Name = "_pbRenderProgress";
            _pbRenderProgress.Size = new Size(217, 22);
            _pbRenderProgress.TabIndex = 21;
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
            _canvas.BackColor = Color.Transparent;
            _canvas.Dock = DockStyle.Fill;
            _canvas.Location = new Point(0, 0);
            _canvas.Name = "_canvas";
            _canvas.Size = new Size(1086, 644);
            _canvas.TabIndex = 2;
            _canvas.TabStop = false;
            // 
            // FractalHenonForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1086, 644);
            Controls.Add(_canvasHost);
            Name = "FractalHenonForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Карта Хенона";
            _canvasHost.ResumeLayout(false);
            _canvasHost.PerformLayout();
            _controlsHost.ResumeLayout(false);
            _pnlControls.ResumeLayout(false);
            _pnlControls.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_nudA).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudB).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudX0).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudY0).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudIterations).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudDiscard).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudZoom).EndInit();
            ((System.ComponentModel.ISupportInitialize)_canvas).EndInit();
            ResumeLayout(false);
        }
    }
}
