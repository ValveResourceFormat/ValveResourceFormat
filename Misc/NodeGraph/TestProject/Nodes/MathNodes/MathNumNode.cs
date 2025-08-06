using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using NodeGraphControl;
using NodeGraphControl.Elements;

namespace TestProject.Nodes.MathNodes {
    public class MathNumNode : AbstractNode {
        private SocketOut _output;
        
        [Category("Settings")] public double DefaultOutputValue { get; set; } = 42d;

        // empty constructor (optional)
        public MathNumNode() : this(new Point(0, 0)) {
        }

        // main constructor
        public MathNumNode(Point location) {
            StartNode = true;
            Location = location;
            
            _output = new SocketOut(typeof(double), "Number out", this);

            Name = "Number Generator";
            NodeType = GetType().ToString().Split('.').Last();
            Description = "This node generates integer.";
            BaseColor = Color.FromArgb(CommonStates.NodeColorAlpha, 31, 36, 42);
            HeaderColor = Color.FromArgb(62, 88, 140);
            FooterHeight = 25f;
            
            _output.UpdateValue(DefaultOutputValue);
            
            Sockets.Add(_output);
        }
        
        public override bool IsReady() {
            return true;
        }

        public override void Execute() {
            _output.UpdateValue(DefaultOutputValue);
            
            OnInvokeRepaint(EventArgs.Empty);
        }
        
        public override void Draw(Graphics g) {
            base.Draw(g);

            var font = new Font(FontFamily.GenericMonospace, 10f, FontStyle.Regular);

            // g.SmoothingMode = SmoothingMode.HighQuality;

            var position = new PointF {
                X = Location.X + 3,
                Y = Location.Y + FullHeight - FooterHeight + 5
            };

            g.DrawString(DefaultOutputValue.ToString(), font, new SolidBrush(Color.MediumOrchid), position);
        }
    }
}