namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>trigger_hurt</c>. A trigger volume that damages what is inside it. Nothing here takes damage, so it
/// only reports touches and answers the inputs every trigger has.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CTriggerHurt">CTriggerHurt</seealso>
public sealed class TriggerHurt : BaseTrigger
{
    /// <summary>Initializes a <c>trigger_hurt</c> from its keyvalues.</summary>
    public TriggerHurt(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        InitTrigger();
    }
}
