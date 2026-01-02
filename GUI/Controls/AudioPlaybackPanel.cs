using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

// Based on https://github.com/naudio/NAudio.WaveFormRenderer (MIT License - Copyright (c) 2021 NAudio)

namespace GUI.Controls
{
    internal partial class AudioPlaybackPanel : UserControl
    {
        private readonly record struct Peak(float Min, float Max);
        private readonly record struct Rgb(byte R, byte G, byte B);

        private readonly WaveOutEvent WaveOut = new();
        private readonly WaveStream WaveStream;
        private readonly MemoryStream audioData;
        private readonly SampleChannel? SampleChannel;

        private Bitmap? WavePeakBitmap;

        private readonly bool AutoPlay;
        private bool Looping;
        private bool WaveformClicked;

        private List<Peak>? cachedPeaks;
        private float maxAmplitude = 1.0f;
        private bool peaksNeedRecalculation = true;
        private int lastRenderedProgressionPixel = -1;

        private readonly Stopwatch playbackStopwatch = new();
        private TimeSpan playbackStartPosition;

        private readonly (int Start, int End) LoopMarkers;

        private readonly Image PlayImage = MainForm.ImageList.Images[MainForm.Icons["AudioPlay"]];
        private readonly Image PauseImage = MainForm.ImageList.Images[MainForm.Icons["AudioPause"]];
        private readonly Image RepeatImage = MainForm.ImageList.Images[MainForm.Icons["AudioRepeat"]];
        private readonly Image RepeatImagePressed = MainForm.ImageList.Images[MainForm.Icons["AudioRepeatPressed"]];

        public AudioPlaybackPanel(WaveStream inputStream, bool autoPlay, (int start, int end) loopMarkers)
        {
            AutoPlay = autoPlay;
            Dock = DockStyle.Fill;

            InitializeComponent();

            WaveStream = inputStream;

            // some files have stupid values;
            loopMarkers = (Math.Max(0, loopMarkers.start), Math.Max(0, loopMarkers.end));

            if (loopMarkers.end > loopMarkers.start)
            {
                LoopMarkers = loopMarkers;
                Looping = true;
            }
            else
            {
                LoopMarkers = (0, (int)(WaveStream.Length / WaveStream.BlockAlign));
                Looping = false;
            }

            volumePictureBox.Image = MainForm.ImageList.Images[MainForm.Icons["AudioVolume"]];

            WaveStream.Position = 0;
            audioData = new MemoryStream((int)WaveStream.Length);
            WaveStream.CopyTo(audioData);

            labelCurrentTime.Text = GetCurrentTimeString(WaveStream.CurrentTime);
            volumeSlider.Value = Settings.Config.Volume;

            WaveStream? stream = null;

            playPauseButton.Image = PlayImage;
            playPauseButton.Text = "";

            loopButton.Image = Looping ? RepeatImagePressed : RepeatImage;
            loopButton.Text = "";
            rewindLeftButton.Image = MainForm.ImageList.Images[MainForm.Icons["AudioRewindLeft"]];
            rewindLeftButton.Text = "";

            playbackSlider.ValueChanged = Value =>
            {
                UpdatePlaybackProgression(Value);
            };

            volumeSlider.ValueChanged = SetVolume;

            try
            {
                if (WaveStream.WaveFormat.Encoding == WaveFormatEncoding.Adpcm)
                {
                    stream = WaveFormatConversionStream.CreatePcmStream(WaveStream);
                    SampleChannel = new SampleChannel(stream, true);
                }
                else
                {
                    SampleChannel = new SampleChannel(WaveStream, true);
                }

                SampleChannel.Volume = volumeSlider.Value;
                WaveOut.PlaybackStopped += OnPlaybackStopped;
                WaveOut.Init(SampleChannel);

                stream = null;
            }
            catch (Exception driverCreateException)
            {
                Program.ShowError(driverCreateException);
            }
            finally
            {
                stream?.Dispose();
            }
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            SetLooping(Looping);
        }

        private void SetVolume(float value)
        {
            SampleChannel?.Volume = value;
            Settings.Config.Volume = value;
        }

        private void SetLooping(bool looping)
        {
            Looping = looping;

            if (Looping)
            {
                loopButton.Image = RepeatImagePressed;
                loopButton.BackColor = Themer.CurrentThemeColors.HoverAccent;
            }
            else
            {
                loopButton.Image = RepeatImage;
                loopButton.BackColor = Themer.CurrentThemeColors.Border;
            }
        }

