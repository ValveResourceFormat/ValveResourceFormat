using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Bumps m_nBehaviorVersion from 3 to 4 unless the root's m_Initializers contains a
/// C_INIT_RemapInitialCPDirectionToRotation initializer.
/// </summary>
internal sealed class Vpcf5ToVpcf6 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf5";
    public override string ToFormat => "vpcf6";

    public override void Apply(KVObject root)
    {
        if (root.GetInt("m_nBehaviorVersion", 0) != 3)
        {
            return;
        }

        foreach (var element in root.ElementsOf("m_Initializers"))
        {
            if (UpgradeKV.IsObject(element) && element.GetString("_class", "") == "C_INIT_RemapInitialCPDirectionToRotation")
            {
                return;
            }
        }

        root.SetInt("m_nBehaviorVersion", 4);
    }
}
