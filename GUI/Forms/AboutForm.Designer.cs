using System.Windows.Forms;

namespace GUI.Forms
{
    partial class AboutForm
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AboutForm));
            this.label1 = new System.Windows.Forms.Label();
            this.labelVersion = new System.Windows.Forms.Label();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.website = new System.Windows.Forms.Button();
            this.github = new System.Windows.Forms.Button();
            this.releases = new System.Windows.Forms.Button();
            this.labelRuntime = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(87, 14);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(27, 15);
            this.label1.TabIndex = 0;
            this.label1.Text = "VRF";
            this.label1.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // labelVersion
            // 
            this.labelVersion.AutoSize = true;
            this.labelVersion.Location = new System.Drawing.Point(87, 38);
            this.labelVersion.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelVersion.Name = "labelVersion";
            this.labelVersion.Size = new System.Drawing.Size(45, 15);
            this.labelVersion.TabIndex = 1;
            this.labelVersion.Text = "Version";
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.pictureBox1.Image = ((System.Drawing.Image)(resources.GetObject("pictureBox1.Image")));
            this.pictureBox1.Location = new System.Drawing.Point(14, 14);
            this.pictureBox1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(64, 64);
            this.pictureBox1.TabIndex = 3;
            this.pictureBox1.TabStop = false;
            // 
            // website
            // 
            this.website.Location = new System.Drawing.Point(12, 95);
            this.website.Name = "website";
            this.website.Size = new System.Drawing.Size(100, 23);
            this.website.TabIndex = 4;
            this.website.Text = "Website";
            this.website.UseVisualStyleBackColor = true;
            this.website.Click += new System.EventHandler(this.website_Click);
            // 
            // github
            // 
            this.github.Location = new System.Drawing.Point(118, 95);
            this.github.Name = "github";
            this.github.Size = new System.Drawing.Size(100, 23);
            this.github.TabIndex = 5;
            this.github.Text = "GitHub";
            this.github.UseVisualStyleBackColor = true;
            this.github.Click += new System.EventHandler(this.github_Click);
            // 
            // releases
            // 
            this.releases.Location = new System.Drawing.Point(224, 95);
            this.releases.Name = "releases";
            this.releases.Size = new System.Drawing.Size(100, 23);
            this.releases.TabIndex = 6;
            this.releases.Text = "View releases";
            this.releases.UseVisualStyleBackColor = true;
            this.releases.Click += new System.EventHandler(this.releases_Click);
            // 
            // labelRuntime
            // 
            this.labelRuntime.AutoSize = true;
            this.labelRuntime.Location = new System.Drawing.Point(87, 62);
            this.labelRuntime.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelRuntime.Name = "labelRuntime";
            this.labelRuntime.Size = new System.Drawing.Size(52, 15);
            this.labelRuntime.TabIndex = 8;
            this.labelRuntime.Text = "Runtime";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(254, 14);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(69, 15);
            this.label2.TabIndex = 9;
            this.label2.Text = "MIT License";
            this.label2.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // AboutForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(336, 131);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.labelRuntime);
            this.Controls.Add(this.releases);
            this.Controls.Add(this.github);
            this.Controls.Add(this.website);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.labelVersion);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "AboutForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "About VRF";
            this.TopMost = true;
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label labelVersion;
        private System.Windows.Forms.PictureBox pictureBox1;
        private Button website;
        private Button github;
        private Button releases;
        private Label labelRuntime;
        private Label label2;
    }
}
