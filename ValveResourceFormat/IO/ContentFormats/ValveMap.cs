using System.Numerics;
using DMElement = Datamodel.Element;
using DMAttributeName = Datamodel.Format.Attribute;
using LowercaseProperties = Datamodel.Format.AttributeNameLowercaseAttribute;
using CamelCaseProperties = Datamodel.Format.AttributeNameCamelCaseAttribute;

namespace ValveResourceFormat.IO.ContentFormats.ValveMap;

/// <summary>
///  Valve Map (VMAP) format version 29.
/// </summary>
[LowercaseProperties]
internal class CMapRootElement : DMElement
{
    public bool IsPrefab { get; set; }
    public int EditorBuild { get; set; } = 8600;
    public int EditorVersion { get; set; } = 400;
    public bool ShowGrid { get; set; } = true;
    public int SnapRotationAngle { get; set; } = 15;
    public float GridSpacing { get; set; } = 64;
    public bool Show3DGrid { get; set; } = true;
    [DMAttributeName("itemFile")]
    public string ItemFile { get; set; } = string.Empty;
    public CStoredCamera DefaultCamera { get; set; } = new CStoredCamera();
    [DMAttributeName("3dcameras")]
    public CStoredCameras Cameras { get; set; } = new CStoredCameras();
    public CMapWorld World { get; set; } = new CMapWorld();
    public CVisibilityMgr Visibility { get; set; } = new CVisibilityMgr();
    [DMAttributeName("mapVariables")]
    public CMapVariableSet MapVariables { get; set; } = new CMapVariableSet();
    [DMAttributeName("rootSelectionSet")]
    public CMapSelectionSet RootSelectionSet { get; set; } = new CMapSelectionSet();
    [DMAttributeName("m_ReferencedMeshSnapshots")]
    public Datamodel.ElementArray ReferencedMeshSnapshots { get; } = new();
    [DMAttributeName("m_bIsCordoning")]
    public bool IsCordoning { get; set; }
    [DMAttributeName("m_bCordonsVisible")]
    public bool CordonsVisible { get; set; }
    [DMAttributeName("nodeInstanceData")]
    public Datamodel.ElementArray NodeInstanceData { get; } = new();
}

[LowercaseProperties]
internal class CStoredCamera : DMElement
{
    public Vector3 Position { get; set; } = new Vector3(0, -1000, 1000);
    public Vector3 LookAt { get; set; }
}

[LowercaseProperties]
internal class CStoredCameras : DMElement
{
    [DMAttributeName("activecamera")]
    public int ActiveCameraIndex { get; set; } = -1;
    public Datamodel.ElementArray Cameras { get; } = new();
}

[CamelCaseProperties]
internal abstract class MapNode : DMElement
{
    public Vector3 Origin { get; set; }
    public Datamodel.QAngle Angles { get; set; }
    public Vector3 Scales { get; set; } = new Vector3(1, 1, 1);

    public int NodeID { get; set; }
    public ulong ReferenceID { get; set; }

    public Datamodel.ElementArray Children { get; } = new();

    public bool EditorOnly { get; set; }
    [DMAttributeName("force_hidden")]
    public bool ForceHidden { get; set; }
    public bool TransformLocked { get; set; }
    public Datamodel.StringArray VariableTargetKeys { get; } = new();
    public Datamodel.StringArray VariableNames { get; } = new();
}

[CamelCaseProperties]
internal abstract class BaseEntity : MapNode
{
    public DmePlugList RelayPlugData { get; set; } = new DmePlugList();
    public Datamodel.ElementArray ConnectionsData { get; } = new();
    [DMAttributeName("entity_properties")]
    public EditGameClassProps EntityProperties { get; set; } = new EditGameClassProps();
}

[CamelCaseProperties]
internal class DmePlugList : DMElement
{
    public Datamodel.StringArray Names { get; } = new();
    public Datamodel.IntArray DataTypes { get; } = new();
    public Datamodel.IntArray PlugTypes { get; } = new();
    public Datamodel.StringArray Descriptions { get; } = new();
}

