using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Upgrade;

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
        if (root.GetIntegerProperty("m_nBehaviorVersion", 0) != 2)
        {
            return;
        }

        foreach (var element in root.ElementsOf("m_ForceGenerators"))
        {
            if (UpgradeKV.IsObject(element) && element.GetStringProperty("_class", "") == "C_OP_TwistAroundAxis"
                && element.GetParticleBooleanProperty("m_bLocalSpace")
                && element.GetIntegerProperty("m_nControlPointNumber", 0) != 0)
            {
                return;
            }
        }

        root.SetInt("m_nBehaviorVersion", 3);
    }
}
