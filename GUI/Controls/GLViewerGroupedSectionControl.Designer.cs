namespace GUI.Controls
{
    partial class GLViewerGroupedSectionControl
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
            tableLayout = new System.Windows.Forms.TableLayoutPanel();
            groupBox = new ThemedGroupBox();
            groupBox.SuspendLayout();
            SuspendLayout();
            //
            // tableLayout
            //
            tableLayout.AutoSize = true;
            tableLayout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            tableLayout.ColumnCount = 1;
            tableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayout.Dock = System.Windows.Forms.DockStyle.Top;
            tableLayout.GrowStyle = System.Windows.Forms.TableLayoutPanelGrowStyle.AddRows;
            tableLayout.Location = new System.Drawing.Point(4, 18);
            tableLayout.Margin = new System.Windows.Forms.Padding(0);
            tableLayout.Name = "tableLayout";
            tableLayout.RowCount = 0;
            tableLayout.Size = new System.Drawing.Size(206, 0);
            tableLayout.TabIndex = 0;
            //
            // groupBox
            //
            groupBox.BackColor = System.Drawing.SystemColors.Control;
            groupBox.BorderColor = System.Drawing.Color.FromArgb(230, 230, 230);
            groupBox.BorderWidth = 2;
            groupBox.Controls.Add(tableLayout);
            groupBox.CornerRadius = 5;
            groupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            groupBox.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            groupBox.ForeColor = System.Drawing.Color.Black;
            groupBox.Location = new System.Drawing.Point(3, 3);
            groupBox.Margin = new System.Windows.Forms.Padding(0);
            groupBox.Name = "groupBox";
            groupBox.Padding = new System.Windows.Forms.Padding(4, 2, 4, 6);
            groupBox.Size = new System.Drawing.Size(214, 40);
            groupBox.TabIndex = 1;
            groupBox.TabStop = false;
            groupBox.Text = "Section";
            //
            // GLViewerGroupedSectionControl
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(groupBox);
            Margin = new System.Windows.Forms.Padding(0);
            Name = "GLViewerGroupedSectionControl";
            Padding = new System.Windows.Forms.Padding(3);
            Size = new System.Drawing.Size(220, 46);
            groupBox.ResumeLayout(false);
            groupBox.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private System.Windows.Forms.TableLayoutPanel tableLayout;
        private ThemedGroupBox groupBox;
    }
}