[CamelCaseProperties]
internal class DmeConnectionData : DMElement
{
    public string OutputName { get; set; } = string.Empty;
    public int TargetType { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public string InputName { get; set; } = string.Empty;
    public string OverrideParam { get; set; } = string.Empty;
    public float Delay { get; set; }
    public int TimesToFire { get; set; } = -1;
}

/// <summary>
///  A string->string dictionary. This stores entity KeyValues.
/// </summary>
internal class EditGameClassProps : DMElement
{
}

/// <summary>
/// The world entity.
/// </summary>
[CamelCaseProperties]
internal class CMapWorld : BaseEntity
{
    public int NextDecalID { get; set; }
    public bool FixupEntityNames { get; set; } = true;
    public string MapUsageType { get; set; } = "standard";

    public CMapWorld()
    {
        EntityProperties["classname"] = "worldspawn";
    }
}

[CamelCaseProperties]
internal class CVisibilityMgr : MapNode
{
    public Datamodel.ElementArray Nodes { get; } = new();
    public Datamodel.IntArray HiddenFlags { get; } = new();
}

[CamelCaseProperties]
internal class CMapVariableSet : DMElement
{
    public Datamodel.StringArray VariableNames { get; } = new();
    public Datamodel.StringArray VariableValues { get; } = new();
    public Datamodel.StringArray VariableTypeNames { get; } = new();
    public Datamodel.StringArray VariableTypeParameters { get; } = new();
    [DMAttributeName("m_ChoiceGroups")]
    public Datamodel.ElementArray ChoiceGroups { get; } = new();
}

[CamelCaseProperties]
internal class CMapSelectionSet : DMElement
{
    public Datamodel.ElementArray Children { get; } = new();
    public string SelectionSetName { get; set; } = string.Empty;
    public DMElement SelectionSetData { get; set; }
}

[CamelCaseProperties]
internal class CMapEntity : BaseEntity
{
    public Vector3 HitNormal { get; set; }
    public bool IsProceduralEntity { get; set; }
}

internal class CMapGroup : MapNode
{
}

[CamelCaseProperties]
internal class CMapWorldLayer : CMapGroup
{
    public string WorldLayerName { get; set; } = string.Empty;
}

[CamelCaseProperties]
internal class CMapMesh : MapNode
{
    public string CubeMapName { get; set; } = string.Empty;
    public string LightGroup { get; set; } = string.Empty;
    [DMAttributeName("visexclude")]
    public bool VisExclude { get; set; }
    [DMAttributeName("renderwithdynamic")]
    public bool RenderWithDynamic { get; set; }
    public bool DisableHeightDisplacement { get; set; }
    [DMAttributeName("fademindist")]
    public float FadeMinDist { get; set; } = -1;
    [DMAttributeName("fademaxdist")]
    public float FadeMaxDist { get; set; }
    [DMAttributeName("bakelighting")]
    public bool BakeLighting { get; set; } = true;
    [DMAttributeName("precomputelightprobes")]
    public bool PrecomputeLightProbes { get; set; } = true;
    public bool RenderToCubemaps { get; set; } = true;
    public bool DisableShadows { get; set; }
    public float SmoothingAngle { get; set; } = 40f;
    public Datamodel.Color TintColor { get; set; } = new Datamodel.Color(255, 255, 255, 255);
    [DMAttributeName("renderAmt")]
    public int RenderAmount { get; set; } = 255;
    public string PhysicsType { get; set; } = "default";
    public string PhysicsGroup { get; set; } = string.Empty;
    public string PhysicsInteractsAs { get; set; } = string.Empty;
    public string PhysicsInteractWsith { get; set; } = string.Empty;
    public string PhysicsInteractsExclude { get; set; } = string.Empty;
    public CDmePolygonMesh MeshData { get; set; } = new CDmePolygonMesh();
    public bool UseAsOccluder { get; set; }
    public bool PhysicsSimplificationOverride { get; set; }
    public float PhysicsSimplificationError { get; set; }
}

[CamelCaseProperties]
internal class CDmePolygonMesh : MapNode
{
    /// <summary>
    /// Index to one of the edges stemming from this vertex.
    /// </summary>
    public Datamodel.IntArray VertexEdgeIndices { get; } = new();

