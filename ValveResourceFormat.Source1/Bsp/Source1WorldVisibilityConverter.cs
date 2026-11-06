using System.IO;
using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Converts the node tree and the potentially visible sets of a BSP map to the world visibility of a Source 2 world.
/// </summary>
internal static class Source1WorldVisibilityConverter
{
    /// <summary>A split plane of the node tree. A negative child is a leaf, <c>~child</c>.</summary>
    internal readonly record struct Node(Vector3 Normal, float Distance, int Front, int Back);

    /// <summary>A leaf of the node tree, with the cluster it belongs to or a negative cluster for solid space.</summary>
    internal readonly record struct Leaf(int Cluster, Vector3 Mins, Vector3 Maxs);

    /// <summary>Converts a Source 1 map.</summary>
    /// <param name="bsp">The map.</param>
    /// <returns>The world visibility, or null when the map has no visibility data.</returns>
    public static Resource? Convert(BspFile bsp)
    {
        ArgumentNullException.ThrowIfNull(bsp);

        if (bsp.Models.Length == 0)
        {
            return null;
        }

        try
        {
            var visibility = bsp.Visibility;

            if (visibility.ClusterCount == 0)
            {
                return null;
            }

            var planes = bsp.Planes;
            var sourceNodes = BspWorldTrace.ReadNodes(bsp);
            var nodes = new Node[sourceNodes.Length];

            for (var i = 0; i < nodes.Length; i++)
            {
                var sourceNode = sourceNodes[i];

                if ((uint)sourceNode.Plane >= (uint)planes.Length)
                {
                    return null;
                }

                var plane = planes[sourceNode.Plane];
                nodes[i] = new Node(plane.Normal, plane.Dist, sourceNode.Child0, sourceNode.Child1);
            }

            var sourceLeaves = bsp.Leafs;
            var leaves = new Leaf[sourceLeaves.Length];

            for (var i = 0; i < leaves.Length; i++)
            {
                var leaf = sourceLeaves[i];
                leaves[i] = new Leaf(leaf.Cluster, leaf.Mins, leaf.Maxs);
            }

            return Create(nodes, bsp.Models[0].HeadNode, leaves, visibility.ClusterCount, visibility.DecodePvs);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Builds the world visibility resource from the world tree.</summary>
    /// <param name="nodes">The nodes of the map.</param>
    /// <param name="rootNode">The index of the node the world tree starts at. Nodes that hang off it are the only ones kept.</param>
    /// <param name="leaves">The leaves of the map.</param>
    /// <param name="clusterCount">The number of clusters, which is the number of bits of a row.</param>
    /// <param name="getRow">Gets the decoded row of the clusters a cluster can see, or null when it can see every cluster.</param>
    /// <returns>The world visibility, or null when the tree is not a tree.</returns>
    internal static Resource? Create(Node[] nodes, int rootNode, Leaf[] leaves, int clusterCount, Func<int, byte[]?> getRow)
    {
        if (clusterCount <= 0 || (uint)rootNode >= (uint)nodes.Length)
        {
            return null;
        }

        var newIndices = new int[nodes.Length];
        Array.Fill(newIndices, -1);

        var order = new List<int> { rootNode };
        newIndices[rootNode] = 0;

        for (var i = 0; i < order.Count; i++)
        {
            var node = nodes[order[i]];

            if (!TryAddChild(node.Front, nodes.Length, newIndices, order) || !TryAddChild(node.Back, nodes.Length, newIndices, order))
            {
                return null;
            }
        }

        // A leaf names its cluster by the index past the end of the nodes, and solid space names none
        var nodeCount = order.Count;
        var solid = nodeCount + clusterCount;

        int EncodeChild(int child)
        {
            if (child >= 0)
            {
                return newIndices[child];
            }

            var leafIndex = ~child;
            var cluster = leafIndex < leaves.Length ? leaves[leafIndex].Cluster : -1;

            return cluster >= 0 && cluster < clusterCount ? nodeCount + cluster : solid;
        }

        var nodeArray = KVObject.Array();

        foreach (var index in order)
        {
            var node = nodes[index];
            var data = KVObject.Collection();
            data.Add("m_vSplitNormal", KVHelpers.ToKVArray(node.Normal));
            data.Add("m_flDist", node.Distance);
            data.Add("m_nChildren", KVObject.PackedArray([EncodeChild(node.Front), EncodeChild(node.Back)]));
            nodeArray.Add(data);
        }

        var clusterBounds = GetClusterBounds(leaves, clusterCount);
        var bytesPerCluster = (clusterCount + 7) / 8;
        var rows = new byte[(((clusterCount * bytesPerCluster) + 3) / 4) * 4];
        var clusterArray = KVObject.Array();

        for (var cluster = 0; cluster < clusterCount; cluster++)
        {
            var offset = cluster * bytesPerCluster;
            var row = getRow(cluster);

            if (row == null)
            {
                rows.AsSpan(offset, bytesPerCluster).Fill(0xFF);
            }
            else
            {
                row.AsSpan(0, Math.Min(row.Length, bytesPerCluster)).CopyTo(rows.AsSpan(offset));
            }

            var data = KVObject.Collection();
            data.Add("m_vMins", KVHelpers.ToKVArray(clusterBounds[cluster].Min));
            data.Add("m_vMaxs", KVHelpers.ToKVArray(clusterBounds[cluster].Max));
            data.Add("m_nData", (long)offset);
            data.Add("m_flNearVisibleDistance", 0f);
            data.Add("m_flFarVisibleDistance", 0f);
            clusterArray.Add(data);
        }

        var root = KVObject.Collection();
        root.Add("m_nodes", nodeArray);
        root.Add("m_clusters", clusterArray);
        root.Add("m_visDataDwords", KVObject.PackedArray(MemoryMarshal.Cast<byte, uint>(rows).ToArray()));

        var resource = new Resource(ResourceType.WorldVisibility);
        resource.AddBlock(new BinaryKV3(root, KV3IDLookup.Get("generic"), BlockType.DATA) { Resource = resource });

        return resource;
    }

    private static bool TryAddChild(int child, int nodeCount, int[] newIndices, List<int> order)
    {
        if (child < 0)
        {
            return true;
        }

        if (child >= nodeCount || newIndices[child] >= 0)
        {
            return false;
        }

        newIndices[child] = order.Count;
        order.Add(child);

        return true;
    }

    // A cluster is the box around its leaves
    private static (Vector3 Min, Vector3 Max)[] GetClusterBounds(Leaf[] leaves, int clusterCount)
    {
        var bounds = new (Vector3 Min, Vector3 Max)[clusterCount];
        var hasLeaves = new bool[clusterCount];

        foreach (var leaf in leaves)
        {
            if (leaf.Cluster < 0 || leaf.Cluster >= clusterCount)
            {
                continue;
            }

            var current = bounds[leaf.Cluster];

            if (hasLeaves[leaf.Cluster])
            {
                bounds[leaf.Cluster] = (Vector3.Min(current.Min, leaf.Mins), Vector3.Max(current.Max, leaf.Maxs));
            }
            else
            {
                bounds[leaf.Cluster] = (leaf.Mins, leaf.Maxs);
                hasLeaves[leaf.Cluster] = true;
            }
        }

        return bounds;
    }
}
