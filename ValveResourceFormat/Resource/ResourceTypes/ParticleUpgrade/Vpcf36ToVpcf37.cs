using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Replaces the scale control point ints on C_OP_DecayMaintainCount and C_OP_MaintainEmitter
/// with a control-point-component m_flScale input, discarding any authored scalar scale.
/// </summary>
internal sealed class Vpcf36ToVpcf37 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf36";
    public override string ToFormat => "vpcf37";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var cls = node.GetString("_class", "");

            if (cls is not ("C_OP_DecayMaintainCount" or "C_OP_MaintainEmitter"))
            {
                return;
            }

            var cp = node.GetInt("m_nScaleControlPoint", -1);

            if (cp <= -1)
            {
                return;
            }

            var component = node.GetInt("m_nScaleControlPointField", 0);
            node.Remove("m_nScaleControlPoint");
            node.Remove("m_nScaleControlPointField");

            var scale = node.SetObject("m_flScale");
            scale.SetString("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
            scale.SetInt("m_nControlPoint", (int)cp);
            scale.SetInt("m_nVectorComponent", (int)component);
        });
    }
}
