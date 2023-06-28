using System;
using GUI.Utils;
using GUI.Types.ParticleRenderer.Utils;
using ValveResourceFormat.Serialization;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RemapParticleCountToScalar : IParticleInitializer
    {
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly long inputMin;
        private readonly long inputMax = 10;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly bool scaleInitialRange; // legacy

        private readonly bool invert;
        private readonly bool wrap;
        private readonly float remapBias = 0.5f;

        private readonly int controlPoint = -1;
        private readonly int controlPointComponent;

        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public RemapParticleCountToScalar(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                fieldOutput = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_nInputMin"))
            {
                inputMin = keyValues.GetIntegerProperty("m_nInputMin");
            }

            if (keyValues.ContainsKey("m_nInputMax"))
            {
                inputMax = keyValues.GetIntegerProperty("m_nInputMax");
            }

            if (keyValues.ContainsKey("m_flOutputMin"))
            {
                outputMin = keyValues.GetFloatProperty("m_flOutputMin");
            }

            if (keyValues.ContainsKey("m_flOutputMax"))
            {
                outputMax = keyValues.GetFloatProperty("m_flOutputMax");
            }

            if (keyValues.ContainsKey("m_bScaleInitialRange"))
            {
                scaleInitialRange = keyValues.GetProperty<bool>("m_bScaleInitialRange");
            }

            if (keyValues.ContainsKey("m_bInvert"))
            {
                invert = keyValues.GetProperty<bool>("m_bInvert");
            }

            if (keyValues.ContainsKey("m_bWrap"))
            {
                wrap = keyValues.GetProperty<bool>("m_bWrap");
            }

            if (keyValues.ContainsKey("m_flRemapBias"))
            {
                remapBias = keyValues.GetFloatProperty("m_flRemapBias");
            }

            if (keyValues.ContainsKey("m_nSetMethod"))
            {
                setMethod = keyValues.GetEnumValue<ParticleSetMethod>("m_nSetMethod");
            }

            if (keyValues.ContainsKey("m_nScaleControlPoint"))
            {
                controlPoint = keyValues.GetInt32Property("m_nScaleControlPoint");
            }
            if (keyValues.ContainsKey("m_nScaleControlPointField"))
            {
                controlPointComponent = keyValues.GetInt32Property("m_nScaleControlPointField");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            // system state currently doesn't track total count, so we can't access that yet
            var count = invert
                ? particleSystemState.ParticleCount - particle.ParticleCount
                : particle.ParticleCount;

            var remappedRange = MathUtils.Remap(count, inputMin, inputMax);

            remappedRange = NumericBias.ApplyBias(remappedRange, remapBias);

            if (controlPoint != -1)
            {
                var cp = particleSystemState.GetControlPoint(controlPoint);
                remappedRange *= cp.Position.GetComponent(controlPointComponent);
            }

            remappedRange = wrap
                ? MathUtils.Wrap(remappedRange, 0f, 1f)
                : Math.Clamp(remappedRange, 0f, 1f);

            var output = MathUtils.Lerp(remappedRange, outputMin, outputMax);

            output = scaleInitialRange
                    ? particle.GetScalar(fieldOutput) * output
                    : output;

            particle.SetInitialScalar(fieldOutput, particle.ModifyScalarBySetMethod(fieldOutput, output, setMethod));

            // Why are we returning an object that we already use a ref for?
            return particle;
        }
    }
}
