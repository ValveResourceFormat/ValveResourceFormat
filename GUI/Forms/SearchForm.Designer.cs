using GUI.Controls;

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
            findButton = new ThemedButton();
            cancelButton = new ThemedButton();
            findTextBox = new System.Windows.Forms.TextBox();
            searchTypeComboBox = new ThemedComboBox();
            SuspendLayout();
            // 
            // findButton
            // 
            findButton.BackColor = System.Drawing.Color.FromArgb(188, 188, 188);
            findButton.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            findButton.CornerRadius = 5;
            findButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            findButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            findButton.ForeColor = System.Drawing.Color.Black;
            findButton.HoveredBackColor = System.Drawing.Color.FromArgb(140, 191, 255);
            findButton.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            findButton.Location = new System.Drawing.Point(304, 16);
            findButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            findButton.Name = "findButton";
            findButton.Size = new System.Drawing.Size(88, 31);
            findButton.Style = true;
            findButton.TabIndex = 0;
            findButton.Text = "Find";
            findButton.UseVisualStyleBackColor = false;
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
            cancelButton.Location = new System.Drawing.Point(304, 53);
            cancelButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(88, 31);
            cancelButton.Style = true;
            cancelButton.TabIndex = 1;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = false;
            // 
            // findTextBox
            // 
            findTextBox.Location = new System.Drawing.Point(13, 18);
            findTextBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            findTextBox.Name = "findTextBox";
            findTextBox.Size = new System.Drawing.Size(283, 25);
            findTextBox.TabIndex = 2;
            // 
            // searchTypeComboBox
            // 
            searchTypeComboBox.BackColor = System.Drawing.Color.FromArgb(218, 218, 218);
            searchTypeComboBox.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            searchTypeComboBox.DropDownBackColor = System.Drawing.Color.FromArgb(218, 218, 218);
            searchTypeComboBox.DropDownForeColor = System.Drawing.Color.Black;
            searchTypeComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            searchTypeComboBox.ForeColor = System.Drawing.Color.Black;
            searchTypeComboBox.FormattingEnabled = true;
            searchTypeComboBox.HeaderColor = System.Drawing.Color.FromArgb(188, 188, 188);
            searchTypeComboBox.HighlightColor = System.Drawing.Color.FromArgb(99, 161, 255);
            searchTypeComboBox.Location = new System.Drawing.Point(13, 53);
            searchTypeComboBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            searchTypeComboBox.Name = "searchTypeComboBox";
            searchTypeComboBox.Size = new System.Drawing.Size(283, 26);
            searchTypeComboBox.TabIndex = 5;
            // 
            // SearchForm
            // 
            AcceptButton = findButton;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(406, 102);
            Controls.Add(searchTypeComboBox);
            Controls.Add(findTextBox);
            Controls.Add(cancelButton);
            Controls.Add(findButton);
            Font = new System.Drawing.Font("Segoe UI", 10F);
            ForeColor = System.Drawing.Color.Black;
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
        private System.Windows.Forms.TextBox findTextBox;
        private ThemedButton findButton;
        private ThemedButton cancelButton;
        private ThemedComboBox searchTypeComboBox;
    }
}
