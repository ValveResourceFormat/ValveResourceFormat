using System.Linq;
using Box3D;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>prop_handpose</c>. A spot where a hand takes a pose to poke, press or hold something, firing
/// <c>OnHandPosed</c> and <c>OnHandUnPosed</c>. A use tap poses it briefly, a held use keeps it posed until let go.
/// Only poses that fire either output can be used, and not those on a simulated body, which use grabs instead.
/// Its model only shows the pose in the editor, so nothing is drawn for it here.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/server/CHandPose">CHandPose</seealso>
public sealed class PropHandpose : BaseEntity, IAimGrabbable
{
    // How far from the pose the aim may pass, as a reaching hand closes the gap
    private const float AimRadius = 8f;

    // Long enough for what a tap starts, such as a horn, to be heard before it stops
    private const float TapPoseTime = 0.2f;

    /// <summary>Gets whether the pose cannot be used. The <c>Disable</c> input sets it.</summary>
    public bool IsDisabled { get; private set; }

    /// <summary>Gets whether a hand holds the pose.</summary>
    public bool IsPosed { get; private set; }

    private bool hasOutputs;
    private float disengageDistance;
    private BaseEntity? poser;

    /// <summary>Initializes a <c>prop_handpose</c> from its keyvalues.</summary>
    public PropHandpose(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        IsDisabled = KeyValues.GetBooleanProperty("startdisabled");
        disengageDistance = KeyValues.GetFloatProperty("disengagedistance");
        hasOutputs = Data?.Connections?.Any(connection =>
            connection.OutputName.Equals("OnHandPosed", StringComparison.OrdinalIgnoreCase)
            || connection.OutputName.Equals("OnHandUnPosed", StringComparison.OrdinalIgnoreCase)) == true;
    }

    // A handle on a door or loose prop moves with its body, which use takes hold of instead
    private bool IsOnSimulatedBody => MoveParent is IHandGrabbable or PropPhysics { HasBody: true, Body.Type: BodyType.Dynamic };

    /// <summary>Poses the hand for a moment, as a tap of use.</summary>
    public override void Use(BaseEntity? activator)
    {
        Pose(activator);
        SetNextThink(EntitySystem.CurrentTime + TapPoseTime);
    }

    /// <inheritdoc/>
    public override void Think() => UnPose();

    /// <inheritdoc/>
    public bool TryAim(Vector3 eyePosition, Vector3 forward, float reach, out float distance)
    {
        distance = reach;

        if (IsDisabled || !hasOutputs || IsOnSimulatedBody)
        {
            return false;
        }

        var point = WorldOrigin;
        var along = Vector3.Dot(point - eyePosition, forward);

        if (along <= 0f || along >= reach || Vector3.Distance(point, eyePosition + forward * along) > AimRadius)
        {
            return false;
        }

        // Poses sit on surfaces, so only a wall well in front of one hides it
        var hit = EntitySystem.PhysicsWorld.TraceRay(eyePosition, point, Rubikon.Cs2PlayerCollisionFilter);

        if (hit.Hit && hit.Distance < Vector3.Distance(eyePosition, point) - AimRadius)
        {
            return false;
        }

        // Ahead of the surface it sits on, which may belong to another interactable
        distance = MathF.Max(along - AimRadius, 0f);
        return true;
    }

    /// <inheritdoc/>
    public void BeginAimGrab(PlayerEntity player, Vector3 eyePosition, Vector3 forward, float distance)
    {
        SetNextThink(-1f);
        Pose(player);
    }

    /// <summary>Lets go of the pose once the aim strays further than <c>disengagedistance</c> from it.</summary>
    public void UpdateAimGrab(Vector3 eyePosition, Vector3 forward, float deltaTime)
    {
        if (!IsPosed)
        {
            return;
        }

        var point = WorldOrigin;
        var along = MathF.Max(Vector3.Dot(point - eyePosition, forward), 0f);

        if (Vector3.Distance(point, eyePosition + forward * along) > AimRadius + disengageDistance)
        {
            UnPose();
        }
    }

    /// <inheritdoc/>
    public void EndAimGrab() => UnPose();

    private void Pose(BaseEntity? activator)
    {
        if (IsPosed)
        {
            return;
        }

        IsPosed = true;
        poser = activator;
        EntitySystem.TriggerOutput(this, "OnHandPosed", activator);
    }

    private void UnPose()
    {
        if (!IsPosed)
        {
            return;
        }

        IsPosed = false;
        EntitySystem.TriggerOutput(this, "OnHandUnPosed", poser);
        poser = null;
    }

    [EntityInput("Enable")]
    private void InputEnable(EntityInputData data) => IsDisabled = false;

    [EntityInput("Disable")]
    private void InputDisable(EntityInputData data)
    {
        IsDisabled = true;
        UnPose();
    }
}
