using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Applies the radians-to-degrees follow-ups: C_OP_SetFloat outputs targeting rotation
/// attributes get the output-side scaling, and both C_INIT_InitFloat and C_OP_SetFloat inputs
/// sampling a rotation-typed particle attribute get their input domain scaled.
/// </summary>
internal sealed class Vpcf44ToVpcf45 : ParticleUpgradeStep
{
    private const float RadiansToDegrees = 57.295776f;
    private const long AngleFieldMask = 0x101030;

    public override string FromFormat => "vpcf44";
    public override string ToFormat => "vpcf45";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var cls = node.GetString("_class", "");
            var isInit = cls == "C_INIT_InitFloat";

            if (!isInit && cls != "C_OP_SetFloat")
            {
                return;
            }

            var input = node.Find("m_InputValue");
            var setMethod = node.GetString("m_nSetMethod", "PARTICLE_SET_REPLACE_VALUE");

            if (input is not { IsCollection: true })
            {
                return;
            }

            if (!isInit)
            {
                var field = unchecked((ulong)node.GetInt("m_nOutputField", 3));

                if (field <= 20
                    && ((AngleFieldMask >> (int)field) & 1) != 0
                    && setMethod != "PARTICLE_SET_SCALE_INITIAL_VALUE"
                    && setMethod != "PARTICLE_SET_SCALE_CURRENT_VALUE")
                {
                    Vpcf39ToVpcf40.ScaleInputToDegrees(input);
                }
            }

            ScaleInputDomainToDegrees(input);
        });
    }

    private static void ScaleInputDomainToDegrees(KVObject input)
    {
        if (input.GetString("m_nType", "PF_TYPE_LITERAL") != "PF_TYPE_PARTICLE_FLOAT")
        {
            return;
        }

        var attribute = unchecked((ulong)input.GetInt("m_nScalarAttribute", 3));

        if (attribute > 20 || ((AngleFieldMask >> (int)attribute) & 1) == 0)
        {
            return;
        }

        var mapType = input.GetString("m_nMapType", "PF_MAP_TYPE_DIRECT");

        if (mapType is "PF_MAP_TYPE_REMAP" or "PF_MAP_TYPE_REMAP_BIASED")
        {
            input.SetFloat("m_flInput0", input.GetFloat("m_flInput0", 0f) * RadiansToDegrees);
            input.SetFloat("m_flInput1", input.GetFloat("m_flInput1", 1f) * RadiansToDegrees);
            return;
        }

        if (mapType != "PF_MAP_TYPE_CURVE")
        {
            return;
        }

        var curve = input.Find("m_Curve");

        if (curve is not { IsCollection: true })
        {
            return;
        }

        foreach (var point in curve.ElementsOf("m_spline"))
        {
            if (UpgradeKV.IsObject(point))
            {
                point.SetFloat("x", point.GetFloat("x", 0f) * RadiansToDegrees);
            }
        }

        ScaleDomain(curve, "m_vDomainMins");
        ScaleDomain(curve, "m_vDomainMaxs");
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

        components[0] *= RadiansToDegrees;
        curve.SetFloatArray(name, components[0], components[1]);
    }
}
