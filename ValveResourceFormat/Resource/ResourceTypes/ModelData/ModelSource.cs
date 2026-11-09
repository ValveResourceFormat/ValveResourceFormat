using ValveKeyValue;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.ResourceTypes.ModelData
{
    /// <summary>
    /// A draw call of a mesh that is being created: a range of one index buffer, drawn with one material.
    /// </summary>
    /// <param name="Material">The material path.</param>
    /// <param name="BaseVertex">The vertex that index 0 refers to.</param>
    /// <param name="VertexCount">The number of vertices the draw call uses.</param>
    /// <param name="StartIndex">The first index of the draw call.</param>
    /// <param name="IndexCount">The number of indices of the draw call.</param>
    public sealed record MeshDrawCall(string Material, int BaseVertex, int VertexCount, int StartIndex, int IndexCount)
    {
        /// <summary>Gets the index of the vertex buffer in <see cref="VBIB.VertexBuffers"/> the draw call reads.</summary>
        public int VertexBufferIndex { get; init; }

        /// <summary>Gets the index of the index buffer in <see cref="VBIB.IndexBuffers"/> the draw call reads.</summary>
        public int IndexBufferIndex { get; init; }

        /// <summary>Gets the handshake of the environment map the draw call reflects, or 0 to reflect the one picked for the model.</summary>
        public int CubeMapHandshake { get; init; }
    }

    /// <summary>
    /// A bone of the skeleton a mesh is skinned with.
    /// </summary>
    /// <param name="Name">The bone name.</param>
    /// <param name="ParentName">The name of the parent bone, empty for a root.</param>
    /// <param name="InverseBindPose">The transform from model space to the space of the bone in the bind pose.</param>
    public sealed record MeshBone(string Name, string ParentName, Matrix4x4 InverseBindPose);

    /// <summary>
    /// A bone of the skeleton of a model that is being created.
    /// </summary>
    /// <param name="Name">The bone name.</param>
    /// <param name="ParentIndex">The index of the parent bone, -1 for a root.</param>
    /// <param name="Position">The position relative to the parent bone.</param>
    /// <param name="Rotation">The rotation relative to the parent bone.</param>
    /// <param name="Scale">The scale relative to the parent bone.</param>
    /// <param name="Flags">How the bone is used.</param>
    public sealed record ModelBone(string Name, int ParentIndex, Vector3 Position, Quaternion Rotation, float Scale, ModelSkeletonBoneFlags Flags = ModelSkeletonBoneFlags.NoBoneFlags);

    /// <summary>
    /// A set of materials that replaces the materials the meshes of a model are drawn with.
    /// </summary>
    /// <param name="Name">The group name.</param>
    /// <param name="Materials">The material paths, in the order the materials of the default group are used.</param>
    public sealed record ModelMaterialGroup(string Name, IReadOnlyList<string> Materials);

    /// <summary>
    /// A mesh to embed in a model that is being created.
    /// </summary>
    /// <param name="Name">The mesh name.</param>
    /// <param name="Buffers">The vertex and index buffers of the mesh.</param>
    /// <param name="DrawCalls">The draw calls of the mesh.</param>
    /// <param name="MinBounds">The lower corner of the bounds of the mesh.</param>
    /// <param name="MaxBounds">The upper corner of the bounds of the mesh.</param>
    public sealed record ModelMeshSource(string Name, VBIB Buffers, IReadOnlyList<MeshDrawCall> DrawCalls, Vector3 MinBounds, Vector3 MaxBounds)
    {
        /// <summary>Gets the mesh groups the mesh belongs to, bit N for group N of the model.</summary>
        public ulong MeshGroupMask { get; init; } = ulong.MaxValue;

        /// <summary>Gets the levels of detail the mesh is part of, bit N for level N.</summary>
        public long LodMask { get; init; } = 255;

        /// <summary>
        /// Gets the bounds of each draw call, which make the mesh an aggregate mesh whose draw calls are drawn
        /// as fragments with a meshlet each, or <see langword="null"/> for a mesh that is drawn whole.
        /// </summary>
        public IReadOnlyList<AABB>? DrawBounds { get; init; }

        /// <summary>Gets the skeleton the mesh is skinned with, empty for a mesh that is not skinned.</summary>
        public IReadOnlyList<MeshBone> Bones { get; init; } = [];

        /// <summary>Gets the bone of the model skeleton each bone of <see cref="Bones"/> stands for.</summary>
        public IReadOnlyList<int> BoneRemap { get; init; } = [];

        /// <summary>Gets how many bone weights each vertex carries.</summary>
        public int BoneWeightCount { get; init; } = 4;

        /// <summary>Gets the morph set of the mesh, whose vertex grid follows the vertices of its draw calls one after another.</summary>
        public Morph? Morph { get; init; }
    }

    /// <summary>
    /// The embedded animation data of a model that is being created, in the layout of the blocks of a compiled model.
    /// </summary>
    /// <param name="AnimationGroup">The animation group data, which holds the <c>m_decodeKey</c>.</param>
    /// <param name="AnimationData">The animation data, which holds the animations, segments and decoders.</param>
    /// <param name="SequenceGroup">The legacy sequence group data that names the sequences, bone masks and pose parameters.</param>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animationsystem/CSequenceGroupData">CSequenceGroupData</seealso>
    public sealed record ModelAnimationSource(KVObject AnimationGroup, KVObject AnimationData, KVObject SequenceGroup);
}
