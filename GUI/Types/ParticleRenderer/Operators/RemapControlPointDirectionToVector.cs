using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class RemapControlPointDirectionToVector : IParticleOperator
    {
        private readonly ParticleField field = ParticleField.Position;
        private readonly int cp;
        private readonly float scale;

        public RemapControlPointDirectionToVector(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                field = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_flScale"))
            {
                scale = keyValues.GetFloatProperty("m_flScale");
            }

            if (keyValues.ContainsKey("m_nControlPointNumber"))
            {
                cp = keyValues.GetInt32Property("m_nControlPointNumber");
            }
        }

        // is this particle id or total particle count?
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (var particle in particles)
            {
                // direction or orientation??
                var direction = particleSystemState.GetControlPoint(cp).Orientation;
                particle.SetVector(field, direction * scale);
            }
        }
    }
}
