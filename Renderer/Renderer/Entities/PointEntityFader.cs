using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>point_entity_fader</c>. On <c>Start</c>, fades its <c>target</c> by a curve of alpha over seconds,
/// leaving the target white.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/server/CPointEntityFader">CPointEntityFader</seealso>
public sealed partial class PointEntityFader : BaseCurveOverTime
{
    private BaseModelEntity? target;

    /// <summary>Initializes a <c>point_entity_fader</c> from its keyvalues.</summary>
    public PointEntityFader(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override bool Apply(float value)
    {
        if (target is not { ModelNode: { } node, IsRemoved: false })
        {
            return false;
        }

        var alpha = unchecked((byte)(int)(value * byte.MaxValue));
        node.TintAlpha = new Vector4(Vector3.One, alpha / (float)byte.MaxValue);
        return true;
    }

    /// <inheritdoc/>
    protected override bool OnStart(EntityInputData data)
    {
        var targetName = KeyValues.GetStringProperty("target");
        var found = string.IsNullOrEmpty(targetName)
            ? null
            : EntitySystem.FindTargets(new EntityIOTarget(targetName, EntityIOTargetType.EntityName), data.Activator, data.Caller).FirstOrDefault();

        target = found as BaseModelEntity;

        if (target == null)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' - failed to find target '{Target}'", Classname, TargetName, targetName);
            return false;
        }

        return true;
    }
}
