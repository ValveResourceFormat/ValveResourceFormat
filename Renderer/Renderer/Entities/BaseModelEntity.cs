using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// An entity with something to draw, Source's <c>CBaseModelEntity</c>.
/// </summary>
public abstract class BaseModelEntity : BaseEntity
{
    /// <summary>
    /// Gets the node this entity draws as, or <see langword="null"/> when its model has no meshes. A brush
    /// compiled for collision alone is the usual reason.
    /// </summary>
    public ModelSceneNode? ModelNode { get; private set; }

    /// <summary>
    /// Initializes a model entity from its keyvalues.
    /// </summary>
    protected BaseModelEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>
    /// Loads the model and makes it this entity's own node. Source's <c>SetModel</c>, done for the entity
    /// rather than by it: deriving from this class is the statement that there is a model to set up. Brush
    /// entities carry their geometry this way, in a model compiled next to the map.
    /// </summary>
    /// <returns>
    /// The model node, or the editor box from <see cref="BaseEntity.CreateRootNode"/> when there is nothing
    /// to draw, so an entity compiled for collision alone can still be seen and picked.
    /// </returns>
    protected override SceneNode? CreateRootNode()
    {
        var modelName = ModelName;

        if (string.IsNullOrEmpty(modelName))
        {
            return base.CreateRootNode();
        }

        var fileLoader = EntitySystem.FileLoader;

        if (fileLoader.LoadFileCompiled(modelName)?.DataBlock is not Model model)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' failed to load model \"{Model}\"", Classname, TargetName, modelName);
            return base.CreateRootNode();
        }

        var modelNode = new ModelSceneNode(Scene, model, Data?.GetStringProperty("skin"))
        {
            Name = modelName,
            Tint = Data?.GetRenderTint() ?? Vector4.One,
        };

        // Whether it draws anything is only knowable once it is built, so a collision-only model costs one
        // node that is then dropped
        if (modelNode.HasMeshes)
        {
            // Not added here: the caller takes the returned node as the entity's own
            ModelNode = modelNode;
        }

        if (EntityCollider.LoadPhysics(model, fileLoader) is { } physics)
        {
            Collider = new EntityCollider(physics);
            UpdateColliderTransform();

            // Owned outright rather than hung off the model: a brush compiled for collision alone has no
            // model node to hang them from, and its hulls are then the only thing there is to show.
            foreach (var physicsNode in PhysSceneNode.CreatePhysSceneNodes(Scene, physics, modelName, Classname))
            {
                AddNode(physicsNode);
            }

            // intentionally skip default scene node if phys exists
            return ModelNode;
        }

        return ModelNode ?? base.CreateRootNode();
    }
}
