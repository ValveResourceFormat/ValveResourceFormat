using System.Linq;
using System.Windows.Forms;
using SkiaSharp;
using ValveResourceFormat.Graphs;

namespace GUI.Types.Graphs.Core;

/// <summary>
/// Holds a graph document (nodes and wires), selection and hover state, hit testing and automatic
/// layout. Rendering lives in the Render partial. Hosted by GLGraphViewer.
/// </summary>
partial class GraphView : IDisposable
{
    public GraphPalette Palette { get; }

    /// <summary>The graph being presented. Every model and layout operation lives there.</summary>
    public GraphDocument Document { get; } = new();

    /// <summary>Resolves a node's <see cref="GraphNode.IconKey"/> to the icon drawn in its left gutter.</summary>
    public Func<string, SKImage?>? IconResolver { get; set; }

    private readonly Dictionary<GraphWire, WirePaths> wirePaths = [];

    private IGraphElement? lastHovered;

    private IReadOnlyList<GraphNode> nodes => Document.Nodes;
    private IReadOnlyList<GraphWire> wires => Document.Wires;
    private string? searchHighlight => Document.SearchHighlight;

    /// <summary>Current selection; render tiers and the host's focus actions derive from it.</summary>
    public GraphSelection Selection => Document.Selection;

    /// <summary>All geometry derived from the model: sizes, pivots, routed wires.</summary>
    internal GraphGeometry Geometry => Document.Geometry;

    /// <summary>Measured size of a node, safe to call while the render thread is drawing.</summary>
    public Vector2 MeasuredSizeOf(GraphNode node)
    {
        using var _ = stateLock.EnterScope();
        return Document.MeasuredSizeOf(node);
    }

    public bool IsMoving { get; private set; }
    private SKPoint lastLocation;
    private SKPoint dragOrigin;
    private bool dragStarted;
    private const float DragThreshold = 4f;

    // Synchronizes graph state between the render thread and UI mouse handlers.
    private readonly System.Threading.Lock stateLock = new();

    public event EventHandler? GraphChanged;

    public int NodeCount => Document.NodeCount;
    public int WireCount => Document.WireCount;

    /// <summary>Live node list in z-order; do not mutate.</summary>
    public IReadOnlyList<GraphNode> Nodes => Document.Nodes;

    /// <summary>Live wire list in creation order; do not mutate.</summary>
    public IReadOnlyList<GraphWire> Wires => Document.Wires;

    /// <summary>Monotonic counter of visual state changes; the host skips repainting while it is stable.</summary>
    public int VisualVersion { get; private set; }

    /// <summary>Marks the rendered output stale without raising <see cref="GraphChanged"/>.</summary>
    public void MarkVisualDirty() => VisualVersion++;

    private void OnGraphChanged()
    {
        VisualVersion++;
        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Which layout improvements the placement engine applies.</summary>
    internal GraphLayoutOptions LayoutOptions => Document.LayoutOptions;

    private bool straightWires;

    /// <summary>
    /// Draws wires as straight segments leaving each socket on a short stub instead of as
    /// curves, and squares off the corners of routed wires.
    /// </summary>
    public bool StraightWires
    {
        get => straightWires;
        set
        {
            using var _ = stateLock.EnterScope();

            if (straightWires == value)
            {
                return;
            }

            straightWires = value;
            ClearWirePaths();
        }
    }

    /// <summary>Legend rows describing this graph's color semantics, declared by the frontend.</summary>
    public List<GraphLegendEntry> Legend => Document.Legend;

    public GraphView(GraphPalette? palette = null)
    {
        Palette = palette ?? GraphPalette.ForCurrentTheme();
    }

    public T AddNode<T>(T node) where T : GraphNode
    {
        using var _ = stateLock.EnterScope();
        return Document.AddNode(node);
    }

    public GraphWire Connect(GraphSocket from, GraphSocket to, bool dashed = false, string? label = null)
    {
        using var _ = stateLock.EnterScope();
        return Document.Connect(from, to, dashed, label);
    }

    /// <summary>Removes a wire from both sockets and the wire list.</summary>
    public void Disconnect(GraphWire wire)
    {
        using var _ = stateLock.EnterScope();
        RemoveWire(wire);
    }

    /// <summary>Removes a socket from <paramref name="node"/> and drops its geometry.</summary>
    public void RemoveSocket(GraphNode node, GraphSocket socket)
    {
        using var _ = stateLock.EnterScope();
        Document.RemoveSocket(node, socket);
    }

    private void RemoveWire(GraphWire wire)
    {
        Document.Disconnect(wire);

        if (wirePaths.Remove(wire, out var entry))
        {
            entry.Dispose();
        }
    }

    private void ClearWirePaths()
    {
        foreach (var entry in wirePaths.Values)
        {
            entry.Dispose();
        }

        wirePaths.Clear();
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
                foreach (var node in Selection.Connected)
                {
                    node.Position += delta;
                    Document.ReanchorWireWaypoints(node);
                }
            }
            else if (Selection.PrimaryNode != null)
            {
                Selection.PrimaryNode.Position += delta;
                Document.ReanchorWireWaypoints(Selection.PrimaryNode);
            }

            ClearWirePaths();
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

    public IGraphElement? FindElementAt(SKPoint point)
    {
        using var _ = stateLock.EnterScope();
        return FindElementAtCore(point);
    }

    private IGraphElement? FindElementAtCore(SKPoint point)
    {
        const float socketHitRadius = GraphMetrics.SocketRadius + 3f;

        // A hit test can land between a rebuild and the next paint, when nothing has measured yet.
        EnsureAllGeometry();

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

            var entry = EnsureWireHitPath(wire);

            if (entry.HitBounds.Contains(point.X, point.Y) && entry.HitPath!.Contains(point.X, point.Y))
            {
                return wire;
            }
        }

        return null;
    }

