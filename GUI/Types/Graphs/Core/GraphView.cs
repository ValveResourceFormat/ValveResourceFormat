using System.Linq;
using System.Windows.Forms;
using SkiaSharp;

namespace GUI.Types.Graphs.Core;

/// <summary>
/// Holds a graph document (nodes and wires), selection and hover state, hit testing and automatic
/// layout. Rendering lives in the Render partial. Hosted by GLGraphViewer.
/// </summary>
partial class GraphView : IDisposable
{
    public GraphPalette Palette { get; }

    private readonly List<GraphNode> nodes = [];
    private readonly List<GraphWire> wires = [];
    private readonly Dictionary<GraphWire, SKPath> wireHitPaths = [];

    private IGraphElement? lastHovered;
    private GraphNode? primarySelectedNode;
    private readonly HashSet<GraphNode> connectedNodes = [];

    public bool IsMoving { get; private set; }
    private SKPoint lastLocation;

    // Synchronizes graph state between the render thread and UI mouse handlers.
    private readonly System.Threading.Lock stateLock = new();

    public event EventHandler? GraphChanged;

    private void OnGraphChanged() => GraphChanged?.Invoke(this, EventArgs.Empty);

    public GraphView(GraphPalette? palette = null)
    {
        Palette = palette ?? GraphPalette.ForCurrentTheme();
    }

    public T AddNode<T>(T node) where T : GraphNode
    {
        nodes.Add(node);
        return node;
    }

    public GraphWire Connect(GraphSocket from, GraphSocket to, bool dashed = false, string? label = null)
    {
        if (from.IsInput || !to.IsInput)
        {
            throw new ArgumentException("Wires connect an output socket to an input socket.");
        }

        if (to.Wires.Any(existing => existing.From == from))
        {
            throw new InvalidOperationException("Connection already exists");
        }

        if (!to.AllowMultiple)
        {
            foreach (var existing in to.Wires.ToArray())
            {
                RemoveWire(existing);
            }
        }

        var wire = new GraphWire(from, to) { Dashed = dashed, Label = label };
        from.Wires.Add(wire);
        to.Wires.Add(wire);
        wires.Add(wire);
        return wire;
    }

    private void RemoveWire(GraphWire wire)
    {
        wire.From.Wires.Remove(wire);
        wire.To.Wires.Remove(wire);
        wires.Remove(wire);

        if (wireHitPaths.Remove(wire, out var path))
        {
            path.Dispose();
        }
    }

    private void ClearWireHitPaths()
    {
        foreach (var path in wireHitPaths.Values)
        {
            path.Dispose();
        }

        wireHitPaths.Clear();
    }

    public SKRect GetGraphBounds()
    {
        using var _ = stateLock.EnterScope();

        if (nodes.Count == 0)
        {
            return new SKRect(-1000, -1000, 1000, 1000);
        }

        EnsureAllGeometry();

        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        var anyVisible = false;

        foreach (var node in nodes)
        {
            if (node.Hidden)
            {
                continue;
            }

            anyVisible = true;
            minX = Math.Min(minX, node.Position.X);
            minY = Math.Min(minY, node.Position.Y);
            maxX = Math.Max(maxX, node.Position.X + node.Size.X);
            maxY = Math.Max(maxY, node.Position.Y + node.Size.Y);
        }

        if (!anyVisible)
        {
            return new SKRect(-1000, -1000, 1000, 1000);
        }

        const float padding = 200f;
        return new SKRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
    }

    public void HandleMouseDown(SKPoint graphPoint, MouseButtons button, Keys modifiers)
    {
        using var _ = stateLock.EnterScope();

        lastLocation = graphPoint;

        var element = FindElementAtCore(graphPoint);

        // Sockets are inert click targets; treat them as their owner node so edges stay draggable.
        if (element is GraphSocket socket)
        {
            element = socket.Owner;
        }

        if ((button & MouseButtons.Left) != 0)
        {
            if (element == null && modifiers != Keys.Shift)
            {
                ClearSelection();
            }

            if (!IsMoving && element is GraphNode selectedNode)
            {
                if (modifiers == Keys.Shift)
                {
                    ToggleSelection(selectedNode);
                }
                else
                {
                    SetPrimarySelection(selectedNode);
                }
            }

            if (element is GraphNode node && primarySelectedNode != null)
            {
                IsMoving = true;
                BringNodeToFront(node);
            }
        }
    }

