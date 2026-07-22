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

    /// <summary>All geometry this view derived from the model: sizes, pivots, routed wires.</summary>
    internal GraphGeometry Geometry { get; } = new();

    public bool IsMoving { get; private set; }
    private SKPoint lastLocation;
    private SKPoint dragOrigin;
    private bool dragStarted;
    private bool dragMovedConnected;
    private const float DragThreshold = 4f;

    // Synchronizes graph state between the render thread and UI mouse handlers.
    private readonly System.Threading.Lock stateLock = new();

    public event EventHandler? GraphChanged;

    /// <summary>Raised when the selection (primary node or wire) changes.</summary>
    public event EventHandler? SelectionChanged;

    private void OnSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

    public int NodeCount => nodes.Count;
    public int WireCount => wires.Count;

    /// <summary>Live node list in z-order; do not mutate.</summary>
    public IReadOnlyList<GraphNode> Nodes => nodes;

    /// <summary>Live wire list in creation order; do not mutate.</summary>
    public IReadOnlyList<GraphWire> Wires => wires;

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
    public GraphPlacement Placement { get; set; } = GraphPlacement.Layered;

    /// <summary>Which layout improvements the placement engines apply.</summary>
    internal GraphLayoutOptions LayoutOptions { get; set; } = GraphLayoutOptions.Default;

    /// <summary>
    /// Draws wires as straight segments leaving each socket on a short stub instead of as
    /// curves, and squares off the corners of routed wires.
    /// </summary>
    public bool StraightWires { get; set; }

    /// <summary>Legend rows describing this graph's color semantics, declared by the frontend.</summary>
    public List<GraphLegendEntry> Legend { get; } = [];

    public GraphView(GraphPalette? palette = null)
    {
        Palette = palette ?? GraphPalette.ForCurrentTheme();
    }

    private int nextNodeSequence;

    public T AddNode<T>(T node) where T : GraphNode
    {
        node.Sequence = nextNodeSequence++;
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
            var size = Geometry.SizeOf(node);
            minX = Math.Min(minX, node.Position.X);
            minY = Math.Min(minY, node.Position.Y);
            maxX = Math.Max(maxX, node.Position.X + size.X);
            maxY = Math.Max(maxY, node.Position.Y + size.Y);
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

        // Dropped wires re-anchor as plain curves; only self-loops carry a synthetic
        // route that must be rebuilt around the new position.
        foreach (var wire in movedWires)
        {
            if (wire.From.Owner == wire.To.Owner)
            {
                GraphLayout.SynthesizeSelfLoop(wire, Geometry);
                continue;
            }

            var route = Geometry.TryRouteOf(wire);

            if (route != null)
            {
                route.CurvePath = null;
                route.Waypoints = null;
            }
        }

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

            var size = Geometry.SizeOf(node);

            // Socket pivots sit on the node border, so a bounds check inflated by the
            // socket hit radius rejects the node and all its sockets in one comparison.
            if (point.X < node.Position.X - socketHitRadius || point.X > node.Position.X + size.X + socketHitRadius ||
                point.Y < node.Position.Y - socketHitRadius || point.Y > node.Position.Y + size.Y + socketHitRadius)
            {
                continue;
            }

            foreach (var socket in node.Inputs)
            {
                var pivot = Geometry.PivotOf(socket);
                if (Math.Abs(point.X - pivot.X) <= socketHitRadius && Math.Abs(point.Y - pivot.Y) <= socketHitRadius)
                {
                    return socket;
                }
            }

            foreach (var socket in node.Outputs)
            {
                var pivot = Geometry.PivotOf(socket);
                if (Math.Abs(point.X - pivot.X) <= socketHitRadius && Math.Abs(point.Y - pivot.Y) <= socketHitRadius)
                {
                    return socket;
                }
            }

            if (point.X >= node.Position.X && point.X <= node.Position.X + size.X &&
                point.Y >= node.Position.Y && point.Y <= node.Position.Y + size.Y)
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
    private void ReanchorWireWaypoints(GraphNode node)
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

        void ReanchorWire(GraphWire wire)
        {
            // Self-loops keep their synthetic route pinned to the moving node.
            if (wire.From.Owner == wire.To.Owner)
            {
                GraphLayout.SynthesizeSelfLoop(wire, Geometry);
                return;
            }

            // A routed curve cannot follow a moving node; fall back to the default
            // bezier until the drop re-anchors the island.
            var route = Geometry.TryRouteOf(wire);

            if (route != null)
            {
                route.CurvePath = null;
                route.Waypoints = null;
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

    private void SelectWire(GraphWire wire)
    {
        Selection.SelectWire(wire);
        OnSelectionChanged();
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
        OnSelectionChanged();
    }

    private void ToggleSelection(GraphNode node)
    {
        if (Selection.PrimaryNode == node || Selection.Connected.Contains(node))
        {
            Selection.Clear();
            OnSelectionChanged();
        }
        else
        {
            SetPrimarySelection(node);
        }

        OnGraphChanged();
    }

    /// <summary>Clears the current node or wire selection.</summary>
    public void ClearSelection()
    {
        Selection.Clear();
        OnSelectionChanged();
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

        // Matches cycle in stable creation order (Sequence), independent of the z-ordered nodes list.
        GraphNode? next = null;
        GraphNode? first = null;

        foreach (var node in nodes)
        {
            if (!node.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !(node.Subtitle?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                continue;
            }

            if (first == null || node.Sequence < first.Sequence)
            {
                first = node;
            }

            if (after != null && node.Sequence > after.Sequence && (next == null || node.Sequence < next.Sequence))
            {
                next = node;
            }
        }

        return next ?? first;
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

    /// <summary>Hides everything outside the directed cone of nodes that can trigger <paramref name="node"/>.</summary>
    public void IsolateUpstreamOf(GraphNode node) => IsolateCone(node, upstream: true);

    /// <summary>Hides everything outside the directed cone of nodes that <paramref name="node"/> can trigger.</summary>
    public void IsolateDownstreamOf(GraphNode node) => IsolateCone(node, upstream: false);

    private void IsolateCone(GraphNode node, bool upstream)
    {
        var cone = new HashSet<GraphNode> { node };
        var queue = new Queue<GraphNode>();
        queue.Enqueue(node);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var socket in upstream ? current.Inputs : current.Outputs)
            {
                foreach (var wire in socket.Wires)
                {
                    var next = upstream ? wire.From.Owner : wire.To.Owner;

                    if (cone.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        foreach (var member in nodes)
        {
            member.Hidden = !cone.Contains(member);
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

    /// <summary>The most connected nodes with their wire counts, descending.</summary>
    public List<(GraphNode Node, int Degree)> GetTopConnectedNodes(int count)
    {
        var result = new List<(GraphNode Node, int Degree)>(nodes.Count);

        foreach (var node in nodes)
        {
            var degree = 0;

            foreach (var socket in node.Inputs)
            {
                degree += socket.Wires.Count;
            }

            foreach (var socket in node.Outputs)
            {
                degree += socket.Wires.Count;
            }

            if (degree > 0)
            {
                result.Add((node, degree));
            }
        }

        result.Sort(static (a, b) => b.Degree.CompareTo(a.Degree));

        if (result.Count > count)
        {
            result.RemoveRange(count, result.Count - count);
        }

        return result;
    }

    /// <summary>Self-loop wire count and the number of nodes no wire touches.</summary>
    public (int SelfLoops, int Orphans) GetGraphHealthCounts()
    {
        var selfLoops = 0;

        foreach (var wire in wires)
        {
            if (wire.From.Owner == wire.To.Owner)
            {
                selfLoops++;
            }
        }

        var orphans = 0;

        foreach (var node in nodes)
        {
            var connected = false;

            foreach (var socket in node.Inputs)
            {
                connected |= socket.Wires.Count > 0;
            }

            foreach (var socket in node.Outputs)
            {
                connected |= socket.Wires.Count > 0;
            }

            if (!connected)
            {
                orphans++;
            }
        }

        return (selfLoops, orphans);
    }

    /// <summary>
    /// Keeps an authored node arrangement but scales it out from its centroid until no cards
    /// overlap; editor formats store positions that assume smaller cards than ours.
    /// </summary>
    public void SpreadAuthoredLayout()
    {
        using var _ = stateLock.EnterScope();

        EnsureAllGeometry();

        const float Pad = 40f;
        var centroid = Vector2.Zero;

        foreach (var node in nodes)
        {
            centroid += node.Position + Geometry.SizeOf(node) / 2f;
        }

        centroid /= Math.Max(1, nodes.Count);

        var scale = 1f;

        for (var i = 0; i < nodes.Count; i++)
        {
            var a = nodes[i];
            var aSize = Geometry.SizeOf(a);
            var aCenter = a.Position + aSize / 2f;

            for (var j = i + 1; j < nodes.Count; j++)
            {
                var b = nodes[j];
                var bSize = Geometry.SizeOf(b);
                var bCenter = b.Position + bSize / 2f;

                var dx = Math.Abs(bCenter.X - aCenter.X);
                var dy = Math.Abs(bCenter.Y - aCenter.Y);
                var needX = (aSize.X + bSize.X) / 2f + Pad;
                var needY = (aSize.Y + bSize.Y) / 2f + Pad;

                if (dx >= needX || dy >= needY)
                {
                    continue;
                }

                // Scale along whichever axis separates this pair cheapest.
                var scaleX = dx > 1f ? needX / dx : float.MaxValue;
                var scaleY = dy > 1f ? needY / dy : float.MaxValue;
                var pairScale = Math.Min(scaleX, scaleY);

                if (pairScale < float.MaxValue)
                {
                    scale = Math.Max(scale, pairScale);
                }
            }
        }

        scale = Math.Min(scale, 6f);

        if (scale > 1f)
        {
            foreach (var node in nodes)
            {
                var size = Geometry.SizeOf(node);
                var center = node.Position + size / 2f;
                node.Position = centroid + (center - centroid) * scale - size / 2f;
            }
        }

        // Residual tight pairs (authored cards that merely sit close) get room by seam
        // insertion: side-by-side pairs open horizontally, moving the right card and every
        // card at or beyond it along with it; stacked pairs open downward the same way.
        // The pair's own arrangement stays intact and nothing else compresses.
        const float PushPad = 70f;

        for (var pass = 0; pass < 64; pass++)
        {
            var moved = false;

            for (var i = 0; i < nodes.Count; i++)
            {
                var a = nodes[i];
                var aSize = Geometry.SizeOf(a);

                for (var j = i + 1; j < nodes.Count; j++)
                {
                    var b = nodes[j];
                    var bSize = Geometry.SizeOf(b);

                    var overlapX = Math.Min(a.Position.X + aSize.X, b.Position.X + bSize.X) - Math.Max(a.Position.X, b.Position.X) + PushPad;
                    var overlapY = Math.Min(a.Position.Y + aSize.Y, b.Position.Y + bSize.Y) - Math.Max(a.Position.Y, b.Position.Y) + PushPad;

                    if (overlapX <= 0 || overlapY <= 0)
                    {
                        continue;
                    }

                    moved = true;

                    var aCenter = a.Position + aSize / 2f;
                    var bCenter = b.Position + bSize / 2f;
                    var needX = (aSize.X + bSize.X) / 2f + PushPad;
                    var needY = (aSize.Y + bSize.Y) / 2f + PushPad;

                    // Whichever axis the pair is proportionally more separated on defines
                    // its arrangement; the seam opens along that axis.
                    if (Math.Abs(bCenter.X - aCenter.X) / needX >= Math.Abs(bCenter.Y - aCenter.Y) / needY)
                    {
                        var rightNode = aCenter.X <= bCenter.X ? b : a;
                        var seamX = rightNode.Position.X - 0.5f;

                        foreach (var node in nodes)
                        {
                            if (node.Position.X >= seamX)
                            {
                                node.Position += new Vector2(overlapX, 0f);
                            }
                        }
                    }
                    else
                    {
                        var lowerNode = aCenter.Y <= bCenter.Y ? b : a;
                        var seamY = lowerNode.Position.Y - 0.5f;

                        foreach (var node in nodes)
                        {
                            if (node.Position.Y >= seamY)
                            {
                                node.Position += new Vector2(0f, overlapY);
                            }
                        }
                    }
                }
            }

            if (!moved)
            {
                break;
            }
        }

        MarkVisualDirty();
    }

    /// <summary>
    /// Lays out each connected component independently and packs the components toward a
    /// screen-like aspect. Suited for graphs made of many disjoint islands, like entity I/O.
    /// Hidden nodes keep their positions and do not participate in layout or packing.
    /// </summary>
    public void LayoutNodesPacked(float padding = 150f)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        // The render thread enumerates nodes/wires under this lock.
        using var _ = stateLock.EnterScope();

        var components = LayoutPass(padding);

        // Socket order decides where a wire docks, so it can only be judged once the cards have
        // somewhere to be. The first pass is what tells the second one which rows to swap.
        if (LayoutOptions.Has(GraphLayoutFeature.PortOrdering) && ReorderPorts(components))
        {
            LayoutPass(padding);
        }

        ClearWireHitPaths();
        OnGraphChanged();
    }

    /// <summary>
    /// Places the nodes with an outside algorithm, then refreshes everything derived from their
    /// positions. Lets the library layouts be measured in this renderer on equal terms.
    /// </summary>
    /// <summary>
    /// Re-runs the crossing repair with no time limit, so a graph the budgeted pass gave up on
    /// gets the full treatment when the user explicitly asks for it.
    /// </summary>
    public void ReduceVisualComplexity()
    {
        using (stateLock.EnterScope())
        {
            EnsureAllGeometry();

            var previous = LayoutOptions;

            LayoutOptions = new GraphLayoutOptions
            {
                Features = previous.Features | GraphLayoutFeature.CrossingSwap,
                LayerSpacing = previous.LayerSpacing,
                NodeSpacing = previous.NodeSpacing,
                CrossingRepairBudgetMs = 0,
            };

            foreach (var component in GetVisibleComponents())
            {
                var componentNodes = new HashSet<GraphNode>(component);
                var componentWires = wires.Where(w => componentNodes.Contains(w.From.Owner) && componentNodes.Contains(w.To.Owner)).ToList();

                GraphLayout.RepairCrossings(component, componentWires, Geometry, LayoutOptions);
            }

            LayoutOptions = previous;
            ClearWireHitPaths();
        }

        OnGraphChanged();
    }

    internal void LayoutWith(Action<IReadOnlyList<GraphNode>, IReadOnlyList<GraphWire>> place)
    {
        using var _ = stateLock.EnterScope();

        EnsureAllGeometry();
        Geometry.ClearAllRoutes();

        place(nodes, wires);

        foreach (var wire in wires)
        {
            if (wire.From.Owner == wire.To.Owner)
            {
                GraphLayout.SynthesizeSelfLoop(wire, Geometry);
            }
        }

        ClearWireHitPaths();
        OnGraphChanged();
    }

    /// <summary>
    /// Orders the socket rows of every node that allows it by where its wires come from and go
    /// to, so incident wires stop crossing inside the gutter. Returns whether anything moved.
    /// </summary>
    private bool ReorderPorts(List<List<GraphNode>> components)
    {
        var changed = false;

        foreach (var component in components)
        {
            foreach (var node in component)
            {
                changed |= node.ReorderSockets(socket =>
                {
                    if (socket.Wires.Count == 0)
                    {
                        return Geometry.PivotOf(socket).Y;
                    }

                    var sum = 0f;

                    foreach (var wire in socket.Wires)
                    {
                        sum += Geometry.PivotOf(wire.From == socket ? wire.To : wire.From).Y;
                    }

                    return sum / socket.Wires.Count;
                });
            }
        }

        return changed;
    }

    private List<List<GraphNode>> LayoutPass(float padding)
    {
        EnsureAllGeometry();
        Geometry.ClearAllRoutes();

        var components = GetVisibleComponents();

        // Component discovery walks the z-ordered node list, which mutates as nodes are
        // clicked; layout runs on the stable creation order instead so a relayout always
        // reproduces the load-time picture.
        foreach (var component in components)
        {
            component.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
        }

        components.Sort(static (a, b) => a[0].Sequence.CompareTo(b[0].Sequence));

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

            GraphLayout.Layout(component, componentWires[i], Placement, Geometry, LayoutOptions);
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
                max = Vector2.Max(max, node.Position + Geometry.SizeOf(node));
            }

            mins[i] = min;
            sizes[i] = max - min;
        }

        var origins = GraphLayout.PackComponents(sizes, padding);

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
                        var route = Geometry.TryRouteOf(wire);

                        if (route == null)
                        {
                            continue;
                        }

                        if (route.Waypoints is { } waypoints)
                        {
                            for (var w = 0; w < waypoints.Count; w++)
                            {
                                waypoints[w] += offset;
                            }
                        }

                        if (route.CurvePath is { } curvePath)
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

        return components;
    }

    /// <summary>
    /// Atomically discards the current graph document and derived state, then runs
    /// <paramref name="build"/> to repopulate it, holding the state lock across the whole
    /// swap so a concurrent render never sees a half-built graph.
    /// </summary>
    public void Rebuild(Action build)
    {
        using var _ = stateLock.EnterScope();

        foreach (var entry in wireHitPaths.Values)
        {
            entry.Path.Dispose();
        }

        wireHitPaths.Clear();
        nodes.Clear();
        wires.Clear();
        Legend.Clear();
        Selection.Clear();
        Geometry.Clear();

        lastHovered = null;
        searchHighlight = null;
        nextNodeSequence = 0;
        IsMoving = false;
        dragStarted = false;

        build();

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
