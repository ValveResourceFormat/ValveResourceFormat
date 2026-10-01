using System.Globalization;
using System.Linq;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Editor.Selection;

/// <summary>
/// Draws the helpers for a <see cref="SelectionSet"/>: bounds with edge lengths, the volume a selected
/// entity covers, the node's name, and the cubemaps and light probe it uses in the matching render modes.
/// The outline itself is drawn by the renderer for every node with <see cref="SceneNode.IsSelected"/>.
/// </summary>
public sealed class SelectionVisuals : LineDebugRenderer, IDisposable
{
    private static readonly Vector2 NodeNameOffset = new(0, -20);

    private readonly SelectionSet selection;
    private readonly List<SimpleVertex> vertices = new(48);
    private readonly HashSet<SceneLightProbe> probesWithGrid = [];
    private readonly HashSet<SceneLightProbe> probesInUse = [];

    private bool disableDepth;
    private bool debugCubeMaps;
    private bool debugLightProbes;

    /// <summary>Creates the line buffer the helpers are drawn with.</summary>
    /// <param name="rendererContext">Renderer context for loading shaders.</param>
    /// <param name="selection">The selection to draw.</param>
    public SelectionVisuals(RendererContext rendererContext, SelectionSet selection)
        : base(rendererContext, nameof(SelectionVisuals))
    {
        this.selection = selection;
    }

    /// <summary>Rebuilds the helper lines and labels for the current selection.</summary>
    /// <param name="renderContext">Render context providing camera and scene state.</param>
    /// <param name="updateContext">Update context providing the text renderer.</param>
    public void Update(Scene.RenderContext renderContext, Scene.UpdateContext updateContext)
    {
        var nodes = selection.Nodes;

        UpdateLightProbeGrids(nodes);

        disableDepth = nodes.Count > 1;

        if (nodes.Count == 0)
        {
            // We don't need to reupload an empty array
            Clear();
            return;
        }

        foreach (var node in nodes)
        {
            AddNodeHelpers(renderContext, updateContext.TextRenderer, node);
        }

        Upload(vertices);

        vertices.Clear();
    }

    /// <summary>Draws the helper lines built by the last <see cref="Update"/>.</summary>
    public void Render()
    {
        RenderLines(disableDepth);
    }

    /// <summary>Updates which debug helpers (cubemaps, light probes) are drawn for the active render mode.</summary>
    /// <param name="mode">The render mode name from the viewer.</param>
    public void SetRenderMode(string mode)
    {
        debugCubeMaps = mode == "Cubemaps";
        debugLightProbes = mode is "Irradiance" or "Illumination";
    }

    /// <summary>Removes the light probe grids this drew and deletes the line buffer.</summary>
    public void Dispose()
    {
        foreach (var probe in probesWithGrid)
        {
            probe.RemoveDebugGridSpheres();
        }

        probesWithGrid.Clear();

        Delete();
    }

    // The grids are scene nodes, so they are added and removed here on the render thread, by comparing
    // against what was shown last frame, rather than whenever the selection changes.
    private void UpdateLightProbeGrids(IReadOnlyList<SceneNode> nodes)
    {
        probesInUse.Clear();

        if (debugLightProbes)
        {
            foreach (var node in nodes)
            {
                if (node.LightProbeBinding is { } probe)
                {
                    probesInUse.Add(probe);
                }
            }
        }

        foreach (var probe in probesWithGrid)
        {
            if (!probesInUse.Contains(probe))
            {
                probe.RemoveDebugGridSpheres();
            }
        }

        foreach (var probe in probesInUse)
        {
            probe.CreateDebugGridSpheres();
        }

        probesWithGrid.Clear();
        probesWithGrid.UnionWith(probesInUse);
    }

