using System;
using GUI.Forms;

namespace GUI.Types.PackageViewer
{
    partial class TreeViewWithSearchResults
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
            mainSplitContainer = new System.Windows.Forms.SplitContainer();
            searchTextBox = new Controls.BetterTextBox();
            mainTreeView = new BetterTreeView();
            rightPanel = new System.Windows.Forms.Panel();
            mainListView = new BetterListView();
            ((System.ComponentModel.ISupportInitialize)mainSplitContainer).BeginInit();
            mainSplitContainer.Panel1.SuspendLayout();
            mainSplitContainer.Panel2.SuspendLayout();
            mainSplitContainer.SuspendLayout();
            rightPanel.SuspendLayout();
            SuspendLayout();
            // 
            // mainSplitContainer
            // 
            mainSplitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            mainSplitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            mainSplitContainer.Location = new System.Drawing.Point(0, 0);
            mainSplitContainer.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            mainSplitContainer.Name = "mainSplitContainer";
            // 
            // mainSplitContainer.Panel1
            // 
            mainSplitContainer.Panel1.Controls.Add(mainTreeView);
            mainSplitContainer.Panel1.Controls.Add(searchTextBox);
            // 
            // mainSplitContainer.Panel2
            // 
            mainSplitContainer.Panel2.Controls.Add(rightPanel);
            mainSplitContainer.Size = new System.Drawing.Size(800, 400);
            mainSplitContainer.SplitterDistance = 394;
            mainSplitContainer.SplitterWidth = 5;
            mainSplitContainer.TabIndex = 0;
            mainSplitContainer.TabStop = false;
            mainSplitContainer.SplitterMoved += MainSplitContainerSplitterMoved;
            // 
            // searchTextBox
            // 
            searchTextBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
            searchTextBox.Dock = System.Windows.Forms.DockStyle.Top;
            searchTextBox.Font = new System.Drawing.Font("Segoe UI", 11F);
            searchTextBox.Location = new System.Drawing.Point(0, 0);
            searchTextBox.Name = "searchTextBox";
            searchTextBox.PlaceholderText = "Search…";
            searchTextBox.Size = new System.Drawing.Size(394, 20);
            searchTextBox.TabIndex = 1;
            searchTextBox.KeyDown += OnSearchTextBoxKeyDown;
            // 
            // mainTreeView
            // 
            mainTreeView.Dock = System.Windows.Forms.DockStyle.Fill;
            mainTreeView.Location = new System.Drawing.Point(0, 20);
            mainTreeView.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            mainTreeView.Name = "mainTreeView";
            mainTreeView.ShowLines = false;
            mainTreeView.Size = new System.Drawing.Size(394, 380);
            mainTreeView.TabIndex = 0;
            mainTreeView.VrfGuiContext = null;
            // 
            // rightPanel
            // 
            rightPanel.Controls.Add(mainListView);
            rightPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            rightPanel.Location = new System.Drawing.Point(0, 0);
            rightPanel.Name = "rightPanel";
            rightPanel.Size = new System.Drawing.Size(401, 400);
            rightPanel.TabIndex = 0;
            // 
            // mainListView
            // 
            mainListView.BorderStyle = System.Windows.Forms.BorderStyle.None;
            mainListView.Dock = System.Windows.Forms.DockStyle.Fill;
            mainListView.Location = new System.Drawing.Point(0, 0);
            mainListView.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            mainListView.Name = "mainListView";
            mainListView.OwnerDraw = true;
            mainListView.Size = new System.Drawing.Size(401, 400);
            mainListView.TabIndex = 3;
            mainListView.UseCompatibleStateImageBehavior = false;
            mainListView.View = System.Windows.Forms.View.Details;
            mainListView.VrfGuiContext = null;
            // 
            // TreeViewWithSearchResults
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(mainSplitContainer);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Name = "TreeViewWithSearchResults";
            Size = new System.Drawing.Size(800, 400);
            Load += TreeViewWithSearchResults_Load;
            mainSplitContainer.Panel1.ResumeLayout(false);
            mainSplitContainer.Panel1.PerformLayout();
            mainSplitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)mainSplitContainer).EndInit();
            mainSplitContainer.ResumeLayout(false);
            rightPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.SplitContainer mainSplitContainer;
        private BetterListView mainListView;
        private System.Windows.Forms.Panel rightPanel;
        private Controls.BetterTextBox searchTextBox;
        public BetterTreeView mainTreeView;
    }
}