        private string GetCurrentTimeString(TimeSpan currentTime)
        {
            return $"{currentTime.ToString("mm\\:ss\\.ff", CultureInfo.InvariantCulture)} / {WaveStream.TotalTime.ToString("mm\\:ss\\.ff", CultureInfo.InvariantCulture)}";
        }

        private void UpdatePlaybackProgression(float progression)
        {
            progression = Math.Clamp(progression, 0, 1);

            WaveStream.CurrentTime = TimeSpan.FromSeconds(WaveStream.TotalTime.TotalSeconds * progression);

            if (WaveOut.PlaybackState == PlaybackState.Playing)
            {
                playbackStopwatch.Restart();
                playbackStartPosition = WaveStream.CurrentTime;

                if (!playbackSlider.Clicked && !WaveformClicked)
                {
                    UpdateTime();
                }
            }
            else
            {
                if (playbackSlider.Clicked || WaveformClicked)
                {
                    RenderWaveForm(progression);
                    labelCurrentTime.Text = GetCurrentTimeString(WaveStream.CurrentTime);
                    playbackSlider.Refresh();
                    waveFormPictureBox.Refresh();
                    labelCurrentTime.Refresh();
                }
                else
                {
                    UpdateTime();
                }
            }
        }

        public void RenderWaveForm(float progression)
        {
            if (WaveStream == null)
            {
                return;
            }

            var width = waveFormPictureBox.Width;
            var height = waveFormPictureBox.Height;
            var widthProgression = (int)(progression * width);

            if (widthProgression == lastRenderedProgressionPixel
                && WavePeakBitmap != null
                && WavePeakBitmap.Width == width
                && WavePeakBitmap.Height == height
                && !peaksNeedRecalculation)
            {
                return;
            }

            var widthChanged = WavePeakBitmap == null || WavePeakBitmap.Width != width;

            if (WavePeakBitmap == null || WavePeakBitmap.Width != width || WavePeakBitmap.Height != height)
            {
                WavePeakBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            }

            if (widthChanged)
            {
                peaksNeedRecalculation = true;
            }

            if (peaksNeedRecalculation)
            {
                CalculatePeaks(width);
            }

            var x = 0;
            var peakIndex = 0;
            var midPoint = height / 2;
            var pixelsPerPeak = this.AdjustForDPI(4);

            var colorRaw = Themer.CurrentThemeColors.Accent;
            var peakColorRgb = new Rgb(colorRaw.R, colorRaw.G, colorRaw.B);
            var midColorRaw = ControlPaint.Dark(colorRaw, 0.2f);
            var midColorRgb = new Rgb(midColorRaw.R, midColorRaw.G, midColorRaw.B);
            var backMidColorRaw = ControlPaint.Dark(midColorRaw, 0.2f);
            var backMidColorRgb = new Rgb(backMidColorRaw.R, backMidColorRaw.G, backMidColorRaw.B);

            var bmpData = WavePeakBitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Span<byte> pixels;
            unsafe
            {
                pixels = new Span<byte>((void*)bmpData.Scan0, bmpData.Stride * height);
            }

            pixels.Clear();

            while (x < width && peakIndex < cachedPeaks!.Count)
            {
                var peak = cachedPeaks[peakIndex];
                var normalizedMax = peak.Max / maxAmplitude;
                var normalizedMin = peak.Min / maxAmplitude;

                for (var n = 0; n < pixelsPerPeak && x < width; n++)
                {
                    var topY = (int)(midPoint - (normalizedMax * midPoint));
                    var bottomY = (int)(midPoint - (normalizedMin * midPoint));

                    var isBeforeProgression = x <= widthProgression;

                    if (topY < midPoint)
                    {
                        for (var y = topY; y < midPoint; y++)
                        {
                            var t = (float)(y - topY) / (midPoint - topY);
                            var rgb = isBeforeProgression
                                ? InterpolateColor(peakColorRgb, midColorRgb, t)
                                : InterpolateColor(midColorRgb, backMidColorRgb, t);

                            SetPixel(pixels, bmpData.Stride, x, y, rgb);
                        }
                    }

                    if (bottomY > midPoint)
                    {
                        for (var y = midPoint; y < bottomY; y++)
                        {
                            var t = (float)(y - midPoint) / (bottomY - midPoint);
                            var rgb = isBeforeProgression
                                ? InterpolateColor(midColorRgb, peakColorRgb, t)
                                : InterpolateColor(backMidColorRgb, midColorRgb, t);

                            SetPixel(pixels, bmpData.Stride, x, y, rgb);
                        }
                    }

                    x++;
                }

                peakIndex++;
            }

            WavePeakBitmap.UnlockBits(bmpData);

            waveFormPictureBox.Image = WavePeakBitmap;
            lastRenderedProgressionPixel = widthProgression;
        }

