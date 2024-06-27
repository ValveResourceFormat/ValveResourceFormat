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
            var dataGridViewCellStyle2 = new DataGridViewCellStyle();
            tabControl = new TabControl();
            tabPageProperties = new TabPage();
            dataGridProperties = new DataGridView();
            ColumnName = new DataGridViewTextBoxColumn();
            ColumnValue = new DataGridViewTextBoxColumn();
            tabPageOutputs = new TabPage();
            dataGridOutputs = new DataGridView();
            Output = new DataGridViewTextBoxColumn();
            TargetEntity = new DataGridViewTextBoxColumn();
            TargetInput = new DataGridViewTextBoxColumn();
            Parameter = new DataGridViewTextBoxColumn();
            Delay = new DataGridViewTextBoxColumn();
            timesToFire = new DataGridViewTextBoxColumn();
            tabControl.SuspendLayout();
            tabPageProperties.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dataGridProperties).BeginInit();
            tabPageOutputs.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dataGridOutputs).BeginInit();
            SuspendLayout();
            //
            // tabControl1
            //
            tabControl.Controls.Add(tabPageProperties);
            tabControl.Controls.Add(tabPageOutputs);
            tabControl.Dock = DockStyle.Fill;
            tabControl.Location = new System.Drawing.Point(0, 0);
            tabControl.Name = "tabControl1";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new System.Drawing.Size(800, 450);
            tabControl.TabIndex = 0;
            //
            // tabPageProperties
            //
            tabPageProperties.Controls.Add(dataGridProperties);
            tabPageProperties.Location = new System.Drawing.Point(4, 24);
            tabPageProperties.Name = "tabPageProperties";
            tabPageProperties.Padding = new Padding(3);
            tabPageProperties.Size = new System.Drawing.Size(792, 422);
            tabPageProperties.TabIndex = 0;
            tabPageProperties.Text = "Properties";
            tabPageProperties.UseVisualStyleBackColor = true;
            //
            // dataGrid
            //
            dataGridProperties.AllowUserToAddRows = false;
            dataGridProperties.AllowUserToDeleteRows = false;
            dataGridProperties.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridProperties.Columns.AddRange(new DataGridViewColumn[] { ColumnName, ColumnValue });
            dataGridViewCellStyle1.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Cascadia Mono", 10F);
            dataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = DataGridViewTriState.True;
            dataGridProperties.DefaultCellStyle = dataGridViewCellStyle1;
            dataGridProperties.Dock = DockStyle.Fill;
            dataGridProperties.Location = new System.Drawing.Point(3, 3);
            dataGridProperties.Name = "dataGrid";
            dataGridProperties.ReadOnly = true;
            dataGridProperties.RowHeadersVisible = false;
            dataGridProperties.Size = new System.Drawing.Size(786, 416);
            dataGridProperties.TabIndex = 1;
            //
            // ColumnName
            //
            ColumnName.FillWeight = 30F;
            ColumnName.HeaderText = "Name";
            ColumnName.Name = "ColumnName";
            ColumnName.ReadOnly = true;
            //
            // ColumnValue
            //
            ColumnValue.FillWeight = 70F;
            ColumnValue.HeaderText = "Value";
            ColumnValue.Name = "ColumnValue";
            ColumnValue.ReadOnly = true;
            //
            // tabPageOutputs
            //
            tabPageOutputs.Controls.Add(dataGridOutputs);
            tabPageOutputs.Location = new System.Drawing.Point(4, 24);
            tabPageOutputs.Name = "tabPageOutputs";
            tabPageOutputs.Padding = new Padding(3);
            tabPageOutputs.Size = new System.Drawing.Size(792, 422);
            tabPageOutputs.TabIndex = 1;
            tabPageOutputs.Text = "Outputs";
            tabPageOutputs.UseVisualStyleBackColor = true;
            //
            // dataGridOutputs
            //
            dataGridOutputs.AllowUserToAddRows = false;
            dataGridOutputs.AllowUserToDeleteRows = false;
            dataGridOutputs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridOutputs.Columns.AddRange(new DataGridViewColumn[] { Output, TargetEntity, TargetInput, Parameter, Delay, timesToFire });
            dataGridViewCellStyle2.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("Cascadia Mono", 10F);
            dataGridViewCellStyle2.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = DataGridViewTriState.True;
            dataGridOutputs.DefaultCellStyle = dataGridViewCellStyle2;
            dataGridOutputs.Dock = DockStyle.Fill;
            dataGridOutputs.Location = new System.Drawing.Point(3, 3);
            dataGridOutputs.Name = "dataGridOutputs";
            dataGridOutputs.ReadOnly = true;
            dataGridOutputs.RowHeadersVisible = false;
            dataGridOutputs.Size = new System.Drawing.Size(786, 416);
            dataGridOutputs.TabIndex = 0;
            //
            // Output
            //
            Output.HeaderText = "Output";
            Output.Name = "Output";
            Output.ReadOnly = true;
            //
            // TargetEntity
            //
            TargetEntity.HeaderText = "Target Entity";
            TargetEntity.Name = "TargetEntity";
            TargetEntity.ReadOnly = true;
            //
            // TargetInput
            //
            TargetInput.HeaderText = "Target Input";
            TargetInput.Name = "TargetInput";
            TargetInput.ReadOnly = true;
            //
            // Parameter
            //
            Parameter.HeaderText = "Parameter";
            Parameter.Name = "Parameter";
            Parameter.ReadOnly = true;
            //
            // Delay
            //
            Delay.HeaderText = "Delay";
            Delay.Name = "Delay";
            Delay.ReadOnly = true;
            //
            // timesToFire
            //
            timesToFire.HeaderText = "Times To Fire";
            timesToFire.Name = "timesToFire";
            timesToFire.ReadOnly = true;
            //
            // EntityInfoForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(800, 450);
            Controls.Add(tabControl);
            Name = "EntityInfoForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "EntityInfoForm";
            TopMost = true;
            tabControl.ResumeLayout(false);
            tabPageProperties.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dataGridProperties).EndInit();
            tabPageOutputs.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dataGridOutputs).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private TabControl tabControl;
        private TabPage tabPageProperties;
        private DataGridView dataGridProperties;
        private DataGridViewTextBoxColumn ColumnName;
        private DataGridViewTextBoxColumn ColumnValue;
        private TabPage tabPageOutputs;
        private DataGridView dataGridOutputs;
        private DataGridViewTextBoxColumn Output;
        private DataGridViewTextBoxColumn TargetEntity;
        private DataGridViewTextBoxColumn TargetInput;
        private DataGridViewTextBoxColumn Parameter;
        private DataGridViewTextBoxColumn Delay;
        private DataGridViewTextBoxColumn timesToFire;
    }
}
