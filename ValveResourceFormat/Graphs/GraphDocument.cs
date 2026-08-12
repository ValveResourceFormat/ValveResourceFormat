using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// A graph and everything derivable from it: its nodes and wires, the selection and search state,
/// the measured geometry, and the layout that arranges it. Holds no drawing or input state, so a
/// host presents it without the graph needing to know a window exists.
/// </summary>
public sealed class GraphDocument
{
    private readonly List<GraphNode> nodes = [];
    private readonly List<GraphWire> wires = [];
    private int nextNodeSequence;
    private string? searchHighlight;

    /// <summary>Live node list in z-order; do not mutate.</summary>
    public IReadOnlyList<GraphNode> Nodes => nodes;

    /// <summary>Live wire list in creation order; do not mutate.</summary>
    public IReadOnlyList<GraphWire> Wires => wires;

    /// <summary>All geometry measured from the model: sizes, socket pivots, routed wires.</summary>
    public GraphGeometry Geometry { get; } = new();

    /// <summary>Current selection; a host's render tiers and focus actions derive from it.</summary>
    public GraphSelection Selection { get; } = new();

    /// <summary>Legend rows describing this graph's color semantics, declared by the frontend.</summary>
    public List<GraphLegendEntry> Legend { get; } = [];

    /// <summary>Which layout improvements the placement engine applies.</summary>
    public GraphLayoutOptions LayoutOptions { get; } = new();

    /// <summary>Live search filter; nodes that do not match are meant to be drawn dimmed.</summary>
    public string? SearchHighlight
    {
        get => searchHighlight;
        set => searchHighlight = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>How many nodes the graph holds.</summary>
    public int NodeCount => nodes.Count;

    /// <summary>How many wires the graph holds.</summary>
    public int WireCount => wires.Count;

    /// <summary>Adds a node, stamping it with the creation order the layout runs in.</summary>
    /// <typeparam name="T">The node type being added.</typeparam>
    /// <param name="node">The node to add.</param>
    /// <returns>The same node.</returns>
    public T AddNode<T>(T node) where T : GraphNode
    {
        ArgumentNullException.ThrowIfNull(node);

        node.Sequence = nextNodeSequence++;
        nodes.Add(node);
        return node;
    }

    /// <summary>Wires an output socket to an input socket.</summary>
    /// <param name="from">The output socket the wire leaves.</param>
    /// <param name="to">The input socket the wire enters.</param>
    /// <param name="dashed">Whether this is a secondary binding rather than primary flow.</param>
    /// <param name="label">Short text drawn at the wire's midpoint.</param>
    /// <returns>The new wire.</returns>
    public GraphWire Connect(GraphSocket from, GraphSocket to, bool dashed = false, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

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
                Disconnect(existing);
            }
        }

