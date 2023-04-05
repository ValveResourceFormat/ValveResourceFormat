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
            bgColorPickButton = new System.Windows.Forms.Button();
            gamePathsAddFolder = new System.Windows.Forms.Button();
            maxTextureSizeLabel = new System.Windows.Forms.Label();
            maxTextureSizeInput = new System.Windows.Forms.NumericUpDown();
            divider1 = new System.Windows.Forms.Label();
            maxFpsInput = new System.Windows.Forms.NumericUpDown();
            maxFpsLabel = new System.Windows.Forms.Label();
            vsyncLabel = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)maxTextureSizeInput).BeginInit();
            ((System.ComponentModel.ISupportInitialize)maxFpsInput).BeginInit();
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
            gamePathsAdd.Size = new System.Drawing.Size(88, 27);
            gamePathsAdd.TabIndex = 1;
            gamePathsAdd.Text = "Add .vpk";
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
            // bgColorPickButton
            // 
            bgColorPickButton.Location = new System.Drawing.Point(14, 213);
            bgColorPickButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            bgColorPickButton.Name = "bgColorPickButton";
            bgColorPickButton.Size = new System.Drawing.Size(222, 27);
            bgColorPickButton.TabIndex = 4;
            bgColorPickButton.Text = "Set model viewer background color";
            bgColorPickButton.UseVisualStyleBackColor = true;
            bgColorPickButton.Click += OpenBackgroundColorPicker;
            // 
            // gamePathsAddFolder
            // 
            gamePathsAddFolder.Location = new System.Drawing.Point(108, 150);
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
            maxTextureSizeLabel.Location = new System.Drawing.Point(15, 261);
            maxTextureSizeLabel.Name = "maxTextureSizeLabel";
            maxTextureSizeLabel.Size = new System.Drawing.Size(95, 15);
            maxTextureSizeLabel.TabIndex = 6;
            maxTextureSizeLabel.Text = "Max texture size:";
            // 
            // maxTextureSizeInput
            // 
            maxTextureSizeInput.Increment = new decimal(new int[] { 64, 0, 0, 0 });
            maxTextureSizeInput.Location = new System.Drawing.Point(116, 259);
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
            // maxFpsInput
            // 
            maxFpsInput.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            maxFpsInput.Location = new System.Drawing.Point(116, 292);
            maxFpsInput.Maximum = new decimal(new int[] { 500, 0, 0, 0 });
            maxFpsInput.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            maxFpsInput.Name = "maxFpsInput";
            maxFpsInput.Size = new System.Drawing.Size(120, 23);
            maxFpsInput.TabIndex = 10;
            maxFpsInput.Value = new decimal(new int[] { 1, 0, 0, 0 });
            maxFpsInput.ValueChanged += OnMaxFpsValueChanged;
            // 
            // maxFpsLabel
            // 
            maxFpsLabel.AutoSize = true;
            maxFpsLabel.Location = new System.Drawing.Point(15, 294);
            maxFpsLabel.Name = "maxFpsLabel";
            maxFpsLabel.Size = new System.Drawing.Size(55, 15);
            maxFpsLabel.TabIndex = 11;
            maxFpsLabel.Text = "Max FPS:";
            // 
            // vsyncLabel
            // 
            vsyncLabel.AutoSize = true;
            vsyncLabel.Location = new System.Drawing.Point(242, 294);
            vsyncLabel.Name = "vsyncLabel";
            vsyncLabel.Size = new System.Drawing.Size(152, 15);
            vsyncLabel.TabIndex = 12;
            vsyncLabel.Text = "(might be limited by vsync)";
            // 
            // SettingsForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(668, 338);
            Controls.Add(vsyncLabel);
            Controls.Add(maxFpsInput);
            Controls.Add(maxFpsLabel);
            Controls.Add(divider1);
            Controls.Add(maxTextureSizeInput);
            Controls.Add(maxTextureSizeLabel);
            Controls.Add(gamePathsAddFolder);
            Controls.Add(bgColorPickButton);
            Controls.Add(gamePathsLabel);
            Controls.Add(gamePathsRemove);
            Controls.Add(gamePathsAdd);
            Controls.Add(gamePaths);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Name = "SettingsForm";
            ShowIcon = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Settings";
            Load += SettingsForm_Load;
            ((System.ComponentModel.ISupportInitialize)maxTextureSizeInput).EndInit();
            ((System.ComponentModel.ISupportInitialize)maxFpsInput).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ListBox gamePaths;
        private System.Windows.Forms.Button gamePathsAdd;
        private System.Windows.Forms.Button gamePathsRemove;
        private System.Windows.Forms.Label gamePathsLabel;
        private System.Windows.Forms.Button bgColorPickButton;
        private System.Windows.Forms.Button gamePathsAddFolder;
        private System.Windows.Forms.Label maxTextureSizeLabel;
        private System.Windows.Forms.NumericUpDown maxTextureSizeInput;
        private System.Windows.Forms.Label divider1;
        private System.Windows.Forms.NumericUpDown maxFpsInput;
        private System.Windows.Forms.Label maxFpsLabel;
        private System.Windows.Forms.Label vsyncLabel;
    }
}
