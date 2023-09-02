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
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(AboutForm));
            label1 = new Label();
            labelVersion = new Label();
            website = new Button();
            github = new Button();
            releases = new Button();
            labelRuntime = new Label();
            label3 = new Label();
            icon = new PictureBox();
            ((System.ComponentModel.ISupportInitialize)icon).BeginInit();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(182, 12);
            label1.Margin = new Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(90, 15);
            label1.TabIndex = 0;
            label1.Text = "Source 2 Viewer";
            label1.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // labelVersion
            // 
            labelVersion.AutoSize = true;
            labelVersion.Location = new System.Drawing.Point(182, 41);
            labelVersion.Margin = new Padding(4, 0, 4, 0);
            labelVersion.Name = "labelVersion";
            labelVersion.Size = new System.Drawing.Size(45, 15);
            labelVersion.TabIndex = 1;
            labelVersion.Text = "Version";
            // 
            // website
            // 
            website.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            website.Location = new System.Drawing.Point(550, 12);
            website.Name = "website";
            website.Size = new System.Drawing.Size(100, 23);
            website.TabIndex = 4;
            website.Text = "Website";
            website.UseVisualStyleBackColor = true;
            website.Click += OnWebsiteClick;
            // 
            // github
            // 
            github.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            github.Location = new System.Drawing.Point(550, 41);
            github.Name = "github";
            github.Size = new System.Drawing.Size(100, 23);
            github.TabIndex = 5;
            github.Text = "GitHub";
            github.UseVisualStyleBackColor = true;
            github.Click += OnGithubClick;
            // 
            // releases
            // 
            releases.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            releases.Location = new System.Drawing.Point(550, 70);
            releases.Name = "releases";
            releases.Size = new System.Drawing.Size(100, 23);
            releases.TabIndex = 6;
            releases.Text = "View releases";
            releases.UseVisualStyleBackColor = true;
            releases.Click += OnReleasesClick;
            // 
            // labelRuntime
            // 
            labelRuntime.AutoSize = true;
            labelRuntime.Location = new System.Drawing.Point(182, 70);
            labelRuntime.Margin = new Padding(4, 0, 4, 0);
            labelRuntime.Name = "labelRuntime";
            labelRuntime.Size = new System.Drawing.Size(52, 15);
            labelRuntime.TabIndex = 8;
            labelRuntime.Text = "Runtime";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new System.Drawing.Point(182, 122);
            label3.Margin = new Padding(4, 0, 4, 0);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(394, 60);
            label3.TabIndex = 10;
            label3.Text = "Available under the MIT license.\r\n\r\nThis project is not affiliated with Valve Software.\r\nSource 2 is a trademark and/or registered trademark of Valve Corporation.";
            // 
            // icon
            // 
            icon.Image = (System.Drawing.Image)resources.GetObject("icon.Image");
            icon.Location = new System.Drawing.Point(0, 12);
            icon.Name = "icon";
            icon.Size = new System.Drawing.Size(170, 170);
            icon.SizeMode = PictureBoxSizeMode.Zoom;
            icon.TabIndex = 11;
            icon.TabStop = false;
            // 
            // AboutForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = System.Drawing.Color.White;
            ClientSize = new System.Drawing.Size(662, 191);
            Controls.Add(icon);
            Controls.Add(label3);
            Controls.Add(labelRuntime);
            Controls.Add(releases);
            Controls.Add(github);
            Controls.Add(website);
            Controls.Add(labelVersion);
            Controls.Add(label1);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Margin = new Padding(4, 3, 4, 3);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "AboutForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            SizeGripStyle = SizeGripStyle.Hide;
            StartPosition = FormStartPosition.CenterParent;
            Text = "About";
            TopMost = true;
            ((System.ComponentModel.ISupportInitialize)icon).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Label labelVersion;
        private Button website;
        private Button github;
        private Button releases;
        private Label labelRuntime;
        private Label label3;
        private PictureBox icon;
    }
}
