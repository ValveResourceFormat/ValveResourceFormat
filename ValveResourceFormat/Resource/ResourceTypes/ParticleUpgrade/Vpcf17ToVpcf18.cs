using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts C_OP_InstantaneousEmitter start time and emit count scalars into float input
/// structs, folding min/max pairs into constant random-uniform inputs and the scale control
/// point into a control-point-component multiply or remap.
/// </summary>
internal sealed class Vpcf17ToVpcf18 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf17";
    public override string ToFormat => "vpcf18";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_OP_InstantaneousEmitter")
            {
                return;
            }

            var emit = node.GetInt("m_nParticlesToEmit", 100);
            var startTime = node.GetFloat("m_flStartTime", 0f);
            var minEmit = node.GetInt("m_nMinParticlesToEmit", -1);
            var startTimeMax = node.GetFloat("m_flStartTimeMax", -1f);
            var scaleCP = node.GetInt("m_nScaleControlPoint", -1);
            var scaleField = node.GetInt("m_nScaleControlPointField", 0);

            node.Remove("m_nMinParticlesToEmit");
            node.Remove("m_nParticlesToEmit");
            node.Remove("m_flStartTime");
            node.Remove("m_flStartTimeMax");
            node.Remove("m_nScaleControlPoint");
            node.Remove("m_nScaleControlPointField");

            var startTimeInput = node.SetObject("m_flStartTime");

            if (startTimeMax <= -1f || startTime == startTimeMax)
            {
                startTimeInput.SetString("m_nType", "PF_TYPE_LITERAL");
                startTimeInput.SetFloat("m_flLiteralValue", startTime);
            }
            else
            {
                startTimeInput.SetString("m_nType", "PF_TYPE_RANDOM_UNIFORM");
                startTimeInput.SetFloat("m_flRandomMin", startTime);
                startTimeInput.SetFloat("m_flRandomMax", startTimeMax);
                startTimeInput.SetString("m_nRandomMode", "PF_RANDOM_MODE_CONSTANT");
            }

            var emitInput = node.SetObject("m_nParticlesToEmit");

            if (scaleCP <= -1)
            {
                if (minEmit <= -1)
                {
                    emitInput.SetString("m_nType", "PF_TYPE_LITERAL");
                    emitInput.SetFloat("m_flLiteralValue", emit);
                }
                else
                {
                    emitInput.SetString("m_nType", "PF_TYPE_RANDOM_UNIFORM");
                    emitInput.SetFloat("m_flRandomMin", minEmit);
                    emitInput.SetFloat("m_flRandomMax", emit);
                    emitInput.SetString("m_nRandomMode", "PF_RANDOM_MODE_CONSTANT");
                }
            }
            else
            {
                emitInput.SetString("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
                emitInput.SetInt("m_nControlPoint", (int)scaleCP);
                emitInput.SetInt("m_nVectorComponent", (int)scaleField);

                if (minEmit <= -1 || minEmit == emit)
                {
                    emitInput.SetString("m_nMapType", "PF_MAP_TYPE_MULT");
                    emitInput.SetFloat("m_flMultFactor", emit);
                }
                else
                {
                    emitInput.SetString("m_nMapType", "PF_MAP_TYPE_REMAP");
                    emitInput.SetFloat("m_flInput0", 0f);
                    emitInput.SetFloat("m_flInput1", 1f);
                    emitInput.SetFloat("m_flOutput0", minEmit);
                    emitInput.SetFloat("m_flOutput1", emit);
                }
            }
        });
    }
}
