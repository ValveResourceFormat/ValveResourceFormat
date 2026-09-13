using Datamodel.Format;
using DMElement = Datamodel.Element;

namespace ValveResourceFormat.IO.ContentFormats.ValveMap;

/// <summary>
///  Valve Map (VMAP) format version 29.
/// </summary>
[LowercaseProperties]
public class CMapRootElement : DMElement
{
    /// <summary>
    /// Whether this file is a prefab rather than a standalone map.
    /// </summary>
    public bool IsPrefab { get; set; }

    /// <summary>
    /// Hammer build number that wrote the file.
    /// </summary>
    public int EditorBuild { get; set; } = 8600;

    /// <summary>
    /// Map format version.
    /// </summary>
    public int EditorVersion { get; set; } = 400;

    /// <summary>
    /// Whether the 2D grid is drawn.
    /// </summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>
    /// Rotation snap in degrees.
    /// </summary>
    public int SnapRotationAngle { get; set; } = 15;

    /// <summary>
    /// Translation snap in world units.
    /// </summary>
    public float GridSpacing { get; set; } = 64;

    /// <summary>
    /// Whether the 3D grid is drawn.
    /// </summary>
    public bool Show3DGrid { get; set; } = true;

    /// <summary>
    /// Path to the item file this map uses, if any.
    /// </summary>
    [DMProperty(name: "itemFile")]
    public string ItemFile { get; set; } = string.Empty;

    /// <summary>
    /// Camera Hammer opens the map with.
    /// </summary>
    public CStoredCamera DefaultCamera { get; init; } = [];

    /// <summary>
    /// Saved cameras.
    /// </summary>
    [DMProperty(name: "3dcameras")]
    public CStoredCameras Cameras { get; init; } = [];

    /// <summary>
    /// Root of the map tree.
    /// </summary>
    public CMapWorld World { get; init; } = [];

    /// <summary>
    /// Per node hidden state.
    /// </summary>
    [DMProperty(name: "visbility")]
    public CVisibilityMgr Visibility { get; init; } = [];

    /// <summary>
    /// Map variables and their values.
    /// </summary>
    [DMProperty(name: "mapVariables")]
    public CMapVariableSet MapVariables { get; init; } = [];

    /// <summary>
    /// Root of the selection set tree.
    /// </summary>
    [DMProperty(name: "rootSelectionSet")]
    public CMapSelectionSet RootSelectionSet { get; init; } = [];

    /// <summary>
    /// Mesh snapshots the map references.
    /// </summary>
    [DMProperty(name: "m_ReferencedMeshSnapshots")]
    public Datamodel.ElementArray ReferencedMeshSnapshots { get; init; } = [];

    /// <summary>
    /// Whether the cordon is active.
    /// </summary>
    [DMProperty(name: "m_bIsCordoning")]
    public bool IsCordoning { get; set; }

    /// <summary>
    /// Whether cordon bounds are drawn.
    /// </summary>
    [DMProperty(name: "m_bCordonsVisible")]
    public bool CordonsVisible { get; set; }

    /// <summary>
    /// Per node instance data.
    /// </summary>
    [DMProperty(name: "nodeInstanceData")]
    public Datamodel.ElementArray NodeInstanceData { get; init; } = [];
}

/// <summary>
/// A saved 3D viewport camera.
/// </summary>
[LowercaseProperties]
public class CStoredCamera : DMElement
{
    /// <summary>
    /// Where the camera sits.
    /// </summary>
    public Vector3 Position { get; set; } = new Vector3(0, -1000, 1000);

    /// <summary>
    /// What the camera points at.
    /// </summary>
    public Vector3 LookAt { get; set; }
}

/// <summary>
/// The saved cameras of a map, and which one is active.
/// </summary>
[LowercaseProperties]
public class CStoredCameras : DMElement
{
    /// <summary>
    /// Index into <see cref="Cameras"/>, -1 when none is active.
    /// </summary>
    [DMProperty(name: "activecamera")]
    public int ActiveCameraIndex { get; set; } = -1;

    /// <summary>
    /// List of <see cref="CStoredCamera"/> elements.
    /// </summary>
    public Datamodel.ElementArray Cameras { get; init; } = [];
}

