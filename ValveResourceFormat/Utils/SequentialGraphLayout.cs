using System.Linq;

namespace ValveResourceFormat.Utils;

/// <summary>
/// Component-based layout algorithm that lays out sequential chains compactly.
/// </summary>
public static class SequentialGraphLayout
{
    /// <summary>
    /// Layouts nodes by grouping connected components and laying sequential chains in a single row.
    /// Disconnected single nodes are placed in a right-side column.
    /// </summary>
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
        GraphLayout.LayoutOptions? options = null)
        where TNode : class
    {
        var nodeList = nodes.ToList();
        var connectionList = connections.ToList();

        if (nodeList.Count == 0)
        {
            return;
        }

        options ??= new GraphLayout.LayoutOptions();

        var components = GetConnectedComponents(nodeList, connectionList, getSourceNode, getTargetNode);
        var connectedComponents = components.Where(component => component.Count > 1).ToList();
        var isolatedNodes = components.Where(component => component.Count == 1).Select(component => component[0]).ToList();
        var currentY = 0f;
        var maxRightEdge = 0f;

        foreach (var component in connectedComponents)
        {
            var componentSet = component.ToHashSet();
            if (TryGetSequentialOrder(component, componentSet, getInputConnections, getOutputConnections, getSourceNode, getTargetNode, out var orderedNodes))
            {
                const float sequentialSpacing = 10f;
                var componentHeight = LayoutSequentialComponent(orderedNodes, getPosition, setPosition, getSize, sequentialSpacing, currentY);
                var sequentialBounds = GetComponentBounds(component, getPosition, getSize);
                maxRightEdge = Math.Max(maxRightEdge, sequentialBounds.MaxX);
                currentY += componentHeight + options.NodeSpacing * 2;
                continue;
            }

            var componentConnections = connectionList
                .Where(connection =>
                {
                    var source = getSourceNode(connection);
                    var target = getTargetNode(connection);
                    return source != null && target != null && componentSet.Contains(source) && componentSet.Contains(target);
                })
                .ToList();

            Func<TNode, IEnumerable<TConnection>> inputConnections = node => FilterInputConnections(node, componentSet, getInputConnections, getSourceNode);
            Func<TNode, IEnumerable<TConnection>> outputConnections = node => FilterOutputConnections(node, componentSet, getOutputConnections, getTargetNode);

            var (nodeLayers, nodeToLayer) = GraphLayout.AssignLayers(component, componentConnections, getSourceNode, getTargetNode);

            nodeLayers = GraphLayout.ReduceCrossings(
                nodeLayers, nodeToLayer,
                getSourceNode, getTargetNode,
                inputConnections, outputConnections,
                options.MaxCrossingReductionIterations);

            GraphLayout.AssignCoordinates(
                nodeLayers,
                getPosition, setPosition, getSize,
                getSourceNode, getTargetNode,
                inputConnections, outputConnections,
                options.LayerSpacing, options.NodeSpacing);

            var layeredBounds = GetComponentBounds(component, getPosition, getSize);
            OffsetComponent(component, getPosition, setPosition, -layeredBounds.MinX, currentY - layeredBounds.MinY);
            maxRightEdge = Math.Max(maxRightEdge, layeredBounds.MaxX - layeredBounds.MinX);
            currentY += layeredBounds.Height + options.NodeSpacing * 2;
        }

        if (isolatedNodes.Count == 0)
        {
            return;
        }

        var rightColumnX = maxRightEdge + options.LayerSpacing;
        var isolatedY = 0f;
        foreach (var node in isolatedNodes)
        {
            setPosition(node, new Vector2(rightColumnX, isolatedY));
            var size = getSize(node);
            isolatedY += size.Y + options.NodeSpacing;
        }
    }

    private static List<List<TNode>> GetConnectedComponents<TNode, TConnection>(
        List<TNode> nodes,
        List<TConnection> connections,
        Func<TConnection, TNode> getSourceNode,
        Func<TConnection, TNode> getTargetNode)
        where TNode : class
    {
        var adjacency = nodes.ToDictionary(node => node, _ => new List<TNode>());

        foreach (var connection in connections)
        {
            var source = getSourceNode(connection);
            var target = getTargetNode(connection);
            if (source == null || target == null)
            {
                continue;
            }

            if (adjacency.TryGetValue(source, out var sourceNeighbors))
            {
                sourceNeighbors.Add(target);
            }

            if (adjacency.TryGetValue(target, out var targetNeighbors))
            {
                targetNeighbors.Add(source);
            }
        }

        var visited = new HashSet<TNode>();
        var components = new List<List<TNode>>();

        foreach (var node in nodes)
        {
            if (!visited.Add(node))
            {
                continue;
            }

            var component = new List<TNode>();
            var queue = new Queue<TNode>();
            queue.Enqueue(node);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                if (!adjacency.TryGetValue(current, out var neighbors))
                {
                    continue;
                }

                foreach (var neighbor in neighbors)
                {
                    if (visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    private static bool TryGetSequentialOrder<TNode, TConnection>(
        List<TNode> component,
        HashSet<TNode> componentSet,
        Func<TNode, IEnumerable<TConnection>> getInputConnections,
        Func<TNode, IEnumerable<TConnection>> getOutputConnections,
        Func<TConnection, TNode> getSourceNode,
        Func<TConnection, TNode> getTargetNode,
        out List<TNode> orderedNodes)
        where TNode : class
    {
        orderedNodes = [];

        var inCounts = new Dictionary<TNode, int>();
        var outCounts = new Dictionary<TNode, int>();

        foreach (var node in component)
        {
            var inputCount = getInputConnections(node)
                .Count(connection =>
                {
                    var source = getSourceNode(connection);
                    return source != null && componentSet.Contains(source);
                });
            var outputCount = getOutputConnections(node)
                .Count(connection =>
                {
                    var target = getTargetNode(connection);
                    return target != null && componentSet.Contains(target);
                });

            if (inputCount > 1 || outputCount > 1)
            {
                return false;
            }

            inCounts[node] = inputCount;
            outCounts[node] = outputCount;
        }

        var startNodes = component.Where(node => inCounts[node] == 0).ToList();
        if (startNodes.Count != 1)
        {
            return false;
        }

        var visited = new HashSet<TNode>();
        var current = startNodes[0];

        while (true)
        {
            if (!visited.Add(current))
            {
                return false;
            }

            orderedNodes.Add(current);

            var nextConnection = getOutputConnections(current)
                .FirstOrDefault(connection =>
                {
                    var target = getTargetNode(connection);
                    return target != null && componentSet.Contains(target);
                });

            if (nextConnection == null)
            {
                break;
            }

            var nextNode = getTargetNode(nextConnection);
            if (nextNode == null)
            {
                break;
            }

            current = nextNode;
        }

        return orderedNodes.Count == component.Count;
    }

    private static IEnumerable<TConnection> FilterInputConnections<TNode, TConnection>(
        TNode node,
        HashSet<TNode> componentSet,
        Func<TNode, IEnumerable<TConnection>> getInputConnections,
        Func<TConnection, TNode> getSourceNode)
        where TNode : class
    {
        return getInputConnections(node)
            .Where(connection =>
            {
                var source = getSourceNode(connection);
                return source != null && componentSet.Contains(source);
            });
    }

    private static IEnumerable<TConnection> FilterOutputConnections<TNode, TConnection>(
        TNode node,
        HashSet<TNode> componentSet,
        Func<TNode, IEnumerable<TConnection>> getOutputConnections,
        Func<TConnection, TNode> getTargetNode)
        where TNode : class
    {
        return getOutputConnections(node)
            .Where(connection =>
            {
                var target = getTargetNode(connection);
                return target != null && componentSet.Contains(target);
            });
    }

    private static float LayoutSequentialComponent<TNode>(
        List<TNode> orderedNodes,
        Func<TNode, Vector2> getPosition,
        Action<TNode, Vector2> setPosition,
        Func<TNode, Vector2> getSize,
        float nodeSpacing,
        float startY)
        where TNode : class
    {
        var currentX = 0f;
        var maxHeight = 0f;

        foreach (var node in orderedNodes)
        {
            setPosition(node, new Vector2(currentX, startY));
            var size = getSize(node);
            currentX += size.X + nodeSpacing;
            maxHeight = Math.Max(maxHeight, size.Y);
        }

        return maxHeight;
    }

    private static (float MinX, float MinY, float MaxX, float MaxY, float Height) GetComponentBounds<TNode>(
        List<TNode> nodes,
        Func<TNode, Vector2> getPosition,
        Func<TNode, Vector2> getSize)
        where TNode : class
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        foreach (var node in nodes)
        {
            var position = getPosition(node);
            var size = getSize(node);
            minX = Math.Min(minX, position.X);
            minY = Math.Min(minY, position.Y);
            maxX = Math.Max(maxX, position.X + size.X);
            maxY = Math.Max(maxY, position.Y + size.Y);
        }

        var height = maxY - minY;
        return (minX, minY, maxX, maxY, height);
    }

    private static void OffsetComponent<TNode>(
        List<TNode> nodes,
        Func<TNode, Vector2> getPosition,
        Action<TNode, Vector2> setPosition,
        float offsetX,
        float offsetY)
        where TNode : class
    {
        foreach (var node in nodes)
        {
            var position = getPosition(node);
            setPosition(node, new Vector2(position.X + offsetX, position.Y + offsetY));
        }
    }
}
