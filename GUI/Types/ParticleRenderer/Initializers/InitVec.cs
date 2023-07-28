using System.Numerics;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class InitVec : IParticleInitializer
    {
        private readonly ParticleField field = ParticleField.Color;
        private readonly IVectorProvider value = new LiteralVectorProvider(Vector3.Zero);

        public InitVec(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nOutputField"))
            {
                field = keyValues.GetParticleField("m_nOutputField");
            }

            if (keyValues.ContainsKey("m_nInputValue"))
            {
                value = keyValues.GetVectorProvider("m_nInputValue");
            }
        }

        // todo: these (operators and initializers) can reference either the current value and the initial value. do we need to store the initial value of all attributes?
        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.SetVector(field, value.NextVector(ref particle, particleSystemState));

            return particle;
        }
    }
}
