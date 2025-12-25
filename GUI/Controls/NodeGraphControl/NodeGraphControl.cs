using SkiaSharp;
using System.Linq;
using System.Windows.Forms;
using NodeGraphControl.Elements;

#nullable disable

namespace NodeGraphControl
{
    /*
        NodeGraphControl
        MIT License
        Copyright (c) 2021 amaurote
        https://github.com/amaurote/NodeGraphControl
    */
    public class NodeGraphControl : IDisposable
    {
        public NodeGraphControl()
        {
            //
        }

        private bool disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    _gridPaint?.Dispose();
                }

                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public event EventHandler GraphChanged;

        private void OnGraphChanged()
        {
            GraphChanged?.Invoke(this, EventArgs.Empty);
        }

        public T AddNode<T>(T node) where T : AbstractNode
        {
            _graphNodes.Add(node);
            node.Calculate();
            return node;
        }

        public static void AddTypeColorPair<T>(SKColor color)
        {
            SharedState.TypeColor.TryAdd(typeof(T), color);
        }

        internal void Connect(SocketOut from, SocketIn to)
        {
            var wire = new Wire(from, to);
            to.Connect(wire);
            from.Connect(wire);
            _connections.Add(wire);
        }

        public void LayoutNodes(float padding = 20f)
        {
            if (_graphNodes.Count == 0)
            {
                return;
            }

            const int maxIterations = 100;

            // Simple overlap removal algorithm using iterative repositioning
            for (var iteration = 0; iteration < maxIterations; iteration++)
            {
                var hasOverlap = false;

                for (var i = 0; i < _graphNodes.Count; i++)
                {
                    var nodeA = _graphNodes[i];
                    nodeA.Calculate();

                    for (var j = i + 1; j < _graphNodes.Count; j++)
                    {
                        var nodeB = _graphNodes[j];
                        nodeB.Calculate();

                        // Check if nodes overlap
                        var boundsA = nodeA.BoundsFull;
                        var boundsB = nodeB.BoundsFull;

                        // Add padding to bounds for overlap check
                        var paddedBoundsA = SKRect.Inflate(boundsA, padding / 2, padding / 2);

                        var paddedBoundsB = SKRect.Inflate(boundsB, padding / 2, padding / 2);

                        if (paddedBoundsA.IntersectsWith(paddedBoundsB))
                        {
                            hasOverlap = true;

                            // Calculate separation vector
                            var centerA = new SKPoint(nodeA.Pivot.X, nodeA.Pivot.Y);
                            var centerB = new SKPoint(nodeB.Pivot.X, nodeB.Pivot.Y);

                            var dx = centerB.X - centerA.X;
                            var dy = centerB.Y - centerA.Y;
                            var distance = (float)Math.Sqrt(dx * dx + dy * dy);

                            if (distance < 1f)
                            {
                                // Nodes are at same position, push apart arbitrarily
                                dx = 1f;
                                dy = 1f;
                                distance = (float)Math.Sqrt(2);
                            }

                            // Normalize direction vector
                            dx /= distance;
                            dy /= distance;

                            // Calculate required separation
                            var overlapX = (paddedBoundsA.Width + paddedBoundsB.Width) / 2 - Math.Abs(centerB.X - centerA.X);
                            var overlapY = (paddedBoundsA.Height + paddedBoundsB.Height) / 2 - Math.Abs(centerB.Y - centerA.Y);

                            // Move nodes apart (both nodes move half the distance)
                            var moveX = dx * overlapX / 2;
                            var moveY = dy * overlapY / 2;

                            // Prefer horizontal separation for better graph readability
                            if (Math.Abs(dx) > 0.1f)
                            {
                                nodeA.Location = new SKPoint(
                                    (int)Math.Round(nodeA.Location.X - moveX),
                                    nodeA.Location.Y);
                                nodeB.Location = new SKPoint(
                                    (int)Math.Round(nodeB.Location.X + moveX),
                                    nodeB.Location.Y);
                            }
                            else
                            {
                                nodeA.Location = new SKPoint(
                                    nodeA.Location.X,
                                    (int)Math.Round(nodeA.Location.Y - moveY));
                                nodeB.Location = new SKPoint(
                                    nodeB.Location.X,
                                    (int)Math.Round(nodeB.Location.Y + moveY));
                            }

                            nodeA.Calculate();
                            nodeB.Calculate();
                        }
                    }
                }

                if (!hasOverlap)
                {
                    break;
                }
            }

            OnGraphChanged();
        }

        // grid style
        public enum EGridStyle
        {
            Grid,
            Dots,
            None
        }

        private EGridStyle _gridStyle = EGridStyle.Grid;

