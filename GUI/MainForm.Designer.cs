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
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.openToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.findToolStripButton = new System.Windows.Forms.ToolStripMenuItem();
            this.exportToolStripButton = new System.Windows.Forms.ToolStripDropDownButton();
            this.settingsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.mainTabs = new System.Windows.Forms.TabControl();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.closeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.closeToolStripMenuItemsToLeft = new System.Windows.Forms.ToolStripMenuItem();
            this.closeToolStripMenuItemsToRight = new System.Windows.Forms.ToolStripMenuItem();
            this.closeToolStripMenuItems = new System.Windows.Forms.ToolStripMenuItem();
            this.vpkContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.extractToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.decompileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.copyFileNameToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openWithDefaultAppToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuStrip1.SuspendLayout();
            this.contextMenuStrip1.SuspendLayout();
            this.vpkContextMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.BackColor = System.Drawing.SystemColors.Window;
            this.menuStrip1.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openToolStripMenuItem,
            this.findToolStripButton,
            this.exportToolStripButton,
            this.settingsToolStripMenuItem,
            this.aboutToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Padding = new System.Windows.Forms.Padding(4, 1, 0, 0);
            this.menuStrip1.RenderMode = System.Windows.Forms.ToolStripRenderMode.System;
            this.menuStrip1.Size = new System.Drawing.Size(944, 24);
            this.menuStrip1.TabIndex = 0;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // openToolStripMenuItem
            // 
            this.openToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("openToolStripMenuItem.Image")));
            this.openToolStripMenuItem.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.openToolStripMenuItem.Name = "openToolStripMenuItem";
            this.openToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O)));
            this.openToolStripMenuItem.Size = new System.Drawing.Size(64, 23);
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
            this.findToolStripButton.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.findToolStripButton.Size = new System.Drawing.Size(58, 20);
            this.findToolStripButton.Text = "&Find";
            this.findToolStripButton.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.F)));
            this.findToolStripButton.Click += new System.EventHandler(this.FindToolStripMenuItem_Click);
            // 
            // exportToolStripButton
            // 
            this.exportToolStripButton.Enabled = false;
            this.exportToolStripButton.Image = ((System.Drawing.Image)(resources.GetObject("exportToolStripButton.Image")));
            this.exportToolStripButton.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.exportToolStripButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.exportToolStripButton.Name = "exportToolStripButton";
            this.exportToolStripButton.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.exportToolStripButton.Size = new System.Drawing.Size(78, 20);
            this.exportToolStripButton.Text = "Export";
            // 
            // settingsToolStripMenuItem
            // 
            this.settingsToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("settingsToolStripMenuItem.Image")));
            this.settingsToolStripMenuItem.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            this.settingsToolStripMenuItem.Size = new System.Drawing.Size(77, 23);
            this.settingsToolStripMenuItem.Text = "Settings";
            this.settingsToolStripMenuItem.Click += new System.EventHandler(this.OnSettingsItemClick);
            // 
            // aboutToolStripMenuItem
            // 
            this.aboutToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("aboutToolStripMenuItem.Image")));
            this.aboutToolStripMenuItem.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            this.aboutToolStripMenuItem.Size = new System.Drawing.Size(68, 23);
            this.aboutToolStripMenuItem.Text = "About";
            this.aboutToolStripMenuItem.Click += new System.EventHandler(this.OnAboutItemClick);
            // 
            // mainTabs
            // 
            this.mainTabs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainTabs.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(186)));
            this.mainTabs.Location = new System.Drawing.Point(0, 24);
            this.mainTabs.Margin = new System.Windows.Forms.Padding(0);
            this.mainTabs.Name = "mainTabs";
            this.mainTabs.Padding = new System.Drawing.Point(0, 0);
            this.mainTabs.SelectedIndex = 0;
            this.mainTabs.Size = new System.Drawing.Size(944, 437);
            this.mainTabs.TabIndex = 1;
            this.mainTabs.MouseClick += new System.Windows.Forms.MouseEventHandler(this.OnTabClick);
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.closeToolStripMenuItem,
            this.closeToolStripMenuItemsToRight,
            this.closeToolStripMenuItemsToLeft,
            this.closeToolStripMenuItems});
            this.contextMenuStrip1.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.Table;
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(194, 124);
            // 
            // closeToolStripMenuItem
            // 
            this.closeToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("closeToolStripMenuItem.Image")));
            this.closeToolStripMenuItem.Name = "closeToolStripMenuItem";
            this.closeToolStripMenuItem.Size = new System.Drawing.Size(193, 30);
            this.closeToolStripMenuItem.Text = "Close this &tab";
            this.closeToolStripMenuItem.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.W)));
            this.closeToolStripMenuItem.Click += new System.EventHandler(this.CloseToolStripMenuItem_Click);
            // 
            // closeToolStripMenuItemsToLeft
            // 
            this.closeToolStripMenuItemsToLeft.Image = ((System.Drawing.Image)(resources.GetObject("closeToolStripMenuItemsToLeft.Image")));
            this.closeToolStripMenuItemsToLeft.Name = "closeToolStripMenuItemsToLeft";
            this.closeToolStripMenuItemsToLeft.Size = new System.Drawing.Size(193, 30);
            this.closeToolStripMenuItemsToLeft.Text = "Close all tabs to &left";
            this.closeToolStripMenuItemsToLeft.Click += new System.EventHandler(this.CloseToolStripMenuItemsToLeft_Click);
            // 
            // closeToolStripMenuItemsToRight
            // 
            this.closeToolStripMenuItemsToRight.Image = ((System.Drawing.Image)(resources.GetObject("closeToolStripMenuItemsToRight.Image")));
            this.closeToolStripMenuItemsToRight.Name = "closeToolStripMenuItemsToRight";
            this.closeToolStripMenuItemsToRight.Size = new System.Drawing.Size(193, 30);
            this.closeToolStripMenuItemsToRight.Text = "Close all tabs to &right";
            this.closeToolStripMenuItemsToRight.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.E)));
            this.closeToolStripMenuItemsToRight.Click += new System.EventHandler(this.CloseToolStripMenuItemsToRight_Click);
            // 
            // closeToolStripMenuItems
            // 
            this.closeToolStripMenuItems.Image = ((System.Drawing.Image)(resources.GetObject("closeToolStripMenuItems.Image")));
            this.closeToolStripMenuItems.Name = "closeToolStripMenuItems";
            this.closeToolStripMenuItems.Size = new System.Drawing.Size(193, 30);
            this.closeToolStripMenuItems.Text = "Close &all tabs";
            this.closeToolStripMenuItems.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Q)));
            this.closeToolStripMenuItems.Click += new System.EventHandler(this.CloseToolStripMenuItems_Click);
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
            this.extractToolStripMenuItem.Text = "Export";
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
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(944, 461);
            this.Controls.Add(this.mainTabs);
            this.Controls.Add(this.menuStrip1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MainMenuStrip = this.menuStrip1;
            this.Margin = new System.Windows.Forms.Padding(2);
            this.MinimumSize = new System.Drawing.Size(300, 300);
            this.Name = "MainForm";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Show;
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.MainForm_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.MainForm_DragEnter);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.contextMenuStrip1.ResumeLayout(false);
            this.vpkContextMenu.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.TabControl mainTabs;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem closeToolStripMenuItem;
        private System.Windows.Forms.ContextMenuStrip vpkContextMenu;
        private System.Windows.Forms.ToolStripMenuItem extractToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem copyFileNameToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem closeToolStripMenuItemsToLeft;
        private System.Windows.Forms.ToolStripMenuItem closeToolStripMenuItemsToRight;
        private System.Windows.Forms.ToolStripMenuItem closeToolStripMenuItems;
        private System.Windows.Forms.ToolStripMenuItem findToolStripButton;
        private System.Windows.Forms.ToolStripDropDownButton exportToolStripButton;
        private System.Windows.Forms.ToolStripMenuItem openWithDefaultAppToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem decompileToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem openToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem settingsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
    }
}

