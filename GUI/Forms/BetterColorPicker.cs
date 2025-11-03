using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;
using System.Drawing;

namespace GUI.Forms
{
    public partial class BetterColorPicker : Form
    {
        public class ColorChangedEventArgs : EventArgs
        {
            public Color Color { get; private set; }

            public ColorChangedEventArgs(Color color)
            {
                this.Color = color;
            }
        }

        public delegate void ColorChangedEventHandler(object sender, ColorChangedEventArgs e);
        public event ColorChangedEventHandler? ColorChanged;

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            // making this resizeable is an absolutele nightmare, keep it fixed but adjust its size based on DPI to be same on screen.
            Size = new Size(AdjustForDpi(this, 300), AdjustForDpi(this, 500));
        }

        internal double H;
        internal double S;
        internal double V;
        internal bool NonPaintRefresh;

        private Color OldPickerColor;

        public Color PickedColor { get; private set; } = Color.White;
        private Color OldColor = Color.White;

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Cursor = Cursors.Default;
        }

        public BetterColorPicker(Color startColor)
        {
            InitializeComponent();

            ColorToHSV(startColor, out H, out S, out V);

            HSlider.SliderType = HSVSlider.ColorSliderType.H;
            HSliderValueInput.CustomTextChanged += (object? sender, EventArgs e) => { HSlider.SetValue(HSliderValueInput.Value * 360); };

            SSlider.SliderType = HSVSlider.ColorSliderType.S;
            SSliderValueInput.CustomTextChanged += (object? sender, EventArgs e) => { SSlider.SetValue(SSliderValueInput.Value); };

            VSlider.SliderType = HSVSlider.ColorSliderType.V;
            VSliderValueInput.CustomTextChanged += (object? sender, EventArgs e) => { VSlider.SetValue(VSliderValueInput.Value); };

            OldColorPanel.BackColor = startColor;
            NewColorPanel.BackColor = startColor;

            OldColor = startColor;

            OK.Select();

            DoubleBuffered = true;

            UpdateAllControls();
            UpdateTextBoxes();
        }

        public void UpdateAllControls(bool updateSliderValue = true)
        {
            SuspendLayout();

            HSlider.Invalidate();
            SSlider.Invalidate();
            SSlider.Invalidate();
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
                ColorChanged?.Invoke(this, new ColorChangedEventArgs(PickedColor));
            }

            OldPickerColor = PickedColor;
            ResumeLayout();
            Refresh();
        }

        public void UpdateTextBoxes()
        {
            HexTextBox.SetTextWithoutCustomEvent(ColorTranslator.ToHtml(PickedColor).Remove(0, 1));

            ColorTextBoxR.SetTextWithoutCustomEvent(PickedColor.R.ToString(CultureInfo.InvariantCulture));
            ColorTextBoxG.SetTextWithoutCustomEvent(PickedColor.G.ToString(CultureInfo.InvariantCulture));
            ColorTextBoxB.SetTextWithoutCustomEvent(PickedColor.B.ToString(CultureInfo.InvariantCulture));
        }

        private void OK_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            PickedColor = ColorFromHSV(H, S, V);
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
                ColorChanged?.Invoke(this, new ColorChangedEventArgs(PickedColor));
            }

            base.OnFormClosing(e);
        }

        private static Color GetColorAt(Point location)
        {
            using (Bitmap pixelContainer = new Bitmap(1, 1))
            {
                using (Graphics g = Graphics.FromImage(pixelContainer))
                {
                    g.CopyFromScreen(location, Point.Empty, pixelContainer.Size);
                }

                return pixelContainer.GetPixel(0, 0);
            }
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
                //EyedropperButton.ForceClicked = false;
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

                this.Invoke(() =>
                {
                    HexTextBox.Text = hexValue;
                });
            };

            form.Show();
        }

        private void CloseColorPicker(Form picker, Color color)
        {
            this.Invoke(() => { HexTextBox.Text = ColorTranslator.ToHtml(color); });
            picker.Close();
        }

        internal static int AdjustForDpi(Control control, int val)
        {
            return (int)(val * control.DeviceDpi / 96f);
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

            int v = (int)value;
            int p = (int)(value * (1.0 - saturation));
            int q = (int)(value * (1.0 - f * saturation));
            int t = (int)(value * (1.0 - (1.0 - f) * saturation));

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

            if (Parent is BetterColorPicker)
            {
                BetterColorPicker = (BetterColorPicker)Parent;
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

            BitmapData bmpData = RenderImage.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            IntPtr ptr = bmpData.Scan0;

            unsafe
            {
                byte* pixelPtr = (byte*)ptr;
                for (int y = 0; y < height; y++)
                {
                    double y_t = (double)y / (height - 1);

                    for (int x = 0; x < width; x++)
                    {
                        double x_t = (double)x / (width - 1);

                        var color = BetterColorPicker.ColorFromHSV(h, x_t, 1 - y_t);

                        int position = y * bmpData.Stride + x * 4;
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
            var circleRadius = BetterColorPicker.AdjustForDpi(this, 10);
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

            int width = Bounds.Width;
            int height = Bounds.Height;

            double h = 0;
            if (BetterColorPicker != null)
            {
                h = BetterColorPicker.H;
            }

            if (RenderImage == null || RenderImage.Width != width || RenderImage.Height != height)
            {
                RenderImage = new Bitmap(width, height);
            }

            BitmapData bmpData = RenderImage.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            IntPtr ptr = bmpData.Scan0;
            int bytes = Math.Abs(bmpData.Stride) * height;

            unsafe
            {
                byte* pixelPtr = (byte*)ptr;
                for (int y = 0; y < height; y++)
                {
                    double hue = 360.0 * y / (height - 1);
                    var color = BetterColorPicker.ColorFromHSV(hue, 1, 1);

                    for (int x = 0; x < width; x++)
                    {
                        int position = y * bmpData.Stride + x * 4;
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

            using var pen = new Pen(Color.FromArgb(BetterColorPicker.ColorFromHSV(h, 1, 1).ToArgb() ^ 0xffffff), BetterColorPicker.AdjustForDpi(this, 2));

            var selectorHeight = BetterColorPicker.AdjustForDpi(this, 4);
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

            int width = Bounds.Width;
            int height = Bounds.Height;

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

            BitmapData bmpData = RenderImage.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            IntPtr ptr = bmpData.Scan0;

            unsafe
            {
                byte* pixelPtr = (byte*)ptr;

                for (int x = 0; x < width; x++)
                {
                    Color color;
                    float normalizedX = (float)x / width;

                    switch (SliderType)
                    {
                        case ColorSliderType.H:
                            color = BetterColorPicker.ColorFromHSV(normalizedX * 360f, s, v);
                            break;
                        case ColorSliderType.S:
                            color = BetterColorPicker.ColorFromHSV(h, normalizedX, v);
                            break;
                        case ColorSliderType.V:
                            color = BetterColorPicker.ColorFromHSV(h, s, normalizedX);
                            break;
                        default:
                            color = Color.Red;
                            break;
                    }

                    byte colorB = color.B;
                    byte colorG = color.G;
                    byte colorR = color.R;
                    byte colorA = color.A;

                    for (int y = 0; y < height; y++)
                    {
                        int position = y * bmpData.Stride + x * 4;
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

            int sliderHeight = height / 2;
            int sliderY = (height - sliderHeight) / 2;
            e.Graphics.Clear(BackColor);
            e.Graphics.DrawImage(RenderImage, new Rectangle(0, sliderY, width, sliderHeight));

            int knobSize = height - BetterColorPicker.AdjustForDpi(this, 4);
            float knobX = SliderType switch
            {
                ColorSliderType.H => (float)(h / 360f),
                ColorSliderType.S => (float)s,
                ColorSliderType.V => (float)v,
                _ => 0
            };

            float knobY = (height - knobSize) / 2;

            KnobColor = BetterColorPicker.ColorFromHSV(h, s, v);

            using SolidBrush knobBrush = new SolidBrush(KnobColor);
            using Pen knobPen = new Pen(Color.FromArgb(KnobColor.ToArgb() ^ 0xffffff), BetterColorPicker.AdjustForDpi(this, 2));

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

                Point mousePos = BetterColorPicker.ClipPointToRect(new Point(e.X, e.Y), ClientRectangle);

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
