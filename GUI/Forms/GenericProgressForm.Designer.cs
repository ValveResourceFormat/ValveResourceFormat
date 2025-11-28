using GUI.Controls;

namespace GUI.Forms
{
    partial class GenericProgressForm
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
            extractStatusLabel = new System.Windows.Forms.Label();
            cancelButton = new ThemedButton();
            tableLayoutPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 1;
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.Controls.Add(extractProgressBar, 0, 0);
            tableLayoutPanel1.Controls.Add(extractStatusLabel, 0, 1);
            tableLayoutPanel1.Controls.Add(cancelButton, 0, 2);
            tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 3;
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 40F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 30F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 30F));
            tableLayoutPanel1.Size = new System.Drawing.Size(704, 162);
            tableLayoutPanel1.TabIndex = 0;
            // 
            // extractProgressBar
            // 
            extractProgressBar.Dock = System.Windows.Forms.DockStyle.Fill;
            extractProgressBar.Location = new System.Drawing.Point(23, 17);
            extractProgressBar.Margin = new System.Windows.Forms.Padding(23, 17, 23, 17);
            extractProgressBar.Name = "extractProgressBar";
            extractProgressBar.Size = new System.Drawing.Size(658, 30);
            extractProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            extractProgressBar.TabIndex = 0;
            // 
            // extractStatusLabel
            // 
            extractStatusLabel.AutoEllipsis = true;
            extractStatusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            extractStatusLabel.Location = new System.Drawing.Point(23, 76);
            extractStatusLabel.Margin = new System.Windows.Forms.Padding(23, 12, 23, 12);
            extractStatusLabel.Name = "extractStatusLabel";
            extractStatusLabel.Size = new System.Drawing.Size(658, 24);
            extractStatusLabel.TabIndex = 1;
            extractStatusLabel.Text = "Calculating…";
            extractStatusLabel.UseMnemonic = false;
            // 
            // cancelButton
            // 
            cancelButton.Dock = System.Windows.Forms.DockStyle.Right;
            cancelButton.Location = new System.Drawing.Point(593, 124);
            cancelButton.Margin = new System.Windows.Forms.Padding(0, 12, 23, 12);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(88, 26);
            cancelButton.TabIndex = 2;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            cancelButton.Click += CancelButton_Click;
            // 
            // GenericProgressForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(704, 162);
            Controls.Add(tableLayoutPanel1);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MaximizeBox = false;
            Name = "GenericProgressForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Extracting files…";
            tableLayoutPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.ProgressBar extractProgressBar;
        private System.Windows.Forms.Label extractStatusLabel;
        private System.Windows.Forms.Button cancelButton;
    }
}
