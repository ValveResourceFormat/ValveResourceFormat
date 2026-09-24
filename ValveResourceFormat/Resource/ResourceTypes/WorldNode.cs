using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a world node resource.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/worldrenderer/WorldNode_t">WorldNode_t</seealso>
    public class WorldNode : KeyValuesOrNTRO
    {
        /// <summary>
        /// Gets the scene objects.
        /// </summary>
        public IReadOnlyList<KVObject> SceneObjects
            => Data.GetArray("m_sceneObjects");

        /// <summary>
        /// Layer indices for <see cref="SceneObjects"/>.
        /// For <see cref="AggregateSceneObjects"/> use the dedicated 'm_nLayer' member.
        /// Value may be null if the node has no layer system.
        /// </summary>
        public IReadOnlyList<long>? SceneObjectLayerIndices
            => Data.ContainsKey("m_sceneObjectLayerIndices")
                ? Data.GetIntegerArray("m_sceneObjectLayerIndices")
                : null;

        /// <summary>
        /// Gets the aggregate scene objects.
        /// </summary>
        public IReadOnlyList<KVObject> AggregateSceneObjects
            => Data.ContainsKey("m_aggregateSceneObjects")
                ? Data.GetArray("m_aggregateSceneObjects")
                : [];

        /// <summary>
        /// Gets the clutter scene objects.
        /// </summary>
        public IReadOnlyList<KVObject> ClutterSceneObjects
            => Data.ContainsKey("m_clutterSceneObjects")
                ? Data.GetArray("m_clutterSceneObjects")
                : [];

        /// <summary>
        /// Gets the visibility cluster ids that scene objects and aggregate fragments with precomputed
        /// cluster membership index into.
        /// </summary>
        public IReadOnlyList<long> VisClusterMembership
            => visClusterMembership ??= Data.ContainsKey("m_visClusterMembership")
                ? Data.GetIntegerArray("m_visClusterMembership")
                : [];

        private long[]? visClusterMembership;

        /// <summary>
        /// Gets the visibility clusters the compiler assigned to one of <see cref="SceneObjects"/>, or
        /// <see langword="null"/> when its clusters come from its bounds instead.
        /// </summary>
        /// <param name="sceneObject">An entry of <see cref="SceneObjects"/>.</param>
        public ushort[]? GetSceneObjectVisClusters(KVObject sceneObject)
        {
            ArgumentNullException.ThrowIfNull(sceneObject);

            var flags = sceneObject.GetEnumValue<ObjectTypeFlags>("m_nObjectTypeFlags", normalize: true);

            if ((flags & ObjectTypeFlags.PrecomputedVismembers) == 0 || !sceneObject.ContainsKey("m_VisClusterMemberBits"))
            {
                return null;
            }

            // Count in the top byte and offset in the low 24 bits, stored rotated left by a byte
            var packed = BitOperations.RotateRight((uint)sceneObject.GetIntegerProperty("m_VisClusterMemberBits"), 8);

            return SliceVisClusterMembership((int)(packed & 0xFFFFFF), (int)(packed >> 24));
        }

        /// <summary>
        /// Gets the visibility clusters the compiler assigned to one fragment of an aggregate, or
        /// <see langword="null"/> when its clusters come from its bounds instead.
        /// </summary>
        /// <param name="aggregateMesh">An entry of an aggregate's <c>m_aggregateMeshes</c>.</param>
        public ushort[]? GetAggregateMeshVisClusters(KVObject aggregateMesh)
        {
            ArgumentNullException.ThrowIfNull(aggregateMesh);

            var flags = aggregateMesh.GetEnumValue<ObjectTypeFlags>("m_objectFlags", normalize: true);
            var count = aggregateMesh.GetInt32Property("m_nVisClusterMemberCount");

            if ((flags & ObjectTypeFlags.PrecomputedVismembers) == 0 || count == 0)
            {
                return null;
            }

            return SliceVisClusterMembership(aggregateMesh.GetInt32Property("m_nVisClusterMemberOffset"), count);
        }

        private ushort[] SliceVisClusterMembership(int offset, int count)
        {
            var membership = VisClusterMembership;
            var clusters = new ushort[offset < 0 ? 0 : Math.Clamp(membership.Count - offset, 0, count)];

            for (var i = 0; i < clusters.Length; i++)
            {
                clusters[i] = (ushort)membership[offset + i];
            }

            return clusters;
        }

        /// <summary>
        /// Gets the layer names.
        /// </summary>
        public IReadOnlyList<string> LayerNames
            => Data.ContainsKey("m_layerNames")
                ? Data.GetArray<string>("m_layerNames")
                : [];
    }
}
