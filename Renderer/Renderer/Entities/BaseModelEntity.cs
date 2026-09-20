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
    /// <summary>SolidType_t from CS2: https://s2v.app/SchemaExplorer/cs2/client/SolidType_t.</summary>
    public enum SolidType
    {
        /// <summary>Not solid.</summary>
        SOLID_NONE = 0,
        /// <summary>Brush model.</summary>
        SOLID_BSP = 1,
        /// <summary>Axis-aligned bounding box.</summary>
        SOLID_BBOX = 2,
        /// <summary>Oriented bounding box.</summary>
        SOLID_OBB = 3,
        /// <summary>Sphere.</summary>
        SOLID_SPHERE = 4,
        /// <summary>Point.</summary>
        SOLID_POINT = 5,
        /// <summary>The model's physics collision.</summary>
        SOLID_VPHYSICS = 6,
        /// <summary>Capsule.</summary>
        SOLID_CAPSULE = 7,
        /// <summary>Cylinder.</summary>
        SOLID_CYLINDER = 8,
    }

    /// <summary>Gets the solid type.</summary>
    public SolidType Solid { get; protected set; }

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
        Solid = KeyValues.ContainsKey("solid") ? KeyValues.GetEnumValue<SolidType>("solid") : SolidType.SOLID_NONE;
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
            particleNode.LayerName = Scene.ParticlesLayerName;
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

    /// <summary>Tints the model with <c>"R G B"</c> in 0-255.</summary>
    [EntityInput("Color")]
    protected void InputColor(EntityInputData data)
    {
        if (ModelNode is not { } node)
        {
            return;
        }

        if (data.Parameter == null || !EntityTransformHelper.TryParseVector3(data.Parameter.Trim(), out var color))
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' cannot take Color \"{Value}\", which is not \"R G B\"", Classname, TargetName, data.Parameter);
            return;
        }

        node.Tint = new Vector4(Vector3.Clamp(color / 255f, Vector3.Zero, Vector3.One), node.Tint.W);
    }

    /// <summary>Sets the model's alpha from 0-255.</summary>
    [EntityInput("Alpha")]
    protected void InputAlpha(EntityInputData data)
    {
        if (ModelNode is not { } node)
        {
            return;
        }

        var alpha = data.Float(float.NaN);

        if (float.IsNaN(alpha))
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' cannot take Alpha \"{Value}\", which is not a number", Classname, TargetName, data.Parameter);
            return;
        }

        node.Tint = node.Tint with { W = MathUtils.Saturate(alpha / 255f) };
    }
}