/// <summary>
/// Base of everything that appears in the map tree: a transform, an id, and child nodes.
/// </summary>
[CamelCaseProperties]
public abstract class MapNode : DMElement
{
    /// <summary>
    /// Position of the node, relative to its parent.
    /// </summary>
    public Vector3 Origin { get; set; }

    /// <summary>
    /// Rotation of the node, relative to its parent.
    /// </summary>
    public Datamodel.QAngle Angles { get; set; }

    /// <summary>
    /// Scale of the node, relative to its parent.
    /// </summary>
    public Vector3 Scales { get; set; } = new Vector3(1, 1, 1);

    /// <summary>
    /// Id of the node within the map, referenced by <see cref="CVisibilityMgr"/> and selection sets.
    /// </summary>
    public int NodeID { get; set; }

    /// <summary>
    /// Id the node keeps across prefab and instance boundaries.
    /// </summary>
    public ulong ReferenceID { get; set; }

    /// <summary>
    /// Child nodes parented to this one.
    /// </summary>
    public Datamodel.ElementArray Children { get; init; } = [];

    /// <summary>
    /// Whether the node is stripped at compile time.
    /// </summary>
    public bool EditorOnly { get; set; }

    /// <summary>
    /// Whether the node is hidden in Hammer.
    /// </summary>
    [DMProperty(name: "force_hidden")]
    public bool ForceHidden { get; set; }

    /// <summary>
    /// Whether Hammer refuses to move the node.
    /// </summary>
    public bool TransformLocked { get; set; }

    /// <summary>
    /// Entity keys driven by a map variable, parallel to <see cref="VariableNames"/>.
    /// </summary>
    public Datamodel.StringArray VariableTargetKeys { get; init; } = [];

    /// <summary>
    /// Map variables driving <see cref="VariableTargetKeys"/>.
    /// </summary>
    public Datamodel.StringArray VariableNames { get; init; } = [];
}

/// <summary>
/// References another map file and places its contents at this node.
/// </summary>
[CamelCaseProperties]
public class CMapPrefab : MapNode
{
    /// <summary>
    /// Whether entity names inside the prefab are prefixed to keep them unique.
    /// </summary>
    public bool FixupEntityNames { get; set; } = true;

    /// <summary>
    /// Whether the prefab is spawned at runtime instead of merged at compile time.
    /// </summary>
    public bool LoadAtRuntime { get; set; }

    /// <summary>
    /// Whether the prefab still loads when it sits inside another prefab.
    /// </summary>
    public bool LoadIfNested { get; set; } = true;

    /// <summary>
    /// Path to the map file this prefab pulls in.
    /// </summary>
    public string TargetMapPath { get; set; } = string.Empty;

    /// <summary>
    /// Name given to the prefab instance.
    /// </summary>
    public string TargetName { get; set; } = string.Empty;
}

/// <summary>
/// Base of every map node that carries entity key values and entity IO.
/// </summary>
[CamelCaseProperties]
public abstract class BaseEntity : MapNode
{
    /// <summary>
    /// Output plugs this entity fires through, one per entity IO connection.
    /// </summary>
    public DmePlugList RelayPlugData { get; init; } = [];

    /// <summary>
    /// List of <see cref="DmeConnectionData"/> elements, one per entity IO connection.
    /// </summary>
    public Datamodel.ElementArray ConnectionsData { get; init; } = [];

    /// <summary>
    /// The entity key values, including "classname".
    /// </summary>
    [DMProperty(name: "entity_properties")]
    public EditGameClassProps EntityProperties { get; init; } = [];

    /// <summary>
    /// Sets one entity key value and returns this entity.
    /// </summary>
    /// <param name="name">Key to set.</param>
    /// <param name="value">Value to set it to.</param>
    public BaseEntity WithProperty(string name, string value)
    {
        EntityProperties[name] = value;
        return this;
    }

    /// <summary>
    /// Sets several entity key values and returns this entity.
    /// </summary>
    /// <param name="properties">Key value pairs to set.</param>
    public BaseEntity WithProperties(params (string name, string value)[] properties)
    {
        foreach (var (name, value) in properties)
        {
            EntityProperties[name] = value;
        }

        return this;
    }

