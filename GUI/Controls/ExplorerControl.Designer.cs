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
            treeView = new TreeViewDoubleBuffered();
            filterTextBox = new ThemedTextBox();
            fileContextMenuStrip = new ThemedContextMenuStrip(components);
            revealInFileExplorerToolStripMenuItem = new ThemedToolStripMenuItem();
            addToFavoritesToolStripMenuItem = new ThemedToolStripMenuItem();
            removeFromFavoritesToolStripMenuItem = new ThemedToolStripMenuItem();
            removeFromRecentToolStripMenuItem = new ThemedToolStripMenuItem();
            recentFilesContextMenuStrip = new ThemedContextMenuStrip(components);
            clearRecentFilesToolStripMenuItem = new ThemedToolStripMenuItem();
            tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            fileContextMenuStrip.SuspendLayout();
            recentFilesContextMenuStrip.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // treeView
            // 
            treeView.Dock = System.Windows.Forms.DockStyle.Fill;
            treeView.ItemHeight = 26;
            treeView.Location = new System.Drawing.Point(0, 23);
            treeView.Margin = new System.Windows.Forms.Padding(0);
            treeView.Name = "treeView";
            treeView.ShowLines = false;
            treeView.Size = new System.Drawing.Size(581, 378);
            treeView.TabIndex = 2;
            treeView.NodeMouseClick += OnTreeViewNodeMouseClick;
            treeView.NodeMouseDoubleClick += OnTreeViewNodeMouseDoubleClick;
            // 
            // filterTextBox
            // 
            filterTextBox.BackColor = System.Drawing.Color.FromArgb(236, 236, 236);
            filterTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            filterTextBox.ForeColor = System.Drawing.Color.Black;
            filterTextBox.Location = new System.Drawing.Point(0, 0);
            filterTextBox.Margin = new System.Windows.Forms.Padding(0);
            filterTextBox.Multiline = true;
            filterTextBox.Name = "filterTextBox";
            filterTextBox.PlaceholderText = "Filter…";
            filterTextBox.Size = new System.Drawing.Size(581, 23);
            filterTextBox.TabIndex = 0;
            filterTextBox.TextChanged += OnFilterTextBoxTextChanged;
            // 
            // fileContextMenuStrip
            // 
            fileContextMenuStrip.BackColor = System.Drawing.Color.FromArgb(244, 244, 244);
            fileContextMenuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            fileContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { revealInFileExplorerToolStripMenuItem, addToFavoritesToolStripMenuItem, removeFromFavoritesToolStripMenuItem, removeFromRecentToolStripMenuItem });
            fileContextMenuStrip.Name = "fileContextMenuStrip";
            fileContextMenuStrip.Size = new System.Drawing.Size(217, 124);
            // 
            // revealInFileExplorerToolStripMenuItem
            // 
            revealInFileExplorerToolStripMenuItem.Name = "revealInFileExplorerToolStripMenuItem";
            revealInFileExplorerToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            revealInFileExplorerToolStripMenuItem.SVGImageResourceName = "GUI.Icons.OpenInExplorer.svg";
            revealInFileExplorerToolStripMenuItem.Text = "R&eveal in File Explorer";
            revealInFileExplorerToolStripMenuItem.Click += OnRevealInFileExplorerClick;
            // 
            // addToFavoritesToolStripMenuItem
            // 
            addToFavoritesToolStripMenuItem.Name = "addToFavoritesToolStripMenuItem";
            addToFavoritesToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            addToFavoritesToolStripMenuItem.SVGImageResourceName = "GUI.Icons.BookmarksAdd.svg";
            addToFavoritesToolStripMenuItem.Text = "Add to &Bookmarks";
            addToFavoritesToolStripMenuItem.Click += OnAddToBookmarksClick;
            // 
            // removeFromFavoritesToolStripMenuItem
            // 
            removeFromFavoritesToolStripMenuItem.Name = "removeFromFavoritesToolStripMenuItem";
            removeFromFavoritesToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            removeFromFavoritesToolStripMenuItem.SVGImageResourceName = "GUI.Icons.BookmarksRemove.svg";
            removeFromFavoritesToolStripMenuItem.Text = "Remove from &Bookmarks";
            removeFromFavoritesToolStripMenuItem.Click += OnRemoveFromBookmarksClick;
            // 
            // removeFromRecentToolStripMenuItem
            // 
            removeFromRecentToolStripMenuItem.Name = "removeFromRecentToolStripMenuItem";
            removeFromRecentToolStripMenuItem.Size = new System.Drawing.Size(216, 30);
            removeFromRecentToolStripMenuItem.SVGImageResourceName = "GUI.Icons.HistoryRemove.svg";
            removeFromRecentToolStripMenuItem.Text = "&Remove from Recent";
            removeFromRecentToolStripMenuItem.Click += OnRemoveFromRecentClick;
            // 
            // recentFilesContextMenuStrip
            // 
            recentFilesContextMenuStrip.BackColor = System.Drawing.Color.FromArgb(244, 244, 244);
            recentFilesContextMenuStrip.ImageScalingSize = new System.Drawing.Size(24, 24);
            recentFilesContextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { clearRecentFilesToolStripMenuItem });
            recentFilesContextMenuStrip.Name = "recentFilesContextMenuStrip";
            recentFilesContextMenuStrip.Size = new System.Drawing.Size(170, 34);
            // 
            // clearRecentFilesToolStripMenuItem
            // 
            clearRecentFilesToolStripMenuItem.Name = "clearRecentFilesToolStripMenuItem";
            clearRecentFilesToolStripMenuItem.Size = new System.Drawing.Size(169, 30);
            clearRecentFilesToolStripMenuItem.SVGImageResourceName = "GUI.Icons.HistoryClear.svg";
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
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 23F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.Size = new System.Drawing.Size(581, 401);
            tableLayoutPanel1.TabIndex = 3;
            // 
            // ExplorerControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(tableLayoutPanel1);
            Font = new System.Drawing.Font("Segoe UI", 10F);
            Name = "ExplorerControl";
            Size = new System.Drawing.Size(581, 401);
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
        private TreeViewDoubleBuffered treeView;
        private ThemedToolStripMenuItem revealInFileExplorerToolStripMenuItem;
        private ThemedToolStripMenuItem clearRecentFilesToolStripMenuItem;
        private ThemedToolStripMenuItem addToFavoritesToolStripMenuItem;
        private ThemedToolStripMenuItem removeFromFavoritesToolStripMenuItem;
        private ThemedToolStripMenuItem removeFromRecentToolStripMenuItem;
        private ThemedContextMenuStrip fileContextMenuStrip;
        private ThemedContextMenuStrip recentFilesContextMenuStrip;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
    }
}
