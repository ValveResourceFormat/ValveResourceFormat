using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts C_OP_ContinuousEmitter duration, start time and emit rate scalars into float input
/// structs, folding the CP scale reference into a control-point-component multiply.
/// </summary>
internal sealed class Vpcf16ToVpcf17 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf16";
    public override string ToFormat => "vpcf17";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_OP_ContinuousEmitter")
            {
                return;
            }

            var duration = node.GetFloat("m_flEmissionDuration", 0f);
            var startTime = node.GetFloat("m_flStartTime", 0f);
            var emitRate = node.GetFloat("m_flEmitRate", 100f);
            var scaleCP = node.GetInt("m_nScaleControlPoint", -1);
            var scaleField = node.GetInt("m_nScaleControlPointField", 0);

            node.Remove("m_flEmissionDuration");
            node.Remove("m_flStartTime");
            node.Remove("m_flEmitRate");
            node.Remove("m_nScaleControlPoint");
            node.Remove("m_nScaleControlPointField");

            var durationInput = node.SetObject("m_flEmissionDuration");
            durationInput.SetString("m_nType", "PF_TYPE_LITERAL");
            durationInput.SetFloat("m_flLiteralValue", duration);

            var startTimeInput = node.SetObject("m_flStartTime");
            startTimeInput.SetString("m_nType", "PF_TYPE_LITERAL");
            startTimeInput.SetFloat("m_flLiteralValue", startTime);

            var emitRateInput = node.SetObject("m_flEmitRate");

            if (scaleCP <= -1)
            {
                emitRateInput.SetString("m_nType", "PF_TYPE_LITERAL");
                emitRateInput.SetFloat("m_flLiteralValue", emitRate);
            }
            else
            {
                emitRateInput.SetString("m_nType", "PF_TYPE_CONTROL_POINT_COMPONENT");
                emitRateInput.SetInt("m_nControlPoint", (int)scaleCP);
                emitRateInput.SetInt("m_nVectorComponent", (int)scaleField);
                emitRateInput.SetString("m_nMapType", "PF_MAP_TYPE_MULT");
                emitRateInput.SetFloat("m_flMultFactor", emitRate);
            }
        });
    }
}
