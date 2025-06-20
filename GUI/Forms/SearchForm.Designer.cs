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
            findButton = new System.Windows.Forms.Button();
            cancelButton = new System.Windows.Forms.Button();
            findTextBox = new System.Windows.Forms.TextBox();
            findLabel = new System.Windows.Forms.Label();
            searchTypeComboBox = new System.Windows.Forms.ComboBox();
            SuspendLayout();
            // 
            // findButton
            // 
            findButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            findButton.Location = new System.Drawing.Point(304, 16);
            findButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            findButton.Name = "findButton";
            findButton.Size = new System.Drawing.Size(88, 31);
            findButton.TabIndex = 0;
            findButton.Text = "Find";
            findButton.UseVisualStyleBackColor = true;
            // 
            // cancelButton
            // 
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Location = new System.Drawing.Point(304, 53);
            cancelButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(88, 31);
            cancelButton.TabIndex = 1;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            // 
            // findTextBox
            // 
            findTextBox.Location = new System.Drawing.Point(80, 18);
            findTextBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            findTextBox.Name = "findTextBox";
            findTextBox.Size = new System.Drawing.Size(216, 25);
            findTextBox.TabIndex = 2;
            // 
            // findLabel
            // 
            findLabel.AutoSize = true;
            findLabel.Location = new System.Drawing.Point(14, 23);
            findLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            findLabel.Name = "findLabel";
            findLabel.Size = new System.Drawing.Size(72, 19);
            findLabel.TabIndex = 3;
            findLabel.Text = "Find what:";
            // 
            // searchTypeComboBox
            // 
            searchTypeComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            searchTypeComboBox.FormattingEnabled = true;
            searchTypeComboBox.Location = new System.Drawing.Point(80, 53);
            searchTypeComboBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            searchTypeComboBox.Name = "searchTypeComboBox";
            searchTypeComboBox.Size = new System.Drawing.Size(216, 25);
            searchTypeComboBox.TabIndex = 5;
            // 
            // SearchForm
            // 
            AcceptButton = findButton;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(406, 116);
            Controls.Add(searchTypeComboBox);
            Controls.Add(findLabel);
            Controls.Add(findTextBox);
            Controls.Add(cancelButton);
            Controls.Add(findButton);
            Font = new System.Drawing.Font("Segoe UI", 10F);
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

        private System.Windows.Forms.Button findButton;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.TextBox findTextBox;
        private System.Windows.Forms.Label findLabel;
        private System.Windows.Forms.ComboBox searchTypeComboBox;
    }
}
