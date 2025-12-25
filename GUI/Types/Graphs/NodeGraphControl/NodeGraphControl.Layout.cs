using System.Linq;
using SkiaSharp;

#nullable disable

namespace GUI.Types.Graphs
{
    public partial class NodeGraphControl
    {
        // Layout algorithm constants
        private const int MaxCrossingReductionIterations = 48;
        private const float DefaultLayerSpacing = 400f;
        private const float DefaultNodeSpacing = 50f;

        public void LayoutNodes()
        {
            if (_graphNodes.Count == 0)
            {
                return;
            }

            // Sugiyama algorithm for hierarchical graph layout
            var (nodeLayers, nodeToLayer) = AssignLayers();
            nodeLayers = ReduceCrossings(nodeLayers, nodeToLayer, MaxCrossingReductionIterations);
            AssignCoordinates(nodeLayers, DefaultLayerSpacing, DefaultNodeSpacing);

            OnGraphChanged();
        }

        private (List<List<AbstractNode>> nodeLayers, Dictionary<AbstractNode, int> nodeToLayer) AssignLayers()
        {
            // Step 1: Build graph structures
            var childrenRemaining = new Dictionary<AbstractNode, int>(); // Out-degree: number of children not yet processed
            var parents = new Dictionary<AbstractNode, List<AbstractNode>>(); // Parent nodes for each node
            var nodeToLayer = new Dictionary<AbstractNode, int>();

            foreach (var node in _graphNodes)
            {
                childrenRemaining[node] = 0;
                parents[node] = [];
            }

            // Count children and build parent list
            foreach (var wire in _connections)
            {
                var fromNode = wire.From.Owner;
                var toNode = wire.To.Owner;

                childrenRemaining[fromNode]++; // fromNode has one more child
                parents[toNode].Add(fromNode); // fromNode is a parent of toNode
            }

            // Step 2: Find sink nodes (nodes with no outgoing connections)
            var queue = new Queue<AbstractNode>();
            foreach (var node in _graphNodes)
            {
                if (childrenRemaining[node] == 0) // No children = sink node
                {
                    nodeToLayer[node] = 0; // Rightmost layer
                    queue.Enqueue(node);
                }
            }

            // Step 3: Reverse BFS with longest path calculation
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

            // Step 4: Handle disconnected nodes (nodes not reachable from any sink)
            var maxLayer = nodeToLayer.Count > 0 ? nodeToLayer.Values.Max() : 0;
            foreach (var node in _graphNodes)
            {
                if (!nodeToLayer.ContainsKey(node))
                {
                    // Place disconnected nodes at the leftmost layer
                    nodeToLayer[node] = maxLayer + 1;
                }
            }

            // Step 5: Normalize layers (reverse so layer 0 is leftmost)
            maxLayer = nodeToLayer.Values.Max();
            foreach (var node in nodeToLayer.Keys.ToList())
            {
                nodeToLayer[node] = maxLayer - nodeToLayer[node];
            }

            // Step 6: Store results in layer-indexed structure
            var nodeLayers = Enumerable.Range(0, maxLayer + 1)
                .Select(_ => new List<AbstractNode>())
                .ToList();

            foreach (var (node, layer) in nodeToLayer)
            {
                nodeLayers[layer].Add(node);
            }

            return (nodeLayers, nodeToLayer);
        }

        private static List<AbstractNode> GetEdgesToLayer(AbstractNode node, int targetLayer, Dictionary<AbstractNode, int> nodeToLayer)
        {
            var targets = new List<AbstractNode>();

            foreach (var socket in node.Sockets.OfType<SocketOut>())
            {
                foreach (var wire in socket.Connections)
                {
                    var targetNode = wire.To.Owner;
                    if (nodeToLayer.TryGetValue(targetNode, out var layer) && layer == targetLayer)
                    {
                        targets.Add(targetNode);
                    }
                }
            }

            return targets;
        }

        private static int CountCrossings(List<List<AbstractNode>> nodeLayers, Dictionary<AbstractNode, int> nodeToLayer)
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
                        var edges1 = GetEdgesToLayer(node1, layerIdx + 1, nodeToLayer);
                        var edges2 = GetEdgesToLayer(node2, layerIdx + 1, nodeToLayer);

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

