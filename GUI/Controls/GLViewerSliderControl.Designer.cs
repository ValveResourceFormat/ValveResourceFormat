namespace GUI.Controls
{
    partial class GLViewerSliderControl
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
            slider1 = new Slider();
            SuspendLayout();
            // 
            // slider1
            // 
            slider1.BackColor = System.Drawing.SystemColors.Control;
            slider1.Dock = System.Windows.Forms.DockStyle.Fill;
            slider1.ForeColor = System.Drawing.Color.Black;
            slider1.Location = new System.Drawing.Point(0, 8);
            slider1.Name = "slider1";
            slider1.Size = new System.Drawing.Size(220, 29);
            slider1.SliderColor = System.Drawing.Color.FromArgb(99, 161, 255);
            slider1.TabIndex = 0;
            slider1.Value = 0F;
            // 
            // GLViewerSliderControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(slider1);
            Name = "GLViewerSliderControl";
            Padding = new System.Windows.Forms.Padding(0, 8, 0, 8);
            Size = new System.Drawing.Size(220, 45);
            ResumeLayout(false);

        }

        #endregion

        private Slider slider1;
    }
}
