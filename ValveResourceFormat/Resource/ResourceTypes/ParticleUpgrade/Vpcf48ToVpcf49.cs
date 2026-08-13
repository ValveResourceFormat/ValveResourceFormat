using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Renames the distance and percentage-between-CPs operator family by replacing every
/// case-insensitive "cp" with "Transform" and then turning "LerpTransforms" back into
/// "LerpCPs", folding m_nStartCP into m_TransformStart and m_nEndCP into m_TransformEnd;
/// the single-CP distance class never gains a transform end.
/// </summary>
internal sealed class Vpcf48ToVpcf49 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf48";
    public override string ToFormat => "vpcf49";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var cls = node.GetString("_class", "");
            var isDistanceToCP = cls == "C_OP_DistanceToCP";
            var isCylindrical = cls == "C_OP_CylindricalDistanceToCP";

            if (!isDistanceToCP && !isCylindrical
                && cls is not ("C_OP_DistanceBetweenCPs" or "C_OP_PercentageBetweenCPs"
                    or "C_OP_PercentageBetweenCPsVector" or "C_OP_PercentageBetweenCPLerpCPs"))
            {
                return;
            }

            var renamed = Vpcf47ToVpcf48.ReplaceCaseInsensitive(cls, "cp", "Transform");
            node.SetString("_class", Vpcf47ToVpcf48.ReplaceCaseInsensitive(renamed, "LerpTransforms", "LerpCPs"));

            var start = unchecked((int)node.TakeInt("m_nStartCP", 0));
            Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_TransformStart"), start);

            if (!isDistanceToCP)
            {
                var end = unchecked((int)node.TakeInt("m_nEndCP", isCylindrical ? 0 : 1));
                Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_TransformEnd"), end);
            }
        });
    }
}
