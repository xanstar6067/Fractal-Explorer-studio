namespace FractalExplorer.Forms.Common
{
    partial class ColorPickerPanelForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            // ── объявления ────────────────────────────────────────────────────
            tableMain = new TableLayoutPanel();

            // левая колонка
            tableLeft = new TableLayoutPanel();
            panelColorMatrixContainer = new Panel();
            tableColorArea = new TableLayoutPanel();
            pnlColorMatrix = new Panel();
            pnlHueSlider = new Panel();
            tableRgb = new TableLayoutPanel();
            lblRed = new Label();
            trkRed = new TrackBar();
            lblRedValue = new Label();
            lblGreen = new Label();
            trkGreen = new TrackBar();
            lblGreenValue = new Label();
            lblBlue = new Label();
            trkBlue = new TrackBar();
            lblBlueValue = new Label();
            tableEyedropperPreview = new TableLayoutPanel();
            btnEyedropper = new Button();
            tablePreviewStrip = new TableLayoutPanel();
            pnlCurrentColor = new Panel();
            lblCurrentHexValue = new Label();
            pnlNewColor = new Panel();
            lblNewHexValue = new Label();

            // правая колонка
            grpPalettes = new GroupBox();
            tablePalettes = new TableLayoutPanel();
            lblStandard = new Label();
            tableStandardColors = new TableLayoutPanel();
            lblCustom = new Label();
            tableCustomColors = new TableLayoutPanel();
            btnAddToCustomColors = new Button();
            grpHexTools = new GroupBox();
            tableHexTools = new TableLayoutPanel();
            lblHexInput = new Label();
            txtHexInput = new TextBox();
            panelHexButtons = new FlowLayoutPanel();
            btnApplyHex = new Button();
            btnCopyCurrentHex = new Button();
            btnSetTransparent = new Button();

            // нижняя строка
            panelButtons = new FlowLayoutPanel();
            btnOk = new Button();
            btnCancel = new Button();

            // ── SuspendLayout ─────────────────────────────────────────────────
            tableMain.SuspendLayout();
            tableLeft.SuspendLayout();
            panelColorMatrixContainer.SuspendLayout();
            tableColorArea.SuspendLayout();
            tableRgb.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)trkRed).BeginInit();
            ((System.ComponentModel.ISupportInitialize)trkGreen).BeginInit();
            ((System.ComponentModel.ISupportInitialize)trkBlue).BeginInit();
            tableEyedropperPreview.SuspendLayout();
            tablePreviewStrip.SuspendLayout();
            grpPalettes.SuspendLayout();
            tablePalettes.SuspendLayout();
            grpHexTools.SuspendLayout();
            tableHexTools.SuspendLayout();
            panelHexButtons.SuspendLayout();
            panelButtons.SuspendLayout();
            SuspendLayout();

            // ══════════════════════════════════════════════════════════════════
            // tableMain — 2 col x 2 row
            //   col 0: левая колонка (340px фикс.)
            //   col 1: правая колонка (fill)
            //   row 0: основной контент (fill)
            //   row 1: кнопки (36px фикс.)
            // ══════════════════════════════════════════════════════════════════
            tableMain.ColumnCount = 2;
            tableMain.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340F));
            tableMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableMain.Controls.Add(tableLeft, 0, 0);
            tableMain.Controls.Add(grpPalettes, 1, 0);
            tableMain.Controls.Add(panelButtons, 0, 1);
            tableMain.SetColumnSpan(panelButtons, 2);
            tableMain.Dock = DockStyle.Fill;
            tableMain.Location = new Point(0, 0);
            tableMain.Name = "tableMain";
            tableMain.Padding = new Padding(10);
            tableMain.RowCount = 2;
            tableMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            tableMain.Size = new Size(780, 450);
            tableMain.TabIndex = 0;

            // ══════════════════════════════════════════════════════════════════
            // tableLeft — левая колонка, 3 строки:
            //   row 0: panelColorMatrixContainer (fill)
            //   row 1: tableRgb                  (96px фикс.)
            //   row 2: tableEyedropperPreview    (52px фикс.)
            // ══════════════════════════════════════════════════════════════════
            tableLeft.ColumnCount = 1;
            tableLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLeft.Controls.Add(panelColorMatrixContainer, 0, 0);
            tableLeft.Controls.Add(tableRgb, 0, 1);
            tableLeft.Controls.Add(tableEyedropperPreview, 0, 2);
            tableLeft.Dock = DockStyle.Fill;
            tableLeft.Name = "tableLeft";
            tableLeft.Padding = new Padding(0, 0, 8, 0);
            tableLeft.RowCount = 3;
            tableLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLeft.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
            tableLeft.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            tableLeft.TabIndex = 0;

            // ══════════════════════════════════════════════════════════════════
            // panelColorMatrixContainer
            // ══════════════════════════════════════════════════════════════════
            panelColorMatrixContainer.Controls.Add(tableColorArea);
            panelColorMatrixContainer.Dock = DockStyle.Fill;
            panelColorMatrixContainer.Name = "panelColorMatrixContainer";
            panelColorMatrixContainer.TabIndex = 0;

            // ══════════════════════════════════════════════════════════════════
            // tableColorArea — 2 колонки:
            //   col 0: матрица цвета (fill)
            //   col 1: hue slider    (38px fixed)
            // ══════════════════════════════════════════════════════════════════
            tableColorArea.ColumnCount = 2;
            tableColorArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableColorArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38F));
            tableColorArea.Controls.Add(pnlColorMatrix, 0, 0);
            tableColorArea.Controls.Add(pnlHueSlider, 1, 0);
            tableColorArea.Dock = DockStyle.Fill;
            tableColorArea.Location = new Point(0, 0);
            tableColorArea.Margin = new Padding(0);
            tableColorArea.Name = "tableColorArea";
            tableColorArea.Padding = new Padding(0);
            tableColorArea.RowCount = 1;
            tableColorArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableColorArea.TabIndex = 0;

            // ── pnlColorMatrix ────────────────────────────────────────────────
            pnlColorMatrix.BorderStyle = BorderStyle.FixedSingle;
            pnlColorMatrix.Cursor = Cursors.Cross;
            pnlColorMatrix.Dock = DockStyle.Fill;
            pnlColorMatrix.Margin = new Padding(0, 0, 6, 0);
            pnlColorMatrix.Name = "pnlColorMatrix";
            pnlColorMatrix.TabIndex = 0;
            pnlColorMatrix.Paint += pnlColorMatrix_Paint;
            pnlColorMatrix.MouseDown += pnlColorMatrix_MouseDown;
            pnlColorMatrix.MouseMove += pnlColorMatrix_MouseMove;

            // ── pnlHueSlider ──────────────────────────────────────────────────
            pnlHueSlider.BorderStyle = BorderStyle.FixedSingle;
            pnlHueSlider.Cursor = Cursors.Hand;
            pnlHueSlider.Dock = DockStyle.Fill;
            pnlHueSlider.Margin = new Padding(0);
            pnlHueSlider.MinimumSize = new Size(38, 0);
            pnlHueSlider.Name = "pnlHueSlider";
            pnlHueSlider.TabIndex = 1;
            pnlHueSlider.Paint += pnlHueSlider_Paint;
            pnlHueSlider.MouseDown += pnlHueSlider_MouseDown;
            pnlHueSlider.MouseMove += pnlHueSlider_MouseMove;

            // ══════════════════════════════════════════════════════════════════
            // tableRgb — R/G/B слайдеры
            // ══════════════════════════════════════════════════════════════════
            tableRgb.ColumnCount = 3;
            tableRgb.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
            tableRgb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableRgb.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            tableRgb.Controls.Add(lblRed, 0, 0);
            tableRgb.Controls.Add(trkRed, 1, 0);
            tableRgb.Controls.Add(lblRedValue, 2, 0);
            tableRgb.Controls.Add(lblGreen, 0, 1);
            tableRgb.Controls.Add(trkGreen, 1, 1);
            tableRgb.Controls.Add(lblGreenValue, 2, 1);
            tableRgb.Controls.Add(lblBlue, 0, 2);
            tableRgb.Controls.Add(trkBlue, 1, 2);
            tableRgb.Controls.Add(lblBlueValue, 2, 2);
            tableRgb.Dock = DockStyle.Fill;
            tableRgb.Name = "tableRgb";
            tableRgb.RowCount = 3;
            tableRgb.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3F));
            tableRgb.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3F));
            tableRgb.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3F));
            tableRgb.TabIndex = 1;

            // R
            lblRed.AutoSize = true;
            lblRed.Dock = DockStyle.Fill;
            lblRed.Name = "lblRed";
            lblRed.TabIndex = 0;
            lblRed.Text = "R";
            lblRed.TextAlign = ContentAlignment.MiddleLeft;

            trkRed.AutoSize = false;
            trkRed.Dock = DockStyle.Fill;
            trkRed.LargeChange = 16;
            trkRed.Margin = new Padding(3, 4, 3, 0);
            trkRed.Maximum = 255;
            trkRed.Name = "trkRed";
            trkRed.TabIndex = 1;
            trkRed.TickFrequency = 16;
            trkRed.Scroll += trackColor_Scroll;

            lblRedValue.AutoSize = true;
            lblRedValue.Dock = DockStyle.Fill;
            lblRedValue.Name = "lblRedValue";
            lblRedValue.TabIndex = 2;
            lblRedValue.Text = "0";
            lblRedValue.TextAlign = ContentAlignment.MiddleRight;

            // G
            lblGreen.AutoSize = true;
            lblGreen.Dock = DockStyle.Fill;
            lblGreen.Name = "lblGreen";
            lblGreen.TabIndex = 3;
            lblGreen.Text = "G";
            lblGreen.TextAlign = ContentAlignment.MiddleLeft;

            trkGreen.AutoSize = false;
            trkGreen.Dock = DockStyle.Fill;
            trkGreen.LargeChange = 16;
            trkGreen.Margin = new Padding(3, 4, 3, 0);
            trkGreen.Maximum = 255;
            trkGreen.Name = "trkGreen";
            trkGreen.TabIndex = 4;
            trkGreen.TickFrequency = 16;
            trkGreen.Scroll += trackColor_Scroll;

            lblGreenValue.AutoSize = true;
            lblGreenValue.Dock = DockStyle.Fill;
            lblGreenValue.Name = "lblGreenValue";
            lblGreenValue.TabIndex = 5;
            lblGreenValue.Text = "0";
            lblGreenValue.TextAlign = ContentAlignment.MiddleRight;

            // B
            lblBlue.AutoSize = true;
            lblBlue.Dock = DockStyle.Fill;
            lblBlue.Name = "lblBlue";
            lblBlue.TabIndex = 6;
            lblBlue.Text = "B";
            lblBlue.TextAlign = ContentAlignment.MiddleLeft;

            trkBlue.AutoSize = false;
            trkBlue.Dock = DockStyle.Fill;
            trkBlue.LargeChange = 16;
            trkBlue.Margin = new Padding(3, 4, 3, 0);
            trkBlue.Maximum = 255;
            trkBlue.Name = "trkBlue";
            trkBlue.TabIndex = 7;
            trkBlue.TickFrequency = 16;
            trkBlue.Scroll += trackColor_Scroll;

            lblBlueValue.AutoSize = true;
            lblBlueValue.Dock = DockStyle.Fill;
            lblBlueValue.Name = "lblBlueValue";
            lblBlueValue.TabIndex = 8;
            lblBlueValue.Text = "0";
            lblBlueValue.TextAlign = ContentAlignment.MiddleRight;

            // ══════════════════════════════════════════════════════════════════
            // tableEyedropperPreview — нижняя строка левой колонки
            // ══════════════════════════════════════════════════════════════════
            tableEyedropperPreview.ColumnCount = 2;
            tableEyedropperPreview.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            tableEyedropperPreview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableEyedropperPreview.Controls.Add(btnEyedropper, 0, 0);
            tableEyedropperPreview.Controls.Add(tablePreviewStrip, 1, 0);
            tableEyedropperPreview.Dock = DockStyle.Fill;
            tableEyedropperPreview.Name = "tableEyedropperPreview";
            tableEyedropperPreview.RowCount = 1;
            tableEyedropperPreview.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableEyedropperPreview.TabIndex = 2;

            // ── btnEyedropper ─────────────────────────────────────────────────
            btnEyedropper.Dock = DockStyle.Fill;
            btnEyedropper.Margin = new Padding(0, 0, 6, 0);
            btnEyedropper.Name = "btnEyedropper";
            btnEyedropper.TabIndex = 0;
            btnEyedropper.Text = "Пипетка";
            btnEyedropper.UseVisualStyleBackColor = true;
            btnEyedropper.Click += btnEyedropper_Click;

            // ══════════════════════════════════════════════════════════════════
            // tablePreviewStrip — два цветовых блока [текущий | новый]
            // ══════════════════════════════════════════════════════════════════
            tablePreviewStrip.ColumnCount = 2;
            tablePreviewStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tablePreviewStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tablePreviewStrip.Controls.Add(pnlCurrentColor, 0, 0);
            tablePreviewStrip.Controls.Add(lblCurrentHexValue, 0, 1);
            tablePreviewStrip.Controls.Add(pnlNewColor, 1, 0);
            tablePreviewStrip.Controls.Add(lblNewHexValue, 1, 1);
            tablePreviewStrip.Dock = DockStyle.Fill;
            tablePreviewStrip.Name = "tablePreviewStrip";
            tablePreviewStrip.RowCount = 2;
            tablePreviewStrip.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tablePreviewStrip.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
            tablePreviewStrip.TabIndex = 1;

            // ── pnlCurrentColor ───────────────────────────────────────────────
            pnlCurrentColor.BorderStyle = BorderStyle.FixedSingle;
            pnlCurrentColor.Dock = DockStyle.Fill;
            pnlCurrentColor.Margin = new Padding(0, 0, 2, 0);
            pnlCurrentColor.Name = "pnlCurrentColor";
            pnlCurrentColor.TabIndex = 0;

            // ── lblCurrentHexValue ────────────────────────────────────────────
            lblCurrentHexValue.AutoSize = true;
            lblCurrentHexValue.Dock = DockStyle.Fill;
            lblCurrentHexValue.Name = "lblCurrentHexValue";
            lblCurrentHexValue.TabIndex = 1;
            lblCurrentHexValue.Text = "#...";
            lblCurrentHexValue.TextAlign = ContentAlignment.MiddleCenter;

            // ── pnlNewColor ───────────────────────────────────────────────────
            pnlNewColor.BorderStyle = BorderStyle.FixedSingle;
            pnlNewColor.Dock = DockStyle.Fill;
            pnlNewColor.Margin = new Padding(2, 0, 0, 0);
            pnlNewColor.Name = "pnlNewColor";
            pnlNewColor.TabIndex = 2;

            // ── lblNewHexValue ────────────────────────────────────────────────
            lblNewHexValue.AutoSize = true;
            lblNewHexValue.Dock = DockStyle.Fill;
            lblNewHexValue.Name = "lblNewHexValue";
            lblNewHexValue.TabIndex = 3;
            lblNewHexValue.Text = "#...";
            lblNewHexValue.TextAlign = ContentAlignment.MiddleCenter;

            // ══════════════════════════════════════════════════════════════════
            // grpPalettes
            // ══════════════════════════════════════════════════════════════════
            grpPalettes.Controls.Add(tablePalettes);
            grpPalettes.Dock = DockStyle.Fill;
            grpPalettes.Name = "grpPalettes";
            grpPalettes.Padding = new Padding(6, 16, 6, 6);
            grpPalettes.TabIndex = 1;
            grpPalettes.TabStop = false;
            grpPalettes.Text = "Палитры";

            // ── tablePalettes ─────────────────────────────────────────────────
            tablePalettes.ColumnCount = 1;
            tablePalettes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tablePalettes.Controls.Add(lblStandard, 0, 0);
            tablePalettes.Controls.Add(tableStandardColors, 0, 1);
            tablePalettes.Controls.Add(lblCustom, 0, 2);
            tablePalettes.Controls.Add(tableCustomColors, 0, 3);
            tablePalettes.Controls.Add(btnAddToCustomColors, 0, 4);
            tablePalettes.Controls.Add(grpHexTools, 0, 5);
            tablePalettes.Dock = DockStyle.Fill;
            tablePalettes.Name = "tablePalettes";
            tablePalettes.RowCount = 6;
            tablePalettes.RowStyles.Add(new RowStyle());
            tablePalettes.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
            tablePalettes.RowStyles.Add(new RowStyle());
            tablePalettes.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            tablePalettes.RowStyles.Add(new RowStyle());
            tablePalettes.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tablePalettes.TabIndex = 0;

            // ── lblStandard ───────────────────────────────────────────────────
            lblStandard.AutoSize = true;
            lblStandard.Margin = new Padding(3, 4, 3, 2);
            lblStandard.Name = "lblStandard";
            lblStandard.TabIndex = 0;
            lblStandard.Text = "Основные цвета:";

            // ── tableStandardColors ───────────────────────────────────────────
            tableStandardColors.Anchor = AnchorStyles.None;
            tableStandardColors.Margin = new Padding(0);
            tableStandardColors.Name = "tableStandardColors";
            tableStandardColors.TabIndex = 1;

            // ── lblCustom ─────────────────────────────────────────────────────
            lblCustom.AutoSize = true;
            lblCustom.Margin = new Padding(3, 4, 3, 2);
            lblCustom.Name = "lblCustom";
            lblCustom.TabIndex = 2;
            lblCustom.Text = "Пользовательские:";

            // ── tableCustomColors ─────────────────────────────────────────────
            tableCustomColors.Anchor = AnchorStyles.None;
            tableCustomColors.Margin = new Padding(0);
            tableCustomColors.Name = "tableCustomColors";
            tableCustomColors.TabIndex = 3;

            // ── btnAddToCustomColors ──────────────────────────────────────────
            btnAddToCustomColors.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnAddToCustomColors.Margin = new Padding(3, 4, 3, 0);
            btnAddToCustomColors.Name = "btnAddToCustomColors";
            btnAddToCustomColors.Size = new Size(148, 23);
            btnAddToCustomColors.TabIndex = 4;
            btnAddToCustomColors.Text = "Добавить цвет";
            btnAddToCustomColors.UseVisualStyleBackColor = true;
            btnAddToCustomColors.Click += btnAddToCustomColors_Click;

            // ── grpHexTools ──────────────────────────────────────────────────
            grpHexTools.Controls.Add(tableHexTools);
            grpHexTools.Dock = DockStyle.Fill;
            grpHexTools.Margin = new Padding(0, 8, 0, 0);
            grpHexTools.Name = "grpHexTools";
            grpHexTools.Padding = new Padding(8);
            grpHexTools.TabIndex = 5;
            grpHexTools.TabStop = false;
            grpHexTools.Text = "HEX";

            // ── tableHexTools ────────────────────────────────────────────────
            tableHexTools.ColumnCount = 1;
            tableHexTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableHexTools.Controls.Add(lblHexInput, 0, 0);
            tableHexTools.Controls.Add(txtHexInput, 0, 1);
            tableHexTools.Controls.Add(panelHexButtons, 0, 2);
            tableHexTools.Dock = DockStyle.Fill;
            tableHexTools.Name = "tableHexTools";
            tableHexTools.RowCount = 4;
            tableHexTools.RowStyles.Add(new RowStyle());
            tableHexTools.RowStyles.Add(new RowStyle());
            tableHexTools.RowStyles.Add(new RowStyle());
            tableHexTools.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableHexTools.TabIndex = 0;

            // ── lblHexInput ──────────────────────────────────────────────────
            lblHexInput.AutoSize = true;
            lblHexInput.Margin = new Padding(0, 0, 0, 4);
            lblHexInput.Name = "lblHexInput";
            lblHexInput.TabIndex = 0;
            lblHexInput.Text = "Ввести HEX (#RRGGBB или #AARRGGBB):";

            // ── txtHexInput ──────────────────────────────────────────────────
            txtHexInput.Dock = DockStyle.Top;
            txtHexInput.Name = "txtHexInput";
            txtHexInput.TabIndex = 1;
            txtHexInput.KeyDown += txtHexInput_KeyDown;

            // ── panelHexButtons ──────────────────────────────────────────────
            panelHexButtons.AutoSize = true;
            panelHexButtons.Controls.Add(btnApplyHex);
            panelHexButtons.Controls.Add(btnCopyCurrentHex);
            panelHexButtons.Controls.Add(btnSetTransparent);
            panelHexButtons.Dock = DockStyle.Top;
            panelHexButtons.FlowDirection = FlowDirection.LeftToRight;
            panelHexButtons.Margin = new Padding(0, 8, 0, 0);
            panelHexButtons.Name = "panelHexButtons";
            panelHexButtons.TabIndex = 2;
            panelHexButtons.WrapContents = true;

            // ── btnApplyHex ──────────────────────────────────────────────────
            btnApplyHex.AutoSize = true;
            btnApplyHex.Name = "btnApplyHex";
            btnApplyHex.TabIndex = 0;
            btnApplyHex.Text = "Применить HEX";
            btnApplyHex.UseVisualStyleBackColor = true;
            btnApplyHex.Click += btnApplyHex_Click;

            // ── btnCopyCurrentHex ────────────────────────────────────────────
            btnCopyCurrentHex.AutoSize = true;
            btnCopyCurrentHex.Name = "btnCopyCurrentHex";
            btnCopyCurrentHex.TabIndex = 1;
            btnCopyCurrentHex.Text = "Копировать HEX текущего";
            btnCopyCurrentHex.UseVisualStyleBackColor = true;
            btnCopyCurrentHex.Click += btnCopyCurrentHex_Click;

            // ── btnSetTransparent ────────────────────────────────────────────
            btnSetTransparent.AutoSize = true;
            btnSetTransparent.Name = "btnSetTransparent";
            btnSetTransparent.TabIndex = 2;
            btnSetTransparent.Text = "Сделать прозрачным";
            btnSetTransparent.UseVisualStyleBackColor = true;
            btnSetTransparent.Click += btnSetTransparent_Click;

            // ══════════════════════════════════════════════════════════════════
            // panelButtons — нижняя строка, кнопки вправо
            // ══════════════════════════════════════════════════════════════════
            panelButtons.AutoSize = true;
            panelButtons.Controls.Add(btnOk);
            panelButtons.Controls.Add(btnCancel);
            panelButtons.Dock = DockStyle.Fill;
            panelButtons.FlowDirection = FlowDirection.RightToLeft;
            panelButtons.Name = "panelButtons";
            panelButtons.Padding = new Padding(0, 4, 0, 0);
            panelButtons.TabIndex = 2;

            // ── btnOk ─────────────────────────────────────────────────────────
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Margin = new Padding(3, 0, 0, 0);
            btnOk.Name = "btnOk";
            btnOk.Size = new Size(80, 26);
            btnOk.TabIndex = 0;
            btnOk.Text = "ОК";
            btnOk.UseVisualStyleBackColor = true;

            // ── btnCancel ─────────────────────────────────────────────────────
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Margin = new Padding(3, 0, 0, 0);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(80, 26);
            btnCancel.TabIndex = 1;
            btnCancel.Text = "Отмена";
            btnCancel.UseVisualStyleBackColor = true;

            // ══════════════════════════════════════════════════════════════════
            // ColorPickerPanelForm
            // ══════════════════════════════════════════════════════════════════
            AcceptButton = btnOk;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = btnCancel;
            ClientSize = new Size(780, 450);
            Controls.Add(tableMain);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "ColorPickerPanelForm";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Выбор цвета";
            Resize += ColorPickerPanelForm_Resize;

            // ── ResumeLayout ──────────────────────────────────────────────────
            tableMain.ResumeLayout(false);
            tableLeft.ResumeLayout(false);
            tableColorArea.ResumeLayout(false);
            panelColorMatrixContainer.ResumeLayout(false);
            tableRgb.ResumeLayout(false);
            tableRgb.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)trkRed).EndInit();
            ((System.ComponentModel.ISupportInitialize)trkGreen).EndInit();
            ((System.ComponentModel.ISupportInitialize)trkBlue).EndInit();
            tableEyedropperPreview.ResumeLayout(false);
            tablePreviewStrip.ResumeLayout(false);
            grpPalettes.ResumeLayout(false);
            tablePalettes.ResumeLayout(false);
            tablePalettes.PerformLayout();
            grpHexTools.ResumeLayout(false);
            tableHexTools.ResumeLayout(false);
            tableHexTools.PerformLayout();
            panelHexButtons.ResumeLayout(false);
            panelHexButtons.PerformLayout();
            panelButtons.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        // ── левая колонка ─────────────────────────────────────────────────────
        private TableLayoutPanel tableLeft;
        private Panel panelColorMatrixContainer;
        private TableLayoutPanel tableColorArea;
        private Panel pnlColorMatrix;
        private Panel pnlHueSlider;
        private TableLayoutPanel tableRgb;
        private Label lblRed;
        private TrackBar trkRed;
        private Label lblRedValue;
        private Label lblGreen;
        private TrackBar trkGreen;
        private Label lblGreenValue;
        private Label lblBlue;
        private TrackBar trkBlue;
        private Label lblBlueValue;
        private TableLayoutPanel tableEyedropperPreview;
        private Button btnEyedropper;
        private TableLayoutPanel tablePreviewStrip;
        private Panel pnlCurrentColor;
        private Label lblCurrentHexValue;
        private Panel pnlNewColor;
        private Label lblNewHexValue;

        // ── правая колонка ────────────────────────────────────────────────────
        private GroupBox grpPalettes;
        private TableLayoutPanel tablePalettes;
        private Label lblStandard;
        private TableLayoutPanel tableStandardColors;
        private Label lblCustom;
        private TableLayoutPanel tableCustomColors;
        private Button btnAddToCustomColors;
        private GroupBox grpHexTools;
        private TableLayoutPanel tableHexTools;
        private Label lblHexInput;
        private TextBox txtHexInput;
        private FlowLayoutPanel panelHexButtons;
        private Button btnApplyHex;
        private Button btnCopyCurrentHex;
        private Button btnSetTransparent;

        // ── корень и нижняя строка ────────────────────────────────────────────
        private TableLayoutPanel tableMain;
        private FlowLayoutPanel panelButtons;
        private Button btnOk;
        private Button btnCancel;
    }
}
