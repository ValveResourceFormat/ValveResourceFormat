using System.Linq;
using System.Windows.Forms;
using SkiaSharp;

namespace GUI.Types.Graphs.Core;

enum GraphLayoutStyle
{
    Layered,
    LayeredV2,
    CompactV2,
    WideV2,
    SquareBlocks,
    FanGrids,
    CollapsedFans,
    SequentialChains,
    GridByClass,
    Organic,
    RelaxedSprings,
    Msagl,
    MsaglSpecialNodes,
    MsaglCombined,
}

/// <summary>
/// Holds a graph document (nodes and wires), selection and hover state, hit testing and automatic
/// layout. Rendering lives in the Render partial. Hosted by GLGraphViewer.
/// </summary>
partial class GraphView : IDisposable
{
    public GraphPalette Palette { get; }

    /// <summary>Resolves a node's <see cref="GraphNode.IconKey"/> to the icon drawn in its left gutter.</summary>
    public Func<string, SKImage?>? IconResolver { get; set; }

    private readonly List<GraphNode> nodes = [];
    private readonly List<GraphWire> wires = [];
    private readonly Dictionary<GraphWire, SKPath> wireHitPaths = [];

    private IGraphElement? lastHovered;
    private GraphNode? primarySelectedNode;
    private GraphWire? selectedWire;
    private string? searchHighlight;
    private readonly HashSet<GraphNode> connectedNodes = [];
    private readonly HashSet<GraphNode> directNodes = [];
    private readonly HashSet<GraphNode> directInNodes = [];
    private readonly HashSet<GraphNode> directOutNodes = [];

    public bool IsMoving { get; private set; }
    private SKPoint lastLocation;

    // Synchronizes graph state between the render thread and UI mouse handlers.
    private readonly System.Threading.Lock stateLock = new();

    public event EventHandler? GraphChanged;

    public int NodeCount => nodes.Count;
    public int WireCount => wires.Count;

    public GraphNode? PrimarySelectedNode
    {
        get
        {
            using var _ = stateLock.EnterScope();
            return primarySelectedNode;
        }
    }

    public GraphWire? SelectedWire
    {
        get
        {
            using var _ = stateLock.EnterScope();
            return selectedWire;
        }
    }

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

