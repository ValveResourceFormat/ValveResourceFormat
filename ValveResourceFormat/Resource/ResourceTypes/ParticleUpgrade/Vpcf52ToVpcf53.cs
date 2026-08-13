using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Folds m_nControlPointNumber into m_TransformInput on ring wave, attract-to-control-point
/// and initial velocity noise; attract additionally moves m_bScaleLocal onto the transform
/// input as m_bUseOrientation, and velocity noise only keeps the control point when
/// m_bLocalSpace was set.
/// </summary>
internal sealed class Vpcf52ToVpcf53 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf52";
    public override string ToFormat => "vpcf53";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            switch (node.GetString("_class", ""))
            {
                case "C_INIT_RingWave":
                {
                    var cp = unchecked((int)node.TakeInt("m_nControlPointNumber", 0));
                    Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_TransformInput"), cp);
                    break;
                }

                case "C_OP_AttractToControlPoint":
                {
                    var cp = unchecked((int)node.TakeInt("m_nControlPointNumber", 0));
                    Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_TransformInput"), cp);
                    var scaleLocal = node.GetBool("m_bScaleLocal", false);
                    node.MergeObject("m_TransformInput").SetBool("m_bUseOrientation", scaleLocal);
                    node.Remove("m_bScaleLocal");
                    break;
                }

                case "C_INIT_InitialVelocityNoise":
                {
                    var cp = unchecked((int)node.TakeInt("m_nControlPointNumber", 0));

                    if (node.GetBool("m_bLocalSpace", false))
                    {
                        Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_TransformInput"), cp);
                    }

                    node.Remove("m_bLocalSpace");
                    break;
                }

                default:
                    break;
            }
        });
    }
}
