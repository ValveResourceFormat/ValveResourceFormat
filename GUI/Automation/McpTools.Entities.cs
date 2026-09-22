#if DEBUG
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GUI.Types.GLViewers;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Automation;

/// <summary>Tools that read and pick a map's entities and scene nodes.</summary>
internal sealed partial class McpTools
{
    /// <summary>
    /// A map entity and its id. Ids count through the map's entities and then carry on through the
    /// 3D sky map's, so one integer names either.
    /// </summary>
    private readonly record struct MapEntity(int Id, EntityLump.Entity Data, bool InSky);

    private void RegisterEntityTools()
    {
        Add("find_entities", "Search the entities of a map and its 3D sky. Positions are where each entity renders, so a 3D sky entity's position can be passed straight to set_camera. Returns 'next_offset' when more matches remain.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["classname"] = Prop("string", "Match entities whose classname contains this."),
                ["targetname"] = Prop("string", "Match entities whose targetname contains this."),
                ["near"] = VectorProp("Only entities within 'radius' of this world point [x, y, z]."),
                ["radius"] = Prop("number", "Distance from 'near'. Defaults to 512."),
                ["sky"] = Prop("boolean", "True for only 3D sky entities, false for none. Both when omitted."),
                ["offset"] = Prop("integer", "Matches to skip, from an earlier 'next_offset'."),
                ["limit"] = Prop("integer", "Most matches to return. Defaults to 50, at most 1000."),
            }),
            FindEntities);

        Add("get_entity", "Everything about one entity: its keyvalues and outputs, where it renders, the class it spawned as with its live transform, and its scene nodes with their layer, physics group, materials and whether they are hidden.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["id"] = Prop("integer", "Entity id from find_entities or pick."),
            }, "id"),
            GetEntity);

        Add("select_entity", "Select an entity and move the camera to it, as double clicking it in the entity list does. Like that, it turns on the layer and physics group of the entity's node when they are off, and reports which it turned on. The selection outline stays until clear_selection.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["id"] = Prop("integer", "Entity id from find_entities or pick."),
                ["instant"] = Prop("boolean", "Skip the fly-in and jump straight there. Defaults to true."),
            }, "id"),
            SelectEntity);

        Add("pick", "Identify the scene node and entity under a viewport pixel. Coordinates are from the top left of the render area. Only reads by default; pass 'select' to select it as clicking would.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["x"] = Prop("integer", "Pixel X in the render area."),
                ["y"] = Prop("integer", "Pixel Y in the render area."),
                ["select"] = Prop("boolean", "Also select what was hit, drawing its outline until clear_selection. Defaults to false."),
            }, "x", "y"),
            Pick);
    }

    private static IEnumerable<MapEntity> MapEntities(WorldLoader world)
    {
        for (var i = 0; i < world.Entities.Count; i++)
        {
            yield return new MapEntity(i, world.Entities[i], InSky: false);
        }

        if (world.SkyboxWorld is { } sky)
        {
            for (var i = 0; i < sky.Entities.Count; i++)
            {
                yield return new MapEntity(world.Entities.Count + i, sky.Entities[i], InSky: true);
            }
        }
    }

    private static MapEntity? EntityById(WorldLoader world, int id)
    {
        if (id >= 0 && id < world.Entities.Count)
        {
            return new MapEntity(id, world.Entities[id], InSky: false);
        }

        var skyIndex = id - world.Entities.Count;

        if (world.SkyboxWorld is { } sky && skyIndex >= 0 && skyIndex < sky.Entities.Count)
        {
            return new MapEntity(id, sky.Entities[skyIndex], InSky: true);
        }

        return null;
    }

    private static MapEntity? EntityByData(WorldLoader world, EntityLump.Entity data)
    {
        foreach (var entity in MapEntities(world))
        {
            if (ReferenceEquals(entity.Data, data))
            {
                return entity;
            }
        }

        return null;
    }

    private static McpToolResult NoSuchEntity(WorldLoader world, int id)
    {
        var count = world.Entities.Count + (world.SkyboxWorld?.Entities.Count ?? 0);

        return McpToolResult.Error($"No entity with id {id}. Ids run from 0 to {count - 1}.");
    }

    /// <summary>Where the entity renders: a 3D sky entity is placed in the world the way its sky scene is.</summary>
    private static Vector3 RenderedPosition(WorldLoader world, MapEntity entity)
    {
        if (entity.InSky && world.SkyboxWorld is { } sky)
        {
            return Vector3.Transform(sky.GetEntityWorldTransform(entity.Data).Translation, world.SkyboxTransform);
        }

        return world.GetEntityWorldTransform(entity.Data).Translation;
    }

    private static JsonObject DescribeEntity(WorldLoader world, MapEntity entity)
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

        result["position"] = Round(RenderedPosition(world, entity));

        return result;
    }

    private static JsonObject DescribeNode(SceneNode node, bool inSky)
    {
        var result = new JsonObject
        {
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

        if (inSky)
        {
            result["sky"] = true;
        }

        // A disabled layer or physics group, or an entity that is not drawn.
        if (!node.LayerEnabled || !node.Visible)
        {
            result["hidden"] = true;
        }

        IEnumerable<string> materials = node switch
        {
            MeshCollectionNode meshes => meshes.RenderableMeshes.SelectMany(mesh => mesh.DrawCalls).Select(draw => draw.Material.Material.Name),
            SceneAggregate.Fragment fragment => [fragment.DrawCall.Material.Material.Name],
            _ => [],
        };

        var materialList = new JsonArray();

        foreach (var material in materials.Distinct(StringComparer.Ordinal))
        {
            materialList.Add(material);
        }

        if (materialList.Count > 0)
        {
            result["materials"] = materialList;
        }

        result["center"] = Round(node.BoundingBox.Center);
        result["size"] = Round(node.BoundingBox.Size);

        return result;
    }

    private async Task<McpToolResult> FindEntities(JsonObject args, CancellationToken cancellationToken)
    {
        var classname = GetString(args, "classname");
        var targetname = GetString(args, "targetname");
        var near = GetVector(args, "near");
        var radius = GetFloat(args, "radius") ?? 512f;
        var sky = GetBool(args, "sky");
        var offset = Math.Max(0, GetInt(args, "offset") ?? 0);
        var limit = Math.Clamp(GetInt(args, "limit") ?? 50, 1, 1000);

        return await WithViewer<GLWorldViewer>(args, viewer =>
        {
            if (viewer.LoadedWorld is not { } world)
            {
                return McpToolResult.Error("This tab has no loaded world.");
            }

            var matches = new JsonArray();
            var total = 0;

            foreach (var entity in MapEntities(world))
            {
                if (sky != null && entity.InSky != sky.Value)
                {
                    continue;
                }

                if (classname != null && !(entity.Data.GetStringProperty("classname") ?? string.Empty).Contains(classname, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (targetname != null && !(entity.Data.TargetName ?? string.Empty).Contains(targetname, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (near != null && Vector3.Distance(RenderedPosition(world, entity), near.Value) > radius)
                {
                    continue;
                }

                if (total >= offset && matches.Count < limit)
                {
                    matches.Add(DescribeEntity(world, entity));
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
                return McpToolResult.Error("This tab has no loaded world.");
            }

            if (EntityById(world, id.Value) is not { } entity)
            {
                return NoSuchEntity(world, id.Value);
            }

            var result = DescribeEntity(world, entity);

            var keyValues = new JsonObject();

            foreach (var (key, value) in entity.Data.Children)
            {
                if (key is "classname" or "targetname")
                {
                    continue;
                }

                // A string is shown bare; anything else in the KV3 form it serializes to.
                keyValues[key] = value.ValueType == KVValueType.String ? (string)value : EntityLump.StringifyValue(value);
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

            if (viewer.Scene.EntitySystem.Entities.FirstOrDefault(spawned => ReferenceEquals(spawned.Data, entity.Data)) is { } instance)
            {
                var spawned = new JsonObject
                {
                    ["class"] = instance.GetType().Name,
                    ["position"] = Round(instance.Transform.Translation),
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

            var nodes = new JsonArray();
            var sceneNodes = viewer.SkyboxScene == null ? viewer.Scene.AllNodes : viewer.Scene.AllNodes.Concat(viewer.SkyboxScene.AllNodes);

            foreach (var node in sceneNodes)
            {
                if (ReferenceEquals(node.EntityData, entity.Data))
                {
                    nodes.Add(DescribeNode(node, node.Scene == viewer.SkyboxScene));
                }
            }

            if (nodes.Count > 0)
            {
                result["nodes"] = nodes;
            }

            return McpToolResult.Json(result);
        }, cancellationToken).ConfigureAwait(false);
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
                return McpToolResult.Error("This tab has no loaded world.");
            }

            if (EntityById(world, id.Value) is not { } entity)
            {
                return NoSuchEntity(world, id.Value);
            }

            var layersBefore = viewer.GetWorldLayers();
            var groupsBefore = viewer.GetPhysicsGroups();

            var node = viewer.SelectAndFocusEntity(entity.Data, RenderedPosition(world, entity));

            if (instant)
            {
                // Focusing flies the camera in; land it now so a screenshot taken straight after
                // shows the destination rather than the journey.
                var camera = viewer.Input.Camera;
                viewer.Input.SetCameraImmediate(camera.Location, camera.GetQAngle());
            }

            var result = new JsonObject
            {
                ["camera"] = DescribeCamera(viewer.Input.Camera),
            };

            if (node != null)
            {
                result["node"] = DescribeNode(node, node.Scene == viewer.SkyboxScene);
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

    private async Task<McpToolResult> Pick(JsonObject args, CancellationToken cancellationToken)
    {
        var x = GetInt(args, "x");
        var y = GetInt(args, "y");

        if (x == null || y == null)
        {
            return MissingArgument("x and y", args);
        }

        var select = GetBool(args, "select") ?? false;

        var (scene, error) = await ActivateViewer<GLSceneViewer>(args, cancellationToken).ConfigureAwait(false);

        if (error != null)
        {
            return error;
        }

        if (await CheckCanRender(cancellationToken).ConfigureAwait(false) is { } renderError)
        {
            return McpToolResult.Error(renderError);
        }

        var picker = await OnUi(() => scene!.PickingTexture, cancellationToken).ConfigureAwait(false);

        if (picker == null)
        {
            return McpToolResult.Error("This viewer has no picker.");
        }

        var answered = new TaskCompletionSource<PickingTexture.PixelInfo>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPicked(object? sender, PickingTexture.PickingResponse response) => answered.TrySetResult(response.PixelInfo);

        var ignoring = select ? null : await OnUi(() => scene!.IgnorePicks(), cancellationToken).ConfigureAwait(false);

        picker.OnPicked += OnPicked;

        try
        {
            using var rendering = RenderLoopThread.BeginAutomationRendering();

            // Select intent leaves the entity info window closed; the answer arrives a frame later.
            picker.RequestNextFrame(x.Value, y.Value, PickingTexture.PickingIntent.Select);

            if (!await WaitOnRenderLoop(answered.Task, cancellationToken).ConfigureAwait(false))
            {
                return McpToolResult.Error($"The picker did not answer within {FrameTimeout.TotalSeconds:F0}s.");
            }

            var pixel = await answered.Task.ConfigureAwait(false);

            return await OnUi(() => DescribePick(scene!, pixel, select), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            picker.OnPicked -= OnPicked;
            ignoring?.Dispose();
        }
    }

    private static McpToolResult DescribePick(GLSceneViewer scene, PickingTexture.PixelInfo pixel, bool selected)
    {
        // The viewer treats a set fourth channel as empty space too; translucent effects leave
        // garbage in the id otherwise.
        if (pixel.ObjectId == 0 || pixel.Unused2 != 0)
        {
            return McpToolResult.Json(new JsonObject
            {
                ["hit"] = false,
            });
        }

        var inSky = pixel.IsSkybox > 0;
        var node = inSky ? scene.SkyboxScene?.Find(pixel.ObjectId) : scene.Scene.Find(pixel.ObjectId);

        var result = new JsonObject
        {
            ["hit"] = true,
        };

        if (node == null)
        {
            result["object_id"] = pixel.ObjectId;
            result["note"] = "No scene node has this id.";
            return McpToolResult.Json(result);
        }

        result["node"] = DescribeNode(node, inSky);

        if (node.EntityData != null && scene is GLWorldViewer { LoadedWorld: { } world } && EntityByData(world, node.EntityData) is { } entity)
        {
            result["entity"] = DescribeEntity(world, entity);
        }

        if (selected)
        {
            result["selected"] = true;
        }

        return McpToolResult.Json(result);
    }
}
#endif
