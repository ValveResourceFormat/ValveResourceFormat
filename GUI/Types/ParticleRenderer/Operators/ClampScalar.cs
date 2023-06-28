using System;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class ClampScalar : IParticleOperator
    {
        private readonly float outputMin;
        private readonly float outputMax = 1;
        private readonly ParticleField field = ParticleField.Radius;

        public ClampScalar(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nOutputField"))
            {
                field = keyValues.GetParticleField("m_nOutputField");
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

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (var particle in particles)
            {
                var clampedValue = Math.Clamp(particle.GetScalar(field), outputMin, outputMax);
                particle.SetScalar(field, clampedValue);
            }
        }
    }
}
