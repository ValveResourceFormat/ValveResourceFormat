using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

#nullable disable
namespace NodeGraphControl.Elements
{
    public enum NodeBoundsArea
    {
        HEADER,
        BASE,
        FOOTER,
        NONE
    }

    public abstract class AbstractNode : IElement
    {
        protected float NodeWidth = 200;

        public bool StartNode;

        public List<AbstractSocket> Sockets { get; } = new List<AbstractSocket>();

        protected const float HeaderHeight = 40;

        protected float MinBaseHeight { get; set; }

        private float _baseHeight;
        protected float FullHeight;
        private float _socketSplit;

        protected float FooterHeight;

        private const float SocketSize = 8;

        [Category("Location"), ReadOnly(true)] public Point Location { get; set; }

        [Category("Location")] public PointF Pivot { get; private set; }

        public string Name { get; set; }

        [ReadOnly(true)] public string NodeType { get; set; }
        public string Description { get; set; }

        protected Color HeaderColor { get; set; } // TODO add default color
        protected Color BaseColor { get; set; } // TODO add default color
        protected Color TextColor { get; set; }

        [Browsable(false)] public bool Selected { get; set; } = false;

        [Category("Bounds")] public RectangleF BoundsFull { get; private set; }
        [Category("Bounds")] public RectangleF BoundsHeader { get; private set; }
        [Category("Bounds")] public RectangleF BoundsBase { get; private set; }
        [Category("Bounds")] public RectangleF BoundsFooter { get; private set; }

        #region Events

        public event EventHandler InvokeRepaint;

        protected void OnInvokeRepaint(EventArgs e)
        {
            EventHandler handler = InvokeRepaint;
            handler?.Invoke(this, e);
        }

        #endregion

        #region Interface

        public abstract bool IsReady();

        public abstract void Execute();

        public AbstractSocket GetSocketByName(string name)
        {
            foreach (var socket in Sockets)
            {
                if (socket.SocketName == name)
                    return socket;
            }

            return null;
        }

        public NodeBoundsArea GetBoundsArea(PointF point)
        {
            if (BoundsHeader.Contains(point)) return NodeBoundsArea.HEADER;
            if (BoundsBase.Contains(point)) return NodeBoundsArea.BASE;
            if (BoundsFooter.Contains(point)) return NodeBoundsArea.FOOTER;

            return NodeBoundsArea.NONE;
        }

        public void Disconnect()
        {
            foreach (var socket in Sockets)
            {
                socket.DisconnectAll();
            }
        }

        #endregion

        #region CalculateDimensions

        public void Calculate()
        {
            if (MinBaseHeight == 0)
                MinBaseHeight = SocketSize * 4; // default minimum height

            var countIn = 0;
            var countOut = 0;

            foreach (var socket in Sockets)
            {
                if (socket.GetType() == typeof(SocketIn)) countIn++;
                if (socket.GetType() == typeof(SocketOut)) countOut++;
            }

            var socketsHeight = (SocketSize * 4) * Math.Max(countIn, countOut);

            _baseHeight = Math.Max(MinBaseHeight, socketsHeight);
            FullHeight = HeaderHeight + _baseHeight + FooterHeight;
            _socketSplit = _baseHeight / (Math.Max(countIn, countOut) + 1);

            BoundsFull = new RectangleF(Location.X, Location.Y, NodeWidth, FullHeight);
            BoundsHeader = new RectangleF(Location.X, Location.Y, NodeWidth, HeaderHeight);
            BoundsBase = new RectangleF(Location.X, Location.Y + HeaderHeight, NodeWidth, _baseHeight);
            BoundsFooter = new RectangleF(Location.X, Location.Y + HeaderHeight + _baseHeight, NodeWidth, FooterHeight);

            Pivot = new PointF(BoundsFull.X + BoundsFull.Width / 2f, BoundsFull.Y + BoundsFull.Height / 2f);

            countIn = 0;
            countOut = 0;

            foreach (var socket in Sockets)
            {
                if (socket.GetType() == typeof(SocketIn))
                {
                    countIn++;
                    socket.Pivot = new PointF(Location.X, Location.Y + HeaderHeight + _socketSplit * countIn);
                }

                if (socket.GetType() == typeof(SocketOut))
                {
                    countOut++;
                    socket.Pivot = new PointF(Location.X + NodeWidth, Location.Y + HeaderHeight + _socketSplit * countOut);
                }

                // update socket bounds
                socket.BoundsFull = new RectangleF(
                    socket.Pivot.X - SocketSize / 2 - 1f,
                    socket.Pivot.Y - SocketSize / 2 - 1f, SocketSize + 2, SocketSize + 2);
            }
        }

