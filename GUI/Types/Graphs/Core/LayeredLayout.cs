namespace GUI.Types.Graphs.Core;

/// <summary>
/// Layered layout for graph views. Compared to the generic <see cref="GraphLayout"/> it
/// removes feedback edges before layering (cycles otherwise collapse into one column) and
/// routes multi-layer edges through virtual slots so ordering accounts for pass-through wires.
/// </summary>
internal static class LayeredLayout
{
    private const float VirtualSlotHeight = 8f;
    private const int OrderingSweeps = 12;
    private const int StraighteningSweeps = 4;

    private struct Slot
    {
        public int NodeIndex;   // -1 for virtual slots
        public float Height;
        public float Y;
        public List<int> Prev;  // slot ids in the previous layer
        public List<int> Next;  // slot ids in the next layer
    }

    public static void Layout(List<GraphNode> nodes, List<(GraphNode From, GraphNode To)> nodeEdges, float layerSpacing, float nodeSpacing, float maxColumnHeight = 0f, bool squareBlocks = false, Action<(GraphNode From, GraphNode To), List<Vector2>>? setEdgeWaypoints = null)
    {
        var count = nodes.Count;

        if (count == 0)
        {
            return;
        }

        var indexOf = new Dictionary<GraphNode, int>(count);

        for (var i = 0; i < count; i++)
        {
            indexOf[nodes[i]] = i;
        }

        // Distinct directed edges between distinct nodes.
        var edges = new HashSet<(int From, int To)>();

        foreach (var (fromNode, toNode) in nodeEdges)
        {
            if (indexOf.TryGetValue(fromNode, out var from) &&
                indexOf.TryGetValue(toNode, out var to) &&
                from != to)
            {
                edges.Add((from, to));
            }
        }

        var outgoing = new List<int>[count];

        for (var i = 0; i < count; i++)
        {
            outgoing[i] = [];
        }

        foreach (var (from, to) in edges)
        {
            outgoing[from].Add(to);
        }

        var feedback = FindFeedbackEdges(count, outgoing);

        // Longest-path layering over the acyclic edge set.
        var layerOf = AssignLayers(count, edges, feedback);
        var layerCount = 0;

        for (var i = 0; i < count; i++)
        {
            layerCount = Math.Max(layerCount, layerOf[i] + 1);
        }

        // Build layer slot lists; long edges contribute virtual slots per crossed layer.
        var layers = new List<int>[layerCount];

        for (var i = 0; i < layerCount; i++)
        {
            layers[i] = [];
        }

        var slots = new List<Slot>(count);
        var slotLayer = new List<int>(count);

        for (var i = 0; i < count; i++)
        {
            slots.Add(new Slot { NodeIndex = i, Height = nodes[i].Size.Y, Prev = [], Next = [] });
            slotLayer.Add(layerOf[i]);
            layers[layerOf[i]].Add(i);
        }

        var edgeChains = new List<(int From, int To, List<int> Virtuals)>();

        foreach (var (from, to) in edges)
        {
            if (feedback.Contains((from, to)) || layerOf[to] <= layerOf[from])
            {
                continue;
            }

            var previous = from;
            List<int>? chain = null;

            for (var layer = layerOf[from] + 1; layer < layerOf[to]; layer++)
            {
                var virtualId = slots.Count;
                slots.Add(new Slot { NodeIndex = -1, Height = VirtualSlotHeight, Prev = [], Next = [] });
                slotLayer.Add(layer);
                layers[layer].Add(virtualId);

                LinkSlots(slots, previous, virtualId);
                previous = virtualId;
                (chain ??= []).Add(virtualId);
            }

            LinkSlots(slots, previous, to);

            if (chain != null)
            {
                edgeChains.Add((from, to, chain));
            }
        }

        OrderLayers(layers, slots);
        AssignY(layers, slots, nodeSpacing);

        // X per layer from the widest real node; virtual-only stretches stay narrow.
        // Layers taller than maxColumnHeight wrap into adjacent sub-columns (order preserved),
        // because entity graphs are shallow but wide: without wrapping, a five-layer graph with
        // hundreds of nodes becomes a 30:1 tower.
        var x = 0f;
        var layerMidX = new float[layerCount];

        for (var layer = 0; layer < layerCount; layer++)
        {
            var realNodes = new List<GraphNode>();
            var totalHeight = 0f;
            var maxWidth = 0f;

            foreach (var slotId in layers[layer])
            {
                var nodeIndex = slots[slotId].NodeIndex;

                if (nodeIndex >= 0)
                {
                    realNodes.Add(nodes[nodeIndex]);
                    totalHeight += nodes[nodeIndex].Size.Y + nodeSpacing;
                    maxWidth = Math.Max(maxWidth, nodes[nodeIndex].Size.X);
                }
            }

            // Square blocks: derive the wrap height per layer so the wrapped block comes out
            // roughly as wide as it is tall.
            var effectiveCap = maxColumnHeight;

            if (squareBlocks && realNodes.Count > 0)
            {
                effectiveCap = Math.Clamp(MathF.Sqrt(totalHeight * (maxWidth + nodeSpacing)), 900f, 4200f);
            }

            if (effectiveCap <= 0f || totalHeight <= effectiveCap)
            {
                foreach (var slotId in layers[layer])
                {
                    var slot = slots[slotId];

                    if (slot.NodeIndex >= 0)
                    {
                        nodes[slot.NodeIndex].Position = new Vector2(x, slot.Y);
                    }
                }

                layerMidX[layer] = x + maxWidth / 2f;
                x += maxWidth + layerSpacing;
                continue;
            }

            var subX = x;
            var subY = 0f;
            var subMaxWidth = 0f;
            var layerRight = x;

            foreach (var node in realNodes)
            {
                if (subY > 0f && subY + node.Size.Y > effectiveCap)
                {
                    subX += subMaxWidth + nodeSpacing;
                    subY = 0f;
                    subMaxWidth = 0f;
                }

                node.Position = new Vector2(subX, subY);
                subY += node.Size.Y + nodeSpacing;
                subMaxWidth = Math.Max(subMaxWidth, node.Size.X);
                layerRight = Math.Max(layerRight, subX + subMaxWidth);
            }

            layerMidX[layer] = (x + layerRight) / 2f;
            x = layerRight + layerSpacing;
        }

        // Wire routing: expose the lane centers of each long edge's virtual slots so wires can
        // be drawn through the gaps the ordering reserved instead of over unrelated nodes.
        if (setEdgeWaypoints != null)
        {
            foreach (var (from, to, virtuals) in edgeChains)
            {
                var points = new List<Vector2>(virtuals.Count);

                foreach (var virtualId in virtuals)
                {
                    points.Add(new Vector2(layerMidX[slotLayer[virtualId]], slots[virtualId].Y + slots[virtualId].Height / 2f));
                }

                setEdgeWaypoints((nodes[from], nodes[to]), points);
            }
        }
    }

