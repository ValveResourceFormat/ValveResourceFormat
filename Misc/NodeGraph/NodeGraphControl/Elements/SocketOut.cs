using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeGraphControl.Elements {
    public class SocketOut : AbstractSocket {
        private readonly List<Wire> OutputConnections = new List<Wire>();

        public SocketOut(Type valueType, string name, AbstractNode owner) : base(valueType, name, owner) {
        }
        
        public object Value { get; private set; }
        
        public void UpdateValue(object value) {
            if(value.Equals(Value))
                return;
            
            if (value != null && value.GetType() != ValueType) {
                throw new Exception("Incompatible Type: " + value.GetType() + " ! Expected: " + ValueType + ".");
            } else {
                Value = value;
                foreach (var connection in OutputConnections) {
                    connection.Flow();
                }
            }
        }

        public override void Connect(Wire wire) {
            if (OutputConnections.Any(connection => wire.To == connection.To)) {
                throw new Exception("Connection already exists");
            } else {
                OutputConnections.Add(wire);
            }
        }

        public override void DisconnectAll() {
            for (var i = OutputConnections.Count - 1; i >= 0; i--) {
                OutputConnections[i].Disconnect();
            }
        }

        public override void Disconnect(Wire wire) {
            OutputConnections.Remove(wire);
        }

        public override bool IsConnected() {
            return OutputConnections.Count > 0;
        }

        public override bool ContainsConnection(Wire wire) {
            return OutputConnections.Contains(wire);
        }
    }
}