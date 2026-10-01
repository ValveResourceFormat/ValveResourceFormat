using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Editor.Entities;

/// <summary>
/// Draws entities the way Hammer shows them, from what their Hammer classes declare: the model, sprite or
/// coloured box an entity is placed as, the helper lines its class draws to the entities it names, and a
/// line along each of its entity I/O connections. None of it is what the game shows.
/// </summary>
public sealed class HammerEntityVisuals : IEntityToolVisuals
{
    /// <summary>Visibility layer of the stand-ins and helper lines, so they can be hidden apart from the world.</summary>
    public const string MarkerLayerName = "Entities (editor only)";

    /// <summary>Visibility layer of the lines along entity I/O connections.</summary>
    public const string ConnectionsLayerName = "Entity Connections";

    private static readonly Color32 ConnectionStartColor = new(0, 255, 0);
    private static readonly Color32 ConnectionEndColor = new(255, 0, 0);

    /// <inheritdoc/>
    public IEnumerable<string> GetResourcesToPreload(string classname) => HammerEntities.Get(classname)?.Icons ?? [];

    /// <inheritdoc/>
    /// <exception cref="InvalidDataException">The Hammer class names an icon of a type not handled here.</exception>
    public SceneNode? CreateStandIn(BaseEntity entity, ObjectTypeFlags flags)
    {
        if (entity.Data is not { } data)
        {
            return null;
        }

        // Whatever the Hammer class draws, even a model, is the editor's preview and never what the game
        // shows, so it goes on the editor-only layer. A template and what it spawns stay grouped together.
        // This also hides entities the game does draw but that only get the Hammer model as a stand-in here,
        // such as CS2 weapons placed on the ground. They will show again once their class spawns its real model.
        var layerName = entity.LayerName == WorldLoader.TemplateLayerName ? WorldLoader.TemplateLayerName : MarkerLayerName;

        var scene = entity.Scene;
        var classname = entity.Classname;
        var hammerEntity = HammerEntities.Get(classname);
        string? filename = null;
        Resource? resource = null;

        foreach (var file in hammerEntity?.Icons ?? [])
        {
            filename = file;
            resource = scene.RendererContext.FileLoader.LoadFileCompiled(file);

            if (resource != null)
            {
                break;
            }
        }

        if (resource == null)
        {
            var color = hammerEntity?.Color ?? new Color32(128, 0, 128, 255);

            // Placed without the entity's scale, which a box must not take
            return new SimpleBoxSceneNode(scene, color, new Vector3(16f))
            {
                Transform = entity.RigidTransform,
                LayerName = layerName,
                Name = filename,
                EntityData = data,
                Flags = flags,
                KeepsMarkerSize = true,
            };
        }

        if (resource.ResourceType == ResourceType.Model && resource.DataBlock is Model modelData)
        {
            var modelNode = IsCamera(classname)
                ? new CameraSceneNode(scene, modelData)
                : new ModelSceneNode(scene, modelData, null, isWorldPreview: true) { Name = filename };

            modelNode.Transform = entity.Transform;
            modelNode.LayerName = layerName;
            modelNode.EntityData = data;
            modelNode.Flags |= flags;
            modelNode.KeepsMarkerSize = true;

            if (SceneLight.IsAccepted(classname).Accepted)
            {
                modelNode.TintAlpha = new Vector4(SceneLight.GetEditorTint(data), 1f);
            }

            modelNode.SetAnimationForWorldPreview("tools_preview");

            return modelNode;
        }

        if (resource.ResourceType == ResourceType.Material)
        {
            var spriteNode = new SpriteSceneNode(scene, scene.RendererContext, resource, entity.Transform.Translation)
            {
                LayerName = layerName,
                Name = filename,
                EntityData = data,
                Flags = flags | ObjectTypeFlags.NoShadows,
                KeepsMarkerSize = true,
            };

            if (SceneLight.IsAccepted(classname).Accepted)
            {
                // light_omni2 has no editor model in the fgd, only an icon, so the cost tint has
                // to go on the sprite for it to show up at all.
                spriteNode.TintAlpha = new Vector4(SceneLight.GetEditorTint(data), 1f);
            }

            return spriteNode;
        }

        throw new InvalidDataException($"Got resource {resource.ResourceType} for class \"{classname}\"");
    }

