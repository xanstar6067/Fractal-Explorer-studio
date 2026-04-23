namespace FractalExplorer
{
    partial class LauncherHubForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LauncherHubForm));
            splitContainerMain = new SplitContainer();
            accordionFractals = new FractalExplorer.Controls.FractalAccordionPanel();
            pnlDetails = new TableLayoutPanel();
            headerPanel = new TableLayoutPanel();
            lblFractalName = new Label();
            btnAboutInfo = new Button();
            pictureBoxPreview = new PictureBox();
            settingsPanel = new TableLayoutPanel();
            lblRenderPatternType = new Label();
            cbRenderPattern = new ComboBox();
            lblTheme = new Label();
            cbTheme = new ComboBox();
            btnLaunchSelected = new Button();
            richTextBoxDescription = new RichTextBox();
            ((System.ComponentModel.ISupportInitialize)splitContainerMain).BeginInit();
            splitContainerMain.Panel1.SuspendLayout();
            splitContainerMain.Panel2.SuspendLayout();
            splitContainerMain.SuspendLayout();
            pnlDetails.SuspendLayout();
            headerPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBoxPreview).BeginInit();
            settingsPanel.SuspendLayout();
            SuspendLayout();
            // 
            // splitContainerMain
            // 
            splitContainerMain.Dock = DockStyle.Fill;
            splitContainerMain.IsSplitterFixed = true;
            splitContainerMain.Location = new Point(0, 0);
            splitContainerMain.Name = "splitContainerMain";
            // 
            // splitContainerMain.Panel1
            // 
            splitContainerMain.Panel1.Controls.Add(accordionFractals);
            splitContainerMain.Panel1.Padding = new Padding(10);
            // 
            // splitContainerMain.Panel2
            // 
            splitContainerMain.Panel2.Controls.Add(pnlDetails);
            splitContainerMain.Size = new Size(896, 521);
            splitContainerMain.SplitterDistance = 301;
            splitContainerMain.SplitterWidth = 2;
            splitContainerMain.TabIndex = 0;
            // 
            // accordionFractals
            // 
            accordionFractals.Dock = DockStyle.Fill;
            accordionFractals.Location = new Point(10, 10);
            accordionFractals.Name = "accordionFractals";
            accordionFractals.Size = new Size(281, 501);
            accordionFractals.TabIndex = 0;
            // 
            // pnlDetails
            // 
            pnlDetails.ColumnCount = 2;
            pnlDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
            pnlDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            pnlDetails.Controls.Add(headerPanel, 0, 0);
            pnlDetails.Controls.Add(pictureBoxPreview, 0, 1);
            pnlDetails.Controls.Add(settingsPanel, 1, 1);
            pnlDetails.Controls.Add(richTextBoxDescription, 0, 2);
            pnlDetails.Dock = DockStyle.Fill;
            pnlDetails.Location = new Point(0, 0);
            pnlDetails.Name = "pnlDetails";
            pnlDetails.Padding = new Padding(15);
            pnlDetails.RowCount = 3;
            pnlDetails.RowStyles.Add(new RowStyle());
            pnlDetails.RowStyles.Add(new RowStyle(SizeType.Absolute, 202F));
            pnlDetails.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            pnlDetails.Size = new Size(593, 521);
            pnlDetails.TabIndex = 0;
            // 
            // headerPanel
            // 
            headerPanel.ColumnCount = 2;
            pnlDetails.SetColumnSpan(headerPanel, 2);
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34F));
            headerPanel.Controls.Add(lblFractalName, 0, 0);
            headerPanel.Controls.Add(btnAboutInfo, 1, 0);
            headerPanel.Dock = DockStyle.Fill;
            headerPanel.Location = new Point(15, 15);
            headerPanel.Margin = new Padding(0, 0, 0, 15);
            headerPanel.Name = "headerPanel";
            headerPanel.RowCount = 1;
            headerPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            headerPanel.Size = new Size(563, 30);
            headerPanel.TabIndex = 0;
            // 
            // lblFractalName
            // 
            lblFractalName.AutoSize = true;
            lblFractalName.Dock = DockStyle.Fill;
            lblFractalName.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);
            lblFractalName.Location = new Point(0, 0);
            lblFractalName.Margin = new Padding(0);
            lblFractalName.Name = "lblFractalName";
            lblFractalName.Size = new Size(529, 30);
            lblFractalName.TabIndex = 0;
            lblFractalName.Text = "Выберите фрактал";
            lblFractalName.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // btnAboutInfo
            // 
            btnAboutInfo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnAboutInfo.Cursor = Cursors.Hand;
            btnAboutInfo.FlatStyle = FlatStyle.Flat;
            btnAboutInfo.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            btnAboutInfo.Location = new Point(534, 1);
            btnAboutInfo.Margin = new Padding(0, 1, 0, 0);
            btnAboutInfo.Name = "btnAboutInfo";
            btnAboutInfo.Size = new Size(29, 29);
            btnAboutInfo.TabIndex = 1;
            btnAboutInfo.Text = "ℹ";
            btnAboutInfo.UseVisualStyleBackColor = true;
            btnAboutInfo.Click += btnAboutInfo_Click;
            // 
            // pictureBoxPreview
            // 
            pictureBoxPreview.Dock = DockStyle.Fill;
            pictureBoxPreview.Location = new Point(15, 60);
            pictureBoxPreview.Margin = new Padding(0, 0, 15, 0);
            pictureBoxPreview.Name = "pictureBoxPreview";
            pictureBoxPreview.Size = new Size(205, 202);
            pictureBoxPreview.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBoxPreview.TabIndex = 1;
            pictureBoxPreview.TabStop = false;
            // 
            // settingsPanel
            // 
            settingsPanel.ColumnCount = 1;
            settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            settingsPanel.Controls.Add(lblRenderPatternType, 0, 0);
            settingsPanel.Controls.Add(cbRenderPattern, 0, 1);
            settingsPanel.Controls.Add(lblTheme, 0, 2);
            settingsPanel.Controls.Add(cbTheme, 0, 3);
            settingsPanel.Controls.Add(btnLaunchSelected, 0, 5);
            settingsPanel.Dock = DockStyle.Fill;
            settingsPanel.Location = new Point(235, 60);
            settingsPanel.Margin = new Padding(0);
            settingsPanel.Name = "settingsPanel";
            settingsPanel.RowCount = 6;
            settingsPanel.RowStyles.Add(new RowStyle());
            settingsPanel.RowStyles.Add(new RowStyle());
            settingsPanel.RowStyles.Add(new RowStyle());
            settingsPanel.RowStyles.Add(new RowStyle());
            settingsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            settingsPanel.Size = new Size(343, 202);
            settingsPanel.TabIndex = 4;
            // 
            // lblRenderPatternType
            // 
            lblRenderPatternType.AutoSize = true;
            lblRenderPatternType.Dock = DockStyle.Fill;
            lblRenderPatternType.Font = new Font("Segoe UI", 10F);
            lblRenderPatternType.Location = new Point(0, 0);
            lblRenderPatternType.Margin = new Padding(0, 0, 0, 5);
            lblRenderPatternType.Name = "lblRenderPatternType";
            lblRenderPatternType.Size = new Size(343, 19);
            lblRenderPatternType.TabIndex = 1;
            lblRenderPatternType.Text = "Тип рендера:";
            // 
            // cbRenderPattern
            // 
            cbRenderPattern.Dock = DockStyle.Top;
            cbRenderPattern.DropDownStyle = ComboBoxStyle.DropDownList;
            cbRenderPattern.Font = new Font("Segoe UI", 11F);
            cbRenderPattern.FormattingEnabled = true;
            cbRenderPattern.Location = new Point(0, 24);
            cbRenderPattern.Margin = new Padding(0);
            cbRenderPattern.Name = "cbRenderPattern";
            cbRenderPattern.Size = new Size(343, 28);
            cbRenderPattern.TabIndex = 2;
            // 
            // lblTheme
            // 
            lblTheme.AutoSize = true;
            lblTheme.Dock = DockStyle.Fill;
            lblTheme.Font = new Font("Segoe UI", 10F);
            lblTheme.Location = new Point(0, 62);
            lblTheme.Margin = new Padding(0, 10, 0, 5);
            lblTheme.Name = "lblTheme";
            lblTheme.Size = new Size(343, 19);
            lblTheme.TabIndex = 3;
            lblTheme.Text = "Тема оформления приложения:";
            // 
            // cbTheme
            // 
            cbTheme.Dock = DockStyle.Top;
            cbTheme.DropDownStyle = ComboBoxStyle.DropDownList;
            cbTheme.Font = new Font("Segoe UI", 11F);
            cbTheme.FormattingEnabled = true;
            cbTheme.Location = new Point(0, 86);
            cbTheme.Margin = new Padding(0);
            cbTheme.Name = "cbTheme";
            cbTheme.Size = new Size(343, 28);
            cbTheme.TabIndex = 4;
            // 
            // btnLaunchSelected
            // 
            btnLaunchSelected.Dock = DockStyle.Fill;
            btnLaunchSelected.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            btnLaunchSelected.Location = new Point(0, 157);
            btnLaunchSelected.Margin = new Padding(0, 5, 0, 0);
            btnLaunchSelected.Name = "btnLaunchSelected";
            btnLaunchSelected.Size = new Size(343, 45);
            btnLaunchSelected.TabIndex = 6;
            btnLaunchSelected.Text = "Запустить";
            btnLaunchSelected.Visible = false;
            btnLaunchSelected.Click += btnLaunchSelected_Click;
            // 
            // richTextBoxDescription
            // 
            richTextBoxDescription.BorderStyle = BorderStyle.None;
            pnlDetails.SetColumnSpan(richTextBoxDescription, 2);
            richTextBoxDescription.Dock = DockStyle.Fill;
            richTextBoxDescription.Font = new Font("Segoe UI", 11F);
            richTextBoxDescription.Location = new Point(15, 277);
            richTextBoxDescription.Margin = new Padding(0, 15, 0, 0);
            richTextBoxDescription.Name = "richTextBoxDescription";
            richTextBoxDescription.ReadOnly = true;
            richTextBoxDescription.Size = new Size(563, 229);
            richTextBoxDescription.TabIndex = 2;
            richTextBoxDescription.Text = "";
            // 
            // LauncherHubForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(896, 521);
            Controls.Add(splitContainerMain);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            MinimumSize = new Size(720, 480);
            Name = "LauncherHubForm";
            Text = "Менеджер фракталов";
            splitContainerMain.Panel1.ResumeLayout(false);
            splitContainerMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainerMain).EndInit();
            splitContainerMain.ResumeLayout(false);
            pnlDetails.ResumeLayout(false);
            headerPanel.ResumeLayout(false);
            headerPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBoxPreview).EndInit();
            settingsPanel.ResumeLayout(false);
            settingsPanel.PerformLayout();
            ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainerMain;
        private FractalExplorer.Controls.FractalAccordionPanel accordionFractals;
        private System.Windows.Forms.TableLayoutPanel pnlDetails;
        private System.Windows.Forms.TableLayoutPanel headerPanel;
        private System.Windows.Forms.Label lblFractalName;
        private System.Windows.Forms.Button btnAboutInfo;
        private System.Windows.Forms.PictureBox pictureBoxPreview;
        private System.Windows.Forms.RichTextBox richTextBoxDescription;
        private System.Windows.Forms.TableLayoutPanel settingsPanel;
        private System.Windows.Forms.Label lblRenderPatternType;
        private System.Windows.Forms.ComboBox cbRenderPattern;
        private System.Windows.Forms.Label lblTheme;
        private System.Windows.Forms.ComboBox cbTheme;
        private System.Windows.Forms.Button btnLaunchSelected;
    }
}