    private void AddNodeHelpers(Scene.RenderContext renderContext, TextRenderer textRenderer, SceneNode node)
    {
        var camera = renderContext.Camera;
        var nodeName = node.Name ?? node.GetType().Name;

        // Drawn with the main camera, so 3D sky nodes are outlined where they appear in the world
        var toWorld = node.Scene.ToViewerWorld;
        var bounds = node.BoundingBox.Transform(toWorld);

        if (!node.IsPointMarker)
        {
            AddBox(camera, textRenderer, vertices, node.Transform * toWorld, node.LocalBoundingBox, Color32.White, showSize: true);
        }

        if (debugCubeMaps)
        {
            AddCubemapLinks(renderContext, textRenderer, node, toWorld, bounds);
        }

        if (debugLightProbes && node.LightProbeBinding is { } probe)
        {
            var probeTransform = probe.Transform * toWorld;

            AddBox(camera, textRenderer, vertices, probeTransform, probe.LocalBoundingBox, new(1.0f, 0.0f, 1.0f, 1.0f));
            ShapeSceneNode.AddLine(vertices, probeTransform.Translation, bounds.Center, new(1.0f, 0.0f, 1.0f, 1.0f));
        }

        if (node.EntityData != null)
        {
            var classname = node.EntityData.GetStringProperty("classname");
            if (classname != null)
            {
                nodeName = classname;
            }

            AddEntityVolume(camera, textRenderer, node, classname, toWorld);
        }

        // draw node name above the bounding box
        var position = bounds.Center;
        position.Z = bounds.Max.Z;

        textRenderer.AddTextBillboard(position, new TextRenderer.TextRenderRequest
        {
            Scale = 20f,
            Text = nodeName,
            CenterHorizontal = true,
            TextOffset = NodeNameOffset
        }, camera, fixedScale: false);
    }

    private void AddCubemapLinks(Scene.RenderContext renderContext, TextRenderer textRenderer, SceneNode node, in Matrix4x4 toWorld, in AABB bounds)
    {
        IEnumerable<SceneEnvMap> tiedEnvmaps = node.EnvMaps;
        if (renderContext.Scene.LightingInfo.CubemapType == CubemapType.CubemapArray)
        {
            var list = new List<SceneEnvMap>();
            foreach (var shaderId in node.ShaderEnvMapVisibility.GetVisibleShaderIndices())
            {
                var env = renderContext.Scene.LightingInfo.EnvMaps.FirstOrDefault(e => e.ShaderIndex == shaderId);
                if (env is SceneEnvMap sem)
                {
                    list.Add(sem);
                }
            }
            tiedEnvmaps = list;
        }

        var i = 0;

        foreach (var tiedEnvMap in tiedEnvmaps)
        {
            var envMapTransform = tiedEnvMap.Transform * toWorld;

            AddBox(renderContext.Camera, textRenderer, vertices, envMapTransform, tiedEnvMap.LocalBoundingBox, new(0.7f, 0.0f, 1.0f, 1.0f));

            if (renderContext.Scene.LightingInfo.CubemapType is CubemapType.IndividualCubemaps && i == 0)
            {
                ShapeSceneNode.AddLine(vertices, envMapTransform.Translation, bounds.Center, new(0.0f, 1.0f, 0.0f, 1.0f));
                i++;
                continue;
            }

            var fractionToTen = Math.Min((float)i / 10, 1.0f);
            var color = new Color32(1.0f, fractionToTen, fractionToTen, 1.0f);
            ShapeSceneNode.AddLine(vertices, envMapTransform.Translation, bounds.Center, color);
            i++;
        }
    }

