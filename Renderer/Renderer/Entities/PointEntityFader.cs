using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>point_entity_fader</c>. On <c>Start</c>, fades its <c>target</c> by a curve of alpha over seconds,
/// leaving the target white.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/server/CPointEntityFader">CPointEntityFader</seealso>
public sealed class PointEntityFader : BaseEntity
{
    private PiecewiseCurve? curve;
    private BaseModelEntity? target;
    private float startTime;

    /// <summary>Initializes a <c>point_entity_fader</c> from its keyvalues.</summary>
    public PointEntityFader(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        var curveText = KeyValues.GetStringProperty("curve");

        if (string.IsNullOrEmpty(curveText))
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(curveText));
            curve = new PiecewiseCurve(KVDocumentExtensions.ParseKV3(stream).Root, isLooped: false);
        }
        catch (Exception e) when (e is InvalidDataException or KeyValueException or FormatException)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' failed to parse curve string - {Error}", Classname, TargetName, e.Message);
        }
    }

    /// <summary>Applies the curve at the time since <c>Start</c>, until the time passes the end of the curve.</summary>
    public override void Think()
    {
        if (curve == null || target is not { ModelNode: { } node, IsRemoved: false })
        {
            return;
        }

        var elapsed = EntitySystem.CurrentTime - startTime;
        var alpha = unchecked((byte)(int)(curve.Evaluate(elapsed) * byte.MaxValue));

        node.TintAlpha = new Vector4(Vector3.One, alpha / (float)byte.MaxValue);

        if (elapsed <= curve.DomainMax.X)
        {
            SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
        }
    }

    // Restarts the fade, finding the target again
    [EntityInput("Start")]
    private void InputStart(EntityInputData data)
    {
        startTime = EntitySystem.CurrentTime;
        var targetName = KeyValues.GetStringProperty("target");
        var found = string.IsNullOrEmpty(targetName)
            ? null
            : EntitySystem.FindTargets(new EntityIOTarget(targetName, EntityIOTargetType.EntityName), data.Activator, data.Caller).FirstOrDefault();

        target = found as BaseModelEntity;

        if (target == null)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' - failed to find target '{Target}'", Classname, TargetName, targetName);
            return;
        }

        Think();
    }
}
