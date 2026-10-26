using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes;
using DMElement = Datamodel.Element;

namespace ValveResourceFormat.IO;

/// <summary>
/// A node of a map where it ends up in the world, once the groups, instances and prefabs above it are
/// expanded. A node an instance or prefab places shows up once per placement.
/// </summary>
public sealed class PlacedMapNode
{
    /// <summary>Gets the map the node is stored in, which for a node of a prefab is the prefab's map.</summary>
    public required MapDocument Document { get; init; }

    /// <summary>Gets the node.</summary>
    public required DMElement Node { get; init; }

    /// <summary>
    /// Gets the map that was walked, the one the node ends up in. For a node of a prefab it holds the
    /// prefab rather than the node.
    /// </summary>
    public MapDocument? RootDocument { get; init; }

    /// <summary>
    /// Gets the move the instances and prefabs above the node add to where it is stored, without scale.
    /// Identity for a node the map places itself.
    /// </summary>
    public Matrix4x4 InstanceTransform { get; init; } = Matrix4x4.Identity;

    /// <summary>Gets the transform from the node's own space to the world, with its scale.</summary>
    public Matrix4x4 Transform => MapDocument.GetNodeTransform(Node) * InstanceTransform;

    /// <summary>Gets the transform from the node's own space to the world, without its scale.</summary>
    public Matrix4x4 RigidTransform => MapDocument.GetNodeRigidTransform(Node) * InstanceTransform;

    /// <summary>Gets the tint of the instances the node was placed through, multiplied together.</summary>
    public Vector4 Tint { get; init; } = Vector4.One;

    /// <summary>Gets the placed node this one is a child of, such as the entity a mesh belongs to.</summary>
    public PlacedMapNode? Parent { get; init; }

    /// <summary>Gets the name of the world layer the node is in, when it is in one.</summary>
    public string? WorldLayer { get; init; }

    /// <summary>Gets whether an instance or prefab placed the node, rather than the map itself.</summary>
    public bool IsInstanced { get; init; }

    /// <summary>Gets how many prefabs deep the node is placed, zero for a node of the map itself.</summary>
    public int PrefabDepth { get; init; }

    /// <summary>
    /// Gets the node IDs of the instances and prefabs the node was placed through, outermost first, each prefab
    /// followed by the world of its map. Empty for a node the map places itself.
    /// </summary>
    public IReadOnlyList<int> InstancePath { get; init; } = [];

    /// <summary>
    /// Gets <see cref="InstancePath"/> followed by the ID of the node itself, which the editor names what it bakes for
    /// this placement of the node by.
    /// </summary>
    public IReadOnlyList<int> NodeIdPath => Node is MapNode node ? [.. InstancePath, node.NodeID] : InstancePath;

    /// <summary>
    /// Builds the keyvalues the entity spawns with where it was placed, the way a compiled map stores them:
    /// its own keyvalues, where it is placed, and its outputs.
    /// </summary>
    /// <returns>The keyvalues, or <see langword="null"/> when the node is not an entity with a classname.</returns>
    public EntityLump.Entity? CreateEntity()
    {
        if (Node is not BaseEntity mapEntity || mapEntity.EntityProperties.Get<string>("classname") is not { Length: > 0 })
        {
            return null;
        }

        var entity = new EntityLump.Entity { ParentLump = SourceMapEntityLump.Instance };

        foreach (var (key, value) in mapEntity.EntityProperties)
        {
            if (value is string text)
            {
                entity[key.ToLowerInvariant()] = text;
            }
        }

        WritePlacementKeys(entity);

        var connections = new List<EntityLump.Connection>(mapEntity.Connections.Count);

        foreach (var connection in mapEntity.Connections)
        {
            connections.Add(new EntityLump.Connection
            {
                SourceEntity = entity,
                OutputName = connection.OutputName,
                InputName = connection.InputName,
                TargetName = connection.TargetName,
                OverrideParam = connection.OverrideParam,
                Delay = connection.Delay,
                TimesToFire = connection.TimesToFire,
                TargetType = connection.TargetType,
            });
        }

        if (connections.Count > 0)
        {
            entity.Connections = connections;
        }

        return entity;
    }

