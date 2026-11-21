using System.Linq;

namespace ValveResourceFormat.Graphs;

/// <summary>Union-find over the integers below a fixed count.</summary>
internal sealed class DisjointSets
{
    private readonly int[] root;

    public DisjointSets(int count)
    {
        root = [.. Enumerable.Range(0, count)];
    }

    /// <summary>The representative of the set holding <paramref name="item"/>.</summary>
    public int Find(int item)
    {
        while (root[item] != item)
        {
            root[item] = root[root[item]];
            item = root[item];
        }

        return item;
    }

    /// <summary>Joins the sets of two items, returning false when they already were one.</summary>
    public bool Union(int a, int b)
    {
        var ra = Find(a);
        var rb = Find(b);

        if (ra == rb)
        {
            return false;
        }

        root[ra] = rb;
        return true;
    }
}
