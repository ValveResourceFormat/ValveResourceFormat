using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Text;
using NodeGraphControl;
using NodeGraphControl.Elements;

namespace TestProject.Nodes {
    public class NodeMonitor : AbstractNode {
        private readonly SocketIn _inputObject;

        private const char WhiteChar = '░';
        private const char UnprintableChar = '☺';
        private const int MaxCharInLine = 23;

        private string _displayText = "";

        [Category("Monitor")]
        public string InputToString {
            get {
                return _inputObject.GetSingleValue().ToString();
            }
        }

        public NodeMonitor() : this(new Point(0, 0)) {
        }

        public NodeMonitor(Point location) {
            Location = location;

            _inputObject = new SocketIn(typeof(object), "Input", this, false);

            Name = "Monitor";
            NodeType = GetType().ToString().Split('.').Last();
            Description = "Monitor node.";
            BaseColor = Color.FromArgb(CommonStates.NodeColorAlpha, 31, 36, 42);
            HeaderColor = Color.FromArgb(168, 72, 88);
            FooterHeight = 50;

            Sockets.Add(_inputObject);
        }

        public override void Draw(Graphics g) {
            base.Draw(g);
            
            var font = new Font(new FontFamily(GenericFontFamilies.Monospace), 10f, FontStyle.Regular);

            // g.SmoothingMode = SmoothingMode.HighQuality;

            var position = new PointF {
                X = Location.X + 3,
                Y = Location.Y + FullHeight - FooterHeight + 5
            };

            if (_displayText.Length == 0)
                g.DrawString("<blank>", font, new SolidBrush(Color.DimGray), position);
            else
                g.DrawString(_displayText, font, new SolidBrush(Color.MediumOrchid), position);
        }

        public override bool IsReady() {
            return true;
        }

        public override void Execute() {
            var value = _inputObject.GetSingleValue();
            if (value != null) {
                EditText(value.ToString());
            } else {
                _displayText = "";
            }
            
            OnInvokeRepaint(EventArgs.Empty);
        }

        private void EditText(string text) {
            var sb = new StringBuilder();
            var charCount = 0;
            foreach (var c in text) {
                if (char.IsWhiteSpace(c)) {
                    sb.Append(WhiteChar);
                } else if (!char.IsControl(c)) {
                    sb.Append(c);
                } else {
                    sb.Append(UnprintableChar);
                }

                if (++charCount >= MaxCharInLine) {
                    sb.Append("\n");
                    charCount = 0;
                }
            }

            _displayText = sb.ToString();
        }
    }
}