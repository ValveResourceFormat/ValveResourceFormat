using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    /// <summary>
    /// Cull particle when its alpha is below a certain threshold.
    /// </summary>
    class AlphaDecay : IParticleOperator
    {
        private readonly float minAlpha;
        public AlphaDecay(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flMinAlpha"))
            {
                minAlpha = keyValues.GetFloatProperty("m_flMinAlpha");
            }
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            for (var i = 0; i < particles.Length; ++i)
            {
                if (particles[i].Alpha <= minAlpha)
                {
                    particles[i].Kill();
                }
            }
        }
    }
}
