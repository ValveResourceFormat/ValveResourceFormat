using DarkModeForms;
using System.Windows.Forms;

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
            findButton = new Button();
            cancelButton = new Button();
            findTextBox = new Controls.BetterTextBox();
            findLabel = new Label();
            searchTypeComboBox = new ComboBox();
            SuspendLayout();
            // 
            // findButton
            // 
            findButton.DialogResult = DialogResult.OK;
            findButton.Location = new System.Drawing.Point(304, 14);
            findButton.Margin = new Padding(4, 3, 4, 3);
            findButton.Name = "findButton";
            findButton.Size = new System.Drawing.Size(88, 27);
            findButton.TabIndex = 0;
            findButton.Text = "Find";
            findButton.UseVisualStyleBackColor = true;
            // 
            // cancelButton
            // 
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Location = new System.Drawing.Point(304, 47);
            cancelButton.Margin = new Padding(4, 3, 4, 3);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(88, 27);
            cancelButton.TabIndex = 1;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            // 
            // findTextBox
            // 
            findTextBox.BorderStyle = BorderStyle.None;
            findTextBox.Location = new System.Drawing.Point(80, 16);
            findTextBox.Margin = new Padding(4, 3, 4, 3);
            findTextBox.Name = "findTextBox";
            findTextBox.Size = new System.Drawing.Size(216, 16);
            findTextBox.TabIndex = 2;
            // 
            // findLabel
            // 
            findLabel.AutoSize = true;
            findLabel.Location = new System.Drawing.Point(14, 20);
            findLabel.Margin = new Padding(4, 0, 4, 0);
            findLabel.Name = "findLabel";
            findLabel.Size = new System.Drawing.Size(62, 15);
            findLabel.TabIndex = 3;
            findLabel.Text = "Find what:";
            // 
            // searchTypeComboBox
            // 
            searchTypeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            searchTypeComboBox.FormattingEnabled = true;
            searchTypeComboBox.Location = new System.Drawing.Point(80, 47);
            searchTypeComboBox.Margin = new Padding(4, 3, 4, 3);
            searchTypeComboBox.Name = "searchTypeComboBox";
            searchTypeComboBox.Size = new System.Drawing.Size(216, 23);
            searchTypeComboBox.TabIndex = 5;
            // 
            // SearchForm
            // 
            AcceptButton = findButton;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(406, 102);
            Controls.Add(searchTypeComboBox);
            Controls.Add(findLabel);
            Controls.Add(findTextBox);
            Controls.Add(cancelButton);
            Controls.Add(findButton);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Margin = new Padding(4, 3, 4, 3);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "SearchForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            SizeGripStyle = SizeGripStyle.Hide;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Find";
            Load += SearchForm_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Button findButton;
        private System.Windows.Forms.Button cancelButton;
        private Controls.BetterTextBox findTextBox;
        private System.Windows.Forms.Label findLabel;
        private ComboBox searchTypeComboBox;
    }
}
