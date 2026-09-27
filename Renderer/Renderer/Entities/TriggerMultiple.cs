using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>trigger_multiple</c>. Fires <c>OnTrigger</c> for whatever is inside its volume, then waits
/// <see cref="Wait"/> seconds before it can fire again. A negative wait makes it fire once and remove
/// itself, which is all <see cref="TriggerOnce"/> is.
/// </summary>
/// <remarks>
/// It fires on the touch, not the start of one, so something standing inside keeps firing it every time
/// the wait runs out.
/// </remarks>
public class TriggerMultiple : BaseTrigger
{
    private const float RemoveDelay = 0.1f;

    private bool isTouchEnabled = true;
    private bool isRemoving;

    /// <summary>
    /// Gets the seconds to wait after firing before firing again. Negative means never again.
    /// </summary>
    public float Wait { get; protected set; }

    /// <summary>Initializes a <c>trigger_multiple</c> from its keyvalues.</summary>
    public TriggerMultiple(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        InitTrigger();

        Wait = KeyValues.GetFloatProperty("wait");

        if (Wait == 0f)
        {
            Wait = 0.2f;
        }
    }

    /// <inheritdoc/>
    protected override void OnTouch(BaseEntity other)
    {
        base.OnTouch(other);

        if (isTouchEnabled && PassesTriggerFilters(other))
        {
            ActivateMultiTrigger(other);
        }
    }

    private void ActivateMultiTrigger(BaseEntity activator)
    {
        // Still waiting out the last firing
        if (NextThink > EntitySystem.CurrentTime)
        {
            return;
        }

        EntitySystem.TriggerOutput(this, "OnTrigger", activator);

        if (Wait > 0f)
        {
            SetNextThink(EntitySystem.CurrentTime + Wait);
            return;
        }

        // Not removed on the spot, since the touch that got here is still being handled. The volume goes
        // at once, so whatever is inside is told it left before the trigger disappears.
        RemoveVolume();
        isTouchEnabled = false;
        isRemoving = true;
        SetNextThink(EntitySystem.CurrentTime + RemoveDelay);
    }

    /// <inheritdoc/>
    public override void Think()
    {
        if (isRemoving)
        {
            EntitySystem.Remove(this);
        }
    }
}
