using System.Collections.Generic;
using Box3D;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// A ragdoll, Source's <c>prop_ragdoll</c>: one rigid body per authored physics part, connected by
/// the authored joints with their swing cones, twist ranges and friction, simulated in
/// <see cref="PhysicsSimulation"/>. The rendered skeleton adopts the body poses every frame - the
/// physics bones directly, everything else riding along on its bind-local offset.
/// </summary>
public class RagdollProp : BaseModelEntity
{
    /// <inheritdoc/>
    protected override bool UsesMoverBody => false;

    // One body per physics part, and which skeleton bone each drives; -1 for a part whose bone
    // name the render skeleton does not carry
    private Body[] bodies = [];
    private bool[] hasBody = [];
    private int[] partBones = [];
    private readonly List<Joint> joints = [];

    // The skeleton bone driven by each part, resolved once, and each bone's bind transform local
    // to its parent, for the bones between and beyond the physics parts
    private int[] boneToPart = [];
    private Matrix4x4[] boneLocalBind = [];

    // The constant frame correction from each physics part to its render bone: the phys bind
    // pose is the SHAPE's frame, not the bone's, and they disagree wildly (70 units, half a
    // turn); a body pose pushed into the skinning without this correction shreds the mesh
    private Matrix4x4[] partToBone = [];

    private Matrix4x4 inverseSpawnTransform = Matrix4x4.Identity;
    private bool simulating;

    /// <summary>
    /// Initializes the ragdoll from its keyvalues.
    /// </summary>
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

        if (!Matrix4x4.Invert(spawnTransform, out inverseSpawnTransform))
        {
            return;
        }

        CreateBodies(phys, spawnTransform);
        CreateJoints(phys);
        ResolveBones(phys, modelNode);

        simulating = true;
        modelNode.PoseDrivenExternally = true;
    }

    private void CreateBodies(PhysAggregateData phys, in Matrix4x4 spawnTransform)
    {
        var bindPose = phys.BindPose;

        bodies = new Body[phys.Parts.Length];
        hasBody = new bool[phys.Parts.Length];

        for (var i = 0; i < phys.Parts.Length; i++)
        {
            var pose = bindPose.Length > i ? bindPose[i] : Matrix4x4.Identity;
            var world = pose * spawnTransform;

            var created = EntitySystem.Physics.CreateRagdollBody(phys, i,
                world.Translation, Quaternion.CreateFromRotationMatrix(world), this);

            if (created is { } body)
            {
                bodies[i] = body;
                hasBody[i] = true;
            }
        }
    }

    private void CreateJoints(PhysAggregateData phys)
    {
        foreach (var joint in phys.Joints)
        {
            if (joint.Body1 >= hasBody.Length || joint.Body2 >= hasBody.Length
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

        partBones = new int[phys.Parts.Length];
        boneToPart = new int[skeleton.Bones.Length];
        boneLocalBind = new Matrix4x4[skeleton.Bones.Length];
        partToBone = new Matrix4x4[phys.Parts.Length];
        Array.Fill(partBones, -1);
        Array.Fill(boneToPart, -1);

        var byName = new Dictionary<string, int>(skeleton.Bones.Length, StringComparer.OrdinalIgnoreCase);

        foreach (var bone in skeleton.Bones)
        {
            byName[bone.Name] = bone.Index;

            // The bind transform local to the parent, so bones without a physics part keep their
            // authored offset from whatever the physics drives above them
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
                partBones[i] = boneIndex;
                boneToPart[boneIndex] = i;
                partToBone[i] = bindPose[boneIndex] * inversePartBind;
            }
        }
    }

    /// <summary>
    /// Hands every body of the ragdoll the same velocity, for a spawn that arrives moving.
    /// </summary>
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

    /// <summary>
    /// Adopts the rigid bodies into the rendered skeleton: a bone with a physics part takes its
    /// body's live pose, and every other bone rides its bind-local offset under its parent. The
    /// world steps with the rendered frame, so this is the frame's true pose.
    /// </summary>
    protected override void UpdateRenderTransform(float fraction)
    {
        base.UpdateRenderTransform(fraction);

        if (!simulating || ModelNode is not { } modelNode)
        {
            return;
        }

        var pose = modelNode.AnimationController.Pose;
        var skeleton = modelNode.AnimationController.Skeleton;

        foreach (var root in skeleton.Roots)
        {
            WriteBonePose(root, Matrix4x4.Identity, pose);
        }
    }

    private void WriteBonePose(ResourceTypes.ModelAnimation.Bone bone, in Matrix4x4 parentPose, Matrix4x4[] pose)
    {
        var part = boneToPart.Length > bone.Index ? boneToPart[bone.Index] : -1;

        Matrix4x4 modelPose;

        if (part >= 0)
        {
            var body = bodies[part];
            var world = Matrix4x4.CreateFromQuaternion(body.Rotation)
                * Matrix4x4.CreateTranslation(body.Position);

            modelPose = partToBone[part] * world * inverseSpawnTransform;
        }
        else
        {
            modelPose = boneLocalBind[bone.Index] * parentPose;
        }

        pose[bone.Index] = modelPose;

        foreach (var child in bone.Children)
        {
            WriteBonePose(child, modelPose, pose);
        }
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        base.OnRemove();

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

        simulating = false;
    }
}
