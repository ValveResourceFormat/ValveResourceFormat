namespace GUI.Controls
{
    partial class AudioPlaybackPanel
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
            CloseWaveOut();
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
            components = new System.ComponentModel.Container();
            trackBarPosition = new System.Windows.Forms.TrackBar();
            label3 = new System.Windows.Forms.Label();
            volumeSlider1 = new NAudio.Gui.VolumeSlider();
            playPauseButton = new ThemedButton();
            labelCurrentTime = new System.Windows.Forms.Label();
            labelTotalTime = new System.Windows.Forms.Label();
            label1 = new System.Windows.Forms.Label();
            waveFormPictureBox = new System.Windows.Forms.PictureBox();
            playbackTimer = new System.Windows.Forms.Timer(components);
            splitContainer1 = new System.Windows.Forms.SplitContainer();
            splitContainer2 = new System.Windows.Forms.SplitContainer();
            tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            panel1 = new System.Windows.Forms.Panel();
            tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
            panel2 = new System.Windows.Forms.Panel();
            ((System.ComponentModel.ISupportInitialize)trackBarPosition).BeginInit();
            ((System.ComponentModel.ISupportInitialize)waveFormPictureBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer2).BeginInit();
            splitContainer2.Panel1.SuspendLayout();
            splitContainer2.Panel2.SuspendLayout();
            splitContainer2.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            panel1.SuspendLayout();
            tableLayoutPanel2.SuspendLayout();
            panel2.SuspendLayout();
            SuspendLayout();
            // 
            // trackBarPosition
            // 
            trackBarPosition.Dock = System.Windows.Forms.DockStyle.Fill;
            trackBarPosition.LargeChange = 10;
            trackBarPosition.Location = new System.Drawing.Point(0, 0);
            trackBarPosition.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            trackBarPosition.Maximum = 100;
            trackBarPosition.Name = "trackBarPosition";
            trackBarPosition.Size = new System.Drawing.Size(931, 33);
            trackBarPosition.TabIndex = 16;
            trackBarPosition.Scroll += OnTrackBarPositionScroll;
            // 
            // label3
            // 
            label3.Dock = System.Windows.Forms.DockStyle.Left;
            label3.Location = new System.Drawing.Point(0, 12);
            label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(62, 19);
            label3.TabIndex = 17;
            label3.Text = "Volume:";
            label3.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            label3.UseCompatibleTextRendering = true;
            // 
            // volumeSlider1
            // 
            volumeSlider1.Dock = System.Windows.Forms.DockStyle.Fill;
            volumeSlider1.Location = new System.Drawing.Point(62, 12);
            volumeSlider1.Margin = new System.Windows.Forms.Padding(0);
            volumeSlider1.Name = "volumeSlider1";
            volumeSlider1.Size = new System.Drawing.Size(182, 19);
            volumeSlider1.TabIndex = 11;
            volumeSlider1.VolumeChanged += OnVolumeSliderChanged;
            // 
            // playPauseButton
            // 
            playPauseButton.BackColor = System.Drawing.Color.FromArgb(188, 188, 188);
            playPauseButton.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            playPauseButton.CornerRadius = 5;
            playPauseButton.Dock = System.Windows.Forms.DockStyle.Left;
            playPauseButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            playPauseButton.ForeColor = System.Drawing.Color.Black;
            playPauseButton.HoveredBackColor = System.Drawing.Color.FromArgb(140, 191, 255);
            playPauseButton.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            playPauseButton.Location = new System.Drawing.Point(0, 0);
            playPauseButton.Name = "playPauseButton";
            playPauseButton.Size = new System.Drawing.Size(64, 43);
            playPauseButton.Style = true;
            playPauseButton.TabIndex = 20;
            playPauseButton.Text = "Play";
            playPauseButton.UseVisualStyleBackColor = false;
            playPauseButton.Click += OnPlayPauseButtonClick;
            // 
            // labelCurrentTime
            // 
            labelCurrentTime.AutoSize = true;
            labelCurrentTime.Dock = System.Windows.Forms.DockStyle.Fill;
            labelCurrentTime.Font = new System.Drawing.Font("Cascadia Mono", 12F);
            labelCurrentTime.Location = new System.Drawing.Point(0, 0);
            labelCurrentTime.Margin = new System.Windows.Forms.Padding(0);
            labelCurrentTime.Name = "labelCurrentTime";
            labelCurrentTime.Size = new System.Drawing.Size(84, 43);
            labelCurrentTime.TabIndex = 21;
            labelCurrentTime.Text = "00:00.00";
            labelCurrentTime.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            labelCurrentTime.UseCompatibleTextRendering = true;
            // 
            // labelTotalTime
            // 
            labelTotalTime.AutoSize = true;
            labelTotalTime.Dock = System.Windows.Forms.DockStyle.Fill;
            labelTotalTime.Font = new System.Drawing.Font("Cascadia Mono", 12F);
            labelTotalTime.Location = new System.Drawing.Point(104, 0);
            labelTotalTime.Margin = new System.Windows.Forms.Padding(0);
            labelTotalTime.Name = "labelTotalTime";
            labelTotalTime.Size = new System.Drawing.Size(85, 43);
            labelTotalTime.TabIndex = 22;
            labelTotalTime.Text = "00:00.00";
            labelTotalTime.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            labelTotalTime.UseCompatibleTextRendering = true;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Dock = System.Windows.Forms.DockStyle.Fill;
            label1.Font = new System.Drawing.Font("Cascadia Mono", 12F);
            label1.Location = new System.Drawing.Point(87, 0);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(14, 43);
            label1.TabIndex = 23;
            label1.Text = "/";
            label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            label1.UseCompatibleTextRendering = true;
            // 
            // waveFormPictureBox
            // 
            waveFormPictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            waveFormPictureBox.Location = new System.Drawing.Point(0, 0);
            waveFormPictureBox.Name = "waveFormPictureBox";
            waveFormPictureBox.Size = new System.Drawing.Size(931, 347);
            waveFormPictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            waveFormPictureBox.TabIndex = 24;
            waveFormPictureBox.TabStop = false;
            // 
            // playbackTimer
            // 
            playbackTimer.Interval = 500;
            playbackTimer.Tick += OnTimerTick;
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer1.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            splitContainer1.Location = new System.Drawing.Point(4, 4);
            splitContainer1.Margin = new System.Windows.Forms.Padding(0);
            splitContainer1.Name = "splitContainer1";
            splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(splitContainer2);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(tableLayoutPanel1);
            splitContainer1.Size = new System.Drawing.Size(931, 431);
            splitContainer1.SplitterDistance = 384;
            splitContainer1.TabIndex = 25;
            // 
            // splitContainer2
            // 
            splitContainer2.Dock = System.Windows.Forms.DockStyle.Fill;
            splitContainer2.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            splitContainer2.IsSplitterFixed = true;
            splitContainer2.Location = new System.Drawing.Point(0, 0);
            splitContainer2.Name = "splitContainer2";
            splitContainer2.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer2.Panel1
            // 
            splitContainer2.Panel1.Controls.Add(waveFormPictureBox);
            // 
            // splitContainer2.Panel2
            // 
            splitContainer2.Panel2.Controls.Add(trackBarPosition);
            splitContainer2.Size = new System.Drawing.Size(931, 384);
            splitContainer2.SplitterDistance = 347;
            splitContainer2.TabIndex = 25;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 3;
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 253F));
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 244F));
            tableLayoutPanel1.Controls.Add(panel1, 0, 0);
            tableLayoutPanel1.Controls.Add(panel2, 2, 0);
            tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 1;
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.Size = new System.Drawing.Size(931, 43);
            tableLayoutPanel1.TabIndex = 26;
            // 
            // panel1
            // 
            panel1.Controls.Add(tableLayoutPanel2);
            panel1.Controls.Add(playPauseButton);
            panel1.Dock = System.Windows.Forms.DockStyle.Fill;
            panel1.Location = new System.Drawing.Point(0, 0);
            panel1.Margin = new System.Windows.Forms.Padding(0);
            panel1.Name = "panel1";
            panel1.Size = new System.Drawing.Size(253, 43);
            panel1.TabIndex = 0;
            // 
            // tableLayoutPanel2
            // 
            tableLayoutPanel2.ColumnCount = 3;
            tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50.0000076F));
            tableLayoutPanel2.Controls.Add(labelTotalTime, 2, 0);
            tableLayoutPanel2.Controls.Add(label1, 1, 0);
            tableLayoutPanel2.Controls.Add(labelCurrentTime, 0, 0);
            tableLayoutPanel2.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel2.Location = new System.Drawing.Point(64, 0);
            tableLayoutPanel2.Name = "tableLayoutPanel2";
            tableLayoutPanel2.RowCount = 1;
            tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel2.Size = new System.Drawing.Size(189, 43);
            tableLayoutPanel2.TabIndex = 22;
            // 
            // panel2
            // 
            panel2.Controls.Add(volumeSlider1);
            panel2.Controls.Add(label3);
            panel2.Dock = System.Windows.Forms.DockStyle.Fill;
            panel2.Location = new System.Drawing.Point(687, 0);
            panel2.Margin = new System.Windows.Forms.Padding(0);
            panel2.Name = "panel2";
            panel2.Padding = new System.Windows.Forms.Padding(0, 12, 0, 12);
            panel2.Size = new System.Drawing.Size(244, 43);
            panel2.TabIndex = 1;
            // 
            // AudioPlaybackPanel
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(splitContainer1);
            Margin = new System.Windows.Forms.Padding(0);
            Name = "AudioPlaybackPanel";
            Padding = new System.Windows.Forms.Padding(4);
            Size = new System.Drawing.Size(939, 439);
            ((System.ComponentModel.ISupportInitialize)trackBarPosition).EndInit();
            ((System.ComponentModel.ISupportInitialize)waveFormPictureBox).EndInit();
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            splitContainer2.Panel1.ResumeLayout(false);
            splitContainer2.Panel2.ResumeLayout(false);
            splitContainer2.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer2).EndInit();
            splitContainer2.ResumeLayout(false);
            tableLayoutPanel1.ResumeLayout(false);
            panel1.ResumeLayout(false);
            tableLayoutPanel2.ResumeLayout(false);
            tableLayoutPanel2.PerformLayout();
            panel2.ResumeLayout(false);
            ResumeLayout(false);

        }

        #endregion
        private NAudio.Gui.VolumeSlider volumeSlider1;
        private System.Windows.Forms.TrackBar trackBarPosition;
        private System.Windows.Forms.Label label3;
        private ThemedButton playPauseButton;
        private System.Windows.Forms.Label labelCurrentTime;
        private System.Windows.Forms.Label labelTotalTime;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.PictureBox waveFormPictureBox;
        private System.Windows.Forms.Timer playbackTimer;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.SplitContainer splitContainer2;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
        private System.Windows.Forms.Panel panel2;
    }
}
