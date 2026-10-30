using System.Collections.Generic;
using Box3D;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>prop_ragdoll</c>: one rigid body per physics part, joined by the authored joints and posing the skeleton.</summary>
public sealed class RagdollProp : BaseModelEntity, ICarryable
{
    [Flags]
    private enum RagdollSpawnFlags : uint
    {
        /// <summary>Collides with neither the player nor other debris.</summary>
        Debris = 4,
    }

    /// <inheritdoc/>
    protected override bool UsesMoverBody => false;

    /// <inheritdoc/>
    protected override bool CreatesPhysDebugNodes => false;

    private Body[] bodies = [];
    private bool[] hasBody = [];
    private readonly Dictionary<ulong, int> partByBodyId = [];

    // A squeezed part can be pushed through the one-sided world mesh between ticks
    private Vector3[] previousPositions = [];
    private readonly List<Joint> joints = [];

    private int[] boneToPart = [];
    private Matrix4x4[] boneLocalBind = [];

    // The physics bind pose is the shape frame, not the bone frame, and the two can be far apart
    private Matrix4x4[] partToBone = [];

    // Bind-posed debug nodes from BaseModelEntity cannot follow the parts
    private SceneNode?[] partPhysNodes = [];

    // Usually the pelvis
    private int rootPart = -1;
    private Matrix4x4 inverseRootBoneBind = Matrix4x4.Identity;

    private bool simulating;

    private int carriedPart = -1;
    private PlayerEntity? carrier;
    private float carryDistance;

    // Near rest the solver feeds a jointed doll impulses faster than damping bleeds them, leaving it
    // buzzing forever; velocities below these speeds are drained
    private const float DrainLinearSpeed = 25f;
    private const float DrainAngularSpeed = 15f;
    private const float DrainRate = 4f;
    private const float DeepRestLinearSpeed = 5f;
    private const float DeepRestAngularSpeed = 1.5f;
    private const float DeepDrainRate = 12f;

    // Any one of many bodies resets the island sleep timer; instead sleep once no part drifts this far for the window
    private const float SleepDriftDistance = 0.75f;
    private const float SleepDriftDot = 0.99966f; // cos of half of ~3 degrees
    private const float ForcedSleepAfter = 0.75f;
    private Vector3[] anchorPositions = [];
    private Quaternion[] anchorRotations = [];
    private float stillTime;

    /// <summary>Initializes the ragdoll from its keyvalues.</summary>
    public RagdollProp(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        IsSolid = false;

        if (Collider is not { } collider || ModelNode is not { } modelNode)
        {
            return;
        }

        var phys = collider.PhysicsData;

        if (phys.Parts.Length == 0 || phys.Joints.Length == 0)
        {
            return;
        }

        var spawnTransform = EntityTransformHelper.ToRigidTransformationMatrix(Angles, Origin);

        CreateBodies(phys, spawnTransform);
        CreateJoints(phys);
        ResolveBones(phys, modelNode);
        CreatePartPhysNodes(phys);

        simulating = true;
        modelNode.PoseDrivenExternally = true;
    }

    private void CreatePartPhysNodes(PhysAggregateData phys)
    {
        partPhysNodes = new SceneNode?[phys.Parts.Length];

        if (EntitySystem.ToolVisuals is not { } toolVisuals)
        {
            return;
        }

        for (var i = 0; i < phys.Parts.Length; i++)
        {
            if (!hasBody[i])
            {
                continue;
            }

            var node = toolVisuals.CreatePartCollisionVisual(Scene, phys, i, ModelName);
            partPhysNodes[i] = node;
            AddNode(node, followsEntity: false);
        }
    }

    private void CreateBodies(PhysAggregateData phys, in Matrix4x4 spawnTransform)
    {
        var bindPose = phys.BindPose;

        bodies = new Body[phys.Parts.Length];
        hasBody = new bool[phys.Parts.Length];
        previousPositions = new Vector3[phys.Parts.Length];
        anchorPositions = new Vector3[phys.Parts.Length];
        anchorRotations = new Quaternion[phys.Parts.Length];

        for (var i = 0; i < phys.Parts.Length; i++)
        {
            var pose = bindPose.Length > i ? bindPose[i] : Matrix4x4.Identity;
            var world = pose * spawnTransform;

            var created = EntitySystem.Physics.CreateRagdollBody(phys, i,
                world.Translation, Quaternion.CreateFromRotationMatrix(world), HasSpawnFlags(RagdollSpawnFlags.Debris), this);

            if (created is { } body)
            {
                bodies[i] = body;
                hasBody[i] = true;
                partByBodyId[body.UserData] = i;
                previousPositions[i] = body.Position;
                anchorPositions[i] = body.Position;
                anchorRotations[i] = body.Rotation;
            }
        }
    }

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        base.PhysicsSimulate(tickInterval);

        if (!simulating)
        {
            return;
        }

        if (rootPart >= 0)
        {
            var root = BoneWorldPose(rootPart);
            SetOriginAndAngles(root.Translation,
                EntityTransformHelper.ToEulerAngles(Quaternion.CreateFromRotationMatrix(root)));
        }

