namespace FractalExplorer.SelectorsForms
{
    partial class ColorConfigurationLyapunovForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ColorConfigurationLyapunovForm));
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
            _nudZeroBand = new NumericUpDown();
            lblZero = new Label();
            _nudRange = new NumericUpDown();
            lblRange = new Label();
            _cbMode = new ComboBox();
            lblMode = new Label();
            _txtName = new TextBox();
            lblName = new Label();
            _btnClose = new Button();
            _btnApply = new Button();
            _btnSave = new Button();
            grpPalettes.SuspendLayout();
            grpEditor.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_nudZeroBand).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_nudRange).BeginInit();
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
            _lbPalettes.ItemHeight = 15;
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
            grpEditor.Controls.Add(_nudZeroBand);
            grpEditor.Controls.Add(lblZero);
            grpEditor.Controls.Add(_nudRange);
            grpEditor.Controls.Add(lblRange);
            grpEditor.Controls.Add(_cbMode);
            grpEditor.Controls.Add(lblMode);
            grpEditor.Controls.Add(_txtName);
            grpEditor.Controls.Add(lblName);
            grpEditor.Location = new Point(272, 12);
            grpEditor.Name = "grpEditor";
            grpEditor.Size = new Size(385, 437);
            grpEditor.TabIndex = 1;
            grpEditor.TabStop = false;
            grpEditor.Text = "Редактор палитры Lyapunov";
            // 
            // _btnRemoveColor
            // 
            _btnRemoveColor.Location = new Point(271, 321);
            _btnRemoveColor.Name = "_btnRemoveColor";
            _btnRemoveColor.Size = new Size(107, 28);
            _btnRemoveColor.TabIndex = 14;
            _btnRemoveColor.Text = "Удалить";
            _btnRemoveColor.UseVisualStyleBackColor = true;
            // 
            // _btnEditColor
            // 
            _btnEditColor.Location = new Point(271, 287);
            _btnEditColor.Name = "_btnEditColor";
            _btnEditColor.Size = new Size(107, 28);
            _btnEditColor.TabIndex = 13;
            _btnEditColor.Text = "Изменить...";
            _btnEditColor.UseVisualStyleBackColor = true;
            // 
            // _btnAddColor
            // 
            _btnAddColor.Location = new Point(271, 253);
            _btnAddColor.Name = "_btnAddColor";
            _btnAddColor.Size = new Size(107, 28);
            _btnAddColor.TabIndex = 12;
            _btnAddColor.Text = "Добавить...";
            _btnAddColor.UseVisualStyleBackColor = true;
            // 
            // _lbColors
            // 
            _lbColors.FormattingEnabled = true;
            _lbColors.ItemHeight = 15;
            _lbColors.Location = new Point(9, 253);
            _lbColors.Name = "_lbColors";
            _lbColors.Size = new Size(256, 139);
            _lbColors.TabIndex = 11;
            // 
            // lblColors
            // 
            lblColors.AutoSize = true;
            lblColors.Location = new Point(9, 235);
            lblColors.Name = "lblColors";
            lblColors.Size = new Size(104, 15);
            lblColors.TabIndex = 10;
            lblColors.Text = "Ключевые цвета:";
            // 
            // _panelPreview
            // 
            _panelPreview.BorderStyle = BorderStyle.FixedSingle;
            _panelPreview.Location = new Point(9, 188);
            _panelPreview.Name = "_panelPreview";
            _panelPreview.Size = new Size(369, 38);
            _panelPreview.TabIndex = 9;
            // 
            // lblPreview
            // 
            lblPreview.AutoSize = true;
            lblPreview.Location = new Point(9, 170);
            lblPreview.Name = "lblPreview";
            lblPreview.Size = new Size(52, 15);
            lblPreview.TabIndex = 8;
            lblPreview.Text = "Превью:";
            // 
            // _nudZeroBand
            // 
            _nudZeroBand.DecimalPlaces = 4;
            _nudZeroBand.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            _nudZeroBand.Location = new Point(200, 138);
            _nudZeroBand.Maximum = new decimal(new int[] { 2, 0, 0, 0 });
            _nudZeroBand.Minimum = new decimal(new int[] { 1, 0, 0, 262144 });
            _nudZeroBand.Name = "_nudZeroBand";
            _nudZeroBand.Size = new Size(178, 23);
            _nudZeroBand.TabIndex = 7;
            _nudZeroBand.Value = new decimal(new int[] { 1, 0, 0, 262144 });
            // 
            // lblZero
            // 
            lblZero.AutoSize = true;
            lblZero.Location = new Point(200, 120);
            lblZero.Name = "lblZero";
            lblZero.Size = new Size(66, 15);
            lblZero.TabIndex = 6;
            lblZero.Text = "Zero-band:";
            // 
            // _nudRange
            // 
            _nudRange.DecimalPlaces = 3;
            _nudRange.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            _nudRange.Location = new Point(9, 138);
            _nudRange.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            _nudRange.Minimum = new decimal(new int[] { 1, 0, 0, 196608 });
            _nudRange.Name = "_nudRange";
            _nudRange.Size = new Size(178, 23);
            _nudRange.TabIndex = 5;
            _nudRange.Value = new decimal(new int[] { 1, 0, 0, 196608 });
            // 
            // lblRange
            // 
            lblRange.AutoSize = true;
            lblRange.Location = new Point(9, 120);
            lblRange.Name = "lblRange";
            lblRange.Size = new Size(86, 15);
            lblRange.TabIndex = 4;
            lblRange.Text = "Диапазон |λ|:";
            // 
            // _cbMode
            // 
            _cbMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbMode.FormattingEnabled = true;
            _cbMode.Location = new Point(9, 89);
            _cbMode.Name = "_cbMode";
            _cbMode.Size = new Size(369, 23);
            _cbMode.TabIndex = 3;
            // 
            // lblMode
            // 
            lblMode.AutoSize = true;
            lblMode.Location = new Point(9, 71);
            lblMode.Name = "lblMode";
            lblMode.Size = new Size(49, 15);
            lblMode.TabIndex = 2;
            lblMode.Text = "Режим:";
            // 
            // _txtName
            // 
            _txtName.Location = new Point(9, 40);
            _txtName.Name = "_txtName";
            _txtName.Size = new Size(369, 23);
            _txtName.TabIndex = 1;
            // 
            // lblName
            // 
            lblName.AutoSize = true;
            lblName.Location = new Point(9, 22);
            lblName.Name = "lblName";
            lblName.Size = new Size(65, 15);
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
            // ColorConfigurationLyapunovForm
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
            Name = "ColorConfigurationLyapunovForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Настройка палитры (Lyapunov)";
            grpPalettes.ResumeLayout(false);
            grpEditor.ResumeLayout(false);
            grpEditor.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_nudZeroBand).EndInit();
            ((System.ComponentModel.ISupportInitialize)_nudRange).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private GroupBox grpPalettes;
        private GroupBox grpEditor;
        private Label lblName;
        private Label lblMode;
        private Label lblRange;
        private Label lblZero;
        private Label lblPreview;
        private Label lblColors;
        private ListBox _lbPalettes;
        private ListBox _lbColors;
        private TextBox _txtName;
        private ComboBox _cbMode;
        private NumericUpDown _nudRange;
        private NumericUpDown _nudZeroBand;
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