    private void BringNodeToFront(GraphNode node)
    {
        Document.BringNodeToFront(node);
        OnGraphChanged();
    }

    private void SelectWire(GraphWire wire)
    {
        Document.SelectWire(wire);
        OnGraphChanged();
    }

    private void SetPrimarySelection(GraphNode node)
    {
        Document.SelectNode(node);
        MarkVisualDirty();
    }

    private void ToggleSelection(GraphNode node)
    {
        Document.ToggleSelection(node);
        OnGraphChanged();
    }

    /// <summary>Clears the current node or wire selection.</summary>
    public void ClearSelection()
    {
        using (stateLock.EnterScope())
        {
            Document.ClearSelection();
        }

        OnGraphChanged();
    }

    /// <summary>Returns the connected components of the graph (each node in exactly one list).</summary>
    public List<List<GraphNode>> GetComponents() => Document.GetComponents();

    /// <summary>Makes <paramref name="node"/> the primary selection.</summary>
    public void SelectNode(GraphNode node)
    {
        using (stateLock.EnterScope())
        {
            Document.SelectNode(node);
        }

        OnGraphChanged();
    }

    /// <summary>Live search filter: non-matching nodes render dimmed. Null or whitespace clears it.</summary>
    public void SetSearchHighlight(string? query)
    {
        using (stateLock.EnterScope())
        {
            var normalized = string.IsNullOrWhiteSpace(query) ? null : query;

            if (normalized == Document.SearchHighlight)
            {
                return;
            }

            Document.SearchHighlight = normalized;
        }

        OnGraphChanged();
    }

    private bool MatchesSearchHighlight(GraphNode node) => Document.MatchesSearchHighlight(node);

    /// <summary>
    /// Finds the next node (cycling, starting after <paramref name="after"/>) whose title or
    /// subtitle contains <paramref name="query"/>.
    /// </summary>
    public GraphNode? FindNextNode(string query, GraphNode? after)
    {
        using var _ = stateLock.EnterScope();
        return Document.FindNextNode(query, after);
    }

    /// <summary>Hides nodes whose subtitle is not in <paramref name="visibleSubtitles"/>; nodes without a subtitle stay visible.</summary>
    public void SetSubtitleFilter(IEnumerable<string> visibleSubtitles)
    {
        using (stateLock.EnterScope())
        {
            Document.SetSubtitleFilter(visibleSubtitles);
        }

        OnGraphChanged();
    }

    /// <summary>Distinct node subtitles (entity classnames, node types), sorted.</summary>
    public List<string> GetDistinctSubtitles() => Document.GetDistinctSubtitles();

    /// <summary>
    /// Hides every node outside the set <paramref name="mode"/> selects around
    /// <paramref name="node"/>. Isolating a group is a no-op for a node at the graph root.
    /// </summary>
    public void Isolate(GraphNode node, GraphIsolateMode mode)
    {
        using (stateLock.EnterScope())
        {
            Document.Isolate(node, mode);
        }

        OnGraphChanged();
    }

    public void ShowAllNodes()
    {
        using (stateLock.EnterScope())
        {
            Document.ShowAllNodes();
        }

        OnGraphChanged();
    }

    public bool HasMultipleIslands() => Document.HasMultipleIslands();

    /// <summary>Self-loop wire count and the number of nodes no wire touches.</summary>
    public (int SelfLoops, int Orphans) GetGraphHealthCounts() => Document.GetGraphHealthCounts();

    /// <summary>
    /// Lays out each connected component independently and packs the components toward a
    /// screen-like aspect. Suited for graphs made of many disjoint islands, like entity I/O.
    /// Hidden nodes keep their positions and do not participate in layout or packing.
    /// </summary>
    public void LayoutNodesPacked(float padding = 150f)
    {
        if (Document.NodeCount == 0)
        {
            return;
        }

        // The render thread enumerates nodes/wires under this lock.
        using (stateLock.EnterScope())
        {
            Document.LayoutNodesPacked(padding);
            ClearWirePaths();
        }

        OnGraphChanged();
    }

    /// <summary>
    /// Re-runs the crossing repair with no time limit, so a graph the budgeted pass gave up on
    /// gets the full treatment when the user explicitly asks for it.
    /// </summary>
    public void ReduceVisualComplexity()
    {
        using (stateLock.EnterScope())
        {
            Document.ReduceVisualComplexity();
            ClearWirePaths();
        }

        OnGraphChanged();
    }

    /// Atomically discards the current graph document and derived state, then runs
    /// <paramref name="build"/> to repopulate it, holding the state lock across the whole
    /// swap so a concurrent render never sees a half-built graph.
    /// </summary>
    public void Rebuild(Action build)
    {
        using var _ = stateLock.EnterScope();

        ClearWirePaths();
        Document.Clear();

        lastHovered = null;
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
                ClearWirePaths();
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
