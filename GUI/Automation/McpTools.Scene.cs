#if DEBUG
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GUI.Forms;
using GUI.Types.GLViewers;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Editor.Picking;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using DepthRange = ValveResourceFormat.Renderer.Renderer.DepthRange;

namespace GUI.Automation;

/// <summary>Tools that find and inspect the entities and scene nodes of a 3D tab.</summary>
internal sealed partial class McpTools
{
    public enum PickAction
    {
        None,
        Select,
        Add,
        Open,
        Clear,
    }

    /// <summary>The entities of a map by id, which they keep while the tab is open.</summary>
    private sealed class EntityTable
    {
        public readonly List<(EntityLump.Entity Data, SpawnGroup? Group)> Entities = [];
        public readonly Dictionary<EntityLump.Entity, int> Ids = new(ReferenceEqualityComparer.Instance);

        /// <summary>Adds the entities not added before.</summary>
        public void Add(IEnumerable<EntityLump.Entity> entities, SpawnGroup? group)
        {
            foreach (var entity in entities)
            {
                if (Ids.TryAdd(entity, Entities.Count))
                {
                    Entities.Add((entity, group));
                }
            }
        }
    }

    private readonly ConditionalWeakTable<GLWorldViewer, EntityTable> EntityTables = [];

    // A scene numbers its nodes by their place in its lists, which shifts as nodes come and go
    private readonly IdRegistry<SceneNode> NodeIds = new();

    /// <summary>An entity with the data find_entities matches, read between frames so the matching can run afterwards.</summary>
    /// <param name="Position">Where it renders, from <see cref="EntityPosition"/>.</param>
    private sealed record EntityRow(int Id, EntityLump.Entity Data, SpawnGroup? Group, BaseEntity? Instance, Vector3 Position);

    /// <summary>
    /// Every entity of the map and of the spawn groups it loaded, such as its 3D sky, indexed by id.
    /// The map's come first, so their ids are their index in it. Call between frames.
    /// </summary>
    private EntityTable Entities(GLWorldViewer viewer)
    {
        var world = viewer.LoadedWorld ?? throw new ToolException("This tab shows world geometry without a map, so it has no entities.");
        var table = EntityTables.GetOrCreateValue(viewer);

        table.Add(world.Entities, null);

        foreach (var group in viewer.Renderer.SpawnGroups.ToArray())
        {
            table.Add(group.Entities, group);
        }

        return table;
    }

    /// <summary>The entities of a map tab, or null for a tab without a map. Call between frames.</summary>
    private EntityTable? MapEntities(GLSceneViewer viewer)
    {
        if (viewer is GLWorldViewer { LoadedWorld: not null } world)
        {
            return Entities(world);
        }

        return null;
    }

    private static (EntityLump.Entity Data, SpawnGroup? Group) EntityById(EntityTable entities, int id)
    {
        if (id < 0 || id >= entities.Entities.Count)
        {
            throw new ToolException($"No entity with id {id}. find_entities lists the entities.");
        }

        return entities.Entities[id];
    }

    /// <summary>The keyvalues of an entity by id, or null for no id.</summary>
    private static EntityLump.Entity? EntityData(EntityTable? entities, int? id)
    {
        if (id == null)
        {
            return null;
        }

        if (entities == null)
        {
            throw new ToolException("'entity' needs a map tab.");
        }

        return EntityById(entities, id.Value).Data;
    }

    private static int? EntityId(EntityTable? entities, EntityLump.Entity? data)
    {
        if (entities == null || data == null)
        {
            return null;
        }

        if (entities.Ids.TryGetValue(data, out var id))
        {
            return id;
        }

        return null;
    }

    private static BaseEntity? Spawned(GLSceneViewer viewer, EntityLump.Entity data)
        => viewer.Renderer.EntitySystem.Entities.ToArray().FirstOrDefault(entity => ReferenceEquals(entity.Data, data));

    /// <summary>Where an entity renders. A 3D sky entity appears where its sky is seen.</summary>
    private static Vector3 EntityPosition(EntityLump.Entity data, SpawnGroup? group, BaseEntity? instance)
    {
        if (instance != null)
        {
            return Vector3.Transform(instance.Transform.Translation, instance.Scene.ToViewerWorld);
        }

        var origin = data.GetVector3Property("origin");

        if (group == null)
        {
            return origin;
        }

        return group.EntityOriginToWorld(origin);
    }

