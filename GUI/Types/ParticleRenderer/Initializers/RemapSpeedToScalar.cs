using System;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RemapSpeedToScalar : IParticleInitializer
    {
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 10;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        private readonly bool perParticle;

        public RemapSpeedToScalar(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                fieldOutput = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_flInputMin"))
            {
                inputMin = keyValues.GetFloatProperty("m_nInputMin");
            }

            if (keyValues.ContainsKey("m_flInputMax"))
            {
                inputMax = keyValues.GetFloatProperty("m_nInputMax");
            }

            if (keyValues.ContainsKey("m_flOutputMin"))
            {
                outputMin = keyValues.GetFloatProperty("m_flOutputMin");
            }

            if (keyValues.ContainsKey("m_flOutputMax"))
            {
                outputMax = keyValues.GetFloatProperty("m_flOutputMax");
            }

            if (keyValues.ContainsKey("m_nSetMethod"))
            {
                setMethod = keyValues.GetEnumValue<ParticleSetMethod>("m_nSetMethod");
            }

            if (keyValues.ContainsKey("m_bPerParticle"))
            {
                perParticle = keyValues.GetProperty<bool>("m_bPerParticle");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            if (!perParticle)
            {
                // I think it depends on the speed of the control point, which we don't track.
                return particle;
            }
            var particleCount = Math.Clamp(particle.ParticleCount, inputMin, inputMax);

            var output = MathUtils.RemapRange(particleCount, inputMin, inputMax, outputMin, outputMax);

            particle.SetInitialScalar(fieldOutput, particle.ModifyScalarBySetMethod(fieldOutput, output, setMethod));

            return particle;
        }
    }
}
