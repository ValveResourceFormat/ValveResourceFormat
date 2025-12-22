using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using NodeGraphControl.Elements;

#nullable disable

namespace NodeGraphControl
{
    public partial class NodeGraphControl : Control
    {
        #region Constructor

        public NodeGraphControl()
        {
            InitializeComponent();
            SetStyle(
                ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.UserPaint, true);
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

        public static void AddTypeColorPair<T>(Color color)
        {
            CommonStates.TypeColor.TryAdd(typeof(T), color);
        }

        public void LayoutNodes(float padding = 20f)
        {
            if (_graphNodes.Count == 0)
            {
                return;
            }

            const int maxIterations = 100;

            // Simple overlap removal algorithm using iterative repositioning
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                bool hasOverlap = false;

                for (int i = 0; i < _graphNodes.Count; i++)
                {
                    var nodeA = _graphNodes[i];
                    nodeA.Calculate();

                    for (int j = i + 1; j < _graphNodes.Count; j++)
                    {
                        var nodeB = _graphNodes[j];
                        nodeB.Calculate();

                        // Check if nodes overlap
                        var boundsA = nodeA.BoundsFull;
                        var boundsB = nodeB.BoundsFull;

                        // Add padding to bounds for overlap check
                        var paddedBoundsA = new RectangleF(
                            boundsA.X - padding / 2,
                            boundsA.Y - padding / 2,
                            boundsA.Width + padding,
                            boundsA.Height + padding);

                        var paddedBoundsB = new RectangleF(
                            boundsB.X - padding / 2,
                            boundsB.Y - padding / 2,
                            boundsB.Width + padding,
                            boundsB.Height + padding);

                        if (paddedBoundsA.IntersectsWith(paddedBoundsB))
                        {
                            hasOverlap = true;

                            // Calculate separation vector
                            var centerA = new PointF(nodeA.Pivot.X, nodeA.Pivot.Y);
                            var centerB = new PointF(nodeB.Pivot.X, nodeB.Pivot.Y);

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
                                nodeA.Location = new Point(
                                    (int)Math.Round(nodeA.Location.X - moveX),
                                    nodeA.Location.Y);
                                nodeB.Location = new Point(
                                    (int)Math.Round(nodeB.Location.X + moveX),
                                    nodeB.Location.Y);
                            }
                            else
                            {
                                nodeA.Location = new Point(
                                    nodeA.Location.X,
                                    (int)Math.Round(nodeA.Location.Y - moveY));
                                nodeB.Location = new Point(
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

        public event EventHandler<List<AbstractNode>> SelectionChanged;

        public event EventHandler<float> ZoomChanged;

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
        private Color _gridColor = Color.LightGray;
        private Pen _gridPen = new(Color.LightGray);
        private Brush _gridBrush = new SolidBrush(Color.LightGray);

        [Description("The color for the grid lines with the largest gap between them"), Category("Appearance")]
        public Color GridColor
        {
            get { return _gridColor; }
            set
            {
                if (_gridColor == value)
                {
                    return;
                }

                _gridColor = value;
                _gridPen = new Pen(_gridColor);
                _gridBrush = new SolidBrush(_gridColor);
                Invalidate();
            }
        }

        // canvas background color
        private Color _canvasBackgroundColor = Color.FromArgb(23, 25, 31);

        [Description("The background color of the canvas"), Category("Appearance")]
        public Color CanvasBackgroundColor
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

        Point lastLocation;
        PointF snappedLocation;
        PointF originalLocation;
        Point originalMouseLocation;

        #endregion

        #region UpdateMatrices

        PointF translation;
        float zoom = 1.0f;
        private float zoomLast;

        readonly Matrix transformation = new();
        readonly Matrix inverse_transformation = new();

        private void UpdateMatrices()
        {
            zoom = Math.Clamp(zoom, 0.25f, 4.00f);

            if (Math.Abs(zoom - zoomLast) > 0.01f)
            {
                zoomLast = zoom;
                ZoomChanged?.Invoke(this, zoom);
            }

            transformation.Reset();
            transformation.Translate(translation.X, translation.Y);
            transformation.Scale(zoom, zoom);

            inverse_transformation.Reset();
            inverse_transformation.Scale(1.0f / zoom, 1.0f / zoom);
            inverse_transformation.Translate(-translation.X, -translation.Y);
        }

        #endregion

        #region GetTransformedLocation
        // TODO refactor
        private PointF GetTransformedLocation()
        {
            var points = new[] { snappedLocation };
            inverse_transformation.TransformPoints(points);
            var transformed_location = points[0];
            return transformed_location;
        }

        #endregion

        #region OnPaint

        // temp
        private bool _renderBounds;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // initialization and settings
            var g = e.Graphics;

            g.PageUnit = GraphicsUnit.Pixel;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // CompositingQuality dependent on zoom value.
            // Close view uses HightSpeed, distant view is GammaCorrected (high quality)
            g.CompositingQuality = zoom < 1f ? CompositingQuality.GammaCorrected : CompositingQuality.HighSpeed;

            // update matrices
            UpdateMatrices();
            g.Transform = transformation;

            // draw background
            OnDrawBackground(e);

            // temp crosshair
            using var pW = new Pen(Color.Gray, 3f);
            g.DrawLine(pW, -_gridStep, 0, _gridStep, 0);
            g.DrawLine(pW, 0, -_gridStep, 0, _gridStep);

            // return if no nodes
            if (_graphNodes.Count == 0)
            {
                return;
            }

            // set smoothing mode quality
            g.SmoothingMode = SmoothingMode.HighQuality;

            // draw all wires
            foreach (var wire in _connections)
            {
                var xFrom = wire.From.BoundsFull.X + wire.From.BoundsFull.Width / 2f;
                var yFrom = wire.From.BoundsFull.Y + wire.From.BoundsFull.Height / 2f;
                var xTo = wire.To.BoundsFull.X + wire.To.BoundsFull.Width / 2;
                var yTo = wire.To.BoundsFull.Y + wire.To.BoundsFull.Height / 2;

                // skip wire if there is no distance between two points
                if (Vector2.Distance(new Vector2(xFrom, yFrom), new Vector2(xTo, yTo)) < 1f)
                {
                    continue;
                }

                // draw wire
                var wireColor = CommonStates.GetColorByType(wire.From.ValueType);
                var wireWidth = (wire == lastHover) ? 4f : 3f;
                using var pen = new Pen(wireColor, wireWidth);
                using var wirePath = DrawWire(g, pen, xFrom, yFrom, xTo, yTo);

                // create wire region
                using var widePen = new Pen(Color.Empty, 10f);
                wirePath.Widen(widePen);
                var wireRegion = new Region(wirePath);
                wire.Region = wireRegion;

                // and eventually draw it
                if (_renderBounds)
                {
                    using var boundsBrush = new SolidBrush(Color.Brown);
                    g.FillRegion(boundsBrush, wireRegion);
                }
            }

            // draw all nodes
            foreach (var node in _graphNodes)
            {
                node.Draw(g);
            }

            // render bounds
            if (_renderBounds)
            {
                using var pen = new Pen(Color.Aqua);
                foreach (var node in _graphNodes)
                {
                    foreach (var socket in node.Sockets)
                    {
                        g.DrawRectangle(pen, socket.BoundsFull.X, socket.BoundsFull.Y,
                            socket.BoundsFull.Width,
                            socket.BoundsFull.Height);
                    }

                    g.DrawRectangle(pen, node.BoundsHeader.X, node.BoundsHeader.Y, node.BoundsHeader.Width, node.BoundsHeader.Height);
                    g.DrawRectangle(pen, node.BoundsBase.X, node.BoundsBase.Y, node.BoundsBase.Width, node.BoundsBase.Height);
                    g.DrawRectangle(pen, node.BoundsFooter.X, node.BoundsFooter.Y, node.BoundsFooter.Width, node.BoundsFooter.Height);
                }
            }

            // draw temp wire during wiring mode
            if (_command == CommandMode.Wiring && _tempWire != null)
            {
                float xFrom, yFrom, xTo, yTo;

                var cursorPoint = GetTranslatedPosition(PointToClient(Cursor.Position));
                using var tempWirePen = new Pen(Color.White, 2f);

                if (_tempWire.From != null)
                {
                    xFrom = _tempWire.From.BoundsFull.X + _tempWire.From.BoundsFull.Width / 2f;
                    yFrom = _tempWire.From.BoundsFull.Y + _tempWire.From.BoundsFull.Height / 2f;
                    xTo = cursorPoint.X;
                    yTo = cursorPoint.Y;

                    using var _ = DrawWire(g, tempWirePen, xFrom, yFrom, xTo, yTo);
                }
                else if (_tempWire.To != null)
                {
                    xFrom = cursorPoint.X;
                    yFrom = cursorPoint.Y;
                    xTo = _tempWire.To.BoundsFull.X + _tempWire.To.BoundsFull.Width / 2;
                    yTo = _tempWire.To.BoundsFull.Y + _tempWire.To.BoundsFull.Height / 2;

                    using var _ = DrawWire(g, tempWirePen, xFrom, yFrom, xTo, yTo);
                }
            }

            // draw marque
            if (_command == CommandMode.MarqueSelection)
            {
                var marqueRectangle = GetMarqueRectangle();
                using var marqueBrush = new SolidBrush(Color.FromArgb(15, 64, 64, 127));
                g.FillRectangle(marqueBrush, marqueRectangle);
                g.DrawRectangle(Pens.DarkGray, marqueRectangle.X, marqueRectangle.Y, marqueRectangle.Width,
                    marqueRectangle.Height);
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

        private GraphicsPath DrawWire(Graphics g, Pen pen, float xFrom, float yFrom, float xTo, float yTo)
        {
            var from = new PointF(xFrom, yFrom);
            var to = new PointF(xTo, yTo);

            var path = new GraphicsPath(FillMode.Winding);

            if (_wireStyle == EWireStyle.Line)
            {
                path.AddLine(from, to);
            }
            else
            {
                var distance = to.X - from.X;
                var spreadDistance = ((distance / 2f) / 100f) * _wireMiddlePointsSpread;

                var fromHalf = new PointF(from.X + distance / 2 - spreadDistance, from.Y);
                var toHalf = new PointF(from.X + distance / 2 + spreadDistance, to.Y);

                PointF[] pathPoints = [from, fromHalf, toHalf, to];

                if (_wireStyle == EWireStyle.StepLine)
                {
                    path.AddLines(pathPoints);
                }

                if (_wireStyle == EWireStyle.Bezier)
                {
                    path.AddBeziers(pathPoints);
                }
            }

            g.DrawPath(pen, path);
            return path;
        }

        #endregion

        #region OnDrawBackground

        private void OnDrawBackground(PaintEventArgs e)
        {
            var g = e.Graphics;

            e.Graphics.Clear(_canvasBackgroundColor);

            if (_gridStyle == EGridStyle.None)
            {
                return;
            }

            var points = new PointF[] {
                new(e.ClipRectangle.Left, e.ClipRectangle.Top),
                new(e.ClipRectangle.Right, e.ClipRectangle.Bottom)
            };

            inverse_transformation.TransformPoints(points);

            var left = points[0].X;
            var right = points[1].X;
            var top = points[0].Y;
            var bottom = points[1].Y;

            var largeXOffset = ((float)Math.Round(left / _gridStep) * _gridStep);
            var largeYOffset = ((float)Math.Round(top / _gridStep) * _gridStep);

            // grid
            if (_gridStyle == EGridStyle.Grid)
            {
                for (var x = largeXOffset; x < right; x += _gridStep)
                {
                    g.DrawLine(_gridPen, x, top, x, bottom);
                }

                for (var y = largeYOffset; y < bottom; y += _gridStep)
                {
                    g.DrawLine(_gridPen, left, y, right, y);
                }
            }

            // dots
            if (_gridStyle == EGridStyle.Dots)
            {
                for (var x = largeXOffset; x < right; x += _gridStep)
                {
                    for (var y = largeYOffset; y < bottom; y += _gridStep)
                    {
                        g.FillRectangle(_gridBrush, x, y, 2, 2);
                    }
                }
            }
        }

        #endregion

        #region GetMarqueRectangle

        private RectangleF GetMarqueRectangle()
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
            return new RectangleF(x, y, width, height);
        }

        #endregion

        #region OnMouseWheel

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            var mousePosition = new PointF(e.Location.X, e.Location.Y);

            // Get world position under mouse before zoom
            Span<PointF> points = [mousePosition];
            inverse_transformation.TransformPoints(points);
            var worldPosition = points[0];

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
            Span<PointF> screenPoints = [worldPosition];
            transformation.TransformPoints(screenPoints);
            var newScreenPosition = screenPoints[0];

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
            var points = new PointF[] { location };
            inverse_transformation.TransformPoints(points);
            var transformed_location = points[0];

            originalLocation = transformed_location;

            snappedLocation = lastLocation = location;
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

            var points = new[] { originalLocation };
            transformation.TransformPoints(points);
            originalMouseLocation = PointToScreen(new Point((int)points[0].X, (int)points[0].Y));
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

            var currentLocation = e.Location;
            var points = new PointF[] { currentLocation };
            inverse_transformation.TransformPoints(points);
            var transformed_location = points[0];

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
                            node.Location = new Point((int)Math.Round(node.Location.X - deltaX),
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
                            bool contains = marque_rectangle.Contains(node.Pivot);
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
                var currentLocation = e.Location;

                var points = new PointF[] { currentLocation };
                inverse_transformation.TransformPoints(points);
                var transformed_location = points[0];

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

                int count = 0;
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

                var avgPoint = new PointF((float)(x / count), (float)(y / count));
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

        protected void FocusView(PointF focusPoint)
        {
            var translatedLocation = GetOriginalPosition(new PointF(focusPoint.X, focusPoint.Y));
            translation.X -= translatedLocation.X - Width / 2f;
            translation.Y -= translatedLocation.Y - Height / 2f;
            Invalidate();
        }

        private PointF GetTranslatedPosition(Point mouseClick)
        {
            var points = new PointF[] { mouseClick };
            inverse_transformation.TransformPoints(points);
            return points[0];
        }

        private PointF GetTranslatedPosition(PointF positionInsideClip)
        {
            var points = new PointF[] { positionInsideClip };
            inverse_transformation.TransformPoints(points);
            return points[0];
        }

        private PointF GetOriginalPosition(PointF transformed)
        {
            var points = new[] { transformed };
            transformation.TransformPoints(points);
            return points[0];
        }

        private NodeUIElement FindElementAtOriginal(PointF point)
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
            for (int i = _connections.Count - 1; i >= 0; i--)
            {
                var wire = _connections[i];
                if (wire.Region != null && wire.Region.IsVisible(point))
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
        /*
                #region ContextMenu

                private Point _contextMenuMouseClick;


                private void OpenContextMenu(Point location)
                {
                    _contextMenuMouseClick = location;

                    var contextMenu = new ContextMenu();
                    var categories = new List<MenuItem>();

                    var menuItemAdd = new MenuItem("Add Node");
                    var menuItemExit = new MenuItem("Exit", MenuItemClickExit); // TODO remove temp exit item

                    foreach (var contextNode in _contextNodeList)
                    {
                        var cnName = contextNode.NodeName;
                        var cnCat = contextNode.NodeCategory;

                        if (cnCat.Equals(""))
                            menuItemAdd.MenuItems.Add(cnName, MenuItemClickNode);
                        else
                        {
                            var categoryMenuItem = categories.Find(item => item.Text.Equals(cnCat));
                            if (categoryMenuItem != null)
                            {
                                categoryMenuItem.MenuItems.Add(new MenuItem(cnName, MenuItemClickNode));
                            }
                            else
                            {
                                categoryMenuItem = new MenuItem(cnCat);
                                categoryMenuItem.MenuItems.Add(new MenuItem(cnName, MenuItemClickNode));
                                categories.Add(categoryMenuItem);
                            }
                        }
                    }

                    foreach (var category in categories)
                    {
                        menuItemAdd.MenuItems.Add(category);
                    }

                    contextMenu.MenuItems.AddRange(new[] {
                        menuItemAdd, menuItemExit
                    });

                    contextMenu.Show(this, location);
                }

                // TODO remove this... exit will be handled by application
                private void MenuItemClickExit(object sender, EventArgs e) {
                    Application.Exit();
                }

                private void MenuItemClickNode(object sender, EventArgs e) {
                    if (!(sender is MenuItem item)) return;

                    var contextNode = _contextNodeList.Find(context => context.NodeName.Equals(item.Text));
                    var nodeType = contextNode.NodeType;

                    var newNodeObj = (AbstractNode) Activator.CreateInstance(nodeType);

                    var locationTranslated = GetTranslatedPosition(_contextMenuMouseClick);

                    newNodeObj.Location = new Point((int) locationTranslated.X, (int) locationTranslated.Y);
                    newNodeObj.Calculate();
                    newNodeObj.Execute();

                    AddNode(newNodeObj);

                    Refresh();
                }

                #endregion
        */
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
