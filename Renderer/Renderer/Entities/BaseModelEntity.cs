using System.Linq;
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

        // Model-referenced particles spawn regardless of meshes, as the plain loader path does
        var particleNodes = ParticleSceneNode.CreateModelParticles(Scene, model, modelNode);

        foreach (var particleNode in particleNodes)
        {
            particleNode.LayerName = "Particles";
            Scene.Add(particleNode, true);
        }

        // Whether it draws anything is only knowable once it is built, so a collision-only model costs one
        // node that is then dropped. A particle-only model keeps its node for the follow attachments.
        if (modelNode.HasMeshes || particleNodes.Count > 0)
        {
            // Not added here: the caller takes the returned node as the entity's own
            ModelNode = modelNode;
        }

        if (modelNode.HasMeshes)
        {
            // The compiler bakes physics in the model's posed frame, while the raw mesh can sit in
            // modeldoc's working frame (de_nuke's doors are 90 degrees apart between the two). The game
            // always poses a prop with a sequence, so the authored animation or the modeldoc ref is applied.
            var animation = Data?.GetStringProperty("startinganim")
                ?? Data?.GetStringProperty("defaultanim")
                ?? Data?.GetStringProperty("idleanim");

            if (animation != null && modelNode.SetAnimationForWorldPreview(animation))
            {
                if (Data?.GetBooleanProperty("holdanimation") == true)
                {
                    modelNode.AnimationController.PauseLastFrame();
                }
            }
            else
            {
                modelNode.SetAnimationForWorldPreview("ref");
            }

            var body = Data?.GetIntegerProperty("body", -1L) ?? -1L;

            if (body != -1L)
            {
                modelNode.SetActiveMeshGroups(modelNode.GetMeshGroups().Skip((int)body).Take(1));
            }
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
