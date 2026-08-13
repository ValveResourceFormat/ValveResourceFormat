using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Folds m_nScaleCP into control-point-component inputs on three classes: epitrochoid radii
/// and offset, constrain-distance bounds, and create-within-box corner vectors. The
/// constrain-distance branch removes the unprefixed flMinDistance and flMaxDistance
/// spellings, leaving any prefixed source scalars to be overwritten by the new inputs.
/// </summary>
internal sealed class Vpcf31ToVpcf32 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf31";
    public override string ToFormat => "vpcf32";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var cls = node.GetString("_class", "");

            if (cls is not ("C_INIT_CreateInEpitrochoid" or "C_OP_ConstrainDistance" or "C_INIT_CreateWithinBox"))
            {
                return;
            }

            var scaleCP = node.GetInt("m_nScaleCP", -1);

            if (scaleCP <= -1)
            {
                return;
            }

            switch (cls)
            {
                case "C_INIT_CreateInEpitrochoid":
                {
                    var radius1 = node.GetFloat("m_flRadius1", 4f);
                    var radius2 = node.GetFloat("m_flRadius2", 40f);
                    var offset = node.GetFloat("m_flOffset", 24f);

                    node.Remove("m_flRadius1");
                    node.Remove("m_flRadius2");
                    node.Remove("m_flOffset");
                    node.Remove("m_nScaleCP");

                    SetCpComponent(node.SetObject("m_flRadius1"), scaleCP, 0, radius1);
                    SetCpComponent(node.SetObject("m_flRadius2"), scaleCP, 1, radius2);
                    SetCpComponent(node.SetObject("m_flOffset"), scaleCP, 2, offset);
                    break;
                }

                case "C_OP_ConstrainDistance":
                {
                    var minDistance = node.GetFloat("m_fMinDistance", 0f);
                    var maxDistance = node.GetFloat("m_fMaxDistance", 100f);

                    node.Remove("flMinDistance");
                    node.Remove("flMaxDistance");
                    node.Remove("m_nScaleCP");

                    SetCpComponent(node.SetObject("m_fMinDistance"), scaleCP, 0, minDistance);
                    SetCpComponent(node.SetObject("m_fMaxDistance"), scaleCP, 1, maxDistance);
                    break;
                }

                default:
                {
                    var vecMin = node.GetFloat3("m_vecMin", Vector3.Zero);
                    var vecMax = node.GetFloat3("m_vecMax", Vector3.Zero);

                    node.Remove("m_vecMin");
                    node.Remove("m_vecMax");
                    node.Remove("m_nScaleCP");

                    SetFloatComponents(node.SetObject("m_vecMin"), scaleCP, vecMin);
                    SetFloatComponents(node.SetObject("m_vecMax"), scaleCP, vecMax);
                    break;
                }
            }
        });
    }

    private static void SetCpComponent(KVObject input, long cp, int component, float factor)
    {
        input.SetString("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
        input.SetInt("m_nControlPoint", (int)cp);
        input.SetInt("m_nVectorComponent", component);
        input.SetString("m_nMapType", "PF_MAP_TYPE_MULT");
        input.SetFloat("m_flMultFactor", factor);
    }

    private static void SetFloatComponents(KVObject input, long cp, Vector3 value)
    {
        input.SetString("m_nType", "PVEC_TYPE_FLOAT_COMPONENTS");
        SetCpComponent(input.SetObject("m_FloatComponentX"), cp, 0, value.X);
        SetCpComponent(input.SetObject("m_FloatComponentY"), cp, 0, value.Y);
        SetCpComponent(input.SetObject("m_FloatComponentZ"), cp, 0, value.Z);
    }
}
