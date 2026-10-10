namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>filter_activator_team</c>. Every test passes, as the player has no team here; this keeps logic
/// gated on the filter reachable.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CFilterTeam">CFilterTeam</seealso>
public sealed class FilterActivatorTeam : BaseEntity
{
    /// <summary>Initializes a <c>filter_activator_team</c>.</summary>
    public FilterActivatorTeam(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    [EntityInput("TestActivator")]
    private void InputTestActivator(EntityInputData data) => EntitySystem.TriggerOutput(this, "OnPass", data.Activator);
}
