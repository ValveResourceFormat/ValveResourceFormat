using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Bumps m_nBehaviorVersion from 9 to 10 unless a root C_OP_RenderSprites renderer fits its
/// animation cycle to the particle lifetime.
/// </summary>
internal sealed class Vpcf22ToVpcf23 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf22";
    public override string ToFormat => "vpcf23";

    public override void Apply(KVObject root)
    {
        if (root.GetInt("m_nBehaviorVersion", 0) != 9)
        {
            return;
        }

        foreach (var element in root.ElementsOf("m_Renderers"))
        {
            if (!UpgradeKV.IsObject(element) || element.GetString("_class", "") != "C_OP_RenderSprites")
            {
                continue;
            }

            if (!element.ContainsKey("m_bFitCycleToLifetime") && !element.ContainsKey("m_nAnimationType"))
            {
                continue;
            }

            if (element.GetBool("m_bFitCycleToLifetime", false))
            {
                return;
            }

            if (element.GetString("m_nAnimationType", "ANIMATION_TYPE_FIXED_RATE") != "ANIMATION_TYPE_FIXED_RATE")
            {
                return;
            }
        }

        root.SetInt("m_nBehaviorVersion", 10);
    }
}
