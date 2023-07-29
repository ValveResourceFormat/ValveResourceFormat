using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class InitFloat : IParticleInitializer
    {
        private readonly ParticleField OutputField = ParticleField.Radius;
        private readonly INumberProvider InputValue = new LiteralNumberProvider(0);

        public InitFloat(ParticleDefinitionParser parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);

            InputValue = parse.NumberProvider("m_InputValue", InputValue);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.SetInitialScalar(OutputField, InputValue.NextNumber(ref particle, particleSystemState));

            return particle;
        }
    }
}
