using Microsoft.Extensions.Logging;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>trigger_teleport</c>. Moves whatever enters its volume to the entity named by <c>target</c>, keeping
/// their velocity.
/// </summary>
/// <remarks>
/// The volume is the trigger's own <c>model</c>, the brush hulls it was compiled with, which
/// <see cref="BaseTrigger.InitTrigger"/> supplies. The destination is a plain map entity, usually an
/// <c>info_teleport_destination</c>, which nothing simulates: it is found through its scene node at
/// <see cref="Activate"/>, once every entity in the map has been loaded.
/// </remarks>
public sealed class TriggerTeleport : BaseTrigger
{
    private Vector3 destination;
    private Vector3 destinationAngles;
    private bool hasDestination;

    /// <summary>
    /// Initializes a <c>trigger_teleport</c> from its keyvalues.
    /// </summary>
    /// <param name="system">The world this entity belongs to.</param>
    /// <param name="spawnInfo">The entity's keyvalues and spawn context.</param>
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

        destination = target.GetVector3Property("origin");
        destinationAngles = target.GetVector3Property("angles");
        hasDestination = true;
    }

    /// <inheritdoc/>
    protected override void OnStartTouch(BaseEntity other)
    {
        base.OnStartTouch(other);

        if (!hasDestination)
        {
            return;
        }

        // Lift a unit so the hull does not arrive embedded in the floor
        other.Teleport(destination + new Vector3(0, 0, 1f), destinationAngles);
    }
}
