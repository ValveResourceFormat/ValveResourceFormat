using System.ComponentModel;
using SkiaSharp;

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

    public abstract class AbstractNode : NodeUIElement
    {
        private static readonly SKFont HeaderNameFont = SKTypeface.FromFamilyName("Helvetica", SKFontStyle.Bold).ToFont(14f);
        private static readonly SKFont HeaderTypeFont = SKTypeface.FromFamilyName("Roboto", SKFontStyle.Bold).ToFont(10f);
        private static readonly SKFont SocketCaptionFont = SKTypeface.FromFamilyName("Helvetica", SKFontStyle.Bold).ToFont(13f);

        static AbstractNode()
        {
            SKFont[] fonts = [HeaderNameFont, HeaderTypeFont, SocketCaptionFont];
            foreach (var font in fonts)
            {
                font.Hinting = SKFontHinting.Full;
                font.Subpixel = true;
            }
        }

        protected float NodeWidth { get; set; } = 200f;

        public bool StartNode { get; init; }

        public List<AbstractSocket> Sockets { get; } = [];

        protected const float HeaderHeight = 40;

        protected float MinBaseHeight { get; set; }

        private float _baseHeight;
        protected float FullHeight { get; private set; }
        private float _socketSplit;

        protected float FooterHeight { get; private set; }

        private const float SocketSize = 8;

        [Category("Location"), ReadOnly(true)] public SKPoint Location { get; set; }

        [Category("Location")] public SKPoint Pivot { get; private set; }

        private bool remeasureWidth;
        public string Name
        {
            get;
            set { field = value; remeasureWidth = true; }
        }

        [ReadOnly(true)] public string NodeType { get; set; }
        public string Description { get; set; }

        public SKColor HeaderColor { get; set; }
        protected SKColor BaseColor { get; set; }
        protected SKColor TextColor { get; set; }
        public SKColor HeaderTypeColor { get; set; } = SKColors.Orange;
        public SKColor HeaderTextColor { get; set; } = SKColors.LightGray;

        [Browsable(false)] public bool Selected { get; set; } = false;

        [Category("Bounds")] public SKRect BoundsFull { get; private set; }
        [Category("Bounds")] public SKRect BoundsHeader { get; private set; }
        [Category("Bounds")] public SKRect BoundsBase { get; private set; }
        [Category("Bounds")] public SKRect BoundsFooter { get; private set; }

        #region Events

        public event EventHandler InvokeRepaint;

        protected void OnInvokeRepaint(EventArgs e)
        {
            var handler = InvokeRepaint;
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
                {
                    return socket;
                }
            }

            return null;
        }

        public NodeBoundsArea GetBoundsArea(SKPoint point)
        {
            if (BoundsHeader.Contains(point))
            {
                return NodeBoundsArea.HEADER;
            }

            if (BoundsBase.Contains(point))
            {
                return NodeBoundsArea.BASE;
            }

            if (BoundsFooter.Contains(point))
            {
                return NodeBoundsArea.FOOTER;
            }

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
            if (remeasureWidth)
            {
                HeaderNameFont.MeasureText(Name ?? string.Empty, out var nameBounds);

                var maxTextWidth = nameBounds.Width;
                var minWidth = maxTextWidth + 20f; // Add padding (10px each side)
                NodeWidth = Math.Max(200f, minWidth); // Minimum 200px
                remeasureWidth = false;
            }

            if (MinBaseHeight == 0)
            {
                MinBaseHeight = SocketSize * 4; // default minimum height
            }

            var countIn = 0;
            var countOut = 0;

            foreach (var socket in Sockets)
            {
                if (socket.GetType() == typeof(SocketIn))
                {
                    countIn++;
                }

                if (socket.GetType() == typeof(SocketOut))
                {
                    countOut++;
                }
            }

            var socketsHeight = (SocketSize * 4) * Math.Max(countIn, countOut);

            _baseHeight = Math.Max(MinBaseHeight, socketsHeight);
            FullHeight = HeaderHeight + _baseHeight + FooterHeight;
            _socketSplit = _baseHeight / (Math.Max(countIn, countOut) + 1);

            BoundsFull = new SKRect(Location.X, Location.Y, Location.X + NodeWidth, Location.Y + FullHeight);
            BoundsHeader = new SKRect(Location.X, Location.Y, Location.X + NodeWidth, Location.Y + HeaderHeight);
            BoundsBase = new SKRect(Location.X, Location.Y + HeaderHeight, Location.X + NodeWidth, Location.Y + HeaderHeight + _baseHeight);
            BoundsFooter = new SKRect(Location.X, Location.Y + HeaderHeight + _baseHeight, Location.X + NodeWidth, Location.Y + HeaderHeight + _baseHeight + FooterHeight);

            Pivot = new SKPoint(BoundsFull.MidX, BoundsFull.MidY);

            countIn = 0;
            countOut = 0;

            foreach (var socket in Sockets)
            {
                if (socket.GetType() == typeof(SocketIn))
                {
                    countIn++;
                    socket.Pivot = new SKPoint(Location.X, Location.Y + HeaderHeight + _socketSplit * countIn);
                }

                if (socket.GetType() == typeof(SocketOut))
                {
                    countOut++;
                    socket.Pivot = new SKPoint(Location.X + NodeWidth, Location.Y + HeaderHeight + _socketSplit * countOut);
                }

                // update socket bounds
                socket.BoundsFull = new SKRect(
                    socket.Pivot.X - SocketSize / 2 - 1f,
                    socket.Pivot.Y - SocketSize / 2 - 1f,
                    socket.Pivot.X + SocketSize / 2 + 1f,
                    socket.Pivot.Y + SocketSize / 2 + 1f);
            }
        }

        #endregion

        #region Draw

        public virtual void Draw(SKCanvas canvas)
        {
            int cornerSize = CommonStates.CornerSize;
            var left = Location.X;
            var top = Location.Y;
            var right = Location.X + NodeWidth;
            var bottom = Location.Y + FullHeight;
            var bottomHeader = Location.Y + HeaderHeight;

            using var baseColorPaint = new SKPaint { Color = BaseColor, IsAntialias = true, Style = SKPaintStyle.Fill };
            using var headerColorPaint = new SKPaint { Color = HeaderColor, IsAntialias = true, Style = SKPaintStyle.Fill };

            // base
            using (var path = new SKPath())
            {
                var rect = SKRect.Create(left, top, NodeWidth, FullHeight);
                path.AddRoundRect(rect, cornerSize / 2f, cornerSize / 2f);

                DropShadow(canvas, path);

                canvas.DrawPath(path, baseColorPaint);
            }

            // header
            using (var path = new SKPath())
            {
                path.MoveTo(left + cornerSize / 2f, top);
                path.LineTo(right - cornerSize / 2f, top);
                path.ArcTo(SKRect.Create(right - cornerSize, top, cornerSize, cornerSize), 270, 90, false);
                path.LineTo(right, bottomHeader);
                path.LineTo(left, bottomHeader);
                path.LineTo(left, top + cornerSize / 2f);
                path.ArcTo(SKRect.Create(left, top, cornerSize, cornerSize), 180, 90, false);
                path.Close();

                canvas.DrawPath(path, headerColorPaint);

                var nodeTextOffset = 8f;
                var nodeTypePositionY = Location.Y + nodeTextOffset + 2;
                var nodeNamePositionY = Location.Y + HeaderHeight / 2 + nodeTextOffset;
                var nodeStringPositionX = Location.X + nodeTextOffset;

                using var headerTextPaint = new SKPaint { Color = HeaderTextColor, IsAntialias = true };
                using var headerTypePaint = new SKPaint { Color = HeaderTypeColor, IsAntialias = true };

                canvas.DrawText(Name, nodeStringPositionX, nodeTypePositionY + 10f, HeaderNameFont, headerTextPaint);
                canvas.DrawText(NodeType, nodeStringPositionX, nodeNamePositionY + 7f, HeaderTypeFont, headerTypePaint);
            }

            // sockets
            DrawSockets(canvas);
        }

        #endregion

        #region DrawSockets

        private void DrawSockets(SKCanvas canvas)
        {
            foreach (var socket in Sockets)
            {
                if (socket.GetType() == typeof(SocketIn))
                {
                    var socketIn = (SocketIn)socket;

                    if (!socketIn.DisplayOnly)
                    {
                        // draw socket
                        DrawSocket(
                            canvas,
                            socketIn.Pivot,
                            CommonStates.GetColorByType(socketIn.ValueType),
                            (socketIn.IsConnected()),
                            socketIn.Hub
                        );
                    }

                    // draw socket caption
                    DrawSocketCaption(canvas,
                        new SKPoint(socketIn.Pivot.X + (socketIn.DisplayOnly ? 5 : SocketSize), socketIn.Pivot.Y), socketIn, Alignment.Left
                    );
                }

                if (socket.GetType() == typeof(SocketOut))
                {
                    var socketOut = (SocketOut)socket;

                    // draw socket
                    DrawSocket(
                        canvas,
                        socketOut.Pivot,
                        CommonStates.GetColorByType(socketOut.ValueType),
                        (socketOut.IsConnected()),
                        false
                    );

                    // draw socket caption
                    DrawSocketCaption(canvas,
                        new SKPoint(socketOut.Pivot.X - SocketSize, socketOut.Pivot.Y), socketOut, Alignment.Right
                    );
                }
            }
        }

        private static void DrawSocket(SKCanvas canvas, SKPoint center, SKColor eColor, bool fill, bool hub)
        {
            using var ePaint = new SKPaint { Color = eColor, StrokeWidth = 1.8f, IsAntialias = true };

            var eX = center.X - SocketSize / 2f;
            var eY = center.Y - SocketSize / 2f;

            if (hub)
            {
                var rect = SKRect.Create(eX + 2, eY - 1, SocketSize - 2, SocketSize + 1);
                if (fill)
                {
                    ePaint.Style = SKPaintStyle.Fill;
                    canvas.DrawRect(rect, ePaint);
                }
                else
                {
                    ePaint.Style = SKPaintStyle.Stroke;
                    canvas.DrawRect(rect, ePaint);
                }
            }
            else
            {
                if (fill)
                {
                    ePaint.Style = SKPaintStyle.Fill;
                    canvas.DrawCircle(center.X, center.Y, SocketSize / 2f, ePaint);
                }
                else
                {
                    ePaint.Style = SKPaintStyle.Stroke;
                    canvas.DrawCircle(center.X, center.Y, SocketSize / 2f, ePaint);
                }
            }
        }

        private enum Alignment
        {
            Left,
            Right,
            Center // just in case... why not have an option to align to center
        }

        private void DrawSocketCaption(SKCanvas canvas, SKPoint center, AbstractSocket socket, Alignment alignment)
        {
            var text = socket.SocketName;
            var textColor = TextColor;

            using var textPaint = new SKPaint { Color = textColor, IsAntialias = true };

            SKRect bounds;
            SocketCaptionFont.MeasureText(text, out bounds, textPaint);
            var textWidth = bounds.Width;
            var metrics = SocketCaptionFont.Metrics;
            var textHeight = metrics.Descent - metrics.Ascent;

            float positionX = alignment switch
            {
                Alignment.Left => center.X,
                Alignment.Right => center.X - textWidth,
                Alignment.Center => center.X - textWidth / 2,
                _ => throw new ArgumentOutOfRangeException(nameof(alignment), alignment, null),
            };
            float positionY = center.Y - metrics.Ascent - textHeight / 2;

            canvas.DrawText(text, positionX, positionY, SocketCaptionFont, textPaint);
        }

        #endregion

        #region DropShadow

        private const int ShadowSteps = 4;
        private const float ShadowMaxOpacity = 32.0f;
        private const int ShadowThickness = 10;

        private readonly SKColor _shadowColor = new SKColor(7, 7, 7);
        private readonly SKColor _shadowColorSelected = new SKColor(255, 255, 255);

        private void DropShadow(SKCanvas canvas, SKPath path)
        {
            // Change in alpha between pens.
            const float delta = (ShadowMaxOpacity / ShadowSteps) / ShadowSteps;

            // Initial alpha.
            var alpha = delta;

            for (var thickness = ShadowThickness; thickness >= 1; thickness--)
            {
                var baseColor = (Selected) ? _shadowColorSelected : _shadowColor;
                var color = new SKColor(baseColor.Red, baseColor.Green, baseColor.Blue, (byte)alpha);
                
                using var paint = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = thickness,
                    IsAntialias = true,
                    StrokeCap = SKStrokeCap.Round
                };
                
                canvas.DrawPath(path, paint);

                alpha += delta;
            }
        }

        #endregion
    }
}