    /// <summary>
    /// Writes where the entity is placed into keyvalues it spawns with: "origin", "angles" and "scales", and for a
    /// path the keys its nodes are flattened into.
    /// </summary>
    /// <param name="entity">The keyvalues.</param>
    public void WritePlacementKeys(EntityLump.Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (Node is not BaseEntity mapEntity)
        {
            return;
        }

        // Instances and prefabs only move and turn what they place, so the scales stay the node's own
        var rigidTransform = RigidTransform;
        var angles = IsInstanced
            ? EntityTransformHelper.ToEulerAngles(Quaternion.CreateFromRotationMatrix(rigidTransform))
            : new Vector3(mapEntity.Angles.Pitch, mapEntity.Angles.Yaw, mapEntity.Angles.Roll);

        entity["origin"] = FormatVector(rigidTransform.Translation);
        entity["angles"] = FormatVector(angles);
        entity["scales"] = FormatVector(mapEntity.Scales);

        if (mapEntity is CMapPath path)
        {
            AddPathKeys(entity, path);
        }
    }

    // The map compiler flattens the nodes of a path into keys of the path itself, in the space of the
    // path. Handles reach a third of the way along their segment, unless a node keeps its own.
    private static void AddPathKeys(EntityLump.Entity entity, CMapPath path)
    {
        var nodes = path.Children.OfType<CMapPathNode>().ToList();

        if (nodes.Count == 0 || !Matrix4x4.Invert(MapDocument.GetNodeTransform(path), out var worldToPath))
        {
            return;
        }

        var count = nodes.Count;
        var pathNodes = new StringBuilder();
        var pins = new StringBuilder();
        var radiusScales = new StringBuilder();
        var colors = new StringBuilder();
        var names = new StringBuilder();

        // A closed path ends on its first node again
        for (var i = 0; i < (path.ClosedLoop && count >= 3 ? count + 1 : count); i++)
        {
            var index = i % count;
            var node = nodes[index];
            var position = node.Origin;
            var previous = index > 0 ? nodes[index - 1].Origin : path.ClosedLoop ? nodes[^1].Origin : position;
            var next = index < count - 1 ? nodes[index + 1].Origin : path.ClosedLoop ? nodes[0].Origin : position;
            var inThird = Vector3.Distance(position, previous) / 3f;
            var outThird = Vector3.Distance(next, position) / 3f;

            var (inType, outType) = (MapPathInterpolationType)path.InterpolationType switch
            {
                MapPathInterpolationType.Linear => (MapPathTangentType.Linear, MapPathTangentType.Linear),
                MapPathInterpolationType.Spline => (MapPathTangentType.Spline, MapPathTangentType.Spline),
                _ => ((MapPathTangentType)node.InTangentType, (MapPathTangentType)node.OutTangentType),
            };

            var inTangent = inType switch
            {
                MapPathTangentType.Linear => MathUtils.SafeNormalize(previous - position) * inThird,
                MapPathTangentType.Spline => MathUtils.SafeNormalize(previous - next) * inThird,
                MapPathTangentType.Direction => MathUtils.SafeNormalize(node.InTangent) * inThird,
                _ => node.InTangent,
            };

            var outTangent = outType switch
            {
                MapPathTangentType.Linear => MathUtils.SafeNormalize(next - position) * outThird,
                MapPathTangentType.Spline => MathUtils.SafeNormalize(next - previous) * outThird,
                MapPathTangentType.Direction => MathUtils.SafeNormalize(node.OutTangent) * outThird,
                _ => node.OutTangent,
            };

            AppendVector(pathNodes, Vector3.Transform(position, worldToPath));
            AppendVector(pathNodes, Vector3.TransformNormal(inTangent, worldToPath));
            AppendVector(pathNodes, Vector3.TransformNormal(outTangent, worldToPath));

            pins.Append(node.PinEnabled ? "1 " : "0 ");
            radiusScales.Append(CultureInfo.InvariantCulture, $"{node.RadiusScale:R} ");
            AppendVector(colors, new Vector3(node.TintColor.R, node.TintColor.G, node.TintColor.B) / 255f);

            if (i < count && node.PathNodeName.Length > 0)
            {
                names.Append(CultureInfo.InvariantCulture, $"{i}: {node.PathNodeName};");
            }
        }

        entity["pathnodes"] = pathNodes.ToString().TrimEnd();
        entity["pathnodepinsenabled"] = pins.ToString().TrimEnd();
        entity["pathnoderadiusscales"] = radiusScales.ToString().TrimEnd();
        entity["pathnodecolors"] = colors.ToString().TrimEnd();
        entity["closed_loop"] = path.ClosedLoop ? "1" : "0";

        if (names.Length > 0)
        {
            entity["pathnodenames"] = names.ToString();
        }
    }

