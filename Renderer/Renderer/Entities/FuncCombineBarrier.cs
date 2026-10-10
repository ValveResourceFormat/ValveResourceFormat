using System.Globalization;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// HL:A's <c>func_combine_barrier</c>: a Combine force field brush that blocks either humans or the
/// Combine, drawn as an ambient effect over the brush.
/// </summary>
/// <remarks>
/// The effect places particles on the brush model with <c>C_INIT_CreateOnModel</c>, which is not
/// implemented yet, so they gather at the barrier's centre.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/server/CFuncCombineBarrier">CFuncCombineBarrier</seealso>
public sealed class FuncCombineBarrier : FuncBrush
{
    /// <summary>What the barrier stops.</summary>
    public enum State
    {
        /// <summary>Blocks humans, the player included.</summary>
        BlocksHumans = 0,

        /// <summary>Blocks the Combine and lets humans through.</summary>
        BlocksCombine = 1,
    }

    // Default size of one field cell, which the effect is sized in
    private const float CellSpacing = 6f;

    private static readonly Vector3 HumanBlockerColor = new(100f, 255f, 255f);
    private static readonly Vector3 CombineBlockerColor = new(228f, 179f, 107f);

    /// <summary>Gets what the barrier currently stops.</summary>
    public State BarrierState { get; private set; }

    /// <summary>Gets the ambient effect, or <see langword="null"/> before it was first switched on.</summary>
    public ParticleSceneNode? Effect { get; private set; }

    /// <summary>Initializes a barrier from its keyvalues.</summary>
    public FuncCombineBarrier(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        // Read first: the base spawn enables the barrier, which applies the state
        BarrierState = ParseState(KeyValues.GetStringProperty("barrier_state"));

        base.Spawn();
    }

    /// <inheritdoc/>
    protected override void OnEnabledChanged()
    {
        if (!IsEnabled)
        {
            // Disabled: no effect, everything passes
            Effect?.Stop();
            IsSolid = false;
            return;
        }

        if (Effect == null)
        {
            CreateBarrierEffect();
        }
        else
        {
            Effect.Play();
        }

        ApplyState();
    }

    // Control points: 1 and 2 are the ends of the field along its width, 18 the cell count (drives
    // emission), 19 the cell grid
    private void CreateBarrierEffect()
    {
        if ((Collider?.LocalBounds ?? ModelNode?.LocalBoundingBox) is not { } bounds)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' has no bounds to size its effect by", Classname, TargetName);
            return;
        }

        Effect = CreateEffect(KeyValues.GetStringProperty("effect_name"));

        if (Effect == null)
        {
            return;
        }

        var size = bounds.Size;
        var width = MathF.Sqrt(size.X * size.X + size.Y * size.Y);
        var columns = (int)(width / CellSpacing);
        var rows = (int)(size.Z / CellSpacing);

        var placement = RigidTransform;

        // Control point 0 is the barrier's centre, not its origin, so the entity must not place the effect
        Effect.Transform = placement with { Translation = Vector3.Transform(bounds.Center, placement) };
        Effect.GetControlPoint(1).Position = Vector3.Transform(new Vector3(0f, width * 0.5f, 0f), placement);
        Effect.GetControlPoint(2).Position = Vector3.Transform(new Vector3(0f, width * -0.5f, 0f), placement);
        Effect.GetControlPoint(18).Position = new Vector3(columns * rows, 0f, 0f);
        Effect.GetControlPoint(19).Position = new Vector3(columns, rows, CellSpacing);

        AddNode(Effect, followsEntity: false);
    }

    // Control point 16 is the field colour, 17 its accent
    private void ApplyState()
    {
        var blocksHumans = BarrierState == State.BlocksHumans;

        IsSolid = blocksHumans;

        if (Effect == null)
        {
            return;
        }

        Effect.GetControlPoint(16).Position = blocksHumans ? HumanBlockerColor : CombineBlockerColor;
        Effect.GetControlPoint(17).Position = blocksHumans ? CombineBlockerColor : HumanBlockerColor;
    }

    [EntityInput("SetBarrierState")]
    private void InputSetBarrierState(EntityInputData data)
    {
        BarrierState = ParseState(data.Parameter);

        if (IsEnabled)
        {
            ApplyState();
        }
    }

    private static State ParseState(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number == 1
            ? State.BlocksCombine
            : State.BlocksHumans;
}
