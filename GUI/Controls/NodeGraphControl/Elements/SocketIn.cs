using System.Linq;

#nullable disable
namespace NodeGraphControl.Elements
{
    public class SocketIn : AbstractSocket
    {
        private readonly List<Wire> InputConnections = [];

        public bool Hub { get; init; }

        public SocketIn(Type valueType, string name, AbstractNode owner, bool hub) : base(valueType, name, owner)
        {
            Hub = hub;
        }

        public override void Connect(Wire wire)
        {
            if (InputConnections.Any(connection => connection.From == wire.From))
            {
                throw new InvalidOperationException("Connection already exists");
            }

            if (Hub)
            {
                InputConnections.Add(wire);
            }
            else
            {
                // Non-hub sockets can only have one connection - clear existing before adding new
                InputConnections.Clear();
                InputConnections.Add(wire);
            }
        }

        public override bool IsConnected()
        {
            return InputConnections.Count > 0;
        }
    }
}
