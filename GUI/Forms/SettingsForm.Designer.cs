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
            ((System.ComponentModel.ISupportInitialize)maxTextureSizeInput).BeginInit();
            SuspendLayout();
            // 
            // gamePaths
            // 
            gamePaths.FormattingEnabled = true;
            gamePaths.ItemHeight = 15;
            gamePaths.Location = new System.Drawing.Point(14, 33);
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
            gamePathsRemove.TabIndex = 2;
            gamePathsRemove.Text = "Remove";
            gamePathsRemove.UseVisualStyleBackColor = true;
            gamePathsRemove.Click += GamePathRemoveClick;
            // 
            // gamePathsLabel
            // 
            gamePathsLabel.AutoSize = true;
            gamePathsLabel.Location = new System.Drawing.Point(15, 15);
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
            bgColorPickButton.Size = new System.Drawing.Size(182, 27);
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
            gamePathsAddFolder.TabIndex = 5;
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
            maxTextureSizeInput.TabIndex = 8;
            maxTextureSizeInput.Value = new decimal(new int[] { 1, 0, 0, 0 });
            maxTextureSizeInput.ValueChanged += OnMaxTextureSizeValueChanged;
            // 
            // SettingsForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(668, 301);
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
            Text = "Settings";
            Load += SettingsForm_Load;
            ((System.ComponentModel.ISupportInitialize)maxTextureSizeInput).EndInit();
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
    }
}
