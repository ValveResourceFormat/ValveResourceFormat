using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NodeGraphControl;
using NodeGraphControl.Elements;

namespace TestProject.Nodes {
    public class NodeLogicOscillator : AbstractNode {
        private Timer _timer = new Timer();

        private readonly SocketOut _output;

        public int Interval {
            get { return _timer.Interval; }
            set {
                _timer.Interval = Math.Max(100, value);
            }
        }

        private bool _outputValue = false;

        public NodeLogicOscillator() : this(new Point(0, 0)) {
        }

        public NodeLogicOscillator(Point location) {
            StartNode = true;
            Location = location;

            _output = new SocketOut(typeof(bool), "Output", this);

            Name = "Logic Output";
            NodeType = GetType().ToString().Split('.').Last();
            Description = "DON'T USE THIS ONE. Takes focus from property grid.";
            BaseColor = Color.FromArgb(CommonStates.NodeColorAlpha, 31, 36, 42);
            HeaderColor = Color.FromArgb(66, 74, 84);
            FooterHeight = 20;

            _timer.Interval = 1000;
            _timer.Enabled = false;
            _timer.Tick += ((sender, args) => Time());

            Sockets.Add(_output);
        }

        public override void Draw(Graphics g) {
            base.Draw(g);

            var x = Location.X + 5f;
            var y = Location.Y + FullHeight - FooterHeight + 5f;

            g.FillEllipse(new SolidBrush((_outputValue) ? Color.Green : Color.Firebrick), x, y, 10, 10);
        }

        public override bool IsReady() {
            return true;
        }

        public override void Execute() {
            _timer.Enabled = true;
        }

        private void Time() {
            _outputValue = !_outputValue;
            _output.UpdateValue(_outputValue);
            
            OnInvokeRepaint(EventArgs.Empty);
        }
    }
}