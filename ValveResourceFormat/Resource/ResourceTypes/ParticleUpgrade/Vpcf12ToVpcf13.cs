using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts m_bParticleFeathering on root renderers into the m_nFeatheringMode enum string.
/// </summary>
internal sealed class Vpcf12ToVpcf13 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf12";
    public override string ToFormat => "vpcf13";

    public override void Apply(KVObject root)
    {
        foreach (var element in root.ElementsOf("m_Renderers"))
        {
            if (!UpgradeKV.IsObject(element) || !element.ContainsKey("m_bParticleFeathering"))
            {
                continue;
            }

            var feathering = element.GetBool("m_bParticleFeathering", false);
            element.Remove("m_bParticleFeathering");
            element.SetString("m_nFeatheringMode", feathering
                ? "PARTICLE_DEPTH_FEATHERING_ON_OPTIONAL"
                : "PARTICLE_DEPTH_FEATHERING_OFF");
        }
    }
}
