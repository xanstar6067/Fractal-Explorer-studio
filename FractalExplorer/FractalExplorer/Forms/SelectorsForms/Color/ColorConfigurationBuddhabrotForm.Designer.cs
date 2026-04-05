namespace FractalExplorer.SelectorsForms
{
    partial class ColorConfigurationBuddhabrotForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ColorConfigurationBuddhabrotForm));
            grpPalettes = new GroupBox();
            _btnDelete = new Button();
            _btnCopy = new Button();
            _btnNew = new Button();
            _lbPalettes = new ListBox();
            grpEditor = new GroupBox();
            _btnRemoveColor = new Button();
            _btnEditColor = new Button();
            _btnAddColor = new Button();
            _lbColors = new ListBox();
            lblColors = new Label();
            _panelPreview = new Panel();
            lblPreview = new Label();
            _nudGamma = new NumericUpDown();
            lblGamma = new Label();
            _nudMaxSteps = new NumericUpDown();
            lblMaxSteps = new Label();
            _checkAlignSteps = new CheckBox();
            _checkIsGradient = new CheckBox();
            _cbMode = new ComboBox();
            lblMode = new Label();
            _txtName = new TextBox();
            lblName = new Label();
            _btnClose = new Button();
            _btnApply = new Button();
            _btnSave = new Button();
            grpPalettes.SuspendLayout();
            grpEditor.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_nudGamma).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudMaxSteps).BeginInit();
            SuspendLayout();
            // 
            // grpPalettes
            // 
            grpPalettes.Controls.Add(_btnDelete);
            grpPalettes.Controls.Add(_btnCopy);
            grpPalettes.Controls.Add(_btnNew);
            grpPalettes.Controls.Add(_lbPalettes);
            grpPalettes.Location = new Point(12, 12);
            grpPalettes.Name = "grpPalettes";
            grpPalettes.Size = new Size(238, 437);
            grpPalettes.TabIndex = 0;
            grpPalettes.TabStop = false;
            grpPalettes.Text = "Список палитр";
            // 
            // _btnDelete
            // 
            _btnDelete.Location = new Point(168, 400);
            _btnDelete.Name = "_btnDelete";
            _btnDelete.Size = new Size(63, 28);
            _btnDelete.TabIndex = 3;
            _btnDelete.Text = "Удалить";
            _btnDelete.UseVisualStyleBackColor = true;
            // 
            // _btnCopy
            // 
            _btnCopy.Location = new Point(74, 400);
            _btnCopy.Name = "_btnCopy";
            _btnCopy.Size = new Size(88, 28);
            _btnCopy.TabIndex = 2;
            _btnCopy.Text = "Копировать";
            _btnCopy.UseVisualStyleBackColor = true;
            // 
            // _btnNew
            // 
            _btnNew.Location = new Point(7, 400);
            _btnNew.Name = "_btnNew";
            _btnNew.Size = new Size(61, 28);
            _btnNew.TabIndex = 1;
            _btnNew.Text = "Новая";
            _btnNew.UseVisualStyleBackColor = true;
            // 
            // _lbPalettes
            // 
            _lbPalettes.FormattingEnabled = true;
            _lbPalettes.Location = new Point(7, 22);
            _lbPalettes.Name = "_lbPalettes";
            _lbPalettes.Size = new Size(224, 364);
            _lbPalettes.TabIndex = 0;
            // 
            // grpEditor
            // 
            grpEditor.Controls.Add(_btnRemoveColor);
            grpEditor.Controls.Add(_btnEditColor);
            grpEditor.Controls.Add(_btnAddColor);
            grpEditor.Controls.Add(_lbColors);
            grpEditor.Controls.Add(lblColors);
            grpEditor.Controls.Add(_panelPreview);
            grpEditor.Controls.Add(lblPreview);
            grpEditor.Controls.Add(_nudGamma);
            grpEditor.Controls.Add(lblGamma);
            grpEditor.Controls.Add(_nudMaxSteps);
            grpEditor.Controls.Add(lblMaxSteps);
            grpEditor.Controls.Add(_checkAlignSteps);
            grpEditor.Controls.Add(_checkIsGradient);
            grpEditor.Controls.Add(_cbMode);
            grpEditor.Controls.Add(lblMode);
            grpEditor.Controls.Add(_txtName);
            grpEditor.Controls.Add(lblName);
            grpEditor.Location = new Point(272, 12);
            grpEditor.Name = "grpEditor";
            grpEditor.Size = new Size(385, 437);
            grpEditor.TabIndex = 1;
            grpEditor.TabStop = false;
            grpEditor.Text = "Редактор палитры Буддаброта";
            // 
            // _btnRemoveColor
            // 
            _btnRemoveColor.Location = new Point(271, 321);
            _btnRemoveColor.Name = "_btnRemoveColor";
            _btnRemoveColor.Size = new Size(107, 28);
            _btnRemoveColor.TabIndex = 16;
            _btnRemoveColor.Text = "Удалить";
            _btnRemoveColor.UseVisualStyleBackColor = true;
            // 
            // _btnEditColor
            // 
            _btnEditColor.Location = new Point(271, 287);
            _btnEditColor.Name = "_btnEditColor";
            _btnEditColor.Size = new Size(107, 28);
            _btnEditColor.TabIndex = 15;
            _btnEditColor.Text = "Изменить...";
            _btnEditColor.UseVisualStyleBackColor = true;
            // 
            // _btnAddColor
            // 
            _btnAddColor.Location = new Point(271, 253);
            _btnAddColor.Name = "_btnAddColor";
            _btnAddColor.Size = new Size(107, 28);
            _btnAddColor.TabIndex = 14;
            _btnAddColor.Text = "Добавить...";
            _btnAddColor.UseVisualStyleBackColor = true;
            // 
            // _lbColors
            // 
            _lbColors.FormattingEnabled = true;
            _lbColors.Location = new Point(9, 253);
            _lbColors.Name = "_lbColors";
            _lbColors.Size = new Size(256, 139);
            _lbColors.TabIndex = 13;
            // 
            // lblColors
            // 
            lblColors.AutoSize = true;
            lblColors.Location = new Point(9, 235);
            lblColors.Name = "lblColors";
            lblColors.Size = new Size(101, 15);
            lblColors.TabIndex = 12;
            lblColors.Text = "Ключевые цвета:";
            // 
            // _panelPreview
            // 
            _panelPreview.BorderStyle = BorderStyle.FixedSingle;
            _panelPreview.Location = new Point(9, 188);
            _panelPreview.Name = "_panelPreview";
            _panelPreview.Size = new Size(369, 38);
            _panelPreview.TabIndex = 11;
            // 
            // lblPreview
            // 
            lblPreview.AutoSize = true;
            lblPreview.Location = new Point(9, 170);
            lblPreview.Name = "lblPreview";
            lblPreview.Size = new Size(54, 15);
            lblPreview.TabIndex = 10;
            lblPreview.Text = "Превью:";
            // 
            // _nudGamma
            // 
            _nudGamma.DecimalPlaces = 2;
            _nudGamma.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            _nudGamma.Location = new Point(200, 138);
            _nudGamma.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            _nudGamma.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            _nudGamma.Name = "_nudGamma";
            _nudGamma.Size = new Size(178, 23);
            _nudGamma.TabIndex = 9;
            _nudGamma.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // lblGamma
            // 
            lblGamma.AutoSize = true;
            lblGamma.Location = new Point(200, 120);
            lblGamma.Name = "lblGamma";
            lblGamma.Size = new Size(52, 15);
            lblGamma.TabIndex = 8;
            lblGamma.Text = "Гамма:";
            // 
            // _nudMaxSteps
            // 
            _nudMaxSteps.Location = new Point(9, 138);
            _nudMaxSteps.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            _nudMaxSteps.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
            _nudMaxSteps.Name = "_nudMaxSteps";
            _nudMaxSteps.Size = new Size(178, 23);
            _nudMaxSteps.TabIndex = 7;
            _nudMaxSteps.Value = new decimal(new int[] { 500, 0, 0, 0 });
            // 
            // lblMaxSteps
            // 
            lblMaxSteps.AutoSize = true;
            lblMaxSteps.Location = new Point(9, 120);
            lblMaxSteps.Name = "lblMaxSteps";
            lblMaxSteps.Size = new Size(78, 15);
            lblMaxSteps.TabIndex = 6;
            lblMaxSteps.Text = "Шагов цвета:";
            // 
            // _checkAlignSteps
            // 
            _checkAlignSteps.AutoSize = true;
            _checkAlignSteps.Location = new Point(200, 92);
            _checkAlignSteps.Name = "_checkAlignSteps";
            _checkAlignSteps.Size = new Size(147, 19);
            _checkAlignSteps.TabIndex = 5;
            _checkAlignSteps.Text = "Связать с итерациями";
            _checkAlignSteps.UseVisualStyleBackColor = true;
            // 
            // _checkIsGradient
            // 
            _checkIsGradient.AutoSize = true;
            _checkIsGradient.Location = new Point(9, 92);
            _checkIsGradient.Name = "_checkIsGradient";
            _checkIsGradient.Size = new Size(76, 19);
            _checkIsGradient.TabIndex = 4;
            _checkIsGradient.Text = "Градиент";
            _checkIsGradient.UseVisualStyleBackColor = true;
            // 
            // _cbMode
            // 
            _cbMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbMode.FormattingEnabled = true;
            _cbMode.Location = new Point(200, 40);
            _cbMode.Name = "_cbMode";
            _cbMode.Size = new Size(178, 23);
            _cbMode.TabIndex = 3;
            // 
            // lblMode
            // 
            lblMode.AutoSize = true;
            lblMode.Location = new Point(200, 22);
            lblMode.Name = "lblMode";
            lblMode.Size = new Size(48, 15);
            lblMode.TabIndex = 2;
            lblMode.Text = "Режим:";
            // 
            // _txtName
            // 
            _txtName.Location = new Point(9, 40);
            _txtName.Name = "_txtName";
            _txtName.Size = new Size(178, 23);
            _txtName.TabIndex = 1;
            // 
            // lblName
            // 
            lblName.AutoSize = true;
            lblName.Location = new Point(9, 22);
            lblName.Name = "lblName";
            lblName.Size = new Size(62, 15);
            lblName.TabIndex = 0;
            lblName.Text = "Название:";
            // 
            // _btnClose
            // 
            _btnClose.Location = new Point(545, 461);
            _btnClose.Name = "_btnClose";
            _btnClose.Size = new Size(107, 28);
            _btnClose.TabIndex = 4;
            _btnClose.Text = "Закрыть";
            _btnClose.UseVisualStyleBackColor = true;
            // 
            // _btnApply
            // 
            _btnApply.Location = new Point(432, 461);
            _btnApply.Name = "_btnApply";
            _btnApply.Size = new Size(107, 28);
            _btnApply.TabIndex = 3;
            _btnApply.Text = "Применить";
            _btnApply.UseVisualStyleBackColor = true;
            // 
            // _btnSave
            // 
            _btnSave.Location = new Point(19, 461);
            _btnSave.Name = "_btnSave";
            _btnSave.Size = new Size(149, 28);
            _btnSave.TabIndex = 2;
            _btnSave.Text = "Сохранить изменения";
            _btnSave.UseVisualStyleBackColor = true;
            // 
            // ColorConfigurationBuddhabrotForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(669, 501);
            Controls.Add(_btnClose);
            Controls.Add(_btnApply);
            Controls.Add(_btnSave);
            Controls.Add(grpEditor);
            Controls.Add(grpPalettes);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            MaximumSize = new Size(685, 540);
            MinimizeBox = false;
            MinimumSize = new Size(685, 540);
            Name = "ColorConfigurationBuddhabrotForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Настройка палитры (Буддаброт)";
            grpPalettes.ResumeLayout(false);
            grpEditor.ResumeLayout(false);
            grpEditor.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_nudGamma).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudMaxSteps).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private GroupBox grpPalettes;
        private GroupBox grpEditor;
        private Label lblName;
        private Label lblMode;
        private Label lblMaxSteps;
        private Label lblGamma;
        private Label lblPreview;
        private Label lblColors;
        private ListBox _lbPalettes;
        private ListBox _lbColors;
        private TextBox _txtName;
        private ComboBox _cbMode;
        private CheckBox _checkIsGradient;
        private CheckBox _checkAlignSteps;
        private NumericUpDown _nudMaxSteps;
        private NumericUpDown _nudGamma;
        private Panel _panelPreview;
        private Button _btnSave;
        private Button _btnApply;
        private Button _btnClose;
        private Button _btnNew;
        private Button _btnCopy;
        private Button _btnDelete;
        private Button _btnAddColor;
        private Button _btnEditColor;
        private Button _btnRemoveColor;
    }
}
