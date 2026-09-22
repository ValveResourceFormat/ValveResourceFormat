using System.Globalization;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// HL:A's <c>func_combine_barrier</c>: a Combine force field. A brush that blocks either humans or the
/// Combine, drawn by an ambient effect spread over the brush and coloured by which of the two it blocks.
/// </summary>
/// <remarks>
/// The effect places its particles on the brush model through <c>C_INIT_CreateOnModel</c>, which the
/// particle system does not implement yet, so they gather at the barrier's centre for now.
/// </remarks>
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

    /// <summary>The size of one cell of the field, which the effect is sized in. The game's own default.</summary>
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
        // Read before the base spawns, which switches the barrier on and so needs to know its state
        BarrierState = ParseState(KeyValues.GetStringProperty("barrier_state"));

        base.Spawn();
    }

    /// <inheritdoc/>
    protected override void OnEnabledChanged()
    {
        if (!IsEnabled)
        {
            // A disabled barrier destroys its effect, and lets everything through
            Effect?.Stop();
            IsSolid = false;
            return;
        }

        IsSolid = BarrierState == State.BlocksHumans;

        if (Effect == null)
        {
            CreateEffect();
        }
        else
        {
            Effect.Play();
        }

        ApplyStateColors();
    }

    /// <summary>
    /// Builds the effect, sized to the brush: control points 1 and 2 at the two ends of the field along
    /// its width, 18 the number of cells, which drives how much it emits, and 19 the cell grid.
    /// </summary>
    private void CreateEffect()
    {
        var effectName = KeyValues.GetStringProperty("effect_name");

        if (string.IsNullOrEmpty(effectName)
            || EntitySystem.FileLoader.LoadFileCompiled(effectName)?.DataBlock is not ParticleSystem particleSystem)
        {
            return;
        }

        if ((Collider?.LocalBounds ?? ModelNode?.LocalBoundingBox) is not { } bounds)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' has no bounds to size its effect by", Classname, TargetName);
            return;
        }

        var size = bounds.Size;
        var width = MathF.Sqrt(size.X * size.X + size.Y * size.Y);
        var columns = (int)(width / CellSpacing);
        var rows = (int)(size.Z / CellSpacing);

        var placement = EntityTransformHelper.ToRigidTransformationMatrix(Angles, Origin) * ParentTransform;

        // Control point 0 follows the centre of the barrier rather than its origin, so the effect is not
        // one the entity places
        Effect = new ParticleSceneNode(Scene, particleSystem, playedByEntity: true)
        {
            Name = effectName,
            Transform = placement with { Translation = Vector3.Transform(bounds.Center, placement) },
            LayerName = Scene.ParticlesLayerName,
        };

        Effect.GetControlPoint(1).Position = Vector3.Transform(new Vector3(0f, width * 0.5f, 0f), placement);
        Effect.GetControlPoint(2).Position = Vector3.Transform(new Vector3(0f, width * -0.5f, 0f), placement);
        Effect.GetControlPoint(18).Position = new Vector3(columns * rows, 0f, 0f);
        Effect.GetControlPoint(19).Position = new Vector3(columns, rows, CellSpacing);

        AddNode(Effect, followsEntity: false);
    }

    /// <summary>Colours the effect by what the barrier blocks: control point 16 the field, 17 its accent.</summary>
    private void ApplyStateColors()
    {
        if (Effect == null)
        {
            return;
        }

        var blocksHumans = BarrierState == State.BlocksHumans;

        Effect.GetControlPoint(16).Position = blocksHumans ? HumanBlockerColor : CombineBlockerColor;
        Effect.GetControlPoint(17).Position = blocksHumans ? CombineBlockerColor : HumanBlockerColor;
    }

    /// <summary>Switches what the barrier blocks.</summary>
    [EntityInput("SetBarrierState")]
    private void InputSetBarrierState(EntityInputData data)
    {
        BarrierState = ParseState(data.Parameter);

        if (IsEnabled)
        {
            IsSolid = BarrierState == State.BlocksHumans;
            ApplyStateColors();
        }
    }

    private static State ParseState(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number == 1
            ? State.BlocksCombine
            : State.BlocksHumans;
}
