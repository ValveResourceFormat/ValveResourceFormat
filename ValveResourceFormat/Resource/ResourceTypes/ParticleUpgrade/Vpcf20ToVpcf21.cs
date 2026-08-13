using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts the legacy scale-mode bools on root operators and initializers into the
/// m_nSetMethod enum string. The range and current passes remove the m_bScaleInitialValue
/// key instead of the key they read, so m_bScaleInitialRange always survives in the output.
/// </summary>
internal sealed class Vpcf20ToVpcf21 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf20";
    public override string ToFormat => "vpcf21";

    public override void Apply(KVObject root)
    {
        var operators = root.ElementsOf("m_Operators");

        for (var i = operators.Count - 1; i >= 0; i--)
        {
            var element = operators[i];

            if (!UpgradeKV.IsObject(element) || !element.ContainsKey("m_bScaleInitialValue"))
            {
                continue;
            }

            var initialValue = element.GetBool("m_bScaleInitialValue", false);
            element.Remove("m_bScaleInitialValue");
            element.SetString("m_nSetMethod", initialValue
                ? "PARTICLE_SET_SCALE_INITIAL_VALUE"
                : "PARTICLE_SET_REPLACE_VALUE");
        }

        for (var i = operators.Count - 1; i >= 0; i--)
        {
            var element = operators[i];

            if (!UpgradeKV.IsObject(element)
                || (!element.ContainsKey("m_bScaleInitialRange") && !element.ContainsKey("m_bScaleCurrent")))
            {
                continue;
            }

            var initialRange = element.GetBool("m_bScaleInitialRange", false);
            element.Remove("m_bScaleInitialValue");

            if (initialRange)
            {
                element.SetString("m_nSetMethod", "PARTICLE_SET_SCALE_INITIAL_VALUE");
            }
            else if (element.ContainsKey("m_bScaleCurrent"))
            {
                var current = element.GetBool("m_bScaleCurrent", false);
                element.Remove("m_bScaleCurrent");
                element.SetString("m_nSetMethod", current
                    ? "PARTICLE_SET_SCALE_CURRENT_VALUE"
                    : "PARTICLE_SET_REPLACE_VALUE");
            }
        }

        var initializers = root.ElementsOf("m_Initializers");

        for (var i = initializers.Count - 1; i >= 0; i--)
        {
            var element = initializers[i];

            if (!UpgradeKV.IsObject(element) || !element.ContainsKey("m_bScaleInitialRange"))
            {
                continue;
            }

            var initialRange = element.GetBool("m_bScaleInitialRange", false);
            element.Remove("m_bScaleInitialValue");
            element.SetString("m_nSetMethod", initialRange
                ? "PARTICLE_SET_SCALE_INITIAL_VALUE"
                : "PARTICLE_SET_REPLACE_VALUE");
        }
    }
}
