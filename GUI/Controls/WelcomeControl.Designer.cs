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
            splitContainer = new System.Windows.Forms.SplitContainer();
            sidePanel = new System.Windows.Forms.Panel();
            sideLayout = new System.Windows.Forms.TableLayoutPanel();
            headerLayout = new System.Windows.Forms.TableLayoutPanel();
            logo = new System.Windows.Forms.PictureBox();
            nameLabel = new System.Windows.Forms.Label();
            versionLabel = new System.Windows.Forms.Label();
            explorerHeading = new System.Windows.Forms.Label();
            explorerLabel = new System.Windows.Forms.Label();
            updatesHeading = new System.Windows.Forms.Label();
            updatesLabel = new System.Windows.Forms.Label();
            installLayout = new System.Windows.Forms.TableLayoutPanel();
            installHeading = new System.Windows.Forms.Label();
            installLabel = new System.Windows.Forms.Label();
            installButton = new ThemedButton();
            helpLayout = new System.Windows.Forms.TableLayoutPanel();
            helpHeading = new System.Windows.Forms.Label();
            helpLabel = new System.Windows.Forms.Label();
            helpLinks = new System.Windows.Forms.FlowLayoutPanel();
            discordLink = new System.Windows.Forms.LinkLabel();
            issuesLink = new System.Windows.Forms.LinkLabel();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.SuspendLayout();
            sidePanel.SuspendLayout();
            sideLayout.SuspendLayout();
            headerLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)logo).BeginInit();
            installLayout.SuspendLayout();
            helpLayout.SuspendLayout();
            helpLinks.SuspendLayout();
            SuspendLayout();
            // 
            // splitContainer
            // 
            splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer.Location = new System.Drawing.Point(0, 0);
            splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            splitContainer.Panel1.Controls.Add(sidePanel);
            splitContainer.Panel1MinSize = 150;
            splitContainer.Size = new System.Drawing.Size(600, 600);
            splitContainer.SplitterDistance = 300;
            splitContainer.TabIndex = 0;
            // 
            // sidePanel
            // 
            sidePanel.AutoScroll = true;
            sidePanel.Controls.Add(sideLayout);
            sidePanel.Dock = System.Windows.Forms.DockStyle.Fill;
            sidePanel.Location = new System.Drawing.Point(0, 0);
            sidePanel.Name = "sidePanel";
            sidePanel.Padding = new System.Windows.Forms.Padding(20, 20, 20, 15);
            sidePanel.Size = new System.Drawing.Size(300, 600);
            sidePanel.TabIndex = 0;
            // 
            // sideLayout
            // 
            sideLayout.AutoSize = true;
            sideLayout.ColumnCount = 1;
            sideLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            sideLayout.Controls.Add(headerLayout, 0, 0);
            sideLayout.Controls.Add(explorerHeading, 0, 1);
            sideLayout.Controls.Add(explorerLabel, 0, 2);
            sideLayout.Controls.Add(updatesHeading, 0, 3);
            sideLayout.Controls.Add(updatesLabel, 0, 4);
            sideLayout.Controls.Add(installLayout, 0, 5);
            sideLayout.Controls.Add(helpLayout, 0, 6);
            sideLayout.Dock = System.Windows.Forms.DockStyle.Top;
            sideLayout.Location = new System.Drawing.Point(20, 20);
            sideLayout.Name = "sideLayout";
            sideLayout.RowCount = 7;
            sideLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            sideLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            sideLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            sideLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            sideLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            sideLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            sideLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            sideLayout.Size = new System.Drawing.Size(260, 436);
            sideLayout.TabIndex = 0;
            // 
            // headerLayout
            // 
            headerLayout.AutoSize = true;
            headerLayout.ColumnCount = 2;
            headerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            headerLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            headerLayout.Controls.Add(logo, 0, 0);
            headerLayout.Controls.Add(nameLabel, 1, 0);
            headerLayout.Controls.Add(versionLabel, 1, 1);
            headerLayout.Dock = System.Windows.Forms.DockStyle.Top;
            headerLayout.Location = new System.Drawing.Point(0, 0);
            headerLayout.Margin = new System.Windows.Forms.Padding(0);
            headerLayout.Name = "headerLayout";
            headerLayout.RowCount = 2;
            headerLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            headerLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            headerLayout.Size = new System.Drawing.Size(260, 48);
            headerLayout.TabIndex = 0;
            // 
            // logo
            // 
            logo.Anchor = System.Windows.Forms.AnchorStyles.Left;
            logo.Location = new System.Drawing.Point(0, 0);
            logo.Margin = new System.Windows.Forms.Padding(0, 0, 12, 0);
            logo.Name = "logo";
            headerLayout.SetRowSpan(logo, 2);
            logo.Size = new System.Drawing.Size(48, 48);
            logo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            logo.TabIndex = 0;
            logo.TabStop = false;
            // 
            // nameLabel
            // 
            nameLabel.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            nameLabel.AutoSize = true;
            nameLabel.Font = new System.Drawing.Font("Segoe UI", 15F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
            nameLabel.Location = new System.Drawing.Point(60, 0);
            nameLabel.Margin = new System.Windows.Forms.Padding(0);
            nameLabel.Name = "nameLabel";
            nameLabel.Size = new System.Drawing.Size(165, 28);
            nameLabel.TabIndex = 1;
            nameLabel.Text = "Source 2 Viewer";
            // 
            // versionLabel
            // 
            versionLabel.AutoSize = true;
            versionLabel.Location = new System.Drawing.Point(60, 28);
            versionLabel.Margin = new System.Windows.Forms.Padding(0);
            versionLabel.Name = "versionLabel";
            versionLabel.Size = new System.Drawing.Size(0, 15);
            versionLabel.TabIndex = 2;
            // 
            // explorerHeading
            // 
            explorerHeading.AutoSize = true;
            explorerHeading.Dock = System.Windows.Forms.DockStyle.Top;
            explorerHeading.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
            explorerHeading.Location = new System.Drawing.Point(0, 72);
            explorerHeading.Margin = new System.Windows.Forms.Padding(0, 24, 0, 6);
            explorerHeading.Name = "explorerHeading";
            explorerHeading.Size = new System.Drawing.Size(260, 20);
            explorerHeading.TabIndex = 1;
            explorerHeading.Text = "Explorer";
            // 
            // explorerLabel
            // 
            explorerLabel.AutoSize = true;
            explorerLabel.Dock = System.Windows.Forms.DockStyle.Top;
            explorerLabel.Location = new System.Drawing.Point(0, 98);
            explorerLabel.Margin = new System.Windows.Forms.Padding(0);
            explorerLabel.Name = "explorerLabel";
            explorerLabel.Size = new System.Drawing.Size(260, 45);
            explorerLabel.TabIndex = 2;
            explorerLabel.Text = "Your installed Steam games, recent files and bookmarks are in the Explorer on the right. Right-click a file to bookmark it.";
            // 
            // updatesHeading
            // 
            updatesHeading.AutoSize = true;
            updatesHeading.Dock = System.Windows.Forms.DockStyle.Top;
            updatesHeading.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
            updatesHeading.Location = new System.Drawing.Point(0, 167);
            updatesHeading.Margin = new System.Windows.Forms.Padding(0, 24, 0, 6);
            updatesHeading.Name = "updatesHeading";
            updatesHeading.Size = new System.Drawing.Size(260, 20);
            updatesHeading.TabIndex = 3;
            updatesHeading.Text = "Updates";
            // 
            // updatesLabel
            // 
            updatesLabel.AutoSize = true;
            updatesLabel.Dock = System.Windows.Forms.DockStyle.Top;
            updatesLabel.Location = new System.Drawing.Point(0, 193);
            updatesLabel.Margin = new System.Windows.Forms.Padding(0);
            updatesLabel.Name = "updatesLabel";
            updatesLabel.Size = new System.Drawing.Size(260, 30);
            updatesLabel.TabIndex = 4;
            updatesLabel.Text = "Updates are checked daily. Change this in the About menu.";
            // 
            // installLayout
            // 
            installLayout.AutoSize = true;
            installLayout.ColumnCount = 1;
            installLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            installLayout.Controls.Add(installHeading, 0, 0);
            installLayout.Controls.Add(installLabel, 0, 1);
            installLayout.Controls.Add(installButton, 0, 2);
            installLayout.Dock = System.Windows.Forms.DockStyle.Top;
            installLayout.Location = new System.Drawing.Point(0, 223);
            installLayout.Margin = new System.Windows.Forms.Padding(0);
            installLayout.Name = "installLayout";
            installLayout.RowCount = 3;
            installLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            installLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            installLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            installLayout.Size = new System.Drawing.Size(260, 127);
            installLayout.TabIndex = 5;
            // 
            // installHeading
            // 
            installHeading.AutoSize = true;
            installHeading.Dock = System.Windows.Forms.DockStyle.Top;
            installHeading.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
            installHeading.Location = new System.Drawing.Point(0, 24);
            installHeading.Margin = new System.Windows.Forms.Padding(0, 24, 0, 6);
            installHeading.Name = "installHeading";
            installHeading.Size = new System.Drawing.Size(260, 20);
            installHeading.TabIndex = 0;
            installHeading.Text = "Install";
            // 
            // installLabel
            // 
            installLabel.AutoSize = true;
            installLabel.Dock = System.Windows.Forms.DockStyle.Top;
            installLabel.Location = new System.Drawing.Point(0, 50);
            installLabel.Margin = new System.Windows.Forms.Padding(0, 0, 0, 12);
            installLabel.Name = "installLabel";
            installLabel.Size = new System.Drawing.Size(260, 30);
            installLabel.TabIndex = 1;
            installLabel.Text = "Adds a Start menu shortcut and opens .vpk files and vpk: links in Source 2 Viewer.";
            // 
            // installButton
            // 
            installButton.Anchor = System.Windows.Forms.AnchorStyles.Left;
            installButton.AutoSize = true;
            installButton.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            installButton.ClickedBackColor = System.Drawing.Color.Gray;
            installButton.CornerRadius = 5;
            installButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            installButton.HoveredBackColor = System.Drawing.Color.Gray;
            installButton.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            installButton.Location = new System.Drawing.Point(0, 92);
            installButton.Margin = new System.Windows.Forms.Padding(0);
            installButton.MinimumSize = new System.Drawing.Size(100, 34);
            installButton.Name = "installButton";
            installButton.Padding = new System.Windows.Forms.Padding(12, 4, 12, 4);
            installButton.Size = new System.Drawing.Size(100, 35);
            installButton.Style = false;
            installButton.TabIndex = 2;
            installButton.Text = "Install app";
            installButton.UseVisualStyleBackColor = false;
            installButton.Click += installButton_Click;
            // 
            // helpLayout
            // 
            helpLayout.AutoSize = true;
            helpLayout.ColumnCount = 1;
            helpLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            helpLayout.Controls.Add(helpHeading, 0, 0);
            helpLayout.Controls.Add(helpLabel, 0, 1);
            helpLayout.Controls.Add(helpLinks, 0, 2);
            helpLayout.Dock = System.Windows.Forms.DockStyle.Top;
            helpLayout.Location = new System.Drawing.Point(0, 350);
            helpLayout.Margin = new System.Windows.Forms.Padding(0);
            helpLayout.Name = "helpLayout";
            helpLayout.RowCount = 3;
            helpLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            helpLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            helpLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            helpLayout.Size = new System.Drawing.Size(260, 86);
            helpLayout.TabIndex = 6;
            // 
            // helpHeading
            // 
            helpHeading.AutoSize = true;
            helpHeading.Dock = System.Windows.Forms.DockStyle.Top;
            helpHeading.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 0);
            helpHeading.Location = new System.Drawing.Point(0, 24);
            helpHeading.Margin = new System.Windows.Forms.Padding(0, 24, 0, 6);
            helpHeading.Name = "helpHeading";
            helpHeading.Size = new System.Drawing.Size(260, 20);
            helpHeading.TabIndex = 0;
            helpHeading.Text = "Having issues?";
            // 
            // helpLabel
            // 
            helpLabel.AutoSize = true;
            helpLabel.Dock = System.Windows.Forms.DockStyle.Top;
            helpLabel.Location = new System.Drawing.Point(0, 50);
            helpLabel.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            helpLabel.Name = "helpLabel";
            helpLabel.Size = new System.Drawing.Size(260, 15);
            helpLabel.TabIndex = 1;
            helpLabel.Text = "Ask the community or report a bug.";
            // 
            // helpLinks
            // 
            helpLinks.AutoSize = true;
            helpLinks.Controls.Add(discordLink);
            helpLinks.Controls.Add(issuesLink);
            helpLinks.Dock = System.Windows.Forms.DockStyle.Top;
            helpLinks.Location = new System.Drawing.Point(0, 71);
            helpLinks.Margin = new System.Windows.Forms.Padding(0);
            helpLinks.Name = "helpLinks";
            helpLinks.Size = new System.Drawing.Size(260, 15);
            helpLinks.TabIndex = 2;
            // 
            // discordLink
            // 
            discordLink.AutoSize = true;
            discordLink.Location = new System.Drawing.Point(0, 0);
            discordLink.Margin = new System.Windows.Forms.Padding(0, 0, 16, 0);
            discordLink.Name = "discordLink";
            discordLink.Size = new System.Drawing.Size(71, 15);
            discordLink.TabIndex = 0;
            discordLink.TabStop = true;
            discordLink.Text = "Join Discord";
            discordLink.LinkClicked += discordLink_LinkClicked;
            // 
            // issuesLink
            // 
            issuesLink.AutoSize = true;
            issuesLink.Location = new System.Drawing.Point(87, 0);
            issuesLink.Margin = new System.Windows.Forms.Padding(0);
            issuesLink.Name = "issuesLink";
            issuesLink.Size = new System.Drawing.Size(100, 15);
            issuesLink.TabIndex = 1;
            issuesLink.TabStop = true;
            issuesLink.Text = "Report on GitHub";
            issuesLink.LinkClicked += issuesLink_LinkClicked;
            // 
            // WelcomeControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(splitContainer);
            Name = "WelcomeControl";
            Size = new System.Drawing.Size(600, 600);
            splitContainer.Panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            sidePanel.ResumeLayout(false);
            sidePanel.PerformLayout();
            sideLayout.ResumeLayout(false);
            sideLayout.PerformLayout();
            headerLayout.ResumeLayout(false);
            headerLayout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)logo).EndInit();
            installLayout.ResumeLayout(false);
            installLayout.PerformLayout();
            helpLayout.ResumeLayout(false);
            helpLayout.PerformLayout();
            helpLinks.ResumeLayout(false);
            helpLinks.PerformLayout();
            ResumeLayout(false);
        }

        #endregion
        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.Panel sidePanel;
        private System.Windows.Forms.TableLayoutPanel sideLayout;
        private System.Windows.Forms.TableLayoutPanel headerLayout;
        private System.Windows.Forms.PictureBox logo;
        private System.Windows.Forms.Label nameLabel;
        private System.Windows.Forms.Label versionLabel;
        private System.Windows.Forms.Label explorerHeading;
        private System.Windows.Forms.Label explorerLabel;
        private System.Windows.Forms.Label updatesHeading;
        private System.Windows.Forms.Label updatesLabel;
        private System.Windows.Forms.TableLayoutPanel installLayout;
        private System.Windows.Forms.Label installHeading;
        private System.Windows.Forms.Label installLabel;
        private ThemedButton installButton;
        private System.Windows.Forms.TableLayoutPanel helpLayout;
        private System.Windows.Forms.Label helpHeading;
        private System.Windows.Forms.Label helpLabel;
        private System.Windows.Forms.FlowLayoutPanel helpLinks;
        private System.Windows.Forms.LinkLabel discordLink;
        private System.Windows.Forms.LinkLabel issuesLink;
    }
}
