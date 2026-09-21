using GUI.Controls;

namespace GUI.Forms
{
    partial class CustomVmdlExtractProgressForm
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
                cancellationTokenSource.Dispose();
                updateTimer.Dispose();
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
            tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            extractProgressBar = new System.Windows.Forms.ProgressBar();
            logTextBox = new System.Windows.Forms.TextBox();
            cancelButton = new ThemedButton();
            tableLayoutPanel1.SuspendLayout();
            SuspendLayout();
            //
            // tableLayoutPanel1
            //
            tableLayoutPanel1.ColumnCount = 1;
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.Controls.Add(extractProgressBar, 0, 0);
            tableLayoutPanel1.Controls.Add(logTextBox, 0, 1);
            tableLayoutPanel1.Controls.Add(cancelButton, 0, 2);
            tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.Padding = new System.Windows.Forms.Padding(12);
            tableLayoutPanel1.RowCount = 3;
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 42F));
            tableLayoutPanel1.Size = new System.Drawing.Size(760, 480);
            tableLayoutPanel1.TabIndex = 0;
            //
            // extractProgressBar
            //
            extractProgressBar.Dock = System.Windows.Forms.DockStyle.Fill;
            extractProgressBar.Margin = new System.Windows.Forms.Padding(0, 0, 0, 8);
            extractProgressBar.Name = "extractProgressBar";
            extractProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            extractProgressBar.TabIndex = 0;
            //
            // logTextBox
            //
            logTextBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            logTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            logTextBox.Font = new System.Drawing.Font("Consolas", 9F);
            logTextBox.Margin = new System.Windows.Forms.Padding(0, 0, 0, 8);
            logTextBox.Multiline = true;
            logTextBox.Name = "logTextBox";
            logTextBox.ReadOnly = true;
            logTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            logTextBox.TabIndex = 1;
            logTextBox.WordWrap = false;
            //
            // cancelButton
            //
            cancelButton.Dock = System.Windows.Forms.DockStyle.Right;
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(88, 26);
            cancelButton.TabIndex = 2;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            cancelButton.Click += CancelButton_Click;
            //
            // CustomVmdlExtractProgressForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(760, 480);
            Controls.Add(tableLayoutPanel1);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            MinimumSize = new System.Drawing.Size(420, 260);
            Name = "CustomVmdlExtractProgressForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Extracting files…";
            tableLayoutPanel1.ResumeLayout(false);
            tableLayoutPanel1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.ProgressBar extractProgressBar;
        private System.Windows.Forms.TextBox logTextBox;
        private System.Windows.Forms.Button cancelButton;
    }
}
