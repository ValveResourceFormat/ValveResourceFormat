using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Bumps m_nBehaviorVersion from 4 to 5 unless any element of a root-level array carries
/// m_bDisableOperator, or a root m_Children entry carries m_bDisableChild.
/// </summary>
internal sealed class Vpcf6ToVpcf7 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf6";
    public override string ToFormat => "vpcf7";

    public override void Apply(KVObject root)
    {
        if (root.GetInt("m_nBehaviorVersion", 0) != 4)
        {
            return;
        }

        foreach (var value in root.Values)
        {
            if (!value.IsArray)
            {
                continue;
            }

            foreach (var element in value.Elements())
            {
                if (UpgradeKV.IsObject(element) && element.GetBool("m_bDisableOperator", false))
                {
                    return;
                }
            }
        }

        foreach (var element in root.ElementsOf("m_Children"))
        {
            if (UpgradeKV.IsObject(element) && element.GetBool("m_bDisableChild", false))
            {
                return;
            }
        }

        root.SetInt("m_nBehaviorVersion", 5);
    }
}
