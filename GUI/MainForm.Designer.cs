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
            fileToolStripMenuItem = new ThemedToolStripMenuItem();
            openToolStripMenuItem = new ThemedToolStripMenuItem();
            toolStripSeparator2 = new ToolStripSeparator();
            registerVpkFileAssociationToolStripMenuItem = new ThemedToolStripMenuItem();
            createVpkFromFolderToolStripMenuItem = new ThemedToolStripMenuItem();
            toolStripSeparator4 = new ToolStripSeparator();
            openWelcomeScreenToolStripMenuItem = new ThemedToolStripMenuItem();
            validateShadersToolStripMenuItem = new ThemedToolStripMenuItem();
            explorerToolStripMenuItem = new ThemedToolStripMenuItem();
            findToolStripButton = new ThemedToolStripMenuItem();
            settingsToolStripMenuItem = new ThemedToolStripMenuItem();
            aboutToolStripMenuItem = new ThemedToolStripMenuItem();
            recoverDeletedToolStripMenuItem = new ThemedToolStripMenuItem();
            mainTabs = new ThemedTabControl();
            tabContextMenuStrip = new ThemedContextMenuStrip(components);
            closeToolStripMenuItem = new ThemedToolStripMenuItem();
            closeToolStripMenuItems = new ThemedToolStripMenuItem();
            closeToolStripMenuItemsToRight = new ThemedToolStripMenuItem();
            closeToolStripMenuItemsToLeft = new ThemedToolStripMenuItem();
            exportAsIsToolStripMenuItem = new ThemedToolStripMenuItem();
            decompileExportToolStripMenuItem = new ThemedToolStripMenuItem();
            clearConsoleToolStripMenuItem = new ThemedToolStripMenuItem();
            vpkContextMenu = new ThemedContextMenuStrip(components);
            extractToolStripMenuItem = new ThemedToolStripMenuItem();
            decompileToolStripMenuItem = new ThemedToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            copyFileNameToolStripMenuItem = new ThemedToolStripMenuItem();
            copyFileNameOnDiskToolStripMenuItem = new ThemedToolStripMenuItem();
            toolStripSeparator3 = new ToolStripSeparator();
            openWithoutViewerToolStripMenuItem = new ThemedToolStripMenuItem();
            openWithDefaultAppToolStripMenuItem = new ThemedToolStripMenuItem();
            viewAssetInfoToolStripMenuItem = new ThemedToolStripMenuItem();
            verifyPackageContentsToolStripMenuItem = new ThemedToolStripMenuItem();
            vpkEditingContextMenu = new ThemedContextMenuStrip(components);
            vpkEditCreateFolderToolStripMenuItem = new ThemedToolStripMenuItem();
            vpkEditAddExistingFolderToolStripMenuItem = new ThemedToolStripMenuItem();
            vpkEditAddExistingFilesToolStripMenuItem = new ThemedToolStripMenuItem();
            vpkEditRemoveThisFolderToolStripMenuItem = new ThemedToolStripMenuItem();
            vpkEditRemoveThisFileToolStripMenuItem = new ThemedToolStripMenuItem();
            vpkEditSaveToDiskToolStripMenuItem = new ThemedToolStripMenuItem();
            transparentPanel1 = new TransparentPanel();
            panel1 = new TransparentPanel();
            mainLogo = new PictureBox();
            controlsBoxPanel = new ControlsBoxPanel();
            mainFormBottomPanel = new MainFormBottomPanel();
            menuStrip.SuspendLayout();
            tabContextMenuStrip.SuspendLayout();
            vpkContextMenu.SuspendLayout();
            vpkEditingContextMenu.SuspendLayout();
            transparentPanel1.SuspendLayout();
            panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)mainLogo).BeginInit();
            SuspendLayout();
            // 
            // menuStrip
            // 
            menuStrip.BackColor = System.Drawing.SystemColors.Window;
            menuStrip.Dock = DockStyle.Fill;
            menuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            menuStrip.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, explorerToolStripMenuItem, findToolStripButton, settingsToolStripMenuItem, aboutToolStripMenuItem });
            menuStrip.Location = new System.Drawing.Point(38, 0);
            menuStrip.Name = "menuStrip";
            menuStrip.Padding = new Padding(0, 8, 0, 8);
            menuStrip.RenderMode = ToolStripRenderMode.System;
            menuStrip.Size = new System.Drawing.Size(511, 48);
            menuStrip.TabIndex = 0;
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { openToolStripMenuItem, toolStripSeparator2, registerVpkFileAssociationToolStripMenuItem, createVpkFromFolderToolStripMenuItem, toolStripSeparator4, openWelcomeScreenToolStripMenuItem, validateShadersToolStripMenuItem });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Padding = new Padding(4);
            fileToolStripMenuItem.Size = new System.Drawing.Size(61, 32);
            fileToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Folder.svg";
            fileToolStripMenuItem.Text = "F&ile";
            // 
            // openToolStripMenuItem
            // 
            openToolStripMenuItem.Name = "openToolStripMenuItem";
            openToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.O;
            openToolStripMenuItem.Size = new System.Drawing.Size(211, 30);
            openToolStripMenuItem.SVGImageResourceName = "GUI.Icons.IconActionCreate.svg";
            openToolStripMenuItem.Text = "&Open";
            openToolStripMenuItem.Click += OpenToolStripMenuItem_Click;
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new System.Drawing.Size(208, 6);
            // 
            // registerVpkFileAssociationToolStripMenuItem
            // 
            registerVpkFileAssociationToolStripMenuItem.Name = "registerVpkFileAssociationToolStripMenuItem";
            registerVpkFileAssociationToolStripMenuItem.Size = new System.Drawing.Size(211, 30);
            registerVpkFileAssociationToolStripMenuItem.SVGImageResourceName = "GUI.Icons.IconActionVPKLink.svg";
            registerVpkFileAssociationToolStripMenuItem.Text = "Open VPKs with this app";
            registerVpkFileAssociationToolStripMenuItem.Click += RegisterVpkFileAssociationToolStripMenuItem_Click;
            // 
            // createVpkFromFolderToolStripMenuItem
            // 
            createVpkFromFolderToolStripMenuItem.Name = "createVpkFromFolderToolStripMenuItem";
            createVpkFromFolderToolStripMenuItem.Size = new System.Drawing.Size(211, 30);
            createVpkFromFolderToolStripMenuItem.SVGImageResourceName = "GUI.Icons.IconActionCreate.svg";
            createVpkFromFolderToolStripMenuItem.Text = "Create VPK from folder";
            createVpkFromFolderToolStripMenuItem.Click += CreateVpkFromFolderToolStripMenuItem_Click;
            // 
            // toolStripSeparator4
            // 
            toolStripSeparator4.Name = "toolStripSeparator4";
            toolStripSeparator4.Size = new System.Drawing.Size(208, 6);
            // 
            // openWelcomeScreenToolStripMenuItem
            // 
            openWelcomeScreenToolStripMenuItem.Name = "openWelcomeScreenToolStripMenuItem";
            openWelcomeScreenToolStripMenuItem.Size = new System.Drawing.Size(211, 30);
            openWelcomeScreenToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Favorite Dark.svg";
            openWelcomeScreenToolStripMenuItem.Text = "Open welcome screen";
            openWelcomeScreenToolStripMenuItem.Click += OnOpenWelcomeScreenToolStripMenuItem_Click;
            // 
            // validateShadersToolStripMenuItem
            // 
            validateShadersToolStripMenuItem.Name = "validateShadersToolStripMenuItem";
            validateShadersToolStripMenuItem.Size = new System.Drawing.Size(211, 30);
            validateShadersToolStripMenuItem.SVGImageResourceName = "GUI.Icons.ValidateShaders.svg";
            validateShadersToolStripMenuItem.Text = "Validate shaders";
            validateShadersToolStripMenuItem.Click += OnValidateShadersToolStripMenuItem_Click;
            // 
            // explorerToolStripMenuItem
            // 
            explorerToolStripMenuItem.Name = "explorerToolStripMenuItem";
            explorerToolStripMenuItem.Padding = new Padding(4);
            explorerToolStripMenuItem.Size = new System.Drawing.Size(85, 32);
            explorerToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Explorer.svg";
            explorerToolStripMenuItem.Text = "Explorer";
            explorerToolStripMenuItem.Click += OpenExplorer_Click;
            // 
            // findToolStripButton
            // 
            findToolStripButton.Enabled = false;
            findToolStripButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            findToolStripButton.Name = "findToolStripButton";
            findToolStripButton.Padding = new Padding(4);
            findToolStripButton.ShortcutKeys = Keys.Control | Keys.F;
            findToolStripButton.Size = new System.Drawing.Size(66, 32);
            findToolStripButton.SVGImageResourceName = "GUI.Icons.Find.svg";
            findToolStripButton.Text = "&Find";
            findToolStripButton.Click += FindToolStripMenuItem_Click;
            // 
            // settingsToolStripMenuItem
            // 
            settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            settingsToolStripMenuItem.Size = new System.Drawing.Size(85, 32);
            settingsToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Settings.svg";
            settingsToolStripMenuItem.Text = "Settings";
            settingsToolStripMenuItem.Click += OnSettingsItemClick;
            // 
            // aboutToolStripMenuItem
            // 
            aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            aboutToolStripMenuItem.Size = new System.Drawing.Size(76, 32);
            aboutToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Info.svg";
            aboutToolStripMenuItem.Text = "About";
            aboutToolStripMenuItem.Click += OnAboutItemClick;
            // 
            // recoverDeletedToolStripMenuItem
            // 
            recoverDeletedToolStripMenuItem.ImageTransparentColor = System.Drawing.Color.Magenta;
            recoverDeletedToolStripMenuItem.Name = "recoverDeletedToolStripMenuItem";
            recoverDeletedToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            recoverDeletedToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Find.svg";
            recoverDeletedToolStripMenuItem.Text = "Recover deleted files";
            recoverDeletedToolStripMenuItem.Click += RecoverDeletedToolStripMenuItem_Click;
            // 
            // mainTabs
            // 
            mainTabs.Appearance = TabAppearance.Buttons;
            mainTabs.BaseTabWidth = 200;
            mainTabs.Dock = DockStyle.Fill;
            mainTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            mainTabs.EndEllipsis = false;
            mainTabs.HoverColor = System.Drawing.Color.FromArgb(99, 161, 255);
            mainTabs.ItemSize = new System.Drawing.Size(200, 32);
            mainTabs.Location = new System.Drawing.Point(0, 52);
            mainTabs.Margin = new Padding(16);
            mainTabs.Name = "mainTabs";
            mainTabs.Padding = new System.Drawing.Point(16, 16);
            mainTabs.SelectedForeColor = System.Drawing.Color.Black;
            mainTabs.SelectedIndex = 0;
            mainTabs.SelectionLine = true;
            mainTabs.SelectTabColor = System.Drawing.Color.FromArgb(231, 236, 236);
            mainTabs.Size = new System.Drawing.Size(749, 343);
            mainTabs.SizeMode = TabSizeMode.Fixed;
            mainTabs.TabHeight = 32;
            mainTabs.TabIndex = 1;
            mainTabs.TabTopRadius = 8;
            mainTabs.MouseClick += OnTabClick;
            // 
            // tabContextMenuStrip
            // 
            tabContextMenuStrip.BackColor = System.Drawing.Color.FromArgb(244, 244, 244);
            tabContextMenuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            tabContextMenuStrip.Items.AddRange(new ToolStripItem[] { closeToolStripMenuItem, closeToolStripMenuItems, closeToolStripMenuItemsToRight, closeToolStripMenuItemsToLeft, exportAsIsToolStripMenuItem, decompileExportToolStripMenuItem, clearConsoleToolStripMenuItem });
            tabContextMenuStrip.LayoutStyle = ToolStripLayoutStyle.Table;
            tabContextMenuStrip.Name = "contextMenuStrip1";
            tabContextMenuStrip.Size = new System.Drawing.Size(234, 214);
            // 
            // closeToolStripMenuItem
            // 
            closeToolStripMenuItem.Name = "closeToolStripMenuItem";
            closeToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.W;
            closeToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            closeToolStripMenuItem.SVGImageResourceName = "GUI.Icons.CloseTab.svg";
            closeToolStripMenuItem.Text = "Close &tab";
            closeToolStripMenuItem.Click += CloseToolStripMenuItem_Click;
            // 
            // closeToolStripMenuItems
            // 
            closeToolStripMenuItems.Name = "closeToolStripMenuItems";
            closeToolStripMenuItems.ShortcutKeys = Keys.Control | Keys.Q;
            closeToolStripMenuItems.Size = new System.Drawing.Size(233, 30);
            closeToolStripMenuItems.SVGImageResourceName = "GUI.Icons.CloseAllTabs.svg";
            closeToolStripMenuItems.Text = "Close &all tabs";
            closeToolStripMenuItems.Click += CloseToolStripMenuItems_Click;
            // 
            // closeToolStripMenuItemsToRight
            // 
            closeToolStripMenuItemsToRight.Name = "closeToolStripMenuItemsToRight";
            closeToolStripMenuItemsToRight.ShortcutKeys = Keys.Control | Keys.E;
            closeToolStripMenuItemsToRight.Size = new System.Drawing.Size(233, 30);
            closeToolStripMenuItemsToRight.SVGImageResourceName = "GUI.Icons.CloseAllTabsRight.svg";
            closeToolStripMenuItemsToRight.Text = "Close all tabs to &right";
            closeToolStripMenuItemsToRight.Click += CloseToolStripMenuItemsToRight_Click;
            // 
            // closeToolStripMenuItemsToLeft
            // 
            closeToolStripMenuItemsToLeft.Name = "closeToolStripMenuItemsToLeft";
            closeToolStripMenuItemsToLeft.Size = new System.Drawing.Size(233, 30);
            closeToolStripMenuItemsToLeft.SVGImageResourceName = "GUI.Icons.CloseAllTabsLeft.svg";
            closeToolStripMenuItemsToLeft.Text = "Close all tabs to &left";
            closeToolStripMenuItemsToLeft.Click += CloseToolStripMenuItemsToLeft_Click;
            // 
            // exportAsIsToolStripMenuItem
            // 
            exportAsIsToolStripMenuItem.Name = "exportAsIsToolStripMenuItem";
            exportAsIsToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            exportAsIsToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Export.svg";
            exportAsIsToolStripMenuItem.Text = "Export as is";
            exportAsIsToolStripMenuItem.Click += ExtractToolStripMenuItem_Click;
            // 
            // decompileExportToolStripMenuItem
            // 
            decompileExportToolStripMenuItem.Name = "decompileExportToolStripMenuItem";
            decompileExportToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            decompileExportToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Decompile.svg";
            decompileExportToolStripMenuItem.Text = "Decompile && export";
            decompileExportToolStripMenuItem.Click += DecompileToolStripMenuItem_Click;
            // 
            // clearConsoleToolStripMenuItem
            // 
            clearConsoleToolStripMenuItem.Name = "clearConsoleToolStripMenuItem";
            clearConsoleToolStripMenuItem.Size = new System.Drawing.Size(233, 30);
            clearConsoleToolStripMenuItem.SVGImageResourceName = "GUI.Icons.CopyAsPath.svg";
            clearConsoleToolStripMenuItem.Text = "Clear console";
            clearConsoleToolStripMenuItem.Click += ClearConsoleToolStripMenuItem_Click;
            // 
            // vpkContextMenu
            // 
            vpkContextMenu.BackColor = System.Drawing.Color.FromArgb(231, 236, 236);
            vpkContextMenu.ImageScalingSize = new System.Drawing.Size(24, 24);
            vpkContextMenu.Items.AddRange(new ToolStripItem[] { extractToolStripMenuItem, decompileToolStripMenuItem, toolStripSeparator1, copyFileNameToolStripMenuItem, copyFileNameOnDiskToolStripMenuItem, toolStripSeparator3, openWithoutViewerToolStripMenuItem, openWithDefaultAppToolStripMenuItem, viewAssetInfoToolStripMenuItem, verifyPackageContentsToolStripMenuItem, recoverDeletedToolStripMenuItem });
            vpkContextMenu.Name = "vpkContextMenu";
            vpkContextMenu.Size = new System.Drawing.Size(217, 286);
            // 
            // extractToolStripMenuItem
            // 
            extractToolStripMenuItem.Name = "extractToolStripMenuItem";
            extractToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            extractToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Export.svg";
            extractToolStripMenuItem.Text = "Export as is";
            extractToolStripMenuItem.Click += ExtractToolStripMenuItem_Click;
            // 
            // decompileToolStripMenuItem
            // 
            decompileToolStripMenuItem.Name = "decompileToolStripMenuItem";
            decompileToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            decompileToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Decompile.svg";
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
            copyFileNameToolStripMenuItem.Name = "copyFileNameToolStripMenuItem";
            copyFileNameToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            copyFileNameToolStripMenuItem.SVGImageResourceName = "GUI.Icons.CopyAsPath.svg";
            copyFileNameToolStripMenuItem.Text = "Copy name";
            copyFileNameToolStripMenuItem.Click += CopyFileNameToolStripMenuItem_Click;
            // 
            // copyFileNameOnDiskToolStripMenuItem
            // 
            copyFileNameOnDiskToolStripMenuItem.Name = "copyFileNameOnDiskToolStripMenuItem";
            copyFileNameOnDiskToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            copyFileNameOnDiskToolStripMenuItem.SVGImageResourceName = "GUI.Icons.CopyAsPath.svg";
            copyFileNameOnDiskToolStripMenuItem.Text = "Copy URL";
            copyFileNameOnDiskToolStripMenuItem.Click += CopyFileNameOnDiskToolStripMenuItem_Click;
            // 
            // toolStripSeparator3
            // 
            toolStripSeparator3.Name = "toolStripSeparator3";
            toolStripSeparator3.Size = new System.Drawing.Size(213, 6);
            // 
            // openWithoutViewerToolStripMenuItem
            // 
            openWithoutViewerToolStripMenuItem.Name = "openWithoutViewerToolStripMenuItem";
            openWithoutViewerToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            openWithoutViewerToolStripMenuItem.SVGImageResourceName = "GUI.Icons.IconActionOpen.svg";
            openWithoutViewerToolStripMenuItem.Text = "Open without viewer";
            openWithoutViewerToolStripMenuItem.Click += OpenWithoutViewerToolStripMenuItem_Click;
            // 
            // openWithDefaultAppToolStripMenuItem
            // 
            openWithDefaultAppToolStripMenuItem.Name = "openWithDefaultAppToolStripMenuItem";
            openWithDefaultAppToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            openWithDefaultAppToolStripMenuItem.SVGImageResourceName = "GUI.Icons.OpenWithDefaultApp.svg";
            openWithDefaultAppToolStripMenuItem.Text = "Open with default app";
            openWithDefaultAppToolStripMenuItem.Click += OpenWithDefaultAppToolStripMenuItem_Click;
            // 
            // viewAssetInfoToolStripMenuItem
            // 
            viewAssetInfoToolStripMenuItem.Name = "viewAssetInfoToolStripMenuItem";
            viewAssetInfoToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            viewAssetInfoToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Info.svg";
            viewAssetInfoToolStripMenuItem.Text = "View asset info";
            viewAssetInfoToolStripMenuItem.Click += OnViewAssetInfoToolStripMenuItemClick;
            // 
            // verifyPackageContentsToolStripMenuItem
            // 
            verifyPackageContentsToolStripMenuItem.Name = "verifyPackageContentsToolStripMenuItem";
            verifyPackageContentsToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            verifyPackageContentsToolStripMenuItem.SVGImageResourceName = "GUI.Icons.IconActionVerifyVPKContent-1.svg";
            verifyPackageContentsToolStripMenuItem.Text = "Verify package contents";
            verifyPackageContentsToolStripMenuItem.Click += VerifyPackageContentsToolStripMenuItem_Click;
            // 
            // vpkEditingContextMenu
            // 
            vpkEditingContextMenu.BackColor = System.Drawing.Color.FromArgb(244, 244, 244);
            vpkEditingContextMenu.ImageScalingSize = new System.Drawing.Size(24, 24);
            vpkEditingContextMenu.ImeMode = ImeMode.Off;
            vpkEditingContextMenu.Items.AddRange(new ToolStripItem[] { vpkEditCreateFolderToolStripMenuItem, vpkEditAddExistingFolderToolStripMenuItem, vpkEditAddExistingFilesToolStripMenuItem, vpkEditRemoveThisFolderToolStripMenuItem, vpkEditRemoveThisFileToolStripMenuItem, vpkEditSaveToDiskToolStripMenuItem });
            vpkEditingContextMenu.Name = "vpkEditingContextMenu";
            vpkEditingContextMenu.Size = new System.Drawing.Size(182, 184);
            // 
            // vpkEditCreateFolderToolStripMenuItem
            // 
            vpkEditCreateFolderToolStripMenuItem.Name = "vpkEditCreateFolderToolStripMenuItem";
            vpkEditCreateFolderToolStripMenuItem.Size = new System.Drawing.Size(181, 30);
            vpkEditCreateFolderToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Folder.svg";
            vpkEditCreateFolderToolStripMenuItem.Text = "Create folder";
            vpkEditCreateFolderToolStripMenuItem.Click += OnVpkCreateFolderToolStripMenuItem_Click;
            // 
            // vpkEditAddExistingFolderToolStripMenuItem
            // 
            vpkEditAddExistingFolderToolStripMenuItem.Name = "vpkEditAddExistingFolderToolStripMenuItem";
            vpkEditAddExistingFolderToolStripMenuItem.Size = new System.Drawing.Size(181, 30);
            vpkEditAddExistingFolderToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Folder.svg";
            vpkEditAddExistingFolderToolStripMenuItem.Text = "&Add existing folder";
            vpkEditAddExistingFolderToolStripMenuItem.Click += OnVpkAddNewFolderToolStripMenuItem_Click;
            // 
            // vpkEditAddExistingFilesToolStripMenuItem
            // 
            vpkEditAddExistingFilesToolStripMenuItem.Name = "vpkEditAddExistingFilesToolStripMenuItem";
            vpkEditAddExistingFilesToolStripMenuItem.Size = new System.Drawing.Size(181, 30);
            vpkEditAddExistingFilesToolStripMenuItem.SVGImageResourceName = "GUI.Icons.IconActionCreate.svg";
            vpkEditAddExistingFilesToolStripMenuItem.Text = "Add existing &files";
            vpkEditAddExistingFilesToolStripMenuItem.Click += OnVpkAddNewFileToolStripMenuItem_Click;
            // 
            // vpkEditRemoveThisFolderToolStripMenuItem
            // 
            vpkEditRemoveThisFolderToolStripMenuItem.Name = "vpkEditRemoveThisFolderToolStripMenuItem";
            vpkEditRemoveThisFolderToolStripMenuItem.Size = new System.Drawing.Size(181, 30);
            vpkEditRemoveThisFolderToolStripMenuItem.SVGImageResourceName = "GUI.Icons.IconActionRemove.svg";
            vpkEditRemoveThisFolderToolStripMenuItem.Text = "&Remove this folder";
            vpkEditRemoveThisFolderToolStripMenuItem.Click += OnVpkEditingRemoveThisToolStripMenuItem_Click;
            // 
            // vpkEditRemoveThisFileToolStripMenuItem
            // 
            vpkEditRemoveThisFileToolStripMenuItem.Name = "vpkEditRemoveThisFileToolStripMenuItem";
            vpkEditRemoveThisFileToolStripMenuItem.Size = new System.Drawing.Size(181, 30);
            vpkEditRemoveThisFileToolStripMenuItem.SVGImageResourceName = "GUI.Icons.CloseTab.svg";
            vpkEditRemoveThisFileToolStripMenuItem.Text = "&Remove this file";
            vpkEditRemoveThisFileToolStripMenuItem.Click += OnVpkEditingRemoveThisToolStripMenuItem_Click;
            // 
            // vpkEditSaveToDiskToolStripMenuItem
            // 
            vpkEditSaveToDiskToolStripMenuItem.Name = "vpkEditSaveToDiskToolStripMenuItem";
            vpkEditSaveToDiskToolStripMenuItem.Size = new System.Drawing.Size(181, 30);
            vpkEditSaveToDiskToolStripMenuItem.SVGImageResourceName = "GUI.Icons.Folder VPK.svg";
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
            panel1.Controls.Add(mainLogo);
            panel1.Dock = DockStyle.Left;
            panel1.Location = new System.Drawing.Point(0, 0);
            panel1.Name = "panel1";
            panel1.Padding = new Padding(4);
            panel1.Size = new System.Drawing.Size(38, 48);
            panel1.TabIndex = 3;
            // 
            // mainLogo
            // 
            mainLogo.BackgroundImageLayout = ImageLayout.Center;
            mainLogo.Dock = DockStyle.Fill;
            mainLogo.Image = (System.Drawing.Image)resources.GetObject("mainLogo.Image");
            mainLogo.Location = new System.Drawing.Point(4, 4);
            mainLogo.Margin = new Padding(0);
            mainLogo.Name = "mainLogo";
            mainLogo.Size = new System.Drawing.Size(30, 40);
            mainLogo.SizeMode = PictureBoxSizeMode.Zoom;
            mainLogo.TabIndex = 2;
            mainLogo.TabStop = false;
            mainLogo.Click += OnMainLogoClick;
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
            ((System.ComponentModel.ISupportInitialize)mainLogo).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private TransparentMenuStrip menuStrip;
        private ThemedToolStripMenuItem closeToolStripMenuItem;
        private ThemedToolStripMenuItem extractToolStripMenuItem;
        private ThemedToolStripMenuItem copyFileNameToolStripMenuItem;
        private ThemedToolStripMenuItem closeToolStripMenuItemsToLeft;
        private ThemedToolStripMenuItem closeToolStripMenuItemsToRight;
        private ThemedToolStripMenuItem closeToolStripMenuItems;
        private ThemedToolStripMenuItem findToolStripButton;
        private ThemedToolStripMenuItem openWithoutViewerToolStripMenuItem;
        private ThemedToolStripMenuItem openWithDefaultAppToolStripMenuItem;
        private ThemedToolStripMenuItem decompileToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator1;
        private ThemedToolStripMenuItem settingsToolStripMenuItem;
        private ThemedToolStripMenuItem aboutToolStripMenuItem;
        private ThemedToolStripMenuItem recoverDeletedToolStripMenuItem;
        private ThemedToolStripMenuItem exportAsIsToolStripMenuItem;
        private ThemedToolStripMenuItem decompileExportToolStripMenuItem;
        private ThemedToolStripMenuItem explorerToolStripMenuItem;
        private ThemedToolStripMenuItem viewAssetInfoToolStripMenuItem;
        private ThemedToolStripMenuItem fileToolStripMenuItem;
        private ThemedToolStripMenuItem openToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator2;
        private ThemedToolStripMenuItem createVpkFromFolderToolStripMenuItem;
        private ThemedToolStripMenuItem verifyPackageContentsToolStripMenuItem;
        private ThemedToolStripMenuItem registerVpkFileAssociationToolStripMenuItem;
        private ThemedToolStripMenuItem clearConsoleToolStripMenuItem;
        private ThemedToolStripMenuItem vpkEditAddExistingFolderToolStripMenuItem;
        private ThemedToolStripMenuItem vpkEditSaveToDiskToolStripMenuItem;
        private ThemedToolStripMenuItem vpkEditAddExistingFilesToolStripMenuItem;
        private ThemedToolStripMenuItem vpkEditCreateFolderToolStripMenuItem;
        private ThemedToolStripMenuItem vpkEditRemoveThisFolderToolStripMenuItem;
        private ThemedToolStripMenuItem vpkEditRemoveThisFileToolStripMenuItem;
        private ThemedToolStripMenuItem copyFileNameOnDiskToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator3;
        private ToolStripSeparator toolStripSeparator4;
        private ThemedToolStripMenuItem openWelcomeScreenToolStripMenuItem;
        private ThemedTabControl mainTabs;
        private Controls.TransparentPanel transparentPanel1;
        private Controls.ControlsBoxPanel controlsBoxPanel;
        private PictureBox mainLogo;
        private TransparentPanel panel1;
        private ThemedContextMenuStrip tabContextMenuStrip;
        private ThemedContextMenuStrip vpkContextMenu;
        private ThemedContextMenuStrip vpkEditingContextMenu;
        private MainFormBottomPanel mainFormBottomPanel;
        private ThemedToolStripMenuItem validateShadersToolStripMenuItem;
    }
}

