namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>filter_activator_model</c>. Filters by the activator's model.</summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CFilterModel">CFilterModel</seealso>
public sealed class FilterActivatorModel : BaseEntity
{
    /// <summary>Initializes a <c>filter_activator_model</c>.</summary>
    public FilterActivatorModel(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }
}
