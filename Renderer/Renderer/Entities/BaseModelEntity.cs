using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// An entity with something to draw.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CBaseModelEntity">CBaseModelEntity</seealso>
public abstract class BaseModelEntity : BaseEntity
{
    /// <summary>How an entity collides.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/client/SolidType_t">SolidType_t</seealso>
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
    /// Gets the node this entity draws as, or <see langword="null"/> when its model has no meshes,
    /// as with a brush compiled for collision alone.
    /// </summary>
    public ModelSceneNode? ModelNode { get; private set; }

    /// <summary>Gets the model this entity loaded, or <see langword="null"/> when it names none or it failed to load.</summary>
    protected Model? LoadedModel { get; private set; }

    /// <summary>Initializes a model entity from its keyvalues.</summary>
    protected BaseModelEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
        Solid = KeyValues.ContainsKey("solid") ? KeyValues.GetEnumValue<SolidType>("solid") : SolidType.SOLID_NONE;
    }

    /// <summary>
    /// Loads the model and makes it this entity's own node. Brush entities carry their geometry
    /// this way, in a model compiled next to the map.
    /// </summary>
    /// <returns>
    /// The model node, or the editor box from <see cref="BaseEntity.CreateRootNode"/> when there is
    /// nothing to draw, so a collision-only entity can still be seen and picked.
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

            // Stands in for the missing model
            if (fileLoader.LoadFile("models/dev/error.vmdl_c")?.DataBlock is Model errorModel)
            {
                return new ModelSceneNode(Scene, errorModel, Data?.GetStringProperty("skin"))
                {
                    Name = "error",
                };
            }

            return base.CreateRootNode();
        }

        LoadedModel = model;

        var modelNode = new ModelSceneNode(Scene, model, Data?.GetStringProperty("skin"))
        {
            Name = modelName,
            TintAlpha = Data?.GetRenderTint() ?? Vector4.One,
        };

        // Model-referenced particles spawn regardless of meshes
        var particleNodes = ParticleSceneNode.CreateModelParticles(Scene, model, modelNode);

        foreach (var particleNode in particleNodes)
        {
            particleNode.LayerName = Scene.ParticlesLayerName;
            Scene.Add(particleNode, true);
        }

        // Meshes are only known once the node is built, so a collision-only model's node is dropped.
        // A particle-only model keeps its node for the follow attachments.
        if (modelNode.HasMeshes || particleNodes.Count > 0)
        {
            // Not added here: the caller takes the returned node as the entity's own
            ModelNode = modelNode;
        }

        if (modelNode.HasMeshes)
        {
            // Physics is baked in the model's posed frame, but the raw mesh can sit in modeldoc's
            // working frame (de_nuke's doors differ by 90 degrees). Props are always posed by a
            // sequence, so apply the authored animation or the modeldoc ref.
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
            if (BuildsCollider)
            {
                Collider = new EntityCollider(physics);
                UpdateColliderTransform();
            }

            // Owned by the entity, not the model node: a collision-only brush has no model node,
            // and its hulls are all there is to show.
            foreach (var physicsNode in PhysSceneNode.CreatePhysSceneNodes(Scene, physics, modelName, Classname))
            {
                AddNode(physicsNode);
            }

            // intentionally skip default scene node if phys exists
            return ModelNode;
        }

        return ModelNode ?? base.CreateRootNode();
    }

    /// <summary>
    /// Gets whether the model's physics becomes a <see cref="BaseEntity.Collider"/>; its hulls are drawn
    /// either way. Read during construction, so overrides must not depend on instance state.
    /// </summary>
    protected virtual bool BuildsCollider => true;

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

        node.Tint = Vector3.Clamp(color / 255f, Vector3.Zero, Vector3.One);
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

        node.Alpha = MathUtils.Saturate(alpha / 255f);
    }

    /// <summary>
    /// Lights the model as if it stood at the named entity. The position is sampled once and does not follow it.
    /// </summary>
    [EntityInput("LightingOrigin")]
    protected void InputLightingOrigin(EntityInputData data)
    {
        if (ModelNode is not { } node || string.IsNullOrEmpty(data.Parameter))
        {
            return;
        }

        if (EntitySystem.FindByTargetName(data.Parameter) is not { } origin)
        {
            return;
        }

        node.LightingOrigin = origin.WorldOrigin;
        Scene.UpdateNodeEnvironmentMaps(node);
    }
}
