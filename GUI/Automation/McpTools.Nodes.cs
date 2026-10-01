#if DEBUG
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GUI.Types.GLViewers;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Automation;

/// <summary>Scene node ids, descriptions, and the tools that hide and show nodes.</summary>
internal sealed partial class McpTools
{
    /// <summary>
    /// Why a node is not drawn. Turning a world layer on or off applies the layers to every node again,
    /// which brings back the nodes that were hidden with Delete.
    /// </summary>
    private static class HiddenReason
    {
        public const string LayerOff = "layer_off";
        public const string InternalLayerOff = "internal_layer_off";
        public const string PhysicsGroupOff = "physics_group_off";
        public const string Deleted = "deleted";
        public const string Disabled = "disabled";
        public const string NotDrawn = "not_drawn";
    }

    private const string NodeIdDescription = "Node id, as 'scene:node' from pick, get_entity or list_hidden. Scene 0 is the map's own, the others are its spawn groups such as the 3D sky.";

    /// <summary>
    /// A node's id: the index of its scene in the renderer's scenes and its id in that scene, the same
    /// pair the picking buffer holds. It stays valid while that scene keeps its nodes.
    /// </summary>
    private static string NodeId(GLSceneViewer viewer, SceneNode node)
    {
        var scenes = viewer.Renderer.Scenes;

        for (var i = 0; i < scenes.Count; i++)
        {
            if (scenes[i] == node.Scene)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{i}:{node.Id}");
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"?:{node.Id}");
    }

    /// <summary>The node an id from <see cref="NodeId"/> names. Throws with a message for the caller when there is none.</summary>
    private static SceneNode NodeById(GLSceneViewer viewer, string id)
    {
        var separator = id.IndexOf(':', StringComparison.Ordinal);

        if (separator < 0
            || !int.TryParse(id.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var sceneIndex)
            || !uint.TryParse(id.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var nodeId))
        {
            throw new ArgumentException($"'{id}' is not a node id. Node ids look like '0:123'.");
        }

        var scenes = viewer.Renderer.Scenes;

        if (sceneIndex >= scenes.Count || scenes[sceneIndex].Find(nodeId) is not { } node || node.Id != nodeId)
        {
            throw new ArgumentException($"No node with id '{id}'. The tab has {scenes.Count} scene(s); ids from an earlier session or a spawn group that has since unloaded no longer resolve.");
        }

        return node;
    }

    private static List<SceneNode> NodesById(GLSceneViewer viewer, List<string> ids)
    {
        var nodes = new List<SceneNode>(ids.Count);

        foreach (var id in ids)
        {
            nodes.Add(NodeById(viewer, id));
        }

        return nodes;
    }

    /// <summary>
    /// Describes the nodes of one viewer. Create it on the UI thread, where it reads the world layer
    /// checkboxes; it can then be used from any thread.
    /// </summary>
    private sealed class NodeDescriber
    {
        private readonly GLSceneViewer viewer;
        private readonly HashSet<string>? layersOff;

        public NodeDescriber(GLSceneViewer viewer)
        {
            this.viewer = viewer;

            if (viewer is GLWorldViewer world)
            {
                layersOff = [];

                foreach (var (name, enabled) in world.GetWorldLayers())
                {
                    if (!enabled)
                    {
                        layersOff.Add(name);
                    }
                }
            }
        }

        public string Id(SceneNode node) => NodeId(viewer, node);

        /// <summary>Why the node is not drawn, empty when it is.</summary>
        public List<string> HiddenReasons(SceneNode node)
        {
            var reasons = new List<string>();

            if (!node.Visible)
            {
                reasons.Add(HiddenReason.NotDrawn);
            }

            var physicsGroupOff = node is PhysSceneNode { Enabled: false };

            if (physicsGroupOff)
            {
                reasons.Add(HiddenReason.PhysicsGroupOff);
            }

            if (node.LayerEnabled || physicsGroupOff)
            {
                return reasons;
            }

            if (layersOff == null)
            {
                reasons.Add(HiddenReason.Disabled);
                return reasons;
            }

            if (node.LayerName?.StartsWith("Internal -", StringComparison.Ordinal) == true)
            {
                reasons.Add(HiddenReason.InternalLayerOff);
            }
            else if (node.LayerName == null || layersOff.Contains(node.LayerName))
            {
                reasons.Add(HiddenReason.LayerOff);
            }
            else
            {
                reasons.Add(HiddenReason.Deleted);
            }

            return reasons;
        }

