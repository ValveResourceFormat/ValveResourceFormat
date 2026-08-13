using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Folds the control points of six classes into lowercase m_transformInput members: the
/// single-CP position setter, the three model-bound classes, which also gain a clamped
/// m_modelInput, the normal-align initializer, and velocity-from-CP, which builds a
/// m_velocityInput as a CP value or delta.
/// </summary>
internal sealed class Vpcf56ToVpcf57 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf56";
    public override string ToFormat => "vpcf57";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            switch (node.GetString("_class", ""))
            {
                case "C_OP_SetSingleControlPointPosition":
                {
                    var cp = unchecked((int)node.TakeInt("m_nHeadLocation", 0));
                    var useWorld = node.GetBool("m_bUseWorldLocation", false);
                    node.Remove("m_bUseWorldLocation");

                    if (useWorld)
                    {
                        cp = -1;
                    }

                    Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_transformInput"), cp);
                    break;
                }

                case "C_INIT_CreateOnModel":
                case "C_OP_LockToBone":
                case "C_OP_MoveToHitbox":
                {
                    var cp = unchecked((int)node.TakeInt("m_nControlPointNumber", 0));
                    Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_transformInput"), cp);
                    var model = node.MergeObject("m_modelInput");
                    model.SetString("m_nType", "PM_TYPE_CONTROL_POINT");
                    model.SetInt("m_nControlPoint", Math.Clamp(cp, 0, 63));
                    break;
                }

                case "C_INIT_NormalAlignToCP":
                {
                    var cp = unchecked((int)node.TakeInt("m_nControlPointNumber", 0));
                    Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_transformInput"), cp);
                    break;
                }

                case "C_INIT_VelocityFromCP":
                {
                    var cp = unchecked((int)node.TakeInt("m_nControlPoint", 0));
                    var compare = unchecked((int)node.TakeInt("m_nControlPointCompare", -1));
                    var local = unchecked((int)node.TakeInt("m_nControlPointLocal", -1));

                    var velocity = node.MergeObject("m_velocityInput");

                    if ((uint)compare > 63)
                    {
                        if ((uint)cp > 63)
                        {
                            velocity.SetString("m_nType", "PVEC_TYPE_INVALID");
                        }
                        else
                        {
                            velocity.SetString("m_nType", "PVEC_TYPE_CP_VALUE");
                            velocity.SetInt("m_nControlPoint", cp);
                        }
                    }
                    else if ((uint)cp <= 63)
                    {
                        velocity.SetString("m_nType", "PVEC_TYPE_CP_DELTA");
                        velocity.SetInt("m_nControlPoint", cp);
                        velocity.SetInt("m_nDeltaControlPoint", compare);
                    }
                    else
                    {
                        velocity.SetString("m_nType", "PVEC_TYPE_INVALID");
                    }

                    Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_transformInput"), local);
                    break;
                }

                default:
                    break;
            }
        });
    }
}
