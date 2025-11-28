using System.Windows.Forms;
using GUI.Controls;

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
            menuStrip = new TransparentMenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            openToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator2 = new ToolStripSeparator();
            registerVpkFileAssociationToolStripMenuItem = new ToolStripMenuItem();
            createVpkFromFolderToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator4 = new ToolStripSeparator();
            openWelcomeScreenToolStripMenuItem = new ToolStripMenuItem();
            explorerToolStripMenuItem = new ToolStripMenuItem();
            findToolStripButton = new ToolStripMenuItem();
            aboutToolStripMenuItem = new ToolStripMenuItem();
            settingsToolStripMenuItem = new ToolStripMenuItem();
            versionLabel = new ToolStripMenuItem();
            newVersionAvailableToolStripMenuItem = new ToolStripMenuItem();
            checkForUpdatesToolStripMenuItem = new ToolStripMenuItem();
            recoverDeletedToolStripMenuItem = new ToolStripMenuItem();
            mainTabs = new ThemedTabControl();
            tabContextMenuStrip = new ThemedContextMenuStrip(components);
            closeToolStripMenuItem = new ToolStripMenuItem();
            closeToolStripMenuItems = new ToolStripMenuItem();
            closeToolStripMenuItemsToRight = new ToolStripMenuItem();
            closeToolStripMenuItemsToLeft = new ToolStripMenuItem();
            exportAsIsToolStripMenuItem = new ToolStripMenuItem();
            decompileExportToolStripMenuItem = new ToolStripMenuItem();
            clearConsoleToolStripMenuItem = new ToolStripMenuItem();
            vpkContextMenu = new ThemedContextMenuStrip(components);
            extractToolStripMenuItem = new ToolStripMenuItem();
            decompileToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            copyFileNameToolStripMenuItem = new ToolStripMenuItem();
            copyFileNameOnDiskToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator3 = new ToolStripSeparator();
            openWithoutViewerToolStripMenuItem = new ToolStripMenuItem();
            openWithDefaultAppToolStripMenuItem = new ToolStripMenuItem();
            viewAssetInfoToolStripMenuItem = new ToolStripMenuItem();
            verifyPackageContentsToolStripMenuItem = new ToolStripMenuItem();
            vpkEditingContextMenu = new ThemedContextMenuStrip(components);
            vpkEditCreateFolderToolStripMenuItem = new ToolStripMenuItem();
            vpkEditAddExistingFolderToolStripMenuItem = new ToolStripMenuItem();
            vpkEditAddExistingFilesToolStripMenuItem = new ToolStripMenuItem();
            vpkEditRemoveThisFolderToolStripMenuItem = new ToolStripMenuItem();
            vpkEditRemoveThisFileToolStripMenuItem = new ToolStripMenuItem();
            vpkEditSaveToDiskToolStripMenuItem = new ToolStripMenuItem();
            transparentPanel1 = new TransparentPanel();
            panel1 = new TransparentPanel();
            pictureBox1 = new PictureBox();
            controlsBoxPanel = new ControlsBoxPanel();
            mainFormBottomPanel = new MainFormBottomPanel();
            menuStrip.SuspendLayout();
            tabContextMenuStrip.SuspendLayout();
            vpkContextMenu.SuspendLayout();
            vpkEditingContextMenu.SuspendLayout();
            transparentPanel1.SuspendLayout();
            panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // menuStrip
            // 
            menuStrip.BackColor = System.Drawing.SystemColors.Window;
            menuStrip.Dock = DockStyle.Fill;
            menuStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
            menuStrip.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, explorerToolStripMenuItem, findToolStripButton, aboutToolStripMenuItem, settingsToolStripMenuItem });
            menuStrip.Location = new System.Drawing.Point(38, 0);
            menuStrip.Name = "menuStrip";
            menuStrip.Padding = new Padding(0, 6, 0, 6);
            menuStrip.RenderMode = ToolStripRenderMode.System;
            menuStrip.Size = new System.Drawing.Size(511, 48);
            menuStrip.TabIndex = 0;
            menuStrip.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { openToolStripMenuItem, toolStripSeparator2, registerVpkFileAssociationToolStripMenuItem, createVpkFromFolderToolStripMenuItem, toolStripSeparator4, openWelcomeScreenToolStripMenuItem });
            fileToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("fileToolStripMenuItem.Image");
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Padding = new Padding(4);
            fileToolStripMenuItem.Size = new System.Drawing.Size(57, 36);
            fileToolStripMenuItem.Text = "F&ile";
            // 
            // openToolStripMenuItem
            // 
            openToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("openToolStripMenuItem.Image");
            openToolStripMenuItem.Name = "openToolStripMenuItem";
            openToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.O;
            openToolStripMenuItem.Size = new System.Drawing.Size(207, 26);
            openToolStripMenuItem.Text = "&Open";
            openToolStripMenuItem.Click += OpenToolStripMenuItem_Click;
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new System.Drawing.Size(204, 6);
            // 
            // registerVpkFileAssociationToolStripMenuItem
            // 
            registerVpkFileAssociationToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("registerVpkFileAssociationToolStripMenuItem.Image");
            registerVpkFileAssociationToolStripMenuItem.Name = "registerVpkFileAssociationToolStripMenuItem";
            registerVpkFileAssociationToolStripMenuItem.Size = new System.Drawing.Size(207, 26);
            registerVpkFileAssociationToolStripMenuItem.Text = "Open VPKs with this app";
            registerVpkFileAssociationToolStripMenuItem.Click += RegisterVpkFileAssociationToolStripMenuItem_Click;
            // 
            // createVpkFromFolderToolStripMenuItem
            // 
            createVpkFromFolderToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("createVpkFromFolderToolStripMenuItem.Image");
            createVpkFromFolderToolStripMenuItem.Name = "createVpkFromFolderToolStripMenuItem";
            createVpkFromFolderToolStripMenuItem.Size = new System.Drawing.Size(207, 26);
            createVpkFromFolderToolStripMenuItem.Text = "Create VPK from folder";
            createVpkFromFolderToolStripMenuItem.Click += CreateVpkFromFolderToolStripMenuItem_Click;
            // 
            // toolStripSeparator4
            // 
            toolStripSeparator4.Name = "toolStripSeparator4";
            toolStripSeparator4.Size = new System.Drawing.Size(204, 6);
            // 
            // openWelcomeScreenToolStripMenuItem
            // 
            openWelcomeScreenToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("openWelcomeScreenToolStripMenuItem.Image");
            openWelcomeScreenToolStripMenuItem.Name = "openWelcomeScreenToolStripMenuItem";
            openWelcomeScreenToolStripMenuItem.Size = new System.Drawing.Size(207, 26);
            openWelcomeScreenToolStripMenuItem.Text = "Open welcome screen";
            openWelcomeScreenToolStripMenuItem.Click += openWelcomeScreenToolStripMenuItem_Click;
            // 
            // explorerToolStripMenuItem
            // 
            explorerToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("explorerToolStripMenuItem.Image");
            explorerToolStripMenuItem.Name = "explorerToolStripMenuItem";
            explorerToolStripMenuItem.Padding = new Padding(4);
            explorerToolStripMenuItem.Size = new System.Drawing.Size(81, 36);
            explorerToolStripMenuItem.Text = "Explorer";
            explorerToolStripMenuItem.Click += OpenExplorer_Click;
            // 
            // findToolStripButton
            // 
            findToolStripButton.Enabled = false;
            findToolStripButton.Image = (System.Drawing.Image)resources.GetObject("findToolStripButton.Image");
            findToolStripButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            findToolStripButton.Name = "findToolStripButton";
            findToolStripButton.Padding = new Padding(4);
            findToolStripButton.ShortcutKeys = Keys.Control | Keys.F;
            findToolStripButton.Size = new System.Drawing.Size(62, 36);
            findToolStripButton.Text = "&Find";
            findToolStripButton.Click += FindToolStripMenuItem_Click;
            // 
            // aboutToolStripMenuItem
            // 
            aboutToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("aboutToolStripMenuItem.Image");
            aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            aboutToolStripMenuItem.Size = new System.Drawing.Size(72, 36);
            aboutToolStripMenuItem.Text = "About";
            aboutToolStripMenuItem.Click += OnAboutItemClick;
            // 
            // settingsToolStripMenuItem
            // 
            settingsToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("settingsToolStripMenuItem.Image");
            settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            settingsToolStripMenuItem.Size = new System.Drawing.Size(81, 36);
            settingsToolStripMenuItem.Text = "Settings";
            settingsToolStripMenuItem.Click += OnSettingsItemClick;
            // 
            // versionLabel
            // 
            versionLabel.Name = "versionLabel";
            versionLabel.Size = new System.Drawing.Size(32, 19);
            // 
            // newVersionAvailableToolStripMenuItem
            // 
            newVersionAvailableToolStripMenuItem.Name = "newVersionAvailableToolStripMenuItem";
            newVersionAvailableToolStripMenuItem.Size = new System.Drawing.Size(32, 19);
            // 
            // checkForUpdatesToolStripMenuItem
            // 
            checkForUpdatesToolStripMenuItem.Name = "checkForUpdatesToolStripMenuItem";
            checkForUpdatesToolStripMenuItem.Size = new System.Drawing.Size(32, 19);
            // 
            // recoverDeletedToolStripMenuItem
            // 
            recoverDeletedToolStripMenuItem.Enabled = false;
            recoverDeletedToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("recoverDeletedToolStripMenuItem.Image");
            recoverDeletedToolStripMenuItem.ImageTransparentColor = System.Drawing.Color.Magenta;
            recoverDeletedToolStripMenuItem.Name = "recoverDeletedToolStripMenuItem";
            recoverDeletedToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            recoverDeletedToolStripMenuItem.Text = "Recover deleted files";
            recoverDeletedToolStripMenuItem.Click += RecoverDeletedToolStripMenuItem_Click;
            // 
            // mainTabs
            // 
            mainTabs.Appearance = TabAppearance.Buttons;
            mainTabs.BaseTabWidth = 150;
            mainTabs.BorderColor = System.Drawing.Color.FromArgb(230, 230, 230);
            mainTabs.Dock = DockStyle.Fill;
            mainTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            mainTabs.HoverColor = System.Drawing.Color.FromArgb(99, 161, 255);
            mainTabs.ItemSize = new System.Drawing.Size(150, 25);
            mainTabs.LineColor = System.Drawing.Color.FromArgb(99, 161, 255);
            mainTabs.Location = new System.Drawing.Point(0, 52);
            mainTabs.Margin = new Padding(0);
            mainTabs.Name = "mainTabs";
            mainTabs.Padding = new System.Drawing.Point(0, 0);
            mainTabs.SelectedForeColor = System.Drawing.Color.Black;
            mainTabs.SelectedIndex = 0;
            mainTabs.SelectTabColor = System.Drawing.Color.FromArgb(244, 244, 244);
            mainTabs.Size = new System.Drawing.Size(749, 343);
            mainTabs.SizeMode = TabSizeMode.Fixed;
            mainTabs.TabColor = System.Drawing.Color.FromArgb(244, 244, 244);
            mainTabs.TabHeight = 25;
            mainTabs.TabIndex = 1;
            mainTabs.MouseClick += OnTabClick;
            // 
            // tabContextMenuStrip
            // 
            tabContextMenuStrip.BackColor = System.Drawing.Color.FromArgb(28, 31, 38);
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
            vpkContextMenu.BackColor = System.Drawing.Color.FromArgb(28, 31, 38);
            vpkContextMenu.ImageScalingSize = new System.Drawing.Size(24, 24);
            vpkContextMenu.Items.AddRange(new ToolStripItem[] { extractToolStripMenuItem, decompileToolStripMenuItem, toolStripSeparator1, copyFileNameToolStripMenuItem, copyFileNameOnDiskToolStripMenuItem, toolStripSeparator3, openWithoutViewerToolStripMenuItem, openWithDefaultAppToolStripMenuItem, viewAssetInfoToolStripMenuItem, verifyPackageContentsToolStripMenuItem, recoverDeletedToolStripMenuItem });
            vpkContextMenu.Name = "vpkContextMenu";
            vpkContextMenu.Size = new System.Drawing.Size(217, 286);
            // 
            // extractToolStripMenuItem
            // 
            extractToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("extractToolStripMenuItem.Image");
            extractToolStripMenuItem.Name = "extractToolStripMenuItem";
            extractToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            extractToolStripMenuItem.Text = "Export as is";
            extractToolStripMenuItem.Click += ExtractToolStripMenuItem_Click;
            // 
            // decompileToolStripMenuItem
            // 
            decompileToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("decompileToolStripMenuItem.Image");
            decompileToolStripMenuItem.Name = "decompileToolStripMenuItem";
            decompileToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            decompileToolStripMenuItem.Text = "Decompile && export";
            decompileToolStripMenuItem.Click += DecompileToolStripMenuItem_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new System.Drawing.Size(213, 6);
            // 
            // copyFileNameToolStripMenuItem
            // 
            copyFileNameToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("copyFileNameToolStripMenuItem.Image");
            copyFileNameToolStripMenuItem.Name = "copyFileNameToolStripMenuItem";
            copyFileNameToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            copyFileNameToolStripMenuItem.Text = "Copy file path in package";
            copyFileNameToolStripMenuItem.Click += CopyFileNameToolStripMenuItem_Click;
            // 
            // copyFileNameOnDiskToolStripMenuItem
            // 
            copyFileNameOnDiskToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("copyFileNameOnDiskToolStripMenuItem.Image");
            copyFileNameOnDiskToolStripMenuItem.Name = "copyFileNameOnDiskToolStripMenuItem";
            copyFileNameOnDiskToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            copyFileNameOnDiskToolStripMenuItem.Text = "Copy file path on disk";
            copyFileNameOnDiskToolStripMenuItem.Click += CopyFileNameOnDiskToolStripMenuItem_Click;
            // 
            // toolStripSeparator3
            // 
            toolStripSeparator3.Name = "toolStripSeparator3";
            toolStripSeparator3.Size = new System.Drawing.Size(213, 6);
            // 
            // openWithoutViewerToolStripMenuItem
            // 
            openWithoutViewerToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("openWithoutViewerToolStripMenuItem.Image");
            openWithoutViewerToolStripMenuItem.Name = "openWithoutViewerToolStripMenuItem";
            openWithoutViewerToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            openWithoutViewerToolStripMenuItem.Text = "Open without viewer";
            openWithoutViewerToolStripMenuItem.Click += OpenWithoutViewerToolStripMenuItem_Click;
            // 
            // openWithDefaultAppToolStripMenuItem
            // 
            openWithDefaultAppToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("openWithDefaultAppToolStripMenuItem.Image");
            openWithDefaultAppToolStripMenuItem.Name = "openWithDefaultAppToolStripMenuItem";
            openWithDefaultAppToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            openWithDefaultAppToolStripMenuItem.Text = "Open with default app";
            openWithDefaultAppToolStripMenuItem.Click += OpenWithDefaultAppToolStripMenuItem_Click;
            // 
            // viewAssetInfoToolStripMenuItem
            // 
            viewAssetInfoToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("viewAssetInfoToolStripMenuItem.Image");
            viewAssetInfoToolStripMenuItem.Name = "viewAssetInfoToolStripMenuItem";
            viewAssetInfoToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            viewAssetInfoToolStripMenuItem.Text = "View asset info";
            viewAssetInfoToolStripMenuItem.Click += OnViewAssetInfoToolStripMenuItemClick;
            // 
            // verifyPackageContentsToolStripMenuItem
            // 
            verifyPackageContentsToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("verifyPackageContentsToolStripMenuItem.Image");
            verifyPackageContentsToolStripMenuItem.Name = "verifyPackageContentsToolStripMenuItem";
            verifyPackageContentsToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            verifyPackageContentsToolStripMenuItem.Text = "Verify package contents";
            verifyPackageContentsToolStripMenuItem.Click += VerifyPackageContentsToolStripMenuItem_Click;
            // 
            // vpkEditingContextMenu
            // 
            vpkEditingContextMenu.BackColor = System.Drawing.Color.FromArgb(28, 31, 38);
            vpkEditingContextMenu.ImageScalingSize = new System.Drawing.Size(24, 24);
            vpkEditingContextMenu.ImeMode = ImeMode.Off;
            vpkEditingContextMenu.Items.AddRange(new ToolStripItem[] { vpkEditCreateFolderToolStripMenuItem, vpkEditAddExistingFolderToolStripMenuItem, vpkEditAddExistingFilesToolStripMenuItem, vpkEditRemoveThisFolderToolStripMenuItem, vpkEditRemoveThisFileToolStripMenuItem, vpkEditSaveToDiskToolStripMenuItem });
            vpkEditingContextMenu.Name = "vpkEditingContextMenu";
            vpkEditingContextMenu.Size = new System.Drawing.Size(182, 184);
            // 
            // vpkEditCreateFolderToolStripMenuItem
            // 
            vpkEditCreateFolderToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("vpkEditCreateFolderToolStripMenuItem.Image");
            vpkEditCreateFolderToolStripMenuItem.Name = "vpkEditCreateFolderToolStripMenuItem";
            vpkEditCreateFolderToolStripMenuItem.Size = new System.Drawing.Size(181, 30);
            vpkEditCreateFolderToolStripMenuItem.Text = "Create folder";
            vpkEditCreateFolderToolStripMenuItem.Click += OnVpkCreateFolderToolStripMenuItem_Click;
            // 
            // vpkEditAddExistingFolderToolStripMenuItem
            // 
            vpkEditAddExistingFolderToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("vpkEditAddExistingFolderToolStripMenuItem.Image");
            vpkEditAddExistingFolderToolStripMenuItem.Name = "vpkEditAddExistingFolderToolStripMenuItem";
            vpkEditAddExistingFolderToolStripMenuItem.Size = new System.Drawing.Size(181, 30);
            vpkEditAddExistingFolderToolStripMenuItem.Text = "&Add existing folder";
            vpkEditAddExistingFolderToolStripMenuItem.Click += OnVpkAddNewFolderToolStripMenuItem_Click;
            // 
            // vpkEditAddExistingFilesToolStripMenuItem
            // 
            vpkEditAddExistingFilesToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("vpkEditAddExistingFilesToolStripMenuItem.Image");
            vpkEditAddExistingFilesToolStripMenuItem.Name = "vpkEditAddExistingFilesToolStripMenuItem";
            vpkEditAddExistingFilesToolStripMenuItem.Size = new System.Drawing.Size(181, 30);
            vpkEditAddExistingFilesToolStripMenuItem.Text = "Add existing &files";
            vpkEditAddExistingFilesToolStripMenuItem.Click += OnVpkAddNewFileToolStripMenuItem_Click;
            // 
            // vpkEditRemoveThisFolderToolStripMenuItem
            // 
            vpkEditRemoveThisFolderToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("vpkEditRemoveThisFolderToolStripMenuItem.Image");
            vpkEditRemoveThisFolderToolStripMenuItem.Name = "vpkEditRemoveThisFolderToolStripMenuItem";
            vpkEditRemoveThisFolderToolStripMenuItem.Size = new System.Drawing.Size(181, 30);
            vpkEditRemoveThisFolderToolStripMenuItem.Text = "&Remove this folder";
            vpkEditRemoveThisFolderToolStripMenuItem.Click += OnVpkEditingRemoveThisToolStripMenuItem_Click;
            // 
            // vpkEditRemoveThisFileToolStripMenuItem
            // 
            vpkEditRemoveThisFileToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("vpkEditRemoveThisFileToolStripMenuItem.Image");
            vpkEditRemoveThisFileToolStripMenuItem.Name = "vpkEditRemoveThisFileToolStripMenuItem";
            vpkEditRemoveThisFileToolStripMenuItem.Size = new System.Drawing.Size(181, 30);
            vpkEditRemoveThisFileToolStripMenuItem.Text = "&Remove this file";
            vpkEditRemoveThisFileToolStripMenuItem.Click += OnVpkEditingRemoveThisToolStripMenuItem_Click;
            // 
            // vpkEditSaveToDiskToolStripMenuItem
            // 
            vpkEditSaveToDiskToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("vpkEditSaveToDiskToolStripMenuItem.Image");
            vpkEditSaveToDiskToolStripMenuItem.Name = "vpkEditSaveToDiskToolStripMenuItem";
            vpkEditSaveToDiskToolStripMenuItem.Size = new System.Drawing.Size(181, 30);
            vpkEditSaveToDiskToolStripMenuItem.Text = "&Save VPK to disk";
            vpkEditSaveToDiskToolStripMenuItem.Click += OnSaveVPKToDiskToolStripMenuItem_Click;
            // 
            // transparentPanel1
            // 
            transparentPanel1.Controls.Add(menuStrip);
            transparentPanel1.Controls.Add(panel1);
            transparentPanel1.Controls.Add(controlsBoxPanel);
            transparentPanel1.Dock = DockStyle.Top;
            transparentPanel1.Location = new System.Drawing.Point(0, 0);
            transparentPanel1.Name = "transparentPanel1";
            transparentPanel1.Padding = new Padding(0, 0, 0, 4);
            transparentPanel1.Size = new System.Drawing.Size(749, 52);
            transparentPanel1.TabIndex = 3;
            // 
            // panel1
            // 
            panel1.Controls.Add(pictureBox1);
            panel1.Dock = DockStyle.Left;
            panel1.Location = new System.Drawing.Point(0, 0);
            panel1.Name = "panel1";
            panel1.Padding = new Padding(4);
            panel1.Size = new System.Drawing.Size(38, 48);
            panel1.TabIndex = 3;
            // 
            // pictureBox1
            // 
            pictureBox1.BackgroundImageLayout = ImageLayout.Center;
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.Image = (System.Drawing.Image)resources.GetObject("pictureBox1.Image");
            pictureBox1.Location = new System.Drawing.Point(4, 4);
            pictureBox1.Margin = new Padding(0);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new System.Drawing.Size(30, 40);
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.TabIndex = 2;
            pictureBox1.TabStop = false;
            // 
            // controlsBoxPanel
            // 
            controlsBoxPanel.ControlBoxHoverCloseColor = System.Drawing.Color.Red;
            controlsBoxPanel.ControlBoxHoverColor = System.Drawing.Color.DimGray;
            controlsBoxPanel.ControlBoxIconColor = System.Drawing.Color.Black;
            controlsBoxPanel.CurrentHoveredButton = ControlsBoxPanel.CustomTitleBarHoveredButton.None;
            controlsBoxPanel.Dock = DockStyle.Right;
            controlsBoxPanel.Location = new System.Drawing.Point(549, 0);
            controlsBoxPanel.Name = "controlsBoxPanel";
            controlsBoxPanel.Size = new System.Drawing.Size(200, 48);
            controlsBoxPanel.TabIndex = 1;
            // 
            // mainFormBottomPanel
            // 
            mainFormBottomPanel.Dock = DockStyle.Bottom;
            mainFormBottomPanel.Location = new System.Drawing.Point(0, 395);
            mainFormBottomPanel.Name = "mainFormBottomPanel";
            mainFormBottomPanel.Size = new System.Drawing.Size(749, 30);
            mainFormBottomPanel.TabIndex = 4;
            // 
            // MainForm
            // 
            AllowDrop = true;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(749, 425);
            Controls.Add(mainTabs);
            Controls.Add(mainFormBottomPanel);
            Controls.Add(transparentPanel1);
            Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            MainMenuStrip = menuStrip;
            Margin = new Padding(2);
            MinimumSize = new System.Drawing.Size(347, 380);
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
            transparentPanel1.ResumeLayout(false);
            transparentPanel1.PerformLayout();
            panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private TransparentMenuStrip menuStrip;
        private ToolStripMenuItem closeToolStripMenuItem;
        private ToolStripMenuItem extractToolStripMenuItem;
        private ToolStripMenuItem copyFileNameToolStripMenuItem;
        private ToolStripMenuItem closeToolStripMenuItemsToLeft;
        private ToolStripMenuItem closeToolStripMenuItemsToRight;
        private ToolStripMenuItem closeToolStripMenuItems;
        private ToolStripMenuItem findToolStripButton;
        private ToolStripMenuItem openWithoutViewerToolStripMenuItem;
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
        private ThemedTabControl mainTabs;
        private Controls.TransparentPanel transparentPanel1;
        private Controls.ControlsBoxPanel controlsBoxPanel;
        private PictureBox pictureBox1;
        private TransparentPanel panel1;
        private ThemedContextMenuStrip tabContextMenuStrip;
        private ThemedContextMenuStrip vpkContextMenu;
        private ThemedContextMenuStrip vpkEditingContextMenu;
        private MainFormBottomPanel mainFormBottomPanel;
    }
}

