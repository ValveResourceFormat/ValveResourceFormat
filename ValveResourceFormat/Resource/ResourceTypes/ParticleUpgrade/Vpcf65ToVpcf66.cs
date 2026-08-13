using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Rebuilds C_OP_MaxVelocity's m_flMaxVelocity into a CP-component input when a consumed
/// m_nOverrideCP holds a value other than minus one, discarding the scalar velocity;
/// m_nOverrideCPField is read but kept, while the unprefixed nOverrideCPField key is always
/// removed.
/// </summary>
internal sealed class Vpcf65ToVpcf66 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf65";
    public override string ToFormat => "vpcf66";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_OP_MaxVelocity")
            {
                return;
            }

            if (node.ContainsKey("m_nOverrideCP"))
            {
                var overrideCP = unchecked((int)node.TakeInt("m_nOverrideCP", 0));

                if (overrideCP != -1)
                {
                    var field = (int)node.GetInt("m_nOverrideCPField", 0);
                    var maxVelocity = node.SetObject("m_flMaxVelocity");
                    maxVelocity.SetString("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
                    maxVelocity.SetInt("m_nControlPoint", overrideCP);
                    maxVelocity.SetInt("m_nVectorComponent", field);
                }
            }

            node.Remove("nOverrideCPField");
        });
    }
}
