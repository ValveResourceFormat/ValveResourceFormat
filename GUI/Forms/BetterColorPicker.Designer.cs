using System.Drawing;
using System.Windows.Forms;

namespace GUI.Forms
{
    partial class BetterColorPicker
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
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(BetterColorPicker));
            MainColorPanel = new ColorPickerPanel();
            ColorTextBoxR = new GUI.Controls.BetterAbstractNumeric<int>();
            label7 = new Label();
            ColorTextBoxG = new GUI.Controls.BetterAbstractNumeric<int>();
            label8 = new Label();
            ColorTextBoxB = new GUI.Controls.BetterAbstractNumeric<int>();
            label9 = new Label();
            HuePanel = new ColorPickerHuePanel();
            tableLayoutPanel2 = new TableLayoutPanel();
            tableLayoutPanel9 = new TableLayoutPanel();
            VSliderValueInput = new GUI.Controls.BetterAbstractNumeric<float>();
            label5 = new Label();
            VSlider = new HSVSlider();
            tableLayoutPanel8 = new TableLayoutPanel();
            SSliderValueInput = new GUI.Controls.BetterAbstractNumeric<float>();
            label4 = new Label();
            SSlider = new HSVSlider();
            tableLayoutPanel1 = new TableLayoutPanel();
            tableLayoutPanel6 = new TableLayoutPanel();
            tableLayoutPanel5 = new TableLayoutPanel();
            tableLayoutPanel4 = new TableLayoutPanel();
            tableLayoutPanel3 = new TableLayoutPanel();
            label6 = new Label();
            HexTextBox = new GUI.Controls.BetterAbstractNumeric<System.Drawing.Color>();
            tableLayoutPanel7 = new TableLayoutPanel();
            HSliderValueInput = new GUI.Controls.BetterAbstractNumeric<float>();
            label3 = new Label();
            HSlider = new HSVSlider();
            OK = new Button();
            Cancel = new Button();
            OldColorPanel = new Panel();
            NewColorPanel = new Panel();
            label2 = new Label();
            label1 = new Label();
            tableLayoutPanel11 = new TableLayoutPanel();
            tableLayoutPanel12 = new TableLayoutPanel();
            tableLayoutPanel10 = new TableLayoutPanel();
            tableLayoutPanel13 = new TableLayoutPanel();
            EyedropperButton = new Button();
            tableLayoutPanel2.SuspendLayout();
            tableLayoutPanel9.SuspendLayout();
            tableLayoutPanel8.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            tableLayoutPanel6.SuspendLayout();
            tableLayoutPanel5.SuspendLayout();
            tableLayoutPanel4.SuspendLayout();
            tableLayoutPanel3.SuspendLayout();
            tableLayoutPanel7.SuspendLayout();
            tableLayoutPanel11.SuspendLayout();
            tableLayoutPanel12.SuspendLayout();
            tableLayoutPanel10.SuspendLayout();
            tableLayoutPanel13.SuspendLayout();
            SuspendLayout();
            // 
            // MainColorPanel
            // 
            MainColorPanel.Dock = DockStyle.Fill;
            MainColorPanel.Location = new Point(0, 0);
            MainColorPanel.Margin = new Padding(0, 0, 2, 0);
            MainColorPanel.Name = "MainColorPanel";
            MainColorPanel.Size = new Size(236, 246);
            MainColorPanel.TabIndex = 0;
            // 
            // ColorTextBoxR
            // 
            ColorTextBoxR.BorderStyle = BorderStyle.FixedSingle;
            ColorTextBoxR.DecimalMax = 4;
            ColorTextBoxR.Dock = DockStyle.Fill;
            ColorTextBoxR.DragScale = 1F;
            ColorTextBoxR.Location = new Point(16, 0);
            ColorTextBoxR.Margin = new Padding(0);
            ColorTextBoxR.MaxLength = 3;
            ColorTextBoxR.MaxValue = 255;
            ColorTextBoxR.MinValue = 0;
            ColorTextBoxR.Multiline = true;
            ColorTextBoxR.Name = "ColorTextBoxR";
            ColorTextBoxR.Size = new Size(50, 19);
            ColorTextBoxR.TabIndex = 1;
            ColorTextBoxR.Text = "0";
            ColorTextBoxR.CustomTextChanged += ColorTextBoxR_TextChanged;
            ColorTextBoxR.MouseDown += ColorTextBoxR_MouseDown;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.BackColor = Color.Transparent;
            label7.Dock = DockStyle.Fill;
            label7.Location = new Point(3, 0);
            label7.Name = "label7";
            label7.Padding = new Padding(0, 2, 0, 0);
            label7.Size = new Size(10, 19);
            label7.TabIndex = 0;
            label7.Text = "R";
            label7.TextAlign = ContentAlignment.MiddleCenter;
            label7.UseCompatibleTextRendering = true;
            // 
            // ColorTextBoxG
            // 
            ColorTextBoxG.BorderStyle = BorderStyle.FixedSingle;
            ColorTextBoxG.DecimalMax = 4;
            ColorTextBoxG.Dock = DockStyle.Fill;
            ColorTextBoxG.DragScale = 1F;
            ColorTextBoxG.Location = new Point(16, 0);
            ColorTextBoxG.Margin = new Padding(0);
            ColorTextBoxG.MaxLength = 3;
            ColorTextBoxG.MaxValue = 255;
            ColorTextBoxG.MinValue = 0;
            ColorTextBoxG.Multiline = true;
            ColorTextBoxG.Name = "ColorTextBoxG";
            ColorTextBoxG.Size = new Size(50, 19);
            ColorTextBoxG.TabIndex = 1;
            ColorTextBoxG.Text = "0";
            ColorTextBoxG.CustomTextChanged += ColorTextBoxG_TextChanged;
            ColorTextBoxG.MouseDown += ColorTextBoxG_MouseDown;
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.BackColor = Color.Transparent;
            label8.Dock = DockStyle.Fill;
            label8.Location = new Point(3, 0);
            label8.Name = "label8";
            label8.Padding = new Padding(0, 2, 0, 0);
            label8.Size = new Size(10, 19);
            label8.TabIndex = 0;
            label8.Text = "G";
            label8.TextAlign = ContentAlignment.MiddleCenter;
            label8.UseCompatibleTextRendering = true;
            // 
            // ColorTextBoxB
            // 
            ColorTextBoxB.BorderStyle = BorderStyle.FixedSingle;
            ColorTextBoxB.DecimalMax = 4;
            ColorTextBoxB.Dock = DockStyle.Fill;
            ColorTextBoxB.DragScale = 1F;
            ColorTextBoxB.Location = new Point(16, 0);
            ColorTextBoxB.Margin = new Padding(0);
            ColorTextBoxB.MaxLength = 3;
            ColorTextBoxB.MaxValue = 255;
            ColorTextBoxB.MinValue = 0;
            ColorTextBoxB.Multiline = true;
            ColorTextBoxB.Name = "ColorTextBoxB";
            ColorTextBoxB.Size = new Size(50, 19);
            ColorTextBoxB.TabIndex = 1;
            ColorTextBoxB.Text = "0";
            ColorTextBoxB.CustomTextChanged += ColorTextBoxB_TextChanged;
            ColorTextBoxB.MouseDown += ColorTextBoxB_MouseDown;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.BackColor = Color.Transparent;
            label9.Dock = DockStyle.Fill;
            label9.Location = new Point(0, 0);
            label9.Margin = new Padding(0);
            label9.Name = "label9";
            label9.Padding = new Padding(0, 2, 0, 0);
            label9.Size = new Size(16, 19);
            label9.TabIndex = 0;
            label9.Text = "B";
            label9.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // HuePanel
            // 
            HuePanel.Dock = DockStyle.Fill;
            HuePanel.Location = new Point(240, 0);
            HuePanel.Margin = new Padding(2, 0, 0, 0);
            HuePanel.Name = "HuePanel";
            HuePanel.Size = new Size(26, 246);
            HuePanel.TabIndex = 0;
            // 
            // tableLayoutPanel2
            // 
            tableLayoutPanel2.ColumnCount = 1;
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayoutPanel2.Controls.Add(tableLayoutPanel9, 0, 3);
            tableLayoutPanel2.Controls.Add(tableLayoutPanel8, 0, 2);
            tableLayoutPanel2.Controls.Add(tableLayoutPanel1, 0, 0);
            tableLayoutPanel2.Controls.Add(tableLayoutPanel7, 0, 1);
            tableLayoutPanel2.Dock = DockStyle.Fill;
            tableLayoutPanel2.Location = new Point(0, 272);
            tableLayoutPanel2.Margin = new Padding(0);
            tableLayoutPanel2.Name = "tableLayoutPanel2";
            tableLayoutPanel2.RowCount = 4;
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableLayoutPanel2.Size = new Size(266, 124);
            tableLayoutPanel2.TabIndex = 2;
            // 
            // tableLayoutPanel9
            // 
            tableLayoutPanel9.ColumnCount = 3;
            tableLayoutPanel9.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 6F));
            tableLayoutPanel9.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80F));
            tableLayoutPanel9.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14F));
            tableLayoutPanel9.Controls.Add(VSliderValueInput, 2, 0);
            tableLayoutPanel9.Controls.Add(label5, 0, 0);
            tableLayoutPanel9.Controls.Add(VSlider, 1, 0);
            tableLayoutPanel9.Dock = DockStyle.Fill;
            tableLayoutPanel9.Location = new Point(0, 99);
            tableLayoutPanel9.Margin = new Padding(0, 6, 0, 6);
            tableLayoutPanel9.Name = "tableLayoutPanel9";
            tableLayoutPanel9.RowCount = 1;
            tableLayoutPanel9.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel9.Size = new Size(266, 19);
            tableLayoutPanel9.TabIndex = 6;
            // 
            // VSliderValueInput
            // 
            VSliderValueInput.BorderStyle = BorderStyle.FixedSingle;
            VSliderValueInput.DecimalMax = 2;
            VSliderValueInput.Dock = DockStyle.Fill;
            VSliderValueInput.DragScale = 1F;
            VSliderValueInput.Location = new Point(231, 0);
            VSliderValueInput.Margin = new Padding(4, 0, 0, 0);
            VSliderValueInput.MaxValue = 1F;
            VSliderValueInput.MinValue = 0F;
            VSliderValueInput.Multiline = true;
            VSliderValueInput.Name = "VSliderValueInput";
            VSliderValueInput.Size = new Size(35, 19);
            VSliderValueInput.TabIndex = 2;
            VSliderValueInput.Text = "0";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Dock = DockStyle.Fill;
            label5.Location = new Point(0, 0);
            label5.Margin = new Padding(0);
            label5.Name = "label5";
            label5.Size = new Size(15, 19);
            label5.TabIndex = 0;
            label5.Text = "V";
            label5.TextAlign = ContentAlignment.MiddleCenter;
            label5.UseCompatibleTextRendering = true;
            // 
            // VSlider
            // 
            VSlider.BackColor = SystemColors.Control;
            VSlider.Dock = DockStyle.Fill;
            VSlider.Location = new Point(15, 0);
            VSlider.Margin = new Padding(0);
            VSlider.Name = "VSlider";
            VSlider.Size = new Size(212, 19);
            VSlider.TabIndex = 3;
            // 
            // tableLayoutPanel8
            // 
            tableLayoutPanel8.ColumnCount = 3;
            tableLayoutPanel8.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 6F));
            tableLayoutPanel8.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80F));
            tableLayoutPanel8.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14F));
            tableLayoutPanel8.Controls.Add(SSliderValueInput, 2, 0);
            tableLayoutPanel8.Controls.Add(label4, 0, 0);
            tableLayoutPanel8.Controls.Add(SSlider, 1, 0);
            tableLayoutPanel8.Dock = DockStyle.Fill;
            tableLayoutPanel8.Location = new Point(0, 68);
            tableLayoutPanel8.Margin = new Padding(0, 6, 0, 6);
            tableLayoutPanel8.Name = "tableLayoutPanel8";
            tableLayoutPanel8.RowCount = 1;
            tableLayoutPanel8.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel8.Size = new Size(266, 19);
            tableLayoutPanel8.TabIndex = 5;
            // 
            // SSliderValueInput
            // 
            SSliderValueInput.BorderStyle = BorderStyle.FixedSingle;
            SSliderValueInput.DecimalMax = 2;
            SSliderValueInput.Dock = DockStyle.Fill;
            SSliderValueInput.DragScale = 1F;
            SSliderValueInput.Location = new Point(231, 0);
            SSliderValueInput.Margin = new Padding(4, 0, 0, 0);
            SSliderValueInput.MaxValue = 1F;
            SSliderValueInput.MinValue = 0F;
            SSliderValueInput.Multiline = true;
            SSliderValueInput.Name = "SSliderValueInput";
            SSliderValueInput.Size = new Size(35, 19);
            SSliderValueInput.TabIndex = 3;
            SSliderValueInput.Text = "0";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Dock = DockStyle.Fill;
            label4.Location = new Point(0, 0);
            label4.Margin = new Padding(0);
            label4.Name = "label4";
            label4.Size = new Size(15, 19);
            label4.TabIndex = 0;
            label4.Text = "S";
            label4.TextAlign = ContentAlignment.MiddleCenter;
            label4.UseCompatibleTextRendering = true;
            // 
            // SSlider
            // 
            SSlider.Dock = DockStyle.Fill;
            SSlider.Location = new Point(15, 0);
            SSlider.Margin = new Padding(0);
            SSlider.Name = "SSlider";
            SSlider.Size = new Size(212, 19);
            SSlider.TabIndex = 4;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 4;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutPanel1.Controls.Add(tableLayoutPanel6, 2, 0);
            tableLayoutPanel1.Controls.Add(tableLayoutPanel5, 1, 0);
            tableLayoutPanel1.Controls.Add(tableLayoutPanel4, 0, 0);
            tableLayoutPanel1.Controls.Add(tableLayoutPanel3, 3, 0);
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Location = new Point(0, 6);
            tableLayoutPanel1.Margin = new Padding(0, 6, 0, 6);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 1;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel1.Size = new Size(266, 19);
            tableLayoutPanel1.TabIndex = 3;
            // 
            // tableLayoutPanel6
            // 
            tableLayoutPanel6.ColumnCount = 2;
            tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75F));
            tableLayoutPanel6.Controls.Add(ColorTextBoxB, 1, 0);
            tableLayoutPanel6.Controls.Add(label9, 0, 0);
            tableLayoutPanel6.Dock = DockStyle.Fill;
            tableLayoutPanel6.Location = new Point(132, 0);
            tableLayoutPanel6.Margin = new Padding(0);
            tableLayoutPanel6.Name = "tableLayoutPanel6";
            tableLayoutPanel6.RowCount = 1;
            tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tableLayoutPanel6.Size = new Size(66, 19);
            tableLayoutPanel6.TabIndex = 2;
            // 
            // tableLayoutPanel5
            // 
            tableLayoutPanel5.ColumnCount = 2;
            tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75F));
            tableLayoutPanel5.Controls.Add(label8, 0, 0);
            tableLayoutPanel5.Controls.Add(ColorTextBoxG, 1, 0);
            tableLayoutPanel5.Dock = DockStyle.Fill;
            tableLayoutPanel5.Location = new Point(66, 0);
            tableLayoutPanel5.Margin = new Padding(0);
            tableLayoutPanel5.Name = "tableLayoutPanel5";
            tableLayoutPanel5.RowCount = 1;
            tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel5.Size = new Size(66, 19);
            tableLayoutPanel5.TabIndex = 2;
            // 
            // tableLayoutPanel4
            // 
            tableLayoutPanel4.ColumnCount = 2;
            tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75F));
            tableLayoutPanel4.Controls.Add(label7, 0, 0);
            tableLayoutPanel4.Controls.Add(ColorTextBoxR, 1, 0);
            tableLayoutPanel4.Dock = DockStyle.Fill;
            tableLayoutPanel4.Location = new Point(0, 0);
            tableLayoutPanel4.Margin = new Padding(0);
            tableLayoutPanel4.Name = "tableLayoutPanel4";
            tableLayoutPanel4.RowCount = 1;
            tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tableLayoutPanel4.Size = new Size(66, 19);
            tableLayoutPanel4.TabIndex = 2;
            // 
            // tableLayoutPanel3
            // 
            tableLayoutPanel3.ColumnCount = 2;
            tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75F));
            tableLayoutPanel3.Controls.Add(label6, 0, 0);
            tableLayoutPanel3.Controls.Add(HexTextBox, 1, 0);
            tableLayoutPanel3.Dock = DockStyle.Fill;
            tableLayoutPanel3.Location = new Point(198, 0);
            tableLayoutPanel3.Margin = new Padding(0);
            tableLayoutPanel3.Name = "tableLayoutPanel3";
            tableLayoutPanel3.RowCount = 1;
            tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel3.Size = new Size(68, 19);
            tableLayoutPanel3.TabIndex = 4;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.BackColor = Color.Transparent;
            label6.Dock = DockStyle.Fill;
            label6.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label6.Location = new Point(3, 0);
            label6.Name = "label6";
            label6.Padding = new Padding(0, 2, 0, 0);
            label6.Size = new Size(11, 19);
            label6.TabIndex = 3;
            label6.Text = "#";
            label6.TextAlign = ContentAlignment.MiddleCenter;
            label6.UseCompatibleTextRendering = true;
            // 
            // HexTextBox
            // 
            HexTextBox.BorderStyle = BorderStyle.FixedSingle;
            HexTextBox.DecimalMax = 4;
            HexTextBox.Dock = DockStyle.Fill;
            HexTextBox.DragScale = 1F;
            HexTextBox.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            HexTextBox.Location = new Point(17, 0);
            HexTextBox.Margin = new Padding(0);
            HexTextBox.MaxValue = Color.Empty;
            HexTextBox.MinValue = Color.Empty;
            HexTextBox.Multiline = true;
            HexTextBox.Name = "HexTextBox";
            HexTextBox.Size = new Size(51, 19);
            HexTextBox.TabIndex = 0;
            HexTextBox.Text = "FFFFFF";
            HexTextBox.CustomTextChanged += HexTextBox_TextChanged;
            // 
            // tableLayoutPanel7
            // 
            tableLayoutPanel7.ColumnCount = 3;
            tableLayoutPanel7.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 6F));
            tableLayoutPanel7.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80F));
            tableLayoutPanel7.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14F));
            tableLayoutPanel7.Controls.Add(HSliderValueInput, 2, 0);
            tableLayoutPanel7.Controls.Add(label3, 0, 0);
            tableLayoutPanel7.Controls.Add(HSlider, 1, 0);
            tableLayoutPanel7.Dock = DockStyle.Fill;
            tableLayoutPanel7.Location = new Point(0, 37);
            tableLayoutPanel7.Margin = new Padding(0, 6, 0, 6);
            tableLayoutPanel7.Name = "tableLayoutPanel7";
            tableLayoutPanel7.RowCount = 1;
            tableLayoutPanel7.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel7.Size = new Size(266, 19);
            tableLayoutPanel7.TabIndex = 4;
            // 
            // HSliderValueInput
            // 
            HSliderValueInput.BorderStyle = BorderStyle.FixedSingle;
            HSliderValueInput.DecimalMax = 2;
            HSliderValueInput.Dock = DockStyle.Fill;
            HSliderValueInput.DragScale = 1F;
            HSliderValueInput.Location = new Point(231, 0);
            HSliderValueInput.Margin = new Padding(4, 0, 0, 0);
            HSliderValueInput.MaxValue = 1F;
            HSliderValueInput.MinValue = 0F;
            HSliderValueInput.Multiline = true;
            HSliderValueInput.Name = "HSliderValueInput";
            HSliderValueInput.Size = new Size(35, 19);
            HSliderValueInput.TabIndex = 4;
            HSliderValueInput.Text = "0";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Dock = DockStyle.Fill;
            label3.Location = new Point(0, 0);
            label3.Margin = new Padding(0);
            label3.Name = "label3";
            label3.Size = new Size(15, 19);
            label3.TabIndex = 0;
            label3.Text = "H";
            label3.TextAlign = ContentAlignment.MiddleCenter;
            label3.UseCompatibleTextRendering = true;
            // 
            // HSlider
            // 
            HSlider.Dock = DockStyle.Fill;
            HSlider.Location = new Point(15, 0);
            HSlider.Margin = new Padding(0);
            HSlider.Name = "HSlider";
            HSlider.Size = new Size(212, 19);
            HSlider.TabIndex = 5;
            // 
            // OK
            // 
            OK.AutoSize = true;
            OK.BackColor = SystemColors.Control;
            OK.Dock = DockStyle.Fill;
            OK.ForeColor = SystemColors.ControlText;
            OK.Location = new Point(131, 0);
            OK.Margin = new Padding(2, 0, 0, 0);
            OK.Name = "OK";
            OK.Size = new Size(127, 37);
            OK.TabIndex = 1;
            OK.Text = "OK";
            OK.UseVisualStyleBackColor = false;
            OK.Click += OK_Click;
            // 
            // Cancel
            // 
            Cancel.AutoSize = true;
            Cancel.BackColor = SystemColors.Control;
            Cancel.Dock = DockStyle.Fill;
            Cancel.Location = new Point(0, 0);
            Cancel.Margin = new Padding(0, 0, 2, 0);
            Cancel.Name = "Cancel";
            Cancel.Size = new Size(127, 37);
            Cancel.TabIndex = 0;
            Cancel.Text = "Cancel";
            Cancel.UseVisualStyleBackColor = false;
            // 
            // OldColorPanel
            // 
            OldColorPanel.BackColor = SystemColors.ActiveCaption;
            OldColorPanel.Dock = DockStyle.Fill;
            OldColorPanel.Location = new Point(39, 0);
            OldColorPanel.Margin = new Padding(0);
            OldColorPanel.Name = "OldColorPanel";
            OldColorPanel.Size = new Size(79, 22);
            OldColorPanel.TabIndex = 3;
            // 
            // NewColorPanel
            // 
            NewColorPanel.BackColor = Color.IndianRed;
            NewColorPanel.Dock = DockStyle.Fill;
            NewColorPanel.Location = new Point(118, 0);
            NewColorPanel.Margin = new Padding(0);
            NewColorPanel.Name = "NewColorPanel";
            NewColorPanel.Size = new Size(79, 22);
            NewColorPanel.TabIndex = 2;
            // 
            // label2
            // 
            label2.BackColor = Color.Transparent;
            label2.Dock = DockStyle.Fill;
            label2.Location = new Point(197, 0);
            label2.Margin = new Padding(0);
            label2.Name = "label2";
            label2.Size = new Size(39, 22);
            label2.TabIndex = 1;
            label2.Text = "New";
            label2.TextAlign = ContentAlignment.MiddleCenter;
            label2.UseCompatibleTextRendering = true;
            // 
            // label1
            // 
            label1.BackColor = Color.Transparent;
            label1.Dock = DockStyle.Fill;
            label1.Location = new Point(0, 0);
            label1.Margin = new Padding(0);
            label1.Name = "label1";
            label1.Size = new Size(39, 22);
            label1.TabIndex = 0;
            label1.Text = "Old";
            label1.TextAlign = ContentAlignment.MiddleCenter;
            label1.UseCompatibleTextRendering = true;
            // 
            // tableLayoutPanel11
            // 
            tableLayoutPanel11.ColumnCount = 1;
            tableLayoutPanel11.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayoutPanel11.Controls.Add(tableLayoutPanel12, 0, 3);
            tableLayoutPanel11.Controls.Add(tableLayoutPanel2, 0, 2);
            tableLayoutPanel11.Controls.Add(tableLayoutPanel10, 0, 1);
            tableLayoutPanel11.Controls.Add(tableLayoutPanel13, 0, 0);
            tableLayoutPanel11.Dock = DockStyle.Fill;
            tableLayoutPanel11.Location = new Point(4, 4);
            tableLayoutPanel11.Margin = new Padding(0);
            tableLayoutPanel11.Name = "tableLayoutPanel11";
            tableLayoutPanel11.RowCount = 4;
            tableLayoutPanel11.RowStyles.Add(new RowStyle(SizeType.Percent, 5.89569139F));
            tableLayoutPanel11.RowStyles.Add(new RowStyle(SizeType.Percent, 55.7823143F));
            tableLayoutPanel11.RowStyles.Add(new RowStyle(SizeType.Percent, 28.1179142F));
            tableLayoutPanel11.RowStyles.Add(new RowStyle(SizeType.Percent, 10.2040815F));
            tableLayoutPanel11.Size = new Size(266, 441);
            tableLayoutPanel11.TabIndex = 0;
            // 
            // tableLayoutPanel12
            // 
            tableLayoutPanel12.ColumnCount = 2;
            tableLayoutPanel12.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel12.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel12.Controls.Add(OK, 1, 0);
            tableLayoutPanel12.Controls.Add(Cancel, 0, 0);
            tableLayoutPanel12.Dock = DockStyle.Fill;
            tableLayoutPanel12.Location = new Point(4, 400);
            tableLayoutPanel12.Margin = new Padding(4);
            tableLayoutPanel12.Name = "tableLayoutPanel12";
            tableLayoutPanel12.RowCount = 1;
            tableLayoutPanel12.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel12.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel12.Size = new Size(258, 37);
            tableLayoutPanel12.TabIndex = 1;
            // 
            // tableLayoutPanel10
            // 
            tableLayoutPanel10.ColumnCount = 2;
            tableLayoutPanel10.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 89.70588F));
            tableLayoutPanel10.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10.2941179F));
            tableLayoutPanel10.Controls.Add(HuePanel, 1, 0);
            tableLayoutPanel10.Controls.Add(MainColorPanel, 0, 0);
            tableLayoutPanel10.Dock = DockStyle.Fill;
            tableLayoutPanel10.Location = new Point(0, 26);
            tableLayoutPanel10.Margin = new Padding(0);
            tableLayoutPanel10.Name = "tableLayoutPanel10";
            tableLayoutPanel10.RowCount = 1;
            tableLayoutPanel10.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel10.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel10.Size = new Size(266, 246);
            tableLayoutPanel10.TabIndex = 0;
            // 
            // tableLayoutPanel13
            // 
            tableLayoutPanel13.ColumnCount = 5;
            tableLayoutPanel13.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15F));
            tableLayoutPanel13.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            tableLayoutPanel13.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            tableLayoutPanel13.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15F));
            tableLayoutPanel13.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10F));
            tableLayoutPanel13.Controls.Add(EyedropperButton, 4, 0);
            tableLayoutPanel13.Controls.Add(label1, 0, 0);
            tableLayoutPanel13.Controls.Add(NewColorPanel, 2, 0);
            tableLayoutPanel13.Controls.Add(OldColorPanel, 1, 0);
            tableLayoutPanel13.Controls.Add(label2, 3, 0);
            tableLayoutPanel13.Dock = DockStyle.Fill;
            tableLayoutPanel13.Location = new Point(0, 0);
            tableLayoutPanel13.Margin = new Padding(0);
            tableLayoutPanel13.Name = "tableLayoutPanel13";
            tableLayoutPanel13.Padding = new Padding(0, 0, 0, 4);
            tableLayoutPanel13.RowCount = 1;
            tableLayoutPanel13.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel13.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tableLayoutPanel13.Size = new Size(266, 26);
            tableLayoutPanel13.TabIndex = 3;
            // 
            // EyedropperButton
            // 
            EyedropperButton.AutoSize = true;
            EyedropperButton.BackgroundImage = (Image)resources.GetObject("EyedropperButton.BackgroundImage");
            EyedropperButton.BackgroundImageLayout = ImageLayout.Center;
            EyedropperButton.Cursor = Cursors.Hand;
            EyedropperButton.Dock = DockStyle.Fill;
            EyedropperButton.Font = new Font("Segoe UI", 9F);
            EyedropperButton.Location = new Point(236, 0);
            EyedropperButton.Margin = new Padding(0);
            EyedropperButton.Name = "EyedropperButton";
            EyedropperButton.Size = new Size(30, 22);
            EyedropperButton.TabIndex = 4;
            EyedropperButton.TabStop = false;
            EyedropperButton.UseVisualStyleBackColor = false;
            EyedropperButton.MouseUp += EyedropperButton_MouseUp;
            // 
            // BetterColorPicker
            // 
            AcceptButton = OK;
            BackColor = SystemColors.Control;
            CancelButton = Cancel;
            ClientSize = new Size(274, 449);
            Controls.Add(tableLayoutPanel11);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "BetterColorPicker";
            Padding = new Padding(4);
            SizeGripStyle = SizeGripStyle.Hide;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Colour Picker";
            TopMost = true;
            Load += BetterColorPicker_Load;
            tableLayoutPanel2.ResumeLayout(false);
            tableLayoutPanel9.ResumeLayout(false);
            tableLayoutPanel9.PerformLayout();
            tableLayoutPanel8.ResumeLayout(false);
            tableLayoutPanel8.PerformLayout();
            tableLayoutPanel1.ResumeLayout(false);
            tableLayoutPanel6.ResumeLayout(false);
            tableLayoutPanel6.PerformLayout();
            tableLayoutPanel5.ResumeLayout(false);
            tableLayoutPanel5.PerformLayout();
            tableLayoutPanel4.ResumeLayout(false);
            tableLayoutPanel4.PerformLayout();
            tableLayoutPanel3.ResumeLayout(false);
            tableLayoutPanel3.PerformLayout();
            tableLayoutPanel7.ResumeLayout(false);
            tableLayoutPanel7.PerformLayout();
            tableLayoutPanel11.ResumeLayout(false);
            tableLayoutPanel12.ResumeLayout(false);
            tableLayoutPanel12.PerformLayout();
            tableLayoutPanel10.ResumeLayout(false);
            tableLayoutPanel13.ResumeLayout(false);
            tableLayoutPanel13.PerformLayout();
            ResumeLayout(false);
        }

        private Color RSliderLastColor = Color.White;
        private Color GSliderLastColor = Color.White;
        private TableLayoutPanel tableLayoutPanel1;
        private TableLayoutPanel tableLayoutPanel2;
        private TableLayoutPanel tableLayoutPanel3;
        private TableLayoutPanel tableLayoutPanel4;
        private TableLayoutPanel tableLayoutPanel6;
        private TableLayoutPanel tableLayoutPanel5;
        private TableLayoutPanel tableLayoutPanel7;
        public Label label3;
        private TableLayoutPanel tableLayoutPanel9;
        public Label label5;
        private TableLayoutPanel tableLayoutPanel8;
        public Label label4;
        private HSVSlider VSlider;
        private HSVSlider SSlider;
        private HSVSlider HSlider;
        private GUI.Controls.BetterAbstractNumeric<float> VSliderValueInput;
        private GUI.Controls.BetterAbstractNumeric<float> SSliderValueInput;
        private GUI.Controls.BetterAbstractNumeric<float> HSliderValueInput;
        private TableLayoutPanel tableLayoutPanel10;
        private TableLayoutPanel tableLayoutPanel11;
        private TableLayoutPanel tableLayoutPanel12;
        private TableLayoutPanel tableLayoutPanel13;
        private Button EyedropperButton;
        private Color BSliderLastColor = Color.White;
        private Label label6;
        private Label label7;
        private Label label8;
        private Label label9;
        private GUI.Controls.BetterAbstractNumeric<Color> HexTextBox;
        private GUI.Controls.BetterAbstractNumeric<int> ColorTextBoxG;
        private GUI.Controls.BetterAbstractNumeric<int> ColorTextBoxB;
        private GUI.Controls.BetterAbstractNumeric<int> ColorTextBoxR;
        private ColorPickerPanel MainColorPanel;
        private ColorPickerHuePanel HuePanel;
        private Button OK;
        private Button Cancel;
        private Label label1;
        private Label label2;
        private Panel NewColorPanel;
        private Panel OldColorPanel;

        #endregion
    }
}
