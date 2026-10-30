using Box3D;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Entities;

// One physics part of an animated model, keyframed onto its bone: it blocks movement where the part is drawn and
// pushes props as it moves
internal sealed class AnimInteractablePart : BaseEntity
{
    private readonly Body body;
    private readonly bool hasBody;
    private Matrix4x4 pose = Matrix4x4.Identity;

    public AnimInteractablePart(BaseModelEntity owner, PhysAggregateData physics, int partIndex, bool simulatesBody)
        : base(owner.EntitySystem, owner.Scene, "animinteractable_part")
    {
        Collider = new EntityCollider(physics, partIndex);
        IsSolid = true;

        if (simulatesBody && EntitySystem.Physics.CreateMoverPartBody(physics, partIndex, Vector3.Zero, Quaternion.Identity) is { } created)
        {
            body = created;
            hasBody = true;
            EntitySystem.Physics.Register(body, owner);
        }
    }

    // Placed outright, as when the model spawns or is set to a completion without moving there
    public void Place(Matrix4x4 partPose)
    {
        pose = partPose;
        Collider?.Transform = partPose;

        if (hasBody)
        {
            body.SetTransform(partPose.Translation, Quaternion.CreateFromRotationMatrix(partPose));
        }
    }

    // Steered over the coming rigid body step, so props are pushed at the speed the part moves
    public void Steer(Matrix4x4 partPose, float deltaTime)
    {
        var isStill = partPose == pose;
        pose = partPose;
        Collider?.Transform = partPose;

        // Left alone once at rest there, letting what lies on it sleep
        if (!hasBody || (isStill && body.LinearVelocity == Vector3.Zero && body.AngularVelocity == Vector3.Zero))
        {
            return;
        }

        body.MoveTowards(partPose.Translation, Quaternion.CreateFromRotationMatrix(partPose), deltaTime, wake: true);
    }

    // The pose is set by the owner, not from an origin this entity never has
    protected override void UpdateColliderTransform()
    {
    }

    protected override void OnRemove()
    {
        base.OnRemove();

        if (hasBody)
        {
            EntitySystem.PhysicsOrNull?.Forget(body);
            body.Destroy();
        }
    }
}
