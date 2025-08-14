using System;
using System.Drawing;
using System.Linq;
using NodeGraphControl;
using NodeGraphControl.Elements;

namespace TestProject.Nodes.MathNodes {
    public class MathAvgNode : AbstractNode {
        private readonly SocketIn _numberHub;
        
        private readonly SocketOut _result;
        
        // empty constructor (optional)
        public MathAvgNode() : this(new Point(0, 0)) {
        }

        // main constructor
        public MathAvgNode(Point location) {
            Location = location;

            _numberHub = new SocketIn(typeof(double), "Numbers", this, true);
            _result = new SocketOut(typeof(double), "Result", this);

            Name = "Number Average";
            NodeType = GetType().ToString().Split('.').Last();
            Description = "This node calculates the average of the numbers on the input.";
            BaseColor = Color.FromArgb(CommonStates.NodeColorAlpha, 31, 36, 42);
            HeaderColor = Color.FromArgb(62, 88, 140);
            
            _result.UpdateValue(0d);

            Sockets.Add(_numberHub);
            Sockets.Add(_result);
        }
        
        public override bool IsReady() {
            return true;
        }

        public override void Execute() {
            var numbers = _numberHub.GetValues();

            if (numbers.Count == 0) {
                _result.UpdateValue(0d);
                return;
            }

            var count = 0;
            double sum = 0;

            foreach (var num in numbers) {
                sum += (double) num;
                count++;
            }

            double avg = (double)sum / count;
            _result.UpdateValue(avg);
        }
    }
}