    private static void LinkSlots(List<Slot> slots, int from, int to)
    {
        slots[from].Next.Add(to);
        slots[to].Prev.Add(from);
    }

    // Iterative DFS; edges into the active stack (gray) are feedback edges.
    private static HashSet<(int From, int To)> FindFeedbackEdges(int count, List<int>[] outgoing)
    {
        var feedback = new HashSet<(int From, int To)>();
        var state = new byte[count]; // 0 unvisited, 1 on stack, 2 done
        var stack = new Stack<(int Node, int EdgeIndex)>();

        for (var start = 0; start < count; start++)
        {
            if (state[start] != 0)
            {
                continue;
            }

            state[start] = 1;
            stack.Push((start, 0));

            while (stack.Count > 0)
            {
                var (node, edgeIndex) = stack.Pop();

                if (edgeIndex < outgoing[node].Count)
                {
                    stack.Push((node, edgeIndex + 1));
                    var next = outgoing[node][edgeIndex];

                    if (state[next] == 0)
                    {
                        state[next] = 1;
                        stack.Push((next, 0));
                    }
                    else if (state[next] == 1)
                    {
                        feedback.Add((node, next));
                    }
                }
                else
                {
                    state[node] = 2;
                }
            }
        }

        return feedback;
    }

    private static int[] AssignLayers(int count, HashSet<(int From, int To)> edges, HashSet<(int From, int To)> feedback)
    {
        var layerOf = new int[count];
        var incoming = new int[count];
        var outgoing = new List<int>[count];

        for (var i = 0; i < count; i++)
        {
            outgoing[i] = [];
        }

        foreach (var (from, to) in edges)
        {
            if (feedback.Contains((from, to)))
            {
                continue;
            }

            outgoing[from].Add(to);
            incoming[to]++;
        }

        var queue = new Queue<int>();

        for (var i = 0; i < count; i++)
        {
            if (incoming[i] == 0)
            {
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();

            foreach (var next in outgoing[node])
            {
                layerOf[next] = Math.Max(layerOf[next], layerOf[node] + 1);

                if (--incoming[next] == 0)
                {
                    queue.Enqueue(next);
                }
            }
        }

        return layerOf;
    }

    // Median barycenter sweeps over slot positions, alternating direction.
    private static void OrderLayers(List<int>[] layers, List<Slot> slots)
    {
        var position = new float[slots.Count];

        void RefreshPositions()
        {
            foreach (var layer in layers)
            {
                for (var i = 0; i < layer.Count; i++)
                {
                    position[layer[i]] = i;
                }
            }
        }

        RefreshPositions();

        var medianBuffer = new List<float>();

        float MedianOf(List<int> neighborSlots, int fallbackSlot)
        {
            if (neighborSlots.Count == 0)
            {
                return position[fallbackSlot];
            }

            medianBuffer.Clear();

            foreach (var neighbor in neighborSlots)
            {
                medianBuffer.Add(position[neighbor]);
            }

            medianBuffer.Sort();
            var mid = medianBuffer.Count / 2;
            return medianBuffer.Count % 2 == 0 ? (medianBuffer[mid - 1] + medianBuffer[mid]) / 2f : medianBuffer[mid];
        }

        for (var sweep = 0; sweep < OrderingSweeps; sweep++)
        {
            var forward = sweep % 2 == 0;

            if (forward)
            {
                for (var layer = 1; layer < layers.Length; layer++)
                {
                    layers[layer].Sort((a, b) => MedianOf(slots[a].Prev, a).CompareTo(MedianOf(slots[b].Prev, b)));
                    RefreshPositions();
                }
            }
            else
            {
                for (var layer = layers.Length - 2; layer >= 0; layer--)
                {
                    layers[layer].Sort((a, b) => MedianOf(slots[a].Next, a).CompareTo(MedianOf(slots[b].Next, b)));
                    RefreshPositions();
                }
            }
        }
    }

    // Initial stacking, then median-of-neighbors straightening with overlap resolution.
    private static void AssignY(List<int>[] layers, List<Slot> slots, float nodeSpacing)
    {
        foreach (var layer in layers)
        {
            var y = 0f;

            foreach (var slotId in layer)
            {
                var slot = slots[slotId];
                slot.Y = y;
                slots[slotId] = slot;
                y += slot.Height + nodeSpacing;
            }
        }

        var idealBuffer = new List<float>();

        float CenterOf(int slotId) => slots[slotId].Y + slots[slotId].Height / 2f;

        for (var sweep = 0; sweep < StraighteningSweeps; sweep++)
        {
            foreach (var layer in layers)
            {
                // Ideal center = median of neighbor centers on both sides.
                var ideals = new float[layer.Count];

                for (var i = 0; i < layer.Count; i++)
                {
                    var slot = slots[layer[i]];
                    idealBuffer.Clear();

                    foreach (var neighbor in slot.Prev)
                    {
                        idealBuffer.Add(CenterOf(neighbor));
                    }

                    foreach (var neighbor in slot.Next)
                    {
                        idealBuffer.Add(CenterOf(neighbor));
                    }

                    if (idealBuffer.Count == 0)
                    {
                        ideals[i] = CenterOf(layer[i]);
                        continue;
                    }

                    idealBuffer.Sort();
                    var mid = idealBuffer.Count / 2;
                    ideals[i] = idealBuffer.Count % 2 == 0 ? (idealBuffer[mid - 1] + idealBuffer[mid]) / 2f : idealBuffer[mid];
                }

                // Convert ideal centers to tops, then resolve overlaps keeping order.
                for (var i = 0; i < layer.Count; i++)
                {
                    var slot = slots[layer[i]];
                    slot.Y = ideals[i] - slot.Height / 2f;
                    slots[layer[i]] = slot;
                }

                for (var i = 1; i < layer.Count; i++)
                {
                    var previous = slots[layer[i - 1]];
                    var slot = slots[layer[i]];
                    var minY = previous.Y + previous.Height + nodeSpacing;

                    if (slot.Y < minY)
                    {
                        slot.Y = minY;
                        slots[layer[i]] = slot;
                    }
                }

                for (var i = layer.Count - 2; i >= 0; i--)
                {
                    var next = slots[layer[i + 1]];
                    var slot = slots[layer[i]];
                    var maxY = next.Y - nodeSpacing - slot.Height;

                    if (slot.Y > maxY)
                    {
                        slot.Y = maxY;
                        slots[layer[i]] = slot;
                    }
                }
            }
        }
    }
}
