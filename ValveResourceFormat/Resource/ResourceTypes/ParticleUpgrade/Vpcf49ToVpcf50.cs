using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Renames C_INIT_CreateWithinSphere to C_INIT_CreateWithinSphereTransform with a CP-range
/// transform input and folds the control point of C_INIT_PositionOffset, C_OP_PositionLock and
/// C_OP_MovementRotateParticleAroundAxis into m_TransformInput. Negative control points clamp
/// to zero in this step. The engine removes the three sphere legacy keys from the transform
/// input instead of the operator, so they survive on the operator and the transform input
/// never carries the end-CP growth time.
/// </summary>
internal sealed class Vpcf49ToVpcf50 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf49";
    public override string ToFormat => "vpcf50";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            switch (node.GetString("_class", ""))
            {
                case "C_INIT_CreateWithinSphere":
                {
                    var useHighest = node.GetBool("m_bUseHighestEndCP", false);
                    var distribute = node.GetBool("m_bDistributeInCPRange", false);

                    node.SetString("_class", "C_INIT_CreateWithinSphereTransform");
                    var cp = Math.Max(unchecked((int)node.TakeInt("m_nControlPointNumber", 0)), 0);
                    var cpMax = unchecked((int)node.TakeInt("m_nControlPointNumberMax", 0));

                    if (useHighest)
                    {
                        cpMax = 63;
                    }

                    var transform = node.MergeObject("m_TransformInput");
                    Vpcf45ToVpcf46.FillTransformInput(transform, cp);
                    transform.SetInt("m_nControlPointRangeMax", cpMax);

                    if (distribute || useHighest)
                    {
                        transform.SetString("m_nType", "PT_TYPE_CONTROL_POINT_RANGE");
                    }

                    break;
                }

                case "C_INIT_PositionOffset":
                case "C_OP_PositionLock":
                {
                    var cp = Math.Max(unchecked((int)node.TakeInt("m_nControlPointNumber", 0)), 0);
                    Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_TransformInput"), cp);
                    break;
                }

                case "C_OP_MovementRotateParticleAroundAxis":
                {
                    var cp = Math.Max(unchecked((int)node.TakeInt("m_nCP", 0)), 0);
                    Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_TransformInput"), cp);
                    break;
                }

                default:
                    break;
            }
        });
    }
}
