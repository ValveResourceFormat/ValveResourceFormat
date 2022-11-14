using System;
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

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            for (var i = 0; i < particles.Length; ++i)
            {
                var time = 1 - (particles[i].Lifetime / particles[i].ConstantLifetime);
                if (time <= fadeInTime)
                {
                    particles[i].Alpha = time / fadeInTime * particles[i].ConstantAlpha;
                }
            }
        }
    }
}
