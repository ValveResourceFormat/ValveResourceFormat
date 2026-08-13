using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Rebuilds the three scalar remap initializers as C_INIT_InitFloat with a remap-mapped input
/// typed per class; zero-length active windows disable the operator and other non-default
/// windows become a notched collection-age strength input.
/// </summary>
internal sealed class Vpcf43ToVpcf44 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf43";
    public override string ToFormat => "vpcf44";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var cls = node.GetString("_class", "");

            if (cls is not ("C_INIT_RemapScalar" or "C_INIT_RemapCPtoScalar" or "C_INIT_RemapSpeedToScalar"))
            {
                return;
            }

            var inputMin = node.GetFloat("m_flInputMin", 0f);
            var inputMax = node.GetFloat("m_flInputMax", 1f);
            var outputMin = node.GetFloat("m_flOutputMin", 0f);
            var outputMax = node.GetFloat("m_flOutputMax", 1f);
            var startTime = node.GetFloat("m_flStartTime", -1f);
            var endTime = node.GetFloat("m_flEndTime", -1f);
            var fieldOutput = (int)node.GetInt("m_nFieldOutput", 3);
            var fieldInput = (int)node.GetInt("m_nFieldInput", 8);
            var setMethod = node.GetString("m_nSetMethod", "PARTICLE_SET_REPLACE_VALUE");
            var remapBias = node.GetFloat("m_flRemapBias", 0.5f);
            var cpInput = (int)node.GetInt("m_nCPInput", node.GetInt("m_nControlPointNumber", 0));
            var field = (int)node.GetInt("m_nField", 0);
            var perParticle = cls == "C_INIT_RemapSpeedToScalar" && node.GetBool("m_bPerParticle", false);

            Vpcf38ToVpcf39.RebuildGenericOperatorFields(node);
            node.SetString("_class", "C_INIT_InitFloat");
            var input = node.SetObject("m_InputValue");
            node.SetInt("m_nOutputField", fieldOutput);
            node.SetString("m_nSetMethod", setMethod);

            var disabled = startTime != -1f && startTime == endTime;

            if (disabled)
            {
                node.SetBool("m_bDisableOperator", true);
            }

            input.SetFloat("m_flInput0", inputMin);
            input.SetFloat("m_flInput1", inputMax);
            input.SetFloat("m_flOutput0", outputMin);
            input.SetFloat("m_flOutput1", outputMax);

            if (remapBias == 0.5f)
            {
                input.SetString("m_nMapType", "PF_MAP_TYPE_REMAP");
            }
            else
            {
                input.SetString("m_nBiasType", "PF_BIAS_TYPE_STANDARD");
                input.SetFloat("m_flBiasParameter", remapBias);
                input.SetString("m_nMapType", "PF_MAP_TYPE_REMAP_BIASED");
            }

            if (!disabled && (startTime != -1f || endTime != -1f))
            {
                var strength = node.MergeObject("m_flOpStrength");
                strength.SetString("m_nType", "PF_TYPE_COLLECTION_AGE");
                strength.SetString("m_nMapType", "PF_MAP_TYPE_NOTCHED");
                strength.SetFloat("m_flNotchedRangeMin", startTime);
                strength.SetFloat("m_flNotchedRangeMax", endTime);
                strength.SetFloat("m_flNotchedOutputOutside", 0f);
                strength.SetFloat("m_flNotchedOutputInside", 1f);
            }

            switch (cls)
            {
                case "C_INIT_RemapScalar":
                    input.SetString("m_nType", "PF_TYPE_PARTICLE_FLOAT");
                    input.SetInt("m_nScalarAttribute", fieldInput);
                    break;

                case "C_INIT_RemapCPtoScalar":
                    input.SetString("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
                    input.SetInt("m_nControlPoint", cpInput);
                    input.SetInt("m_nVectorComponent", field);
                    break;

                default:
                    if (perParticle)
                    {
                        input.SetString("m_nType", "PF_TYPE_PARTICLE_SPEED");
                    }
                    else
                    {
                        input.SetString("m_nType", "PF_TYPE_CONTROL_POINT_SPEED");
                        input.SetInt("m_nControlPoint", cpInput);
                    }

                    break;
            }
        });
    }
}
