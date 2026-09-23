using Microsoft.Extensions.Logging;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_movelinear</c>. A door that travels an authored <c>movedistance</c> rather than its own length,
/// and only moves when told to.
/// </summary>
public class FuncMoveLinear : FuncDoor
{
    /// <summary>Initializes a <c>func_movelinear</c> from its keyvalues.</summary>
    public FuncMoveLinear(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override float ReadWait() => -1f;

    /// <inheritdoc/>
    protected override bool SpawnsOpen() => false;

    /// <inheritdoc/>
    protected override void SetUpTravel()
    {
        PositionOpen = PositionClosed + MoveDirection * KeyValues.GetFloatProperty("movedistance", 100f);

        var start = Math.Clamp(KeyValues.GetFloatProperty("startposition"), 0f, 1f);

        if (start > 0f)
        {
            Origin = Vector3.Lerp(PositionClosed, PositionOpen, start);
            State = start >= 1f ? ToggleState.AtTop : ToggleState.AtBottom;
        }
    }

    /// <summary>Stops wherever it is and jumps to the named entity's origin, ready to set off again.</summary>
    [EntityInput("TeleportToTarget")]
    protected void InputTeleportToTarget(EntityInputData data)
    {
        if (string.IsNullOrEmpty(data.Parameter) || EntitySystem.FindByTargetName(data.Parameter, Scene) is not { } target)
        {
            EntitySystem.Logger.LogWarning("func_movelinear '{TargetName}' teleport target '{Target}' was not found", TargetName, data.Parameter);
            return;
        }

        StopMoving();
        Teleport(target.RigidTransform.Translation, null);

        State = Origin == PositionOpen ? ToggleState.AtTop : ToggleState.AtBottom;
    }
}
