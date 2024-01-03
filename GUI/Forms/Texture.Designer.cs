using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using ValveResourceFormat.CompiledShader;

namespace GUI.Forms
{
    partial class Texture
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
            if (disposing)
            {
                cts?.Cancel();
                cts?.Dispose();
                if (channelChangingTask?.Status == TaskStatus.Running)
                {
                    channelChangingTask.ContinueWith(t =>
                    {
                        t.Dispose();
                        skBitmap?.Dispose();
                    });
                }
                else
                {
                    skBitmap?.Dispose();
                }

                hardwareDecoder?.Dispose();
                components?.Dispose();
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Texture));
            this.pictureBox1 = new ProPictureBox();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.saveAsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.hardwareDecodeCheckBox = new System.Windows.Forms.ToolStripMenuItem();
            this.viewChannelsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.viewChannelsRed = new System.Windows.Forms.ToolStripMenuItem();
            this.viewChannelsGreen = new System.Windows.Forms.ToolStripMenuItem();
            this.viewChannelsBlue = new System.Windows.Forms.ToolStripMenuItem();
            this.viewChannelsAlpha = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.viewChannelsOpaque = new System.Windows.Forms.ToolStripMenuItem();
            this.viewChannelsTransparent = new System.Windows.Forms.ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.contextMenuStrip1.SuspendLayout();
            this.SuspendLayout();
            //
            // pictureBox1
            //
            this.pictureBox1.ContextMenuStrip = this.contextMenuStrip1;
            this.pictureBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pictureBox1.Location = new System.Drawing.Point(0, 0);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(300, 150);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            this.pictureBox1.BackgroundImageLayout = ImageLayout.Tile;
            this.pictureBox1.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("checkered.Image")));
            //
            // contextMenuStrip1
            //
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.saveAsToolStripMenuItem,
            this.toolStripSeparator1,
            this.hardwareDecodeCheckBox,
            this.viewChannelsToolStripMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(122, 26);
            this.contextMenuStrip1.ItemClicked += new System.Windows.Forms.ToolStripItemClickedEventHandler(this.ContextMenuStrip1_ItemClicked);
            //
            // saveAsToolStripMenuItem
            //
            this.saveAsToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("saveAsToolStripMenuItem.Image")));
            this.saveAsToolStripMenuItem.Name = "saveAsToolStripMenuItem";
            this.saveAsToolStripMenuItem.Size = new System.Drawing.Size(121, 22);
            this.saveAsToolStripMenuItem.Text = "Save as…";
            //
            // toolStripSeparator1
            //
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(177, 6);
            //
            // hardwareDecodeCheckBox
            //
            this.hardwareDecodeCheckBox.Name = "hardwareDecodeCheckBox";
            this.hardwareDecodeCheckBox.Size = new System.Drawing.Size(180, 22);
            this.hardwareDecodeCheckBox.Text = "Hardware decode";
            this.hardwareDecodeCheckBox.Checked = false;
            this.hardwareDecodeCheckBox.CheckState = System.Windows.Forms.CheckState.Unchecked;
            this.hardwareDecodeCheckBox.Click += new System.EventHandler(this.HardwareDecodeCheckBox_Click);
            //
            // viewChannelsToolStripMenuItem
            //
            this.viewChannelsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.viewChannelsRed,
            this.viewChannelsGreen,
            this.viewChannelsBlue,
            this.viewChannelsAlpha,
            this.toolStripSeparator2,
            this.viewChannelsOpaque,
            this.viewChannelsTransparent});
            this.viewChannelsToolStripMenuItem.Name = "viewChannelsToolStripMenuItem";
            this.viewChannelsToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.viewChannelsToolStripMenuItem.Text = "View channels";
            //
            // viewChannelsRed
            //
            this.viewChannelsRed.Name = "viewChannelsRed";
            this.viewChannelsRed.Size = new System.Drawing.Size(180, 22);
            this.viewChannelsRed.Text = "Red";
            this.viewChannelsRed.Tag = ChannelMapping.R;
            this.viewChannelsRed.Click += new System.EventHandler(this.OnChannelMenuItem_Click);
            //
            // viewChannelsGreen
            //
            this.viewChannelsGreen.Name = "viewChannelsGreen";
            this.viewChannelsGreen.Size = new System.Drawing.Size(180, 22);
            this.viewChannelsGreen.Text = "Green";
            this.viewChannelsGreen.Tag = ChannelMapping.G;
            this.viewChannelsGreen.Click += new System.EventHandler(this.OnChannelMenuItem_Click);
            //
            // viewChannelsBlue
            //
            this.viewChannelsBlue.Name = "viewChannelsBlue";
            this.viewChannelsBlue.Size = new System.Drawing.Size(180, 22);
            this.viewChannelsBlue.Text = "Blue";
            this.viewChannelsBlue.Tag = ChannelMapping.B;
            this.viewChannelsBlue.Click += new System.EventHandler(this.OnChannelMenuItem_Click);
            //
            // viewChannelsAlpha
            //
            this.viewChannelsAlpha.Name = "viewChannelsAlpha";
            this.viewChannelsAlpha.Size = new System.Drawing.Size(180, 22);
            this.viewChannelsAlpha.Text = "Alpha";
            this.viewChannelsAlpha.Tag = ChannelMapping.A;
            this.viewChannelsAlpha.Click += new System.EventHandler(this.OnChannelMenuItem_Click);
            //
            // toolStripSeparator2
            //
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(177, 6);
            //
            // viewChannelsOpaque
            //
            this.viewChannelsOpaque.Name = "viewChannelsOpaque";
            this.viewChannelsOpaque.Size = new System.Drawing.Size(180, 22);
            this.viewChannelsOpaque.Text = "Opaque";
            this.viewChannelsOpaque.Tag = ChannelMapping.RGB;
            this.viewChannelsOpaque.Click += new System.EventHandler(this.OnChannelMenuItem_Click);
            //
            // viewChannelsTransparent
            //
            this.viewChannelsTransparent.Checked = true;
            this.viewChannelsTransparent.CheckState = System.Windows.Forms.CheckState.Checked;
            this.viewChannelsTransparent.Name = "viewChannelsTransparent";
            this.viewChannelsTransparent.Size = new System.Drawing.Size(180, 22);
            this.viewChannelsTransparent.Text = "Transparent";
            this.viewChannelsTransparent.Tag = ChannelMapping.RGBA;
            this.viewChannelsTransparent.Click += new System.EventHandler(this.OnChannelMenuItem_Click);
            //
            // Texture
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Controls.Add(this.pictureBox1);
            this.Name = "Texture";
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.contextMenuStrip1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private ProPictureBox pictureBox1;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem saveAsToolStripMenuItem;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripMenuItem hardwareDecodeCheckBox;
        private ToolStripMenuItem viewChannelsToolStripMenuItem;
        private ToolStripMenuItem viewChannelsRed;
        private ToolStripMenuItem viewChannelsGreen;
        private ToolStripMenuItem viewChannelsBlue;
        private ToolStripMenuItem viewChannelsAlpha;
        private ToolStripSeparator toolStripSeparator2;
        private ToolStripMenuItem viewChannelsOpaque;
        private ToolStripMenuItem viewChannelsTransparent;
    }
}
