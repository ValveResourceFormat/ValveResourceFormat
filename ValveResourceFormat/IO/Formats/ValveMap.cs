using System.Numerics;
using DMElement = Datamodel.Element;
using DMAttributeName = Datamodel.Format.Attribute;
using LowercaseProperties = Datamodel.Format.AttributeNameLowercaseAttribute;
using CamelCaseProperties = Datamodel.Format.AttributeNameCamelCaseAttribute;

namespace ValveResourceFormat.IO.Formats.ValveMap;

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
internal abstract class BaseNode : DMElement
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
internal abstract class BaseEntity : BaseNode
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
internal class CVisibilityMgr : BaseNode
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

internal class CMapGroup : BaseNode
{
}