        public EGridStyle GridStyle
        {
            get { return _gridStyle; }
            set
            {
                if (_gridStyle == value)
                {
                    return;
                }

                _gridStyle = value;
                OnGraphChanged();
            }
        }

        // grid step
        private int _gridStep = 16 * 8;

        public int GridStep
        {
            get { return _gridStep; }
            set
            {
                if (_gridStep == value)
                {
                    return;
                }

                _gridStep = value;
                OnGraphChanged();
            }
        }

        // grid color
        private SKColor _gridColor = SKColors.LightGray;
        private SKPaint _gridPaint = new() { Color = SKColors.LightGray, StrokeWidth = 1f, IsAntialias = true };

        public SKColor GridColor
        {
            get { return _gridColor; }
            set
            {
                if (_gridColor == value)
                {
                    return;
                }

                _gridColor = value;
                _gridPaint?.Dispose();
                _gridPaint = new SKPaint { Color = _gridColor, StrokeWidth = 1f, IsAntialias = true };
                OnGraphChanged();
            }
        }

        // canvas background color
        private SKColor _canvasBackgroundColor = new(23, 25, 31);

        public SKColor CanvasBackgroundColor
        {
            get { return _canvasBackgroundColor; }
            set
            {
                if (_canvasBackgroundColor == value)
                {
                    return;
                }

                _canvasBackgroundColor = value;
                OnGraphChanged();
            }
        }

        private readonly List<AbstractNode> _graphNodes = [];
        private readonly List<Wire> _connections = [];

        private bool isMoving;
        SKPoint lastLocation;

        public void RenderToCanvas(SKCanvas canvas, SKPoint topLeft, SKPoint bottomRight)
        {
            canvas.Clear(_canvasBackgroundColor);

            OnDrawBackground(canvas, topLeft, bottomRight);

            // Return if no nodes
            if (_graphNodes.Count == 0)
            {
                return;
            }

            // Take snapshots to avoid collection modification during enumeration (render thread vs UI thread)
            var connectionSnapshot = _connections.ToArray();
            var nodeSnapshot = _graphNodes.ToArray();

            // Draw all wires
            foreach (var wire in connectionSnapshot)
            {
                var xFrom = wire.From.BoundsFull.MidX;
                var yFrom = wire.From.BoundsFull.MidY;
                var xTo = wire.To.BoundsFull.MidX;
                var yTo = wire.To.BoundsFull.MidY;

                if (Vector2.Distance(new Vector2(xFrom, yFrom), new Vector2(xTo, yTo)) < 1f)
                {
                    continue;
                }

                var wireColor = SharedState.GetColorByType(wire.From.ValueType);
                var wireWidth = (wire == lastHover) ? 5f : 3f;
                using var wirePaint = new SKPaint { Color = wireColor, StrokeWidth = wireWidth, IsAntialias = true, Style = SKPaintStyle.Stroke };
                using var wirePath = DrawWire(canvas, wirePaint, xFrom, yFrom, xTo, yTo);

                using var widerPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 10f };
                wire.HitTestPath = widerPaint.GetFillPath(wirePath);
            }

