using System.Windows.Forms;

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
                DisposeFramebuffer();
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
            controlsPanel = new Panel();
            moveSpeed = new Label();
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
            // controlsPanel
            // 
            controlsPanel.Controls.Add(moveSpeed);
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
        private Panel controlsPanel;
        private Label moveSpeed;
    }
}
