using GUI.Controls;

namespace GUI.Forms
{
    partial class CharacterSelectForm
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

            if (disposing)
            {
                previewViewer?.Dispose();
                previewViewer = null;
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
            components = new System.ComponentModel.Container();
            mainTable = new System.Windows.Forms.TableLayoutPanel();
            previewPanel = new System.Windows.Forms.Panel();
            previewStatusLabel = new System.Windows.Forms.Label();
            controlsTable = new System.Windows.Forms.TableLayoutPanel();
            heroNavigationTable = new System.Windows.Forms.TableLayoutPanel();
            previousHeroButton = new ThemedButton();
            heroNameLabel = new System.Windows.Forms.Label();
            nextHeroButton = new ThemedButton();
            heroSearchTextBox = new ThemedTextBox();
            itemSetLabel = new System.Windows.Forms.Label();
            itemSetComboBox = new ThemedComboBox();
            itemsGroupBox = new ThemedGroupBox();
            slotsTable = new System.Windows.Forms.TableLayoutPanel();
            includeGroupBox = new ThemedGroupBox();
            includeTable = new System.Windows.Forms.TableLayoutPanel();
            heroModelCheckBox = new System.Windows.Forms.CheckBox();
            itemModelsCheckBox = new System.Windows.Forms.CheckBox();
            itemParticlesCheckBox = new System.Windows.Forms.CheckBox();
            heroParticlesCheckBox = new System.Windows.Forms.CheckBox();
            itemSoundsCheckBox = new System.Windows.Forms.CheckBox();
            heroSoundsCheckBox = new System.Windows.Forms.CheckBox();
            heroVoiceCheckBox = new System.Windows.Forms.CheckBox();
            iconsCheckBox = new System.Windows.Forms.CheckBox();
            buttonsTable = new System.Windows.Forms.TableLayoutPanel();
            summaryLabel = new System.Windows.Forms.Label();
            cancelButton = new ThemedButton();
            exportButton = new ThemedButton();
            previewTimer = new System.Windows.Forms.Timer(components);
            toolTip = new System.Windows.Forms.ToolTip(components);
            mainTable.SuspendLayout();
            previewPanel.SuspendLayout();
            controlsTable.SuspendLayout();
            heroNavigationTable.SuspendLayout();
            itemsGroupBox.SuspendLayout();
            includeGroupBox.SuspendLayout();
            includeTable.SuspendLayout();
            buttonsTable.SuspendLayout();
            SuspendLayout();
            //
            // mainTable
            //
            mainTable.ColumnCount = 2;
            mainTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            mainTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 440F));
            mainTable.Controls.Add(previewPanel, 0, 0);
            mainTable.Controls.Add(controlsTable, 1, 0);
            mainTable.Dock = System.Windows.Forms.DockStyle.Fill;
            mainTable.Location = new System.Drawing.Point(0, 0);
            mainTable.Name = "mainTable";
            mainTable.Padding = new System.Windows.Forms.Padding(8);
            mainTable.RowCount = 1;
            mainTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            mainTable.Size = new System.Drawing.Size(1184, 781);
            mainTable.TabIndex = 0;
            //
            // previewPanel
            //
            previewPanel.Controls.Add(previewStatusLabel);
            previewPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            previewPanel.Location = new System.Drawing.Point(11, 11);
            previewPanel.Name = "previewPanel";
            previewPanel.Size = new System.Drawing.Size(722, 759);
            previewPanel.TabIndex = 0;
            //
            // previewStatusLabel
            //
            previewStatusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            previewStatusLabel.Location = new System.Drawing.Point(0, 0);
            previewStatusLabel.Name = "previewStatusLabel";
            previewStatusLabel.Size = new System.Drawing.Size(722, 759);
            previewStatusLabel.TabIndex = 0;
            previewStatusLabel.Text = "Loading preview...";
            previewStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // controlsTable
            //
            controlsTable.ColumnCount = 1;
            controlsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            controlsTable.Controls.Add(heroNavigationTable, 0, 0);
            controlsTable.Controls.Add(heroSearchTextBox, 0, 1);
            controlsTable.Controls.Add(itemSetLabel, 0, 2);
            controlsTable.Controls.Add(itemSetComboBox, 0, 3);
            controlsTable.Controls.Add(itemsGroupBox, 0, 4);
            controlsTable.Controls.Add(includeGroupBox, 0, 5);
            controlsTable.Controls.Add(buttonsTable, 0, 6);
            controlsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            controlsTable.Location = new System.Drawing.Point(739, 11);
            controlsTable.Name = "controlsTable";
            controlsTable.RowCount = 7;
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            controlsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            controlsTable.Size = new System.Drawing.Size(434, 759);
            controlsTable.TabIndex = 1;
            //
            // heroNavigationTable
            //
            heroNavigationTable.AutoSize = true;
            heroNavigationTable.ColumnCount = 3;
            heroNavigationTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            heroNavigationTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            heroNavigationTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            heroNavigationTable.Controls.Add(previousHeroButton, 0, 0);
            heroNavigationTable.Controls.Add(heroNameLabel, 1, 0);
            heroNavigationTable.Controls.Add(nextHeroButton, 2, 0);
            heroNavigationTable.Dock = System.Windows.Forms.DockStyle.Fill;
            heroNavigationTable.Location = new System.Drawing.Point(3, 3);
            heroNavigationTable.Name = "heroNavigationTable";
            heroNavigationTable.RowCount = 1;
            heroNavigationTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            heroNavigationTable.Size = new System.Drawing.Size(428, 48);
            heroNavigationTable.TabIndex = 0;
            //
            // previousHeroButton
            //
            previousHeroButton.Dock = System.Windows.Forms.DockStyle.Fill;
            previousHeroButton.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            previousHeroButton.Location = new System.Drawing.Point(3, 3);
            previousHeroButton.Name = "previousHeroButton";
            previousHeroButton.Size = new System.Drawing.Size(42, 42);
            previousHeroButton.TabIndex = 0;
            previousHeroButton.Text = "<";
            previousHeroButton.UseVisualStyleBackColor = false;
            previousHeroButton.Click += PreviousHeroButton_Click;
            //
            // heroNameLabel
            //
            heroNameLabel.AutoEllipsis = true;
            heroNameLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            heroNameLabel.Font = new System.Drawing.Font("Segoe UI", 16F, System.Drawing.FontStyle.Bold);
            heroNameLabel.Location = new System.Drawing.Point(51, 0);
            heroNameLabel.Name = "heroNameLabel";
            heroNameLabel.Size = new System.Drawing.Size(326, 48);
            heroNameLabel.TabIndex = 1;
            heroNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // nextHeroButton
            //
            nextHeroButton.Dock = System.Windows.Forms.DockStyle.Fill;
            nextHeroButton.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            nextHeroButton.Location = new System.Drawing.Point(383, 3);
            nextHeroButton.Name = "nextHeroButton";
            nextHeroButton.Size = new System.Drawing.Size(42, 42);
            nextHeroButton.TabIndex = 2;
            nextHeroButton.Text = ">";
            nextHeroButton.UseVisualStyleBackColor = false;
            nextHeroButton.Click += NextHeroButton_Click;
            //
            // heroSearchTextBox
            //
            heroSearchTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            heroSearchTextBox.Location = new System.Drawing.Point(3, 57);
            heroSearchTextBox.Name = "heroSearchTextBox";
            heroSearchTextBox.PlaceholderText = "Search heroes";
            heroSearchTextBox.Size = new System.Drawing.Size(428, 25);
            heroSearchTextBox.TabIndex = 1;
            heroSearchTextBox.TextChanged += HeroSearchTextBox_TextChanged;
            //
            // itemSetLabel
            //
            itemSetLabel.AutoSize = true;
            itemSetLabel.Location = new System.Drawing.Point(3, 91);
            itemSetLabel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            itemSetLabel.Name = "itemSetLabel";
            itemSetLabel.Size = new System.Drawing.Size(58, 19);
            itemSetLabel.TabIndex = 2;
            itemSetLabel.Text = "Item set";
            //
            // itemSetComboBox
            //
            itemSetComboBox.Dock = System.Windows.Forms.DockStyle.Fill;
            itemSetComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            itemSetComboBox.Location = new System.Drawing.Point(3, 113);
            itemSetComboBox.MaxDropDownItems = 20;
            itemSetComboBox.Name = "itemSetComboBox";
            itemSetComboBox.Size = new System.Drawing.Size(428, 26);
            itemSetComboBox.TabIndex = 3;
            itemSetComboBox.SelectedIndexChanged += ItemSetComboBox_SelectedIndexChanged;
            //
            // itemsGroupBox
            //
            itemsGroupBox.Controls.Add(slotsTable);
            itemsGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            itemsGroupBox.Location = new System.Drawing.Point(3, 145);
            itemsGroupBox.Name = "itemsGroupBox";
            itemsGroupBox.Size = new System.Drawing.Size(428, 399);
            itemsGroupBox.TabIndex = 4;
            itemsGroupBox.TabStop = false;
            itemsGroupBox.Text = "Individual items";
            //
            // slotsTable
            //
            slotsTable.AutoScroll = true;
            slotsTable.ColumnCount = 2;
            slotsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            slotsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            slotsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            slotsTable.Location = new System.Drawing.Point(3, 21);
            slotsTable.Name = "slotsTable";
            slotsTable.RowCount = 0;
            slotsTable.Size = new System.Drawing.Size(422, 375);
            slotsTable.TabIndex = 0;
            //
            // includeGroupBox
            //
            includeGroupBox.AutoSize = true;
            includeGroupBox.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            includeGroupBox.Controls.Add(includeTable);
            includeGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            includeGroupBox.Location = new System.Drawing.Point(3, 550);
            includeGroupBox.Name = "includeGroupBox";
            includeGroupBox.Size = new System.Drawing.Size(428, 157);
            includeGroupBox.TabIndex = 5;
            includeGroupBox.TabStop = false;
            includeGroupBox.Text = "Export";
            //
            // includeTable
            //
            includeTable.AutoSize = true;
            includeTable.ColumnCount = 2;
            includeTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            includeTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            includeTable.Controls.Add(heroModelCheckBox, 0, 0);
            includeTable.Controls.Add(itemModelsCheckBox, 1, 0);
            includeTable.Controls.Add(itemParticlesCheckBox, 0, 1);
            includeTable.Controls.Add(heroParticlesCheckBox, 1, 1);
            includeTable.Controls.Add(itemSoundsCheckBox, 0, 2);
            includeTable.Controls.Add(heroSoundsCheckBox, 1, 2);
            includeTable.Controls.Add(heroVoiceCheckBox, 0, 3);
            includeTable.Controls.Add(iconsCheckBox, 1, 3);
            includeTable.Dock = System.Windows.Forms.DockStyle.Top;
            includeTable.Location = new System.Drawing.Point(3, 21);
            includeTable.Name = "includeTable";
            includeTable.RowCount = 4;
            includeTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            includeTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            includeTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            includeTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            includeTable.Size = new System.Drawing.Size(422, 116);
            includeTable.TabIndex = 0;
            //
            // heroModelCheckBox
            //
            heroModelCheckBox.AutoSize = true;
            heroModelCheckBox.Checked = true;
            heroModelCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            heroModelCheckBox.Name = "heroModelCheckBox";
            heroModelCheckBox.TabIndex = 0;
            heroModelCheckBox.Text = "Hero base model";
            heroModelCheckBox.UseVisualStyleBackColor = true;
            //
            // itemModelsCheckBox
            //
            itemModelsCheckBox.AutoSize = true;
            itemModelsCheckBox.Checked = true;
            itemModelsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            itemModelsCheckBox.Name = "itemModelsCheckBox";
            itemModelsCheckBox.TabIndex = 1;
            itemModelsCheckBox.Text = "Item models";
            itemModelsCheckBox.UseVisualStyleBackColor = true;
            //
            // itemParticlesCheckBox
            //
            itemParticlesCheckBox.AutoSize = true;
            itemParticlesCheckBox.Checked = true;
            itemParticlesCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            itemParticlesCheckBox.Name = "itemParticlesCheckBox";
            itemParticlesCheckBox.TabIndex = 2;
            itemParticlesCheckBox.Text = "Item particles";
            itemParticlesCheckBox.UseVisualStyleBackColor = true;
            //
            // heroParticlesCheckBox
            //
            heroParticlesCheckBox.AutoSize = true;
            heroParticlesCheckBox.Name = "heroParticlesCheckBox";
            heroParticlesCheckBox.TabIndex = 3;
            heroParticlesCheckBox.Text = "All hero particles";
            heroParticlesCheckBox.UseVisualStyleBackColor = true;
            //
            // itemSoundsCheckBox
            //
            itemSoundsCheckBox.AutoSize = true;
            itemSoundsCheckBox.Checked = true;
            itemSoundsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            itemSoundsCheckBox.Name = "itemSoundsCheckBox";
            itemSoundsCheckBox.TabIndex = 4;
            itemSoundsCheckBox.Text = "Item sounds";
            itemSoundsCheckBox.UseVisualStyleBackColor = true;
            //
            // heroSoundsCheckBox
            //
            heroSoundsCheckBox.AutoSize = true;
            heroSoundsCheckBox.Checked = true;
            heroSoundsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            heroSoundsCheckBox.Name = "heroSoundsCheckBox";
            heroSoundsCheckBox.TabIndex = 5;
            heroSoundsCheckBox.Text = "Hero sounds";
            heroSoundsCheckBox.UseVisualStyleBackColor = true;
            //
            // heroVoiceCheckBox
            //
            heroVoiceCheckBox.AutoSize = true;
            heroVoiceCheckBox.Checked = true;
            heroVoiceCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            heroVoiceCheckBox.Name = "heroVoiceCheckBox";
            heroVoiceCheckBox.TabIndex = 6;
            heroVoiceCheckBox.Text = "Voice lines";
            heroVoiceCheckBox.UseVisualStyleBackColor = true;
            //
            // iconsCheckBox
            //
            iconsCheckBox.AutoSize = true;
            iconsCheckBox.Checked = true;
            iconsCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            iconsCheckBox.Name = "iconsCheckBox";
            iconsCheckBox.TabIndex = 7;
            iconsCheckBox.Text = "Panorama icons";
            iconsCheckBox.UseVisualStyleBackColor = true;
            //
            // buttonsTable
            //
            buttonsTable.AutoSize = true;
            buttonsTable.ColumnCount = 3;
            buttonsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            buttonsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            buttonsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            buttonsTable.Controls.Add(summaryLabel, 0, 0);
            buttonsTable.Controls.Add(cancelButton, 1, 0);
            buttonsTable.Controls.Add(exportButton, 2, 0);
            buttonsTable.Dock = System.Windows.Forms.DockStyle.Fill;
            buttonsTable.Location = new System.Drawing.Point(3, 713);
            buttonsTable.Name = "buttonsTable";
            buttonsTable.RowCount = 1;
            buttonsTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            buttonsTable.Size = new System.Drawing.Size(428, 43);
            buttonsTable.TabIndex = 6;
            //
            // summaryLabel
            //
            summaryLabel.AutoEllipsis = true;
            summaryLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            summaryLabel.Name = "summaryLabel";
            summaryLabel.TabIndex = 0;
            summaryLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // cancelButton
            //
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(96, 37);
            cancelButton.TabIndex = 1;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = false;
            //
            // exportButton
            //
            exportButton.Name = "exportButton";
            exportButton.Size = new System.Drawing.Size(96, 37);
            exportButton.TabIndex = 2;
            exportButton.Text = "Export...";
            exportButton.UseVisualStyleBackColor = false;
            exportButton.Click += ExportButton_Click;
            //
            // previewTimer
            //
            previewTimer.Interval = 250;
            previewTimer.Tick += PreviewTimer_Tick;
            //
            // CharacterSelectForm
            //
            AcceptButton = exportButton;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(1184, 781);
            Controls.Add(mainTable);
            Font = new System.Drawing.Font("Segoe UI", 10F);
            MinimumSize = new System.Drawing.Size(900, 640);
            Name = "CharacterSelectForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Choose Character";
            mainTable.ResumeLayout(false);
            previewPanel.ResumeLayout(false);
            controlsTable.ResumeLayout(false);
            controlsTable.PerformLayout();
            heroNavigationTable.ResumeLayout(false);
            itemsGroupBox.ResumeLayout(false);
            includeGroupBox.ResumeLayout(false);
            includeGroupBox.PerformLayout();
            includeTable.ResumeLayout(false);
            includeTable.PerformLayout();
            buttonsTable.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel mainTable;
        private System.Windows.Forms.Panel previewPanel;
        private System.Windows.Forms.Label previewStatusLabel;
        private System.Windows.Forms.TableLayoutPanel controlsTable;
        private System.Windows.Forms.TableLayoutPanel heroNavigationTable;
        private ThemedButton previousHeroButton;
        private System.Windows.Forms.Label heroNameLabel;
        private ThemedButton nextHeroButton;
        private ThemedTextBox heroSearchTextBox;
        private System.Windows.Forms.Label itemSetLabel;
        private ThemedComboBox itemSetComboBox;
        private ThemedGroupBox itemsGroupBox;
        private System.Windows.Forms.TableLayoutPanel slotsTable;
        private ThemedGroupBox includeGroupBox;
        private System.Windows.Forms.TableLayoutPanel includeTable;
        private System.Windows.Forms.CheckBox heroModelCheckBox;
        private System.Windows.Forms.CheckBox itemModelsCheckBox;
        private System.Windows.Forms.CheckBox itemParticlesCheckBox;
        private System.Windows.Forms.CheckBox heroParticlesCheckBox;
        private System.Windows.Forms.CheckBox itemSoundsCheckBox;
        private System.Windows.Forms.CheckBox heroSoundsCheckBox;
        private System.Windows.Forms.CheckBox heroVoiceCheckBox;
        private System.Windows.Forms.CheckBox iconsCheckBox;
        private System.Windows.Forms.TableLayoutPanel buttonsTable;
        private System.Windows.Forms.Label summaryLabel;
        private ThemedButton cancelButton;
        private ThemedButton exportButton;
        private System.Windows.Forms.Timer previewTimer;
        private System.Windows.Forms.ToolTip toolTip;
    }
}
