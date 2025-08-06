using System;
using System.Drawing;
using System.Linq;

namespace NodeGraphControl.Elements {
    public abstract class AbstractSocket : IElement {
        public string SocketName { get; protected set; }

        public string SocketDescription { get; set; }

        public readonly Type ValueType;

        public string ValueTypeStr;

        protected readonly AbstractNode Owner;

        private RectangleF _bounds;

        public RectangleF BoundsFull {
            get => _bounds;
            set => _bounds = value;
        }

        public PointF Pivot { get; set; }

        public AbstractSocket(Type valueType, string socketName, AbstractNode owner) {
            ValueType = valueType;
            SocketName = socketName;
            Owner = owner;
            ValueTypeStr = ValueType.ToString().Split('.').Last();
        }

        public abstract void Connect(Wire wire);
        
        public abstract void DisconnectAll();

        public abstract void Disconnect(Wire wire);

        public abstract bool IsConnected();

        public abstract bool ContainsConnection(Wire wire);
    }
}