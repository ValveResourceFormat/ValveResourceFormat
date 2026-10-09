using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>trigger_look</c>. Fires <c>OnTrigger</c> once the player inside it has looked at its <c>target</c>
/// for <see cref="LookTime"/> seconds, and <c>OnTimeout</c> if they spend <c>timeout</c> seconds inside
/// without managing it.
/// </summary>
/// <remarks>
/// <para>
/// In the engine this is a <c>trigger_once</c>, but it replaces the touch outright, so nothing of
/// <see cref="TriggerMultiple"/> is left to inherit. The look is tested on every touch: the view direction
/// (or the direction of travel) has to be within <see cref="FieldOfView"/> of the direction from the eye to
/// the target's origin, a cosine rather than an angle. Looking away or leaving resets the clock.
/// </para>
/// <para>
/// <c>test_occlusion</c> traces to the target against the world, <c>test_visible_occlusion</c> against
/// solid entities too; hitting the target itself still counts as seeing it.
/// </para>
/// </remarks>
public sealed class TriggerLook : BaseTrigger
{
    /// <summary>What a <c>trigger_look</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum LookSpawnFlag : uint
    {
        /// <summary>Removes the trigger once it has fired, or timed out.</summary>
        FireOnce = 128,

        /// <summary>Tests the direction the player moves in instead of where they look.</summary>
        UseVelocity = 256,
    }

    private string? lookTargetName;
    private BaseEntity? lookTarget;
    private float timeoutDuration;
    private bool is2DFieldOfView;
    private bool useVelocity;
    private bool testOcclusion;
    private bool testAllVisibleOcclusion;

    // Seconds looked so far, null while not looking
    private float? lookTimeTotal;
    private float lookTimeLast;
    private bool isLooking;
    private bool timeoutFired;
    private BaseEntity? timeoutActivator;
    private bool isRemoving;

    /// <summary>
    /// Gets the cosine of the widest angle between the view and the target that still counts as looking at
    /// it: 1 is dead ahead, 0 is anywhere in front, -1 is any direction.
    /// </summary>
    public float FieldOfView { get; private set; }

    /// <summary>Gets how many seconds the target has to be looked at before the trigger fires.</summary>
    public float LookTime { get; private set; }

    /// <summary>Initializes a <c>trigger_look</c> from its keyvalues.</summary>
    public TriggerLook(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        useVelocity = HasSpawnFlags(LookSpawnFlag.UseVelocity);
        SpawnFlags = (uint)SpawnFlag.AllowClients | (SpawnFlags & (uint)LookSpawnFlag.FireOnce);

        InitTrigger();

        lookTargetName = KeyValues.GetStringProperty("target");
        timeoutDuration = KeyValues.GetFloatProperty("timeout");
        is2DFieldOfView = KeyValues.GetBooleanProperty("fov2d");
        testOcclusion = KeyValues.GetBooleanProperty("test_occlusion");
        testAllVisibleOcclusion = KeyValues.GetBooleanProperty("test_visible_occlusion");
        FieldOfView = KeyValues.GetFloatProperty("fieldofview");
        LookTime = KeyValues.GetFloatProperty("looktime");
    }

    /// <summary>Sets <see cref="FieldOfView"/>.</summary>
    [EntityInput("FieldOfView")]
    private void InputFieldOfView(EntityInputData data) => FieldOfView = data.Float();

    /// <summary>Sets <see cref="LookTime"/>.</summary>
    [EntityInput("LookTime")]
    private void InputLookTime(EntityInputData data) => LookTime = data.Float();

    /// <inheritdoc/>
    protected override void OnStartTouch(BaseEntity other)
    {
        base.OnStartTouch(other);

        if (timeoutDuration == 0f || isRemoving || !PassesTriggerFilters(other))
        {
            return;
        }

        timeoutFired = false;
        timeoutActivator = other;
        SetNextThink(EntitySystem.CurrentTime + timeoutDuration);
    }

    /// <inheritdoc/>
    protected override void OnEndTouch(BaseEntity other)
    {
        base.OnEndTouch(other);

        if (!PassesTriggerFilters(other))
        {
            return;
        }

        if (!isRemoving)
        {
            SetNextThink(-1f);
        }

        lookTimeTotal = null;
        isLooking = false;
    }

    /// <inheritdoc/>
    protected override void OnTouch(BaseEntity other)
    {
        base.OnTouch(other);

        if (timeoutFired || isRemoving || !PassesTriggerFilters(other) || other is not PlayerEntity player)
        {
            return;
        }

        if (lookTarget is null or { IsRemoved: true })
        {
            lookTarget = string.IsNullOrEmpty(lookTargetName) ? null : EntitySystem.FindByTargetName(lookTargetName);

            if (lookTarget == null)
            {
                return;
            }
        }

        var lookDirection = useVelocity
            ? MathUtils.SafeNormalize(player.Velocity)
            : player.Controller.ViewForward;

        var eyePosition = player.Controller.EyePosition;
        var targetPosition = lookTarget.WorldOrigin;
        var targetDirection = MathUtils.SafeNormalize(targetPosition - eyePosition);

        if (is2DFieldOfView)
        {
            lookDirection = MathUtils.SafeNormalize(lookDirection with { Z = 0f });
            targetDirection = MathUtils.SafeNormalize(targetDirection with { Z = 0f });
        }

        if (Vector3.Dot(lookDirection, targetDirection) > FieldOfView && CanSee(eyePosition, targetPosition))
        {
            var now = EntitySystem.CurrentTime;

            lookTimeTotal = lookTimeTotal is { } total ? total + (now - lookTimeLast) : 0f;
            lookTimeLast = now;

            if (lookTimeTotal >= LookTime)
            {
                Trigger(other);
            }

            return;
        }

        if (isLooking)
        {
            EntitySystem.TriggerOutput(this, "OnEndLook", other);
            isLooking = false;
        }

        lookTimeTotal = null;
    }

    /// <summary>Times out, unless the player managed to look in time.</summary>
    public override void Think()
    {
        if (isRemoving)
        {
            EntitySystem.Remove(this);
            return;
        }

        EntitySystem.TriggerOutput(this, "OnTimeout", timeoutActivator is { IsRemoved: false } ? timeoutActivator : null);
        timeoutFired = true;
        RemoveIfFireOnce();
    }

    private void Trigger(BaseEntity activator)
    {
        EntitySystem.TriggerOutput(this, "OnTrigger", activator);
        lookTimeTotal = null;

        if (!isLooking)
        {
            EntitySystem.TriggerOutput(this, "OnStartLook", activator);
            isLooking = true;
        }

        SetNextThink(-1f);
        RemoveIfFireOnce();
    }

    // Removed on the next think, as the engine does, rather than while the touch is still being handled
    private void RemoveIfFireOnce()
    {
        if (HasSpawnFlags(LookSpawnFlag.FireOnce))
        {
            isRemoving = true;
            SetNextThink(EntitySystem.CurrentTime);
        }
    }

    // Seeing the target counts even when the trace stops on the target itself
    private bool CanSee(Vector3 eyePosition, Vector3 targetPosition)
    {
        if (!testOcclusion && !testAllVisibleOcclusion)
        {
            return true;
        }

        var trace = EntitySystem.PhysicsWorld.TraceRay(eyePosition, targetPosition, Rubikon.DefaultGeometry);

        if (testAllVisibleOcclusion)
        {
            EntitySystem.TraceRay(eyePosition, targetPosition, Rubikon.DefaultGeometry, ref trace);
        }

        return !trace.Hit || trace.HitEntity == lookTarget;
    }
}