        #endregion

        #region Draw

        public virtual void Draw(Graphics g)
        {
            int cornerSize = CommonStates.CornerSize;
            var left = Location.X;
            var top = Location.Y;
            var right = Location.X + NodeWidth;
            var bottom = Location.Y + FullHeight;
            var bottomHeader = Location.Y + HeaderHeight;

            using var baseColorBrush = new SolidBrush(BaseColor);
            using var headerColorBrush = new SolidBrush(HeaderColor);

            using var headerTextBrush = new SolidBrush(Color.LightGray);
            using var headerTypeTextBrush = new SolidBrush(Color.Orange);
            using var penEmpty = new Pen(Color.Empty);

            // base
            using (var path = new GraphicsPath(FillMode.Winding))
            {
                path.AddArc(left, top, cornerSize, cornerSize, 180, 90);
                path.AddArc(right - cornerSize, top, cornerSize, cornerSize, 270, 90);

                path.AddArc(right - cornerSize, bottom - cornerSize, cornerSize, cornerSize, 0, 90);
                path.AddArc(left, bottom - cornerSize, cornerSize, cornerSize, 90, 90);
                path.CloseFigure();

                DropShadow(g, path);

                // g.SmoothingMode = SmoothingMode.HighQuality;
                g.FillPath(baseColorBrush, path);
                g.DrawPath(penEmpty, path);
            }

            // header
            using (var path = new GraphicsPath(FillMode.Winding))
            {
                path.AddArc(left, top, cornerSize, cornerSize, 180, 90);
                path.AddArc(right - cornerSize, top, cornerSize, cornerSize, 270, 90);
                path.AddLine(right, top + cornerSize, right, bottomHeader);
                path.AddLine(right, bottomHeader, left, bottomHeader);
                path.CloseFigure();

                // g.SmoothingMode = SmoothingMode.HighQuality;
                g.FillPath(headerColorBrush, path);
                g.DrawPath(penEmpty, path);

                float nodeTextOffset = 2f;
                float nodeTypePositionY = Location.Y + nodeTextOffset + 2;
                float nodeNamePositionY = Location.Y + HeaderHeight / 2 + nodeTextOffset;
                float nodeStringPositionX = Location.X + nodeTextOffset;

                g.DrawString(Name, HeaderFont, headerTextBrush, nodeStringPositionX, nodeTypePositionY);
                g.DrawString(NodeType, HeaderFont, headerTypeTextBrush, nodeStringPositionX, nodeNamePositionY);
            }

            // sockets
            DrawSockets(g);
        }

        #endregion

        #region DrawSockets

