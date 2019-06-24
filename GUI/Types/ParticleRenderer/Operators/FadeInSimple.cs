using System.Collections.Generic;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class FadeInSimple : IParticleOperator
    {
        private readonly float fadeInTime = 0.25f;

        public FadeInSimple(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flFadeInTime"))
            {
                fadeInTime = keyValues.GetFloatProperty("m_flFadeInTime");
            }
        }

        public void Update(IEnumerable<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (var particle in particles)
            {
                var time = 1 - (particle.Lifetime / particle.ConstantLifetime);
                if (time <= fadeInTime)
                {
                    particle.Alpha = (time / fadeInTime) * particle.ConstantAlpha;
                }
            }
        }
    }
}
