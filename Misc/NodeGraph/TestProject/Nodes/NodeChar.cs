using System.ComponentModel;
using System.Drawing;
using System.Linq;
using NodeGraphControl;
using NodeGraphControl.Elements;

namespace TestProject.Nodes {
    public class NodeChar : AbstractNode {
        private readonly SocketOut _outputChar;

        [Category("Settings")] public char DefaultOutputValue { get; set; } = 'F';

        public NodeChar() : this(new Point(0, 0)) {
        }

        public NodeChar(Point location) {
            StartNode = true;
            Location = location;

            _outputChar = new SocketOut(typeof(char), "Output char", this);

            Name = "Sample Char";
            NodeType = this.GetType().ToString().Split('.').Last();
            Description = "This node provides sample char: \"" + DefaultOutputValue + "\".";
            BaseColor = Color.FromArgb(CommonStates.NodeColorAlpha, 31, 36, 42);
            HeaderColor = Color.FromArgb(62, 88, 140);
            FooterHeight = 25;

            Sockets.Add(_outputChar);
        }

        public override bool IsReady() {
            return true;
        }

        public override void Execute() {
            _outputChar.UpdateValue(DefaultOutputValue);
        }

        public override void Draw(Graphics g) {
            base.Draw(g);

            var font = new Font(new FontFamily("Helvetica"), 10f, FontStyle.Regular);

            // g.SmoothingMode = SmoothingMode.HighQuality;

            var position = new PointF {
                X = Location.X + 3,
                Y = Location.Y + FullHeight - FooterHeight + 5
            };

            g.DrawString(DefaultOutputValue.ToString(), font, new SolidBrush(Color.MediumOrchid), position);
        }
    }
}