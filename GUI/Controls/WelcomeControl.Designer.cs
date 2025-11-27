namespace GUI.Controls
{
    partial class WelcomeControl
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
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(WelcomeControl));
            groupBox1 = new ThemedGroupBox();
            label1 = new System.Windows.Forms.Label();
            updateCheckButton = new ThemedButton();
            groupBox2 = new ThemedGroupBox();
            label3 = new System.Windows.Forms.Label();
            groupBox3 = new ThemedGroupBox();
            fileAssociationButton = new ThemedButton();
            splitContainer = new System.Windows.Forms.SplitContainer();
            panel1 = new System.Windows.Forms.Panel();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.SuspendLayout();
            panel1.SuspendLayout();
            SuspendLayout();
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(label1);
            groupBox1.Controls.Add(updateCheckButton);
            groupBox1.Dock = System.Windows.Forms.DockStyle.Top;
            groupBox1.Location = new System.Drawing.Point(15, 395);
            groupBox1.Name = "groupBox1";
            groupBox1.Padding = new System.Windows.Forms.Padding(20);
            groupBox1.Size = new System.Drawing.Size(270, 110);
            groupBox1.TabIndex = 2;
            groupBox1.TabStop = false;
            groupBox1.Text = "Check for updates";
            // 
            // label1
            // 
            label1.Dock = System.Windows.Forms.DockStyle.Top;
            label1.Location = new System.Drawing.Point(20, 74);
            label1.Margin = new System.Windows.Forms.Padding(3);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(230, 15);
            label1.TabIndex = 1;
            label1.Text = "Updates are checked daily by connecting to github.com";
            label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // updateCheckButton
            // 
            updateCheckButton.Dock = System.Windows.Forms.DockStyle.Top;
            updateCheckButton.Location = new System.Drawing.Point(20, 36);
            updateCheckButton.Name = "updateCheckButton";
            updateCheckButton.Size = new System.Drawing.Size(230, 38);
            updateCheckButton.TabIndex = 0;
            updateCheckButton.Text = "Enable automatic update checks";
            updateCheckButton.UseVisualStyleBackColor = true;
            updateCheckButton.Click += updateCheckButton_Click;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(label3);
            groupBox2.Dock = System.Windows.Forms.DockStyle.Top;
            groupBox2.Location = new System.Drawing.Point(15, 15);
            groupBox2.Name = "groupBox2";
            groupBox2.Padding = new System.Windows.Forms.Padding(20);
            groupBox2.Size = new System.Drawing.Size(270, 280);
            groupBox2.TabIndex = 3;
            groupBox2.TabStop = false;
            groupBox2.Text = "Welcome to Source 2 Viewer";
            // 
            // label3
            // 
            label3.Dock = System.Windows.Forms.DockStyle.Fill;
            label3.Location = new System.Drawing.Point(20, 36);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(230, 224);
            label3.TabIndex = 0;
            label3.Text = resources.GetString("label3.Text");
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(fileAssociationButton);
            groupBox3.Dock = System.Windows.Forms.DockStyle.Top;
            groupBox3.Location = new System.Drawing.Point(15, 295);
            groupBox3.Name = "groupBox3";
            groupBox3.Padding = new System.Windows.Forms.Padding(20);
            groupBox3.Size = new System.Drawing.Size(270, 100);
            groupBox3.TabIndex = 4;
            groupBox3.TabStop = false;
            groupBox3.Text = "File association";
            // 
            // fileAssociationButton
            // 
            fileAssociationButton.Dock = System.Windows.Forms.DockStyle.Top;
            fileAssociationButton.Location = new System.Drawing.Point(20, 36);
            fileAssociationButton.Name = "fileAssociationButton";
            fileAssociationButton.Size = new System.Drawing.Size(230, 41);
            fileAssociationButton.TabIndex = 0;
            fileAssociationButton.Text = "Set Source 2 Viewer as the default program for .VPK files";
            fileAssociationButton.UseVisualStyleBackColor = true;
            fileAssociationButton.Click += fileAssociationButton_Click;
            // 
            // splitContainer
            // 
            splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer.Location = new System.Drawing.Point(0, 0);
            splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            splitContainer.Panel1.Controls.Add(panel1);
            splitContainer.Panel1MinSize = 300;
            splitContainer.Size = new System.Drawing.Size(600, 600);
            splitContainer.SplitterDistance = 300;
            splitContainer.TabIndex = 5;
            // 
            // panel1
            // 
            panel1.AutoScroll = true;
            panel1.Controls.Add(groupBox1);
            panel1.Controls.Add(groupBox3);
            panel1.Controls.Add(groupBox2);
            panel1.Dock = System.Windows.Forms.DockStyle.Fill;
            panel1.Location = new System.Drawing.Point(0, 0);
            panel1.Name = "panel1";
            panel1.Padding = new System.Windows.Forms.Padding(15);
            panel1.Size = new System.Drawing.Size(300, 600);
            panel1.TabIndex = 0;
            // 
            // WelcomeControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(splitContainer);
            Name = "WelcomeControl";
            Size = new System.Drawing.Size(600, 600);
            groupBox1.ResumeLayout(false);
            groupBox2.ResumeLayout(false);
            groupBox3.ResumeLayout(false);
            splitContainer.Panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            panel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button updateCheckButton;
        private System.Windows.Forms.Button fileAssociationButton;
        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.Panel panel1;
    }
}
