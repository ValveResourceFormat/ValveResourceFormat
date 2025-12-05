using GUI.Controls;

namespace GUI.Forms
{
    partial class SettingsControl
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
            if (disposing)
            {
                GUI.Utils.Settings.Save();
            }
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
            vsyncCheckBox = new System.Windows.Forms.CheckBox();
            gamePaths = new System.Windows.Forms.ListBox();
            gamePathsAdd = new ThemedButton();
            gamePathsRemove = new ThemedButton();
            gamePathsAddFolder = new ThemedButton();
            maxTextureSizeLabel = new System.Windows.Forms.Label();
            maxTextureSizeInput = new ThemedIntNumeric() { MaxValue = 8192, MinValue = 16 };
            fovInput = new ThemedFloatNumeric() { MaxValue = 170, MinValue = 1 };
            fovLabel = new System.Windows.Forms.Label();
            antiAliasingLabel = new System.Windows.Forms.Label();
            antiAliasingComboBox = new ThemedComboBox();
            registerAssociationButton = new ThemedButton();
            displayFpsCheckBox = new System.Windows.Forms.CheckBox();
            groupBox1 = new ThemedGroupBox();
            groupBox2 = new ThemedGroupBox();
            setFovTo4by3Button = new ThemedButton();
            shadowResolutionInput = new ThemedIntNumeric() { MaxValue = 4096, MinValue = 16 };
            shadowResolutionLabel = new System.Windows.Forms.Label();
            groupBox3 = new ThemedGroupBox();
            textViewerFontSizeLabel = new System.Windows.Forms.Label();
            textViewerFontSize = new ThemedFloatNumeric() { MaxValue = 24, MinValue = 8 };
            openExplorerOnStartCheckbox = new System.Windows.Forms.CheckBox();
            themeComboBox = new ThemedComboBox();
            themeLabel = new System.Windows.Forms.Label();
            groupBox4 = new ThemedGroupBox();
            quickPreviewCheckbox = new System.Windows.Forms.CheckBox();
            quickPreviewSoundsCheckbox = new System.Windows.Forms.CheckBox();
            footerLabel = new System.Windows.Forms.Label();
            footerPanel = new System.Windows.Forms.Panel();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox4.SuspendLayout();
            footerPanel.SuspendLayout();
            SuspendLayout();
            // 
            // vsyncCheckBox
            // 
            vsyncCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            vsyncCheckBox.AutoSize = true;
            vsyncCheckBox.Location = new System.Drawing.Point(15, 227);
            vsyncCheckBox.Name = "vsyncCheckBox";
            vsyncCheckBox.Size = new System.Drawing.Size(104, 23);
            vsyncCheckBox.TabIndex = 8;
            vsyncCheckBox.Text = "Vertical Sync";
            vsyncCheckBox.UseVisualStyleBackColor = true;
            vsyncCheckBox.CheckedChanged += OnVsyncValueChanged;
            // 
            // gamePaths
            // 
            gamePaths.FormattingEnabled = true;
            gamePaths.Location = new System.Drawing.Point(16, 36);
            gamePaths.Margin = new System.Windows.Forms.Padding(0);
            gamePaths.Name = "gamePaths";
            gamePaths.Size = new System.Drawing.Size(501, 123);
            gamePaths.TabIndex = 1;
            // 
            // gamePathsAdd
            // 
            gamePathsAdd.ClickedBackColor = System.Drawing.Color.Gray;
            gamePathsAdd.CornerRadius = 5;
            gamePathsAdd.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            gamePathsAdd.ForceClicked = false;
            gamePathsAdd.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            gamePathsAdd.Location = new System.Drawing.Point(16, 168);
            gamePathsAdd.Margin = new System.Windows.Forms.Padding(0, 9, 8, 9);
            gamePathsAdd.Name = "gamePathsAdd";
            gamePathsAdd.Size = new System.Drawing.Size(212, 30);
            gamePathsAdd.TabIndex = 2;
            gamePathsAdd.Text = "Add .vpk or gameinfo.gi";
            gamePathsAdd.UseVisualStyleBackColor = true;
            gamePathsAdd.Click += GamePathAdd;
            // 
            // gamePathsRemove
            // 
            gamePathsRemove.ClickedBackColor = System.Drawing.Color.Gray;
            gamePathsRemove.CornerRadius = 5;
            gamePathsRemove.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            gamePathsRemove.ForceClicked = false;
            gamePathsRemove.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            gamePathsRemove.Location = new System.Drawing.Point(429, 168);
            gamePathsRemove.Margin = new System.Windows.Forms.Padding(8, 9, 0, 9);
            gamePathsRemove.Name = "gamePathsRemove";
            gamePathsRemove.Size = new System.Drawing.Size(88, 30);
            gamePathsRemove.TabIndex = 4;
            gamePathsRemove.Text = "Remove";
            gamePathsRemove.UseVisualStyleBackColor = true;
            gamePathsRemove.Click += GamePathRemoveClick;
            // 
            // gamePathsAddFolder
            // 
            gamePathsAddFolder.ClickedBackColor = System.Drawing.Color.Gray;
            gamePathsAddFolder.CornerRadius = 5;
            gamePathsAddFolder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            gamePathsAddFolder.ForceClicked = false;
            gamePathsAddFolder.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            gamePathsAddFolder.Location = new System.Drawing.Point(244, 168);
            gamePathsAddFolder.Margin = new System.Windows.Forms.Padding(8, 9, 8, 9);
            gamePathsAddFolder.Name = "gamePathsAddFolder";
            gamePathsAddFolder.Size = new System.Drawing.Size(88, 30);
            gamePathsAddFolder.TabIndex = 3;
            gamePathsAddFolder.Text = "Add folder";
            gamePathsAddFolder.UseVisualStyleBackColor = true;
            gamePathsAddFolder.Click += GamePathAddFolder;
            // 
            // maxTextureSizeLabel
            // 
            maxTextureSizeLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            maxTextureSizeLabel.AutoSize = true;
            maxTextureSizeLabel.Location = new System.Drawing.Point(15, 134);
            maxTextureSizeLabel.Name = "maxTextureSizeLabel";
            maxTextureSizeLabel.Size = new System.Drawing.Size(111, 19);
            maxTextureSizeLabel.TabIndex = 0;
            maxTextureSizeLabel.Text = "Max texture size:";
            // 
            // maxTextureSizeInput
            // 
            maxTextureSizeInput.Anchor = System.Windows.Forms.AnchorStyles.Left;
            maxTextureSizeInput.Increment = 64;
            maxTextureSizeInput.Location = new System.Drawing.Point(170, 132);
            maxTextureSizeInput.Name = "maxTextureSizeInput";
            maxTextureSizeInput.Size = new System.Drawing.Size(100, 25);
            maxTextureSizeInput.TabIndex = 5;
            maxTextureSizeInput.Value = 1024;
            maxTextureSizeInput.ValueChanged += OnMaxTextureSizeValueChanged;
            // 
            // fovInput
            // 
            fovInput.Anchor = System.Windows.Forms.AnchorStyles.Left;
            fovInput.DecimalMax = 6;
            fovInput.Location = new System.Drawing.Point(170, 81);
            fovInput.Name = "fovInput";
            fovInput.Size = new System.Drawing.Size(100, 25);
            fovInput.TabIndex = 6;
            fovInput.Value = 90;
            fovInput.ValueChanged += OnFovValueChanged;
            // 
            // fovLabel
            // 
            fovLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            fovLabel.AutoSize = true;
            fovLabel.Location = new System.Drawing.Point(15, 83);
            fovLabel.Name = "fovLabel";
            fovLabel.Size = new System.Drawing.Size(87, 19);
            fovLabel.TabIndex = 4;
            fovLabel.Text = "Vertical FOV:";
            // 
            // antiAliasingLabel
            // 
            antiAliasingLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            antiAliasingLabel.AutoSize = true;
            antiAliasingLabel.Location = new System.Drawing.Point(15, 36);
            antiAliasingLabel.Name = "antiAliasingLabel";
            antiAliasingLabel.Size = new System.Drawing.Size(88, 19);
            antiAliasingLabel.TabIndex = 6;
            antiAliasingLabel.Text = "Anti-aliasing:";
            // 
            // antiAliasingComboBox
            // 
            antiAliasingComboBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            antiAliasingComboBox.BackColor = System.Drawing.Color.FromArgb(34, 39, 51);
            antiAliasingComboBox.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            antiAliasingComboBox.DropDownBackColor = System.Drawing.Color.FromArgb(34, 39, 51);
            antiAliasingComboBox.DropDownForeColor = System.Drawing.Color.White;
            antiAliasingComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            antiAliasingComboBox.ForeColor = System.Drawing.Color.White;
            antiAliasingComboBox.FormattingEnabled = true;
            antiAliasingComboBox.HeaderColor = System.Drawing.Color.FromArgb(51, 57, 74);
            antiAliasingComboBox.HighlightColor = System.Drawing.Color.FromArgb(99, 161, 255);
            antiAliasingComboBox.Location = new System.Drawing.Point(170, 33);
            antiAliasingComboBox.Name = "antiAliasingComboBox";
            antiAliasingComboBox.Size = new System.Drawing.Size(100, 26);
            antiAliasingComboBox.TabIndex = 7;
            antiAliasingComboBox.SelectedIndexChanged += OnAntiAliasingValueChanged;
            // 
            // registerAssociationButton
            // 
            registerAssociationButton.ClickedBackColor = System.Drawing.Color.Gray;
            registerAssociationButton.CornerRadius = 5;
            registerAssociationButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            registerAssociationButton.ForceClicked = false;
            registerAssociationButton.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            registerAssociationButton.Location = new System.Drawing.Point(15, 174);
            registerAssociationButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            registerAssociationButton.Name = "registerAssociationButton";
            registerAssociationButton.Size = new System.Drawing.Size(208, 30);
            registerAssociationButton.TabIndex = 10;
            registerAssociationButton.Text = "Register .vpk file association";
            registerAssociationButton.UseVisualStyleBackColor = true;
            registerAssociationButton.Click += OnRegisterAssociationButtonClick;
            // 
            // displayFpsCheckBox
            // 
            displayFpsCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            displayFpsCheckBox.AutoSize = true;
            displayFpsCheckBox.Location = new System.Drawing.Point(15, 274);
            displayFpsCheckBox.Name = "displayFpsCheckBox";
            displayFpsCheckBox.Size = new System.Drawing.Size(98, 23);
            displayFpsCheckBox.TabIndex = 9;
            displayFpsCheckBox.Text = "Display FPS";
            displayFpsCheckBox.UseVisualStyleBackColor = true;
            displayFpsCheckBox.CheckedChanged += OnDisplayFpsValueChanged;
            // 
            // groupBox1
            // 
            groupBox1.AutoSize = true;
            groupBox1.BorderColor = System.Drawing.Color.Black;
            groupBox1.BorderWidth = 2;
            groupBox1.Controls.Add(gamePathsRemove);
            groupBox1.Controls.Add(gamePathsAddFolder);
            groupBox1.Controls.Add(gamePathsAdd);
            groupBox1.Controls.Add(gamePaths);
            groupBox1.CornerRadius = 5;
            groupBox1.Dock = System.Windows.Forms.DockStyle.Top;
            groupBox1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            groupBox1.Location = new System.Drawing.Point(16, 18);
            groupBox1.Name = "groupBox1";
            groupBox1.Padding = new System.Windows.Forms.Padding(16, 18, 16, 18);
            groupBox1.Size = new System.Drawing.Size(501, 243);
            groupBox1.TabIndex = 1;
            groupBox1.TabStop = false;
            groupBox1.Text = "Game content search paths";
            // 
            // groupBox2
            // 
            groupBox2.AutoSize = true;
            groupBox2.BorderColor = System.Drawing.Color.Black;
            groupBox2.BorderWidth = 2;
            groupBox2.Controls.Add(displayFpsCheckBox);
            groupBox2.Controls.Add(setFovTo4by3Button);
            groupBox2.Controls.Add(vsyncCheckBox);
            groupBox2.Controls.Add(fovInput);
            groupBox2.Controls.Add(fovLabel);
            groupBox2.Controls.Add(shadowResolutionInput);
            groupBox2.Controls.Add(shadowResolutionLabel);
            groupBox2.Controls.Add(antiAliasingLabel);
            groupBox2.Controls.Add(antiAliasingComboBox);
            groupBox2.Controls.Add(maxTextureSizeLabel);
            groupBox2.Controls.Add(maxTextureSizeInput);
            groupBox2.CornerRadius = 5;
            groupBox2.Dock = System.Windows.Forms.DockStyle.Top;
            groupBox2.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            groupBox2.Location = new System.Drawing.Point(16, 261);
            groupBox2.Name = "groupBox2";
            groupBox2.Padding = new System.Windows.Forms.Padding(16, 18, 16, 18);
            groupBox2.Size = new System.Drawing.Size(501, 336);
            groupBox2.TabIndex = 2;
            groupBox2.TabStop = false;
            groupBox2.Text = "Video settings";
            // 
            // setFovTo4by3Button
            // 
            setFovTo4by3Button.Anchor = System.Windows.Forms.AnchorStyles.Left;
            setFovTo4by3Button.ClickedBackColor = System.Drawing.Color.Gray;
            setFovTo4by3Button.CornerRadius = 5;
            setFovTo4by3Button.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            setFovTo4by3Button.ForceClicked = false;
            setFovTo4by3Button.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            setFovTo4by3Button.Location = new System.Drawing.Point(293, 78);
            setFovTo4by3Button.Name = "setFovTo4by3Button";
            setFovTo4by3Button.Size = new System.Drawing.Size(39, 26);
            setFovTo4by3Button.TabIndex = 7;
            setFovTo4by3Button.Text = "4:3";
            setFovTo4by3Button.UseVisualStyleBackColor = true;
            setFovTo4by3Button.Click += OnSetFovTo4by3ButtonClick;
            // 
            // shadowResolutionInput
            // 
            shadowResolutionInput.Anchor = System.Windows.Forms.AnchorStyles.Left;
            shadowResolutionInput.Increment = 64;
            shadowResolutionInput.Location = new System.Drawing.Point(170, 180);
            shadowResolutionInput.Name = "shadowResolutionInput";
            shadowResolutionInput.Size = new System.Drawing.Size(100, 25);
            shadowResolutionInput.TabIndex = 12;
            shadowResolutionInput.Value = 2048;
            // 
            // shadowResolutionLabel
            // 
            shadowResolutionLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            shadowResolutionLabel.AutoSize = true;
            shadowResolutionLabel.Location = new System.Drawing.Point(15, 182);
            shadowResolutionLabel.Name = "shadowResolutionLabel";
            shadowResolutionLabel.Size = new System.Drawing.Size(125, 19);
            shadowResolutionLabel.TabIndex = 11;
            shadowResolutionLabel.Text = "Shadow resolution:";
            // 
            // groupBox3
            // 
            groupBox3.AutoSize = true;
            groupBox3.BorderColor = System.Drawing.Color.Black;
            groupBox3.BorderWidth = 2;
            groupBox3.Controls.Add(textViewerFontSizeLabel);
            groupBox3.Controls.Add(registerAssociationButton);
            groupBox3.Controls.Add(textViewerFontSize);
            groupBox3.Controls.Add(openExplorerOnStartCheckbox);
            groupBox3.Controls.Add(themeComboBox);
            groupBox3.Controls.Add(themeLabel);
            groupBox3.CornerRadius = 5;
            groupBox3.Dock = System.Windows.Forms.DockStyle.Top;
            groupBox3.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            groupBox3.Location = new System.Drawing.Point(16, 735);
            groupBox3.Name = "groupBox3";
            groupBox3.Padding = new System.Windows.Forms.Padding(16, 18, 16, 18);
            groupBox3.Size = new System.Drawing.Size(501, 243);
            groupBox3.TabIndex = 3;
            groupBox3.TabStop = false;
            groupBox3.Text = "Explorer";
            // 
            // textViewerFontSizeLabel
            // 
            textViewerFontSizeLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            textViewerFontSizeLabel.AutoSize = true;
            textViewerFontSizeLabel.Location = new System.Drawing.Point(15, 78);
            textViewerFontSizeLabel.Name = "textViewerFontSizeLabel";
            textViewerFontSizeLabel.Size = new System.Drawing.Size(134, 19);
            textViewerFontSizeLabel.TabIndex = 15;
            textViewerFontSizeLabel.Text = "Text viewer font size:";
            // 
            // textViewerFontSize
            // 
            textViewerFontSize.Anchor = System.Windows.Forms.AnchorStyles.Left;
            textViewerFontSize.Location = new System.Drawing.Point(209, 76);
            textViewerFontSize.Name = "textViewerFontSize";
            textViewerFontSize.Size = new System.Drawing.Size(100, 25);
            textViewerFontSize.TabIndex = 16;
            textViewerFontSize.Value = 8;
            textViewerFontSize.ValueChanged += OnTextViewerFontSizeValueChanged;
            // 
            // openExplorerOnStartCheckbox
            // 
            openExplorerOnStartCheckbox.AutoSize = true;
            openExplorerOnStartCheckbox.Location = new System.Drawing.Point(15, 124);
            openExplorerOnStartCheckbox.Name = "openExplorerOnStartCheckbox";
            openExplorerOnStartCheckbox.Size = new System.Drawing.Size(167, 23);
            openExplorerOnStartCheckbox.TabIndex = 12;
            openExplorerOnStartCheckbox.Text = "Open explorer on start";
            openExplorerOnStartCheckbox.UseVisualStyleBackColor = true;
            openExplorerOnStartCheckbox.CheckedChanged += OnOpenExplorerOnStartValueChanged;
            // 
            // themeComboBox
            // 
            themeComboBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            themeComboBox.BackColor = System.Drawing.Color.FromArgb(34, 39, 51);
            themeComboBox.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            themeComboBox.DropDownBackColor = System.Drawing.Color.FromArgb(34, 39, 51);
            themeComboBox.DropDownForeColor = System.Drawing.Color.White;
            themeComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            themeComboBox.ForeColor = System.Drawing.Color.White;
            themeComboBox.FormattingEnabled = true;
            themeComboBox.HeaderColor = System.Drawing.Color.FromArgb(51, 57, 74);
            themeComboBox.HighlightColor = System.Drawing.Color.FromArgb(99, 161, 255);
            themeComboBox.Location = new System.Drawing.Point(209, 29);
            themeComboBox.Name = "themeComboBox";
            themeComboBox.Size = new System.Drawing.Size(100, 26);
            themeComboBox.TabIndex = 14;
            themeComboBox.SelectedIndexChanged += OnThemeSelectedIndexChanged;
            // 
            // themeLabel
            // 
            themeLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            themeLabel.AutoSize = true;
            themeLabel.Location = new System.Drawing.Point(15, 32);
            themeLabel.Name = "themeLabel";
            themeLabel.Size = new System.Drawing.Size(158, 19);
            themeLabel.TabIndex = 13;
            themeLabel.Text = "Theme (requires restart):";
            // 
            // groupBox4
            // 
            groupBox4.AutoSize = true;
            groupBox4.BorderColor = System.Drawing.Color.Black;
            groupBox4.BorderWidth = 2;
            groupBox4.Controls.Add(quickPreviewCheckbox);
            groupBox4.Controls.Add(quickPreviewSoundsCheckbox);
            groupBox4.CornerRadius = 5;
            groupBox4.Dock = System.Windows.Forms.DockStyle.Top;
            groupBox4.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            groupBox4.Location = new System.Drawing.Point(16, 597);
            groupBox4.Name = "groupBox4";
            groupBox4.Padding = new System.Windows.Forms.Padding(16, 18, 16, 16);
            groupBox4.Size = new System.Drawing.Size(501, 138);
            groupBox4.TabIndex = 4;
            groupBox4.TabStop = false;
            groupBox4.Text = "Quick file preview";
            // 
            // quickPreviewCheckbox
            // 
            quickPreviewCheckbox.AutoSize = true;
            quickPreviewCheckbox.Location = new System.Drawing.Point(16, 39);
            quickPreviewCheckbox.Name = "quickPreviewCheckbox";
            quickPreviewCheckbox.Size = new System.Drawing.Size(191, 23);
            quickPreviewCheckbox.TabIndex = 2;
            quickPreviewCheckbox.Text = "Preview files after selecting";
            quickPreviewCheckbox.UseVisualStyleBackColor = true;
            quickPreviewCheckbox.CheckedChanged += OnQuickPreviewCheckboxChanged;
            // 
            // quickPreviewSoundsCheckbox
            // 
            quickPreviewSoundsCheckbox.AutoSize = true;
            quickPreviewSoundsCheckbox.Location = new System.Drawing.Point(16, 78);
            quickPreviewSoundsCheckbox.Name = "quickPreviewSoundsCheckbox";
            quickPreviewSoundsCheckbox.Size = new System.Drawing.Size(135, 23);
            quickPreviewSoundsCheckbox.TabIndex = 3;
            quickPreviewSoundsCheckbox.Text = "Auto play sounds";
            quickPreviewSoundsCheckbox.UseVisualStyleBackColor = true;
            quickPreviewSoundsCheckbox.CheckedChanged += OnQuickPreviewSoundsCheckboxChanged;
            // 
            // footerLabel
            // 
            footerLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            footerLabel.ForeColor = System.Drawing.SystemColors.InactiveCaption;
            footerLabel.Location = new System.Drawing.Point(0, 0);
            footerLabel.Name = "footerLabel";
            footerLabel.Size = new System.Drawing.Size(501, 100);
            footerLabel.TabIndex = 5;
            footerLabel.Text = "No regrets, Mr. Freeman";
            footerLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // footerPanel
            // 
            footerPanel.Controls.Add(footerLabel);
            footerPanel.Dock = System.Windows.Forms.DockStyle.Top;
            footerPanel.Location = new System.Drawing.Point(16, 978);
            footerPanel.Name = "footerPanel";
            footerPanel.Size = new System.Drawing.Size(501, 100);
            footerPanel.TabIndex = 6;
            // 
            // SettingsControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            AutoScroll = true;
            Controls.Add(footerPanel);
            Controls.Add(groupBox3);
            Controls.Add(groupBox4);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            Font = new System.Drawing.Font("Segoe UI", 10F);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MinimumSize = new System.Drawing.Size(300, 221);
            Name = "SettingsControl";
            Padding = new System.Windows.Forms.Padding(16, 18, 16, 18);
            Size = new System.Drawing.Size(533, 648);
            Load += SettingsControl_Load;
            Leave += SettingsControl_Leave;
            groupBox1.ResumeLayout(false);
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            footerPanel.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ListBox gamePaths;
        private System.Windows.Forms.Label maxTextureSizeLabel;
        private ThemedIntNumeric maxTextureSizeInput;
        private ThemedFloatNumeric fovInput;
        private System.Windows.Forms.Label fovLabel;
        private System.Windows.Forms.Label antiAliasingLabel;
        private System.Windows.Forms.CheckBox vsyncCheckBox;
        private System.Windows.Forms.CheckBox displayFpsCheckBox;
        private System.Windows.Forms.CheckBox quickPreviewCheckbox;
        private System.Windows.Forms.CheckBox quickPreviewSoundsCheckbox;
        private System.Windows.Forms.CheckBox openExplorerOnStartCheckbox;
        private System.Windows.Forms.Label shadowResolutionLabel;
        private ThemedIntNumeric shadowResolutionInput;
        private System.Windows.Forms.Label themeLabel;
        private System.Windows.Forms.Label textViewerFontSizeLabel;
        private ThemedFloatNumeric textViewerFontSize;
        private System.Windows.Forms.Label footerLabel;
        private System.Windows.Forms.Panel footerPanel;
        private ThemedButton gamePathsAdd;
        private ThemedButton gamePathsRemove;
        private ThemedButton gamePathsAddFolder;
        private ThemedComboBox antiAliasingComboBox;
        private ThemedButton registerAssociationButton;
        private ThemedButton setFovTo4by3Button;
        private ThemedComboBox themeComboBox;
        private ThemedGroupBox groupBox1;
        private ThemedGroupBox groupBox2;
        private ThemedGroupBox groupBox3;
        private ThemedGroupBox groupBox4;
    }
}
