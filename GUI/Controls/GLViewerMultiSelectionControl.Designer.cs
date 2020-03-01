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
            this.selectionNameLabel = new System.Windows.Forms.Label();
            this.checkedListBox = new System.Windows.Forms.CheckedListBox();
            this.SuspendLayout();
            // 
            // selectionNameLabel
            // 
            this.selectionNameLabel.AutoSize = true;
            this.selectionNameLabel.Location = new System.Drawing.Point(0, 2);
            this.selectionNameLabel.Name = "selectionNameLabel";
            this.selectionNameLabel.Size = new System.Drawing.Size(40, 13);
            this.selectionNameLabel.TabIndex = 0;
            this.selectionNameLabel.Text = "Select:";
            // 
            // checkedListBox
            // 
            this.checkedListBox.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.checkedListBox.CheckOnClick = true;
            this.checkedListBox.FormattingEnabled = true;
            this.checkedListBox.Location = new System.Drawing.Point(4, 19);
            this.checkedListBox.Name = "checkedListBox";
            this.checkedListBox.Size = new System.Drawing.Size(176, 79);
            this.checkedListBox.TabIndex = 1;
            // 
            // GLViewerMultiSelectionControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.checkedListBox);
            this.Controls.Add(this.selectionNameLabel);
            this.Name = "GLViewerMultiSelectionControl";
            this.Size = new System.Drawing.Size(183, 101);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label selectionNameLabel;
        private System.Windows.Forms.CheckedListBox checkedListBox;
    }
}
