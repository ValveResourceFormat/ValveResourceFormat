using System.Linq;
using Box3D;
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

    /// <summary>Gets the model this entity loaded, or <see langword="null"/> when it names none or it failed to load.</summary>
    protected Model? LoadedModel { get; private set; }

    /// <summary>
    /// Whether this entity mirrors its collider into the rigid body world as a kinematic mover, so
    /// props collide with it and a door swings them aside. A class that runs its own body - a
    /// physics prop - turns this off.
    /// </summary>
    protected virtual bool UsesMoverBody => true;

    /// <summary>
    /// Whether the collision debug nodes are built with the model, posed at bind under the entity
    /// transform. An entity that visualizes its physics itself - a ragdoll moving each part's
    /// hull with its body - opts out and builds its own.
    /// </summary>
    protected virtual bool CreatesPhysDebugNodes => true;

    // The kinematic mirror of the collider, and whether it currently collides; a mover the map
    // makes non-solid takes its body along
    private Body moverBody;
    private bool hasMoverBody;
    private bool moverBodyEnabled;
    private bool moverBodyIsAnchorOnly;

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

            // Shown in place of the missing model, the way the engine does, so the gap is visible
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
            if (Scene.EntitiesCollide && BuildsCollider)
            {
                Collider = new EntityCollider(physics);

                // The same collision again as a kinematic body, so props collide with this entity and
                // a moving one - a door - carries them with a real velocity
                if (UsesMoverBody && !IsTrigger
                    && EntitySystem.Physics.CreateMoverBody(physics, Origin,
                        EntityTransformHelper.EulerAnglesToQuaternion(Angles)) is { } body)
                {
                    moverBody = body;
                    hasMoverBody = true;
                    moverBodyEnabled = true;
                    EntitySystem.Physics.Register(body, this);
                }

                UpdateColliderTransform();
            }

            // Owned outright rather than hung off the model: a brush compiled for collision alone has no
            // model node to hang them from, and its hulls are then the only thing there is to show.
            if (CreatesPhysDebugNodes)
            {
                foreach (var physicsNode in PhysSceneNode.CreatePhysSceneNodes(Scene, physics, modelName, Classname))
                {
                    AddNode(physicsNode);
                }
            }

            // intentionally skip default scene node if phys exists
            return ModelNode;
        }

        return ModelNode ?? base.CreateRootNode();
    }

    /// <summary>
    /// Gets whether the model's physics becomes a <see cref="BaseEntity.Collider"/>. Its hulls are drawn
    /// either way. Read while the entity is constructed, so an override must not depend on its own state.
    /// </summary>
    protected virtual bool BuildsCollider => true;

    /// <summary>
    /// Finds the rigid body a physics constraint anchored at <paramref name="anchor"/> holds this entity
    /// by: its kinematic mirror, so a constraint can hang things off a moving brush. A brush with no
    /// mirror - its collision is a triangle mesh, which a moving body cannot carry - gets a shapeless
    /// one on demand, which only follows the entity for the joint's sake.
    /// </summary>
    internal virtual bool TryGetConstraintBody(Vector3 anchor, out Body body)
    {
        if (!hasMoverBody && UsesMoverBody && Scene.EntitiesCollide)
        {
            moverBody = EntitySystem.Physics.World.CreateKinematicBody(Origin, EntityTransformHelper.EulerAnglesToQuaternion(Angles));
            hasMoverBody = true;
            moverBodyEnabled = true;
            moverBodyIsAnchorOnly = true;
            EntitySystem.Physics.Register(moverBody, this);
        }

        body = moverBody;
        return hasMoverBody;
    }

    /// <inheritdoc/>
    protected override void UpdateColliderTransform()
    {
        base.UpdateColliderTransform();

        if (!hasMoverBody)
        {
            return;
        }

        // Solidity the map toggles takes the body along, so a door made passable stops pushing. A
        // shapeless anchor pushes nothing and always follows, since disabling it would drop its joints.
        var shouldCollide = moverBodyIsAnchorOnly || (IsSolid && !IsTrigger && !IsRemoved);

        if (shouldCollide != moverBodyEnabled)
        {
            moverBodyEnabled = shouldCollide;

            if (shouldCollide)
            {
                moverBody.Enable();
            }
            else
            {
                moverBody.Disable();
            }
        }

        if (!shouldCollide)
        {
            return;
        }

        var rotation = EntityTransformHelper.EulerAnglesToQuaternion(Angles);

        // Moved with a velocity rather than teleported, so the solver sweeps props aside with the
        // mover's real speed; a jump across the map is not a sweep, so that snaps instead
        if (Vector3.DistanceSquared(moverBody.Position, Origin) > 256f * 256f)
        {
            moverBody.SetTransform(Origin, rotation);
        }
        else
        {
            moverBody.MoveTowards(Origin, rotation, EntitySystem.TickInterval, wake: true);
        }
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        base.OnRemove();

        if (hasMoverBody)
        {
            EntitySystem.PhysicsOrNull?.Forget(moverBody);
            moverBody.Destroy();
            hasMoverBody = false;
        }
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
}
