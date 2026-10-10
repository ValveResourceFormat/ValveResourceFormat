namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>trigger_once</c>. A <see cref="TriggerMultiple"/> that fires <c>OnTrigger</c> for the first thing
/// inside its volume and then removes itself. Any authored <c>wait</c> is ignored.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CTriggerOnce">CTriggerOnce</seealso>
public sealed class TriggerOnce : TriggerMultiple
{
    /// <summary>Initializes a <c>trigger_once</c> from its keyvalues.</summary>
    public TriggerOnce(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        Wait = -1f;
    }
}
