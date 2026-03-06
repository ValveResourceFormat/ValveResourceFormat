using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ValveResourceFormat.Renderer;

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
                OnValueChanged(EventArgs.Empty);
            }
        }

        public event EventHandler? ValueChanged;

        protected virtual void OnValueChanged(EventArgs e)
        {
            ValueChanged?.Invoke(this, e);
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
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        // makes it so dragging is the same distance no matter the values within the range
        // disable this if you want to have a range like min/max float
        public bool DragWithinRange { get; set; } = true;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public T? MinValue { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public T? MaxValue { get; set; }

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
        public int Increment { get; set; } = 1;

        // todo: tryparse
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
            var clamped = Math.Clamp(value, MinValue, MaxValue);
            var snapped = (int)Math.Round((double)clamped / Increment) * Increment;
            return Math.Clamp(snapped, MinValue, MaxValue);
        }

        protected override int CalculateNewValueFromDelta(int value, float delta)
        {
            if (DragWithinRange)
            {
                var deltaValue = MathUtils.RemapRange(delta, 0, DragDistance, 0, MaxValue - MinValue);
                var newValue = value + deltaValue;

                var steps = (int)Math.Round((newValue - MinValue) / Increment);
                var snappedValue = MinValue + steps * Increment;

                return Math.Clamp(snappedValue, MinValue, MaxValue);
            }
            else
            {
                var sensitivity = DragDistance / 10f;
                var steps = (int)Math.Round(delta / sensitivity);
                return value + (steps * Increment);
            }

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
            if (DragWithinRange)
            {
                return value + MathUtils.RemapRange(delta, 0, DragDistance, MinValue, MaxValue - MinValue);
            }
            else
            {
                var sensitivity = DragDistance / 10f;
                return value + (delta / sensitivity);
            }
        }
    }
}
