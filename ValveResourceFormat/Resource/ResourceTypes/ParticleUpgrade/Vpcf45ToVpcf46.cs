using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Renames C_INIT_RemapCPtoVector to C_INIT_RemapTransformToVector, folding m_nCPInput into
/// m_TransformInput and a non-default m_nLocalSpaceCP into m_LocalSpaceTransform.
/// </summary>
internal sealed class Vpcf45ToVpcf46 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf45";
    public override string ToFormat => "vpcf46";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_INIT_RemapCPtoVector")
            {
                return;
            }

            node.SetString("_class", "C_INIT_RemapTransformToVector");
            var cpInput = unchecked((int)node.TakeInt("m_nCPInput", 0));
            var localCP = unchecked((int)node.TakeInt("m_nLocalSpaceCP", -1));

            FillTransformInput(node.MergeObject("m_TransformInput"), cpInput);

            if (localCP != -1)
            {
                FillTransformInput(node.MergeObject("m_LocalSpaceTransform"), localCP);
            }
        });
    }

    /// <summary>
    /// Writes the transform-input members for a folded control point: values outside 0..63,
    /// negatives included, become PT_TYPE_INVALID without a control point member.
    /// </summary>
    internal static void FillTransformInput(KVObject transform, int controlPoint)
    {
        if ((uint)controlPoint > 63)
        {
            transform.SetString("m_nType", "PT_TYPE_INVALID");
        }
        else
        {
            transform.SetString("m_nType", "PT_TYPE_CONTROL_POINT");
            transform.SetInt("m_nControlPoint", controlPoint);
        }
    }
}