            if (element is GraphWire wire)
            {
                SelectWire(wire);
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
                    ClearWireWaypoints(node);
                }
            }
            else if (primarySelectedNode != null)
            {
                primarySelectedNode.Position += delta;
                ClearWireWaypoints(primarySelectedNode);
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

    // Dragging a node invalidates its wires' routed lanes; they fall back to direct curves.
    private static void ClearWireWaypoints(GraphNode node)
    {
        foreach (var socket in node.Inputs)
        {
            foreach (var wire in socket.Wires)
            {
                wire.Waypoints = null;
            }
        }

        foreach (var socket in node.Outputs)
        {
            foreach (var wire in socket.Wires)
            {
                wire.Waypoints = null;
            }
        }
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
        TraverseConnectedGraph(startNode, connectedNodes);
    }

    private static void TraverseConnectedGraph(GraphNode startNode, HashSet<GraphNode> connectedNodes)
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

    // Direct wire neighbors of the primary selection; everything else renders dimmed.
    // Upstream and downstream are tracked separately for directional highlighting.
    private void CollectDirectNeighbors(GraphNode node)
    {
        directNodes.Clear();
        directInNodes.Clear();
        directOutNodes.Clear();

        foreach (var socket in node.Inputs)
        {
            foreach (var wire in socket.Wires)
            {
                directNodes.Add(wire.From.Owner);
                directInNodes.Add(wire.From.Owner);
            }
        }

        foreach (var socket in node.Outputs)
        {
            foreach (var wire in socket.Wires)
            {
                directNodes.Add(wire.To.Owner);
                directOutNodes.Add(wire.To.Owner);
            }
        }

        directNodes.Remove(node);
        directInNodes.Remove(node);
        directOutNodes.Remove(node);
    }

    private void ClearDirectNeighbors()
    {
        directNodes.Clear();
        directInNodes.Clear();
        directOutNodes.Clear();
    }

    // Clicking a wire focuses just its two endpoint nodes; clicking it again deselects.
    private void SelectWire(GraphWire wire)
    {
        primarySelectedNode = null;
        connectedNodes.Clear();
        ClearDirectNeighbors();
        selectedWire = selectedWire == wire ? null : wire;
        OnGraphChanged();
    }

    private void SetPrimarySelection(GraphNode node)
    {
        primarySelectedNode = node;
        selectedWire = null;
        TraverseConnectedGraph(node);
        CollectDirectNeighbors(node);

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
            selectedWire = null;
            connectedNodes.Clear();
            ClearDirectNeighbors();
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
        selectedWire = null;
        connectedNodes.Clear();
        ClearDirectNeighbors();
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

    /// <summary>Removes all nodes, wires and selection state so the graph can be rebuilt.</summary>
    public void ClearGraph()
    {
        using var _ = stateLock.EnterScope();
        nodes.Clear();
        wires.Clear();
        ClearWireHitPaths();
        primarySelectedNode = null;
        selectedWire = null;
        connectedNodes.Clear();
        ClearDirectNeighbors();
        lastHovered = null;
        fanAggregates = null;
        OnGraphChanged();
    }

    /// <summary>Makes <paramref name="node"/> the primary selection.</summary>
    public void SelectNode(GraphNode node)
    {
        using var _ = stateLock.EnterScope();
        SetPrimarySelection(node);
        OnGraphChanged();
    }

    /// <summary>Live search filter: non-matching nodes render dimmed. Null or whitespace clears it.</summary>
    public void SetSearchHighlight(string? query)
    {
        var normalized = string.IsNullOrWhiteSpace(query) ? null : query;

        if (normalized == searchHighlight)
        {
            return;
        }

        searchHighlight = normalized;
        OnGraphChanged();
    }

    private bool MatchesSearchHighlight(GraphNode node) => searchHighlight == null
        || node.Title.Contains(searchHighlight, StringComparison.OrdinalIgnoreCase)
        || (node.Subtitle?.Contains(searchHighlight, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Finds the next node (cycling, starting after <paramref name="after"/>) whose title or
    /// subtitle contains <paramref name="query"/>.
    /// </summary>
    public GraphNode? FindNextNode(string query, GraphNode? after)
    {
        using var _ = stateLock.EnterScope();

        if (nodes.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var start = after != null ? nodes.IndexOf(after) + 1 : 0;

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[(start + i) % nodes.Count];

            if (node.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (node.Subtitle?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>Hides nodes whose subtitle is not in <paramref name="visibleSubtitles"/>; nodes without a subtitle stay visible.</summary>
    public void SetSubtitleFilter(IEnumerable<string> visibleSubtitles)
    {
        var visible = new HashSet<string>(visibleSubtitles, StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            if (!string.IsNullOrEmpty(node.Subtitle))
            {
                node.Hidden = !visible.Contains(node.Subtitle);
            }
        }

        OnGraphChanged();
    }

    /// <summary>Distinct node subtitles (entity classnames, node types), sorted.</summary>
    public List<string> GetDistinctSubtitles()
    {
        var subtitles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            if (!string.IsNullOrEmpty(node.Subtitle))
            {
                subtitles.Add(node.Subtitle);
            }
        }

        return [.. subtitles];
    }

    /// <summary>Hides every node outside the transitive up/downstream chain of <paramref name="node"/>.</summary>
    public void IsolateChainOf(GraphNode node)
    {
        var chain = new HashSet<GraphNode>();
        TraverseConnectedGraph(node, chain);

        foreach (var member in nodes)
        {
            member.Hidden = !chain.Contains(member);
        }

        OnGraphChanged();
    }

    /// <summary>Hides every island except the one containing <paramref name="node"/>.</summary>
    public void FocusIslandOf(GraphNode node)
    {
        foreach (var component in GetComponents())
        {
            var visible = component.Contains(node);

            foreach (var member in component)
            {
                member.Hidden = !visible;
            }
        }

        OnGraphChanged();
    }

    public void ShowAllNodes()
    {
        foreach (var node in nodes)
        {
            node.Hidden = false;
        }

        OnGraphChanged();
    }

    public bool HasMultipleIslands() => GetComponents().Count > 1;

    public bool HasHiddenNodes() => nodes.Exists(static n => n.Hidden);

    /// <summary>
    /// Lays out each connected component independently and packs the components into rows.
    /// Suited for graphs made of many disjoint islands, like entity I/O. Hidden nodes keep
    /// their positions and do not participate in layout or packing.
    /// </summary>
    public void LayoutNodesPacked(GraphLayoutStyle style = GraphLayoutStyle.Layered, float padding = 150f)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        // The render thread enumerates nodes/wires under this lock; fan collapse adds and
        // hides nodes and wires, so the whole restructure-and-position pass must hold it.
        using var _ = stateLock.EnterScope();

        SetFanCollapse(style == GraphLayoutStyle.CollapsedFans);
        EnsureAllGeometry();

        foreach (var wire in wires)
        {
            wire.Waypoints = null;
            wire.OrthogonalWaypoints = false;
        }

        var components = GetVisibleComponents();
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

            var componentWires = wires.Where(w => componentOf.TryGetValue(w.From.Owner, out var c) && c == i && componentOf.ContainsKey(w.To.Owner)).ToList();

            LayoutComponent(style, component, componentWires);
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

                // Routed wire waypoints live in the same island space and must shift along.
                foreach (var socket in node.Outputs)
                {
                    foreach (var wire in socket.Wires)
                    {
                        if (wire.Waypoints is { } waypoints)
                        {
                            for (var w = 0; w < waypoints.Count; w++)
                            {
                                waypoints[w] += offset;
                            }
                        }
                    }
                }
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

    /// <summary>Exports the visible graph as Graphviz DOT (sizes in inches, layout left-to-right).</summary>
    public string ToDot()
    {
        using var _ = stateLock.EnterScope();
        EnsureAllGeometry();

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("digraph G {");
        builder.AppendLine("  rankdir=LR;");
        builder.AppendLine("  node [shape=box, fixedsize=true];");

        var ids = new Dictionary<GraphNode, string>();
        var next = 0;

        foreach (var node in nodes)
        {
            if (node.Hidden)
            {
                continue;
            }

            var id = $"n{next++}";
            ids[node] = id;
            var label = node.Title.Replace("\"", "\\\"", StringComparison.Ordinal);
            builder.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"  {id} [label=\"{label}\", width={node.Size.X / 72f:F2}, height={node.Size.Y / 72f:F2}];"));
        }

        foreach (var wire in wires)
        {
            if (ids.TryGetValue(wire.From.Owner, out var from) && ids.TryGetValue(wire.To.Owner, out var to))
            {
                builder.AppendLine($"  {from} -> {to};");
            }
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    /// <summary>Exports the visible graph as yEd-flavored GraphML with node geometry.</summary>
    public string ToGraphMl()
    {
        using var _ = stateLock.EnterScope();
        EnsureAllGeometry();

        static string Escape(string value) => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<graphml xmlns=\"http://graphml.graphdrawing.org/xmlns\" xmlns:y=\"http://www.yworks.com/xml/graphml\">");
        builder.AppendLine("  <key id=\"d0\" for=\"node\" yfiles.type=\"nodegraphics\"/>");
        builder.AppendLine("  <graph edgedefault=\"directed\">");

        var ids = new Dictionary<GraphNode, string>();
        var next = 0;

        foreach (var node in nodes)
        {
            if (node.Hidden)
            {
                continue;
            }

            var id = $"n{next++}";
            ids[node] = id;
            builder.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"    <node id=\"{id}\"><data key=\"d0\"><y:ShapeNode><y:Geometry x=\"{node.Position.X:F1}\" y=\"{node.Position.Y:F1}\" width=\"{node.Size.X:F1}\" height=\"{node.Size.Y:F1}\"/><y:NodeLabel>{Escape(node.Title)}</y:NodeLabel><y:Shape type=\"rectangle\"/></y:ShapeNode></data></node>"));
        }

        var edgeId = 0;

        foreach (var wire in wires)
        {
            if (ids.TryGetValue(wire.From.Owner, out var from) && ids.TryGetValue(wire.To.Owner, out var to))
            {
                builder.AppendLine($"    <edge id=\"e{edgeId++}\" source=\"{from}\" target=\"{to}\"/>");
            }
        }

        builder.AppendLine("  </graph>");
        builder.AppendLine("</graphml>");
        return builder.ToString();
    }

    private const int FanGroupMinSize = 3;
    private const float FanCellGap = 14f;

    private sealed record FanGroup(GraphNode Hub, GraphSocket HubSocket, bool LeavesFeedHub, string? Subtitle, List<GraphNode> Leaves);

    private List<(GraphNode Aggregate, List<GraphNode> Leaves)>? fanAggregates;

    private List<List<GraphNode>> GetVisibleComponents()
    {
        var visited = new HashSet<GraphNode>();
        var components = new List<List<GraphNode>>();

        foreach (var start in nodes)
        {
            if (start.Hidden || !visited.Add(start))
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
                        if (!wire.From.Owner.Hidden && visited.Add(wire.From.Owner))
                        {
                            stack.Push(wire.From.Owner);
                        }
                    }
                }

                foreach (var socket in node.Outputs)
                {
                    foreach (var wire in socket.Wires)
                    {
                        if (!wire.To.Owner.Hidden && visited.Add(wire.To.Owner))
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

    private static List<(GraphNode From, GraphNode To)> EdgePairs(List<GraphWire> componentWires)
    {
        var pairs = new List<(GraphNode From, GraphNode To)>(componentWires.Count);

        foreach (var wire in componentWires)
        {
            pairs.Add((wire.From.Owner, wire.To.Owner));
        }

        return pairs;
    }

    private static void LayoutComponent(GraphLayoutStyle style, List<GraphNode> component, List<GraphWire> componentWires)
    {
        switch (style)
        {
            case GraphLayoutStyle.LayeredV2:
            case GraphLayoutStyle.CollapsedFans:
                LayoutLayeredRouted(component, componentWires, layerSpacing: 300f, nodeSpacing: 40f, maxColumnHeight: 2200f);
                break;

            case GraphLayoutStyle.CompactV2:
                LayoutLayeredRouted(component, componentWires, layerSpacing: 150f, nodeSpacing: 22f, maxColumnHeight: 1400f);
                break;

            case GraphLayoutStyle.WideV2:
                LayoutLayeredRouted(component, componentWires, layerSpacing: 420f, nodeSpacing: 55f, maxColumnHeight: 3200f);
                break;

            case GraphLayoutStyle.SquareBlocks:
                LayoutLayeredRouted(component, componentWires, layerSpacing: 300f, nodeSpacing: 36f, maxColumnHeight: 0f, squareBlocks: true);
                break;

            case GraphLayoutStyle.FanGrids:
                LayoutFanGrids(component, componentWires);
                break;

            case GraphLayoutStyle.SequentialChains:
                SequentialGraphLayout.LayoutNodes(
                    component, componentWires,
                    LayoutGetPosition, LayoutSetPosition, LayoutGetSize,
                    LayoutGetSource, LayoutGetTarget, LayoutGetInputs, LayoutGetOutputs,
                    new GraphLayout.LayoutOptions { LayerSpacing = 100f });
                break;

            case GraphLayoutStyle.GridByClass:
                LayoutGridByClass(component);
                break;

            case GraphLayoutStyle.Organic:
                LayoutOrganic(component, componentWires);
                break;

            case GraphLayoutStyle.RelaxedSprings:
                LayoutRelaxedSprings(component, componentWires);
                break;

            case GraphLayoutStyle.Msagl:
            case GraphLayoutStyle.MsaglSpecialNodes:
            case GraphLayoutStyle.MsaglCombined:
                MsaglLayoutAdapter.Layout(component, componentWires);
                break;

            default:
                GraphLayout.LayoutNodes(
                    component, componentWires,
                    LayoutGetPosition, LayoutSetPosition, LayoutGetSize,
                    LayoutGetSource, LayoutGetTarget, LayoutGetInputs, LayoutGetOutputs);
                break;
        }
    }

    private static void LayoutLayeredRouted(List<GraphNode> component, List<GraphWire> componentWires, float layerSpacing, float nodeSpacing, float maxColumnHeight, bool squareBlocks = false)
    {
        var pairWires = new Dictionary<(GraphNode, GraphNode), List<GraphWire>>();

        foreach (var wire in componentWires)
        {
            var key = (wire.From.Owner, wire.To.Owner);

            if (!pairWires.TryGetValue(key, out var list))
            {
                list = [];
                pairWires[key] = list;
            }

            list.Add(wire);
        }

        LayeredLayout.Layout(component, EdgePairs(componentWires), layerSpacing, nodeSpacing, maxColumnHeight, squareBlocks, (pair, waypoints) =>
        {
            if (pairWires.TryGetValue(pair, out var list))
            {
                foreach (var wire in list)
                {
                    // Own copy per wire: island packing shifts waypoint lists in place.
                    wire.Waypoints = [.. waypoints];
                }
            }
        });
    }

    // Degree-one nodes grouped by the hub socket they connect to and their subtitle.
    private static List<FanGroup> GroupFans(List<GraphNode> candidates, int minSize)
    {
        var groups = new Dictionary<(GraphSocket, bool, string?), FanGroup>();
        var result = new List<FanGroup>();

        foreach (var node in candidates)
        {
            GraphSocket? hubSocket = null;
            GraphNode? neighbor = null;
            var feedsHub = false;
            var singleNeighbor = true;

            foreach (var socket in node.Inputs)
            {
                foreach (var wire in socket.Wires)
                {
                    if (neighbor == null)
                    {
                        neighbor = wire.From.Owner;
                        hubSocket = wire.From;
                        feedsHub = false;
                    }
                    else if (neighbor != wire.From.Owner)
                    {
                        singleNeighbor = false;
                    }
                }
            }

            foreach (var socket in node.Outputs)
            {
                foreach (var wire in socket.Wires)
                {
                    if (neighbor == null)
                    {
                        neighbor = wire.To.Owner;
                        hubSocket = wire.To;
                        feedsHub = true;
                    }
                    else if (neighbor != wire.To.Owner)
                    {
                        singleNeighbor = false;
                    }
                }
            }

            if (neighbor == null || neighbor == node || !singleNeighbor || hubSocket == null)
            {
                continue;
            }

            var key = (hubSocket, feedsHub, node.Subtitle);

            if (!groups.TryGetValue(key, out var group))
            {
                group = new FanGroup(neighbor, hubSocket, feedsHub, node.Subtitle, []);
                groups[key] = group;
                result.Add(group);
            }

            group.Leaves.Add(node);
        }

        result.RemoveAll(g => g.Leaves.Count < minSize);
        return result;
    }

    // Leaf fans render as compact grids: each group is laid out as one placeholder super-node
    // sized like the grid, then the leaves fill the placeholder's rectangle.
    private static void LayoutFanGrids(List<GraphNode> component, List<GraphWire> componentWires)
    {
        var groups = GroupFans(component, FanGroupMinSize);

        if (groups.Count == 0)
        {
            LayeredLayout.Layout(component, EdgePairs(componentWires), layerSpacing: 300f, nodeSpacing: 40f, maxColumnHeight: 2200f);
            return;
        }

        var placeholderOf = new Dictionary<GraphNode, GraphNode>();
        var placeholders = new List<(GraphNode Placeholder, FanGroup Group, int Columns, float CellW, float CellH)>();

        foreach (var group in groups)
        {
            var cellW = 0f;
            var cellH = 0f;

            foreach (var leaf in group.Leaves)
            {
                cellW = Math.Max(cellW, leaf.Size.X);
                cellH = Math.Max(cellH, leaf.Size.Y);
            }

            var count = group.Leaves.Count;
            var columns = Math.Max(1, (int)MathF.Round(MathF.Sqrt(count * (cellH + FanCellGap) / (cellW + FanCellGap))));
            var rows = (count + columns - 1) / columns;

            var placeholder = new GraphNode
            {
                Size = new Vector2(columns * (cellW + FanCellGap) - FanCellGap, rows * (cellH + FanCellGap) - FanCellGap),
                GeometryDirty = false,
            };

            placeholders.Add((placeholder, group, columns, cellW, cellH));

            foreach (var leaf in group.Leaves)
            {
                placeholderOf[leaf] = placeholder;
            }
        }

        var layoutNodes = new List<GraphNode>();

        foreach (var node in component)
        {
            if (!placeholderOf.ContainsKey(node))
            {
                layoutNodes.Add(node);
            }
        }

        foreach (var (placeholder, _, _, _, _) in placeholders)
        {
            layoutNodes.Add(placeholder);
        }

        var edges = new List<(GraphNode From, GraphNode To)>();

        foreach (var wire in componentWires)
        {
            var from = placeholderOf.GetValueOrDefault(wire.From.Owner, wire.From.Owner);
            var to = placeholderOf.GetValueOrDefault(wire.To.Owner, wire.To.Owner);

            if (from != to)
            {
                edges.Add((from, to));
            }
        }

        LayeredLayout.Layout(layoutNodes, edges, layerSpacing: 300f, nodeSpacing: 40f, maxColumnHeight: 2600f);

        foreach (var (placeholder, group, columns, cellW, cellH) in placeholders)
        {
            for (var i = 0; i < group.Leaves.Count; i++)
            {
                var column = i % columns;
                var row = i / columns;
                group.Leaves[i].Position = placeholder.Position + new Vector2(column * (cellW + FanCellGap), row * (cellH + FanCellGap));
            }
        }
    }

    private static void LayoutGridByClass(List<GraphNode> component)
    {
        var ordered = new List<GraphNode>(component);
        ordered.Sort(static (a, b) =>
        {
            var bySubtitle = string.Compare(a.Subtitle, b.Subtitle, StringComparison.OrdinalIgnoreCase);
            return bySubtitle != 0 ? bySubtitle : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        });

        var totalArea = 0f;

        foreach (var node in ordered)
        {
            totalArea += (node.Size.X + 60f) * (node.Size.Y + 40f);
        }

        var targetWidth = MathF.Max(1600f, MathF.Sqrt(totalArea * 1.5f));
        var x = 0f;
        var y = 0f;
        var rowHeight = 0f;

        foreach (var node in ordered)
        {
            if (x > 0f && x + node.Size.X > targetWidth)
            {
                x = 0f;
                y += rowHeight + 40f;
                rowHeight = 0f;
            }

            node.Position = new Vector2(x, y);
            x += node.Size.X + 60f;
            rowHeight = Math.Max(rowHeight, node.Size.Y);
        }
    }

    // Fruchterman-Reingold refinement seeded by the layered result.
    private static void LayoutOrganic(List<GraphNode> component, List<GraphWire> componentWires)
    {
        LayeredLayout.Layout(component, EdgePairs(componentWires), layerSpacing: 300f, nodeSpacing: 40f, maxColumnHeight: 2200f);

        var count = component.Count;

        if (count < 3 || count > 1500)
        {
            return;
        }

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        foreach (var node in component)
        {
            min = Vector2.Min(min, node.Position);
            max = Vector2.Max(max, node.Position + node.Size);
        }

        var area = MathF.Max((max.X - min.X) * (max.Y - min.Y), 500_000f);
        var k = MathF.Sqrt(area / count);

        var index = new Dictionary<GraphNode, int>(count);

        for (var i = 0; i < count; i++)
        {
            index[component[i]] = i;
        }

        var positions = new Vector2[count];

        for (var i = 0; i < count; i++)
        {
            positions[i] = component[i].Position + component[i].Size / 2f;
        }

        var edges = new List<(int A, int B)>();

        foreach (var wire in componentWires)
        {
            if (index.TryGetValue(wire.From.Owner, out var a) && index.TryGetValue(wire.To.Owner, out var b) && a != b)
            {
                edges.Add((a, b));
            }
        }

        var displacement = new Vector2[count];
        const int Iterations = 40;

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            Array.Clear(displacement);

            for (var i = 0; i < count; i++)
            {
                for (var j = i + 1; j < count; j++)
                {
                    var delta = positions[i] - positions[j];
                    var dist = MathF.Max(delta.Length(), 1f);
                    var force = k * k / dist;
                    var direction = delta / dist;
                    displacement[i] += direction * force;
                    displacement[j] -= direction * force;
                }
            }

            foreach (var (a, b) in edges)
            {
                var delta = positions[a] - positions[b];
                var dist = MathF.Max(delta.Length(), 1f);
                var force = dist * dist / k;
                var direction = delta / dist;
                displacement[a] -= direction * force;
                displacement[b] += direction * force;
            }

            var temperature = k * 2f * (1f - iteration / (float)Iterations) + 4f;

            for (var i = 0; i < count; i++)
            {
                var length = displacement[i].Length();

                if (length > 0.01f)
                {
                    positions[i] += displacement[i] / length * MathF.Min(length, temperature);
                }
            }
        }

        for (var i = 0; i < count; i++)
        {
            component[i].Position = positions[i] - component[i].Size / 2f;
        }
    }

    // Stress minimization: wires act as springs with a rest length and iterate until the system
    // relaxes; nodes only repel when their boxes get too close. Seeded by the layered result.
    private static void LayoutRelaxedSprings(List<GraphNode> component, List<GraphWire> componentWires)
    {
        LayeredLayout.Layout(component, EdgePairs(componentWires), layerSpacing: 300f, nodeSpacing: 40f, maxColumnHeight: 2200f);

        var count = component.Count;

        if (count < 2 || count > 2500)
        {
            return;
        }

        var centers = new Vector2[count];
        var halfSizes = new Vector2[count];
        var degree = new int[count];
        var index = new Dictionary<GraphNode, int>(count);

        for (var i = 0; i < count; i++)
        {
            index[component[i]] = i;
            centers[i] = component[i].Position + component[i].Size / 2f;
            halfSizes[i] = component[i].Size / 2f;
        }

        var pairs = new List<(int A, int B)>();
        var seenPairs = new HashSet<(int, int)>();

        foreach (var wire in componentWires)
        {
            if (!index.TryGetValue(wire.From.Owner, out var a) || !index.TryGetValue(wire.To.Owner, out var b) || a == b)
            {
                continue;
            }

            var key = a < b ? (a, b) : (b, a);

            if (!seenPairs.Add(key))
            {
                continue;
            }

            pairs.Add((a, b));
            degree[a]++;
            degree[b]++;
        }

        // Leaves hug their only neighbor: a wire ending in a degree-one node rests much shorter.
        var springs = new List<(int A, int B, float RestLength)>(pairs.Count);

        foreach (var (a, b) in pairs)
        {
            var slack = degree[a] == 1 || degree[b] == 1 ? 50f : 160f;
            springs.Add((a, b, halfSizes[a].Length() + halfSizes[b].Length() + slack));
        }

        var displacement = new Vector2[count];
        const int MaxIterations = 400;
        const float SpringStrength = 0.12f;
        const float CollisionMargin = 40f;
        const float CellSize = 420f;

        var grid = new Dictionary<long, List<int>>();

        void AddCollisionForces()
        {
            grid.Clear();

            for (var i = 0; i < count; i++)
            {
                var key = ((long)MathF.Floor(centers[i].X / CellSize) << 32) ^ (uint)(int)MathF.Floor(centers[i].Y / CellSize);

                if (!grid.TryGetValue(key, out var bucket))
                {
                    bucket = [];
                    grid[key] = bucket;
                }

                bucket.Add(i);
            }

            for (var i = 0; i < count; i++)
            {
                var cellX = (int)MathF.Floor(centers[i].X / CellSize);
                var cellY = (int)MathF.Floor(centers[i].Y / CellSize);

                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var key = ((long)(cellX + dx) << 32) ^ (uint)(cellY + dy);

                        if (!grid.TryGetValue(key, out var bucket))
                        {
                            continue;
                        }

                        foreach (var j in bucket)
                        {
                            if (j <= i)
                            {
                                continue;
                            }

                            var delta = centers[j] - centers[i];
                            var overlapX = halfSizes[i].X + halfSizes[j].X + CollisionMargin - MathF.Abs(delta.X);
                            var overlapY = halfSizes[i].Y + halfSizes[j].Y + CollisionMargin - MathF.Abs(delta.Y);

                            if (overlapX <= 0f || overlapY <= 0f)
                            {
                                continue;
                            }

                            // Push along the axis of least penetration.
                            if (overlapX < overlapY)
                            {
                                var push = overlapX * 0.5f * (delta.X >= 0f ? 1f : -1f);
                                displacement[i] -= new Vector2(push, 0f);
                                displacement[j] += new Vector2(push, 0f);
                            }
                            else
                            {
                                var push = overlapY * 0.5f * (delta.Y >= 0f ? 1f : -1f);
                                displacement[i] -= new Vector2(0f, push);
                                displacement[j] += new Vector2(0f, push);
                            }
                        }
                    }
                }
            }
        }

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            Array.Clear(displacement);

            // Springs pull or push toward their rest length, plus a flow-alignment term:
            // sockets exit rightward, so the cost of a wire grows as its direction deviates
            // from +X (1 - dot); the gradient rotates wire pairs toward horizontal flow.
            const float DirectionStrength = 6f;

            foreach (var (a, b, rest) in springs)
            {
                var delta = centers[b] - centers[a];
                var dist = MathF.Max(delta.Length(), 1f);
                var force = SpringStrength * (dist - rest);
                var direction = delta / dist;
                displacement[a] += direction * force;
                displacement[b] -= direction * force;

                var misalignment = 1f - direction.X;

                if (misalignment > 0.01f)
                {
                    var align = DirectionStrength * misalignment;
                    var alignForce = new Vector2(align, -direction.Y * align * 0.5f);
                    displacement[b] += alignForce;
                    displacement[a] -= alignForce;
                }
            }

            AddCollisionForces();

            var maxMove = 0f;

            for (var i = 0; i < count; i++)
            {
                // High-degree hubs move slower so fans do not make them oscillate.
                var damping = 0.9f / (1f + 0.25f * degree[i]);
                var move = displacement[i] * damping;
                var length = move.Length();

                if (length > 60f)
                {
                    move *= 60f / length;
                    length = 60f;
                }

                centers[i] += move;
                maxMove = MathF.Max(maxMove, length);
            }

            if (maxMove < 0.4f)
            {
                break;
            }
        }

        // Springs and collision balance mid-penetration, so finish with undamped
        // projection passes that push remaining box overlaps fully apart.
        for (var pass = 0; pass < 60; pass++)
        {
            Array.Clear(displacement);
            AddCollisionForces();

            var maxMove = 0f;

            for (var i = 0; i < count; i++)
            {
                var move = displacement[i];
                var length = move.Length();

                if (length > 90f)
                {
                    move *= 90f / length;
                    length = 90f;
                }

                centers[i] += move;
                maxMove = MathF.Max(maxMove, length);
            }

            if (maxMove < 0.5f)
            {
                break;
            }
        }

        for (var i = 0; i < count; i++)
        {
            component[i].Position = centers[i] - halfSizes[i];
        }
    }

    // Collapses each leaf fan into one aggregate node ("189 × snd_event_point"); aggregates are
    // created once and toggled by visibility so switching styles is lossless.
    private void SetFanCollapse(bool collapsed)
    {
        if (collapsed && fanAggregates == null)
        {
            fanAggregates = [];

            foreach (var group in GroupFans(nodes.Where(n => !n.Hidden).ToList(), FanGroupMinSize))
            {
                var leaf = group.Leaves[0];
                var aggregate = new GraphNode
                {
                    Title = $"{group.Leaves.Count} × {group.Subtitle ?? leaf.Title}",
                    Subtitle = group.Subtitle,
                    Category = leaf.Category,
                    IconKey = leaf.IconKey,
                };

                if (group.LeavesFeedHub)
                {
                    var output = aggregate.AddOutput(leaf.Outputs.Count > 0 ? leaf.Outputs[0].Name : "out", group.HubSocket.Hue);
                    Connect(output, group.HubSocket);
                }
                else
                {
                    var input = aggregate.AddInput(leaf.Inputs.Count > 0 ? leaf.Inputs[0].Name : "in", group.HubSocket.Hue, allowMultiple: true);
                    Connect(group.HubSocket, input);
                }

                AddNode(aggregate);
                fanAggregates.Add((aggregate, group.Leaves));
            }
        }

        if (fanAggregates == null)
        {
            return;
        }

        foreach (var (aggregate, leaves) in fanAggregates)
        {
            aggregate.Hidden = !collapsed;

            foreach (var leaf in leaves)
            {
                leaf.Hidden = collapsed;
            }
        }
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
