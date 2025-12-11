using GUI.Controls;

namespace GUI.Forms
{
    partial class ExtractProgressForm
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

            if (disposing && cancellationTokenSource != null)
            {
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
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
            cancelButton = new ThemedButton();
            progressLog = new System.Windows.Forms.TextBox();
            tableLayoutPanel1.SuspendLayout();
            SuspendLayout();
            //
            // tableLayoutPanel1
            //
            tableLayoutPanel1.BackColor = System.Drawing.Color.FromArgb(218, 218, 218);
            tableLayoutPanel1.ColumnCount = 1;
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.Controls.Add(extractProgressBar, 0, 0);
            tableLayoutPanel1.Controls.Add(cancelButton, 0, 2);
            tableLayoutPanel1.Controls.Add(progressLog, 0, 1);
            tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel1.ForeColor = System.Drawing.Color.Black;
            tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 3;
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 12F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 76F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 12F));
            tableLayoutPanel1.Size = new System.Drawing.Size(918, 503);
            tableLayoutPanel1.TabIndex = 0;
            //
            // extractProgressBar
            //
            extractProgressBar.BackColor = System.Drawing.Color.FromArgb(218, 218, 218);
            extractProgressBar.Dock = System.Windows.Forms.DockStyle.Fill;
            extractProgressBar.ForeColor = System.Drawing.Color.FromArgb(99, 161, 255);
            extractProgressBar.Location = new System.Drawing.Point(23, 12);
            extractProgressBar.Margin = new System.Windows.Forms.Padding(23, 12, 23, 12);
            extractProgressBar.Name = "extractProgressBar";
            extractProgressBar.Size = new System.Drawing.Size(872, 36);
            extractProgressBar.TabIndex = 0;
            //
            // cancelButton
            //
            cancelButton.BackColor = System.Drawing.Color.FromArgb(230, 230, 230);
            cancelButton.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            cancelButton.CornerRadius = 5;
            cancelButton.Dock = System.Windows.Forms.DockStyle.Right;
            cancelButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            cancelButton.ForeColor = System.Drawing.Color.Black;
            cancelButton.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            cancelButton.Location = new System.Drawing.Point(807, 454);
            cancelButton.Margin = new System.Windows.Forms.Padding(0, 12, 23, 12);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(88, 37);
            cancelButton.Style = true;
            cancelButton.TabIndex = 2;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = false;
            cancelButton.Click += CancelButton_Click;
            //
            // progressLog
            //
            progressLog.BackColor = System.Drawing.Color.FromArgb(218, 218, 218);
            progressLog.BorderStyle = System.Windows.Forms.BorderStyle.None;
            progressLog.Dock = System.Windows.Forms.DockStyle.Fill;
            progressLog.Font = new System.Drawing.Font("Cascadia Mono", 10F);
            progressLog.ForeColor = System.Drawing.Color.Black;
            progressLog.Location = new System.Drawing.Point(23, 60);
            progressLog.Margin = new System.Windows.Forms.Padding(23, 0, 23, 0);
            progressLog.Multiline = true;
            progressLog.Name = "progressLog";
            progressLog.ReadOnly = true;
            progressLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            progressLog.Size = new System.Drawing.Size(872, 382);
            progressLog.TabIndex = 3;
            progressLog.WordWrap = false;
            //
            // ExtractProgressForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(918, 503);
            Controls.Add(tableLayoutPanel1);
            ForeColor = System.Drawing.Color.Black;
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MaximizeBox = false;
            Name = "ExtractProgressForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Source 2 Viewer - Extracting files…";
            tableLayoutPanel1.ResumeLayout(false);
            tableLayoutPanel1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.ProgressBar extractProgressBar;
        private System.Windows.Forms.TextBox progressLog;
        private ThemedButton cancelButton;
    }
}