        private static void SetPixel(Span<byte> pixels, int stride, int x, int y, Rgb rgb)
        {
            var position = y * stride + x * 4;
            pixels[position] = rgb.B;
            pixels[position + 1] = rgb.G;
            pixels[position + 2] = rgb.R;
            pixels[position + 3] = 255;
        }

        private static Rgb InterpolateColor(Rgb c1, Rgb c2, float t)
        {
            return new Rgb(
                (byte)(c1.R + (c2.R - c1.R) * t),
                (byte)(c1.G + (c2.G - c1.G) * t),
                (byte)(c1.B + (c2.B - c1.B) * t)
            );
        }

        private void CalculatePeaks(int width)
        {
            var pixelsPerPeak = this.AdjustForDPI(4);

            audioData.Position = 0;
            using var waveStream = new RawSourceWaveStream(audioData, WaveStream.WaveFormat);

            var samples = waveStream.Length / waveStream.BlockAlign;
            var samplesPerPixel = (double)samples / width;

            var provider = waveStream.ToSampleProvider();
            var samplesPerPeak = samplesPerPixel * pixelsPerPeak;
            samplesPerPeak -= samplesPerPeak % waveStream.WaveFormat.BlockAlign;
            var readBuffer = new float[(int)samplesPerPeak];

            var peaks = new List<Peak>();
            while (peaks.Count < width / pixelsPerPeak)
            {
                var peak = GetNextPeak(provider, readBuffer);
                peaks.Add(peak);
            }

            maxAmplitude = peaks.Max(static p => Math.Max(Math.Abs(p.Max), Math.Abs(p.Min)));
            if (maxAmplitude < 0.0001f)
            {
                maxAmplitude = 1.0f;
            }

            cachedPeaks = peaks;
            peaksNeedRecalculation = false;
        }

