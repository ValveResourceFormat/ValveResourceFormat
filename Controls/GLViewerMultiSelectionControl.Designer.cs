namespace GUI.Controls
{
    partial class GLViewerMultiSelectionControl
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
            selectionNameLabel = new System.Windows.Forms.Label();
            checkedListBox = new System.Windows.Forms.CheckedListBox();
            SuspendLayout();
            // 
            // selectionNameLabel
            // 
            selectionNameLabel.AutoSize = true;
            selectionNameLabel.Dock = System.Windows.Forms.DockStyle.Top;
            selectionNameLabel.Location = new System.Drawing.Point(0, 0);
            selectionNameLabel.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            selectionNameLabel.Name = "selectionNameLabel";
            selectionNameLabel.Size = new System.Drawing.Size(41, 15);
            selectionNameLabel.TabIndex = 0;
            selectionNameLabel.Text = "Select:";
            // 
            // checkedListBox
            // 
            checkedListBox.CheckOnClick = true;
            checkedListBox.Dock = System.Windows.Forms.DockStyle.Top;
            checkedListBox.FormattingEnabled = true;
            checkedListBox.Location = new System.Drawing.Point(0, 15);
            checkedListBox.Margin = new System.Windows.Forms.Padding(0);
            checkedListBox.Name = "checkedListBox";
            checkedListBox.Size = new System.Drawing.Size(220, 130);
            checkedListBox.TabIndex = 1;
            // 
            // GLViewerMultiSelectionControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(checkedListBox);
            Controls.Add(selectionNameLabel);
            Margin = new System.Windows.Forms.Padding(2);
            Name = "GLViewerMultiSelectionControl";
            Size = new System.Drawing.Size(220, 154);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label selectionNameLabel;
        private System.Windows.Forms.CheckedListBox checkedListBox;
    }
}