        var anyAwake = false;
        var nearRest = true;
        var deepRest = true;
        var visiblyStill = true;

        for (var i = 0; i < bodies.Length; i++)
        {
            if (!hasBody[i])
            {
                continue;
            }

            EntitySystem.Physics.CatchTunneledBody(bodies[i], previousPositions[i]);
            previousPositions[i] = bodies[i].Position;

            if (bodies[i].IsAwake)
            {
                anyAwake = true;

                var linearSpeed = bodies[i].LinearVelocity.Length();
                var angularSpeed = bodies[i].AngularVelocity.Length();

                nearRest &= linearSpeed <= DrainLinearSpeed && angularSpeed <= DrainAngularSpeed;
                deepRest &= linearSpeed <= DeepRestLinearSpeed && angularSpeed <= DeepRestAngularSpeed;

                visiblyStill &= Vector3.DistanceSquared(bodies[i].Position, anchorPositions[i])
                        <= SleepDriftDistance * SleepDriftDistance
                    && MathF.Abs(Quaternion.Dot(bodies[i].Rotation, anchorRotations[i])) >= SleepDriftDot;
            }
        }

        // A held doll never rests, and a sleeping one has nothing to drain
        if (carriedPart >= 0 || !anyAwake)
        {
            stillTime = 0f;
            ReanchorParts();
            return;
        }

        if (nearRest)
        {
            var drain = MathF.Exp(-(deepRest ? DeepDrainRate : DrainRate) * tickInterval);

            for (var i = 0; i < bodies.Length; i++)
            {
                if (hasBody[i])
                {
                    var body = bodies[i];
                    body.LinearVelocity *= drain;
                    body.AngularVelocity *= drain;
                }
            }
        }

        if (!visiblyStill)
        {
            stillTime = 0f;
            ReanchorParts();
            return;
        }

        stillTime += tickInterval;

