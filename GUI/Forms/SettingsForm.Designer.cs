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
            this.gamePaths = new System.Windows.Forms.ListBox();
            this.gamePathsAdd = new System.Windows.Forms.Button();
            this.gamePathsRemove = new System.Windows.Forms.Button();
            this.gamePathsLabel = new System.Windows.Forms.Label();
            this.bgColorPickButton = new System.Windows.Forms.Button();
            this.gamePathsAddFolder = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // gamePaths
            // 
            this.gamePaths.FormattingEnabled = true;
            this.gamePaths.Location = new System.Drawing.Point(12, 29);
            this.gamePaths.Name = "gamePaths";
            this.gamePaths.Size = new System.Drawing.Size(549, 95);
            this.gamePaths.TabIndex = 0;
            // 
            // gamePathsAdd
            // 
            this.gamePathsAdd.Location = new System.Drawing.Point(12, 130);
            this.gamePathsAdd.Name = "gamePathsAdd";
            this.gamePathsAdd.Size = new System.Drawing.Size(75, 23);
            this.gamePathsAdd.TabIndex = 1;
            this.gamePathsAdd.Text = "Add .vpk";
            this.gamePathsAdd.UseVisualStyleBackColor = true;
            this.gamePathsAdd.Click += new System.EventHandler(this.GamePathAdd);
            // 
            // gamePathsRemove
            // 
            this.gamePathsRemove.Location = new System.Drawing.Point(486, 130);
            this.gamePathsRemove.Name = "gamePathsRemove";
            this.gamePathsRemove.Size = new System.Drawing.Size(75, 23);
            this.gamePathsRemove.TabIndex = 2;
            this.gamePathsRemove.Text = "Remove";
            this.gamePathsRemove.UseVisualStyleBackColor = true;
            this.gamePathsRemove.Click += new System.EventHandler(this.GamePathRemoveClick);
            // 
            // gamePathsLabel
            // 
            this.gamePathsLabel.AutoSize = true;
            this.gamePathsLabel.Location = new System.Drawing.Point(13, 13);
            this.gamePathsLabel.Name = "gamePathsLabel";
            this.gamePathsLabel.Size = new System.Drawing.Size(138, 13);
            this.gamePathsLabel.TabIndex = 3;
            this.gamePathsLabel.Text = "Game content search paths";
            // 
            // bgColorPickButton
            // 
            this.bgColorPickButton.Location = new System.Drawing.Point(12, 185);
            this.bgColorPickButton.Name = "bgColorPickButton";
            this.bgColorPickButton.Size = new System.Drawing.Size(156, 23);
            this.bgColorPickButton.TabIndex = 4;
            this.bgColorPickButton.Text = "Set model viewer background color";
            this.bgColorPickButton.UseVisualStyleBackColor = true;
            this.bgColorPickButton.Click += new System.EventHandler(this.Button1_Click);
            // 
            // gamePathsAddFolder
            // 
            this.gamePathsAddFolder.Location = new System.Drawing.Point(93, 130);
            this.gamePathsAddFolder.Name = "gamePathsAddFolder";
            this.gamePathsAddFolder.Size = new System.Drawing.Size(75, 23);
            this.gamePathsAddFolder.TabIndex = 5;
            this.gamePathsAddFolder.Text = "Add folder";
            this.gamePathsAddFolder.UseVisualStyleBackColor = true;
            this.gamePathsAddFolder.Click += new System.EventHandler(this.GamePathAddFolder);
            // 
            // SettingsForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(573, 261);
            this.Controls.Add(this.gamePathsAddFolder);
            this.Controls.Add(this.bgColorPickButton);
            this.Controls.Add(this.gamePathsLabel);
            this.Controls.Add(this.gamePathsRemove);
            this.Controls.Add(this.gamePathsAdd);
            this.Controls.Add(this.gamePaths);
            this.Name = "SettingsForm";
            this.ShowIcon = false;
            this.Text = "Settings";
            this.Load += new System.EventHandler(this.SettingsForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ListBox gamePaths;
        private System.Windows.Forms.Button gamePathsAdd;
        private System.Windows.Forms.Button gamePathsRemove;
        private System.Windows.Forms.Label gamePathsLabel;
        private System.Windows.Forms.Button bgColorPickButton;
        private System.Windows.Forms.Button gamePathsAddFolder;
    }
}