    /// <summary>
    /// Sets the "classname" key value and returns this entity.
    /// </summary>
    /// <param name="className">Entity class name.</param>
    public BaseEntity WithClassName(string className)
        => WithProperty("classname", className);
}

/// <summary>
/// The output plugs of an entity, stored as parallel arrays.
/// </summary>
[CamelCaseProperties]
public class DmePlugList : DMElement
{
    /// <summary>
    /// Plug names.
    /// </summary>
    public Datamodel.StringArray Names { get; init; } = [];

    /// <summary>
    /// Data type of each plug.
    /// </summary>
    public Datamodel.IntArray DataTypes { get; init; } = [];

    /// <summary>
    /// Kind of each plug, input or output.
    /// </summary>
    public Datamodel.IntArray PlugTypes { get; init; } = [];

    /// <summary>
    /// Description of each plug.
    /// </summary>
    public Datamodel.StringArray Descriptions { get; init; } = [];
}

/// <summary>
/// One entity IO connection: an output firing an input on a target.
/// </summary>
[CamelCaseProperties]
public class DmeConnectionData : DMElement
{
    /// <summary>
    /// Output that fires, for example "OnTrigger".
    /// </summary>
    public string OutputName { get; set; } = string.Empty;

    /// <summary>
    /// How <see cref="TargetName"/> resolves to entities.
    /// </summary>
    public int TargetType { get; set; }

    /// <summary>
    /// Entities the output fires at.
    /// </summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>
    /// Input fired on the target, for example "Enable".
    /// </summary>
    public string InputName { get; set; } = string.Empty;

    /// <summary>
    /// Parameter passed to the input, overriding the output's own.
    /// </summary>
    public string OverrideParam { get; set; } = string.Empty;

    /// <summary>
    /// Delay before the input fires, in seconds.
    /// </summary>
    public float Delay { get; set; }

    /// <summary>
    /// How often the connection may fire, -1 for unlimited.
    /// </summary>
    public int TimesToFire { get; set; } = -1;
}

/// <summary>
///  A string->string dictionary. This stores entity KeyValues.
/// </summary>
public class EditGameClassProps : DMElement
{
}

/// <summary>
/// The world entity.
/// </summary>
[CamelCaseProperties]
public class CMapWorld : BaseEntity
{
    /// <summary>
    /// Next free decal id, handed out as decals are placed.
    /// </summary>
    public int NextDecalID { get; set; }

    /// <summary>
    /// Whether entity names are prefixed to keep them unique across prefabs.
    /// </summary>
    public bool FixupEntityNames { get; set; } = true;

    /// <summary>
    /// What the map is for, "standard" for a playable map.
    /// </summary>
    public string MapUsageType { get; set; } = "standard";

    /// <summary>
    /// Initializes a new instance of the <see cref="CMapWorld"/> class with classname "worldspawn".
    /// </summary>
    public CMapWorld()
    {
        EntityProperties["classname"] = "worldspawn";
    }
}

/// <summary>
/// Per node hidden state, as two parallel arrays.
/// </summary>
[CamelCaseProperties]
public class CVisibilityMgr : MapNode
{
    /// <summary>
    /// The nodes whose visibility is tracked.
    /// </summary>
    public Datamodel.ElementArray Nodes { get; init; } = [];

    /// <summary>
    /// Hidden flags, one per entry of <see cref="Nodes"/>.
    /// </summary>
    public Datamodel.IntArray HiddenFlags { get; init; } = [];
}

/// <summary>
/// Map variables, stored as parallel arrays of name, value, type and type parameters.
/// </summary>
[CamelCaseProperties]
public class CMapVariableSet : DMElement
{
    /// <summary>
    /// Variable names.
    /// </summary>
    public Datamodel.StringArray VariableNames { get; init; } = [];

    /// <summary>
    /// Variable values.
    /// </summary>
    public Datamodel.StringArray VariableValues { get; init; } = [];

    /// <summary>
    /// Variable type names.
    /// </summary>
    public Datamodel.StringArray VariableTypeNames { get; init; } = [];

    /// <summary>
    /// Parameters of the variable types, such as the options of a choice.
    /// </summary>
    public Datamodel.StringArray VariableTypeParameters { get; init; } = [];

