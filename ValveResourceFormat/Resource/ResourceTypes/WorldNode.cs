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
        /// One entry of <see cref="ClutterSceneObjects"/>: a model drawn at many compiled instance
        /// placements, such as detail props.
        /// </summary>
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/worldrenderer/ClutterSceneObject_t">ClutterSceneObject_t</seealso>
        public class ClutterSceneObject
        {
            /// <summary>Gets the model every instance draws.</summary>
            public string RenderableModel { get; }

            /// <summary>Gets the material group (skin) of the model, empty for the default.</summary>
            public string MaterialGroup { get; }

            /// <summary>Gets the index into <see cref="LayerNames"/>.</summary>
            public int Layer { get; }

            /// <summary>Gets the object type flags shared by all instances.</summary>
            public ObjectTypeFlags Flags { get; }

            /// <summary>
            /// Gets the screen size fraction below which instances start to fade out, reaching fully faded at
            /// <see cref="EndCullSize"/>. Which instances are dropped depends on <see cref="EndCullSize"/> alone.
            /// </summary>
            public float BeginCullSize { get; }

            /// <summary>Gets the screen size fraction below which instances are no longer drawn.</summary>
            public float EndCullSize { get; }

            /// <summary>Gets the world space position of each instance.</summary>
            public Vector3[] InstancePositions { get; }

            /// <summary>Gets the uniform scale of each instance.</summary>
            public float[] InstanceScales { get; }

            /// <summary>Gets the world space rotation of each instance.</summary>
            public Quaternion[] InstanceOrientations { get; }

            /// <summary>Gets the sRGB tint of each instance, in the 0-255 range.</summary>
            public Vector3[] InstanceTints { get; }

            /// <summary>Gets the tiles that split the instances into spatially grouped ranges.</summary>
            public Tile[] Tiles { get; }

            /// <summary>A contiguous range of spatially grouped instances.</summary>
            /// <param name="FirstInstance">Index of the first instance in the tile.</param>
            /// <param name="EndInstance">Index one past the last instance in the tile.</param>
            public readonly record struct Tile(int FirstInstance, int EndInstance);

            /// <summary>
            /// Reads a clutter scene object from its keyvalues.
            /// </summary>
            /// <param name="data">An entry of <see cref="ClutterSceneObjects"/>.</param>
            public ClutterSceneObject(KVObject data)
            {
                ArgumentNullException.ThrowIfNull(data);

                RenderableModel = data.GetStringProperty("m_renderableModel");
                MaterialGroup = data.GetStringProperty("m_materialGroup");
                Layer = data.GetInt32Property("m_nLayer");
                Flags = data.GetEnumValue<ObjectTypeFlags>("m_flags", normalize: true);
                BeginCullSize = data.GetFloatProperty("m_flBeginCullSize");
                EndCullSize = data.GetFloatProperty("m_flEndCullSize");

                InstancePositions = [.. data.GetArray("m_instancePositions").Select(static position => position.ToVector3())];
                InstanceScales = data.GetFloatArray("m_instanceScales");
                InstanceOrientations = [.. data.GetIntegerArray("m_InstanceOrientations32").Select(static packed => UnpackQuaternion32(unchecked((uint)packed)))];
                InstanceTints = [.. data.GetArray("m_instanceTintSrgb").Select(static tint => tint.ToVector3())];
                Tiles = [.. data.GetArray("m_tiles").Select(static tile => new Tile(tile.GetInt32Property("m_nFirstInstance"), tile.GetInt32Property("m_nLastInstance")))];
            }

            /// <summary>
            /// Gets the world transform of one instance: its uniform scale, then rotation, then position.
            /// </summary>
            /// <param name="index">The instance index.</param>
            public Matrix4x4 GetInstanceTransform(int index)
                => Matrix4x4.CreateScale(InstanceScales[index])
                    * Matrix4x4.CreateFromQuaternion(InstanceOrientations[index])
                    * Matrix4x4.CreateTranslation(InstancePositions[index]);

            // x in bits 0-10, y in bits 11-20 and z in bits 21-30, each mapped from [0, 1] to [-1, 1],
            // with w rebuilt from unit length and negated when bit 31 is set. The result is not normalized.
            private static Quaternion UnpackQuaternion32(uint packed)
            {
                var x = (packed & 0x7FF) * (2f / 2047f) - 1f;
                var y = ((packed >> 11) & 0x3FF) * (2f / 1023f) - 1f;
                var z = ((packed >> 21) & 0x3FF) * (2f / 1023f) - 1f;
                var w = MathF.Sqrt(1f - MathF.Min(0.999999f, x * x + y * y + z * z));

                return new Quaternion(x, y, z, (packed & 0x80000000) != 0 ? -w : w);
            }
        }

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
        /// A material that replaces the one of a single draw call of one of <see cref="SceneObjects"/>.
        /// </summary>
        /// <param name="SceneObjectIndex">Index into <see cref="SceneObjects"/>.</param>
        /// <param name="SubSceneObject">Index of the mesh within the scene object's model.</param>
        /// <param name="DrawCallIndex">Index of the draw call within the mesh.</param>
        /// <param name="Material">Name of the replacement material.</param>
        public readonly record struct MaterialOverride(int SceneObjectIndex, int SubSceneObject, int DrawCallIndex, string Material);

        /// <summary>
        /// Gets the materials that replace those of single draw calls of <see cref="SceneObjects"/>.
        /// </summary>
        public IEnumerable<MaterialOverride> MaterialOverrides
            => (Data.GetArray("m_materialOverrides") ?? []).Select(static materialOverride => new MaterialOverride(
                materialOverride.GetInt32Property("m_nSceneObjectIndex"),
                materialOverride.GetInt32Property("m_nSubSceneObject"),
                materialOverride.GetInt32Property("m_nDrawCallIndex"),
                materialOverride.GetStringProperty("m_pMaterial")));

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
