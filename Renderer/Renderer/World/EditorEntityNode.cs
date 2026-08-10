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
/// Shared by the two things that need it. The loader draws these for classnames the entity system does not
/// implement, and <see cref="Entities.BaseEntity"/> draws one for an entity that has nothing else to show,
/// so an implemented classname does not become invisible by virtue of being implemented.
/// </remarks>
internal static class EditorEntityNode
{
    /// <summary>Visibility layer these are drawn on, so they can be hidden apart from the world.</summary>
    internal const string ToolEntitiesLayerName = "Entities (editor only)";

    /// <summary>
    /// Builds the node for an entity, without adding it to the scene: the caller owns it, and decides
    /// where it goes and what drives it.
    /// </summary>
    /// <param name="scene">The scene the node is built for.</param>
    /// <param name="entity">The entity's keyvalues, stamped onto the node for the property viewer.</param>
    /// <param name="classname">The classname whose Hammer class supplies the icon.</param>
    /// <param name="transform">Where to place it. Ignored for the box, which drops scale, see below.</param>
    /// <param name="flags">Object type flags to stamp onto the node.</param>
    /// <param name="layerName">Visibility layer for the node.</param>
    /// <returns>The node, which is never <see langword="null"/> but may be a plain box.</returns>
    /// <exception cref="InvalidDataException">The Hammer class names an icon of a type not handled here.</exception>
    internal static SceneNode Create(
        Scene scene,
        Entity entity,
        string classname,
        Matrix4x4 transform,
        ObjectTypeFlags flags = ObjectTypeFlags.None,
        string layerName = ToolEntitiesLayerName)
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
            var color = hammerEntity?.Color ?? new Color32(255, 0, 255, 255);

            // Do not use transform because scales need to be ignored
            EntityTransformHelper.DecomposeTransformationMatrix(entity, out _, out var rotationMatrix, out var positionVector);

            return new SimpleBoxSceneNode(scene, color, new Vector3(16f))
            {
                Transform = rotationMatrix * Matrix4x4.CreateTranslation(positionVector),
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
                modelNode.Tint = new Vector4(SceneLight.GetEditorTint(entity), 1f);
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
                spriteNode.Tint = new Vector4(SceneLight.GetEditorTint(entity), 1f);
            }

            return spriteNode;
        }

        throw new InvalidDataException($"Got resource {resource.ResourceType} for class \"{classname}\"");
    }

    /// <summary>Gets whether a classname is one Hammer draws as a camera.</summary>
    /// <param name="classname">The entity's classname.</param>
    internal static bool IsCamera(string classname)
        => classname is "sky_camera"
        or "point_devshot_camera"
        or "point_camera_vertical_fov"
        or "point_camera";
}