    /// <summary>
    /// Groups the choice variables are presented in.
    /// </summary>
    [DMProperty(name: "m_ChoiceGroups")]
    public Datamodel.ElementArray ChoiceGroups { get; init; } = [];
}

/// <summary>
/// A named selection of map nodes, as shown in Hammer's selection set tree.
/// </summary>
[CamelCaseProperties]
public class CMapSelectionSet : DMElement
{
    /// <summary>
    /// Nested selection sets.
    /// </summary>
    public Datamodel.ElementArray Children { get; init; } = [];

    /// <summary>
    /// Name shown in Hammer.
    /// </summary>
    public string SelectionSetName { get; set; } = string.Empty;

    /// <summary>
    /// What this set selects: a <see cref="CObjectSelectionSetDataElement"/>, or a face, edge or vertex selection in maps from newer Hammer versions.
    /// </summary>
    public DMElement? SelectionSetData { get; init; } = new CObjectSelectionSetDataElement();

    /// <summary>
    /// Gets <see cref="SelectionSetData"/> as the node selection that sets created by VRF use.
    /// </summary>
    public CObjectSelectionSetDataElement ObjectSelection => (CObjectSelectionSetDataElement)SelectionSetData!;

    /// <summary>
    /// Initializes a new instance of the <see cref="CMapSelectionSet"/> class.
    /// </summary>
    public CMapSelectionSet() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CMapSelectionSet"/> class with a name.
    /// </summary>
    /// <param name="name">Name shown in Hammer.</param>
    public CMapSelectionSet(string name)
    {
        SelectionSetName = name;
    }
}

/// <summary>
/// The map nodes a <see cref="CMapSelectionSet"/> selects.
/// </summary>
[CamelCaseProperties]
public class CObjectSelectionSetDataElement : DMElement
{
    /// <summary>
    /// The selected nodes.
    /// </summary>
    public Datamodel.ElementArray SelectedObjects { get; init; } = [];
}

/// <summary>
/// A point or brush entity placed in the map.
/// </summary>
[CamelCaseProperties]
public class CMapEntity : BaseEntity
{
    /// <summary>
    /// Surface normal the entity was dropped onto when it was placed.
    /// </summary>
    public Vector3 HitNormal { get; set; }

    /// <summary>
    /// Whether the entity was generated by a tool rather than placed by hand.
    /// </summary>
    public bool IsProceduralEntity { get; set; }
}

/// <summary>
/// A path: an ordered chain of <see cref="CMapPathNode"/> children that Hammer edits as one spline,
/// carrying the entity keys of the path class it was placed as. The map compiler flattens the chain
/// into the entity's own <c>pathNodes</c> key and drops the children.
/// </summary>
[CamelCaseProperties]
public class CMapPath : CMapEntity
{
    /// <summary>How the spline runs between its nodes.</summary>
    public int InterpolationType { get; set; } = 1;

    /// <summary>Whether the last node joins back onto the first.</summary>
    public bool ClosedLoop { get; set; }

    /// <summary>Distance, in units, between the points of the particle snapshot a path can generate.</summary>
    public float ParticleSnapshotSpacing { get; set; } = 16f;
}

/// <summary>
/// One node of a <see cref="CMapPath"/>: a point on the spline, its two handles, and the entity keys
/// of the path node class the parent path declares.
/// </summary>
[CamelCaseProperties]
public class CMapPathNode : CMapEntity
{
    /// <summary>Name entity IO addresses this node by.</summary>
    public string PathNodeName { get; set; } = string.Empty;

    /// <summary>Incoming spline handle, relative to <see cref="MapNode.Origin"/>.</summary>
    public Vector3 InTangent { get; set; }

    /// <summary>Outgoing spline handle, relative to <see cref="MapNode.Origin"/>.</summary>
    public Vector3 OutTangent { get; set; }

    /// <summary>How Hammer derives the incoming handle.</summary>
    public int InTangentType { get; set; } = 1;

    /// <summary>How Hammer derives the outgoing handle.</summary>
    public int OutTangentType { get; set; } = 1;

