namespace GUI.Forms
{
    partial class UpdateAvailableForm
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
            currentVersionLabel = new System.Windows.Forms.Label();
            newVersionLabel = new System.Windows.Forms.Label();
            viewReleaseNotesButton = new System.Windows.Forms.Button();
            downloadButton = new System.Windows.Forms.Button();
            SuspendLayout();
            // 
            // currentVersionLabel
            // 
            currentVersionLabel.AutoSize = true;
            currentVersionLabel.Location = new System.Drawing.Point(12, 9);
            currentVersionLabel.Name = "currentVersionLabel";
            currentVersionLabel.Size = new System.Drawing.Size(94, 15);
            currentVersionLabel.TabIndex = 1;
            currentVersionLabel.Text = "Current version: ";
            // 
            // newVersionLabel
            // 
            newVersionLabel.AutoSize = true;
            newVersionLabel.Location = new System.Drawing.Point(12, 37);
            newVersionLabel.Name = "newVersionLabel";
            newVersionLabel.Size = new System.Drawing.Size(78, 15);
            newVersionLabel.TabIndex = 2;
            newVersionLabel.Text = "New version: ";
            // 
            // viewReleaseNotesButton
            // 
            viewReleaseNotesButton.Location = new System.Drawing.Point(12, 67);
            viewReleaseNotesButton.Name = "viewReleaseNotesButton";
            viewReleaseNotesButton.Size = new System.Drawing.Size(223, 23);
            viewReleaseNotesButton.TabIndex = 3;
            viewReleaseNotesButton.Text = "View release notes";
            viewReleaseNotesButton.UseVisualStyleBackColor = true;
            viewReleaseNotesButton.Click += OnViewReleaseNotesButtonClick;
            // 
            // downloadButton
            // 
            downloadButton.Location = new System.Drawing.Point(12, 96);
            downloadButton.Name = "downloadButton";
            downloadButton.Size = new System.Drawing.Size(223, 23);
            downloadButton.TabIndex = 4;
            downloadButton.Text = "Download new version";
            downloadButton.UseVisualStyleBackColor = true;
            downloadButton.Click += OnDownloadButtonClick;
            // 
            // UpdateAvailableForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(247, 135);
            Controls.Add(downloadButton);
            Controls.Add(viewReleaseNotesButton);
            Controls.Add(newVersionLabel);
            Controls.Add(currentVersionLabel);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "UpdateAvailableForm";
            ShowIcon = false;
            SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Update is available";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private System.Windows.Forms.Label currentVersionLabel;
        private System.Windows.Forms.Label newVersionLabel;
        private System.Windows.Forms.Button viewReleaseNotesButton;
        private System.Windows.Forms.Button downloadButton;
    }
}
