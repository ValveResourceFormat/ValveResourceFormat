using GUI.Theme;
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
            System.Windows.Forms.Label label1;
            label2 = new System.Windows.Forms.Label();
            cmbPositions = new CustomComboBox();
            btnSave = new CustomButton();
            btnDelete = new CustomButton();
            btnRestore = new CustomButton();
            btnSetPos = new CustomButton();
            btnGetPos = new CustomButton();
            tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            label1 = new System.Windows.Forms.Label();
            tableLayoutPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Dock = System.Windows.Forms.DockStyle.Top;
            label1.Location = new System.Drawing.Point(0, 0);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(134, 15);
            label1.TabIndex = 1;
            label1.Text = "Saved camera positions:";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Dock = System.Windows.Forms.DockStyle.Fill;
            label2.Location = new System.Drawing.Point(0, 33);
            label2.Margin = new System.Windows.Forms.Padding(0);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(97, 27);
            label2.TabIndex = 5;
            label2.Text = "Clipboard:";
            label2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // cmbPositions
            // 
            cmbPositions.Dock = System.Windows.Forms.DockStyle.Top;
            cmbPositions.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cmbPositions.FormattingEnabled = true;
            cmbPositions.Location = new System.Drawing.Point(0, 15);
            cmbPositions.Margin = new System.Windows.Forms.Padding(0);
            cmbPositions.Name = "cmbPositions";
            cmbPositions.Size = new System.Drawing.Size(291, 23);
            cmbPositions.TabIndex = 0;
            // 
            // btnSave
            // 
            btnSave.Dock = System.Windows.Forms.DockStyle.Fill;
            btnSave.Location = new System.Drawing.Point(0, 6);
            btnSave.Margin = new System.Windows.Forms.Padding(0);
            btnSave.Name = "btnSave";
            btnSave.Size = new System.Drawing.Size(97, 27);
            btnSave.TabIndex = 2;
            btnSave.Text = "Save";
            btnSave.UseVisualStyleBackColor = true;
            btnSave.Click += BtnSave_Click;
            // 
            // btnDelete
            // 
            btnDelete.Dock = System.Windows.Forms.DockStyle.Fill;
            btnDelete.Location = new System.Drawing.Point(194, 6);
            btnDelete.Margin = new System.Windows.Forms.Padding(0);
            btnDelete.Name = "btnDelete";
            btnDelete.Size = new System.Drawing.Size(97, 27);
            btnDelete.TabIndex = 3;
            btnDelete.Text = "Delete";
            btnDelete.UseVisualStyleBackColor = true;
            btnDelete.Click += BtnDelete_Click;
            // 
            // btnRestore
            // 
            btnRestore.Dock = System.Windows.Forms.DockStyle.Fill;
            btnRestore.Location = new System.Drawing.Point(97, 6);
            btnRestore.Margin = new System.Windows.Forms.Padding(0);
            btnRestore.Name = "btnRestore";
            btnRestore.Size = new System.Drawing.Size(97, 27);
            btnRestore.TabIndex = 4;
            btnRestore.Text = "Restore";
            btnRestore.UseVisualStyleBackColor = true;
            btnRestore.Click += BtnRestore_Click;
            // 
            // btnSetPos
            // 
            btnSetPos.Dock = System.Windows.Forms.DockStyle.Fill;
            btnSetPos.Location = new System.Drawing.Point(97, 33);
            btnSetPos.Margin = new System.Windows.Forms.Padding(0);
            btnSetPos.Name = "btnSetPos";
            btnSetPos.Size = new System.Drawing.Size(97, 27);
            btnSetPos.TabIndex = 6;
            btnSetPos.Text = "setpos";
            btnSetPos.UseVisualStyleBackColor = true;
            btnSetPos.Click += BtnSetPos_Click;
            // 
            // btnGetPos
            // 
            btnGetPos.Dock = System.Windows.Forms.DockStyle.Fill;
            btnGetPos.Location = new System.Drawing.Point(194, 33);
            btnGetPos.Margin = new System.Windows.Forms.Padding(0);
            btnGetPos.Name = "btnGetPos";
            btnGetPos.Size = new System.Drawing.Size(97, 27);
            btnGetPos.TabIndex = 7;
            btnGetPos.Text = "getpos";
            btnGetPos.UseVisualStyleBackColor = true;
            btnGetPos.Click += BtnGetPos_Click;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 3;
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.3333321F));
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.3333321F));
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.3333321F));
            tableLayoutPanel1.Controls.Add(btnSave, 0, 0);
            tableLayoutPanel1.Controls.Add(btnRestore, 1, 0);
            tableLayoutPanel1.Controls.Add(btnDelete, 2, 0);
            tableLayoutPanel1.Controls.Add(btnGetPos, 2, 1);
            tableLayoutPanel1.Controls.Add(label2, 0, 1);
            tableLayoutPanel1.Controls.Add(btnSetPos, 1, 1);
            tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Top;
            tableLayoutPanel1.Location = new System.Drawing.Point(0, 38);
            tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);
            tableLayoutPanel1.RowCount = 2;
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            tableLayoutPanel1.Size = new System.Drawing.Size(291, 60);
            tableLayoutPanel1.TabIndex = 8;
            // 
            // SavedCameraPositionsControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(tableLayoutPanel1);
            Controls.Add(cmbPositions);
            Controls.Add(label1);
            Name = "SavedCameraPositionsControl";
            Size = new System.Drawing.Size(291, 120);
            tableLayoutPanel1.ResumeLayout(false);
            tableLayoutPanel1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private CustomComboBox cmbPositions;
        private CustomButton btnSave;
        private CustomButton btnDelete;
        private CustomButton btnRestore;
        private CustomButton btnSetPos;
        private CustomButton btnGetPos;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
    }
}