        if (stillTime >= ForcedSleepAfter)
        {
            // Zeroed too, or a later wake resumes mid-vibration
            for (var i = 0; i < bodies.Length; i++)
            {
                if (hasBody[i])
                {
                    var body = bodies[i];
                    body.LinearVelocity = Vector3.Zero;
                    body.AngularVelocity = Vector3.Zero;
                    body.IsAwake = false;
                }
            }
        }
    }

    private void ReanchorParts()
    {
        for (var i = 0; i < bodies.Length; i++)
        {
            if (hasBody[i])
            {
                anchorPositions[i] = bodies[i].Position;
                anchorRotations[i] = bodies[i].Rotation;
            }
        }
    }

    private void CreateJoints(PhysAggregateData phys)
    {
        foreach (var joint in phys.Joints)
        {
            // Some models carry joints from a part to itself
            if (joint.Body1 == joint.Body2 || joint.Body1 >= hasBody.Length || joint.Body2 >= hasBody.Length
                || !hasBody[joint.Body1] || !hasBody[joint.Body2])
            {
                continue;
            }

            if (EntitySystem.Physics.CreateRagdollJoint(joint, bodies[joint.Body1], bodies[joint.Body2]) is { } created)
            {
                joints.Add(created);
            }
        }
    }

    private void ResolveBones(PhysAggregateData phys, SceneNodes.ModelSceneNode modelNode)
    {
        var skeleton = modelNode.AnimationController.Skeleton;
        var bindPose = modelNode.AnimationController.BindPose;
        var names = phys.BoneNames;

        boneToPart = new int[skeleton.Bones.Length];
        boneLocalBind = new Matrix4x4[skeleton.Bones.Length];
        partToBone = new Matrix4x4[phys.Parts.Length];
        Array.Fill(boneToPart, -1);

        var byName = new Dictionary<string, int>(skeleton.Bones.Length, StringComparer.OrdinalIgnoreCase);

        foreach (var bone in skeleton.Bones)
        {
            byName[bone.Name] = bone.Index;

            var parentBind = bone.Parent != null ? bindPose[bone.Parent.Index] : Matrix4x4.Identity;
            boneLocalBind[bone.Index] = Matrix4x4.Invert(parentBind, out var inverse)
                ? bindPose[bone.Index] * inverse
                : Matrix4x4.Identity;
        }

        var physBind = phys.BindPose;

        for (var i = 0; i < phys.Parts.Length && i < names.Length; i++)
        {
            if (hasBody[i] && byName.TryGetValue(names[i], out var boneIndex)
                && Matrix4x4.Invert(physBind.Length > i ? physBind[i] : Matrix4x4.Identity, out var inversePartBind))
            {
                boneToPart[boneIndex] = i;
                partToBone[i] = bindPose[boneIndex] * inversePartBind;
            }
        }

        // Parents precede children: the first bone with a part is the root-most one
        foreach (var bone in skeleton.Bones)
        {
            if (boneToPart[bone.Index] >= 0 && Matrix4x4.Invert(bindPose[bone.Index], out inverseRootBoneBind))
            {
                rootPart = boneToPart[bone.Index];
                break;
            }
        }
    }

    private Matrix4x4 BoneWorldPose(int part)
    {
        var body = bodies[part];

        return partToBone[part]
            * Matrix4x4.CreateFromQuaternion(body.Rotation)
            * Matrix4x4.CreateTranslation(body.Position);
    }

    /// <summary>Gets whether the ragdoll simulates and can be picked up.</summary>
    public bool CanBeCarried => simulating;

    Body ICarryable.CarryBody => bodies[carriedPart];

    void ICarryable.BeginCarry(PlayerEntity carrier, float carryDistance, Body grabbedBody)
    {
        carriedPart = partByBodyId[grabbedBody.UserData];
        this.carrier = carrier;
        this.carryDistance = carryDistance;

        // Only the held part floats, the rest keeps its weight and hangs off it
        var body = bodies[carriedPart];
        body.GravityScale = 0f;
        body.CanSleep = false;
        body.IsAwake = true;
    }

    void ICarryable.EndCarry()
    {
        if (carriedPart >= 0 && hasBody[carriedPart])
        {
            var body = bodies[carriedPart];
            body.GravityScale = 1f;
            body.CanSleep = true;
            body.IsAwake = true;
        }

        carriedPart = -1;
        carrier = null;
    }

    // Held by position alone, letting the doll dangle on its joints
    (Vector3 Position, Quaternion? Rotation) ICarryable.ComputeHoldPose(float deltaTime)
    {
        var body = bodies[carriedPart];
        var (eyePosition, forward, _) = carrier!.GetHoldView();

        var reach = EntitySystem.Physics.ClearReach(eyePosition, forward, carryDistance, body);
        var position = eyePosition + forward * reach
            - Vector3.Transform(body.LocalCenterOfMass, body.Rotation);

        return (position, null);
    }

    // The nearest part, as a rope anchored at the neck should hold the head
    internal override bool TryGetConstraintBody(Vector3 anchor, out Body body)
    {
        body = default;
        var nearest = float.PositiveInfinity;

        for (var i = 0; i < bodies.Length; i++)
        {
            if (!hasBody[i])
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(bodies[i].CenterOfMass, anchor);

            if (distance < nearest)
            {
                nearest = distance;
                body = bodies[i];
            }
        }

        return !float.IsPositiveInfinity(nearest);
    }

    /// <summary>Sets the linear velocity of every part.</summary>
    public void SetVelocity(Vector3 velocity)
    {
        for (var i = 0; i < bodies.Length; i++)
        {
            if (hasBody[i])
            {
                bodies[i].LinearVelocity = velocity;
            }
        }
    }

    /// <inheritdoc/>
    protected override bool UpdatesRenderTransformEveryFrame => simulating;

    /// <summary>Poses the skeleton from the part bodies.</summary>
    protected override void UpdateRenderTransform(float fraction)
    {
        if (!simulating || rootPart < 0)
        {
            base.UpdateRenderTransform(fraction);
        }
        else
        {
            var root = BoneWorldPose(rootPart);
            SetRenderTransform(root.Translation, Quaternion.CreateFromRotationMatrix(root));
        }

        if (!simulating || ModelNode is not { } modelNode || !Matrix4x4.Invert(Transform, out var worldToModel))
        {
            return;
        }

        var pose = modelNode.AnimationController.Pose;
        var skeleton = modelNode.AnimationController.Skeleton;

        // Unsimulated bones such as the root motion bone keep their bind relation to the root physics bone
        var rootsPose = rootPart >= 0
            ? inverseRootBoneBind * BoneWorldPose(rootPart) * worldToModel
            : Matrix4x4.Identity;

        foreach (var root in skeleton.Roots)
        {
            WriteBonePose(root, rootsPose, worldToModel, pose);
        }

        for (var i = 0; i < partPhysNodes.Length; i++)
        {
            if (partPhysNodes[i] is { } node)
            {
                var body = bodies[i];
                node.Transform = Matrix4x4.CreateFromQuaternion(body.Rotation)
                    * Matrix4x4.CreateTranslation(body.Position);
                Scene.DynamicOctree.Update(node);
            }
        }
    }

    private void WriteBonePose(ResourceTypes.ModelAnimation.Bone bone, in Matrix4x4 parentPose, in Matrix4x4 worldToModel, Matrix4x4[] pose)
    {
        var part = boneToPart.Length > bone.Index ? boneToPart[bone.Index] : -1;

        var modelPose = part >= 0
            ? BoneWorldPose(part) * worldToModel
            : boneLocalBind[bone.Index] * parentPose;

        pose[bone.Index] = modelPose;

        foreach (var child in bone.Children)
        {
            WriteBonePose(child, modelPose, worldToModel, pose);
        }
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        base.OnRemove();

        // Removed by the base with the other nodes
        partPhysNodes = [];

        foreach (var joint in joints)
        {
            joint.Destroy();
        }

        joints.Clear();

        for (var i = 0; i < bodies.Length; i++)
        {
            if (hasBody[i])
            {
                EntitySystem.PhysicsOrNull?.Forget(bodies[i]);
                bodies[i].Destroy();
                hasBody[i] = false;
            }
        }

        partByBodyId.Clear();

        simulating = false;
    }
}
