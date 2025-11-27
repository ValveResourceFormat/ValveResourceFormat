using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace GUI.Controls
{
    public class BetterAbstractNumeric<T> : ThemedTextBox
    {
        private T? _value;
        private string _oldText = "";
        private T? _valueWhenMiddlePressed;
        private Vector2 _mousePosWhenPressed = Vector2.Zero;
        private float _distanceDragged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public float DragScale { get; set; } = 1.0f;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public T? MaxValue { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public T? MinValue { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public int DecimalMax { get; set; } = 4;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public T? Value
        {
            get => _value;
            set
            {
                _value = value;
                Text = _value?.ToString() ?? "";
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool MiddleMouseDown { get; private set; }

        public BetterAbstractNumeric()
        {
            ResizeRedraw = true;
        }

        private void UpdateValue(bool updateOnly = false)
        {
            var oldValue = Value;

            try
            {
                if (typeof(T) == typeof(int))
                {
                    UpdateIntValue(updateOnly);
                }
                else if (typeof(T) == typeof(float))
                {
                    UpdateFloatValue(updateOnly);
                }
                else if (typeof(T) == typeof(Color))
                {
                    UpdateColorValue(updateOnly);
                }
            }
            catch (Exception)
            {
                if (!updateOnly)
                {
                    Text = _oldText;
                    _value = oldValue;
                }
            }
        }

        private void UpdateIntValue(bool updateOnly)
        {
            var parsedValue = int.Parse(Text, CultureInfo.InvariantCulture);
            _oldText = Text;

            parsedValue = ClampValue(parsedValue,
                MinValue != null ? Convert.ToInt32(MinValue) : int.MinValue,
                MaxValue != null ? Convert.ToInt32(MaxValue) : int.MaxValue);

            if (!updateOnly && parsedValue.ToString() != Text)
            {
                SetTextWithoutCustomEvent(parsedValue.ToString());
            }

            _value = (T)Convert.ChangeType(parsedValue, typeof(T));
        }

        private void UpdateFloatValue(bool updateOnly)
        {
            var parsedValue = float.Parse(Text, CultureInfo.InvariantCulture);
            _oldText = Text;

            parsedValue = ClampValue(parsedValue,
                MinValue != null ? Convert.ToSingle(MinValue) : float.MinValue,
                MaxValue != null ? Convert.ToSingle(MaxValue) : float.MaxValue);

            if (DecimalMax > 0)
            {
                parsedValue = (float)Math.Round(parsedValue, DecimalMax);
            }

            if (!updateOnly && parsedValue.ToString(CultureInfo.InvariantCulture) != Text)
            {
                SetTextWithoutCustomEvent(parsedValue.ToString(CultureInfo.InvariantCulture));
            }

            _value = (T)Convert.ChangeType(parsedValue, typeof(T));
        }

        private void UpdateColorValue(bool updateOnly)
        {
            Text = Text.Replace("#", string.Empty);

            if (Text.Length > 6)
            {
                Text = _oldText;
                return;
            }

            var parsedValue = ColorTranslator.FromHtml("#" + Text);
            _oldText = Text;
            _value = (T)Convert.ChangeType(parsedValue, typeof(T));
        }

        private static TNum ClampValue<TNum>(TNum value, TNum min, TNum max) where TNum : IComparable<TNum>
        {
            if (value.CompareTo(min) < 0) return min;
            if (value.CompareTo(max) > 0) return max;
            return value;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (MiddleMouseDown && _valueWhenMiddlePressed != null)
            {
                _distanceDragged = _mousePosWhenPressed.X - e.X;
                UpdateDraggedValue();
            }

            base.OnMouseMove(e);
        }

        private void UpdateDraggedValue()
        {
            if (typeof(T) == typeof(int))
            {
                var baseValue = Convert.ToInt32(_valueWhenMiddlePressed);
                var newValue = baseValue - (int)(_distanceDragged * DragScale);
                Text = ClampValue(newValue, Convert.ToInt32(MinValue), Convert.ToInt32(MaxValue)).ToString(CultureInfo.InvariantCulture);
            }
            else if (typeof(T) == typeof(float))
            {
                var baseValue = Convert.ToSingle(_valueWhenMiddlePressed);
                var newValue = baseValue - (_distanceDragged * 0.0025f * DragScale);
                Text = ClampValue(newValue, Convert.ToSingle(MinValue), Convert.ToSingle(MaxValue)).ToString(CultureInfo.InvariantCulture);
            }

            UpdateValue();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            UpdateValue(updateOnly: true);
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                _mousePosWhenPressed = new Vector2(e.X, e.Y);
                MiddleMouseDown = true;
                _valueWhenMiddlePressed = Value;
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Middle)
            {
                MiddleMouseDown = false;
            }

            base.OnMouseUp(mevent);
        }
    }
}
