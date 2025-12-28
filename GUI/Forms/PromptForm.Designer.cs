using GUI.Controls;

namespace GUI.Forms
{
    partial class PromptForm
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
            textLabel = new System.Windows.Forms.Label();
            inputTextBox = new System.Windows.Forms.TextBox();
            cancelButton = new ThemedButton();
            submitButton = new ThemedButton();
            SuspendLayout();
            // 
            // textLabel
            // 
            textLabel.AutoSize = true;
            textLabel.Location = new System.Drawing.Point(13, 10);
            textLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            textLabel.Name = "textLabel";
            textLabel.Size = new System.Drawing.Size(36, 19);
            textLabel.TabIndex = 7;
            textLabel.Text = "Text:";
            // 
            // inputTextBox
            // 
            inputTextBox.Location = new System.Drawing.Point(13, 40);
            inputTextBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            inputTextBox.Name = "inputTextBox";
            inputTextBox.Size = new System.Drawing.Size(380, 25);
            inputTextBox.TabIndex = 6;
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
            cancelButton.Location = new System.Drawing.Point(13, 76);
            cancelButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(88, 31);
            cancelButton.Style = true;
            cancelButton.TabIndex = 5;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = false;
            // 
            // submitButton
            // 
            submitButton.BackColor = System.Drawing.Color.FromArgb(188, 188, 188);
            submitButton.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            submitButton.CornerRadius = 5;
            submitButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            submitButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            submitButton.ForeColor = System.Drawing.Color.Black;
            submitButton.HoveredBackColor = System.Drawing.Color.FromArgb(140, 191, 255);
            submitButton.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            submitButton.Location = new System.Drawing.Point(305, 76);
            submitButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            submitButton.Name = "submitButton";
            submitButton.Size = new System.Drawing.Size(88, 31);
            submitButton.Style = true;
            submitButton.TabIndex = 0;
            submitButton.Text = "Submit";
            submitButton.UseVisualStyleBackColor = false;
            // 
            // PromptForm
            // 
            AcceptButton = submitButton;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(406, 127);
            Controls.Add(textLabel);
            Controls.Add(inputTextBox);
            Controls.Add(cancelButton);
            Controls.Add(submitButton);
            Font = new System.Drawing.Font("Segoe UI", 10F);
            ForeColor = System.Drawing.Color.Black;
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "PromptForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "PromptForm";
            TopMost = true;
            Load += PromptForm_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label textLabel;
        private System.Windows.Forms.TextBox inputTextBox;
        private ThemedButton cancelButton;
        private ThemedButton submitButton;
    }
}