    /// <summary>Colour this node tints the path with, written to the path's <c>pathNodeColors</c>.</summary>
    public Datamodel.Color TintColor { get; set; } = new Datamodel.Color(255, 255, 255, 255);

    /// <summary>Whether a simulated path is held in place at this node.</summary>
    public bool PinEnabled { get; set; } = true;

    /// <summary>Multiplier on the path's radius at this node.</summary>
    public float RadiusScale { get; set; } = 1f;
}

/// <summary>
/// A cable: a path the map compiler turns into a tube mesh, with the tube's shape and texturing
/// carried on the path itself.
/// </summary>
[CamelCaseProperties]
public class CMapCable : CMapPath
{
    /// <summary>Material of the tube mesh.</summary>
    public string MaterialName { get; set; } = string.Empty;

    /// <summary>Colour a cable_dynamic renders with, written to its <c>rendercolor</c> key.</summary>
    public Datamodel.Color TintColor { get; set; } = new Datamodel.Color(255, 255, 255, 255);

    /// <summary>Name of the entity the cable takes its lighting origin from.</summary>
    public string LightingOriginName { get; set; } = string.Empty;

    /// <summary>Vertices around the tube.</summary>
    public int NumSides { get; set; } = 8;

    /// <summary>Distance, in units, between the rings along the tube.</summary>
    public float TessellationSpacing { get; set; } = 16f;

    /// <summary>Tube radius, in units, before the per-node radius scale.</summary>
    public float Radius { get; set; } = 16f;

    /// <summary>Whether the tube faces inwards.</summary>
    public bool FlipFaces { get; set; }

    /// <summary>0 runs U along the path, 1 runs V along the path.</summary>
    public int TextureOrientation { get; set; }

    /// <summary>Units of path per texture repeat, divided by the texture's size in pixels.</summary>
    public float TextureScale { get; set; } = 0.25f;

    /// <summary>Texture repeats around the tube.</summary>
    public float TextureRepeatsCircumference { get; set; } = 1f;

    /// <summary>Texture offset along the path, in repeats.</summary>
    public float TextureOffsetAlongPath { get; set; }

    /// <summary>Texture offset around the tube, in repeats.</summary>
    public float TextureOffsetCircumference { get; set; }

    /// <summary>Whether a collision tube is generated.</summary>
    public bool CollisionEnabled { get; set; } = true;

    /// <summary>Simplification error allowed on the collision tube; 0 keeps every ring.</summary>
    public float PhysicsSimplificationError { get; set; } = 2f;

    /// <summary>Whether the tube occludes visibility.</summary>
    public bool VisOccluder { get; set; }
}

/// <summary>
/// Places another map group into the map with its own transform and tint.
/// </summary>
[CamelCaseProperties]
public class CMapInstance : BaseEntity
{
    /// <summary>
    /// A target <see cref="CMapGroup"/> to instance. With custom tint and transform.
    /// </summary>
    public DMElement? Target { get; init; }

    /// <summary>
    /// Tint applied to everything in the instance.
    /// </summary>
    public Datamodel.Color TintColor { get; set; } = new Datamodel.Color(255, 255, 255, 255);
}

/// <summary>
/// Groups child nodes under one selectable node. Also the target of a <see cref="CMapInstance"/>.
/// </summary>
public class CMapGroup : MapNode
{
}

/// <summary>
/// A named world layer, which is a map group that compiles into its own layer.
/// </summary>
[CamelCaseProperties]
public class CMapWorldLayer : CMapGroup
{
    /// <summary>
    /// Name of the layer.
    /// </summary>
    public string WorldLayerName { get; set; } = string.Empty;
}

/// <summary>
/// A mesh authored in Hammer, with its render, lighting and physics settings.
/// </summary>
[CamelCaseProperties]
public class CMapMesh : MapNode
{
    /// <summary>
    /// Cubemap this mesh samples, empty to pick automatically.
    /// </summary>
    public string CubeMapName { get; set; } = string.Empty;

    /// <summary>
    /// Light group this mesh belongs to.
    /// </summary>
    public string LightGroup { get; set; } = string.Empty;

    /// <summary>
    /// Whether the mesh is left out of visibility computation.
    /// </summary>
    [DMProperty(name: "visexclude")]
    public bool VisExclude { get; set; }

