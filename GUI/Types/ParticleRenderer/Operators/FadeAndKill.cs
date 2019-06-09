using System.Collections.Generic;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class FadeAndKill : IParticleOperator
    {
        private readonly float startFadeInTime = 0f;
        private readonly float endFadeInTime = 0.5f;
        private readonly float startFadeOutTime = 0.5f;
        private readonly float endFadeOutTime = 1f;

        private readonly float startAlpha = 1f;
        private readonly float endAlpha = 0f;

        public FadeAndKill(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flStartFadeInTime"))
            {
                startFadeInTime = keyValues.GetFloatProperty("m_flStartFadeInTime");
            }
        }

        public void Update(IEnumerable<Particle> particles, float frameTime)
        {
            foreach (var particle in particles)
            {
                var time = 1 - (particle.Lifetime / particle.ConstantLifetime);

                // If fading in
                if (time >= startFadeInTime && time <= endFadeInTime)
                {
                    var t = (time - startFadeInTime) / (endFadeInTime - startFadeInTime);

                    // Interpolate from startAlpha to 1
                    particle.Alpha = ((1 - t) * startAlpha) + t;
                }

                // If fading out
                if (time >= startFadeOutTime && time <= endFadeOutTime)
                {
                    var t = (time - startFadeOutTime) / (endFadeOutTime - startFadeOutTime);

                    // Interpolate from 1 to end alpha
                    particle.Alpha = (1 - t) + (t * endAlpha);
                }

                particle.Lifetime -= frameTime;
            }
        }
    }
}
