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

    /// <summary>Resolves a node's <see cref="GraphNode.IconKey"/> to the icon drawn in its left gutter.</summary>
    public Func<string, SKImage?>? IconResolver { get; set; }

    private readonly List<GraphNode> nodes = [];
    private readonly List<GraphWire> wires = [];
    private readonly Dictionary<GraphWire, (SKPath Path, SKRect Bounds)> wireHitPaths = [];

    private IGraphElement? lastHovered;
    private string? searchHighlight;

    /// <summary>Current selection; render tiers and the host's focus actions derive from it.</summary>
    public GraphSelection Selection { get; } = new();

    public bool IsMoving { get; private set; }
    private SKPoint lastLocation;
    private SKPoint dragOrigin;
    private bool dragStarted;
    private bool dragMovedConnected;
    private const float DragThreshold = 4f;

    // Synchronizes graph state between the render thread and UI mouse handlers.
    private readonly System.Threading.Lock stateLock = new();

    public event EventHandler? GraphChanged;

    public int NodeCount => nodes.Count;
    public int WireCount => wires.Count;

    /// <summary>Monotonic counter of visual state changes; the host skips repainting while it is stable.</summary>
    public int VisualVersion { get; private set; }

    /// <summary>Marks the rendered output stale without raising <see cref="GraphChanged"/>.</summary>
    public void MarkVisualDirty() => VisualVersion++;

    private void OnGraphChanged()
    {
        VisualVersion++;
        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Node placement engine used by <see cref="LayoutNodesPacked"/>.</summary>
    public GraphPlacement Placement { get; set; } = GraphPlacement.Organic;

    /// <summary>Legend rows describing this graph's color semantics, declared by the frontend.</summary>
    public List<GraphLegendEntry> Legend { get; } = [];

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

        if (wireHitPaths.Remove(wire, out var entry))
        {
            entry.Path.Dispose();
        }
    }

    private void ClearWireHitPaths()
    {
        foreach (var entry in wireHitPaths.Values)
        {
            entry.Path.Dispose();
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

            if (!IsMoving && element is GraphNode node && Selection.PrimaryNode != null)
            {
                IsMoving = true;
                dragStarted = false;
                dragMovedConnected = false;
                dragOrigin = graphPoint;
                BringNodeToFront(node);
            }
        }
    }

    public void HandleMouseMove(SKPoint graphPoint, Keys modifiers = Keys.None)
    {
        using var _ = stateLock.EnterScope();

        if (IsMoving)
        {
            // Clicks come with a pixel of jitter; only a real drag may move nodes and
            // invalidate their routed wires.
            if (!dragStarted)
            {
                var travelX = graphPoint.X - dragOrigin.X;
                var travelY = graphPoint.Y - dragOrigin.Y;

                if (travelX * travelX + travelY * travelY < DragThreshold * DragThreshold)
                {
                    return;
                }

                dragStarted = true;
            }

            var delta = new Vector2(graphPoint.X - lastLocation.X, graphPoint.Y - lastLocation.Y);
            var moveAllConnected = (modifiers & Keys.Control) != 0;

            if (moveAllConnected && Selection.Connected.Count > 0)
            {
                dragMovedConnected = true;

                foreach (var node in Selection.Connected)
                {
                    node.Position += delta;
                    ReanchorWireWaypoints(node);
                }
            }
            else if (Selection.PrimaryNode != null)
            {
                Selection.PrimaryNode.Position += delta;
                ReanchorWireWaypoints(Selection.PrimaryNode);
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

    public void HandleMouseUp(SKPoint graphPoint, MouseButtons button)
    {
        using var _ = stateLock.EnterScope();

        // Only the button that started the drag may end it; a chorded right/middle
        // release mid-drag would otherwise tear the gesture down.
        if ((button & MouseButtons.Left) == 0)
        {
            return;
        }

        lastLocation = graphPoint;

        // After a real drag, re-route the moved island's wires around the new positions.
        if (IsMoving && dragStarted && Selection.PrimaryNode != null)
        {
            RerouteComponentOf(Selection.PrimaryNode);
        }

        IsMoving = false;
        dragStarted = false;
    }

    /// <summary>Abandons a live node drag, e.g. when focus is lost mid-gesture and the mouse-up will never arrive.</summary>
    public void CancelDrag()
    {
        using var _ = stateLock.EnterScope();

        IsMoving = false;
        dragStarted = false;
    }

    private void RerouteComponentOf(GraphNode start)
    {
        var component = new List<GraphNode>();
        var visited = new HashSet<GraphNode> { start };
        WalkComponent(start, visited, component, includeHidden: false);

        // Only the wires touching the moved nodes need new routes; the rest of the
        // component participates as obstacles but keeps its existing geometry.
        HashSet<GraphNode> movedNodes = dragMovedConnected ? [.. Selection.Connected, start] : [start];

        var movedWires = wires.Where(w =>
            visited.Contains(w.From.Owner) && visited.Contains(w.To.Owner) &&
            !w.From.Owner.Hidden && !w.To.Owner.Hidden &&
            (movedNodes.Contains(w.From.Owner) || movedNodes.Contains(w.To.Owner))).ToList();

        MsaglLayoutAdapter.RouteComponent(component, movedWires);
        ClearWireHitPaths();
        OnGraphChanged();
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

            // Socket pivots sit on the node border, so a bounds check inflated by the
            // socket hit radius rejects the node and all its sockets in one comparison.
            if (point.X < node.Position.X - socketHitRadius || point.X > node.Position.X + node.Size.X + socketHitRadius ||
                point.Y < node.Position.Y - socketHitRadius || point.Y > node.Position.Y + node.Size.Y + socketHitRadius)
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

            if (!wireHitPaths.TryGetValue(wire, out var entry))
            {
                var path = BuildWireHitPath(wire);
                entry = (path, path.Bounds);
                wireHitPaths[wire] = entry;
            }

            if (entry.Bounds.Contains(point.X, point.Y) && entry.Path.Contains(point.X, point.Y))
            {
                return wire;
            }
        }

        return null;
    }

    // Dragging keeps routes orthogonal: terminal runs re-anchor live to the moving sockets.
    private static void ReanchorWireWaypoints(GraphNode node)
    {
        foreach (var socket in node.Inputs)
        {
            foreach (var wire in socket.Wires)
            {
                ReanchorWire(wire);
            }
        }

        foreach (var socket in node.Outputs)
        {
            foreach (var wire in socket.Wires)
            {
                ReanchorWire(wire);
            }
        }

        static void ReanchorWire(GraphWire wire)
        {
            // Self-loops keep their synthetic route pinned to the moving node.
            if (wire.From.Owner == wire.To.Owner)
            {
                MsaglLayoutAdapter.SynthesizeSelfLoop(wire);
                return;
            }

            // A bundled curve cannot follow a moving node; fall back to the default
            // bezier until the drop re-routes the island.
            wire.CurvePath = null;
            wire.Waypoints = null;
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

    private void SelectWire(GraphWire wire)
    {
        Selection.SelectWire(wire);
        OnGraphChanged();
    }

    private void SetPrimarySelection(GraphNode node)
    {
        Selection.SetPrimary(node);

        // Raise the whole chain, primary on top, so the focused nodes draw over the rest.
        foreach (var connectedNode in Selection.Connected)
        {
            if (connectedNode != node && nodes.Remove(connectedNode))
            {
                nodes.Add(connectedNode);
            }
        }

        if (nodes.Remove(node))
        {
            nodes.Add(node);
        }

        MarkVisualDirty();
    }

    private void ToggleSelection(GraphNode node)
    {
        if (Selection.PrimaryNode == node || Selection.Connected.Contains(node))
        {
            Selection.Clear();
        }
        else
        {
            SetPrimarySelection(node);
        }

        OnGraphChanged();
    }

    private void ClearSelection()
    {
        Selection.Clear();
        OnGraphChanged();
    }

    /// <summary>
    /// Returns the connected components of the graph (each node in exactly one list).
    /// </summary>
    public List<List<GraphNode>> GetComponents() => CollectComponents(includeHidden: true);

    private List<List<GraphNode>> GetVisibleComponents() => CollectComponents(includeHidden: false);

    private List<List<GraphNode>> CollectComponents(bool includeHidden)
    {
        var visited = new HashSet<GraphNode>();
        var components = new List<List<GraphNode>>();

        foreach (var start in nodes)
        {
            if ((!includeHidden && start.Hidden) || !visited.Add(start))
            {
                continue;
            }

            var component = new List<GraphNode>();
            WalkComponent(start, visited, component, includeHidden);
            components.Add(component);
        }

        return components;
    }

    // Undirected flood fill across socket wires; appends every newly visited node to component.
    // Callers seed visited with start.
    private static void WalkComponent(GraphNode start, HashSet<GraphNode> visited, List<GraphNode> component, bool includeHidden)
    {
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
                    if ((includeHidden || !wire.From.Owner.Hidden) && visited.Add(wire.From.Owner))
                    {
                        stack.Push(wire.From.Owner);
                    }
                }
            }

            foreach (var socket in node.Outputs)
            {
                foreach (var wire in socket.Wires)
                {
                    if ((includeHidden || !wire.To.Owner.Hidden) && visited.Add(wire.To.Owner))
                    {
                        stack.Push(wire.To.Owner);
                    }
                }
            }
        }
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
        GraphSelection.TraverseConnected(node, chain);

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
    /// Lays out each connected component independently with MSAGL and packs the components into
    /// rows. Suited for graphs made of many disjoint islands, like entity I/O. Hidden nodes keep
    /// their positions and do not participate in layout or packing.
    /// </summary>
    public void LayoutNodesPacked(float padding = 150f)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        // The render thread enumerates nodes/wires under this lock.
        using var _ = stateLock.EnterScope();

        EnsureAllGeometry();

        foreach (var wire in wires)
        {
            wire.Waypoints = null;
            wire.CurvePath = null;
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

        var componentWires = new List<GraphWire>[components.Count];

        for (var i = 0; i < components.Count; i++)
        {
            componentWires[i] = [];
        }

        foreach (var wire in wires)
        {
            if (componentOf.TryGetValue(wire.From.Owner, out var c) && componentOf.ContainsKey(wire.To.Owner))
            {
                componentWires[c].Add(wire);
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

            MsaglLayoutAdapter.Layout(component, componentWires[i], Placement);
        }

        // Pack the island bounding boxes toward a screen-like aspect so large graphs open
        // dense instead of in sparse rows.
        var mins = new Vector2[components.Count];
        var sizes = new Vector2[components.Count];

        for (var i = 0; i < components.Count; i++)
        {
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);

            foreach (var node in components[i])
            {
                min = Vector2.Min(min, node.Position);
                max = Vector2.Max(max, node.Position + node.Size);
            }

            mins[i] = min;
            sizes[i] = max - min;
        }

        var origins = MsaglLayoutAdapter.PackComponents(sizes, padding);

        for (var i = 0; i < components.Count; i++)
        {
            var offset = origins[i] - mins[i];

            foreach (var node in components[i])
            {
                node.Position += offset;

                // Routed wire waypoints and exact curves live in the same island space and
                // must shift along.
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

                        if (wire.CurvePath is { } curvePath)
                        {
                            for (var c = 0; c < curvePath.Count; c++)
                            {
                                var command = curvePath[c];
                                curvePath[c] = command with
                                {
                                    A = command.A + offset,
                                    B = command.B + offset,
                                    End = command.End + offset,
                                };
                            }
                        }
                    }
                }
            }
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
                ClearWireHitPaths();
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