    /// <summary>An entity by id, with where it renders. Call between frames.</summary>
    private static EntityRow EntityRowOf(EntityTable entities, int id, BaseEntity? instance)
    {
        var (data, group) = EntityById(entities, id);
        return new EntityRow(id, data, group, instance, EntityPosition(data, group, instance));
    }

    private static object DescribeEntity(EntityRow entity, string[]? fields = null)
    {
        var data = entity.Data;

        return new
        {
            entity.Id,
            Classname = data.GetStringProperty("classname"),
            Targetname = data.TargetName,
            SpawnGroup = entity.Group?.MapName,
            Sky = Flag(entity.Group?.WorldGroup != null),
            entity.Position,
            Fields = fields == null ? null : data.Children.Where(pair => fields.Contains(pair.Key)).ToDictionary(static pair => pair.Key, static pair => KeyValueText(pair.Value)),
        };
    }

    /// <summary>A keyvalue as the entity properties show it.</summary>
    private static string KeyValueText(KVObject value) => EntityInfoControl.BareText(value) ?? EntityLump.StringifyValue(value);

    [Tool("Search the entities of a map, its 3D sky and the other spawn groups it loaded. Text filters are case insensitive regular expressions. They match anywhere unless anchored with ^ and $. Positions are where each entity renders. Returns 'next_offset' when more entities match.")]
    private async Task<object> FindEntities(
        GLWorldViewer viewer,
        [Description("Classname to match.")] string? classname = null,
        [Description("Targetname to match.")] string? targetname = null,
        [Description("Only entities with this exact key.")] string? key = null,
        [Description("Value to match, of 'key' or of any keyvalue.")] string? value = null,
        [Description("True for only triggers, false for none.")] bool? trigger = null,
        [Description("True for only 3D sky entities, false for none.")] bool? sky = null,
        [Description("Only entities within 'radius' of this world point.")] Vector3? near = null,
        [Description("Distance from 'near', in world units.")][Range(0, double.MaxValue)] float radius = 512f,
        [Description("Exact keys of keyvalues to include with each match.")] string[]? fields = null,
        [Description("Matches to skip, from an earlier 'next_offset'.")][Range(0, int.MaxValue)] int offset = 0,
        [Description("Maximum matches to return.")][Range(1, int.MaxValue)] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var classnamePattern = Pattern(classname);
        var targetnamePattern = Pattern(targetname);
        var valuePattern = Pattern(value);

        // Only the live state is read between frames. The keyvalues from the map load are matched afterwards, so rendering does not wait
        var entities = await WithGl(viewer, () => EntityRows(viewer), cancellationToken).ConfigureAwait(false);
        var matches = new List<EntityRow>();

        foreach (var entity in entities)
        {
            var data = entity.Data;

            if (sky != null && (entity.Group?.WorldGroup != null) != sky)
            {
                continue;
            }

            if (!Matches(classnamePattern, data.GetStringProperty("classname")))
            {
                continue;
            }

            if (!Matches(targetnamePattern, data.TargetName))
            {
                continue;
            }

            if ((key != null || valuePattern != null) && !HasKeyValue(data, key, valuePattern))
            {
                continue;
            }

            if (trigger != null && IsTrigger(data, entity.Instance) != trigger)
            {
                continue;
            }

            if (near != null && Vector3.Distance(entity.Position, near.Value) > radius)
            {
                continue;
            }

            matches.Add(entity);
        }

        return new
        {
            Entities = matches.Skip(offset).Take(limit).Select(entity => DescribeEntity(entity, fields)).ToList(),
            Total = matches.Count,
            NextOffset = offset + limit < matches.Count ? offset + limit : (int?)null,
        };
    }

    /// <summary>Every entity of a map with where it renders, and the entity it spawned as. Call between frames.</summary>
    private List<EntityRow> EntityRows(GLWorldViewer viewer)
    {
        var spawned = new Dictionary<EntityLump.Entity, BaseEntity>(ReferenceEqualityComparer.Instance);

        foreach (var instance in viewer.Renderer.EntitySystem.Entities.ToArray())
        {
            if (instance.Data != null)
            {
                spawned.TryAdd(instance.Data, instance);
            }
        }

        var entities = Entities(viewer).Entities;
        var rows = new List<EntityRow>(entities.Count);

        for (var id = 0; id < entities.Count; id++)
        {
            var (data, group) = entities[id];
            var instance = spawned.GetValueOrDefault(data);

            rows.Add(new EntityRow(id, data, group, instance, EntityPosition(data, group, instance)));
        }

        return rows;
    }

