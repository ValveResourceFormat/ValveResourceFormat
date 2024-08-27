using System.Windows.Forms;

namespace GUI.Controls
{
    partial class TextControl
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
                TextBox?.Dispose();
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
            textContainer = new Panel();
            controlsPanel = new Panel();
            SuspendLayout();
            // 
            // textContainer
            // 
            textContainer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            textContainer.BackColor = System.Drawing.Color.Transparent;
            textContainer.Location = new System.Drawing.Point(320, 0);
            textContainer.Margin = new Padding(4, 3, 4, 3);
            textContainer.Name = "textContainer";
            textContainer.Size = new System.Drawing.Size(710, 412);
            textContainer.TabIndex = 0;
            // 
            // controlsPanel
            // 
            controlsPanel.Dock = DockStyle.Left;
            controlsPanel.Location = new System.Drawing.Point(0, 0);
            controlsPanel.Margin = new Padding(0);
            controlsPanel.Name = "controlsPanel";
            controlsPanel.Size = new System.Drawing.Size(320, 412);
            controlsPanel.TabIndex = 4;
            // 
            // TextControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(controlsPanel);
            Controls.Add(textContainer);
            Margin = new Padding(0);
            Name = "TextControl";
            Size = new System.Drawing.Size(1030, 412);
            controlsPanel.ResumeLayout(false);
            controlsPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel textContainer;
        private Panel controlsPanel;
    }
}
