using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts m_bFitCycleToLifetime on root renderers into the m_nAnimationType enum string,
/// with no class filter.
/// </summary>
internal sealed class Vpcf19ToVpcf20 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf19";
    public override string ToFormat => "vpcf20";

    public override void Apply(KVObject root)
    {
        foreach (var element in root.ElementsOf("m_Renderers"))
        {
            if (!UpgradeKV.IsObject(element) || !element.ContainsKey("m_bFitCycleToLifetime"))
            {
                continue;
            }

            var fit = element.GetBool("m_bFitCycleToLifetime", false);
            element.Remove("m_bFitCycleToLifetime");
            element.SetString("m_nAnimationType", fit
                ? "ANIMATION_TYPE_FIT_LIFETIME"
                : "ANIMATION_TYPE_FIXED_RATE");
        }
    }
}
