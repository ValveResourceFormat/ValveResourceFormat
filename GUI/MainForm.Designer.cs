using System.Windows.Forms;
using DarkModeForms;
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
            explorerToolStripMenuItem = new ToolStripMenuItem();
            findToolStripButton = new ToolStripMenuItem();
            aboutToolStripMenuItem = new ToolStripMenuItem();
            settingsToolStripMenuItem = new ToolStripMenuItem();
            recoverDeletedToolStripMenuItem = new ToolStripMenuItem();
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
            mainTabs = new FlatTabControl();
            topNavBarPanel = new TransparentPanel();
            logoButton = new SysMenuLogoButton();
            controlsBoxPanel = new ControlsBoxPanel();
            AppTitleTextLabel = new Label();
            bottomPanel = new MainformBottomPanel();
            menuStripBottom = new MenuStrip();
            versionLabel = new ToolStripMenuItem();
            checkForUpdatesToolStripMenuItem = new ToolStripMenuItem();
            newVersionAvailableToolStripMenuItem = new ToolStripMenuItem();
            menuStrip.SuspendLayout();
            tabContextMenuStrip.SuspendLayout();
            vpkContextMenu.SuspendLayout();
            vpkEditingContextMenu.SuspendLayout();
            topNavBarPanel.SuspendLayout();
            bottomPanel.SuspendLayout();
            menuStripBottom.SuspendLayout();
            SuspendLayout();
            // 
            // menuStrip
            // 
            menuStrip.BackColor = System.Drawing.SystemColors.Window;
            menuStrip.Dock = DockStyle.Fill;
            menuStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
            menuStrip.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, explorerToolStripMenuItem, findToolStripButton, aboutToolStripMenuItem, settingsToolStripMenuItem });
            menuStrip.Location = new System.Drawing.Point(43, 0);
            menuStrip.Name = "menuStrip";
            menuStrip.Padding = new Padding(0);
            menuStrip.RenderMode = ToolStripRenderMode.System;
            menuStrip.Size = new System.Drawing.Size(677, 32);
            menuStrip.TabIndex = 0;
            menuStrip.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { openToolStripMenuItem, toolStripSeparator2, registerVpkFileAssociationToolStripMenuItem, createVpkFromFolderToolStripMenuItem });
            fileToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("fileToolStripMenuItem.Image");
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Padding = new Padding(4);
            fileToolStripMenuItem.Size = new System.Drawing.Size(57, 32);
            fileToolStripMenuItem.Text = "F&ile";
            // 
            // openToolStripMenuItem
            // 
            openToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("openToolStripMenuItem.Image");
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
            registerVpkFileAssociationToolStripMenuItem.Name = "registerVpkFileAssociationToolStripMenuItem";
            registerVpkFileAssociationToolStripMenuItem.Size = new System.Drawing.Size(203, 22);
            registerVpkFileAssociationToolStripMenuItem.Text = "Open VPKs with this app";
            registerVpkFileAssociationToolStripMenuItem.Click += RegisterVpkFileAssociationToolStripMenuItem_Click;
            // 
            // createVpkFromFolderToolStripMenuItem
            // 
            createVpkFromFolderToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("createVpkFromFolderToolStripMenuItem.Image");
            createVpkFromFolderToolStripMenuItem.Name = "createVpkFromFolderToolStripMenuItem";
            createVpkFromFolderToolStripMenuItem.Size = new System.Drawing.Size(203, 22);
            createVpkFromFolderToolStripMenuItem.Text = "Create VPK from folder";
            createVpkFromFolderToolStripMenuItem.Click += CreateVpkFromFolderToolStripMenuItem_Click;
            // 
            // explorerToolStripMenuItem
            // 
            explorerToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("explorerToolStripMenuItem.Image");
            explorerToolStripMenuItem.Name = "explorerToolStripMenuItem";
            explorerToolStripMenuItem.Padding = new Padding(4);
            explorerToolStripMenuItem.Size = new System.Drawing.Size(82, 32);
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
            findToolStripButton.Size = new System.Drawing.Size(62, 32);
            findToolStripButton.Text = "&Find";
            findToolStripButton.Click += FindToolStripMenuItem_Click;
            // 
            // aboutToolStripMenuItem
            // 
            aboutToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("aboutToolStripMenuItem.Image");
            aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            aboutToolStripMenuItem.Size = new System.Drawing.Size(72, 32);
            aboutToolStripMenuItem.Text = "About";
            aboutToolStripMenuItem.Click += OnAboutItemClick;
            // 
            // settingsToolStripMenuItem
            // 
            settingsToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("settingsToolStripMenuItem.Image");
            settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            settingsToolStripMenuItem.Size = new System.Drawing.Size(81, 32);
            settingsToolStripMenuItem.Text = "Settings";
            settingsToolStripMenuItem.Click += OnSettingsItemClick;
            // 
            // recoverDeletedToolStripMenuItem
            // 
            recoverDeletedToolStripMenuItem.Enabled = false;
            recoverDeletedToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("recoverDeletedToolStripMenuItem.Image");
            recoverDeletedToolStripMenuItem.ImageTransparentColor = System.Drawing.Color.Magenta;
            recoverDeletedToolStripMenuItem.Name = "recoverDeletedToolStripMenuItem";
            recoverDeletedToolStripMenuItem.Size = new System.Drawing.Size(212, 26);
            recoverDeletedToolStripMenuItem.Text = "Recover deleted files";
            recoverDeletedToolStripMenuItem.Click += RecoverDeletedToolStripMenuItem_Click;
            // 
            // tabContextMenuStrip
            // 
            tabContextMenuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            tabContextMenuStrip.Items.AddRange(new ToolStripItem[] { closeToolStripMenuItem, closeToolStripMenuItems, closeToolStripMenuItemsToRight, closeToolStripMenuItemsToLeft, exportAsIsToolStripMenuItem, decompileExportToolStripMenuItem, clearConsoleToolStripMenuItem });
            tabContextMenuStrip.LayoutStyle = ToolStripLayoutStyle.Table;
            tabContextMenuStrip.Name = "contextMenuStrip1";
            tabContextMenuStrip.Size = new System.Drawing.Size(234, 236);
            tabContextMenuStrip.Opening += tabContextMenuStrip_Opening;
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
            vpkContextMenu.ImageScalingSize = new System.Drawing.Size(20, 20);
            vpkContextMenu.Items.AddRange(new ToolStripItem[] { extractToolStripMenuItem, decompileToolStripMenuItem, toolStripSeparator1, copyFileNameToolStripMenuItem, copyFileNameOnDiskToolStripMenuItem, toolStripSeparator3, openWithDefaultAppToolStripMenuItem, viewAssetInfoToolStripMenuItem, verifyPackageContentsToolStripMenuItem, recoverDeletedToolStripMenuItem });
            vpkContextMenu.Name = "vpkContextMenu";
            vpkContextMenu.Size = new System.Drawing.Size(213, 224);
            vpkContextMenu.Opening += vpkContextMenu_Opening;
            // 
            // extractToolStripMenuItem
            // 
            extractToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("extractToolStripMenuItem.Image");
            extractToolStripMenuItem.Name = "extractToolStripMenuItem";
            extractToolStripMenuItem.Size = new System.Drawing.Size(212, 26);
            extractToolStripMenuItem.Text = "Export as is";
            extractToolStripMenuItem.Click += ExtractToolStripMenuItem_Click;
            // 
            // decompileToolStripMenuItem
            // 
            decompileToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("decompileToolStripMenuItem.Image");
            decompileToolStripMenuItem.Name = "decompileToolStripMenuItem";
            decompileToolStripMenuItem.Size = new System.Drawing.Size(212, 26);
            decompileToolStripMenuItem.Text = "Decompile && export";
            decompileToolStripMenuItem.Click += DecompileToolStripMenuItem_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new System.Drawing.Size(209, 6);
            // 
            // copyFileNameToolStripMenuItem
            // 
            copyFileNameToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("copyFileNameToolStripMenuItem.Image");
            copyFileNameToolStripMenuItem.Name = "copyFileNameToolStripMenuItem";
            copyFileNameToolStripMenuItem.Size = new System.Drawing.Size(212, 26);
            copyFileNameToolStripMenuItem.Text = "Copy file path in package";
            copyFileNameToolStripMenuItem.Click += CopyFileNameToolStripMenuItem_Click;
            // 
            // copyFileNameOnDiskToolStripMenuItem
            // 
            copyFileNameOnDiskToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("copyFileNameOnDiskToolStripMenuItem.Image");
            copyFileNameOnDiskToolStripMenuItem.Name = "copyFileNameOnDiskToolStripMenuItem";
            copyFileNameOnDiskToolStripMenuItem.Size = new System.Drawing.Size(212, 26);
            copyFileNameOnDiskToolStripMenuItem.Text = "Copy file path on disk";
            copyFileNameOnDiskToolStripMenuItem.Click += CopyFileNameOnDiskToolStripMenuItem_Click;
            // 
            // toolStripSeparator3
            // 
            toolStripSeparator3.Name = "toolStripSeparator3";
            toolStripSeparator3.Size = new System.Drawing.Size(209, 6);
            // 
            // openWithDefaultAppToolStripMenuItem
            // 
            openWithDefaultAppToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("openWithDefaultAppToolStripMenuItem.Image");
            openWithDefaultAppToolStripMenuItem.Name = "openWithDefaultAppToolStripMenuItem";
            openWithDefaultAppToolStripMenuItem.Size = new System.Drawing.Size(212, 26);
            openWithDefaultAppToolStripMenuItem.Text = "Open with default app";
            openWithDefaultAppToolStripMenuItem.Click += OpenWithDefaultAppToolStripMenuItem_Click;
            // 
            // viewAssetInfoToolStripMenuItem
            // 
            viewAssetInfoToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("viewAssetInfoToolStripMenuItem.Image");
            viewAssetInfoToolStripMenuItem.Name = "viewAssetInfoToolStripMenuItem";
            viewAssetInfoToolStripMenuItem.Size = new System.Drawing.Size(212, 26);
            viewAssetInfoToolStripMenuItem.Text = "View asset info";
            viewAssetInfoToolStripMenuItem.Click += OnViewAssetInfoToolStripMenuItemClick;
            // 
            // verifyPackageContentsToolStripMenuItem
            // 
            verifyPackageContentsToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("verifyPackageContentsToolStripMenuItem.Image");
            verifyPackageContentsToolStripMenuItem.Name = "verifyPackageContentsToolStripMenuItem";
            verifyPackageContentsToolStripMenuItem.Size = new System.Drawing.Size(212, 26);
            verifyPackageContentsToolStripMenuItem.Text = "Verify package contents";
            verifyPackageContentsToolStripMenuItem.Click += VerifyPackageContentsToolStripMenuItem_Click;
            // 
            // vpkEditingContextMenu
            // 
            vpkEditingContextMenu.Items.AddRange(new ToolStripItem[] { vpkEditCreateFolderToolStripMenuItem, vpkEditAddExistingFolderToolStripMenuItem, vpkEditAddExistingFilesToolStripMenuItem, vpkEditRemoveThisFolderToolStripMenuItem, vpkEditRemoveThisFileToolStripMenuItem, vpkEditSaveToDiskToolStripMenuItem });
            vpkEditingContextMenu.Name = "vpkEditingContextMenu";
            vpkEditingContextMenu.Size = new System.Drawing.Size(175, 136);
            vpkEditingContextMenu.Opening += vpkEditingContextMenu_Opening;
            // 
            // vpkEditCreateFolderToolStripMenuItem
            // 
            vpkEditCreateFolderToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("vpkEditCreateFolderToolStripMenuItem.Image");
            vpkEditCreateFolderToolStripMenuItem.Name = "vpkEditCreateFolderToolStripMenuItem";
            vpkEditCreateFolderToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            vpkEditCreateFolderToolStripMenuItem.Text = "Create folder";
            vpkEditCreateFolderToolStripMenuItem.Click += OnVpkCreateFolderToolStripMenuItem_Click;
            // 
            // vpkEditAddExistingFolderToolStripMenuItem
            // 
            vpkEditAddExistingFolderToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("vpkEditAddExistingFolderToolStripMenuItem.Image");
            vpkEditAddExistingFolderToolStripMenuItem.Name = "vpkEditAddExistingFolderToolStripMenuItem";
            vpkEditAddExistingFolderToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            vpkEditAddExistingFolderToolStripMenuItem.Text = "&Add existing folder";
            vpkEditAddExistingFolderToolStripMenuItem.Click += OnVpkAddNewFolderToolStripMenuItem_Click;
            // 
            // vpkEditAddExistingFilesToolStripMenuItem
            // 
            vpkEditAddExistingFilesToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("vpkEditAddExistingFilesToolStripMenuItem.Image");
            vpkEditAddExistingFilesToolStripMenuItem.Name = "vpkEditAddExistingFilesToolStripMenuItem";
            vpkEditAddExistingFilesToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            vpkEditAddExistingFilesToolStripMenuItem.Text = "Add existing &files";
            vpkEditAddExistingFilesToolStripMenuItem.Click += OnVpkAddNewFileToolStripMenuItem_Click;
            // 
            // vpkEditRemoveThisFolderToolStripMenuItem
            // 
            vpkEditRemoveThisFolderToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("vpkEditRemoveThisFolderToolStripMenuItem.Image");
            vpkEditRemoveThisFolderToolStripMenuItem.Name = "vpkEditRemoveThisFolderToolStripMenuItem";
            vpkEditRemoveThisFolderToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            vpkEditRemoveThisFolderToolStripMenuItem.Text = "&Remove this folder";
            vpkEditRemoveThisFolderToolStripMenuItem.Click += OnVpkEditingRemoveThisToolStripMenuItem_Click;
            // 
            // vpkEditRemoveThisFileToolStripMenuItem
            // 
            vpkEditRemoveThisFileToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("vpkEditRemoveThisFileToolStripMenuItem.Image");
            vpkEditRemoveThisFileToolStripMenuItem.Name = "vpkEditRemoveThisFileToolStripMenuItem";
            vpkEditRemoveThisFileToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            vpkEditRemoveThisFileToolStripMenuItem.Text = "&Remove this file";
            vpkEditRemoveThisFileToolStripMenuItem.Click += OnVpkEditingRemoveThisToolStripMenuItem_Click;
            // 
            // vpkEditSaveToDiskToolStripMenuItem
            // 
            vpkEditSaveToDiskToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("vpkEditSaveToDiskToolStripMenuItem.Image");
            vpkEditSaveToDiskToolStripMenuItem.Name = "vpkEditSaveToDiskToolStripMenuItem";
            vpkEditSaveToDiskToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            vpkEditSaveToDiskToolStripMenuItem.Text = "&Save VPK to disk";
            vpkEditSaveToDiskToolStripMenuItem.Click += OnSaveVPKToDiskToolStripMenuItem_Click;
            // 
            // mainTabs
            // 
            mainTabs.Appearance = TabAppearance.Buttons;
            mainTabs.BorderColor = System.Drawing.SystemColors.GrayText;
            mainTabs.Dock = DockStyle.Fill;
            mainTabs.Font = new System.Drawing.Font("Segoe UI", 9F);
            mainTabs.HoverColor = System.Drawing.SystemColors.Highlight;
            mainTabs.LineColor = System.Drawing.Color.FromArgb(136, 54, 82, 113);
            mainTabs.Location = new System.Drawing.Point(0, 40);
            mainTabs.Margin = new Padding(0);
            mainTabs.Name = "mainTabs";
            mainTabs.Padding = new System.Drawing.Point(0, 0);
            mainTabs.SelectedForeColor = System.Drawing.SystemColors.ControlText;
            mainTabs.SelectedIndex = 0;
            mainTabs.SelectTabColor = System.Drawing.SystemColors.ControlLightLight;
            mainTabs.Size = new System.Drawing.Size(870, 342);
            mainTabs.TabColor = System.Drawing.SystemColors.ButtonFace;
            mainTabs.TabIndex = 1;
            mainTabs.SelectedIndexChanged += OnMainSelectedTabChanged;
            mainTabs.MouseClick += OnTabClick;
            // 
            // topNavBarPanel
            // 
            topNavBarPanel.Controls.Add(menuStrip);
            topNavBarPanel.Controls.Add(logoButton);
            topNavBarPanel.Controls.Add(controlsBoxPanel);
            topNavBarPanel.Dock = DockStyle.Top;
            topNavBarPanel.Location = new System.Drawing.Point(0, 0);
            topNavBarPanel.Margin = new Padding(0);
            topNavBarPanel.Name = "topNavBarPanel";
            topNavBarPanel.Padding = new Padding(0, 0, 0, 8);
            topNavBarPanel.Size = new System.Drawing.Size(870, 40);
            topNavBarPanel.TabIndex = 3;
            // 
            // logoButton
            // 
            logoButton.BackColor = System.Drawing.Color.Transparent;
            logoButton.BackgroundImageLayout = ImageLayout.None;
            logoButton.Dock = DockStyle.Left;
            logoButton.FlatAppearance.BorderSize = 0;
            logoButton.FlatStyle = FlatStyle.Flat;
            logoButton.ForeColor = System.Drawing.Color.Transparent;
            logoButton.ImageIndex = 0;
            logoButton.Location = new System.Drawing.Point(0, 0);
            logoButton.Margin = new Padding(0);
            logoButton.Name = "logoButton";
            logoButton.Padding = new Padding(4);
            logoButton.Size = new System.Drawing.Size(43, 32);
            logoButton.TabIndex = 13;
            logoButton.UseVisualStyleBackColor = false;
            logoButton.Click += logoButton_Click;
            // 
            // controlsBoxPanel
            // 
            controlsBoxPanel.BackColor = System.Drawing.Color.Transparent;
            controlsBoxPanel.CurrentHoveredButton = ControlsBoxPanel.CustomTitleBarHoveredButton.None;
            controlsBoxPanel.Dock = DockStyle.Right;
            controlsBoxPanel.ForeColor = System.Drawing.Color.Transparent;
            controlsBoxPanel.Location = new System.Drawing.Point(720, 0);
            controlsBoxPanel.Margin = new Padding(0);
            controlsBoxPanel.Name = "controlsBoxPanel";
            controlsBoxPanel.Size = new System.Drawing.Size(150, 32);
            controlsBoxPanel.TabIndex = 1;
            // 
            // AppTitleTextLabel
            // 
            AppTitleTextLabel.AutoEllipsis = true;
            AppTitleTextLabel.Dock = DockStyle.Fill;
            AppTitleTextLabel.Location = new System.Drawing.Point(0, 2);
            AppTitleTextLabel.Name = "AppTitleTextLabel";
            AppTitleTextLabel.Padding = new Padding(2, 1, 2, 1);
            AppTitleTextLabel.Size = new System.Drawing.Size(696, 28);
            AppTitleTextLabel.TabIndex = 0;
            AppTitleTextLabel.Text = "Source2 Viewer -----";
            AppTitleTextLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // bottomPanel
            // 
            bottomPanel.Controls.Add(AppTitleTextLabel);
            bottomPanel.Controls.Add(menuStripBottom);
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Location = new System.Drawing.Point(0, 382);
            bottomPanel.Margin = new Padding(0);
            bottomPanel.Name = "bottomPanel";
            bottomPanel.Padding = new Padding(0, 2, 0, 0);
            bottomPanel.Size = new System.Drawing.Size(870, 30);
            bottomPanel.TabIndex = 4;
            // 
            // menuStripBottom
            // 
            menuStripBottom.Dock = DockStyle.Right;
            menuStripBottom.Items.AddRange(new ToolStripItem[] { versionLabel, checkForUpdatesToolStripMenuItem, newVersionAvailableToolStripMenuItem });
            menuStripBottom.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
            menuStripBottom.Location = new System.Drawing.Point(696, 2);
            menuStripBottom.Name = "menuStripBottom";
            menuStripBottom.Padding = new Padding(0);
            menuStripBottom.Size = new System.Drawing.Size(174, 28);
            menuStripBottom.TabIndex = 3;
            menuStripBottom.Text = "menuStrip1";
            // 
            // versionLabel
            // 
            versionLabel.Alignment = ToolStripItemAlignment.Right;
            versionLabel.Name = "versionLabel";
            versionLabel.Size = new System.Drawing.Size(57, 28);
            versionLabel.Text = "Version";
            versionLabel.Click += OnAboutItemClick;
            // 
            // checkForUpdatesToolStripMenuItem
            // 
            checkForUpdatesToolStripMenuItem.Alignment = ToolStripItemAlignment.Right;
            checkForUpdatesToolStripMenuItem.Name = "checkForUpdatesToolStripMenuItem";
            checkForUpdatesToolStripMenuItem.Size = new System.Drawing.Size(115, 28);
            checkForUpdatesToolStripMenuItem.Text = "Check for updates";
            checkForUpdatesToolStripMenuItem.Click += CheckForUpdatesToolStripMenuItem_Click;
            // 
            // newVersionAvailableToolStripMenuItem
            // 
            newVersionAvailableToolStripMenuItem.Alignment = ToolStripItemAlignment.Right;
            newVersionAvailableToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("newVersionAvailableToolStripMenuItem.Image");
            newVersionAvailableToolStripMenuItem.Name = "newVersionAvailableToolStripMenuItem";
            newVersionAvailableToolStripMenuItem.Size = new System.Drawing.Size(149, 28);
            newVersionAvailableToolStripMenuItem.Text = "New version available";
            newVersionAvailableToolStripMenuItem.Visible = false;
            newVersionAvailableToolStripMenuItem.Click += NewVersionAvailableToolStripMenuItem_Click;
            // 
            // MainForm
            // 
            AllowDrop = true;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(870, 412);
            Controls.Add(mainTabs);
            Controls.Add(bottomPanel);
            Controls.Add(topNavBarPanel);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            MainMenuStrip = menuStrip;
            Margin = new Padding(2);
            MinimumSize = new System.Drawing.Size(347, 340);
            Name = "MainForm";
            SizeGripStyle = SizeGripStyle.Hide;
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
            topNavBarPanel.ResumeLayout(false);
            topNavBarPanel.PerformLayout();
            bottomPanel.ResumeLayout(false);
            bottomPanel.PerformLayout();
            menuStripBottom.ResumeLayout(false);
            menuStripBottom.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TransparentMenuStrip menuStrip;
        private FlatTabControl mainTabs;
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
        private ToolStripMenuItem clearConsoleToolStripMenuItem;
        private ToolStripMenuItem vpkEditAddExistingFolderToolStripMenuItem;
        private ToolStripMenuItem vpkEditSaveToDiskToolStripMenuItem;
        private ToolStripMenuItem vpkEditAddExistingFilesToolStripMenuItem;
        private ToolStripMenuItem vpkEditCreateFolderToolStripMenuItem;
        private ToolStripMenuItem vpkEditRemoveThisFolderToolStripMenuItem;
        private ToolStripMenuItem vpkEditRemoveThisFileToolStripMenuItem;
        private ToolStripMenuItem copyFileNameOnDiskToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator3;
        private TransparentPanel topNavBarPanel;
        private ControlsBoxPanel controlsBoxPanel;
        private SysMenuLogoButton logoButton;
        private Label AppTitleTextLabel;
        private MainformBottomPanel bottomPanel;
        private MenuStrip menuStripBottom;
        private ToolStripMenuItem versionLabel;
        private ToolStripMenuItem checkForUpdatesToolStripMenuItem;
        private ToolStripMenuItem newVersionAvailableToolStripMenuItem;
    }
}

