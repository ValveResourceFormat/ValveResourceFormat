#if DEBUG
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GUI.Types.GLViewers;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Automation;

/// <summary>Tools that read and pick a map's entities and scene nodes.</summary>
internal sealed partial class McpTools
{
    /// <summary>
    /// A map entity and its id, with the spawn group it came in, or null for the map's own. Ids count
    /// through the map's entities and then carry on through the 3D sky map's, so one integer names
    /// either. Entities of the other spawn groups, such as stages a map loads while it plays, get ids
    /// from <see cref="SpawnGroupEntityIdBase"/> up the first time a tool lists them, which they keep.
    /// </summary>
    private readonly record struct MapEntity(int Id, EntityLump.Entity Data, SpawnGroup? Group)
    {
        public bool InSky => Group?.WorldGroup != null;
    }

    private const int SpawnGroupEntityIdBase = 1_000_000;

    private readonly ConditionalWeakTable<EntityLump.Entity, StrongBox<int>> spawnGroupEntityIds = [];
    private readonly Dictionary<int, WeakReference<EntityLump.Entity>> spawnGroupEntities = [];
    private int nextSpawnGroupEntityId = SpawnGroupEntityIdBase;

    private void RegisterEntityTools()
    {
        Add("find_entities", "Search the entities of a map, its 3D sky and any other spawn group it loaded, such as a stage. Positions are where each entity renders, so a 3D sky entity's position can be passed straight to set_camera. Text filters match case insensitively, as substrings unless 'match' says otherwise. Returns 'next_offset' when more matches remain.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["classname"] = Prop("string", "Match entities by classname."),
                ["targetname"] = Prop("string", "Match entities by targetname."),
                ["key"] = Prop("string", "Only entities that have this keyvalue, by exact key name."),
                ["value"] = Prop("string", "Match the value of 'key', or of any keyvalue when 'key' is omitted. Values that are not strings are matched in the form get_entity shows them."),
                ["match"] = Prop("string", "How 'classname', 'targetname' and 'value' match. Defaults to contains.", "contains", "exact", "regex"),
                ["trigger"] = Prop("boolean", "True for only triggers: entities that spawned as triggers or whose classname starts with trigger_. False for none. Both when omitted."),
                ["near"] = VectorProp("Only entities within 'radius' of this world point [x, y, z]."),
                ["radius"] = Prop("number", "Distance from 'near'. Defaults to 512."),
                ["sky"] = Prop("boolean", "True for only 3D sky entities, false for none. Both when omitted."),
                ["fields"] = StringArrayProp("Keyvalues to include with each match, by exact key name."),
                ["offset"] = Prop("integer", "Matches to skip, from an earlier 'next_offset'."),
                ["limit"] = Prop("integer", "Most matches to return. Defaults to 50, at most 1000."),
            }),
            FindEntities, MapViewer);

        Add("get_entity", "Everything about one entity: its keyvalues and outputs, where it renders, the spawn group it came in, the class it spawned as with its live transform, and its scene nodes with their id, layer, physics group, materials and why any is hidden.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["id"] = Prop("integer", "Entity id from find_entities or pick."),
            }, "id"),
            GetEntity, MapViewer);

        Add("select_entity", "Select an entity and move the camera to it, as double clicking it in the entity list does. Like that, it turns on the layer and physics group of the entity's node when they are off, and reports which it turned on. The selection outline stays until clear_selection.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["id"] = Prop("integer", "Entity id from find_entities or pick."),
                ["instant"] = Prop("boolean", "Skip the fly-in and jump straight there. Defaults to true."),
            }, "id"),
            SelectEntity, MapViewer);

        Add("pick", "Identify what is under a viewport pixel of a 3D tab: the scene node with its id, the mesh and materials drawn there, what the double click details window shows for it, and its entity. A pixel showing no node answers hit false with background 'sky' or 'nothing'; one outside the render area is an error. Coordinates are from the top left of the render area, whose size get_camera returns. Only reads by default.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["x"] = Prop("integer", "Pixel X in the render area."),
                ["y"] = Prop("integer", "Pixel Y in the render area."),
                ["select"] = Prop("boolean", "Select what was hit as clicking does, replacing the selection. Its outline stays until clear_selection."),
                ["add"] = Prop("boolean", "Add what was hit to the selection as Ctrl+click does, or take it out when it is already selected."),
                ["open"] = Prop("boolean", "Open what was hit in a tab of its own as Ctrl+double click does in a map, with the same mesh groups, material group and animation, and wait for it to load. Returns that tab like open_file."),
            }, "x", "y"),
            Pick, SceneViewer);
    }

    /// <summary>The spawn group of the map's 3D sky, picked the way the renderer picks the one it draws as the sky.</summary>
    private static SpawnGroup? SkyGroup(WorldLoader world) => world.SpawnGroups.FindLast(static group => group.WorldGroup != null);

    private int SpawnGroupEntityId(EntityLump.Entity entity)
    {
        if (spawnGroupEntityIds.TryGetValue(entity, out var known))
        {
            return known.Value;
        }

        var id = nextSpawnGroupEntityId++;
        spawnGroupEntityIds.Add(entity, new StrongBox<int>(id));
        spawnGroupEntities[id] = new WeakReference<EntityLump.Entity>(entity);

        return id;
    }

    private IEnumerable<MapEntity> MapEntities(GLWorldViewer viewer, WorldLoader world)
    {
        for (var i = 0; i < world.Entities.Count; i++)
        {
            yield return new MapEntity(i, world.Entities[i], Group: null);
        }

        var sky = SkyGroup(world);

        if (sky != null)
        {
            var id = world.Entities.Count;

            foreach (var entity in sky.Entities)
            {
                yield return new MapEntity(id++, entity, sky);
            }
        }

        foreach (var group in viewer.Renderer.SpawnGroups.ToArray())
        {
            if (group == sky)
            {
                continue;
            }

            foreach (var entity in group.Entities)
            {
                yield return new MapEntity(SpawnGroupEntityId(entity), entity, group);
            }
        }
    }

    private MapEntity? EntityById(GLWorldViewer viewer, WorldLoader world, int id)
    {
        if (id >= 0 && id < world.Entities.Count)
        {
            return new MapEntity(id, world.Entities[id], Group: null);
        }

        var skyIndex = id - world.Entities.Count;

        if (SkyGroup(world) is { } sky && skyIndex >= 0 && skyIndex < sky.Entities.Count)
        {
            return new MapEntity(id, sky.Entities[skyIndex], sky);
        }

        if (spawnGroupEntities.TryGetValue(id, out var reference) && reference.TryGetTarget(out var entity))
        {
            foreach (var group in viewer.Renderer.SpawnGroups.ToArray())
            {
                if (group.Entities.Contains(entity, ReferenceEqualityComparer.Instance))
                {
                    return new MapEntity(id, entity, group);
                }
            }
        }

        return null;
    }

    private MapEntity? EntityByData(GLWorldViewer viewer, WorldLoader world, EntityLump.Entity data)
    {
        foreach (var entity in MapEntities(viewer, world))
        {
            if (ReferenceEquals(entity.Data, data))
            {
                return entity;
            }
        }

        return null;
    }

    private const string NoMapEntities = "This tab shows world geometry without a map, so it has no entities.";

    private static McpToolResult NoSuchEntity(WorldLoader world, int id)
    {
        var count = world.Entities.Count + (SkyGroup(world)?.Entities.Count ?? 0);

        return McpToolResult.Error($"No entity with id {id}. Ids run from 0 to {count - 1}, and entities of other spawn groups have the ids find_entities gave them, from {SpawnGroupEntityIdBase}, while their group stays loaded.");
    }

    /// <summary>The entities the entity system spawned, by the keyvalues they spawned from.</summary>
    private static Dictionary<EntityLump.Entity, BaseEntity> SpawnedEntities(GLSceneViewer viewer)
    {
        var spawned = new Dictionary<EntityLump.Entity, BaseEntity>(ReferenceEqualityComparer.Instance);

        foreach (var instance in viewer.Renderer.EntitySystem.Entities.ToArray())
        {
            if (instance.Data != null)
            {
                spawned.TryAdd(instance.Data, instance);
            }
        }

        return spawned;
    }

    /// <summary>
    /// Where the entity is placed as it renders: an entity of a prefab or spawn group is placed with it,
    /// and one of the 3D sky shows up where its sky scene is seen.
    /// </summary>
    private static Vector3 RenderedPosition(MapEntity entity, BaseEntity? instance)
    {
        var origin = entity.Data.GetVector3Property("origin");
        var placement = instance?.ParentTransform ?? entity.Group?.Transform ?? Matrix4x4.Identity;
        var toViewer = instance?.Scene.ToViewerWorld ?? entity.Group?.Scene.ToViewerWorld ?? Matrix4x4.Identity;

        return Vector3.Transform(Vector3.Transform(origin, placement), toViewer);
    }

    private static JsonObject DescribeEntity(MapEntity entity, BaseEntity? instance)
    {
        var result = new JsonObject
        {
            ["id"] = entity.Id,
            ["classname"] = entity.Data.GetStringProperty("classname"),
        };

        if (!string.IsNullOrEmpty(entity.Data.TargetName))
        {
            result["targetname"] = entity.Data.TargetName;
        }

        if (entity.InSky)
        {
            result["sky"] = true;
        }

        if (entity.Group != null)
        {
            result["spawn_group"] = entity.Group.MapName;
        }

        result["position"] = Round(RenderedPosition(entity, instance));

        return result;
    }

    /// <summary>A keyvalue as get_entity shows it: a string bare, anything else in the KV3 form it serializes to.</summary>
    private static string KeyValueText(KVObject value)
        => value.ValueType == KVValueType.String ? (string)value : EntityLump.StringifyValue(value);

    private static Func<string?, bool>? TextMatcher(string? pattern, string match, string name)
    {
        if (pattern == null)
        {
            return null;
        }

        switch (match)
        {
            case "contains":
                return text => text != null && text.Contains(pattern, StringComparison.OrdinalIgnoreCase);

            case "exact":
                return text => text != null && string.Equals(text, pattern, StringComparison.OrdinalIgnoreCase);

            default:
                Regex regex;

                try
                {
                    regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
                }
                catch (ArgumentException e)
                {
                    throw new ArgumentException($"'{name}' is not a valid regular expression: {e.Message}", e);
                }

                return text => text != null && regex.IsMatch(text);
        }
    }

    private async Task<McpToolResult> FindEntities(JsonObject args, CancellationToken cancellationToken)
    {
        var match = GetString(args, "match") ?? "contains";

        if (match is not ("contains" or "exact" or "regex"))
        {
            return McpToolResult.Error($"Unknown match '{match}'. Use contains, exact or regex.");
        }

        var classname = TextMatcher(GetString(args, "classname"), match, "classname");
        var targetname = TextMatcher(GetString(args, "targetname"), match, "targetname");
        var key = GetString(args, "key");
        var value = TextMatcher(GetString(args, "value"), match, "value");
        var trigger = GetBool(args, "trigger");
        var near = GetVector(args, "near");
        var radius = GetFloat(args, "radius") ?? 512f;
        var sky = GetBool(args, "sky");
        var fields = GetStringArray(args, "fields");
        var offset = GetOffset(args);
        var limit = GetLimit(args, 50, 1000);

        if (!float.IsFinite(radius) || radius < 0f)
        {
            return McpToolResult.Error("'radius' must be a finite distance that is not negative.");
        }

        return await WithViewer<GLWorldViewer>(args, viewer =>
        {
            if (viewer.LoadedWorld is not { } world)
            {
                return McpToolResult.Error(NoMapEntities);
            }

            var spawned = SpawnedEntities(viewer);
            var matches = new JsonArray();
            var total = 0;

            foreach (var entity in MapEntities(viewer, world))
            {
                if (sky != null && entity.InSky != sky.Value)
                {
                    continue;
                }

                if (classname != null && !classname(entity.Data.GetStringProperty("classname")))
                {
                    continue;
                }

                if (targetname != null && !targetname(entity.Data.TargetName))
                {
                    continue;
                }

                if (key != null || value != null)
                {
                    var found = false;

                    foreach (var (entityKey, entityValue) in entity.Data.Children)
                    {
                        if ((key == null || string.Equals(entityKey, key, StringComparison.Ordinal))
                            && (value == null || value(KeyValueText(entityValue))))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        continue;
                    }
                }

                var instance = spawned.GetValueOrDefault(entity.Data);

                var isTrigger = (instance?.IsTrigger ?? false)
                    || entity.Data.GetStringProperty("classname").StartsWith("trigger_", StringComparison.OrdinalIgnoreCase);

                if (trigger != null && isTrigger != trigger.Value)
                {
                    continue;
                }

                if (near != null && Vector3.Distance(RenderedPosition(entity, instance), near.Value) > radius)
                {
                    continue;
                }

                if (total >= offset && matches.Count < limit)
                {
                    var described = DescribeEntity(entity, instance);

                    if (fields != null)
                    {
                        var values = new JsonObject();

                        foreach (var field in fields)
                        {
                            if (entity.Data.TryGetValue(field, out var fieldValue))
                            {
                                values[field] = KeyValueText(fieldValue);
                            }
                        }

                        described["fields"] = values;
                    }

                    matches.Add(described);
                }

                total++;
            }

            var result = new JsonObject
            {
                ["entities"] = matches,
            };

            if (offset + matches.Count < total)
            {
                result["total"] = total;
                result["next_offset"] = offset + matches.Count;
            }

            return McpToolResult.Json(result);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpToolResult> GetEntity(JsonObject args, CancellationToken cancellationToken)
    {
        var id = GetInt(args, "id");

        if (id == null)
        {
            return MissingArgument("id", args);
        }

        return await WithViewer<GLWorldViewer>(args, viewer =>
        {
            if (viewer.LoadedWorld is not { } world)
            {
                return McpToolResult.Error(NoMapEntities);
            }

            if (EntityById(viewer, world, id.Value) is not { } entity)
            {
                return NoSuchEntity(world, id.Value);
            }

            var instance = SpawnedEntities(viewer).GetValueOrDefault(entity.Data);
            var result = DescribeEntity(entity, instance);

            var keyValues = new JsonObject();

            foreach (var (key, value) in entity.Data.Children)
            {
                if (key is "classname" or "targetname")
                {
                    continue;
                }

                keyValues[key] = KeyValueText(value);
            }

            result["keyvalues"] = keyValues;

            if (entity.Data.Connections is { Count: > 0 } connections)
            {
                var outputs = new JsonArray();

                foreach (var connection in connections)
                {
                    // The form Hammer writes them in: output target:input:parameter:delay:times.
                    outputs.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{connection.OutputName} {connection.TargetName}:{connection.InputName}:{connection.OverrideParam}:{connection.Delay}:{connection.TimesToFire}"));
                }

                result["outputs"] = outputs;
            }

            if (instance != null)
            {
                var spawned = new JsonObject
                {
                    ["class"] = instance.GetType().Name,
                    ["position"] = Round(Vector3.Transform(instance.Transform.Translation, instance.Scene.ToViewerWorld)),
                    ["angles"] = Round(instance.Angles),
                };

                if (instance.Velocity != Vector3.Zero)
                {
                    spawned["velocity"] = Round(instance.Velocity);
                }

                if (!instance.IsDrawn)
                {
                    spawned["not_drawn"] = true;
                }

                if (instance.IsTrigger)
                {
                    spawned["trigger"] = true;
                }

                if (instance.IsRemoved)
                {
                    spawned["removed"] = true;
                }

                result["spawned"] = spawned;
            }

            var describer = new NodeDescriber(viewer);
            var nodes = new JsonArray();

            foreach (var node in EntityNodes(viewer, entity.Data))
            {
                nodes.Add(describer.Describe(node));
            }

            if (nodes.Count > 0)
            {
                result["nodes"] = nodes;
            }

            return McpToolResult.Json(result);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Every node of every scene that carries this entity's keyvalues.</summary>
    private static List<SceneNode> EntityNodes(GLSceneViewer viewer, EntityLump.Entity data)
    {
        var nodes = new List<SceneNode>();

        foreach (var scene in viewer.Renderer.Scenes)
        {
            foreach (var node in scene.AllNodes)
            {
                if (ReferenceEquals(node.EntityData, data))
                {
                    nodes.Add(node);
                }
            }
        }

        return nodes;
    }

    private async Task<McpToolResult> SelectEntity(JsonObject args, CancellationToken cancellationToken)
    {
        var id = GetInt(args, "id");

        if (id == null)
        {
            return MissingArgument("id", args);
        }

        var instant = GetBool(args, "instant") ?? true;

        return await WithViewer<GLWorldViewer>(args, viewer =>
        {
            if (viewer.LoadedWorld is not { } world)
            {
                return McpToolResult.Error(NoMapEntities);
            }

            if (EntityById(viewer, world, id.Value) is not { } entity)
            {
                return NoSuchEntity(world, id.Value);
            }

            var layersBefore = viewer.GetWorldLayers();
            var groupsBefore = viewer.GetPhysicsGroups();
            var instance = SpawnedEntities(viewer).GetValueOrDefault(entity.Data);

            var node = viewer.SelectAndFocusEntity(entity.Data, RenderedPosition(entity, instance));

            if (instant)
            {
                LandCamera(viewer);
            }

            var result = new JsonObject
            {
                ["camera"] = DescribeCamera(viewer.Input.Camera),
            };

            if (node != null)
            {
                result["node"] = new NodeDescriber(viewer).Describe(node);
            }

            if (TurnedOn(layersBefore, viewer.GetWorldLayers()) is { } layers)
            {
                result["enabled_layers"] = layers;
            }

            if (TurnedOn(groupsBefore, viewer.GetPhysicsGroups()) is { } groups)
            {
                result["enabled_physics_groups"] = groups;
            }

            return McpToolResult.Json(result);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Focusing flies the camera in; this lands it where it was headed, so a screenshot taken straight
    /// after shows the destination rather than the journey.
    /// </summary>
    private static void LandCamera(GLSceneViewer viewer)
    {
        var camera = viewer.Input.Camera;
        viewer.Input.SetCameraImmediate(camera.Location, camera.GetQAngle());
    }

    private static JsonArray? TurnedOn(List<(string Name, bool Enabled)> before, List<(string Name, bool Enabled)> after)
    {
        var wasOn = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, enabled) in before)
        {
            if (enabled)
            {
                wasOn.Add(name);
            }
        }

        var turnedOn = new JsonArray();

        foreach (var (name, enabled) in after)
        {
            if (enabled && !wasOn.Contains(name))
            {
                turnedOn.Add(name);
            }
        }

        return turnedOn.Count > 0 ? turnedOn : null;
    }
}
#endif
