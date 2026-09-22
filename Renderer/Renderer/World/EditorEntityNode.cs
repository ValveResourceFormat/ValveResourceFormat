using System.IO;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.World;

/// <summary>
/// Builds the node an entity is drawn as in the editor: the model, sprite or icon its Hammer class names,
/// falling back to a coloured box. What Hammer itself shows for an entity that has no geometry of its own.
/// </summary>
/// <remarks>
/// <see cref="Entities.BaseEntity"/> draws one for an entity with nothing else to show, so an entity without
/// geometry, implemented or not, is still seen and can be picked.
/// </remarks>
internal static class EditorEntityNode
{
    /// <summary>Visibility layer these are drawn on, so they can be hidden apart from the world.</summary>
    internal const string LayerName = "Entities (editor only)";

    /// <summary>Visibility layer of the entities a <c>point_template</c> spawns, and of the template itself.</summary>
    internal const string TemplateLayerName = "Template Entities";

    /// <summary>
    /// Builds the node for an entity, without adding it to the scene: the caller owns it, and decides
    /// where it goes and what drives it.
    /// </summary>
    /// <param name="scene">The scene the node is for.</param>
    /// <param name="entity">The entity keyvalues.</param>
    /// <param name="classname">The classname whose Hammer icon to draw.</param>
    /// <param name="transform">Where an icon goes.</param>
    /// <param name="boxTransform">Where the box goes when there is no icon, without the scale a box must not take.</param>
    /// <param name="flags">Flags for the node.</param>
    /// <param name="layerName">The layer for the node.</param>
    /// <returns>The node, which is never <see langword="null"/> but may be a plain box.</returns>
    /// <exception cref="InvalidDataException">The Hammer class names an icon of a type not handled here.</exception>
    internal static SceneNode Create(
        Scene scene,
        Entity entity,
        string classname,
        Matrix4x4 transform,
        Matrix4x4 boxTransform,
        ObjectTypeFlags flags,
        string layerName)
    {
        var hammerEntity = HammerEntities.Get(classname);
        string? filename = null;
        Resource? resource = null;

        if (hammerEntity?.Icons.Length > 0)
        {
            foreach (var file in hammerEntity.Icons)
            {
                filename = file;

                resource = scene.RendererContext.FileLoader.LoadFileCompiled(file);

                if (resource != null)
                {
                    break;
                }
            }
        }

        if (resource == null)
        {
            var color = hammerEntity?.Color ?? new Color32(128, 0, 128, 255);

            return new SimpleBoxSceneNode(scene, color, new Vector3(16f))
            {
                Transform = boxTransform,
                LayerName = layerName,
                Name = filename,
                EntityData = entity,
                Flags = flags,
            };
        }

        if (resource.ResourceType == ResourceType.Model && resource.DataBlock is Model modelData)
        {
            var modelNode = IsCamera(classname)
                ? new CameraSceneNode(scene, modelData)
                : new ModelSceneNode(scene, modelData, null, isWorldPreview: true) { Name = filename };

            modelNode.Transform = transform;
            modelNode.LayerName = layerName;
            modelNode.EntityData = entity;
            modelNode.Flags |= flags;

            if (SceneLight.IsAccepted(classname).Accepted)
            {
                modelNode.TintAlpha = new Vector4(SceneLight.GetEditorTint(entity), 1f);
            }

            modelNode.SetAnimationForWorldPreview("tools_preview");

            return modelNode;
        }

        if (resource.ResourceType == ResourceType.Material)
        {
            var spriteNode = new SpriteSceneNode(scene, scene.RendererContext, resource, transform.Translation)
            {
                LayerName = layerName,
                Name = filename,
                EntityData = entity,
                Flags = flags,
            };

            if (SceneLight.IsAccepted(classname).Accepted)
            {
                // light_omni2 has no editor model in the fgd, only an icon, so the cost tint has
                // to go on the sprite for it to show up at all.
                spriteNode.TintAlpha = new Vector4(SceneLight.GetEditorTint(entity), 1f);
            }

            return spriteNode;
        }

        throw new InvalidDataException($"Got resource {resource.ResourceType} for class \"{classname}\"");
    }

    /// <summary>Gets whether a classname is one Hammer draws as a camera.</summary>
    internal static bool IsCamera(string classname)
        => classname is "sky_camera"
        or "point_devshot_camera"
        or "point_camera_vertical_fov"
        or "point_camera";
}
