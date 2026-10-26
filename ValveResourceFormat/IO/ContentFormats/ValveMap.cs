using System.Globalization;
using System.Linq;
using Datamodel.Format;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using DMElement = Datamodel.Element;

namespace ValveResourceFormat.IO.ContentFormats.ValveMap;

/// <summary>
///  Valve Map (VMAP) format version 29.
/// </summary>
[LowercaseProperties]
public class CMapRootElement : DMElement
{
    private CStoredCamera defaultCamera = [];
    private CMapVariableSet mapVariables = [];
    private CVisibilityMgr visibility = [];

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
    public CStoredCamera DefaultCamera
    {
        get => defaultCamera;
        init => defaultCamera = value;
    }

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
    public CVisibilityMgr Visibility
    {
        get => visibility;
        init => visibility = value;
    }

    /// <summary>
    /// Map variables and their values.
    /// </summary>
    [DMProperty(name: "mapVariables")]
    public CMapVariableSet MapVariables
    {
        get => mapVariables;
        init => mapVariables = value;
    }

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

    /// <summary>
    /// Called after the map is loaded. Lets every node convert its saved data into the form it is used in at runtime.
    /// </summary>
    public void PostLoadFromFile()
    {
        // defaults for maps lacking these
        defaultCamera ??= Adopt(new CStoredCamera { Name = "defaultcamera" });
        mapVariables ??= Adopt(new CMapVariableSet { Name = "mapVariables" });
        visibility ??= Adopt(new CVisibilityMgr { Name = "visbility" });

        Visibility.World = World;

        World.PostLoadFromFile();
        Visibility.PostLoadFromFile();
    }

    private T Adopt<T>(T element) where T : DMElement
    {
        Owner?.ImportElement(element, Datamodel.Datamodel.ImportRecursionMode.Recursive, Datamodel.Datamodel.ImportOverwriteMode.None);

        return element;
    }

    /// <summary>
    /// Called before the map is saved. Lets every node rebuild its saved data.
    /// </summary>
    public void PreSaveToFile()
    {
        World.PreSaveToFile();
        Visibility.PreSaveToFile();
    }

    /// <summary>
    /// Called after the map is saved. Lets every node discard the saved data that <see cref="PreSaveToFile"/> rebuilt.
    /// </summary>
    public void PostSaveToFile()
    {
        World.PostSaveToFile();
        Visibility.PostSaveToFile();
    }
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

    /// <summary>
    /// Enumerates every node under this one, a node before its own children. A node of a class that is not
    /// modelled here is passed over, the nodes under it are not.
    /// </summary>
    public IEnumerable<MapNode> EnumerateDescendants() => EnumerateDescendantElements(this).OfType<MapNode>();

    // Everything in the tree under a node. A node of a class that is not modelled here is a plain element, which
    // keeps its children in an attribute.
    internal static IEnumerable<DMElement> EnumerateDescendantElements(DMElement node)
    {
        if (GetChildren(node) is not { } children)
        {
            yield break;
        }

        foreach (var child in children)
        {
            if (child == null)
            {
                continue;
            }

            yield return child;

            foreach (var descendant in EnumerateDescendantElements(child))
            {
                yield return descendant;
            }
        }
    }

    private static Datamodel.ElementArray? GetChildren(DMElement node)
        => node is MapNode mapNode ? mapNode.Children : node.ContainsKey("children") ? node["children"] as Datamodel.ElementArray : null;

    // The nodes right under this one, looking through nodes of classes that are not modelled here
    private IEnumerable<MapNode> EnumerateChildNodes()
    {
        var pending = new Stack<DMElement>();
        pending.Push(this);

        while (pending.TryPop(out var parent))
        {
            if (GetChildren(parent) is not { } children)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (child is MapNode node)
                {
                    yield return node;
                }
                else if (child != null)
                {
                    pending.Push(child);
                }
            }
        }
    }

    /// <summary>
    /// Called after the map is loaded, for the node to convert its saved data into the form it is used in at
    /// runtime. It is not passed on to the children, the world calls it on every node under it.
    /// </summary>
    public virtual void PostLoadFromFile()
    {
    }

    /// <summary>
    /// Called before the map is saved, for the node and its children to rebuild their saved data.
    /// </summary>
    public virtual void PreSaveToFile()
    {
        foreach (var child in EnumerateChildNodes())
        {
            child.PreSaveToFile();
        }
    }

    /// <summary>
    /// Called after the map is saved, for the node and its children to discard the saved data that
    /// <see cref="PreSaveToFile"/> rebuilt.
    /// </summary>
    public virtual void PostSaveToFile()
    {
        foreach (var child in EnumerateChildNodes())
        {
            child.PostSaveToFile();
        }
    }
}

