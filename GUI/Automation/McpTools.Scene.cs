#if DEBUG
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
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
        public readonly HashSet<SpawnGroup> Groups = [];

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

    private readonly ConditionalWeakTable<GLWorldViewer, EntityTable> entityTables = [];

    // A scene numbers its nodes by their place in its lists, which shifts as nodes come and go
    private readonly IdRegistry<SceneNode> nodeIds = new();

    /// <summary>
    /// Every entity of the map and of the spawn groups it loaded, such as its 3D sky, indexed by id.
    /// The map's come first, so their ids are their index in it.
    /// </summary>
    private EntityTable Entities(GLWorldViewer viewer)
    {
        var world = viewer.LoadedWorld ?? throw new ToolException("This tab shows world geometry without a map, so it has no entities.");
        var table = entityTables.GetOrCreateValue(viewer);

        if (table.Entities.Count == 0)
        {
            table.Add(world.Entities, null);
        }

        foreach (var group in viewer.Renderer.SpawnGroups.ToArray())
        {
            if (table.Groups.Add(group))
            {
                table.Add(group.Entities, group);
            }
        }

        return table;
    }

    private (EntityLump.Entity Data, SpawnGroup? Group) EntityById(GLWorldViewer viewer, int id)
        => Entities(viewer).Entities is var entities && id >= 0 && id < entities.Count ? entities[id] : throw new ToolException($"No entity with id {id}.");

    /// <summary>The keyvalues of an entity by id, or null for no id.</summary>
    private EntityLump.Entity? EntityData(GLSceneViewer viewer, int? id)
        => id is { } entity ? EntityById(viewer as GLWorldViewer ?? throw new ToolException("'entity' needs a map."), entity).Data : null;

    private int? EntityId(GLSceneViewer viewer, EntityLump.Entity? data)
        => data != null && viewer is GLWorldViewer { LoadedWorld: not null } world && Entities(world).Ids.TryGetValue(data, out var id) ? id : null;

    private static BaseEntity? Spawned(GLSceneViewer viewer, EntityLump.Entity data)
        => viewer.Renderer.EntitySystem.Entities.ToArray().FirstOrDefault(entity => ReferenceEquals(entity.Data, data));

    /// <summary>Where an entity renders: a 3D sky entity shows up where its sky is seen.</summary>
    private static Vector3 EntityPosition(EntityLump.Entity data, SpawnGroup? group, BaseEntity? instance)
        => instance != null
            ? Vector3.Transform(instance.Transform.Translation, instance.Scene.ToViewerWorld)
            : group?.EntityOriginToWorld(data.GetVector3Property("origin")) ?? data.GetVector3Property("origin");

    private object DescribeEntity(GLWorldViewer viewer, int id, BaseEntity? instance, string[]? fields = null)
    {
        var (data, group) = EntityById(viewer, id);

        return new
        {
            Id = id,
            Classname = data.GetStringProperty("classname"),
            Targetname = data.TargetName,
            SpawnGroup = group?.MapName,
            Sky = Flag(group?.WorldGroup != null),
            Position = EntityPosition(data, group, instance),
            Fields = fields == null ? null : data.Children.Where(pair => fields.Contains(pair.Key)).ToDictionary(static pair => pair.Key, static pair => KeyValueText(pair.Value)),
        };
    }

    /// <summary>A keyvalue as get_entity shows it: a string bare, anything else in the KV3 form it serializes to.</summary>
    private static string KeyValueText(KVObject value) => value.ValueType == KVValueType.String ? (string)value : EntityLump.StringifyValue(value);

    [Tool("Search the entities of a map, its 3D sky and the other spawn groups it loaded. Text filters are case insensitive regular expressions, matched anywhere unless anchored with ^ and $. Positions are where each entity renders. Returns 'next_offset' when more match.")]
    private object FindEntities(
        GLWorldViewer viewer,
        [Description("Classname to match.")] string? classname = null,
        [Description("Targetname to match.")] string? targetname = null,
        [Description("Only entities that have this keyvalue, by exact key.")] string? key = null,
        [Description("Value to match, of 'key' or of any keyvalue.")] string? value = null,
        [Description("True for only triggers, false for none.")] bool? trigger = null,
        [Description("True for only 3D sky entities, false for none.")] bool? sky = null,
        [Description("Only entities within 'radius' of this world point.")] Vector3? near = null,
        [Description("Distance from 'near'.")][Range(0, double.MaxValue)] float radius = 512f,
        [Description("Keyvalues to include with each match, by exact key.")] string[]? fields = null,
        [Description("Matches to skip, from an earlier 'next_offset'.")][Range(0, int.MaxValue)] int offset = 0,
        [Description("Most matches to return.")][Range(1, int.MaxValue)] int limit = 50)
    {
        var (classnamePattern, targetnamePattern, valuePattern) = (Pattern(classname), Pattern(targetname), Pattern(value));
        var spawned = new Dictionary<EntityLump.Entity, BaseEntity>(ReferenceEqualityComparer.Instance);

        foreach (var instance in viewer.Renderer.EntitySystem.Entities.ToArray())
        {
            if (instance.Data != null)
            {
                spawned.TryAdd(instance.Data, instance);
            }
        }

        var matches = Entities(viewer).Entities
            .Select((entity, id) => (entity.Data, entity.Group, Id: id, Instance: spawned.GetValueOrDefault(entity.Data)))
            .Where(entity => (sky == null || (entity.Group?.WorldGroup != null) == sky)
                && classnamePattern?.IsMatch(entity.Data.GetStringProperty("classname") ?? string.Empty) != false
                && targetnamePattern?.IsMatch(entity.Data.TargetName ?? string.Empty) != false
                && ((key == null && valuePattern == null) || entity.Data.Children.Any(pair => (key == null || pair.Key == key) && valuePattern?.IsMatch(KeyValueText(pair.Value)) != false))
                && (trigger == null || ((entity.Instance?.IsTrigger ?? false) || entity.Data.GetStringProperty("classname")?.StartsWith("trigger_", StringComparison.OrdinalIgnoreCase) == true) == trigger)
                && (near == null || Vector3.Distance(EntityPosition(entity.Data, entity.Group, entity.Instance), near.Value) <= radius))
            .ToList();

        return new
        {
            Entities = matches.Skip(offset).Take(limit).Select(entity => DescribeEntity(viewer, entity.Id, entity.Instance, fields)).ToList(),
            Total = matches.Count,
            NextOffset = offset + limit < matches.Count ? offset + limit : (int?)null,
        };
    }

    [Tool("Everything about one entity: its keyvalues and outputs, where it renders, the spawn group it came in, the class it spawned as with its live transform, and its scene nodes.")]
    private object GetEntity(GLWorldViewer viewer, [Description("Entity id from find_entities or pick.")] int id)
    {
        var data = EntityById(viewer, id).Data;
        var instance = Spawned(viewer, data);

        return new
        {
            Entity = DescribeEntity(viewer, id, instance),
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
            Nodes = AllNodes(viewer).Where(node => ReferenceEquals(node.EntityData, data)).Select(node => DescribeNode(viewer, node)).ToList(),
        };
    }

    /// <summary>A node of the viewer's scenes by an id from <see cref="nodeIds"/>. Call between frames.</summary>
    private SceneNode NodeById(GLSceneViewer viewer, int id)
        => nodeIds.Find(id) is { } node && viewer.Renderer.Scenes.Contains(node.Scene) && node.Scene.AllNodes.Contains(node)
            ? node
            : throw new ToolException($"No node with id {id} in this tab. Ids from earlier calls last until the node is removed from the scene.");

    /// <summary>Why a node is not drawn, or null when it is. Delete and hidden_nodes turn off a node whose layer is on.</summary>
    private static string? HiddenReason(SceneNode node)
        => !node.Visible ? "not_drawn"
        : node is PhysSceneNode { Enabled: false } ? "physics_group_off"
        : node.LayerEnabled ? null
        : node.Scene.IsLayerEnabled(node.LayerName) ? "deleted"
        : "layer_off";

    private static bool IsDeleted(SceneNode node) => HiddenReason(node) == "deleted";

    private static SpawnGroup? SpawnGroupOf(GLSceneViewer viewer, Scene scene) => viewer.Renderer.SpawnGroups.FirstOrDefault(group => group.Scene == scene);

    private object DescribeNode(GLSceneViewer viewer, SceneNode node)
    {
        var bounds = node.BoundingBox.Transform(node.Scene.ToViewerWorld);
        var group = SpawnGroupOf(viewer, node.Scene);

        return new
        {
            Id = nodeIds.IdOf(node),
            Type = node.GetType().Name.Replace("SceneNode", string.Empty, StringComparison.Ordinal),
            node.Name,
            Entity = EntityId(viewer, node.EntityData),
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

    [Tool("Identify what is drawn at a pixel of a 3D tab, the centre by default: its node, the mesh and materials there, its entity, and the world position and distance the depth buffer puts it at. Pixels run from the top left of the render area whose size get_info reports.", Redraws = true)]
    private async Task<object> Pick(
        GLSceneViewer viewer,
        [Description("Pixel X in the render area.")] int? x = null,
        [Description("Pixel Y in the render area.")] int? y = null,
        [Description("select replaces the selection as a click does, add toggles it in the selection as Ctrl+click does, and open opens it in a tab of its own as Ctrl+double click does in a map, returning that tab. clear drops the selection without reading a pixel; the outline of a selected node, and the debug geometry of a selected light probe or envmap, stay in screenshots until then.")] PickAction action = PickAction.None,
        CancellationToken cancellationToken = default)
    {
        if (action == PickAction.Clear)
        {
            await WithGl(viewer, () => viewer.SelectedNodeRenderer?.SelectNode(null), cancellationToken).ConfigureAwait(false);
            return new { Cleared = true };
        }

        var picker = viewer.Picker ?? throw new ToolException("This viewer has no picker.");
        var (pixelX, pixelY) = (x ?? picker.Width / 2, y ?? picker.Height / 2);

        if (pixelX < 0 || pixelX >= picker.Width || pixelY < 0 || pixelY >= picker.Height)
        {
            throw new ToolException($"Pixel ({pixelX}, {pixelY}) is outside the {picker.Width}x{picker.Height} render area.");
        }

        if (action == PickAction.Open)
        {
            return await OpenTab(ct =>
            {
                // The frame that picks opens the tab before it ends, and the map stops drawing once it has
                picker.RequestNextFrame(pixelX, pixelY, PickingTexture.PickingIntent.Open);
                return RenderFrames(viewer, 1, ct);
            }, "what is under that pixel", null, cancellationToken).ConfigureAwait(false);
        }

        var query = picker.QueryNextFrame(pixelX, pixelY);

        await RenderFrames(viewer, 2, cancellationToken).ConfigureAwait(false);

        var (pixel, depth) = query.IsCompleted ? await query.ConfigureAwait(false) : throw new ToolException("The picker did not answer.");

        return await WithGl<object>(viewer, () =>
        {
            if (viewer.Renderer.FindPickedNode(pixel) is not { } node)
            {
                return new { Hit = false };
            }

            if (action == PickAction.Select)
            {
                viewer.SelectedNodeRenderer?.SelectNode(node);
            }
            else if (action == PickAction.Add)
            {
                viewer.SelectedNodeRenderer?.ToggleNode(node);
            }

            var position = Unproject(viewer.Renderer, node.Scene, pixelX / (float)picker.Width, pixelY / (float)picker.Height, depth);

            return new
            {
                Hit = true,
                Position = position,
                Distance = position is { } point ? Vector3.Distance(viewer.Renderer.Camera.Location, point) : (float?)null,
                Node = DescribeNode(viewer, node),
                Mesh = DescribeMesh(node, pixel.MeshId),
                Selected = action is PickAction.Select or PickAction.Add ? node.IsSelected : (bool?)null,
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The world point a pixel's depth puts it at, where the main camera sees it. Each view draws into
    /// its own slice of the reverse-Z depth range, so the slice says which camera to unproject through.
    /// </summary>
    private static Vector3? Unproject(ValveResourceFormat.Renderer.Renderer renderer, Scene scene, float u, float v, float depth)
    {
        var inSky = scene.WorldGroup != null;
        var (range, camera) = inSky ? (DepthRange.Sky, renderer.SkyCamera)
            : depth > DepthRange.Scene.Far ? (DepthRange.Viewmodel, renderer.ViewmodelCamera)
            : (DepthRange.Scene, renderer.Camera);

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

    [Tool("Trace a ray, or sweep a box with 'hull', through what the player collides with in a 3D tab: the static world and the solid entities such as doors. Starts at the camera along its view unless given 'from' with 'direction' or 'to'. Returns the hit position, normal, distance and the entity hit, or world.")]
    private async Task<object> Trace(
        GLSceneViewer viewer,
        [Description("Start point. Defaults to the camera position.")] Vector3? from = null,
        [Description("Direction to trace in. Defaults to where the camera looks.")] Vector3? direction = null,
        [Description("End point, instead of 'direction'.")] Vector3? to = null,
        [Description("How far to trace along 'direction'.")][Range(0, double.MaxValue, MinimumIsExclusive = true)] float maxDistance = 65536f,
        [Description("Half extents of a box to sweep instead of a ray. The player stands in [16, 16, 36].")] Vector3? hull = null,
        [Description("Also stop at the enabled trigger volumes the viewer simulates.")] bool triggers = false,
        CancellationToken cancellationToken = default)
    {
        if (to != null && direction != null)
        {
            throw new ArgumentException("Pass 'direction' or 'to', not both.");
        }

        if (direction == Vector3.Zero)
        {
            throw new ArgumentException("'direction' must not be zero.");
        }

        if (hull is { } size && Vector3.Min(size, Vector3.Zero) != Vector3.Zero)
        {
            throw new ArgumentException("'hull' half extents must not be negative.");
        }

        var camera = viewer.Input.Camera;
        var start = from ?? camera.Location;
        var end = to ?? start + MathUtils.SafeNormalize(direction ?? EntityTransformHelper.EulerAnglesToForwardDirection(camera.GetQAngle())) * maxDistance;

        // Between frames, because the entity system moves colliders as it ticks
        var hit = await WithGl(viewer, () =>
        {
            var entities = viewer.Renderer.EntitySystem;
            Rubikon.TraceResult result;

            if (hull is { } extents)
            {
                result = entities.PhysicsWorld.TraceAABB(start, end, extents, "player", detectStartSolid: true);
                entities.TraceAABB(start, end, extents, detectStartSolid: true, ref result);
            }
            else
            {
                result = entities.PhysicsWorld.TraceRay(start, end, "player");
                entities.TraceRay(start, end, "player", ref result);
            }

            if (triggers)
            {
                foreach (var trigger in entities.Entities.OfType<BaseTrigger>())
                {
                    if (trigger is { IsEnabled: true, IsRemoved: false, Collider: { IsEmpty: false } collider })
                    {
                        var triggerHit = hull is { } triggerExtents ? collider.TraceAABB(start, end, triggerExtents, detectStartSolid: true) : collider.TraceRay(start, end, "player");
                        triggerHit.HitEntity = trigger;
                        result.MinimizeWith(triggerHit);
                    }
                }
            }

            return result;
        }, cancellationToken).ConfigureAwait(false);

        return new
        {
            hit.Hit,
            From = start,
            To = end,
            Position = hit.Hit ? hit.HitPosition : (Vector3?)null,
            Normal = hit.Hit ? hit.HitNormal : (Vector3?)null,
            Distance = hit.Hit ? hit.Distance : (float?)null,
            StartSolid = Flag(hit.StartSolid),
            World = Flag(hit.Hit && hit.HitEntity == null),
            Entity = hit.HitEntity is not { } entity ? null
                : EntityId(viewer, entity.Data) is { } id ? DescribeEntity((GLWorldViewer)viewer, id, entity)
                : new { Class = entity.GetType().Name },
        };
    }

    [Tool("List the scene nodes of a 3D tab hidden with Delete or by this tool, which are not drawn although their layer and physics group are on. With 'hidden', hide or show nodes instead, as selecting them and pressing Delete does but setting the state rather than flipping it; nodes hidden by a layer, a physics group or their entity are left alone and reported.", Redraws = true)]
    private object HiddenNodes(
        GLSceneViewer viewer,
        [Description("True to hide, false to show again. Leave out to list the hidden nodes.")] bool? hidden = null,
        [Description("Node ids from pick or get_entity.")] int[]? nodes = null,
        [Description("Every node of this entity.")] int? entity = null,
        [Description("With hidden false, every hidden node.")] bool all = false,
        [Description("Listed nodes to skip, from an earlier 'next_offset'.")][Range(0, int.MaxValue)] int offset = 0,
        [Description("Most nodes to list.")][Range(1, int.MaxValue)] int limit = 100)
    {
        if (hidden is not { } hide)
        {
            if (nodes != null || entity != null || all)
            {
                throw new ArgumentException("Pass 'hidden' to hide or show nodes.");
            }

            var listed = AllNodes(viewer).Where(IsDeleted).ToList();

            return new
            {
                Nodes = listed.Skip(offset).Take(limit).Select(node => DescribeNode(viewer, node)).ToList(),
                Total = listed.Count,
                NextOffset = offset + limit < listed.Count ? offset + limit : (int?)null,
            };
        }

        if (nodes == null && entity == null && !all)
        {
            throw new ArgumentException("Pass 'nodes', 'entity' or 'all'.");
        }

        var data = EntityData(viewer, entity);
        var scanned = all || data != null ? AllNodes(viewer).Where(node => (all && IsDeleted(node)) || (data != null && ReferenceEquals(node.EntityData, data))) : [];
        var targets = scanned.Concat(nodes?.Select(id => NodeById(viewer, id)) ?? []).Distinct().ToList();
        var changing = targets.Where(node => hide ? HiddenReason(node) == null : IsDeleted(node)).ToList();

        changing.ForEach(node => node.LayerEnabled = !hide);

        return new
        {
            Changed = changing.Select(nodeIds.IdOf).ToList(),
            Unchanged = targets.Except(changing).Select(node => new { Id = nodeIds.IdOf(node), Hidden = HiddenReason(node) }).ToList(),
        };
    }

    [Tool("Facts about a model that the sidebar does not show: its skeleton, attachments, hitboxes, LODs, animations and the files it references, and the groups and animation its node shows. Reads a model tab, or any model node of a 3D tab, such as a map prop from pick.")]
    private object GetModelInfo(GLSceneViewer viewer, [Description("Model node id from pick or get_entity. Defaults to the model of a model tab.")] int? node = null)
    {
        var modelNode = (node is { } id ? NodeById(viewer, id) : (viewer as GLModelViewer)?.Scene.AllNodes.OfType<ModelSceneNode>().FirstOrDefault()) as ModelSceneNode
            ?? throw new ToolException(node == null ? "This tab shows no model of its own. Pass a model 'node'." : $"Node {node} is not a model.");

        // The tab's own model, or one of the scene's, which its loader still holds
        var model = (node == null ? (viewer as GLModelViewer)?.model : null)
            ?? viewer.GuiContext.LoadFileCompiled(modelNode.Name!)?.DataBlock as Model
            ?? throw new ToolException($"Could not load the model '{modelNode.Name}'.");
        var bounds = modelNode.LocalBoundingBox;

        return new
        {
            model.Name,
            Node = nodeIds.IdOf(modelNode),
            Bounds = new { bounds.Min, bounds.Max },
            Bones = model.Skeleton.Bones.Select(static bone => new { bone.Name, Parent = bone.Parent?.Name, Cloth = Flag(bone.IsProceduralCloth) }).ToList(),
            Attachments = model.Attachments.Keys.Order(StringComparer.Ordinal).ToList(),
            HitboxSets = model.HitboxSets.ToDictionary(static set => set.Key, static set => set.Value.Length),
            Lods = model.LodInfo.HasDistinctLevels ? model.LodInfo.AvailableLevels.Count : 1,
            Meshes = modelNode.RenderableMeshes.Count,
            Animations = modelNode.Animations.Count,
            FlexControllers = model.FlexControllers.Length > 0 ? model.FlexControllers.Length : (int?)null,
            BoneConstraints = model.BoneConstraints.Count > 0 ? model.BoneConstraints.Count : (int?)null,
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
