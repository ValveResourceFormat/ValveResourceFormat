using GUI.Theme;

namespace GUI.Forms
{
    partial class SearchForm
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
            findButton = new CustomButton();
            cancelButton = new CustomButton();
            findTextBox = new System.Windows.Forms.TextBox();
            findLabel = new System.Windows.Forms.Label();
            searchTypeComboBox = new CustomComboBox();
            SuspendLayout();
            // 
            // findButton
            // 
            findButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            findButton.Location = new System.Drawing.Point(304, 14);
            findButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            findButton.Name = "findButton";
            findButton.Size = new System.Drawing.Size(88, 27);
            findButton.TabIndex = 0;
            findButton.Text = "Find";
            findButton.UseVisualStyleBackColor = true;
            // 
            // cancelButton
            // 
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Location = new System.Drawing.Point(304, 47);
            cancelButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(88, 27);
            cancelButton.TabIndex = 1;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            // 
            // findTextBox
            // 
            findTextBox.Location = new System.Drawing.Point(80, 16);
            findTextBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            findTextBox.Name = "findTextBox";
            findTextBox.Size = new System.Drawing.Size(216, 23);
            findTextBox.TabIndex = 2;
            // 
            // findLabel
            // 
            findLabel.AutoSize = true;
            findLabel.Location = new System.Drawing.Point(14, 20);
            findLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            findLabel.Name = "findLabel";
            findLabel.Size = new System.Drawing.Size(62, 15);
            findLabel.TabIndex = 3;
            findLabel.Text = "Find what:";
            // 
            // searchTypeComboBox
            // 
            searchTypeComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            searchTypeComboBox.FormattingEnabled = true;
            searchTypeComboBox.Location = new System.Drawing.Point(80, 47);
            searchTypeComboBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            searchTypeComboBox.Name = "searchTypeComboBox";
            searchTypeComboBox.Size = new System.Drawing.Size(216, 23);
            searchTypeComboBox.TabIndex = 5;
            // 
            // SearchForm
            // 
            AcceptButton = findButton;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(406, 102);
            Controls.Add(searchTypeComboBox);
            Controls.Add(findLabel);
            Controls.Add(findTextBox);
            Controls.Add(cancelButton);
            Controls.Add(findButton);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "SearchForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Find";
            Load += SearchForm_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private CustomButton findButton;
        private CustomButton cancelButton;
        private System.Windows.Forms.TextBox findTextBox;
        private System.Windows.Forms.Label findLabel;
        private CustomComboBox searchTypeComboBox;
    }
}
