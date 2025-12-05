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
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(AudioPlaybackPanel));
            toolStrip1 = new System.Windows.Forms.ToolStrip();
            buttonPlay = new System.Windows.Forms.ToolStripButton();
            buttonPause = new System.Windows.Forms.ToolStripButton();
            toolStripLabel1 = new System.Windows.Forms.ToolStripLabel();
            labelCurrentTime = new System.Windows.Forms.ToolStripLabel();
            toolStripLabel3 = new System.Windows.Forms.ToolStripLabel();
            labelTotalTime = new System.Windows.Forms.ToolStripLabel();
            trackBarPosition = new System.Windows.Forms.TrackBar();
            playbackTimer = new System.Windows.Forms.Timer(components);
            label3 = new System.Windows.Forms.Label();
            waveformPainter2 = new NAudio.Gui.WaveformPainter();
            waveformPainter1 = new NAudio.Gui.WaveformPainter();
            volumeMeter2 = new NAudio.Gui.VolumeMeter();
            volumeMeter1 = new NAudio.Gui.VolumeMeter();
            volumeSlider1 = new NAudio.Gui.VolumeSlider();
            toolStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)trackBarPosition).BeginInit();
            SuspendLayout();
            // 
            // toolStrip1
            // 
            toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { buttonPlay, buttonPause, toolStripLabel1, labelCurrentTime, toolStripLabel3, labelTotalTime });
            toolStrip1.Location = new System.Drawing.Point(0, 0);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new System.Drawing.Size(626, 25);
            toolStrip1.TabIndex = 15;
            toolStrip1.Text = "toolStrip1";
            // 
            // buttonPlay
            // 
            buttonPlay.Image = (System.Drawing.Image)resources.GetObject("buttonPlay.Image");
            buttonPlay.ImageTransparentColor = System.Drawing.Color.Magenta;
            buttonPlay.Name = "buttonPlay";
            buttonPlay.Size = new System.Drawing.Size(49, 22);
            buttonPlay.Text = "Play";
            buttonPlay.Click += OnButtonPlayClick;
            // 
            // buttonPause
            // 
            buttonPause.Image = (System.Drawing.Image)resources.GetObject("buttonPause.Image");
            buttonPause.ImageTransparentColor = System.Drawing.Color.Magenta;
            buttonPause.Name = "buttonPause";
            buttonPause.Size = new System.Drawing.Size(58, 22);
            buttonPause.Text = "Pause";
            buttonPause.Click += OnButtonPauseClick;
            // 
            // toolStripLabel1
            // 
            toolStripLabel1.Name = "toolStripLabel1";
            toolStripLabel1.Size = new System.Drawing.Size(80, 22);
            toolStripLabel1.Text = "Current Time:";
            // 
            // labelCurrentTime
            // 
            labelCurrentTime.Name = "labelCurrentTime";
            labelCurrentTime.Size = new System.Drawing.Size(49, 22);
            labelCurrentTime.Text = "00:00.00";
            // 
            // toolStripLabel3
            // 
            toolStripLabel3.Name = "toolStripLabel3";
            toolStripLabel3.Size = new System.Drawing.Size(66, 22);
            toolStripLabel3.Text = "Total Time:";
            // 
            // labelTotalTime
            // 
            labelTotalTime.Name = "labelTotalTime";
            labelTotalTime.Size = new System.Drawing.Size(49, 22);
            labelTotalTime.Text = "00:00.00";
            // 
            // trackBarPosition
            // 
            trackBarPosition.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            trackBarPosition.LargeChange = 10;
            trackBarPosition.Location = new System.Drawing.Point(21, 188);
            trackBarPosition.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            trackBarPosition.Maximum = 100;
            trackBarPosition.Name = "trackBarPosition";
            trackBarPosition.Size = new System.Drawing.Size(600, 45);
            trackBarPosition.TabIndex = 16;
            trackBarPosition.Scroll += OnTrackBarPositionScroll;
            // 
            // playbackTimer
            // 
            playbackTimer.Interval = 500;
            playbackTimer.Tick += OnTimerTick;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new System.Drawing.Point(26, 236);
            label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(50, 15);
            label3.TabIndex = 17;
            label3.Text = "Volume:";
            // 
            // waveformPainter2
            // 
            waveformPainter2.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            waveformPainter2.BackColor = System.Drawing.Color.FromArgb(255, 255, 192);
            waveformPainter2.ForeColor = System.Drawing.Color.SaddleBrown;
            waveformPainter2.Location = new System.Drawing.Point(67, 113);
            waveformPainter2.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            waveformPainter2.Name = "waveformPainter2";
            waveformPainter2.Size = new System.Drawing.Size(554, 69);
            waveformPainter2.TabIndex = 19;
            waveformPainter2.Text = "waveformPainter1";
            // 
            // waveformPainter1
            // 
            waveformPainter1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            waveformPainter1.BackColor = System.Drawing.Color.FromArgb(255, 255, 192);
            waveformPainter1.ForeColor = System.Drawing.Color.SaddleBrown;
            waveformPainter1.Location = new System.Drawing.Point(67, 39);
            waveformPainter1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            waveformPainter1.Name = "waveformPainter1";
            waveformPainter1.Size = new System.Drawing.Size(554, 69);
            waveformPainter1.TabIndex = 19;
            waveformPainter1.Text = "waveformPainter1";
            // 
            // volumeMeter2
            // 
            volumeMeter2.Amplitude = 0F;
            volumeMeter2.ForeColor = System.Drawing.Color.FromArgb(0, 192, 0);
            volumeMeter2.Location = new System.Drawing.Point(43, 39);
            volumeMeter2.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            volumeMeter2.MaxDb = 3F;
            volumeMeter2.MinDb = -60F;
            volumeMeter2.Name = "volumeMeter2";
            volumeMeter2.Size = new System.Drawing.Size(16, 143);
            volumeMeter2.TabIndex = 18;
            volumeMeter2.Text = "volumeMeter1";
            // 
            // volumeMeter1
            // 
            volumeMeter1.Amplitude = 0F;
            volumeMeter1.ForeColor = System.Drawing.Color.FromArgb(0, 192, 0);
            volumeMeter1.Location = new System.Drawing.Point(21, 39);
            volumeMeter1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            volumeMeter1.MaxDb = 3F;
            volumeMeter1.MinDb = -60F;
            volumeMeter1.Name = "volumeMeter1";
            volumeMeter1.Size = new System.Drawing.Size(16, 143);
            volumeMeter1.TabIndex = 18;
            volumeMeter1.Text = "volumeMeter1";
            // 
            // volumeSlider1
            // 
            volumeSlider1.Location = new System.Drawing.Point(84, 233);
            volumeSlider1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            volumeSlider1.Name = "volumeSlider1";
            volumeSlider1.Size = new System.Drawing.Size(112, 18);
            volumeSlider1.TabIndex = 11;
            volumeSlider1.VolumeChanged += OnVolumeSliderChanged;
            // 
            // AudioPlaybackPanel
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(waveformPainter2);
            Controls.Add(waveformPainter1);
            Controls.Add(volumeMeter2);
            Controls.Add(volumeMeter1);
            Controls.Add(label3);
            Controls.Add(trackBarPosition);
            Controls.Add(toolStrip1);
            Controls.Add(volumeSlider1);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Name = "AudioPlaybackPanel";
            Size = new System.Drawing.Size(626, 270);
            toolStrip1.ResumeLayout(false);
            toolStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)trackBarPosition).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion
        private NAudio.Gui.VolumeSlider volumeSlider1;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton buttonPlay;
        private System.Windows.Forms.ToolStripButton buttonPause;
        private System.Windows.Forms.TrackBar trackBarPosition;
        private System.Windows.Forms.ToolStripLabel toolStripLabel1;
        private System.Windows.Forms.ToolStripLabel labelCurrentTime;
        private System.Windows.Forms.ToolStripLabel toolStripLabel3;
        private System.Windows.Forms.ToolStripLabel labelTotalTime;
        private System.Windows.Forms.Label label3;
        private NAudio.Gui.VolumeMeter volumeMeter1;
        private NAudio.Gui.VolumeMeter volumeMeter2;
        private NAudio.Gui.WaveformPainter waveformPainter1;
        private NAudio.Gui.WaveformPainter waveformPainter2;
        private System.Windows.Forms.Timer playbackTimer;
    }
}
