using System.Linq;

namespace ValveResourceFormat.Utils;

/// <summary>
/// Generic graph layout algorithm based on Sugiyama's hierarchical layout method.
/// </summary>
public static class GraphLayout
{
    /// <summary>
    /// Options for controlling the layout algorithm.
    /// </summary>
    public class LayoutOptions
    {
        /// <summary>
        /// Spacing between layers (columns) in pixels.
        /// </summary>
        public float LayerSpacing { get; set; } = 400f;

        /// <summary>
        /// Spacing between nodes within a layer in pixels.
        /// </summary>
        public float NodeSpacing { get; set; } = 50f;

        /// <summary>
        /// Maximum number of iterations for crossing reduction.
        /// </summary>
        public int MaxCrossingReductionIterations { get; set; } = 48;
    }

    /// <summary>
    /// Layouts nodes in a hierarchical arrangement using the Sugiyama algorithm.
    /// </summary>
    /// <typeparam name="TNode">Type representing a node.</typeparam>
    /// <typeparam name="TConnection">Type representing a connection between nodes.</typeparam>
    /// <param name="nodes">The nodes to layout.</param>
    /// <param name="connections">The connections between nodes.</param>
    /// <param name="getPosition">Function to get a node's current position.</param>
    /// <param name="setPosition">Function to set a node's position.</param>
    /// <param name="getSize">Function to get a node's size.</param>
    /// <param name="getSourceNode">Function to get the source node from a connection.</param>
    /// <param name="getTargetNode">Function to get the target node from a connection.</param>
    /// <param name="getInputConnections">Function to get connections where this node is the target (optional, for barycenter calculation).</param>
    /// <param name="getOutputConnections">Function to get connections where this node is the source (optional, for barycenter calculation).</param>
    /// <param name="options">Layout options (uses defaults if null).</param>
    public static void LayoutNodes<TNode, TConnection>(
        IEnumerable<TNode> nodes,
        IEnumerable<TConnection> connections,
        Func<TNode, Vector2> getPosition,
        Action<TNode, Vector2> setPosition,
        Func<TNode, Vector2> getSize,
        Func<TConnection, TNode> getSourceNode,
        Func<TConnection, TNode> getTargetNode,
        Func<TNode, IEnumerable<TConnection>> getInputConnections,
        Func<TNode, IEnumerable<TConnection>> getOutputConnections,
        LayoutOptions? options = null)
        where TNode : class
    {
        var nodeList = nodes.ToList();
        var connectionList = connections.ToList();

        if (nodeList.Count == 0)
        {
            return;
        }

        options ??= new LayoutOptions();

        // Build edge lookup for nodes if not provided
        var nodeInputConnections = new Dictionary<TNode, List<TConnection>>();
        var nodeOutputConnections = new Dictionary<TNode, List<TConnection>>();

        foreach (var node in nodeList)
        {
            nodeInputConnections[node] = [];
            nodeOutputConnections[node] = [];
        }

        foreach (var conn in connectionList)
        {
            var source = getSourceNode(conn);
            var target = getTargetNode(conn);

            if (source != null && nodeOutputConnections.TryGetValue(source, out var sourceOutputs))
            {
                sourceOutputs.Add(conn);
            }

            if (target != null && nodeInputConnections.TryGetValue(target, out var targetInputs))
            {
                targetInputs.Add(conn);
            }
        }

        // Step 1: Assign layers
        var (nodeLayers, nodeToLayer) = AssignLayers(
            nodeList, connectionList, getSourceNode, getTargetNode);

        // Step 2: Reduce crossings
        nodeLayers = ReduceCrossings(
            nodeLayers, nodeToLayer,
            getSourceNode, getTargetNode,
            getInputConnections, getOutputConnections,
            options.MaxCrossingReductionIterations);

        // Step 3: Assign coordinates
        AssignCoordinates(
            nodeLayers,
            getPosition, setPosition, getSize,
            getSourceNode, getTargetNode,
            getInputConnections, getOutputConnections,
            options.LayerSpacing, options.NodeSpacing);
    }

