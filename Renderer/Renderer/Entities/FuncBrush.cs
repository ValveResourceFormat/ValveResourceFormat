using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_brush</c>. A piece of world geometry a map can show, hide, and make solid or not.
/// <c>Enable</c> and <c>Disable</c> switch drawing and solidity together, as in the engine;
/// <c>SetSolid</c> and <c>SetNonsolid</c> leave what is drawn alone. The authored <c>solidity</c>
/// decides what "solid" means for a given brush.
/// </summary>
public sealed class FuncBrush : BaseModelEntity
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

    [EntityInput("Enable")]
    private void InputEnable(EntityInputData data) => SetEnabled(true);

    [EntityInput("Disable")]
    private void InputDisable(EntityInputData data) => SetEnabled(false);

    [EntityInput("Toggle")]
    private void InputToggle(EntityInputData data) => SetEnabled(!IsEnabled);

    [EntityInput("SetSolid")]
    private void InputSetSolid(EntityInputData data) => IsSolid = true;

    [EntityInput("SetNonsolid")]
    private void InputSetNonsolid(EntityInputData data) => IsSolid = false;

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
    }
}
