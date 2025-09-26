using System.ComponentModel;
using System.Drawing;
using System.Linq;
using NodeGraphControl;
using NodeGraphControl.Elements;

namespace TestProject.Nodes {
    public class NodeString : AbstractNode {
        private readonly SocketOut _outputString;

        [Category("Settings")] public string DefaultOutputValue { get; set; } = "This is my very long Sample OUTPUT, just to be sure! 1234567890";

        public NodeString() : this(new Point(0, 0)) {
        }

        public NodeString(Point location) {
            StartNode = true;
            Location = location;

            _outputString = new SocketOut(typeof(string), "Output string", this);

            Name = "Sample String";
            NodeType = this.GetType().ToString().Split('.').Last();
            Description = "This node provides sample string: \"" + DefaultOutputValue + "\".";
            BaseColor = Color.FromArgb(CommonStates.NodeColorAlpha, 31, 36, 42);
            HeaderColor = Color.FromArgb(168, 72, 88);
            FooterHeight = 25;

            Sockets.Add(_outputString);
        }

        public override bool IsReady() {
            return true;
        }

        public override void Execute() {
            _outputString.UpdateValue(DefaultOutputValue);
        }

        public override void Draw(Graphics g) {
            base.Draw(g);

            var font = new Font(FontFamily.GenericMonospace, 10f, FontStyle.Regular);

            // g.SmoothingMode = SmoothingMode.HighQuality;

            var position = new PointF {
                X = Location.X + 3,
                Y = Location.Y + FullHeight - FooterHeight + 5
            };

            string trimStr;
            if (DefaultOutputValue.Length > 23)
                trimStr = DefaultOutputValue.Substring(0, 20) + "...";
            else
                trimStr = DefaultOutputValue;

            g.DrawString(trimStr, font, new SolidBrush(Color.MediumOrchid), position);
        }
    }
}