namespace GUI.Forms
{
    partial class VmapExport
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            decompile_skybox = new System.Windows.Forms.CheckBox();
            button_continue = new System.Windows.Forms.Button();
            label1 = new System.Windows.Forms.Label();
            label2 = new System.Windows.Forms.Label();
            SuspendLayout();
            // 
            // decompile_skybox
            // 
            decompile_skybox.AutoSize = true;
            decompile_skybox.Checked = true;
            decompile_skybox.CheckState = System.Windows.Forms.CheckState.Checked;
            decompile_skybox.Location = new System.Drawing.Point(12, 50);
            decompile_skybox.Name = "decompile_skybox";
            decompile_skybox.Size = new System.Drawing.Size(199, 19);
            decompile_skybox.TabIndex = 0;
            decompile_skybox.Text = "decompile 3D skybox (if present)";
            decompile_skybox.UseVisualStyleBackColor = true;
            decompile_skybox.CheckedChanged += checkBox1_CheckedChanged;
            // 
            // button_continue
            // 
            button_continue.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            button_continue.DialogResult = System.Windows.Forms.DialogResult.Continue;
            button_continue.Location = new System.Drawing.Point(184, 268);
            button_continue.Margin = new System.Windows.Forms.Padding(3, 3, 20, 20);
            button_continue.Name = "button_continue";
            button_continue.Size = new System.Drawing.Size(75, 23);
            button_continue.TabIndex = 3;
            button_continue.Text = "Continue";
            button_continue.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            label1.Anchor = System.Windows.Forms.AnchorStyles.Top;
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(62, 8);
            label1.Margin = new System.Windows.Forms.Padding(3);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(155, 15);
            label1.TabIndex = 4;
            label1.Text = "Select Vmap export options.";
            // 
            // label2
            // 
            label2.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            label2.Location = new System.Drawing.Point(-5, 32);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(299, 3);
            label2.TabIndex = 5;
            // 
            // VmapExport
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(277, 303);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(button_continue);
            Controls.Add(decompile_skybox);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            Name = "VmapExport";
            Text = "VmapExport";
            Load += VmapExport_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.CheckBox decompile_skybox;
        private System.Windows.Forms.Button button_continue;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
    }
}
