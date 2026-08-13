using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts C_INIT_PositionPlaceOnGround's trace-along-normal flag into a m_vecTraceDir
/// particle-vector input, copying the trace direction attribute into
/// m_nGroundNormalAttribute when m_bSetNormal is set; m_bSetNormal itself is kept.
/// </summary>
internal sealed class Vpcf63ToVpcf64 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf63";
    public override string ToFormat => "vpcf64";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_INIT_PositionPlaceOnGround"
                || !node.GetBool("m_bTraceAlongNormal", false))
            {
                return;
            }

            var attribute = (int)node.GetInt("m_nTraceDirectionAttribute", 21);
            var setNormal = node.GetBool("m_bSetNormal", false);
            node.Remove("m_bTraceAlongNormal");
            node.Remove("m_nTraceDirectionAttribute");

            if (setNormal)
            {
                node.SetInt("m_nGroundNormalAttribute", attribute);
            }

            var traceDir = node.MergeObject("m_vecTraceDir");
            traceDir.SetString("m_nType", "PVEC_TYPE_PARTICLE_VECTOR");
            traceDir.SetInt("m_nVectorAttribute", attribute);
        });
    }
}
