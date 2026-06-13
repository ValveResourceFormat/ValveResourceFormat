namespace GUI.Controls
{
    partial class GLViewerCheckboxControl
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
            checkBox = new System.Windows.Forms.CheckBox();
            SuspendLayout();
            // 
            // checkBox
            // 
            checkBox.AutoSize = true;
            checkBox.Dock = System.Windows.Forms.DockStyle.Top;
            checkBox.Location = new System.Drawing.Point(0, 0);
            checkBox.Name = "checkBox";
            checkBox.Size = new System.Drawing.Size(220, 14);
            checkBox.TabIndex = 0;
            checkBox.UseVisualStyleBackColor = true;
            // 
            // GLViewerCheckboxControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(checkBox);
            MinimumSize = new System.Drawing.Size(0, 23);
            Name = "GLViewerCheckboxControl";
            Size = new System.Drawing.Size(220, 25);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.CheckBox checkBox;
    }
}
