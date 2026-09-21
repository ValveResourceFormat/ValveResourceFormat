using GUI.Controls;

namespace GUI.Forms
{
    partial class CustomVmdlExtractOptionsForm
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
            autoBuildCheckBox = new System.Windows.Forms.CheckBox();
            resourceCompilerLabel = new System.Windows.Forms.Label();
            resourceCompilerTextBox = new System.Windows.Forms.TextBox();
            browseResourceCompilerButton = new ThemedButton();
            gameDirLabel = new System.Windows.Forms.Label();
            gameDirTextBox = new System.Windows.Forms.TextBox();
            browseGameDirButton = new ThemedButton();
            hintLabel = new System.Windows.Forms.Label();
            verboseCheckBox = new System.Windows.Forms.CheckBox();
            cancelButton = new ThemedButton();
            submitButton = new ThemedButton();
            SuspendLayout();
            //
            // autoBuildCheckBox
            //
            autoBuildCheckBox.AutoSize = true;
            autoBuildCheckBox.Location = new System.Drawing.Point(13, 13);
            autoBuildCheckBox.Name = "autoBuildCheckBox";
            autoBuildCheckBox.Size = new System.Drawing.Size(340, 23);
            autoBuildCheckBox.TabIndex = 0;
            autoBuildCheckBox.Text = "Compile output with resourcecompiler.exe (auto-build)";
            autoBuildCheckBox.UseVisualStyleBackColor = true;
            autoBuildCheckBox.CheckedChanged += AutoBuildCheckBox_CheckedChanged;
            //
            // resourceCompilerLabel
            //
            resourceCompilerLabel.AutoSize = true;
            resourceCompilerLabel.Location = new System.Drawing.Point(13, 46);
            resourceCompilerLabel.Name = "resourceCompilerLabel";
            resourceCompilerLabel.Size = new System.Drawing.Size(140, 19);
            resourceCompilerLabel.TabIndex = 1;
            resourceCompilerLabel.Text = "resourcecompiler.exe:";
            //
            // resourceCompilerTextBox
            //
            resourceCompilerTextBox.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            resourceCompilerTextBox.Location = new System.Drawing.Point(13, 68);
            resourceCompilerTextBox.Name = "resourceCompilerTextBox";
            resourceCompilerTextBox.Size = new System.Drawing.Size(339, 25);
            resourceCompilerTextBox.TabIndex = 2;
            //
            // browseResourceCompilerButton
            //
            browseResourceCompilerButton.BackColor = System.Drawing.Color.FromArgb(188, 188, 188);
            browseResourceCompilerButton.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            browseResourceCompilerButton.CornerRadius = 5;
            browseResourceCompilerButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            browseResourceCompilerButton.ForeColor = System.Drawing.Color.Black;
            browseResourceCompilerButton.HoveredBackColor = System.Drawing.Color.FromArgb(140, 191, 255);
            browseResourceCompilerButton.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            browseResourceCompilerButton.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            browseResourceCompilerButton.Location = new System.Drawing.Point(358, 66);
            browseResourceCompilerButton.Name = "browseResourceCompilerButton";
            browseResourceCompilerButton.Size = new System.Drawing.Size(88, 29);
            browseResourceCompilerButton.Style = true;
            browseResourceCompilerButton.TabIndex = 3;
            browseResourceCompilerButton.Text = "Browse...";
            browseResourceCompilerButton.UseVisualStyleBackColor = false;
            browseResourceCompilerButton.Click += BrowseResourceCompilerButton_Click;
            //
            // gameDirLabel
            //
            gameDirLabel.AutoSize = true;
            gameDirLabel.Location = new System.Drawing.Point(13, 104);
            gameDirLabel.Name = "gameDirLabel";
            gameDirLabel.Size = new System.Drawing.Size(120, 19);
            gameDirLabel.TabIndex = 4;
            gameDirLabel.Text = "\"-game\" directory:";
            //
            // gameDirTextBox
            //
            gameDirTextBox.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            gameDirTextBox.Location = new System.Drawing.Point(13, 126);
            gameDirTextBox.Name = "gameDirTextBox";
            gameDirTextBox.Size = new System.Drawing.Size(339, 25);
            gameDirTextBox.TabIndex = 5;
            //
            // browseGameDirButton
            //
            browseGameDirButton.BackColor = System.Drawing.Color.FromArgb(188, 188, 188);
            browseGameDirButton.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            browseGameDirButton.CornerRadius = 5;
            browseGameDirButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            browseGameDirButton.ForeColor = System.Drawing.Color.Black;
            browseGameDirButton.HoveredBackColor = System.Drawing.Color.FromArgb(140, 191, 255);
            browseGameDirButton.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            browseGameDirButton.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            browseGameDirButton.Location = new System.Drawing.Point(358, 124);
            browseGameDirButton.Name = "browseGameDirButton";
            browseGameDirButton.Size = new System.Drawing.Size(88, 29);
            browseGameDirButton.Style = true;
            browseGameDirButton.TabIndex = 6;
            browseGameDirButton.Text = "Browse...";
            browseGameDirButton.UseVisualStyleBackColor = false;
            browseGameDirButton.Click += BrowseGameDirButton_Click;
            //
            // hintLabel
            //
            hintLabel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            hintLabel.AutoSize = true;
            hintLabel.Location = new System.Drawing.Point(13, 160);
            hintLabel.MaximumSize = new System.Drawing.Size(433, 0);
            hintLabel.Name = "hintLabel";
            hintLabel.TabIndex = 7;
            hintLabel.Text = "Auto-build re-compiles every exported .vmdl and, when the original package is still open, patches motion data and transplants morph/physics blocks from it. The output folder must sit under a \"content\" directory next to a sibling \"game\" directory (standard Source 2 addon layout).";
            //
            // verboseCheckBox
            //
            verboseCheckBox.AutoSize = true;
            verboseCheckBox.Location = new System.Drawing.Point(13, 224);
            verboseCheckBox.Name = "verboseCheckBox";
            verboseCheckBox.Size = new System.Drawing.Size(140, 23);
            verboseCheckBox.TabIndex = 8;
            verboseCheckBox.Text = "Verbose logging";
            verboseCheckBox.UseVisualStyleBackColor = true;
            //
            // cancelButton
            //
            cancelButton.BackColor = System.Drawing.Color.FromArgb(188, 188, 188);
            cancelButton.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            cancelButton.CornerRadius = 5;
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            cancelButton.ForeColor = System.Drawing.Color.Black;
            cancelButton.HoveredBackColor = System.Drawing.Color.FromArgb(140, 191, 255);
            cancelButton.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            cancelButton.Location = new System.Drawing.Point(13, 260);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(88, 31);
            cancelButton.Style = true;
            cancelButton.TabIndex = 9;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = false;
            //
            // submitButton
            //
            submitButton.BackColor = System.Drawing.Color.FromArgb(188, 188, 188);
            submitButton.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            submitButton.CornerRadius = 5;
            submitButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            submitButton.ForeColor = System.Drawing.Color.Black;
            submitButton.HoveredBackColor = System.Drawing.Color.FromArgb(140, 191, 255);
            submitButton.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            submitButton.Location = new System.Drawing.Point(358, 260);
            submitButton.Name = "submitButton";
            submitButton.Size = new System.Drawing.Size(88, 31);
            submitButton.Style = true;
            submitButton.TabIndex = 10;
            submitButton.Text = "Export";
            submitButton.UseVisualStyleBackColor = false;
            submitButton.Click += SubmitButton_Click;
            //
            // CustomVmdlExtractOptionsForm
            //
            AcceptButton = submitButton;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(459, 304);
            Controls.Add(autoBuildCheckBox);
            Controls.Add(resourceCompilerLabel);
            Controls.Add(resourceCompilerTextBox);
            Controls.Add(browseResourceCompilerButton);
            Controls.Add(gameDirLabel);
            Controls.Add(gameDirTextBox);
            Controls.Add(browseGameDirButton);
            Controls.Add(hintLabel);
            Controls.Add(verboseCheckBox);
            Controls.Add(cancelButton);
            Controls.Add(submitButton);
            Font = new System.Drawing.Font("Segoe UI", 10F);
            ForeColor = System.Drawing.Color.Black;
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            Name = "CustomVmdlExtractOptionsForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Custom VMDL Extractor Options";
            TopMost = true;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.CheckBox autoBuildCheckBox;
        private System.Windows.Forms.Label resourceCompilerLabel;
        private System.Windows.Forms.TextBox resourceCompilerTextBox;
        private ThemedButton browseResourceCompilerButton;
        private System.Windows.Forms.Label gameDirLabel;
        private System.Windows.Forms.TextBox gameDirTextBox;
        private ThemedButton browseGameDirButton;
        private System.Windows.Forms.Label hintLabel;
        private System.Windows.Forms.CheckBox verboseCheckBox;
        private ThemedButton cancelButton;
        private ThemedButton submitButton;
    }
}