        private static Peak GetNextPeak(ISampleProvider provider, float[] readBuffer)
        {
            var samplesRead = provider.Read(readBuffer, 0, readBuffer.Length);

            if (samplesRead == 0)
            {
                return new(0, 0);
            }

            var max = float.MinValue;
            var min = float.MaxValue;

            for (var i = 0; i < samplesRead; i++)
            {
                var p = readBuffer[i];

                if (max < p)
                {
                    max = p;
                }

                if (min > p)
                {
                    min = p;
                }
            }

            return new Peak(min, max);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RenderWaveForm(playbackSlider.Value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            WaveStream?.Position = 0;

            if (AutoPlay)
            {
                Play();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!Visible)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            const double SeekIncrementSeconds = 5.0;
            const float VolumeIncrement = 0.05f;

            switch (keyData)
            {
                case Keys.Space:
                case Keys.Enter:
                    OnPlayPauseButtonClick(this, EventArgs.Empty);
                    return true;

                case Keys.Left:
                    SeekRelative(-SeekIncrementSeconds);
                    return true;

                case Keys.Right:
                    SeekRelative(SeekIncrementSeconds);
                    return true;

                case Keys.Up:
                    volumeSlider.Value += VolumeIncrement;
                    SetVolume(volumeSlider.Value);
                    return true;

                case Keys.Down:
                    volumeSlider.Value -= VolumeIncrement;
                    SetVolume(volumeSlider.Value);
                    return true;

                case Keys.Home:
                    UpdatePlaybackProgression(0f);
                    playbackSlider.Value = 0f;
                    return true;

                case Keys.End:
                    UpdatePlaybackProgression(1f);
                    playbackSlider.Value = 1f;
                    return true;

                case Keys.L:
                    loopButton_Click(this, EventArgs.Empty);
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SeekRelative(double seconds)
        {
            if (WaveStream == null)
            {
                return;
            }

            var currentSeconds = WaveStream.CurrentTime.TotalSeconds;
            var newSeconds = Math.Clamp(currentSeconds + seconds, 0, WaveStream.TotalTime.TotalSeconds);
            var progression = (float)(newSeconds / WaveStream.TotalTime.TotalSeconds);

            UpdatePlaybackProgression(progression);
            playbackSlider.Value = progression;
        }

        private void OnPlayPauseButtonClick(object sender, EventArgs e)
        {
            if (WaveOut.PlaybackState == PlaybackState.Playing)
            {
                WaveOut.Pause();
                playbackTimer.Enabled = false;
                playPauseButton.Image = PlayImage;
            }
            else
            {
                Play();
            }
        }

        public void Play()
        {
            if (WaveOut.PlaybackState == PlaybackState.Playing)
            {
                return;
            }

            if (WaveStream.CurrentTime >= WaveStream.TotalTime)
            {
                WaveStream.Position = 0;
            }

            playbackStopwatch.Restart();
            playbackStartPosition = WaveStream.CurrentTime;

            SampleChannel?.Volume = volumeSlider.Value;

            WaveOut.Play();
            playbackTimer.Enabled = true;
            playPauseButton.Image = PauseImage;
            UpdateTime();
        }

        void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Program.ShowError(e.Exception);
            }

            if (IsDisposed)
            {
                return;
            }

            BeginInvoke(() =>
            {
                playPauseButton.Image = PlayImage;
                playbackTimer.Enabled = false;
                UpdateTime();
            });
        }

        private void CloseWaveOut()
        {
            playbackTimer.Enabled = false;

            if (WaveOut != null)
            {
                WaveOut.PlaybackStopped -= OnPlaybackStopped;
                WaveOut.Stop();
                WaveOut.Dispose();
            }

            PlayImage.Dispose();
            PauseImage.Dispose();
            RepeatImage.Dispose();
            RepeatImagePressed.Dispose();

            WavePeakBitmap?.Dispose();
            WaveStream?.Dispose();
        }

        private void Tick(object? sender, EventArgs e)
        {
            UpdateTime();
        }

        private void UpdateTime(bool updatePlaybackSlider = true)
        {
            if (IsDisposed)
            {
                return;
            }

            var currentTime = playbackStartPosition;

            if (WaveOut.PlaybackState == PlaybackState.Playing)
            {
                var elapsed = playbackStopwatch.Elapsed;
                currentTime = playbackStartPosition + elapsed;

                if (currentTime > WaveStream.TotalTime)
                {
                    currentTime = WaveStream.TotalTime;
                }
            }
            else
            {
                currentTime = WaveStream.CurrentTime;
            }

            if (Looping)
            {
                var sampleCount = (WaveStream.Length / WaveStream.BlockAlign);
                var endLoopTime = (float)LoopMarkers.End / sampleCount * WaveStream.TotalTime;

                if (currentTime >= endLoopTime)
                {
                    UpdatePlaybackProgression(LoopMarkers.Start / sampleCount);
                }
            }

            var progression = (float)Math.Min(1, currentTime.TotalSeconds / WaveStream.TotalTime.TotalSeconds);

            if (updatePlaybackSlider && !playbackSlider.Clicked)
            {
                playbackSlider.Value = progression;
            }

            RenderWaveForm(progression);

            labelCurrentTime.Text = GetCurrentTimeString(currentTime);
        }

        private void WaveFormPictureBox_MouseLeave(object sender, EventArgs e)
        {
            Cursor = Cursors.Default;
        }

        private void WaveFormPictureBox_MouseEnter(object sender, EventArgs e)
        {
            Cursor = Cursors.Hand;
        }

        private void WaveFormPictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (WaveformClicked)
            {
                var progression = (float)e.Location.X / waveFormPictureBox.Width;
                UpdatePlaybackProgression(progression);
                playbackSlider.Value = progression;
            }
        }


        private void WaveFormPictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            WaveformClicked = false;
        }

        private void WaveFormPictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            WaveformClicked = true;

            var progression = (float)e.Location.X / waveFormPictureBox.Width;
            UpdatePlaybackProgression(progression);
            playbackSlider.Value = progression;
        }

        private void loopButton_Click(object sender, EventArgs e)
        {
            SetLooping(!Looping);
        }

        private void rewindLeftButton_Click(object sender, EventArgs e)
        {
            UpdatePlaybackProgression(0);
            Play();
        }
    }
}