    /// <summary>
    /// Whether the mesh renders in the dynamic pass.
    /// </summary>
    [DMProperty(name: "renderwithdynamic")]
    public bool RenderWithDynamic { get; set; }

    /// <summary>
    /// Whether height displacement is skipped for this mesh.
    /// </summary>
    public bool DisableHeightDisplacement { get; set; }

    /// <summary>
    /// Distance at which the mesh starts fading out, -1 to never fade.
    /// </summary>
    [DMProperty(name: "fademindist")]
    public float FadeMinDist { get; set; } = -1;

    /// <summary>
    /// Distance at which the mesh is fully faded out.
    /// </summary>
    [DMProperty(name: "fademaxdist")]
    public float FadeMaxDist { get; set; }

    /// <summary>
    /// Whether the mesh takes part in baked lighting.
    /// </summary>
    [DMProperty(name: "bakelighting")]
    public bool BakeLighting { get; set; } = true;

    /// <summary>
    /// Whether light probes are precomputed around the mesh.
    /// </summary>
    [DMProperty(name: "precomputelightprobes")]
    public bool PrecomputeLightProbes { get; set; } = true;

    /// <summary>
    /// Whether the mesh appears in cubemap renders.
    /// </summary>
    public bool RenderToCubemaps { get; set; } = true;

    /// <summary>
    /// Whether the mesh casts no shadows.
    /// </summary>
    public bool DisableShadows { get; set; }

    /// <summary>
    /// Angle below which adjacent faces are shaded smooth, in degrees.
    /// </summary>
    public float SmoothingAngle { get; set; } = 40f;

    /// <summary>
    /// Tint applied to the mesh.
    /// </summary>
    public Datamodel.Color TintColor { get; set; } = new Datamodel.Color(255, 255, 255, 255);

    /// <summary>
    /// Render alpha, 0 to 255.
    /// </summary>
    [DMProperty(name: "renderAmt")]
    public int RenderAmount { get; set; } = 255;

    /// <summary>
    /// Physics model to build for the mesh.
    /// </summary>
    public string PhysicsType { get; set; } = "default";

    /// <summary>
    /// Collision group of the mesh.
    /// </summary>
    public string PhysicsGroup { get; set; } = string.Empty;

    /// <summary>
    /// Collision categories the mesh counts as.
    /// </summary>
    public string PhysicsInteractsAs { get; set; } = string.Empty;

    /// <summary>
    /// Collision categories the mesh collides with.
    /// </summary>
    public string PhysicsInteractsWith { get; set; } = string.Empty;

    /// <summary>
    /// Collision categories the mesh never collides with.
    /// </summary>
    public string PhysicsInteractsExclude { get; set; } = string.Empty;

    /// <summary>
    /// The geometry itself.
    /// </summary>
    public CDmePolygonMesh MeshData { get; init; } = [];

    /// <summary>
    /// Whether the mesh occludes what is behind it.
    /// </summary>
    public bool UseAsOccluder { get; set; }

    /// <summary>
    /// Whether <see cref="PhysicsSimplificationError"/> overrides the default simplification.
    /// </summary>
    public bool PhysicsSimplificationOverride { get; set; }

    /// <summary>
    /// Error the physics simplification is allowed to introduce.
    /// </summary>
    public float PhysicsSimplificationError { get; set; }
}

/// <summary>
/// A decal which uses its own hammer editable mesh to project onto geometry.
/// </summary>
[CamelCaseProperties]
public class CMapStaticOverlay : CMapMesh
{
    /// <summary>
    /// Node ids of the nodes the overlay projects onto.
    /// </summary>
    public Datamodel.IntArray ProjectionTargets { get; init; } = [];

    /// <summary>
    /// Order the overlay is drawn in where overlays stack, higher on top.
    /// </summary>
    public int RenderOrder { get; set; }

    /// <summary>
    /// Whether the overlay is left out at low quality settings.
    /// </summary>
    public bool DisabledInLowQuality { get; set; }

    /// <summary>
    /// Whether the overlay shades with the normals of the surface under it rather than its own.
    /// </summary>
    public bool UseBaseNormals { get; set; }

