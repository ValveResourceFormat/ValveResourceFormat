using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Negates the Y component of C_INIT_CreateWithinSphereTransform's two local-coordinate-system
/// speed vector inputs across every vector-input type. Literal inputs are rebuilt at the
/// operator tail, dropping extra members; every other branch edits values in place, with
/// float-component Y inputs negated through both their value type and their map type, where a
/// direct-mapped CP component becomes a mult mapping whose absent factor negates to
/// negative zero.
/// </summary>
internal sealed class Vpcf61ToVpcf62 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf61";
    public override string ToFormat => "vpcf62";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_INIT_CreateWithinSphereTransform")
            {
                return;
            }

            NegateVectorInput(node, "m_LocalCoordinateSystemSpeedMin");
            NegateVectorInput(node, "m_LocalCoordinateSystemSpeedMax");
        });
    }

    private static void NegateVectorInput(KVObject node, string name)
    {
        var input = node.Find(name);

        if (input is not { IsCollection: true })
        {
            return;
        }

        switch (input.GetString("m_nType", "PVEC_TYPE_LITERAL"))
        {
            case "PVEC_TYPE_LITERAL":
            {
                var value = input.GetFloat3("m_vLiteralValue", Vector3.Zero);
                node.Remove(name);
                var rebuilt = node.SetObject(name);
                rebuilt.SetString("m_nType", "PVEC_TYPE_LITERAL");
                rebuilt.SetFloatArray("m_vLiteralValue", value.X, -value.Y, value.Z);
                break;
            }

            case "PVEC_TYPE_PARTICLE_VECTOR":
                NegateY(input, "m_vVectorAttributeScale");
                break;

            case "PVEC_TYPE_RANDOM_UNIFORM":
                NegateY(input, "m_vRandomMin");
                NegateY(input, "m_vRandomMax");
                break;

            case "PVEC_TYPE_CP_VALUE":
                NegateY(input, "m_vCPValueScale");
                break;

            case "PVEC_TYPE_FLOAT_INTERP":
            case "PVEC_TYPE_FLOAT_INTERP_CLAMPED":
                NegateY(input, "m_vInterpOutput0");
                NegateY(input, "m_vInterpOutput1");
                break;

            case "PVEC_TYPE_FLOAT_COMPONENTS":
            {
                var componentY = input.Find("m_FloatComponentY");

                if (componentY is { IsCollection: true })
                {
                    NegateFloatInput(componentY);
                }

                break;
            }

            default:
                break;
        }
    }

    private static void NegateFloatInput(KVObject input)
    {
        switch (input.GetString("m_nType", "PF_TYPE_LITERAL"))
        {
            case "PF_TYPE_LITERAL":
                NegateFloat(input, "m_flLiteralValue");
                break;

            case "PF_TYPE_RANDOM_UNIFORM":
            case "PF_TYPE_RANDOM_BIASED":
                NegateFloat(input, "m_flRandomMin");
                NegateFloat(input, "m_flRandomMax");
                break;

            case "PF_TYPE_CONTROL_POINT_COMPONENT":
                if (input.GetString("m_nMapType", "PF_MAP_TYPE_INVALID") == "PF_MAP_TYPE_DIRECT")
                {
                    input.SetString("m_nMapType", "PF_MAP_TYPE_MULT");
                }

                break;

            case "PF_TYPE_PARTICLE_DETAIL_LEVEL":
                NegateFloat(input, "m_flLOD0");
                NegateFloat(input, "m_flLOD1");
                NegateFloat(input, "m_flLOD2");
                NegateFloat(input, "m_flLOD3");
                break;

            case "PF_TYPE_NOISE":
                NegateFloat(input, "m_flNoiseOutputMin");
                NegateFloat(input, "m_flNoiseOutputMax");
                break;

            default:
                break;
        }

        switch (input.GetString("m_nMapType", "PF_MAP_TYPE_INVALID"))
        {
            case "PF_MAP_TYPE_MULT":
                NegateFloat(input, "m_flMultFactor");
                break;

            case "PF_MAP_TYPE_REMAP":
            case "PF_MAP_TYPE_REMAP_BIASED":
                NegateFloat(input, "m_flOutput0");
                NegateFloat(input, "m_flOutput1");
                break;

            case "PF_MAP_TYPE_NOTCHED":
                NegateFloat(input, "m_flNotchedOutputOutside");
                NegateFloat(input, "m_flNotchedOutputInside");
                break;

            case "PF_MAP_TYPE_CURVE":
            {
                var curve = input.Find("m_Curve");

                if (curve is { IsCollection: true })
                {
                    NegateDomainY(curve, "m_vDomainMins");
                    NegateDomainY(curve, "m_vDomainMaxs");
                }

                break;
            }

            default:
                break;
        }
    }

    private static void NegateY(KVObject obj, string name)
    {
        var value = obj.GetFloat3(name, Vector3.Zero);
        obj.SetFloatArray(name, value.X, -value.Y, value.Z);
    }

    private static void NegateFloat(KVObject obj, string name)
        => obj.SetFloat(name, -obj.GetFloat(name, 0f));

    private static void NegateDomainY(KVObject curve, string name)
    {
        var domain = curve.Find(name);
        Span<float> components = stackalloc float[2];

        if (domain is { IsArray: true })
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

        curve.SetFloatArray(name, components[0], -components[1]);
    }
}
