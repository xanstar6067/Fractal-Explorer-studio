namespace FractalExplorer.Forms.Fractals
{
    partial class FractalLorenzForm
    {
        private System.ComponentModel.IContainer components = null;

        private Panel _canvasHost;
        private PictureBox _canvas;
        private Panel _controlsHost;
        private TableLayoutPanel _pnlControls;
        private Button _btnToggleControls;

        private NumericUpDown _nudSigma;
        private Label _lblSigma;
        private NumericUpDown _nudRho;
        private Label _lblRho;
        private NumericUpDown _nudBeta;
        private Label _lblBeta;
        private NumericUpDown _nudDt;
        private Label _lblDt;
        private NumericUpDown _nudSteps;
        private Label _lblSteps;
        private NumericUpDown _nudStartX;
        private Label _lblStartX;
        private NumericUpDown _nudStartY;
        private Label _lblStartY;
        private NumericUpDown _nudStartZ;
        private Label _lblStartZ;
        private ComboBox _cbProjection;
        private Label _lblProjection;
        private NumericUpDown _nudZoom;
        private Label _lblZoom;
        private ComboBox _cbThreads;
        private Label _lblThreads;

        private Button _btnSaveImage;
        private Button _btnRender;
        private Button _btnReset;
        private Button _btnState;
        private Button _btnBackgroundColor;
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
            _nudSigma = new NumericUpDown();
            _lblSigma = new Label();
            _nudRho = new NumericUpDown();
            _lblRho = new Label();
            _nudBeta = new NumericUpDown();
            _lblBeta = new Label();
            _nudDt = new NumericUpDown();
            _lblDt = new Label();
            _nudSteps = new NumericUpDown();
            _lblSteps = new Label();
            _nudStartX = new NumericUpDown();
            _lblStartX = new Label();
            _nudStartY = new NumericUpDown();
            _lblStartY = new Label();
            _nudStartZ = new NumericUpDown();
            _lblStartZ = new Label();
            _cbProjection = new ComboBox();
            _lblProjection = new Label();
            _nudZoom = new NumericUpDown();
            _lblZoom = new Label();
            _cbThreads = new ComboBox();
            _lblThreads = new Label();
            _btnSaveImage = new Button();
            _btnRender = new Button();
            _btnReset = new Button();
            _btnState = new Button();
            _btnBackgroundColor = new Button();
            _lblProgress = new Label();
            _pbRenderProgress = new ProgressBar();
            _btnToggleControls = new Button();
            _canvas = new PictureBox();
            _canvasHost.SuspendLayout();
            _controlsHost.SuspendLayout();
            _pnlControls.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_nudSigma).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudRho).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudBeta).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudDt).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudSteps).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudStartX).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudStartY).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudStartZ).BeginInit();
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
            _pnlControls.Controls.Add(_nudSigma, 0, 0);
            _pnlControls.Controls.Add(_lblSigma, 1, 0);
            _pnlControls.Controls.Add(_nudRho, 0, 1);
            _pnlControls.Controls.Add(_lblRho, 1, 1);
            _pnlControls.Controls.Add(_nudBeta, 0, 2);
            _pnlControls.Controls.Add(_lblBeta, 1, 2);
            _pnlControls.Controls.Add(_nudDt, 0, 3);
            _pnlControls.Controls.Add(_lblDt, 1, 3);
            _pnlControls.Controls.Add(_nudSteps, 0, 4);
            _pnlControls.Controls.Add(_lblSteps, 1, 4);
            _pnlControls.Controls.Add(_nudStartX, 0, 5);
            _pnlControls.Controls.Add(_lblStartX, 1, 5);
            _pnlControls.Controls.Add(_nudStartY, 0, 6);
            _pnlControls.Controls.Add(_lblStartY, 1, 6);
            _pnlControls.Controls.Add(_nudStartZ, 0, 7);
            _pnlControls.Controls.Add(_lblStartZ, 1, 7);
            _pnlControls.Controls.Add(_cbProjection, 0, 8);
            _pnlControls.Controls.Add(_lblProjection, 1, 8);
            _pnlControls.Controls.Add(_nudZoom, 0, 9);
            _pnlControls.Controls.Add(_lblZoom, 1, 9);
            _pnlControls.Controls.Add(_cbThreads, 0, 10);
            _pnlControls.Controls.Add(_lblThreads, 1, 10);
            _pnlControls.Controls.Add(_btnSaveImage, 0, 11);
            _pnlControls.Controls.Add(_btnRender, 0, 12);
            _pnlControls.Controls.Add(_btnReset, 0, 13);
            _pnlControls.Controls.Add(_btnState, 0, 14);
            _pnlControls.Controls.Add(_btnBackgroundColor, 0, 15);
            _pnlControls.Controls.Add(_lblProgress, 0, 16);
            _pnlControls.Controls.Add(_pbRenderProgress, 0, 17);
            _pnlControls.Dock = DockStyle.Fill;
            _pnlControls.Location = new Point(0, 0);
            _pnlControls.Name = "_pnlControls";
            _pnlControls.RowCount = 19;
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
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            _pnlControls.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _pnlControls.Size = new Size(229, 642);
            _pnlControls.TabIndex = 0;
            // 
            // _nudSigma
            // 
            _nudSigma.Dock = DockStyle.Fill;
            _nudSigma.Location = new Point(6, 6);
            _nudSigma.Margin = new Padding(6, 6, 3, 3);
            _nudSigma.Name = "_nudSigma";
            _nudSigma.Size = new Size(116, 23);
            _nudSigma.TabIndex = 0;
            // 
            // _lblSigma
            // 
            _lblSigma.AutoSize = true;
            _lblSigma.Dock = DockStyle.Fill;
            _lblSigma.Location = new Point(128, 0);
            _lblSigma.Name = "_lblSigma";
            _lblSigma.Size = new Size(98, 32);
            _lblSigma.TabIndex = 1;
            _lblSigma.Text = "sigma";
            _lblSigma.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudRho
            // 
            _nudRho.Dock = DockStyle.Fill;
            _nudRho.Location = new Point(6, 35);
            _nudRho.Margin = new Padding(6, 3, 3, 3);
            _nudRho.Name = "_nudRho";
            _nudRho.Size = new Size(116, 23);
            _nudRho.TabIndex = 2;
            // 
            // _lblRho
            // 
            _lblRho.AutoSize = true;
            _lblRho.Dock = DockStyle.Fill;
            _lblRho.Location = new Point(128, 32);
            _lblRho.Name = "_lblRho";
            _lblRho.Size = new Size(98, 29);
            _lblRho.TabIndex = 3;
            _lblRho.Text = "rho";
            _lblRho.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudBeta
            // 
            _nudBeta.Dock = DockStyle.Fill;
            _nudBeta.Location = new Point(6, 64);
            _nudBeta.Margin = new Padding(6, 3, 3, 3);
            _nudBeta.Name = "_nudBeta";
            _nudBeta.Size = new Size(116, 23);
            _nudBeta.TabIndex = 4;
            // 
            // _lblBeta
            // 
            _lblBeta.AutoSize = true;
            _lblBeta.Dock = DockStyle.Fill;
            _lblBeta.Location = new Point(128, 61);
            _lblBeta.Name = "_lblBeta";
            _lblBeta.Size = new Size(98, 29);
            _lblBeta.TabIndex = 5;
            _lblBeta.Text = "beta";
            _lblBeta.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudDt
            // 
            _nudDt.Dock = DockStyle.Fill;
            _nudDt.Location = new Point(6, 93);
            _nudDt.Margin = new Padding(6, 3, 3, 3);
            _nudDt.Name = "_nudDt";
            _nudDt.Size = new Size(116, 23);
            _nudDt.TabIndex = 6;
            // 
            // _lblDt
            // 
            _lblDt.AutoSize = true;
            _lblDt.Dock = DockStyle.Fill;
            _lblDt.Location = new Point(128, 90);
            _lblDt.Name = "_lblDt";
            _lblDt.Size = new Size(98, 29);
            _lblDt.TabIndex = 7;
            _lblDt.Text = "dt";
            _lblDt.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudSteps
            // 
            _nudSteps.Dock = DockStyle.Fill;
            _nudSteps.Location = new Point(6, 122);
            _nudSteps.Margin = new Padding(6, 3, 3, 3);
            _nudSteps.Name = "_nudSteps";
            _nudSteps.Size = new Size(116, 23);
            _nudSteps.TabIndex = 8;
            // 
            // _lblSteps
            // 
            _lblSteps.AutoSize = true;
            _lblSteps.Dock = DockStyle.Fill;
            _lblSteps.Location = new Point(128, 119);
            _lblSteps.Name = "_lblSteps";
            _lblSteps.Size = new Size(98, 29);
            _lblSteps.TabIndex = 9;
            _lblSteps.Text = "Шаги";
            _lblSteps.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudStartX
            // 
            _nudStartX.Dock = DockStyle.Fill;
            _nudStartX.Location = new Point(6, 151);
            _nudStartX.Margin = new Padding(6, 3, 3, 3);
            _nudStartX.Name = "_nudStartX";
            _nudStartX.Size = new Size(116, 23);
            _nudStartX.TabIndex = 10;
            // 
            // _lblStartX
            // 
            _lblStartX.AutoSize = true;
            _lblStartX.Dock = DockStyle.Fill;
            _lblStartX.Location = new Point(128, 148);
            _lblStartX.Name = "_lblStartX";
            _lblStartX.Size = new Size(98, 29);
            _lblStartX.TabIndex = 11;
            _lblStartX.Text = "Старт X";
            _lblStartX.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudStartY
            // 
            _nudStartY.Dock = DockStyle.Fill;
            _nudStartY.Location = new Point(6, 180);
            _nudStartY.Margin = new Padding(6, 3, 3, 3);
            _nudStartY.Name = "_nudStartY";
            _nudStartY.Size = new Size(116, 23);
            _nudStartY.TabIndex = 12;
            // 
            // _lblStartY
            // 
            _lblStartY.AutoSize = true;
            _lblStartY.Dock = DockStyle.Fill;
            _lblStartY.Location = new Point(128, 177);
            _lblStartY.Name = "_lblStartY";
            _lblStartY.Size = new Size(98, 29);
            _lblStartY.TabIndex = 13;
            _lblStartY.Text = "Старт Y";
            _lblStartY.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudStartZ
            // 
            _nudStartZ.Dock = DockStyle.Fill;
            _nudStartZ.Location = new Point(6, 209);
            _nudStartZ.Margin = new Padding(6, 3, 3, 3);
            _nudStartZ.Name = "_nudStartZ";
            _nudStartZ.Size = new Size(116, 23);
            _nudStartZ.TabIndex = 14;
            // 
            // _lblStartZ
            // 
            _lblStartZ.AutoSize = true;
            _lblStartZ.Dock = DockStyle.Fill;
            _lblStartZ.Location = new Point(128, 206);
            _lblStartZ.Name = "_lblStartZ";
            _lblStartZ.Size = new Size(98, 29);
            _lblStartZ.TabIndex = 15;
            _lblStartZ.Text = "Старт Z";
            _lblStartZ.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _cbProjection
            // 
            _cbProjection.Dock = DockStyle.Fill;
            _cbProjection.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbProjection.FormattingEnabled = true;
            _cbProjection.Location = new Point(6, 238);
            _cbProjection.Margin = new Padding(6, 3, 3, 3);
            _cbProjection.Name = "_cbProjection";
            _cbProjection.Size = new Size(116, 23);
            _cbProjection.TabIndex = 16;
            // 
            // _lblProjection
            // 
            _lblProjection.AutoSize = true;
            _lblProjection.Dock = DockStyle.Fill;
            _lblProjection.Location = new Point(128, 235);
            _lblProjection.Name = "_lblProjection";
            _lblProjection.Size = new Size(98, 29);
            _lblProjection.TabIndex = 17;
            _lblProjection.Text = "Проекция";
            _lblProjection.TextAlign = ContentAlignment.MiddleLeft;
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
            _lblZoom.Text = "Приближение";
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
            _lblThreads.Text = "Потоки ЦП";
            _lblThreads.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _btnSaveImage
            // 
            _pnlControls.SetColumnSpan(_btnSaveImage, 2);
            _btnSaveImage.Dock = DockStyle.Fill;
            _btnSaveImage.Location = new Point(6, 325);
            _btnSaveImage.Margin = new Padding(6, 3, 6, 3);
            _btnSaveImage.Name = "_btnSaveImage";
            _btnSaveImage.Size = new Size(217, 39);
            _btnSaveImage.TabIndex = 22;
            _btnSaveImage.Text = "Сохранить изображение";
            _btnSaveImage.UseVisualStyleBackColor = true;
            _btnSaveImage.Click += btnSaveImage_Click;
            // 
            // _btnRender
            // 
            _pnlControls.SetColumnSpan(_btnRender, 2);
            _btnRender.Dock = DockStyle.Fill;
            _btnRender.Location = new Point(6, 370);
            _btnRender.Margin = new Padding(6, 3, 6, 3);
            _btnRender.Name = "_btnRender";
            _btnRender.Size = new Size(217, 39);
            _btnRender.TabIndex = 23;
            _btnRender.Text = "Запустить рендер";
            _btnRender.UseVisualStyleBackColor = true;
            _btnRender.Click += btnRender_Click;
            // 
            // _btnReset
            // 
            _pnlControls.SetColumnSpan(_btnReset, 2);
            _btnReset.Dock = DockStyle.Fill;
            _btnReset.Location = new Point(6, 415);
            _btnReset.Margin = new Padding(6, 3, 6, 3);
            _btnReset.Name = "_btnReset";
            _btnReset.Size = new Size(217, 39);
            _btnReset.TabIndex = 24;
            _btnReset.Text = "Сбросить вид";
            _btnReset.UseVisualStyleBackColor = true;
            _btnReset.Click += btnReset_Click;
            // 
            // _btnState
            // 
            _pnlControls.SetColumnSpan(_btnState, 2);
            _btnState.Dock = DockStyle.Fill;
            _btnState.Location = new Point(6, 460);
            _btnState.Margin = new Padding(6, 3, 6, 3);
            _btnState.Name = "_btnState";
            _btnState.Size = new Size(217, 39);
            _btnState.TabIndex = 25;
            _btnState.Text = "Менеджер сохранений";
            _btnState.UseVisualStyleBackColor = true;
            _btnState.Click += btnState_Click;
            //
            // _btnBackgroundColor
            //
            _pnlControls.SetColumnSpan(_btnBackgroundColor, 2);
            _btnBackgroundColor.Dock = DockStyle.Fill;
            _btnBackgroundColor.Location = new Point(6, 505);
            _btnBackgroundColor.Margin = new Padding(6, 3, 6, 3);
            _btnBackgroundColor.Name = "_btnBackgroundColor";
            _btnBackgroundColor.Size = new Size(217, 39);
            _btnBackgroundColor.TabIndex = 26;
            _btnBackgroundColor.Text = "Цвет фона";
            _btnBackgroundColor.UseVisualStyleBackColor = true;
            _btnBackgroundColor.Click += btnBackgroundColor_Click;
            //
            // _lblProgress
            //
            _lblProgress.AutoSize = true;
            _pnlControls.SetColumnSpan(_lblProgress, 2);
            _lblProgress.Dock = DockStyle.Fill;
            _lblProgress.Location = new Point(6, 547);
            _lblProgress.Margin = new Padding(6, 0, 3, 0);
            _lblProgress.Name = "_lblProgress";
            _lblProgress.Size = new Size(220, 20);
            _lblProgress.TabIndex = 27;
            _lblProgress.Text = "Обработка";
            _lblProgress.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _pbRenderProgress
            //
            _pbRenderProgress.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _pnlControls.SetColumnSpan(_pbRenderProgress, 2);
            _pbRenderProgress.Location = new Point(6, 570);
            _pbRenderProgress.Margin = new Padding(6, 3, 6, 3);
            _pbRenderProgress.Name = "_pbRenderProgress";
            _pbRenderProgress.Size = new Size(217, 24);
            _pbRenderProgress.TabIndex = 28;
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
            _canvas.Size = new Size(1086, 644);
            _canvas.TabIndex = 0;
            _canvas.TabStop = false;
            // 
            // FractalLorenzForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1086, 644);
            Controls.Add(_canvasHost);
            MinimumSize = new Size(1024, 680);
            Name = "FractalLorenzForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Аттрактор Лоренца";
            _canvasHost.ResumeLayout(false);
            _canvasHost.PerformLayout();
            _controlsHost.ResumeLayout(false);
            _pnlControls.ResumeLayout(false);
            _pnlControls.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_nudSigma).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudRho).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudBeta).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudDt).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudSteps).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudStartX).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudStartY).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudStartZ).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudZoom).EndInit();
            ((System.ComponentModel.ISupportInitialize)_canvas).EndInit();
            ResumeLayout(false);
        }
    }
}
