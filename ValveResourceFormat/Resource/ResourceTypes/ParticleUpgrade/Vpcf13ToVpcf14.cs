using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Bumps m_nBehaviorVersion from 6 to 7 and then 7 to 8, each stage gated on the absence of
/// sequential-path and CP-direction-remap operators; a clean document goes 6 to 8 in one step.
/// </summary>
internal sealed class Vpcf13ToVpcf14 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf13";
    public override string ToFormat => "vpcf14";

    public override void Apply(KVObject root)
    {
        if (root.GetInt("m_nBehaviorVersion", 0) == 6)
        {
            foreach (var element in root.ElementsOf("m_Initializers"))
            {
                if (UpgradeKV.IsObject(element) && element.GetString("_class", "") == "C_INIT_RemapInitialCPDirectionToRotation")
                {
                    return;
                }
            }

            root.SetInt("m_nBehaviorVersion", 7);
        }

        if (root.GetInt("m_nBehaviorVersion", 0) == 7)
        {
            foreach (var element in root.ElementsOf("m_Initializers"))
            {
                if (UpgradeKV.IsObject(element) && element.GetString("_class", "") == "C_INIT_CreateSequentialPath")
                {
                    return;
                }
            }

            foreach (var element in root.ElementsOf("m_Operators"))
            {
                if (UpgradeKV.IsObject(element) && element.GetString("_class", "") == "C_OP_LockToSavedSequentialPath")
                {
                    return;
                }
            }

            root.SetInt("m_nBehaviorVersion", 8);
        }
    }
}
