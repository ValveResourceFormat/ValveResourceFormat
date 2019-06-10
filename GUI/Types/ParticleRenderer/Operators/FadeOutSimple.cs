using System.Collections.Generic;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class FadeOutSimple : IParticleOperator
    {
        private readonly float fadeOutTime = 0.25f;

        public FadeOutSimple(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flFadeOutTime"))
            {
                fadeOutTime = keyValues.GetFloatProperty("m_flFadeOutTime");
            }
        }

        public void Update(IEnumerable<Particle> particles, float frameTime)
        {
            foreach (var particle in particles)
            {
                var time = particle.Lifetime / particle.ConstantLifetime;
                if (time >= 1 - fadeOutTime)
                {
                    var t = (time - (1 - fadeOutTime)) / fadeOutTime;
                    particle.Alpha = (1 - t) * particle.ConstantAlpha;
                }
            }
        }
    }
}
