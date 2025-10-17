using System.Drawing;
using System.Linq;
using NodeGraphControl;
using NodeGraphControl.Elements;

namespace TestProject.Nodes {
    public class NodeLogicOutput : AbstractNode {
        private readonly SocketOut _outputH;
        private readonly SocketOut _outputL;

        public NodeLogicOutput() : this(new Point(0, 0)) {
        }

        public NodeLogicOutput(Point location) {
            StartNode = true;
            Location = location;

            _outputH = new SocketOut(typeof(bool), "Output H", this);
            _outputL = new SocketOut(typeof(bool), "Output L", this);

            Name = "Logic Output";
            NodeType = this.GetType().ToString().Split('.').Last();
            Description = "This node provides logic state H or L.";
            BaseColor = Color.FromArgb(CommonStates.NodeColorAlpha, 31, 36, 42);
            HeaderColor = Color.FromArgb(66, 74, 84);

            Sockets.Add(_outputH);
            Sockets.Add(_outputL);
        }


        public override bool IsReady() {
            return true;
        }

        public override void Execute() {
            _outputH.UpdateValue(true);
            _outputL.UpdateValue(false);
        }
    }
}