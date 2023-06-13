using System;
using System.Numerics;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class InitFloat : IParticleInitializer
    {
        private readonly ParticleAttribute field;
        private readonly INumberProvider value = new LiteralNumberProvider(0);

        public InitFloat(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nOutputField"))
            {
                field = (ParticleAttribute)keyValues.GetIntegerProperty("m_nOutputField");
            }
            else
            {
                field = ParticleAttribute.Radius;
            }

            value = keyValues.GetNumberProvider("m_InputValue");
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            switch (field)
            {
                case ParticleAttribute.Alpha:
                    {
                        particle.ConstantAlpha = (float)value.NextNumber();
                        particle.Alpha = particle.ConstantAlpha;
                        break;
                    }
                case ParticleAttribute.LifeDuration:
                    {
                        particle.ConstantLifetime = (float)value.NextNumber();
                        particle.Lifetime = particle.ConstantLifetime;
                        break;
                    }
                case ParticleAttribute.Radius:
                    {
                        particle.ConstantRadius = (float)value.NextNumber();
                        particle.Radius = particle.ConstantRadius;
                        break;
                    }
                case ParticleAttribute.Roll:
                    {
                        particle.Rotation = new Vector3(0.0f, 0.0f, (float)(value.NextNumber() * Math.PI / 180.0));
                        break;
                    }

            }

            return particle;
        }
    }
}