        /// <summary>Whether showing the node again is a matter of undoing Delete rather than of a layer, group or the entity.</summary>
        public bool IsRestorable(SceneNode node)
        {
            var reasons = HiddenReasons(node);

            return reasons.Contains(HiddenReason.Deleted) || reasons.Contains(HiddenReason.Disabled);
        }

        public JsonObject Describe(SceneNode node)
        {
            var result = new JsonObject
            {
                ["id"] = Id(node),
                ["type"] = node.GetType().Name.Replace("SceneNode", string.Empty, StringComparison.Ordinal),
            };

            if (!string.IsNullOrEmpty(node.Name))
            {
                result["name"] = node.Name;
            }

            if (node.LayerName != null)
            {
                result["layer"] = node.LayerName;
            }

            if (node is PhysSceneNode physNode)
            {
                result["physics_group"] = physNode.PhysGroupName;
            }

            if (viewer.Renderer.SpawnGroups.FirstOrDefault(group => group.Scene == node.Scene) is { } spawnGroup)
            {
                if (spawnGroup.WorldGroup != null)
                {
                    result["sky"] = true;
                }

                result["spawn_group"] = spawnGroup.MapName;
            }

            if (HiddenReasons(node) is { Count: > 0 } reasons)
            {
                var hidden = new JsonArray();

                foreach (var reason in reasons)
                {
                    hidden.Add(reason);
                }

                result["hidden"] = hidden;
            }

            var materials = new JsonArray();

            foreach (var material in NodeMaterials(node).Distinct(StringComparer.Ordinal))
            {
                materials.Add(material);
            }

            if (materials.Count > 0)
            {
                result["materials"] = materials;
            }

            var bounds = node.BoundingBox.Transform(node.Scene.ToViewerWorld);

            result["center"] = Round(bounds.Center);
            result["size"] = Round(bounds.Size);

            return result;
        }
    }

    private static IEnumerable<string> NodeMaterials(SceneNode node) => node switch
    {
        MeshCollectionNode meshes => meshes.RenderableMeshes.SelectMany(mesh => mesh.DrawCalls).Select(draw => draw.Material.Material.Name),
        SceneAggregate.Fragment fragment => [fragment.DrawCall.Material.Material.Name],
        _ => [],
    };

