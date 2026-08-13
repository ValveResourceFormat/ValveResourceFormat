using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Bumps m_nBehaviorVersion from 2 to 3 unless a C_OP_TwistAroundAxis force generator uses
/// local space with a non-zero control point.
/// </summary>
internal sealed class Vpcf4ToVpcf5 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf4";
    public override string ToFormat => "vpcf5";

    public override void Apply(KVObject root)
    {
        if (root.GetInt("m_nBehaviorVersion", 0) != 2)
        {
            return;
        }

        foreach (var element in root.ElementsOf("m_ForceGenerators"))
        {
            if (UpgradeKV.IsObject(element) && element.GetString("_class", "") == "C_OP_TwistAroundAxis"
                && element.GetBool("m_bLocalSpace", false)
                && element.GetInt("m_nControlPointNumber", 0) != 0)
            {
                return;
            }
        }

        root.SetInt("m_nBehaviorVersion", 3);
    }
}