/// <summary>
/// Base of every map node that takes part in entity IO.
/// </summary>
[CamelCaseProperties]
public abstract class EntityIONode : MapNode
{
    private DmePlugList? relayPlugData = [];
    private List<EntityConnection>? connections;
    private List<RelayPlug>? relayPlugs;

    /// <summary>
    /// Output plugs this entity fires through, in their saved form. After loading they are in
    /// <see cref="RelayPlugs"/> instead and this is null, except while the map is being saved.
    /// </summary>
    public DmePlugList? RelayPlugData
    {
        get => relayPlugData;
        init => relayPlugData = value;
    }

    /// <summary>
    /// The entity IO connections in their saved form, one <see cref="DmeConnectionData"/> element each. After
    /// loading they are in <see cref="Connections"/> instead and this is empty, except while the map is being saved.
    /// </summary>
    public Datamodel.ElementArray ConnectionsData { get; init; } = [];

    /// <summary>
    /// The entity IO connections of a node that was loaded, see <see cref="PostLoadFromFile"/>.
    /// </summary>
    [DMIgnore]
    public List<EntityConnection> Connections => connections ??= [];

    /// <summary>
    /// The output plugs of a node that was loaded, see <see cref="PostLoadFromFile"/>.
    /// </summary>
    [DMIgnore]
    public List<RelayPlug> RelayPlugs => relayPlugs ??= [];

    /// <summary>
    /// Converts the saved entity IO into its runtime form: <see cref="RelayPlugData"/> and
    /// <see cref="ConnectionsData"/> are copied into <see cref="RelayPlugs"/> and <see cref="Connections"/>, and
    /// their elements are then removed from the map.
    /// </summary>
    public override void PostLoadFromFile()
    {
        base.PostLoadFromFile();

        relayPlugs = [];
        connections = [];

        if (relayPlugData is { } plugs)
        {
            for (var i = 0; i < plugs.Names.Count; i++)
            {
                relayPlugs.Add(new RelayPlug(
                    plugs.Names[i],
                    i < plugs.DataTypes.Count ? plugs.DataTypes[i] : 0,
                    i < plugs.PlugTypes.Count ? plugs.PlugTypes[i] : 0,
                    i < plugs.Descriptions.Count ? plugs.Descriptions[i] : string.Empty));
            }
        }

        var stored = ConnectionsData.OfType<DmeConnectionData>().ToArray();

        foreach (var connection in stored)
        {
            connections.Add(new EntityConnection
            {
                OutputName = connection.OutputName,
                TargetType = (EntityIOTargetType)connection.TargetType,
                TargetName = connection.TargetName,
                InputName = connection.InputName,
                OverrideParam = connection.OverrideParam,
                Delay = connection.Delay,
                TimesToFire = connection.TimesToFire,
            });
        }

        ReleaseStoredConnections();
    }

    /// <summary>
    /// Rebuilds <see cref="RelayPlugData"/> and <see cref="ConnectionsData"/> from <see cref="RelayPlugs"/> and
    /// <see cref="Connections"/>, so that the map is saved with them.
    /// </summary>
    public override void PreSaveToFile()
    {
        base.PreSaveToFile();

        // A node that was never loaded still has its saved data, so there is nothing to rebuild
        if (connections == null || relayPlugs == null)
        {
            return;
        }

        relayPlugData ??= [];
        relayPlugData.Names.Clear();
        relayPlugData.DataTypes.Clear();
        relayPlugData.PlugTypes.Clear();
        relayPlugData.Descriptions.Clear();

        foreach (var plug in relayPlugs)
        {
            relayPlugData.Names.Add(plug.Name);
            relayPlugData.DataTypes.Add(plug.DataType);
            relayPlugData.PlugTypes.Add(plug.PlugType);
            relayPlugData.Descriptions.Add(plug.Description);
        }

        ConnectionsData.Clear();

        foreach (var connection in connections)
        {
            ConnectionsData.Add(new DmeConnectionData
            {
                OutputName = connection.OutputName,
                TargetType = (int)connection.TargetType,
                TargetName = connection.TargetName,
                InputName = connection.InputName,
                OverrideParam = connection.OverrideParam,
                Delay = connection.Delay,
                TimesToFire = connection.TimesToFire,
            });
        }

        // The map only writes elements that belong to it
        Owner?.ImportElement(relayPlugData, Datamodel.Datamodel.ImportRecursionMode.Recursive, Datamodel.Datamodel.ImportOverwriteMode.None);

        foreach (var connection in ConnectionsData)
        {
            Owner?.ImportElement(connection, Datamodel.Datamodel.ImportRecursionMode.Recursive, Datamodel.Datamodel.ImportOverwriteMode.None);
        }
    }

