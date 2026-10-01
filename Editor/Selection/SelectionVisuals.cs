using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Editor.Selection;

/// <summary>Which lighting a selected node uses is shown linked to it.</summary>
public enum LightingBindingDisplay
{
    /// <summary>None.</summary>
    None,

    /// <summary>The environment maps the node is shaded with.</summary>
    EnvMaps,

    /// <summary>The light probe volume the node is bound to.</summary>
    LightProbe,
}

/// <summary>
/// Draws the helpers for a <see cref="SelectionSet"/>: bounds with edge lengths, the volume a selected
/// entity covers, the node's name, and optionally the lighting it uses. The outline itself is drawn by
/// the renderer for every node with <see cref="SceneNode.IsSelected"/>.
/// </summary>
public sealed class SelectionVisuals : LineDebugRenderer, IDisposable
{
    private static readonly Vector2 NodeNameOffset = new(0, -20);
    private static readonly Color32 VolumeColor = new(0.0f, 1.0f, 0.0f, 1.0f);
    private static readonly Color32 LightingBindingColor = new(1.0f, 0.0f, 1.0f, 1.0f);

    private readonly SelectionSet selection;
    private readonly List<SimpleVertex> vertices = new(48);
    private readonly List<SceneEnvMap> envMaps = [];

    private bool disableDepth;

    /// <summary>Gets or sets which lighting is shown linked to each selected node.</summary>
    public LightingBindingDisplay LightingBindings { get; set; }

    /// <summary>Creates the line buffer the helpers are drawn with.</summary>
    /// <param name="rendererContext">Renderer context for loading shaders.</param>
    /// <param name="selection">The selection to draw.</param>
    public SelectionVisuals(RendererContext rendererContext, SelectionSet selection)
        : base(rendererContext, nameof(SelectionVisuals))
    {
        this.selection = selection;
    }

    /// <summary>Rebuilds the helper lines and labels for the current selection.</summary>
    /// <param name="renderContext">Render context providing the camera.</param>
    /// <param name="updateContext">Update context providing the text renderer.</param>
    public void Update(Scene.RenderContext renderContext, Scene.UpdateContext updateContext)
    {
        var nodes = selection.Nodes;

        disableDepth = nodes.Count > 1;

        if (nodes.Count == 0)
        {
            // We don't need to reupload an empty array
            Clear();
            return;
        }

        foreach (var node in nodes)
        {
            AddNodeHelpers(renderContext.Camera, updateContext.TextRenderer, node);
        }

        Upload(vertices);

        vertices.Clear();
    }

    /// <summary>Draws the helper lines built by the last <see cref="Update"/>.</summary>
    public void Render()
    {
        RenderLines(disableDepth);
    }

    /// <summary>Deletes the line buffer.</summary>
    public void Dispose()
    {
        Delete();
    }

    private void AddNodeHelpers(Camera camera, TextRenderer textRenderer, SceneNode node)
    {
        var nodeName = node.Name ?? node.GetType().Name;

        // Drawn with the main camera, so 3D sky nodes are outlined where they appear in the world
        var toWorld = node.Scene.ToViewerWorld;
        var bounds = node.BoundingBox.Transform(toWorld);

        if (!node.IsPointMarker)
        {
            BoxLines.AddWithSize(vertices, node.Transform * toWorld, node.LocalBoundingBox, Color32.White, camera, textRenderer);
        }

        if (node.EntityData != null)
        {
            var classname = node.EntityData.GetStringProperty("classname");
            if (classname != null)
            {
                nodeName = classname;
            }

            AddEntityVolume(node, classname, toWorld);
        }

        AddLightingBindings(node, toWorld, bounds.Center);

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

    private void AddLightingBindings(SceneNode node, in Matrix4x4 toWorld, Vector3 nodeCenter)
    {
        switch (LightingBindings)
        {
            case LightingBindingDisplay.EnvMaps:
                envMaps.Clear();
                node.Scene.GetEnvMapsUsedBy(node, envMaps);

                foreach (var envMap in envMaps)
                {
                    AddLink(envMap, toWorld, nodeCenter);
                }

                break;

            case LightingBindingDisplay.LightProbe when node.LightProbeBinding is { } probe:
                AddLink(probe, toWorld, nodeCenter);
                break;
        }
    }

    /// <summary>Outlines a lighting volume and connects it to the node that uses it.</summary>
    private void AddLink(SceneNode source, in Matrix4x4 toWorld, Vector3 nodeCenter)
    {
        var transform = source.Transform * toWorld;

        BoxLines.Add(vertices, transform, source.LocalBoundingBox, LightingBindingColor);
        ShapeSceneNode.AddLine(vertices, transform.Translation, nodeCenter, LightingBindingColor);
    }

    private void AddEntityVolume(SceneNode node, string? classname, in Matrix4x4 toWorld)
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

            BoxLines.Add(vertices, volumeTransform * toWorld, volumeBounds, VolumeColor);

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

            BoxLines.Add(vertices, toWorld, precomputedBounds, VolumeColor);

            ShapeSceneNode.AddLine(vertices, lightPosition, origin, new(0.0f, 0.0f, 1.0f, 1.0f));
            ShapeSceneNode.AddLine(vertices, lightPosition, extent, new(1.0f, 1.0f, 0.0f, 1.0f));
        }

        disableDepth = true;
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
