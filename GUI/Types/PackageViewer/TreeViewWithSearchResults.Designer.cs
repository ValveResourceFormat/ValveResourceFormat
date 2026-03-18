using System;
using GUI.Controls;
using GUI.Forms;

namespace GUI.Types.PackageViewer
{
    partial class TreeViewWithSearchResults
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            mainSplitContainer = new System.Windows.Forms.SplitContainer();
            mainTreeView = new BetterTreeView();
            rightPanel = new System.Windows.Forms.Panel();
            tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
            listRadioButton = new System.Windows.Forms.RadioButton();
            gridRadioButton = new System.Windows.Forms.RadioButton();
            gridSizeSlider = new System.Windows.Forms.TrackBar();
            panel1 = new System.Windows.Forms.Panel();
            mainListView = new BetterListView();
            searchTextBox = new ThemedTextBox();
            ((System.ComponentModel.ISupportInitialize)mainSplitContainer).BeginInit();
            mainSplitContainer.Panel1.SuspendLayout();
            mainSplitContainer.Panel2.SuspendLayout();
            mainSplitContainer.SuspendLayout();
            rightPanel.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            tableLayoutPanel2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)gridSizeSlider).BeginInit();
            panel1.SuspendLayout();
            SuspendLayout();
            // 
            // mainSplitContainer
            // 
            mainSplitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            mainSplitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            mainSplitContainer.Location = new System.Drawing.Point(0, 23);
            mainSplitContainer.Margin = new System.Windows.Forms.Padding(0);
            mainSplitContainer.Name = "mainSplitContainer";
            // 
            // mainSplitContainer.Panel1
            // 
            mainSplitContainer.Panel1.Controls.Add(mainTreeView);
            // 
            // mainSplitContainer.Panel2
            // 
            mainSplitContainer.Panel2.Controls.Add(rightPanel);
            mainSplitContainer.Size = new System.Drawing.Size(800, 377);
            mainSplitContainer.SplitterDistance = 400;
            mainSplitContainer.SplitterWidth = 5;
            mainSplitContainer.TabIndex = 0;
            mainSplitContainer.TabStop = false;
            mainSplitContainer.SplitterMoved += MainSplitContainerSplitterMoved;
            // 
            // mainTreeView
            // 
            mainTreeView.Dock = System.Windows.Forms.DockStyle.Fill;
            mainTreeView.ItemHeight = 26;
            mainTreeView.Location = new System.Drawing.Point(0, 0);
            mainTreeView.Margin = new System.Windows.Forms.Padding(0);
            mainTreeView.Name = "mainTreeView";
            mainTreeView.ShowLines = false;
            mainTreeView.Size = new System.Drawing.Size(400, 377);
            mainTreeView.TabIndex = 0;
            mainTreeView.VrfGuiContext = null;
            // 
            // rightPanel
            // 
            rightPanel.Controls.Add(tableLayoutPanel1);
            rightPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            rightPanel.Location = new System.Drawing.Point(0, 0);
            rightPanel.Name = "rightPanel";
            rightPanel.Size = new System.Drawing.Size(395, 377);
            rightPanel.TabIndex = 0;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 1;
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.Controls.Add(tableLayoutPanel2, 0, 0);
            tableLayoutPanel1.Controls.Add(panel1, 0, 1);
            tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 3;
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            tableLayoutPanel1.Size = new System.Drawing.Size(395, 377);
            tableLayoutPanel1.TabIndex = 4;
            // 
            // tableLayoutPanel2
            // 
            tableLayoutPanel2.ColumnCount = 3;
            tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 170F));
            tableLayoutPanel2.Controls.Add(listRadioButton, 0, 0);
            tableLayoutPanel2.Controls.Add(gridRadioButton, 1, 0);
            tableLayoutPanel2.Controls.Add(gridSizeSlider, 2, 0);
            tableLayoutPanel2.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel2.Location = new System.Drawing.Point(0, 0);
            tableLayoutPanel2.Margin = new System.Windows.Forms.Padding(0);
            tableLayoutPanel2.Name = "tableLayoutPanel2";
            tableLayoutPanel2.Padding = new System.Windows.Forms.Padding(8, 0, 0, 0);
            tableLayoutPanel2.RowCount = 1;
            tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel2.Size = new System.Drawing.Size(395, 40);
            tableLayoutPanel2.TabIndex = 5;
            // 
            // listRadioButton
            // 
            listRadioButton.AutoSize = true;
            listRadioButton.Dock = System.Windows.Forms.DockStyle.Fill;
            listRadioButton.Location = new System.Drawing.Point(11, 3);
            listRadioButton.Name = "listRadioButton";
            listRadioButton.Size = new System.Drawing.Size(54, 34);
            listRadioButton.TabIndex = 0;
            listRadioButton.Text = "List";
            listRadioButton.UseVisualStyleBackColor = true;
            listRadioButton.CheckedChanged += listRadioButton_CheckedChanged;
            // 
            // gridRadioButton
            // 
            gridRadioButton.AutoSize = true;
            gridRadioButton.Checked = true;
            gridRadioButton.Dock = System.Windows.Forms.DockStyle.Fill;
            gridRadioButton.Location = new System.Drawing.Point(71, 3);
            gridRadioButton.Name = "gridRadioButton";
            gridRadioButton.Size = new System.Drawing.Size(54, 34);
            gridRadioButton.TabIndex = 1;
            gridRadioButton.TabStop = true;
            gridRadioButton.Text = "Grid";
            gridRadioButton.UseVisualStyleBackColor = true;
            gridRadioButton.CheckedChanged += gridRadioButton_CheckedChanged;
            // 
            // gridSizeSlider
            // 
            gridSizeSlider.LargeChange = 1;
            gridSizeSlider.Location = new System.Drawing.Point(131, 3);
            gridSizeSlider.Maximum = 4;
            gridSizeSlider.Name = "gridSizeSlider";
            gridSizeSlider.Size = new System.Drawing.Size(107, 34);
            gridSizeSlider.TabIndex = 2;
            gridSizeSlider.Value = 4;
            gridSizeSlider.Scroll += gridSizeSlider_Scroll;
            // 
            // panel1
            // 
            panel1.Controls.Add(mainListView);
            panel1.Dock = System.Windows.Forms.DockStyle.Fill;
            panel1.Location = new System.Drawing.Point(0, 40);
            panel1.Margin = new System.Windows.Forms.Padding(0);
            panel1.Name = "panel1";
            panel1.Size = new System.Drawing.Size(395, 317);
            panel1.TabIndex = 4;
            // 
            // mainListView
            // 
            mainListView.BorderColor = System.Drawing.Color.White;
            mainListView.BorderStyle = System.Windows.Forms.BorderStyle.None;
            mainListView.Dock = System.Windows.Forms.DockStyle.Fill;
            mainListView.Highlight = System.Drawing.Color.White;
            mainListView.Location = new System.Drawing.Point(0, 0);
            mainListView.Margin = new System.Windows.Forms.Padding(0);
            mainListView.Name = "mainListView";
            mainListView.OwnerDraw = true;
            mainListView.Size = new System.Drawing.Size(395, 317);
            mainListView.TabIndex = 3;
            mainListView.UseCompatibleStateImageBehavior = false;
            mainListView.View = System.Windows.Forms.View.Details;
            mainListView.VrfGuiContext = null;
            // 
            // searchTextBox
            // 
            searchTextBox.BackColor = System.Drawing.Color.FromArgb(236, 236, 236);
            searchTextBox.Dock = System.Windows.Forms.DockStyle.Top;
            searchTextBox.ForeColor = System.Drawing.Color.Black;
            searchTextBox.Location = new System.Drawing.Point(0, 0);
            searchTextBox.Margin = new System.Windows.Forms.Padding(0);
            searchTextBox.Multiline = true;
            searchTextBox.Name = "searchTextBox";
            searchTextBox.PlaceholderText = "Search…";
            searchTextBox.Size = new System.Drawing.Size(800, 23);
            searchTextBox.TabIndex = 1;
            searchTextBox.KeyDown += OnSearchTextBoxKeyDown;
            // 
            // TreeViewWithSearchResults
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(mainSplitContainer);
            Controls.Add(searchTextBox);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Name = "TreeViewWithSearchResults";
            Size = new System.Drawing.Size(800, 400);
            Load += TreeViewWithSearchResults_Load;
            mainSplitContainer.Panel1.ResumeLayout(false);
            mainSplitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)mainSplitContainer).EndInit();
            mainSplitContainer.ResumeLayout(false);
            rightPanel.ResumeLayout(false);
            tableLayoutPanel1.ResumeLayout(false);
            tableLayoutPanel2.ResumeLayout(false);
            tableLayoutPanel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)gridSizeSlider).EndInit();
            panel1.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.SplitContainer mainSplitContainer;
        private BetterListView mainListView;
        public BetterTreeView mainTreeView;
        private System.Windows.Forms.Panel rightPanel;
        private ThemedTextBox searchTextBox;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
        private System.Windows.Forms.RadioButton listRadioButton;
        private System.Windows.Forms.RadioButton gridRadioButton;
        private System.Windows.Forms.TrackBar gridSizeSlider;
    }
}
