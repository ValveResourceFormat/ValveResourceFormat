namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Finds the leaf of the world model that contains a point, and the ambient light the map baked into the leaf.
/// </summary>
internal sealed class BspLeafAmbient
{
    private const float PlaneTolerance = 0.5f;

    private sealed record LeafSamples(Vector3[] Positions, Vector3[] Cubes);

    private readonly BspFile bsp;
    private readonly BspWorldTrace.Node[] nodes;
    private readonly LeafAmbientIndex[] indices;
    private readonly LeafAmbientLighting[] samples;
    private readonly LeafSamples?[] leafSamples;
    private bool[]? nodeHasAmbient;

    /// <summary>The number of faces of an ambient cube.</summary>
    public const int FaceCount = 6;

    /// <summary>Reads the nodes and the ambient samples of a map, from the HDR lumps when the map has HDR lighting.</summary>
    /// <param name="bsp">The map.</param>
    public BspLeafAmbient(BspFile bsp)
    {
        ArgumentNullException.ThrowIfNull(bsp);

        this.bsp = bsp;

        nodes = BspWorldTrace.ReadNodes(bsp);
        indices = bsp.HasHdrLighting && bsp.LeafAmbientIndicesHdr.Length > 0 ? bsp.LeafAmbientIndicesHdr : bsp.LeafAmbientIndices;
        samples = bsp.HasHdrLighting && bsp.LeafAmbientLightingHdr.Length > 0 ? bsp.LeafAmbientLightingHdr : bsp.LeafAmbientLighting;
        leafSamples = new LeafSamples?[bsp.Leafs.Length];
    }

    /// <summary>Gets the leaf of the world model that contains a point.</summary>
    /// <param name="point">The point in the world.</param>
    /// <returns>The leaf index, or -1 for a map without nodes.</returns>
    public int FindLeaf(Vector3 point)
    {
        if (nodes.Length == 0 || bsp.Models.Length == 0)
        {
            return -1;
        }

        var node = bsp.Models[0].HeadNode;

        while (node >= 0)
        {
            var current = nodes[node];
            var plane = bsp.Planes[current.Plane];
            var distance = Vector3.Dot(plane.Normal, point) - plane.Dist;

            node = distance >= 0f ? current.Child0 : current.Child1;
        }

        return -1 - node;
    }

    /// <summary>Gets whether a box can contain a point in a leaf with ambient samples.</summary>
    /// <param name="min">The minimum corner of the box.</param>
    /// <param name="max">The maximum corner of the box.</param>
    /// <returns>False when no point of the box is in such a leaf, true when one may be.</returns>
    public bool MayHaveAmbient(Vector3 min, Vector3 max)
    {
        if (nodes.Length == 0 || bsp.Models.Length == 0)
        {
            return true;
        }

        nodeHasAmbient ??= FindNodesWithAmbient();

        return MayHaveAmbient(bsp.Models[0].HeadNode, min, max);
    }

    private bool MayHaveAmbient(int child, Vector3 min, Vector3 max)
    {
        if (child < 0)
        {
            return HasAmbient(-1 - child);
        }

        if (!nodeHasAmbient![child])
        {
            return false;
        }

        var current = nodes[child];
        var plane = bsp.Planes[current.Plane];
        var low = Vector3.Min(plane.Normal * min, plane.Normal * max);
        var high = Vector3.Max(plane.Normal * min, plane.Normal * max);
        var lowestDistance = low.X + low.Y + low.Z - plane.Dist;
        var highestDistance = high.X + high.Y + high.Z - plane.Dist;

        if (lowestDistance > PlaneTolerance)
        {
            return MayHaveAmbient(current.Child0, min, max);
        }

        if (highestDistance < -PlaneTolerance)
        {
            return MayHaveAmbient(current.Child1, min, max);
        }

        return MayHaveAmbient(current.Child0, min, max) || MayHaveAmbient(current.Child1, min, max);
    }

    private bool[] FindNodesWithAmbient()
    {
        var result = new bool[nodes.Length];

        if (bsp.Models.Length > 0)
        {
            MarkNodesWithAmbient(bsp.Models[0].HeadNode, result);
        }

        return result;
    }

