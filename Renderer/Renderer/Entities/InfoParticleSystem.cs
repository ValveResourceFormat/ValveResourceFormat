using System.Globalization;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>info_particle_system</c>, and Dota's <c>dota_world_particle_system</c>: plays <c>effect_name</c> at
/// the entity, with its control points bound to the entities the <c>cpointN</c> keys name.
/// </summary>
public class InfoParticleSystem : BaseEntity
{
    // At most this many cpointN_value keys are applied
    private const int MaxLiteralControlPointValues = 4;

    private static readonly string[] ControlPointKeys = CreateKeys("cpoint{0}");
    private static readonly string[] ControlPointValueKeys = CreateKeys("cpoint{0}_value");

    /// <summary>Gets the effect, or <see langword="null"/> when <c>effect_name</c> did not load.</summary>
    public ParticleSceneNode? Effect { get; private set; }

    /// <summary>Initializes a particle system from its keyvalues.</summary>
    public InfoParticleSystem(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        var snapshotFile = KeyValues.GetStringProperty("snapshot_file");
        var snapshot = string.IsNullOrEmpty(snapshotFile)
            ? null
            : EntitySystem.FileLoader.LoadFileCompiled(snapshotFile)?.GetBlockByType(BlockType.SNAP) as ParticleSnapshot;

        Effect = CreateEffect(KeyValues.GetStringProperty("effect_name"), snapshot);

        if (Effect == null)
        {
            return;
        }

        // Control point 0 places the effect, so when it names another entity this one must not
        AddNode(Effect, followsEntity: string.IsNullOrEmpty(KeyValues.GetStringProperty(ControlPointKeys[0])));

        if (!KeyValues.GetBooleanProperty("start_active", true))
        {
            Effect.Stop();
        }
    }

    // Bound in Activate: the entities the control points name may be authored after this one
    /// <inheritdoc/>
    public override void Activate()
    {
        if (Effect == null)
        {
            return;
        }

        for (var index = 0; index < ControlPointKeys.Length; index++)
        {
            var targetName = KeyValues.GetStringProperty(ControlPointKeys[index]);

            if (!string.IsNullOrEmpty(targetName))
            {
                BindControlPoint(Effect, index, targetName);
            }
        }

        ApplyLiteralControlPointValues(Effect);
    }

    // Control point 0 is the effect's placement, so naming an entity there moves the whole effect to it
    private void BindControlPoint(ParticleSceneNode effect, int index, string targetName)
    {
        BaseEntity? target;

        if (targetName[0] == '!')
        {
            if (!string.Equals(targetName, "!self", StringComparison.OrdinalIgnoreCase))
            {
                EntitySystem.Logger.LogDebug("Particle entity '{Target}' points {Key} at '{ControlPointTarget}', which only exists at runtime",
                    TargetName, ControlPointKeys[index], targetName);
                return;
            }

            target = this;
        }
        else
        {
            target = EntitySystem.FindByTargetName(targetName);
        }

        if (target == null)
        {
            EntitySystem.Logger.LogWarning("Particle entity '{Target}' points {Key} at '{ControlPointTarget}', which does not exist",
                TargetName, ControlPointKeys[index], targetName);
            return;
        }

        if (index == 0)
        {
            effect.Transform = target.Transform;
        }

        if (target.RootNode is { } targetNode)
        {
            effect.BindControlPoint(index, targetNode, target.Transform);
        }
        else
        {
            effect.SetControlPoint(index, target.Transform);
        }
    }

    // data_cp takes a vector, tint_cp a colour, and up to four cpointN_value keys a position each.
    // Applied after the cpointN bindings, overriding them.
    private void ApplyLiteralControlPointValues(ParticleSceneNode effect)
    {
        var dataControlPoint = LiteralControlPointIndex("data_cp");

        if (dataControlPoint >= 0)
        {
            effect.GetControlPoint(dataControlPoint).Position = KeyValues.GetVector3Property("data_cp_value");
        }

        var tintControlPoint = LiteralControlPointIndex("tint_cp");

        if (tintControlPoint >= 0)
        {
            effect.GetControlPoint(tintControlPoint).Position = KeyValues.GetVector3Property("tint_cp_color", new Vector3(255f));
        }

        var applied = 0;

        for (var index = 0; index < ControlPointValueKeys.Length && applied < MaxLiteralControlPointValues; index++)
        {
            var value = KeyValues.GetStringProperty(ControlPointValueKeys[index]);

            if (!string.IsNullOrEmpty(value) && EntityTransformHelper.TryParseVector3(value, out var position))
            {
                effect.GetControlPoint(index).Position = position;
                applied++;
            }
        }
    }

    // -1 when unused or out of range
    private int LiteralControlPointIndex(string key)
        => KeyValues.GetInt32Property(key, -1) is var index && index >= 0 && index < ControlPointKeys.Length ? index : -1;

    /// <summary>Starts the effect over.</summary>
    [EntityInput("Start")]
    protected void InputStart(EntityInputData data) => Effect?.Play();

    /// <summary>Stops emitting, leaving the particles already alive to finish.</summary>
    [EntityInput("Stop")]
    protected void InputStop(EntityInputData data) => Effect?.StopEmission();

    /// <summary>Stops emitting and plays the effect's endcap.</summary>
    [EntityInput("StopPlayEndCap")]
    protected void InputStopPlayEndCap(EntityInputData data) => Effect?.PlayEndCap();

    /// <summary>Removes every particle at once.</summary>
    [EntityInput("DestroyImmediately")]
    protected void InputDestroyImmediately(EntityInputData data) => Effect?.Stop();

    private static string[] CreateKeys(string format)
    {
        var keys = new string[64];

        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = string.Format(CultureInfo.InvariantCulture, format, i);
        }

        return keys;
    }
}
