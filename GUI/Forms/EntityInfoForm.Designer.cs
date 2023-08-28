using System.Windows.Forms;

namespace GUI.Forms
{
    partial class EntityInfoForm
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
            var dataGridViewCellStyle1 = new DataGridViewCellStyle();
            dataGrid = new DataGridView();
            ColumnName = new DataGridViewTextBoxColumn();
            ColumnValue = new DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)dataGrid).BeginInit();
            SuspendLayout();
            // 
            // dataGrid
            // 
            dataGrid.AllowUserToAddRows = false;
            dataGrid.AllowUserToDeleteRows = false;
            dataGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGrid.Columns.AddRange(new DataGridViewColumn[] { ColumnName, ColumnValue });
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Cascadia Mono", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            dataGridViewCellStyle1.WrapMode = DataGridViewTriState.True;
            dataGrid.DefaultCellStyle = dataGridViewCellStyle1;
            dataGrid.Dock = DockStyle.Fill;
            dataGrid.Location = new System.Drawing.Point(0, 0);
            dataGrid.Name = "dataGrid";
            dataGrid.ReadOnly = true;
            dataGrid.RowHeadersVisible = false;
            dataGrid.Size = new System.Drawing.Size(800, 450);
            dataGrid.TabIndex = 0;
            // 
            // ColumnName
            // 
            ColumnName.HeaderText = "Name";
            ColumnName.Name = "ColumnName";
            ColumnName.ReadOnly = true;
            // 
            // ColumnValue
            // 
            ColumnValue.HeaderText = "Value";
            ColumnValue.Name = "ColumnValue";
            ColumnValue.ReadOnly = true;
            // 
            // EntityInfoForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(800, 450);
            Controls.Add(dataGrid);
            Name = "EntityInfoForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "EntityInfoForm";
            ((System.ComponentModel.ISupportInitialize)dataGrid).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private DataGridView dataGrid;
        private DataGridViewTextBoxColumn ColumnName;
        private DataGridViewTextBoxColumn ColumnValue;
    }
}
