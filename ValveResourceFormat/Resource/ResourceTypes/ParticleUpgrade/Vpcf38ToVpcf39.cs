using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Rebuilds six legacy random initializers as C_INIT_InitFloat: the fifteen generic operator
/// fields are re-materialized with defaults, every other member is discarded, and the min/max
/// pair becomes an m_InputValue literal or constant random input, exponentially biased when
/// the exponent key was present.
/// </summary>
internal sealed class Vpcf38ToVpcf39 : ParticleUpgradeStep
{
    private sealed record RandomInitializer(string MinKey, float MinDefault, string MaxKey, float MaxDefault, string ExponentKey, int OutputField, bool ReadsFieldOutput);

    private static readonly Dictionary<string, RandomInitializer> Initializers = new()
    {
        ["C_INIT_RandomLifeTime"] = new("m_fLifetimeMin", 0f, "m_fLifetimeMax", 0f, "m_fLifetimeRandExponent", 1, false),
        ["C_INIT_RandomRadius"] = new("m_flRadiusMin", 1f, "m_flRadiusMax", 1f, "m_flRadiusRandExponent", 3, false),
        ["C_INIT_RandomScalar"] = new("m_flMin", 0f, "m_flMax", 0f, "m_flExponent", 3, true),
        ["C_INIT_RandomAlphaWindowThreshold"] = new("m_flMin", 0f, "m_flMax", 0f, "m_flExponent", 7, false),
        ["C_INIT_RandomAlpha"] = new("m_nAlphaMin", 255f, "m_nAlphaMax", 255f, "m_flAlphaRandExponent", 7, true),
        ["C_INIT_RandomTrailLength"] = new("m_flMinLength", 0.1f, "m_flMaxLength", 0.1f, "m_flLengthRandExponent", 10, false),
    };

    public override string FromFormat => "vpcf38";
    public override string ToFormat => "vpcf39";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var cls = node.GetString("_class", "");

            if (!Initializers.TryGetValue(cls, out var spec))
            {
                return;
            }

            float min;
            float max;

            if (cls == "C_INIT_RandomAlpha")
            {
                min = node.GetInt(spec.MinKey, 255) / 255f;
                max = node.GetInt(spec.MaxKey, 255) / 255f;
            }
            else
            {
                min = node.GetFloat(spec.MinKey, spec.MinDefault);
                max = node.GetFloat(spec.MaxKey, spec.MaxDefault);
            }

            var hasExponent = node.ContainsKey(spec.ExponentKey);
            var exponent = node.GetFloat(spec.ExponentKey, 1f);
            var outputField = spec.ReadsFieldOutput ? (int)node.GetInt("m_nFieldOutput", spec.OutputField) : spec.OutputField;

            RebuildAsInitFloat(node, min, max, hasExponent, exponent);
            node.SetInt("m_nOutputField", outputField);
        });
    }

    /// <summary>
    /// Wipes the operator down to the fifteen generic operator fields, re-materialized with
    /// defaults; m_flOpStrength is kept verbatim when present and every other member is
    /// discarded.
    /// </summary>
    internal static void RebuildGenericOperatorFields(KVObject node)
    {
        var strength = node.Find("m_flOpStrength");
        var disable = node.GetBool("m_bDisableOperator", false);
        var endCap = node.GetString("m_nOpEndCapState", "PARTICLE_ENDCAP_ALWAYS_ON");
        var startFadeIn = node.GetFloat("m_flOpStartFadeInTime", 0f);
        var endFadeIn = node.GetFloat("m_flOpEndFadeInTime", 0f);
        var startFadeOut = node.GetFloat("m_flOpStartFadeOutTime", 0f);
        var endFadeOut = node.GetFloat("m_flOpEndFadeOutTime", 0f);
        var oscillatePeriod = node.GetFloat("m_flOpFadeOscillatePeriod", 0f);
        var normalize = node.GetBool("m_bNormalizeToStopTime", false);
        var timeOffsetMin = node.GetFloat("m_flOpTimeOffsetMin", 0f);
        var timeOffsetMax = node.GetFloat("m_flOpTimeOffsetMax", 0f);
        var timeOffsetSeed = (int)node.GetInt("m_nOpTimeOffsetSeed", 0);
        var timeScaleSeed = (int)node.GetInt("m_nOpTimeScaleSeed", 0);
        var timeScaleMin = node.GetFloat("m_flOpTimeScaleMin", 1f);
        var timeScaleMax = node.GetFloat("m_flOpTimeScaleMax", 1f);

        node.Clear();

        if (strength != null)
        {
            node.SetMember("m_flOpStrength", strength);
        }

        node.SetBool("m_bDisableOperator", disable);
        node.SetString("m_nOpEndCapState", endCap);
        node.SetFloat("m_flOpStartFadeInTime", startFadeIn);
        node.SetFloat("m_flOpEndFadeInTime", endFadeIn);
        node.SetFloat("m_flOpStartFadeOutTime", startFadeOut);
        node.SetFloat("m_flOpEndFadeOutTime", endFadeOut);
        node.SetFloat("m_flOpFadeOscillatePeriod", oscillatePeriod);
        node.SetBool("m_bNormalizeToStopTime", normalize);
        node.SetFloat("m_flOpTimeOffsetMin", timeOffsetMin);
        node.SetFloat("m_flOpTimeOffsetMax", timeOffsetMax);
        node.SetInt("m_nOpTimeOffsetSeed", timeOffsetSeed);
        node.SetInt("m_nOpTimeScaleSeed", timeScaleSeed);
        node.SetFloat("m_flOpTimeScaleMin", timeScaleMin);
        node.SetFloat("m_flOpTimeScaleMax", timeScaleMax);
    }

    /// <summary>
    /// Rebuilds the operator as C_INIT_InitFloat with an m_InputValue built from the min/max
    /// pair and m_nOutputField set to one for the caller to overwrite.
    /// </summary>
    internal static void RebuildAsInitFloat(KVObject node, float min, float max, bool hasExponent, float exponent)
    {
        RebuildGenericOperatorFields(node);
        node.SetString("_class", "C_INIT_InitFloat");
        var input = node.SetObject("m_InputValue");
        node.SetInt("m_nOutputField", 1);

        if (min == max && !hasExponent)
        {
            input.SetString("m_nType", "PF_TYPE_LITERAL");
            input.SetFloat("m_flLiteralValue", min);
            return;
        }

        input.SetString("m_nType", hasExponent ? "PF_TYPE_RANDOM_BIASED" : "PF_TYPE_RANDOM_UNIFORM");
        input.SetFloat("m_flRandomMin", min);
        input.SetFloat("m_flRandomMax", max);
        input.SetString("m_nRandomMode", "PF_RANDOM_MODE_CONSTANT");

        if (hasExponent)
        {
            input.SetString("m_nBiasType", "PF_BIAS_TYPE_EXPONENTIAL");
            input.SetFloat("m_flBiasParameter", BiasParameter(exponent));
        }
    }

    /// <summary>
    /// The exponent-to-bias mapping: exponents of one and above map to 0..-1 over nineteen
    /// units, values below one map to 1..0 directly.
    /// </summary>
    private static float BiasParameter(float exponent)
        => exponent >= 1f
            ? 0f - Math.Clamp((exponent - 1f) / 19f, 0f, 1f)
            : 1f - Math.Clamp(exponent, 0f, 1f);
}
