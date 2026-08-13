using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts C_OP_AttractToControlPoint force scalars into float input structs, mapping the
/// pull-force-to-life remap onto a particle-age input with an exponential bias.
/// </summary>
internal sealed class Vpcf10ToVpcf11 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf10";
    public override string ToFormat => "vpcf11";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_OP_AttractToControlPoint")
            {
                return;
            }

            var forceAmount = node.EnsureMember("m_fForceAmount");
            node.EnsureMember("m_fForceAmountMin");

            if (!forceAmount.IsFloatingPoint())
            {
                return;
            }

            var force = node.GetFloat("m_fForceAmount", 0f);
            var scaleCP = node.GetInt("m_nScaleCP", -1);
            var scaleField = node.GetInt("m_nScaleCPField", 0);
            var remapPull = node.GetBool("m_bRemapPullForceToLife", false);
            var forceMin = node.GetFloat("m_fForceAmountMin", 0f);
            var exponent = node.GetFloat("m_fLifespanScaleExp", 1f);

            node.Remove("m_nScaleCP");
            node.Remove("m_nScaleCPField");
            node.Remove("m_bRemapPullForceToLife");
            node.Remove("m_fLifespanScaleExp");
            node.SetBool("m_bApplyMinForce", false);

            if (remapPull)
            {
                var input = node.SetObject("m_fForceAmount");
                input.SetString("m_nType", "PF_TYPE_PARTICLE_AGE_NORMALIZED");

                if (exponent == 1f)
                {
                    input.SetString("m_nMapType", "PF_MAP_TYPE_REMAP");
                    input.SetFloat("m_flInput0", 0f);
                    input.SetFloat("m_flInput1", 1f);
                    input.SetFloat("m_flOutput0", 0f);
                    input.SetFloat("m_flOutput1", force);
                }
                else
                {
                    input.SetString("m_nMapType", "PF_MAP_TYPE_REMAP_BIASED");
                    input.SetFloat("m_flInput0", 0f);
                    input.SetFloat("m_flInput1", 1f);
                    input.SetFloat("m_flOutput0", 0f);
                    input.SetFloat("m_flOutput1", force);
                    input.SetString("m_nBiasType", "PF_BIAS_TYPE_EXPONENTIAL");
                    input.SetFloat("m_flBiasParameter", Bias(exponent));
                }

                node.SetBool("m_bApplyMinForce", true);

                var minInput = node.SetObject("m_fForceAmountMin");
                minInput.SetString("m_nType", "PF_TYPE_LITERAL");
                minInput.SetFloat("m_flLiteralValue", forceMin);
            }
            else
            {
                var input = node.SetObject("m_fForceAmount");

                if (scaleCP == -1)
                {
                    input.SetString("m_nType", "PF_TYPE_LITERAL");
                    input.SetFloat("m_flLiteralValue", force);
                }
                else
                {
                    input.SetString("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
                    input.SetInt("m_nControlPoint", (int)scaleCP);
                    input.SetInt("m_nVectorComponent", (int)scaleField);
                    input.SetString("m_nMapType", "PF_MAP_TYPE_MULT");
                    input.SetFloat("m_flMultFactor", force);
                }
            }
        });
    }

    /// <summary>
    /// The engine's lifespan-exponent-to-bias mapping: the exponent quantizes to quarter steps
    /// truncated toward zero; quantized values of one and above map to 0..-1 over nineteen
    /// units, values below one map to 1..0 directly.
    /// </summary>
    private static float Bias(float exponent)
    {
        var quantized = (float)Math.Truncate(exponent * 4.0) * 0.25f;

        return quantized >= 1f
            ? 0f - Math.Clamp((quantized - 1f) / 19f, 0f, 1f)
            : 1f - Math.Clamp(quantized, 0f, 1f);
    }
}