    private static (List<List<TNode>> nodeLayers, Dictionary<TNode, int> nodeToLayer) AssignLayers<TNode, TConnection>(
        List<TNode> nodes,
        List<TConnection> connections,
        Func<TConnection, TNode> getSourceNode,
        Func<TConnection, TNode> getTargetNode)
        where TNode : class
    {
        // Build graph structures
        var childrenRemaining = new Dictionary<TNode, int>(); // Out-degree: number of children not yet processed
        var parents = new Dictionary<TNode, List<TNode>>(); // Parent nodes for each node
        var nodeToLayer = new Dictionary<TNode, int>();

        foreach (var node in nodes)
        {
            childrenRemaining[node] = 0;
            parents[node] = [];
        }

        // Count children and build parent list
        foreach (var conn in connections)
        {
            var fromNode = getSourceNode(conn);
            var toNode = getTargetNode(conn);

            if (fromNode != null && toNode != null &&
                childrenRemaining.TryGetValue(fromNode, out var fromCount) &&
                parents.TryGetValue(toNode, out var toParents))
            {
                childrenRemaining[fromNode] = fromCount + 1; // fromNode has one more child
                toParents.Add(fromNode); // fromNode is a parent of toNode
            }
        }

        // Find sink nodes (nodes with no outgoing connections)
        var queue = new Queue<TNode>();
        foreach (var node in nodes)
        {
            if (childrenRemaining[node] == 0) // No children = sink node
            {
                nodeToLayer[node] = 0; // Rightmost layer
                queue.Enqueue(node);
            }
        }

        // Reverse BFS with longest path calculation
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentLayer = nodeToLayer[current];

            // Process all parent nodes (nodes that connect TO current)
            foreach (var parent in parents[current])
            {
                // Parent should be at least one layer to the left (use max for longest path)
                var proposedLayer = currentLayer + 1;
                nodeToLayer[parent] = Math.Max(nodeToLayer.GetValueOrDefault(parent), proposedLayer);

                // Decrement children remaining (one child has been processed)
                if (--childrenRemaining[parent] == 0)
                {
                    queue.Enqueue(parent);
                }
            }
        }

        // Handle disconnected nodes (nodes not reachable from any sink)
        var maxLayer = nodeToLayer.Count > 0 ? nodeToLayer.Values.Max() : 0;
        foreach (var node in nodes)
        {
            if (!nodeToLayer.ContainsKey(node))
            {
                // Place disconnected nodes at the leftmost layer
                nodeToLayer[node] = maxLayer + 1;
            }
        }

        // Normalize layers (reverse so layer 0 is leftmost)
        maxLayer = nodeToLayer.Values.Max();
        foreach (var node in nodeToLayer.Keys.ToList())
        {
            nodeToLayer[node] = maxLayer - nodeToLayer[node];
        }

        // Store results in layer-indexed structure
        var nodeLayers = Enumerable.Range(0, maxLayer + 1)
            .Select(_ => new List<TNode>())
            .ToList();

        foreach (var (node, layer) in nodeToLayer)
        {
            nodeLayers[layer].Add(node);
        }

