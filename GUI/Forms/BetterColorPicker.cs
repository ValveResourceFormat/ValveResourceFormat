using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using GUI.Utils;
using Svg.Skia;

namespace GUI.Forms
{
    public partial class BetterColorPicker : ThemedForm
    {
        private readonly Action<Color>? ColorChanged;

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            // making this resizeable is an absolute nightmare, keep it fixed but adjust its size based on DPI to be same on screen.
            Size = new Size(Themer.AdjustForDPI(this, 300), Themer.AdjustForDPI(this, 500));
        }

        internal double H;
        internal double S;
        internal double V;
        internal bool NonPaintRefresh;

        private Color OldPickerColor;

        public Color PickedColor { get; private set; } = Color.White;
        private readonly Color OldColor = Color.White;

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Cursor = Cursors.Default;
        }

        public BetterColorPicker(Color startColor, Action<Color> colorChanged)
        {
            InitializeComponent();

            ColorToHSV(startColor, out H, out S, out V);

            HSlider.SliderType = HSVSlider.ColorSliderType.H;
            HSliderValueInput.CustomTextChanged += (sender, e) => { HSlider.SetValue(HSliderValueInput.Value * 360); };

            SSlider.SliderType = HSVSlider.ColorSliderType.S;
            SSliderValueInput.CustomTextChanged += (sender, e) => { SSlider.SetValue(SSliderValueInput.Value); };

            VSlider.SliderType = HSVSlider.ColorSliderType.V;
            VSliderValueInput.CustomTextChanged += (sender, e) => { VSlider.SetValue(VSliderValueInput.Value); };

            OldColorPanel.BackColor = startColor;
            NewColorPanel.BackColor = startColor;

            OldColor = startColor;

            OK.Select();

            DoubleBuffered = true;

            UpdateAllControls();
            UpdateTextBoxes();
            ColorChanged = colorChanged;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            using var svgResource = Assembly.GetExecutingAssembly().GetManifestResourceStream("GUI.Icons.ColorEyeDropper.svg");
            Debug.Assert(svgResource is not null);
            using var svg = new SKSvg();
            svg.Load(svgResource);
            Debug.Assert(EyedropperButton.BackgroundImage is not null);
            EyedropperButton.BackgroundImage = Themer.SvgToBitmap(svg, this.AdjustForDPI(EyedropperButton.BackgroundImage.Width), this.AdjustForDPI(EyedropperButton.BackgroundImage.Height));
        }

        public void UpdateAllControls(bool updateSliderValue = true)
        {
            SuspendLayout();

            HSlider.Invalidate();
            SSlider.Invalidate();
            VSlider.Invalidate();
            MainColorPanel.Invalidate();
            HuePanel.Invalidate();

            PickedColor = ColorFromHSV(H, S, V);

            NewColorPanel.BackColor = PickedColor;

            if (updateSliderValue)
            {
                NonPaintRefresh = true;
                HSliderValueInput.SetTextWithoutCustomEvent((H / 360f).ToString(CultureInfo.InvariantCulture));
                SSliderValueInput.SetTextWithoutCustomEvent(S.ToString(CultureInfo.InvariantCulture));
                VSliderValueInput.SetTextWithoutCustomEvent(V.ToString(CultureInfo.InvariantCulture));
                NonPaintRefresh = false;
            }

            if (PickedColor != OldPickerColor)
            {
                ColorChanged?.Invoke(PickedColor);
            }

            OldPickerColor = PickedColor;
            ResumeLayout();
            Refresh();
        }

        public void UpdateTextBoxes()
        {
            HexTextBox.SetTextWithoutCustomEvent(ColorTranslator.ToHtml(PickedColor)[1..]);

            ColorTextBoxR.SetTextWithoutCustomEvent(PickedColor.R.ToString(CultureInfo.InvariantCulture));
            ColorTextBoxG.SetTextWithoutCustomEvent(PickedColor.G.ToString(CultureInfo.InvariantCulture));
            ColorTextBoxB.SetTextWithoutCustomEvent(PickedColor.B.ToString(CultureInfo.InvariantCulture));
        }

        private void OK_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            PickedColor = ColorFromHSV(H, S, V);
            ColorChanged?.Invoke(PickedColor);
            Close();
        }

        private void HexTextBox_TextChanged(object sender, EventArgs e)
        {
            try
            {
                ColorToHSV(HexTextBox.Value, out H, out S, out V);
            }
            catch (Exception)
            {
            }

            UpdateAllControls();
        }

        private void ColorTextBoxR_TextChanged(object sender, EventArgs e)
        {
            if (!ColorTextBoxR.MiddleMouseDown)
            {
                RSliderLastColor = ColorFromHSV(H, S, V);
            }

            var currentColor = Color.FromArgb(
                ColorTextBoxR.Value,
                RSliderLastColor.G,
                RSliderLastColor.B);
            ColorToHSV(currentColor, out H, out S, out V);

            UpdateAllControls();
        }

        private void ColorTextBoxR_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                RSliderLastColor = ColorFromHSV(H, S, V);
            }
        }

        private void ColorTextBoxG_TextChanged(object sender, EventArgs e)
        {
            if (!ColorTextBoxG.MiddleMouseDown)
            {
                GSliderLastColor = ColorFromHSV(H, S, V);
            }

            var currentColor = Color.FromArgb(
                GSliderLastColor.R,
               ColorTextBoxG.Value,
                GSliderLastColor.B);
            ColorToHSV(currentColor, out H, out S, out V);

            UpdateAllControls();
        }

        private void ColorTextBoxG_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                GSliderLastColor = ColorFromHSV(H, S, V);
            }
        }

        private void ColorTextBoxB_TextChanged(object sender, EventArgs e)
        {
            if (!ColorTextBoxB.MiddleMouseDown)
            {
                BSliderLastColor = ColorFromHSV(H, S, V);
            }

            var currentColor = Color.FromArgb(
                BSliderLastColor.R,
                BSliderLastColor.G,
                ColorTextBoxB.Value);
            ColorToHSV(currentColor, out H, out S, out V);

            UpdateAllControls();
        }

        private void ColorTextBoxB_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                BSliderLastColor = ColorFromHSV(H, S, V);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.Cancel)
            {
                PickedColor = OldColor;
                ColorChanged?.Invoke(PickedColor);
            }

            base.OnFormClosing(e);
        }

        private static Color GetColorAt(Point location)
        {
            using var pixelContainer = new Bitmap(1, 1);
            using (var g = Graphics.FromImage(pixelContainer))
            {
                g.CopyFromScreen(location, Point.Empty, pixelContainer.Size);
            }

            return pixelContainer.GetPixel(0, 0);
        }

        // eyedropper stuff

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "<Pending>")]
        private void EyedropperButton_MouseUp(object sender, MouseEventArgs e)
        {
            var oldColor = PickedColor;

            var form = new Form
            {
                TopMost = true,
                FormBorderStyle = FormBorderStyle.None,
                AllowTransparency = true,
                Opacity = 0.01,
                BackColor = Color.Black,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                KeyPreview = true
            };

            form.Shown += (s, e) =>
            {
                form.Bounds = SystemInformation.VirtualScreen;
            };

            form.FormClosed += (s, e) =>
            {
                EyedropperButton.Invalidate();
            };

            form.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    CloseColorPicker(form, GetColorAt(Cursor.Position));
                }
                else
                {
                    CloseColorPicker(form, oldColor);
                }
            };

            form.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    CloseColorPicker(form, oldColor);
                }
            };

            form.MouseMove += (s, e) =>
            {
                var selectedColor = GetColorAt(Cursor.Position);
                var hexValue = ColorTranslator.ToHtml(selectedColor);

                Invoke(() =>
                {
                    HexTextBox.Text = hexValue;
                });
            };

            form.Show();
        }

        private void CloseColorPicker(Form picker, Color color)
        {
            Invoke(() => { HexTextBox.Text = ColorTranslator.ToHtml(color); });
            picker.Close();
        }

        internal static float Remap(float source, float sourceFrom, float sourceTo, float targetFrom, float targetTo)
        {
            return targetFrom + (source - sourceFrom) * (targetTo - targetFrom) / (sourceTo - sourceFrom);
        }

        internal static void ColorToHSV(Color color, out double hue, out double saturation, out double value)
        {
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));

            hue = color.GetHue();
            saturation = (max == 0) ? 0 : 1d - (1d * min / max);
            value = max / 255d;
        }

        internal static Color ColorFromHSV(double hue, double saturation, double value)
        {
            hue = hue % 360 / 60.0;
            var f = hue - (int)hue;
            value *= 255.0;

            var v = (int)value;
            var p = (int)(value * (1.0 - saturation));
            var q = (int)(value * (1.0 - f * saturation));
            var t = (int)(value * (1.0 - (1.0 - f) * saturation));

            return (int)hue switch
            {
                0 => Color.FromArgb(255, v, t, p),
                1 => Color.FromArgb(255, q, v, p),
                2 => Color.FromArgb(255, p, v, t),
                3 => Color.FromArgb(255, p, q, v),
                4 => Color.FromArgb(255, t, p, v),
                _ => Color.FromArgb(255, v, p, q)
            };
        }

        internal static Point ClipPointToRect(Point point, Rectangle rectangle)
        {
            if (point.X < 0) { point.X = 0; }
            if (point.Y < 0) { point.Y = 0; }
            if (point.X > rectangle.Width) { point.X = rectangle.Width; }
            if (point.Y > rectangle.Height) { point.Y = rectangle.Height; }

            return point;
        }

        private void BetterColorPicker_Load(object sender, EventArgs e)
        {
            DesktopLocation = Cursor.Position;
        }
    }

    abstract class PickerBasePanel : Panel
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
            Justification = "This class can only exist as a parent of BetterColorPicker")]
        internal BetterColorPicker? BetterColorPicker;
        internal Bitmap? RenderImage;

        public PickerBasePanel()
        {
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            if (Parent is BetterColorPicker picker)
            {
                BetterColorPicker = picker;
            }
            else
            {
                BetterColorPicker = Parent!.FindForm()! as BetterColorPicker;
            }
        }
    }

    internal class ColorPickerPanel : PickerBasePanel
    {
        private bool MouseIsDown;

        public ColorPickerPanel()
        {
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            MouseIsDown = true;
            SampleColor(new Point(e.X, e.Y));
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            MouseIsDown = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (MouseIsDown)
            {
                SampleColor(BetterColorPicker.ClipPointToRect(new Point(e.X, e.Y), ClientRectangle));
            }
            base.OnMouseMove(e);
        }

        public void SampleColor(Point samplePos)
        {
            if (!DesignMode)
            {
                if (BetterColorPicker == null)
                {
                    return;
                }

                BetterColorPicker.S = samplePos.X / (float)Width;
                BetterColorPicker.V = 1 - samplePos.Y / (float)Height;
                BetterColorPicker.UpdateAllControls();
                BetterColorPicker.UpdateTextBoxes();
            }
        }


        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var width = Bounds.Width;
            var height = Bounds.Height;

            double h = 0;
            double s = 0;
            double v = 0;

            if (BetterColorPicker != null)
            {
                h = BetterColorPicker.H;
                s = BetterColorPicker.S;
                v = BetterColorPicker.V;
            }


            if (RenderImage == null || RenderImage.Width != width || RenderImage.Height != height)
            {
                RenderImage = new Bitmap(width, height);
            }

            var bmpData = RenderImage.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var ptr = bmpData.Scan0;

            unsafe
            {
                var pixelPtr = (byte*)ptr;
                for (var y = 0; y < height; y++)
                {
                    var y_t = (double)y / (height - 1);

                    for (var x = 0; x < width; x++)
                    {
                        var x_t = (double)x / (width - 1);

                        var color = BetterColorPicker.ColorFromHSV(h, x_t, 1 - y_t);

                        var position = y * bmpData.Stride + x * 4;
                        pixelPtr[position] = color.B;
                        pixelPtr[position + 1] = color.G;
                        pixelPtr[position + 2] = color.R;
                        pixelPtr[position + 3] = color.A;
                    }
                }
                ;
            }

            RenderImage.UnlockBits(bmpData);

            using var pen = new Pen(Color.FromArgb(BetterColorPicker.ColorFromHSV(h, s, v).ToArgb() ^ 0xffffff), 2);

            e.Graphics.DrawImage(RenderImage, ClientRectangle);
            var circleRadius = Themer.AdjustForDPI(this, 10);
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawEllipse(pen, new Rectangle((int)(s * Width) - circleRadius / 2, (int)((1.0f - v) * Height) - circleRadius / 2, circleRadius, circleRadius));
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            Cursor = Cursors.Cross;

            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Cursor = Cursors.Arrow;

            base.OnMouseLeave(e);
        }
    }

    internal class ColorPickerHuePanel : PickerBasePanel
    {
        private bool Clicked;

        public ColorPickerHuePanel()
        {
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        public void UpdateSelectedHue(MouseEventArgs e)
        {
            if (DesignMode) { return; }

            var mouseYPos = BetterColorPicker.ClipPointToRect(new Point(e.X, e.Y), ClientRectangle).Y;

            if (mouseYPos > Height)
            {
                mouseYPos = Height;
            }

            if (mouseYPos < 0)
            {
                mouseYPos = 0;
            }


            if (!DesignMode && BetterColorPicker != null)
            {
                BetterColorPicker.H = (double)mouseYPos / Height * 360f;
                BetterColorPicker.UpdateAllControls();
                BetterColorPicker.UpdateTextBoxes();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var width = Bounds.Width;
            var height = Bounds.Height;

            double h = 0;
            if (BetterColorPicker != null)
            {
                h = BetterColorPicker.H;
            }

            if (RenderImage == null || RenderImage.Width != width || RenderImage.Height != height)
            {
                RenderImage = new Bitmap(width, height);
            }

            var bmpData = RenderImage.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var ptr = bmpData.Scan0;
            var bytes = Math.Abs(bmpData.Stride) * height;

            unsafe
            {
                var pixelPtr = (byte*)ptr;
                for (var y = 0; y < height; y++)
                {
                    var hue = 360.0 * y / (height - 1);
                    var color = BetterColorPicker.ColorFromHSV(hue, 1, 1);

                    for (var x = 0; x < width; x++)
                    {
                        var position = y * bmpData.Stride + x * 4;
                        pixelPtr[position] = color.B;
                        pixelPtr[position + 1] = color.G;
                        pixelPtr[position + 2] = color.R;
                        pixelPtr[position + 3] = color.A;
                    }
                }
                ;
            }

            RenderImage.UnlockBits(bmpData);

            e.Graphics.DrawImage(RenderImage, ClientRectangle);

            using var pen = new Pen(Color.FromArgb(BetterColorPicker.ColorFromHSV(h, 1, 1).ToArgb() ^ 0xffffff), Themer.AdjustForDPI(this, 2));

            var selectorHeight = Themer.AdjustForDPI(this, 4);
            e.Graphics.DrawRectangle(pen, new Rectangle(ClientRectangle.X, (int)(h / 360 * Height - selectorHeight / 2), ClientRectangle.Width, selectorHeight));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (Clicked)
            {
                UpdateSelectedHue(e);
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Clicked = true;
            UpdateSelectedHue(e);

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Clicked = false;

            Cursor.Show();
            Cursor.Clip = Rectangle.Empty;

            base.OnMouseUp(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            Cursor = Cursors.Cross;
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Cursor = Cursors.Arrow;
            base.OnMouseLeave(e);
        }
    }

    internal class HSVSlider : PickerBasePanel
    {
        public Color KnobColor = Color.White;
        public ColorSliderType SliderType = ColorSliderType.H;
        public float Value;

        private bool Clicked;

        public enum ColorSliderType
        {
            H,
            S,
            V
        }

        internal void SetValue(float value)
        {
            Value = value;

            if (BetterColorPicker == null)
            {
                return;
            }

            switch (SliderType)
            {
                case ColorSliderType.H:
                    BetterColorPicker.H = Value;
                    break;
                case ColorSliderType.S:
                    BetterColorPicker.S = Value;
                    break;
                case ColorSliderType.V:
                    BetterColorPicker.V = Value;
                    break;
            }

            if (BetterColorPicker!.NonPaintRefresh)
            {
                return;
            }

            BetterColorPicker.UpdateAllControls(false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var width = Bounds.Width;
            var height = Bounds.Height;

            double h = 0;
            double s = 1;
            double v = 1;

            if (!DesignMode && BetterColorPicker != null)
            {
                h = BetterColorPicker.H;
                s = BetterColorPicker.S;
                v = BetterColorPicker.V;
            }

            if (RenderImage == null || RenderImage.Width != width || RenderImage.Height != height)
            {
                RenderImage = new Bitmap(width, height);
            }

            var bmpData = RenderImage.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var ptr = bmpData.Scan0;

            unsafe
            {
                var pixelPtr = (byte*)ptr;

                for (var x = 0; x < width; x++)
                {
                    var normalizedX = (float)x / width;
                    var color = SliderType switch
                    {
                        ColorSliderType.H => BetterColorPicker.ColorFromHSV(normalizedX * 360f, s, v),
                        ColorSliderType.S => BetterColorPicker.ColorFromHSV(h, normalizedX, v),
                        ColorSliderType.V => BetterColorPicker.ColorFromHSV(h, s, normalizedX),
                        _ => Color.Red,
                    };
                    var colorB = color.B;
                    var colorG = color.G;
                    var colorR = color.R;
                    var colorA = color.A;

                    for (var y = 0; y < height; y++)
                    {
                        var position = y * bmpData.Stride + x * 4;
                        pixelPtr[position] = colorB;
                        pixelPtr[position + 1] = colorG;
                        pixelPtr[position + 2] = colorR;
                        pixelPtr[position + 3] = colorA;

                    }
                }
                ;
            }

            RenderImage.UnlockBits(bmpData);

            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var sliderHeight = height / 2;
            var sliderY = (height - sliderHeight) / 2;
            e.Graphics.Clear(BackColor);
            e.Graphics.DrawImage(RenderImage, new Rectangle(0, sliderY, width, sliderHeight));

            var knobSize = height - Themer.AdjustForDPI(this, 4);
            var knobX = SliderType switch
            {
                ColorSliderType.H => (float)(h / 360f),
                ColorSliderType.S => (float)s,
                ColorSliderType.V => (float)v,
                _ => 0
            };

            float knobY = (height - knobSize) / 2;

            KnobColor = BetterColorPicker.ColorFromHSV(h, s, v);

            using var knobBrush = new SolidBrush(KnobColor);
            using var knobPen = new Pen(Color.FromArgb(KnobColor.ToArgb() ^ 0xffffff), Themer.AdjustForDPI(this, 2));

            var knobXTransformed = knobX * width - knobSize / 2;

            e.Graphics.FillEllipse(knobBrush, knobXTransformed, knobY, knobSize, knobSize);
            e.Graphics.DrawEllipse(knobPen, knobXTransformed, knobY, knobSize, knobSize);
        }

        public void CalcPos(MouseEventArgs? e = null)
        {
            if (e != null)
            {
                if (!Clicked)
                {
                    return;
                }

                if (DesignMode) { return; }

                var mousePos = BetterColorPicker.ClipPointToRect(new Point(e.X, e.Y), ClientRectangle);

                if (BetterColorPicker == null)
                {
                    return;
                }

                var distance = mousePos.X / (float)Width;

                if (SliderType == ColorSliderType.H)
                {
                    BetterColorPicker.H = distance * 360f;
                }
                else if (SliderType == ColorSliderType.S)
                {
                    BetterColorPicker.S = distance;
                }
                else if (SliderType == ColorSliderType.V)
                {
                    BetterColorPicker.V = distance;
                }

                BetterColorPicker.UpdateAllControls();
                BetterColorPicker.UpdateTextBoxes();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            CalcPos(e);
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Clicked = true;
            CalcPos(e);

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Clicked = false;

            Cursor.Clip = Rectangle.Empty;
            Cursor.Show();

            base.OnMouseUp(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);

            Cursor = Cursors.Hand;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            Cursor = Cursors.Default;
        }
    }
}
