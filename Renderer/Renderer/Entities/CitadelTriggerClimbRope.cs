namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>citadel_trigger_climb_rope</c>, Deadlock's climbable rope. The rope itself is drawn by the map; this is
/// the volume around it, and the rope runs up the middle of it.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/deadlock/server/CCitadelTriggerClimbRope">CCitadelTriggerClimbRope</seealso>
public sealed class CitadelTriggerClimbRope : BaseTrigger
{
    /// <summary>Initializes a rope from its keyvalues.</summary>
    public CitadelTriggerClimbRope(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        InitTrigger();
    }

    /// <summary>Gets the world bounds of the volume around the rope, or null when it has none.</summary>
    public AABB? Volume => IsEnabled && Collider is { } collider ? collider.WorldBounds : null;
}
