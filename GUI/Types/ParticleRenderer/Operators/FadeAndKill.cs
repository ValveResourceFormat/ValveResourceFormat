using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class FadeAndKill : IParticleOperator
    {
        private readonly float startFadeInTime;
        private readonly float endFadeInTime = 0.5f;
        private readonly float startFadeOutTime = 0.5f;
        private readonly float endFadeOutTime = 1f;

        private readonly float startAlpha = 1f;
        private readonly float endAlpha;

        public FadeAndKill(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flStartFadeInTime"))
            {
                startFadeInTime = keyValues.GetFloatProperty("m_flStartFadeInTime");
            }

            if (keyValues.ContainsKey("m_flEndFadeInTime"))
            {
                endFadeInTime = keyValues.GetFloatProperty("m_flEndFadeInTime");
            }

            if (keyValues.ContainsKey("m_flStartFadeOutTime"))
            {
                startFadeOutTime = keyValues.GetFloatProperty("m_flStartFadeOutTime");
            }

            if (keyValues.ContainsKey("m_flEndFadeOutTime"))
            {
                endFadeOutTime = keyValues.GetFloatProperty("m_flEndFadeOutTime");
            }

            if (keyValues.ContainsKey("m_flStartAlpha"))
            {
                startAlpha = keyValues.GetFloatProperty("m_flStartAlpha");
            }

            if (keyValues.ContainsKey("m_flEndAlpha"))
            {
                endAlpha = keyValues.GetFloatProperty("m_flEndAlpha");
            }
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            for (var i = 0; i < particles.Length; ++i)
            {
                var time = 1 - (particles[i].Lifetime / particles[i].ConstantLifetime);

                // If fading in
                if (time >= startFadeInTime && time <= endFadeInTime)
                {
                    var t = (time - startFadeInTime) / (endFadeInTime - startFadeInTime);

                    // Interpolate from startAlpha to constantAlpha
                    particles[i].Alpha = ((1 - t) * startAlpha) + (t * particles[i].ConstantAlpha);
                }

                // If fading out
                if (time >= startFadeOutTime && time <= endFadeOutTime)
                {
                    var t = (time - startFadeOutTime) / (endFadeOutTime - startFadeOutTime);

                    // Interpolate from constantAlpha to end alpha
                    particles[i].Alpha = ((1 - t) * particles[i].ConstantAlpha) + (t * endAlpha);
                }

                particles[i].Lifetime -= frameTime;
            }
        }
    }
}
