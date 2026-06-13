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
            filterKeyLabel = new System.Windows.Forms.Label();
            filterKeyComboBox = new ThemedComboBox();
            filterValueComboBox = new ThemedComboBox();
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
            findButton.Location = new System.Drawing.Point(13, 193);
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
            cancelButton.Location = new System.Drawing.Point(325, 193);
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
            findTextBox.Size = new System.Drawing.Size(400, 25);
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
            searchTypeComboBox.HeaderColor = System.Drawing.Color.FromArgb(188, 188, 188);
            searchTypeComboBox.HighlightColor = System.Drawing.Color.FromArgb(99, 161, 255);
            searchTypeComboBox.Location = new System.Drawing.Point(13, 53);
            searchTypeComboBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            searchTypeComboBox.Name = "searchTypeComboBox";
            searchTypeComboBox.Size = new System.Drawing.Size(400, 26);
            searchTypeComboBox.TabIndex = 5;
            // 
            // filterKeyLabel
            // 
            filterKeyLabel.AutoSize = true;
            filterKeyLabel.Location = new System.Drawing.Point(13, 91);
            filterKeyLabel.Name = "filterKeyLabel";
            filterKeyLabel.Size = new System.Drawing.Size(103, 19);
            filterKeyLabel.TabIndex = 6;
            filterKeyLabel.Text = "Asset info filter:";
            // 
            // filterKeyComboBox
            // 
            filterKeyComboBox.BackColor = System.Drawing.Color.FromArgb(218, 218, 218);
            filterKeyComboBox.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            filterKeyComboBox.DropDownBackColor = System.Drawing.Color.FromArgb(218, 218, 218);
            filterKeyComboBox.DropDownForeColor = System.Drawing.Color.Black;
            filterKeyComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            filterKeyComboBox.ForeColor = System.Drawing.Color.Black;
            filterKeyComboBox.HeaderColor = System.Drawing.Color.FromArgb(188, 188, 188);
            filterKeyComboBox.HighlightColor = System.Drawing.Color.FromArgb(99, 161, 255);
            filterKeyComboBox.Location = new System.Drawing.Point(13, 118);
            filterKeyComboBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            filterKeyComboBox.Name = "filterKeyComboBox";
            filterKeyComboBox.Size = new System.Drawing.Size(400, 26);
            filterKeyComboBox.TabIndex = 7;
            // 
            // filterValueComboBox
            // 
            filterValueComboBox.BackColor = System.Drawing.Color.FromArgb(218, 218, 218);
            filterValueComboBox.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            filterValueComboBox.DropDownBackColor = System.Drawing.Color.FromArgb(218, 218, 218);
            filterValueComboBox.DropDownForeColor = System.Drawing.Color.Black;
            filterValueComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            filterValueComboBox.ForeColor = System.Drawing.Color.Black;
            filterValueComboBox.HeaderColor = System.Drawing.Color.FromArgb(188, 188, 188);
            filterValueComboBox.HighlightColor = System.Drawing.Color.FromArgb(99, 161, 255);
            filterValueComboBox.Location = new System.Drawing.Point(13, 150);
            filterValueComboBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            filterValueComboBox.Name = "filterValueComboBox";
            filterValueComboBox.Size = new System.Drawing.Size(400, 26);
            filterValueComboBox.TabIndex = 8;
            filterValueComboBox.Visible = false;
            // 
            // SearchForm
            // 
            AcceptButton = findButton;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(427, 238);
            Controls.Add(filterValueComboBox);
            Controls.Add(filterKeyComboBox);
            Controls.Add(filterKeyLabel);
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
        private System.Windows.Forms.Label filterKeyLabel;
        private ThemedComboBox filterKeyComboBox;
        private ThemedComboBox filterValueComboBox;
    }
}
