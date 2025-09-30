using NodeGraphControl.Elements;
using SkiaSharp;

#nullable disable
namespace NodeGraphControl
{
    public class Wire : NodeUIElement
    {
        public SKPath HitTestPath { get; set; }

        private SocketOut _from;

        public SocketOut From
        {
            get => _from;
            set
            {
                if (value == null)
                {
                    _from = null;
                }
                else
                {
                    if (_to != null
                        && !_to.Hub
                        && value.ValueType != _to.ValueType
                        && !value.ValueType.IsSubclassOf(_to.ValueType))
                    {
                        throw new InvalidOperationException("Type mismatch!");
                    }

                    if (value.GetType() == typeof(SocketOut))
                    {
                        _from = value;
                    }
                    else
                    {
                        throw new InvalidOperationException("Can't connect output to output!");
                    }
                }
            }
        }

        private SocketIn _to;

        public SocketIn To
        {
            get => _to;
            set
            {
                if (value == null)
                {
                    _to = null;
                }
                else
                {
                    if (_from != null
                        && !value.Hub
                        && value.ValueType != _from.ValueType
                        && !_from.ValueType.IsSubclassOf(value.ValueType))
                    {
                        throw new InvalidOperationException("Type mismatch!");
                    }

                    if (value.GetType() == typeof(SocketIn))
                    {
                        _to = value;
                    }
                    else
                    {
                        throw new InvalidOperationException("Can't connect input to input!");
                    }
                }
            }
        }

        public Wire()
        {
        }

        public Wire(SocketOut from, SocketIn to)
        {
            From = from;
            To = to;
        }

        public void Flow()
        {
            _to.UpdateValue(this, _from.Value);
        }

        public void Disconnect()
        {
            _from.Disconnect(this);
            _to.Disconnect(this);
        }
    }
}
