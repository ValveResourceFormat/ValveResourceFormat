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
            gamePathsLabel = new System.Windows.Forms.Label();
            gamePathsAddFolder = new System.Windows.Forms.Button();
            maxTextureSizeLabel = new System.Windows.Forms.Label();
            maxTextureSizeInput = new System.Windows.Forms.NumericUpDown();
            divider1 = new System.Windows.Forms.Label();
            fovInput = new System.Windows.Forms.NumericUpDown();
            fovLabel = new System.Windows.Forms.Label();
            antiAliasingLabel = new System.Windows.Forms.Label();
            antiAliasingComboBox = new System.Windows.Forms.ComboBox();
            registerAssociationButton = new System.Windows.Forms.Button();
            vsyncLabel = new System.Windows.Forms.Label();
            vsyncCheckBox = new System.Windows.Forms.CheckBox();
            displayFpsCheckBox = new System.Windows.Forms.CheckBox();
            displayFpsLabel = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)maxTextureSizeInput).BeginInit();
            ((System.ComponentModel.ISupportInitialize)fovInput).BeginInit();
            SuspendLayout();
            // 
            // gamePaths
            // 
            gamePaths.FormattingEnabled = true;
            gamePaths.ItemHeight = 15;
            gamePaths.Location = new System.Drawing.Point(14, 35);
            gamePaths.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            gamePaths.Name = "gamePaths";
            gamePaths.Size = new System.Drawing.Size(640, 109);
            gamePaths.TabIndex = 0;
            // 
            // gamePathsAdd
            // 
            gamePathsAdd.Location = new System.Drawing.Point(14, 150);
            gamePathsAdd.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            gamePathsAdd.Name = "gamePathsAdd";
            gamePathsAdd.Size = new System.Drawing.Size(150, 27);
            gamePathsAdd.TabIndex = 1;
            gamePathsAdd.Text = "Add .vpk or gameinfo.gi";
            gamePathsAdd.UseVisualStyleBackColor = true;
            gamePathsAdd.Click += GamePathAdd;
            // 
            // gamePathsRemove
            // 
            gamePathsRemove.Location = new System.Drawing.Point(567, 150);
            gamePathsRemove.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            gamePathsRemove.Name = "gamePathsRemove";
            gamePathsRemove.Size = new System.Drawing.Size(88, 27);
            gamePathsRemove.TabIndex = 3;
            gamePathsRemove.Text = "Remove";
            gamePathsRemove.UseVisualStyleBackColor = true;
            gamePathsRemove.Click += GamePathRemoveClick;
            // 
            // gamePathsLabel
            // 
            gamePathsLabel.AutoSize = true;
            gamePathsLabel.Location = new System.Drawing.Point(13, 9);
            gamePathsLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            gamePathsLabel.Name = "gamePathsLabel";
            gamePathsLabel.Size = new System.Drawing.Size(151, 15);
            gamePathsLabel.TabIndex = 3;
            gamePathsLabel.Text = "Game content search paths";
            // 
            // gamePathsAddFolder
            // 
            gamePathsAddFolder.Location = new System.Drawing.Point(172, 150);
            gamePathsAddFolder.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            gamePathsAddFolder.Name = "gamePathsAddFolder";
            gamePathsAddFolder.Size = new System.Drawing.Size(88, 27);
            gamePathsAddFolder.TabIndex = 2;
            gamePathsAddFolder.Text = "Add folder";
            gamePathsAddFolder.UseVisualStyleBackColor = true;
            gamePathsAddFolder.Click += GamePathAddFolder;
            // 
            // maxTextureSizeLabel
            // 
            maxTextureSizeLabel.AutoSize = true;
            maxTextureSizeLabel.Location = new System.Drawing.Point(14, 219);
            maxTextureSizeLabel.Name = "maxTextureSizeLabel";
            maxTextureSizeLabel.Size = new System.Drawing.Size(95, 15);
            maxTextureSizeLabel.TabIndex = 6;
            maxTextureSizeLabel.Text = "Max texture size:";
            // 
            // maxTextureSizeInput
            // 
            maxTextureSizeInput.Increment = new decimal(new int[] { 64, 0, 0, 0 });
            maxTextureSizeInput.Location = new System.Drawing.Point(115, 217);
            maxTextureSizeInput.Maximum = new decimal(new int[] { 10240, 0, 0, 0 });
            maxTextureSizeInput.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            maxTextureSizeInput.Name = "maxTextureSizeInput";
            maxTextureSizeInput.Size = new System.Drawing.Size(120, 23);
            maxTextureSizeInput.TabIndex = 5;
            maxTextureSizeInput.Value = new decimal(new int[] { 1, 0, 0, 0 });
            maxTextureSizeInput.ValueChanged += OnMaxTextureSizeValueChanged;
            // 
            // divider1
            // 
            divider1.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            divider1.Location = new System.Drawing.Point(14, 196);
            divider1.Name = "divider1";
            divider1.Size = new System.Drawing.Size(640, 2);
            divider1.TabIndex = 9;
            // 
            // fovInput
            // 
            fovInput.Location = new System.Drawing.Point(115, 247);
            fovInput.Maximum = new decimal(new int[] { 150, 0, 0, 0 });
            fovInput.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            fovInput.Name = "fovInput";
            fovInput.Size = new System.Drawing.Size(120, 23);
            fovInput.TabIndex = 10;
            fovInput.Value = new decimal(new int[] { 1, 0, 0, 0 });
            fovInput.ValueChanged += OnFovValueChanged;
            // 
            // fovLabel
            // 
            fovLabel.AutoSize = true;
            fovLabel.Location = new System.Drawing.Point(14, 249);
            fovLabel.Name = "fovLabel";
            fovLabel.Size = new System.Drawing.Size(73, 15);
            fovLabel.TabIndex = 11;
            fovLabel.Text = "Vertical FOV:";
            // 
            // antiAliasingLabel
            // 
            antiAliasingLabel.AutoSize = true;
            antiAliasingLabel.Location = new System.Drawing.Point(14, 279);
            antiAliasingLabel.Name = "antiAliasingLabel";
            antiAliasingLabel.Size = new System.Drawing.Size(77, 15);
            antiAliasingLabel.TabIndex = 12;
            antiAliasingLabel.Text = "Anti-aliasing:";
            // 
            // antiAliasingComboBox
            // 
            antiAliasingComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            antiAliasingComboBox.FormattingEnabled = true;
            antiAliasingComboBox.Location = new System.Drawing.Point(115, 276);
            antiAliasingComboBox.Name = "antiAliasingComboBox";
            antiAliasingComboBox.Size = new System.Drawing.Size(120, 23);
            antiAliasingComboBox.TabIndex = 13;
            antiAliasingComboBox.SelectedIndexChanged += OnAntiAliasingValueChanged;
            // 
            // registerAssociationButton
            // 
            registerAssociationButton.Location = new System.Drawing.Point(432, 213);
            registerAssociationButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            registerAssociationButton.Name = "registerAssociationButton";
            registerAssociationButton.Size = new System.Drawing.Size(222, 27);
            registerAssociationButton.TabIndex = 16;
            registerAssociationButton.Text = "Register .vpk file association";
            registerAssociationButton.UseVisualStyleBackColor = true;
            registerAssociationButton.Click += OnRegisterAssociationButtonClick;
            // 
            // vsyncLabel
            // 
            vsyncLabel.AutoSize = true;
            vsyncLabel.Location = new System.Drawing.Point(14, 309);
            vsyncLabel.Name = "vsyncLabel";
            vsyncLabel.Size = new System.Drawing.Size(76, 15);
            vsyncLabel.TabIndex = 14;
            vsyncLabel.Text = "Vertical Sync:";
            // 
            // vsyncCheckBox
            // 
            vsyncCheckBox.AutoSize = true;
            vsyncCheckBox.Location = new System.Drawing.Point(115, 309);
            vsyncCheckBox.Name = "vsyncCheckBox";
            vsyncCheckBox.Size = new System.Drawing.Size(15, 14);
            vsyncCheckBox.TabIndex = 15;
            vsyncCheckBox.UseVisualStyleBackColor = true;
            vsyncCheckBox.CheckedChanged += OnVsyncValueChanged;
            // 
            // displayFpsCheckBox
            // 
            displayFpsCheckBox.AutoSize = true;
            displayFpsCheckBox.Location = new System.Drawing.Point(115, 339);
            displayFpsCheckBox.Name = "displayFpsCheckBox";
            displayFpsCheckBox.Size = new System.Drawing.Size(15, 14);
            displayFpsCheckBox.TabIndex = 18;
            displayFpsCheckBox.UseVisualStyleBackColor = true;
            displayFpsCheckBox.CheckedChanged += OnDisplayFpsValueChanged;
            // 
            // displayFpsLabel
            // 
            displayFpsLabel.AutoSize = true;
            displayFpsLabel.Location = new System.Drawing.Point(14, 339);
            displayFpsLabel.Name = "displayFpsLabel";
            displayFpsLabel.Size = new System.Drawing.Size(70, 15);
            displayFpsLabel.TabIndex = 17;
            displayFpsLabel.Text = "Display FPS:";
            // 
            // SettingsForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(668, 387);
            Controls.Add(displayFpsCheckBox);
            Controls.Add(displayFpsLabel);
            Controls.Add(registerAssociationButton);
            Controls.Add(vsyncCheckBox);
            Controls.Add(vsyncLabel);
            Controls.Add(antiAliasingComboBox);
            Controls.Add(antiAliasingLabel);
            Controls.Add(fovInput);
            Controls.Add(fovLabel);
            Controls.Add(divider1);
            Controls.Add(maxTextureSizeInput);
            Controls.Add(maxTextureSizeLabel);
            Controls.Add(gamePathsAddFolder);
            Controls.Add(gamePathsLabel);
            Controls.Add(gamePathsRemove);
            Controls.Add(gamePathsAdd);
            Controls.Add(gamePaths);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Name = "SettingsForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Settings";
            Load += SettingsForm_Load;
            ((System.ComponentModel.ISupportInitialize)maxTextureSizeInput).EndInit();
            ((System.ComponentModel.ISupportInitialize)fovInput).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ListBox gamePaths;
        private System.Windows.Forms.Button gamePathsAdd;
        private System.Windows.Forms.Button gamePathsRemove;
        private System.Windows.Forms.Label gamePathsLabel;
        private System.Windows.Forms.Button gamePathsAddFolder;
        private System.Windows.Forms.Label maxTextureSizeLabel;
        private System.Windows.Forms.NumericUpDown maxTextureSizeInput;
        private System.Windows.Forms.Label divider1;
        private System.Windows.Forms.NumericUpDown fovInput;
        private System.Windows.Forms.Label fovLabel;
        private System.Windows.Forms.Label antiAliasingLabel;
        private System.Windows.Forms.ComboBox antiAliasingComboBox;
        private System.Windows.Forms.Label vsyncLabel;
        private System.Windows.Forms.CheckBox vsyncCheckBox;
        private System.Windows.Forms.Button registerAssociationButton;
        private System.Windows.Forms.CheckBox displayFpsCheckBox;
        private System.Windows.Forms.Label displayFpsLabel;
    }
}
