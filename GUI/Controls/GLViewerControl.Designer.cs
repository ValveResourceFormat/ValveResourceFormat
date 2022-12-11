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
            this.label1 = new System.Windows.Forms.Label();
            this.controlsPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // glControlContainer
            // 
            this.glControlContainer.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.glControlContainer.BackColor = System.Drawing.Color.Black;
            this.glControlContainer.Location = new System.Drawing.Point(220, 0);
            this.glControlContainer.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.glControlContainer.Name = "glControlContainer";
            this.glControlContainer.Size = new System.Drawing.Size(810, 412);
            this.glControlContainer.TabIndex = 0;
            // 
            // fpsLabel
            // 
            this.fpsLabel.Location = new System.Drawing.Point(32, 8);
            this.fpsLabel.Margin = new System.Windows.Forms.Padding(0, 0, 3, 0);
            this.fpsLabel.Name = "fpsLabel";
            this.fpsLabel.Size = new System.Drawing.Size(100, 15);
            this.fpsLabel.TabIndex = 4;
            this.fpsLabel.Text = "0";
            // 
            // controlsPanel
            // 
            this.controlsPanel.Controls.Add(this.label1);
            this.controlsPanel.Controls.Add(this.fpsLabel);
            this.controlsPanel.Dock = System.Windows.Forms.DockStyle.Left;
            this.controlsPanel.Location = new System.Drawing.Point(0, 0);
            this.controlsPanel.Margin = new System.Windows.Forms.Padding(0);
            this.controlsPanel.Name = "controlsPanel";
            this.controlsPanel.Size = new System.Drawing.Size(220, 412);
            this.controlsPanel.TabIndex = 4;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(3, 8);
            this.label1.Margin = new System.Windows.Forms.Padding(3, 0, 0, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(29, 15);
            this.label1.TabIndex = 3;
            this.label1.Text = "FPS:";
            // 
            // GLViewerControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.controlsPanel);
            this.Controls.Add(this.glControlContainer);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.Name = "GLViewerControl";
            this.Size = new System.Drawing.Size(1030, 412);
            this.controlsPanel.ResumeLayout(false);
            this.controlsPanel.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private Panel glControlContainer;
        private Label fpsLabel;
        private Panel controlsPanel;
        private Label label1;
    }
}
