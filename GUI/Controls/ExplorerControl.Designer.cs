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
            treeView = new GUI.Utils.TreeViewDoubleBuffered();
            filterTextBox = new System.Windows.Forms.TextBox();
            SuspendLayout();
            // 
            // treeView
            // 
            treeView.Dock = System.Windows.Forms.DockStyle.Fill;
            treeView.Location = new System.Drawing.Point(0, 23);
            treeView.Name = "treeView";
            treeView.Size = new System.Drawing.Size(581, 331);
            treeView.TabIndex = 2;
            treeView.NodeMouseDoubleClick += OnTreeViewNodeMouseDoubleClick;
            // 
            // filterTextBox
            // 
            filterTextBox.Dock = System.Windows.Forms.DockStyle.Top;
            filterTextBox.Location = new System.Drawing.Point(0, 0);
            filterTextBox.Name = "filterTextBox";
            filterTextBox.PlaceholderText = "Filter…";
            filterTextBox.Size = new System.Drawing.Size(581, 23);
            filterTextBox.TabIndex = 0;
            filterTextBox.TextChanged += OnFilterTextBoxTextChanged;
            // 
            // ExplorerControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(treeView);
            Controls.Add(filterTextBox);
            Name = "ExplorerControl";
            Size = new System.Drawing.Size(581, 354);
            VisibleChanged += OnVisibleChanged;
            ResumeLayout(false);
            PerformLayout();
        }
        #endregion

        private System.Windows.Forms.TreeView treeView;
        private System.Windows.Forms.TextBox filterTextBox;
    }
}
