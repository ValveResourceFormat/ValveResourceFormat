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
            treeView = new System.Windows.Forms.TreeView();
            SuspendLayout();
            // 
            // treeView
            // 
            treeView.Dock = System.Windows.Forms.DockStyle.Fill;
            treeView.Location = new System.Drawing.Point(0, 0);
            treeView.Name = "treeView";
            treeView.Size = new System.Drawing.Size(581, 354);
            treeView.TabIndex = 0;
            treeView.NodeMouseDoubleClick += OnTreeViewNodeMouseDoubleClick;
            // 
            // ExplorerControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(treeView);
            Name = "ExplorerControl";
            Size = new System.Drawing.Size(581, 354);
            ResumeLayout(false);
        }
        #endregion

        private System.Windows.Forms.TreeView treeView;
    }
}
