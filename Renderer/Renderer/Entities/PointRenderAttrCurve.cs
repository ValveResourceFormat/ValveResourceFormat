using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>point_render_attr_curve</c>. On <c>Start</c>, drives a global render attribute by a curve over time.
/// </summary>
/// <remarks>
/// The dynamic combo that <c>active_combo_name</c> enables while the curve plays is not applied.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/server/CPointRenderAttrCurve">CPointRenderAttrCurve</seealso>
public sealed partial class PointRenderAttrCurve : BaseCurveOverTime
{
    private readonly string attributeName;

    /// <summary>Initializes a <c>point_render_attr_curve</c> from its keyvalues.</summary>
    public PointRenderAttrCurve(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
        attributeName = KeyValues.GetStringProperty("attribute_name");
    }

    /// <inheritdoc/>
    protected override bool OnStart(EntityInputData data) => !string.IsNullOrEmpty(attributeName);

    /// <inheritdoc/>
    protected override bool Apply(float value)
    {
        EntitySystem.RendererContext.GlobalRenderAttributes[attributeName] = value;
        return true;
    }
}
