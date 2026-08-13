using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts C_OP_StopAfterCPDuration duration scalars in the root's pre-emission operators
/// into a float input, folding the CP reference into a control-point-component multiply.
/// </summary>
internal sealed class Vpcf24ToVpcf25 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf24";
    public override string ToFormat => "vpcf25";

    public override void Apply(KVObject root)
    {
        foreach (var element in root.ElementsOf("m_PreEmissionOperators"))
        {
            if (!UpgradeKV.IsObject(element) || element.GetString("_class", "") != "C_OP_StopAfterCPDuration")
            {
                continue;
            }

            if (!element.ContainsKey("m_nCP") && !element.ContainsKey("m_nCPField"))
            {
                continue;
            }

            var duration = element.GetFloat("m_flDuration", 1f);
            var cp = element.GetInt("m_nCP", -1);
            var cpField = element.GetInt("m_nCPField", 0);

            element.Remove("m_flDuration");
            element.Remove("m_nCP");
            element.Remove("m_nCPField");

            var input = element.SetObject("m_flDuration");

            if (cp <= -1)
            {
                input.SetString("m_nType", "PF_TYPE_LITERAL");
                input.SetFloat("m_flLiteralValue", duration);
            }
            else
            {
                input.SetString("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
                input.SetInt("m_nControlPoint", (int)cp);
                input.SetInt("m_nVectorComponent", (int)cpField);
                input.SetString("m_nMapType", "PF_MAP_TYPE_MULT");
                input.SetFloat("m_flMultFactor", duration);
            }
        }
    }
}
