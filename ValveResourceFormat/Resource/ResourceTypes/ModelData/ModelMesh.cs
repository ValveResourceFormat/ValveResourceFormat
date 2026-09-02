namespace ValveResourceFormat.ResourceTypes.ModelData
{
    /// <summary>
    /// One of a model's meshes, embedded in the model itself.
    /// </summary>
    /// <param name="Mesh">The mesh.</param>
    /// <param name="MeshIndex">
    /// The mesh's index in the model. This addresses the model's LOD mask table, mesh group masks and bone
    /// remap slices, and is not the mesh's position among the embedded ones.
    /// </param>
    /// <param name="Name">The mesh's authored name.</param>
    /// <param name="LodMask">Bit N set means the mesh is in LOD level N. Zero when the model declares no LOD data.</param>
    public readonly record struct ModelMesh(Mesh Mesh, int MeshIndex, string Name, long LodMask);

    /// <summary>
    /// A reference from a model to a mesh that lives in its own vmesh, which a file loader has to resolve
    /// before the mesh itself is available.
    /// </summary>
    /// <param name="MeshIndex">
    /// The mesh's index in the model, addressing the same tables as <see cref="ModelMesh.MeshIndex"/>.
    /// </param>
    /// <param name="MeshName">The referenced mesh's resource path.</param>
    /// <param name="LodMask">Bit N set means the mesh is in LOD level N. Zero when the model declares no LOD data.</param>
    public readonly record struct ModelMeshReference(int MeshIndex, string MeshName, long LodMask);
}
