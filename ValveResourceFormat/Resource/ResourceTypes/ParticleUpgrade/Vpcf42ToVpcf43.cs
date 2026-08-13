using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Rebuilds C_INIT_AddVectorToVector and C_INIT_OffsetVectorToVector as C_INIT_InitVec with a
/// random-uniform-offset input; the add variant gains the add-to-initial set method.
/// </summary>
internal sealed class Vpcf42ToVpcf43 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf42";
    public override string ToFormat => "vpcf43";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var cls = node.GetString("_class", "");
            var isAdd = cls == "C_INIT_AddVectorToVector";

            if (!isAdd && cls != "C_INIT_OffsetVectorToVector")
            {
                return;
            }

            var vecMin = node.GetFloat3(isAdd ? "m_vOffsetMin" : "m_vecOutputMin", Vector3.Zero);
            var vecMax = node.GetFloat3(isAdd ? "m_vOffsetMax" : "m_vecOutputMax", isAdd ? Vector3.Zero : Vector3.One);
            var fieldOut = (int)node.GetInt("m_nFieldOutput", 0);
            var fieldIn = (int)node.GetInt("m_nFieldInput", 0);
            var scale = node.GetFloat3("m_vecScale", Vector3.One);

            Vpcf38ToVpcf39.RebuildGenericOperatorFields(node);
            node.SetString("_class", "C_INIT_InitVec");
            var input = node.SetObject("m_InputValue");
            node.SetInt("m_nOutputField", fieldOut);

            input.SetString("m_nType", "PVEC_TYPE_RANDOM_UNIFORM_OFFSET");
            input.SetFloatArray("m_vRandomMin", vecMin.X, vecMin.Y, vecMin.Z);
            input.SetFloatArray("m_vRandomMax", vecMax.X, vecMax.Y, vecMax.Z);
            input.SetInt("m_nVectorAttribute", fieldIn);
            input.SetFloatArray("m_vVectorAttributeScale", scale.X, scale.Y, scale.Z);

            if (isAdd)
            {
                node.SetString("m_nSetMethod", "PARTICLE_SET_ADD_TO_INITIAL_VALUE");
            }
        });
    }
}