    public void HandleMouseMove(SKPoint graphPoint, Keys modifiers = Keys.None)
    {
        using var _ = stateLock.EnterScope();

        if (IsMoving)
        {
            var delta = new Vector2(graphPoint.X - lastLocation.X, graphPoint.Y - lastLocation.Y);
            var moveAllConnected = (modifiers & Keys.Control) != 0;

            if (moveAllConnected && connectedNodes.Count > 0)
            {
                foreach (var node in connectedNodes)
                {
                    node.Position += delta;
                }
            }
            else if (primarySelectedNode != null)
            {
                primarySelectedNode.Position += delta;
            }

            ClearWireHitPaths();
            lastLocation = graphPoint;
            OnGraphChanged();
            return;
        }

        var element = FindElementAtCore(graphPoint);

        if (lastHovered != element)
        {
            lastHovered = element;
            OnGraphChanged();
        }
    }

    public void HandleMouseUp(SKPoint graphPoint)
    {
        using var _ = stateLock.EnterScope();

        lastLocation = graphPoint;
        IsMoving = false;
    }

    public IGraphElement? FindElementAt(SKPoint point)
    {
        using var _ = stateLock.EnterScope();
        return FindElementAtCore(point);
    }

    private IGraphElement? FindElementAtCore(SKPoint point)
    {
        const float socketHitRadius = SocketRadius + 3f;

        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];

            if (node.Hidden)
            {
                continue;
            }

            foreach (var socket in node.Inputs)
            {
                var pivot = socket.Pivot;
                if (Math.Abs(point.X - pivot.X) <= socketHitRadius && Math.Abs(point.Y - pivot.Y) <= socketHitRadius)
                {
                    return socket;
                }
            }

            foreach (var socket in node.Outputs)
            {
                var pivot = socket.Pivot;
                if (Math.Abs(point.X - pivot.X) <= socketHitRadius && Math.Abs(point.Y - pivot.Y) <= socketHitRadius)
                {
                    return socket;
                }
            }

