namespace FractalExplorer.Forms.Fractals
{
    partial class IfsTransformEditorForm
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
            _grid = new DataGridView();
            _colA = new DataGridViewTextBoxColumn();
            _colB = new DataGridViewTextBoxColumn();
            _colC = new DataGridViewTextBoxColumn();
            _colD = new DataGridViewTextBoxColumn();
            _colE = new DataGridViewTextBoxColumn();
            _colF = new DataGridViewTextBoxColumn();
            _colProbability = new DataGridViewTextBoxColumn();
            _leftFooter = new FlowLayoutPanel();
            _btnAdd = new Button();
            _btnRemove = new Button();
            _btnNormalize = new Button();
            _rightFooter = new FlowLayoutPanel();
            _btnCancel = new Button();
            _btnOk = new Button();
            ((System.ComponentModel.ISupportInitialize)_grid).BeginInit();
            _leftFooter.SuspendLayout();
            _rightFooter.SuspendLayout();
            SuspendLayout();
            // 
            // _grid
            // 
            _grid.AllowUserToAddRows = false;
            _grid.AutoGenerateColumns = false;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _grid.Columns.AddRange(new DataGridViewColumn[] { _colA, _colB, _colC, _colD, _colE, _colF, _colProbability });
            _grid.Dock = DockStyle.Fill;
            _grid.Location = new Point(0, 44);
            _grid.Name = "_grid";
            _grid.RowHeadersVisible = false;
            _grid.RowHeadersWidth = 51;
            _grid.Size = new Size(982, 412);
            _grid.TabIndex = 0;
            // 
            // _colA
            // 
            _colA.DataPropertyName = "A";
            _colA.HeaderText = "A";
            _colA.MinimumWidth = 6;
            _colA.Name = "_colA";
            _colA.Width = 90;
            // 
            // _colB
            // 
            _colB.DataPropertyName = "B";
            _colB.HeaderText = "B";
            _colB.MinimumWidth = 6;
            _colB.Name = "_colB";
            _colB.Width = 90;
            // 
            // _colC
            // 
            _colC.DataPropertyName = "C";
            _colC.HeaderText = "C";
            _colC.MinimumWidth = 6;
            _colC.Name = "_colC";
            _colC.Width = 90;
            // 
            // _colD
            // 
            _colD.DataPropertyName = "D";
            _colD.HeaderText = "D";
            _colD.MinimumWidth = 6;
            _colD.Name = "_colD";
            _colD.Width = 90;
            // 
            // _colE
            // 
            _colE.DataPropertyName = "E";
            _colE.HeaderText = "E";
            _colE.MinimumWidth = 6;
            _colE.Name = "_colE";
            _colE.Width = 90;
            // 
            // _colF
            // 
            _colF.DataPropertyName = "F";
            _colF.HeaderText = "F";
            _colF.MinimumWidth = 6;
            _colF.Name = "_colF";
            _colF.Width = 90;
            // 
            // _colProbability
            // 
            _colProbability.DataPropertyName = "Probability";
            _colProbability.HeaderText = "P";
            _colProbability.MinimumWidth = 6;
            _colProbability.Name = "_colProbability";
            _colProbability.Width = 120;
            // 
            // _leftFooter
            // 
            _leftFooter.Controls.Add(_btnAdd);
            _leftFooter.Controls.Add(_btnRemove);
            _leftFooter.Controls.Add(_btnNormalize);
            _leftFooter.Dock = DockStyle.Top;
            _leftFooter.FlowDirection = FlowDirection.LeftToRight;
            _leftFooter.Location = new Point(0, 0);
            _leftFooter.Name = "_leftFooter";
            _leftFooter.Padding = new Padding(10, 8, 10, 8);
            _leftFooter.Size = new Size(982, 44);
            _leftFooter.TabIndex = 1;
            _leftFooter.WrapContents = false;
            // 
            // _btnAdd
            // 
            _btnAdd.Location = new Point(13, 11);
            _btnAdd.Name = "_btnAdd";
            _btnAdd.Size = new Size(120, 29);
            _btnAdd.TabIndex = 0;
            _btnAdd.Text = "+ Добавить";
            _btnAdd.UseVisualStyleBackColor = true;
            // 
            // _btnRemove
            // 
            _btnRemove.Location = new Point(139, 11);
            _btnRemove.Name = "_btnRemove";
            _btnRemove.Size = new Size(120, 29);
            _btnRemove.TabIndex = 1;
            _btnRemove.Text = "− Удалить";
            _btnRemove.UseVisualStyleBackColor = true;
            // 
            // _btnNormalize
            // 
            _btnNormalize.Location = new Point(265, 11);
            _btnNormalize.Name = "_btnNormalize";
            _btnNormalize.Size = new Size(170, 29);
            _btnNormalize.TabIndex = 2;
            _btnNormalize.Text = "Норм. вероятности";
            _btnNormalize.UseVisualStyleBackColor = true;
            // 
            // _rightFooter
            // 
            _rightFooter.Controls.Add(_btnCancel);
            _rightFooter.Controls.Add(_btnOk);
            _rightFooter.Dock = DockStyle.Bottom;
            _rightFooter.FlowDirection = FlowDirection.RightToLeft;
            _rightFooter.Location = new Point(0, 456);
            _rightFooter.Name = "_rightFooter";
            _rightFooter.Padding = new Padding(10, 8, 10, 8);
            _rightFooter.Size = new Size(982, 54);
            _rightFooter.TabIndex = 2;
            _rightFooter.WrapContents = false;
            // 
            // _btnCancel
            // 
            _btnCancel.DialogResult = DialogResult.Cancel;
            _btnCancel.Location = new Point(849, 11);
            _btnCancel.Name = "_btnCancel";
            _btnCancel.Size = new Size(120, 29);
            _btnCancel.TabIndex = 1;
            _btnCancel.Text = "Отмена";
            _btnCancel.UseVisualStyleBackColor = true;
            // 
            // _btnOk
            // 
            _btnOk.DialogResult = DialogResult.OK;
            _btnOk.Location = new Point(723, 11);
            _btnOk.Name = "_btnOk";
            _btnOk.Size = new Size(120, 29);
            _btnOk.TabIndex = 0;
            _btnOk.Text = "Применить";
            _btnOk.UseVisualStyleBackColor = true;
            // 
            // IfsTransformEditorForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(982, 510);
            Controls.Add(_grid);
            Controls.Add(_leftFooter);
            Controls.Add(_rightFooter);
            MinimumSize = new Size(900, 420);
            Name = "IfsTransformEditorForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Редактор аффинных преобразований IFS";
            ((System.ComponentModel.ISupportInitialize)_grid).EndInit();
            _leftFooter.ResumeLayout(false);
            _rightFooter.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private DataGridView _grid;
        private FlowLayoutPanel _leftFooter;
        private FlowLayoutPanel _rightFooter;
        private Button _btnAdd;
        private Button _btnRemove;
        private Button _btnNormalize;
        private Button _btnCancel;
        private Button _btnOk;
        private DataGridViewTextBoxColumn _colA;
        private DataGridViewTextBoxColumn _colB;
        private DataGridViewTextBoxColumn _colC;
        private DataGridViewTextBoxColumn _colD;
        private DataGridViewTextBoxColumn _colE;
        private DataGridViewTextBoxColumn _colF;
        private DataGridViewTextBoxColumn _colProbability;
    }
}
