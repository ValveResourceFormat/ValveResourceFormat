using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Rebuilds C_OP_RenderModels' m_nSkin into a CP-component float input from m_nSkinCP; only an
/// existing key with a value other than minus one triggers the rebuild, and the control point
/// is written verbatim with no range check.
/// </summary>
internal sealed class Vpcf57ToVpcf58 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf57";
    public override string ToFormat => "vpcf58";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_OP_RenderModels" || !node.ContainsKey("m_nSkinCP"))
            {
                return;
            }

            var cp = unchecked((int)node.TakeInt("m_nSkinCP", 0));

            if (cp == -1)
            {
                return;
            }

            var skin = node.SetObject("m_nSkin");
            skin.SetString("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
            skin.SetInt("m_nControlPoint", cp);
            skin.SetInt("m_nVectorComponent", 0);
        });
    }
}