    private void AddEntityVolume(Camera camera, TextRenderer textRenderer, SceneNode node, string? classname, in Matrix4x4 toWorld)
    {
        var entityData = node.EntityData!;

        if (TryGetVolumeBounds(classname, entityData, out var volumeBounds, out var volumeSpace))
        {
            var placement = node.IsPointMarker
                ? node.EntityInstance?.RigidTransform ?? EntityTransformHelper.ToRigidTransformationMatrix(entityData)
                : node.Transform;

            var volumeTransform = volumeSpace switch
            {
                VolumeSpace.Oriented => placement,
                VolumeSpace.WorldAligned => Matrix4x4.CreateTranslation(placement.Translation),
                _ => Matrix4x4.Identity,
            };

            AddBox(camera, textRenderer, vertices, volumeTransform * toWorld, volumeBounds, new(0.0f, 1.0f, 0.0f, 1.0f));

            disableDepth = true;
            return;
        }

        if (classname is not "light_barn" and not "light_omni2")
        {
            return;
        }

        var boundsMins = entityData.GetStringProperty("precomputedboundsmins");
        var boundsMaxs = entityData.GetStringProperty("precomputedboundsmaxs");
        var obbExtent = entityData.GetStringProperty("precomputedobbextent");
        var obbOrigin = entityData.GetStringProperty("precomputedobborigin");

        if (boundsMins != null && boundsMaxs != null && obbExtent != null && obbOrigin != null)
        {
            var precomputedBounds = new AABB(
                EntityTransformHelper.ParseVector3(boundsMins),
                EntityTransformHelper.ParseVector3(boundsMaxs)
            );

            var origin = Vector3.Transform(EntityTransformHelper.ParseVector3(obbExtent), toWorld);
            var extent = Vector3.Transform(EntityTransformHelper.ParseVector3(obbOrigin), toWorld);
            var lightPosition = Vector3.Transform(node.Transform.Translation, toWorld);

            AddBox(camera, textRenderer, vertices, toWorld, precomputedBounds, new(0.0f, 1.0f, 0.0f, 1.0f));

            ShapeSceneNode.AddLine(vertices, lightPosition, origin, new(0.0f, 0.0f, 1.0f, 1.0f));
            ShapeSceneNode.AddLine(vertices, lightPosition, extent, new(1.0f, 1.0f, 0.0f, 1.0f));
        }

        disableDepth = true;
    }

    private static int ClosestVertexInView(Camera camera, ReadOnlySpan<Vector3> vertices)
    {
        var minDistance = float.MaxValue;
        var closestIndex = -1;

        for (var i = 0; i < vertices.Length; i++)
        {
            if (camera.ViewFrustum.Intersects(vertices[i]))
            {
                var distance = Vector3.DistanceSquared(vertices[i], camera.Location);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = i;
                }
            }
        }

