using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Collision;
using ValveResourceFormat.Renderer.Editor.Helpers;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Entities;

/// <summary>
/// Draws entities the way Hammer shows them, with the helpers their classes declare for the editor of the
/// game: the model, icon or box an entity is placed as, the lines its class draws to the entities it names,
/// and a line along each of its entity I/O connections. None of it is what the game shows. It holds the
/// helpers of every entity of a view, of a compiled map or of a source map, so the shapes a selected entity
/// shows are drawn by the same helpers either way.
/// </summary>
public sealed class HammerEntityVisuals : IEntityToolVisuals
{
    /// <summary>
    /// Visibility layer of what the editor draws entities with that is hidden while it shows only what the game
    /// does, such as the boxes, icons and models entities stand in as. Turning it off is that option.
    /// </summary>
    public const string MarkerLayerName = "Entities (editor only)";

    /// <summary>Visibility layer of the lines along entity I/O connections.</summary>
    public const string ConnectionsLayerName = "Entity Connections";

    /// <summary>
    /// Visibility layer of the helpers the editor only draws while it shows helpers, such as lines to the
    /// entities an entity names, spheres of its radii and the edges of its volumes. Turning it off is that
    /// option, which also hides those helpers of a selected entity.
    /// </summary>
    public const string HelpersLayerName = "Entity Helpers (editor only)";

    // What a volume tied to an entity is most likely built from, where its class applies no material
    private const string TriggerMaterial = "materials/tools/toolstrigger.vmat";

    private static readonly Color32 ConnectionStartColor = new(0, 255, 0);
    private static readonly Color32 ConnectionEndColor = new(255, 0, 0);

    private readonly Lazy<EntityClassTable> classes;
    private readonly ConditionalWeakTable<BaseEntity, EntityHelperOwner> gameEntities = [];

    /// <summary>Gets the entity classes the entities are drawn by, loaded when they are first needed.</summary>
    public EntityClassTable Classes => classes.Value;

    /// <summary>Initializes the visuals for the entities of a game.</summary>
    /// <param name="fileLoader">The file loader of what is shown, which finds its game.</param>
    /// <param name="logger">Where it is said which classes are used.</param>
    public HammerEntityVisuals(IFileLoader fileLoader, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileLoader);
        ArgumentNullException.ThrowIfNull(logger);

        classes = new Lazy<EntityClassTable>(() => EntityClassCatalog.Get(fileLoader, logger));
    }

    /// <summary>Finds the entity a node is drawn for, with the helpers of its class.</summary>
    /// <param name="node">A node of the view.</param>
    /// <returns>The entity, or <see langword="null"/> when the node is not drawn for one.</returns>
    public IHelperOwner? FindEntity(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.EntityInstance is { Data: not null } entity ? GetEntity(entity) : null;
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
        if (Classes.FindClass(classname) is not { } entityClass)
        {
            yield break;
        }

        foreach (var declaration in entityClass.Helpers)
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
            if (helper.IsStandIn)
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

        owner.StandIn = placed.FirstOrDefault();
        return owner.StandIn;
    }

    /// <inheritdoc/>
    public IEnumerable<SceneNode> CreateCollisionVisuals(Scene scene, PhysAggregateData physics, string name, BaseEntity? entity)
    {
        // The class of an entity draws its volume with one material, as Hammer applies it to the faces of a new one
        var autoApplyMaterial = entity == null ? null : Classes.FindClass(entity.Classname)?.AutoApplyMaterial;

        // An entity whose model draws nothing is a volume, built from a tools material the compiled map no longer names
        var fallbackMaterial = entity is BaseModelEntity { ModelNode: null } ? TriggerMaterial : null;

        // A group built from a tools material is all the compiled map keeps of the faces Hammer drew with it
        return PhysSceneNode.Create(scene, physics, name, group => autoApplyMaterial
            ?? MapExtract.GetToolsMaterialForInteractAs(PhysAggregateData.GetInteractAsTags(physics.CollisionAttributes[group]))
            ?? fallbackMaterial);
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

    // The lines the class of the entity declares to the entities its keyvalues name, and the shapes the
    // editor draws for it whether or not it is selected, none of which the game draws
    private void AddToolShapes(BaseEntity entity)
    {
        if (entity.Data == null)
        {
            return;
        }

        var helpers = GetEntity(entity).Helpers;

        foreach (var helper in helpers)
        {
            if (helper.IsToolShape)
            {
                helper.AddToScene();
            }
        }

        // Every helper links, as the ones of a source map do, once its shapes are in the scene
        foreach (var helper in helpers)
        {
            helper.Link();
        }
    }

    // A 3D sky shares names with the map it is placed in, and the entity is the caller a connection aimed at !self reaches
    private void AddConnectionLines(Scene scene, BaseEntity entity)
    {
        if (entity.Data?.Connections is { } connections)
        {
            AddConnectionLines(GetEntity(entity), connections, connection => entity.EntitySystem
                .FindTargets(EntitySystem.ConnectionTarget(connection, entity.NameFixup), caller: entity)
                .Where(target => target.Scene.WorldGroup == scene.WorldGroup && (target.SpawnData ?? target.Data) != null)
                .Select(GetEntity));
        }
    }

    /// <summary>Draws a line from an entity to every entity its entity I/O connections reach, once for each.</summary>
    /// <param name="entity">The entity the connections start at, which owns the lines.</param>
    /// <param name="connections">The connections.</param>
    /// <param name="findTargets">Finds the entities a connection reaches.</param>
    internal static void AddConnectionLines(IHelperOwner entity, IReadOnlyList<EntityLump.Connection> connections, Func<EntityLump.Connection, IEnumerable<IHelperOwner>> findTargets)
    {
        var start = entity.Transform.Translation;
        var alreadySeen = new HashSet<IHelperOwner>(connections.Count);

        foreach (var connection in connections)
        {
            var matched = false;

            foreach (var target in findTargets(connection))
            {
                matched = true;

                if (!alreadySeen.Add(target))
                {
                    continue;
                }

                string? name = null;
#if DEBUG
                name = $"Line from {entity.KeyValues.GetStringProperty("hammeruniqueid")} to {target.KeyValues.GetStringProperty("hammeruniqueid")}";
#endif

                // Owned by the entity the line starts at, so it leaves the scene with it
                entity.AddNode(CreateConnectionLine(entity.Scene, start, target.Transform.Translation, name));
            }

            if (!matched)
            {
                entity.Scene.RendererContext.Logger.LogDebug("Skipping entity i/o output {TargetName}: no entity matches it", connection.TargetName);
            }
        }
    }

    /// <summary>Creates the line along an entity I/O connection, green at the caller and red at the target.</summary>
    internal static ToolSceneNode CreateConnectionLine(Scene scene, Vector3 start, Vector3 end, string? name = null)
    {
        var node = new ToolSceneNode(scene)
        {
            Name = name,
            LayerName = ConnectionsLayerName,
        };

        node.Draw(context =>
        {
            context.PushRenderMode(ToolRenderMode.Wireframe);
            context.DrawLine(start, end, ConnectionStartColor, ConnectionEndColor);
            context.PopRenderMode();
        });

        return node;
    }
}