            // Draw all nodes
            foreach (var node in nodeSnapshot)
            {
                node.Draw(canvas);
            }
        }

        // Find element at graph-space point
        public NodeUIElement FindElementAt(SKPoint graphPoint)
        {
            return FindElementAtOriginal(graphPoint);
        }

        // Get the bounds of the entire graph in graph space
        public SKRect GetGraphBounds()
        {
            if (_graphNodes.Count == 0)
            {
                return new SKRect(-1000, -1000, 1000, 1000); // Default large area
            }

            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;

            foreach (var node in _graphNodes)
            {
                var bounds = node.BoundsFull;
                minX = Math.Min(minX, bounds.Left);
                minY = Math.Min(minY, bounds.Top);
                maxX = Math.Max(maxX, bounds.Right);
                maxY = Math.Max(maxY, bounds.Bottom);
            }

            // Add some padding
            const float padding = 200f;
            return new SKRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
        }

        public void HandleMouseDown(SKPoint graphPoint, MouseButtons button, Keys modifiers)
        {
            UpdateOriginalLocation(graphPoint);

            var element = FindElementAtOriginal(lastLocation);

            if ((button & MouseButtons.Left) != 0)
            {
                if (!isMoving)
                {
                    if (element == null && modifiers != Keys.Shift)
                    {
                        // Deselect all nodes when clicking on empty space
                        foreach (var n in _graphNodes)
                        {
                            n.Selected = false;
                        }
                        OnGraphChanged();
                    }

                    if (element is AbstractNode abstractNode)
                    {
                        if (modifiers != Keys.Shift && !abstractNode.Selected)
                        {
                            // Deselect all other nodes
                            foreach (var n in _graphNodes)
                            {
                                n.Selected = false;
                            }
                        }

                        if (modifiers == Keys.Shift)
                        {
                            abstractNode.Selected = !abstractNode.Selected;
                        }
                        else
                        {
                            abstractNode.Selected = true;
                        }

                        OnGraphChanged();
                    }
                }

                if (element is AbstractNode node)
                {
                    isMoving = true;
                    BringNodeToFront(node);
                }
            }
        }

        public void HandleMouseMove(SKPoint graphPoint)
        {
            if (isMoving)
            {
                var delta = new SKPoint(
                    graphPoint.X - lastLocation.X,
                    graphPoint.Y - lastLocation.Y
                );

                foreach (var node in _graphNodes.Where(node => node.Selected))
                {
                    node.Location = new SKPoint(
                        (int)Math.Round(node.Location.X + delta.X),
                        (int)Math.Round(node.Location.Y + delta.Y)
                    );
                    node.Calculate();
                }

                lastLocation = graphPoint;
                OnGraphChanged();
                return;
            }

            var element = FindElementAtOriginal(graphPoint);

            if (lastHover != element)
            {
                lastHover = element;
                OnGraphChanged();
                return;
            }
        }

        public void HandleMouseUp(SKPoint graphPoint, MouseButtons button)
        {
            UpdateOriginalLocation(graphPoint);

            isMoving = false;
        }

        private void UpdateOriginalLocation(SKPoint graphPoint)
        {
            lastLocation = graphPoint;
        }

        private static SKPath DrawWire(SKCanvas canvas, SKPaint paint, float xFrom, float yFrom, float xTo, float yTo)
        {
            var from = new SKPoint(xFrom, yFrom);
            var to = new SKPoint(xTo, yTo);

            var path = new SKPath();

            var distance = to.X - from.X;
            var spreadDistance = ((distance / 2f) / 100f) * 1;

            var fromHalf = new SKPoint(from.X + distance / 2 - spreadDistance, from.Y);
            var toHalf = new SKPoint(from.X + distance / 2 + spreadDistance, to.Y);

            path.MoveTo(from);
            path.CubicTo(fromHalf, toHalf, to);

            canvas.DrawPath(path, paint);
            return path;
        }

        private void OnDrawBackground(SKCanvas canvas, SKPoint topLeft, SKPoint bottomRight)
        {
            if (_gridStyle == EGridStyle.None)
            {
                return;
            }

            var left = topLeft.X;
            var right = bottomRight.X;
            var top = topLeft.Y;
            var bottom = bottomRight.Y;

            var largeXOffset = ((float)Math.Round(left / _gridStep) * _gridStep);
            var largeYOffset = ((float)Math.Round(top / _gridStep) * _gridStep);

            // grid
            if (_gridStyle == EGridStyle.Grid)
            {
                for (var x = largeXOffset; x < right; x += _gridStep)
                {
                    canvas.DrawLine(x, top, x, bottom, _gridPaint);
                }

                for (var y = largeYOffset; y < bottom; y += _gridStep)
                {
                    canvas.DrawLine(left, y, right, y, _gridPaint);
                }
            }

            // dots
            if (_gridStyle == EGridStyle.Dots)
            {
                _gridPaint.Style = SKPaintStyle.Fill;
                for (var x = largeXOffset; x < right; x += _gridStep)
                {
                    for (var y = largeYOffset; y < bottom; y += _gridStep)
                    {
                        canvas.DrawRect(x, y, 2, 2, _gridPaint);
                    }
                }
                _gridPaint.Style = SKPaintStyle.Stroke;
            }
        }

        private NodeUIElement lastHover;

        private void BringNodeToFront(AbstractNode node)
        {
            if (_graphNodes.Remove(node))
            {
                _graphNodes.Add(node);
            }

            OnGraphChanged();
        }

        private NodeUIElement FindElementAtOriginal(SKPoint point)
        {
            foreach (var node in _graphNodes)
            {
                // find socket
                foreach (var socket in node.Sockets.Where(socket => !socket.DisplayOnly && socket.BoundsFull.Contains(point)))
                {
                    if (socket is SocketIn)
                    {
                        return socket;
                    }

                    if (socket is SocketOut)
                    {
                        return socket;
                    }
                }

                // find node
                if (node.BoundsFull.Contains(point))
                {
                    return node;
                }
            }

            // find wire
            for (var i = _connections.Count - 1; i >= 0; i--)
            {
                var wire = _connections[i];
                if (wire.HitTestPath != null && wire.HitTestPath.Contains(point.X, point.Y))
                {
                    return wire;
                }
            }

            return null;
        }
    }
}
