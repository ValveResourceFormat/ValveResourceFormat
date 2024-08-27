using GUI.Theme;
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
            menuStrip = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            openToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator2 = new ToolStripSeparator();
            registerVpkFileAssociationToolStripMenuItem = new ToolStripMenuItem();
            createVpkFromFolderToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator4 = new ToolStripSeparator();
            openWelcomeScreenToolStripMenuItem = new ToolStripMenuItem();
            explorerToolStripMenuItem = new ToolStripMenuItem();
            findToolStripButton = new ToolStripMenuItem();
            settingsToolStripMenuItem = new ToolStripMenuItem();
            themeToolStripMenuItem = new ToolStripMenuItem();
            aboutToolStripMenuItem = new ToolStripMenuItem();
            versionLabel = new ToolStripMenuItem();
            newVersionAvailableToolStripMenuItem = new ToolStripMenuItem();
            checkForUpdatesToolStripMenuItem = new ToolStripMenuItem();
            recoverDeletedToolStripMenuItem = new ToolStripMenuItem();
            mainTabs = new CustomTabControl();
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
            copyFileNameOnDiskToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator3 = new ToolStripSeparator();
            openWithDefaultAppToolStripMenuItem = new ToolStripMenuItem();
            viewAssetInfoToolStripMenuItem = new ToolStripMenuItem();
            verifyPackageContentsToolStripMenuItem = new ToolStripMenuItem();
            vpkEditingContextMenu = new ContextMenuStrip(components);
            vpkEditCreateFolderToolStripMenuItem = new ToolStripMenuItem();
            vpkEditAddExistingFolderToolStripMenuItem = new ToolStripMenuItem();
            vpkEditAddExistingFilesToolStripMenuItem = new ToolStripMenuItem();
            vpkEditRemoveThisFolderToolStripMenuItem = new ToolStripMenuItem();
            vpkEditRemoveThisFileToolStripMenuItem = new ToolStripMenuItem();
            vpkEditSaveToDiskToolStripMenuItem = new ToolStripMenuItem();
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
            menuStrip.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, explorerToolStripMenuItem, findToolStripButton, settingsToolStripMenuItem, themeToolStripMenuItem, aboutToolStripMenuItem, versionLabel, newVersionAvailableToolStripMenuItem, checkForUpdatesToolStripMenuItem });
            menuStrip.Location = new System.Drawing.Point(0, 0);
            menuStrip.Name = "menuStrip";
            menuStrip.RenderMode = ToolStripRenderMode.System;
            menuStrip.Size = new System.Drawing.Size(1300, 24);
            menuStrip.TabIndex = 0;
            menuStrip.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { openToolStripMenuItem, toolStripSeparator2, registerVpkFileAssociationToolStripMenuItem, createVpkFromFolderToolStripMenuItem, toolStripSeparator4, openWelcomeScreenToolStripMenuItem });
            fileToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
            fileToolStripMenuItem.Text = "F&ile";
            // 
            // openToolStripMenuItem
            // 
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
            registerVpkFileAssociationToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            registerVpkFileAssociationToolStripMenuItem.Name = "registerVpkFileAssociationToolStripMenuItem";
            registerVpkFileAssociationToolStripMenuItem.Size = new System.Drawing.Size(203, 22);
            registerVpkFileAssociationToolStripMenuItem.Text = "Open VPKs with this app";
            registerVpkFileAssociationToolStripMenuItem.Click += RegisterVpkFileAssociationToolStripMenuItem_Click;
            // 
            // createVpkFromFolderToolStripMenuItem
            // 
            createVpkFromFolderToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            createVpkFromFolderToolStripMenuItem.Name = "createVpkFromFolderToolStripMenuItem";
            createVpkFromFolderToolStripMenuItem.Size = new System.Drawing.Size(203, 22);
            createVpkFromFolderToolStripMenuItem.Text = "Create VPK from folder";
            createVpkFromFolderToolStripMenuItem.Click += CreateVpkFromFolderToolStripMenuItem_Click;
            // 
            // toolStripSeparator4
            // 
            toolStripSeparator4.Name = "toolStripSeparator4";
            toolStripSeparator4.Size = new System.Drawing.Size(200, 6);
            // 
            // openWelcomeScreenToolStripMenuItem
            // 
            openWelcomeScreenToolStripMenuItem.Name = "openWelcomeScreenToolStripMenuItem";
            openWelcomeScreenToolStripMenuItem.Size = new System.Drawing.Size(203, 22);
            openWelcomeScreenToolStripMenuItem.Text = "Open welcome screen";
            openWelcomeScreenToolStripMenuItem.Click += OnOpenWelcomeScreenToolStripMenuItem_Click;
            // 
            // explorerToolStripMenuItem
            // 
            explorerToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            explorerToolStripMenuItem.Name = "explorerToolStripMenuItem";
            explorerToolStripMenuItem.Size = new System.Drawing.Size(62, 20);
            explorerToolStripMenuItem.Text = "Explorer";
            explorerToolStripMenuItem.Click += OpenExplorer_Click;
            // 
            // findToolStripButton
            // 
            findToolStripButton.Enabled = false;
            findToolStripButton.ImageScaling = ToolStripItemImageScaling.None;
            findToolStripButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            findToolStripButton.Name = "findToolStripButton";
            findToolStripButton.ShortcutKeys = Keys.Control | Keys.F;
            findToolStripButton.Size = new System.Drawing.Size(42, 20);
            findToolStripButton.Text = "&Find";
            findToolStripButton.Click += FindToolStripMenuItem_Click;
            // 
            // settingsToolStripMenuItem
            // 
            settingsToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            settingsToolStripMenuItem.Size = new System.Drawing.Size(61, 20);
            settingsToolStripMenuItem.Text = "Settings";
            settingsToolStripMenuItem.Click += OnSettingsItemClick;
            // 
            // themeToolStripMenuItem
            // 
            themeToolStripMenuItem.Name = "themeToolStripMenuItem";
            themeToolStripMenuItem.Size = new System.Drawing.Size(60, 20);
            themeToolStripMenuItem.Text = "Themes";
            // 
            // aboutToolStripMenuItem
            // 
            aboutToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            aboutToolStripMenuItem.Size = new System.Drawing.Size(52, 20);
            aboutToolStripMenuItem.Text = "About";
            aboutToolStripMenuItem.Click += OnAboutItemClick;
            // 
            // versionLabel
            // 
            versionLabel.Alignment = ToolStripItemAlignment.Right;
            versionLabel.Name = "versionLabel";
            versionLabel.Size = new System.Drawing.Size(57, 20);
            versionLabel.Text = "Version";
            versionLabel.Click += OnAboutItemClick;
            // 
            // newVersionAvailableToolStripMenuItem
            // 
            newVersionAvailableToolStripMenuItem.Alignment = ToolStripItemAlignment.Right;
            newVersionAvailableToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            newVersionAvailableToolStripMenuItem.Name = "newVersionAvailableToolStripMenuItem";
            newVersionAvailableToolStripMenuItem.Size = new System.Drawing.Size(133, 20);
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
            recoverDeletedToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            recoverDeletedToolStripMenuItem.ImageTransparentColor = System.Drawing.Color.Magenta;
            recoverDeletedToolStripMenuItem.Name = "recoverDeletedToolStripMenuItem";
            recoverDeletedToolStripMenuItem.Size = new System.Drawing.Size(208, 22);
            recoverDeletedToolStripMenuItem.Text = "Recover deleted files";
            recoverDeletedToolStripMenuItem.Click += RecoverDeletedToolStripMenuItem_Click;
            // 
            // mainTabs
            // 
            mainTabs.Dock = DockStyle.Fill;
            mainTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            mainTabs.Font = new System.Drawing.Font("Segoe UI", 9F);
            mainTabs.Location = new System.Drawing.Point(0, 24);
            mainTabs.Margin = new Padding(0);
            mainTabs.Name = "mainTabs";
            mainTabs.Padding = new System.Drawing.Point(0, 0);
            mainTabs.PageBackColor = System.Drawing.SystemColors.ControlDark;
            mainTabs.SelectedIndex = 0;
            mainTabs.Size = new System.Drawing.Size(1300, 776);
            mainTabs.TabIndex = 1;
            mainTabs.TCBackColor = System.Drawing.SystemColors.Control;
            mainTabs.TCForeColor = System.Drawing.Color.FromArgb(255, 255, 255);
            mainTabs.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            mainTabs.MouseClick += OnTabClick;
            // 
            // tabContextMenuStrip
            // 
            tabContextMenuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            tabContextMenuStrip.Items.AddRange(new ToolStripItem[] { closeToolStripMenuItem, closeToolStripMenuItems, closeToolStripMenuItemsToRight, closeToolStripMenuItemsToLeft, exportAsIsToolStripMenuItem, decompileExportToolStripMenuItem, clearConsoleToolStripMenuItem });
            tabContextMenuStrip.LayoutStyle = ToolStripLayoutStyle.Table;
            tabContextMenuStrip.Name = "contextMenuStrip1";
            tabContextMenuStrip.Size = new System.Drawing.Size(226, 158);
            // 
            // closeToolStripMenuItem
            // 
            closeToolStripMenuItem.Name = "closeToolStripMenuItem";
            closeToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.W;
            closeToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            closeToolStripMenuItem.Text = "Close &tab";
            closeToolStripMenuItem.Click += CloseToolStripMenuItem_Click;
            // 
            // closeToolStripMenuItems
            // 
            closeToolStripMenuItems.Name = "closeToolStripMenuItems";
            closeToolStripMenuItems.ShortcutKeys = Keys.Control | Keys.Q;
            closeToolStripMenuItems.Size = new System.Drawing.Size(225, 22);
            closeToolStripMenuItems.Text = "Close &all tabs";
            closeToolStripMenuItems.Click += CloseToolStripMenuItems_Click;
            // 
            // closeToolStripMenuItemsToRight
            // 
            closeToolStripMenuItemsToRight.Name = "closeToolStripMenuItemsToRight";
            closeToolStripMenuItemsToRight.ShortcutKeys = Keys.Control | Keys.E;
            closeToolStripMenuItemsToRight.Size = new System.Drawing.Size(225, 22);
            closeToolStripMenuItemsToRight.Text = "Close all tabs to &right";
            closeToolStripMenuItemsToRight.Click += CloseToolStripMenuItemsToRight_Click;
            // 
            // closeToolStripMenuItemsToLeft
            // 
            closeToolStripMenuItemsToLeft.Name = "closeToolStripMenuItemsToLeft";
            closeToolStripMenuItemsToLeft.Size = new System.Drawing.Size(225, 22);
            closeToolStripMenuItemsToLeft.Text = "Close all tabs to &left";
            closeToolStripMenuItemsToLeft.Click += CloseToolStripMenuItemsToLeft_Click;
            // 
            // exportAsIsToolStripMenuItem
            // 
            exportAsIsToolStripMenuItem.Name = "exportAsIsToolStripMenuItem";
            exportAsIsToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            exportAsIsToolStripMenuItem.Text = "Export as is";
            exportAsIsToolStripMenuItem.Click += ExtractToolStripMenuItem_Click;
            // 
            // decompileExportToolStripMenuItem
            // 
            decompileExportToolStripMenuItem.Name = "decompileExportToolStripMenuItem";
            decompileExportToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            decompileExportToolStripMenuItem.Text = "Decompile && export";
            decompileExportToolStripMenuItem.Click += DecompileToolStripMenuItem_Click;
            // 
            // clearConsoleToolStripMenuItem
            // 
            clearConsoleToolStripMenuItem.Name = "clearConsoleToolStripMenuItem";
            clearConsoleToolStripMenuItem.Size = new System.Drawing.Size(225, 22);
            clearConsoleToolStripMenuItem.Text = "Clear console";
            clearConsoleToolStripMenuItem.Click += ClearConsoleToolStripMenuItem_Click;
            // 
            // vpkContextMenu
            // 
            vpkContextMenu.Items.AddRange(new ToolStripItem[] { extractToolStripMenuItem, decompileToolStripMenuItem, toolStripSeparator1, copyFileNameToolStripMenuItem, copyFileNameOnDiskToolStripMenuItem, toolStripSeparator3, openWithDefaultAppToolStripMenuItem, viewAssetInfoToolStripMenuItem, verifyPackageContentsToolStripMenuItem, recoverDeletedToolStripMenuItem });
            vpkContextMenu.Name = "vpkContextMenu";
            vpkContextMenu.Size = new System.Drawing.Size(209, 192);
            // 
            // extractToolStripMenuItem
            // 
            extractToolStripMenuItem.Name = "extractToolStripMenuItem";
            extractToolStripMenuItem.Size = new System.Drawing.Size(208, 22);
            extractToolStripMenuItem.Text = "Export as is";
            extractToolStripMenuItem.Click += ExtractToolStripMenuItem_Click;
            // 
            // decompileToolStripMenuItem
            // 
            decompileToolStripMenuItem.Name = "decompileToolStripMenuItem";
            decompileToolStripMenuItem.Size = new System.Drawing.Size(208, 22);
            decompileToolStripMenuItem.Text = "Decompile && export";
            decompileToolStripMenuItem.Click += DecompileToolStripMenuItem_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new System.Drawing.Size(205, 6);
            // 
            // copyFileNameToolStripMenuItem
            // 
            copyFileNameToolStripMenuItem.Name = "copyFileNameToolStripMenuItem";
            copyFileNameToolStripMenuItem.Size = new System.Drawing.Size(208, 22);
            copyFileNameToolStripMenuItem.Text = "Copy file path in package";
            copyFileNameToolStripMenuItem.Click += CopyFileNameToolStripMenuItem_Click;
            // 
            // copyFileNameOnDiskToolStripMenuItem
            // 
            copyFileNameOnDiskToolStripMenuItem.Name = "copyFileNameOnDiskToolStripMenuItem";
            copyFileNameOnDiskToolStripMenuItem.Size = new System.Drawing.Size(208, 22);
            copyFileNameOnDiskToolStripMenuItem.Text = "Copy file path on disk";
            copyFileNameOnDiskToolStripMenuItem.Click += CopyFileNameOnDiskToolStripMenuItem_Click;
            // 
            // toolStripSeparator3
            // 
            toolStripSeparator3.Name = "toolStripSeparator3";
            toolStripSeparator3.Size = new System.Drawing.Size(205, 6);
            // 
            // openWithDefaultAppToolStripMenuItem
            // 
            openWithDefaultAppToolStripMenuItem.Name = "openWithDefaultAppToolStripMenuItem";
            openWithDefaultAppToolStripMenuItem.Size = new System.Drawing.Size(208, 22);
            openWithDefaultAppToolStripMenuItem.Text = "Open with default app";
            openWithDefaultAppToolStripMenuItem.Click += OpenWithDefaultAppToolStripMenuItem_Click;
            // 
            // viewAssetInfoToolStripMenuItem
            // 
            viewAssetInfoToolStripMenuItem.Name = "viewAssetInfoToolStripMenuItem";
            viewAssetInfoToolStripMenuItem.Size = new System.Drawing.Size(208, 22);
            viewAssetInfoToolStripMenuItem.Text = "View asset info";
            viewAssetInfoToolStripMenuItem.Click += OnViewAssetInfoToolStripMenuItemClick;
            // 
            // verifyPackageContentsToolStripMenuItem
            // 
            verifyPackageContentsToolStripMenuItem.Name = "verifyPackageContentsToolStripMenuItem";
            verifyPackageContentsToolStripMenuItem.Size = new System.Drawing.Size(208, 22);
            verifyPackageContentsToolStripMenuItem.Text = "Verify package contents";
            verifyPackageContentsToolStripMenuItem.Click += VerifyPackageContentsToolStripMenuItem_Click;
            // 
            // vpkEditingContextMenu
            // 
            vpkEditingContextMenu.Items.AddRange(new ToolStripItem[] { vpkEditCreateFolderToolStripMenuItem, vpkEditAddExistingFolderToolStripMenuItem, vpkEditAddExistingFilesToolStripMenuItem, vpkEditRemoveThisFolderToolStripMenuItem, vpkEditRemoveThisFileToolStripMenuItem, vpkEditSaveToDiskToolStripMenuItem });
            vpkEditingContextMenu.Name = "vpkEditingContextMenu";
            vpkEditingContextMenu.Size = new System.Drawing.Size(175, 136);
            // 
            // vpkEditCreateFolderToolStripMenuItem
            // 
            vpkEditCreateFolderToolStripMenuItem.Name = "vpkEditCreateFolderToolStripMenuItem";
            vpkEditCreateFolderToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            vpkEditCreateFolderToolStripMenuItem.Text = "Create folder";
            vpkEditCreateFolderToolStripMenuItem.Click += OnVpkCreateFolderToolStripMenuItem_Click;
            // 
            // vpkEditAddExistingFolderToolStripMenuItem
            // 
            vpkEditAddExistingFolderToolStripMenuItem.Name = "vpkEditAddExistingFolderToolStripMenuItem";
            vpkEditAddExistingFolderToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            vpkEditAddExistingFolderToolStripMenuItem.Text = "&Add existing folder";
            vpkEditAddExistingFolderToolStripMenuItem.Click += OnVpkAddNewFolderToolStripMenuItem_Click;
            // 
            // vpkEditAddExistingFilesToolStripMenuItem
            // 
            vpkEditAddExistingFilesToolStripMenuItem.Name = "vpkEditAddExistingFilesToolStripMenuItem";
            vpkEditAddExistingFilesToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            vpkEditAddExistingFilesToolStripMenuItem.Text = "Add existing &files";
            vpkEditAddExistingFilesToolStripMenuItem.Click += OnVpkAddNewFileToolStripMenuItem_Click;
            // 
            // vpkEditRemoveThisFolderToolStripMenuItem
            // 
            vpkEditRemoveThisFolderToolStripMenuItem.Name = "vpkEditRemoveThisFolderToolStripMenuItem";
            vpkEditRemoveThisFolderToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            vpkEditRemoveThisFolderToolStripMenuItem.Text = "&Remove this folder";
            vpkEditRemoveThisFolderToolStripMenuItem.Click += OnVpkEditingRemoveThisToolStripMenuItem_Click;
            // 
            // vpkEditRemoveThisFileToolStripMenuItem
            // 
            vpkEditRemoveThisFileToolStripMenuItem.Name = "vpkEditRemoveThisFileToolStripMenuItem";
            vpkEditRemoveThisFileToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            vpkEditRemoveThisFileToolStripMenuItem.Text = "&Remove this file";
            vpkEditRemoveThisFileToolStripMenuItem.Click += OnVpkEditingRemoveThisToolStripMenuItem_Click;
            // 
            // vpkEditSaveToDiskToolStripMenuItem
            // 
            vpkEditSaveToDiskToolStripMenuItem.Name = "vpkEditSaveToDiskToolStripMenuItem";
            vpkEditSaveToDiskToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            vpkEditSaveToDiskToolStripMenuItem.Text = "&Save VPK to disk";
            vpkEditSaveToDiskToolStripMenuItem.Click += OnSaveVPKToDiskToolStripMenuItem_Click;
            // 
            // MainForm
            // 
            AllowDrop = true;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(1300, 800);
            Controls.Add(mainTabs);
            Controls.Add(menuStrip);
            MainMenuStrip = menuStrip;
            Margin = new Padding(2);
            MinimumSize = new System.Drawing.Size(347, 340);
            Name = "MainForm";
            SizeGripStyle = SizeGripStyle.Show;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Source 2 Viewer";
            Load += MainForm_Load;
            Shown += MainForm_Shown;
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
        private CustomTabControl mainTabs;
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
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem openToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator2;
        private ToolStripMenuItem createVpkFromFolderToolStripMenuItem;
        private ToolStripMenuItem verifyPackageContentsToolStripMenuItem;
        private ToolStripMenuItem registerVpkFileAssociationToolStripMenuItem;
        private ToolStripMenuItem newVersionAvailableToolStripMenuItem;
        private ToolStripMenuItem checkForUpdatesToolStripMenuItem;
        private ToolStripMenuItem clearConsoleToolStripMenuItem;
        private ToolStripMenuItem vpkEditAddExistingFolderToolStripMenuItem;
        private ToolStripMenuItem vpkEditSaveToDiskToolStripMenuItem;
        private ToolStripMenuItem vpkEditAddExistingFilesToolStripMenuItem;
        private ToolStripMenuItem vpkEditCreateFolderToolStripMenuItem;
        private ToolStripMenuItem vpkEditRemoveThisFolderToolStripMenuItem;
        private ToolStripMenuItem vpkEditRemoveThisFileToolStripMenuItem;
        private ToolStripMenuItem versionLabel;
        private ToolStripMenuItem copyFileNameOnDiskToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator3;
        private ToolStripSeparator toolStripSeparator4;
        private ToolStripMenuItem openWelcomeScreenToolStripMenuItem;
        private ToolStripMenuItem themeToolStripMenuItem;
    }
}

