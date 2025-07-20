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
        tableLayoutPanelLeft = new System.Windows.Forms.TableLayoutPanel();
        groupBox5 = new System.Windows.Forms.GroupBox();
        tableLayoutPanelFiltersContainer = new System.Windows.Forms.TableLayoutPanel();
        tableLayoutPanelKeysContainers = new System.Windows.Forms.TableLayoutPanel();
        groupBox3 = new System.Windows.Forms.GroupBox();
        tableLayoutPanelKeys = new System.Windows.Forms.TableLayoutPanel();
        KeyValue_Key = new System.Windows.Forms.TextBox();
        KeyValue_Value = new System.Windows.Forms.TextBox();
        KeyValue_MatchWholeValue = new System.Windows.Forms.CheckBox();
        label1 = new System.Windows.Forms.Label();
        label3 = new System.Windows.Forms.Label();
        groupBox2 = new System.Windows.Forms.GroupBox();
        tableLayoutPanelObjects = new System.Windows.Forms.TableLayoutPanel();
        ObjectsToInclude_Everything = new System.Windows.Forms.RadioButton();
        ObjectsToInclude_MeshEntities = new System.Windows.Forms.RadioButton();
        ObjectsToInclude_PointEntities = new System.Windows.Forms.RadioButton();
        ObjectsToInclude_ClassTextBox = new System.Windows.Forms.TextBox();
        label2 = new System.Windows.Forms.Label();
        EntityPropertiesGroup = new System.Windows.Forms.GroupBox();
        EntityInfo = new GUI.Forms.EntityInfoControl();
        splitContainer = new System.Windows.Forms.SplitContainer();
        ((System.ComponentModel.ISupportInitialize)EntityViewerGrid).BeginInit();
        tableLayoutPanelLeft.SuspendLayout();
        groupBox5.SuspendLayout();
        tableLayoutPanelFiltersContainer.SuspendLayout();
        tableLayoutPanelKeysContainers.SuspendLayout();
        groupBox3.SuspendLayout();
        tableLayoutPanelKeys.SuspendLayout();
        groupBox2.SuspendLayout();
        tableLayoutPanelObjects.SuspendLayout();
        EntityPropertiesGroup.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
        splitContainer.Panel1.SuspendLayout();
        splitContainer.Panel2.SuspendLayout();
        splitContainer.SuspendLayout();
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
        EntityViewerGrid.CellDoubleClick += EntityViewerGrid_CellDoubleClick;
        EntityViewerGrid.SelectionChanged += EntityViewerGrid_SelectionChanged;
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
        // tableLayoutPanelLeft
        // 
        tableLayoutPanelLeft.ColumnCount = 1;
        tableLayoutPanelLeft.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanelLeft.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanelLeft.Controls.Add(groupBox5, 0, 0);
        tableLayoutPanelLeft.Controls.Add(tableLayoutPanelFiltersContainer, 0, 1);
        tableLayoutPanelLeft.Dock = System.Windows.Forms.DockStyle.Fill;
        tableLayoutPanelLeft.Location = new System.Drawing.Point(0, 0);
        tableLayoutPanelLeft.Margin = new System.Windows.Forms.Padding(0);
        tableLayoutPanelLeft.Name = "tableLayoutPanelLeft";
        tableLayoutPanelLeft.RowCount = 2;
        tableLayoutPanelLeft.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 75.31306F));
        tableLayoutPanelLeft.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 24.6869411F));
        tableLayoutPanelLeft.Size = new System.Drawing.Size(512, 741);
        tableLayoutPanelLeft.TabIndex = 1;
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
        // tableLayoutPanelFiltersContainer
        // 
        tableLayoutPanelFiltersContainer.ColumnCount = 2;
        tableLayoutPanelFiltersContainer.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanelFiltersContainer.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
        tableLayoutPanelFiltersContainer.Controls.Add(tableLayoutPanelKeysContainers, 1, 0);
        tableLayoutPanelFiltersContainer.Controls.Add(groupBox2, 0, 0);
        tableLayoutPanelFiltersContainer.Dock = System.Windows.Forms.DockStyle.Fill;
        tableLayoutPanelFiltersContainer.Location = new System.Drawing.Point(0, 558);
        tableLayoutPanelFiltersContainer.Margin = new System.Windows.Forms.Padding(0);
        tableLayoutPanelFiltersContainer.Name = "tableLayoutPanelFiltersContainer";
        tableLayoutPanelFiltersContainer.RowCount = 1;
        tableLayoutPanelFiltersContainer.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
        tableLayoutPanelFiltersContainer.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
        tableLayoutPanelFiltersContainer.Size = new System.Drawing.Size(512, 183);
        tableLayoutPanelFiltersContainer.TabIndex = 0;
        // 
        // tableLayoutPanelKeysContainers
        // 
        tableLayoutPanelKeysContainers.ColumnCount = 1;
        tableLayoutPanelKeysContainers.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        tableLayoutPanelKeysContainers.Controls.Add(groupBox3, 0, 0);
        tableLayoutPanelKeysContainers.Dock = System.Windows.Forms.DockStyle.Fill;
        tableLayoutPanelKeysContainers.Location = new System.Drawing.Point(256, 0);
        tableLayoutPanelKeysContainers.Margin = new System.Windows.Forms.Padding(0);
        tableLayoutPanelKeysContainers.Name = "tableLayoutPanelKeysContainers";
        tableLayoutPanelKeysContainers.RowCount = 1;
        tableLayoutPanelKeysContainers.RowStyles.Add(new System.Windows.Forms.RowStyle());
        tableLayoutPanelKeysContainers.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
        tableLayoutPanelKeysContainers.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
        tableLayoutPanelKeysContainers.Size = new System.Drawing.Size(256, 183);
        tableLayoutPanelKeysContainers.TabIndex = 0;
        // 
        // groupBox3
        // 
        groupBox3.Controls.Add(tableLayoutPanelKeys);
        groupBox3.Dock = System.Windows.Forms.DockStyle.Fill;
        groupBox3.Location = new System.Drawing.Point(3, 3);
        groupBox3.Name = "groupBox3";
        groupBox3.Size = new System.Drawing.Size(250, 177);
        groupBox3.TabIndex = 1;
        groupBox3.TabStop = false;
        groupBox3.Text = "Key / Value";
        // 
        // tableLayoutPanelKeys
        // 
        tableLayoutPanelKeys.ColumnCount = 2;
        tableLayoutPanelKeys.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
        tableLayoutPanelKeys.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
        tableLayoutPanelKeys.Controls.Add(KeyValue_Key, 1, 0);
        tableLayoutPanelKeys.Controls.Add(KeyValue_Value, 1, 1);
        tableLayoutPanelKeys.Controls.Add(KeyValue_MatchWholeValue, 1, 2);
        tableLayoutPanelKeys.Controls.Add(label1, 0, 0);
        tableLayoutPanelKeys.Controls.Add(label3, 0, 1);
        tableLayoutPanelKeys.Dock = System.Windows.Forms.DockStyle.Fill;
        tableLayoutPanelKeys.Location = new System.Drawing.Point(3, 19);
        tableLayoutPanelKeys.Name = "tableLayoutPanelKeys";
        tableLayoutPanelKeys.RowCount = 4;
        tableLayoutPanelKeys.RowStyles.Add(new System.Windows.Forms.RowStyle());
        tableLayoutPanelKeys.RowStyles.Add(new System.Windows.Forms.RowStyle());
        tableLayoutPanelKeys.RowStyles.Add(new System.Windows.Forms.RowStyle());
        tableLayoutPanelKeys.RowStyles.Add(new System.Windows.Forms.RowStyle());
        tableLayoutPanelKeys.Size = new System.Drawing.Size(244, 155);
        tableLayoutPanelKeys.TabIndex = 0;
        // 
        // KeyValue_Key
        // 
        KeyValue_Key.Dock = System.Windows.Forms.DockStyle.Fill;
        KeyValue_Key.Location = new System.Drawing.Point(44, 3);
        KeyValue_Key.Name = "KeyValue_Key";
        KeyValue_Key.Size = new System.Drawing.Size(197, 23);
        KeyValue_Key.TabIndex = 0;
        KeyValue_Key.TextChanged += KeyValue_Key_TextChanged;
        // 
        // KeyValue_Value
        // 
        KeyValue_Value.Dock = System.Windows.Forms.DockStyle.Fill;
        KeyValue_Value.Location = new System.Drawing.Point(44, 32);
        KeyValue_Value.Name = "KeyValue_Value";
        KeyValue_Value.Size = new System.Drawing.Size(197, 23);
        KeyValue_Value.TabIndex = 1;
        KeyValue_Value.TextChanged += KeyValue_Value_TextChanged;
        // 
        // KeyValue_MatchWholeValue
        // 
        KeyValue_MatchWholeValue.AutoSize = true;
        KeyValue_MatchWholeValue.Dock = System.Windows.Forms.DockStyle.Fill;
        KeyValue_MatchWholeValue.Location = new System.Drawing.Point(44, 61);
        KeyValue_MatchWholeValue.Name = "KeyValue_MatchWholeValue";
        KeyValue_MatchWholeValue.Size = new System.Drawing.Size(197, 19);
        KeyValue_MatchWholeValue.TabIndex = 2;
        KeyValue_MatchWholeValue.Text = "Match whole value";
        KeyValue_MatchWholeValue.UseVisualStyleBackColor = true;
        KeyValue_MatchWholeValue.CheckedChanged += KeyValue_MatchWholeValue_CheckedChanged;
        // 
        // label1
        // 
        label1.AutoSize = true;
        label1.Dock = System.Windows.Forms.DockStyle.Fill;
        label1.Location = new System.Drawing.Point(3, 0);
        label1.Name = "label1";
        label1.Size = new System.Drawing.Size(35, 29);
        label1.TabIndex = 3;
        label1.Text = "Key";
        label1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        // 
        // label3
        // 
        label3.AutoSize = true;
        label3.Dock = System.Windows.Forms.DockStyle.Fill;
        label3.Location = new System.Drawing.Point(3, 29);
        label3.Name = "label3";
        label3.Size = new System.Drawing.Size(35, 29);
        label3.TabIndex = 4;
        label3.Text = "Value";
        label3.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        // 
        // groupBox2
        // 
        groupBox2.Controls.Add(tableLayoutPanelObjects);
        groupBox2.Dock = System.Windows.Forms.DockStyle.Fill;
        groupBox2.Location = new System.Drawing.Point(3, 3);
        groupBox2.Name = "groupBox2";
        groupBox2.Size = new System.Drawing.Size(250, 177);
        groupBox2.TabIndex = 0;
        groupBox2.TabStop = false;
        groupBox2.Text = "Objects To Include";
        // 
        // tableLayoutPanelObjects
        // 
        tableLayoutPanelObjects.ColumnCount = 1;
        tableLayoutPanelObjects.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        tableLayoutPanelObjects.Controls.Add(ObjectsToInclude_Everything, 0, 0);
        tableLayoutPanelObjects.Controls.Add(ObjectsToInclude_MeshEntities, 0, 1);
        tableLayoutPanelObjects.Controls.Add(ObjectsToInclude_PointEntities, 0, 2);
        tableLayoutPanelObjects.Controls.Add(ObjectsToInclude_ClassTextBox, 0, 4);
        tableLayoutPanelObjects.Controls.Add(label2, 0, 3);
        tableLayoutPanelObjects.Dock = System.Windows.Forms.DockStyle.Fill;
        tableLayoutPanelObjects.Location = new System.Drawing.Point(3, 19);
        tableLayoutPanelObjects.Name = "tableLayoutPanelObjects";
        tableLayoutPanelObjects.RowCount = 5;
        tableLayoutPanelObjects.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
        tableLayoutPanelObjects.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
        tableLayoutPanelObjects.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
        tableLayoutPanelObjects.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
        tableLayoutPanelObjects.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
        tableLayoutPanelObjects.Size = new System.Drawing.Size(244, 155);
        tableLayoutPanelObjects.TabIndex = 0;
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
        // ObjectsToInclude_ClassTextBox
        // 
        ObjectsToInclude_ClassTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
        ObjectsToInclude_ClassTextBox.Location = new System.Drawing.Point(3, 127);
        ObjectsToInclude_ClassTextBox.Name = "ObjectsToInclude_ClassTextBox";
        ObjectsToInclude_ClassTextBox.Size = new System.Drawing.Size(238, 23);
        ObjectsToInclude_ClassTextBox.TabIndex = 4;
        ObjectsToInclude_ClassTextBox.TextChanged += ObjectsToInclude_ClassTextBox_TextChanged;
        // 
        // label2
        // 
        label2.AutoSize = true;
        label2.Dock = System.Windows.Forms.DockStyle.Fill;
        label2.Location = new System.Drawing.Point(3, 93);
        label2.Name = "label2";
        label2.Size = new System.Drawing.Size(238, 31);
        label2.TabIndex = 5;
        label2.Text = "Classname:";
        label2.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        // 
        // EntityPropertiesGroup
        // 
        EntityPropertiesGroup.Controls.Add(EntityInfo);
        EntityPropertiesGroup.Dock = System.Windows.Forms.DockStyle.Fill;
        EntityPropertiesGroup.Location = new System.Drawing.Point(0, 0);
        EntityPropertiesGroup.Name = "EntityPropertiesGroup";
        EntityPropertiesGroup.Size = new System.Drawing.Size(509, 741);
        EntityPropertiesGroup.TabIndex = 2;
        EntityPropertiesGroup.TabStop = false;
        EntityPropertiesGroup.Text = "Entity Properties - ";
        // 
        // EntityInfo
        // 
        EntityInfo.Dock = System.Windows.Forms.DockStyle.Fill;
        EntityInfo.Location = new System.Drawing.Point(3, 19);
        EntityInfo.Name = "EntityInfo";
        EntityInfo.Size = new System.Drawing.Size(503, 719);
        EntityInfo.TabIndex = 0;
        // 
        // splitContainer
        // 
        splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
        splitContainer.Location = new System.Drawing.Point(0, 0);
        splitContainer.Name = "splitContainer";
        // 
        // splitContainer.Panel1
        // 
        splitContainer.Panel1.Controls.Add(tableLayoutPanelLeft);
        // 
        // splitContainer.Panel2
        // 
        splitContainer.Panel2.Controls.Add(EntityPropertiesGroup);
        splitContainer.Size = new System.Drawing.Size(1025, 741);
        splitContainer.SplitterDistance = 512;
        splitContainer.TabIndex = 2;
        // 
        // EntityViewer
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        Controls.Add(splitContainer);
        Name = "EntityViewer";
        Size = new System.Drawing.Size(1025, 741);
        ((System.ComponentModel.ISupportInitialize)EntityViewerGrid).EndInit();
        tableLayoutPanelLeft.ResumeLayout(false);
        groupBox5.ResumeLayout(false);
        tableLayoutPanelFiltersContainer.ResumeLayout(false);
        tableLayoutPanelKeysContainers.ResumeLayout(false);
        groupBox3.ResumeLayout(false);
        tableLayoutPanelKeys.ResumeLayout(false);
        tableLayoutPanelKeys.PerformLayout();
        groupBox2.ResumeLayout(false);
        tableLayoutPanelObjects.ResumeLayout(false);
        tableLayoutPanelObjects.PerformLayout();
        EntityPropertiesGroup.ResumeLayout(false);
        splitContainer.Panel1.ResumeLayout(false);
        splitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
        splitContainer.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private System.Windows.Forms.DataGridView EntityViewerGrid;
    private System.Windows.Forms.DataGridViewTextBoxColumn Class;
    private System.Windows.Forms.DataGridViewTextBoxColumn targetname;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanelLeft;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanelFiltersContainer;
    private System.Windows.Forms.GroupBox groupBox2;
    private System.Windows.Forms.GroupBox groupBox3;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanelKeysContainers;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanelObjects;
    private System.Windows.Forms.RadioButton ObjectsToInclude_Everything;
    private System.Windows.Forms.RadioButton ObjectsToInclude_MeshEntities;
    private System.Windows.Forms.RadioButton ObjectsToInclude_PointEntities;
    private System.Windows.Forms.TextBox ObjectsToInclude_ClassTextBox;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanelKeys;
    private System.Windows.Forms.TextBox KeyValue_Key;
    private System.Windows.Forms.TextBox KeyValue_Value;
    private System.Windows.Forms.GroupBox EntityPropertiesGroup;
    private System.Windows.Forms.GroupBox groupBox5;
    private Forms.EntityInfoControl EntityInfo;
    private System.Windows.Forms.Label label2;
    private System.Windows.Forms.CheckBox KeyValue_MatchWholeValue;
    private System.Windows.Forms.Label label1;
    private System.Windows.Forms.Label label3;
    private System.Windows.Forms.SplitContainer splitContainer;
}
