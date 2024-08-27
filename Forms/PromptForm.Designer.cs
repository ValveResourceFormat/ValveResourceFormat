using GUI.Theme;

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
            cancelButton = new CustomButton();
            submitButton = new CustomButton();
            SuspendLayout();
            // 
            // textLabel
            // 
            textLabel.AutoSize = true;
            textLabel.Location = new System.Drawing.Point(13, 9);
            textLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            textLabel.Name = "textLabel";
            textLabel.Size = new System.Drawing.Size(31, 15);
            textLabel.TabIndex = 7;
            textLabel.Text = "Text:";
            // 
            // inputTextBox
            // 
            inputTextBox.Location = new System.Drawing.Point(13, 27);
            inputTextBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            inputTextBox.Name = "inputTextBox";
            inputTextBox.Size = new System.Drawing.Size(380, 23);
            inputTextBox.TabIndex = 6;
            // 
            // cancelButton
            // 
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Location = new System.Drawing.Point(13, 63);
            cancelButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(88, 27);
            cancelButton.TabIndex = 5;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            // 
            // submitButton
            // 
            submitButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            submitButton.Location = new System.Drawing.Point(305, 63);
            submitButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            submitButton.Name = "submitButton";
            submitButton.Size = new System.Drawing.Size(88, 27);
            submitButton.TabIndex = 0;
            submitButton.Text = "Submit";
            submitButton.UseVisualStyleBackColor = true;
            // 
            // PromptForm
            // 
            AcceptButton = submitButton;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(406, 102);
            Controls.Add(textLabel);
            Controls.Add(inputTextBox);
            Controls.Add(cancelButton);
            Controls.Add(submitButton);
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
        private CustomButton cancelButton;
        private CustomButton submitButton;
    }
}
