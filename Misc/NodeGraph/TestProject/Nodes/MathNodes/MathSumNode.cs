using System.Drawing;
using System.Linq;
using NodeGraphControl;
using NodeGraphControl.Elements;

namespace TestProject.Nodes.MathNodes {
    public class MathSumNode : AbstractNode {
        
        // input sockets
        private readonly SocketIn _numberA;
        private readonly SocketIn _numberB;
        
        // output socket
        private readonly SocketOut _resultOut;
        
        // empty constructor (optional)
        public MathSumNode() : this(new Point(0, 0)) {
        }

        // main constructor
        public MathSumNode(Point location) {
            Location = location;

            _numberA = new SocketIn(typeof(double), "Number A", this, false);
            _numberB = new SocketIn(typeof(double), "Number B", this, false);
            _resultOut = new SocketOut(typeof(double), "Result", this);

            Name = "Sample Node";
            NodeType = GetType().ToString().Split('.').Last();
            Description = "This node encodes string on input with XOR function.";
            BaseColor = Color.FromArgb(CommonStates.NodeColorAlpha, 31, 36, 42);
            HeaderColor = Color.FromArgb(62, 88, 140);

            _resultOut.UpdateValue(0d);

            Sockets.Add(_numberA);
            Sockets.Add(_numberB);
            Sockets.Add(_resultOut);
        }
        
        // without use right now
        public override bool IsReady() {
            return true;
        }

        // Execute method
        public override void Execute() {
            double valueA = _numberA.GetSingleValue() is double ? (double) _numberA.GetSingleValue() : 0;
            double valueB = _numberB.GetSingleValue() is double ? (double) _numberB.GetSingleValue() : 0;
            _resultOut.UpdateValue(valueA + valueB);
        }
    }
}