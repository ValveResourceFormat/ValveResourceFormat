using System.Collections.Generic;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class InterpolateRadius : IParticleOperator
    {
        private readonly float startTime = 0;
        private readonly float endTime = 1;
        private readonly float startScale = 1;
        private readonly float endScale = 1;

        public InterpolateRadius(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flStartTime"))
            {
                startTime = keyValues.GetFloatProperty("m_flStartTime");
            }

            if (keyValues.ContainsKey("m_flEndTime"))
            {
                endTime = keyValues.GetFloatProperty("m_flEndTime");
            }

            if (keyValues.ContainsKey("m_flStartScale"))
            {
                startScale = keyValues.GetFloatProperty("m_flStartScale");
            }

            if (keyValues.ContainsKey("m_flEndScale"))
            {
                endScale = keyValues.GetFloatProperty("m_flEndScale");
            }
        }

        public void Update(IEnumerable<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (var particle in particles)
            {
                var time = 1 - (particle.Lifetime / particle.ConstantLifetime);

                if (time >= startTime && time <= endTime)
                {
                    var t = (time - startTime) / (endTime - startTime);
                    var radiusScale = (startScale * (1 - t)) + (endScale * t);

                    particle.Radius = particle.ConstantRadius * radiusScale;
                }
            }
        }
    }
}