        var wire = new GraphWire(from, to) { Dashed = dashed, Label = label };
        from.Wires.Add(wire);
        to.Wires.Add(wire);
        wires.Add(wire);
        return wire;
    }

    /// <summary>Removes a wire from both its sockets and from the graph.</summary>
    /// <param name="wire">The wire to remove.</param>
    public void Disconnect(GraphWire wire)
    {
        ArgumentNullException.ThrowIfNull(wire);

        wire.From.Wires.Remove(wire);
        wire.To.Wires.Remove(wire);
        wires.Remove(wire);
        Geometry.RemoveWire(wire);
    }

    /// <summary>Removes a socket from its node and drops its geometry.</summary>
    /// <param name="node">The node holding the socket.</param>
    /// <param name="socket">The socket to remove.</param>
    public void RemoveSocket(GraphNode node, GraphSocket socket)
    {
        ArgumentNullException.ThrowIfNull(node);

        node.RemoveSocket(socket);
        Geometry.RemoveSocket(socket);
    }

    /// <summary>Discards the whole graph and everything derived from it.</summary>
    public void Clear()
    {
        nodes.Clear();
        wires.Clear();
        Legend.Clear();
        Selection.Clear();
        Geometry.Clear();
        nextNodeSequence = 0;
        searchHighlight = null;
    }

    /// <summary>Measures every node whose content changed since it was last measured.</summary>
    /// <returns>Whether any node was remeasured.</returns>
    public bool EnsureGeometry()
    {
        var anyChanged = false;

        foreach (var node in nodes)
        {
            anyChanged |= GraphMetrics.Measure(node, Geometry);
        }

        return anyChanged;
    }

    /// <summary>Measured size of one node, remeasuring it first if its content changed.</summary>
    /// <param name="node">The node to size.</param>
    public Vector2 MeasuredSizeOf(GraphNode node)
    {
        EnsureGeometry();
        return Geometry.SizeOf(node);
    }

    /// <summary>Moves a node to the top of the z-order.</summary>
    /// <param name="node">The node to raise.</param>
    public void BringNodeToFront(GraphNode node)
    {
        if (nodes.Remove(node))
        {
            nodes.Add(node);
        }
    }

    /// <summary>Makes a node the primary selection and raises its whole chain above the rest.</summary>
    /// <param name="node">The node to select.</param>
    public void SelectNode(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Selection.SetPrimary(node);

        foreach (var connectedNode in Selection.Connected)
        {
            if (connectedNode != node && nodes.Remove(connectedNode))
            {
                nodes.Add(connectedNode);
            }
        }

        BringNodeToFront(node);
    }

    /// <summary>Selects a node, or clears the selection when it is already in the selected chain.</summary>
    /// <param name="node">The node to toggle.</param>
    public void ToggleSelection(GraphNode node)
    {
        if (Selection.PrimaryNode == node || Selection.Connected.Contains(node))
        {
            Selection.Clear();
        }
        else
        {
            SelectNode(node);
        }
    }

    /// <summary>Selects a wire, or clears it when that wire is already selected.</summary>
    /// <param name="wire">The wire to toggle.</param>
    public void SelectWire(GraphWire wire) => Selection.SelectWire(wire);

    /// <summary>Clears the current node or wire selection.</summary>
    public void ClearSelection() => Selection.Clear();

    /// <summary>Whether a node matches the active search filter.</summary>
    /// <param name="node">The node to test.</param>
    public bool MatchesSearchHighlight(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return searchHighlight == null
            || node.Title.Contains(searchHighlight, StringComparison.OrdinalIgnoreCase)
            || (node.Subtitle?.Contains(searchHighlight, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>
    /// Finds the next node whose title or subtitle contains <paramref name="query"/>, cycling in
    /// creation order from just after <paramref name="after"/>. Matches cycle in that order rather
    /// than in the z-ordered node list, which mutates as nodes are clicked.
    /// </summary>
    /// <param name="query">Text to search for.</param>
    /// <param name="after">The node the previous match was found at, or null to start over.</param>
    public GraphNode? FindNextNode(string query, GraphNode? after)
    {
        if (nodes.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

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

    /// <summary>Hides nodes whose subtitle is not listed; nodes without a subtitle stay visible.</summary>
    /// <param name="visibleSubtitles">The subtitles to keep visible.</param>
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
    }

    /// <summary>Makes every node visible again.</summary>
    public void ShowAllNodes()
    {
        foreach (var node in nodes)
        {
            node.Hidden = false;
        }
    }

    /// <summary>
    /// Hides every node outside the set <paramref name="mode"/> selects around
    /// <paramref name="node"/>. Isolating a group is a no-op for a node at the graph root.
    /// </summary>
    /// <param name="node">The node to isolate around.</param>
    /// <param name="mode">Which surrounding nodes to keep.</param>
    public void Isolate(GraphNode node, GraphIsolateMode mode)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (mode == GraphIsolateMode.Group && node.GroupPath == null)
        {
            return;
        }

        var keep = NodesToKeep(node, mode);

        foreach (var member in nodes)
        {
            member.Hidden = !keep.Contains(member);
        }
    }

    private HashSet<GraphNode> NodesToKeep(GraphNode node, GraphIsolateMode mode)
    {
        switch (mode)
        {
            case GraphIsolateMode.Chain:
                var chain = new HashSet<GraphNode>();
                GraphSelection.TraverseConnected(node, chain);
                return chain;

            case GraphIsolateMode.Upstream:
            case GraphIsolateMode.Downstream:
                var cone = new HashSet<GraphNode> { node };
                GraphSelection.TraverseDirection(node, cone, mode == GraphIsolateMode.Upstream);
                return cone;

            case GraphIsolateMode.Group:
                var prefix = node.GroupPath + "/";
                return [.. nodes.Where(member => member.GroupPath != null
                    && (member.GroupPath == node.GroupPath || member.GroupPath.StartsWith(prefix, StringComparison.Ordinal)))];

            default:
                return [.. GetComponents().Find(component => component.Contains(node)) ?? [node]];
        }
    }

    /// <summary>The connected components of the graph, each node in exactly one list.</summary>
    [SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Walks the whole graph on every call.")]
    public List<List<GraphNode>> GetComponents() => CollectComponents(includeHidden: true);

    /// <summary>Whether the graph is made of more than one island.</summary>
    public bool HasMultipleIslands() => GetComponents().Count > 1;

    /// <summary>Self-loop wire count and the number of nodes no wire touches.</summary>
    [SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Walks every node and wire on every call.")]
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

    private List<List<GraphNode>> GetVisibleComponents() => CollectComponents(includeHidden: false);

    private List<List<GraphNode>> CollectComponents(bool includeHidden, bool skipDashed = false)
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
            WalkComponent(start, visited, component, includeHidden, skipDashed);
            components.Add(component);
        }

        return components;
    }

    /// <summary>
    /// Undirected flood fill across socket wires, appending every newly visited node to
    /// <paramref name="component"/>. Callers seed <paramref name="visited"/> with the start node.
    /// </summary>
    private static void WalkComponent(GraphNode start, HashSet<GraphNode> visited, List<GraphNode> component, bool includeHidden, bool skipDashed = false)
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
                    Visit(wire, wire.From.Owner);
                }
            }

            foreach (var socket in node.Outputs)
            {
                foreach (var wire in socket.Wires)
                {
                    Visit(wire, wire.To.Owner);
                }
            }
        }

        void Visit(GraphWire wire, GraphNode neighbor)
        {
            if (skipDashed && wire.Dashed)
            {
                return;
            }

            if ((includeHidden || !neighbor.Hidden) && visited.Add(neighbor))
            {
                stack.Push(neighbor);
            }
        }
    }

    /// <summary>
    /// Visible components for packing: dashed wires (variable links, transitions) do not fuse two
    /// islands into one block, but a node reached only over dashed wires still packs with the
    /// island it links to the most, rather than drifting off as its own island.
    /// </summary>
    private List<List<GraphNode>> GetPackingComponents()
    {
        var components = CollectComponents(includeHidden: false, skipDashed: true);
        var componentOf = new Dictionary<GraphNode, int>();

        for (var i = 0; i < components.Count; i++)
        {
            foreach (var node in components[i])
            {
                componentOf[node] = i;
            }
        }

        var dashedOnly = new List<GraphNode>();

        foreach (var node in nodes)
        {
            if (node.Hidden || !componentOf.ContainsKey(node))
            {
                continue;
            }

            var wireCount = 0;
            var dashedCount = 0;

            foreach (var wire in WiresOf(node))
            {
                wireCount++;
                dashedCount += wire.Dashed ? 1 : 0;
            }

            if (wireCount > 0 && wireCount == dashedCount)
            {
                dashedOnly.Add(node);
            }
        }

        dashedOnly.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));

        var shared = new Dictionary<int, (int Count, int LowestSequence)>();

        foreach (var node in dashedOnly)
        {
            shared.Clear();
            var from = componentOf[node];

            foreach (var wire in WiresOf(node))
            {
                var partner = wire.From.Owner == node ? wire.To.Owner : wire.From.Owner;

                if (partner == node || !componentOf.TryGetValue(partner, out var partnerComponent) || partnerComponent == from)
                {
                    continue;
                }

                if (!shared.TryGetValue(partnerComponent, out var entry))
                {
                    entry = (0, int.MaxValue);
                }

                shared[partnerComponent] = (entry.Count + 1, Math.Min(entry.LowestSequence, partner.Sequence));
            }

            var target = -1;
            var best = (Count: 0, LowestSequence: int.MaxValue);

            foreach (var (component, entry) in shared)
            {
                if (entry.Count > best.Count || (entry.Count == best.Count && entry.LowestSequence < best.LowestSequence))
                {
                    target = component;
                    best = entry;
                }
            }

            if (target < 0)
            {
                continue;
            }

            components[from].Remove(node);
            components[target].Add(node);
            componentOf[node] = target;
        }

        components.RemoveAll(static component => component.Count == 0);
        return components;
    }

    /// <summary>Every wire touching a node, inputs first.</summary>
    /// <param name="node">The node to enumerate around.</param>
    public static IEnumerable<GraphWire> WiresOf(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        foreach (var socket in node.Inputs)
        {
            foreach (var wire in socket.Wires)
            {
                yield return wire;
            }
        }

        foreach (var socket in node.Outputs)
        {
            foreach (var wire in socket.Wires)
            {
                yield return wire;
            }
        }
    }

    /// <summary>
    /// Lays out each connected component independently and packs the components toward a
    /// screen-like aspect. Hidden nodes keep their positions and take no part in either.
    /// </summary>
    /// <param name="padding">Gap left around each island.</param>
    public void LayoutNodesPacked(float padding = 150f)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        EnsureGeometry();
        Geometry.ClearAllRoutes();

        var components = GetPackingComponents();

        // Discovery walks the z-order, which clicks mutate; layout runs on stable creation order.
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
            if (componentOf.TryGetValue(wire.From.Owner, out var c) &&
                componentOf.TryGetValue(wire.To.Owner, out var to) && c == to)
            {
                componentWires[c].Add(wire);
            }
        }

        // The budget covers one whole layout, not each of a hundred islands, so it is shared first.
        var slices = GraphLayout.SplitBudget([.. components.Select(static c => c.Count)], LayoutOptions.LayoutBudgetMs);

        for (var i = 0; i < components.Count; i++)
        {
            var component = components[i];

            if (component.Count == 1)
            {
                component[0].Position = Vector2.Zero;
                continue;
            }

            LayoutOptions.LayoutSliceMs = slices[i];
            GraphModelLayout.Layout(component, componentWires[i], Geometry, LayoutOptions);
        }

        LayoutOptions.LayoutSliceMs = null;

        var loose = components.Where(static c => c.Count == 1 && c[0].Inputs.Concat(c[0].Outputs).All(static s => s.Wires.Count == 0)).ToList();

        if (loose.Count > 0 && loose.Count < components.Count)
        {
            components = [.. components.Where(c => !loose.Contains(c))];
        }
        else
        {
            loose.Clear();
        }

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

                foreach (var socket in node.Outputs)
                {
                    foreach (var wire in socket.Wires)
                    {
                        if (Geometry.TryRouteOf(wire)?.Waypoints is { } waypoints)
                        {
                            for (var w = 0; w < waypoints.Count; w++)
                            {
                                waypoints[w] += offset;
                            }
                        }
                    }
                }
            }
        }

        StackLooseNodes(loose, padding);
    }

    /// <summary>
    /// Puts the wireless nodes in a single column past the right edge of everything else, stacked
    /// in creation order. They carry no structure to read, so mixing them into the packing would
    /// only push the parts that do connect further apart.
    /// </summary>
    private void StackLooseNodes(List<List<GraphNode>> loose, float padding)
    {
        if (loose.Count == 0)
        {
            return;
        }

        var right = float.MinValue;

        foreach (var node in nodes)
        {
            if (!node.Hidden && !loose.Any(c => c[0] == node))
            {
                right = Math.Max(right, node.Position.X + Geometry.SizeOf(node).X);
            }
        }

        var x = right > float.MinValue ? right + padding * 2f : 0f;
        var y = 0f;

        foreach (var node in loose.Select(static c => c[0]).OrderBy(static n => n.Sequence))
        {
            node.Position = new Vector2(x, y);
            y += Geometry.SizeOf(node).Y + LayoutOptions.NodeSpacing;
        }
    }

    /// <summary>
    /// Re-runs the crossing repair with no time limit, so a graph the budgeted pass gave up on
    /// gets the full treatment when the user explicitly asks for it. The repair moves cards out
    /// from under the routes the layout built for them, so a wire whose ends moved drops back to
    /// a plain curve; only self-loops keep a synthetic route.
    /// </summary>
    public void ReduceVisualComplexity()
    {
        EnsureGeometry();

        var positions = new Dictionary<GraphNode, Vector2>(nodes.Count);

        foreach (var node in nodes)
        {
            positions[node] = node.Position;
        }

        var previousBudget = LayoutOptions.LayoutBudgetMs;
        LayoutOptions.LayoutBudgetMs = 0;
        LayoutOptions.LayoutSliceMs = null;

        try
        {
            foreach (var component in GetVisibleComponents())
            {
                var componentNodes = new HashSet<GraphNode>(component);
                var componentWires = wires.Where(w => componentNodes.Contains(w.From.Owner) && componentNodes.Contains(w.To.Owner)).ToList();

                GraphModelLayout.RepairCrossings(component, componentWires, Geometry, LayoutOptions);
            }
        }
        finally
        {
            LayoutOptions.LayoutBudgetMs = previousBudget;
        }

        foreach (var wire in wires)
        {
            if (positions[wire.From.Owner] == wire.From.Owner.Position &&
                positions[wire.To.Owner] == wire.To.Owner.Position)
            {
                continue;
            }

            if (wire.From.Owner == wire.To.Owner)
            {
                GraphModelLayout.SynthesizeSelfLoop(wire, Geometry);
                continue;
            }

            var route = Geometry.TryRouteOf(wire);

            if (route != null)
            {
                route.Waypoints = null;
            }
        }
    }

    /// <summary>
    /// Rebuilds the routes of the wires touching a node after it moved: a self-loop keeps its
    /// synthetic orbit pinned to the node, and a routed wire drops back to a plain curve, since a
    /// routed curve cannot follow a moving node.
    /// </summary>
    /// <param name="node">The node that moved.</param>
    public void ReanchorWireWaypoints(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        foreach (var wire in WiresOf(node))
        {
            if (wire.From.Owner == wire.To.Owner)
            {
                GraphModelLayout.SynthesizeSelfLoop(wire, Geometry);
                continue;
            }

            var route = Geometry.TryRouteOf(wire);

            if (route != null)
            {
                route.Waypoints = null;
            }
        }
    }
}
