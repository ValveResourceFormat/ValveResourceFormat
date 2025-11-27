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
            cancelButton = new BetterButton();
            progressLog = new System.Windows.Forms.TextBox();
            tableLayoutPanel1.SuspendLayout();
            SuspendLayout();
            //
            // tableLayoutPanel1
            //
            tableLayoutPanel1.ColumnCount = 1;
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.Controls.Add(extractProgressBar, 0, 0);
            tableLayoutPanel1.Controls.Add(cancelButton, 0, 2);
            tableLayoutPanel1.Controls.Add(progressLog, 0, 1);
            tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 3;
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 12F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 76F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 12F));
            tableLayoutPanel1.Size = new System.Drawing.Size(704, 424);
            tableLayoutPanel1.TabIndex = 0;
            //
            // extractProgressBar
            //
            extractProgressBar.Dock = System.Windows.Forms.DockStyle.Fill;
            extractProgressBar.Location = new System.Drawing.Point(23, 12);
            extractProgressBar.Margin = new System.Windows.Forms.Padding(23, 12, 23, 12);
            extractProgressBar.Name = "extractProgressBar";
            extractProgressBar.Size = new System.Drawing.Size(658, 26);
            extractProgressBar.TabIndex = 0;
            //
            // cancelButton
            //
            cancelButton.Dock = System.Windows.Forms.DockStyle.Right;
            cancelButton.Location = new System.Drawing.Point(593, 384);
            cancelButton.Margin = new System.Windows.Forms.Padding(0, 12, 23, 12);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(88, 28);
            cancelButton.TabIndex = 2;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            cancelButton.Click += CancelButton_Click;
            //
            // progressLog
            //
            progressLog.BorderStyle = System.Windows.Forms.BorderStyle.None;
            progressLog.Dock = System.Windows.Forms.DockStyle.Fill;
            progressLog.Font = new System.Drawing.Font("Cascadia Mono", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            progressLog.Location = new System.Drawing.Point(23, 50);
            progressLog.Margin = new System.Windows.Forms.Padding(23, 0, 23, 0);
            progressLog.Multiline = true;
            progressLog.Name = "progressLog";
            progressLog.ReadOnly = true;
            progressLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            progressLog.Size = new System.Drawing.Size(658, 322);
            progressLog.TabIndex = 3;
            progressLog.WordWrap = false;
            //
            // ExtractProgressForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(704, 424);
            Controls.Add(tableLayoutPanel1);
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
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.TextBox progressLog;
    }
}
