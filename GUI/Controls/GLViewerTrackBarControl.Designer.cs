namespace GUI.Controls
{
    partial class GLViewerTrackBarControl
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
            this.trackBarLabel = new System.Windows.Forms.Label();
            this.trackBar = new System.Windows.Forms.TrackBar();
            this.SuspendLayout();
            // 
            // selectionNameLabel
            // 
            this.trackBarLabel.AutoSize = true;
            this.trackBarLabel.Location = new System.Drawing.Point(0, 2);
            this.trackBarLabel.Name = "trackBarLabel";
            this.trackBarLabel.Size = new System.Drawing.Size(40, 13);
            this.trackBarLabel.TabIndex = 0;
            this.trackBarLabel.Text = "Select:";
            // 
            // trackBar
            // 
            this.trackBar.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.trackBar.Location = new System.Drawing.Point(4, 4);
            this.trackBar.Name = "trackBar";
            this.trackBar.Size = new System.Drawing.Size(172, 21);
            this.trackBar.MaximumSize = new System.Drawing.Size(172, 21);
            this.trackBar.TabIndex = 0;
            this.trackBar.Minimum = 0;
            this.trackBar.Maximum = 1;
            // 
            // GLViewerCheckboxControl
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Inherit;
            this.Controls.Add(this.trackBar);
            this.Controls.Add(this.trackBarLabel);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.MinimumSize = new System.Drawing.Size(0, 41);
            this.Name = "GLViewerTrackBarControl";
            this.Size = new System.Drawing.Size(180, 41);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label trackBarLabel;
        private System.Windows.Forms.TrackBar trackBar;
    }
}
