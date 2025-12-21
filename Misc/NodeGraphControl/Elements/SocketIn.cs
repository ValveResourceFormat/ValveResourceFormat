using System;
using System.Collections.Generic;
using System.Linq;

#nullable disable
namespace NodeGraphControl.Elements
{
    public class SocketIn : AbstractSocket
    {

        private readonly Dictionary<Wire, object> InputConnections = new Dictionary<Wire, object>();

        public readonly bool Hub;

        public SocketIn(Type valueType, string name, AbstractNode owner, bool hub) : base(valueType, name, owner)
        {
            Hub = hub;
        }

        public void UpdateValue(Wire wire, object value)
        {
            if (InputConnections.ContainsKey(wire))
            {
                InputConnections[wire] = value;
                Owner.Execute();
            }
        }

        public override void Connect(Wire wire)
        {
            if (InputConnections.Any(connection => connection.Key.From == wire.From))
            {
                throw new Exception("Connection already exists");
            }

            if (Hub)
            {
                InputConnections.Add(wire, null);
            }
            else
            {
                if (IsConnected())
                {
                    InputConnections.Keys.ToList()[0].Disconnect();
                }
                InputConnections.Add(wire, null);
            }
        }

        public object GetSingleValue()
        {
            if (Hub)
                throw new Exception("GetSingleValue is invalid when Socket is Hub");

            if (InputConnections.Count == 0)
                return null;

            return InputConnections.Values.ToList()[0];
        }

        public List<object> GetValues()
        {
            return InputConnections.Values.ToList();
        }

        public override void DisconnectAll()
        {
            InputConnections.Clear();
            Owner.Execute();
        }

        public override void Disconnect(Wire wire)
        {
            InputConnections.Remove(wire);
            Owner.Execute();
        }

        public override bool IsConnected()
        {
            return InputConnections.Count > 0;
        }

        public override bool ContainsConnection(Wire wire)
        {
            return InputConnections.ContainsKey(wire);
        }

        public List<Wire> GetAllConnections()
        {
            return InputConnections.Keys.ToList();
        }
    }
}