        private void DrawSockets(Graphics g)
        {
            // g.SmoothingMode = SmoothingMode.HighQuality;

            foreach (var socket in Sockets)
            {
                if (socket.GetType() == typeof(SocketIn))
                {
                    var socketIn = (SocketIn)socket;

                    if (!socketIn.DisplayOnly)
                    {
                        // draw socket
                        DrawSocket(
                            g,
                            socketIn.Pivot,
                            CommonStates.GetColorByType(socketIn.ValueType),
                            (socketIn.IsConnected()),
                            socketIn.Hub
                        );
                    }

                    // draw socket caption
                    DrawSocketCaption(g,
                        new PointF(socketIn.Pivot.X + (socketIn.DisplayOnly ? 0 : SocketSize), socketIn.Pivot.Y), socketIn, Alignment.Left
                    );
                }

                if (socket.GetType() == typeof(SocketOut))
                {
                    var socketOut = (SocketOut)socket;

                    // draw socket
                    DrawSocket(
                        g,
                        socketOut.Pivot,
                        CommonStates.GetColorByType(socketOut.ValueType),
                        (socketOut.IsConnected()),
                        false
                    );

                    // draw socket caption
                    DrawSocketCaption(g,
                        new PointF(socketOut.Pivot.X - SocketSize, socketOut.Pivot.Y), socketOut, Alignment.Right
                    );
                }
            }
        }

        private static void DrawSocket(Graphics g, PointF center, Color eColor, bool fill, bool hub)
        {
            using var ePen = new Pen(eColor, 1.8f);
            using var eBrush = new SolidBrush(eColor);

            var eX = center.X - SocketSize / 2f;
            var eY = center.Y - SocketSize / 2f;

            if (hub)
            {
                if (fill)
                    g.FillRectangle(eBrush, eX + 2, eY - 1, SocketSize - 2, SocketSize + 1);
                else
                    g.DrawRectangle(ePen, eX + 2, eY - 1, SocketSize - 2, SocketSize + 1);
            }
            else
            {
                if (fill)
                    g.FillEllipse(eBrush, eX, eY, SocketSize, SocketSize);
                else
                    g.DrawEllipse(ePen, eX, eY, SocketSize, SocketSize);
            }
        }

        private enum Alignment
        {
            Left,
            Right,
            Center // just in case... why not have an option to align to center
        }

        public static readonly Font SocketCaptionFont = new Font(new FontFamily("Helvetica"), 10f, FontStyle.Bold);
        public static readonly Font HeaderFont = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Bold);

        private void DrawSocketCaption(Graphics g, PointF center, AbstractSocket socket, Alignment alignment)
        {
            var text = socket.SocketName;
            var textColor = TextColor;

            var sSizeF = g.MeasureString(text, SocketCaptionFont);
            var position = PointF.Empty;

            switch (alignment)
            {
                case Alignment.Left:
                    position.X = center.X;
                    break;
                case Alignment.Right:
                    position.X = center.X - sSizeF.Width;
                    break;
                case Alignment.Center:
                    position.X = center.X + sSizeF.Width / 2;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(alignment), alignment, null);
            }

            position.Y = center.Y - (sSizeF.Height / 2);
            using var brush = new SolidBrush(textColor);

            g.DrawString(
                text,
                SocketCaptionFont,
                brush,
                position.X,
                position.Y
            );
        }

        #endregion

        #region DropShadow

        private const int ShadowSteps = 4;
        private const float ShadowMaxOpacity = 32.0f;
        private const int ShadowThickness = 10;

        private readonly Color _shadowColor = Color.FromArgb(7, 7, 7);
        private readonly Color _shadowColorSelected = Color.FromArgb(255, 255, 255);

        private void DropShadow(Graphics g, GraphicsPath path)
        {
            // g.SmoothingMode = SmoothingMode.AntiAlias;

            // Change in alpha between pens.
            const float delta = (ShadowMaxOpacity / ShadowSteps) / ShadowSteps;

            // Initial alpha.
            var alpha = delta;

            for (var thickness = ShadowThickness; thickness >= 1; thickness--)
            {
                var color = (Selected)
                    ? Color.FromArgb((int)alpha, _shadowColorSelected)
                    : Color.FromArgb((int)alpha, _shadowColor);
                using (var pen = new Pen(color, thickness))
                {
                    pen.EndCap = LineCap.Round;
                    pen.StartCap = LineCap.Round;
                    g.DrawPath(pen, path);
                }

                alpha += delta;
            }
        }

        #endregion
    }
}
