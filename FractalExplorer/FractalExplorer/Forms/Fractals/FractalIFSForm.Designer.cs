namespace FractalExplorer.Forms.Fractals
{
    partial class FractalIFSForm
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
            panelControls = new Panel();
            btnSaveLoad = new Button();
            btnBackgroundColor = new Button();
            btnFractalColor = new Button();
            label3 = new Label();
            nudIterations = new NumericUpDown();
            label2 = new Label();
            cbPreset = new ComboBox();
            label1 = new Label();
            canvas = new PictureBox();
            panelControls.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudIterations).BeginInit();
            ((System.ComponentModel.ISupportInitialize)canvas).BeginInit();
            SuspendLayout();
            // 
            // panelControls
            // 
            panelControls.Controls.Add(btnSaveLoad);
            panelControls.Controls.Add(btnBackgroundColor);
            panelControls.Controls.Add(btnFractalColor);
            panelControls.Controls.Add(label3);
            panelControls.Controls.Add(nudIterations);
            panelControls.Controls.Add(label2);
            panelControls.Controls.Add(cbPreset);
            panelControls.Controls.Add(label1);
            panelControls.Dock = DockStyle.Left;
            panelControls.Location = new Point(0, 0);
            panelControls.Name = "panelControls";
            panelControls.Size = new Size(245, 681);
            panelControls.TabIndex = 0;
            // 
            // btnSaveLoad
            // 
            btnSaveLoad.Location = new Point(12, 220);
            btnSaveLoad.Name = "btnSaveLoad";
            btnSaveLoad.Size = new Size(215, 32);
            btnSaveLoad.TabIndex = 7;
            btnSaveLoad.Text = "Сохранить / Загрузить";
            btnSaveLoad.UseVisualStyleBackColor = true;
            btnSaveLoad.Click += btnSaveLoad_Click;
            // 
            // btnBackgroundColor
            // 
            btnBackgroundColor.Location = new Point(12, 176);
            btnBackgroundColor.Name = "btnBackgroundColor";
            btnBackgroundColor.Size = new Size(215, 30);
            btnBackgroundColor.TabIndex = 6;
            btnBackgroundColor.Text = "Цвет фона";
            btnBackgroundColor.UseVisualStyleBackColor = true;
            btnBackgroundColor.Click += btnBackgroundColor_Click;
            // 
            // btnFractalColor
            // 
            btnFractalColor.Location = new Point(12, 140);
            btnFractalColor.Name = "btnFractalColor";
            btnFractalColor.Size = new Size(215, 30);
            btnFractalColor.TabIndex = 5;
            btnFractalColor.Text = "Цвет фрактала";
            btnFractalColor.UseVisualStyleBackColor = true;
            btnFractalColor.Click += btnFractalColor_Click;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(12, 81);
            label3.Name = "label3";
            label3.Size = new Size(73, 20);
            label3.TabIndex = 4;
            label3.Text = "Итерации";
            // 
            // nudIterations
            // 
            nudIterations.Increment = new decimal(new int[] { 5000, 0, 0, 0 });
            nudIterations.Location = new Point(12, 104);
            nudIterations.Maximum = new decimal(new int[] { 5000000, 0, 0, 0 });
            nudIterations.Minimum = new decimal(new int[] { 1000, 0, 0, 0 });
            nudIterations.Name = "nudIterations";
            nudIterations.Size = new Size(215, 27);
            nudIterations.TabIndex = 3;
            nudIterations.Value = new decimal(new int[] { 200000, 0, 0, 0 });
            nudIterations.ValueChanged += ParamChanged;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(12, 21);
            label2.Name = "label2";
            label2.Size = new Size(60, 20);
            label2.TabIndex = 2;
            label2.Text = "Пресет";
            // 
            // cbPreset
            // 
            cbPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            cbPreset.FormattingEnabled = true;
            cbPreset.Location = new Point(12, 44);
            cbPreset.Name = "cbPreset";
            cbPreset.Size = new Size(215, 28);
            cbPreset.TabIndex = 1;
            cbPreset.SelectedIndexChanged += ParamChanged;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(12, 273);
            label1.Name = "label1";
            label1.Size = new Size(212, 80);
            label1.TabIndex = 0;
            label1.Text = "Колесо мыши: масштаб\r\nЛКМ + движение: панорамирование\r\n\r\nДля быстрого старта:\r\nBarnsleyFern, 200k итераций";
            // 
            // canvas
            // 
            canvas.BackColor = Color.Black;
            canvas.Dock = DockStyle.Fill;
            canvas.Location = new Point(245, 0);
            canvas.Name = "canvas";
            canvas.Size = new Size(967, 681);
            canvas.SizeMode = PictureBoxSizeMode.Zoom;
            canvas.TabIndex = 1;
            canvas.TabStop = false;
            // 
            // FractalIFSForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1212, 681);
            Controls.Add(canvas);
            Controls.Add(panelControls);
            Name = "FractalIFSForm";
            Text = "IFS Geometry";
            FormClosing += FractalIFSForm_FormClosing;
            Load += FractalIFSForm_Load;
            panelControls.ResumeLayout(false);
            panelControls.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudIterations).EndInit();
            ((System.ComponentModel.ISupportInitialize)canvas).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private Panel panelControls;
        private PictureBox canvas;
        private Label label1;
        private Label label2;
        private ComboBox cbPreset;
        private Label label3;
        private NumericUpDown nudIterations;
        private Button btnBackgroundColor;
        private Button btnFractalColor;
        private Button btnSaveLoad;
    }
}
