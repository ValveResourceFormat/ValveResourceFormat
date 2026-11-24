using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>An entity that plays its <c>curve</c> of values from <c>Start</c>, holding the last value after.</summary>
public abstract partial class BaseCurveOverTime : BaseEntity
{
    private PiecewiseCurve? curve;
    private float startTime;

    /// <summary>Initializes the entity from its keyvalues.</summary>
    protected BaseCurveOverTime(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
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
            using var stream = new StringStream(curveText, Encoding.UTF8);
            curve = new PiecewiseCurve(KVDocumentExtensions.ParseKV3(stream).Root, isLooped: false);
        }
        catch (Exception e) when (e is InvalidDataException or KeyValueException or FormatException)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' failed to parse curve string - {Error}", Classname, TargetName, e.Message);
        }
    }

    /// <summary>Applies the curve until it ends.</summary>
    public override void Think()
    {
        if (curve == null)
        {
            return;
        }

        var elapsed = EntitySystem.CurrentTime - startTime;

        if (!Apply(curve.Evaluate(elapsed)))
        {
            return;
        }

        if (elapsed <= curve.DomainMax.X)
        {
            SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
        }
    }

    /// <summary>Applies a value of the curve.</summary>
    /// <returns>Whether to keep playing the curve.</returns>
    protected abstract bool Apply(float value);

    /// <summary>Prepares a new play of the curve.</summary>
    /// <returns>Whether to play it.</returns>
    protected virtual bool OnStart(EntityInputData data) => true;

    // Restarts the curve from its beginning
    [EntityInput("Start")]
    private void InputStart(EntityInputData data)
    {
        startTime = EntitySystem.CurrentTime;

        if (OnStart(data))
        {
            Think();
        }
    }
}
