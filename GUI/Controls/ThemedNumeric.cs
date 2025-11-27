using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace GUI.Controls
{
    public abstract class ThemedAbstractNumeric<T> : ThemedTextBox
    {
        private T _value = default!;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public T Value
        {
            get => _value;
            set
            {
                _value = value;
                Text = ConvertToText(_value);
            }
        }

        protected ThemedAbstractNumeric()
        {
            ResizeRedraw = true;
        }

        protected abstract string ConvertToText(T value);
        protected abstract T Parse(string text);

        private void UpdateValue(bool internalUpdateOnly = false)
        {
            var val = Parse(Text);

            if (internalUpdateOnly)
            {
                _value = val;
            }
            else
            {
                Value = val;
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            UpdateValue(true);
            base.OnTextChanged(e);
            SelectionStart = Text.Length;
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            UpdateValue();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                UpdateValue();
            }

            base.OnKeyDown(e);
        }
    }

    public abstract class ThemedAbstractDragableNumeric<T> : ThemedAbstractNumeric<T>
    {
        internal static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax)
        {
            return (value - fromMin) / (fromMax - fromMin) * (toMax - toMin) + toMin;

        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public required T MinValue { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public required T MaxValue { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        // drag distance to go from min to max in pixels (adjusted by DPI)
        public int DragDistance { get; set; } = 300;

        private T ValueWhenMiddlePressed = default!;
        private Vector2 MousePosWhenPressed = Vector2.Zero;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool MiddleMouseDown { get; private set; }

        protected abstract T Clamp(T value);
        protected abstract T CalculateNewValueFromDelta(T value, float delta);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                MousePosWhenPressed = new Vector2(e.X, e.Y);
                MiddleMouseDown = true;
                ValueWhenMiddlePressed = Value;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Middle)
                MiddleMouseDown = false;

            base.OnMouseUp(mevent);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (MiddleMouseDown)
            {
                var delta = e.X - MousePosWhenPressed.X;

                var newValue = CalculateNewValueFromDelta(ValueWhenMiddlePressed, delta);
                Value = Clamp(newValue);
            }

            base.OnMouseMove(e);
        }
    }

    public class ThemedColorNumeric : ThemedAbstractNumeric<Color>
    {
        protected override string ConvertToText(Color value) => ColorTranslator.ToHtml(value).Replace("#", "");

        protected override Color Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Value;
            }

            text = text.TrimStart('#');

            if (text.Length < 6)
            {
                text = text.PadRight(6, '0');
            }

            text = "#" + text;

            var color = Value;

            try
            {
                color = ColorTranslator.FromHtml(text);
            }
            catch (Exception)
            {
            }

            return color;
        }
    }

    public class ThemedIntNumeric : ThemedAbstractDragableNumeric<int>
    {
        protected override string ConvertToText(int value) => value.ToString(CultureInfo.InvariantCulture);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public int Incrument { get; set; } = 1;

        protected override int Parse(string text)
        {
            try
            {
                return Clamp(int.Parse(text));
            }
            catch (Exception)
            {
            }

            return Value;
        }

        protected override int Clamp(int value)
        {
            int clamped = Math.Clamp(value, MinValue, MaxValue);

            int offset = clamped - MinValue;
            int snapped = MinValue + (int)Math.Round((double)offset / Incrument) * Incrument;

            return Math.Clamp(snapped, MinValue, MaxValue);
        }

        protected override int CalculateNewValueFromDelta(int value, float delta)
        {
            float deltaValue = Remap(delta, 0, DragDistance, 0, MaxValue - MinValue);
            float newValue = value + deltaValue;

            int steps = (int)Math.Round((newValue - MinValue) / Incrument);
            int snappedValue = MinValue + steps * Incrument;

            return Math.Clamp(snappedValue, MinValue, MaxValue);
        }
    }

    public class ThemedFloatNumeric : ThemedAbstractDragableNumeric<float>
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public int DecimalMax { get; set; } = 4;

        protected override string ConvertToText(float value) => Math.Round(value, DecimalMax).ToString(CultureInfo.InvariantCulture);

        protected override float Parse(string text)
        {
            try
            {
                return Clamp(float.Parse(text));
            }
            catch (Exception)
            {
            }

            return Value;
        }

        protected override float Clamp(float value)
        {
            return Math.Clamp(value, MinValue, MaxValue);
        }

        protected override float CalculateNewValueFromDelta(float value, float delta)
        {
            return value + Remap(delta, 0, DragDistance, MinValue, MaxValue - MinValue);
        }
    }
}