    /// <summary>
    /// How far from its mesh the overlay projects.
    /// </summary>
    public float ProjectionFar { get; set; } = 128f;

    /// <summary>
    /// Adjustments applied to the decal material.
    /// </summary>
    [DMProperty(name: "MaterialAdjustmentParamsStruct")]
    public DMElement MaterialAdjustmentParamsStruct { get; init; } = new CMapOverlayMaterialAdjustmentParams();

    /// <summary>
    /// Whether the overlay also lands on faces turned away from it.
    /// </summary>
    public bool ProjectOnBackFaces { get; set; }

    /// <summary>
    /// Angle from the projection direction beyond which a face counts as facing away, in degrees.
    /// </summary>
    public float BackFacingAngle { get; set; } = 90f;

    /// <summary>
    /// What the overlay projects onto: everything (0), world geometry (1), models (2) or its
    /// <see cref="ProjectionTargets"/> (3).
    /// </summary>
    public int ProjectionMode { get; set; }
}

/// <summary>
/// The material adjustments of a <see cref="CMapStaticOverlay"/>.
/// </summary>
public class CMapOverlayMaterialAdjustmentParams : DMElement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CMapOverlayMaterialAdjustmentParams"/> class with Hammer's defaults.
    /// </summary>
    public CMapOverlayMaterialAdjustmentParams()
    {
        ClassName = "DmElement";
        Name = "MaterialAdjustmentParamsStruct";
    }

    /// <summary>Colour brightness adjustment, 0.5 for none.</summary>
    public float ColorBrightness { get; set; } = 0.5f;

    /// <summary>Colour contrast adjustment, 0.5 for none.</summary>
    public float ColorContrast { get; set; } = 0.5f;

    /// <summary>Opacity of the colour.</summary>
    public float ColorAlpha { get; set; } = 1f;

    /// <summary>Roughness brightness adjustment, 0.5 for none.</summary>
    public float RoughnessBrightness { get; set; } = 0.5f;

    /// <summary>Roughness contrast adjustment, 0.5 for none.</summary>
    public float RoughnessContrast { get; set; } = 0.5f;

    /// <summary>Opacity of the shading.</summary>
    public float ShadingAlpha { get; set; } = 1f;

    /// <summary>Strength of the decal's normal map.</summary>
    public float NormalIntensity { get; set; } = 0.75f;

    /// <summary>Whether the decal's roughness and metalness replace the surface's.</summary>
    public bool RoughnessMetalnessOverride { get; set; }

    /// <summary>Whether the decal's normals blend over the surface's.</summary>
    public bool NormalBlendOverride { get; set; } = true;
}

/// <summary>
/// Hammer's editable mesh, stored as a half edge mesh with parallel index arrays and data streams.
/// </summary>
[CamelCaseProperties]
public class CDmePolygonMesh : DMElement
{
    /// <summary>
    /// Index to one of the edges stemming from this vertex.
    /// </summary>
    public Datamodel.IntArray VertexEdgeIndices { get; init; } = [];

    /// <summary>
    /// Index to the <see cref="VertexData"/> streams.
    /// </summary>
    public Datamodel.IntArray VertexDataIndices { get; init; } = [];

    /// <summary>
    /// The destination vertex of this edge.
    /// </summary>
    public Datamodel.IntArray EdgeVertexIndices { get; init; } = [];

    /// <summary>
    /// Index to the opposite/twin edge.
    /// </summary>
    public Datamodel.IntArray EdgeOppositeIndices { get; init; } = [];

    /// <summary>
    /// Index to the next edge in the loop, in counter-clockwise order.
    /// </summary>
    public Datamodel.IntArray EdgeNextIndices { get; init; } = [];

    /// <summary>
    /// Per half-edge index to the adjacent face. -1 if void (open edge).
    /// </summary>
    public Datamodel.IntArray EdgeFaceIndices { get; init; } = [];

    /// <summary>
    /// Per half-edge index to the <see cref="EdgeData"/> streams.
    /// </summary>
    public Datamodel.IntArray EdgeDataIndices { get; init; } = [];

