using System.Drawing;
using System.Linq;
using System.Text;
using NodeGraphControl;
using NodeGraphControl.Elements;

namespace TestProject.Nodes {
    public class NodeStringXOR : AbstractNode {
        private readonly SocketIn _inputString;
        private readonly SocketIn _inputKey;
        private readonly SocketIn _inputEnabled;
        private readonly SocketOut _outputString;

        private const string DefaultOutputValue = "";

        public NodeStringXOR() : this(new Point(0, 0)) {
        }

        public NodeStringXOR(Point location) {
            Location = location;

            _inputString = new SocketIn(typeof(string), "Input string", this, false);
            _inputKey = new SocketIn(typeof(char), "Input key", this, false);
            _inputEnabled = new SocketIn(typeof(bool), "Input enabled", this, false);
            _outputString = new SocketOut(typeof(string), "Output string", this);

            Name = "String XOR";
            NodeType = GetType().ToString().Split('.').Last();
            Description = "This node encodes string on input with XOR function.";
            BaseColor = Color.FromArgb(CommonStates.NodeColorAlpha, 31, 36, 42);
            HeaderColor = Color.FromArgb(61, 115, 99);

            _outputString.UpdateValue(DefaultOutputValue);

            Sockets.Add(_inputString);
            Sockets.Add(_inputKey);
            Sockets.Add(_inputEnabled);
            Sockets.Add(_outputString);
        }

        public override bool IsReady() {
            // return !String.IsNullOrEmpty(inputString.Value);
            return true;
        }

        public override void Execute() {
            var inputString = _inputString.GetSingleValue() as string;
            var inputKey = _inputKey.GetSingleValue() is char ? (char) _inputKey.GetSingleValue() : '\0';
            var inputEnabled = _inputEnabled.GetSingleValue();

            if (inputEnabled == null || (bool) inputEnabled == false) {
                _outputString.UpdateValue(DefaultOutputValue);
                return;
            }

            if (string.IsNullOrEmpty(inputString)) {
                _outputString.UpdateValue(DefaultOutputValue);
            } else {
                var result = new StringBuilder();

                foreach (var c in inputString)
                    result.Append((char) (c ^ (uint) inputKey));

                _outputString.UpdateValue(result.ToString());
            }
        }
    }
}