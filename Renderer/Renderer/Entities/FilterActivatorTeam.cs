namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>filter_activator_team</c>. A filter that filters by the team of the activator.
/// </summary>
/// <remarks>
/// The player has no team here, so every test passes, which keeps logic gated on a team filter reachable.
/// </remarks>
public sealed class FilterActivatorTeam : BaseEntity
{
    /// <summary>Initializes a <c>filter_activator_team</c>.</summary>
    public FilterActivatorTeam(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    [EntityInput("TestActivator")]
    private void InputTestActivator(EntityInputData data) => EntitySystem.TriggerOutput(this, "OnPass", data.Activator);
}
