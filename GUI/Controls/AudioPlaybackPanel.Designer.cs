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
            toolStrip1 = new System.Windows.Forms.ToolStrip();
            trackBarPosition = new System.Windows.Forms.TrackBar();
            playbackTimer = new System.Windows.Forms.Timer(components);
            label3 = new System.Windows.Forms.Label();
            volumeSlider1 = new NAudio.Gui.VolumeSlider();
            playPauseButton = new System.Windows.Forms.Button();
            labelCurrentTime = new System.Windows.Forms.Label();
            labelTotalTime = new System.Windows.Forms.Label();
            label1 = new System.Windows.Forms.Label();
            waveFormPictureBox = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)trackBarPosition).BeginInit();
            ((System.ComponentModel.ISupportInitialize)waveFormPictureBox).BeginInit();
            SuspendLayout();
            //
            // toolStrip1
            //
            toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            toolStrip1.Location = new System.Drawing.Point(0, 0);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new System.Drawing.Size(600, 25);
            toolStrip1.TabIndex = 15;
            toolStrip1.Text = "toolStrip1";
            //
            // trackBarPosition
            //
            trackBarPosition.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            trackBarPosition.LargeChange = 10;
            trackBarPosition.Location = new System.Drawing.Point(16, 176);
            trackBarPosition.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            trackBarPosition.Maximum = 100;
            trackBarPosition.Name = "trackBarPosition";
            trackBarPosition.Size = new System.Drawing.Size(568, 45);
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
            label3.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            label3.AutoSize = true;
            label3.Location = new System.Drawing.Point(534, 227);
            label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(50, 15);
            label3.TabIndex = 17;
            label3.Text = "Volume:";
            //
            // volumeSlider1
            //
            volumeSlider1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            volumeSlider1.Location = new System.Drawing.Point(472, 252);
            volumeSlider1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            volumeSlider1.Name = "volumeSlider1";
            volumeSlider1.Size = new System.Drawing.Size(112, 18);
            volumeSlider1.TabIndex = 11;
            volumeSlider1.VolumeChanged += OnVolumeSliderChanged;
            //
            // playPauseButton
            //
            playPauseButton.Location = new System.Drawing.Point(16, 227);
            playPauseButton.Name = "playPauseButton";
            playPauseButton.Size = new System.Drawing.Size(64, 64);
            playPauseButton.TabIndex = 20;
            playPauseButton.Text = "Play";
            playPauseButton.UseVisualStyleBackColor = true;
            playPauseButton.Click += OnPlayPauseButtonClick;
            //
            // labelCurrentTime
            //
            labelCurrentTime.AutoSize = true;
            labelCurrentTime.Font = new System.Drawing.Font("Cascadia Mono", 16F);
            labelCurrentTime.Location = new System.Drawing.Point(104, 243);
            labelCurrentTime.Name = "labelCurrentTime";
            labelCurrentTime.Size = new System.Drawing.Size(117, 29);
            labelCurrentTime.TabIndex = 21;
            labelCurrentTime.Text = "00:00.00";
            //
            // labelTotalTime
            //
            labelTotalTime.AutoSize = true;
            labelTotalTime.Font = new System.Drawing.Font("Cascadia Mono", 12F);
            labelTotalTime.Location = new System.Drawing.Point(256, 247);
            labelTotalTime.Name = "labelTotalTime";
            labelTotalTime.Size = new System.Drawing.Size(82, 21);
            labelTotalTime.TabIndex = 22;
            labelTotalTime.Text = "00:00.00";
            //
            // label1
            //
            label1.AutoSize = true;
            label1.Font = new System.Drawing.Font("Cascadia Mono", 12F);
            label1.Location = new System.Drawing.Point(229, 247);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(19, 21);
            label1.TabIndex = 23;
            label1.Text = "/";
            //
            // waveFormPictureBox
            //
            waveFormPictureBox.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            waveFormPictureBox.Location = new System.Drawing.Point(16, 16);
            waveFormPictureBox.Name = "waveFormPictureBox";
            waveFormPictureBox.Size = new System.Drawing.Size(568, 160);
            waveFormPictureBox.TabIndex = 24;
            waveFormPictureBox.TabStop = false;
            //
            // AudioPlaybackPanel
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(waveFormPictureBox);
            Controls.Add(label1);
            Controls.Add(labelTotalTime);
            Controls.Add(labelCurrentTime);
            Controls.Add(playPauseButton);
            Controls.Add(label3);
            Controls.Add(trackBarPosition);
            Controls.Add(toolStrip1);
            Controls.Add(volumeSlider1);
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            Name = "AudioPlaybackPanel";
            Size = new System.Drawing.Size(600, 300);
            ((System.ComponentModel.ISupportInitialize)trackBarPosition).EndInit();
            ((System.ComponentModel.ISupportInitialize)waveFormPictureBox).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion
        private NAudio.Gui.VolumeSlider volumeSlider1;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.TrackBar trackBarPosition;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Timer playbackTimer;
        private System.Windows.Forms.Button playPauseButton;
        private System.Windows.Forms.Label labelCurrentTime;
        private System.Windows.Forms.Label labelTotalTime;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.PictureBox waveFormPictureBox;
    }
}
