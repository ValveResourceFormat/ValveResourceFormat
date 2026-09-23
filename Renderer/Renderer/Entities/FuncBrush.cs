using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_brush</c>. A piece of world geometry a map can show, hide, and make solid or not.
/// <c>Enable</c> and <c>Disable</c> switch drawing and solidity together, as in the engine;
/// <c>SetSolid</c> and <c>SetNonsolid</c> leave what is drawn alone. The authored <c>solidity</c>
/// decides what "solid" means for a given brush.
/// </summary>
public class FuncBrush : BaseModelEntity
{
    /// <summary>What a <c>func_brush</c>'s <c>solidity</c> keyvalue means.</summary>
    public enum SolidityMode
    {
        /// <summary>Solid whenever it is drawn.</summary>
        ToggleSolid = 0,

        /// <summary>Never solid, whatever else happens to it.</summary>
        NeverSolid = 1,

        /// <summary>Always solid, even while hidden.</summary>
        AlwaysSolid = 2,
    }

    /// <summary>Gets how this brush decides whether it is solid.</summary>
    public SolidityMode Solidity { get; private set; }

    /// <summary>Gets whether the brush is switched on: drawn, and solid if its solidity allows.</summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>Initializes a <c>func_brush</c> from its keyvalues.</summary>
    public FuncBrush(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        Solidity = (SolidityMode)KeyValues.GetInt32Property("solidity");

        SetEnabled(!KeyValues.GetBooleanProperty("startdisabled"));
    }

    /// <summary>Switches the brush on.</summary>
    [EntityInput("Enable")]
    protected void InputEnable(EntityInputData data) => SetEnabled(true);

    /// <summary>Switches the brush off.</summary>
    [EntityInput("Disable")]
    protected void InputDisable(EntityInputData data) => SetEnabled(false);

    /// <summary>Switches the brush on or off.</summary>
    [EntityInput("Toggle")]
    protected void InputToggle(EntityInputData data) => SetEnabled(!IsEnabled);

    /// <summary>Makes the brush solid.</summary>
    [EntityInput("SetSolid")]
    protected void InputSetSolid(EntityInputData data) => IsSolid = true;

    /// <summary>Makes the brush not solid.</summary>
    [EntityInput("SetNonsolid")]
    protected void InputSetNonsolid(EntityInputData data) => IsSolid = false;

    /// <summary>Called after the brush was switched on or off, and once as it spawns.</summary>
    protected virtual void OnEnabledChanged()
    {
    }

    private void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        IsDrawn = enabled;

        IsSolid = Solidity switch
        {
            SolidityMode.NeverSolid => false,
            SolidityMode.AlwaysSolid => true,
            _ => enabled,
        };

        OnEnabledChanged();
    }
}
