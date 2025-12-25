using System.Linq;

#nullable disable

namespace GUI.Types.Graphs
{
    public class SocketOut : AbstractSocket
    {
        private readonly List<Wire> OutputConnections = [];

        public SocketOut(Type valueType, string name, AbstractNode owner) : base(valueType, name, owner)
        {
        }

        public override void Connect(Wire wire)
        {
            if (OutputConnections.Any(connection => wire.To == connection.To))
            {
                throw new InvalidOperationException("Connection already exists");
            }
            else
            {
                OutputConnections.Add(wire);
            }
        }

        public override bool IsConnected()
        {
            return OutputConnections.Count > 0;
        }

        public override IReadOnlyList<Wire> Connections => OutputConnections;
    }
}
