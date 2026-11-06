using ValveKeyValue;
using ValveResourceFormat.IO;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// The visibility clusters of the models a world node draws. A model with clusters is drawn when any of them is
/// visible, in place of the clusters its bounding box overlaps.
/// </summary>
internal sealed class WorldPartClusters
{
    private const long PrecomputedVismembers = 0x4000;

    // The count of a model is stored in one byte
    private const int MaxClustersPerPart = 255;

    private readonly Dictionary<string, int[]> parts = new(StringComparer.Ordinal);
    private readonly List<WorldAggregate> aggregates = [];

    /// <summary>Sets the clusters of a model.</summary>
    /// <param name="part">The model path.</param>
    /// <param name="clusters">The clusters, which are left out when there are none or more than a world node can hold.</param>
    public void Set(string part, IReadOnlyCollection<int> clusters)
    {
        if (clusters.Count == 0 || clusters.Count > MaxClustersPerPart)
        {
            return;
        }

        parts[part] = [.. clusters];
    }

    /// <summary>Creates the scene objects that place each model of the world at the origin.</summary>
    /// <param name="partPaths">The model paths.</param>
    /// <param name="membership">Receives the cluster lists the scene objects index into.</param>
    public KVObject CreateSceneObjects(List<string> partPaths, out KVObject membership)
    {
        var sceneObjects = KVObject.Array();
        membership = KVObject.Array();
        var offset = 0;

        foreach (var path in partPaths)
        {
            var sceneObject = KVObject.Collection();
            sceneObject.Add("m_renderableModel", Source1ResourceReferences.Reference(path));
            sceneObject.Add("m_vTransform", Source1WorldConverter.ToColumns(Matrix4x4.Identity));

            if (parts.TryGetValue(path, out var clusters))
            {
                // Count in the top byte and offset in the low 24 bits, stored rotated left by a byte
                var packed = ((uint)clusters.Length << 24) | (uint)offset;

                sceneObject.Add("m_nObjectTypeFlags", PrecomputedVismembers);
                sceneObject.Add("m_VisClusterMemberBits", (long)BitOperations.RotateLeft(packed, 8));

                foreach (var cluster in clusters)
                {
                    membership.Add((long)cluster);
                }

                offset += clusters.Length;
            }
            else
            {
                sceneObject.Add("m_nObjectTypeFlags", 0L);
            }

            sceneObject.Add("m_vTintColor", KVHelpers.ToKVArray(Vector4.One));
            sceneObjects.Add(sceneObject);
        }

        return sceneObjects;
    }

    /// <summary>Adds a model whose draw calls are the fragments of an aggregate scene object.</summary>
    /// <param name="path">The model path.</param>
    /// <param name="fragments">The fragments, one per draw call of the model in order.</param>
    public void AddAggregate(string path, List<AggregateFragment> fragments) => aggregates.Add(new WorldAggregate(path, fragments));

    /// <summary>Gets whether any aggregate was added.</summary>
    public bool HasAggregates => aggregates.Count > 0;

    /// <summary>Creates the aggregate scene objects, which draw each fragment of a model with its own bounds and clusters.</summary>
    /// <param name="membership">The cluster lists that the fragments index into, which the clusters of the fragments are appended to.</param>
    public KVObject CreateAggregateSceneObjects(KVObject membership)
    {
        var sceneObjects = KVObject.Array();

        foreach (var aggregate in aggregates)
        {
            var meshes = KVObject.Array();
            var allFlags = long.MaxValue;
            var anyFlags = 0L;

            for (var i = 0; i < aggregate.Fragments.Count; i++)
            {
                var fragment = aggregate.Fragments[i];
                var offset = membership.Count;
                var hasClusters = fragment.Clusters.Count > 0;
                var flags = hasClusters ? PrecomputedVismembers : 0L;

                foreach (var cluster in fragment.Clusters)
                {
                    membership.Add((long)cluster);
                }

                var mesh = KVObject.Collection();
                mesh.Add("m_nVisClusterMemberOffset", (long)offset);
                mesh.Add("m_nVisClusterMemberCount", (long)fragment.Clusters.Count);
                mesh.Add("m_bHasTransform", false);
                mesh.Add("m_nLODGroupMask", 0L);
                mesh.Add("m_nDrawCallIndex", (long)i);
                mesh.Add("m_nLODSetupIndex", -1L);
                mesh.Add("m_vTintColor", CreateWhiteTint());
                mesh.Add("m_objectFlags", flags);
                mesh.Add("m_nLightProbeVolumePrecomputedHandshake", 0L);
                mesh.Add("m_nInstanceStreamOffset", 0L);
                mesh.Add("m_nVertexAlbedoStreamOffset", 0L);
                mesh.Add("m_nVertexEmissiveStreamOffset", 0L);
                mesh.Add("m_instanceStreams", "AGGREGATE_INSTANCE_STREAM_NONE");
                mesh.Add("m_fEmissiveFactor", 0f);
                meshes.Add(mesh);

                allFlags &= flags;
                anyFlags |= flags;
            }

            var sceneObject = KVObject.Collection();
            sceneObject.Add("m_allFlags", allFlags);
            sceneObject.Add("m_anyFlags", anyFlags);
            sceneObject.Add("m_nLayer", 0L);
            sceneObject.Add("m_instanceStream", -1L);
            sceneObject.Add("m_vertexAlbedoStream", -1L);
            sceneObject.Add("m_vertexEmissiveStream", -1L);
            sceneObject.Add("m_aggregateMeshes", meshes);
            sceneObject.Add("m_lodSetups", KVObject.Array());
            sceneObject.Add("m_visClusterMembership", KVObject.Array());
            sceneObject.Add("m_fragmentTransforms", KVObject.Array());
            sceneObject.Add("m_renderableModel", Source1ResourceReferences.Reference(aggregate.Path));
            sceneObjects.Add(sceneObject);
        }

        return sceneObjects;
    }

    private static KVObject CreateWhiteTint()
    {
        var tint = KVObject.Array();
        tint.Add(255L);
        tint.Add(255L);
        tint.Add(255L);

        return tint;
    }
}

/// <summary>One draw call of an aggregate model.</summary>
/// <param name="Bounds">The bounds of the draw call, in the space of the world.</param>
/// <param name="Clusters">The visibility clusters that list the draw call, or none to cull it by its bounds.</param>
internal sealed record AggregateFragment(AABB Bounds, IReadOnlyCollection<int> Clusters);

internal sealed record WorldAggregate(string Path, List<AggregateFragment> Fragments);