        private static float CalculateBarycenter(AbstractNode node, int adjacentLayer, bool isForward, List<List<AbstractNode>> nodeLayers, Dictionary<AbstractNode, int> nodeToLayer)
        {
            var connectedPositions = new List<float>();
            var adjacentNodes = nodeLayers[adjacentLayer];

            if (isForward)
            {
                // Node is in right layer, look at left layer (inputs)
                foreach (var socket in node.Sockets.OfType<SocketIn>())
                {
                    foreach (var wire in socket.Connections)
                    {
                        var connectedNode = wire.From.Owner;
                        if (nodeToLayer.TryGetValue(connectedNode, out var layer) && layer == adjacentLayer)
                        {
                            var position = adjacentNodes.IndexOf(connectedNode);
                            if (position >= 0)
                            {
                                connectedPositions.Add(position);
                            }
                        }
                    }
                }
            }
            else
            {
                // Node is in left layer, look at right layer (outputs)
                foreach (var socket in node.Sockets.OfType<SocketOut>())
                {
                    foreach (var wire in socket.Connections)
                    {
                        var connectedNode = wire.To.Owner;
                        if (nodeToLayer.TryGetValue(connectedNode, out var layer) && layer == adjacentLayer)
                        {
                            var position = adjacentNodes.IndexOf(connectedNode);
                            if (position >= 0)
                            {
                                connectedPositions.Add(position);
                            }
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

        private static void OrderByBarycenter(int fixedLayer, int targetLayer, bool isForward, List<List<AbstractNode>> nodeLayers, Dictionary<AbstractNode, int> nodeToLayer)
        {
            var nodeBarycenters = new List<(AbstractNode node, float barycenter)>();

            foreach (var node in nodeLayers[targetLayer])
            {
                var barycenter = CalculateBarycenter(node, fixedLayer, isForward, nodeLayers, nodeToLayer);
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

        private static List<List<AbstractNode>> ReduceCrossings(List<List<AbstractNode>> nodeLayers, Dictionary<AbstractNode, int> nodeToLayer, int maxIterations = 24)
        {
            var bestCrossings = int.MaxValue;
            List<List<AbstractNode>> bestConfiguration = null;

            for (var iteration = 0; iteration < maxIterations; iteration++)
            {
                // Alternate direction each iteration
                var forwardPass = (iteration % 2 == 0);

                if (forwardPass)
                {
                    // Left to right: fix layer i, optimize layer i+1
                    for (var layer = 0; layer < nodeLayers.Count - 1; layer++)
                    {
                        OrderByBarycenter(layer, layer + 1, isForward: true, nodeLayers, nodeToLayer);
                    }
                }
                else
                {
                    // Right to left: fix layer i, optimize layer i-1
                    for (var layer = nodeLayers.Count - 1; layer > 0; layer--)
                    {
                        OrderByBarycenter(layer, layer - 1, isForward: false, nodeLayers, nodeToLayer);
                    }
                }

                // Count crossings
                var currentCrossings = CountCrossings(nodeLayers, nodeToLayer);

                // Track best configuration
                if (currentCrossings < bestCrossings)
                {
                    bestCrossings = currentCrossings;
                    bestConfiguration = nodeLayers.Select(layer => layer.ToList()).ToList();
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

        private static List<float> CalculateIdealYPositions(List<AbstractNode> layer)
        {
            var idealPositions = new List<float>();

            foreach (var node in layer)
            {
                // Calculate ideal Y as median of INPUT connections only (from previous layer)
                // This positions nodes near their data sources, reducing wire length
                var inputYPositions = new List<float>();
                foreach (var socket in node.Sockets.OfType<SocketIn>())
                {
                    foreach (var wire in socket.Connections)
                    {
                        var sourceNode = wire.From.Owner;
                        inputYPositions.Add(sourceNode.Location.Y + sourceNode.BoundsFull.Height / 2);
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
                    foreach (var socket in node.Sockets.OfType<SocketOut>())
                    {
                        foreach (var wire in socket.Connections)
                        {
                            var targetNode = wire.To.Owner;
                            outputYPositions.Add(targetNode.Location.Y + targetNode.BoundsFull.Height / 2);
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
                            ? idealPositions[^1] + DefaultNodeSpacing
                            : 0f;
                    }
                }

                idealPositions.Add(idealY);
            }

            return idealPositions;
        }

        private static List<float> ResolveVerticalOverlaps(List<AbstractNode> layer, List<float> idealYPositions, float nodeSpacing)
        {
            var finalPositions = new List<float>(idealYPositions);

            // Forward pass: resolve overlaps by adjusting positions while maintaining order
            for (var i = 1; i < layer.Count; i++)
            {
                var prevNode = layer[i - 1];
                var minY = finalPositions[i - 1] + prevNode.BoundsFull.Height + nodeSpacing;

                if (finalPositions[i] < minY)
                {
                    finalPositions[i] = minY;
                }
            }

            // Backward pass to balance spacing
            for (var i = layer.Count - 2; i >= 0; i--)
            {
                var currNode = layer[i];
                var maxY = finalPositions[i + 1] - currNode.BoundsFull.Height - nodeSpacing;

                if (finalPositions[i] > maxY)
                {
                    finalPositions[i] = maxY;
                }
            }

            return finalPositions;
        }

        private void AssignCoordinates(List<List<AbstractNode>> nodeLayers, float layerSpacing = DefaultLayerSpacing, float nodeSpacing = DefaultNodeSpacing)
        {
            // Step 1: Calculate node bounds first
            foreach (var node in _graphNodes)
            {
                node.Calculate();
            }

            // Step 2: Assign X and Y coordinates for each layer
            var currentX = 0f;

            for (var layerIdx = 0; layerIdx < nodeLayers.Count; layerIdx++)
            {
                var layer = nodeLayers[layerIdx];

                if (layer.Count == 0)
                {
                    continue;
                }

                // Split layer into nodes with inputs and nodes without inputs
                var nodesWithInputs = new List<AbstractNode>();
                var nodesWithoutInputs = new List<AbstractNode>();

                foreach (var node in layer)
                {
                    var hasInputs = node.Sockets.OfType<SocketIn>().Any(s => s.Connections.Count > 0);
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
                    var idealYPositions = CalculateIdealYPositions(nodesWithInputs);
                    var finalYPositions = ResolveVerticalOverlaps(nodesWithInputs, idealYPositions, nodeSpacing);
                    var maxWidth = nodesWithInputs.Max(n => n.BoundsFull.Width);

                    for (var i = 0; i < nodesWithInputs.Count; i++)
                    {
                        var node = nodesWithInputs[i];
                        node.Location = new SKPoint(currentX, finalYPositions[i]);
                        node.Calculate();
                    }

                    currentX += maxWidth;
                }

                // Position nodes without inputs in a sub-column (between main column and next layer)
                if (nodesWithoutInputs.Count > 0)
                {
                    var subColumnX = currentX + (nodesWithInputs.Count > 0 ? layerSpacing / 2 : 0);
                    var idealYPositions = CalculateIdealYPositions(nodesWithoutInputs);
                    // Use larger spacing for nodes without inputs to reduce wire overlap
                    var finalYPositions = ResolveVerticalOverlaps(nodesWithoutInputs, idealYPositions, nodeSpacing * 3);
                    var maxWidth = nodesWithoutInputs.Max(n => n.BoundsFull.Width);

                    for (var i = 0; i < nodesWithoutInputs.Count; i++)
                    {
                        var node = nodesWithoutInputs[i];
                        node.Location = new SKPoint(subColumnX, finalYPositions[i]);
                        node.Calculate();
                    }

                    currentX = subColumnX + maxWidth;
                }

                // Move to next layer
                currentX += layerSpacing;
            }

            // Step 3: Refine positions for nodes without inputs now that outputs are positioned
            RefineNodesWithoutInputs(nodeLayers, nodeSpacing);
        }

        private static void RefineNodesWithoutInputs(List<List<AbstractNode>> nodeLayers, float nodeSpacing)
        {
            // Go through each layer and adjust nodes without inputs based on their output positions
            for (var layerIdx = 0; layerIdx < nodeLayers.Count - 1; layerIdx++)
            {
                var layer = nodeLayers[layerIdx];
                if (layer.Count == 0) continue;

                // Collect nodes without inputs from this layer
                var nodesWithoutInputs = layer
                    .Where(n => !n.Sockets.OfType<SocketIn>().Any(s => s.Connections.Count > 0))
                    .ToList();

                if (nodesWithoutInputs.Count == 0) continue;

                // Recalculate ideal Y positions based on outputs (which are now positioned)
                var adjustments = new List<(AbstractNode node, float idealY)>();

                foreach (var node in nodesWithoutInputs)
                {
                    var outputYPositions = new List<float>();
                    foreach (var socket in node.Sockets.OfType<SocketOut>())
                    {
                        foreach (var wire in socket.Connections)
                        {
                            var targetNode = wire.To.Owner;
                            outputYPositions.Add(targetNode.Location.Y + targetNode.BoundsFull.Height / 2);
                        }
                    }

                    if (outputYPositions.Count > 0)
                    {
                        var idealY = CalculateMedian(outputYPositions);
                        adjustments.Add((node, idealY));
                    }
                }

                if (adjustments.Count == 0) continue;

                // Sort by ideal Y to maintain relative order
                adjustments = adjustments.OrderBy(a => a.idealY).ToList();

                // Use minimal spacing to avoid overlaps while staying close to ideal positions
                var idealYs = adjustments.Select(a => a.idealY).ToList();
                var finalYs = ResolveVerticalOverlaps(
                    adjustments.Select(a => a.node).ToList(),
                    idealYs,
                    nodeSpacing  // Use normal spacing (50px) instead of 3x to minimize position drift
                );

                // Apply new positions
                for (var i = 0; i < adjustments.Count; i++)
                {
                    var node = adjustments[i].node;
                    node.Location = new SKPoint(node.Location.X, finalYs[i]);
                    node.Calculate();
                }
            }
        }
    }
}
