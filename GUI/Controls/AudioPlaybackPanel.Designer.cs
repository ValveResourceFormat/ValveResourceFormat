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
            playPauseButton = new ThemedButton();
            labelCurrentTime = new System.Windows.Forms.Label();
            waveFormPictureBox = new System.Windows.Forms.PictureBox();
            playbackTimer = new System.Windows.Forms.Timer(components);
            playbackSlider = new Slider();
            tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            panel1 = new System.Windows.Forms.Panel();
            tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
            loopButton = new ThemedButton();
            rewindLeftButton = new ThemedButton();
            panel2 = new System.Windows.Forms.Panel();
            tableLayoutPanel5 = new System.Windows.Forms.TableLayoutPanel();
            volumeSlider = new Slider();
            volumePictureBox = new System.Windows.Forms.PictureBox();
            tableLayoutPanel3 = new System.Windows.Forms.TableLayoutPanel();
            tableLayoutPanel4 = new System.Windows.Forms.TableLayoutPanel();
            ((System.ComponentModel.ISupportInitialize)waveFormPictureBox).BeginInit();
            tableLayoutPanel1.SuspendLayout();
            panel1.SuspendLayout();
            tableLayoutPanel2.SuspendLayout();
            panel2.SuspendLayout();
            tableLayoutPanel5.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)volumePictureBox).BeginInit();
            tableLayoutPanel3.SuspendLayout();
            tableLayoutPanel4.SuspendLayout();
            SuspendLayout();
            // 
            // playPauseButton
            // 
            playPauseButton.BackColor = System.Drawing.Color.FromArgb(188, 188, 188);
            playPauseButton.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            playPauseButton.CornerRadius = 5;
            playPauseButton.Dock = System.Windows.Forms.DockStyle.Fill;
            playPauseButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            playPauseButton.ForeColor = System.Drawing.Color.Black;
            playPauseButton.HoveredBackColor = System.Drawing.Color.FromArgb(140, 191, 255);
            playPauseButton.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            playPauseButton.Location = new System.Drawing.Point(2, 0);
            playPauseButton.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            playPauseButton.Name = "playPauseButton";
            playPauseButton.Size = new System.Drawing.Size(53, 33);
            playPauseButton.Style = true;
            playPauseButton.TabIndex = 20;
            playPauseButton.Text = "Play";
            playPauseButton.UseVisualStyleBackColor = false;
            playPauseButton.Click += OnPlayPauseButtonClick;
            // 
            // labelCurrentTime
            // 
            labelCurrentTime.Dock = System.Windows.Forms.DockStyle.Fill;
            labelCurrentTime.Font = new System.Drawing.Font("Cascadia Mono", 12F);
            labelCurrentTime.Location = new System.Drawing.Point(171, 8);
            labelCurrentTime.Margin = new System.Windows.Forms.Padding(0);
            labelCurrentTime.Name = "labelCurrentTime";
            labelCurrentTime.Padding = new System.Windows.Forms.Padding(4, 0, 0, 0);
            labelCurrentTime.Size = new System.Drawing.Size(452, 33);
            labelCurrentTime.TabIndex = 21;
            labelCurrentTime.Text = "00:00.00 / 00:00.00";
            labelCurrentTime.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            labelCurrentTime.UseCompatibleTextRendering = true;
            // 
            // waveFormPictureBox
            // 
            waveFormPictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            waveFormPictureBox.Location = new System.Drawing.Point(3, 3);
            waveFormPictureBox.Name = "waveFormPictureBox";
            waveFormPictureBox.Size = new System.Drawing.Size(805, 437);
            waveFormPictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            waveFormPictureBox.TabIndex = 24;
            waveFormPictureBox.TabStop = false;
            waveFormPictureBox.MouseDown += WaveFormPictureBox_MouseDown;
            waveFormPictureBox.MouseEnter += WaveFormPictureBox_MouseEnter;
            waveFormPictureBox.MouseLeave += WaveFormPictureBox_MouseLeave;
            waveFormPictureBox.MouseMove += WaveFormPictureBox_MouseMove;
            waveFormPictureBox.MouseUp += WaveFormPictureBox_MouseUp;
            // 
            // playbackTimer
            // 
            playbackTimer.Interval = 16;
            playbackTimer.Tick += Tick;
            // 
            // playbackSlider
            // 
            playbackSlider.BackColor = System.Drawing.SystemColors.Control;
            playbackSlider.Dock = System.Windows.Forms.DockStyle.Fill;
            playbackSlider.ForeColor = System.Drawing.Color.Black;
            playbackSlider.KnobSize = 14;
            playbackSlider.Location = new System.Drawing.Point(3, 446);
            playbackSlider.Name = "playbackSlider";
            playbackSlider.Size = new System.Drawing.Size(805, 24);
            playbackSlider.SliderColor = System.Drawing.Color.FromArgb(99, 161, 255);
            playbackSlider.SliderHeight = 6;
            playbackSlider.TabIndex = 0;
            playbackSlider.Value = 0F;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 2;
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 77.391304F));
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 22.608696F));
            tableLayoutPanel1.Controls.Add(panel1, 0, 0);
            tableLayoutPanel1.Controls.Add(panel2, 1, 0);
            tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel1.Location = new System.Drawing.Point(3, 476);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 1;
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.Size = new System.Drawing.Size(805, 49);
            tableLayoutPanel1.TabIndex = 26;
            // 
            // panel1
            // 
            panel1.Controls.Add(labelCurrentTime);
            panel1.Controls.Add(tableLayoutPanel2);
            panel1.Dock = System.Windows.Forms.DockStyle.Fill;
            panel1.Location = new System.Drawing.Point(0, 0);
            panel1.Margin = new System.Windows.Forms.Padding(0);
            panel1.Name = "panel1";
            panel1.Padding = new System.Windows.Forms.Padding(0, 8, 0, 8);
            panel1.Size = new System.Drawing.Size(623, 49);
            panel1.TabIndex = 0;
            // 
            // tableLayoutPanel2
            // 
            tableLayoutPanel2.ColumnCount = 3;
            tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            tableLayoutPanel2.Controls.Add(playPauseButton, 0, 0);
            tableLayoutPanel2.Controls.Add(loopButton, 1, 0);
            tableLayoutPanel2.Controls.Add(rewindLeftButton, 2, 0);
            tableLayoutPanel2.Dock = System.Windows.Forms.DockStyle.Left;
            tableLayoutPanel2.Location = new System.Drawing.Point(0, 8);
            tableLayoutPanel2.Margin = new System.Windows.Forms.Padding(0);
            tableLayoutPanel2.Name = "tableLayoutPanel2";
            tableLayoutPanel2.RowCount = 1;
            tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel2.Size = new System.Drawing.Size(171, 33);
            tableLayoutPanel2.TabIndex = 22;
            // 
            // loopButton
            // 
            loopButton.BackColor = System.Drawing.Color.FromArgb(188, 188, 188);
            loopButton.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            loopButton.CornerRadius = 5;
            loopButton.Dock = System.Windows.Forms.DockStyle.Fill;
            loopButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            loopButton.ForeColor = System.Drawing.Color.Black;
            loopButton.HoveredBackColor = System.Drawing.Color.FromArgb(140, 191, 255);
            loopButton.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            loopButton.Location = new System.Drawing.Point(59, 0);
            loopButton.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            loopButton.Name = "loopButton";
            loopButton.Size = new System.Drawing.Size(53, 33);
            loopButton.Style = true;
            loopButton.TabIndex = 21;
            loopButton.Text = "repeat";
            loopButton.UseVisualStyleBackColor = false;
            loopButton.Click += loopButton_Click;
            // 
            // rewindLeftButton
            // 
            rewindLeftButton.BackColor = System.Drawing.Color.FromArgb(188, 188, 188);
            rewindLeftButton.ClickedBackColor = System.Drawing.Color.FromArgb(99, 161, 255);
            rewindLeftButton.CornerRadius = 5;
            rewindLeftButton.Dock = System.Windows.Forms.DockStyle.Fill;
            rewindLeftButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            rewindLeftButton.ForeColor = System.Drawing.Color.Black;
            rewindLeftButton.HoveredBackColor = System.Drawing.Color.FromArgb(140, 191, 255);
            rewindLeftButton.LabelFormatFlags = System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.EndEllipsis;
            rewindLeftButton.Location = new System.Drawing.Point(116, 0);
            rewindLeftButton.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            rewindLeftButton.Name = "rewindLeftButton";
            rewindLeftButton.Size = new System.Drawing.Size(53, 33);
            rewindLeftButton.Style = true;
            rewindLeftButton.TabIndex = 22;
            rewindLeftButton.Text = "<<";
            rewindLeftButton.UseVisualStyleBackColor = false;
            rewindLeftButton.Click += rewindLeftButton_Click;
            // 
            // panel2
            // 
            panel2.Controls.Add(tableLayoutPanel5);
            panel2.Dock = System.Windows.Forms.DockStyle.Fill;
            panel2.Location = new System.Drawing.Point(623, 0);
            panel2.Margin = new System.Windows.Forms.Padding(0);
            panel2.Name = "panel2";
            panel2.Size = new System.Drawing.Size(182, 49);
            panel2.TabIndex = 1;
            // 
            // tableLayoutPanel5
            // 
            tableLayoutPanel5.ColumnCount = 2;
            tableLayoutPanel5.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            tableLayoutPanel5.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel5.Controls.Add(volumeSlider, 1, 0);
            tableLayoutPanel5.Controls.Add(volumePictureBox, 0, 0);
            tableLayoutPanel5.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel5.Location = new System.Drawing.Point(0, 0);
            tableLayoutPanel5.Name = "tableLayoutPanel5";
            tableLayoutPanel5.RowCount = 1;
            tableLayoutPanel5.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel5.Size = new System.Drawing.Size(182, 49);
            tableLayoutPanel5.TabIndex = 19;
            // 
            // volumeSlider
            // 
            volumeSlider.BackColor = System.Drawing.SystemColors.Control;
            volumeSlider.Dock = System.Windows.Forms.DockStyle.Fill;
            volumeSlider.ForeColor = System.Drawing.Color.Black;
            volumeSlider.KnobSize = 14;
            volumeSlider.Location = new System.Drawing.Point(24, 12);
            volumeSlider.Margin = new System.Windows.Forms.Padding(0, 12, 0, 12);
            volumeSlider.Name = "volumeSlider";
            volumeSlider.Size = new System.Drawing.Size(158, 25);
            volumeSlider.SliderColor = System.Drawing.Color.FromArgb(99, 161, 255);
            volumeSlider.SliderHeight = 6;
            volumeSlider.TabIndex = 18;
            volumeSlider.Value = 0F;
            // 
            // volumePictureBox
            // 
            volumePictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            volumePictureBox.Location = new System.Drawing.Point(0, 0);
            volumePictureBox.Margin = new System.Windows.Forms.Padding(0);
            volumePictureBox.Name = "volumePictureBox";
            volumePictureBox.Size = new System.Drawing.Size(24, 49);
            volumePictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            volumePictureBox.TabIndex = 19;
            volumePictureBox.TabStop = false;
            // 
            // tableLayoutPanel3
            // 
            tableLayoutPanel3.ColumnCount = 1;
            tableLayoutPanel3.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel3.Controls.Add(tableLayoutPanel4, 0, 0);
            tableLayoutPanel3.Controls.Add(tableLayoutPanel1, 1, 1);
            tableLayoutPanel3.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel3.Location = new System.Drawing.Point(4, 4);
            tableLayoutPanel3.Margin = new System.Windows.Forms.Padding(0);
            tableLayoutPanel3.Name = "tableLayoutPanel3";
            tableLayoutPanel3.RowCount = 2;
            tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel3.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 55F));
            tableLayoutPanel3.Size = new System.Drawing.Size(811, 528);
            tableLayoutPanel3.TabIndex = 25;
            // 
            // tableLayoutPanel4
            // 
            tableLayoutPanel4.ColumnCount = 1;
            tableLayoutPanel4.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel4.Controls.Add(playbackSlider, 1, 1);
            tableLayoutPanel4.Controls.Add(waveFormPictureBox, 0, 0);
            tableLayoutPanel4.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel4.Location = new System.Drawing.Point(0, 0);
            tableLayoutPanel4.Margin = new System.Windows.Forms.Padding(0);
            tableLayoutPanel4.Name = "tableLayoutPanel4";
            tableLayoutPanel4.RowCount = 2;
            tableLayoutPanel4.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel4.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            tableLayoutPanel4.Size = new System.Drawing.Size(811, 473);
            tableLayoutPanel4.TabIndex = 25;
            // 
            // AudioPlaybackPanel
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(tableLayoutPanel3);
            Margin = new System.Windows.Forms.Padding(0);
            Name = "AudioPlaybackPanel";
            Padding = new System.Windows.Forms.Padding(4);
            Size = new System.Drawing.Size(819, 536);
            ((System.ComponentModel.ISupportInitialize)waveFormPictureBox).EndInit();
            tableLayoutPanel1.ResumeLayout(false);
            panel1.ResumeLayout(false);
            tableLayoutPanel2.ResumeLayout(false);
            panel2.ResumeLayout(false);
            tableLayoutPanel5.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)volumePictureBox).EndInit();
            tableLayoutPanel3.ResumeLayout(false);
            tableLayoutPanel4.ResumeLayout(false);
            ResumeLayout(false);

        }

        #endregion
        private ThemedButton playPauseButton;
        private System.Windows.Forms.Label labelCurrentTime;
        private System.Windows.Forms.PictureBox waveFormPictureBox;
        private System.Windows.Forms.Timer playbackTimer;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Panel panel2;
        private Slider playbackSlider;
        private Slider volumeSlider;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel3;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel4;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
        private ThemedButton loopButton;
        private ThemedButton rewindLeftButton;
        private System.Windows.Forms.PictureBox volumePictureBox;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel5;
    }
}
