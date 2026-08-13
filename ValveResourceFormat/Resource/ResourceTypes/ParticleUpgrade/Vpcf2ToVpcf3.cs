using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Bumps m_nBehaviorVersion from 0 to 1 unless the root's m_PreEmissionOperators contains a
/// C_OP_SetControlPointRotation operator.
/// </summary>
internal sealed class Vpcf2ToVpcf3 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf2";
    public override string ToFormat => "vpcf3";

    public override void Apply(KVObject root)
    {
        if (root.GetInt("m_nBehaviorVersion", 0) != 0)
        {
            return;
        }

        foreach (var element in root.ElementsOf("m_PreEmissionOperators"))
        {
            if (UpgradeKV.IsObject(element) && element.GetString("_class", "") == "C_OP_SetControlPointRotation")
            {
                return;
            }
        }

        root.SetInt("m_nBehaviorVersion", 1);
    }
}
