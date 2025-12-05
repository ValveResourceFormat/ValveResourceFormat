using System.Windows.Forms;
using GUI.Controls;

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
            website = new ThemedButton();
            github = new ThemedButton();
            label3 = new Label();
            icon = new PictureBox();
            keybinds = new ThemedButton();
            copyVersion = new ThemedButton();
            checkForUpdatesCheckbox = new CheckBox();
            downloadButton = new ThemedButton();
            viewReleaseNotesButton = new ThemedButton();
            newVersionLabelText = new Label();
            currentVersionLabelText = new Label();
            groupBox1 = new ThemedGroupBox();
            tableLayoutPanel1 = new TableLayoutPanel();
            groupBox2 = new ThemedGroupBox();
            newVersionLabel = new Label();
            currentVersionLabel = new Label();
            tableLayoutPanel2 = new TableLayoutPanel();
            ((System.ComponentModel.ISupportInitialize)icon).BeginInit();
            groupBox1.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            groupBox2.SuspendLayout();
            tableLayoutPanel2.SuspendLayout();
            SuspendLayout();
            //
            // website
            //
            website.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            website.Location = new System.Drawing.Point(3, 3);
            website.Name = "website";
            website.Size = new System.Drawing.Size(148, 30);
            website.TabIndex = 4;
            website.Text = "&Website";
            website.UseVisualStyleBackColor = true;
            website.Click += OnWebsiteClick;
            //
            // github
            //
            github.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            github.Location = new System.Drawing.Point(157, 3);
            github.Name = "github";
            github.Size = new System.Drawing.Size(148, 30);
            github.TabIndex = 5;
            github.Text = "&GitHub";
            github.UseVisualStyleBackColor = true;
            github.Click += OnGithubClick;
            //
            // label3
            //
            label3.AutoSize = true;
            label3.Location = new System.Drawing.Point(16, 31);
            label3.Margin = new Padding(4, 0, 4, 0);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(463, 57);
            label3.TabIndex = 10;
            label3.Text = "This is open-source software under the MIT license.\r\nThis project is not affiliated with Valve Software.\r\nSource 2 is a trademark and/or registered trademark of Valve Corporation.";
            //
            // icon
            //
            icon.Image = (System.Drawing.Image)resources.GetObject("icon.Image");
            icon.Location = new System.Drawing.Point(515, 12);
            icon.Name = "icon";
            icon.Size = new System.Drawing.Size(148, 148);
            icon.SizeMode = PictureBoxSizeMode.Zoom;
            icon.TabIndex = 11;
            icon.TabStop = false;
            //
            // keybinds
            //
            keybinds.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            keybinds.Location = new System.Drawing.Point(311, 3);
            keybinds.Name = "keybinds";
            keybinds.Size = new System.Drawing.Size(149, 30);
            keybinds.TabIndex = 12;
            keybinds.Text = "View &keybinds";
            keybinds.UseVisualStyleBackColor = true;
            keybinds.Click += OnKeybindsClick;
            //
            // copyVersion
            //
            copyVersion.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            copyVersion.Location = new System.Drawing.Point(537, 28);
            copyVersion.Name = "copyVersion";
            copyVersion.Size = new System.Drawing.Size(100, 30);
            copyVersion.TabIndex = 13;
            copyVersion.Text = "Copy &version";
            copyVersion.UseVisualStyleBackColor = true;
            copyVersion.Click += OnCopyVersionClick;
            //
            // checkForUpdatesCheckbox
            //
            checkForUpdatesCheckbox.Location = new System.Drawing.Point(16, 136);
            checkForUpdatesCheckbox.Name = "checkForUpdatesCheckbox";
            checkForUpdatesCheckbox.Padding = new Padding(3, 0, 0, 0);
            checkForUpdatesCheckbox.Size = new System.Drawing.Size(618, 30);
            checkForUpdatesCheckbox.TabIndex = 14;
            checkForUpdatesCheckbox.Text = "Automatically check for updates daily";
            checkForUpdatesCheckbox.UseVisualStyleBackColor = true;
            checkForUpdatesCheckbox.CheckedChanged += OnCheckForUpdatesCheckboxChanged;
            //
            // downloadButton
            //
            downloadButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            downloadButton.Location = new System.Drawing.Point(3, 3);
            downloadButton.Name = "downloadButton";
            downloadButton.Size = new System.Drawing.Size(304, 30);
            downloadButton.TabIndex = 18;
            downloadButton.Text = "Download new version";
            downloadButton.UseVisualStyleBackColor = true;
            downloadButton.Click += OnDownloadButtonClick;
            //
            // viewReleaseNotesButton
            //
            viewReleaseNotesButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            viewReleaseNotesButton.Location = new System.Drawing.Point(313, 3);
            viewReleaseNotesButton.Name = "viewReleaseNotesButton";
            viewReleaseNotesButton.Size = new System.Drawing.Size(305, 30);
            viewReleaseNotesButton.TabIndex = 17;
            viewReleaseNotesButton.Text = "View release notes";
            viewReleaseNotesButton.UseVisualStyleBackColor = true;
            viewReleaseNotesButton.Click += OnViewReleaseNotesButtonClick;
            //
            // newVersionLabelText
            //
            newVersionLabelText.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            newVersionLabelText.AutoSize = true;
            newVersionLabelText.Location = new System.Drawing.Point(16, 58);
            newVersionLabelText.Name = "newVersionLabelText";
            newVersionLabelText.Size = new System.Drawing.Size(91, 19);
            newVersionLabelText.TabIndex = 16;
            newVersionLabelText.Text = "New version: ";
            //
            // currentVersionLabelText
            //
            currentVersionLabelText.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            currentVersionLabelText.AutoSize = true;
            currentVersionLabelText.Location = new System.Drawing.Point(16, 28);
            currentVersionLabelText.Name = "currentVersionLabelText";
            currentVersionLabelText.Size = new System.Drawing.Size(111, 19);
            currentVersionLabelText.TabIndex = 15;
            currentVersionLabelText.Text = "Current version: ";
            //
            // groupBox1
            //
            groupBox1.Controls.Add(tableLayoutPanel1);
            groupBox1.Controls.Add(label3);
            groupBox1.Location = new System.Drawing.Point(16, 16);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new System.Drawing.Size(493, 144);
            groupBox1.TabIndex = 19;
            groupBox1.TabStop = false;
            groupBox1.Text = "Source 2 Viewer";
            //
            // tableLayoutPanel1
            //
            tableLayoutPanel1.ColumnCount = 3;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333321F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333321F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333321F));
            tableLayoutPanel1.Controls.Add(website, 0, 0);
            tableLayoutPanel1.Controls.Add(keybinds, 2, 0);
            tableLayoutPanel1.Controls.Add(github, 1, 0);
            tableLayoutPanel1.Location = new System.Drawing.Point(16, 96);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 1;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel1.Size = new System.Drawing.Size(463, 36);
            tableLayoutPanel1.TabIndex = 20;
            //
            // groupBox2
            //
            groupBox2.Controls.Add(newVersionLabel);
            groupBox2.Controls.Add(currentVersionLabel);
            groupBox2.Controls.Add(tableLayoutPanel2);
            groupBox2.Controls.Add(currentVersionLabelText);
            groupBox2.Controls.Add(copyVersion);
            groupBox2.Controls.Add(checkForUpdatesCheckbox);
            groupBox2.Controls.Add(newVersionLabelText);
            groupBox2.Location = new System.Drawing.Point(16, 172);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new System.Drawing.Size(652, 177);
            groupBox2.TabIndex = 20;
            groupBox2.TabStop = false;
            groupBox2.Text = "Version";
            //
            // newVersionLabel
            //
            newVersionLabel.AutoSize = true;
            newVersionLabel.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            newVersionLabel.Location = new System.Drawing.Point(150, 58);
            newVersionLabel.Name = "newVersionLabel";
            newVersionLabel.Size = new System.Drawing.Size(58, 19);
            newVersionLabel.TabIndex = 19;
            newVersionLabel.Text = "version";
            //
            // currentVersionLabel
            //
            currentVersionLabel.AutoSize = true;
            currentVersionLabel.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            currentVersionLabel.Location = new System.Drawing.Point(150, 28);
            currentVersionLabel.Name = "currentVersionLabel";
            currentVersionLabel.Size = new System.Drawing.Size(58, 19);
            currentVersionLabel.TabIndex = 18;
            currentVersionLabel.Text = "version";
            //
            // tableLayoutPanel2
            //
            tableLayoutPanel2.ColumnCount = 2;
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel2.Controls.Add(downloadButton, 0, 0);
            tableLayoutPanel2.Controls.Add(viewReleaseNotesButton, 1, 0);
            tableLayoutPanel2.Location = new System.Drawing.Point(16, 92);
            tableLayoutPanel2.Name = "tableLayoutPanel2";
            tableLayoutPanel2.RowCount = 1;
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel2.Size = new System.Drawing.Size(621, 36);
            tableLayoutPanel2.TabIndex = 17;
            //
            // AboutForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(684, 361);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            Controls.Add(icon);
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
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            tableLayoutPanel1.ResumeLayout(false);
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            tableLayoutPanel2.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        private Button website;
        private Button github;
        private Label label3;
        private PictureBox icon;
        private Button keybinds;
        private Button copyVersion;
        private CheckBox checkForUpdatesCheckbox;
        private Button downloadButton;
        private Button viewReleaseNotesButton;
        private Label newVersionLabelText;
        private Label currentVersionLabelText;
        private ThemedGroupBox groupBox1;
        private TableLayoutPanel tableLayoutPanel1;
        private ThemedGroupBox groupBox2;
        private TableLayoutPanel tableLayoutPanel2;
        private Label newVersionLabel;
        private Label currentVersionLabel;
    }
}
