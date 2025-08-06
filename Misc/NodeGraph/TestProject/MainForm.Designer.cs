using System;
using System.Collections.Generic;
using NodeGraphControl;
using NodeGraphControl.Elements;

namespace TestProject {
    partial class MainForm {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null)) {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            this.nodeGraphControl = new NodeGraphControl.NodeGraphControl();
            this.propertyGrid = new System.Windows.Forms.PropertyGrid();
            this.statusBar = new System.Windows.Forms.StatusBar();
            this.statusBarPanelZoom = new System.Windows.Forms.StatusBarPanel();
            ((System.ComponentModel.ISupportInitialize) (this.statusBarPanelZoom)).BeginInit();
            this.SuspendLayout();
            // 
            // nodeGraphControl
            // 
            this.nodeGraphControl.Anchor = ((System.Windows.Forms.AnchorStyles) ((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.nodeGraphControl.BackColor = System.Drawing.Color.FromArgb(((int) (((byte) (23)))), ((int) (((byte) (25)))), ((int) (((byte) (31)))));
            this.nodeGraphControl.GridColor = System.Drawing.Color.FromArgb(((int) (((byte) (31)))), ((int) (((byte) (36)))), ((int) (((byte) (42)))));
            this.nodeGraphControl.GridStep = 32;
            this.nodeGraphControl.GridStyle = NodeGraphControl.NodeGraphControl.EGridStyle.Grid;
            this.nodeGraphControl.Location = new System.Drawing.Point(0, 0);
            this.nodeGraphControl.Name = "nodeGraphControl";
            this.nodeGraphControl.Size = new System.Drawing.Size(662, 701);
            this.nodeGraphControl.TabIndex = 1;
            this.nodeGraphControl.Text = "nodeGraphControl";
            this.nodeGraphControl.WireMiddlePointsSpread = 0;
            this.nodeGraphControl.WireStyle = NodeGraphControl.NodeGraphControl.EWireStyle.Bezier;
            this.nodeGraphControl.SelectionChanged += new System.EventHandler<System.Collections.Generic.List<NodeGraphControl.Elements.AbstractNode>>(this.NodeGraph_SelectionChanged);
            this.nodeGraphControl.ZoomChanged += new System.EventHandler<float>(this.nodeGraphControl_ZoomChanged);
            this.nodeGraphControl.Paint += new System.Windows.Forms.PaintEventHandler(this.nodeGraphControl_Paint);
            // 
            // propertyGrid
            // 
            this.propertyGrid.AllowDrop = true;
            this.propertyGrid.Anchor = ((System.Windows.Forms.AnchorStyles) (((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Right)));
            this.propertyGrid.Location = new System.Drawing.Point(668, 0);
            this.propertyGrid.Name = "propertyGrid";
            this.propertyGrid.Size = new System.Drawing.Size(340, 701);
            this.propertyGrid.TabIndex = 2;
            this.propertyGrid.PropertyValueChanged += new System.Windows.Forms.PropertyValueChangedEventHandler(this.propertyGrid_PropertyValueChanged);
            // 
            // statusBar
            // 
            this.statusBar.Location = new System.Drawing.Point(0, 707);
            this.statusBar.Name = "statusBar";
            this.statusBar.Panels.AddRange(new System.Windows.Forms.StatusBarPanel[] {this.statusBarPanelZoom});
            this.statusBar.ShowPanels = true;
            this.statusBar.Size = new System.Drawing.Size(1008, 22);
            this.statusBar.TabIndex = 3;
            this.statusBar.Text = "statusBar";
            // 
            // statusBarPanelZoom
            // 
            this.statusBarPanelZoom.Name = "statusBarPanelZoom";
            this.statusBarPanelZoom.Text = "statusBarPanelZoom";
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1008, 729);
            this.ControlBox = false;
            this.Controls.Add(this.statusBar);
            this.Controls.Add(this.propertyGrid);
            this.Controls.Add(this.nodeGraphControl);
            this.DoubleBuffered = true;
            this.Name = "MainForm";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.Text = "NodeGraphControl Sample App";
            this.Load += new System.EventHandler(this.MainForm_Load);
            ((System.ComponentModel.ISupportInitialize) (this.statusBarPanelZoom)).EndInit();
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.StatusBarPanel statusBarPanelZoom;

        private System.Windows.Forms.StatusBar statusBar;

        private System.Windows.Forms.PropertyGrid propertyGrid;

        #endregion

        private NodeGraphControl.NodeGraphControl nodeGraphControl;
    }
}