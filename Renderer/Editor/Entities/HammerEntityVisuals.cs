using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Helpers;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Entities;

/// <summary>
/// Draws entities the way Hammer shows them, with the helpers their classes declare in the FGD files of the
/// game: the model, icon or box an entity is placed as, the lines its class draws to the entities it names,
/// and a line along each of its entity I/O connections. None of it is what the game shows. It holds the
/// helpers of every entity of a view, of a compiled map or of a source map, so the shapes a selected entity
/// shows are drawn by the same helpers either way.
/// </summary>
public sealed class HammerEntityVisuals : IEntityToolVisuals
{
    /// <summary>Visibility layer of the stand-ins and helper lines, so they can be hidden apart from the world.</summary>
    public const string MarkerLayerName = "Entities (editor only)";

    /// <summary>Visibility layer of the lines along entity I/O connections.</summary>
    public const string ConnectionsLayerName = "Entity Connections";

    /// <summary>
    /// Visibility layer of the edges of the volumes entities have while they are not selected, such as the
    /// boxes of cubemaps and light probes. They clutter the view, so it is off until it is turned on.
    /// </summary>
    public const string HelpersLayerName = "Entity Helpers (editor only)";

    private static readonly Color32 ConnectionStartColor = new(0, 255, 0);
    private static readonly Color32 ConnectionEndColor = new(255, 0, 0);

    private readonly Lazy<ForgeGameData> fgd;
    private readonly ConditionalWeakTable<BaseEntity, EntityHelperOwner> gameEntities = [];
    private readonly ConditionalWeakTable<EntityLump.Entity, IHelperOwner> mapEntities = [];

    /// <summary>Gets the entity classes the entities are drawn by, loaded when they are first needed.</summary>
    public ForgeGameData Fgd => fgd.Value;

    /// <summary>Initializes the visuals for the entities of a game.</summary>
    /// <param name="fileLoader">The file loader of what is shown, which finds the FGD files of its game.</param>
    /// <param name="logger">Where it is said which classes are used.</param>
    public HammerEntityVisuals(IFileLoader fileLoader, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileLoader);
        ArgumentNullException.ThrowIfNull(logger);

        fgd = new Lazy<ForgeGameData>(() => EditorFgd.Load(fileLoader, logger));
    }

    /// <summary>Adds an entity of a source map, so the nodes it is drawn with are found to belong to it.</summary>
    /// <param name="entity">The entity, with its helpers.</param>
    public void AddMapEntity(IHelperOwner entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        mapEntities.AddOrUpdate(entity.KeyValues, entity);
    }

    /// <summary>Finds the entity a node is drawn for, with the helpers of its class.</summary>
    /// <param name="node">A node of the view.</param>
    /// <returns>The entity, or <see langword="null"/> when the node is not drawn for one.</returns>
    public IHelperOwner? FindEntity(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.EntityInstance is { Data: not null } entity)
        {
            return GetEntity(entity);
        }

        return node.EntityData != null && mapEntities.TryGetValue(node.EntityData, out var mapEntity) ? mapEntity : null;
    }

    // Every entity of the game has one set of helpers, made the first time anything asks for them
    internal EntityHelperOwner GetEntity(BaseEntity entity) => gameEntities.GetValue(entity, CreateEntity);

    private EntityHelperOwner CreateEntity(BaseEntity entity)
    {
        var owner = new EntityHelperOwner(entity, entity.SpawnData ?? entity.Data!, this);

        owner.CreateHelpers();
        return owner;
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetResourcesToPreload(string classname)
    {
        if (Fgd.FindClass(classname) is not { } gameClass)
        {
            yield break;
        }

        foreach (var declaration in gameClass.Helpers)
        {
            if (declaration.Name.Equals("iconsprite", StringComparison.OrdinalIgnoreCase))
            {
                if (IconHelper.GetDeclaredMaterialName(declaration) is { } material)
                {
                    yield return material;
                }
            }
            else if (declaration.Name is "editormodel" or "model" or "studio" or "studioprop" && ModelHelper.GetDeclaredModelName(declaration) is { } model)
            {
                yield return ModelHelper.GetModelName(model);
            }
        }
    }

    /// <inheritdoc/>
    public SceneNode? CreateStandIn(BaseEntity entity, ObjectTypeFlags flags)
    {
        if (entity.Data == null)
        {
            return null;
        }

        // The game draws what the entity shows in the game, so of the helpers of its class only the ones
        // that stand in for it are drawn
        var placed = new List<SceneNode>();
        var owner = GetEntity(entity);

        owner.PlacedNodes = placed;

        foreach (var helper in owner.Helpers)
        {
            if (EntityHelperOwner.IsStandIn(helper))
            {
                helper.AddToScene();
            }
        }

        owner.PlacedNodes = null;

        foreach (var node in placed)
        {
            node.Flags |= flags;
        }

        // The entity takes the first as its own, and owns the others along with it
        foreach (var node in placed.Skip(1))
        {
            entity.AddNode(node);
        }

        return placed.FirstOrDefault();
    }

    /// <inheritdoc/>
    public void AddEntityRelations(Scene scene, IReadOnlyList<BaseEntity> entities)
    {
        foreach (var entity in entities)
        {
            AddConnectionLines(scene, entity);
            AddToolShapes(entity);
        }
    }

    /// <summary>Gets whether a classname is one Hammer draws as a camera.</summary>
    internal static bool IsCamera(string classname)
        => classname is "sky_camera"
        or "point_devshot_camera"
        or "point_camera_vertical_fov"
        or "point_camera";

    // The lines the class of the entity declares to the entities its keyvalues name, and the shapes the
    // editor draws for it whether or not it is selected, none of which the game draws
    private void AddToolShapes(BaseEntity entity)
    {
        if (entity.Data == null)
        {
            return;
        }

        foreach (var helper in GetEntity(entity).Helpers)
        {
            if (helper is LineHelper)
            {
                helper.Link();
            }
            else if (EntityHelperOwner.IsToolShape(helper))
            {
                helper.AddToScene();
            }
        }
    }

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
            foreach (var target in entity.EntitySystem.FindTargets(EntitySystem.ConnectionTarget(connection, entity.NameFixup), caller: entity))
            {
                // A 3D sky shares names with the map it is placed in
                if (target.Scene.WorldGroup != scene.WorldGroup)
                {
                    continue;
                }

                matched = true;

                if (!alreadySeen.Add(target))
                {
                    continue;
                }

                var line = CreateConnectionLine(entity.Scene, start, target.Transform.Translation);
#if DEBUG
                line.Name = $"Line from {entity.Data.GetStringProperty("hammeruniqueid")} to {target.Data?.GetStringProperty("hammeruniqueid")}";
#endif

                // Owned by the entity the line starts at, so it leaves the scene with it
                entity.AddNode(line, followsEntity: false);
            }

            if (!matched)
            {
                scene.RendererContext.Logger.LogDebug("Skipping entity i/o output {TargetName}: no entity matches it", connection.TargetName);
            }
        }
    }

    /// <summary>Creates the line along an entity I/O connection, green at the caller and red at the target.</summary>
    internal static LineSceneNode CreateConnectionLine(Scene scene, Vector3 start, Vector3 end)
    {
        var origin = (start + end) / 2f;

        return new LineSceneNode(scene, start - origin, end - origin, ConnectionStartColor, ConnectionEndColor)
        {
            LayerName = ConnectionsLayerName,
            Transform = Matrix4x4.CreateTranslation(origin),
        };
    }
}
