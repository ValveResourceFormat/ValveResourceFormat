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
            matchCaseCheckBox = new System.Windows.Forms.CheckBox();
            wholeWordCheckBox = new System.Windows.Forms.CheckBox();
            regexCheckBox = new System.Windows.Forms.CheckBox();
            onlyInFolderCheckBox = new System.Windows.Forms.CheckBox();
            extensionLabel = new System.Windows.Forms.Label();
            extensionComboBox = new ThemedComboBox();
            hintLabel = new System.Windows.Forms.Label();
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
            findButton.Location = new System.Drawing.Point(13, 336);
            findButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            findButton.Name = "findButton";
            findButton.Size = new System.Drawing.Size(88, 31);
            findButton.Style = true;
            findButton.TabIndex = 12;
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
            cancelButton.Location = new System.Drawing.Point(325, 336);
            cancelButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(88, 31);
            cancelButton.Style = true;
            cancelButton.TabIndex = 13;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = false;
            // 
            // findTextBox
            // 
            findTextBox.Location = new System.Drawing.Point(13, 18);
            findTextBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            findTextBox.Name = "findTextBox";
            findTextBox.Size = new System.Drawing.Size(400, 25);
            findTextBox.TabIndex = 0;
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
            searchTypeComboBox.TabIndex = 1;
            // 
            // filterKeyLabel
            // 
            filterKeyLabel.AutoSize = true;
            filterKeyLabel.Location = new System.Drawing.Point(13, 210);
            filterKeyLabel.Name = "filterKeyLabel";
            filterKeyLabel.Size = new System.Drawing.Size(103, 19);
            filterKeyLabel.TabIndex = 8;
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
            filterKeyComboBox.Location = new System.Drawing.Point(13, 234);
            filterKeyComboBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            filterKeyComboBox.Name = "filterKeyComboBox";
            filterKeyComboBox.Size = new System.Drawing.Size(400, 26);
            filterKeyComboBox.TabIndex = 9;
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
            filterValueComboBox.Location = new System.Drawing.Point(13, 266);
            filterValueComboBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            filterValueComboBox.Name = "filterValueComboBox";
            filterValueComboBox.Size = new System.Drawing.Size(400, 26);
            filterValueComboBox.TabIndex = 10;
            filterValueComboBox.Visible = false;
            // 
            // matchCaseCheckBox
            // 
            matchCaseCheckBox.AutoSize = true;
            matchCaseCheckBox.FlatStyle = System.Windows.Forms.FlatStyle.System;
            matchCaseCheckBox.Location = new System.Drawing.Point(13, 86);
            matchCaseCheckBox.Name = "matchCaseCheckBox";
            matchCaseCheckBox.TabIndex = 2;
            matchCaseCheckBox.Text = "Match case";
            matchCaseCheckBox.UseMnemonic = false;
            matchCaseCheckBox.UseVisualStyleBackColor = true;
            // 
            // wholeWordCheckBox
            // 
            wholeWordCheckBox.AutoSize = true;
            wholeWordCheckBox.FlatStyle = System.Windows.Forms.FlatStyle.System;
            wholeWordCheckBox.Location = new System.Drawing.Point(130, 86);
            wholeWordCheckBox.Name = "wholeWordCheckBox";
            wholeWordCheckBox.TabIndex = 3;
            wholeWordCheckBox.Text = "Whole word";
            wholeWordCheckBox.UseMnemonic = false;
            wholeWordCheckBox.UseVisualStyleBackColor = true;
            // 
            // regexCheckBox
            // 
            regexCheckBox.AutoSize = true;
            regexCheckBox.FlatStyle = System.Windows.Forms.FlatStyle.System;
            regexCheckBox.Location = new System.Drawing.Point(247, 86);
            regexCheckBox.Name = "regexCheckBox";
            regexCheckBox.TabIndex = 4;
            regexCheckBox.Text = "Regex";
            regexCheckBox.UseMnemonic = false;
            regexCheckBox.UseVisualStyleBackColor = true;
            // 
            // onlyInFolderCheckBox
            // 
            onlyInFolderCheckBox.AutoEllipsis = true;
            onlyInFolderCheckBox.Location = new System.Drawing.Point(13, 116);
            onlyInFolderCheckBox.Name = "onlyInFolderCheckBox";
            onlyInFolderCheckBox.Size = new System.Drawing.Size(400, 24);
            onlyInFolderCheckBox.TabIndex = 5;
            onlyInFolderCheckBox.Text = "Only in the selected folder";
            onlyInFolderCheckBox.UseMnemonic = false;
            onlyInFolderCheckBox.UseVisualStyleBackColor = true;
            // 
            // extensionLabel
            // 
            extensionLabel.AutoSize = true;
            extensionLabel.Location = new System.Drawing.Point(13, 148);
            extensionLabel.Name = "extensionLabel";
            extensionLabel.Size = new System.Drawing.Size(70, 19);
            extensionLabel.TabIndex = 6;
            extensionLabel.Text = "Extension:";
            // 
            // extensionComboBox
            // 
            extensionComboBox.BackColor = System.Drawing.Color.FromArgb(218, 218, 218);
            extensionComboBox.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            extensionComboBox.DropDownBackColor = System.Drawing.Color.FromArgb(218, 218, 218);
            extensionComboBox.DropDownForeColor = System.Drawing.Color.Black;
            extensionComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            extensionComboBox.ForeColor = System.Drawing.Color.Black;
            extensionComboBox.HeaderColor = System.Drawing.Color.FromArgb(188, 188, 188);
            extensionComboBox.HighlightColor = System.Drawing.Color.FromArgb(99, 161, 255);
            extensionComboBox.Location = new System.Drawing.Point(13, 172);
            extensionComboBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            extensionComboBox.Name = "extensionComboBox";
            extensionComboBox.Size = new System.Drawing.Size(400, 26);
            extensionComboBox.TabIndex = 7;
            // 
            // hintLabel
            // 
            hintLabel.AutoEllipsis = true;
            hintLabel.Location = new System.Drawing.Point(13, 304);
            hintLabel.Name = "hintLabel";
            hintLabel.Size = new System.Drawing.Size(400, 22);
            hintLabel.TabIndex = 11;
            hintLabel.UseMnemonic = false;
            // 
            // SearchForm
            // 
            AcceptButton = findButton;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(427, 381);
            Controls.Add(hintLabel);
            Controls.Add(extensionComboBox);
            Controls.Add(extensionLabel);
            Controls.Add(onlyInFolderCheckBox);
            Controls.Add(regexCheckBox);
            Controls.Add(wholeWordCheckBox);
            Controls.Add(matchCaseCheckBox);
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
        private System.Windows.Forms.CheckBox matchCaseCheckBox;
        private System.Windows.Forms.CheckBox wholeWordCheckBox;
        private System.Windows.Forms.CheckBox regexCheckBox;
        private System.Windows.Forms.CheckBox onlyInFolderCheckBox;
        private System.Windows.Forms.Label extensionLabel;
        private ThemedComboBox extensionComboBox;
        private System.Windows.Forms.Label hintLabel;
    }
}
