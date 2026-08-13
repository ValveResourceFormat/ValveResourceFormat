using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Applies the CP-to-Transform renames in the initializer remap family and folds m_nCP into
/// m_TransformInput on all four classes; C_INIT_RemapQAnglesToRotation keeps its name but
/// still gains the transform input.
/// </summary>
internal sealed class Vpcf46ToVpcf47 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf46";
    public override string ToFormat => "vpcf47";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            switch (node.GetString("_class", ""))
            {
                case "C_INIT_RemapInitialDirectionToCPToVector":
                    node.SetString("_class", "C_INIT_RemapInitialDirectionToTransformToVector");
                    break;

                case "C_INIT_RemapInitialCPDirectionToRotation":
                    node.SetString("_class", "C_INIT_RemapInitialTransformDirectionToRotation");
                    break;

                case "C_INIT_RemapQAnglesToRotation":
                    break;

                case "C_INIT_RemapCPOrientationToRotations":
                    node.SetString("_class", "C_INIT_RemapTransformOrientationToRotations");
                    break;

                default:
                    return;
            }

            var cp = unchecked((int)node.TakeInt("m_nCP", 0));
            Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_TransformInput"), cp);
        });
    }
}
