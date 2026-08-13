using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Bumps m_nBehaviorVersion from 8 to 9, gated on the absence of the inherit-from-parent
/// initializer in m_Initializers and the matching operator in m_Operators.
/// </summary>
internal sealed class Vpcf18ToVpcf19 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf18";
    public override string ToFormat => "vpcf19";

    public override void Apply(KVObject root)
    {
        if (root.GetInt("m_nBehaviorVersion", 0) != 8)
        {
            return;
        }

        foreach (var element in root.ElementsOf("m_Initializers"))
        {
            if (UpgradeKV.IsObject(element) && element.GetString("_class", "") == "C_INIT_InheritFromParentParticles")
            {
                return;
            }
        }

        foreach (var element in root.ElementsOf("m_Operators"))
        {
            if (UpgradeKV.IsObject(element) && element.GetString("_class", "") == "C_OP_InheritFromParentParticles")
            {
                return;
            }
        }

        root.SetInt("m_nBehaviorVersion", 9);
    }
}
