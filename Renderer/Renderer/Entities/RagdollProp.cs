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
public class RagdollProp : BaseModelEntity, ICarryable
{
    /// <inheritdoc/>
    protected override bool UsesMoverBody => false;

    /// <inheritdoc/>
    protected override bool CreatesPhysDebugNodes => false;

    // One body per physics part, and which skeleton bone each drives; -1 for a part whose bone
    // name the render skeleton does not carry
    private Body[] bodies = [];
    private bool[] hasBody = [];
    private int[] partBones = [];

    // Each part's center as of the last tick, for the tunnel catch: a squeezed part can be
    // pushed straight through the one-sided mesh world between two looks
    private Vector3[] previousPositions = [];
    private readonly List<Joint> joints = [];

    // The skeleton bone driven by each part, resolved once, and each bone's bind transform local
    // to its parent, for the bones between and beyond the physics parts
    private int[] boneToPart = [];
    private Matrix4x4[] boneLocalBind = [];

    // The constant frame correction from each physics part to its render bone: the phys bind
    // pose is the SHAPE's frame, not the bone's, and they disagree wildly (70 units, half a
    // turn); a body pose pushed into the skinning without this correction shreds the mesh
    private Matrix4x4[] partToBone = [];

    // One debug node per part, in part-local space, moved with its body every frame; the bind-posed
    // statue BaseModelEntity would build cannot follow a ragdoll
    private SceneNodes.PhysSceneNode?[] partPhysNodes = [];

    private Matrix4x4 inverseSpawnTransform = Matrix4x4.Identity;
    private bool simulating;

    // The carry: which part the player grabbed, who is holding it, and how far out it is held
    private int carriedPart = -1;
    private PlayerEntity? carrier;
    private float carryDistance;

    // The rest-energy drain, the way Rubikon ragdolls shed energy: the solver injects micro
    // impulses into a jointed assembly with every outer step's contact update, faster than the
    // authored damping bleeds them - measured 1-3 u/s and a visibly spinning head forever,
    // since a sphere's point contact has no twist friction. Once every part is below these
    // speeds the doll cannot be doing anything watchable, so its velocities are drained
    // outright until the solver's own island sleep closes - the island, not the doll, so a
    // pile of ragdolls goes down together instead of freezing one doll against a moving
    // neighbor. Active motion - falls, throws, swings - sits above the thresholds untouched.
    // Wide enough to cover the worst measured limit-cycle floor (an HL:A grunt's wrist holds
    // 18 u/s and 12 rad/s against the ground forever), still far below thrown or falling speeds
    private const float DrainLinearSpeed = 25f;
    private const float DrainAngularSpeed = 15f;
    private const float DrainRate = 4f;

    // The extra drain stage once nothing moves beyond a crawl, pushing the chatter floor down
    private const float DeepRestLinearSpeed = 5f;
    private const float DeepRestAngularSpeed = 1.5f;
    private const float DeepDrainRate = 12f;

    // The terminal state is still sleep - the solver's island sleep never fires for a pile,
    // where one of dozens of bodies always spikes over its threshold inside the shared timer's
    // window - but it is judged by what the eye can see: displacement, not velocity. A limit
    // cycle buzzes a wrist at 2.5 u/s inside a half-unit envelope forever, which no velocity
    // gate ever passes and no eye ever notices. If no part leaves its anchor by this distance
    // or angle for the whole window, the doll is visually still and is frozen; a doll creeping
    // anywhere keeps re-anchoring and stays awake.
    private const float SleepDriftDistance = 0.75f;
    private const float SleepDriftDot = 0.99966f; // cos of half of ~3 degrees
    private const float ForcedSleepAfter = 0.75f;
    private Vector3[] anchorPositions = [];
    private Quaternion[] anchorRotations = [];
    private float stillTime;

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
        CreatePartPhysNodes(phys);

        simulating = true;
        modelNode.PoseDrivenExternally = true;
    }

    private void CreatePartPhysNodes(PhysAggregateData phys)
    {
        partPhysNodes = new SceneNodes.PhysSceneNode?[phys.Parts.Length];

        for (var i = 0; i < phys.Parts.Length; i++)
        {
            if (!hasBody[i])
            {
                continue;
            }

            var node = SceneNodes.PhysSceneNode.CreatePartPhysSceneNode(Scene, phys, i, ModelName, Classname);
            node.LayerName ??= LayerName;
            node.EntityInstance = this;

            partPhysNodes[i] = node;
            Scene.Add(node, dynamic: true);
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
                world.Translation, Quaternion.CreateFromRotationMatrix(world), this);

            if (created is { } body)
            {
                bodies[i] = body;
                hasBody[i] = true;
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
            // Frozen where it visibly already was: the buzz velocities go too, so a later wake
            // resumes from stillness rather than mid-vibration
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
    /// Gets whether the player can grab a part of this ragdoll: only once it actually simulates.
    /// </summary>
    public bool CanBeCarried => simulating;

    // The carry holds the one grabbed part by position alone; its orientation and the whole rest
    // of the ragdoll swing free on the joints, which is what makes a carried ragdoll dangle
    bool ICarryable.CarriesOrientation => false;
    Body ICarryable.CarryBody => bodies[carriedPart];

    void ICarryable.BeginCarry(PlayerEntity carrier, float carryDistance, Body grabbedBody)
    {
        carriedPart = FindPart(grabbedBody);
        this.carrier = carrier;
        this.carryDistance = carryDistance;

        // Gravity off on the grabbed part only: the held part floats where it is steered while
        // everything hanging off it keeps its full weight. No collision to suspend - ragdoll
        // parts never collide with the player's pushing body in the first place.
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

    (Vector3 Position, Quaternion Rotation) ICarryable.ComputeHoldPose()
    {
        var body = bodies[carriedPart];

        Vector3 eyePosition;
        Vector3 forward;

        // The camera the frame is drawn with when there is one, exactly as the prop carry does:
        // view smoothing sits between the input camera and the drawn view
        if (EntitySystem.RenderCamera is { } camera)
        {
            eyePosition = camera.Location;
            forward = camera.Forward;
        }
        else
        {
            var controller = carrier!.Controller;
            eyePosition = controller.EyePosition;
            forward = controller.ViewForward;
        }

        var position = eyePosition + forward * carryDistance
            - Vector3.Transform(body.LocalCenterOfMass, body.Rotation);

        // The body's own rotation as the target leaves no rotation error to steer against
        return (position, body.Rotation);
    }

    void ICarryable.AdoptCarryRotation(float fraction)
    {
        // Never called: the carry does not steer this ragdoll's orientation
    }

    private int FindPart(Body grabbedBody)
    {
        for (var i = 0; i < bodies.Length; i++)
        {
            if (hasBody[i] && bodies[i].UserData == grabbedBody.UserData)
            {
                return i;
            }
        }

        return Array.IndexOf(hasBody, true);
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

        foreach (var node in partPhysNodes)
        {
            if (node != null)
            {
                Scene.Remove(node, dynamic: true);
                node.Delete();
            }
        }

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

        simulating = false;
    }
}
