using System.Windows.Forms;
using DarkModeForms;

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
            tabControl = new FlatTabControl();
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
            OnlyOnce = new DataGridViewTextBoxColumn();
            tabControl.SuspendLayout();
            tabPageProperties.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dataGridProperties).BeginInit();
            tabPageOutputs.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dataGridOutputs).BeginInit();
            SuspendLayout();
            // 
            // tabControl
            // 
            tabControl.Appearance = TabAppearance.Buttons;
            tabControl.BorderColor = System.Drawing.SystemColors.ControlDark;
            tabControl.Controls.Add(tabPageProperties);
            tabControl.Controls.Add(tabPageOutputs);
            tabControl.Dock = DockStyle.Fill;
            tabControl.HoverColor = System.Drawing.SystemColors.Highlight;
            tabControl.LineColor = System.Drawing.SystemColors.Highlight;
            tabControl.Location = new System.Drawing.Point(0, 0);
            tabControl.Name = "tabControl";
            tabControl.SelectedForeColor = System.Drawing.SystemColors.HighlightText;
            tabControl.SelectedIndex = 0;
            tabControl.SelectTabColor = System.Drawing.SystemColors.ControlLight;
            tabControl.Size = new System.Drawing.Size(800, 450);
            tabControl.SizeMode = TabSizeMode.Fixed;
            tabControl.TabColor = System.Drawing.SystemColors.ControlLight;
            tabControl.TabIndex = 0;
            // 
            // tabPageProperties
            // 
            tabPageProperties.BackColor = System.Drawing.SystemColors.ControlLight;
            tabPageProperties.Controls.Add(dataGridProperties);
            tabPageProperties.Location = new System.Drawing.Point(4, 27);
            tabPageProperties.Name = "tabPageProperties";
            tabPageProperties.Size = new System.Drawing.Size(792, 419);
            tabPageProperties.TabIndex = 0;
            tabPageProperties.Text = "Properties";
            // 
            // dataGridProperties
            // 
            dataGridProperties.AllowUserToAddRows = false;
            dataGridProperties.AllowUserToDeleteRows = false;
            dataGridProperties.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridProperties.Columns.AddRange(new DataGridViewColumn[] { ColumnName, ColumnValue });
            dataGridViewCellStyle1.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Cascadia Mono", 10F);
            dataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.GrayText;
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = DataGridViewTriState.True;
            dataGridProperties.DefaultCellStyle = dataGridViewCellStyle1;
            dataGridProperties.Dock = DockStyle.Fill;
            dataGridProperties.Location = new System.Drawing.Point(0, 0);
            dataGridProperties.Name = "dataGridProperties";
            dataGridProperties.ReadOnly = true;
            dataGridProperties.RowHeadersVisible = false;
            dataGridProperties.Size = new System.Drawing.Size(792, 419);
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
            tabPageOutputs.BackColor = System.Drawing.SystemColors.ControlLight;
            tabPageOutputs.Controls.Add(dataGridOutputs);
            tabPageOutputs.Location = new System.Drawing.Point(4, 27);
            tabPageOutputs.Name = "tabPageOutputs";
            tabPageOutputs.Size = new System.Drawing.Size(792, 419);
            tabPageOutputs.TabIndex = 1;
            tabPageOutputs.Text = "Outputs";
            // 
            // dataGridOutputs
            // 
            dataGridOutputs.AllowUserToAddRows = false;
            dataGridOutputs.AllowUserToDeleteRows = false;
            dataGridOutputs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridOutputs.Columns.AddRange(new DataGridViewColumn[] { Output, TargetEntity, TargetInput, Parameter, Delay, OnlyOnce });
            dataGridViewCellStyle2.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("Cascadia Mono", 10F);
            dataGridViewCellStyle2.ForeColor = System.Drawing.SystemColors.GrayText;
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = DataGridViewTriState.True;
            dataGridOutputs.DefaultCellStyle = dataGridViewCellStyle2;
            dataGridOutputs.Dock = DockStyle.Fill;
            dataGridOutputs.Location = new System.Drawing.Point(0, 0);
            dataGridOutputs.Name = "dataGridOutputs";
            dataGridOutputs.ReadOnly = true;
            dataGridOutputs.RowHeadersVisible = false;
            dataGridOutputs.Size = new System.Drawing.Size(792, 419);
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
            // OnlyOnce
            // 
            OnlyOnce.HeaderText = "Only Once";
            OnlyOnce.Name = "OnlyOnce";
            OnlyOnce.ReadOnly = true;
            // 
            // EntityInfoForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(800, 450);
            Controls.Add(tabControl);
            Name = "EntityInfoForm";
            SizeGripStyle = SizeGripStyle.Hide;
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
        private DataGridViewTextBoxColumn OnlyOnce;
        private FlatTabControl tabControl;
    }
}
