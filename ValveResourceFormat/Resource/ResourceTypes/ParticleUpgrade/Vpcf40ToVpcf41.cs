using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Rebuilds C_INIT_RandomVector as C_INIT_InitVec with an m_InputValue holding either the
/// literal corner vector when both corners match or a random-uniform vector range.
/// </summary>
internal sealed class Vpcf40ToVpcf41 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf40";
    public override string ToFormat => "vpcf41";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node => RewriteRandomVector(node));
    }

    internal static void RewriteRandomVector(KVObject node)
    {
        if (node.GetString("_class", "") != "C_INIT_RandomVector")
        {
            return;
        }

        var vecMin = node.GetFloat3("m_vecMin", Vector3.Zero);
        var vecMax = node.GetFloat3("m_vecMax", Vector3.Zero);
        var outputField = (int)node.GetInt("m_nFieldOutput", 0);

        Vpcf38ToVpcf39.RebuildGenericOperatorFields(node);
        node.SetString("_class", "C_INIT_InitVec");
        var input = node.SetObject("m_InputValue");
        node.SetInt("m_nOutputField", outputField);

        if (vecMin == vecMax)
        {
            input.SetString("m_nType", "PVEC_TYPE_LITERAL");
            input.SetFloatArray("m_vLiteralValue", vecMin.X, vecMin.Y, vecMin.Z);
        }
        else
        {
            input.SetString("m_nType", "PVEC_TYPE_RANDOM_UNIFORM");
            input.SetFloatArray("m_vRandomMin", vecMin.X, vecMin.Y, vecMin.Z);
            input.SetFloatArray("m_vRandomMax", vecMax.X, vecMax.Y, vecMax.Z);
        }
    }
}
