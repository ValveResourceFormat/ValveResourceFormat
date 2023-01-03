using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class InitFloat : IParticleInitializer
    {
        private readonly ParticleAttribute field;
        private readonly INumberProvider value = new LiteralNumberProvider(0);

        public InitFloat(IKeyValueCollection keyValues)
        {
            field = (ParticleAttribute)keyValues.GetIntegerProperty("m_nOutputField");
            value = keyValues.GetNumberProvider("m_InputValue");
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            switch (field)
            {
                case ParticleAttribute.LifeDuration:
                    {
                        var lifetime = (float)value.NextNumber();
                        particle.ConstantLifetime = lifetime;
                        particle.Lifetime = lifetime;
                        break;
                    }
            }

            return particle;
        }
    }
}
