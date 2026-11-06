using System.IO;
using ValveResourceFormat.Source1.Bsp;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// Converts the node tree and the potentially visible sets of a GoldSrc map to the world visibility of a Source 2 world.
/// </summary>
internal static class GoldSrcWorldVisibilityConverter
{
    /// <summary>Converts a map.</summary>
    /// <param name="bsp">The map.</param>
    /// <returns>The world visibility, or null when the map has no visibility data.</returns>
    public static Resource? Convert(GoldSrcBspFile bsp)
    {
        if (bsp.Models.Length == 0 || bsp.Visibility.Length == 0)
        {
            return null;
        }

        // Leaf 0 is the solid space outside the map, and the rows of the other leaves follow it
        var sourceLeaves = bsp.Leafs;
        var clusterCount = Math.Clamp(bsp.Models[0].VisibilityLeafs, 0, Math.Max(sourceLeaves.Length - 1, 0));
        var leaves = new Source1WorldVisibilityConverter.Leaf[sourceLeaves.Length];

        for (var i = 0; i < leaves.Length; i++)
        {
            var leaf = sourceLeaves[i];
            var cluster = i >= 1 && i <= clusterCount ? i - 1 : -1;

            leaves[i] = new Source1WorldVisibilityConverter.Leaf(
                cluster,
                new Vector3(leaf.MinX, leaf.MinY, leaf.MinZ),
                new Vector3(leaf.MaxX, leaf.MaxY, leaf.MaxZ));
        }

        var planes = bsp.Planes;
        var sourceNodes = bsp.Nodes;
        var nodes = new Source1WorldVisibilityConverter.Node[sourceNodes.Length];

        for (var i = 0; i < nodes.Length; i++)
        {
            var sourceNode = sourceNodes[i];

            if ((uint)sourceNode.PlaneNum >= (uint)planes.Length)
            {
                return null;
            }

            var plane = planes[sourceNode.PlaneNum];
            nodes[i] = new Source1WorldVisibilityConverter.Node(plane.Normal, plane.Dist, sourceNode.Front, sourceNode.Back);
        }

        var rowBytes = (clusterCount + 7) / 8;

        // A leaf without a row sees every leaf
        byte[]? GetRow(int cluster)
        {
            var offset = sourceLeaves[cluster + 1].VisibilityOffset;

            return offset < 0 ? null : BspVisibility.DecodeRow(bsp.Visibility, offset, rowBytes);
        }

        try
        {
            return Source1WorldVisibilityConverter.Create(nodes, bsp.Models[0].HeadNode0, leaves, clusterCount, GetRow);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
