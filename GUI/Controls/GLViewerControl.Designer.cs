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
            this.glControlContainer = new System.Windows.Forms.Panel();
            this.fpsLabel = new System.Windows.Forms.Label();
            this.controlsPanel = new System.Windows.Forms.Panel();
            this.controlsPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // glControlContainer
            // 
            this.glControlContainer.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.glControlContainer.BackColor = System.Drawing.Color.Black;
            this.glControlContainer.Location = new System.Drawing.Point(192, 0);
            this.glControlContainer.Name = "glControlContainer";
            this.glControlContainer.Size = new System.Drawing.Size(691, 357);
            this.glControlContainer.TabIndex = 0;
            // 
            // fpsLabel
            // 
            this.fpsLabel.AutoSize = true;
            this.fpsLabel.Location = new System.Drawing.Point(1, 7);
            this.fpsLabel.Name = "fpsLabel";
            this.fpsLabel.Size = new System.Drawing.Size(39, 13);
            this.fpsLabel.TabIndex = 3;
            this.fpsLabel.Text = "FPS: 0";
            // 
            // controlsPanel
            // 
            this.controlsPanel.Controls.Add(this.fpsLabel);
            this.controlsPanel.Dock = System.Windows.Forms.DockStyle.Left;
            this.controlsPanel.Location = new System.Drawing.Point(0, 0);
            this.controlsPanel.Name = "controlsPanel";
            this.controlsPanel.Size = new System.Drawing.Size(192, 357);
            this.controlsPanel.TabIndex = 4;
            // 
            // GLViewerControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.controlsPanel);
            this.Controls.Add(this.glControlContainer);
            this.Name = "GLViewerControl";
            this.Size = new System.Drawing.Size(883, 357);
            this.controlsPanel.ResumeLayout(false);
            this.controlsPanel.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private Panel glControlContainer;
        private Label fpsLabel;
        private Panel controlsPanel;
    }
}