    /// <summary>
    /// Index to the <see cref="VertexData"/> streams.
    /// </summary>
    public Datamodel.IntArray VertexDataIndices { get; } = new();

    /// <summary>
    /// The origin (or destination, I'm not sure) vertex of this edge.
    /// </summary>
    public Datamodel.IntArray EdgeVertexIndices { get; } = new();

    /// <summary>
    /// Index to the opposite/twin edge.
    /// </summary>
    public Datamodel.IntArray EdgeOppositeIndices { get; } = new();

    /// <summary>
    /// Index to the next edge in the loop, in counter-clockwise order.
    /// </summary>
    public Datamodel.IntArray EdgeNextIndices { get; } = new();

    /// <summary>
    /// Per half-edge index to the adjacent face. -1 if void (open edge).
    /// </summary>
    public Datamodel.IntArray EdgeFaceIndices { get; } = new();

    /// <summary>
    /// Per half-edge index to the <see cref="EdgeData"/> streams.
    /// </summary>
    public Datamodel.IntArray EdgeDataIndices { get; } = new();

    /// <summary>
    /// Per half-edge index to the <see cref="FaceVertexData"/> streams.
    /// </summary>
    public Datamodel.IntArray EdgeVertexDataIndices { get; } = new();

    /// <summary>
    /// Per face index to one of the *inner* edges encapsulating this face.
    /// </summary>
    public Datamodel.IntArray FaceEdgeIndices { get; } = new();

    /// <summary>
    /// Per face index to the <see cref="FaceData"/> streams.
    /// </summary>
    public Datamodel.IntArray FaceDataIndices { get; } = new();

    /// <summary>
    /// List of material names. Indexed by the 'meshindex' <see cref="FaceData"/> stream.
    /// </summary>
    public Datamodel.StringArray Materials { get; } = new();

    /// <summary>
    /// Stores vertex positions.
    /// </summary>
    public CDmePolygonMeshDataArray VertexData { get; } = new();

    /// <summary>
    /// Stores vertex uv, normal, tangent, etc. Two per vertex (for each half?).
    /// </summary>
    public CDmePolygonMeshDataArray FaceVertexData { get; } = new();

    /// <summary>
    /// Stores edge data such as soft or hard normals.
    /// </summary>
    public CDmePolygonMeshDataArray EdgeData { get; } = new();

    /// <summary>
    /// Stores face data such as texture scale, UV offset, material, lightmap bias.
    /// </summary>
    public CDmePolygonMeshDataArray FaceData { get; } = new();

    public CDmePolygonMeshSubdivisionData SubdivisionData { get; } = new();
}

[CamelCaseProperties]
internal class CDmePolygonMeshDataArray : DMElement
{
    public int Size { get; set; }
    /// <summary>
    /// Array of <see cref="CDmePolygonMeshDataStream"/>.
    /// </summary>
    public Datamodel.ElementArray Streams { get; } = new();
}

[CamelCaseProperties]
internal class CDmePolygonMeshSubdivisionData : DMElement
{
    public Datamodel.IntArray SubdivisionLevels { get; set; } = new();
    /// <summary>
    /// Array of <see cref="CDmePolygonMeshDataStream"/>.
    /// </summary>
    public Datamodel.ElementArray Streams { get; } = new();
}

[CamelCaseProperties]
internal class CDmePolygonMeshDataStream<T> : DMElement
{
    public string StandardAttributeName { get; set; } = string.Empty;
    public string SemanticName { get; set; } = string.Empty;
    public int SemanticIndex { get; set; }
    public int VertexBufferLocation { get; set; }
    public int DataStateFlags { get; set; }
    public DMElement SubdivisionBinding { get; set; }
    /// <summary>
    /// An int, vector2, vector3, or vector4 array.
    /// </summary>
    public Datamodel.Array<T> Data { get; set; }
}
