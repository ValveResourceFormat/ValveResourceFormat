using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.Renderer;

namespace GUI.Controls;

internal class Slider : UserControl
{
    public Color SliderColor { get; set; }

    public int SliderHeight { get; set => field = this.AdjustForDPI(value); } = 6;
    public int KnobSize { get; set => field = this.AdjustForDPI(value); } = 14;

    public float Value { get; set { field = MathUtils.Saturate(value); Invalidate(); } }

    public Action<float>? ValueChanged;

    public bool Clicked { get; private set; }

    public Slider()
    {
        DoubleBuffered = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        BackColor = Parent?.BackColor ?? Themer.CurrentThemeColors.AppMiddle;
        SliderColor = Themer.CurrentThemeColors.Accent;
        ForeColor = Themer.CurrentThemeColors.Contrast;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var width = ClientRectangle.Width;

        e.Graphics.Clear(BackColor);

        var knobRadius = KnobSize / 2;

        var isLight = Themer.CurrentThemeColors.ColorMode == SystemColorMode.Classic;

        var sliderY = ClientRectangle.Top + (Height - SliderHeight) / 2;
        var sliderStartX = knobRadius;
        var sliderWidth = width - KnobSize;

        var knobCenterX = sliderStartX + Value * sliderWidth + knobRadius;
        var filledWidth = knobCenterX - sliderStartX;
        var unfilledWidth = sliderWidth - filledWidth;

        var activeSliderColor = Enabled ? SliderColor : ControlPaint.Dark(SliderColor, 0.5f);

        if (filledWidth > 0)
        {
            var filledRectangle = new Rectangle(sliderStartX, sliderY, (int)filledWidth, SliderHeight);
            using var filledBrush = new SolidBrush(activeSliderColor);
            e.Graphics.FillRectangle(filledBrush, filledRectangle);
        }

        if (unfilledWidth > 0)
        {
            var unfilledRectangle = new Rectangle((int)knobCenterX, sliderY, (int)unfilledWidth, SliderHeight);
            var unfilledColor = isLight ? ControlPaint.Light(activeSliderColor, 0.3f) : ControlPaint.Dark(activeSliderColor, 0.1f);
            using var unfilledBrush = new SolidBrush(unfilledColor);
            e.Graphics.FillRectangle(unfilledBrush, unfilledRectangle);
        }

        float knobY = (Height - KnobSize) / 2;

        using var knobBrush = new SolidBrush(activeSliderColor);

        var knobX = Value * (width - KnobSize);

        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        e.Graphics.FillEllipse(knobBrush, knobX, knobY, KnobSize, KnobSize);
    }

    public void CalcValue()
    {
        if (!Clicked)
        {
            return;
        }

        if (DesignMode) { return; }

        var mousePos = ClipPointToRect(PointToClient(Cursor.Position), ClientRectangle);

        var knobSize = (int)(Height / 1.5);
        var knobRadius = knobSize / 2;
        var penWidth = Themer.AdjustForDPI(this, 2);
        var halfPenWidth = (int)Math.Ceiling(penWidth / 2f);
        var effectiveWidth = Width - knobSize - halfPenWidth * 2;

        Value = MathUtils.Saturate((mousePos.X - knobRadius - halfPenWidth) / (float)effectiveWidth);

        ValueChanged?.Invoke(Value);
    }

    private static Point ClipPointToRect(Point point, Rectangle rectangle)
    {
        if (point.X < 0) { point.X = 0; }
        if (point.Y < 0) { point.Y = 0; }
        if (point.X > rectangle.Width) { point.X = rectangle.Width; }
        if (point.Y > rectangle.Height) { point.Y = rectangle.Height; }

        return point;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!Enabled)
        {
            return;
        }

        CalcValue();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!Enabled)
        {
            return;
        }

        Clicked = true;
        CalcValue();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!Enabled)
        {
            base.OnMouseUp(e);
            return;
        }

        Clicked = false;

        Cursor.Clip = Rectangle.Empty;
        Cursor.Show();

        base.OnMouseUp(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);

        if (!Enabled)
        {
            return;
        }

        Cursor = Cursors.Hand;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (!Enabled)
        {
            return;
        }

        Cursor = Cursors.Default;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }
}
