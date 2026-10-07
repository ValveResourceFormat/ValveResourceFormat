using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
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
        /// A vertex buffer bound in addition to the geometry of one draw call of one of <see cref="SceneObjects"/>,
        /// such as vertex paint on a placed prop.
        /// </summary>
        /// <param name="SceneObjectIndex">Index into <see cref="SceneObjects"/>.</param>
        /// <param name="SubSceneObject">Index of the mesh within the scene object's model.</param>
        /// <param name="DrawCallIndex">Index of the draw call within the mesh.</param>
        /// <param name="BufferIndex">Index into <see cref="GetExtraVertexStreams"/>.</param>
        public readonly record struct ExtraVertexStreamOverride(int SceneObjectIndex, int SubSceneObject, int DrawCallIndex, int BufferIndex);

        /// <summary>
        /// Gets the extra vertex streams bound to single draw calls of <see cref="SceneObjects"/>.
        /// </summary>
        public IEnumerable<ExtraVertexStreamOverride> ExtraVertexStreamOverrides
            => (Data.GetArray("m_extraVertexStreamOverrides") ?? []).Select(static streamOverride => new ExtraVertexStreamOverride(
                streamOverride.GetInt32Property("m_nSceneObjectIndex"),
                streamOverride.GetInt32Property("m_nSubSceneObject"),
                streamOverride.GetInt32Property("m_nDrawCallIndex"),
                streamOverride.GetSubCollection("m_extraBufferBinding").GetInt32Property("m_hBuffer")));

        /// <summary>
        /// Reads the vertex buffers that <see cref="ExtraVertexStreamOverrides"/> bind.
        /// </summary>
        public VBIB GetExtraVertexStreams()
        {
            var streams = new VBIB { Resource = Resource };
            streams.AddVertexBuffers(Data.GetArray("m_extraVertexStreams") ?? []);

            return streams;
        }

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
        /// <see langword="null"/> when its clusters come from its bounds instead. An object that opts out
        /// of vis culling ignores its precomputed clusters.
        /// </summary>
        /// <param name="sceneObject">An entry of <see cref="SceneObjects"/>.</param>
        public ushort[]? GetSceneObjectVisClusters(KVObject sceneObject)
        {
            ArgumentNullException.ThrowIfNull(sceneObject);

            var flags = sceneObject.GetEnumValue<ObjectTypeFlags>("m_nObjectTypeFlags", normalize: true);

            if (!UsesPrecomputedVisClusters(flags) || !sceneObject.ContainsKey("m_VisClusterMemberBits"))
            {
                return null;
            }

            // Count in the top byte and offset in the low 24 bits, stored rotated left by a byte
            var packed = BitOperations.RotateRight((uint)sceneObject.GetIntegerProperty("m_VisClusterMemberBits"), 8);

            return SliceVisClusterMembership((int)(packed & 0xFFFFFF), (int)(packed >> 24));
        }

        /// <summary>
        /// Gets the visibility clusters the compiler assigned to one fragment of an aggregate, or
        /// <see langword="null"/> when its clusters come from its bounds instead. The list may be empty,
        /// which leaves the fragment out of vis culling.
        /// </summary>
        /// <param name="aggregateMesh">An entry of an aggregate's <c>m_aggregateMeshes</c>.</param>
        public ushort[]? GetAggregateMeshVisClusters(KVObject aggregateMesh)
        {
            ArgumentNullException.ThrowIfNull(aggregateMesh);

            var flags = aggregateMesh.GetEnumValue<ObjectTypeFlags>("m_objectFlags", normalize: true);

            if (!UsesPrecomputedVisClusters(flags))
            {
                return null;
            }

            return SliceVisClusterMembership(aggregateMesh.GetInt32Property("m_nVisClusterMemberOffset"),
                aggregateMesh.GetInt32Property("m_nVisClusterMemberCount"));
        }

        private static bool UsesPrecomputedVisClusters(ObjectTypeFlags flags)
            => (flags & (ObjectTypeFlags.PrecomputedVismembers | ObjectTypeFlags.DisableVisCulling)) == ObjectTypeFlags.PrecomputedVismembers;

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
