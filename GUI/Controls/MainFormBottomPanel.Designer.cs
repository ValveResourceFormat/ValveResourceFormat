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
        menuStrip1 = new MenuStrip();
        versionLabel = new ToolStripMenuItem();
        checkForUpdatesToolStripMenuItem = new ToolStripMenuItem();
        newVersionAvailableToolStripMenuItem = new ToolStripMenuItem();
        label1 = new Label();
        menuStrip1.SuspendLayout();
        SuspendLayout();
        // 
        // menuStrip1
        // 
        menuStrip1.Dock = DockStyle.Right;
        menuStrip1.Items.AddRange(new ToolStripItem[] { versionLabel, checkForUpdatesToolStripMenuItem, newVersionAvailableToolStripMenuItem });
        menuStrip1.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
        menuStrip1.Location = new System.Drawing.Point(479, 0);
        menuStrip1.Name = "menuStrip1";
        menuStrip1.Size = new System.Drawing.Size(180, 30);
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
        // checkForUpdatesToolStripMenuItem
        // 
        checkForUpdatesToolStripMenuItem.Alignment = ToolStripItemAlignment.Right;
        checkForUpdatesToolStripMenuItem.Name = "checkForUpdatesToolStripMenuItem";
        checkForUpdatesToolStripMenuItem.Size = new System.Drawing.Size(115, 26);
        checkForUpdatesToolStripMenuItem.Text = "Check for updates";
        checkForUpdatesToolStripMenuItem.Click += CheckForUpdatesToolStripMenuItem_Click;
        // 
        // newVersionAvailableToolStripMenuItem
        // 
        newVersionAvailableToolStripMenuItem.Alignment = ToolStripItemAlignment.Right;
        newVersionAvailableToolStripMenuItem.Name = "newVersionAvailableToolStripMenuItem";
        newVersionAvailableToolStripMenuItem.Size = new System.Drawing.Size(133, 26);
        newVersionAvailableToolStripMenuItem.Text = "New version available";
        newVersionAvailableToolStripMenuItem.Visible = false;
        newVersionAvailableToolStripMenuItem.Click += NewVersionAvailableToolStripMenuItem_Click;
        // 
        // label1
        // 
        label1.AutoEllipsis = true;
        label1.Dock = DockStyle.Fill;
        label1.Location = new System.Drawing.Point(0, 0);
        label1.Name = "label1";
        label1.Padding = new Padding(4, 0, 0, 0);
        label1.Size = new System.Drawing.Size(479, 30);
        label1.TabIndex = 2;
        label1.Text = "titleLabel";
        label1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        label1.UseCompatibleTextRendering = true;
        // 
        // MainFormBottomPanel
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(label1);
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
    private System.Windows.Forms.ToolStripMenuItem checkForUpdatesToolStripMenuItem;
    private System.Windows.Forms.ToolStripMenuItem newVersionAvailableToolStripMenuItem;
    private Label label1;
}
