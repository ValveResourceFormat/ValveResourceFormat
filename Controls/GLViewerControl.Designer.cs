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
            splitContainer = new SplitContainer();
            controlsPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            SuspendLayout();
            // 
            // glControlContainer
            // 
            glControlContainer.BackColor = System.Drawing.Color.Black;
            glControlContainer.Dock = DockStyle.Fill;
            glControlContainer.Location = new System.Drawing.Point(0, 0);
            glControlContainer.Margin = new Padding(4, 50, 4, 3);
            glControlContainer.Name = "glControlContainer";
            glControlContainer.Size = new System.Drawing.Size(1273, 967);
            glControlContainer.TabIndex = 0;
            // 
            // controlsPanel
            // 
            controlsPanel.AutoScroll = true;
            controlsPanel.Controls.Add(moveSpeed);
            controlsPanel.Dock = DockStyle.Fill;
            controlsPanel.Location = new System.Drawing.Point(0, 0);
            controlsPanel.Margin = new Padding(0);
            controlsPanel.Name = "controlsPanel";
            controlsPanel.Padding = new Padding(5, 5, 5, 0);
            controlsPanel.Size = new System.Drawing.Size(220, 967);
            controlsPanel.TabIndex = 4;
            // 
            // moveSpeed
            // 
            moveSpeed.AutoSize = true;
            moveSpeed.Dock = DockStyle.Bottom;
            moveSpeed.Location = new System.Drawing.Point(5, 952);
            moveSpeed.Name = "moveSpeed";
            moveSpeed.Size = new System.Drawing.Size(193, 15);
            moveSpeed.TabIndex = 5;
            moveSpeed.Text = "Move speed: 1.0x (scroll to change)";
            // 
            // splitContainer
            // 
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.FixedPanel = FixedPanel.Panel1;
            splitContainer.Location = new System.Drawing.Point(0, 0);
            splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            splitContainer.Panel1.Controls.Add(controlsPanel);
            splitContainer.Panel1MinSize = 0;
            // 
            // splitContainer.Panel2
            // 
            splitContainer.Panel2.Controls.Add(glControlContainer);
            splitContainer.Size = new System.Drawing.Size(1497, 967);
            splitContainer.SplitterDistance = 220;
            splitContainer.TabIndex = 5;
            // 
            // GLViewerControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(splitContainer);
            Margin = new Padding(0);
            Name = "GLViewerControl";
            Size = new System.Drawing.Size(1497, 967);
            controlsPanel.ResumeLayout(false);
            controlsPanel.PerformLayout();
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private Panel glControlContainer;
        private Panel controlsPanel;
        private Label moveSpeed;
        private SplitContainer splitContainer;
    }
}