    private void RegisterNodeTools()
    {
        Add("set_hidden", "Hide or show scene nodes of a 3D tab the way selecting them and pressing Delete does, but setting the state rather than flipping it: only the nodes that need to change are selected, Delete is pressed, and the selection is dropped as Escape does, so any selection is lost. Nodes hidden by a layer, a physics group or their entity are left alone and reported. Turning a world layer on or off applies the layers to every node again, which brings back every node hidden this way or with Delete.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["hidden"] = Prop("boolean", "True to hide, false to show again."),
                ["entity"] = Prop("integer", "Every node of this entity, by id from find_entities or pick."),
                ["nodes"] = StringArrayProp("Node ids from pick, get_entity or list_hidden."),
                ["all"] = Prop("boolean", "With hidden false, show every node that was hidden with Delete or this tool."),
            }, "hidden"),
            SetHidden, SceneViewer);

        Add("list_hidden", "List the scene nodes of a 3D tab that were hidden with Delete or set_hidden, and so are not drawn although their layer and physics group are on.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["offset"] = Prop("integer", "Nodes to skip, from an earlier 'next_offset'."),
                ["limit"] = Prop("integer", "Most nodes to return. Defaults to 100, at most 1000."),
            }),
            ListHidden, SceneViewer);
    }

    private async Task<McpToolResult> SetHidden(JsonObject args, CancellationToken cancellationToken)
    {
        var hidden = GetBool(args, "hidden");

        if (hidden == null)
        {
            return MissingArgument("hidden", args);
        }

        var entityId = GetInt(args, "entity");
        var nodeIds = GetStringArray(args, "nodes");
        var all = GetBool(args, "all") ?? false;

        if (all && hidden.Value)
        {
            return McpToolResult.Error("'all' only shows nodes again; pass hidden false with it.");
        }

        if (!all && entityId == null && nodeIds == null)
        {
            return MissingArgument("entity or nodes", args);
        }

        var (viewer, error) = await ActivateViewer<GLSceneViewer>(args, cancellationToken).ConfigureAwait(false);

        if (error != null)
        {
            return error;
        }

        var (toToggle, unchanged, failure) = await OnUi<(List<SceneNode> Change, List<SceneNode> Unchanged, McpToolResult? Failure)>(() =>
        {
            var describer = new NodeDescriber(viewer!);
            var targets = new List<SceneNode>();

            if (all)
            {
                foreach (var scene in viewer!.Renderer.Scenes)
                {
                    targets.AddRange(scene.AllNodes.Where(describer.IsRestorable));
                }
            }

            if (entityId != null)
            {
                if (viewer is not GLWorldViewer { LoadedWorld: { } world } worldViewer)
                {
                    return ([], [], McpToolResult.Error("'entity' needs a tab with a map loaded. Pass 'nodes' instead."));
                }

                if (EntityById(worldViewer, world, entityId.Value) is not { } entity)
                {
                    return ([], [], NoSuchEntity(world, entityId.Value));
                }

                var entityNodes = EntityNodes(viewer, entity.Data);

                if (entityNodes.Count == 0)
                {
                    return ([], [], McpToolResult.Error($"Entity {entityId} has no scene nodes, so there is nothing to hide or show."));
                }

                targets.AddRange(entityNodes);
            }

            if (nodeIds != null)
            {
                targets.AddRange(NodesById(viewer!, nodeIds));
            }

            var change = new List<SceneNode>();
            var rest = new List<SceneNode>();

            foreach (var node in targets.Distinct(ReferenceEqualityComparer.Instance).Cast<SceneNode>())
            {
                var changes = hidden.Value ? node.LayerEnabled : describer.IsRestorable(node);
                (changes ? change : rest).Add(node);
            }

            return (change, rest, null);
        }, cancellationToken).ConfigureAwait(false);

        if (failure != null)
        {
            return failure;
        }

        if (toToggle.Count > 0)
        {
            // Off the UI thread, because holding a frame there can deadlock against a frame that is
            // waiting on the UI thread. Held so the render thread does not walk the selection as it changes.
            await Task.Run(() =>
            {
                using var frame = viewer!.HoldFrame();
                viewer.ToggleNodesWithDelete(toToggle);
            }, cancellationToken).ConfigureAwait(false);
        }

        return await OnUi(() =>
        {
            var describer = new NodeDescriber(viewer!);
            var changed = new JsonArray();

            foreach (var node in toToggle)
            {
                changed.Add(describer.Id(node));
            }

            var result = new JsonObject
            {
                [hidden.Value ? "hidden" : "shown"] = changed,
            };

            if (unchanged.Count > 0)
            {
                var entries = new JsonArray();

                foreach (var node in unchanged)
                {
                    var entry = new JsonObject
                    {
                        ["id"] = describer.Id(node),
                    };

                    if (describer.HiddenReasons(node) is { Count: > 0 } reasons)
                    {
                        entry["hidden"] = new JsonArray([.. reasons.Select(static reason => (JsonNode?)reason)]);
                    }

                    entries.Add(entry);
                }

                result["unchanged"] = entries;
            }

            return McpToolResult.Json(result);
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task<McpToolResult> ListHidden(JsonObject args, CancellationToken cancellationToken)
    {
        var offset = GetOffset(args);
        var limit = GetLimit(args, 100, 1000);

        return WithViewer<GLSceneViewer>(args, viewer =>
        {
            var describer = new NodeDescriber(viewer);
            var nodes = new JsonArray();
            var total = 0;

            foreach (var scene in viewer.Renderer.Scenes)
            {
                foreach (var node in scene.AllNodes)
                {
                    if (!describer.IsRestorable(node))
                    {
                        continue;
                    }

                    if (total >= offset && nodes.Count < limit)
                    {
                        nodes.Add(describer.Describe(node));
                    }

                    total++;
                }
            }

            var result = new JsonObject
            {
                ["nodes"] = nodes,
            };

            if (offset + nodes.Count < total)
            {
                result["total"] = total;
                result["next_offset"] = offset + nodes.Count;
            }

            return McpToolResult.Json(result);
        }, cancellationToken);
    }
}
#endif
