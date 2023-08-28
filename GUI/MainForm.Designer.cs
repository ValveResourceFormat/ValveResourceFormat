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
            components = new System.ComponentModel.Container();
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            menuStrip = new MenuStrip();
            openToolStripMenuItem = new ToolStripMenuItem();
            explorerToolStripMenuItem = new ToolStripMenuItem();
            findToolStripButton = new ToolStripMenuItem();
            recoverDeletedToolStripMenuItem = new ToolStripMenuItem();
            settingsToolStripMenuItem = new ToolStripMenuItem();
            aboutToolStripMenuItem = new ToolStripMenuItem();
            versionToolStripLabel = new ToolStripLabel();
            mainTabs = new TabControl();
            tabContextMenuStrip = new ContextMenuStrip(components);
            closeToolStripMenuItem = new ToolStripMenuItem();
            closeToolStripMenuItemsToRight = new ToolStripMenuItem();
            closeToolStripMenuItemsToLeft = new ToolStripMenuItem();
            closeToolStripMenuItems = new ToolStripMenuItem();
            exportAsIsToolStripMenuItem = new ToolStripMenuItem();
            decompileExportToolStripMenuItem = new ToolStripMenuItem();
            vpkContextMenu = new ContextMenuStrip(components);
            extractToolStripMenuItem = new ToolStripMenuItem();
            decompileToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            copyFileNameToolStripMenuItem = new ToolStripMenuItem();
            openWithDefaultAppToolStripMenuItem = new ToolStripMenuItem();
            viewAssetInfoToolStripMenuItem = new ToolStripMenuItem();
            menuStrip.SuspendLayout();
            tabContextMenuStrip.SuspendLayout();
            vpkContextMenu.SuspendLayout();
            SuspendLayout();
            // 
            // menuStrip
            // 
            menuStrip.BackColor = System.Drawing.SystemColors.Window;
            menuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            menuStrip.Items.AddRange(new ToolStripItem[] { openToolStripMenuItem, explorerToolStripMenuItem, findToolStripButton, recoverDeletedToolStripMenuItem, settingsToolStripMenuItem, aboutToolStripMenuItem, versionToolStripLabel });
            menuStrip.Location = new System.Drawing.Point(0, 0);
            menuStrip.Name = "menuStrip";
            menuStrip.RenderMode = ToolStripRenderMode.System;
            menuStrip.Size = new System.Drawing.Size(1300, 24);
            menuStrip.TabIndex = 0;
            menuStrip.Text = "menuStrip1";
            // 
            // openToolStripMenuItem
            // 
            openToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("openToolStripMenuItem.Image");
            openToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            openToolStripMenuItem.Name = "openToolStripMenuItem";
            openToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.O;
            openToolStripMenuItem.Size = new System.Drawing.Size(64, 20);
            openToolStripMenuItem.Text = "&Open";
            openToolStripMenuItem.Click += OpenToolStripMenuItem_Click;
            // 
            // explorerToolStripMenuItem
            // 
            explorerToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("explorerToolStripMenuItem.Image");
            explorerToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            explorerToolStripMenuItem.Name = "explorerToolStripMenuItem";
            explorerToolStripMenuItem.Size = new System.Drawing.Size(78, 20);
            explorerToolStripMenuItem.Text = "Explorer";
            explorerToolStripMenuItem.Click += OpenExplorer_Click;
            // 
            // findToolStripButton
            // 
            findToolStripButton.Enabled = false;
            findToolStripButton.Image = (System.Drawing.Image)resources.GetObject("findToolStripButton.Image");
            findToolStripButton.ImageScaling = ToolStripItemImageScaling.None;
            findToolStripButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            findToolStripButton.Name = "findToolStripButton";
            findToolStripButton.ShortcutKeys = Keys.Control | Keys.F;
            findToolStripButton.Size = new System.Drawing.Size(58, 20);
            findToolStripButton.Text = "&Find";
            findToolStripButton.Click += FindToolStripMenuItem_Click;
            // 
            // recoverDeletedToolStripMenuItem
            // 
            recoverDeletedToolStripMenuItem.Enabled = false;
            recoverDeletedToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("recoverDeletedToolStripMenuItem.Image");
            recoverDeletedToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            recoverDeletedToolStripMenuItem.ImageTransparentColor = System.Drawing.Color.Magenta;
            recoverDeletedToolStripMenuItem.Name = "recoverDeletedToolStripMenuItem";
            recoverDeletedToolStripMenuItem.Size = new System.Drawing.Size(143, 20);
            recoverDeletedToolStripMenuItem.Text = "Recover deleted files";
            recoverDeletedToolStripMenuItem.Click += RecoverDeletedToolStripMenuItem_Click;
            // 
            // settingsToolStripMenuItem
            // 
            settingsToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("settingsToolStripMenuItem.Image");
            settingsToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            settingsToolStripMenuItem.Size = new System.Drawing.Size(77, 20);
            settingsToolStripMenuItem.Text = "Settings";
            settingsToolStripMenuItem.Click += OnSettingsItemClick;
            // 
            // aboutToolStripMenuItem
            // 
            aboutToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("aboutToolStripMenuItem.Image");
            aboutToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            aboutToolStripMenuItem.Size = new System.Drawing.Size(68, 20);
            aboutToolStripMenuItem.Text = "About";
            aboutToolStripMenuItem.Click += OnAboutItemClick;
            // 
            // versionToolStripLabel
            // 
            versionToolStripLabel.Alignment = ToolStripItemAlignment.Right;
            versionToolStripLabel.Margin = new Padding(0);
            versionToolStripLabel.Name = "versionToolStripLabel";
            versionToolStripLabel.Padding = new Padding(4, 0, 4, 0);
            versionToolStripLabel.Size = new System.Drawing.Size(53, 20);
            versionToolStripLabel.Text = "Version";
            // 
            // mainTabs
            // 
            mainTabs.Dock = DockStyle.Fill;
            mainTabs.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            mainTabs.Location = new System.Drawing.Point(0, 24);
            mainTabs.Margin = new Padding(0);
            mainTabs.Name = "mainTabs";
            mainTabs.Padding = new System.Drawing.Point(0, 0);
            mainTabs.SelectedIndex = 0;
            mainTabs.Size = new System.Drawing.Size(1300, 776);
            mainTabs.TabIndex = 1;
            mainTabs.MouseClick += OnTabClick;
            // 
            // tabContextMenuStrip
            // 
            tabContextMenuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            tabContextMenuStrip.Items.AddRange(new ToolStripItem[] { closeToolStripMenuItem, closeToolStripMenuItemsToRight, closeToolStripMenuItemsToLeft, closeToolStripMenuItems, exportAsIsToolStripMenuItem, decompileExportToolStripMenuItem });
            tabContextMenuStrip.LayoutStyle = ToolStripLayoutStyle.Table;
            tabContextMenuStrip.Name = "contextMenuStrip1";
            tabContextMenuStrip.Size = new System.Drawing.Size(234, 184);
            // 
            // closeToolStripMenuItem
            // 
            closeToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("closeToolStripMenuItem.Image");
            closeToolStripMenuItem.Name = "closeToolStripMenuItem";
            closeToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.W;
            closeToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            closeToolStripMenuItem.Text = "Close this &tab";
            closeToolStripMenuItem.Click += CloseToolStripMenuItem_Click;
            // 
            // closeToolStripMenuItemsToRight
            // 
            closeToolStripMenuItemsToRight.Image = (System.Drawing.Image)resources.GetObject("closeToolStripMenuItemsToRight.Image");
            closeToolStripMenuItemsToRight.Name = "closeToolStripMenuItemsToRight";
            closeToolStripMenuItemsToRight.ShortcutKeys = Keys.Control | Keys.E;
            closeToolStripMenuItemsToRight.Size = new System.Drawing.Size(233, 30);
            closeToolStripMenuItemsToRight.Text = "Close all tabs to &right";
            closeToolStripMenuItemsToRight.Click += CloseToolStripMenuItemsToRight_Click;
            // 
            // closeToolStripMenuItemsToLeft
            // 
            closeToolStripMenuItemsToLeft.Image = (System.Drawing.Image)resources.GetObject("closeToolStripMenuItemsToLeft.Image");
            closeToolStripMenuItemsToLeft.Name = "closeToolStripMenuItemsToLeft";
            closeToolStripMenuItemsToLeft.Size = new System.Drawing.Size(233, 30);
            closeToolStripMenuItemsToLeft.Text = "Close all tabs to &left";
            closeToolStripMenuItemsToLeft.Click += CloseToolStripMenuItemsToLeft_Click;
            // 
            // closeToolStripMenuItems
            // 
            closeToolStripMenuItems.Image = (System.Drawing.Image)resources.GetObject("closeToolStripMenuItems.Image");
            closeToolStripMenuItems.Name = "closeToolStripMenuItems";
            closeToolStripMenuItems.ShortcutKeys = Keys.Control | Keys.Q;
            closeToolStripMenuItems.Size = new System.Drawing.Size(233, 30);
            closeToolStripMenuItems.Text = "Close &all tabs";
            closeToolStripMenuItems.Click += CloseToolStripMenuItems_Click;
            // 
            // exportAsIsToolStripMenuItem
            // 
            exportAsIsToolStripMenuItem.Name = "exportAsIsToolStripMenuItem";
            exportAsIsToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            exportAsIsToolStripMenuItem.Text = "Export as is";
            exportAsIsToolStripMenuItem.Click += ExtractToolStripMenuItem_Click;
            // 
            // decompileExportToolStripMenuItem
            // 
            decompileExportToolStripMenuItem.Name = "decompileExportToolStripMenuItem";
            decompileExportToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            decompileExportToolStripMenuItem.Text = "Decompile && export";
            decompileExportToolStripMenuItem.Click += DecompileToolStripMenuItem_Click;
            // 
            // vpkContextMenu
            // 
            vpkContextMenu.Items.AddRange(new ToolStripItem[] { extractToolStripMenuItem, decompileToolStripMenuItem, toolStripSeparator1, copyFileNameToolStripMenuItem, openWithDefaultAppToolStripMenuItem, viewAssetInfoToolStripMenuItem });
            vpkContextMenu.Name = "vpkContextMenu";
            vpkContextMenu.Size = new System.Drawing.Size(193, 120);
            // 
            // extractToolStripMenuItem
            // 
            extractToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("extractToolStripMenuItem.Image");
            extractToolStripMenuItem.Name = "extractToolStripMenuItem";
            extractToolStripMenuItem.Size = new System.Drawing.Size(192, 22);
            extractToolStripMenuItem.Text = "Export as is";
            extractToolStripMenuItem.Click += ExtractToolStripMenuItem_Click;
            // 
            // decompileToolStripMenuItem
            // 
            decompileToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("decompileToolStripMenuItem.Image");
            decompileToolStripMenuItem.Name = "decompileToolStripMenuItem";
            decompileToolStripMenuItem.Size = new System.Drawing.Size(192, 22);
            decompileToolStripMenuItem.Text = "Decompile && export";
            decompileToolStripMenuItem.Click += DecompileToolStripMenuItem_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new System.Drawing.Size(189, 6);
            // 
            // copyFileNameToolStripMenuItem
            // 
            copyFileNameToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("copyFileNameToolStripMenuItem.Image");
            copyFileNameToolStripMenuItem.Name = "copyFileNameToolStripMenuItem";
            copyFileNameToolStripMenuItem.Size = new System.Drawing.Size(192, 22);
            copyFileNameToolStripMenuItem.Text = "Copy file path";
            copyFileNameToolStripMenuItem.Click += CopyFileNameToolStripMenuItem_Click;
            // 
            // openWithDefaultAppToolStripMenuItem
            // 
            openWithDefaultAppToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("openWithDefaultAppToolStripMenuItem.Image");
            openWithDefaultAppToolStripMenuItem.Name = "openWithDefaultAppToolStripMenuItem";
            openWithDefaultAppToolStripMenuItem.Size = new System.Drawing.Size(192, 22);
            openWithDefaultAppToolStripMenuItem.Text = "Open with default app";
            openWithDefaultAppToolStripMenuItem.Click += OpenWithDefaultAppToolStripMenuItem_Click;
            // 
            // viewAssetInfoToolStripMenuItem
            // 
            viewAssetInfoToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("viewAssetInfoToolStripMenuItem.Image");
            viewAssetInfoToolStripMenuItem.Name = "viewAssetInfoToolStripMenuItem";
            viewAssetInfoToolStripMenuItem.Size = new System.Drawing.Size(192, 22);
            viewAssetInfoToolStripMenuItem.Text = "View asset info";
            viewAssetInfoToolStripMenuItem.Click += OnViewAssetInfoToolStripMenuItemClick;
            // 
            // MainForm
            // 
            AllowDrop = true;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(1300, 800);
            Controls.Add(mainTabs);
            Controls.Add(menuStrip);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            MainMenuStrip = menuStrip;
            Margin = new Padding(2);
            MinimumSize = new System.Drawing.Size(347, 340);
            Name = "MainForm";
            SizeGripStyle = SizeGripStyle.Show;
            Text = "Source 2 Viewer";
            Load += MainForm_Load;
            DragDrop += MainForm_DragDrop;
            DragEnter += MainForm_DragEnter;
            menuStrip.ResumeLayout(false);
            menuStrip.PerformLayout();
            tabContextMenuStrip.ResumeLayout(false);
            vpkContextMenu.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private MenuStrip menuStrip;
        private TabControl mainTabs;
        private ContextMenuStrip tabContextMenuStrip;
        private ToolStripMenuItem closeToolStripMenuItem;
        private ContextMenuStrip vpkContextMenu;
        private ToolStripMenuItem extractToolStripMenuItem;
        private ToolStripMenuItem copyFileNameToolStripMenuItem;
        private ToolStripMenuItem closeToolStripMenuItemsToLeft;
        private ToolStripMenuItem closeToolStripMenuItemsToRight;
        private ToolStripMenuItem closeToolStripMenuItems;
        private ToolStripMenuItem findToolStripButton;
        private ToolStripMenuItem openWithDefaultAppToolStripMenuItem;
        private ToolStripMenuItem decompileToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripMenuItem openToolStripMenuItem;
        private ToolStripMenuItem settingsToolStripMenuItem;
        private ToolStripMenuItem aboutToolStripMenuItem;
        private ToolStripMenuItem recoverDeletedToolStripMenuItem;
        private ToolStripMenuItem exportAsIsToolStripMenuItem;
        private ToolStripMenuItem decompileExportToolStripMenuItem;
        private ToolStripMenuItem explorerToolStripMenuItem;
        private ToolStripMenuItem viewAssetInfoToolStripMenuItem;
        private ToolStripLabel versionToolStripLabel;
    }
}

