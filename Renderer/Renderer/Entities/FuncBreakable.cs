using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>func_breakable</c>. A solid brush that can be broken</summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CBreakable">CBreakable</seealso>
public sealed class FuncBreakable : BaseModelEntity
{
    /// <summary>Gets the remaining strength; breaking happens at zero.</summary>
    public float Health { get; private set; }

    /// <summary>Gets whether the brush has been broken.</summary>
    public bool IsBroken { get; private set; }

    /// <summary>Initializes a <c>func_breakable</c> from its keyvalues.</summary>
    public FuncBreakable(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        Health = KeyValues.GetInt32Property("health", 1);
    }

    /// <summary>Breaks the brush: hidden, passable, and <c>OnBreak</c> fired.</summary>
    public void Break(BaseEntity? activator = null)
    {
        if (IsBroken)
        {
            return;
        }

        IsBroken = true;
        Health = 0f;
        IsDrawn = false;
        IsSolid = false;

        EntitySystem.TriggerOutput(this, "OnBreak", activator);
    }

    [EntityInput("Break")]
    private void InputBreak(EntityInputData data) => Break(data.Activator);

    [EntityInput("SetHealth")]
    private void InputSetHealth(EntityInputData data) => SetHealth(data.Int((int)Health), data.Activator);

    [EntityInput("AddHealth")]
    private void InputAddHealth(EntityInputData data) => SetHealth(Health + data.Int(), data.Activator);

    [EntityInput("RemoveHealth")]
    private void InputRemoveHealth(EntityInputData data) => SetHealth(Health - data.Int(), data.Activator);

    private void SetHealth(float health, BaseEntity? activator)
    {
        if (IsBroken)
        {
            return;
        }

        Health = health;

        EntitySystem.TriggerOutput(this, "OnHealthChanged", activator);

        if (Health <= 0f)
        {
            Break(activator);
        }
    }
}