    /// <summary>
    /// Removes <see cref="RelayPlugData"/> and <see cref="ConnectionsData"/> again after the map is saved.
    /// </summary>
    public override void PostSaveToFile()
    {
        base.PostSaveToFile();

        if (connections != null && relayPlugs != null)
        {
            ReleaseStoredConnections();
        }
    }

    private void ReleaseStoredConnections()
    {
        var stored = ConnectionsData.OfType<DMElement>().ToArray();
        var plugs = relayPlugData;

        relayPlugData = null;
        ConnectionsData.Clear();

        plugs?.Owner?.AllElements.RemoveUnreferenced(plugs);

        foreach (var connection in stored)
        {
            connection.Owner?.AllElements.RemoveUnreferenced(connection);
        }
    }
}

/// <summary>
/// Base of every map node that carries entity key values and entity IO.
/// </summary>
[CamelCaseProperties]
public abstract class BaseEntity : EntityIONode
{
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
/// One output plug of an entity, see <see cref="EntityIONode.RelayPlugs"/>.
/// </summary>
/// <param name="Name">Name of the plug.</param>
/// <param name="DataType">Type of the value the plug passes on.</param>
/// <param name="PlugType">Kind of plug.</param>
/// <param name="Description">What the plug is for.</param>
public readonly record struct RelayPlug(string Name, int DataType, int PlugType, string Description);

/// <summary>
/// One entity IO connection of a loaded entity: an output that fires an input on a target.
/// </summary>
public sealed class EntityConnection
{
    /// <summary>
    /// Output that triggers the connection.
    /// </summary>
    public string OutputName { get; set; } = string.Empty;

    /// <summary>
    /// How <see cref="TargetName"/> is resolved.
    /// </summary>
    public EntityIOTargetType TargetType { get; set; }

    /// <summary>
    /// Entity the input is sent to.
    /// </summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>
    /// Input fired on the target.
    /// </summary>
    public string InputName { get; set; } = string.Empty;

    /// <summary>
    /// Parameter passed with the input, replacing the one the output carries.
    /// </summary>
    public string OverrideParam { get; set; } = string.Empty;

    /// <summary>
    /// Seconds to wait before firing.
    /// </summary>
    public float Delay { get; set; }

    /// <summary>
    /// How many times the connection fires, -1 for no limit.
    /// </summary>
    public int TimesToFire { get; set; } = -1;
}

/// <summary>
/// One entity IO connection in its saved form: an output firing an input on a target.
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

    /// <summary>
    /// Called once the map has been read from a file. The world passes it on to every node under it.
    /// </summary>
    public override void PostLoadFromFile()
    {
        base.PostLoadFromFile();

        foreach (var node in EnumerateDescendants())
        {
            node.PostLoadFromFile();
        }
    }
}

/// <summary>
/// Why a map node is hidden, as <see cref="CVisibilityMgr.HiddenFlags"/> stores it for each node.
/// </summary>
[Flags]
public enum MapNodeHiddenFlags
{
    /// <summary>The node is not hidden.</summary>
    None = 0,

    /// <summary>The node was hidden by hand.</summary>
    Hidden = 1,

    /// <summary>The node is hidden through an automatic visibility group.</summary>
    AutoHidden = 2,

    /// <summary>Does not hide the node. Maps are saved with it, and it is cleared when a map is loaded.</summary>
    Transient = 4,

    /// <summary>The node is hidden by a tool that is working on it, such as what is being clipped.</summary>
    HiddenByTool = 8,
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

    private readonly Dictionary<DMElement, int> nodeIndices = [];
    private int indexedNodeCount = -1;

    /// <summary>
    /// The world of the map the manager belongs to, which only nodes under it are tracked for.
    /// </summary>
    [DMIgnore]
    public CMapWorld? World { get; internal set; }