    /// <inheritdoc/>
    public void AddEntityRelations(Scene scene, IReadOnlyList<BaseEntity> entities)
    {
        foreach (var entity in entities)
        {
            AddConnectionLines(scene, entity);
            AddHelperLines(scene, entity);
        }
    }

    /// <summary>Gets whether a classname is one Hammer draws as a camera.</summary>
    private static bool IsCamera(string classname)
        => classname is "sky_camera"
        or "point_devshot_camera"
        or "point_camera_vertical_fov"
        or "point_camera";

    /// <summary>
    /// Draws the helper lines the entity's Hammer class declares, from the entity to the ones its
    /// keyvalues name, as the editor shows them.
    /// </summary>
    private static void AddHelperLines(Scene scene, BaseEntity entity)
    {
        if (entity.Data is not { } data || HammerEntities.Get(entity.Classname) is not { Lines.Length: > 0 } hammerEntity)
        {
            return;
        }

        var layerName = entity.LayerName == WorldLoader.TemplateLayerName ? WorldLoader.TemplateLayerName : MarkerLayerName;

        foreach (var line in hammerEntity.Lines)
        {
            if (data.GetStringProperty(line.StartValueKey) is not { } startValue
                || FindHelperLineEnd(scene, entity.EntitySystem, line.StartKey, startValue) is not { } startEntity)
            {
                continue;
            }

            var start = startEntity.Transform.Translation;
            var end = entity.Transform.Translation;

            if (line.EndKey != null && line.EndValueKey != null)
            {
                if (data.GetStringProperty(line.EndValueKey) is not { } endValue
                    || FindHelperLineEnd(scene, entity.EntitySystem, line.EndKey, endValue) is not { } endEntity)
                {
                    continue;
                }

                end = endEntity.Transform.Translation;
            }

            AddLine(scene, start, end, line.Color, line.Color, layerName);
        }
    }

    /// <summary>
    /// Finds the entity a helper line ends at. Lines address entities by name; the few Hammer classes that
    /// address AI nodes by <c>nodeid</c> instead do not appear in compiled maps.
    /// </summary>
    private static BaseEntity? FindHelperLineEnd(Scene scene, EntitySystem entitySystem, string key, string name)
        => key.Equals("targetname", StringComparison.OrdinalIgnoreCase)
            ? entitySystem.FindAllByTargetName(name, scene).FirstOrDefault()
            : null;

    /// <summary>Draws a line from the entity to every entity its entity I/O connections reach.</summary>
    private static void AddConnectionLines(Scene scene, BaseEntity entity)
    {
        if (entity.Data?.Connections is not { } connections)
        {
            return;
        }

        var start = entity.Transform.Translation;
        var alreadySeen = new HashSet<BaseEntity>(connections.Count);

        foreach (var connection in connections)
        {
            var matched = false;

            // The entity as the caller, so a connection aimed at !self reaches it
            foreach (var target in entity.EntitySystem.FindTargets(new EntityIOTarget(connection.TargetName, connection.TargetType), caller: entity))
            {
                // A 3D sky shares names with the map it is placed in
                if (target.Scene != scene)
                {
                    continue;
                }

                matched = true;

                if (!alreadySeen.Add(target))
                {
                    continue;
                }

                string? name = null;
#if DEBUG
                name = $"Line from {entity.Data.GetStringProperty("hammeruniqueid")} to {target.Data?.GetStringProperty("hammeruniqueid")}";
#endif

                AddLine(scene, start, target.Transform.Translation, ConnectionStartColor, ConnectionEndColor, ConnectionsLayerName, name);
            }

            if (!matched)
            {
                scene.RendererContext.Logger.LogDebug("Skipping entity i/o output {TargetName}: no entity matches it", connection.TargetName);
            }
        }
    }

    private static void AddLine(Scene scene, Vector3 start, Vector3 end, Color32 startColor, Color32 endColor, string layerName, string? name = null)
    {
        var origin = (start + end) / 2f;

        var lineNode = new LineSceneNode(scene, start - origin, end - origin, startColor, endColor)
        {
            LayerName = layerName,
            Transform = Matrix4x4.CreateTranslation(origin),
            Name = name,
        };

        scene.Add(lineNode, true);
    }
}
