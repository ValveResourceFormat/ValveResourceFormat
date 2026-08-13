using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Rewrites C_OP_ContinuousEmitter emission scale into m_flScalePerParentParticle and bumps
/// m_nBehaviorVersion from 1 to 2 unless legacy emission or CP-creation behavior is in use.
/// </summary>
internal sealed class Vpcf3ToVpcf4 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf3";
    public override string ToFormat => "vpcf4";

    public override void Apply(KVObject root)
    {
        if (root.GetInt("m_nBehaviorVersion", 0) != 1)
        {
            return;
        }

        var bump = true;

        foreach (var element in root.ElementsOf("m_Emitters"))
        {
            if (!UpgradeKV.IsObject(element) || element.GetString("_class", "") != "C_OP_ContinuousEmitter")
            {
                continue;
            }

            var scale = element.GetFloat("m_flEmissionScale", 0f);

            if (scale <= 0f)
            {
                element.Remove("m_flEmissionScale");
            }
            else if (element.GetBool("m_bScalePerParticle", false))
            {
                element.Remove("m_bScalePerParticle");
                element.Remove("m_flEmissionScale");
                element.SetFloat("m_flScalePerParentParticle", scale);
            }
            else
            {
                bump = false;
            }
        }

        if (!bump)
        {
            return;
        }

        foreach (var element in root.ElementsOf("m_Initializers"))
        {
            if (UpgradeKV.IsObject(element) && element.GetString("_class", "") == "C_INIT_CreateWithinSphere"
                && element.GetBool("m_bUseHighestEndCP", false))
            {
                return;
            }
        }

        foreach (var element in root.ElementsOf("m_Initializers"))
        {
            if (UpgradeKV.IsObject(element) && element.GetString("_class", "") == "C_INIT_CreateFromCPs"
                && element.GetInt("m_nIncrement", 1) != 0)
            {
                return;
            }
        }

        foreach (var element in root.ElementsOf("m_Emitters"))
        {
            if (UpgradeKV.IsObject(element) && element.GetString("_class", "") == "C_OP_NoiseEmitter"
                && element.GetFloat("m_flEmissionScale", 0f) > 0f)
            {
                return;
            }
        }

        root.SetInt("m_nBehaviorVersion", 2);
    }
}
