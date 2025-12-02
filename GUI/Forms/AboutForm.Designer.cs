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
            website = new Button();
            github = new Button();
            releases = new Button();
            label3 = new Label();
            icon = new PictureBox();
            keybinds = new Button();
            copyVersion = new Button();
            checkForUpdatesCheckbox = new CheckBox();
            downloadButton = new Button();
            viewReleaseNotesButton = new Button();
            newVersionLabel = new Label();
            currentVersionLabel = new Label();
            ((System.ComponentModel.ISupportInitialize)icon).BeginInit();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(182, 14);
            label1.Margin = new Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(107, 19);
            label1.TabIndex = 0;
            label1.Text = "Source 2 Viewer";
            label1.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // website
            // 
            website.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            website.Location = new System.Drawing.Point(550, 14);
            website.Name = "website";
            website.Size = new System.Drawing.Size(100, 26);
            website.TabIndex = 4;
            website.Text = "&Website";
            website.UseVisualStyleBackColor = true;
            website.Click += OnWebsiteClick;
            // 
            // github
            // 
            github.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            github.Location = new System.Drawing.Point(550, 46);
            github.Name = "github";
            github.Size = new System.Drawing.Size(100, 26);
            github.TabIndex = 5;
            github.Text = "&GitHub";
            github.UseVisualStyleBackColor = true;
            github.Click += OnGithubClick;
            // 
            // releases
            // 
            releases.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            releases.Location = new System.Drawing.Point(550, 79);
            releases.Name = "releases";
            releases.Size = new System.Drawing.Size(100, 26);
            releases.TabIndex = 6;
            releases.Text = "View &releases";
            releases.UseVisualStyleBackColor = true;
            releases.Click += OnReleasesClick;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new System.Drawing.Point(177, 142);
            label3.Margin = new Padding(4, 0, 4, 0);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(463, 57);
            label3.TabIndex = 10;
            label3.Text = "Available under the MIT license.\r\nThis project is not affiliated with Valve Software.\r\nSource 2 is a trademark and/or registered trademark of Valve Corporation.";
            // 
            // icon
            // 
            icon.Image = (System.Drawing.Image)resources.GetObject("icon.Image");
            icon.Location = new System.Drawing.Point(0, 14);
            icon.Name = "icon";
            icon.Size = new System.Drawing.Size(170, 193);
            icon.SizeMode = PictureBoxSizeMode.Zoom;
            icon.TabIndex = 11;
            icon.TabStop = false;
            // 
            // keybinds
            // 
            keybinds.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            keybinds.Location = new System.Drawing.Point(550, 112);
            keybinds.Name = "keybinds";
            keybinds.Size = new System.Drawing.Size(100, 26);
            keybinds.TabIndex = 12;
            keybinds.Text = "View &keybinds";
            keybinds.UseVisualStyleBackColor = true;
            keybinds.Click += OnKeybindsClick;
            // 
            // copyVersion
            // 
            copyVersion.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            copyVersion.Location = new System.Drawing.Point(550, 238);
            copyVersion.Name = "copyVersion";
            copyVersion.Size = new System.Drawing.Size(100, 26);
            copyVersion.TabIndex = 13;
            copyVersion.Text = "Copy &version";
            copyVersion.UseVisualStyleBackColor = true;
            copyVersion.Click += OnCopyVersionClick;
            // 
            // checkForUpdatesCheckbox
            // 
            checkForUpdatesCheckbox.Location = new System.Drawing.Point(12, 375);
            checkForUpdatesCheckbox.Name = "checkForUpdatesCheckbox";
            checkForUpdatesCheckbox.Size = new System.Drawing.Size(638, 22);
            checkForUpdatesCheckbox.TabIndex = 14;
            checkForUpdatesCheckbox.Text = "Automatically check for updates daily";
            checkForUpdatesCheckbox.UseVisualStyleBackColor = true;
            checkForUpdatesCheckbox.CheckedChanged += OnCheckForUpdatesCheckboxChanged;
            // 
            // downloadButton
            // 
            downloadButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            downloadButton.Location = new System.Drawing.Point(12, 342);
            downloadButton.Name = "downloadButton";
            downloadButton.Size = new System.Drawing.Size(638, 26);
            downloadButton.TabIndex = 18;
            downloadButton.Text = "Download new version";
            downloadButton.UseVisualStyleBackColor = true;
            downloadButton.Click += OnDownloadButtonClick;
            // 
            // viewReleaseNotesButton
            // 
            viewReleaseNotesButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            viewReleaseNotesButton.Location = new System.Drawing.Point(12, 309);
            viewReleaseNotesButton.Name = "viewReleaseNotesButton";
            viewReleaseNotesButton.Size = new System.Drawing.Size(638, 26);
            viewReleaseNotesButton.TabIndex = 17;
            viewReleaseNotesButton.Text = "View release notes";
            viewReleaseNotesButton.UseVisualStyleBackColor = true;
            viewReleaseNotesButton.Click += OnViewReleaseNotesButtonClick;
            // 
            // newVersionLabel
            // 
            newVersionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            newVersionLabel.AutoSize = true;
            newVersionLabel.Location = new System.Drawing.Point(12, 275);
            newVersionLabel.Name = "newVersionLabel";
            newVersionLabel.Size = new System.Drawing.Size(91, 19);
            newVersionLabel.TabIndex = 16;
            newVersionLabel.Text = "New version: ";
            // 
            // currentVersionLabel
            // 
            currentVersionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            currentVersionLabel.AutoSize = true;
            currentVersionLabel.Location = new System.Drawing.Point(12, 243);
            currentVersionLabel.Name = "currentVersionLabel";
            currentVersionLabel.Size = new System.Drawing.Size(111, 19);
            currentVersionLabel.TabIndex = 15;
            currentVersionLabel.Text = "Current version: ";
            // 
            // AboutForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(662, 423);
            Controls.Add(checkForUpdatesCheckbox);
            Controls.Add(downloadButton);
            Controls.Add(viewReleaseNotesButton);
            Controls.Add(newVersionLabel);
            Controls.Add(currentVersionLabel);
            Controls.Add(copyVersion);
            Controls.Add(keybinds);
            Controls.Add(icon);
            Controls.Add(label3);
            Controls.Add(releases);
            Controls.Add(github);
            Controls.Add(website);
            Controls.Add(label1);
            Font = new System.Drawing.Font("Segoe UI", 10F);
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
            ((System.ComponentModel.ISupportInitialize)icon).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Button website;
        private Button github;
        private Button releases;
        private Label label3;
        private PictureBox icon;
        private Button keybinds;
        private Button copyVersion;
        private CheckBox checkForUpdatesCheckbox;
        private Button downloadButton;
        private Button viewReleaseNotesButton;
        private Label newVersionLabel;
        private Label currentVersionLabel;
    }
}
