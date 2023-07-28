using System;
using System.Numerics;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class NormalizeVector : IParticleOperator
    {
        private readonly ParticleField field = ParticleField.Position;
        private readonly float scale = 1.0f;

        public NormalizeVector(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nOutputField"))
            {
                field = keyValues.GetParticleField("m_nOutputField");
            }

            if (keyValues.ContainsKey("m_flScale"))
            {
                scale = keyValues.GetFloatProperty("m_flScale");
            }

            // there's also a Lerp value that will fade it in when at low values. Further testing is needed to know anything more
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var vector = particle.GetVector(field);
                vector = Vector3.Normalize(vector) * scale;

                particle.SetVector(field, vector);
            }
        }
    }
}
