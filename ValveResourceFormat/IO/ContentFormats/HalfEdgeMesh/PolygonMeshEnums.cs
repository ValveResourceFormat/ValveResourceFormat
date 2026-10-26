namespace ValveResourceFormat.IO;

/// <summary>
/// What a data stream of a mesh holds one value for.
/// </summary>
public enum MeshComponentType
{
    /// <summary>Each vertex.</summary>
    Vertex,

    /// <summary>Each corner of each face.</summary>
    FaceVertex,

    /// <summary>Each edge.</summary>
    Edge,

    /// <summary>Each face.</summary>
    Face,

    /// <summary>Each point of the patches of the subdivided face corners.</summary>
    Subdivision,
}

/// <summary>
/// The type of the values of a data stream. Streams are stored, and referred to, by type in this order.
/// </summary>
public enum PolygonMeshDataType
{
    /// <summary>A number.</summary>
    Float,

    /// <summary>A vector of two numbers.</summary>
    Vector2,

    /// <summary>A vector of three numbers.</summary>
    Vector3,

    /// <summary>A vector of four numbers.</summary>
    Vector4,

    /// <summary>An integer.</summary>
    Int,
}

/// <summary>
/// Where a stream gets its values from at the points of a subdivided face.
/// </summary>
public enum SubdivisionStreamSource
{
    /// <summary>A subdivision stream that holds a value for every point.</summary>
    Stream,

    /// <summary>The displaced position of the point.</summary>
    Position,

    /// <summary>The normal of the displaced surface at the point.</summary>
    Normal,

    /// <summary>The tangent of the displaced surface at the point.</summary>
    Tangent,
}

/// <summary>
/// What the flags of an edge say about how it shades across its two faces.
/// </summary>
[Flags]
public enum PolygonMeshEdgeFlags
{
    /// <summary>Shading follows the smoothing angle.</summary>
    None = 0,

    /// <summary>Normals break at the edge.</summary>
    HardNormals = 1,

    /// <summary>Normals are averaged across the edge.</summary>
    SoftNormals = 2,
}

/// <summary>
/// What the flags of a face say about it.
/// </summary>
[Flags]
public enum PolygonMeshFaceFlags
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The face is selected. Only lasts while a mesh is being edited.</summary>
    Selected = 1,

    /// <summary>The face is hidden: kept in the mesh, but not drawn.</summary>
    Hidden = 2,

    /// <summary>The face belongs to the base of a tile mesh and has a tile on it. Only lasts while a mesh is being edited.</summary>
    CoveredByTile = 4,
}
