using NodeGraphControl.Elements;
using SkiaSharp;

#nullable disable
namespace NodeGraphControl
{
    public class Wire : NodeUIElement
    {
        public SKPath HitTestPath { get; set; }

        public SocketOut From { get; private set; }

        public SocketIn To { get; private set; }

        public Wire(SocketOut from, SocketIn to)
        {
            From = from;
            To = to;
        }
    }
}
