using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class InitFloat : IParticleInitializer
    {
        private readonly ParticleField field = ParticleField.Radius;
        private readonly INumberProvider value = new LiteralNumberProvider(0);

        public InitFloat(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nOutputField"))
            {
                field = keyValues.GetParticleField("m_nOutputField");
            }

            if (keyValues.ContainsKey("m_InputValue"))
            {
                value = keyValues.GetNumberProvider("m_InputValue");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.SetInitialScalar(field, value.NextNumber(ref particle, particleSystemState));

            return particle;
        }
    }
}