    /// <summary>Whether an entity has a keyvalue with this key, or any key when null, whose value matches.</summary>
    private static bool HasKeyValue(EntityLump.Entity data, string? key, Regex? valuePattern)
    {
        foreach (var pair in data.Children)
        {
            if (key != null && pair.Key != key)
            {
                continue;
            }

            if (valuePattern == null || valuePattern.IsMatch(KeyValueText(pair.Value)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTrigger(EntityLump.Entity data, BaseEntity? instance)
        => instance?.IsTrigger == true || data.GetStringProperty("classname")?.StartsWith("trigger_", StringComparison.OrdinalIgnoreCase) == true;

    [Tool("Describe one entity: its keyvalues and outputs, where it renders, spawn group, the class it spawned as with its live transform, and its scene nodes.")]
    private object GetEntity(GLWorldViewer viewer, [Description("Entity id from find_entities or pick.")] int id)
    {
        var entities = Entities(viewer);
        var data = EntityById(entities, id).Data;
        var instance = Spawned(viewer, data);

        return new
        {
            Entity = DescribeEntity(EntityRowOf(entities, id, instance)),
            Keyvalues = data.Children.Where(static pair => pair.Key is not ("classname" or "targetname")).ToDictionary(static pair => pair.Key, static pair => KeyValueText(pair.Value)),

            // The form Hammer writes them in: output target:input:parameter:delay:times
            Outputs = data.Connections?.Select(static c => string.Create(CultureInfo.InvariantCulture,
                $"{c.OutputName} {c.TargetName}:{c.InputName}:{c.OverrideParam}:{c.Delay}:{c.TimesToFire}")).ToList(),

            Spawned = instance == null ? null : new
            {
                Class = instance.GetType().Name,
                instance.Angles,
                Velocity = instance.Velocity == Vector3.Zero ? (Vector3?)null : instance.Velocity,
                NotDrawn = Flag(!instance.IsDrawn),
                Trigger = Flag(instance.IsTrigger),
                Removed = Flag(instance.IsRemoved),
            },
            Nodes = AllNodes(viewer).Where(node => ReferenceEquals(node.EntityData, data)).Select(node => DescribeNode(viewer, entities, node)).ToList(),
        };
    }

    /// <summary>A node of the viewer's scenes by an id from <see cref="NodeIds"/>. Call between frames.</summary>
    private SceneNode NodeById(GLSceneViewer viewer, int id)
    {
        var node = NodeIds.Find(id);

        if (node == null || !viewer.Renderer.Scenes.Contains(node.Scene) || !node.Scene.AllNodes.Contains(node))
        {
            throw NoNode(id);
        }

        return node;
    }

    /// <summary>A node of <paramref name="sceneNodes"/>, every node of the viewer's scenes, by an id from <see cref="NodeIds"/>.</summary>
    private SceneNode NodeById(HashSet<SceneNode> sceneNodes, int id)
    {
        var node = NodeIds.Find(id);

        if (node == null || !sceneNodes.Contains(node))
        {
            throw NoNode(id);
        }

        return node;
    }

    private static ToolException NoNode(int id) => new($"No node with id {id} in this tab. A node id is valid until the node leaves the scene.");

    /// <summary>Why a node is not drawn, or null when it is.</summary>
    private static string? HiddenReason(SceneNode node)
    {
        if (!node.Visible)
        {
            return "not_drawn";
        }

        if (node is PhysSceneNode { Enabled: false })
        {
            return "physics_group_off";
        }

        if (node.LayerEnabled)
        {
            return null;
        }

        return "layer_off";
    }

    private static SpawnGroup? SpawnGroupOf(GLSceneViewer viewer, Scene scene) => viewer.Renderer.SpawnGroups.FirstOrDefault(group => group.Scene == scene);

    /// <param name="entities">The entities of the viewer's map from <see cref="MapEntities"/>.</param>
    private object DescribeNode(GLSceneViewer viewer, EntityTable? entities, SceneNode node)
    {
        var bounds = node.BoundingBox.Transform(node.Scene.ToViewerWorld);
        var group = SpawnGroupOf(viewer, node.Scene);

        return new
        {
            Id = NodeIds.IdOf(node),
            Type = node.GetType().Name.Replace("SceneNode", string.Empty, StringComparison.Ordinal),
            node.Name,
            Entity = EntityId(entities, node.EntityData),
            Layer = node.LayerName,
            PhysicsGroup = (node as PhysSceneNode)?.PhysGroupName,
            SpawnGroup = group?.MapName,
            Sky = Flag(group?.WorldGroup != null),
            Hidden = HiddenReason(node),
            Materials = node switch
            {
                MeshCollectionNode meshes => meshes.RenderableMeshes.SelectMany(static mesh => mesh.DrawCalls).Select(static draw => draw.Material.Material.Name).Distinct().ToList(),
                SceneAggregate.Fragment fragment => [fragment.DrawCall.Material.Material.Name],
                _ => null,
            },
            bounds.Center,
            bounds.Size,
        };
    }

    [Tool("Identify what a 3D tab draws at a pixel: its node, the mesh and materials there, and its entity. It also returns the world position and distance from the depth buffer. Pixels start at the top left of the render area. get_info gives the size of that area.", Redraws = true)]
    private async Task<object> Pick(
        GLSceneViewer viewer,
        [Description("Pixel X in the render area. Defaults to the centre.")] int? x = null,
        [Description("Pixel Y in the render area. Defaults to the centre.")] int? y = null,
        [Description("select replaces the selection, as a click does. add toggles it in the selection, as Ctrl+click does. open opens it in its own tab, as Ctrl+double click does in a map, and returns that tab. clear removes the selection and reads no pixel. Until then, screenshots show the outline of a selected node, and the debug geometry of a selected light probe or envmap.")] PickAction action = PickAction.None,
        CancellationToken cancellationToken = default)
    {
        if (action == PickAction.Clear)
        {
            await WithGl(viewer, viewer.Selection.Clear, cancellationToken).ConfigureAwait(false);
            return new { Cleared = true };
        }

        var picker = viewer.Picker ?? throw new ToolException("This viewer has no picker.");
        var width = picker.Texture.Width;
        var height = picker.Texture.Height;
        var pixelX = x ?? width / 2;
        var pixelY = y ?? height / 2;

        if (pixelX < 0 || pixelX >= width || pixelY < 0 || pixelY >= height)
        {
            throw new ToolException($"Pixel ({pixelX}, {pixelY}) is outside the {width}x{height} render area.");
        }

        if (action == PickAction.Open)
        {
            var open = new PickRequest(pixelX, pixelY, PickIntent.Open);
            return await OpenTab(ct => RequestPick(viewer, picker, open, ct), "what is under that pixel", cancellationToken).ConfigureAwait(false);
        }

        var request = action switch
        {
            PickAction.Select => new PickRequest(pixelX, pixelY, PickIntent.Select),
            PickAction.Add => new PickRequest(pixelX, pixelY, PickIntent.Select, PickModifiers.Control),
            _ => new PickRequest(pixelX, pixelY, PickIntent.Query),
        };

        var result = await RequestPick(viewer, picker, request, cancellationToken).ConfigureAwait(false);

        var described = await WithGl<object>(viewer, () =>
        {
            if (viewer.Renderer.FindPickedNode(result) is not { } node)
            {
                return new { Hit = false };
            }

            var position = Unproject(viewer.Renderer, node.Scene, pixelX / (float)width, pixelY / (float)height, result.Depth);
            float? distance = null;

            if (position != null)
            {
                distance = Vector3.Distance(viewer.Renderer.Camera.Location, position.Value);
            }

            return new
            {
                Hit = true,
                Position = position,
                Distance = distance,
                Node = DescribeNode(viewer, MapEntities(viewer), node),
                Mesh = DescribeMesh(node, result.Pixel.MeshId),
                Selected = action is PickAction.Select or PickAction.Add ? node.IsSelected : (bool?)null,
            };
        }, cancellationToken).ConfigureAwait(false);

        // The pick drew the frames it needed, and a query changes nothing to show
        if (action == PickAction.None)
        {
            return new UnchangedReply(described);
        }

        return described;
    }

    /// <summary>
    /// Queues a pick on the viewer's own picker, which hands the result to the viewer as a click does,
    /// and draws frames until it has. Pick redraws, so its tab is already shown.
    /// </summary>
    private static async Task<PickResult> RequestPick(GLSceneViewer viewer, ScenePicker picker, PickRequest request, CancellationToken cancellationToken)
    {
        var picked = picker.RequestAsync(request);

        using var stopDrawing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var frames = DrawShownFrames(viewer, 1, ReportedErrors.NextAsync(), stopDrawing.Token);

        // The pick ends the wait rather than the frame count: a frame that started before the frames were
        // counted can take the pick, and when it opens another tab it is the last frame this tab draws
        if (await Task.WhenAny(picked, frames).ConfigureAwait(false) == frames)
        {
            await frames.ConfigureAwait(false);
        }

        await stopDrawing.CancelAsync().ConfigureAwait(false);

        return await picked.WaitAsync(FrameTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The world point a pixel's depth puts it at, where the main camera sees it. Each view draws into
    /// its own slice of the reverse-Z depth range, so the slice says which camera to unproject through.
    /// </summary>
    private static Vector3? Unproject(ValveResourceFormat.Renderer.Renderer renderer, Scene scene, float u, float v, float depth)
    {
        var inSky = scene.WorldGroup != null;
        var range = DepthRange.Scene;
        var camera = renderer.Camera;

        if (inSky)
        {
            range = DepthRange.Sky;
            camera = renderer.SkyCamera;
        }
        else if (depth > DepthRange.Scene.Far)
        {
            range = DepthRange.Viewmodel;
            camera = renderer.ViewmodelCamera;
        }

        if (depth <= 0f || !Matrix4x4.Invert(camera.ViewProjectionMatrix, out var clipToWorld))
        {
            return null;
        }

        var point = Vector4.Transform(new Vector4(u * 2f - 1f, 1f - v * 2f, (depth - range.Near) / (range.Far - range.Near), 1f), clipToWorld);
        var position = new Vector3(point.X, point.Y, point.Z) / point.W;

        return inSky ? Vector3.Transform(position, scene.ToViewerWorld) : position;
    }

    private static object? DescribeMesh(SceneNode node, uint meshId) => node switch
    {
        SceneAggregate.Fragment fragment => new
        {
            Index = meshId,
            Materials = new[] { fragment.DrawCall.Material.Material.Name },
            Shaders = new[] { fragment.DrawCall.Material.Material.ShaderName },
        },
        MeshCollectionNode meshes when meshes.RenderableMeshes.FirstOrDefault(mesh => mesh.MeshIndex == meshId) is { } mesh => new
        {
            Index = meshId,
            mesh.Name,
            Materials = mesh.DrawCalls.Select(static draw => draw.Material.Material.Name).Distinct().ToArray(),
            Shaders = mesh.DrawCalls.Select(static draw => draw.Material.Material.ShaderName).Distinct().ToArray(),
        },
        _ => null,
    };

    [Tool("Trace a ray, or sweep a 'hull' box, against what the player collides with in a 3D tab. That is the static world and solid entities such as doors. Returns the hit position, normal, distance and entity. A hit without an entity is the world.")]
    private object Trace(
        GLSceneViewer viewer,
        [Description("Start point. Defaults to the camera position.")] Vector3? from = null,
        [Description("Direction to trace in. Defaults to the camera view direction.")] Vector3? direction = null,
        [Description("End point. Pass it instead of 'direction'.")] Vector3? to = null,
        [Description("Length of the trace along 'direction', in world units.")][Range(0, double.MaxValue, MinimumIsExclusive = true)] float maxDistance = 65536f,
        [Description("Half extents of a box to sweep instead of a ray. The player box is [16, 16, 36].")] Vector3? hull = null,
        [Description("Also stop at the enabled trigger volumes the viewer simulates.")] bool triggers = false)
    {
        if (to != null && direction != null)
        {
            throw new ArgumentException("Pass 'direction' or 'to', not both.");
        }

        if (direction == Vector3.Zero)
        {
            throw new ArgumentException("'direction' must not be zero.");
        }

        var sweepsBox = hull != null;
        var extents = hull.GetValueOrDefault();

        if (Vector3.Min(extents, Vector3.Zero) != Vector3.Zero)
        {
            throw new ArgumentException("'hull' half extents must not be negative.");
        }

        var camera = viewer.Input.Camera;
        var start = from ?? camera.Location;
        var forward = direction ?? EntityTransformHelper.EulerAnglesToForwardDirection(camera.GetQAngle());
        var end = to ?? start + MathUtils.SafeNormalize(forward) * maxDistance;

        var entities = viewer.Renderer.EntitySystem;
        Rubikon.TraceResult hit;

        if (sweepsBox)
        {
            hit = entities.PhysicsWorld.TraceAABB(start, end, extents, Rubikon.Cs2PlayerCollisionFilter, detectStartSolid: true);
            entities.TraceAABB(start, end, extents, detectStartSolid: true, ref hit);
        }
        else
        {
            hit = entities.PhysicsWorld.TraceRay(start, end, Rubikon.Cs2PlayerCollisionFilter);
            entities.TraceRay(start, end, Rubikon.Cs2PlayerCollisionFilter, ref hit);
        }

        if (triggers)
        {
            foreach (var trigger in entities.Entities.OfType<BaseTrigger>())
            {
                if (trigger is { IsEnabled: true, IsRemoved: false, Collider: { IsEmpty: false } collider })
                {
                    var triggerHit = sweepsBox ? collider.TraceAABB(start, end, extents, detectStartSolid: true) : collider.TraceRay(start, end, Rubikon.Cs2PlayerCollisionFilter);
                    triggerHit.HitEntity = trigger;
                    hit.MinimizeWith(triggerHit);
                }
            }
        }

        return new
        {
            hit.Hit,
            From = start,
            To = end,
            Position = hit.Hit ? hit.HitPosition : (Vector3?)null,
            Normal = hit.Hit ? hit.HitNormal : (Vector3?)null,
            Distance = hit.Hit ? hit.Distance : (float?)null,
            StartSolid = Flag(hit.StartSolid),
            Entity = hit.HitEntity == null ? null : DescribeHitEntity(viewer, hit.HitEntity),
        };
    }

    /// <summary>A map entity as find_entities describes it, or the class of one the viewer created itself.</summary>
    private object DescribeHitEntity(GLSceneViewer viewer, BaseEntity entity)
    {
        var entities = MapEntities(viewer);
        var id = EntityId(entities, entity.Data);

        if (entities == null || id == null)
        {
            return new { Class = entity.GetType().Name };
        }

        return DescribeEntity(EntityRowOf(entities, id.Value, entity));
    }

    /// <summary>The nodes that <see cref="HiddenNodes"/> hid. A layer switch may show them again.</summary>
    private readonly ConditionalWeakTable<SceneNode, object?> NodesHiddenByTool = [];

    /// <summary>Whether <see cref="HiddenNodes"/> hid the node and it is still hidden. A node that shows again is removed from the table.</summary>
    private bool IsHiddenByTool(SceneNode node)
    {
        if (!NodesHiddenByTool.TryGetValue(node, out _))
        {
            return false;
        }

        if (node.LayerEnabled)
        {
            NodesHiddenByTool.Remove(node);
            return false;
        }

        return true;
    }

    [Tool("List the scene nodes of a 3D tab that this tool hid and that are still hidden. With 'hidden', hide or show nodes instead. This works as select and Delete do, but it sets the state instead of toggling it. Like Delete, it disables the layer for that node only. The node then reports layer_off, and a sidebar layer switch can show it again. This tool does not list or show nodes hidden with Delete in the viewer. It does not change nodes that a layer, a physics group or their entity hides, and reports them.", Redraws = true)]
    private object HiddenNodes(
        GLSceneViewer viewer,
        [Description("True to hide, false to show again. Omit to list the hidden nodes.")] bool? hidden = null,
        [Description("Node ids from pick or get_entity.")] int[]? nodes = null,
        [Description("Every node of this entity.")] int? entity = null,
        [Description("With 'hidden' false, every node that this tool hid.")] bool all = false)
    {
        if (hidden == null)
        {
            if (nodes != null || entity != null || all)
            {
                throw new ArgumentException("Pass 'hidden' to hide or show nodes.");
            }

            var entities = MapEntities(viewer);
            var listed = AllNodes(viewer).Where(IsHiddenByTool).Select(node => DescribeNode(viewer, entities, node)).ToList();

            return new UnchangedReply(new { Nodes = listed });
        }

        if (nodes == null && entity == null && !all)
        {
            throw new ArgumentException("Pass 'nodes', 'entity' or 'all'.");
        }

        var hide = hidden.Value;
        var data = EntityData(MapEntities(viewer), entity);
        var sceneNodes = AllNodes(viewer).ToHashSet();
        var targets = new HashSet<SceneNode>();

        foreach (var node in sceneNodes)
        {
            var isHiddenTarget = all && IsHiddenByTool(node);
            var isEntityTarget = data != null && ReferenceEquals(node.EntityData, data);

            if (isHiddenTarget || isEntityTarget)
            {
                targets.Add(node);
            }
        }

        foreach (var id in nodes ?? [])
        {
            targets.Add(NodeById(sceneNodes, id));
        }

        var changed = new List<int>();
        var unchanged = new List<object>();

        foreach (var node in targets)
        {
            var changes = hide ? HiddenReason(node) == null : IsHiddenByTool(node);

            if (changes)
            {
                node.LayerEnabled = !hide;

                if (hide)
                {
                    NodesHiddenByTool.AddOrUpdate(node, null);
                }
                else
                {
                    NodesHiddenByTool.Remove(node);
                }

                changed.Add(NodeIds.IdOf(node));
            }
            else
            {
                unchanged.Add(new { Id = NodeIds.IdOf(node), Hidden = HiddenReason(node) });
            }
        }

        var result = new
        {
            Changed = changed,
            Unchanged = unchanged,
        };

        if (changed.Count == 0)
        {
            return new UnchangedReply(result);
        }

        return result;
    }

    [Tool("Return model data that the sidebar does not show: skeleton, attachments, hitboxes, LODs, animations and referenced files. It also returns the groups and animation that its node shows. Reads a model tab, or any model node of a 3D tab, such as a map prop from pick.")]
    private object GetModelInfo(GLSceneViewer viewer, [Description("Model node id from pick or get_entity. Defaults to the model of a model tab.")] int? node = null)
    {
        ModelSceneNode modelNode;

        if (node != null)
        {
            modelNode = NodeById(viewer, node.Value) as ModelSceneNode ?? throw new ToolException($"Node {node} is not a model.");
        }
        else
        {
            modelNode = (viewer as GLModelViewer)?.modelSceneNode
                ?? throw new ToolException("This tab shows no model of its own. Pass a model 'node'.");
        }

        var model = modelNode.Model;
        var bounds = modelNode.LocalBoundingBox;

        return new
        {
            model.Name,
            Node = NodeIds.IdOf(modelNode),
            Bounds = new { bounds.Min, bounds.Max },
            Bones = model.Skeleton.Bones.Select(static bone => new { bone.Name, Parent = bone.Parent?.Name, Cloth = Flag(bone.IsProceduralCloth) }).ToList(),
            Attachments = model.Attachments.Keys.Order(StringComparer.Ordinal).ToList(),
            HitboxSets = model.HitboxSets.ToDictionary(static set => set.Key, static set => set.Value.Length),
            Lods = model.LodInfo.HasDistinctLevels ? model.LodInfo.AvailableLevels.Count : 1,
            Meshes = modelNode.RenderableMeshes.Count,
            Animations = modelNode.Animations.Count,
            FlexControllers = NonZero(model.FlexControllers.Length),
            BoneConstraints = NonZero(model.BoneConstraints.Count),
            Physics = model.GetReferencedPhysNames().ToList(),
            EmbeddedPhysics = Flag(model.GetEmbeddedPhys() != null),
            NmSkeletons = model.NmSkeletonRefs,
            AnimationGraphs = model.AnimGraph2References.Select(static graph => new { graph.Identifier, graph.GraphPath }).ToList(),
            MaterialGroup = modelNode.ActiveMaterialGroup,
            MeshGroups = modelNode.GetActiveMeshGroups(),
            Animation = modelNode.AnimationController.ActiveAnimation?.Name,
        };
    }
}
#endif
