using Microsoft.Extensions.Logging;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>trigger_teleport</c>. Moves whatever enters its volume to the entity named by <c>target</c>, keeping
/// their velocity.
/// </summary>
/// <remarks>
/// The volume is the trigger's own <c>model</c>, the brush hulls it was compiled with, supplied by
/// <see cref="BaseTrigger.InitTrigger"/>. The destination is a plain map entity, usually an
/// <c>info_teleport_destination</c>, that nothing simulates, so it is found through its scene node in
/// <see cref="Activate"/> once the whole map has loaded.
/// </remarks>
public sealed class TriggerTeleport : BaseTrigger
{
    private (Vector3 Origin, Vector3 Angles)? destination;

    /// <summary>
    /// Initializes a <c>trigger_teleport</c> from its keyvalues.
    /// </summary>
    public TriggerTeleport(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        InitTrigger();
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        var targetName = KeyValues.GetStringProperty("target");

        if (string.IsNullOrEmpty(targetName))
        {
            EntitySystem.Logger.LogWarning("trigger_teleport '{TargetName}' has no target to teleport to", TargetName);
            return;
        }

        // The destination is a marker nothing simulates, but it still has a scene node carrying its keyvalues
        if (Scene.FindNodeByTargetName(targetName)?.EntityData is not { } target)
        {
            EntitySystem.Logger.LogWarning("trigger_teleport '{TargetName}' target '{Target}' was not found", TargetName, targetName);
            return;
        }

        destination = (target.GetVector3Property("origin"), target.GetVector3Property("angles"));
    }

    /// <inheritdoc/>
    protected override void OnStartTouch(BaseEntity other)
    {
        base.OnStartTouch(other);

        if (destination is not { } target)
        {
            return;
        }

        // Lift a unit so the hull does not arrive embedded in the floor
        other.Teleport(target.Origin + new Vector3(0, 0, 1f), target.Angles);
    }
}
