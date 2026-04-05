namespace FractalExplorer.Forms.Fractals
{
    partial class FlameTransformEditorForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer? components = null;

        private DataGridView _grid;
        private FlowLayoutPanel _buttonsPanel;
        private Button _btnOk;
        private Button _btnCancel;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            _grid = new DataGridView();
            _buttonsPanel = new FlowLayoutPanel();
            _btnOk = new Button();
            _btnCancel = new Button();
            ((System.ComponentModel.ISupportInitialize)_grid).BeginInit();
            _buttonsPanel.SuspendLayout();
            SuspendLayout();
            // 
            // _grid
            // 
            _grid.Dock = DockStyle.Fill;
            _grid.Location = new Point(0, 0);
            _grid.Name = "_grid";
            _grid.Size = new Size(1084, 357);
            _grid.TabIndex = 0;
            // 
            // _buttonsPanel
            // 
            _buttonsPanel.Controls.Add(_btnOk);
            _buttonsPanel.Controls.Add(_btnCancel);
            _buttonsPanel.Dock = DockStyle.Bottom;
            _buttonsPanel.FlowDirection = FlowDirection.RightToLeft;
            _buttonsPanel.Location = new Point(0, 357);
            _buttonsPanel.Name = "_buttonsPanel";
            _buttonsPanel.Size = new Size(1084, 44);
            _buttonsPanel.TabIndex = 1;
            // 
            // _btnOk
            // 
            _btnOk.DialogResult = DialogResult.OK;
            _btnOk.Location = new Point(981, 3);
            _btnOk.Name = "_btnOk";
            _btnOk.Size = new Size(100, 31);
            _btnOk.TabIndex = 0;
            _btnOk.Text = "OK";
            _btnOk.UseVisualStyleBackColor = true;
            // 
            // _btnCancel
            // 
            _btnCancel.DialogResult = DialogResult.Cancel;
            _btnCancel.Location = new Point(875, 3);
            _btnCancel.Name = "_btnCancel";
            _btnCancel.Size = new Size(100, 31);
            _btnCancel.TabIndex = 1;
            _btnCancel.Text = "Cancel";
            _btnCancel.UseVisualStyleBackColor = true;
            // 
            // FlameTransformEditorForm
            // 
            AcceptButton = _btnOk;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = _btnCancel;
            ClientSize = new Size(1084, 401);
            Controls.Add(_grid);
            Controls.Add(_buttonsPanel);
            Name = "FlameTransformEditorForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Редактор трансформаций";
            ((System.ComponentModel.ISupportInitialize)_grid).EndInit();
            _buttonsPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
    }
}
