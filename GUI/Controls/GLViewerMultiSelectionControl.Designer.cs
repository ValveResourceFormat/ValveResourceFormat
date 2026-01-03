namespace GUI.Controls
{
    partial class GLViewerMultiSelectionControl
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
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            checkedListBox = new System.Windows.Forms.CheckedListBox();
            groupBox = new ThemedGroupBox();
            groupBox.SuspendLayout();
            SuspendLayout();
            // 
            // checkedListBox
            // 
            checkedListBox.CheckOnClick = true;
            checkedListBox.Dock = System.Windows.Forms.DockStyle.Fill;
            checkedListBox.Location = new System.Drawing.Point(4, 16);
            checkedListBox.Margin = new System.Windows.Forms.Padding(0);
            checkedListBox.Name = "checkedListBox";
            checkedListBox.Size = new System.Drawing.Size(206, 132);
            checkedListBox.TabIndex = 1;
            // 
            // groupBox
            // 
            groupBox.BackColor = System.Drawing.SystemColors.Control;
            groupBox.BorderColor = System.Drawing.Color.FromArgb(230, 230, 230);
            groupBox.BorderWidth = 2;
            groupBox.Controls.Add(checkedListBox);
            groupBox.CornerRadius = 5;
            groupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            groupBox.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            groupBox.ForeColor = System.Drawing.Color.Black;
            groupBox.Location = new System.Drawing.Point(3, 3);
            groupBox.Margin = new System.Windows.Forms.Padding(0);
            groupBox.Name = "groupBox";
            groupBox.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
            groupBox.Size = new System.Drawing.Size(214, 148);
            groupBox.TabIndex = 2;
            groupBox.TabStop = false;
            groupBox.Text = "Selection";
            // 
            // GLViewerMultiSelectionControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(groupBox);
            Margin = new System.Windows.Forms.Padding(0);
            Name = "GLViewerMultiSelectionControl";
            Padding = new System.Windows.Forms.Padding(3);
            Size = new System.Drawing.Size(220, 154);
            groupBox.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        private System.Windows.Forms.CheckedListBox checkedListBox;
        private ThemedGroupBox groupBox;
    }
}
