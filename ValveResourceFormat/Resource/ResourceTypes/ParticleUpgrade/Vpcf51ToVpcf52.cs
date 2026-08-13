using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Rebuilds C_OP_ExternalWindForce's m_nCP into a m_vecSamplePosition CP-value vector input
/// with a unit value scale; the control point is written verbatim with no range check.
/// </summary>
internal sealed class Vpcf51ToVpcf52 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf51";
    public override string ToFormat => "vpcf52";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_OP_ExternalWindForce")
            {
                return;
            }

            var cp = unchecked((int)node.TakeInt("m_nCP", 0));
            var sample = node.SetObject("m_vecSamplePosition");
            sample.SetString("m_nType", "PVEC_TYPE_CP_VALUE");
            sample.SetInt("m_nControlPoint", cp);
            sample.SetFloatArray("m_vCPValueScale", 1f, 1f, 1f);
        });
    }
}
