using SkiaSharp;

namespace GUI.Types.Graphs
{
    public abstract class AbstractNode : NodeUIElement, IDisposable
    {
        private static readonly SKFont HeaderNameFont = SKTypeface.FromFamilyName("Helvetica", SKFontStyle.Bold).ToFont(14f);
        private static readonly SKFont HeaderTypeFont = SKTypeface.FromFamilyName("Roboto", SKFontStyle.Bold).ToFont(10f);
        private static readonly SKFont SocketCaptionFont = SKTypeface.FromFamilyName("Helvetica", SKFontStyle.Bold).ToFont(13f);

        static AbstractNode()
        {
            SKFont[] fonts = [HeaderNameFont, HeaderTypeFont, SocketCaptionFont];
            foreach (var font in fonts)
            {
                font.Hinting = SKFontHinting.Normal;
                font.Subpixel = true;
                font.Edging = SKFontEdging.SubpixelAntialias;
            }
        }

        protected float NodeWidth { get; set; } = 200f;

        public List<AbstractSocket> Sockets { get; } = [];

        protected const float HeaderHeight = 40;

        protected float MinBaseHeight { get; set; }

        private float _baseHeight;
        protected float FullHeight { get; private set; }
        private float _socketSplit;

        protected float FooterHeight { get; private set; }

        private const float SocketSize = 8;

        // Reusable paint objects to avoid allocations in render loop
        private readonly SKPaint baseColorPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint headerColorPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint headerTextPaint = new() { IsAntialias = true };
        private readonly SKPaint headerTypePaint = new() { IsAntialias = true };
        private readonly SKPaint textPaint = new() { IsAntialias = true };
        private readonly SKPaint ePaint = new() { StrokeWidth = 1.8f, IsAntialias = true };

        public SKPoint Location { get; set; }

        public SKPoint Pivot { get; private set; }

        private bool remeasureWidth;
        public string? Name
        {
            get;
            set { field = value; remeasureWidth = true; }
        }

        public required string NodeType { get; init; }

        public SKColor HeaderColor { get; set; }
        protected SKColor BaseColor { get; set; }
        protected SKColor TextColor { get; set; }
        public SKColor HeaderTypeColor { get; set; } = SKColors.Orange;
        public SKColor HeaderTextColor { get; set; } = SKColors.LightGray;

        public SKRect BoundsFull { get; private set; }
        public SKRect BoundsHeader { get; private set; }
        public SKRect BoundsBase { get; private set; }
        public SKRect BoundsFooter { get; private set; }

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
                if (socket is SocketIn)
                {
                    countIn++;
                }
                else if (socket is SocketOut)
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
                if (socket is SocketIn)
                {
                    countIn++;
                    socket.Pivot = new SKPoint(Location.X, Location.Y + HeaderHeight + _socketSplit * countIn);
                }
                else if (socket is SocketOut)
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

        public virtual void Draw(SKCanvas canvas, bool isPrimarySelected, bool isConnected, bool isHovered)
        {
            var cornerSize = NodeGraphControl.CornerSize;
            var left = Location.X;
            var top = Location.Y;
            var right = Location.X + NodeWidth;
            var bottom = Location.Y + FullHeight;
            var bottomHeader = Location.Y + HeaderHeight;

            baseColorPaint.Color = BaseColor;
            headerColorPaint.Color = HeaderColor;

            // base
            using (var path = new SKPath())
            {
                var rect = SKRect.Create(left, top, NodeWidth, FullHeight);
                path.AddRoundRect(rect, cornerSize / 2f, cornerSize / 2f);

                SKColor shadowColor;
                var shadowBlur = 10f;

                if (isPrimarySelected)
                {
                    shadowColor = HeaderColor;
                    shadowBlur = 15f;
                }
                else if (isConnected)
                {
                    shadowColor = HeaderColor.WithAlpha(128);
                }
                else if (isHovered)
                {
                    shadowColor = new SKColor(200, 200, 255);
                    shadowBlur = 20f;
                }
                else
                {
                    shadowColor = new SKColor(7, 7, 7);
                }

                using var shadowFilter = SKImageFilter.CreateDropShadow(0, 0, shadowBlur, shadowBlur, shadowColor.WithAlpha((byte)128));
                baseColorPaint.ImageFilter = shadowFilter;
                canvas.DrawPath(path, baseColorPaint);
                baseColorPaint.ImageFilter = null;

                if (isPrimarySelected || isConnected)
                {
                    using var borderPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke,
                        Color = shadowColor,
                        StrokeWidth = isPrimarySelected ? 2f : 1.5f,
                        IsAntialias = true
                    };
                    canvas.DrawPath(path, borderPaint);
                }
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

                headerTextPaint.Color = HeaderTextColor;
                headerTypePaint.Color = HeaderTypeColor;

                canvas.DrawText(Name, nodeStringPositionX, nodeTypePositionY + 10f, HeaderNameFont, headerTextPaint);
                canvas.DrawText(NodeType, nodeStringPositionX, nodeNamePositionY + 7f, HeaderTypeFont, headerTypePaint);
            }

            // sockets
            DrawSockets(canvas);
        }

        private void DrawSockets(SKCanvas canvas)
        {
            foreach (var socket in Sockets)
            {
                if (socket is SocketIn socketIn)
                {

                    if (!socketIn.DisplayOnly)
                    {
                        // draw socket
                        DrawSocket(
                            canvas,
                            socketIn.Pivot,
                            NodeGraphControl.GetColorByType(socketIn.ValueType),
                            (socketIn.IsConnected()),
                            socketIn.Hub
                        );
                    }

                    // draw socket caption
                    DrawSocketCaption(canvas,
                        new SKPoint(socketIn.Pivot.X + (socketIn.DisplayOnly ? 5 : SocketSize), socketIn.Pivot.Y), socketIn, Alignment.Left
                    );
                }
                else if (socket is SocketOut socketOut)
                {

                    // draw socket
                    DrawSocket(
                        canvas,
                        socketOut.Pivot,
                        NodeGraphControl.GetColorByType(socketOut.ValueType),
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

        private void DrawSocket(SKCanvas canvas, SKPoint center, SKColor eColor, bool fill, bool hub)
        {
            ePaint.Color = eColor;

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
            Center,
        }

        private void DrawSocketCaption(SKCanvas canvas, SKPoint center, AbstractSocket socket, Alignment alignment)
        {
            var text = socket.SocketName;
            textPaint.Color = TextColor;

            SocketCaptionFont.MeasureText(text, out var bounds, textPaint);
            var textWidth = bounds.Width;
            var metrics = SocketCaptionFont.Metrics;
            var textHeight = metrics.Descent - metrics.Ascent;

            var positionX = alignment switch
            {
                Alignment.Left => center.X,
                Alignment.Right => center.X - textWidth,
                Alignment.Center => center.X - textWidth / 2,
                _ => throw new ArgumentOutOfRangeException(nameof(alignment), alignment, null),
            };
            var positionY = center.Y - metrics.Ascent - textHeight / 2;

            canvas.DrawText(text, positionX, positionY, SocketCaptionFont, textPaint);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                baseColorPaint?.Dispose();
                headerColorPaint?.Dispose();
                headerTextPaint?.Dispose();
                headerTypePaint?.Dispose();
                textPaint?.Dispose();
                ePaint?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
