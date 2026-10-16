using System.Linq;
using Box3D;
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

    /// <summary>Gets the physics of the model, or <see langword="null"/> when it has none.</summary>
    public PhysAggregateData? Physics { get; private set; }

    /// <summary>Gets the model this entity loaded, or <see langword="null"/> when it names none or it failed to load.</summary>
    public Model? LoadedModel { get; private set; }

    /// <summary>
    /// Whether this entity mirrors its collider into the rigid body world as a kinematic mover, so
    /// props collide with it and a door swings them aside. A class that runs its own body - a
    /// physics prop - turns this off.
    /// </summary>
    protected virtual bool UsesMoverBody => true;

    // The kinematic mirror of the collider, and whether it currently collides; a mover the map
    // makes non-solid takes its body along
    private Body moverBody;
    private bool hasMoverBody;
    private bool moverBodyEnabled;
    private bool moverBodyIsAnchorOnly;

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

            // Owned, so they leave the scene with the entity
            AddNode(particleNode, followsEntity: false);
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
            Physics = physics;

            if (BuildsCollider)
            {
                Collider = new EntityCollider(physics);

                // The same collision again as a kinematic body, so props collide with this entity and
                // a moving one - a door - carries them with a real velocity
                if (UsesMoverBody && !IsTrigger && IsRigidBodySimulated
                    && EntitySystem.Physics.CreateMoverBody(physics, WorldOrigin,
                        EntityTransformHelper.EulerAnglesToQuaternion(WorldAngles)) is { } body)
                {
                    moverBody = body;
                    hasMoverBody = true;
                    moverBodyEnabled = true;
                    EntitySystem.Physics.Register(body, this);
                }

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

    // Only the main world group has a rigid body world, matching the static geometry the loader adds
    private bool IsRigidBodySimulated => Scene.WorldGroup == null;

    private const float MoverSnapDistance = 256f;

    /// <summary>
    /// Finds the rigid body a physics constraint anchored at <paramref name="anchor"/> holds this entity
    /// by: its kinematic mirror, so a constraint can hang things off a moving brush. A brush with no
    /// mirror - its collision is a triangle mesh, which a moving body cannot carry - gets a shapeless
    /// one on demand, which only follows the entity for the joint's sake.
    /// </summary>
    internal virtual bool TryGetConstraintBody(Vector3 anchor, out Body body)
    {
        if (!hasMoverBody && UsesMoverBody && IsRigidBodySimulated)
        {
            moverBody = EntitySystem.Physics.World.CreateKinematicBody(WorldOrigin, EntityTransformHelper.EulerAnglesToQuaternion(WorldAngles));
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

        // Ordinary motion is steered in FrameSimulate. A paused world steps nothing that would carry the
        // body, and a jump across the map is a teleport rather than a sweep, so both place it outright.
        var pose = RigidTransform;
        var isTeleport = Vector3.DistanceSquared(moverBody.Position, pose.Translation) > MoverSnapDistance * MoverSnapDistance;

        if (isTeleport || !EntitySystem.Enabled)
        {
            moverBody.SetTransform(pose.Translation, Quaternion.CreateFromRotationMatrix(pose));
        }
    }

    /// <inheritdoc/>
    internal override void FrameSimulate(float frameTime)
    {
        if (!hasMoverBody || !moverBodyEnabled)
        {
            return;
        }

        // Steered over the coming step onto where the entity is drawn this frame, so props collide with
        // the door they see and are swept aside at its real speed
        var pose = GetInterpolatedWorldFrame(EntitySystem.InterpolationFraction);
        var rotation = Quaternion.CreateFromRotationMatrix(pose);

        // Left alone once it rests there, so a still mover lets what sits on it sleep
        var isResting = moverBody.LinearVelocity == Vector3.Zero && moverBody.AngularVelocity == Vector3.Zero;

        if (isResting && moverBody.Position == pose.Translation && moverBody.Rotation == rotation)
        {
            return;
        }

        moverBody.MoveTowards(pose.Translation, rotation, frameTime, wake: true);
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
