using System.Windows.Forms;

namespace GUI.Controls
{
    partial class GLViewerSelectionControl
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            selectionNameLabel = new Label();
            comboBox = new ThemedComboBox();
            SuspendLayout();
            // 
            // selectionNameLabel
            // 
            selectionNameLabel.AutoSize = true;
            selectionNameLabel.Dock = DockStyle.Top;
            selectionNameLabel.Location = new System.Drawing.Point(0, 0);
            selectionNameLabel.Name = "selectionNameLabel";
            selectionNameLabel.Size = new System.Drawing.Size(41, 15);
            selectionNameLabel.TabIndex = 0;
            selectionNameLabel.Text = "Select:";
            // 
            // comboBox
            // 
            comboBox.Dock = DockStyle.Top;
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.FormattingEnabled = true;
            comboBox.Location = new System.Drawing.Point(0, 15);
            comboBox.Margin = new Padding(0);
            comboBox.Name = "comboBox";
            comboBox.Size = new System.Drawing.Size(220, 23);
            comboBox.TabIndex = 1;
            // 
            // GLViewerSelectionControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(comboBox);
            Controls.Add(selectionNameLabel);
            MinimumSize = new System.Drawing.Size(0, 41);
            Name = "GLViewerSelectionControl";
            Size = new System.Drawing.Size(220, 45);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label selectionNameLabel;
        private ThemedComboBox comboBox;
        private TableLayoutPanel layoutPanel;
    }
}
