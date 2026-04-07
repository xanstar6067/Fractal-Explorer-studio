namespace FractalExplorer
{
    partial class NewtonPools
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(NewtonPools));
            btnToggleControls = new Button();
            contentPanel = new Panel();
            canvasHost = new Panel();
            controlsHost = new Panel();
            pnlControls = new TableLayoutPanel();
            lblFormula = new Label();
            cbSelector = new ComboBox();
            pnlFormulaGlow = new Panel();
            pnlFormulaInput = new Panel();
            richTextInput = new RichTextBox();
            lblFormulaExample = new Label();
            btnApplyFormula = new Button();
            nudIterations = new NumericUpDown();
            lblIterations = new Label();
            nudZoom = new NumericUpDown();
            lblZoom = new Label();
            cbThreads = new ComboBox();
            lblThreads = new Label();
            cbMethod = new ComboBox();
            lblMethod = new Label();
            nudHouseholderOrder = new NumericUpDown();
            lblHouseholderOrder = new Label();
            btnSave = new Button();
            btnConfigurePalette = new Button();
            btnRender = new Button();
            btnStateManager = new Button();
            lblProgress = new Label();
            progressBar = new ProgressBar();
            lblDebug = new Label();
            richTextDebugOutput = new RichTextBox();
            fractal_bitmap = new PictureBox();
            toolTipFormula = new ToolTip(components);
            contentPanel.SuspendLayout();
            canvasHost.SuspendLayout();
            controlsHost.SuspendLayout();
            pnlControls.SuspendLayout();
            pnlFormulaGlow.SuspendLayout();
            pnlFormulaInput.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudIterations).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudZoom).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudHouseholderOrder).BeginInit();
            ((System.ComponentModel.ISupportInitialize)fractal_bitmap).BeginInit();
            SuspendLayout();
            // 
            // btnToggleControls
            // 
            btnToggleControls.AutoSize = true;
            btnToggleControls.Anchor = ((AnchorStyles)(AnchorStyles.Top | AnchorStyles.Left));
            btnToggleControls.BackColor = Color.FromArgb(235, 32, 32, 32);
            btnToggleControls.FlatStyle = FlatStyle.Popup;
            btnToggleControls.ForeColor = Color.White;
            btnToggleControls.Location = new Point(256, 12);
            btnToggleControls.Name = "btnToggleControls";
            btnToggleControls.Size = new Size(44, 32);
            btnToggleControls.TabIndex = 0;
            btnToggleControls.Text = "✕";
            btnToggleControls.UseVisualStyleBackColor = true;
            btnToggleControls.Click += btnToggleControls_Click;
            // 
            // contentPanel
            // 
            contentPanel.Controls.Add(canvasHost);
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.Location = new Point(0, 0);
            contentPanel.Name = "contentPanel";
            contentPanel.Size = new Size(1084, 636);
            contentPanel.TabIndex = 0;
            // 
            // canvasHost
            // 
            canvasHost.Controls.Add(controlsHost);
            canvasHost.Controls.Add(btnToggleControls);
            canvasHost.Controls.Add(fractal_bitmap);
            canvasHost.Dock = DockStyle.Fill;
            canvasHost.Location = new Point(0, 0);
            canvasHost.Name = "canvasHost";
            canvasHost.Size = new Size(1084, 636);
            canvasHost.TabIndex = 0;
            // 
            // controlsHost
            // 
            controlsHost.Anchor = ((AnchorStyles)(((AnchorStyles.Top | AnchorStyles.Bottom) | AnchorStyles.Left)));
            controlsHost.BackColor = SystemColors.Control;
            controlsHost.BorderStyle = BorderStyle.FixedSingle;
            controlsHost.Controls.Add(pnlControls);
            controlsHost.Location = new Point(0, 0);
            controlsHost.Name = "controlsHost";
            controlsHost.Size = new Size(231, 636);
            controlsHost.TabIndex = 0;
            // 
            // pnlControls
            // 
            pnlControls.ColumnCount = 2;
            pnlControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            pnlControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            pnlControls.Controls.Add(lblFormula, 0, 0);
            pnlControls.Controls.Add(cbSelector, 0, 1);
            pnlControls.Controls.Add(pnlFormulaGlow, 0, 2);
            pnlControls.Controls.Add(btnApplyFormula, 0, 3);
            pnlControls.Controls.Add(nudIterations, 0, 4);
            pnlControls.Controls.Add(lblIterations, 1, 4);
            pnlControls.Controls.Add(nudZoom, 0, 5);
            pnlControls.Controls.Add(lblZoom, 1, 5);
            pnlControls.Controls.Add(cbThreads, 0, 6);
            pnlControls.Controls.Add(lblThreads, 1, 6);
            pnlControls.Controls.Add(cbMethod, 0, 7);
            pnlControls.Controls.Add(lblMethod, 1, 7);
            pnlControls.Controls.Add(nudHouseholderOrder, 0, 8);
            pnlControls.Controls.Add(lblHouseholderOrder, 1, 8);
            pnlControls.Controls.Add(btnSave, 0, 9);
            pnlControls.Controls.Add(btnConfigurePalette, 0, 10);
            pnlControls.Controls.Add(btnRender, 0, 11);
            pnlControls.Controls.Add(btnStateManager, 0, 12);
            pnlControls.Controls.Add(lblProgress, 0, 13);
            pnlControls.Controls.Add(progressBar, 0, 14);
            pnlControls.Controls.Add(lblDebug, 0, 15);
            pnlControls.Controls.Add(richTextDebugOutput, 0, 16);
            pnlControls.Dock = DockStyle.Fill;
            pnlControls.Location = new Point(0, 0);
            pnlControls.Name = "pnlControls";
            pnlControls.RowCount = 18;
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 115F));
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle());
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            pnlControls.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F));
            pnlControls.Size = new Size(231, 636);
            pnlControls.TabIndex = 0;
            // 
            // lblFormula
            // 
            lblFormula.AutoSize = true;
            pnlControls.SetColumnSpan(lblFormula, 2);
            lblFormula.Dock = DockStyle.Fill;
            lblFormula.Location = new Point(3, 0);
            lblFormula.Name = "lblFormula";
            lblFormula.Size = new Size(225, 25);
            lblFormula.TabIndex = 0;
            lblFormula.Text = "Выбрать полином/формулу";
            lblFormula.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // cbSelector
            // 
            pnlControls.SetColumnSpan(cbSelector, 2);
            cbSelector.Dock = DockStyle.Fill;
            cbSelector.FormattingEnabled = true;
            cbSelector.Location = new Point(6, 28);
            cbSelector.Margin = new Padding(6, 3, 6, 3);
            cbSelector.Name = "cbSelector";
            cbSelector.Size = new Size(219, 23);
            cbSelector.TabIndex = 1;
            // 
            // 
            // pnlFormulaGlow
            // 
            pnlControls.SetColumnSpan(pnlFormulaGlow, 2);
            pnlFormulaGlow.Controls.Add(pnlFormulaInput);
            pnlFormulaGlow.Dock = DockStyle.Fill;
            pnlFormulaGlow.Location = new Point(6, 57);
            pnlFormulaGlow.Margin = new Padding(6, 3, 6, 3);
            pnlFormulaGlow.Name = "pnlFormulaGlow";
            pnlFormulaGlow.Padding = new Padding(3);
            pnlFormulaGlow.Size = new Size(219, 109);
            pnlFormulaGlow.TabIndex = 2;
            // 
            // pnlFormulaInput
            // 
            pnlFormulaInput.Controls.Add(richTextInput);
            pnlFormulaInput.Controls.Add(lblFormulaExample);
            pnlFormulaInput.Dock = DockStyle.Fill;
            pnlFormulaInput.Location = new Point(3, 3);
            pnlFormulaInput.Margin = new Padding(0);
            pnlFormulaInput.Name = "pnlFormulaInput";
            pnlFormulaInput.Padding = new Padding(6);
            pnlFormulaInput.Size = new Size(213, 103);
            pnlFormulaInput.TabIndex = 0;
            // 
            // richTextInput
            // 
            richTextInput.BorderStyle = BorderStyle.None;
            richTextInput.Dock = DockStyle.Fill;
            richTextInput.Location = new Point(6, 28);
            richTextInput.Name = "richTextInput";
            richTextInput.Size = new Size(205, 73);
            richTextInput.TabIndex = 1;
            richTextInput.Text = "z^3 - 1";
            // 
            // lblFormulaExample
            // 
            lblFormulaExample.Dock = DockStyle.Top;
            lblFormulaExample.Location = new Point(6, 6);
            lblFormulaExample.Name = "lblFormulaExample";
            lblFormulaExample.Size = new Size(205, 22);
            lblFormulaExample.TabIndex = 0;
            lblFormulaExample.Text = "Пример: z^3 - 1";
            lblFormulaExample.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // btnApplyFormula
            // 
            pnlControls.SetColumnSpan(btnApplyFormula, 2);
            btnApplyFormula.Dock = DockStyle.Fill;
            btnApplyFormula.Location = new Point(6, 172);
            btnApplyFormula.Margin = new Padding(6, 3, 6, 3);
            btnApplyFormula.Name = "btnApplyFormula";
            btnApplyFormula.Size = new Size(219, 23);
            btnApplyFormula.TabIndex = 3;
            btnApplyFormula.Text = "Применить формулу";
            btnApplyFormula.UseVisualStyleBackColor = true;
            // 
            // nudIterations
            // 
            nudIterations.Dock = DockStyle.Fill;
            nudIterations.Location = new Point(6, 201);
            nudIterations.Margin = new Padding(6, 3, 3, 3);
            nudIterations.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            nudIterations.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudIterations.Name = "nudIterations";
            nudIterations.Size = new Size(118, 23);
            nudIterations.TabIndex = 4;
            nudIterations.Value = new decimal(new int[] { 500, 0, 0, 0 });
            // 
            // lblIterations
            // 
            lblIterations.AutoSize = true;
            lblIterations.Dock = DockStyle.Fill;
            lblIterations.Location = new Point(130, 198);
            lblIterations.Name = "lblIterations";
            lblIterations.Size = new Size(98, 29);
            lblIterations.TabIndex = 5;
            lblIterations.Text = "Итерации";
            lblIterations.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // nudZoom
            // 
            nudZoom.DecimalPlaces = 2;
            nudZoom.Dock = DockStyle.Fill;
            nudZoom.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            nudZoom.Location = new Point(6, 230);
            nudZoom.Margin = new Padding(6, 3, 3, 3);
            nudZoom.Maximum = new decimal(new int[] { 268435455, 1042612833, 542101086, 0 });
            nudZoom.Name = "nudZoom";
            nudZoom.Size = new Size(118, 23);
            nudZoom.TabIndex = 6;
            nudZoom.Value = new decimal(new int[] { 100, 0, 0, 0 });
            // 
            // lblZoom
            // 
            lblZoom.AutoSize = true;
            lblZoom.Dock = DockStyle.Fill;
            lblZoom.Location = new Point(130, 227);
            lblZoom.Name = "lblZoom";
            lblZoom.Size = new Size(98, 29);
            lblZoom.TabIndex = 7;
            lblZoom.Text = "Приближение";
            lblZoom.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // cbThreads
            // 
            cbThreads.Dock = DockStyle.Fill;
            cbThreads.DropDownStyle = ComboBoxStyle.DropDownList;
            cbThreads.FormattingEnabled = true;
            cbThreads.Location = new Point(6, 259);
            cbThreads.Margin = new Padding(6, 3, 3, 3);
            cbThreads.Name = "cbThreads";
            cbThreads.Size = new Size(118, 23);
            cbThreads.TabIndex = 8;
            // 
            // lblThreads
            // 
            lblThreads.AutoSize = true;
            lblThreads.Dock = DockStyle.Fill;
            lblThreads.Location = new Point(130, 256);
            lblThreads.Name = "lblThreads";
            lblThreads.Size = new Size(98, 29);
            lblThreads.TabIndex = 9;
            lblThreads.Text = "Потоки ЦП";
            lblThreads.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // 
            // cbMethod
            // 
            cbMethod.Dock = DockStyle.Fill;
            cbMethod.DropDownStyle = ComboBoxStyle.DropDownList;
            cbMethod.FormattingEnabled = true;
            cbMethod.Items.AddRange(new object[] { "Newton", "Halley", "Householder" });
            cbMethod.Location = new Point(6, 288);
            cbMethod.Margin = new Padding(6, 3, 3, 3);
            cbMethod.Name = "cbMethod";
            cbMethod.Size = new Size(118, 23);
            cbMethod.TabIndex = 10;
            // 
            // lblMethod
            // 
            lblMethod.AutoSize = true;
            lblMethod.Dock = DockStyle.Fill;
            lblMethod.Location = new Point(130, 285);
            lblMethod.Name = "lblMethod";
            lblMethod.Size = new Size(98, 29);
            lblMethod.TabIndex = 11;
            lblMethod.Text = "Метод";
            lblMethod.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // nudHouseholderOrder
            // 
            nudHouseholderOrder.Dock = DockStyle.Fill;
            nudHouseholderOrder.Location = new Point(6, 317);
            nudHouseholderOrder.Margin = new Padding(6, 3, 3, 3);
            nudHouseholderOrder.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
            nudHouseholderOrder.Name = "nudHouseholderOrder";
            nudHouseholderOrder.Size = new Size(118, 23);
            nudHouseholderOrder.TabIndex = 12;
            nudHouseholderOrder.Value = new decimal(new int[] { 3, 0, 0, 0 });
            // 
            // lblHouseholderOrder
            // 
            lblHouseholderOrder.AutoSize = true;
            lblHouseholderOrder.Dock = DockStyle.Fill;
            lblHouseholderOrder.Location = new Point(130, 314);
            lblHouseholderOrder.Name = "lblHouseholderOrder";
            lblHouseholderOrder.Size = new Size(98, 29);
            lblHouseholderOrder.TabIndex = 13;
            lblHouseholderOrder.Text = "Порядок";
            lblHouseholderOrder.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // btnSave
            // 
            pnlControls.SetColumnSpan(btnSave, 2);
            btnSave.Dock = DockStyle.Fill;
            btnSave.Location = new Point(6, 346);
            btnSave.Margin = new Padding(6, 3, 6, 3);
            btnSave.Name = "btnSave";
            btnSave.Size = new Size(219, 39);
            btnSave.TabIndex = 14;
            btnSave.Text = "Сохранить изображение";
            btnSave.UseVisualStyleBackColor = true;
            btnSave.Click += btnOpenSaveManager_Click;
            // 
            // btnConfigurePalette
            // 
            pnlControls.SetColumnSpan(btnConfigurePalette, 2);
            btnConfigurePalette.Dock = DockStyle.Fill;
            btnConfigurePalette.Location = new Point(6, 391);
            btnConfigurePalette.Margin = new Padding(6, 3, 6, 3);
            btnConfigurePalette.Name = "btnConfigurePalette";
            btnConfigurePalette.Size = new Size(219, 39);
            btnConfigurePalette.TabIndex = 15;
            btnConfigurePalette.Text = "Настроить палитру";
            btnConfigurePalette.UseVisualStyleBackColor = true;
            // 
            // btnRender
            // 
            pnlControls.SetColumnSpan(btnRender, 2);
            btnRender.Dock = DockStyle.Fill;
            btnRender.Location = new Point(6, 436);
            btnRender.Margin = new Padding(6, 3, 6, 3);
            btnRender.Name = "btnRender";
            btnRender.Size = new Size(219, 39);
            btnRender.TabIndex = 16;
            btnRender.Text = "Запустить рендер";
            btnRender.UseVisualStyleBackColor = true;
            // 
            // btnStateManager
            // 
            pnlControls.SetColumnSpan(btnStateManager, 2);
            btnStateManager.Dock = DockStyle.Fill;
            btnStateManager.Location = new Point(6, 481);
            btnStateManager.Margin = new Padding(6, 3, 6, 3);
            btnStateManager.Name = "btnStateManager";
            btnStateManager.Size = new Size(219, 39);
            btnStateManager.TabIndex = 17;
            btnStateManager.Text = "Менеджер сохранений";
            btnStateManager.UseVisualStyleBackColor = true;
            btnStateManager.Click += btnStateManager_Click;
            // 
            // lblProgress
            // 
            lblProgress.AutoSize = true;
            pnlControls.SetColumnSpan(lblProgress, 2);
            lblProgress.Dock = DockStyle.Fill;
            lblProgress.Location = new Point(3, 523);
            lblProgress.Name = "lblProgress";
            lblProgress.Size = new Size(225, 20);
            lblProgress.TabIndex = 18;
            lblProgress.Text = "Обработка";
            lblProgress.TextAlign = ContentAlignment.BottomCenter;
            // 
            // progressBar
            // 
            pnlControls.SetColumnSpan(progressBar, 2);
            progressBar.Dock = DockStyle.Fill;
            progressBar.Location = new Point(6, 546);
            progressBar.Margin = new Padding(6, 3, 6, 3);
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(219, 24);
            progressBar.TabIndex = 19;
            // 
            // lblDebug
            // 
            lblDebug.AutoSize = true;
            pnlControls.SetColumnSpan(lblDebug, 2);
            lblDebug.Dock = DockStyle.Fill;
            lblDebug.Location = new Point(3, 573);
            lblDebug.Name = "lblDebug";
            lblDebug.Size = new Size(225, 20);
            lblDebug.TabIndex = 20;
            lblDebug.Text = "Отладка";
            lblDebug.TextAlign = ContentAlignment.BottomCenter;
            // 
            // richTextDebugOutput
            // 
            pnlControls.SetColumnSpan(richTextDebugOutput, 2);
            richTextDebugOutput.Dock = DockStyle.Fill;
            richTextDebugOutput.Location = new Point(6, 596);
            richTextDebugOutput.Margin = new Padding(6, 3, 6, 3);
            richTextDebugOutput.Name = "richTextDebugOutput";
            richTextDebugOutput.ReadOnly = true;
            richTextDebugOutput.Size = new Size(219, 87);
            richTextDebugOutput.TabIndex = 21;
            richTextDebugOutput.Text = "";
            // 
            // fractal_bitmap
            // 
            fractal_bitmap.Dock = DockStyle.Fill;
            fractal_bitmap.Location = new Point(0, 0);
            fractal_bitmap.Name = "fractal_bitmap";
            fractal_bitmap.Size = new Size(853, 636);
            fractal_bitmap.TabIndex = 1;
            fractal_bitmap.TabStop = false;
            // 
            // NewtonPools
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1084, 636);
            Controls.Add(contentPanel);
            Icon = (Icon)resources.GetObject("$this.Icon");
            MinimumSize = new Size(1100, 675);
            Name = "NewtonPools";
            Text = "Бассейны Ньютона";
            controlsHost.ResumeLayout(false);
            contentPanel.ResumeLayout(false);
            canvasHost.ResumeLayout(false);
            canvasHost.PerformLayout();
            pnlControls.ResumeLayout(false);
            pnlControls.PerformLayout();
            pnlFormulaGlow.ResumeLayout(false);
            pnlFormulaInput.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)nudIterations).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudZoom).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudHouseholderOrder).EndInit();
            ((System.ComponentModel.ISupportInitialize)fractal_bitmap).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Button btnToggleControls;
        private System.Windows.Forms.Panel contentPanel;
        private System.Windows.Forms.Panel canvasHost;
        private System.Windows.Forms.Panel controlsHost;
        private System.Windows.Forms.TableLayoutPanel pnlControls;
        private System.Windows.Forms.Label lblFormula;
        private System.Windows.Forms.ComboBox cbSelector;
        private System.Windows.Forms.Panel pnlFormulaGlow;
        private System.Windows.Forms.Panel pnlFormulaInput;
        private System.Windows.Forms.RichTextBox richTextInput;
        private System.Windows.Forms.Label lblFormulaExample;
        private System.Windows.Forms.Button btnApplyFormula;
        private System.Windows.Forms.NumericUpDown nudIterations;
        private System.Windows.Forms.Label lblIterations;
        private System.Windows.Forms.NumericUpDown nudZoom;
        private System.Windows.Forms.Label lblZoom;
        private System.Windows.Forms.ComboBox cbThreads;
        private System.Windows.Forms.Label lblThreads;
        private System.Windows.Forms.ComboBox cbMethod;
        private System.Windows.Forms.Label lblMethod;
        private System.Windows.Forms.NumericUpDown nudHouseholderOrder;
        private System.Windows.Forms.Label lblHouseholderOrder;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnConfigurePalette;
        private System.Windows.Forms.Button btnRender;
        private System.Windows.Forms.Button btnStateManager;
        private System.Windows.Forms.Label lblProgress;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblDebug;
        private System.Windows.Forms.RichTextBox richTextDebugOutput;
        private System.Windows.Forms.PictureBox fractal_bitmap;
        private System.Windows.Forms.ToolTip toolTipFormula;
    }
}
