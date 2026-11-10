using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Upgrade;

/// <summary>
/// Clears m_bDisableOperator on root initializers when an emitter initializes from killed
/// parent particles, honoring the m_bRunForParentApplyKillList opt-out except on
/// C_INIT_InitFromParentKilled itself.
/// </summary>
internal sealed class Vpcf9ToVpcf10 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf9";
    public override string ToFormat => "vpcf10";

    public override void Apply(KVObject root)
    {
        var gate = false;

        foreach (var element in root.ElementsOf("m_Emitters"))
        {
            if (!UpgradeKV.IsObject(element))
            {
                continue;
            }

            var cls = element.GetStringProperty("_class", "");

            if ((cls == "C_OP_ContinuousEmitter" && element.GetFloatProperty("m_bInitFromKilledParentParticles") > 0f)
                || (cls == "C_OP_InstantaneousEmitter" && element.GetParticleBooleanProperty("m_flInitFromKilledParentParticles")))
            {
                gate = true;
                break;
            }
        }

        if (!gate)
        {
            return;
        }

        foreach (var element in root.ElementsOf("m_Initializers"))
        {
            if (!UpgradeKV.IsObject(element))
            {
                continue;
            }

            if (element.GetStringProperty("_class", "") != "C_INIT_InitFromParentKilled"
                && !element.GetParticleBooleanProperty("m_bRunForParentApplyKillList", true))
            {
                continue;
            }

            if (element.GetParticleBooleanProperty("m_bDisableOperator"))
            {
                element.SetBool("m_bDisableOperator", false);
            }
        }
    }
}