        return closestIndex;
    }

    private static void AddBox(Camera camera, TextRenderer textRenderer, List<SimpleVertex> vertices, in Matrix4x4 transform, in AABB box, Color32 color, bool showSize = false)
    {
        // Adding a box will add many vertices, so ensure the required capacity for it up front
        vertices.EnsureCapacity(vertices.Count + 2 * 12);

        ReadOnlySpan<Vector3> c =
        [
            Vector3.Transform(new Vector3(box.Min.X, box.Min.Y, box.Min.Z), transform),
            Vector3.Transform(new Vector3(box.Max.X, box.Min.Y, box.Min.Z), transform),
            Vector3.Transform(new Vector3(box.Max.X, box.Max.Y, box.Min.Z), transform),
            Vector3.Transform(new Vector3(box.Min.X, box.Max.Y, box.Min.Z), transform),
            Vector3.Transform(new Vector3(box.Min.X, box.Min.Y, box.Max.Z), transform),
            Vector3.Transform(new Vector3(box.Max.X, box.Min.Y, box.Max.Z), transform),
            Vector3.Transform(new Vector3(box.Max.X, box.Max.Y, box.Max.Z), transform),
            Vector3.Transform(new Vector3(box.Min.X, box.Max.Y, box.Max.Z), transform),
        ];

        ReadOnlySpan<(int Start, int End)> Lines =
        [
            (0, 1), (1, 2), (2, 3), (3, 0), // Bottom face
            (4, 5), (5, 6), (6, 7), (7, 4), // Top face
            (0, 4), (1, 5), (2, 6), (3, 7), // Vertical edges
        ];

        var closestIndex = showSize ? ClosestVertexInView(camera, c) : -1;

        for (var i = 0; i < Lines.Length; i++)
        {
            var line = Lines[i];

            if (closestIndex == line.Start || closestIndex == line.End)
            {
                var axis = i >= 8 ? 2 : i % 2;

                var axisColor = axis switch
                {
                    0 => new Color32(1.0f, 0.2f, 0.2f, 1),
                    1 => new Color32(0.2f, 0.8f, 0.2f, 1),
                    2 => new Color32(0.2f, 0.2f, 1.0f, 1),
                    _ => color,
                };

                var (v0, v1) = (c[line.Start], c[line.End]);
                var length = Vector3.Distance(v0, v1);

                textRenderer.AddTextBillboard(Vector3.Lerp(v0, v1, 0.5f), new TextRenderer.TextRenderRequest
                {
                    Scale = 13f,
                    Color = axisColor,
                    Text = length.ToString("0.##", CultureInfo.InvariantCulture),
                    CenterVertical = true,
                    CenterHorizontal = true,
                }, camera);

                ShapeSceneNode.AddLine(vertices, c[line.Start], c[line.End], axisColor);
                continue;
            }

            ShapeSceneNode.AddLine(vertices, c[line.Start], c[line.End], color);
        }
    }

    private enum VolumeSpace
    {
        /// <summary>Relative to the entity's origin, rotated with its angles.</summary>
        Oriented,

        /// <summary>Relative to the entity's origin, along the world axes.</summary>
        WorldAligned,

        /// <summary>In world coordinates, independent of where the entity is.</summary>
        World,
    }

    /// <summary>Gets the volume a box-shaped point entity covers, the box its Hammer helper draws.</summary>
    /// <param name="classname">The entity's classname.</param>
    /// <param name="entity">The entity keyvalues.</param>
    /// <param name="bounds">The volume, in the space <paramref name="space"/> names.</param>
    /// <param name="space">What the volume is relative to.</param>
    /// <returns><see langword="true"/> when the class is a box volume.</returns>
    private static bool TryGetVolumeBounds(string? classname, Entity entity, out AABB bounds, out VolumeSpace space)
    {
        space = VolumeSpace.Oriented;

        switch (classname)
        {
            case "env_cubemap":
                var radius = entity.GetFloatProperty("influenceradius");
                bounds = new AABB(-radius, -radius, -radius, radius, radius, radius);
                return true;

            case "info_visibility_box"
                or "info_cull_triangles":
                bounds = AABB.FromCenteredSize(entity.GetVector3Property("box_size"));
                return true;

            case "env_combined_light_probe_volume"
                or "env_light_probe_volume"
                or "env_volumetric_fog_volume"
                or "env_wind_volume"
                or "steampal_kill_volume"
                or "env_cubemap_box"
                or "sky_camera_volume"
                or "env_shake_volume"
                or "point_deathcam_bounds"
                or "light_importance_volume"
                or "info_dynamic_shadow_hint_box"
                or "snd_event_alignedbox"
                or "snd_event_orientedbox"
                or "snd_event_box_helper"
                or "snd_opvar_set_wind_obb"
                or "citadel_snd_obb"
                or "citadel_snd_base_music_obb"
                or "citadel_snd_stack_field_obb":
                bounds = new AABB(entity.GetVector3Property("box_mins"), entity.GetVector3Property("box_maxs"));
                return true;

            case "snd_opvar_set_obb"
                or "logic_npc_counter_obb":
                bounds = new AABB(entity.GetVector3Property("box_outer_mins"), entity.GetVector3Property("box_outer_maxs"));
                return true;

            case "snd_sound_area_obb":
                bounds = new AABB(entity.GetVector3Property("areamin"), entity.GetVector3Property("areamax"));
                return true;

            case "point_grabbable":
                bounds = new AABB(entity.GetVector3Property("limit_mins"), entity.GetVector3Property("limit_maxs"));
                return true;

            case "env_volumetric_fog_controller"
                or "visibility_hint":
                space = VolumeSpace.WorldAligned;
                bounds = new AABB(entity.GetVector3Property("box_mins"), entity.GetVector3Property("box_maxs"));
                return true;

            case "snd_opvar_set_aabb"
                or "logic_npc_counter_aabb":
                space = VolumeSpace.WorldAligned;
                bounds = new AABB(entity.GetVector3Property("box_outer_mins"), entity.GetVector3Property("box_outer_maxs"));
                return true;

            case "world_bounds":
                space = VolumeSpace.World;
                bounds = new AABB(entity.GetVector3Property("min"), entity.GetVector3Property("max"));
                return true;

            default:
                bounds = default;
                return false;
        }
    }
}
