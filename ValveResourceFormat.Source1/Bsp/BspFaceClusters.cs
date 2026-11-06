namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// The visibility clusters each face of a map is listed in.
/// </summary>
internal sealed class BspFaceClusters
{
    private readonly List<int>?[] clusters;

    public BspFaceClusters(BspFile bsp)
    {
        clusters = new List<int>?[bsp.Faces.Length];

        var leafFaces = bsp.LeafFaces;

        foreach (var leaf in bsp.Leafs)
        {
            if (leaf.Cluster < 0)
            {
                continue;
            }

            var end = Math.Min(leaf.FirstLeafFace + leaf.NumLeafFaces, leafFaces.Length);

            for (var i = leaf.FirstLeafFace; i < end; i++)
            {
                var face = leafFaces[i];

                if (face >= clusters.Length)
                {
                    continue;
                }

                var faceClusters = clusters[face] ??= [];

                if (!faceClusters.Contains(leaf.Cluster))
                {
                    faceClusters.Add(leaf.Cluster);
                }
            }
        }
    }

    /// <summary>Gets the lowest cluster a face is listed in, or -1 when no leaf with a cluster lists it.</summary>
    public int GetPrimaryCluster(int face)
    {
        var faceClusters = Get(face);

        var lowest = -1;

        foreach (var cluster in faceClusters)
        {
            if (lowest < 0 || cluster < lowest)
            {
                lowest = cluster;
            }
        }

        return lowest;
    }

    /// <summary>Gets every cluster a face is listed in.</summary>
    public IReadOnlyList<int> Get(int face)
    {
        if ((uint)face >= (uint)clusters.Length)
        {
            return [];
        }

        return clusters[face] ?? [];
    }
}
