namespace GUI.Forms
{
    partial class SettingsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            gamePaths = new System.Windows.Forms.ListBox();
            gamePathsAdd = new System.Windows.Forms.Button();
            gamePathsRemove = new System.Windows.Forms.Button();
            gamePathsAddFolder = new System.Windows.Forms.Button();
            maxTextureSizeLabel = new System.Windows.Forms.Label();
            maxTextureSizeInput = new System.Windows.Forms.NumericUpDown();
            fovInput = new System.Windows.Forms.NumericUpDown();
            fovLabel = new System.Windows.Forms.Label();
            antiAliasingLabel = new System.Windows.Forms.Label();
            antiAliasingComboBox = new System.Windows.Forms.ComboBox();
            registerAssociationButton = new System.Windows.Forms.Button();
            vsyncLabel = new System.Windows.Forms.Label();
            vsyncCheckBox = new System.Windows.Forms.CheckBox();
            displayFpsCheckBox = new System.Windows.Forms.CheckBox();
            displayFpsLabel = new System.Windows.Forms.Label();
            groupBox1 = new System.Windows.Forms.GroupBox();
            tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            groupBox2 = new System.Windows.Forms.GroupBox();
            tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
            groupBox3 = new System.Windows.Forms.GroupBox();
            ((System.ComponentModel.ISupportInitialize)maxTextureSizeInput).BeginInit();
            ((System.ComponentModel.ISupportInitialize)fovInput).BeginInit();
            groupBox1.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            groupBox2.SuspendLayout();
            tableLayoutPanel2.SuspendLayout();
            groupBox3.SuspendLayout();
            SuspendLayout();
            // 
            // gamePaths
            // 
            gamePaths.Dock = System.Windows.Forms.DockStyle.Top;
            gamePaths.FormattingEnabled = true;
            gamePaths.ItemHeight = 15;
            gamePaths.Location = new System.Drawing.Point(16, 32);
            gamePaths.Margin = new System.Windows.Forms.Padding(0);
            gamePaths.Name = "gamePaths";
            gamePaths.Size = new System.Drawing.Size(420, 109);
            gamePaths.TabIndex = 1;
            // 
            // gamePathsAdd
            // 
            gamePathsAdd.Dock = System.Windows.Forms.DockStyle.Left;
            gamePathsAdd.Location = new System.Drawing.Point(0, 8);
            gamePathsAdd.Margin = new System.Windows.Forms.Padding(0, 8, 8, 8);
            gamePathsAdd.Name = "gamePathsAdd";
            gamePathsAdd.Size = new System.Drawing.Size(150, 26);
            gamePathsAdd.TabIndex = 2;
            gamePathsAdd.Text = "Add .vpk or gameinfo.gi";
            gamePathsAdd.UseVisualStyleBackColor = true;
            gamePathsAdd.Click += GamePathAdd;
            // 
            // gamePathsRemove
            // 
            gamePathsRemove.Dock = System.Windows.Forms.DockStyle.Right;
            gamePathsRemove.Location = new System.Drawing.Point(332, 8);
            gamePathsRemove.Margin = new System.Windows.Forms.Padding(8, 8, 0, 8);
            gamePathsRemove.Name = "gamePathsRemove";
            gamePathsRemove.Size = new System.Drawing.Size(88, 26);
            gamePathsRemove.TabIndex = 4;
            gamePathsRemove.Text = "Remove";
            gamePathsRemove.UseVisualStyleBackColor = true;
            gamePathsRemove.Click += GamePathRemoveClick;
            // 
            // gamePathsAddFolder
            // 
            gamePathsAddFolder.Dock = System.Windows.Forms.DockStyle.Left;
            gamePathsAddFolder.Location = new System.Drawing.Point(166, 8);
            gamePathsAddFolder.Margin = new System.Windows.Forms.Padding(8);
            gamePathsAddFolder.Name = "gamePathsAddFolder";
            gamePathsAddFolder.Size = new System.Drawing.Size(88, 26);
            gamePathsAddFolder.TabIndex = 3;
            gamePathsAddFolder.Text = "Add folder";
            gamePathsAddFolder.UseVisualStyleBackColor = true;
            gamePathsAddFolder.Click += GamePathAddFolder;
            // 
            // maxTextureSizeLabel
            // 
            maxTextureSizeLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            maxTextureSizeLabel.AutoSize = true;
            maxTextureSizeLabel.Location = new System.Drawing.Point(3, 12);
            maxTextureSizeLabel.Name = "maxTextureSizeLabel";
            maxTextureSizeLabel.Size = new System.Drawing.Size(95, 15);
            maxTextureSizeLabel.TabIndex = 0;
            maxTextureSizeLabel.Text = "Max texture size:";
            // 
            // maxTextureSizeInput
            // 
            maxTextureSizeInput.Anchor = System.Windows.Forms.AnchorStyles.Left;
            maxTextureSizeInput.Increment = new decimal(new int[] { 64, 0, 0, 0 });
            maxTextureSizeInput.Location = new System.Drawing.Point(104, 8);
            maxTextureSizeInput.Maximum = new decimal(new int[] { 10240, 0, 0, 0 });
            maxTextureSizeInput.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            maxTextureSizeInput.Name = "maxTextureSizeInput";
            maxTextureSizeInput.Size = new System.Drawing.Size(100, 23);
            maxTextureSizeInput.TabIndex = 5;
            maxTextureSizeInput.Value = new decimal(new int[] { 1, 0, 0, 0 });
            maxTextureSizeInput.ValueChanged += OnMaxTextureSizeValueChanged;
            // 
            // fovInput
            // 
            fovInput.Anchor = System.Windows.Forms.AnchorStyles.Left;
            fovInput.Location = new System.Drawing.Point(104, 48);
            fovInput.Maximum = new decimal(new int[] { 150, 0, 0, 0 });
            fovInput.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            fovInput.Name = "fovInput";
            fovInput.Size = new System.Drawing.Size(100, 23);
            fovInput.TabIndex = 6;
            fovInput.Value = new decimal(new int[] { 1, 0, 0, 0 });
            fovInput.ValueChanged += OnFovValueChanged;
            // 
            // fovLabel
            // 
            fovLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            fovLabel.AutoSize = true;
            fovLabel.Location = new System.Drawing.Point(3, 52);
            fovLabel.Name = "fovLabel";
            fovLabel.Size = new System.Drawing.Size(73, 15);
            fovLabel.TabIndex = 4;
            fovLabel.Text = "Vertical FOV:";
            // 
            // antiAliasingLabel
            // 
            antiAliasingLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            antiAliasingLabel.AutoSize = true;
            antiAliasingLabel.Location = new System.Drawing.Point(3, 92);
            antiAliasingLabel.Name = "antiAliasingLabel";
            antiAliasingLabel.Size = new System.Drawing.Size(77, 15);
            antiAliasingLabel.TabIndex = 6;
            antiAliasingLabel.Text = "Anti-aliasing:";
            // 
            // antiAliasingComboBox
            // 
            antiAliasingComboBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            antiAliasingComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            antiAliasingComboBox.FormattingEnabled = true;
            antiAliasingComboBox.Location = new System.Drawing.Point(104, 88);
            antiAliasingComboBox.Name = "antiAliasingComboBox";
            antiAliasingComboBox.Size = new System.Drawing.Size(100, 23);
            antiAliasingComboBox.TabIndex = 7;
            antiAliasingComboBox.SelectedIndexChanged += OnAntiAliasingValueChanged;
            // 
            // registerAssociationButton
            // 
            registerAssociationButton.Location = new System.Drawing.Point(16, 35);
            registerAssociationButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            registerAssociationButton.Name = "registerAssociationButton";
            registerAssociationButton.Size = new System.Drawing.Size(200, 27);
            registerAssociationButton.TabIndex = 10;
            registerAssociationButton.Text = "Register .vpk file association";
            registerAssociationButton.UseVisualStyleBackColor = true;
            registerAssociationButton.Click += OnRegisterAssociationButtonClick;
            // 
            // vsyncLabel
            // 
            vsyncLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            vsyncLabel.AutoSize = true;
            vsyncLabel.Location = new System.Drawing.Point(3, 132);
            vsyncLabel.Name = "vsyncLabel";
            vsyncLabel.Size = new System.Drawing.Size(76, 15);
            vsyncLabel.TabIndex = 7;
            vsyncLabel.Text = "Vertical Sync:";
            // 
            // vsyncCheckBox
            // 
            vsyncCheckBox.AutoSize = true;
            vsyncCheckBox.Dock = System.Windows.Forms.DockStyle.Fill;
            vsyncCheckBox.Location = new System.Drawing.Point(104, 123);
            vsyncCheckBox.Name = "vsyncCheckBox";
            vsyncCheckBox.Size = new System.Drawing.Size(313, 34);
            vsyncCheckBox.TabIndex = 8;
            vsyncCheckBox.UseVisualStyleBackColor = true;
            vsyncCheckBox.CheckedChanged += OnVsyncValueChanged;
            // 
            // displayFpsCheckBox
            // 
            displayFpsCheckBox.AutoSize = true;
            displayFpsCheckBox.Dock = System.Windows.Forms.DockStyle.Fill;
            displayFpsCheckBox.Location = new System.Drawing.Point(104, 163);
            displayFpsCheckBox.Name = "displayFpsCheckBox";
            displayFpsCheckBox.Size = new System.Drawing.Size(313, 34);
            displayFpsCheckBox.TabIndex = 9;
            displayFpsCheckBox.UseVisualStyleBackColor = true;
            displayFpsCheckBox.CheckedChanged += OnDisplayFpsValueChanged;
            // 
            // displayFpsLabel
            // 
            displayFpsLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            displayFpsLabel.AutoSize = true;
            displayFpsLabel.Location = new System.Drawing.Point(3, 172);
            displayFpsLabel.Name = "displayFpsLabel";
            displayFpsLabel.Size = new System.Drawing.Size(70, 15);
            displayFpsLabel.TabIndex = 3;
            displayFpsLabel.Text = "Display FPS:";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(tableLayoutPanel1);
            groupBox1.Controls.Add(gamePaths);
            groupBox1.Dock = System.Windows.Forms.DockStyle.Top;
            groupBox1.Location = new System.Drawing.Point(16, 16);
            groupBox1.Margin = new System.Windows.Forms.Padding(0, 0, 0, 16);
            groupBox1.Name = "groupBox1";
            groupBox1.Padding = new System.Windows.Forms.Padding(16);
            groupBox1.Size = new System.Drawing.Size(452, 193);
            groupBox1.TabIndex = 1;
            groupBox1.TabStop = false;
            groupBox1.Text = "Game content search paths";
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 3;
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            tableLayoutPanel1.Controls.Add(gamePathsAdd, 0, 0);
            tableLayoutPanel1.Controls.Add(gamePathsAddFolder, 1, 0);
            tableLayoutPanel1.Controls.Add(gamePathsRemove, 2, 0);
            tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Top;
            tableLayoutPanel1.Location = new System.Drawing.Point(16, 141);
            tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 1;
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.Size = new System.Drawing.Size(420, 42);
            tableLayoutPanel1.TabIndex = 1;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(tableLayoutPanel2);
            groupBox2.Dock = System.Windows.Forms.DockStyle.Top;
            groupBox2.Location = new System.Drawing.Point(16, 209);
            groupBox2.Margin = new System.Windows.Forms.Padding(3, 16, 3, 3);
            groupBox2.Name = "groupBox2";
            groupBox2.Padding = new System.Windows.Forms.Padding(16);
            groupBox2.Size = new System.Drawing.Size(452, 252);
            groupBox2.TabIndex = 2;
            groupBox2.TabStop = false;
            groupBox2.Text = "Video settings";
            // 
            // tableLayoutPanel2
            // 
            tableLayoutPanel2.ColumnCount = 2;
            tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            tableLayoutPanel2.Controls.Add(maxTextureSizeLabel, 0, 0);
            tableLayoutPanel2.Controls.Add(displayFpsCheckBox, 1, 4);
            tableLayoutPanel2.Controls.Add(maxTextureSizeInput, 1, 0);
            tableLayoutPanel2.Controls.Add(displayFpsLabel, 0, 4);
            tableLayoutPanel2.Controls.Add(fovLabel, 0, 1);
            tableLayoutPanel2.Controls.Add(vsyncCheckBox, 1, 3);
            tableLayoutPanel2.Controls.Add(antiAliasingLabel, 0, 2);
            tableLayoutPanel2.Controls.Add(vsyncLabel, 0, 3);
            tableLayoutPanel2.Controls.Add(antiAliasingComboBox, 1, 2);
            tableLayoutPanel2.Controls.Add(fovInput, 1, 1);
            tableLayoutPanel2.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel2.Location = new System.Drawing.Point(16, 32);
            tableLayoutPanel2.Name = "tableLayoutPanel2";
            tableLayoutPanel2.RowCount = 6;
            tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle());
            tableLayoutPanel2.Size = new System.Drawing.Size(420, 204);
            tableLayoutPanel2.TabIndex = 0;
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(registerAssociationButton);
            groupBox3.Dock = System.Windows.Forms.DockStyle.Top;
            groupBox3.Location = new System.Drawing.Point(16, 461);
            groupBox3.Margin = new System.Windows.Forms.Padding(0);
            groupBox3.Name = "groupBox3";
            groupBox3.Padding = new System.Windows.Forms.Padding(16);
            groupBox3.Size = new System.Drawing.Size(452, 82);
            groupBox3.TabIndex = 3;
            groupBox3.TabStop = false;
            groupBox3.Text = "Windows explorer";
            // 
            // SettingsForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            AutoScroll = true;
            ClientSize = new System.Drawing.Size(484, 561);
            Controls.Add(groupBox3);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MinimumSize = new System.Drawing.Size(300, 200);
            Name = "SettingsForm";
            Padding = new System.Windows.Forms.Padding(16);
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Settings";
            Load += SettingsForm_Load;
            ((System.ComponentModel.ISupportInitialize)maxTextureSizeInput).EndInit();
            ((System.ComponentModel.ISupportInitialize)fovInput).EndInit();
            groupBox1.ResumeLayout(false);
            tableLayoutPanel1.ResumeLayout(false);
            groupBox2.ResumeLayout(false);
            tableLayoutPanel2.ResumeLayout(false);
            tableLayoutPanel2.PerformLayout();
            groupBox3.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.ListBox gamePaths;
        private System.Windows.Forms.Button gamePathsAdd;
        private System.Windows.Forms.Button gamePathsRemove;
        private System.Windows.Forms.Button gamePathsAddFolder;
        private System.Windows.Forms.Label maxTextureSizeLabel;
        private System.Windows.Forms.NumericUpDown maxTextureSizeInput;
        private System.Windows.Forms.NumericUpDown fovInput;
        private System.Windows.Forms.Label fovLabel;
        private System.Windows.Forms.Label antiAliasingLabel;
        private System.Windows.Forms.ComboBox antiAliasingComboBox;
        private System.Windows.Forms.Label vsyncLabel;
        private System.Windows.Forms.CheckBox vsyncCheckBox;
        private System.Windows.Forms.Button registerAssociationButton;
        private System.Windows.Forms.CheckBox displayFpsCheckBox;
        private System.Windows.Forms.Label displayFpsLabel;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
        private System.Windows.Forms.GroupBox groupBox3;
    }
}
