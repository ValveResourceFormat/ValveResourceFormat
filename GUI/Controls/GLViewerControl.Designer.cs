using System.Windows.Forms;
using OpenTK.Graphics.OpenGL;

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
            if (disposing)
            {
                components?.Dispose();

                FullScreenForm?.Dispose();

                //fboColor?.Dispose();
                //fboDepth?.Dispose();

                GL.DeleteTexture(fboColor.Handle);
                GL.DeleteTexture(fboDepth.Handle);
                GL.DeleteFramebuffer(fbo);

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
            glControlContainer = new Panel();
            fpsLabel = new Label();
            controlsPanel = new Panel();
            moveSpeed = new Label();
            label1 = new Label();
            controlsPanel.SuspendLayout();
            SuspendLayout();
            // 
            // glControlContainer
            // 
            glControlContainer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            glControlContainer.BackColor = System.Drawing.Color.Black;
            glControlContainer.Location = new System.Drawing.Point(220, 0);
            glControlContainer.Margin = new Padding(4, 3, 4, 3);
            glControlContainer.Name = "glControlContainer";
            glControlContainer.Size = new System.Drawing.Size(810, 412);
            glControlContainer.TabIndex = 0;
            // 
            // fpsLabel
            // 
            fpsLabel.Location = new System.Drawing.Point(32, 8);
            fpsLabel.Margin = new Padding(0, 0, 3, 0);
            fpsLabel.Name = "fpsLabel";
            fpsLabel.Size = new System.Drawing.Size(100, 15);
            fpsLabel.TabIndex = 4;
            fpsLabel.Text = "0";
            // 
            // controlsPanel
            // 
            controlsPanel.Controls.Add(moveSpeed);
            controlsPanel.Controls.Add(label1);
            controlsPanel.Controls.Add(fpsLabel);
            controlsPanel.Dock = DockStyle.Left;
            controlsPanel.Location = new System.Drawing.Point(0, 0);
            controlsPanel.Margin = new Padding(0);
            controlsPanel.Name = "controlsPanel";
            controlsPanel.Size = new System.Drawing.Size(220, 412);
            controlsPanel.TabIndex = 4;
            // 
            // moveSpeed
            // 
            moveSpeed.AutoSize = true;
            moveSpeed.Dock = DockStyle.Bottom;
            moveSpeed.Location = new System.Drawing.Point(0, 397);
            moveSpeed.Name = "moveSpeed";
            moveSpeed.Size = new System.Drawing.Size(193, 15);
            moveSpeed.TabIndex = 5;
            moveSpeed.Text = "Move speed: 1.0x (scroll to change)";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(3, 8);
            label1.Margin = new Padding(3, 0, 0, 0);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(29, 15);
            label1.TabIndex = 3;
            label1.Text = "FPS:";
            // 
            // GLViewerControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(controlsPanel);
            Controls.Add(glControlContainer);
            Margin = new Padding(0);
            Name = "GLViewerControl";
            Size = new System.Drawing.Size(1030, 412);
            controlsPanel.ResumeLayout(false);
            controlsPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel glControlContainer;
        private Label fpsLabel;
        private Panel controlsPanel;
        private Label label1;
        private Label moveSpeed;
    }
}