    /// <summary>
    /// Brings the two lists to the same length, clears the flags that do not outlast a session and drops the
    /// nodes that are not under <see cref="World"/>.
    /// </summary>
    public override void PostLoadFromFile()
    {
        base.PostLoadFromFile();

        var count = Math.Min(Nodes.Count, HiddenFlags.Count);

        while (Nodes.Count > count)
        {
            Nodes.RemoveAt(Nodes.Count - 1);
        }

        while (HiddenFlags.Count > count)
        {
            HiddenFlags.RemoveAt(HiddenFlags.Count - 1);
        }

        for (var i = 0; i < count; i++)
        {
            HiddenFlags[i] &= ~(int)MapNodeHiddenFlags.Transient;
        }

        if (World is { } world)
        {
            var nodesInWorld = EnumerateDescendantElements(world).ToHashSet();

            for (var i = count - 1; i >= 0; i--)
            {
                if (Nodes[i] is not { } node || !nodesInWorld.Contains(node))
                {
                    Nodes.RemoveAt(i);
                    HiddenFlags.RemoveAt(i);
                }
            }
        }

        RebuildNodeIndexMap();
    }

    /// <summary>
    /// Gets the hidden flags of a node, 0 for a node that is not tracked.
    /// </summary>
    /// <param name="node">The node.</param>
    public MapNodeHiddenFlags GetNodeHiddenFlags(DMElement node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (indexedNodeCount != Nodes.Count)
        {
            RebuildNodeIndexMap();
        }

        return nodeIndices.TryGetValue(node, out var index) && index < HiddenFlags.Count ? (MapNodeHiddenFlags)HiddenFlags[index] : MapNodeHiddenFlags.None;
    }

    /// <summary>
    /// Gets whether a node is visible going by the hidden flags in a mask. A node that is not tracked is visible.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="hiddenFlagsMask">The flags that hide a node.</param>
    public bool IsNodeVisible(DMElement node, MapNodeHiddenFlags hiddenFlagsMask)
        => (GetNodeHiddenFlags(node) & hiddenFlagsMask) == 0;

    private void RebuildNodeIndexMap()
    {
        nodeIndices.Clear();

        for (var i = 0; i < Nodes.Count; i++)
        {
            if (Nodes[i] is { } node)
            {
                nodeIndices[node] = i;
            }
        }

        indexedNodeCount = Nodes.Count;
    }
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
    [DMIgnore]
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

    /// <summary>
    /// Takes over a placement the entity keeps in its key values: "origin", "angles" and "scales" are moved from
    /// <see cref="BaseEntity.EntityProperties"/> onto the node.
    /// </summary>
    public override void PostLoadFromFile()
    {
        base.PostLoadFromFile();

        if (TakeVectorKey("origin") is { } origin)
        {
            Origin = origin;
        }

        if (TakeVectorKey("angles") is { } angles)
        {
            Angles = new Datamodel.QAngle(angles.X, angles.Y, angles.Z);
        }

        if (TakeVectorKey("scales") is { } scales)
        {
            Scales = scales;
        }
    }

    // Reads up to three numbers from a key and removes it, a number that is missing being zero
    private Vector3? TakeVectorKey(string key)
    {
        if (!EntityProperties.ContainsKey(key) || EntityProperties[key] is not string text)
        {
            return null;
        }

        Span<float> components = stackalloc float[3];
        var count = 0;

        foreach (var part in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (count == components.Length || !float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out components[count]))
            {
                break;
            }

            count++;
        }

        EntityProperties.Remove(key);

        return new Vector3(components);
    }
}

/// <summary>
/// Extra vertex streams for props.
/// </summary>
public class CDmExtraVertexData : DMElement
{
    /// <summary>
    /// The painted streams, each a <see cref="CDmExtraVertexStream"/>.
    /// </summary>
    [DMProperty(name: "m_ExtraStreams")]
    public Datamodel.ElementArray ExtraStreams { get; init; } = [];
}

/// <summary>
/// Vertex data painted onto one draw call of a prop.
/// </summary>
public class CDmExtraVertexStream : DMElement
{
    /// <summary>
    /// Index of the draw call within the mesh.
    /// </summary>
    [DMProperty(name: "m_nDrawCallIndex")]
    public int DrawCallIndex { get; set; }

    /// <summary>
    /// Index of the mesh within the model.
    /// </summary>
    [DMProperty(name: "m_nMeshIndex")]
    public int MeshIndex { get; set; }

    /// <summary>
    /// The painted values, one per vertex of the draw call.
    /// </summary>
    [DMProperty(name: "m_pVertexData")]
    public DmxModel.DmeVertexData VertexData { get; init; } = [];
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
public class CMapInstance : EntityIONode
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
/// References another map file and places its contents at this node.
/// </summary>
[CamelCaseProperties]
public class CMapPrefab : CMapInstance
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
    /// Which shadows the mesh casts: 0 all, 1 none, 2 only realtime, 3 only baked.
    /// </summary>
    public int DisableShadows { get; set; }

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

