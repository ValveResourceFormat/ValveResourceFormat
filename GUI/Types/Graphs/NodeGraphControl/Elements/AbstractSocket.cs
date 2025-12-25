using SkiaSharp;

#nullable disable

namespace GUI.Types.Graphs
{
    public abstract class AbstractSocket : NodeUIElement
    {
        public string SocketName { get; protected set; }

        public Type ValueType { get; private set; }

        public bool DisplayOnly { get; set; }

        public AbstractNode Owner { get; private set; }

        private SKRect _bounds;

        public SKRect BoundsFull
        {
            get => _bounds;
            set => _bounds = value;
        }

        public SKPoint Pivot { get; set; }

        protected AbstractSocket(Type valueType, string socketName, AbstractNode owner)
        {
            ValueType = valueType;
            SocketName = socketName;
            Owner = owner;
        }

        public abstract void Connect(Wire wire);

        public abstract bool IsConnected();

        public abstract IReadOnlyList<Wire> Connections { get; }
    }
}
