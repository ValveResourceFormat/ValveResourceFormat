using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Scales C_INIT_InitFloat inputs targeting the rotation attributes from radians to degrees,
/// skipping the scale set methods, and rebuilds the three rotation random initializers as
/// C_INIT_InitFloat with degree sums, optional exponential bias and the random sign flip.
/// </summary>
internal sealed class Vpcf39ToVpcf40 : ParticleUpgradeStep
{
    private const float RadiansToDegrees = 57.295776f;
    private const long AngleFieldMask = 0x101030;

    public override string FromFormat => "vpcf39";
    public override string ToFormat => "vpcf40";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            ScaleInitFloatAngles(node);
            RewriteRotationRandom(node);
        });
    }

    internal static void ScaleInitFloatAngles(KVObject node)
    {
        if (node.GetString("_class", "") != "C_INIT_InitFloat")
        {
            return;
        }

        var field = unchecked((ulong)node.GetInt("m_nOutputField", 3));
        var setMethod = node.GetString("m_nSetMethod", "PARTICLE_SET_REPLACE_VALUE");

        if (field > 20 || ((AngleFieldMask >> (int)field) & 1) == 0)
        {
            return;
        }

        var input = node.Find("m_InputValue");

        if (input is not { IsCollection: true }
            || setMethod == "PARTICLE_SET_SCALE_INITIAL_VALUE"
            || setMethod == "PARTICLE_SET_SCALE_CURRENT_VALUE")
        {
            return;
        }

        ScaleInputToDegrees(input);
    }

    internal static void ScaleInputToDegrees(KVObject input)
    {
        var type = input.GetString("m_nType", "PF_TYPE_LITERAL");
        var mapType = input.GetString("m_nMapType", "PF_MAP_TYPE_DIRECT");

        if (mapType is "PF_MAP_TYPE_REMAP" or "PF_MAP_TYPE_REMAP_BIASED")
        {
            input.SetFloat("m_flOutput0", input.GetFloat("m_flOutput0", 0f) * RadiansToDegrees);
            input.SetFloat("m_flOutput1", input.GetFloat("m_flOutput1", 1f) * RadiansToDegrees);
            return;
        }

        if (mapType == "PF_MAP_TYPE_CURVE")
        {
            var curve = input.Find("m_Curve");

            if (curve is not { IsCollection: true })
            {
                return;
            }

            foreach (var point in curve.ElementsOf("m_spline"))
            {
                if (UpgradeKV.IsObject(point))
                {
                    point.SetFloat("y", point.GetFloat("y", 0f) * RadiansToDegrees);
                }
            }

            ScaleDomain(curve, "m_vDomainMins");
            ScaleDomain(curve, "m_vDomainMaxs");
            return;
        }

        if (mapType == "PF_MAP_TYPE_MULT")
        {
            input.SetFloat("m_flMultFactor", input.GetFloat("m_flMultFactor", 1f) * RadiansToDegrees);
            return;
        }

        if (type is "PF_TYPE_RANDOM_UNIFORM" or "PF_TYPE_RANDOM_BIASED")
        {
            input.SetFloat("m_flRandomMin", input.GetFloat("m_flRandomMin", 0f) * RadiansToDegrees);
            input.SetFloat("m_flRandomMax", input.GetFloat("m_flRandomMax", 1f) * RadiansToDegrees);
        }
        else if (type == "PF_TYPE_LITERAL")
        {
            input.SetFloat("m_flLiteralValue", input.GetFloat("m_flLiteralValue", 0f) * RadiansToDegrees);
        }
    }

    private static void ScaleDomain(KVObject curve, string name)
    {
        var domain = curve.Find(name);

        if (domain == null)
        {
            return;
        }

        Span<float> components = stackalloc float[2];

        if (domain.IsArray)
        {
            var index = 0;

            foreach (var element in domain.Values)
            {
                if (index >= 2)
                {
                    break;
                }

                components[index++] = element.TryGetNumber(out var number) ? (float)number : 0f;
            }
        }

        components[1] *= RadiansToDegrees;
        curve.SetFloatArray(name, components[0], components[1]);
    }

    internal static void RewriteRotationRandom(KVObject node)
    {
        var cls = node.GetString("_class", "");
        int outputField;

        switch (cls)
        {
            case "C_INIT_RandomYaw":
                outputField = 12;
                break;
            case "C_INIT_RandomRotationSpeed":
                outputField = 5;
                break;
            case "C_INIT_RandomRotation":
                outputField = (int)node.GetInt("m_nFieldOutput", 4);
                break;
            default:
                return;
        }

        var degrees = node.GetFloat("m_flDegrees", 0f);
        var degreesMin = node.GetFloat("m_flDegreesMin", 0f);
        var degreesMax = node.GetFloat("m_flDegreesMax", 360f);
        var min = degrees + degreesMin;
        var max = degrees + degreesMax;
        var hasExponent = node.ContainsKey("m_flRotationRandExponent");
        var exponent = node.GetFloat("m_flRotationRandExponent", 1f);
        var flip = node.GetBool("m_bRandomlyFlipDirection", true);

        Vpcf38ToVpcf39.RebuildAsInitFloat(node, min, max, hasExponent, exponent);

        if (flip)
        {
            node.Find("m_InputValue")!.SetBool("m_bHasRandomSignFlip", true);
        }

        node.SetInt("m_nOutputField", outputField);
    }
}
