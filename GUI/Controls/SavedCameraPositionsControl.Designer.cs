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
            System.Windows.Forms.Label label2;
            cmbPositions = new System.Windows.Forms.ComboBox();
            btnSave = new System.Windows.Forms.Button();
            btnDelete = new System.Windows.Forms.Button();
            btnRestore = new System.Windows.Forms.Button();
            btnSetPos = new System.Windows.Forms.Button();
            btnGetPos = new System.Windows.Forms.Button();
            label1 = new System.Windows.Forms.Label();
            label2 = new System.Windows.Forms.Label();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(3, 2);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(134, 15);
            label1.TabIndex = 1;
            label1.Text = "Saved camera positions:";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new System.Drawing.Point(8, 89);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(62, 15);
            label2.TabIndex = 5;
            label2.Text = "Clipboard:";
            // 
            // cmbPositions
            // 
            cmbPositions.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            cmbPositions.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cmbPositions.FormattingEnabled = true;
            cmbPositions.Location = new System.Drawing.Point(3, 20);
            cmbPositions.Margin = new System.Windows.Forms.Padding(0);
            cmbPositions.Name = "cmbPositions";
            cmbPositions.Size = new System.Drawing.Size(214, 23);
            cmbPositions.TabIndex = 0;
            // 
            // btnSave
            // 
            btnSave.Location = new System.Drawing.Point(3, 46);
            btnSave.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnSave.Name = "btnSave";
            btnSave.Size = new System.Drawing.Size(66, 33);
            btnSave.TabIndex = 2;
            btnSave.Text = "Save";
            btnSave.UseVisualStyleBackColor = true;
            btnSave.Click += BtnSave_Click;
            // 
            // btnDelete
            // 
            btnDelete.Location = new System.Drawing.Point(151, 46);
            btnDelete.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnDelete.Name = "btnDelete";
            btnDelete.Size = new System.Drawing.Size(66, 33);
            btnDelete.TabIndex = 3;
            btnDelete.Text = "Delete";
            btnDelete.UseVisualStyleBackColor = true;
            btnDelete.Click += BtnDelete_Click;
            // 
            // btnRestore
            // 
            btnRestore.Location = new System.Drawing.Point(77, 46);
            btnRestore.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnRestore.Name = "btnRestore";
            btnRestore.Size = new System.Drawing.Size(66, 33);
            btnRestore.TabIndex = 4;
            btnRestore.Text = "Restore";
            btnRestore.UseVisualStyleBackColor = true;
            btnRestore.Click += BtnRestore_Click;
            // 
            // btnSetPos
            // 
            btnSetPos.Location = new System.Drawing.Point(77, 85);
            btnSetPos.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnSetPos.Name = "btnSetPos";
            btnSetPos.Size = new System.Drawing.Size(66, 23);
            btnSetPos.TabIndex = 6;
            btnSetPos.Text = "setpos";
            btnSetPos.UseVisualStyleBackColor = true;
            btnSetPos.Click += BtnSetPos_Click;
            // 
            // btnGetPos
            // 
            btnGetPos.Location = new System.Drawing.Point(151, 85);
            btnGetPos.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            btnGetPos.Name = "btnGetPos";
            btnGetPos.Size = new System.Drawing.Size(66, 23);
            btnGetPos.TabIndex = 7;
            btnGetPos.Text = "getpos";
            btnGetPos.UseVisualStyleBackColor = true;
            btnGetPos.Click += BtnGetPos_Click;
            // 
            // SavedCameraPositionsControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(btnGetPos);
            Controls.Add(btnSetPos);
            Controls.Add(label2);
            Controls.Add(btnRestore);
            Controls.Add(btnDelete);
            Controls.Add(btnSave);
            Controls.Add(label1);
            Controls.Add(cmbPositions);
            Name = "SavedCameraPositionsControl";
            Size = new System.Drawing.Size(220, 120);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ComboBox cmbPositions;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnDelete;
        private System.Windows.Forms.Button btnRestore;
        private System.Windows.Forms.Button btnSetPos;
        private System.Windows.Forms.Button btnGetPos;
    }
}
