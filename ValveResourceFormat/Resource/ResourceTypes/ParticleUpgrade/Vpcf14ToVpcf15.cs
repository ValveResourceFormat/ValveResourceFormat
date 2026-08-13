using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts legacy per-operator strength-scale fields on every object into an m_flOpStrength
/// input struct, discarding any authored scalar strength, and drops the legacy fields.
/// </summary>
internal sealed class Vpcf14ToVpcf15 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf14";
    public override string ToFormat => "vpcf15";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var scaleCP = node.GetInt("m_nOpScaleCP", -1);
            var seed = node.GetInt("m_nOpStrengthScaleSeed", 0);
            var minScale = node.GetFloat("m_flOpStrengthMinScale", 1f);
            var maxScale = node.GetFloat("m_flOpStrengthMaxScale", 1f);

            if (scaleCP > -1 || (seed != 0 && (minScale != 1f || maxScale != 1f)))
            {
                var component = node.GetInt("m_nScaleCPComponent", 0);
                var strength = node.SetObject("m_flOpStrength");

                if (scaleCP > -1)
                {
                    strength.SetString("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
                    strength.SetInt("m_nControlPoint", (int)scaleCP);
                    strength.SetInt("m_nVectorComponent", (int)component);
                    strength.SetString("m_nMapType", "PF_MAP_TYPE_REMAP");
                    strength.SetFloat("m_flInput0", 0f);
                    strength.SetFloat("m_flInput1", 1f);
                    strength.SetFloat("m_flOutput0", 0f);
                    strength.SetFloat("m_flOutput1", 1f);
                }
                else
                {
                    strength.SetString("m_nType", "PF_TYPE_RANDOM_UNIFORM");
                    strength.SetFloat("m_flRandomMin", minScale);
                    strength.SetFloat("m_flRandomMax", maxScale);
                    strength.SetString("m_nRandomMode", "PF_RANDOM_MODE_VARYING");
                }
            }

            node.Remove("m_nOpScaleCP");
            node.Remove("m_nScaleCPComponent");
            node.Remove("m_nOpStrengthScaleSeed");
            node.Remove("m_flOpStrengthMinScale");
            node.Remove("m_flOpStrengthMaxScale");
        });
    }
}
