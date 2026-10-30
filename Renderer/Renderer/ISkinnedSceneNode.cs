namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// A scene node that supplies bone matrices to the GPU skinning of its meshes. The scene reserves a run of
    /// transforms that starts two slots after the node's own transform and calls
    /// <see cref="WriteBoneTransforms"/> to fill it.
    /// </summary>
    public interface ISkinnedSceneNode
    {
        /// <summary>Gets the number of bones the node's meshes index. Zero when the node does not skin.</summary>
        int BoneCount { get; }

        /// <summary>Gets or sets the slot of the node's own transform in the scene transform table, set by the scene.</summary>
        uint TransformSlot { get; set; }

        /// <summary>Writes the skinning matrix of every bone, taking a vertex from the bind pose to the current pose in the node's object space.</summary>
        /// <param name="boneTransforms">Receives <see cref="BoneCount"/> matrices.</param>
        void WriteBoneTransforms(Span<OpenTK.Mathematics.Matrix3x4> boneTransforms);
    }
}
