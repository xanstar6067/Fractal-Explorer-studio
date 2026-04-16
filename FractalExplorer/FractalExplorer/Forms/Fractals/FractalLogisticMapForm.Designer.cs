namespace FractalExplorer.Forms.Fractals
{
    partial class FractalLogisticMapForm
    {
        private System.ComponentModel.IContainer components = null;

        private Panel _canvasHost;
        private PictureBox _canvas;
        private Panel _controlsHost;
        private TableLayoutPanel _pnlControls;
        private Button _btnToggleControls;
        private NumericUpDown _nudR;
        private Label _lblR;
        private NumericUpDown _nudX0;
        private Label _lblX0;
        private NumericUpDown _nudIterations;
        private Label _lblIterations;
        private NumericUpDown _nudTransient;
        private Label _lblTransient;
        private ComboBox _cbVisualizationMode;
        private Label _lblVisualizationMode;
        private NumericUpDown _nudBifurcationRMin;
        private Label _lblBifurcationRMin;
        private NumericUpDown _nudBifurcationRMax;
        private Label _lblBifurcationRMax;
        private NumericUpDown _nudBifurcationSamples;
        private Label _lblBifurcationSamples;
        private NumericUpDown _nudBifurcationTransient;
        private Label _lblBifurcationTransient;
        private NumericUpDown _nudBifurcationPlotted;
        private Label _lblBifurcationPlotted;
        private NumericUpDown _nudCobwebSteps;
        private Label _lblCobwebSteps;
        private NumericUpDown _nudZoom;
        private Label _lblZoom;
        private ComboBox _cbThreads;
        private Label _lblThreads;
        private Button _btnSaveImage;
        private Button _btnPalette;
        private Button _btnRender;
        private Button _btnReset;
        private Button _btnState;
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FractalLogisticMapForm));
            _canvasHost = new Panel();
            _controlsHost = new Panel();
            _pnlControls = new TableLayoutPanel();
            _nudR = new NumericUpDown();
            _lblR = new Label();
            _nudX0 = new NumericUpDown();
            _lblX0 = new Label();
            _nudIterations = new NumericUpDown();
            _lblIterations = new Label();
            _nudTransient = new NumericUpDown();
            _lblTransient = new Label();
            _cbVisualizationMode = new ComboBox();
            _lblVisualizationMode = new Label();
            _nudBifurcationRMin = new NumericUpDown();
            _lblBifurcationRMin = new Label();
            _nudBifurcationRMax = new NumericUpDown();
            _lblBifurcationRMax = new Label();
            _nudBifurcationSamples = new NumericUpDown();
            _lblBifurcationSamples = new Label();
            _nudBifurcationTransient = new NumericUpDown();
            _lblBifurcationTransient = new Label();
            _nudBifurcationPlotted = new NumericUpDown();
            _lblBifurcationPlotted = new Label();
            _nudCobwebSteps = new NumericUpDown();
            _lblCobwebSteps = new Label();
            _nudZoom = new NumericUpDown();
            _lblZoom = new Label();
            _cbThreads = new ComboBox();
            _lblThreads = new Label();
            _btnSaveImage = new Button();
            _btnPalette = new Button();
            _btnRender = new Button();
            _btnReset = new Button();
            _btnState = new Button();
            _pbRenderProgress = new ProgressBar();
            _btnToggleControls = new Button();
            _canvas = new PictureBox();
            _canvasHost.SuspendLayout();
            _controlsHost.SuspendLayout();
            _pnlControls.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_nudR).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudX0).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudIterations).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudTransient).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudBifurcationRMin).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudBifurcationRMax).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudBifurcationSamples).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudBifurcationTransient).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudBifurcationPlotted).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudCobwebSteps).BeginInit();
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
            _pnlControls.Controls.Add(_nudR, 0, 0);
            _pnlControls.Controls.Add(_lblR, 1, 0);
            _pnlControls.Controls.Add(_nudX0, 0, 1);
            _pnlControls.Controls.Add(_lblX0, 1, 1);
            _pnlControls.Controls.Add(_nudIterations, 0, 2);
            _pnlControls.Controls.Add(_lblIterations, 1, 2);
            _pnlControls.Controls.Add(_nudTransient, 0, 3);
            _pnlControls.Controls.Add(_lblTransient, 1, 3);
            _pnlControls.Controls.Add(_cbVisualizationMode, 0, 4);
            _pnlControls.Controls.Add(_lblVisualizationMode, 1, 4);
            _pnlControls.Controls.Add(_nudBifurcationRMin, 0, 5);
            _pnlControls.Controls.Add(_lblBifurcationRMin, 1, 5);
            _pnlControls.Controls.Add(_nudBifurcationRMax, 0, 6);
            _pnlControls.Controls.Add(_lblBifurcationRMax, 1, 6);
            _pnlControls.Controls.Add(_nudBifurcationSamples, 0, 7);
            _pnlControls.Controls.Add(_lblBifurcationSamples, 1, 7);
            _pnlControls.Controls.Add(_nudBifurcationTransient, 0, 8);
            _pnlControls.Controls.Add(_lblBifurcationTransient, 1, 8);
            _pnlControls.Controls.Add(_nudBifurcationPlotted, 0, 9);
            _pnlControls.Controls.Add(_lblBifurcationPlotted, 1, 9);
            _pnlControls.Controls.Add(_nudCobwebSteps, 0, 10);
            _pnlControls.Controls.Add(_lblCobwebSteps, 1, 10);
            _pnlControls.Controls.Add(_nudZoom, 0, 11);
            _pnlControls.Controls.Add(_lblZoom, 1, 11);
            _pnlControls.Controls.Add(_cbThreads, 0, 12);
            _pnlControls.Controls.Add(_lblThreads, 1, 12);
            _pnlControls.Controls.Add(_btnSaveImage, 0, 13);
            _pnlControls.Controls.Add(_btnPalette, 0, 14);
            _pnlControls.Controls.Add(_btnRender, 0, 15);
            _pnlControls.Controls.Add(_btnReset, 0, 16);
            _pnlControls.Controls.Add(_btnState, 0, 17);
            _pnlControls.Controls.Add(_pbRenderProgress, 0, 18);
            _pnlControls.Dock = DockStyle.Fill;
            _pnlControls.Location = new Point(0, 0);
            _pnlControls.Name = "_pnlControls";
            _pnlControls.RowCount = 20;
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
            // _nudR
            // 
            _nudR.Dock = DockStyle.Fill;
            _nudR.Location = new Point(6, 6);
            _nudR.Margin = new Padding(6, 6, 3, 3);
            _nudR.Name = "_nudR";
            _nudR.Size = new Size(116, 23);
            _nudR.TabIndex = 0;
            // 
            // _lblR
            // 
            _lblR.AutoSize = true;
            _lblR.Dock = DockStyle.Fill;
            _lblR.Location = new Point(128, 0);
            _lblR.Name = "_lblR";
            _lblR.Size = new Size(98, 32);
            _lblR.TabIndex = 1;
            _lblR.Text = "Параметр r";
            _lblR.TextAlign = ContentAlignment.MiddleLeft;
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
            // _nudIterations
            // 
            _nudIterations.Dock = DockStyle.Fill;
            _nudIterations.Location = new Point(6, 64);
            _nudIterations.Margin = new Padding(6, 3, 3, 3);
            _nudIterations.Name = "_nudIterations";
            _nudIterations.Size = new Size(116, 23);
            _nudIterations.TabIndex = 4;
            // 
            // _lblIterations
            // 
            _lblIterations.AutoSize = true;
            _lblIterations.Dock = DockStyle.Fill;
            _lblIterations.Location = new Point(128, 61);
            _lblIterations.Name = "_lblIterations";
            _lblIterations.Size = new Size(98, 29);
            _lblIterations.TabIndex = 5;
            _lblIterations.Text = "Итерации";
            _lblIterations.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudTransient
            // 
            _nudTransient.Dock = DockStyle.Fill;
            _nudTransient.Location = new Point(6, 93);
            _nudTransient.Margin = new Padding(6, 3, 3, 3);
            _nudTransient.Name = "_nudTransient";
            _nudTransient.Size = new Size(116, 23);
            _nudTransient.TabIndex = 6;
            // 
            // _lblTransient
            // 
            _lblTransient.AutoSize = true;
            _lblTransient.Dock = DockStyle.Fill;
            _lblTransient.Location = new Point(128, 90);
            _lblTransient.Name = "_lblTransient";
            _lblTransient.Size = new Size(98, 29);
            _lblTransient.TabIndex = 7;
            _lblTransient.Text = "Прогрев";
            _lblTransient.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _cbVisualizationMode
            // 
            _cbVisualizationMode.Dock = DockStyle.Fill;
            _cbVisualizationMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbVisualizationMode.FormattingEnabled = true;
            _cbVisualizationMode.Location = new Point(6, 122);
            _cbVisualizationMode.Margin = new Padding(6, 3, 3, 3);
            _cbVisualizationMode.Name = "_cbVisualizationMode";
            _cbVisualizationMode.Size = new Size(116, 23);
            _cbVisualizationMode.TabIndex = 8;
            // 
            // _lblVisualizationMode
            // 
            _lblVisualizationMode.AutoSize = true;
            _lblVisualizationMode.Dock = DockStyle.Fill;
            _lblVisualizationMode.Location = new Point(128, 119);
            _lblVisualizationMode.Name = "_lblVisualizationMode";
            _lblVisualizationMode.Size = new Size(98, 29);
            _lblVisualizationMode.TabIndex = 9;
            _lblVisualizationMode.Text = "Режим";
            _lblVisualizationMode.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudBifurcationRMin
            // 
            _nudBifurcationRMin.Dock = DockStyle.Fill;
            _nudBifurcationRMin.Location = new Point(6, 151);
            _nudBifurcationRMin.Margin = new Padding(6, 3, 3, 3);
            _nudBifurcationRMin.Name = "_nudBifurcationRMin";
            _nudBifurcationRMin.Size = new Size(116, 23);
            _nudBifurcationRMin.TabIndex = 10;
            // 
            // _lblBifurcationRMin
            // 
            _lblBifurcationRMin.AutoSize = true;
            _lblBifurcationRMin.Dock = DockStyle.Fill;
            _lblBifurcationRMin.Location = new Point(128, 148);
            _lblBifurcationRMin.Name = "_lblBifurcationRMin";
            _lblBifurcationRMin.Size = new Size(98, 29);
            _lblBifurcationRMin.TabIndex = 11;
            _lblBifurcationRMin.Text = "Bif r min";
            _lblBifurcationRMin.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudBifurcationRMax
            // 
            _nudBifurcationRMax.Dock = DockStyle.Fill;
            _nudBifurcationRMax.Location = new Point(6, 180);
            _nudBifurcationRMax.Margin = new Padding(6, 3, 3, 3);
            _nudBifurcationRMax.Name = "_nudBifurcationRMax";
            _nudBifurcationRMax.Size = new Size(116, 23);
            _nudBifurcationRMax.TabIndex = 12;
            // 
            // _lblBifurcationRMax
            // 
            _lblBifurcationRMax.AutoSize = true;
            _lblBifurcationRMax.Dock = DockStyle.Fill;
            _lblBifurcationRMax.Location = new Point(128, 177);
            _lblBifurcationRMax.Name = "_lblBifurcationRMax";
            _lblBifurcationRMax.Size = new Size(98, 29);
            _lblBifurcationRMax.TabIndex = 13;
            _lblBifurcationRMax.Text = "Bif r max";
            _lblBifurcationRMax.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudBifurcationSamples
            // 
            _nudBifurcationSamples.Dock = DockStyle.Fill;
            _nudBifurcationSamples.Location = new Point(6, 209);
            _nudBifurcationSamples.Margin = new Padding(6, 3, 3, 3);
            _nudBifurcationSamples.Name = "_nudBifurcationSamples";
            _nudBifurcationSamples.Size = new Size(116, 23);
            _nudBifurcationSamples.TabIndex = 14;
            // 
            // _lblBifurcationSamples
            // 
            _lblBifurcationSamples.AutoSize = true;
            _lblBifurcationSamples.Dock = DockStyle.Fill;
            _lblBifurcationSamples.Location = new Point(128, 206);
            _lblBifurcationSamples.Name = "_lblBifurcationSamples";
            _lblBifurcationSamples.Size = new Size(98, 29);
            _lblBifurcationSamples.TabIndex = 15;
            _lblBifurcationSamples.Text = "Bif samples";
            _lblBifurcationSamples.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudBifurcationTransient
            // 
            _nudBifurcationTransient.Dock = DockStyle.Fill;
            _nudBifurcationTransient.Location = new Point(6, 238);
            _nudBifurcationTransient.Margin = new Padding(6, 3, 3, 3);
            _nudBifurcationTransient.Name = "_nudBifurcationTransient";
            _nudBifurcationTransient.Size = new Size(116, 23);
            _nudBifurcationTransient.TabIndex = 16;
            // 
            // _lblBifurcationTransient
            // 
            _lblBifurcationTransient.AutoSize = true;
            _lblBifurcationTransient.Dock = DockStyle.Fill;
            _lblBifurcationTransient.Location = new Point(128, 235);
            _lblBifurcationTransient.Name = "_lblBifurcationTransient";
            _lblBifurcationTransient.Size = new Size(98, 29);
            _lblBifurcationTransient.TabIndex = 17;
            _lblBifurcationTransient.Text = "Bif transient";
            _lblBifurcationTransient.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudBifurcationPlotted
            // 
            _nudBifurcationPlotted.Dock = DockStyle.Fill;
            _nudBifurcationPlotted.Location = new Point(6, 267);
            _nudBifurcationPlotted.Margin = new Padding(6, 3, 3, 3);
            _nudBifurcationPlotted.Name = "_nudBifurcationPlotted";
            _nudBifurcationPlotted.Size = new Size(116, 23);
            _nudBifurcationPlotted.TabIndex = 18;
            // 
            // _lblBifurcationPlotted
            // 
            _lblBifurcationPlotted.AutoSize = true;
            _lblBifurcationPlotted.Dock = DockStyle.Fill;
            _lblBifurcationPlotted.Location = new Point(128, 264);
            _lblBifurcationPlotted.Name = "_lblBifurcationPlotted";
            _lblBifurcationPlotted.Size = new Size(98, 29);
            _lblBifurcationPlotted.TabIndex = 19;
            _lblBifurcationPlotted.Text = "Bif plotted";
            _lblBifurcationPlotted.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudCobwebSteps
            // 
            _nudCobwebSteps.Dock = DockStyle.Fill;
            _nudCobwebSteps.Location = new Point(6, 296);
            _nudCobwebSteps.Margin = new Padding(6, 3, 3, 3);
            _nudCobwebSteps.Name = "_nudCobwebSteps";
            _nudCobwebSteps.Size = new Size(116, 23);
            _nudCobwebSteps.TabIndex = 20;
            // 
            // _lblCobwebSteps
            // 
            _lblCobwebSteps.AutoSize = true;
            _lblCobwebSteps.Dock = DockStyle.Fill;
            _lblCobwebSteps.Location = new Point(128, 293);
            _lblCobwebSteps.Name = "_lblCobwebSteps";
            _lblCobwebSteps.Size = new Size(98, 29);
            _lblCobwebSteps.TabIndex = 21;
            _lblCobwebSteps.Text = "Cobweb шаги";
            _lblCobwebSteps.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _nudZoom
            // 
            _nudZoom.Dock = DockStyle.Fill;
            _nudZoom.Location = new Point(6, 325);
            _nudZoom.Margin = new Padding(6, 3, 3, 3);
            _nudZoom.Name = "_nudZoom";
            _nudZoom.Size = new Size(116, 23);
            _nudZoom.TabIndex = 22;
            // 
            // _lblZoom
            // 
            _lblZoom.AutoSize = true;
            _lblZoom.Dock = DockStyle.Fill;
            _lblZoom.Location = new Point(128, 322);
            _lblZoom.Name = "_lblZoom";
            _lblZoom.Size = new Size(98, 29);
            _lblZoom.TabIndex = 23;
            _lblZoom.Text = "Приближение";
            _lblZoom.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _cbThreads
            // 
            _cbThreads.Dock = DockStyle.Fill;
            _cbThreads.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbThreads.FormattingEnabled = true;
            _cbThreads.Location = new Point(6, 354);
            _cbThreads.Margin = new Padding(6, 3, 3, 3);
            _cbThreads.Name = "_cbThreads";
            _cbThreads.Size = new Size(116, 23);
            _cbThreads.TabIndex = 24;
            // 
            // _lblThreads
            // 
            _lblThreads.AutoSize = true;
            _lblThreads.Dock = DockStyle.Fill;
            _lblThreads.Location = new Point(128, 351);
            _lblThreads.Name = "_lblThreads";
            _lblThreads.Size = new Size(98, 29);
            _lblThreads.TabIndex = 25;
            _lblThreads.Text = "Потоки ЦП";
            _lblThreads.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // _btnSaveImage
            // 
            _pnlControls.SetColumnSpan(_btnSaveImage, 2);
            _btnSaveImage.Dock = DockStyle.Fill;
            _btnSaveImage.Location = new Point(6, 386);
            _btnSaveImage.Margin = new Padding(6, 3, 6, 3);
            _btnSaveImage.Name = "_btnSaveImage";
            _btnSaveImage.Size = new Size(217, 39);
            _btnSaveImage.TabIndex = 26;
            _btnSaveImage.Text = "Сохранить изображение";
            _btnSaveImage.UseVisualStyleBackColor = true;
            _btnSaveImage.Click += btnSaveImage_Click;
            // 
            // _btnPalette
            // 
            _pnlControls.SetColumnSpan(_btnPalette, 2);
            _btnPalette.Dock = DockStyle.Fill;
            _btnPalette.Location = new Point(6, 431);
            _btnPalette.Margin = new Padding(6, 3, 6, 3);
            _btnPalette.Name = "_btnPalette";
            _btnPalette.Size = new Size(217, 39);
            _btnPalette.TabIndex = 27;
            _btnPalette.Text = "Настройка палитры";
            _btnPalette.UseVisualStyleBackColor = true;
            _btnPalette.Click += btnPalette_Click;
            // 
            // _btnRender
            // 
            _pnlControls.SetColumnSpan(_btnRender, 2);
            _btnRender.Dock = DockStyle.Fill;
            _btnRender.Location = new Point(6, 476);
            _btnRender.Margin = new Padding(6, 3, 6, 3);
            _btnRender.Name = "_btnRender";
            _btnRender.Size = new Size(217, 39);
            _btnRender.TabIndex = 28;
            _btnRender.Text = "Перерендерить";
            _btnRender.UseVisualStyleBackColor = true;
            _btnRender.Click += btnRender_Click;
            // 
            // _btnReset
            // 
            _pnlControls.SetColumnSpan(_btnReset, 2);
            _btnReset.Dock = DockStyle.Fill;
            _btnReset.Location = new Point(6, 521);
            _btnReset.Margin = new Padding(6, 3, 6, 3);
            _btnReset.Name = "_btnReset";
            _btnReset.Size = new Size(217, 39);
            _btnReset.TabIndex = 29;
            _btnReset.Text = "Сбросить вид";
            _btnReset.UseVisualStyleBackColor = true;
            _btnReset.Click += btnReset_Click;
            // 
            // _btnState
            // 
            _pnlControls.SetColumnSpan(_btnState, 2);
            _btnState.Dock = DockStyle.Fill;
            _btnState.Location = new Point(6, 566);
            _btnState.Margin = new Padding(6, 3, 6, 3);
            _btnState.Name = "_btnState";
            _btnState.Size = new Size(217, 39);
            _btnState.TabIndex = 30;
            _btnState.Text = "Сохранение/загрузка";
            _btnState.UseVisualStyleBackColor = true;
            _btnState.Click += btnState_Click;
            // 
            // _pbRenderProgress
            // 
            _pnlControls.SetColumnSpan(_pbRenderProgress, 2);
            _pbRenderProgress.Dock = DockStyle.Fill;
            _pbRenderProgress.Location = new Point(6, 611);
            _pbRenderProgress.Margin = new Padding(6, 3, 6, 3);
            _pbRenderProgress.Name = "_pbRenderProgress";
            _pbRenderProgress.Size = new Size(217, 22);
            _pbRenderProgress.TabIndex = 31;
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
            _canvas.TabIndex = 2;
            _canvas.TabStop = false;
            // 
            // FractalLogisticMapForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1086, 591);
            Controls.Add(_canvasHost);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "FractalLogisticMapForm";
            Text = "Орбиты логистического отображения";
            _canvasHost.ResumeLayout(false);
            _canvasHost.PerformLayout();
            _controlsHost.ResumeLayout(false);
            _pnlControls.ResumeLayout(false);
            _pnlControls.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_nudR).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudX0).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudIterations).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudTransient).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudBifurcationRMin).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudBifurcationRMax).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudBifurcationSamples).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudBifurcationTransient).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudBifurcationPlotted).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudCobwebSteps).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudZoom).EndInit();
            ((System.ComponentModel.ISupportInitialize)_canvas).EndInit();
            ResumeLayout(false);
        }
    }
}
