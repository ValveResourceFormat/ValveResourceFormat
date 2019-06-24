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

        public void Update(IEnumerable<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (var particle in particles)
            {
                var timeLeft = particle.Lifetime / particle.ConstantLifetime;
                if (timeLeft <= fadeOutTime)
                {
                    var t = timeLeft / fadeOutTime;
                    particle.Alpha = t * particle.ConstantAlpha;
                }
            }
        }
    }
}
