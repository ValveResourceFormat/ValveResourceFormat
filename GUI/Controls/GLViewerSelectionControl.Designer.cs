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
            this.selectionNameLabel = new System.Windows.Forms.Label();
            this.comboBox = new System.Windows.Forms.ComboBox();
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
            // comboBox
            // 
            this.comboBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.comboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox.FormattingEnabled = true;
            this.comboBox.Location = new System.Drawing.Point(3, 18);
            this.comboBox.Name = "comboBox";
            this.comboBox.Size = new System.Drawing.Size(174, 21);
            this.comboBox.TabIndex = 1;
            // 
            // GLViewerSelectionControl
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Inherit;
            this.Controls.Add(this.comboBox);
            this.Controls.Add(this.selectionNameLabel);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.MinimumSize = new System.Drawing.Size(0, 41);
            this.Name = "GLViewerSelectionControl";
            this.Size = new System.Drawing.Size(180, 41);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label selectionNameLabel;
        private System.Windows.Forms.ComboBox comboBox;
    }
}