    /// <summary>
    /// Per half-edge index to the <see cref="FaceVertexData"/> streams.
    /// </summary>
    public Datamodel.IntArray EdgeVertexDataIndices { get; init; } = [];

    /// <summary>
    /// Per face index to one of the *inner* edges encapsulating this face.
    /// </summary>
    public Datamodel.IntArray FaceEdgeIndices { get; init; } = [];

    /// <summary>
    /// Per face index to the <see cref="FaceData"/> streams.
    /// </summary>
    public Datamodel.IntArray FaceDataIndices { get; init; } = [];

    /// <summary>
    /// List of material names. Indexed by the 'meshindex' <see cref="FaceData"/> stream.
    /// </summary>
    public Datamodel.StringArray Materials { get; init; } = [];

    /// <summary>
    /// Stores vertex positions.
    /// </summary>
    public CDmePolygonMeshDataArray VertexData { get; init; } = [];

    /// <summary>
    /// Stores vertex uv, normal, tangent, etc. Two per vertex (for each half?).
    /// </summary>
    public CDmePolygonMeshDataArray FaceVertexData { get; init; } = [];

    /// <summary>
    /// Stores edge data such as soft or hard normals.
    /// </summary>
    public CDmePolygonMeshDataArray EdgeData { get; init; } = [];

    /// <summary>
    /// Stores face data such as texture scale, UV offset, material, lightmap bias.
    /// </summary>
    public CDmePolygonMeshDataArray FaceData { get; init; } = [];

    /// <summary>
    /// Stores the subdivision level of each half-edge.
    /// </summary>
    public CDmePolygonMeshSubdivisionData SubdivisionData { get; init; } = [];
}

/// <summary>
/// A set of parallel data streams attached to one mesh component (vertices, half edges, or faces).
/// </summary>
[CamelCaseProperties]
public class CDmePolygonMeshDataArray : DMElement
{
    /// <summary>
    /// Number of entries each stream in <see cref="Streams"/> holds.
    /// </summary>
    public int Size { get; set; }

    /// <summary>
    /// Array of <see cref="CDmePolygonMeshDataStream"/>.
    /// </summary>
    public Datamodel.ElementArray Streams { get; init; } = [];
}

/// <summary>
/// Subdivision state of a <see cref="CDmePolygonMesh"/>.
/// </summary>
[CamelCaseProperties]
public class CDmePolygonMeshSubdivisionData : DMElement
{
    /// <summary>
    /// Subdivision level per half edge.
    /// </summary>
    public Datamodel.IntArray SubdivisionLevels { get; init; } = [];

    /// <summary>
    /// Array of <see cref="CDmePolygonMeshDataStream"/>.
    /// </summary>
    public Datamodel.ElementArray Streams { get; init; } = [];
}

/// <summary>
/// One named data stream of a <see cref="CDmePolygonMeshDataArray"/>, such as position, uv, or material index.
/// </summary>
[CamelCaseProperties]
public class CDmePolygonMeshDataStream : DMElement
{
    /// <summary>
    /// Name Hammer knows this stream by, for example "position" or "texcoord".
    /// </summary>
    public string StandardAttributeName { get; set; } = string.Empty;

    /// <summary>
    /// Name the stream binds to in the shader, for example "position" or "normal".
    /// </summary>
    public string SemanticName { get; set; } = string.Empty;

    /// <summary>
    /// Channel of <see cref="SemanticName"/> this stream fills.
    /// </summary>
    public int SemanticIndex { get; set; }

    /// <summary>
    /// Slot this stream occupies in the vertex buffer.
    /// </summary>
    public int VertexBufferLocation { get; set; }

    /// <summary>
    /// Flags describing how the stream is stored.
    /// </summary>
    public int DataStateFlags { get; set; }

    /// <summary>
    /// Subdivision stream this one mirrors, or null.
    /// </summary>
    public DMElement? SubdivisionBinding { get; init; }

    /// <summary>
    /// An int, vector2, vector3, or vector4 array: <see cref="Datamodel.IntArray"/>, <see cref="Datamodel.Vector2Array"/>,
    /// <see cref="Datamodel.Vector3Array"/> or <see cref="Datamodel.Vector4Array"/>.
    /// </summary>
    public System.Collections.IList? Data { get; init; }
}
