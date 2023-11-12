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
            fileToolStripMenuItem = new ToolStripMenuItem();
            openToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator2 = new ToolStripSeparator();
            registerVpkFileAssociationToolStripMenuItem = new ToolStripMenuItem();
            createVpkFromFolderToolStripMenuItem = new ToolStripMenuItem();
            explorerToolStripMenuItem = new ToolStripMenuItem();
            findToolStripButton = new ToolStripMenuItem();
            settingsToolStripMenuItem = new ToolStripMenuItem();
            aboutToolStripMenuItem = new ToolStripMenuItem();
            versionToolStripLabel = new ToolStripLabel();
            newVersionAvailableToolStripMenuItem = new ToolStripMenuItem();
            checkForUpdatesToolStripMenuItem = new ToolStripMenuItem();
            recoverDeletedToolStripMenuItem = new ToolStripMenuItem();
            mainTabs = new TabControl();
            tabContextMenuStrip = new ContextMenuStrip(components);
            closeToolStripMenuItem = new ToolStripMenuItem();
            closeToolStripMenuItems = new ToolStripMenuItem();
            closeToolStripMenuItemsToRight = new ToolStripMenuItem();
            closeToolStripMenuItemsToLeft = new ToolStripMenuItem();
            exportAsIsToolStripMenuItem = new ToolStripMenuItem();
            decompileExportToolStripMenuItem = new ToolStripMenuItem();
            clearConsoleToolStripMenuItem = new ToolStripMenuItem();
            vpkContextMenu = new ContextMenuStrip(components);
            extractToolStripMenuItem = new ToolStripMenuItem();
            decompileToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            copyFileNameToolStripMenuItem = new ToolStripMenuItem();
            openWithDefaultAppToolStripMenuItem = new ToolStripMenuItem();
            viewAssetInfoToolStripMenuItem = new ToolStripMenuItem();
            verifyPackageContentsToolStripMenuItem = new ToolStripMenuItem();
            vpkEditingContextMenu = new ContextMenuStrip(components);
            addNewFileOrFolderToolStripMenuItem = new ToolStripMenuItem();
            saveVPKToDiskToolStripMenuItem = new ToolStripMenuItem();
            menuStrip.SuspendLayout();
            tabContextMenuStrip.SuspendLayout();
            vpkContextMenu.SuspendLayout();
            vpkEditingContextMenu.SuspendLayout();
            SuspendLayout();
            // 
            // menuStrip
            // 
            menuStrip.BackColor = System.Drawing.SystemColors.Window;
            menuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            menuStrip.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, explorerToolStripMenuItem, findToolStripButton, settingsToolStripMenuItem, aboutToolStripMenuItem, versionToolStripLabel, newVersionAvailableToolStripMenuItem, checkForUpdatesToolStripMenuItem });
            menuStrip.Location = new System.Drawing.Point(0, 0);
            menuStrip.Name = "menuStrip";
            menuStrip.RenderMode = ToolStripRenderMode.System;
            menuStrip.Size = new System.Drawing.Size(1300, 24);
            menuStrip.TabIndex = 0;
            menuStrip.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { openToolStripMenuItem, toolStripSeparator2, registerVpkFileAssociationToolStripMenuItem, createVpkFromFolderToolStripMenuItem });
            fileToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("fileToolStripMenuItem.Image");
            fileToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new System.Drawing.Size(53, 20);
            fileToolStripMenuItem.Text = "F&ile";
            // 
            // openToolStripMenuItem
            // 
            openToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("openToolStripMenuItem.Image");
            openToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            openToolStripMenuItem.Name = "openToolStripMenuItem";
            openToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.O;
            openToolStripMenuItem.Size = new System.Drawing.Size(203, 22);
            openToolStripMenuItem.Text = "&Open";
            openToolStripMenuItem.Click += OpenToolStripMenuItem_Click;
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new System.Drawing.Size(200, 6);
            // 
            // registerVpkFileAssociationToolStripMenuItem
            // 
            registerVpkFileAssociationToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("registerVpkFileAssociationToolStripMenuItem.Image");
            registerVpkFileAssociationToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            registerVpkFileAssociationToolStripMenuItem.Name = "registerVpkFileAssociationToolStripMenuItem";
            registerVpkFileAssociationToolStripMenuItem.Size = new System.Drawing.Size(203, 22);
            registerVpkFileAssociationToolStripMenuItem.Text = "Open VPKs with this app";
            registerVpkFileAssociationToolStripMenuItem.Click += RegisterVpkFileAssociationToolStripMenuItem_Click;
            // 
            // createVpkFromFolderToolStripMenuItem
            // 
            createVpkFromFolderToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("createVpkFromFolderToolStripMenuItem.Image");
            createVpkFromFolderToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            createVpkFromFolderToolStripMenuItem.Name = "createVpkFromFolderToolStripMenuItem";
            createVpkFromFolderToolStripMenuItem.Size = new System.Drawing.Size(203, 22);
            createVpkFromFolderToolStripMenuItem.Text = "Create VPK from folder";
            createVpkFromFolderToolStripMenuItem.Click += CreateVpkFromFolderToolStripMenuItem_Click;
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
            // newVersionAvailableToolStripMenuItem
            // 
            newVersionAvailableToolStripMenuItem.Alignment = ToolStripItemAlignment.Right;
            newVersionAvailableToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("newVersionAvailableToolStripMenuItem.Image");
            newVersionAvailableToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            newVersionAvailableToolStripMenuItem.Name = "newVersionAvailableToolStripMenuItem";
            newVersionAvailableToolStripMenuItem.Size = new System.Drawing.Size(149, 20);
            newVersionAvailableToolStripMenuItem.Text = "New version available";
            newVersionAvailableToolStripMenuItem.Visible = false;
            newVersionAvailableToolStripMenuItem.Click += NewVersionAvailableToolStripMenuItem_Click;
            // 
            // checkForUpdatesToolStripMenuItem
            // 
            checkForUpdatesToolStripMenuItem.Alignment = ToolStripItemAlignment.Right;
            checkForUpdatesToolStripMenuItem.Name = "checkForUpdatesToolStripMenuItem";
            checkForUpdatesToolStripMenuItem.Size = new System.Drawing.Size(115, 20);
            checkForUpdatesToolStripMenuItem.Text = "Check for updates";
            checkForUpdatesToolStripMenuItem.Click += CheckForUpdatesToolStripMenuItem_Click;
            // 
            // recoverDeletedToolStripMenuItem
            // 
            recoverDeletedToolStripMenuItem.Enabled = false;
            recoverDeletedToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("recoverDeletedToolStripMenuItem.Image");
            recoverDeletedToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            recoverDeletedToolStripMenuItem.ImageTransparentColor = System.Drawing.Color.Magenta;
            recoverDeletedToolStripMenuItem.Name = "recoverDeletedToolStripMenuItem";
            recoverDeletedToolStripMenuItem.Size = new System.Drawing.Size(199, 22);
            recoverDeletedToolStripMenuItem.Text = "Recover deleted files";
            recoverDeletedToolStripMenuItem.Click += RecoverDeletedToolStripMenuItem_Click;
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
            tabContextMenuStrip.Items.AddRange(new ToolStripItem[] { closeToolStripMenuItem, closeToolStripMenuItems, closeToolStripMenuItemsToRight, closeToolStripMenuItemsToLeft, exportAsIsToolStripMenuItem, decompileExportToolStripMenuItem, clearConsoleToolStripMenuItem });
            tabContextMenuStrip.LayoutStyle = ToolStripLayoutStyle.Table;
            tabContextMenuStrip.Name = "contextMenuStrip1";
            tabContextMenuStrip.Size = new System.Drawing.Size(234, 214);
            // 
            // closeToolStripMenuItem
            // 
            closeToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("closeToolStripMenuItem.Image");
            closeToolStripMenuItem.Name = "closeToolStripMenuItem";
            closeToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.W;
            closeToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            closeToolStripMenuItem.Text = "Close &tab";
            closeToolStripMenuItem.Click += CloseToolStripMenuItem_Click;
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
            // exportAsIsToolStripMenuItem
            // 
            exportAsIsToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("exportAsIsToolStripMenuItem.Image");
            exportAsIsToolStripMenuItem.Name = "exportAsIsToolStripMenuItem";
            exportAsIsToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            exportAsIsToolStripMenuItem.Text = "Export as is";
            exportAsIsToolStripMenuItem.Click += ExtractToolStripMenuItem_Click;
            // 
            // decompileExportToolStripMenuItem
            // 
            decompileExportToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("decompileExportToolStripMenuItem.Image");
            decompileExportToolStripMenuItem.Name = "decompileExportToolStripMenuItem";
            decompileExportToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            decompileExportToolStripMenuItem.Text = "Decompile && export";
            decompileExportToolStripMenuItem.Click += DecompileToolStripMenuItem_Click;
            // 
            // clearConsoleToolStripMenuItem
            // 
            clearConsoleToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("clearConsoleToolStripMenuItem.Image");
            clearConsoleToolStripMenuItem.Name = "clearConsoleToolStripMenuItem";
            clearConsoleToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            clearConsoleToolStripMenuItem.Text = "Clear console";
            clearConsoleToolStripMenuItem.Click += ClearConsoleToolStripMenuItem_Click;
            // 
            // vpkContextMenu
            // 
            vpkContextMenu.Items.AddRange(new ToolStripItem[] { extractToolStripMenuItem, decompileToolStripMenuItem, toolStripSeparator1, copyFileNameToolStripMenuItem, openWithDefaultAppToolStripMenuItem, viewAssetInfoToolStripMenuItem, verifyPackageContentsToolStripMenuItem, recoverDeletedToolStripMenuItem });
            vpkContextMenu.Name = "vpkContextMenu";
            vpkContextMenu.Size = new System.Drawing.Size(200, 164);
            // 
            // extractToolStripMenuItem
            // 
            extractToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("extractToolStripMenuItem.Image");
            extractToolStripMenuItem.Name = "extractToolStripMenuItem";
            extractToolStripMenuItem.Size = new System.Drawing.Size(199, 22);
            extractToolStripMenuItem.Text = "Export as is";
            extractToolStripMenuItem.Click += ExtractToolStripMenuItem_Click;
            // 
            // decompileToolStripMenuItem
            // 
            decompileToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("decompileToolStripMenuItem.Image");
            decompileToolStripMenuItem.Name = "decompileToolStripMenuItem";
            decompileToolStripMenuItem.Size = new System.Drawing.Size(199, 22);
            decompileToolStripMenuItem.Text = "Decompile && export";
            decompileToolStripMenuItem.Click += DecompileToolStripMenuItem_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new System.Drawing.Size(196, 6);
            // 
            // copyFileNameToolStripMenuItem
            // 
            copyFileNameToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("copyFileNameToolStripMenuItem.Image");
            copyFileNameToolStripMenuItem.Name = "copyFileNameToolStripMenuItem";
            copyFileNameToolStripMenuItem.Size = new System.Drawing.Size(199, 22);
            copyFileNameToolStripMenuItem.Text = "Copy file path";
            copyFileNameToolStripMenuItem.Click += CopyFileNameToolStripMenuItem_Click;
            // 
            // openWithDefaultAppToolStripMenuItem
            // 
            openWithDefaultAppToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("openWithDefaultAppToolStripMenuItem.Image");
            openWithDefaultAppToolStripMenuItem.Name = "openWithDefaultAppToolStripMenuItem";
            openWithDefaultAppToolStripMenuItem.Size = new System.Drawing.Size(199, 22);
            openWithDefaultAppToolStripMenuItem.Text = "Open with default app";
            openWithDefaultAppToolStripMenuItem.Click += OpenWithDefaultAppToolStripMenuItem_Click;
            // 
            // viewAssetInfoToolStripMenuItem
            // 
            viewAssetInfoToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("viewAssetInfoToolStripMenuItem.Image");
            viewAssetInfoToolStripMenuItem.Name = "viewAssetInfoToolStripMenuItem";
            viewAssetInfoToolStripMenuItem.Size = new System.Drawing.Size(199, 22);
            viewAssetInfoToolStripMenuItem.Text = "View asset info";
            viewAssetInfoToolStripMenuItem.Click += OnViewAssetInfoToolStripMenuItemClick;
            // 
            // verifyPackageContentsToolStripMenuItem
            // 
            verifyPackageContentsToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("verifyPackageContentsToolStripMenuItem.Image");
            verifyPackageContentsToolStripMenuItem.Name = "verifyPackageContentsToolStripMenuItem";
            verifyPackageContentsToolStripMenuItem.Size = new System.Drawing.Size(199, 22);
            verifyPackageContentsToolStripMenuItem.Text = "Verify package contents";
            verifyPackageContentsToolStripMenuItem.Click += VerifyPackageContentsToolStripMenuItem_Click;
            // 
            // vpkEditingContextMenu
            // 
            vpkEditingContextMenu.Items.AddRange(new ToolStripItem[] { addNewFileOrFolderToolStripMenuItem, saveVPKToDiskToolStripMenuItem });
            vpkEditingContextMenu.Name = "vpkEditingContextMenu";
            vpkEditingContextMenu.Size = new System.Drawing.Size(189, 70);
            // 
            // addNewFileOrFolderToolStripMenuItem
            // 
            addNewFileOrFolderToolStripMenuItem.Name = "addNewFileOrFolderToolStripMenuItem";
            addNewFileOrFolderToolStripMenuItem.Size = new System.Drawing.Size(188, 22);
            addNewFileOrFolderToolStripMenuItem.Text = "&Add new file or folder";
            addNewFileOrFolderToolStripMenuItem.Click += OnVpkAddNewFileToolStripMenuItem_Click;
            // 
            // saveVPKToDiskToolStripMenuItem
            // 
            saveVPKToDiskToolStripMenuItem.Name = "saveVPKToDiskToolStripMenuItem";
            saveVPKToDiskToolStripMenuItem.Size = new System.Drawing.Size(188, 22);
            saveVPKToDiskToolStripMenuItem.Text = "&Save VPK to disk";
            saveVPKToDiskToolStripMenuItem.Click += OnSaveVPKToDiskToolStripMenuItem_Click;
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
            vpkEditingContextMenu.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private MenuStrip menuStrip;
        private TabControl mainTabs;
        private ContextMenuStrip tabContextMenuStrip;
        private ToolStripMenuItem closeToolStripMenuItem;
        private ContextMenuStrip vpkContextMenu;
        private ContextMenuStrip vpkEditingContextMenu;
        private ToolStripMenuItem extractToolStripMenuItem;
        private ToolStripMenuItem copyFileNameToolStripMenuItem;
        private ToolStripMenuItem closeToolStripMenuItemsToLeft;
        private ToolStripMenuItem closeToolStripMenuItemsToRight;
        private ToolStripMenuItem closeToolStripMenuItems;
        private ToolStripMenuItem findToolStripButton;
        private ToolStripMenuItem openWithDefaultAppToolStripMenuItem;
        private ToolStripMenuItem decompileToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripMenuItem settingsToolStripMenuItem;
        private ToolStripMenuItem aboutToolStripMenuItem;
        private ToolStripMenuItem recoverDeletedToolStripMenuItem;
        private ToolStripMenuItem exportAsIsToolStripMenuItem;
        private ToolStripMenuItem decompileExportToolStripMenuItem;
        private ToolStripMenuItem explorerToolStripMenuItem;
        private ToolStripMenuItem viewAssetInfoToolStripMenuItem;
        private ToolStripLabel versionToolStripLabel;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem openToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator2;
        private ToolStripMenuItem createVpkFromFolderToolStripMenuItem;
        private ToolStripMenuItem verifyPackageContentsToolStripMenuItem;
        private ToolStripMenuItem registerVpkFileAssociationToolStripMenuItem;
        private ToolStripMenuItem newVersionAvailableToolStripMenuItem;
        private ToolStripMenuItem checkForUpdatesToolStripMenuItem;
        private ToolStripMenuItem clearConsoleToolStripMenuItem;
        private ToolStripMenuItem addNewFileOrFolderToolStripMenuItem;
        private ToolStripMenuItem saveVPKToDiskToolStripMenuItem;
    }
}

