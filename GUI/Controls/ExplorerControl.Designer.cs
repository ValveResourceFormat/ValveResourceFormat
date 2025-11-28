namespace GUI.Controls
{
    partial class ExplorerControl
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
            components = new System.ComponentModel.Container();
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(ExplorerControl));
            treeView = new GUI.Utils.TreeViewDoubleBuffered();
            filterTextBox = new ThemedTextBox();
            fileContextMenuStrip = new ThemedContextMenuStrip(components);
            revealInFileExplorerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            addToFavoritesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            removeFromFavoritesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            removeFromRecentToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            recentFilesContextMenuStrip = new ThemedContextMenuStrip(components);
            clearRecentFilesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            fileContextMenuStrip.SuspendLayout();
            recentFilesContextMenuStrip.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // treeView
            // 
            treeView.Dock = System.Windows.Forms.DockStyle.Fill;
            treeView.Location = new System.Drawing.Point(0, 20);
            treeView.Margin = new System.Windows.Forms.Padding(0);
            treeView.Name = "treeView";
            treeView.ShowLines = false;
            treeView.Size = new System.Drawing.Size(581, 334);
            treeView.TabIndex = 2;
            treeView.NodeMouseClick += OnTreeViewNodeMouseClick;
            treeView.NodeMouseDoubleClick += OnTreeViewNodeMouseDoubleClick;
            // 
            // filterTextBox
            // 
            filterTextBox.BackColor = System.Drawing.Color.FromArgb(231, 236, 236);
            filterTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            filterTextBox.ForeColor = System.Drawing.Color.Black;
            filterTextBox.Location = new System.Drawing.Point(0, 0);
            filterTextBox.Margin = new System.Windows.Forms.Padding(0);
            filterTextBox.Multiline = true;
            filterTextBox.Name = "filterTextBox";
            filterTextBox.PlaceholderText = "Filter…";
            filterTextBox.Size = new System.Drawing.Size(581, 20);
            filterTextBox.TabIndex = 0;
            filterTextBox.TextChanged += OnFilterTextBoxTextChanged;
            // 
            // fileContextMenuStrip
            // 
            fileContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { revealInFileExplorerToolStripMenuItem, addToFavoritesToolStripMenuItem, removeFromFavoritesToolStripMenuItem, removeFromRecentToolStripMenuItem });
            fileContextMenuStrip.Name = "fileContextMenuStrip";
            fileContextMenuStrip.Size = new System.Drawing.Size(209, 92);
            // 
            // revealInFileExplorerToolStripMenuItem
            // 
            revealInFileExplorerToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("revealInFileExplorerToolStripMenuItem.Image");
            revealInFileExplorerToolStripMenuItem.Name = "revealInFileExplorerToolStripMenuItem";
            revealInFileExplorerToolStripMenuItem.Size = new System.Drawing.Size(208, 22);
            revealInFileExplorerToolStripMenuItem.Text = "R&eveal in File Explorer";
            revealInFileExplorerToolStripMenuItem.Click += OnRevealInFileExplorerClick;
            // 
            // addToFavoritesToolStripMenuItem
            // 
            addToFavoritesToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("addToFavoritesToolStripMenuItem.Image");
            addToFavoritesToolStripMenuItem.Name = "addToFavoritesToolStripMenuItem";
            addToFavoritesToolStripMenuItem.Size = new System.Drawing.Size(208, 22);
            addToFavoritesToolStripMenuItem.Text = "Add to &Bookmarks";
            addToFavoritesToolStripMenuItem.Click += OnAddToBookmarksClick;
            // 
            // removeFromFavoritesToolStripMenuItem
            // 
            removeFromFavoritesToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("removeFromFavoritesToolStripMenuItem.Image");
            removeFromFavoritesToolStripMenuItem.Name = "removeFromFavoritesToolStripMenuItem";
            removeFromFavoritesToolStripMenuItem.Size = new System.Drawing.Size(208, 22);
            removeFromFavoritesToolStripMenuItem.Text = "Remove from &Bookmarks";
            removeFromFavoritesToolStripMenuItem.Click += OnRemoveFromBookmarksClick;
            // 
            // removeFromRecentToolStripMenuItem
            // 
            removeFromRecentToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("removeFromRecentToolStripMenuItem.Image");
            removeFromRecentToolStripMenuItem.Name = "removeFromRecentToolStripMenuItem";
            removeFromRecentToolStripMenuItem.Size = new System.Drawing.Size(208, 22);
            removeFromRecentToolStripMenuItem.Text = "&Remove from Recent";
            removeFromRecentToolStripMenuItem.Click += OnRemoveFromRecentClick;
            // 
            // recentFilesContextMenuStrip
            // 
            recentFilesContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { clearRecentFilesToolStripMenuItem });
            recentFilesContextMenuStrip.Name = "recentFilesContextMenuStrip";
            recentFilesContextMenuStrip.Size = new System.Drawing.Size(162, 26);
            // 
            // clearRecentFilesToolStripMenuItem
            // 
            clearRecentFilesToolStripMenuItem.Image = (System.Drawing.Image)resources.GetObject("clearRecentFilesToolStripMenuItem.Image");
            clearRecentFilesToolStripMenuItem.Name = "clearRecentFilesToolStripMenuItem";
            clearRecentFilesToolStripMenuItem.Size = new System.Drawing.Size(161, 22);
            clearRecentFilesToolStripMenuItem.Text = "&Clear recent files";
            clearRecentFilesToolStripMenuItem.Click += OnClearRecentFilesClick;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 1;
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.Controls.Add(treeView, 0, 1);
            tableLayoutPanel1.Controls.Add(filterTextBox, 0, 0);
            tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 2;
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.Size = new System.Drawing.Size(581, 354);
            tableLayoutPanel1.TabIndex = 3;
            // 
            // ExplorerControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(tableLayoutPanel1);
            Name = "ExplorerControl";
            Size = new System.Drawing.Size(581, 354);
            Load += OnExplorerLoad;
            VisibleChanged += OnVisibleChanged;
            fileContextMenuStrip.ResumeLayout(false);
            recentFilesContextMenuStrip.ResumeLayout(false);
            tableLayoutPanel1.ResumeLayout(false);
            tableLayoutPanel1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion
        private ThemedTextBox filterTextBox;
        private Utils.TreeViewDoubleBuffered treeView;
        private System.Windows.Forms.ToolStripMenuItem revealInFileExplorerToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem clearRecentFilesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem addToFavoritesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem removeFromFavoritesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem removeFromRecentToolStripMenuItem;
        private ThemedContextMenuStrip fileContextMenuStrip;
        private ThemedContextMenuStrip recentFilesContextMenuStrip;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
    }
}
