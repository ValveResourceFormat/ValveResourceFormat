using GUI.Utils;

namespace GUI.Controls
{
    partial class SavedCameraPositionsControl
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

            if (disposing)
            {
                Settings.RefreshCamerasOnSave -= RefreshSavedPositions;
            }
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            label2 = new System.Windows.Forms.Label();
            cmbPositions = new ThemedComboBox();
            btnSave = new ThemedButton();
            btnDelete = new ThemedButton();
            btnRestore = new ThemedButton();
            btnSetPos = new ThemedButton();
            btnGetPos = new ThemedButton();
            tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            themedGroupBox1 = new ThemedGroupBox();
            tableLayoutPanel1.SuspendLayout();
            themedGroupBox1.SuspendLayout();
            SuspendLayout();
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Dock = System.Windows.Forms.DockStyle.Fill;
            label2.Location = new System.Drawing.Point(0, 33);
            label2.Margin = new System.Windows.Forms.Padding(0);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(110, 27);
            label2.TabIndex = 5;
            label2.Text = "Clipboard:";
            label2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // cmbPositions
            // 
            cmbPositions.BackColor = System.Drawing.SystemColors.Control;
            cmbPositions.Dock = System.Windows.Forms.DockStyle.Top;
            cmbPositions.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            cmbPositions.DropDownBackColor = System.Drawing.SystemColors.Control;
            cmbPositions.DropDownForeColor = System.Drawing.Color.Black;
            cmbPositions.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cmbPositions.ForeColor = System.Drawing.Color.Black;
            cmbPositions.HeaderColor = System.Drawing.Color.FromArgb(230, 230, 230);
            cmbPositions.HighlightColor = System.Drawing.Color.FromArgb(99, 161, 255);
            cmbPositions.Location = new System.Drawing.Point(4, 19);
            cmbPositions.Margin = new System.Windows.Forms.Padding(0);
            cmbPositions.Name = "cmbPositions";
            cmbPositions.Size = new System.Drawing.Size(277, 24);
            cmbPositions.TabIndex = 0;
            // 
            // btnSave
            // 
            btnSave.BackColor = System.Drawing.Color.FromArgb(230, 230, 230);
            btnSave.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            btnSave.CornerRadius = 5;
            btnSave.Dock = System.Windows.Forms.DockStyle.Fill;
            btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnSave.ForeColor = System.Drawing.Color.Black;
            btnSave.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            btnSave.Location = new System.Drawing.Point(0, 6);
            btnSave.Margin = new System.Windows.Forms.Padding(0);
            btnSave.Name = "btnSave";
            btnSave.Size = new System.Drawing.Size(110, 27);
            btnSave.Style = true;
            btnSave.TabIndex = 2;
            btnSave.Text = "Save";
            btnSave.UseVisualStyleBackColor = false;
            btnSave.Click += BtnSave_Click;
            // 
            // btnDelete
            // 
            btnDelete.BackColor = System.Drawing.Color.FromArgb(230, 230, 230);
            btnDelete.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            btnDelete.CornerRadius = 5;
            btnDelete.Dock = System.Windows.Forms.DockStyle.Fill;
            btnDelete.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnDelete.ForeColor = System.Drawing.Color.Black;
            btnDelete.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            btnDelete.Location = new System.Drawing.Point(193, 6);
            btnDelete.Margin = new System.Windows.Forms.Padding(0);
            btnDelete.Name = "btnDelete";
            btnDelete.Size = new System.Drawing.Size(84, 27);
            btnDelete.Style = true;
            btnDelete.TabIndex = 3;
            btnDelete.Text = "Delete";
            btnDelete.UseVisualStyleBackColor = false;
            btnDelete.Click += BtnDelete_Click;
            // 
            // btnRestore
            // 
            btnRestore.BackColor = System.Drawing.Color.FromArgb(230, 230, 230);
            btnRestore.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            btnRestore.CornerRadius = 5;
            btnRestore.Dock = System.Windows.Forms.DockStyle.Fill;
            btnRestore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnRestore.ForeColor = System.Drawing.Color.Black;
            btnRestore.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            btnRestore.Location = new System.Drawing.Point(110, 6);
            btnRestore.Margin = new System.Windows.Forms.Padding(0);
            btnRestore.Name = "btnRestore";
            btnRestore.Size = new System.Drawing.Size(83, 27);
            btnRestore.Style = true;
            btnRestore.TabIndex = 4;
            btnRestore.Text = "Restore";
            btnRestore.UseVisualStyleBackColor = false;
            btnRestore.Click += BtnRestore_Click;
            // 
            // btnSetPos
            // 
            btnSetPos.BackColor = System.Drawing.Color.FromArgb(230, 230, 230);
            btnSetPos.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            btnSetPos.CornerRadius = 5;
            btnSetPos.Dock = System.Windows.Forms.DockStyle.Fill;
            btnSetPos.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnSetPos.ForeColor = System.Drawing.Color.Black;
            btnSetPos.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            btnSetPos.Location = new System.Drawing.Point(110, 33);
            btnSetPos.Margin = new System.Windows.Forms.Padding(0);
            btnSetPos.Name = "btnSetPos";
            btnSetPos.Size = new System.Drawing.Size(83, 27);
            btnSetPos.Style = true;
            btnSetPos.TabIndex = 6;
            btnSetPos.Text = "setpos";
            btnSetPos.UseVisualStyleBackColor = false;
            btnSetPos.Click += BtnSetPos_Click;
            // 
            // btnGetPos
            // 
            btnGetPos.BackColor = System.Drawing.Color.FromArgb(230, 230, 230);
            btnGetPos.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            btnGetPos.CornerRadius = 5;
            btnGetPos.Dock = System.Windows.Forms.DockStyle.Fill;
            btnGetPos.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnGetPos.ForeColor = System.Drawing.Color.Black;
            btnGetPos.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            btnGetPos.Location = new System.Drawing.Point(193, 33);
            btnGetPos.Margin = new System.Windows.Forms.Padding(0);
            btnGetPos.Name = "btnGetPos";
            btnGetPos.Size = new System.Drawing.Size(84, 27);
            btnGetPos.Style = true;
            btnGetPos.TabIndex = 7;
            btnGetPos.Text = "getpos";
            btnGetPos.UseVisualStyleBackColor = false;
            btnGetPos.Click += BtnGetPos_Click;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 3;
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 40F));
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 30F));
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 30F));
            tableLayoutPanel1.Controls.Add(btnSave, 0, 0);
            tableLayoutPanel1.Controls.Add(btnRestore, 1, 0);
            tableLayoutPanel1.Controls.Add(btnDelete, 2, 0);
            tableLayoutPanel1.Controls.Add(btnGetPos, 2, 1);
            tableLayoutPanel1.Controls.Add(label2, 0, 1);
            tableLayoutPanel1.Controls.Add(btnSetPos, 1, 1);
            tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Top;
            tableLayoutPanel1.Location = new System.Drawing.Point(4, 43);
            tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);
            tableLayoutPanel1.RowCount = 2;
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            tableLayoutPanel1.Size = new System.Drawing.Size(277, 60);
            tableLayoutPanel1.TabIndex = 8;
            // 
            // themedGroupBox1
            // 
            themedGroupBox1.BackColor = System.Drawing.SystemColors.Control;
            themedGroupBox1.BorderColor = System.Drawing.Color.FromArgb(230, 230, 230);
            themedGroupBox1.BorderWidth = 2;
            themedGroupBox1.Controls.Add(tableLayoutPanel1);
            themedGroupBox1.Controls.Add(cmbPositions);
            themedGroupBox1.CornerRadius = 5;
            themedGroupBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            themedGroupBox1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            themedGroupBox1.ForeColor = System.Drawing.Color.Black;
            themedGroupBox1.Location = new System.Drawing.Point(3, 3);
            themedGroupBox1.Name = "themedGroupBox1";
            themedGroupBox1.Padding = new System.Windows.Forms.Padding(4, 3, 4, 3);
            themedGroupBox1.Size = new System.Drawing.Size(285, 114);
            themedGroupBox1.TabIndex = 0;
            themedGroupBox1.TabStop = false;
            themedGroupBox1.Text = "Saved Camera";
            // 
            // SavedCameraPositionsControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(themedGroupBox1);
            Name = "SavedCameraPositionsControl";
            Padding = new System.Windows.Forms.Padding(3);
            Size = new System.Drawing.Size(291, 120);
            tableLayoutPanel1.ResumeLayout(false);
            tableLayoutPanel1.PerformLayout();
            themedGroupBox1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private ThemedComboBox cmbPositions;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private ThemedButton btnSave;
        private ThemedButton btnDelete;
        private ThemedButton btnRestore;
        private ThemedButton btnSetPos;
        private ThemedButton btnGetPos;
        private ThemedGroupBox themedGroupBox1;
    }
}
