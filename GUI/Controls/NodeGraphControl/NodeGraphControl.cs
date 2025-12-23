using System.ComponentModel;
using System.Drawing;
using SkiaSharp;
using System.Linq;
using System.Text.RegularExpressions;
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
    public partial class NodeGraphControl : SKControl
    {
        #region Constructor

        public NodeGraphControl()
        {
            InitializeComponent();
        }

        #endregion

        #region Interface

        public void Run()
        {
            foreach (var abstractNode in _graphNodes.Where(abstractNode => abstractNode.StartNode))
            {
                abstractNode.Execute();
            }
        }

        private readonly List<ContextNode> _contextNodeList = [];

        public void AddContextNodeType<T>(string contextName, string contextDescription, string contextCategory)
            where T : AbstractNode
        {
            var cn = new ContextNode();

            const string defaultDescription = "No Description";
            const string defaultCategory = "";

            // create name from type
            var typeStr = typeof(T).ToString().Split('.').Last();
            // insert space before Capital letter and ignore acronyms
            var nameFromTypeStr = MyRegex().Replace(typeStr, " $0");

            // add context node data
            cn.NodeType = typeof(T);

            cn.NodeName = (!string.IsNullOrEmpty(contextName)) ? contextName : nameFromTypeStr;
            cn.NodeDescription = (!string.IsNullOrEmpty(contextDescription)) ? contextDescription : defaultDescription;
            cn.NodeCategory = (!string.IsNullOrEmpty(contextCategory)) ? contextCategory : defaultCategory;

            _contextNodeList.Add(cn);
        }

        public T AddNode<T>(T node) where T : AbstractNode
        {
            node.InvokeRepaint += n_InvokeRepaint;
            _graphNodes.Add(node);
            node.Calculate();
            return node;
        }

        public void DeleteNode(AbstractNode node)
        {
            node.Disconnect();
            _graphNodes.Remove(node);
            ValidateConnections();
            HandleSelection();
            Refresh();
        }

        private void DeleteSelectedNodes()
        {
            for (var i = _graphNodes.Count - 1; i >= 0; i--)
            {
                var node = _graphNodes[i];
                if (node.Selected)
                {
                    node.Disconnect();
                    _graphNodes.Remove(node);
                }
            }

            ValidateConnections();
            HandleSelection();
            Refresh();
        }

        public void Connect(SocketOut from, SocketIn to)
        {
            Wire wire = null;

            try
            {
                wire = new Wire(from, to);
                to.Connect(wire);
                from.Connect(wire);

                _connections.Add(wire);

                wire.Flow();
            }
            catch (Exception e)
            {
                wire?.Disconnect();
                Console.WriteLine(e);
            }

            ValidateConnections();
        }

        private void Disconnect(Wire wire)
        {
            if (wire == null)
            {
                return;
            }

            wire.Disconnect();
            _connections.Remove(wire);
        }

        private void ValidateConnections()
        {
            for (var i = _connections.Count - 1; i >= 0; i--)
            {
                var con = _connections[i];

                if (con.From == null || con.To == null
                                     || !con.From.ContainsConnection(con)
                                     || !con.To.ContainsConnection(con))
                {
                    Disconnect(con);
                }
            }
        }

        public void SetNodeSelected(bool selected)
        {
            foreach (var node in _graphNodes)
            {
                node.Selected = selected;
            }

            HandleSelection();
        }

        public static void AddTypeColorPair<T>(SKColor color)
        {
            SharedState.TypeColor.TryAdd(typeof(T), color);
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

            Invalidate();
        }

        #endregion

        #region Events

#pragma warning disable CA1003 // Use generic event handler instances
        public event EventHandler<List<AbstractNode>> SelectionChanged;

        public event EventHandler<float> ZoomChanged;
#pragma warning restore CA1003 // Use generic event handler instances

        #endregion

        #region EventData

        #endregion

        #region EventHandlers

        private void n_InvokeRepaint(object sender, EventArgs e)
        {
            Invalidate();
        }

        #endregion

        #region GridSettings

        // grid style
        public enum EGridStyle
        {
            Grid,
            Dots,
            None
        }

        private EGridStyle _gridStyle = EGridStyle.Grid;

        [Description("The type of rendered grid"), Category("Appearance"), DisplayName("Grid Style")]
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
                Invalidate();
            }
        }

        // grid step
        private int _gridStep = 16 * 8;

        [Description("The distance between the largest grid lines"), Category("Appearance")]
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
                Invalidate();
            }
        }

        // grid color
        private SKColor _gridColor = SKColors.LightGray;
        private SKPaint _gridPaint = new() { Color = SKColors.LightGray, StrokeWidth = 1f, IsAntialias = true };

        [Description("The color for the grid lines with the largest gap between them"), Category("Appearance")]
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
                Invalidate();
            }
        }

        // canvas background color
        private SKColor _canvasBackgroundColor = new(23, 25, 31);

        [Description("The background color of the canvas"), Category("Appearance")]
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
                Invalidate();
            }
        }

        #endregion

        #region Elements

        private readonly List<AbstractNode> _graphNodes = [];
        private readonly List<Wire> _connections = [];
        private Wire _tempWire;

        #endregion

        #region regionToBeSorted

        private enum CommandMode
        {
            Edit,
            MarqueSelection,
            MoveSelection,
            Wiring,
            TranslateView,
            ScaleView
        }

        private CommandMode _command = CommandMode.Edit;

        // NodeUIElement internalDragOverElement;
        bool mouseMoved;
        bool dragging;

        SKPoint lastLocation;
        SKPoint snappedLocation;
        SKPoint originalLocation;
        SKPoint originalMouseLocation;

        #endregion

        #region UpdateMatrices

        SKPoint translation;
        float zoom = 1.0f;
        private float zoomLast;

        SKMatrix transformation = SKMatrix.Identity;
        SKMatrix inverse_transformation = SKMatrix.Identity;

        private void UpdateMatrices()
        {
            zoom = Math.Clamp(zoom, 0.25f, 4.00f);

            if (Math.Abs(zoom - zoomLast) > 0.01f)
            {
                zoomLast = zoom;
                ZoomChanged?.Invoke(this, zoom);
            }

            transformation = SKMatrix.CreateScale(zoom, zoom);
            transformation = transformation.PostConcat(SKMatrix.CreateTranslation(translation.X, translation.Y));

            transformation.TryInvert(out inverse_transformation);
        }

        #endregion

        #region GetTransformedLocation
        // TODO refactor
        private SKPoint GetTransformedLocation()
        {
            return inverse_transformation.MapPoint(snappedLocation);
        }

        #endregion

        #region OnPaint

        // temp
        private bool _renderBounds;

        protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
        {
            base.OnPaintSurface(e);

            var canvas = e.Surface.Canvas;
            canvas.Clear(_canvasBackgroundColor);

            // update matrices
            UpdateMatrices();
            canvas.SetMatrix(transformation);

            // draw background
            OnDrawBackground(canvas, e.Info.Width, e.Info.Height);

            // temp crosshair
            using var crosshairPaint = new SKPaint { Color = SKColors.Gray, StrokeWidth = 3f, IsAntialias = true };
            canvas.DrawLine(-_gridStep, 0, _gridStep, 0, crosshairPaint);
            canvas.DrawLine(0, -_gridStep, 0, _gridStep, crosshairPaint);

            // return if no nodes
            if (_graphNodes.Count == 0)
            {
                return;
            }

            // draw all wires
            foreach (var wire in _connections)
            {
                var xFrom = wire.From.BoundsFull.MidX;
                var yFrom = wire.From.BoundsFull.MidY;
                var xTo = wire.To.BoundsFull.MidX;
                var yTo = wire.To.BoundsFull.MidY;

                // skip wire if there is no distance between two points
                if (Vector2.Distance(new Vector2(xFrom, yFrom), new Vector2(xTo, yTo)) < 1f)
                {
                    continue;
                }

                // draw wire
                var wireColor = SharedState.GetColorByType(wire.From.ValueType);
                var wireWidth = (wire == lastHover) ? 4f : 3f;
                using var wirePaint = new SKPaint { Color = wireColor, StrokeWidth = wireWidth, IsAntialias = true, Style = SKPaintStyle.Stroke };
                using var wirePath = DrawWire(canvas, wirePaint, xFrom, yFrom, xTo, yTo);

                // Create a wider path for hit testing
                using var widerPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 10f };
                wire.HitTestPath = widerPaint.GetFillPath(wirePath);

                // and eventually draw it
                if (_renderBounds)
                {
                    using var boundsPaint = new SKPaint { Color = new SKColor(165, 42, 42), IsAntialias = true, Style = SKPaintStyle.Fill };
                    canvas.DrawPath(wirePath, boundsPaint);
                }
            }

            // draw all nodes
            foreach (var node in _graphNodes)
            {
                node.Draw(canvas);
            }

            // render bounds
            if (_renderBounds)
            {
                using var boundsPaint = new SKPaint { Color = SKColors.Aqua, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
                foreach (var node in _graphNodes)
                {
                    foreach (var socket in node.Sockets)
                    {
                        canvas.DrawRect(socket.BoundsFull, boundsPaint);
                    }

                    canvas.DrawRect(node.BoundsHeader, boundsPaint);
                    canvas.DrawRect(node.BoundsBase, boundsPaint);
                    canvas.DrawRect(node.BoundsFooter, boundsPaint);
                }
            }

            // draw temp wire during wiring mode
            if (_command == CommandMode.Wiring && _tempWire != null)
            {
                float xFrom, yFrom, xTo, yTo;

                var cursorPoint = GetTranslatedPosition(PointToClient(Cursor.Position));
                using var tempWirePaint = new SKPaint { Color = SKColors.White, StrokeWidth = 2f, IsAntialias = true, Style = SKPaintStyle.Stroke };

                if (_tempWire.From != null)
                {
                    xFrom = _tempWire.From.BoundsFull.MidX;
                    yFrom = _tempWire.From.BoundsFull.MidY;
                    xTo = cursorPoint.X;
                    yTo = cursorPoint.Y;

                    using var _ = DrawWire(canvas, tempWirePaint, xFrom, yFrom, xTo, yTo);
                }
                else if (_tempWire.To != null)
                {
                    xFrom = cursorPoint.X;
                    yFrom = cursorPoint.Y;
                    xTo = _tempWire.To.BoundsFull.MidX;
                    yTo = _tempWire.To.BoundsFull.MidY;

                    using var _ = DrawWire(canvas, tempWirePaint, xFrom, yFrom, xTo, yTo);
                }
            }

            // draw marque
            if (_command == CommandMode.MarqueSelection)
            {
                var marqueRectangle = GetMarqueRectangle();
                using var marquePaint = new SKPaint { Color = new SKColor(64, 64, 127, 15), IsAntialias = true, Style = SKPaintStyle.Fill };
                canvas.DrawRect(marqueRectangle, marquePaint);
                using var marqueStrokePaint = new SKPaint { Color = SKColors.DarkGray, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
                canvas.DrawRect(marqueRectangle, marqueStrokePaint);
            }
        }

        // wire style
        public enum EWireStyle
        {
            Bezier, Line, StepLine
        }

        private EWireStyle _wireStyle = EWireStyle.Bezier;

        [Description("The style in which wires will be drown"), Category("Experimental")]
        public EWireStyle WireStyle
        {
            get { return _wireStyle; }
            set
            {
                if (_wireStyle == value)
                {
                    return;
                }

                _wireStyle = value;
                Invalidate();
            }
        }

        // wire middle points spread (percentage)
        private int _wireMiddlePointsSpread;

        [Description("The middle point of wires spread in percentages"), Category("Experimental")]
        public int WireMiddlePointsSpread
        {
            get { return _wireMiddlePointsSpread; }
            set
            {
                var tempValue = Math.Min(100, Math.Max(0, value));
                if (_wireMiddlePointsSpread == tempValue)
                {
                    return;
                }

                _wireMiddlePointsSpread = tempValue;
                Invalidate();
            }
        }

        private SKPath DrawWire(SKCanvas canvas, SKPaint paint, float xFrom, float yFrom, float xTo, float yTo)
        {
            var from = new SKPoint(xFrom, yFrom);
            var to = new SKPoint(xTo, yTo);

            var path = new SKPath();

            if (_wireStyle == EWireStyle.Line)
            {
                path.MoveTo(from);
                path.LineTo(to);
            }
            else
            {
                var distance = to.X - from.X;
                var spreadDistance = ((distance / 2f) / 100f) * _wireMiddlePointsSpread;

                var fromHalf = new SKPoint(from.X + distance / 2 - spreadDistance, from.Y);
                var toHalf = new SKPoint(from.X + distance / 2 + spreadDistance, to.Y);

                if (_wireStyle == EWireStyle.StepLine)
                {
                    path.MoveTo(from);
                    path.LineTo(fromHalf);
                    path.LineTo(toHalf);
                    path.LineTo(to);
                }

                if (_wireStyle == EWireStyle.Bezier)
                {
                    path.MoveTo(from);
                    path.CubicTo(fromHalf, toHalf, to);
                }
            }

            canvas.DrawPath(path, paint);
            return path;
        }

        #endregion

        #region OnDrawBackground

        private void OnDrawBackground(SKCanvas canvas, int width, int height)
        {
            if (_gridStyle == EGridStyle.None)
            {
                return;
            }

            var topLeft = inverse_transformation.MapPoint(new SKPoint(0, 0));
            var bottomRight = inverse_transformation.MapPoint(new SKPoint(width, height));

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

        #endregion

        #region GetMarqueRectangle

        private SKRect GetMarqueRectangle()
        {
            var transformedLocation = GetTransformedLocation();
            var x1 = transformedLocation.X;
            var y1 = transformedLocation.Y;
            var x2 = originalLocation.X;
            var y2 = originalLocation.Y;
            var x = Math.Min(x1, x2);
            var y = Math.Min(y1, y2);
            var width = Math.Max(x1, x2) - x;
            var height = Math.Max(y1, y2) - y;
            return new SKRect(x, y, x + width, y + height);
        }

        #endregion

        #region OnMouseWheel

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            var mousePosition = new SKPoint(e.Location.X, e.Location.Y);

            // Get world position under mouse before zoom
            var worldPosition = inverse_transformation.MapPoint(mousePosition);

            // zoom in (mouse wheel ↑)
            if (e.Delta > 0)
            {
                zoom += 0.05f;
            }

            // zoom out (mouse wheel ↓)
            if (e.Delta < 0)
            {
                zoom -= 0.1f;
            }

            UpdateMatrices();

            // Calculate where that world position is now in screen space
            var newScreenPosition = transformation.MapPoint(worldPosition);

            // Adjust translation to keep the world position under the mouse
            translation.X += mousePosition.X - newScreenPosition.X;
            translation.Y += mousePosition.Y - newScreenPosition.Y;

            UpdateMatrices();
            Invalidate();
        }

        #endregion

        #region MouseProperties

        private bool leftMouseButton;
        private bool rightMouseButton;

        private NodeUIElement lastHover;

        private void UpdateOriginalLocation(Point location)
        {
            var skLocation = new SKPoint(location.X, location.Y);
            originalLocation = inverse_transformation.MapPoint(skLocation);

            snappedLocation = lastLocation = skLocation;
        }

        #endregion

        #region OnMouseDown

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            UpdateOriginalLocation(e.Location);

            var element = FindElementAtOriginal(originalLocation);

            if ((e.Button & MouseButtons.Left) != 0)
            {
                leftMouseButton = true;

                if (_command == CommandMode.Edit)
                {
                    if (element == null && Control.ModifierKeys != Keys.Shift)
                    {
                        SetNodeSelected(false);
                        Refresh();
                    }

                    if (element is AbstractNode abstractNode)
                    {
                        if (Control.ModifierKeys != Keys.Shift && !abstractNode.Selected)
                        {
                            SetNodeSelected(false);
                        }

                        if (Control.ModifierKeys == Keys.Shift)
                        {
                            abstractNode.Selected = !abstractNode.Selected;
                        }
                        else
                        {
                            abstractNode.Selected = true;
                        }

                        HandleSelection();
                        Refresh();
                    }
                }

                if (leftMouseButton && rightMouseButton)
                {
                    _command = CommandMode.ScaleView;
                    return;
                }

                if (element == null)
                {
                    // Use marquee selection if Ctrl or Shift is held
                    if (Control.ModifierKeys == Keys.Control || Control.ModifierKeys == Keys.Shift)
                    {
                        _command = CommandMode.MarqueSelection;
                    }
                    else
                    {
                        _command = CommandMode.TranslateView;
                    }
                    return;
                }



                if (element is SocketIn socketIn)
                {
                    _command = CommandMode.Wiring;

                    if (socketIn.Hub || !socketIn.IsConnected())
                    {
                        _tempWire = new Wire { From = null, To = socketIn };
                    }
                    else
                    {
                        var connection = socketIn.AllConnections[0];

                        _tempWire = new Wire { From = connection.From, To = null };
                        Disconnect(connection);
                    }

                    return;
                }

                if (element is SocketOut socketOut)
                {
                    _command = CommandMode.Wiring;
                    _tempWire = new Wire { From = socketOut };
                    return;
                }

                if (element is AbstractNode node)
                {
                    _command = CommandMode.MoveSelection;
                    BringNodeToFront(node);
                }
            }

            if ((e.Button & MouseButtons.Right) != 0)
            {
                rightMouseButton = true;

                if (leftMouseButton && rightMouseButton)
                {
                    _command = CommandMode.ScaleView;
                    return;
                }

                if (_command == CommandMode.Edit && FindElementAtMousePoint(e.Location) == null)
                {
                    rightMouseButton = false;
                    //OpenContextMenu(e.Location);
                    return;
                }
            }

            if (e.Button == MouseButtons.Middle)
            {
                _command = CommandMode.TranslateView;
            }

            var transformedPoint = transformation.MapPoint(originalLocation);
            originalMouseLocation = new SKPoint(transformedPoint.X, transformedPoint.Y);
        }

        private void BringNodeToFront(AbstractNode node)
        {
            if (_graphNodes.Remove(node))
            {
                _graphNodes.Add(node);
            }

            Refresh();
        }

        #endregion

        #region OnMouseMove

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var currentLocation = new SKPoint(e.Location.X, e.Location.Y);
            var transformed_location = inverse_transformation.MapPoint(currentLocation);

            var deltaX = (lastLocation.X - currentLocation.X) / zoom;
            var deltaY = (lastLocation.Y - currentLocation.Y) / zoom;

            switch (_command)
            {
                case CommandMode.TranslateView:
                    {
                        if (!mouseMoved)
                        {
                            if ((Math.Abs(deltaX) > 1) ||
                                (Math.Abs(deltaY) > 1))
                            {
                                mouseMoved = true;
                            }
                        }

                        if (mouseMoved &&
                            (Math.Abs(deltaX) > 0) ||
                            (Math.Abs(deltaY) > 0))
                        {
                            translation.X -= deltaX * zoom;
                            translation.Y -= deltaY * zoom;
                            snappedLocation = lastLocation = currentLocation;
                            Invalidate();
                        }

                        return;
                    }
                case CommandMode.MoveSelection:
                    {
                        foreach (var node in _graphNodes.Where(node => node.Selected))
                        {
                            node.Location = new SKPoint((int)Math.Round(node.Location.X - deltaX),
                                (int)Math.Round(node.Location.Y - deltaY));
                            node.Calculate();
                        }

                        snappedLocation = lastLocation = currentLocation;
                        Invalidate();
                        return;
                    }
                case CommandMode.Wiring:
                    {
                        Invalidate();
                        return;
                    }
                case CommandMode.MarqueSelection:
                    if (!mouseMoved)
                    {
                        if ((Math.Abs(deltaX) > 1) ||
                            (Math.Abs(deltaY) > 1))
                        {
                            mouseMoved = true;
                        }
                    }

                    if (mouseMoved &&
                        (Math.Abs(deltaX) > 0) ||
                        (Math.Abs(deltaY) > 0))
                    {
                        var marque_rectangle = GetMarqueRectangle();

                        foreach (var node in _graphNodes)
                        {
                            var contains = marque_rectangle.Contains(node.Pivot);
                            node.Selected = contains || (Control.ModifierKeys == Keys.Shift && node.Selected);
                        }

                        snappedLocation = lastLocation = currentLocation;
                        Invalidate();
                    }

                    return;

                default:
                    {
                        var element = FindElementAtOriginal(transformed_location);

                        if (lastHover != element && (lastHover is Wire || element is Wire))
                        {
                            lastHover = element;
                            Invalidate();
                            return;
                        }
                    }
                    break;
            }
        }

        #endregion

        #region OnMouseUp

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            UpdateOriginalLocation(e.Location);

            var element = FindElementAtOriginal(originalLocation);

            if ((e.Button & MouseButtons.Left) != 0)
            {
                leftMouseButton = false;

                if (_command == CommandMode.ScaleView)
                {
                    _command = CommandMode.Edit;
                    return;
                }

                if (_command == CommandMode.TranslateView)
                {
                    _command = CommandMode.Edit;
                    return;
                }

                if (_command == CommandMode.MarqueSelection)
                {
                    _command = CommandMode.Edit;
                    HandleSelection();
                    Refresh();
                    return;
                }

                if (_command == CommandMode.MoveSelection)
                {
                    _command = CommandMode.Edit;
                    return;
                }

                if (_command == CommandMode.Wiring && _tempWire != null)
                {
                    if (_tempWire.From != null && element is SocketIn @socketIn)
                    {
                        Connect(_tempWire.From, @socketIn);
                    }

                    if (_tempWire.To != null && element is SocketOut @socketOut)
                    {
                        Connect(@socketOut, _tempWire.To);
                    }

                    _tempWire = null;
                    _command = CommandMode.Edit;
                    Refresh();
                    return;
                }
            }

            if ((e.Button & MouseButtons.Right) != 0)
            {
                rightMouseButton = false;
                if (_command == CommandMode.ScaleView)
                {
                    _command = CommandMode.Edit;
                    return;
                }
            }

            if ((e.Button & MouseButtons.Middle) != 0)
            {
                if (_command == CommandMode.TranslateView)
                {
                    _command = CommandMode.Edit;
                    return;
                }
            }

            if (!dragging)
            {
                return;
            }

            try
            {
                var currentLocation = new SKPoint(e.Location.X, e.Location.Y);
                var transformed_location = inverse_transformation.MapPoint(currentLocation);

                switch (_command)
                {
                    case CommandMode.MarqueSelection:
                        Invalidate();
                        return;
                    case CommandMode.ScaleView:
                        return;
                    case CommandMode.TranslateView:
                        return;

                    default:
                    case CommandMode.Edit:
                        break;
                }
            }
            finally
            {
                dragging = false;
                _command = CommandMode.Edit;

                base.OnMouseUp(e);
            }
        }

        #endregion

        #region OnMouseClick

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            if (e.Button == MouseButtons.Left)
            {
            }

            if (e.Button == MouseButtons.Right)
            {
                // TODO context menu
            }
        }

        #endregion

        #region OnKeyDown

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // reset view
            if (e.KeyCode == Keys.Space)
            {
                ResetView();
            }

            // show bounds (dev)
            if (e.KeyCode == Keys.X)
            {
                _renderBounds = true;
                Refresh();
            }

            // focus view to center of the selection
            if (e.KeyCode == Keys.F)
            {

                var count = 0;
                double x = 0, y = 0;

                foreach (var node in _graphNodes.Where(node => node.Selected))
                {
                    x += node.Pivot.X;
                    y += node.Pivot.Y;
                    count++;
                }

                if (count == 0)
                {
                    return;
                }

                var avgPoint = new SKPoint((float)(x / count), (float)(y / count));
                FocusView(avgPoint);
            }

            // back to edit mode
            if ((e.KeyData & Keys.Escape) == Keys.Escape)
            {
                _command = CommandMode.Edit;
            }

            // select all
            if (e.Control && e.KeyCode == Keys.A)
            {
                SetNodeSelected(true);
                Refresh();
            }

            // deselect all
            if (e.Control && e.Shift && e.KeyCode == Keys.A)
            {
                SetNodeSelected(false);
                Refresh();
            }

            // delete selected
            if ((e.KeyData & Keys.Delete) == Keys.Delete)
            {
                DeleteSelectedNodes();
            }
        }

        #endregion

        #region OnKeyUp

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);

            // hide bounds (dev)
            if ((e.KeyData & Keys.X) == Keys.X)
            {
                _renderBounds = false;
                Refresh();
            }
        }

        #endregion

        #region SpaceInMatrix

        protected void ResetView()
        {
            translation.X = (Width / 2f);
            translation.Y = (Height / 2f);
            zoom = 1f;
            Refresh();
        }

        protected void FocusView(SKPoint focusPoint)
        {
            var translatedLocation = GetOriginalPosition(focusPoint);
            translation.X -= translatedLocation.X - Width / 2f;
            translation.Y -= translatedLocation.Y - Height / 2f;
            Invalidate();
        }

        private SKPoint GetTranslatedPosition(Point mouseClick)
        {
            return inverse_transformation.MapPoint(new SKPoint(mouseClick.X, mouseClick.Y));
        }

        private SKPoint GetTranslatedPosition(SKPoint positionInsideClip)
        {
            return inverse_transformation.MapPoint(positionInsideClip);
        }

        private SKPoint GetOriginalPosition(SKPoint transformed)
        {
            return transformation.MapPoint(transformed);
        }

        private NodeUIElement FindElementAtOriginal(SKPoint point)
        {
            foreach (var node in _graphNodes)
            {
                // find socket
                foreach (var socket in node.Sockets.Where(socket => !socket.DisplayOnly && socket.BoundsFull.Contains(point)))
                {
                    if (socket.GetType() == typeof(SocketIn))
                    {
                        return (SocketIn)socket;
                    }

                    if (socket.GetType() == typeof(SocketOut))
                    {
                        return (SocketOut)socket;
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

        public NodeUIElement FindElementAtMousePoint(Point mouseClickPosition)
        {
            var position = GetTranslatedPosition(mouseClickPosition);
            return FindElementAtOriginal(position);
        }

        #endregion

        #region NodeSelection

        private List<AbstractNode> lastSelected = [];

        private void HandleSelection()
        {
            var selected = _graphNodes.Where(node => node.Selected).ToList();

            if (lastSelected.Count != selected.Count)
            {
                SelectionChanged?.Invoke(this, selected);
                lastSelected = selected;
            }
            else if (!lastSelected.SequenceEqual(selected))
            {
                SelectionChanged?.Invoke(this, selected);
                lastSelected = selected;
            }
        }

        [GeneratedRegex(@"((?<=\p{Ll})\p{Lu})|((?!\A)\p{Lu}(?>\p{Ll}))")]
        private static partial Regex MyRegex();

        #endregion
    }
}
