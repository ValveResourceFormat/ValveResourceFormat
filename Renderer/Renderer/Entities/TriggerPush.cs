using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>trigger_push</c>. Pushes what is inside it along <c>pushdir</c> at <c>speed</c>: the player as a
/// base velocity carried on top of their own, physics props as an acceleration.
/// </summary>
/// <remarks>
/// <c>alternateticksfix</c> is not read.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CTriggerPush">CTriggerPush</seealso>
public sealed class TriggerPush : BaseTrigger
{
    // The push on a prop is an impulse per tick of speed times this mass, so a prop of this many
    // kilograms gains the push speed every second and a lighter one more
    private const float PropReferenceMass = 100f;

    private float speed;
    private Vector3 pushAngles;
    private bool isPushDirLocal;
    private bool pushesOnStartTouchOnly;

    // Reused by the prop push every tick
    private readonly List<Body> bodies = [];
    private readonly HashSet<ulong> bodyIds = [];

    /// <summary>Initializes a <c>trigger_push</c> from its keyvalues.</summary>
    public TriggerPush(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        InitTrigger();

        speed = KeyValues.GetFloatProperty("speed");
        pushAngles = KeyValues.GetVector3Property("pushdir");
        isPushDirLocal = !KeyValues.ContainsKey("pushdir_islocal") || KeyValues.GetBooleanProperty("pushdir_islocal");
        pushesOnStartTouchOnly = KeyValues.GetBooleanProperty("triggeronstarttouch");

        if (HasSpawnFlags(SpawnFlag.AllowPhysics | SpawnFlag.AllowAll))
        {
            SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
        }
    }

    // The push velocity in the world, turned with the trigger when the direction is local to it
    private Vector3 PushVelocity
    {
        get
        {
            var direction = EntityTransformHelper.EulerAnglesToForwardDirection(pushAngles);

            if (isPushDirLocal)
            {
                direction = Vector3.Transform(direction, EntityTransformHelper.EulerAnglesToQuaternion(WorldAngles));
            }

            return direction * speed;
        }
    }

    /// <inheritdoc/>
    protected override void OnStartTouch(BaseEntity other)
    {
        base.OnStartTouch(other);

        if (pushesOnStartTouchOnly)
        {
            PushPlayer(other);
        }
    }

    /// <inheritdoc/>
    protected override void OnTouch(BaseEntity other)
    {
        if (!pushesOnStartTouchOnly)
        {
            PushPlayer(other);
        }
    }

    private void PushPlayer(BaseEntity other)
    {
        if (IsEnabled && other is PlayerEntity player && PassesTriggerFilters(player))
        {
            player.AddBaseVelocity(PushVelocity);
        }
    }

    /// <summary>
    /// Pushes the physics props whose mass center is inside the volume, every tick: only the player
    /// opens touch links with triggers.
    /// </summary>
    public override void Think()
    {
        SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);

        if (!IsEnabled || Collider is not { IsEmpty: false } volume || EntitySystem.PhysicsOrNull is not { } physics)
        {
            return;
        }

        var bounds = volume.WorldBounds;

        // Collected first, pushed after: the world is locked while a query runs
        bodies.Clear();
        bodyIds.Clear();

        var overlapped = new BodyOverlap(bodies, bodyIds);
        physics.World.OverlapBox(bounds.Center, bounds.Size * 0.5f, ref overlapped,
            new QueryFilter(~PhysicsSimulation.PlayerCategory, PhysicsSimulation.PropCategory | PhysicsSimulation.DebrisCategory));

        var impulse = PushVelocity * (PropReferenceMass * EntitySystem.TickInterval);

        foreach (var body in bodies)
        {
            if (body.Type == BodyType.Dynamic && volume.ContainsPoint(body.CenterOfMass))
            {
                var pushed = body;
                pushed.ApplyImpulseToCenter(impulse, wake: true);
            }
        }
    }

    // One entry per body however many of its shapes overlap
    private readonly struct BodyOverlap(List<Body> bodies, HashSet<ulong> bodyIds) : IOverlapCallback
    {
        public bool OnOverlap(Shape shape)
        {
            var body = shape.Body;

            if (bodyIds.Add(body.UserData))
            {
                bodies.Add(body);
            }

            return true;
        }
    }
}
