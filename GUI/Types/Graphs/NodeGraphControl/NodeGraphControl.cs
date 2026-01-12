using System.Linq;
using System.Windows.Forms;
using SkiaSharp;

namespace GUI.Types.Graphs
{
    /*
        NodeGraphControl
        MIT License
        Copyright (c) 2021 amaurote
        https://github.com/amaurote/NodeGraphControl
    */
    public partial class NodeGraphControl : IDisposable
    {
        public const int CornerSize = 12;

        public static SKColor DefaultTypeColor { get; set; } = SKColors.Fuchsia;

        public static readonly Dictionary<Type, SKColor> TypeColor = [];

        public static SKColor GetColorByType(Type type)
        {
            TypeColor.TryGetValue(type, out var color);
            return (color != SKColor.Empty) ? color : DefaultTypeColor;
        }

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

        public event EventHandler? GraphChanged;

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
            TypeColor.TryAdd(typeof(T), color);
        }

        internal void Connect(SocketOut from, SocketIn to)
        {
            var wire = new Wire(from, to);
            to.Connect(wire);
            from.Connect(wire);
            _connections.Add(wire);
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

        private NodeUIElement? lastHoveredNode;
        private AbstractNode? primarySelectedNode;
        private readonly HashSet<AbstractNode> connectedNodes = [];

        public bool IsMoving { get; private set; }
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

                var wireColor = GetColorByType(wire.From.ValueType);
                var wireWidth = (wire == lastHoveredNode) ? 5f : 3f;
                using var wirePaint = new SKPaint { Color = wireColor, StrokeWidth = wireWidth, IsAntialias = true, Style = SKPaintStyle.Stroke };
                using var wirePath = DrawWire(canvas, wirePaint, xFrom, yFrom, xTo, yTo);

                using var widerPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 10f };
                wire.HitTestPath = widerPaint.GetFillPath(wirePath);
            }

            // Draw all nodes
            foreach (var node in nodeSnapshot)
            {
                var isPrimarySelected = (node == primarySelectedNode);
                var isConnected = !isPrimarySelected && connectedNodes.Contains(node);
                var isHovered = !isPrimarySelected && !isConnected && (node == lastHoveredNode);

                node.Draw(canvas, isPrimarySelected, isConnected, isHovered);
            }
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

            var element = FindElementAt(lastLocation);

            if ((button & MouseButtons.Left) != 0)
            {
                if (element == null && modifiers != Keys.Shift)
                {
                    ClearSelection();
                }

                if (!IsMoving)
                {
                    if (element is AbstractNode abstractNode)
                    {
                        if (modifiers == Keys.Shift)
                        {
                            ToggleSelection(abstractNode);
                        }
                        else
                        {
                            SetPrimarySelection(abstractNode);
                        }
                    }
                }

                if (element is AbstractNode node)
                {
                    IsMoving = true;
                    BringNodeToFront(node);
                }
            }
        }

        public void HandleMouseMove(SKPoint graphPoint, Keys modifiers = Keys.None)
        {
            if (IsMoving)
            {
                var delta = new SKPoint(
                    graphPoint.X - lastLocation.X,
                    graphPoint.Y - lastLocation.Y
                );

                var moveAllConnected = (modifiers & Keys.Control) != 0;

                if (moveAllConnected && connectedNodes.Count > 0)
                {
                    foreach (var node in connectedNodes)
                    {
                        node.Location = new SKPoint(
                            node.Location.X + delta.X,
                            node.Location.Y + delta.Y
                        );
                        node.Calculate();
                    }
                }
                else if (primarySelectedNode != null)
                {
                    primarySelectedNode.Location = new SKPoint(
                        primarySelectedNode.Location.X + delta.X,
                        primarySelectedNode.Location.Y + delta.Y
                    );
                    primarySelectedNode.Calculate();
                }

                lastLocation = graphPoint;
                OnGraphChanged();
                return;
            }

            var element = FindElementAt(graphPoint);

            if (lastHoveredNode != element)
            {
                lastHoveredNode = element;
                OnGraphChanged();
            }
        }

        public void HandleMouseUp(SKPoint graphPoint)
        {
            UpdateOriginalLocation(graphPoint);

            IsMoving = false;
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

            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var absDx = Math.Abs(dx);
            var absDy = Math.Abs(dy);

            var horizontalOffset = absDx * 0.5f + 50f / (absDx + 50f) * 50f;

            var backwardAmount = Math.Max(0, -dx);
            var backwardFactor = backwardAmount / (backwardAmount + 100f);
            var verticalOffset = backwardFactor * (50f / (1f + absDy / 50f) + dy * 0.1f);

            var fromControl = new SKPoint(from.X + horizontalOffset, from.Y + verticalOffset);
            var toControl = new SKPoint(to.X - horizontalOffset, to.Y + verticalOffset);

            path.MoveTo(from);
            path.CubicTo(fromControl, toControl, to);

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

        private void BringNodeToFront(AbstractNode node)
        {
            if (_graphNodes.Remove(node))
            {
                _graphNodes.Add(node);
            }

            OnGraphChanged();
        }

        // Find element at graph-space point
        public NodeUIElement? FindElementAt(SKPoint point)
        {
            // Iterate in reverse order to find topmost (frontmost) nodes first
            for (var i = _graphNodes.Count - 1; i >= 0; i--)
            {
                var node = _graphNodes[i];

                foreach (var socket in node.Sockets)
                {
                    if (socket.DisplayOnly || !socket.BoundsFull.Contains(point))
                    {
                        continue;
                    }

                    if (socket is SocketIn)
                    {
                        return socket;
                    }

                    if (socket is SocketOut)
                    {
                        return socket;
                    }
                }

                if (node.BoundsFull.Contains(point))
                {
                    return node;
                }
            }

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

        private void TraverseConnectedGraph(AbstractNode startNode)
        {
            connectedNodes.Clear();
            if (startNode == null)
            {
                return;
            }

            connectedNodes.Add(startNode);

            // Traverse upstream: follow inputs backwards from primary node to find all source nodes
            TraverseUpstream(startNode, connectedNodes);

            // Traverse downstream: follow outputs forward from primary node to find all destination nodes
            TraverseDownstream(startNode, connectedNodes);
        }

        private static void TraverseUpstream(AbstractNode startNode, HashSet<AbstractNode> visited)
        {
            var queue = new Queue<AbstractNode>();
            queue.Enqueue(startNode);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                // Only follow input sockets (go backwards in the flow)
                foreach (var socket in current.Sockets.OfType<SocketIn>())
                {
                    foreach (var wire in socket.Connections)
                    {
                        var sourceNode = wire.From.Owner;
                        if (sourceNode != null && visited.Add(sourceNode))
                        {
                            queue.Enqueue(sourceNode);
                        }
                    }
                }
            }
        }

        private static void TraverseDownstream(AbstractNode startNode, HashSet<AbstractNode> visited)
        {
            var queue = new Queue<AbstractNode>();
            queue.Enqueue(startNode);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                // Only follow output sockets (go forward in the flow)
                foreach (var socket in current.Sockets.OfType<SocketOut>())
                {
                    foreach (var wire in socket.Connections)
                    {
                        var destNode = wire.To.Owner;
                        if (destNode != null && visited.Add(destNode))
                        {
                            queue.Enqueue(destNode);
                        }
                    }
                }
            }
        }

        private void SetPrimarySelection(AbstractNode node)
        {
            primarySelectedNode = node;
            TraverseConnectedGraph(node);

            foreach (var connectedNode in connectedNodes)
            {
                if (connectedNode != primarySelectedNode && _graphNodes.Remove(connectedNode))
                {
                    _graphNodes.Add(connectedNode);
                }
            }

            // Bring primary node to front last (so it's on top)
            if (_graphNodes.Remove(primarySelectedNode))
            {
                _graphNodes.Add(primarySelectedNode);
            }
        }

        private void ToggleSelection(AbstractNode node)
        {
            if (primarySelectedNode == node || connectedNodes.Contains(node))
            {
                primarySelectedNode = null;
                connectedNodes.Clear();
            }
            else
            {
                SetPrimarySelection(node);
            }
            OnGraphChanged();
        }

        private void ClearSelection()
        {
            primarySelectedNode = null;
            connectedNodes.Clear();
            OnGraphChanged();
        }
    }
}
