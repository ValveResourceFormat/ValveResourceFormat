using System.Drawing;

#nullable disable
namespace NodeGraphControl.Elements
{
    public abstract class AbstractSocket : NodeUIElement
    {
        public string SocketName { get; protected set; }

        public string SocketDescription { get; set; }

        public Type ValueType { get; private set; }

        public string ValueTypeStr { get; private set; }

        public bool DisplayOnly { get; set; }

        protected AbstractNode Owner { get; private set; }

        private RectangleF _bounds;

        public RectangleF BoundsFull
        {
            get => _bounds;
            set => _bounds = value;
        }

        public PointF Pivot { get; set; }

        protected AbstractSocket(Type valueType, string socketName, AbstractNode owner)
        {
            ValueType = valueType;
            SocketName = socketName;
            Owner = owner;
            ValueTypeStr = ValueType.ToString().Split('.')[^1].Split('+')[^1];
        }

        public abstract void Connect(Wire wire);

        public abstract void DisconnectAll();

        public abstract void Disconnect(Wire wire);

        public abstract bool IsConnected();

        public abstract bool ContainsConnection(Wire wire);
    }
}