        return (nodeLayers, nodeToLayer);
    }

    private static List<TNode> GetEdgesToLayer<TNode, TConnection>(
        TNode node,
        int targetLayer,
        Dictionary<TNode, int> nodeToLayer,
        Func<TNode, IEnumerable<TConnection>> getOutputConnections,
        Func<TConnection, TNode> getTargetNode)
        where TNode : class
    {
        var targets = new List<TNode>();

        foreach (var conn in getOutputConnections(node))
        {
            var targetNode = getTargetNode(conn);
            if (targetNode != null && nodeToLayer.TryGetValue(targetNode, out var layer) && layer == targetLayer)
            {
                targets.Add(targetNode);
            }
        }

        return targets;
    }

    private static int CountCrossings<TNode, TConnection>(
        List<List<TNode>> nodeLayers,
        Dictionary<TNode, int> nodeToLayer,
        Func<TNode, IEnumerable<TConnection>> getOutputConnections,
        Func<TConnection, TNode> getTargetNode)
        where TNode : class
    {
        var totalCrossings = 0;

        // For each pair of adjacent layers
        for (var layerIdx = 0; layerIdx < nodeLayers.Count - 1; layerIdx++)
        {
            var leftLayer = nodeLayers[layerIdx];
            var rightLayer = nodeLayers[layerIdx + 1];

            // For each pair of edges between these layers
            for (var i = 0; i < leftLayer.Count; i++)
            {
                for (var j = i + 1; j < leftLayer.Count; j++)
                {
                    var node1 = leftLayer[i];
                    var node2 = leftLayer[j];

                    // Get all edges from node1 and node2 to right layer
                    var edges1 = GetEdgesToLayer(node1, layerIdx + 1, nodeToLayer, getOutputConnections, getTargetNode);
                    var edges2 = GetEdgesToLayer(node2, layerIdx + 1, nodeToLayer, getOutputConnections, getTargetNode);

                    // Count crossings between these edge sets
                    foreach (var edge1Target in edges1)
                    {
                        var pos1 = rightLayer.IndexOf(edge1Target);
                        foreach (var edge2Target in edges2)
                        {
                            var pos2 = rightLayer.IndexOf(edge2Target);

                            // Crossing occurs if edges "cross" (node1 is above node2, but target1 is below target2)
                            if (pos1 > pos2)
                            {
                                totalCrossings++;
                            }
                        }
                    }
                }
            }
        }

        return totalCrossings;
    }

    private static float CalculateBarycenter<TNode, TConnection>(
        TNode node,
        int adjacentLayer,
        bool isForward,
        List<List<TNode>> nodeLayers,
        Dictionary<TNode, int> nodeToLayer,
        Func<TNode, IEnumerable<TConnection>> getInputConnections,
        Func<TNode, IEnumerable<TConnection>> getOutputConnections,
        Func<TConnection, TNode> getSourceNode,
        Func<TConnection, TNode> getTargetNode)
        where TNode : class
    {
        var connectedPositions = new List<float>();
        var adjacentNodes = nodeLayers[adjacentLayer];

        if (isForward)
        {
            // Node is in right layer, look at left layer (inputs)
            foreach (var conn in getInputConnections(node))
            {
                var connectedNode = getSourceNode(conn);
                if (connectedNode != null && nodeToLayer.TryGetValue(connectedNode, out var layer) && layer == adjacentLayer)
                {
                    var position = adjacentNodes.IndexOf(connectedNode);
                    if (position >= 0)
                    {
                        connectedPositions.Add(position);
                    }
                }
            }
        }
        else
        {
            // Node is in left layer, look at right layer (outputs)
            foreach (var conn in getOutputConnections(node))
            {
                var connectedNode = getTargetNode(conn);
                if (connectedNode != null && nodeToLayer.TryGetValue(connectedNode, out var layer) && layer == adjacentLayer)
                {
                    var position = adjacentNodes.IndexOf(connectedNode);
                    if (position >= 0)
                    {
                        connectedPositions.Add(position);
                    }
                }
            }
        }

        // Return median position (more robust than average for high fan-out) or current position if no connections
        if (connectedPositions.Count > 0)
        {
            return CalculateMedian(connectedPositions);
        }

        return nodeToLayer.TryGetValue(node, out var currentLayer)
            ? nodeLayers[currentLayer].IndexOf(node)
            : 0;
    }

    private static void OrderByBarycenter<TNode, TConnection>(
        int fixedLayer,
        int targetLayer,
        bool isForward,
        List<List<TNode>> nodeLayers,
        Dictionary<TNode, int> nodeToLayer,
        Func<TNode, IEnumerable<TConnection>> getInputConnections,
        Func<TNode, IEnumerable<TConnection>> getOutputConnections,
        Func<TConnection, TNode> getSourceNode,
        Func<TConnection, TNode> getTargetNode)
        where TNode : class
    {
        var nodeBarycenters = new List<(TNode node, float barycenter)>();

        foreach (var node in nodeLayers[targetLayer])
        {
            var barycenter = CalculateBarycenter(
                node, fixedLayer, isForward, nodeLayers, nodeToLayer,
                getInputConnections, getOutputConnections, getSourceNode, getTargetNode);
            nodeBarycenters.Add((node, barycenter));
        }

        // Sort by barycenter value
        nodeBarycenters.Sort((a, b) => a.barycenter.CompareTo(b.barycenter));

        // Update layer order
        nodeLayers[targetLayer].Clear();
        foreach (var (node, _) in nodeBarycenters)
        {
            nodeLayers[targetLayer].Add(node);
        }
    }

    private static float CalculateMedian(List<float> values)
    {
        if (values.Count == 0)
        {
            return 0f;
        }

        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[mid - 1] + values[mid]) / 2
            : values[mid];
    }

    private static List<List<TNode>> ReduceCrossings<TNode, TConnection>(
        List<List<TNode>> nodeLayers,
        Dictionary<TNode, int> nodeToLayer,
        Func<TConnection, TNode> getSourceNode,
        Func<TConnection, TNode> getTargetNode,
        Func<TNode, IEnumerable<TConnection>> getInputConnections,
        Func<TNode, IEnumerable<TConnection>> getOutputConnections,
        int maxIterations)
        where TNode : class
    {
        var bestCrossings = int.MaxValue;
        List<List<TNode>>? bestConfiguration = null;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            // Alternate direction each iteration
            var forwardPass = (iteration % 2 == 0);

            if (forwardPass)
            {
                // Left to right: fix layer i, optimize layer i+1
                for (var layer = 0; layer < nodeLayers.Count - 1; layer++)
                {
                    OrderByBarycenter(
                        layer, layer + 1, isForward: true, nodeLayers, nodeToLayer,
                        getInputConnections, getOutputConnections, getSourceNode, getTargetNode);
                }
            }
            else
            {
                // Right to left: fix layer i, optimize layer i-1
                for (var layer = nodeLayers.Count - 1; layer > 0; layer--)
                {
                    OrderByBarycenter(
                        layer, layer - 1, isForward: false, nodeLayers, nodeToLayer,
                        getInputConnections, getOutputConnections, getSourceNode, getTargetNode);
                }
            }

            // Count crossings
            var currentCrossings = CountCrossings(nodeLayers, nodeToLayer, getOutputConnections, getTargetNode);

            // Track best configuration
            if (currentCrossings < bestCrossings)
            {
                bestCrossings = currentCrossings;
                bestConfiguration = [.. nodeLayers.Select(layer => layer.ToList())];
            }

            // Early exit if no crossings
            if (currentCrossings == 0)
            {
                break;
            }
        }

        // Restore best configuration
        return bestConfiguration ?? nodeLayers;
    }

    private static List<float> CalculateIdealYPositions<TNode, TConnection>(
        List<TNode> layer,
        Func<TNode, Vector2> getPosition,
        Func<TNode, Vector2> getSize,
        Func<TNode, IEnumerable<TConnection>> getInputConnections,
        Func<TNode, IEnumerable<TConnection>> getOutputConnections,
        Func<TConnection, TNode> getSourceNode,
        Func<TConnection, TNode> getTargetNode,
        float nodeSpacing)
        where TNode : class
    {
        var idealPositions = new List<float>();

        foreach (var node in layer)
        {
            // Calculate ideal Y as median of INPUT connections only (from previous layer)
            // This positions nodes near their data sources, reducing wire length
            var inputYPositions = new List<float>();
            foreach (var conn in getInputConnections(node))
            {
                var sourceNode = getSourceNode(conn);
                if (sourceNode != null)
                {
                    var sourcePos = getPosition(sourceNode);
                    var sourceSize = getSize(sourceNode);
                    inputYPositions.Add(sourcePos.Y + sourceSize.Y / 2);
                }
            }

            float idealY;
            if (inputYPositions.Count > 0)
            {
                idealY = CalculateMedian(inputYPositions);
            }
            else
            {
                // No input connections: try to position near outputs instead
                var outputYPositions = new List<float>();
                foreach (var conn in getOutputConnections(node))
                {
                    var targetNode = getTargetNode(conn);
                    if (targetNode != null)
                    {
                        var targetPos = getPosition(targetNode);
                        var targetSize = getSize(targetNode);
                        outputYPositions.Add(targetPos.Y + targetSize.Y / 2);
                    }
                }

                if (outputYPositions.Count > 0)
                {
                    idealY = CalculateMedian(outputYPositions);
                }
                else
                {
                    // No connections at all: use tight spacing
                    idealY = idealPositions.Count > 0
                        ? idealPositions[^1] + nodeSpacing
                        : 0f;
                }
            }

            idealPositions.Add(idealY);
        }

        return idealPositions;
    }

    private static List<float> ResolveVerticalOverlaps<TNode>(
        List<TNode> layer,
        List<float> idealYPositions,
        Func<TNode, Vector2> getSize,
        float nodeSpacing)
        where TNode : class
    {
        var finalPositions = new List<float>(idealYPositions);

        // Forward pass: resolve overlaps by adjusting positions while maintaining order
        for (var i = 1; i < layer.Count; i++)
        {
            var prevNode = layer[i - 1];
            var prevSize = getSize(prevNode);
            var minY = finalPositions[i - 1] + prevSize.Y + nodeSpacing;

            if (finalPositions[i] < minY)
            {
                finalPositions[i] = minY;
            }
        }

        // Backward pass to balance spacing
        for (var i = layer.Count - 2; i >= 0; i--)
        {
            var currNode = layer[i];
            var currSize = getSize(currNode);
            var maxY = finalPositions[i + 1] - currSize.Y - nodeSpacing;

            if (finalPositions[i] > maxY)
            {
                finalPositions[i] = maxY;
            }
        }

        return finalPositions;
    }

    private static void AssignCoordinates<TNode, TConnection>(
        List<List<TNode>> nodeLayers,
        Func<TNode, Vector2> getPosition,
        Action<TNode, Vector2> setPosition,
        Func<TNode, Vector2> getSize,
        Func<TConnection, TNode> getSourceNode,
        Func<TConnection, TNode> getTargetNode,
        Func<TNode, IEnumerable<TConnection>> getInputConnections,
        Func<TNode, IEnumerable<TConnection>> getOutputConnections,
        float layerSpacing,
        float nodeSpacing)
        where TNode : class
    {
        // Assign X and Y coordinates for each layer
        var currentX = 0f;

        for (var layerIdx = 0; layerIdx < nodeLayers.Count; layerIdx++)
        {
            var layer = nodeLayers[layerIdx];

            if (layer.Count == 0)
            {
                continue;
            }

            // Split layer into nodes with inputs and nodes without inputs
            var nodesWithInputs = new List<TNode>();
            var nodesWithoutInputs = new List<TNode>();

            foreach (var node in layer)
            {
                var hasInputs = getInputConnections(node).Any();
                if (hasInputs)
                {
                    nodesWithInputs.Add(node);
                }
                else
                {
                    nodesWithoutInputs.Add(node);
                }
            }

            // Position nodes with inputs in main column
            if (nodesWithInputs.Count > 0)
            {
                var idealYPositions = CalculateIdealYPositions(
                    nodesWithInputs, getPosition, getSize,
                    getInputConnections, getOutputConnections,
                    getSourceNode, getTargetNode, nodeSpacing);
                var finalYPositions = ResolveVerticalOverlaps(nodesWithInputs, idealYPositions, getSize, nodeSpacing);
                var maxWidth = nodesWithInputs.Max(n => getSize(n).X);

                for (var i = 0; i < nodesWithInputs.Count; i++)
                {
                    var node = nodesWithInputs[i];
                    setPosition(node, new Vector2(currentX, finalYPositions[i]));
                }

                currentX += maxWidth;
            }

            // Position nodes without inputs in a sub-column (between main column and next layer)
            if (nodesWithoutInputs.Count > 0)
            {
                var subColumnX = currentX + (nodesWithInputs.Count > 0 ? layerSpacing / 2 : 0);
                var idealYPositions = CalculateIdealYPositions(
                    nodesWithoutInputs, getPosition, getSize,
                    getInputConnections, getOutputConnections,
                    getSourceNode, getTargetNode, nodeSpacing);
                // Use larger spacing for nodes without inputs to reduce wire overlap
                var finalYPositions = ResolveVerticalOverlaps(nodesWithoutInputs, idealYPositions, getSize, nodeSpacing * 3);
                var maxWidth = nodesWithoutInputs.Max(n => getSize(n).X);

                for (var i = 0; i < nodesWithoutInputs.Count; i++)
                {
                    var node = nodesWithoutInputs[i];
                    setPosition(node, new Vector2(subColumnX, finalYPositions[i]));
                }

                currentX = subColumnX + maxWidth;
            }

            // Move to next layer
            currentX += layerSpacing;
        }

        // Step 3: Refine positions for nodes without inputs now that outputs are positioned
        RefineNodesWithoutInputs(
            nodeLayers, getPosition, setPosition, getSize,
            getInputConnections, getOutputConnections,
            getTargetNode, nodeSpacing);
    }

    private static void RefineNodesWithoutInputs<TNode, TConnection>(
        List<List<TNode>> nodeLayers,
        Func<TNode, Vector2> getPosition,
        Action<TNode, Vector2> setPosition,
        Func<TNode, Vector2> getSize,
        Func<TNode, IEnumerable<TConnection>> getInputConnections,
        Func<TNode, IEnumerable<TConnection>> getOutputConnections,
        Func<TConnection, TNode> getTargetNode,
        float nodeSpacing)
        where TNode : class
    {
        // Go through each layer and adjust nodes without inputs based on their output positions
        for (var layerIdx = 0; layerIdx < nodeLayers.Count - 1; layerIdx++)
        {
            var layer = nodeLayers[layerIdx];
            if (layer.Count == 0)
            {
                continue;
            }

            // Collect nodes without inputs from this layer
            var nodesWithoutInputs = layer
                .Where(n => !getInputConnections(n).Any())
                .ToList();

            if (nodesWithoutInputs.Count == 0)
            {
                continue;
            }

            // Recalculate ideal Y positions based on outputs (which are now positioned)
            var adjustments = new List<(TNode node, float idealY)>();

            foreach (var node in nodesWithoutInputs)
            {
                var outputYPositions = new List<float>();
                foreach (var conn in getOutputConnections(node))
                {
                    var targetNode = getTargetNode(conn);
                    if (targetNode != null)
                    {
                        var targetPos = getPosition(targetNode);
                        var targetSize = getSize(targetNode);
                        outputYPositions.Add(targetPos.Y + targetSize.Y / 2);
                    }
                }

                if (outputYPositions.Count > 0)
                {
                    var idealY = CalculateMedian(outputYPositions);
                    adjustments.Add((node, idealY));
                }
            }

            if (adjustments.Count == 0)
            {
                continue;
            }

            // Sort by ideal Y to maintain relative order
            adjustments = [.. adjustments.OrderBy(a => a.idealY)];

            // Use minimal spacing to avoid overlaps while staying close to ideal positions
            var idealYs = adjustments.Select(a => a.idealY).ToList();
            var finalYs = ResolveVerticalOverlaps(
                [.. adjustments.Select(a => a.node)],
                idealYs,
                getSize,
                nodeSpacing  // Use normal spacing (50px) instead of 3x to minimize position drift
            );

            // Apply new positions
            for (var i = 0; i < adjustments.Count; i++)
            {
                var node = adjustments[i].node;
                var currentPos = getPosition(node);
                setPosition(node, new Vector2(currentPos.X, finalYs[i]));
            }
        }
    }
}
