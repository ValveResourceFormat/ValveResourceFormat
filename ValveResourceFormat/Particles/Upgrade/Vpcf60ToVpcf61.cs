using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Upgrade;

/// <summary>
/// Rebuilds C_INIT_VelocityRadialRandom's speed scalars as literal float inputs appended at
/// the operator tail with negated values; absent scalars produce negative-zero literals.
/// </summary>
internal sealed class Vpcf60ToVpcf61 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf60";
    public override string ToFormat => "vpcf61";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetStringProperty("_class", "") != "C_INIT_VelocityRadialRandom")
            {
                return;
            }

            var speedMin = node.GetFloatProperty("m_fSpeedMin");
            var speedMax = node.GetFloatProperty("m_fSpeedMax");
            node.Remove("m_fSpeedMin");
            node.Remove("m_fSpeedMax");

            var min = node.SetObject("m_fSpeedMin");
            min.SetString("m_nType", "PF_TYPE_LITERAL");
            min.SetFloat("m_flLiteralValue", -speedMin);

            var max = node.SetObject("m_fSpeedMax");
            max.SetString("m_nType", "PF_TYPE_LITERAL");
            max.SetFloat("m_flLiteralValue", -speedMax);
        });
    }
}
