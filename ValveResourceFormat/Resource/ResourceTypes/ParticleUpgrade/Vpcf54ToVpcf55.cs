using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Folds C_INIT_CreateInEpitrochoid's control point into m_TransformInput unconditionally and
/// C_OP_RenderModels' m_nModelCP into m_modelInput only when the key exists and holds an
/// in-range control point.
/// </summary>
internal sealed class Vpcf54ToVpcf55 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf54";
    public override string ToFormat => "vpcf55";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            switch (node.GetString("_class", ""))
            {
                case "C_INIT_CreateInEpitrochoid":
                {
                    var cp = unchecked((int)node.TakeInt("m_nControlPointNumber", 0));
                    Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_TransformInput"), cp);
                    break;
                }

                case "C_OP_RenderModels":
                {
                    if (!node.ContainsKey("m_nModelCP"))
                    {
                        break;
                    }

                    var cp = unchecked((int)node.TakeInt("m_nModelCP", 0));

                    if ((uint)cp > 63)
                    {
                        break;
                    }

                    var model = node.MergeObject("m_modelInput");
                    model.SetString("m_nType", "PM_TYPE_CONTROL_POINT");
                    model.SetInt("m_nControlPoint", Math.Clamp(cp, 0, 63));
                    break;
                }

                default:
                    break;
            }
        });
    }
}
