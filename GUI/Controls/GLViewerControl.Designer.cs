using System.Windows.Forms;
using OpenTK;

namespace GUI.Controls
{
    partial class GLViewerControl
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
#if DEBUG
            glControl = new OpenTK.GLControl(new OpenTK.Graphics.GraphicsMode(32, 24, 0, 8), 3, 3, OpenTK.Graphics.GraphicsContextFlags.Debug);
#else
            glControl = new OpenTK.GLControl(new OpenTK.Graphics.GraphicsMode(32, 24, 0, 8), 3, 3, OpenTK.Graphics.GraphicsContextFlags.Default);
#endif

            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.viewerControls = new System.Windows.Forms.Panel();
            this.labelFpsNumber = new System.Windows.Forms.Label();
            this.labelFps = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.viewerControls.SuspendLayout();
            this.SuspendLayout();
            // 
            // glControl
            // 
            this.glControl.AutoSize = true;
            this.glControl.BackColor = System.Drawing.Color.Black;
            this.glControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.glControl.Location = new System.Drawing.Point(0, 0);
            this.glControl.Name = "glControl";
            this.glControl.Size = new System.Drawing.Size(1060, 628);
            this.glControl.TabIndex = 0;
            this.glControl.VSync = true;
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 0);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.viewerControls);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.glControl);
            this.splitContainer1.Size = new System.Drawing.Size(1216, 628);
            this.splitContainer1.SplitterDistance = 152;
            this.splitContainer1.TabIndex = 0;
            // 
            // viewerControls
            // 
            this.viewerControls.Controls.Add(this.labelFpsNumber);
            this.viewerControls.Controls.Add(this.labelFps);
            this.viewerControls.Dock = System.Windows.Forms.DockStyle.Fill;
            this.viewerControls.Location = new System.Drawing.Point(0, 0);
            this.viewerControls.Name = "viewerControls";
            this.viewerControls.Size = new System.Drawing.Size(152, 628);
            this.viewerControls.TabIndex = 0;
            // 
            // labelFpsNumber
            // 
            this.labelFpsNumber.AutoSize = true;
            this.labelFpsNumber.Dock = System.Windows.Forms.DockStyle.Left;
            this.labelFpsNumber.Location = new System.Drawing.Point(30, 0);
            this.labelFpsNumber.Name = "labelFpsNumber";
            this.labelFpsNumber.Size = new System.Drawing.Size(13, 13);
            this.labelFpsNumber.TabIndex = 1;
            this.labelFpsNumber.Text = "0";
            // 
            // labelFps
            // 
            this.labelFps.AutoSize = true;
            this.labelFps.Dock = System.Windows.Forms.DockStyle.Left;
            this.labelFps.Location = new System.Drawing.Point(0, 0);
            this.labelFps.Name = "labelFps";
            this.labelFps.Size = new System.Drawing.Size(30, 13);
            this.labelFps.TabIndex = 0;
            this.labelFps.Text = "FPS:";
            // 
            // GLViewerControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitContainer1);
            this.Name = "GLViewerControl";
            this.Size = new System.Drawing.Size(1216, 628);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            this.splitContainer1.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.viewerControls.ResumeLayout(false);
            this.viewerControls.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion



        private System.Windows.Forms.SplitContainer splitContainer1;
        public System.Windows.Forms.Panel viewerControls;
        public OpenTK.GLControl glControl;
        private Label labelFps;
        private Label labelFpsNumber;
    }
}
