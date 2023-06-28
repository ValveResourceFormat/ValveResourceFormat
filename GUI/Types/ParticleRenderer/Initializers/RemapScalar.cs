using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RemapScalar : IParticleInitializer
    {
        private readonly ParticleField fieldInput = ParticleField.Alpha;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax;
        private readonly float outputMin;
        private readonly float outputMax;

        public RemapScalar(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldInput"))
            {
                fieldInput = keyValues.GetParticleField("m_nFieldInput");
            }

            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                fieldOutput = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_flInputMin"))
            {
                inputMin = keyValues.GetFloatProperty("m_flInputMin");
            }

            if (keyValues.ContainsKey("m_flInputMax"))
            {
                inputMax = keyValues.GetFloatProperty("m_flInputMax");
            }

            if (keyValues.ContainsKey("m_flOutputMin"))
            {
                outputMin = keyValues.GetFloatProperty("m_flOutputMin");
            }

            if (keyValues.ContainsKey("m_flOutputMax"))
            {
                outputMax = keyValues.GetFloatProperty("m_flOutputMax");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var value = particle.GetScalar(fieldInput);

            value = MathUtils.RemapRange(value, inputMin, inputMax, outputMin, outputMax);

            particle.SetInitialScalar(fieldOutput, value);

            return particle;
        }
    }
}