            if (point.X >= node.Position.X && point.X <= node.Position.X + node.Size.X &&
                point.Y >= node.Position.Y && point.Y <= node.Position.Y + node.Size.Y)
            {
                return node;
            }
        }

        for (var i = wires.Count - 1; i >= 0; i--)
        {
            var wire = wires[i];

            if (wire.From.Owner.Hidden || wire.To.Owner.Hidden)
            {
                continue;
            }

            if (!wireHitPaths.TryGetValue(wire, out var path))
            {
                path = BuildWireHitPath(wire);
                wireHitPaths[wire] = path;
            }

            if (path.Contains(point.X, point.Y))
            {
                return wire;
            }
        }

        return null;
    }

    private void BringNodeToFront(GraphNode node)
    {
        if (nodes.Remove(node))
        {
            nodes.Add(node);
        }

        OnGraphChanged();
    }

    private void TraverseConnectedGraph(GraphNode startNode)
    {
        connectedNodes.Clear();
        connectedNodes.Add(startNode);

        // Upstream over inputs, downstream over outputs.
        var queue = new Queue<GraphNode>();
        queue.Enqueue(startNode);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var socket in current.Inputs)
            {
                foreach (var wire in socket.Wires)
                {
                    if (connectedNodes.Add(wire.From.Owner))
                    {
                        queue.Enqueue(wire.From.Owner);
                    }
                }
            }
        }

        queue.Enqueue(startNode);
        var downstreamVisited = new HashSet<GraphNode> { startNode };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var socket in current.Outputs)
            {
                foreach (var wire in socket.Wires)
                {
                    if (downstreamVisited.Add(wire.To.Owner))
                    {
                        connectedNodes.Add(wire.To.Owner);
                        queue.Enqueue(wire.To.Owner);
                    }
                }
            }
        }
    }

    private void SetPrimarySelection(GraphNode node)
    {
        primarySelectedNode = node;
        TraverseConnectedGraph(node);

        foreach (var connectedNode in connectedNodes)
        {
            if (connectedNode != primarySelectedNode && nodes.Remove(connectedNode))
            {
                nodes.Add(connectedNode);
            }
        }

        if (nodes.Remove(primarySelectedNode))
        {
            nodes.Add(primarySelectedNode);
        }
    }

    private void ToggleSelection(GraphNode node)
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

    private static readonly Func<GraphNode, Vector2> LayoutGetPosition = static n => n.Position;
    private static readonly Action<GraphNode, Vector2> LayoutSetPosition = static (n, p) => n.Position = p;
    private static readonly Func<GraphNode, Vector2> LayoutGetSize = static n => n.Size;
    private static readonly Func<GraphWire, GraphNode> LayoutGetSource = static w => w.From.Owner;
    private static readonly Func<GraphWire, GraphNode> LayoutGetTarget = static w => w.To.Owner;
    private static readonly Func<GraphNode, IEnumerable<GraphWire>> LayoutGetInputs = static n => n.Inputs.SelectMany(s => s.Wires);
    private static readonly Func<GraphNode, IEnumerable<GraphWire>> LayoutGetOutputs = static n => n.Outputs.SelectMany(s => s.Wires);

    public void LayoutNodes() => RunLayout(sequential: false, 100f);

    public void LayoutNodesSequential(float nodeSpacing = 100f) => RunLayout(sequential: true, nodeSpacing);

    /// <summary>
    /// Returns the connected components of the graph (each node in exactly one list).
    /// </summary>
    public List<List<GraphNode>> GetComponents()
    {
        var visited = new HashSet<GraphNode>();
        var components = new List<List<GraphNode>>();

        foreach (var start in nodes)
        {
            if (!visited.Add(start))
            {
                continue;
            }

            var component = new List<GraphNode>();
            var stack = new Stack<GraphNode>();
            stack.Push(start);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                component.Add(node);

                foreach (var socket in node.Inputs)
                {
                    foreach (var wire in socket.Wires)
                    {
                        if (visited.Add(wire.From.Owner))
                        {
                            stack.Push(wire.From.Owner);
                        }
                    }
                }

                foreach (var socket in node.Outputs)
                {
                    foreach (var wire in socket.Wires)
                    {
                        if (visited.Add(wire.To.Owner))
                        {
                            stack.Push(wire.To.Owner);
                        }
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    /// <summary>
    /// Lays out each connected component independently and packs the components into rows.
    /// Suited for graphs made of many disjoint islands, like entity I/O.
    /// </summary>
    public void LayoutNodesPacked(float padding = 150f)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        using (stateLock.EnterScope())
        {
            EnsureAllGeometry();
        }

        var components = GetComponents();
        var componentOf = new Dictionary<GraphNode, int>();

        for (var i = 0; i < components.Count; i++)
        {
            foreach (var node in components[i])
            {
                componentOf[node] = i;
            }
        }

        for (var i = 0; i < components.Count; i++)
        {
            var component = components[i];

            if (component.Count == 1)
            {
                component[0].Position = Vector2.Zero;
                continue;
            }

            var componentWires = wires.Where(w => componentOf[w.From.Owner] == i).ToList();

            GraphLayout.LayoutNodes(
                component, componentWires,
                LayoutGetPosition, LayoutSetPosition, LayoutGetSize,
                LayoutGetSource, LayoutGetTarget, LayoutGetInputs, LayoutGetOutputs);
        }

        // Shelf-pack component bounding boxes into rows, tallest first.
        var boxes = new List<(List<GraphNode> Component, Vector2 Min, Vector2 Size)>(components.Count);
        var totalArea = 0f;

        foreach (var component in components)
        {
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);

            foreach (var node in component)
            {
                min = Vector2.Min(min, node.Position);
                max = Vector2.Max(max, node.Position + node.Size);
            }

            var size = max - min;
            boxes.Add((component, min, size));
            totalArea += (size.X + padding) * (size.Y + padding);
        }

        boxes.Sort(static (a, b) => b.Size.Y.CompareTo(a.Size.Y));

        var targetWidth = MathF.Max(3000f, MathF.Sqrt(totalArea * 2f));
        var x = 0f;
        var y = 0f;
        var rowHeight = 0f;

        foreach (var (component, min, size) in boxes)
        {
            if (x > 0f && x + size.X > targetWidth)
            {
                x = 0f;
                y += rowHeight + padding;
                rowHeight = 0f;
            }

            var offset = new Vector2(x, y) - min;

            foreach (var node in component)
            {
                node.Position += offset;
            }

            x += size.X + padding;
            rowHeight = MathF.Max(rowHeight, size.Y);
        }

        using (stateLock.EnterScope())
        {
            ClearWireHitPaths();
        }

        OnGraphChanged();
    }

    private void RunLayout(bool sequential, float nodeSpacing)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        using (stateLock.EnterScope())
        {
            EnsureAllGeometry();
        }

        if (sequential)
        {
            SequentialGraphLayout.LayoutNodes(
                nodes, wires,
                LayoutGetPosition, LayoutSetPosition, LayoutGetSize,
                LayoutGetSource, LayoutGetTarget, LayoutGetInputs, LayoutGetOutputs,
                new GraphLayout.LayoutOptions { LayerSpacing = nodeSpacing });
        }
        else
        {
            GraphLayout.LayoutNodes(
                nodes, wires,
                LayoutGetPosition, LayoutSetPosition, LayoutGetSize,
                LayoutGetSource, LayoutGetTarget, LayoutGetInputs, LayoutGetOutputs);
        }

        using (stateLock.EnterScope())
        {
            ClearWireHitPaths();
        }

        OnGraphChanged();
    }

    private bool disposed;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposed)
        {
            if (disposing)
            {
                foreach (var path in wireHitPaths.Values)
                {
                    path.Dispose();
                }

                wireHitPaths.Clear();
                DisposeRenderResources();
            }

            disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
