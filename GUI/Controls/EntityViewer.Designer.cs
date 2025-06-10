namespace GUI.Types.Viewers;

partial class EntityViewer
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
        var dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
        EntityViewerGrid = new System.Windows.Forms.DataGridView();
        Class = new System.Windows.Forms.DataGridViewTextBoxColumn();
        targetname = new System.Windows.Forms.DataGridViewTextBoxColumn();
        tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
        tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
        groupBox5 = new System.Windows.Forms.GroupBox();
        tableLayoutPanel3 = new System.Windows.Forms.TableLayoutPanel();
        tableLayoutPanel4 = new System.Windows.Forms.TableLayoutPanel();
        groupBox3 = new System.Windows.Forms.GroupBox();
        tableLayoutPanel6 = new System.Windows.Forms.TableLayoutPanel();
        KeyValue_Key = new System.Windows.Forms.TextBox();
        KeyValue_Value = new System.Windows.Forms.TextBox();
        KeyValue_MatchWholeValue = new System.Windows.Forms.CheckBox();
        label1 = new System.Windows.Forms.Label();
        groupBox2 = new System.Windows.Forms.GroupBox();
        tableLayoutPanel5 = new System.Windows.Forms.TableLayoutPanel();
        ObjectsToInclude_Everything = new System.Windows.Forms.RadioButton();
        ObjectsToInclude_MeshEntities = new System.Windows.Forms.RadioButton();
        ObjectsToInclude_PointEntities = new System.Windows.Forms.RadioButton();
        ObjectsToInclude_Class = new System.Windows.Forms.RadioButton();
        ObjectsToInclude_ClassTextBox = new System.Windows.Forms.TextBox();
        EntityPropertiesGroup = new System.Windows.Forms.GroupBox();
        EntityInfo = new GUI.Forms.EntityInfoControl();
        ((System.ComponentModel.ISupportInitialize)EntityViewerGrid).BeginInit();
        tableLayoutPanel1.SuspendLayout();
        tableLayoutPanel2.SuspendLayout();
        groupBox5.SuspendLayout();
        tableLayoutPanel3.SuspendLayout();
        tableLayoutPanel4.SuspendLayout();
        groupBox3.SuspendLayout();
        tableLayoutPanel6.SuspendLayout();
        groupBox2.SuspendLayout();
        tableLayoutPanel5.SuspendLayout();
        EntityPropertiesGroup.SuspendLayout();
        SuspendLayout();
        // 
        // EntityViewerGrid
        // 
        EntityViewerGrid.AllowUserToAddRows = false;
        EntityViewerGrid.AllowUserToDeleteRows = false;
        EntityViewerGrid.AllowUserToResizeRows = false;
        dataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.ActiveBorder;
        EntityViewerGrid.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle1;
        EntityViewerGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
        EntityViewerGrid.BorderStyle = System.Windows.Forms.BorderStyle.None;
        EntityViewerGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        EntityViewerGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { Class, targetname });
        EntityViewerGrid.Dock = System.Windows.Forms.DockStyle.Fill;
        EntityViewerGrid.Location = new System.Drawing.Point(3, 19);
        EntityViewerGrid.Margin = new System.Windows.Forms.Padding(0);
        EntityViewerGrid.MultiSelect = false;
        EntityViewerGrid.Name = "EntityViewerGrid";
        EntityViewerGrid.ReadOnly = true;
        EntityViewerGrid.RowHeadersVisible = false;
        EntityViewerGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
        EntityViewerGrid.Size = new System.Drawing.Size(500, 530);
        EntityViewerGrid.TabIndex = 0;
        EntityViewerGrid.CellClick += EntityViewerGrid_CellClick;
        // 
        // Class
        // 
        Class.HeaderText = "Class";
        Class.Name = "Class";
        Class.ReadOnly = true;
        // 
        // targetname
        // 
        targetname.HeaderText = "Name";
        targetname.Name = "targetname";
        targetname.ReadOnly = true;
        // 
        // tableLayoutPanel1
        // 
        tableLayoutPanel1.ColumnCount = 2;
        tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanel1.Controls.Add(tableLayoutPanel2, 0, 0);
        tableLayoutPanel1.Controls.Add(EntityPropertiesGroup, 1, 0);
        tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
        tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
        tableLayoutPanel1.Name = "tableLayoutPanel1";
        tableLayoutPanel1.RowCount = 1;
        tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanel1.Size = new System.Drawing.Size(1025, 741);
        tableLayoutPanel1.TabIndex = 1;
        // 
        // tableLayoutPanel2
        // 
        tableLayoutPanel2.ColumnCount = 1;
        tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanel2.Controls.Add(groupBox5, 0, 0);
        tableLayoutPanel2.Controls.Add(tableLayoutPanel3, 0, 1);
        tableLayoutPanel2.Dock = System.Windows.Forms.DockStyle.Fill;
        tableLayoutPanel2.Location = new System.Drawing.Point(0, 0);
        tableLayoutPanel2.Margin = new System.Windows.Forms.Padding(0);
        tableLayoutPanel2.Name = "tableLayoutPanel2";
        tableLayoutPanel2.RowCount = 2;
        tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 75.31306F));
        tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 24.6869411F));
        tableLayoutPanel2.Size = new System.Drawing.Size(512, 741);
        tableLayoutPanel2.TabIndex = 1;
        // 
        // groupBox5
        // 
        groupBox5.Controls.Add(EntityViewerGrid);
        groupBox5.Dock = System.Windows.Forms.DockStyle.Fill;
        groupBox5.Location = new System.Drawing.Point(3, 3);
        groupBox5.Name = "groupBox5";
        groupBox5.Size = new System.Drawing.Size(506, 552);
        groupBox5.TabIndex = 3;
        groupBox5.TabStop = false;
        groupBox5.Text = "Entity List";
        // 
        // tableLayoutPanel3
        // 
        tableLayoutPanel3.ColumnCount = 2;
        tableLayoutPanel3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanel3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanel3.Controls.Add(tableLayoutPanel4, 1, 0);
        tableLayoutPanel3.Controls.Add(groupBox2, 0, 0);
        tableLayoutPanel3.Dock = System.Windows.Forms.DockStyle.Fill;
        tableLayoutPanel3.Location = new System.Drawing.Point(0, 558);
        tableLayoutPanel3.Margin = new System.Windows.Forms.Padding(0);
        tableLayoutPanel3.Name = "tableLayoutPanel3";
        tableLayoutPanel3.RowCount = 1;
        tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
        tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
        tableLayoutPanel3.Size = new System.Drawing.Size(512, 183);
        tableLayoutPanel3.TabIndex = 0;
        // 
        // tableLayoutPanel4
        // 
        tableLayoutPanel4.ColumnCount = 1;
        tableLayoutPanel4.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        tableLayoutPanel4.Controls.Add(groupBox3, 0, 0);
        tableLayoutPanel4.Dock = System.Windows.Forms.DockStyle.Fill;
        tableLayoutPanel4.Location = new System.Drawing.Point(256, 0);
        tableLayoutPanel4.Margin = new System.Windows.Forms.Padding(0);
        tableLayoutPanel4.Name = "tableLayoutPanel4";
        tableLayoutPanel4.RowCount = 3;
        tableLayoutPanel4.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 55F));
        tableLayoutPanel4.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanel4.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanel4.Size = new System.Drawing.Size(256, 183);
        tableLayoutPanel4.TabIndex = 0;
        // 
        // groupBox3
        // 
        groupBox3.Controls.Add(tableLayoutPanel6);
        groupBox3.Dock = System.Windows.Forms.DockStyle.Fill;
        groupBox3.Location = new System.Drawing.Point(3, 3);
        groupBox3.Name = "groupBox3";
        groupBox3.Size = new System.Drawing.Size(250, 49);
        groupBox3.TabIndex = 1;
        groupBox3.TabStop = false;
        groupBox3.Text = "Key / Value";
        // 
        // tableLayoutPanel6
        // 
        tableLayoutPanel6.ColumnCount = 4;
        tableLayoutPanel6.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.3333321F));
        tableLayoutPanel6.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 20F));
        tableLayoutPanel6.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.3333321F));
        tableLayoutPanel6.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.3333321F));
        tableLayoutPanel6.Controls.Add(KeyValue_Key, 0, 0);
        tableLayoutPanel6.Controls.Add(KeyValue_Value, 2, 0);
        tableLayoutPanel6.Controls.Add(KeyValue_MatchWholeValue, 3, 0);
        tableLayoutPanel6.Controls.Add(label1, 1, 0);
        tableLayoutPanel6.Dock = System.Windows.Forms.DockStyle.Fill;
        tableLayoutPanel6.Location = new System.Drawing.Point(3, 19);
        tableLayoutPanel6.Name = "tableLayoutPanel6";
        tableLayoutPanel6.RowCount = 1;
        tableLayoutPanel6.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
        tableLayoutPanel6.Size = new System.Drawing.Size(244, 27);
        tableLayoutPanel6.TabIndex = 0;
        // 
        // KeyValue_Key
        // 
        KeyValue_Key.Dock = System.Windows.Forms.DockStyle.Fill;
        KeyValue_Key.Location = new System.Drawing.Point(3, 3);
        KeyValue_Key.Multiline = true;
        KeyValue_Key.Name = "KeyValue_Key";
        KeyValue_Key.Size = new System.Drawing.Size(68, 21);
        KeyValue_Key.TabIndex = 0;
        KeyValue_Key.TextChanged += KeyValue_Key_TextChanged;
        // 
        // KeyValue_Value
        // 
        KeyValue_Value.Dock = System.Windows.Forms.DockStyle.Fill;
        KeyValue_Value.Location = new System.Drawing.Point(97, 3);
        KeyValue_Value.Multiline = true;
        KeyValue_Value.Name = "KeyValue_Value";
        KeyValue_Value.Size = new System.Drawing.Size(68, 21);
        KeyValue_Value.TabIndex = 1;
        KeyValue_Value.TextChanged += KeyValue_Value_TextChanged;
        // 
        // KeyValue_MatchWholeValue
        // 
        KeyValue_MatchWholeValue.AutoSize = true;
        KeyValue_MatchWholeValue.Dock = System.Windows.Forms.DockStyle.Fill;
        KeyValue_MatchWholeValue.Location = new System.Drawing.Point(171, 3);
        KeyValue_MatchWholeValue.Name = "KeyValue_MatchWholeValue";
        KeyValue_MatchWholeValue.Size = new System.Drawing.Size(70, 21);
        KeyValue_MatchWholeValue.TabIndex = 2;
        KeyValue_MatchWholeValue.Text = "Match whole value";
        KeyValue_MatchWholeValue.UseVisualStyleBackColor = true;
        KeyValue_MatchWholeValue.CheckedChanged += KeyValue_MatchWholeValue_CheckedChanged;
        // 
        // label1
        // 
        label1.AutoSize = true;
        label1.Dock = System.Windows.Forms.DockStyle.Fill;
        label1.Location = new System.Drawing.Point(74, 0);
        label1.Margin = new System.Windows.Forms.Padding(0);
        label1.Name = "label1";
        label1.Size = new System.Drawing.Size(20, 27);
        label1.TabIndex = 3;
        label1.Text = "=";
        label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        label1.UseCompatibleTextRendering = true;
        // 
        // groupBox2
        // 
        groupBox2.Controls.Add(tableLayoutPanel5);
        groupBox2.Dock = System.Windows.Forms.DockStyle.Fill;
        groupBox2.Location = new System.Drawing.Point(3, 3);
        groupBox2.Name = "groupBox2";
        groupBox2.Size = new System.Drawing.Size(250, 177);
        groupBox2.TabIndex = 0;
        groupBox2.TabStop = false;
        groupBox2.Text = "Objects To Include";
        // 
        // tableLayoutPanel5
        // 
        tableLayoutPanel5.ColumnCount = 1;
        tableLayoutPanel5.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        tableLayoutPanel5.Controls.Add(ObjectsToInclude_Everything, 0, 0);
        tableLayoutPanel5.Controls.Add(ObjectsToInclude_MeshEntities, 0, 1);
        tableLayoutPanel5.Controls.Add(ObjectsToInclude_PointEntities, 0, 2);
        tableLayoutPanel5.Controls.Add(ObjectsToInclude_Class, 0, 3);
        tableLayoutPanel5.Controls.Add(ObjectsToInclude_ClassTextBox, 0, 4);
        tableLayoutPanel5.Dock = System.Windows.Forms.DockStyle.Fill;
        tableLayoutPanel5.Location = new System.Drawing.Point(3, 19);
        tableLayoutPanel5.Name = "tableLayoutPanel5";
        tableLayoutPanel5.RowCount = 5;
        tableLayoutPanel5.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
        tableLayoutPanel5.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
        tableLayoutPanel5.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
        tableLayoutPanel5.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
        tableLayoutPanel5.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
        tableLayoutPanel5.Size = new System.Drawing.Size(244, 155);
        tableLayoutPanel5.TabIndex = 0;
        // 
        // ObjectsToInclude_Everything
        // 
        ObjectsToInclude_Everything.AutoSize = true;
        ObjectsToInclude_Everything.Checked = true;
        ObjectsToInclude_Everything.Dock = System.Windows.Forms.DockStyle.Fill;
        ObjectsToInclude_Everything.Location = new System.Drawing.Point(3, 3);
        ObjectsToInclude_Everything.Name = "ObjectsToInclude_Everything";
        ObjectsToInclude_Everything.Size = new System.Drawing.Size(238, 25);
        ObjectsToInclude_Everything.TabIndex = 0;
        ObjectsToInclude_Everything.TabStop = true;
        ObjectsToInclude_Everything.Text = "Everything";
        ObjectsToInclude_Everything.UseVisualStyleBackColor = true;
        ObjectsToInclude_Everything.CheckedChanged += ObjectsToInclude_Everything_CheckedChanged;
        // 
        // ObjectsToInclude_MeshEntities
        // 
        ObjectsToInclude_MeshEntities.AutoSize = true;
        ObjectsToInclude_MeshEntities.Dock = System.Windows.Forms.DockStyle.Fill;
        ObjectsToInclude_MeshEntities.Location = new System.Drawing.Point(3, 34);
        ObjectsToInclude_MeshEntities.Name = "ObjectsToInclude_MeshEntities";
        ObjectsToInclude_MeshEntities.Size = new System.Drawing.Size(238, 25);
        ObjectsToInclude_MeshEntities.TabIndex = 1;
        ObjectsToInclude_MeshEntities.Text = "Mesh Entities";
        ObjectsToInclude_MeshEntities.UseVisualStyleBackColor = true;
        ObjectsToInclude_MeshEntities.CheckedChanged += ObjectsToInclude_MeshEntities_CheckedChanged;
        // 
        // ObjectsToInclude_PointEntities
        // 
        ObjectsToInclude_PointEntities.AutoSize = true;
        ObjectsToInclude_PointEntities.Dock = System.Windows.Forms.DockStyle.Fill;
        ObjectsToInclude_PointEntities.Location = new System.Drawing.Point(3, 65);
        ObjectsToInclude_PointEntities.Name = "ObjectsToInclude_PointEntities";
        ObjectsToInclude_PointEntities.Size = new System.Drawing.Size(238, 25);
        ObjectsToInclude_PointEntities.TabIndex = 2;
        ObjectsToInclude_PointEntities.Text = "Point Entities";
        ObjectsToInclude_PointEntities.UseVisualStyleBackColor = true;
        ObjectsToInclude_PointEntities.CheckedChanged += ObjectsToInclude_PointEntities_CheckedChanged;
        // 
        // ObjectsToInclude_Class
        // 
        ObjectsToInclude_Class.AutoSize = true;
        ObjectsToInclude_Class.Dock = System.Windows.Forms.DockStyle.Fill;
        ObjectsToInclude_Class.Location = new System.Drawing.Point(3, 96);
        ObjectsToInclude_Class.Name = "ObjectsToInclude_Class";
        ObjectsToInclude_Class.Size = new System.Drawing.Size(238, 25);
        ObjectsToInclude_Class.TabIndex = 3;
        ObjectsToInclude_Class.Text = "Class:";
        ObjectsToInclude_Class.UseVisualStyleBackColor = true;
        ObjectsToInclude_Class.CheckedChanged += ObjectsToInclude_Class_CheckedChanged;
        // 
        // ObjectsToInclude_ClassTextBox
        // 
        ObjectsToInclude_ClassTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
        ObjectsToInclude_ClassTextBox.Enabled = false;
        ObjectsToInclude_ClassTextBox.Location = new System.Drawing.Point(3, 127);
        ObjectsToInclude_ClassTextBox.Name = "ObjectsToInclude_ClassTextBox";
        ObjectsToInclude_ClassTextBox.Size = new System.Drawing.Size(238, 23);
        ObjectsToInclude_ClassTextBox.TabIndex = 4;
        ObjectsToInclude_ClassTextBox.TextChanged += ObjectsToInclude_ClassTextBox_TextChanged;
        // 
        // EntityPropertiesGroup
        // 
        EntityPropertiesGroup.Controls.Add(EntityInfo);
        EntityPropertiesGroup.Dock = System.Windows.Forms.DockStyle.Fill;
        EntityPropertiesGroup.Location = new System.Drawing.Point(515, 3);
        EntityPropertiesGroup.Name = "EntityPropertiesGroup";
        EntityPropertiesGroup.Size = new System.Drawing.Size(507, 735);
        EntityPropertiesGroup.TabIndex = 2;
        EntityPropertiesGroup.TabStop = false;
        EntityPropertiesGroup.Text = "Entity Properties - ";
        // 
        // EntityInfo
        // 
        EntityInfo.Dock = System.Windows.Forms.DockStyle.Fill;
        EntityInfo.Location = new System.Drawing.Point(3, 19);
        EntityInfo.Name = "EntityInfo";
        EntityInfo.Size = new System.Drawing.Size(501, 713);
        EntityInfo.TabIndex = 0;
        // 
        // EntityViewer
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        Controls.Add(tableLayoutPanel1);
        Name = "EntityViewer";
        Size = new System.Drawing.Size(1025, 741);
        ((System.ComponentModel.ISupportInitialize)EntityViewerGrid).EndInit();
        tableLayoutPanel1.ResumeLayout(false);
        tableLayoutPanel2.ResumeLayout(false);
        groupBox5.ResumeLayout(false);
        tableLayoutPanel3.ResumeLayout(false);
        tableLayoutPanel4.ResumeLayout(false);
        groupBox3.ResumeLayout(false);
        tableLayoutPanel6.ResumeLayout(false);
        tableLayoutPanel6.PerformLayout();
        groupBox2.ResumeLayout(false);
        tableLayoutPanel5.ResumeLayout(false);
        tableLayoutPanel5.PerformLayout();
        EntityPropertiesGroup.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private System.Windows.Forms.DataGridView EntityViewerGrid;
    private System.Windows.Forms.DataGridViewTextBoxColumn Class;
    private System.Windows.Forms.DataGridViewTextBoxColumn targetname;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanel3;
    private System.Windows.Forms.GroupBox groupBox2;
    private System.Windows.Forms.GroupBox groupBox3;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanel4;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanel5;
    private System.Windows.Forms.RadioButton ObjectsToInclude_Everything;
    private System.Windows.Forms.RadioButton ObjectsToInclude_MeshEntities;
    private System.Windows.Forms.RadioButton ObjectsToInclude_PointEntities;
    private System.Windows.Forms.RadioButton ObjectsToInclude_Class;
    private System.Windows.Forms.TextBox ObjectsToInclude_ClassTextBox;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanel6;
    private System.Windows.Forms.TextBox KeyValue_Key;
    private System.Windows.Forms.TextBox KeyValue_Value;
    private System.Windows.Forms.CheckBox KeyValue_MatchWholeValue;
    private System.Windows.Forms.Label label1;
    private System.Windows.Forms.GroupBox EntityPropertiesGroup;
    private System.Windows.Forms.GroupBox groupBox5;
    private Forms.EntityInfoControl EntityInfo;
}
