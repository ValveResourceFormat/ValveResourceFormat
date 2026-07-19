using System.Windows.Forms;
using GUI.Controls;

namespace GUI.Forms
{
    partial class EntityInfoControl
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
            var dataGridViewCellStyle3 = new DataGridViewCellStyle();
            var dataGridViewCellStyle4 = new DataGridViewCellStyle();
            var dataGridViewCellStyle5 = new DataGridViewCellStyle();
            var dataGridViewCellStyle6 = new DataGridViewCellStyle();
            var dataGridViewCellStyle7 = new DataGridViewCellStyle();
            var dataGridViewCellStyle8 = new DataGridViewCellStyle();
            var dataGridViewCellStyle9 = new DataGridViewCellStyle();
            var dataGridViewCellStyle10 = new DataGridViewCellStyle();
            var dataGridViewCellStyle11 = new DataGridViewCellStyle();
            var dataGridViewCellStyle12 = new DataGridViewCellStyle();
            tabControl = new ThemedTabControl();
            tabPageProperties = new ThemedTabPage();
            dataGridProperties = new DataGridView();
            ColumnName = new DataGridViewTextBoxColumn();
            ColumnValue = new DataGridViewTextBoxColumn();
            tabPageOutputs = new ThemedTabPage();
            dataGridOutputs = new DataGridView();
            OutputsOutput = new DataGridViewTextBoxColumn();
            OutputsTargetEntity = new DataGridViewTextBoxColumn();
            OutputsTargetInput = new DataGridViewTextBoxColumn();
            OutputsParameter = new DataGridViewTextBoxColumn();
            OutputsDelay = new DataGridViewTextBoxColumn();
            OutputsTimesToFire = new DataGridViewTextBoxColumn();
            tabPageInputs = new TabPage();
            dataGridInputs = new DataGridView();
            InputsSource = new DataGridViewTextBoxColumn();
            InputsOutput = new DataGridViewTextBoxColumn();
            InputsTargetInput = new DataGridViewTextBoxColumn();
            InputsParameter = new DataGridViewTextBoxColumn();
            InputsDelay = new DataGridViewTextBoxColumn();
            InputsTimeToFire = new DataGridViewTextBoxColumn();
            tabControl.SuspendLayout();
            tabPageProperties.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dataGridProperties).BeginInit();
            tabPageOutputs.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dataGridOutputs).BeginInit();
            tabPageInputs.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dataGridInputs).BeginInit();
            SuspendLayout();
            // 
            // tabControl
            // 
            tabControl.BaseTabWidth = 200;
            tabControl.Controls.Add(tabPageProperties);
            tabControl.Controls.Add(tabPageOutputs);
            tabControl.Controls.Add(tabPageInputs);
            tabControl.Dock = DockStyle.Fill;
            tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabControl.EndEllipsis = false;
            tabControl.HideTabHeader = false;
            tabControl.Location = new System.Drawing.Point(0, 0);
            tabControl.Name = "tabControl";
            tabControl.Padding = new System.Drawing.Point(12, 8);
            tabControl.SelectedIndex = 0;
            tabControl.SelectionLine = true;
            tabControl.Size = new System.Drawing.Size(800, 450);
            tabControl.TabHeight = 32;
            tabControl.TabIndex = 0;
            tabControl.TabTopRadius = 0;
            // 
            // tabPageProperties
            // 
            tabPageProperties.BackColor = System.Drawing.Color.FromArgb(236, 236, 236);
            tabPageProperties.Controls.Add(dataGridProperties);
            tabPageProperties.ForeColor = System.Drawing.Color.Black;
            tabPageProperties.Location = new System.Drawing.Point(0, 30);
            tabPageProperties.Name = "tabPageProperties";
            tabPageProperties.Size = new System.Drawing.Size(800, 420);
            tabPageProperties.TabIndex = 0;
            tabPageProperties.Text = "Properties";
            // 
            // dataGridProperties
            // 
            dataGridProperties.AllowUserToAddRows = false;
            dataGridProperties.AllowUserToDeleteRows = false;
            dataGridProperties.AllowUserToResizeRows = false;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.FromArgb(251, 251, 251);
            dataGridProperties.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle1;
            dataGridProperties.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridProperties.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
            dataGridProperties.BackgroundColor = System.Drawing.Color.FromArgb(236, 236, 236);
            dataGridProperties.BorderStyle = BorderStyle.None;
            dataGridProperties.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle2.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = System.Drawing.Color.FromArgb(251, 251, 251);
            dataGridViewCellStyle2.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle2.ForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = DataGridViewTriState.True;
            dataGridProperties.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle2;
            dataGridProperties.Columns.AddRange(new DataGridViewColumn[] { ColumnName, ColumnValue });
            dataGridViewCellStyle3.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle3.BackColor = System.Drawing.Color.FromArgb(236, 236, 236);
            dataGridViewCellStyle3.Font = new System.Drawing.Font("Cascadia Mono", 10F);
            dataGridViewCellStyle3.ForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle3.SelectionBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            dataGridViewCellStyle3.SelectionForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle3.WrapMode = DataGridViewTriState.True;
            dataGridProperties.DefaultCellStyle = dataGridViewCellStyle3;
            dataGridProperties.Dock = DockStyle.Fill;
            dataGridProperties.EnableHeadersVisualStyles = false;
            dataGridProperties.GridColor = System.Drawing.Color.FromArgb(188, 188, 188);
            dataGridProperties.Location = new System.Drawing.Point(0, 0);
            dataGridProperties.Name = "dataGridProperties";
            dataGridProperties.ReadOnly = true;
            dataGridProperties.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle4.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle4.BackColor = System.Drawing.Color.FromArgb(236, 236, 236);
            dataGridViewCellStyle4.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle4.ForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle4.SelectionBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            dataGridViewCellStyle4.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle4.WrapMode = DataGridViewTriState.True;
            dataGridProperties.RowHeadersDefaultCellStyle = dataGridViewCellStyle4;
            dataGridProperties.RowHeadersVisible = false;
            dataGridProperties.SelectionMode = DataGridViewSelectionMode.CellSelect;
            dataGridProperties.Size = new System.Drawing.Size(800, 420);
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
            tabPageOutputs.BackColor = System.Drawing.Color.FromArgb(236, 236, 236);
            tabPageOutputs.Controls.Add(dataGridOutputs);
            tabPageOutputs.ForeColor = System.Drawing.Color.Black;
            tabPageOutputs.Location = new System.Drawing.Point(0, 31);
            tabPageOutputs.Name = "tabPageOutputs";
            tabPageOutputs.Size = new System.Drawing.Size(200, 69);
            tabPageOutputs.TabIndex = 1;
            tabPageOutputs.Text = "Outputs";
            // 
            // dataGridOutputs
            // 
            dataGridOutputs.AllowUserToAddRows = false;
            dataGridOutputs.AllowUserToDeleteRows = false;
            dataGridOutputs.AllowUserToResizeRows = false;
            dataGridViewCellStyle5.BackColor = System.Drawing.Color.FromArgb(251, 251, 251);
            dataGridOutputs.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle5;
            dataGridOutputs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridOutputs.BackgroundColor = System.Drawing.Color.FromArgb(236, 236, 236);
            dataGridOutputs.BorderStyle = BorderStyle.None;
            dataGridOutputs.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle6.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle6.BackColor = System.Drawing.Color.FromArgb(251, 251, 251);
            dataGridViewCellStyle6.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle6.ForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle6.SelectionBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            dataGridViewCellStyle6.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle6.WrapMode = DataGridViewTriState.True;
            dataGridOutputs.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle6;
            dataGridOutputs.Columns.AddRange(new DataGridViewColumn[] { OutputsOutput, OutputsTargetEntity, OutputsTargetInput, OutputsParameter, OutputsDelay, OutputsTimesToFire });
            dataGridViewCellStyle7.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle7.BackColor = System.Drawing.Color.FromArgb(236, 236, 236);
            dataGridViewCellStyle7.Font = new System.Drawing.Font("Cascadia Mono", 10F);
            dataGridViewCellStyle7.ForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle7.SelectionBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            dataGridViewCellStyle7.SelectionForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle7.WrapMode = DataGridViewTriState.True;
            dataGridOutputs.DefaultCellStyle = dataGridViewCellStyle7;
            dataGridOutputs.Dock = DockStyle.Fill;
            dataGridOutputs.EnableHeadersVisualStyles = false;
            dataGridOutputs.GridColor = System.Drawing.Color.FromArgb(188, 188, 188);
            dataGridOutputs.Location = new System.Drawing.Point(0, 0);
            dataGridOutputs.Name = "dataGridOutputs";
            dataGridOutputs.ReadOnly = true;
            dataGridOutputs.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle8.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle8.BackColor = System.Drawing.Color.FromArgb(236, 236, 236);
            dataGridViewCellStyle8.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle8.ForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle8.SelectionBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            dataGridViewCellStyle8.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle8.WrapMode = DataGridViewTriState.True;
            dataGridOutputs.RowHeadersDefaultCellStyle = dataGridViewCellStyle8;
            dataGridOutputs.RowHeadersVisible = false;
            dataGridOutputs.SelectionMode = DataGridViewSelectionMode.CellSelect;
            dataGridOutputs.Size = new System.Drawing.Size(200, 69);
            dataGridOutputs.TabIndex = 0;
            // 
            // OutputsOutput
            // 
            OutputsOutput.HeaderText = "Output";
            OutputsOutput.Name = "OutputsOutput";
            OutputsOutput.ReadOnly = true;
            // 
            // OutputsTargetEntity
            // 
            OutputsTargetEntity.HeaderText = "Target Entity";
            OutputsTargetEntity.Name = "OutputsTargetEntity";
            OutputsTargetEntity.ReadOnly = true;
            // 
            // OutputsTargetInput
            // 
            OutputsTargetInput.HeaderText = "Target Input";
            OutputsTargetInput.Name = "OutputsTargetInput";
            OutputsTargetInput.ReadOnly = true;
            // 
            // OutputsParameter
            // 
            OutputsParameter.HeaderText = "Parameter";
            OutputsParameter.Name = "OutputsParameter";
            OutputsParameter.ReadOnly = true;
            // 
            // OutputsDelay
            // 
            OutputsDelay.HeaderText = "Delay";
            OutputsDelay.Name = "OutputsDelay";
            OutputsDelay.ReadOnly = true;
            // 
            // OutputsTimesToFire
            // 
            OutputsTimesToFire.HeaderText = "Times To Fire";
            OutputsTimesToFire.Name = "OutputsTimesToFire";
            OutputsTimesToFire.ReadOnly = true;
            // 
            // tabPageInputs
            // 
            tabPageInputs.Controls.Add(dataGridInputs);
            tabPageInputs.Location = new System.Drawing.Point(0, 30);
            tabPageInputs.Name = "tabPageInputs";
            tabPageInputs.Size = new System.Drawing.Size(800, 420);
            tabPageInputs.TabIndex = 2;
            tabPageInputs.Text = "Inputs";
            // 
            // dataGridInputs
            // 
            dataGridInputs.AllowUserToAddRows = false;
            dataGridInputs.AllowUserToDeleteRows = false;
            dataGridInputs.AllowUserToResizeRows = false;
            dataGridViewCellStyle9.BackColor = System.Drawing.Color.FromArgb(251, 251, 251);
            dataGridInputs.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle9;
            dataGridInputs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridInputs.BackgroundColor = System.Drawing.Color.FromArgb(236, 236, 236);
            dataGridInputs.BorderStyle = BorderStyle.None;
            dataGridInputs.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle10.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle10.BackColor = System.Drawing.Color.FromArgb(251, 251, 251);
            dataGridViewCellStyle10.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle10.ForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle10.SelectionBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            dataGridViewCellStyle10.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle10.WrapMode = DataGridViewTriState.True;
            dataGridInputs.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle10;
            dataGridInputs.Columns.AddRange(new DataGridViewColumn[] { InputsSource, InputsOutput, InputsTargetInput, InputsParameter, InputsDelay, InputsTimeToFire });
            dataGridViewCellStyle11.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle11.BackColor = System.Drawing.Color.FromArgb(236, 236, 236);
            dataGridViewCellStyle11.Font = new System.Drawing.Font("Cascadia Mono", 10F);
            dataGridViewCellStyle11.ForeColor = System.Drawing.Color.FromArgb(80, 80, 80);
            dataGridViewCellStyle11.SelectionBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            dataGridViewCellStyle11.SelectionForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle11.WrapMode = DataGridViewTriState.True;
            dataGridInputs.DefaultCellStyle = dataGridViewCellStyle11;
            dataGridInputs.Dock = DockStyle.Fill;
            dataGridInputs.EnableHeadersVisualStyles = false;
            dataGridInputs.GridColor = System.Drawing.Color.FromArgb(188, 188, 188);
            dataGridInputs.Location = new System.Drawing.Point(0, 0);
            dataGridInputs.Name = "dataGridInputs";
            dataGridInputs.ReadOnly = true;
            dataGridInputs.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle12.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle12.BackColor = System.Drawing.Color.FromArgb(236, 236, 236);
            dataGridViewCellStyle12.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle12.ForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle12.SelectionBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            dataGridViewCellStyle12.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle12.WrapMode = DataGridViewTriState.True;
            dataGridInputs.RowHeadersDefaultCellStyle = dataGridViewCellStyle12;
            dataGridInputs.RowHeadersVisible = false;
            dataGridInputs.SelectionMode = DataGridViewSelectionMode.CellSelect;
            dataGridInputs.Size = new System.Drawing.Size(800, 420);
            dataGridInputs.TabIndex = 1;
            // 
            // InputsSource
            // 
            InputsSource.HeaderText = "Source";
            InputsSource.Name = "InputsSource";
            InputsSource.ReadOnly = true;
            // 
            // InputsOutput
            // 
            InputsOutput.HeaderText = "Output";
            InputsOutput.Name = "InputsOutput";
            InputsOutput.ReadOnly = true;
            // 
            // InputsTargetInput
            // 
            InputsTargetInput.HeaderText = "Target Input";
            InputsTargetInput.Name = "InputsTargetInput";
            InputsTargetInput.ReadOnly = true;
            // 
            // InputsParameter
            // 
            InputsParameter.HeaderText = "Parameter";
            InputsParameter.Name = "InputsParameter";
            InputsParameter.ReadOnly = true;
            // 
            // InputsDelay
            // 
            InputsDelay.HeaderText = "Delay";
            InputsDelay.Name = "InputsDelay";
            InputsDelay.ReadOnly = true;
            // 
            // InputsTimeToFire
            // 
            InputsTimeToFire.HeaderText = "Times To Fire";
            InputsTimeToFire.Name = "InputsTimeToFire";
            InputsTimeToFire.ReadOnly = true;
            // 
            // EntityInfoControl
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(tabControl);
            Name = "EntityInfoControl";
            Size = new System.Drawing.Size(800, 450);
            tabControl.ResumeLayout(false);
            tabPageProperties.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dataGridProperties).EndInit();
            tabPageOutputs.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dataGridOutputs).EndInit();
            tabPageInputs.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dataGridInputs).EndInit();
            ResumeLayout(false);
        }

        #endregion
        private DataGridView dataGridProperties;
        private DataGridViewTextBoxColumn ColumnName;
        private DataGridViewTextBoxColumn ColumnValue;
        private DataGridView dataGridOutputs;
        private DataGridViewTextBoxColumn OutputsOutput;
        private DataGridViewTextBoxColumn OutputsTargetEntity;
        private DataGridViewTextBoxColumn OutputsTargetInput;
        private DataGridViewTextBoxColumn OutputsParameter;
        private DataGridViewTextBoxColumn OutputsDelay;
        private DataGridViewTextBoxColumn OutputsTimesToFire;
        private ThemedTabControl tabControl;
        private TabPage tabPageInputs;
        private ThemedTabPage tabPageProperties;
        private ThemedTabPage tabPageOutputs;
        private DataGridView dataGridInputs;
        private DataGridViewTextBoxColumn InputsSource;
        private DataGridViewTextBoxColumn InputsOutput;
        private DataGridViewTextBoxColumn InputsTargetInput;
        private DataGridViewTextBoxColumn InputsParameter;
        private DataGridViewTextBoxColumn InputsDelay;
        private DataGridViewTextBoxColumn InputsTimeToFire;
    }
}