    private bool MarkNodesWithAmbient(int child, bool[] result)
    {
        if (child < 0)
        {
            return HasAmbient(-1 - child);
        }

        var current = nodes[child];
        var front = MarkNodesWithAmbient(current.Child0, result);
        var back = MarkNodesWithAmbient(current.Child1, result);

        result[child] = front || back;

        return result[child];
    }

    /// <summary>
    /// Blends the ambient samples of a leaf by inverse squared distance to a point. A leaf without samples of its own
    /// can name another leaf to take them from.
    /// </summary>
    /// <param name="leafIndex">The leaf the point is in.</param>
    /// <param name="point">The point in the world.</param>
    /// <param name="cube">Receives the linear color of each face in the order +X, -X, +Y, -Y, +Z, -Z. It must hold <see cref="FaceCount"/> faces.</param>
    /// <returns>False when the leaf has no samples.</returns>
    public bool TryBlendAmbientCube(int leafIndex, Vector3 point, Span<Vector3> cube)
    {
        if (!TryGetSampleLeaf(leafIndex, out var sampleLeaf, out var index))
        {
            return false;
        }

        var leafSamples = GetLeafSamples(sampleLeaf, index);
        var totalWeight = 0f;

        cube[..FaceCount].Clear();

        for (var i = 0; i < leafSamples.Positions.Length; i++)
        {
            var weight = 1f / (Vector3.DistanceSquared(leafSamples.Positions[i], point) + 1f);
            var faces = leafSamples.Cubes.AsSpan(i * FaceCount, FaceCount);

            totalWeight += weight;

            for (var face = 0; face < FaceCount; face++)
            {
                cube[face] += faces[face] * weight;
            }
        }

        if (totalWeight == 0f)
        {
            return false;
        }

        for (var face = 0; face < FaceCount; face++)
        {
            cube[face] /= totalWeight;
        }

        return true;
    }

    // The positions and linear colors of the samples of a leaf, which are the same for every point blended in the leaf
    private LeafSamples GetLeafSamples(int leaf, LeafAmbientIndex index)
    {
        if (leafSamples[leaf] is { } cached)
        {
            return cached;
        }

        var bounds = bsp.Leafs[leaf];
        var count = Math.Clamp(samples.Length - index.FirstAmbientSample, 0, index.AmbientSampleCount);
        var positions = new Vector3[count];
        var cubes = new Vector3[count * FaceCount];

        for (var i = 0; i < count; i++)
        {
            var sample = samples[index.FirstAmbientSample + i];
            var fraction = new Vector3(sample.X, sample.Y, sample.Z) / 255f;

            positions[i] = bounds.Mins + (fraction * (bounds.Maxs - bounds.Mins));

            for (var face = 0; face < FaceCount; face++)
            {
                cubes[(i * FaceCount) + face] = sample.Cube[face].ToAmbientLinear();
            }
        }

        var result = new LeafSamples(positions, cubes);
        leafSamples[leaf] = result;

        return result;
    }

    /// <summary>Gets whether a leaf can be blended, which solid leaves and leaves without samples cannot.</summary>
    /// <param name="leafIndex">The leaf.</param>
    public bool HasAmbient(int leafIndex)
    {
        if ((uint)leafIndex >= (uint)bsp.Leafs.Length || bsp.Leafs[leafIndex].Contents == 1)
        {
            return false;
        }

        return TryGetSampleLeaf(leafIndex, out _, out _);
    }

    private bool TryGetSampleLeaf(int leafIndex, out int sampleLeaf, out LeafAmbientIndex index)
    {
        sampleLeaf = leafIndex;
        index = default;

        if ((uint)leafIndex >= (uint)indices.Length || leafIndex >= bsp.Leafs.Length)
        {
            return false;
        }

        index = indices[leafIndex];

        if (index.AmbientSampleCount == 0 && index.FirstAmbientSample != 0)
        {
            sampleLeaf = index.FirstAmbientSample;

            if ((uint)sampleLeaf >= (uint)indices.Length || sampleLeaf >= bsp.Leafs.Length)
            {
                return false;
            }

            index = indices[sampleLeaf];
        }

        return index.AmbientSampleCount != 0;
    }
}
