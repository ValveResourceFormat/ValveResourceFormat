using System.Windows.Forms;
using GUI.Properties;

namespace GUI.Controls;

partial class MainFormBottomPanel
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
        menuStrip1 = new ThemedMenuStrip();
        versionLabel = new ToolStripMenuItem();
        newVersionAvailableToolStripMenuItem = new ThemedToolStripMenuItem();
        menuStrip1.SuspendLayout();
        SuspendLayout();
        // 
        // menuStrip1
        // 
        menuStrip1.Dock = DockStyle.Right;
        menuStrip1.Items.AddRange(new ToolStripItem[] { versionLabel, newVersionAvailableToolStripMenuItem });
        menuStrip1.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
        menuStrip1.Location = new System.Drawing.Point(350, 0);
        menuStrip1.Name = "menuStrip1";
        menuStrip1.Size = new System.Drawing.Size(309, 30);
        menuStrip1.TabIndex = 1;
        menuStrip1.Text = "menuStrip1";
        // 
        // versionLabel
        // 
        versionLabel.Alignment = ToolStripItemAlignment.Right;
        versionLabel.Name = "versionLabel";
        versionLabel.Size = new System.Drawing.Size(57, 26);
        versionLabel.Text = "Version";
        versionLabel.Click += OnAboutItemClick;
        // 
        // newVersionAvailableToolStripMenuItem
        // 
        newVersionAvailableToolStripMenuItem.Alignment = ToolStripItemAlignment.Right;
        newVersionAvailableToolStripMenuItem.Name = "newVersionAvailableToolStripMenuItem";
        newVersionAvailableToolStripMenuItem.Size = new System.Drawing.Size(124, 26);
        newVersionAvailableToolStripMenuItem.SVGImageResourceName = "GUI.Icons.UpdateAvailable.svg";
        newVersionAvailableToolStripMenuItem.Text = "Update Available";
        newVersionAvailableToolStripMenuItem.Click += OnAboutItemClick;
        // 
        // MainFormBottomPanel
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(menuStrip1);
        Name = "MainFormBottomPanel";
        Size = new System.Drawing.Size(659, 30);
        menuStrip1.ResumeLayout(false);
        menuStrip1.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion
    private System.Windows.Forms.MenuStrip menuStrip1;
    private System.Windows.Forms.ToolStripMenuItem versionLabel;
    private ThemedToolStripMenuItem newVersionAvailableToolStripMenuItem;
}
