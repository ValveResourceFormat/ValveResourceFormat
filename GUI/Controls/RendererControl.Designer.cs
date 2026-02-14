namespace GUI.Controls;

partial class RendererControl
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
            namedGroups.Clear();
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
        glControlContainer = new System.Windows.Forms.Panel();
        controlsPanel = new System.Windows.Forms.Panel();
        bottomPanel = new System.Windows.Forms.Panel();
        moveSpeed = new System.Windows.Forms.Label();
        splitContainer = new System.Windows.Forms.SplitContainer();
        controlsPanel.SuspendLayout();
        bottomPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
        splitContainer.Panel1.SuspendLayout();
        splitContainer.Panel2.SuspendLayout();
        splitContainer.SuspendLayout();
        SuspendLayout();
        //
        // glControlContainer
        //
        glControlContainer.BackColor = System.Drawing.Color.Black;
        glControlContainer.Dock = System.Windows.Forms.DockStyle.Fill;
        glControlContainer.Location = new System.Drawing.Point(0, 0);
        glControlContainer.Margin = new System.Windows.Forms.Padding(4, 50, 4, 3);
        glControlContainer.Name = "glControlContainer";
        glControlContainer.Size = new System.Drawing.Size(618, 716);
        glControlContainer.TabIndex = 0;
        //
        // controlsPanel
        //
        controlsPanel.AutoScroll = true;
        controlsPanel.Controls.Add(bottomPanel);
        controlsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        controlsPanel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
        controlsPanel.Location = new System.Drawing.Point(0, 0);
        controlsPanel.Margin = new System.Windows.Forms.Padding(0);
        controlsPanel.Name = "controlsPanel";
        controlsPanel.Padding = new System.Windows.Forms.Padding(5, 5, 5, 0);
        controlsPanel.Size = new System.Drawing.Size(220, 716);
        controlsPanel.TabIndex = 4;
        //
        // bottomPanel
        //
        bottomPanel.Controls.Add(moveSpeed);
        bottomPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
        bottomPanel.Location = new System.Drawing.Point(5, 676);
        bottomPanel.Margin = new System.Windows.Forms.Padding(0);
        bottomPanel.Name = "bottomPanel";
        bottomPanel.Padding = new System.Windows.Forms.Padding(5);
        bottomPanel.Size = new System.Drawing.Size(210, 40);
        bottomPanel.TabIndex = 7;
        //
        // moveSpeed
        //
        moveSpeed.Dock = System.Windows.Forms.DockStyle.Bottom;
        moveSpeed.Location = new System.Drawing.Point(5, 20);
        moveSpeed.Name = "moveSpeed";
        moveSpeed.Size = new System.Drawing.Size(200, 15);
        moveSpeed.TabIndex = 5;
        moveSpeed.Text = "Move speed: 1.0x (scroll to change)";
        //
        // splitContainer
        //
        splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
        splitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
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
        splitContainer.Size = new System.Drawing.Size(842, 716);
        splitContainer.SplitterDistance = 220;
        splitContainer.TabIndex = 6;
        //
        // RendererControl
        //
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        Controls.Add(splitContainer);
        Margin = new System.Windows.Forms.Padding(0);
        Name = "RendererControl";
        Size = new System.Drawing.Size(842, 716);
        controlsPanel.ResumeLayout(false);
        bottomPanel.ResumeLayout(false);
        splitContainer.Panel1.ResumeLayout(false);
        splitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
        splitContainer.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private System.Windows.Forms.Panel glControlContainer;
    private System.Windows.Forms.Panel controlsPanel;
    private System.Windows.Forms.Panel bottomPanel;
    private System.Windows.Forms.Label moveSpeed;
    private System.Windows.Forms.SplitContainer splitContainer;
}
