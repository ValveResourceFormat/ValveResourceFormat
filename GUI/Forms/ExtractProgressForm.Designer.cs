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
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.extractProgressBar = new System.Windows.Forms.ProgressBar();
            this.extractStatusLabel = new System.Windows.Forms.Label();
            this.cancelButton = new System.Windows.Forms.Button();
            this.tableLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 1;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Controls.Add(this.extractProgressBar, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.extractStatusLabel, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.cancelButton, 0, 2);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 3;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 40F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 30F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 30F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(704, 162);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // extractProgressBar
            // 
            this.extractProgressBar.Dock = System.Windows.Forms.DockStyle.Fill;
            this.extractProgressBar.Location = new System.Drawing.Point(23, 17);
            this.extractProgressBar.Margin = new System.Windows.Forms.Padding(23, 17, 23, 17);
            this.extractProgressBar.Name = "extractProgressBar";
            this.extractProgressBar.Size = new System.Drawing.Size(658, 30);
            this.extractProgressBar.TabIndex = 0;
            // 
            // extractStatusLabel
            // 
            this.extractStatusLabel.AutoEllipsis = true;
            this.extractStatusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.extractStatusLabel.Location = new System.Drawing.Point(23, 76);
            this.extractStatusLabel.Margin = new System.Windows.Forms.Padding(23, 12, 23, 12);
            this.extractStatusLabel.Name = "extractStatusLabel";
            this.extractStatusLabel.Size = new System.Drawing.Size(658, 24);
            this.extractStatusLabel.TabIndex = 1;
            // 
            // cancelButton
            // 
            this.cancelButton.Dock = System.Windows.Forms.DockStyle.Right;
            this.cancelButton.Location = new System.Drawing.Point(593, 124);
            this.cancelButton.Margin = new System.Windows.Forms.Padding(0, 12, 23, 12);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(88, 26);
            this.cancelButton.TabIndex = 2;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            this.cancelButton.Click += new System.EventHandler(this.CancelButton_Click);
            // 
            // ExtractProgressForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(704, 162);
            this.Controls.Add(this.tableLayoutPanel1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.MaximizeBox = false;
            this.Name = "ExtractProgressForm";
            this.Text = "Extracting files...";
            this.tableLayoutPanel1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.ProgressBar extractProgressBar;
        private System.Windows.Forms.Label extractStatusLabel;
        private System.Windows.Forms.Button cancelButton;
    }
}
