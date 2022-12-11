using System.Windows.Forms;

namespace GUI
{
    partial class MainForm
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

            if (disposing && searchForm != null)
            {
                searchForm.Dispose();
                searchForm = null;
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.menuStrip = new System.Windows.Forms.MenuStrip();
            this.openToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.findToolStripButton = new System.Windows.Forms.ToolStripMenuItem();
            this.recoverDeletedToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.settingsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.mainTabs = new System.Windows.Forms.TabControl();
            this.tabContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.closeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.closeToolStripMenuItemsToRight = new System.Windows.Forms.ToolStripMenuItem();
            this.closeToolStripMenuItemsToLeft = new System.Windows.Forms.ToolStripMenuItem();
            this.closeToolStripMenuItems = new System.Windows.Forms.ToolStripMenuItem();
            this.exportAsIsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.decompileExportToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.vpkContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.extractToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.decompileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.copyFileNameToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openWithDefaultAppToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuStrip.SuspendLayout();
            this.tabContextMenuStrip.SuspendLayout();
            this.vpkContextMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip
            // 
            this.menuStrip.BackColor = System.Drawing.SystemColors.Window;
            this.menuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openToolStripMenuItem,
            this.findToolStripButton,
            this.recoverDeletedToolStripMenuItem,
            this.settingsToolStripMenuItem,
            this.aboutToolStripMenuItem});
            this.menuStrip.Location = new System.Drawing.Point(0, 0);
            this.menuStrip.Name = "menuStrip";
            this.menuStrip.RenderMode = System.Windows.Forms.ToolStripRenderMode.System;
            this.menuStrip.Size = new System.Drawing.Size(1101, 24);
            this.menuStrip.TabIndex = 0;
            this.menuStrip.Text = "menuStrip1";
            // 
            // openToolStripMenuItem
            // 
            this.openToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("openToolStripMenuItem.Image")));
            this.openToolStripMenuItem.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.openToolStripMenuItem.Name = "openToolStripMenuItem";
            this.openToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O)));
            this.openToolStripMenuItem.Size = new System.Drawing.Size(64, 20);
            this.openToolStripMenuItem.Text = "&Open";
            this.openToolStripMenuItem.Click += new System.EventHandler(this.OpenToolStripMenuItem_Click);
            // 
            // findToolStripButton
            // 
            this.findToolStripButton.Enabled = false;
            this.findToolStripButton.Image = ((System.Drawing.Image)(resources.GetObject("findToolStripButton.Image")));
            this.findToolStripButton.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.findToolStripButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.findToolStripButton.Name = "findToolStripButton";
            this.findToolStripButton.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.F)));
            this.findToolStripButton.Size = new System.Drawing.Size(58, 20);
            this.findToolStripButton.Text = "&Find";
            this.findToolStripButton.Click += new System.EventHandler(this.FindToolStripMenuItem_Click);
            // 
            // recoverDeletedToolStripMenuItem
            // 
            this.recoverDeletedToolStripMenuItem.Enabled = false;
            this.recoverDeletedToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("recoverDeletedToolStripMenuItem.Image")));
            this.recoverDeletedToolStripMenuItem.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.recoverDeletedToolStripMenuItem.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.recoverDeletedToolStripMenuItem.Name = "recoverDeletedToolStripMenuItem";
            this.recoverDeletedToolStripMenuItem.Size = new System.Drawing.Size(143, 20);
            this.recoverDeletedToolStripMenuItem.Text = "Recover deleted files";
            this.recoverDeletedToolStripMenuItem.Click += new System.EventHandler(this.RecoverDeletedToolStripMenuItem_Click);
            // 
            // settingsToolStripMenuItem
            // 
            this.settingsToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("settingsToolStripMenuItem.Image")));
            this.settingsToolStripMenuItem.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            this.settingsToolStripMenuItem.Size = new System.Drawing.Size(77, 20);
            this.settingsToolStripMenuItem.Text = "Settings";
            this.settingsToolStripMenuItem.Click += new System.EventHandler(this.OnSettingsItemClick);
            // 
            // aboutToolStripMenuItem
            // 
            this.aboutToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("aboutToolStripMenuItem.Image")));
            this.aboutToolStripMenuItem.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            this.aboutToolStripMenuItem.Size = new System.Drawing.Size(68, 20);
            this.aboutToolStripMenuItem.Text = "About";
            this.aboutToolStripMenuItem.Click += new System.EventHandler(this.OnAboutItemClick);
            // 
            // mainTabs
            // 
            this.mainTabs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainTabs.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.mainTabs.Location = new System.Drawing.Point(0, 24);
            this.mainTabs.Margin = new System.Windows.Forms.Padding(0);
            this.mainTabs.Name = "mainTabs";
            this.mainTabs.Padding = new System.Drawing.Point(0, 0);
            this.mainTabs.SelectedIndex = 0;
            this.mainTabs.Size = new System.Drawing.Size(1101, 508);
            this.mainTabs.TabIndex = 1;
            this.mainTabs.MouseClick += new System.Windows.Forms.MouseEventHandler(this.OnTabClick);
            // 
            // tabContextMenuStrip
            // 
            this.tabContextMenuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.tabContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.closeToolStripMenuItem,
            this.closeToolStripMenuItemsToRight,
            this.closeToolStripMenuItemsToLeft,
            this.closeToolStripMenuItems,
            this.exportAsIsToolStripMenuItem,
            this.decompileExportToolStripMenuItem});
            this.tabContextMenuStrip.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.Table;
            this.tabContextMenuStrip.Name = "contextMenuStrip1";
            this.tabContextMenuStrip.Size = new System.Drawing.Size(234, 184);
            // 
            // closeToolStripMenuItem
            // 
            this.closeToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("closeToolStripMenuItem.Image")));
            this.closeToolStripMenuItem.Name = "closeToolStripMenuItem";
            this.closeToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.W)));
            this.closeToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            this.closeToolStripMenuItem.Text = "Close this &tab";
            this.closeToolStripMenuItem.Click += new System.EventHandler(this.CloseToolStripMenuItem_Click);
            // 
            // closeToolStripMenuItemsToRight
            // 
            this.closeToolStripMenuItemsToRight.Image = ((System.Drawing.Image)(resources.GetObject("closeToolStripMenuItemsToRight.Image")));
            this.closeToolStripMenuItemsToRight.Name = "closeToolStripMenuItemsToRight";
            this.closeToolStripMenuItemsToRight.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.E)));
            this.closeToolStripMenuItemsToRight.Size = new System.Drawing.Size(233, 30);
            this.closeToolStripMenuItemsToRight.Text = "Close all tabs to &right";
            this.closeToolStripMenuItemsToRight.Click += new System.EventHandler(this.CloseToolStripMenuItemsToRight_Click);
            // 
            // closeToolStripMenuItemsToLeft
            // 
            this.closeToolStripMenuItemsToLeft.Image = ((System.Drawing.Image)(resources.GetObject("closeToolStripMenuItemsToLeft.Image")));
            this.closeToolStripMenuItemsToLeft.Name = "closeToolStripMenuItemsToLeft";
            this.closeToolStripMenuItemsToLeft.Size = new System.Drawing.Size(233, 30);
            this.closeToolStripMenuItemsToLeft.Text = "Close all tabs to &left";
            this.closeToolStripMenuItemsToLeft.Click += new System.EventHandler(this.CloseToolStripMenuItemsToLeft_Click);
            // 
            // closeToolStripMenuItems
            // 
            this.closeToolStripMenuItems.Image = ((System.Drawing.Image)(resources.GetObject("closeToolStripMenuItems.Image")));
            this.closeToolStripMenuItems.Name = "closeToolStripMenuItems";
            this.closeToolStripMenuItems.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Q)));
            this.closeToolStripMenuItems.Size = new System.Drawing.Size(233, 30);
            this.closeToolStripMenuItems.Text = "Close &all tabs";
            this.closeToolStripMenuItems.Click += new System.EventHandler(this.CloseToolStripMenuItems_Click);
            // 
            // exportAsIsToolStripMenuItem
            // 
            this.exportAsIsToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("exportAsIsToolStripMenuItem.Image")));
            this.exportAsIsToolStripMenuItem.Name = "exportAsIsToolStripMenuItem";
            this.exportAsIsToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            this.exportAsIsToolStripMenuItem.Text = "Export as is";
            this.exportAsIsToolStripMenuItem.Click += new System.EventHandler(this.ExtractToolStripMenuItem_Click);
            // 
            // decompileExportToolStripMenuItem
            // 
            this.decompileExportToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("decompileExportToolStripMenuItem.Image")));
            this.decompileExportToolStripMenuItem.Name = "decompileExportToolStripMenuItem";
            this.decompileExportToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            this.decompileExportToolStripMenuItem.Text = "Decompile && export";
            this.decompileExportToolStripMenuItem.Click += new System.EventHandler(this.DecompileToolStripMenuItem_Click);
            // 
            // vpkContextMenu
            // 
            this.vpkContextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.extractToolStripMenuItem,
            this.decompileToolStripMenuItem,
            this.toolStripSeparator1,
            this.copyFileNameToolStripMenuItem,
            this.openWithDefaultAppToolStripMenuItem});
            this.vpkContextMenu.Name = "vpkContextMenu";
            this.vpkContextMenu.Size = new System.Drawing.Size(193, 98);
            // 
            // extractToolStripMenuItem
            // 
            this.extractToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("extractToolStripMenuItem.Image")));
            this.extractToolStripMenuItem.Name = "extractToolStripMenuItem";
            this.extractToolStripMenuItem.Size = new System.Drawing.Size(192, 22);
            this.extractToolStripMenuItem.Text = "Export as is";
            this.extractToolStripMenuItem.Click += new System.EventHandler(this.ExtractToolStripMenuItem_Click);
            // 
            // decompileToolStripMenuItem
            // 
            this.decompileToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("decompileToolStripMenuItem.Image")));
            this.decompileToolStripMenuItem.Name = "decompileToolStripMenuItem";
            this.decompileToolStripMenuItem.Size = new System.Drawing.Size(192, 22);
            this.decompileToolStripMenuItem.Text = "Decompile && export";
            this.decompileToolStripMenuItem.Click += new System.EventHandler(this.DecompileToolStripMenuItem_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(189, 6);
            // 
            // copyFileNameToolStripMenuItem
            // 
            this.copyFileNameToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("copyFileNameToolStripMenuItem.Image")));
            this.copyFileNameToolStripMenuItem.Name = "copyFileNameToolStripMenuItem";
            this.copyFileNameToolStripMenuItem.Size = new System.Drawing.Size(192, 22);
            this.copyFileNameToolStripMenuItem.Text = "Copy file path";
            this.copyFileNameToolStripMenuItem.Click += new System.EventHandler(this.CopyFileNameToolStripMenuItem_Click);
            // 
            // openWithDefaultAppToolStripMenuItem
            // 
            this.openWithDefaultAppToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("openWithDefaultAppToolStripMenuItem.Image")));
            this.openWithDefaultAppToolStripMenuItem.Name = "openWithDefaultAppToolStripMenuItem";
            this.openWithDefaultAppToolStripMenuItem.Size = new System.Drawing.Size(192, 22);
            this.openWithDefaultAppToolStripMenuItem.Text = "Open with default app";
            this.openWithDefaultAppToolStripMenuItem.Click += new System.EventHandler(this.OpenWithDefaultAppToolStripMenuItem_Click);
            // 
            // MainForm
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1101, 532);
            this.Controls.Add(this.mainTabs);
            this.Controls.Add(this.menuStrip);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MainMenuStrip = this.menuStrip;
            this.Margin = new System.Windows.Forms.Padding(2);
            this.MinimumSize = new System.Drawing.Size(347, 340);
            this.Name = "MainForm";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Show;
            this.Text = "VRF";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.MainForm_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.MainForm_DragEnter);
            this.menuStrip.ResumeLayout(false);
            this.menuStrip.PerformLayout();
            this.tabContextMenuStrip.ResumeLayout(false);
            this.vpkContextMenu.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.TabControl mainTabs;
        private System.Windows.Forms.ContextMenuStrip tabContextMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem closeToolStripMenuItem;
        private System.Windows.Forms.ContextMenuStrip vpkContextMenu;
        private System.Windows.Forms.ToolStripMenuItem extractToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem copyFileNameToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem closeToolStripMenuItemsToLeft;
        private System.Windows.Forms.ToolStripMenuItem closeToolStripMenuItemsToRight;
        private System.Windows.Forms.ToolStripMenuItem closeToolStripMenuItems;
        private System.Windows.Forms.ToolStripMenuItem findToolStripButton;
        private System.Windows.Forms.ToolStripMenuItem openWithDefaultAppToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem decompileToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem openToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem settingsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
        private ToolStripMenuItem recoverDeletedToolStripMenuItem;
        private ToolStripMenuItem exportAsIsToolStripMenuItem;
        private ToolStripMenuItem decompileExportToolStripMenuItem;
    }
}