    private static void AppendVector(StringBuilder builder, Vector3 value)
        => builder.Append(CultureInfo.InvariantCulture, $"{value.X:R} {value.Y:R} {value.Z:R} ");

    private static string FormatVector(Vector3 value)
        => string.Create(CultureInfo.InvariantCulture, $"{value.X:R} {value.Y:R} {value.Z:R}");

    // The entities of a map that was never compiled belong to no lump
    private sealed class SourceMapEntityLump : EntityLump
    {
        public static readonly SourceMapEntityLump Instance = new();

        [SetsRequiredMembers]
        private SourceMapEntityLump()
        {
            Resource = null!;

            var data = new KVObject();
            data.Add("m_name", string.Empty);
            data.Add("m_childLumps", KVObject.Array());
            data.Add("m_entityKeyValues", KVObject.Array());
            Data = data;
        }
    }
}

/// <summary>
/// Walks the node tree of a map and yields what it places in the world: groups are flattened, and
/// instances and prefabs are replaced by what they place.
/// </summary>
public sealed class MapNodeWalker
{
    private readonly record struct WalkState(Matrix4x4 InstanceTransform, Vector4 Tint, PlacedMapNode? Parent, string? WorldLayer, bool IsInstanced, int PrefabDepth)
    {
        public IReadOnlyList<int> InstancePath { get; init; } = [];
    }

    private readonly Func<MapDocument, string, MapDocument?>? prefabLoader;
    private readonly HashSet<DMElement> activeTargets = [];
    private MapDocument? rootDocument;

    /// <summary>Gets or sets whether hidden nodes are walked too.</summary>
    public bool IncludeHidden { get; init; }

    /// <summary>Initializes a new instance of the <see cref="MapNodeWalker"/> class.</summary>
    /// <param name="prefabLoader">
    /// Loads the map a prefab names, given the map holding the prefab and the path it names. Prefabs place
    /// nothing without it.
    /// </param>
    public MapNodeWalker(Func<MapDocument, string, MapDocument?>? prefabLoader = null)
    {
        this.prefabLoader = prefabLoader;
    }

    /// <summary>Walks everything a map places.</summary>
    /// <param name="document">The map.</param>
    public IEnumerable<PlacedMapNode> Walk(MapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        rootDocument = document;

        return WalkChildren(document, document.World, new WalkState(Matrix4x4.Identity, Vector4.One, null, null, false, 0));
    }

    private IEnumerable<PlacedMapNode> WalkChildren(MapDocument document, MapNode parent, WalkState state)
    {
        foreach (var child in parent.Children)
        {
            if (child == null)
            {
                continue;
            }

            foreach (var placedNode in WalkNode(document, child, state))
            {
                yield return placedNode;
            }
        }
    }

