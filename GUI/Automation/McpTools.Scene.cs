#if DEBUG
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GUI.Types.Exporter;
using GUI.Types.GLViewers;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Automation;

/// <summary>Tools that trace through a scene, frame what is in it, and read the models it shows.</summary>
internal sealed partial class McpTools
{
    private const float DefaultTraceDistance = 65536f;

    /// <summary>Further than this from the origin is no place in any map, and more likely a mistake.</summary>
    private const float MaxCoordinate = 1_000_000f;

    private void RegisterSceneTools()
    {
        RegisterNodeTools();

        Add("trace", "Trace a ray through a 3D tab. Mode 'physics' traces the map's collision, the static world and the colliders of solid brush entities such as doors, the way the player's use trace does: it starts at the camera along its view unless given 'from' with 'direction' or 'to', can sweep a box with 'hull', and returns hit, position, normal, distance and the entity hit or world. A trace that starts inside a solid or an included trigger answers start_solid at distance 0. The pivot Alt orbits around is a ray along the view against the static world alone. Mode 'render' reads what is drawn at a pixel, the viewport centre by default: its node and mesh, and the world position the depth buffer puts it at with its distance from 'from', the camera the frame was drawn from, which can still be flying in after a load; a 3D sky surface is placed where the sky is seen. A pixel with nothing drawn answers hit false with background 'sky' or 'nothing'; one outside the render area is an error.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["mode"] = Prop("string", "What to trace against. Defaults to physics.", "physics", "render"),
                ["from"] = VectorProp("Physics: start point [x, y, z]. Defaults to the camera position."),
                ["direction"] = VectorProp("Physics: direction to trace in. Defaults to where the camera looks."),
                ["to"] = VectorProp("Physics: end point, instead of 'direction'."),
                ["max_distance"] = Prop("number", $"Physics: how far to trace along 'direction'. Defaults to {DefaultTraceDistance:F0}."),
                ["hull"] = VectorProp("Physics: half extents [x, y, z] of a box to sweep instead of a ray, against what the player collides with. The player stands in [16, 16, 36]."),
                ["triggers"] = Prop("boolean", "Physics: also stop at the enabled trigger volumes the viewer simulates, such as trigger_multiple, trigger_once and trigger_teleport. Trigger classes it does not simulate have no collision here. Defaults to false."),
                ["x"] = Prop("integer", "Render: pixel X in the render area. Defaults to the centre."),
                ["y"] = Prop("integer", "Render: pixel Y in the render area. Defaults to the centre."),
            }),
            Trace, SceneViewer);

        Add("frame", "Move the camera of a 3D tab to frame an entity, a node, a box, or with none of them the whole scene, landing there at once. A map frames the way focusing an entity in the entity list does, from where the camera has a clear view; nothing is selected and no layer is turned on.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["entity"] = Prop("integer", "Entity id from find_entities or pick."),
                ["node"] = Prop("string", NodeIdDescription),
                ["min"] = VectorProp("Lowest corner of a world box to frame, with 'max'."),
                ["max"] = VectorProp("Highest corner of a world box to frame, with 'min'."),
            }),
            Frame, SceneViewer);

        Add("get_model_info", "Facts about a model that its tab's sidebar does not show: its bones with their parents, bounds, attachments, hitbox sets, LOD and mesh counts, animations loaded, flex controllers, bone constraints, and the physics, NM skeleton and animation graph files it references, plus the material group, mesh groups and animation its node shows. Reads a model tab, or any model node of a 3D tab by id, such as a map prop from pick.",
            Schema(new JsonObject
            {
                ["tab"] = TabProp(),
                ["node"] = Prop("string", "Model node id from pick or get_entity. Defaults to the model of a model tab."),
            }),
            GetModelInfo, SceneViewer);
    }

    private static bool IsFinite(Vector3 vector) => float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);

    private static bool IsSanePosition(Vector3 position) => IsFinite(position) && MathF.Abs(position.X) <= MaxCoordinate && MathF.Abs(position.Y) <= MaxCoordinate && MathF.Abs(position.Z) <= MaxCoordinate;

    private static string NotAPosition(string name) => $"'{name}' must be finite and within {MaxCoordinate:F0} units of the origin.";

    private Task<McpToolResult> Trace(JsonObject args, CancellationToken cancellationToken) => (GetString(args, "mode") ?? "physics") switch
    {
        "physics" => TracePhysics(args, cancellationToken),
        "render" => TraceRender(args, cancellationToken),
        var other => Task.FromResult(McpToolResult.Error($"Unknown mode '{other}'. Use physics or render.")),
    };

    private async Task<McpToolResult> TracePhysics(JsonObject args, CancellationToken cancellationToken)
    {
        var from = GetVector(args, "from");
        var direction = GetVector(args, "direction");
        var to = GetVector(args, "to");
        var hull = GetVector(args, "hull");
        var maxDistance = GetFloat(args, "max_distance") ?? DefaultTraceDistance;
        var triggers = GetBool(args, "triggers") ?? false;

        if (direction != null && to != null)
        {
            return McpToolResult.Error("Pass either 'direction' or 'to', not both.");
        }

        foreach (var (name, point) in (ReadOnlySpan<(string, Vector3?)>)[("from", from), ("to", to)])
        {
            if (point != null && !IsSanePosition(point.Value))
            {
                return McpToolResult.Error(NotAPosition(name));
            }
        }

        if (direction != null && (!IsFinite(direction.Value) || direction.Value.LengthSquared() < 1e-12f))
        {
            return McpToolResult.Error("'direction' must be a finite vector that is not zero.");
        }

        if (hull != null && (!IsFinite(hull.Value) || hull.Value.X < 0f || hull.Value.Y < 0f || hull.Value.Z < 0f || hull.Value.MaxComponent() > 4096f))
        {
            return McpToolResult.Error("'hull' must be half extents between 0 and 4096.");
        }

        if (!float.IsFinite(maxDistance) || maxDistance <= 0f || maxDistance > 4f * MaxCoordinate)
        {
            return McpToolResult.Error($"'max_distance' must be more than 0 and at most {4f * MaxCoordinate:F0}.");
        }

        var (viewer, error) = await ActivateViewer<GLSceneViewer>(args, cancellationToken).ConfigureAwait(false);

        if (error != null)
        {
            return error;
        }

        var (physics, cameraLocation, cameraForward) = await OnUi(() =>
        {
            var camera = viewer!.Input.Camera;
            return (viewer.Renderer.EntitySystem.PhysicsWorld, camera.Location, EntityTransformHelper.EulerAnglesToForwardDirection(camera.GetQAngle()));
        }, cancellationToken).ConfigureAwait(false);

        if (physics == null)
        {
            return McpToolResult.Error("This tab has no physics world to trace against. Use mode 'render' for what is drawn.");
        }

        var start = from ?? cameraLocation;
        var end = to ?? start + Vector3.Normalize(direction ?? cameraForward) * maxDistance;

        if (Vector3.DistanceSquared(start, end) < 1e-6f)
        {
            return McpToolResult.Error("The trace starts and ends at the same point.");
        }

        // Off the UI thread, because holding a frame there can deadlock against a frame that is
        // waiting on the UI thread. Held because the entity system moves colliders as it ticks.
        var (hit, hitEntity, startInside) = await Task.Run(() =>
        {
            using var frame = viewer!.HoldFrame();

            var closest = hull is { } halfExtents
                ? physics.TraceAABB(start, end, halfExtents, "player")
                : physics.TraceRay(start, end, "player");

            BaseEntity? struck = null;
            BaseEntity? inside = null;

            foreach (var entity in viewer.Renderer.EntitySystem.Entities)
            {
                if (entity.IsRemoved || entity.Collider is not { IsEmpty: false } collider)
                {
                    continue;
                }

                if (IsTrigger(entity, entity.Classname)
                    ? !triggers || entity is BaseTrigger { IsEnabled: false } || (entity is not BaseTrigger && entity.Data?.GetBooleanProperty("startdisabled") == true)
                    : !entity.IsCollidable)
                {
                    continue;
                }

                if (hull is { } startHalfExtents ? collider.OverlapsVolume(start, startHalfExtents) : collider.ContainsPoint(start))
                {
                    inside ??= entity;
                    continue;
                }

                var result = hull is { } entityHalfExtents
                    ? collider.TraceAABB(start, end, entityHalfExtents)
                    : collider.TraceRay(start, end, "player");

                if (closest.MinimizeWith(result))
                {
                    struck = entity;
                }
            }

            return (closest, struck, inside);
        }, cancellationToken).ConfigureAwait(false);

        if (startInside != null)
        {
            hitEntity = startInside;
        }

        var startsInside = startInside != null || (hit.Hit && Vector3.Dot(hit.HitNormal, end - start) > 0f);

        return await OnUi(() =>
        {
            var result = new JsonObject
            {
                ["hit"] = hit.Hit,
                ["from"] = Round(start),
                ["to"] = Round(end),
            };

            if (startsInside)
            {
                result["hit"] = true;
                result["position"] = Round(start);
                result["distance"] = 0;
                result["start_solid"] = true;
            }
            else if (!hit.Hit)
            {
                return McpToolResult.Json(result);
            }
            else
            {
                result["position"] = Round(hit.HitPosition);
                result["normal"] = Round(hit.HitNormal);
                result["distance"] = Round(hit.Distance);

                if (hit.StartSolid)
                {
                    result["start_solid"] = true;
                }
            }

            if (hitEntity == null)
            {
                result["world"] = true;
            }
            else if (hitEntity.Data != null && viewer is GLWorldViewer { LoadedWorld: { } world } worldViewer && EntityByData(worldViewer, world, hitEntity.Data) is { } entity)
            {
                result["entity"] = DescribeEntity(entity, hitEntity);
            }
            else
            {
                result["entity"] = new JsonObject
                {
                    ["class"] = hitEntity.GetType().Name,
                };
            }

            return McpToolResult.Json(result);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpToolResult> TraceRender(JsonObject args, CancellationToken cancellationToken)
    {
        var x = GetInt(args, "x");
        var y = GetInt(args, "y");

        var (viewer, error) = await ActivateViewer<GLSceneViewer>(args, cancellationToken).ConfigureAwait(false);

        if (error != null)
        {
            return error;
        }

        var (read, readError) = await ReadPixel(viewer!, x, y, PickingTexture.PickingIntent.Select, viewerActs: false, null, cancellationToken).ConfigureAwait(false);

        if (readError != null)
        {
            return readError;
        }

        return await OnUi(() =>
        {
            var pixel = new JsonArray(read!.X, read.Y);

            if (read.Node == null)
            {
                var miss = NoHit(viewer!);
                miss["pixel"] = pixel;
                return McpToolResult.Json(miss);
            }

            var node = read.Node;
            var describer = new NodeDescriber(viewer!);

            var result = new JsonObject
            {
                ["hit"] = true,
                ["pixel"] = pixel,
            };

            if (UnprojectPixel(read) is { } position)
            {
                result["from"] = Round(read.CameraLocation);
                result["position"] = Round(position);
                result["distance"] = Round(Vector3.Distance(read.CameraLocation, position));
            }
            else
            {
                result["note"] = "The picking pass wrote no depth here, so the position is unknown.";
            }

            var brief = new JsonObject
            {
                ["id"] = describer.Id(node),
                ["type"] = node.GetType().Name.Replace("SceneNode", string.Empty, StringComparison.Ordinal),
            };

            if (!string.IsNullOrEmpty(node.Name))
            {
                brief["name"] = node.Name;
            }

            if (read.Scene?.WorldGroup != null)
            {
                brief["sky"] = true;
            }

            result["node"] = brief;

            if (DescribeMesh(node, read.Pixel.MeshId) is { } mesh)
            {
                result["mesh"] = mesh;
            }

            if (node.EntityData != null && viewer is GLWorldViewer { LoadedWorld: { } world } worldViewer && EntityByData(worldViewer, world, node.EntityData) is { } entity)
            {
                result["entity"] = DescribeEntity(entity, SpawnedEntities(viewer).GetValueOrDefault(entity.Data));
            }

            return McpToolResult.Json(result);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpToolResult> Frame(JsonObject args, CancellationToken cancellationToken)
    {
        var entityId = GetInt(args, "entity");
        var nodeId = GetString(args, "node");
        var min = GetVector(args, "min");
        var max = GetVector(args, "max");

        if ((min == null) != (max == null))
        {
            return McpToolResult.Error("Pass both 'min' and 'max', or neither.");
        }

        if ((entityId != null ? 1 : 0) + (nodeId != null ? 1 : 0) + (min != null ? 1 : 0) > 1)
        {
            return McpToolResult.Error("Pass at most one of 'entity', 'node' and 'min' with 'max'.");
        }

        if (min != null && max != null)
        {
            if (!IsSanePosition(min.Value) || !IsSanePosition(max.Value))
            {
                return McpToolResult.Error(NotAPosition("min' and 'max"));
            }

            if (min.Value.X > max.Value.X || min.Value.Y > max.Value.Y || min.Value.Z > max.Value.Z)
            {
                return McpToolResult.Error("Every component of 'min' must be at most the same component of 'max'.");
            }
        }

        return await WithViewer<GLSceneViewer>(args, viewer =>
        {
            AABB bounds;

            if (entityId != null)
            {
                if (viewer is not GLWorldViewer { LoadedWorld: { } world } worldViewer)
                {
                    return McpToolResult.Error("'entity' needs a tab with a map loaded.");
                }

                if (EntityById(worldViewer, world, entityId.Value) is not { } entity)
                {
                    return NoSuchEntity(world, entityId.Value);
                }

                bounds = viewer.Renderer.FindNode(entity.Data) is { } entityNode
                    ? GLWorldViewer.FocusBounds(entityNode)
                    : GLWorldViewer.FocusBounds(RenderedPosition(entity, SpawnedEntities(viewer).GetValueOrDefault(entity.Data)));
            }
            else if (nodeId != null)
            {
                bounds = GLWorldViewer.FocusBounds(NodeById(viewer, nodeId));
            }
            else if (min != null && max != null)
            {
                bounds = new AABB(min.Value, max.Value);
            }
            else if (SceneBounds(viewer) is { } sceneBounds)
            {
                bounds = sceneBounds;
            }
            else
            {
                return McpToolResult.Error("Nothing in the scene is drawn with bounds to frame.");
            }

            if (viewer is GLWorldViewer map)
            {
                map.FocusCameraOn(bounds);
                LandCamera(map);
            }
            else
            {
                FrameWithoutWorld(viewer, bounds);
            }

            return McpToolResult.Json(new JsonObject
            {
                ["camera"] = DescribeCamera(viewer.Input.Camera),
                ["center"] = Round(bounds.Center),
                ["size"] = Round(bounds.Size),
            });
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The world bounds of every drawn node the main view shows, or null when there is none.</summary>
    private static AABB? SceneBounds(GLSceneViewer viewer)
    {
        AABB? bounds = null;

        foreach (var scene in viewer.Renderer.Scenes)
        {
            if (scene.WorldGroup != null)
            {
                continue;
            }

            foreach (var node in scene.AllNodes)
            {
                var size = node.BoundingBox.Size;

                if (!node.LayerEnabled || !node.Visible || !IsFinite(size) || size.X < 0f || size.MaxComponent() <= 0f || size.MaxComponent() > MaxCoordinate)
                {
                    continue;
                }

                var nodeBounds = node.BoundingBox.Transform(scene.ToViewerWorld);
                bounds = bounds is { } union ? union.Union(nodeBounds) : nodeBounds;
            }
        }

        return bounds;
    }

    /// <summary>
    /// Frames bounds in a viewer that has no world to keep a clear view in, standing off in the
    /// direction a map would prefer, at the distance that fits the bounds on screen.
    /// </summary>
    private static void FrameWithoutWorld(GLSceneViewer viewer, AABB bounds)
    {
        var size = bounds.Size * 1.25f;
        var direction = CameraPlacement.PreferredDirection(size);
        var distance = viewer.Input.Camera.GetFramingDistance(size, -direction);

        viewer.Input.SetCameraImmediate(bounds.Center + direction * distance, EntityTransformHelper.ForwardDirectionToEulerAngles(-direction));

        if (viewer.Input.OrbitModeAlways)
        {
            viewer.Input.OrbitTarget = bounds.Center;
        }
    }

    private async Task<McpToolResult> GetModelInfo(JsonObject args, CancellationToken cancellationToken)
    {
        var nodeId = GetString(args, "node");

        return await WithViewer<GLSceneViewer>(args, viewer =>
        {
            ModelSceneNode? node;

            if (nodeId != null)
            {
                var named = NodeById(viewer, nodeId);
                node = named as ModelSceneNode;

                if (node == null)
                {
                    return McpToolResult.Error($"Node '{nodeId}' is a {named.GetType().Name}, not a model.");
                }
            }
            else
            {
                node = viewer is GLWorldViewer ? null : viewer.Scene.AllNodes.OfType<ModelSceneNode>().FirstOrDefault();

                if (node == null)
                {
                    return McpToolResult.Error("This tab shows no model of its own. Pass 'node' with a model node id from pick or get_entity.");
                }
            }

            ValveResourceFormat.Resource? loaded = null;

            try
            {
                var model = TabModel(node);

                if (model == null && node.Name != null)
                {
                    loaded = viewer.GuiContext.FileLoaderNoCache.LoadFileCompiled(node.Name);
                    model = loaded?.DataBlock as Model;
                }

                if (model == null)
                {
                    return McpToolResult.Error($"Could not load the model '{node.Name}' of node '{nodeId}'.");
                }

                return McpToolResult.Json(DescribeModel(viewer, node, model));
            }
            finally
            {
                loaded?.Dispose();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The model the active tab was opened on, when it is the one the node shows.</summary>
    private static Model? TabModel(ModelSceneNode node)
        => Program.MainForm.Tabs.SelectedTab?.Tag is ExportData { DisposableContents: GUI.Types.Viewers.Resource viewer }
            && viewer.LoadedResource?.DataBlock is Model model
            && model.Name == node.Name
            ? model
            : null;

    private static JsonObject DescribeModel(GLSceneViewer viewer, ModelSceneNode node, Model model)
    {
        var result = new JsonObject
        {
            ["name"] = model.Name,
            ["node"] = NodeId(viewer, node),
        };

        var bounds = node.LocalBoundingBox;

        result["bounds"] = new JsonObject
        {
            ["min"] = Round(bounds.Min),
            ["max"] = Round(bounds.Max),
        };

        var bones = new JsonArray();

        foreach (var bone in model.Skeleton.Bones)
        {
            var entry = new JsonObject
            {
                ["name"] = bone.Name,
            };

            if (bone.Parent != null)
            {
                entry["parent"] = bone.Parent.Name;
            }

            if (bone.IsProceduralCloth)
            {
                entry["cloth"] = true;
            }

            bones.Add(entry);
        }

        result["bones"] = bones;

        if (model.Attachments.Count > 0)
        {
            var attachments = new JsonArray();

            foreach (var name in model.Attachments.Keys.Order(StringComparer.Ordinal))
            {
                attachments.Add(name);
            }

            result["attachments"] = attachments;
        }

        if (model.HitboxSets.Count > 0)
        {
            var hitboxSets = new JsonObject();

            foreach (var (name, hitboxes) in model.HitboxSets)
            {
                hitboxSets[name] = hitboxes.Length;
            }

            result["hitbox_sets"] = hitboxSets;
        }

        result["lods"] = model.LodInfo.HasDistinctLevels ? model.LodInfo.AvailableLevels.Count : 1;
        result["meshes"] = node.RenderableMeshes.Count;
        result["animations"] = node.Animations.Count;

        if (model.FlexControllers.Length > 0)
        {
            result["flex_controllers"] = model.FlexControllers.Length;
        }

        if (model.BoneConstraints.Count > 0)
        {
            result["bone_constraints"] = model.BoneConstraints.Count;
        }

        var physics = new JsonArray();

        foreach (var name in model.GetReferencedPhysNames())
        {
            physics.Add(name);
        }

        if (physics.Count > 0)
        {
            result["physics"] = physics;
        }

        if (model.GetEmbeddedPhys() != null)
        {
            result["embedded_physics"] = true;
        }

        if (model.NmSkeletonRefs.Length > 0)
        {
            result["nm_skeletons"] = new JsonArray([.. model.NmSkeletonRefs.Select(static name => (JsonNode?)name)]);
        }

        if (model.AnimGraph2References.Count > 0)
        {
            var graphs = new JsonObject();

            foreach (var (identifier, graphPath) in model.AnimGraph2References)
            {
                graphs[identifier] = graphPath;
            }

            result["animation_graphs"] = graphs;
        }

        var shown = new JsonObject();

        if (!string.IsNullOrEmpty(node.ActiveMaterialGroup))
        {
            shown["material_group"] = node.ActiveMaterialGroup;
        }

        if (node.GetActiveMeshGroups() is { Count: > 0 } meshGroups)
        {
            shown["mesh_groups"] = new JsonArray([.. meshGroups.Select(static name => (JsonNode?)name)]);
        }

        if (node.AnimationController.ActiveAnimation is { } animation)
        {
            shown["animation"] = animation.Name;
        }

        if (shown.Count > 0)
        {
            result["shown"] = shown;
        }

        return result;
    }
}
#endif