    // Only last while a mesh is being edited, and are cleared when it is loaded
    private const PolygonMeshFaceFlags TransientFaceFlags = PolygonMeshFaceFlags.Selected | PolygonMeshFaceFlags.CoveredByTile;

    private CDmePolygonMesh? meshData = [];
    private PolygonMesh? mesh;

    /// <summary>
    /// The geometry in its saved form. After loading it is in <see cref="Mesh"/> instead and this is null, except
    /// while the map is being saved.
    /// </summary>
    public CDmePolygonMesh? MeshData
    {
        get => meshData;
        init => meshData = value;
    }

    /// <summary>
    /// The geometry of a mesh that was loaded, see <see cref="PostLoadFromFile"/>.
    /// </summary>
    [DMIgnore]
    public PolygonMesh Mesh => mesh ??= new();

    /// <summary>
    /// Converts the saved geometry into its runtime form: <see cref="MeshData"/> is copied into <see cref="Mesh"/>
    /// and its elements are then removed from the map. Invalid parts of the mesh are removed, and the streams and
    /// tangents every mesh is expected to have are filled in.
    /// </summary>
    public override void PostLoadFromFile()
    {
        base.PostLoadFromFile();

        if (meshData is not { } stored)
        {
            return;
        }

        var loaded = new PolygonMesh();
        stored.CopyToPolyMesh(loaded);

        mesh = loaded;
        meshData = null;
        stored.Owner?.AllElements.RemoveUnreferenced(stored);

        loaded.RemoveInvalidComponents();
        loaded.RemoveIsolatedVertices();
        loaded.InitFaceSubdivisionLevels();

        // A mesh without edge flags predates smoothing by angle, and all of its edges are hard
        if (!loaded.HasStoredStream(MeshComponentType.Edge, "flags"))
        {
            SmoothingAngle = 0f;
        }

        loaded.SetSmoothingAngle(SmoothingAngle);

        var hadTangents = loaded.HasStoredStream(MeshComponentType.FaceVertex, "tangent");

        loaded.EnsureStandardStreams();
        loaded.ClearFaceFlags(TransientFaceFlags);

        if (!hadTangents)
        {
            loaded.ComputeFaceVertexTangents();
        }
    }

    /// <summary>
    /// Rebuilds <see cref="MeshData"/> from <see cref="Mesh"/>, so that the map is saved with it.
    /// </summary>
    public override void PreSaveToFile()
    {
        base.PreSaveToFile();

        // A mesh that was never loaded still has its saved data, so there is nothing to rebuild
        if (mesh == null)
        {
            return;
        }

        if (meshData == null)
        {
            meshData = new CDmePolygonMesh { Name = "meshData" };

            // The map only writes elements that belong to it
            Owner?.ImportElement(meshData, Datamodel.Datamodel.ImportRecursionMode.Recursive, Datamodel.Datamodel.ImportOverwriteMode.None);
        }

        meshData.CopyFromPolyMesh(mesh);
    }

    /// <summary>
    /// Removes <see cref="MeshData"/> again after the map is saved.
    /// </summary>
    public override void PostSaveToFile()
    {
        base.PostSaveToFile();

        if (mesh == null || meshData is not { } stored)
        {
            return;
        }

        meshData = null;
        stored.Owner?.AllElements.RemoveUnreferenced(stored);
    }

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
public partial class CDmePolygonMesh : DMElement
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
/// Tells how a subdivided face fills a stream of its corners for the points of its patches.
/// </summary>
[CamelCaseProperties]
public class CDmePolygonMeshSubdivisiondataBinding : DMElement
{
    /// <summary>
    /// Data type of the subdivision stream the values are read from, -1 for none.
    /// </summary>
    public int TargetDataType { get; set; } = -1;

    /// <summary>
    /// Index of that stream among the subdivision streams of its data type, -1 for none.
    /// </summary>
    public int TargetStreamIndex { get; set; } = -1;

    /// <summary>
    /// Where the values come from: 0 the subdivision stream, anything else is worked out from the mesh.
    /// </summary>
    public int StreamSourceType { get; set; }
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
    /// How a subdivided face fills this stream for its points, a <see cref="CDmePolygonMeshSubdivisiondataBinding"/>, or null.
    /// </summary>
    public DMElement? SubdivisionBinding { get; init; }

    /// <summary>
    /// An int, vector2, vector3, or vector4 array: <see cref="Datamodel.IntArray"/>, <see cref="Datamodel.Vector2Array"/>,
    /// <see cref="Datamodel.Vector3Array"/> or <see cref="Datamodel.Vector4Array"/>.
    /// </summary>
    public System.Collections.IList? Data { get; init; }
}