    private IEnumerable<PlacedMapNode> WalkNode(MapDocument document, DMElement node, WalkState state)
    {
        if (!IncludeHidden && document.IsHidden(node))
        {
            return [];
        }

        return node switch
        {
            CMapPrefab prefab => WalkPrefab(document, prefab, state),
            CMapInstance instance => WalkInstance(document, instance, state),
            CMapWorldLayer layer => WalkChildren(document, layer, state with { WorldLayer = layer.WorldLayerName }),
            CMapGroup group => WalkChildren(document, group, state),
            _ => WalkPlaced(document, node, state),
        };
    }

    private IEnumerable<PlacedMapNode> WalkPlaced(MapDocument document, DMElement node, WalkState state)
    {
        var placedNode = new PlacedMapNode
        {
            Document = document,
            Node = node,
            RootDocument = rootDocument,
            InstanceTransform = state.InstanceTransform,
            Tint = state.Tint,
            Parent = state.Parent,
            WorldLayer = state.WorldLayer,
            IsInstanced = state.IsInstanced,
            PrefabDepth = state.PrefabDepth,
            InstancePath = state.InstancePath,
        };

        yield return placedNode;

        if (node is not MapNode mapNode)
        {
            yield break;
        }

        foreach (var child in WalkChildren(document, mapNode, state with { Parent = placedNode }))
        {
            yield return child;
        }
    }

    // The transform of the target and of everything under it is where they are in the world, so what the
    // instance adds is the move from where the target is to where the instance is. Nested instances
    // multiply these moves together, innermost first. The move is rigid: the scale of the instance and of
    // its target take no part in it.
    private IEnumerable<PlacedMapNode> WalkInstance(MapDocument document, CMapInstance instance, WalkState state)
    {
        if (instance.Target is not MapNode { Stub: false } target
            || (!IncludeHidden && document.IsHidden(target))
            || !activeTargets.Add(target))
        {
            yield break;
        }

        try
        {
            if (!Matrix4x4.Invert(MapDocument.GetNodeRigidTransform(target), out var targetToLocal))
            {
                yield break;
            }

            var inner = state with
            {
                InstanceTransform = targetToLocal * MapDocument.GetNodeRigidTransform(instance) * state.InstanceTransform,
                Tint = state.Tint * GetTint(instance),
                IsInstanced = true,
                InstancePath = [.. state.InstancePath, instance.NodeID],
            };

            foreach (var placedNode in WalkChildren(document, target, inner))
            {
                yield return placedNode;
            }
        }
        finally
        {
            activeTargets.Remove(target);
        }
    }

    // A prefab is an instance of the world of another map, which sits at the origin
    private IEnumerable<PlacedMapNode> WalkPrefab(MapDocument document, CMapPrefab prefab, WalkState state)
    {
        if (string.IsNullOrEmpty(prefab.TargetMapPath) || (state.PrefabDepth > 0 && !prefab.LoadIfNested))
        {
            yield break;
        }

        var prefabDocument = prefabLoader?.Invoke(document, prefab.TargetMapPath);

        if (prefabDocument == null || !activeTargets.Add(prefabDocument.World))
        {
            yield break;
        }

        try
        {
            var inner = state with
            {
                InstanceTransform = MapDocument.GetNodeRigidTransform(prefab) * state.InstanceTransform,
                Tint = state.Tint * GetTint(prefab),
                IsInstanced = true,
                PrefabDepth = state.PrefabDepth + 1,
                InstancePath = [.. state.InstancePath, prefab.NodeID, prefabDocument.World.NodeID],
            };

            foreach (var placedNode in WalkChildren(prefabDocument, prefabDocument.World, inner))
            {
                yield return placedNode;
            }
        }
        finally
        {
            activeTargets.Remove(prefabDocument.World);
        }
    }

    private static Vector4 GetTint(CMapInstance instance)
    {
        var color = instance.TintColor;

        return new Vector4(color.R, color.G, color.B, color.A) / 255f;
    }
}
